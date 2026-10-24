// CinematicCapture.cs
// Purpose: Editor-only demo capture of a Level 2 fight (the landing-page clip). In Play mode it plays a scripted 20-25 s fight
//          through the game's real systems, films it with two cameras (over the player's shoulder, then a wider side view for the
//          clash) and records the Game view through Unity Recorder (Editor/CinematicCaptureRecorder: 1920x1080, 30 fps, H.264
//          MP4, no audio, Captures/Demo/level2_demo_raw.mp4).
//          Choreography (every hit, guard, clash and counter is the game's own path; no gameplay value is changed):
//            1. a forced table attack (Executioner Slam) winds up and telegraphs;
//            2. the player sidesteps out of its swing arc and lands a medium swing in its recovery;
//            3. a full-power backhand with the other sword (damage number);
//            4. the monster guards (MonsterAI.BeginGuard) and the next swing lands reduced;
//            5. a sword swept into the monster's weapon during its strike: a clash (sparks, colour flash), side camera;
//            6. the monster's counter (MonsterAI.BeginCounter);
//            7. at low HP (set for the demo, as the test harnesses do), a final strong overhead hit.
//          While it runs: the monster's own attack picks are held off (its cooldown parked, as DefenseHarness does), the player is
//          healed between beats, debug overlays (Expert overlay, debug readout, pause hint, name picker) are hidden, and
//          SessionReporter points at a dead local address (http://127.0.0.1:9) from the first scene load until the capture ends,
//          then is restored, so no demo session can reach the live server.
//          Entry: CinematicCaptureRecorder.Arm() before entering Play mode, then CinematicCapture.Run() in Play mode; poll Status.
// Dependencies: BattleManager, MonsterController, MonsterAI, MonsterHealth, MonsterEntrance, PlayerCombat, PlayerHealth,
//               HitDetection, SessionReporter, OVRCameraRig, Editor/CinematicCaptureRecorder (Recorder hooks)
// Attach to: nothing (added at runtime by Run)

using System.Collections;
using System.Text;
using UnityEngine;

public class CinematicCapture : MonoBehaviour
{
#if UNITY_EDITOR
    // ───────────────────────── Recorder hooks (set by Editor/CinematicCaptureRecorder) ─────────────────────────
    public static System.Func<string, int, int, int, bool> BeginRecording;
    public static System.Action EndRecording;
    public const string ArmedKey = "CinematicCapture.Armed";
    public const string DeadUrl = "http://127.0.0.1:9";

    [Header("Output")]
    public string outputFile = "Captures/Demo/level2_demo_raw";
    public int width = 1920, height = 1080, fps = 30;
    public bool record = true;

    [Header("Over the shoulder")]
    public float shoulderBack = 0.75f, shoulderUp = 0.04f, shoulderRight = 0.3f, shoulderFov = 62f;
    [Header("Side view (clash)")]
    public float sideDistance = 3.4f, sideHeight = 1.55f, sideFov = 42f;

    [Header("Fight geometry")]
    [Tooltip("Head distance from the monster's root before a swing (the swords reach about 1.4 m).")]
    public float fightDistance = 1.75f;
    [Tooltip("Pivot-to-hand reach of a scripted swing.")]
    public float reach = 0.55f;

    public static string Status { get; private set; } = "IDLE";
    public static readonly StringBuilder Log = new StringBuilder();
    public static bool Done { get; private set; }

    MonsterController mon;
    MonsterAI ai;
    MonsterHealth hp;
    OVRCameraRig rig;
    Transform head;
    Camera shoulderCam, sideCam, activeCam;
    Transform[] weapons = new Transform[2];
    Vector3[] restPos = new Vector3[2];
    Quaternion[] restRot = new Quaternion[2];
    Vector3[] bladeLocal = new Vector3[2];
    bool[] scripted = new bool[2];
    float savedCd, savedBase;
    string savedUrl;
    int hits, clashes, blockedHits, playerHurt;
    float lastDamage;
    bool lastDefended;

    /// <summary>Starts the capture (Play mode, any level; the clip is made on Level 2).</summary>
    public static string Run(bool record = true)
    {
        if (!Application.isPlaying) return "not playing";
        if (FindAnyObjectByType<CinematicCapture>() != null) return "already running";
        Log.Length = 0; Done = false; Status = "STARTING";
        var go = new GameObject("ZZCinematicCapture");
        go.AddComponent<CinematicCapture>().record = record;
        return "started";
    }

    // The first scene load of an armed Play session: the reporter is redirected before any Start (its wake-up ping included).
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void RedirectIfArmed()
    {
        if (!UnityEditor.SessionState.GetBool(ArmedKey, false)) return;
        foreach (var r in FindObjectsByType<SessionReporter>(FindObjectsInactive.Include))
        {
            UnityEditor.SessionState.SetString(ArmedKey + ".url", r.baseUrl);
            r.baseUrl = DeadUrl;
        }
    }

    void Note(string s) { Log.Append(Time.time.ToString("00.00")).Append("  ").AppendLine(s); }

    IEnumerator Start()
    {
        Application.runInBackground = true;
        var rep = SessionReporter.Instance;
        savedUrl = UnityEditor.SessionState.GetString(ArmedKey + ".url", rep != null ? rep.baseUrl : "");
        if (rep != null) rep.baseUrl = DeadUrl;
        Note("reporter -> " + (rep != null ? rep.baseUrl : "none") + " (restored to " + savedUrl + " at the end)");

        HideDebug();
        if (BattleManager.Instance.ActiveMonster == null || !BattleManager.Instance.ActiveMonster.isAlive) BattleManager.Instance.StartBattleFromInspector();
        Status = "WAITING FOR THE ENTRANCE";
        float w0 = Time.time;
        while (Time.time - w0 < 40f)
        {
            var m = BattleManager.Instance.ActiveMonster;
            if (m != null && m.isAlive) { var e = m.GetComponent<MonsterEntrance>(); if (e == null || e.Complete) break; }
            yield return null;
        }
        mon = BattleManager.Instance.ActiveMonster;
        if (mon == null) { Finish("no monster"); yield break; }
        ai = mon.GetComponent<MonsterAI>(); hp = mon.GetComponent<MonsterHealth>();
        rig = FindAnyObjectByType<OVRCameraRig>(); head = rig.centerEyeAnchor;
        HitDetection.HitLanded += OnHit; HitDetection.WeaponClashed += OnClash; hp.OnDamaged += OnDamaged;
        PlayerHealth.DamageTaken += OnHurt;

        // Let the monster finish whatever it started, then hold off its own picks (the scripted beats force theirs).
        Status = "SETTLING";
        float s0 = Time.time;
        while ((ai.IsBusy || mon.isAttacking || ai.IsDefending) && Time.time - s0 < 10f) yield return null;
        Quiet(true);
        hp.currentHealth = hp.maxHealth;
        if (PlayerHealth.Instance != null) PlayerHealth.Instance.FullHeal();
        CaptureRest();
        PlaceHead(1.83f);
        BuildCameras();
        foreach (var t in head.GetComponentsInChildren<Transform>(true))
            if (t != head && (t.GetComponent<Renderer>() != null || t.GetComponent<Canvas>() != null))
                Note("     under the head: " + t.name + " layer " + LayerMask.LayerToName(t.gameObject.layer) + (t.GetComponent<Canvas>() != null ? " (canvas)" : "") + " active " + t.gameObject.activeInHierarchy);
        yield return null;
        HideDebug();

        if (record && BeginRecording != null)
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(outputFile)));
            if (!BeginRecording(System.IO.Path.GetFullPath(outputFile), width, height, fps)) { Finish("recorder did not start"); yield break; }
            Note("recording " + outputFile + ".mp4 " + width + "x" + height + " @" + fps);
        }
        else Note(record ? "no recorder hook (Editor/CinematicCaptureRecorder not loaded): dry run" : "dry run (record off)");
        float t0 = Time.time;
        Status = "RECORDING";
        yield return Fight();
        Note("fight over after " + (Time.time - t0).ToString("0.00") + " s: hits " + hits + " (defended " + blockedHits + "), clashes " + clashes + ", player hurt " + playerHurt);
        if (record && EndRecording != null) EndRecording();
        Finish("DONE");
    }

    // ───────────────────────── the fight ─────────────────────────
    IEnumerator Fight()
    {
        // Establishing shot: weapons ready, the monster breathing.
        yield return Hold(3.0f);

        // 1. Telegraph: the slam winds up (its aim locks at the start of the wind-up).
        int slam = Row("Executioner Slam", 5);
        float forcedAt = Time.time;
        bool ok = Force(slam);
        Note("1. telegraph: " + ai.attackTable[slam].name + " forced=" + ok + " wind-up " + ai.attackTable[slam].windUp.ToString("0.00") + " s, arc " + ai.attackTable[slam].hitArc + " deg");
        yield return Hold(ai.attackTable[slam].windUp * 0.3f);

        // 2. Sidestep onto its flank, out of its arc but within reach, and punish while the axe is still down: a medium chop,
        //    then (3.) a full-power chop with the other sword on its heels.
        int hurt0 = playerHurt;
        float away = AxeSide();   // lateral + is the player's left: axe on the right -> step left
        yield return Step(1.2f * away, 1.0f, 0.55f, false);
        float strike = forcedAt + ai.attackTable[slam].windUp / Mathf.Max(0.25f, ai.tempoScale);
        int h0 = hits, hMid = -1, hFull = -1; float dMid = 0f, dFull = 0f;
        yield return Until(strike - 0.42f);
        int first = AxeSide() > 0f ? 1 : 0;
        StartCoroutine(Chop(first, MidSpeed(), 4f));
        yield return Until(strike - 0.1f);
        Note("2. dodge: head " + AimAngle().ToString("0") + " deg off the monster's facing (arc " + ai.attackTable[slam].hitArc + ")");
        int hitsBefore = hits;
        StartCoroutine(Chop(1 - first, FullSpeed(), 3f));
        float tEnd = Time.time + 0.9f;
        while (Time.time < tEnd) { ReadyPose(); if (hits > hitsBefore && hMid < 0) { hMid = hits; dMid = lastDamage; Note("   first chop: " + lastDamage.ToString("0.0") + " " + Tier(lastSpeed)); } else if (hMid > 0 && hits > hMid && hFull < 0) { hFull = hits; dFull = lastDamage; Note("   second chop: " + lastDamage.ToString("0.0") + " " + Tier(lastSpeed)); } yield return null; }
        Note("   slam hurt the player = " + (playerHurt > hurt0) + "; chops landed " + (hits - h0));
        yield return Ready(0.3f);
        Heal();

        // Back to the front, square on.
        yield return Hold(1.1f);
        yield return Step(0f, fightDistance, 0.8f, false);
        yield return Hold(0.6f);

        // 4. Guard: the monster raises its guard against the incoming swing; the swing lands reduced.
        yield return WaitFree();
        bool g = ai.BeginGuard(1.3f, true, null, new Color(0.78f, 0.9f, 1f), 0.08f);
        Note("4. guard raised=" + g);
        yield return Step(0f, 1.5f, 0.3f, false);   // in close behind the swing
        h0 = hits;
        yield return BodySwing(MidSpeed(), 0.1f, -1);
        Note("   guarded swing: landed " + (hits > h0) + " defended " + lastDefended + " dmg " + lastDamage.ToString("0.0"));
        yield return Ready(0.25f);
        while (ai.IsDefending) { ReadyPose(); yield return null; }
        yield return Step(0f, fightDistance, 0.5f, false);
        yield return Hold(0.3f);

        // 5. Clash: a quick table attack meets the sword held across its path (side view).
        Cut(sideCam);
        yield return Hold(0.6f);
        yield return WaitFree();
        int quick = Row("Horizontal Sweep", 0);
        float forcedAt5 = Time.time;
        ok = false;
        for (int f = 0; f < 90 && !ok; f++) { ok = Force(quick); if (!ok) { ReadyPose(); yield return null; } else if (f > 0) Note("   (forced after " + f + " frames)"); }
        forcedAt5 = Time.time;
        Note("5. clash: forced " + ai.attackTable[quick].name + "=" + ok);
        int c0 = clashes;
        yield return Parry(forcedAt5, ai.attackTable[quick]);
        Note("   clash happened " + (clashes > c0) + " (total " + clashes + ")");
        yield return Ready(0.3f);
        while (mon.isAttacking || ai.IsBusy) { ReadyPose(); yield return null; }
        Heal();
        yield return Hold(0.4f);

        // 6. The monster's counter (its own counter path: the fastest melee row with a readable telegraph).
        yield return WaitFree();
        hurt0 = playerHurt;
        ok = ai.BeginCounter(0.55f);
        Note("6. counter started=" + ok + (ai.CurrentAttack != null ? " (" + ai.CurrentAttack.name + ")" : ""));
        while (mon.isAttacking || ai.IsBusy) { ReadyPose(); yield return null; }
        Note("   counter hit the player=" + (playerHurt > hurt0));
        Cut(shoulderCam);
        Heal();
        yield return Hold(0.9f);

        // 7. Low HP (set for the demo; the wound tint follows HP) and a final strong overhead hit after a dodge.
        hp.currentHealth = hp.maxHealth * 0.16f - 1f;
        hp.Heal(1f);   // fires OnHealthChanged: the HUD bar and the wound tint follow
        yield return Hold(0.8f);
        yield return WaitFree();
        int chop = Row("Overhead Chop", 9);
        forcedAt = Time.time;
        Force(chop);
        Note("7. last exchange: forced " + ai.attackTable[chop].name + ", arc " + ai.attackTable[chop].hitArc + " deg");
        hurt0 = playerHurt;
        away = AxeSide();
        yield return Hold(ai.attackTable[chop].windUp * 0.1f);
        yield return Step(1.2f * away, 1.0f, 0.4f, false);   // out of its arc, within reach
        strike = forcedAt + ai.attackTable[chop].windUp / Mathf.Max(0.25f, ai.tempoScale);
        yield return Until(strike - 0.3f);
        h0 = hits;
        StartCoroutine(Chop(AxeSide() > 0f ? 1 : 0, FullSpeed(), 3f));
        tEnd = Time.time + 0.9f;
        while (Time.time < tEnd) { ReadyPose(); yield return null; }
        Note("   dodge: hurt " + (playerHurt > hurt0) + ", head " + AimAngle().ToString("0") + " deg off the monster's facing");
        Note("   final hit: landed " + (hits > h0) + " dmg " + lastDamage.ToString("0.0") + " tier " + Tier(lastSpeed) + " HP left " + (hp.Normalized * 100f).ToString("0") + " %");
        yield return Ready(0.4f);
        yield return Hold(2.8f);
    }

    // ───────────────────────── aiming around the monster's weapon ─────────────────────────
    IEnumerator Until(float t) { while (Time.time < t) { ReadyPose(); yield return null; } }

    /// <summary>A chop with sword i down through the chest's bearing, `wide` degrees away from the axe.</summary>
    IEnumerator Chop(int i, float speed, float wide)
    {
        float yaw = Azimuth(Chest()) - AxeSide() * wide;
        Note("   chop with " + weapons[i].name + " at " + yaw.ToString("0") + " deg, chest " + (Chest() - head.position).magnitude.ToString("0.00") + " m, " + speed.ToString("0.0") + " m/s");
        yield return Overhead(i, speed, yaw, 0f);
        yield return Ready(0.25f);
    }

    int lastSword;

    Collider Axe()
    {
        Collider best = null; float vol = 0f;
        foreach (var c in mon.GetComponentsInChildren<Collider>(true))
        {
            if (!c.enabled || !c.CompareTag("MonsterWeapon")) continue;
            var e = c.bounds.extents; float v = e.x * e.y * e.z;
            if (v > vol) { vol = v; best = c; }
        }
        return best;
    }

    Vector3 Chest()
    {
        var an = mon.GetComponentInChildren<Animator>();
        var b = an != null && an.isHuman ? an.GetBoneTransform(HumanBodyBones.Chest) : null;
        return b != null ? b.position : mon.transform.position + Vector3.up * 1.3f;
    }

    float Azimuth(Vector3 world) { Vector3 l = head.InverseTransformPoint(world); return Mathf.Atan2(l.x, l.z) * Mathf.Rad2Deg; }

    /// <summary>+1 when the monster's axe is to the right of its chest as the player sees it, -1 to the left. The fight frame's
    /// lateral + is the player's left, so a step of AxeSide() * x moves away from the axe.</summary>
    float AxeSide()
    {
        var axe = Axe();
        if (axe == null) return 1f;
        return Azimuth(axe.bounds.center) >= Azimuth(Chest()) ? 1f : -1f;
    }

    /// <summary>A horizontal cut that starts on the side away from the axe, so the blade reaches the body first (a swing that has
    /// landed on the body cannot turn into a clash on the way through). sword -1 = the one on that side.</summary>
    IEnumerator BodySwing(float speed, float wind, int sword)
    {
        float side = AxeSide(), chest = Azimuth(Chest());
        int i = sword >= 0 ? sword : (side > 0f ? 1 : 0);   // axe on the right: the left sword cuts left to right
        lastSword = i;
        float from = chest - side * 75f, to = chest + side * 35f;
        Note("   cut with " + weapons[i].name + " from " + from.ToString("0") + " to " + to.ToString("0") + " deg (chest " + chest.ToString("0") + ", axe " + (side > 0 ? "right" : "left") + ")");
        yield return Swing(i, from, to, speed, 0f, wind);
    }

    /// <summary>A chop down through the chest's bearing (over the axe, which is held at chest height), nudged away from the axe.
    /// roll tilts the plane into a diagonal cut (toward the sword's own side); delay lets a step finish first.</summary>
    IEnumerator BodyOverhead(float speed, int sword, float roll, float delay, float wide)
    {
        float side = AxeSide();
        int i = sword >= 0 ? sword : (side > 0f ? 1 : 0);
        lastSword = i;
        float t0 = Time.time;
        while (Time.time - t0 < delay) { ReadyPose(); yield return null; }
        float yaw = Azimuth(Chest()) - side * wide;
        var axe = Axe();
        Note("   chop with " + weapons[i].name + " at " + yaw.ToString("0") + " deg, roll " + roll.ToString("0") + " (axe at " + (axe != null ? Azimuth(axe.bounds.center).ToString("0") : "-") + " deg, chest " + (Chest() - head.position).magnitude.ToString("0.00") + " m)");
        yield return Overhead(i, speed, yaw, i == 0 ? -roll : roll);
    }

    // ───────────────────────── beats ─────────────────────────
    IEnumerator Hold(float s)
    {
        float end = Time.time + s;
        while (Time.time < end) { ReadyPose(); yield return null; }
    }

    IEnumerator WaitFree()
    {
        float t0 = Time.time;
        while ((ai.IsBusy || mon.isAttacking || ai.IsDefending || ai.IsStaggered) && Time.time - t0 < 6f) { ReadyPose(); yield return null; }
    }

    /// <summary>Waits for the impact frame of an attack forced at `forcedAt` (its wind-up at the monster's tempo).</summary>
    IEnumerator UntilStrike(float forcedAt, MonsterAI.AttackDefinition a)
    {
        float at = forcedAt + a.windUp / Mathf.Max(0.25f, ai.tempoScale) + 0.03f;
        while (Time.time < at) { ReadyPose(); yield return null; }
    }

    float AimAngle()
    {
        Vector3 to = head.position - mon.transform.position; to.y = 0f;
        return Vector3.Angle(mon.transform.forward, to);
    }

    float MidSpeed() { var x = XPManager.Instance; return x != null ? (x.mediumSwingSpeed + x.fullSwingSpeed) * 0.5f : 3.7f; }
    float FullSpeed() { var x = XPManager.Instance; return x != null ? Mathf.Min(x.fullSwingSpeed * 1.35f, 7.5f) : 6.2f; }
    static string Tier(float v) => XPManager.Instance != null ? XPManager.Instance.GetSwingTier(v) : v.ToString("0.0");

    int Row(string name, int fallback)
    {
        for (int i = 0; i < ai.attackTable.Count; i++) if (ai.attackTable[i] != null && ai.attackTable[i].name == name) return i;
        return Mathf.Clamp(fallback, 0, ai.attackTable.Count - 1);
    }

    /// <summary>A forced table attack. The monster's own picks are held off by parking its cooldown, which BeginAttackTrigger also
    /// checks, so the cooldown is opened for this one call (the routine starts synchronously) and parked again.</summary>
    bool Force(int row)
    {
        float cd = mon.attackCooldown;
        // a riposte may have deferred the next attack (lastAttackTime in the future): open the gate by exactly that much
        var f = typeof(MonsterController).GetField("lastAttackTime", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        float last = f != null ? (float)f.GetValue(mon) : 0f;
        mon.attackCooldown = Mathf.Min(0f, Time.time - last);
        ai.DebugForceAttack(row);
        mon.attackCooldown = cd;
        return mon.isAttacking && ai.CurrentAttack == ai.attackTable[row];
    }

    void Heal() { if (PlayerHealth.Instance != null) PlayerHealth.Instance.FullHeal(); }

    void Quiet(bool on)
    {
        var f = typeof(MonsterAI).GetField("baseAttackCooldown", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var nt = typeof(MonsterAI).GetField("nextThinkTime", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (on) { savedCd = mon.attackCooldown; savedBase = (float)f.GetValue(ai); mon.attackCooldown = 9999f; f.SetValue(ai, 9999f); }
        else { mon.attackCooldown = savedCd; f.SetValue(ai, savedBase); nt.SetValue(ai, 0f); }
    }

    // ───────────────────────── the player's body (rig) ─────────────────────────
    /// <summary>Puts the head `distance` in front of the monster, facing it.</summary>
    void PlaceHead(float distance)
    {
        MoveHeadTo(HeadTarget(0f, distance));
        FaceMonster();
    }

    Vector3 HeadTarget(float lateral, float distance)
    {
        // lateral + = the player's left (seen from the player); the frame is the monster where it stands now, facing the player
        Vector3 o = mon.transform.position, fwd = Flat(head.position - o), right = Vector3.Cross(Vector3.up, fwd);
        Vector3 p = o + fwd * distance + right * lateral;
        p.y = head.position.y;
        return p;
    }

    /// <summary>A sidestep target in the monster's own frame (its facing is the attack's aim): lateral + = the player's left.</summary>
    Vector3 DodgeTarget(float lateral, float distance)
    {
        Vector3 o = mon.transform.position, fwd = Flat(mon.transform.forward), right = Vector3.Cross(Vector3.up, fwd);
        // seen from the player (facing the monster), the player's left is the monster's right
        Vector3 p = o + fwd * distance + right * lateral;
        p.y = head.position.y;
        return p;
    }

    void MoveHeadTo(Vector3 p) { Vector3 d = p - head.position; d.y = 0f; rig.transform.position += d; }

    void FaceMonster()
    {
        Vector3 to = mon.transform.position - head.position; to.y = 0f;
        if (to.sqrMagnitude < 1e-4f) return;
        Quaternion want = Quaternion.LookRotation(to);
        Quaternion delta = want * Quaternion.Inverse(Quaternion.LookRotation(Flat(head.forward)));
        Vector3 pivot = head.position;
        rig.transform.rotation = delta * rig.transform.rotation;
        rig.transform.position = pivot + delta * (rig.transform.position - pivot);
    }

    /// <summary>A step to (lateral, distance) over `seconds`, eased; the head keeps facing the monster.</summary>
    IEnumerator Step(float lateral, float distance, float seconds, bool quick)
    {
        Vector3 a = head.position, b = lateral == 0f ? HeadTarget(0f, distance) : DodgeTarget(lateral, distance);
        float t0 = Time.time;
        while (true)
        {
            float k = Mathf.Clamp01((Time.time - t0) / seconds);
            float e = quick ? 1f - (1f - k) * (1f - k) * (1f - k) : k * k * (3f - 2f * k);
            MoveHeadTo(Vector3.Lerp(a, b, e));
            FaceMonster();
            ReadyPose();
            if (k >= 1f) break;
            yield return null;
        }
    }

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward; }

    // ───────────────────────── the weapons ─────────────────────────
    void CaptureRest()
    {
        var pc = PlayerCombat.Instance;
        weapons[0] = pc.weaponTransform; weapons[1] = pc.offhandWeaponTransform;
        for (int i = 0; i < 2; i++)
        {
            var w = weapons[i];
            if (w == null) continue;
            restPos[i] = head.InverseTransformPoint(w.position);
            restRot[i] = Quaternion.Inverse(head.rotation) * w.rotation;
            var col = w.GetComponentInChildren<HitDetection>().GetComponent<Collider>();
            bladeLocal[i] = (Quaternion.Inverse(head.rotation) * (col.bounds.center - w.position)).normalized;
        }
    }

    /// <summary>Weapons the script is not swinging: a raised guard in front of the head (blades up and out, so both read on camera),
    /// with a slow breath.</summary>
    void ReadyPose()
    {
        float b = Mathf.Sin(Time.time * 1.7f) * 0.012f;
        for (int i = 0; i < 2; i++)
        {
            if (weapons[i] == null || scripted[i]) continue;
            GuardPose(i, out Vector3 p, out Quaternion r);
            weapons[i].position = head.TransformPoint(p + new Vector3(0f, b * (i == 0 ? 1f : -1f), 0f));
            weapons[i].rotation = head.rotation * r;
        }
    }

    void GuardPose(int i, out Vector3 pos, out Quaternion rot)
    {
        float side = i == 0 ? 1f : -1f;
        // close to the body and upright: this monster holds its axe about 0.8 m in front of the player's face, and a blade resting
        // inside it would clash and lock the next swing (HitDetection: a parried swing cannot score until the weapon slows)
        pos = new Vector3(0.25f * side, -0.33f, 0.24f);
        rot = Quaternion.FromToRotation(bladeLocal[i], new Vector3(0.28f * side, 0.92f, 0.28f).normalized) * restRot[i];
    }

    IEnumerator Ready(float seconds)
    {
        float t0 = Time.time;
        Vector3[] p0 = new Vector3[2]; Quaternion[] r0 = new Quaternion[2];
        for (int i = 0; i < 2; i++) if (weapons[i] != null) { p0[i] = head.InverseTransformPoint(weapons[i].position); r0[i] = Quaternion.Inverse(head.rotation) * weapons[i].rotation; }
        while (true)
        {
            float k = Mathf.Clamp01((Time.time - t0) / seconds), e = k * k * (3f - 2f * k);
            for (int i = 0; i < 2; i++)
            {
                if (weapons[i] == null || !scripted[i]) continue;
                GuardPose(i, out Vector3 gp, out Quaternion gr);
                weapons[i].position = head.TransformPoint(Vector3.Lerp(p0[i], gp, e));
                weapons[i].rotation = head.rotation * Quaternion.Slerp(r0[i], gr, e);
            }
            if (k >= 1f) break;
            yield return null;
        }
        scripted[0] = scripted[1] = false;
    }

    float lastSpeed;

    /// <summary>Hand pose of an arc swing at angle `deg` (head space): the hand on a circle around the shoulder, the blade along
    /// the radius (arm and sword in one line), the arc plane tilted by `tilt` degrees.</summary>
    void ArcPose(int i, float deg, float tilt, out Vector3 pos, out Quaternion rot)
    {
        float side = i == 0 ? 1f : -1f;
        Vector3 shoulder = new Vector3(0.17f * side, -0.3f, 0.02f);
        Quaternion plane = Quaternion.AngleAxis(tilt, Vector3.forward) * Quaternion.AngleAxis(deg, Vector3.up);
        Vector3 radial = plane * new Vector3(0f, 0.12f, 1f).normalized;
        pos = shoulder + radial * reach;
        Vector3 blade = (radial + Vector3.up * 0.08f).normalized;
        rot = Quaternion.FromToRotation(bladeLocal[i], blade) * restRot[i];
    }

    /// <summary>A scripted arc swing: wind back to the start angle, cut through at `speed` (m/s at the hand, the speed HitDetection
    /// measures), follow through, hold. Angles: + = the player's right.</summary>
    IEnumerator Swing(int i, float fromDeg, float toDeg, float speed, float tilt, float windSeconds)
    {
        if (weapons[i] == null) yield break;
        scripted[i] = true;
        // wind back
        Vector3 p0 = head.InverseTransformPoint(weapons[i].position); Quaternion r0 = Quaternion.Inverse(head.rotation) * weapons[i].rotation;
        ArcPose(i, fromDeg, tilt, out Vector3 ps, out Quaternion rs);
        float t0 = Time.time, wind = Mathf.Max(0.08f, windSeconds + 0.12f);
        while (true)
        {
            float k = Mathf.Clamp01((Time.time - t0) / wind), e = k * k * (3f - 2f * k);
            Apply(i, Vector3.Lerp(p0, ps, e), Quaternion.Slerp(r0, rs, e));
            ReadyPose();
            if (k >= 1f) break;
            yield return null;
        }
        // the cut: constant angular speed, so the hand moves at `speed`
        float omega = speed / reach * Mathf.Rad2Deg;
        float dur = Mathf.Abs(toDeg - fromDeg) / omega;
        t0 = Time.time;
        while (true)
        {
            float k = Mathf.Clamp01((Time.time - t0) / dur);
            ArcPose(i, Mathf.Lerp(fromDeg, toDeg, k), tilt, out Vector3 p, out Quaternion r);
            Apply(i, p, r);
            ReadyPose();
            if (k >= 1f) break;
            yield return null;
        }
        // follow through and slow down (below the speed gate within a few frames)
        float extra = Mathf.Sign(toDeg - fromDeg) * 25f;
        t0 = Time.time;
        while (true)
        {
            float k = Mathf.Clamp01((Time.time - t0) / 0.22f), e = 1f - (1f - k) * (1f - k);
            ArcPose(i, toDeg + extra * e, tilt, out Vector3 p, out Quaternion r);
            Apply(i, p, r);
            ReadyPose();
            if (k >= 1f) break;
            yield return null;
        }
        yield return Frames(4);
    }

    /// <summary>An overhead chop (the arc plane vertical) with sword i.</summary>
    IEnumerator Overhead(int i, float speed, float yaw, float roll)
    {
        if (weapons[i] == null) yield break;
        scripted[i] = true;
        Quaternion turn = Quaternion.AngleAxis(yaw, Vector3.up) * Quaternion.AngleAxis(roll, Vector3.forward);
        Vector3 shoulder = turn * new Vector3(0f, -0.22f, 0.05f) + new Vector3(i == 0 ? 0.12f : -0.12f, 0f, 0f);
        System.Func<float, Vector3> radial = deg => turn * (Quaternion.AngleAxis(deg, Vector3.right) * Vector3.forward);   // + = down
        System.Action<float> pose = deg =>
        {
            Vector3 r = radial(deg);
            Apply(i, shoulder + r * reach, Quaternion.FromToRotation(bladeLocal[i], (r + Vector3.down * 0.02f).normalized) * restRot[i]);
        };
        Vector3 p0 = head.InverseTransformPoint(weapons[i].position); Quaternion r0 = Quaternion.Inverse(head.rotation) * weapons[i].rotation;
        Vector3 rs = radial(-95f);
        Quaternion rq = Quaternion.FromToRotation(bladeLocal[i], rs) * restRot[i];
        float t0 = Time.time;
        while (true)
        {
            float k = Mathf.Clamp01((Time.time - t0) / 0.3f), e = k * k * (3f - 2f * k);
            Apply(i, Vector3.Lerp(p0, shoulder + rs * reach, e), Quaternion.Slerp(r0, rq, e));
            ReadyPose();
            if (k >= 1f) break;
            yield return null;
        }
        float omega = speed / reach * Mathf.Rad2Deg, dur = 140f / omega;
        t0 = Time.time;
        while (true)
        {
            float k = Mathf.Clamp01((Time.time - t0) / dur);
            pose(Mathf.Lerp(-95f, 45f, k));
            ReadyPose();
            if (k >= 1f) break;
            yield return null;
        }
        t0 = Time.time;
        while (true)
        {
            float k = Mathf.Clamp01((Time.time - t0) / 0.2f);
            pose(45f + 18f * (1f - (1f - k) * (1f - k)));
            ReadyPose();
            if (k >= 1f) break;
            yield return null;
        }
        yield return Frames(4);
    }

    /// <summary>The clash: both swords crossed in front at the height of the axe; the sweep runs into them through HitDetection's
    /// own clash check (an attack in its strike window passes the gate whatever the swords' speed). Held until the clash or the
    /// end of the attack.</summary>
    IEnumerator Parry(float forcedAt, MonsterAI.AttackDefinition a)
    {
        if (Axe() == null) { Note("   no monster weapon collider"); yield break; }
        scripted[0] = scripted[1] = true;
        Vector3[] p0 = new Vector3[2]; Quaternion[] r0 = new Quaternion[2], x = new Quaternion[2];
        Vector3[] hand = { new Vector3(0.2f, -0.38f, 0.42f), new Vector3(-0.2f, -0.38f, 0.42f) };
        for (int i = 0; i < 2; i++)
        {
            p0[i] = head.InverseTransformPoint(weapons[i].position); r0[i] = Quaternion.Inverse(head.rotation) * weapons[i].rotation;
            float side = i == 0 ? 1f : -1f;
            x[i] = Quaternion.FromToRotation(bladeLocal[i], new Vector3(-0.55f * side, 0.75f, 0.4f).normalized) * restRot[i];
        }
        float t0 = Time.time;
        while (true)
        {
            float k = Mathf.Clamp01((Time.time - t0) / 0.3f), e = k * k * (3f - 2f * k);
            for (int i = 0; i < 2; i++) Apply(i, Vector3.Lerp(p0[i], hand[i], e), Quaternion.Slerp(r0[i], x[i], e));
            if (k >= 1f) break;
            yield return null;
        }
        int c0 = clashes; t0 = Time.time;
        while (clashes == c0 && Time.time - t0 < 2.5f && (mon.isAttacking || Time.time - t0 < 0.3f))
        {
            float k = Mathf.Min(1f, (Time.time - t0) / 0.8f);
            for (int i = 0; i < 2; i++) Apply(i, hand[i] + new Vector3(0f, 0f, 0.12f * k), x[i]);
            yield return null;
        }
        yield return Hold(0.25f);
    }

    void Apply(int i, Vector3 posHead, Quaternion rotHead)
    {
        weapons[i].position = head.TransformPoint(posHead);
        weapons[i].rotation = head.rotation * rotHead;
    }

    static IEnumerator Frames(int n) { for (int k = 0; k < n; k++) yield return null; }

    // ───────────────────────── cameras ─────────────────────────
    void BuildCameras()
    {
        var src = head.GetComponent<Camera>();
        shoulderCam = MakeCam("ZZDemoCam_Shoulder", src, shoulderFov);
        sideCam = MakeCam("ZZDemoCam_Side", src, sideFov);
        // the in-headset damage vignette hangs in front of the eyes: from outside it reads as a red box, so these cameras skip it
        foreach (var r in head.GetComponentsInChildren<Renderer>(true)) headOnly.Add(r);
        UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering += BeginCam;
        UnityEngine.Rendering.RenderPipelineManager.endCameraRendering += EndCam;
        var ui = CombatUIController.Instance;
        var hud = ui != null ? ui.GetComponentInParent<Canvas>() : null;
        if (hud == null) { var go = GameObject.Find("CombatCanvas"); if (go != null) hud = go.GetComponent<Canvas>(); }
        if (hud != null && hud.gameObject.layer != 0) sideCam.cullingMask &= ~(1 << hud.gameObject.layer);   // the HUD faces the player: edge-on from the side
        Cut(shoulderCam);
        SnapShoulder();
    }

    readonly System.Collections.Generic.List<Renderer> headOnly = new System.Collections.Generic.List<Renderer>();
    readonly System.Collections.Generic.List<bool> headOnlyWas = new System.Collections.Generic.List<bool>();

    void BeginCam(UnityEngine.Rendering.ScriptableRenderContext ctx, Camera c)
    {
        if (c != shoulderCam && c != sideCam) return;
        headOnlyWas.Clear();
        foreach (var r in headOnly) { headOnlyWas.Add(r != null && r.forceRenderingOff); if (r != null) r.forceRenderingOff = true; }
    }

    void EndCam(UnityEngine.Rendering.ScriptableRenderContext ctx, Camera c)
    {
        if (c != shoulderCam && c != sideCam) return;
        for (int i = 0; i < headOnly.Count && i < headOnlyWas.Count; i++) if (headOnly[i] != null) headOnly[i].forceRenderingOff = headOnlyWas[i];
    }

    /// <summary>Side camera, placed at the cut: square to the line between the fighters where they stand now, on the player's
    /// right, a little toward the player.</summary>
    void PlaceSide()
    {
        Vector3 m = mon.transform.position, h = head.position; m.y = 0f; h.y = 0f;
        Vector3 fwd = Flat(h - m), right = Vector3.Cross(Vector3.up, fwd);   // -right = the player's right
        Vector3 mid = Vector3.Lerp(m, h, 0.5f);
        Vector3 pos = mid - right * sideDistance + fwd * 0.35f; pos.y = sideHeight;
        sideCam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(new Vector3(mid.x, 1.2f, mid.z) - pos));
    }

    Camera MakeCam(string n, Camera src, float fov)
    {
        var go = new GameObject(n);
        var c = go.AddComponent<Camera>();
        c.CopyFrom(src);
        c.targetTexture = null;
        c.fieldOfView = fov;
        c.nearClipPlane = 0.05f;
        c.depth = 50f;
        c.enabled = false;
        var srcData = src.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
        var data = go.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
        if (srcData != null) { data.renderPostProcessing = srcData.renderPostProcessing; data.renderShadows = srcData.renderShadows; }
        data.antialiasing = UnityEngine.Rendering.Universal.AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        return c;
    }

    void Cut(Camera c)
    {
        if (shoulderCam != null) shoulderCam.enabled = c == shoulderCam;
        if (sideCam != null) sideCam.enabled = c == sideCam;
        activeCam = c;
        if (c == shoulderCam) SnapShoulder();
        if (c == sideCam) PlaceSide();
        Note("camera -> " + c.name);
    }

    Vector3 camVel;
    void ShoulderPose(out Vector3 pos, out Quaternion rot)
    {
        Vector3 fwd = Flat(mon.transform.position - head.position);
        Vector3 right = Vector3.Cross(Vector3.up, fwd);
        pos = head.position - fwd * shoulderBack + Vector3.up * shoulderUp + right * shoulderRight;
        Vector3 chest = mon.transform.position + Vector3.up * 1.15f;
        Vector3 look = Vector3.Lerp(head.position + fwd * 0.5f + Vector3.down * 0.35f, chest, 0.62f);
        rot = Quaternion.LookRotation(look - pos);
    }

    void SnapShoulder()
    {
        if (shoulderCam == null || mon == null) return;
        ShoulderPose(out Vector3 p, out Quaternion r);
        shoulderCam.transform.SetPositionAndRotation(p, r);
        camVel = Vector3.zero;
    }

    void LateUpdate()
    {
        if (shoulderCam == null || mon == null || activeCam != shoulderCam) return;
        ShoulderPose(out Vector3 p, out Quaternion r);
        var t = shoulderCam.transform;
        t.position = Vector3.SmoothDamp(t.position, p, ref camVel, 0.14f);
        t.rotation = Quaternion.Slerp(t.rotation, r, 1f - Mathf.Exp(-Time.deltaTime * 9f));
    }

    // ───────────────────────── overlays ─────────────────────────
    void HideDebug()
    {
        foreach (var p in FindObjectsByType<ExpertMotionPredictor>()) p.debugOverlay = false;
        var ui = CombatUIController.Instance;
        if (ui != null) ui.debugReadout = false;
        var pnp = FindAnyObjectByType<PlayerNamePicker>();
        var pf = typeof(PlayerNamePicker).GetField("panel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (pnp != null && pf != null && pf.GetValue(pnp) is GameObject panel && panel != null) panel.SetActive(false);
        if (pnp != null) pnp.enabled = false;
        var pm = FindAnyObjectByType<PauseMenu>();
        var hf = typeof(PauseMenu).GetField("hintRoot", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (pm != null && hf != null && hf.GetValue(pm) is GameObject hint && hint != null) hint.SetActive(false);
    }

    // ───────────────────────── events ─────────────────────────
    void OnHit(HitDetection s, float speed, bool defended)
    {
        hits++; lastSpeed = speed; lastDefended = defended; if (defended) blockedHits++;
        Note("     hit by " + s.name + " at " + speed.ToString("0.0") + " m/s" + (defended ? " (defended)" : ""));
    }
    void OnClash(HitDetection s, float speed)
    {
        clashes++;
        Note("     clash: " + s.name + " on " + (s.LastClashWeapon != null ? s.LastClashWeapon.name : "?") + " at " + speed.ToString("0.0") + " m/s, monster striking " + (mon != null && mon.isAttacking && ai.InStrikeWindow));
    }
    void OnDamaged(float amount, Vector3 at) { lastDamage = amount; }
    void OnHurt(float amount, bool blocked, float _) { playerHurt++; Note("     player hurt " + amount.ToString("0.0") + (blocked ? " (blocked)" : "")); }

    bool wasAttacking, wasStrike;
    void Update()
    {
        if (mon == null) return;
        bool atk = mon.isAttacking, st = ai.InStrikeWindow;
        if (atk != wasAttacking) Note("     monster " + (atk ? "attack starts: " + (ai.CurrentAttack != null ? ai.CurrentAttack.name : "?") : "attack ends") + ", distance " + ai.DistanceToTarget().ToString("0.00"));
        if (st != wasStrike) Note("     strike window " + (st ? "opens" : "closes"));
        wasAttacking = atk; wasStrike = st;
        if (Time.frameCount % 15 == 0) HideDebug();
    }

    void Finish(string why)
    {
        if (mon != null) Quiet(false);
        HitDetection.HitLanded -= OnHit; HitDetection.WeaponClashed -= OnClash;
        if (hp != null) hp.OnDamaged -= OnDamaged;
        PlayerHealth.DamageTaken -= OnHurt;
        var rep = SessionReporter.Instance;
        if (rep != null && !string.IsNullOrEmpty(savedUrl)) rep.baseUrl = savedUrl;
        UnityEditor.SessionState.EraseBool(ArmedKey);
        Note(why + "; reporter restored to " + (rep != null ? rep.baseUrl : "none"));
        Status = why; Done = true;
        UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering -= BeginCam;
        UnityEngine.Rendering.RenderPipelineManager.endCameraRendering -= EndCam;
        if (shoulderCam != null) Destroy(shoulderCam.gameObject);
        if (sideCam != null) Destroy(sideCam.gameObject);
        enabled = false;
    }

    void OnDestroy() { if (!Done) Finish("stopped"); }
#endif
}
