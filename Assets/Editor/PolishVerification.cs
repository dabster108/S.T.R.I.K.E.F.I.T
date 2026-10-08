// PolishVerification.cs
// Purpose: Play-mode probes for the polish-2 pass (Editor only, never in a build). Drives the real weapon colliders through
//          the real HitDetection trigger path and logs every combat sound (SfxTrace) with its cause.
//          Jobs: "sound" = scripted fight (strikes, air swings, weapons crossing, floor touches) with a sound audit;
//                "cross" = the two player weapons overlapped and dragged through each other only.
// Dependencies: CombatVerification.Prepare, SfxTrace, HitDetection, CombatFeedback, PlayerCombat, BattleManager
// Attach to: nothing (static, driven over MCP)

using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class PolishVerification
{
    // ───────────────────────── job runner (same pattern as CombatVerification) ─────────────────────────
    static IEnumerator job;
    static readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
    static readonly StringBuilder log = new StringBuilder();
    static int lastFrame = -1;
    static string jobName = "";

    public static string Status() => (job != null ? $"RUNNING {jobName} t={Time.time:0.0}\n" : "IDLE\n") + log;

    public static string Run(string name)
    {
        if (!EditorApplication.isPlaying) return "not playing";
        Stop();
        log.Length = 0;
        jobName = name;
        switch (name)
        {
            case "sound": job = SoundFight(fightSeconds); break;
            case "cross": job = CrossOnly(); break;
            case "centre": job = Centre(centreSeconds); break;
            case "blood": job = Blood(); break;
            case "fire": job = Fire(); break;
            case "breath": job = Breath(); break;
            case "shake": job = ShakeProbe(); break;
            case "jitter": job = Jitter(); break;
            case "regress": job = Regress(); break;
            case "alloc": job = AllocProbe(); break;
            default: return "unknown job " + name;
        }
        EditorApplication.update += Tick;
        return "started " + name;
    }

    public static void Stop()
    {
        job = null; stack.Clear();
        EditorApplication.update -= Tick;
        Listen(false);
    }

    static void Tick()
    {
        if (job == null || !EditorApplication.isPlaying) { Stop(); return; }
        if (Time.frameCount == lastFrame) return;
        lastFrame = Time.frameCount;
        try
        {
            while (true)
            {
                var top = stack.Count > 0 ? stack.Peek() : job;
                if (top.MoveNext())
                {
                    if (top.Current is IEnumerator nested) { stack.Push(nested); continue; }
                    break;
                }
                if (stack.Count > 0) { stack.Pop(); continue; }
                log.AppendLine("DONE"); Stop(); break;
            }
        }
        catch (System.Exception e) { log.AppendLine("EXCEPTION " + e); Stop(); }
    }

    static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }

    // ───────────────────────── settings ─────────────────────────
    public static float fightSeconds = 100f;
    /// <summary>true = switch every new toggle off (old behaviour) for a before/after comparison.</summary>
    public static bool legacy = false;
    // Old WeaponSwingAudio values per weapon, restored by legacy mode.
    public static float legacyWhooshTrigger = 2.2f, legacyWhooshInterval = 0.3f;

    // ───────────────────────── sound / event log ─────────────────────────
    struct Snd { public float t; public int frame; public string src, clip, cause, phase; public bool touching; }
    static readonly List<Snd> sounds = new List<Snd>();
    static readonly Dictionary<int, List<Object>> hitsByFrame = new Dictionary<int, List<Object>>();
    static readonly Dictionary<int, List<Object>> clashByFrame = new Dictionary<int, List<Object>>();
    static readonly HashSet<int> damageFrames = new HashSet<int>();
    static int hits, clashes, damageEvents, slow;
    static string phase = "idle";
    static bool touchingMonster;

    static void OnSound(Object src, AudioClip clip, string cause)
    {
        sounds.Add(new Snd { t = Time.time, frame = Time.frameCount, src = src != null ? src.name : "?", clip = clip.name, cause = cause, phase = phase, touching = touchingMonster });
    }
    static void Add(Dictionary<int, List<Object>> d, Object o)
    {
        if (!d.TryGetValue(Time.frameCount, out var l)) d[Time.frameCount] = l = new List<Object>();
        l.Add(o);
    }
    // Contact events are only kept when the weapon collider really overlaps a collider of the right kind at that moment
    // (the events fire inside the physics callback, so the poses are current).
    static void OnHit(HitDetection s, float v, bool def) { hits++; if (Overlaps(s, false)) Add(hitsByFrame, s); else unverified++; }
    static void OnClash(HitDetection s, float v) { clashes++; if (Overlaps(s, true)) Add(clashByFrame, s); else unverified++; }
    static int unverified;
    static bool Overlaps(HitDetection s, bool monsterWeapon)
    {
        var a = s.GetComponent<Collider>();
        foreach (var c in Object.FindObjectsByType<Collider>(FindObjectsInactive.Exclude))
        {
            if (!c.enabled || c.CompareTag("MonsterWeapon") != monsterWeapon) continue;
            if (!monsterWeapon && c.GetComponentInParent<MonsterController>() == null) continue;
            if (Physics.ComputePenetration(a, a.transform.position, a.transform.rotation, c, c.transform.position, c.transform.rotation, out _, out _))
                return true;
        }
        return false;
    }
    static void OnDamage(float a, bool b, float hp) { if (a > 0f) { damageEvents++; damageFrames.Add(Time.frameCount); } }
    static void OnSlow(HitDetection s, float v, float m) { slow++; }

    static void Listen(bool on)
    {
        SfxTrace.Played -= OnSound; HitDetection.HitLanded -= OnHit; HitDetection.WeaponClashed -= OnClash;
        PlayerHealth.DamageTaken -= OnDamage; HitDetection.SwingTooSlow -= OnSlow;
        if (!on) return;
        SfxTrace.Played += OnSound; HitDetection.HitLanded += OnHit; HitDetection.WeaponClashed += OnClash;
        PlayerHealth.DamageTaken += OnDamage; HitDetection.SwingTooSlow += OnSlow;
    }

    static void ResetLog()
    {
        sounds.Clear(); hitsByFrame.Clear(); clashByFrame.Clear(); damageFrames.Clear();
        hits = clashes = damageEvents = slow = unverified = 0;
    }

    // ───────────────────────── scene helpers ─────────────────────────
    static OVRCameraRig rig;
    static Transform Head => rig.centerEyeAnchor;
    static MonsterController M => BattleManager.Instance != null ? BattleManager.Instance.ActiveMonster : null;
    static readonly List<Collider> monsterCols = new List<Collider>();

    static IEnumerator StartFight()
    {
        log.AppendLine(CombatVerification.Prepare());
        rig = Object.FindAnyObjectByType<OVRCameraRig>();
        var pc = PlayerCombat.Instance;
        if (pc.weaponSets != null && pc.weaponSets.Count > 0) pc.EquipWeaponSet(CombatVerification.l3Weapon);
        yield return Frames(3);
        if (!BattleManager.Instance.IsBattleActive) BattleManager.Instance.StartBattleFromInspector();
        yield return Frames(2);
        float t0 = Time.time;
        while (Time.time - t0 < 20f)
        {
            var m = M;
            var e = m != null ? m.GetComponent<MonsterEntrance>() : null;
            if (m != null && (e == null || e.Complete)) break;
            yield return null;
        }
        if (M != null) { monsterCols.Clear(); M.GetComponentsInChildren(monsterCols); }
    }

    static void ApplyLegacy(bool on)
    {
        foreach (var hd in Object.FindObjectsByType<HitDetection>(FindObjectsInactive.Include))
        { hd.oneContactPerSwing = !on; hd.ignoreDuringEntrance = !on; hd.clashNeedsSwingOrAttack = !on; }
        foreach (var w in Object.FindObjectsByType<WeaponSwingAudio>(FindObjectsInactive.Include))
        {
            if (on) { w.triggerSpeed = legacyWhooshTrigger; w.minInterval = legacyWhooshInterval; w.sustainSeconds = 0f; }
        }
        var pc = PlayerCombat.Instance;
        pc.ignoreOwnWeaponCollisions = !on;
        var cols = pc.GetComponentsInChildren<Collider>(false);
        for (int i = 0; i < cols.Length; i++) for (int j = i + 1; j < cols.Length; j++) Physics.IgnoreCollision(cols[i], cols[j], !on);
        if (!on) pc.IgnoreOwnWeaponCollisions();
        var m = M; if (m != null) { var ai = m.GetComponent<MonsterAI>(); if (ai != null) ai.braceSoundOnReactiveGuard = on; }
    }

    static bool WeaponTouchesMonster(Collider c)
    {
        var b = c.bounds;
        foreach (var mc in monsterCols) if (mc != null && mc.enabled && mc.bounds.Intersects(b)) return true;
        return false;
    }

    struct Move { public Transform root; public Vector3 restLocal; public Quaternion restRot; public Vector3 offset, a, b; public float t0, dur; }

    static Move MakeMove(Transform root, Vector3 a, Vector3 b, float speed)
    {
        var col = root.GetComponentInChildren<HitDetection>().GetComponent<Collider>();
        var mv = new Move { root = root, restLocal = root.localPosition, restRot = root.localRotation, offset = col.bounds.center - root.position, a = a, b = b, t0 = Time.time, dur = Mathf.Max(0.03f, Vector3.Distance(a, b) / speed) };
        return mv;
    }

    static IEnumerator Play(Move[] moves, string label)
    {
        phase = label;
        // wind-back below every speed gate (0.8 m/s; a teleport would read as a tracking spike), then the stroke
        string saved = phase; phase = "windback";
        float w0 = Time.time;
        while (Time.time - w0 < 2.5f)
        {
            bool there = true;
            foreach (var mv in moves)
            {
                mv.root.position = Vector3.MoveTowards(mv.root.position, mv.a - mv.offset, 0.8f * Time.deltaTime);
                if (Vector3.Distance(mv.root.position, mv.a - mv.offset) > 0.005f) there = false;
            }
            if (there) break;
            yield return null;
        }
        phase = saved;
        yield return null;
        float t0 = Time.time, dur = 0f; foreach (var mv in moves) dur = Mathf.Max(dur, mv.dur);
        while (Time.time - t0 <= dur)
        {
            foreach (var mv in moves)
            {
                float k = Mathf.Clamp01((Time.time - t0) / mv.dur);
                mv.root.position = Vector3.Lerp(mv.a, mv.b, k) - mv.offset;
            }
            if (PlayerCombat.Instance != null) { maxR = Mathf.Max(maxR, PlayerCombat.Instance.CurrentSwingSpeed); maxL = Mathf.Max(maxL, PlayerCombat.Instance.CurrentOffhandSwingSpeed); }
            touchingMonster = false;
            foreach (var mv in moves) if (WeaponTouchesMonster(mv.root.GetComponentInChildren<HitDetection>().GetComponent<Collider>())) touchingMonster = true;
            yield return null;
        }
        if (label == "strike" || label == "air" || label == "air-fast" || label == "cross-fast" || label == "floor") lastFastStrokeEnd = Time.time;
        phase = "return";
        // slow return (below every gate)
        float r0 = Time.time;
        while (Time.time - r0 < 1.2f)
        {
            bool all = true;
            foreach (var mv in moves)
            {
                Vector3 rest = mv.root.parent.TransformPoint(mv.restLocal);
                mv.root.position = Vector3.MoveTowards(mv.root.position, rest, 0.8f * Time.deltaTime);
                mv.root.localRotation = mv.restRot;
                if (Vector3.Distance(mv.root.position, rest) > 0.01f) all = false;
            }
            if (all) break;
            yield return null;
        }
        foreach (var mv in moves) { mv.root.localPosition = mv.restLocal; mv.root.localRotation = mv.restRot; }
        phase = "rest";
        touchingMonster = false;
    }

    static float maxR, maxL, lastFastStrokeEnd = -99f;
    static readonly StringBuilder dragLog = new StringBuilder();

    static float FloorY()
    {
        RaycastHit h;
        int mask = ~0;
        var hitsArr = Physics.RaycastAll(Head.position, Vector3.down, 4f, mask, QueryTriggerInteraction.Ignore);
        float best = rig.transform.position.y;
        float bestD = 99f;
        foreach (var x in hitsArr)
        {
            if (x.collider.GetComponentInParent<OVRCameraRig>() != null || x.collider.GetComponentInParent<MonsterController>() != null) continue;
            if (x.distance < bestD) { bestD = x.distance; best = x.point.y; }
        }
        h = default;
        return best;
    }

    // ───────────────────────── Part 2: centring / yaw ─────────────────────────
    public static float centreSeconds = 40f;
    public static float swayLateral = 0.04f, swayForward = 0.03f, swayYawDeg = 6f;
    /// <summary>Player standing off the play-space centre (tracking-space offset, rig-local metres).</summary>
    public static Vector3 headOffset = Vector3.zero;
    /// <summary>Old behaviour for the before numbers: constant-speed turn, no recentring, spawn at the fixed point.</summary>
    public static bool centreLegacy = false;

    static Transform Hips(MonsterController m)
    {
        var an = m.GetComponentInChildren<Animator>();
        if (an != null && an.isHuman) { var h = an.GetBoneTransform(HumanBodyBones.Hips); if (h != null) return h; }
        foreach (var t in m.GetComponentsInChildren<Transform>()) if (t.name.EndsWith("Hips")) return t;
        return m.transform;
    }

    static IEnumerator Centre(float seconds)
    {
        log.AppendLine(CombatVerification.Prepare());
        rig = Object.FindAnyObjectByType<OVRCameraRig>();
        var bm = BattleManager.Instance;
        bm.spawnOnPlayerForwardLine = !centreLegacy;
        Vector3 tsOrig = rig.trackingSpace.localPosition;
        rig.trackingSpace.localPosition = tsOrig + headOffset;
        yield return Frames(2);
        Vector3 head0 = Head.position; head0.y = 0f;
        Vector3 F = rig.transform.forward; F.y = 0f; F.Normalize();
        Vector3 Rt = Vector3.Cross(Vector3.up, F);
        string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        log.AppendLine($"== CENTRE {scene}: rig yaw {rig.transform.eulerAngles.y:0.0}, head offset from rig origin ({Vector3.Dot(Head.position - rig.transform.position, Rt):0.000} lateral, {Vector3.Dot(Head.position - rig.transform.position, F):0.000} fwd)");
        if (bm.monsterSpawnPoint != null)
        {
            Vector3 sp = bm.monsterSpawnPoint.position; Vector3 d = sp - head0; d.y = 0f;
            log.AppendLine($"   spawn point: lateral {Vector3.Dot(d, Rt):0.000} m, distance {Vector3.Dot(d, F):0.000} m, bearing {Vector3.SignedAngle(F, d, Vector3.up):0.0} deg from rig forward, spawn yaw error {Mathf.DeltaAngle(bm.monsterSpawnPoint.eulerAngles.y, Quaternion.LookRotation(-d).eulerAngles.y):0.0} deg");
        }
        var pc = PlayerCombat.Instance;
        if (pc.weaponSets != null && pc.weaponSets.Count > 0) pc.EquipWeaponSet(CombatVerification.l3Weapon);
        yield return Frames(3);
        if (!bm.IsBattleActive) bm.StartBattleFromInspector();
        yield return Frames(1);
        var mon = M; if (mon == null) { log.AppendLine("no monster"); yield break; }
        var an = mon.GetComponentInChildren<Animator>();
        Vector3 s0 = mon.transform.position - head0; s0.y = 0f;
        log.AppendLine($"   spawned: lateral {Vector3.Dot(s0, Rt):0.000} m, distance {Vector3.Dot(s0, F):0.000} m, yaw error {Mathf.DeltaAngle(mon.transform.eulerAngles.y, Quaternion.LookRotation(-s0).eulerAngles.y):0.0} deg, applyRootMotion={(an != null && an.applyRootMotion)}");
        var hips = Hips(mon);
        var mdef = mon.GetComponent<MonsterDefense>();
        if (mdef != null) { mdef.dampedTurn = !centreLegacy; mdef.recentre = !centreLegacy; }
        log.AppendLine($"   mode: {(centreLegacy ? "LEGACY (fixed spawn, constant-speed turn, no recentre)" : "CURRENT")}, head offset {headOffset}, turn cap {(mdef != null ? mdef.turnSpeed : 0):0} deg/s");
        var entr = mon.GetComponent<MonsterEntrance>();
        float tE = Time.time; while (entr != null && !entr.Complete && Time.time - tE < 20f) yield return null;
        var ai = mon.GetComponent<MonsterAI>();
        var health = mon.GetComponent<MonsterHealth>();
        var cps = mon.GetComponent<MonsterPersonalSpace>(); if (cps != null) cps.ResetStats();
        var ts = rig.trackingSpace; Vector3 tsPos = ts.localPosition; Quaternion tsRot = ts.localRotation;
        float t0 = Time.time, lastYaw = mon.transform.eulerAngles.y, lastRate = 0f;
        float rootAbsSum = 0f, rootAbsMax = 0f, hipsAbsSum = 0f, hipsAbsMax = 0f, hipsRelMax = 0f; int n = 0;
        float freeTravel = 0f, freeTime = 0f, freeRateMax = 0f; int reversals = 0;
        float yawErrMax = 0f; int attacks = 0; bool wasAtk = false; float atkEnd = -1f;
        var post = new List<float>();
        float settledSum = 0f, settledMax = 0f; int settledN = 0, osc = 0; float lastTurnSign = 0f, lastTurnT = -9f, lastBusyT = -9f;
        float hipYawMin = 999f, hipYawMax = -999f, hipYawFreeMin = 999f, hipYawFreeMax = -999f;
        Quaternion hipRest = Quaternion.Inverse(mon.transform.rotation) * hips.rotation;
        while (Time.time - t0 < seconds && mon != null && mon.isAlive)
        {
            float t = Time.time - t0, dt = Time.deltaTime;
            if (PlayerHealth.Instance.currentHealth < PlayerHealth.Instance.maxHealth * 0.5f) PlayerHealth.Instance.FullHeal();
            if (health.currentHealth < health.maxHealth * 0.2f) health.currentHealth = health.maxHealth * 0.6f;
            // head sway: the tracking space carries the head (the monster aims at the head)
            ts.localPosition = tsPos + Vector3.right * (swayLateral * Mathf.Sin(2f * Mathf.PI * 0.25f * t)) + Vector3.forward * (swayForward * Mathf.Sin(2f * Mathf.PI * 0.17f * t + 1f));
            ts.localRotation = tsRot * Quaternion.Euler(0f, swayYawDeg * Mathf.Sin(2f * Mathf.PI * 0.2f * t), 0f);
            yield return null;
            Vector3 r = mon.transform.position - head0; r.y = 0f;
            Vector3 h = hips.position - head0; h.y = 0f;
            float rl = Vector3.Dot(r, Rt), hl = Vector3.Dot(h, Rt);
            rootAbsSum += Mathf.Abs(rl); rootAbsMax = Mathf.Max(rootAbsMax, Mathf.Abs(rl));
            hipsAbsSum += Mathf.Abs(hl); hipsAbsMax = Mathf.Max(hipsAbsMax, Mathf.Abs(hl));
            Vector3 hr = mon.transform.InverseTransformPoint(hips.position); hipsRelMax = Mathf.Max(hipsRelMax, Mathf.Abs(hr.x));
            n++;
            float yaw = mon.transform.eulerAngles.y;
            float rate = dt > 0f ? Mathf.DeltaAngle(lastYaw, yaw) / dt : 0f; lastYaw = yaw;
            bool busy = mon.isAttacking || (ai != null && ai.IsBusy);
            if (busy && !wasAtk) attacks++;
            if (!busy && wasAtk) atkEnd = Time.time;
            if (atkEnd > 0f && Time.time - atkEnd >= 1.0f) { post.Add(rl); atkEnd = -1f; }
            wasAtk = busy;
            if (busy) lastBusyT = Time.time;
            // body yaw (hips) relative to the root: what the eye reads as the monster turning, animation included
            float hy = Mathf.DeltaAngle(0f, (Quaternion.Inverse(hipRest) * (Quaternion.Inverse(mon.transform.rotation) * hips.rotation)).eulerAngles.y);
            if (!busy && Time.time - lastBusyT > 1.5f)
            {
                settledSum += Mathf.Abs(rl); settledMax = Mathf.Max(settledMax, Mathf.Abs(rl)); settledN++;
                hipYawFreeMin = Mathf.Min(hipYawFreeMin, hy); hipYawFreeMax = Mathf.Max(hipYawFreeMax, hy);
            }
            hipYawMin = Mathf.Min(hipYawMin, hy); hipYawMax = Mathf.Max(hipYawMax, hy);
            if (!busy && Mathf.Abs(rate) > 3f)
            {
                float sg = Mathf.Sign(rate);
                if (lastTurnSign != 0f && sg != lastTurnSign && Time.time - lastTurnT < 0.6f) osc++;
                lastTurnSign = sg; lastTurnT = Time.time;
            }
            if (!busy)
            {
                freeTravel += Mathf.Abs(rate) * dt; freeTime += dt; freeRateMax = Mathf.Max(freeRateMax, Mathf.Abs(rate));
                if (Mathf.Abs(rate) > 3f && Mathf.Abs(lastRate) > 3f && Mathf.Sign(rate) != Mathf.Sign(lastRate)) reversals++;
                if (Mathf.Abs(rate) > 3f) lastRate = rate;
                Vector3 toHead = Head.position - mon.transform.position; toHead.y = 0f;
                yawErrMax = Mathf.Max(yawErrMax, Mathf.Abs(Vector3.SignedAngle(mon.transform.forward, toHead, Vector3.up)));
            }
        }
        ts.localPosition = tsOrig; ts.localRotation = tsRot;
        var ps = new StringBuilder(); foreach (var v in post) ps.Append(v.ToString("0.00")).Append(' ');
        log.AppendLine($"   {seconds:0} s with head sway (+-{swayLateral * 100:0} cm lateral, +-{swayForward * 100:0} cm fwd, +-{swayYawDeg:0} deg yaw), {attacks} attacks/guards:");
        log.AppendLine($"   lateral offset from the player's forward line: root mean {rootAbsSum / Mathf.Max(1, n):0.000} / max {rootAbsMax:0.000} m; hips (visual) mean {hipsAbsSum / Mathf.Max(1, n):0.000} / max {hipsAbsMax:0.000} m; hips vs own root max {hipsRelMax:0.000} m");
        log.AppendLine($"   yaw while free: travel {freeTravel / Mathf.Max(0.01f, freeTime):0.0} deg/s, peak rate {freeRateMax:0.0} deg/s, direction reversals {reversals} ({reversals * 60f / Mathf.Max(0.01f, freeTime):0.0}/min), max aim error {yawErrMax:0.0} deg");
        log.AppendLine($"   root lateral 1 s after each attack/guard: {ps}");
        log.AppendLine($"   SETTLED (free >= 1.5 s after any attack/guard): root lateral mean {settledSum / Mathf.Max(1, settledN):0.000} / max {settledMax:0.000} m over {settledN} frames; yaw oscillations (turn reversed within 0.6 s): {osc}");
        if (cps != null) log.AppendLine($"   personal-space clamp: min head distance {cps.MinHeadDistance:0.00} m (limit {cps.headClearance:0.00}), min hand distance (monster-caused) {cps.MinHandDistanceMonsterCaused:0.00} m (limit {cps.handClearance:0.00}), breaches head {cps.HeadBreaches} hand {cps.HandBreaches}, max push {cps.MaxPush * 100f:0.0} cm");
        log.AppendLine($"   hips yaw vs root (animation): all {hipYawMin:0.0}..{hipYawMax:0.0} deg, settled idle {hipYawFreeMin:0.0}..{hipYawFreeMax:0.0} deg");
    }

    // ───────────────────────── Part 3: blood ─────────────────────────
    public static string shotDir = "";

    static IEnumerator Shot(string name)
    {
        if (string.IsNullOrEmpty(shotDir)) yield break;
        // Hide the name-picker canvas for this frame only (Canvas component, never the GameObject).
        var hidden = new List<Canvas>();
        var pnp = Object.FindAnyObjectByType<PlayerNamePicker>(FindObjectsInactive.Include);
        var f = typeof(PlayerNamePicker).GetField("panel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var panel = pnp != null && f != null ? f.GetValue(pnp) as GameObject : null;
        Vector3 panelScale = panel != null ? panel.transform.localScale : Vector3.one;
        if (panel != null) panel.transform.localScale = Vector3.zero; // capture frame only; never SetActive on the picker
        ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(shotDir, name + ".png"));
        yield return null;
        yield return null;
        if (panel != null) panel.transform.localScale = panelScale;
    }

    static IEnumerator FreezeMonster(MonsterController m)
    {
        m.attackCooldown = 9999f;
        var fai = m.GetComponent<MonsterAI>();
        float a0 = Time.time; while (fai.DistanceToTarget() > m.attackRange + 0.05f && Time.time - a0 < 8f) yield return null;
        float w0 = Time.time; while ((m.isAttacking || m.GetComponent<MonsterAI>().IsBusy) && Time.time - w0 < 8f) yield return null;
        var d = m.GetComponent<MonsterDefense>(); if (d != null) { d.reactiveGuard = false; d.timedGuard = false; }
        yield return Frames(20);
    }

    /// <summary>Centre of the body capsule (above knee height) farthest from the monster's own weapon colliders, so a probe
    /// strike lands on flesh instead of the axe held across the chest.</summary>
    static Vector3 AimTarget(MonsterController mon)
    {
        var def = mon.GetComponent<MonsterDefense>();
        Vector3 fallback = def != null && def.guardTarget != null ? def.guardTarget.position : mon.transform.position + Vector3.up * 1.3f;
        var weapons = new List<Collider>();
        foreach (var c in mon.GetComponentsInChildren<Collider>()) if (c.CompareTag("MonsterWeapon")) weapons.Add(c);
        if (weapons.Count == 0) return fallback;
        Vector3 best = fallback; float bestD = -1f;
        foreach (var cap in mon.GetComponentsInChildren<CapsuleCollider>())
        {
            if (cap.isTrigger || cap.CompareTag("MonsterWeapon") || cap.name == mon.name) continue;
            Vector3 c = cap.bounds.center;
            if (c.y < mon.transform.position.y + 0.8f) continue;
            float d = 99f; foreach (var w in weapons) d = Mathf.Min(d, Vector3.Distance(c, w.ClosestPoint(c)));
            d += cap.radius; // prefer a big body part
            if (d > bestD) { bestD = d; best = c; }
        }
        return best;
    }

    static IEnumerator StrikeAt(MonsterController mon, Transform W, float speed, bool high)
    {
        var mv = new Move[1];
        yield return StrikePrepare(mon, W, speed, high, mv);
        yield return StrikeRun(W, mv[0]);
    }

    static Vector3 strikeA;
    static IEnumerator StrikePrepare(MonsterController mon, Transform W, float speed, bool high, Move[] outMove)
    {
        Vector3 H = Head.position;
        Vector3 target = AimTarget(mon);
        Vector3 inward = target - H; inward.y = 0f; inward.Normalize();
        Vector3 side = Vector3.Cross(Vector3.up, inward);
        Vector3 b = target - inward * 0.1f;
        Vector3 a = b - inward * 0.9f + (high ? Vector3.up * 0.6f : side * 0.7f);
        var mv = MakeMove(W, a, b, speed);
        phase = "windback";
        while (Vector3.Distance(W.position, a - mv.offset) > 0.005f) { W.position = Vector3.MoveTowards(W.position, a - mv.offset, 0.8f * Time.deltaTime); yield return null; }
        outMove[0] = mv;
    }

    static IEnumerator StrikeRun(Transform W, Move mv)
    {
        phase = "strike";
        float t0 = Time.time;
        while (Time.time - t0 <= mv.dur) { W.position = Vector3.Lerp(mv.a, mv.b, Mathf.Clamp01((Time.time - t0) / mv.dur)) - mv.offset; yield return null; }
    }

    static IEnumerator ReturnHome(Transform W, Vector3 restLocal)
    {
        phase = "return";
        float r0 = Time.time;
        while (Time.time - r0 < 2f && Vector3.Distance(W.localPosition, restLocal) > 0.01f)
        { W.position = Vector3.MoveTowards(W.position, W.parent.TransformPoint(restLocal), 0.8f * Time.deltaTime); yield return null; }
        W.localPosition = restLocal;
    }

    static IEnumerator DrawCallPair(ParticleSystemRenderer r, string label)
    {
        // renderer on / off on alternate frames while particles are alive: the difference is the blood's draw-call cost
        int onSum = 0, offSum = 0, onN = 0, offN = 0, bOn = 0, bOff = 0;
        for (int i = 0; i < 40; i++)
        {
            bool on = (i & 1) == 0;
            r.enabled = on;
            yield return null;
            yield return null; // stats describe the frame that just rendered
            if (on) { onSum += UnityStats.drawCalls; bOn += UnityStats.setPassCalls; onN++; } else { offSum += UnityStats.drawCalls; bOff += UnityStats.setPassCalls; offN++; }
        }
        r.enabled = true;
        log.AppendLine($"   draw calls {label}: renderer on {onSum / (float)onN:0.0} (setpass {bOn / (float)onN:0.0}) / off {offSum / (float)offN:0.0} (setpass {bOff / (float)offN:0.0}) -> +{(onSum / (float)onN) - (offSum / (float)offN):0.0}");
    }

    static IEnumerator Blood()
    {
        yield return StartFight();
        var mon = M; if (mon == null) { log.AppendLine("no monster"); yield break; }
        yield return FreezeMonster(mon);
        var fx = Object.FindAnyObjectByType<HitBloodFx>();
        var pc = PlayerCombat.Instance;
        Transform W = pc.weaponTransform; Vector3 rest = W.localPosition;
        var hd = W.GetComponentInChildren<HitDetection>();
        string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        log.AppendLine($"== BLOOD {scene}: mode {fx.mode}, head-to-monster (root, horizontal) {mon.GetComponent<MonsterAI>().DistanceToTarget():0.00} m, 3-D {Vector3.Distance(Head.position, mon.transform.position):0.00} m");
        float[] speeds = { 1.6f, 3.0f, 4.6f, 9f };
        string[] names = { "tap/light", "light/medium", "medium", "full" };
        ParticleSystemRenderer rend = null;
        for (int k = 0; k < speeds.Length; k++)
        {
            int b0 = fx.Bursts;
            yield return StrikeAt(mon, W, speeds[k], (k & 1) == 0);
            int peak = 0;
            for (int f = 0; f < 6; f++) { peak = Mathf.Max(peak, fx.LiveParticles); yield return null; }
            string tier = CombatUIController.StyleForSpeed(speeds[k]).ToString();
            log.AppendLine($"   strike {speeds[k]:0.0} m/s ({tier}): result {hd.LastResult}, burst {(fx.Bursts > b0 ? fx.LastBurstSize : 0)} particles, live peak {peak}");
            if (k == speeds.Length - 1)
            {
                W.localPosition = rest; // weapon out of the line of sight for the capture
                yield return Shot(scene + "_blood_full_hit");
                float td = Time.time; while (Time.time - td < 0.8f) yield return null;
                log.AppendLine($"   1.0 s after the full hit: live particles {fx.LiveParticles} (drips), drips emitted so far {fx.DripsEmitted}");
                yield return Shot(scene + "_blood_drips");
                var go = GameObject.Find("HitBloodFx_" + mon.name);
                rend = go != null ? go.GetComponent<ParticleSystemRenderer>() : null;
                if (rend != null)
                {
                    // keep particles alive through the whole measurement: a steady stream of drips + a fresh burst
                    for (int j = 0; j < 3; j++) { yield return DrawCallPair(rend, "blood particles alive, pass " + (j + 1)); }
                }
            }
            yield return ReturnHome(W, rest);
            float t1 = Time.time; while (Time.time - t1 < 1.2f) yield return null;

        }
        float t2 = Time.time; while (Time.time - t2 < 2.5f) yield return null;
        log.AppendLine($"   4 s after the last hit: live particles {fx.LiveParticles}, drips emitted {fx.DripsEmitted}");
        // guarded hit: no blood
        var ai = mon.GetComponent<MonsterAI>();
        if (ai.BeginGuard(2f, false, null, Color.clear, 0f))
        {
            yield return Frames(5);
            int b0 = fx.Bursts;
            yield return StrikeAt(mon, W, 9f, true);
            yield return Frames(3);
            log.AppendLine($"   guarded full hit: result {hd.LastResult}, new bursts {fx.Bursts - b0}, live {fx.LiveParticles}");
            yield return ReturnHome(W, rest);
        }
        else log.AppendLine("   guard could not be raised for the guarded-hit test");
        float t3 = Time.time; while (Time.time - t3 < 2.5f) yield return null;
        // death burst
        var health = mon.GetComponent<MonsterHealth>();
        health.currentHealth = 1f;
        int d0 = fx.DeathBursts;
        yield return StrikeAt(mon, W, 9f, false);
        int peakD = 0;
        W.localPosition = rest;
        for (int f = 0; f < 8; f++) { peakD = Mathf.Max(peakD, fx.LiveParticles); if (f == 4) yield return Shot(scene + "_blood_death"); yield return null; }
        log.AppendLine($"   killing blow: death bursts {fx.DeathBursts - d0}, live peak {peakD}");
        yield return ReturnHome(W, rest);
        // off mode costs nothing
        log.AppendLine($"   blood system renderer enabled after test: {(rend != null && rend.enabled)}");
    }

    // ───────────────────────── Part 4: fire aura ─────────────────────────
    static int MonsterFxParticles(MonsterController m, ParticleSystem exclude)
    {
        int n = 0;
        foreach (var p in m.GetComponentsInChildren<ParticleSystem>()) if (p != exclude) n += p.particleCount;
        return n;
    }

    static IEnumerator Sample(System.Func<int> f, float secs, float[] outAvgMax)
    {
        float sum = 0f, max = 0f; int n = 0; float t0 = Time.time;
        while (Time.time - t0 < secs) { int v = f(); sum += v; max = Mathf.Max(max, v); n++; yield return null; }
        outAvgMax[0] = n > 0 ? sum / n : 0f; outAvgMax[1] = max;
    }

    static IEnumerator Fire()
    {
        yield return StartFight();
        var mon = M; if (mon == null) { log.AppendLine("no monster"); yield break; }
        string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        var aura = mon.GetComponent<MonsterFireAura>();
        var ai = mon.GetComponent<MonsterAI>();
        var health = mon.GetComponent<MonsterHealth>();
        yield return FreezeMonster(mon);
        var am = new float[2];
        if (aura == null)
        {
            yield return Sample(() => MonsterFxParticles(mon, null), 4f, am);
            log.AppendLine($"== FIRE {scene}: no MonsterFireAura (Level 3 keeps its own fire). Monster fire particles calm avg {am[0]:0.0} / max {am[1]:0}");
            yield return Shot(scene + "_fire_calm");
            int heavy3 = -1; for (int i = 0; i < ai.attackTable.Count; i++) if (ai.attackTable[i].category == MonsterAI.AttackCategory.Heavy && ai.attackTable[i].rangedKind == MonsterAI.RangedKind.None) { heavy3 = i; break; }
            if (heavy3 >= 0) { mon.attackCooldown = 0f; ai.DebugForceAttack(heavy3); mon.attackCooldown = 9999f; yield return Frames(14); yield return Shot(scene + "_fire_heavy_windup"); yield return Sample(() => MonsterFxParticles(mon, null), 1.5f, am); log.AppendLine($"   heavy attack '{ai.attackTable[heavy3].name}': avg {am[0]:0.0} / max {am[1]:0}"); }
            yield break;
        }
        log.AppendLine($"== FIRE {scene}: rate {aura.ratePerSecond}/s, life {aura.lifetime}, low-HP event {aura.lowHpFraction:P0}");
        yield return Sample(() => aura.LiveParticles, 5f, am);
        log.AppendLine($"   calm: live avg {am[0]:0.0} / max {am[1]:0}; other monster particles {MonsterFxParticles(mon, aura.Particles)}");
        yield return Shot(scene + "_fire_calm");
        // draw-call cost: aura renderer on / off on alternate frames
        var r = aura.Particles.GetComponent<ParticleSystemRenderer>();
        yield return DrawCallPair(r, "aura alive");
        // heavy / special flare
        int heavy = -1;
        for (int i = 0; i < ai.attackTable.Count; i++) if (ai.attackTable[i].category == MonsterAI.AttackCategory.Heavy) { heavy = i; break; }
        if (heavy >= 0)
        {
            mon.attackCooldown = 0f;
            ai.DebugForceAttack(heavy);
            mon.attackCooldown = 9999f;
            yield return Frames(8);
            yield return Shot(scene + "_fire_heavy_windup");
            yield return Sample(() => aura.LiveParticles, 1.0f, am);
            log.AppendLine($"   heavy '{ai.attackTable[heavy].name}' (flaring={aura.Flaring}): live avg {am[0]:0.0} / max {am[1]:0}");
            float w0 = Time.time; while (mon.isAttacking && Time.time - w0 < 6f) yield return null;
            yield return Frames(30);
        }
        // low HP
        health.currentHealth = health.maxHealth * (aura.lowHpFraction - 0.05f);
        yield return Frames(15);
        yield return Sample(() => aura.LiveParticles, 3f, am);
        log.AppendLine($"   low HP ({health.Normalized:P0}, lowHp={aura.LowHp}): live avg {am[0]:0.0} / max {am[1]:0}");
        // blood + fire together
        var pc = PlayerCombat.Instance; Transform W = pc.weaponTransform; Vector3 rest = W.localPosition;
        yield return StrikeAt(mon, W, 9f, false);
        W.localPosition = rest;
        yield return Shot(scene + "_fire_with_blood");
        yield return Frames(30);
        // death fade
        health.currentHealth = 1f;
        yield return StrikeAt(mon, W, 9f, true);
        W.localPosition = rest;
        float d0 = Time.time; int at05 = -1, at1 = -1, at2 = -1;
        while (Time.time - d0 < 2.6f)
        {
            float t = Time.time - d0;
            if (at05 < 0 && t >= 0.5f) at05 = aura.LiveParticles;
            if (at1 < 0 && t >= 1.2f) at1 = aura.LiveParticles;
            if (at2 < 0 && t >= 2.5f) at2 = aura.LiveParticles;
            yield return null;
        }
        var mpb = new MaterialPropertyBlock();
        Color em = Color.black;
        if (aura.bodyRenderer != null) { aura.bodyRenderer.GetPropertyBlock(mpb); em = mpb.GetColor("_EmissionColor"); }
        log.AppendLine($"   death (alive={mon.isAlive}): live at 0.5 s {at05}, 1.2 s {at1}, 2.5 s {at2}; level {aura.Level:0.00}; body emission now {em}");
    }

    // ───────────────────────── Part 5: breathing ─────────────────────────
    // Real per-level hit-speed histograms (GET /api/analytics, 2026-10-08): {bin start m/s, count}, 0.5 m/s bins.
    static readonly Dictionary<string, float[][]> realSpeeds = new Dictionary<string, float[][]>
    {
        { "Easy",   new[] { new[]{1f,6}, new[]{1.5f,7}, new[]{2f,7}, new[]{2.5f,2}, new[]{3f,4}, new[]{3.5f,5}, new[]{4f,4}, new[]{4.5f,9}, new[]{5f,3}, new[]{5.5f,2}, new[]{6f,34} } },
        { "Medium", new[] { new[]{1f,60}, new[]{1.5f,48}, new[]{2f,37}, new[]{2.5f,23}, new[]{3f,18}, new[]{3.5f,13}, new[]{4f,5}, new[]{4.5f,1}, new[]{5f,1} } },
        { "Hard",   new[] { new[]{1.5f,1}, new[]{2f,18}, new[]{2.5f,18}, new[]{3f,11}, new[]{3.5f,14}, new[]{4f,8}, new[]{4.5f,9}, new[]{5f,11}, new[]{5.5f,77} } },
    };
    static float RealSpeed(string level)
    {
        var bins = realSpeeds[level]; float tot = 0f; foreach (var b in bins) tot += b[1];
        float r = Random.value * tot;
        foreach (var b in bins) { r -= b[1]; if (r <= 0f) return b[0] + Random.value * 0.5f; }
        return 4f;
    }

    /// <summary>true = feed real-histogram speeds to PlayerExertionAudio.RegisterSwing at swingsPerSecond (gate applied like
    /// DetectSwing does); false = drive the weapon transforms (their smoothed hand speed under-reads the drive speed).</summary>
    public static bool breathInject = true;
    public static float swingsPerSecond = 1f;

    static IEnumerator Breath()
    {
        yield return StartFight();
        var mon = M; if (mon == null) { log.AppendLine("no monster"); yield break; }
        yield return FreezeMonster(mon);
        var ex = PlayerExertionAudio.Instance;
        string level = LevelManager.Instance != null ? LevelManager.Instance.sceneDifficulty.ToString() : "Easy";
        string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        var fastSrc = ex.fastSource; var medSrc = ex.breathingSource;
        log.AppendLine($"== BREATH {scene}: heavy src {(medSrc != null ? medSrc.name : "-")} clip {(medSrc != null && medSrc.clip != null ? medSrc.clip.name : "-")}; fast src {(fastSrc != null ? fastSrc.name : "-")} clip {(fastSrc != null && fastSrc.clip != null ? fastSrc.clip.name : "-")}, gain filter {(fastSrc != null && fastSrc.GetComponent<AudioGainFilter>() != null)}");
        log.AppendLine($"   thresholds: count >= {ex.swingTriggerSpeed} m/s, weight 1 at {ex.fullWeightSpeed} m/s, {ex.swingsForFullEffort} weighted swings / {ex.exertionWindow} s, high on {ex.highOn}");
        float r0 = Time.time; float maxRest = 0f;
        while (Time.time - r0 < 4f) { maxRest = Mathf.Max(maxRest, ex.CurrentVolume); yield return null; }
        log.AppendLine($"   at rest 4 s: exertion {ex.Exertion01:0.00}, tier {ex.CurrentTier}, max breathing volume {maxRest:0.000}");
        // sustained air swings, alternating hands, ~1 per second, speeds from the live histogram (beside the player, never on the monster)
        var pc = PlayerCombat.Instance;
        Transform[] W = { pc.weaponTransform, pc.offhandWeaponTransform != null ? pc.offhandWeaponTransform : pc.weaponTransform };
        Vector3[] rest = { W[0].localPosition, W[1].localPosition };
        float t0 = Time.time, tMed = -1f, tHigh = -1f, fastPeak = 0f; int swings = 0, counted0 = 0;
        Vector3 H = Head.position; Vector3 inward = mon.transform.position - H; inward.y = 0f; inward.Normalize(); Vector3 side = Vector3.Cross(Vector3.up, inward);
        Vector3[] A = { H - inward * 0.2f + side * 0.55f + Vector3.up * 0.25f, H - inward * 0.2f - side * 0.55f + Vector3.up * 0.25f };
        Vector3[] Bp = { H - inward * 0.3f - side * 0.25f - Vector3.up * 0.45f, H - inward * 0.3f + side * 0.25f - Vector3.up * 0.45f };
        Vector3[] off = new Vector3[2];
        for (int k = 0; k < 2; k++)
        {
            off[k] = MakeMove(W[k], A[k], Bp[k], 1f).offset;
            // ease both hands to their start points first (below every gate)
            while (Vector3.Distance(W[k].position, A[k] - off[k]) > 0.005f) { W[k].position = Vector3.MoveTowards(W[k].position, A[k] - off[k], 0.8f * Time.deltaTime); yield return null; }
        }
        t0 = Time.time;
        if (breathInject)
        {
            float nx = Time.time; int fired = 0;
            while (Time.time - t0 < 60f && tHigh < 0f)
            {
                if (Time.time >= nx) { nx += 1f / swingsPerSecond; float v = RealSpeed(level); if (v >= ex.swingTriggerSpeed) { ex.RegisterSwing(v); fired++; } swings++; }
                yield return null;
                if (tMed < 0f && ex.CurrentTier != PlayerExertionAudio.Tier.Calm) tMed = Time.time - t0;
                if (ex.CurrentTier == PlayerExertionAudio.Tier.High) tHigh = Time.time - t0;
            }
            log.AppendLine($"   (injected: {swings} attempts at {swingsPerSecond:0.0}/s, {fired} above the {ex.swingTriggerSpeed} m/s count gate)");
        }
        float nextSwing = Time.time; int h = 0; float st = -1f, dur = 0f;
        while (!breathInject && Time.time - t0 < 50f && tHigh < 0f)
        {
            if (st < 0f && Time.time >= nextSwing)
            {
                float v = RealSpeed(level);
                dur = Vector3.Distance(A[h], Bp[h]) / v; st = Time.time; nextSwing = Time.time + 1f; swings++;
            }
            for (int k = 0; k < 2; k++)
            {
                if (st >= 0f && k == h)
                {
                    float f = Mathf.Clamp01((Time.time - st) / dur);
                    W[k].position = Vector3.Lerp(A[k], Bp[k], f) - off[k];
                    if (f >= 1f) { st = -1f; h = 1 - h; }
                }
                else W[k].position = Vector3.MoveTowards(W[k].position, A[k] - off[k], 0.9f * Time.deltaTime); // slow return, never counted
            }
            yield return null;
            if (tMed < 0f && ex.CurrentTier != PlayerExertionAudio.Tier.Calm) tMed = Time.time - t0;
            if (ex.CurrentTier == PlayerExertionAudio.Tier.High) tHigh = Time.time - t0;
        }
        W[0].localPosition = rest[0]; W[1].localPosition = rest[1];
        float h0 = Time.time; while (Time.time - h0 < 2.5f) { fastPeak = Mathf.Max(fastPeak, ex.FastVolume); yield return null; }
        log.AppendLine($"   sustained swinging (1 per second, alternating hands): {swings} swings in {Time.time - t0 - 2.5f:0.0} s; medium tier at {tMed:0.0} s, FAST tier at {tHigh:0.0} s; exertion {ex.Exertion01:0.00}; fast source volume {fastPeak:0.000} (cap {ex.fastMaxVolume}), playing={(fastSrc != null && fastSrc.isPlaying)}, medium volume {ex.MediumVolume:0.000}");
        // victory: fast tier cut
        var health = mon.GetComponent<MonsterHealth>();
        health.currentHealth = 1f;
        yield return StrikeAt(mon, W[0], 9f, false);
        W[0].localPosition = rest[0];
        float v0 = Time.time, cut = -1f; float startVol = ex.CurrentVolume;
        while (Time.time - v0 < 2f) { if (cut < 0f && ex.CurrentVolume <= 0.001f) cut = Time.time - v0; yield return null; }
        log.AppendLine($"   victory (alive={mon.isAlive}): breathing {startVol:0.000} -> 0 in {cut:0.00} s");
    }

    // ───────────────────────── Part 6: hit shake, layers, idle jitter ─────────────────────────
    static IEnumerator ShakeProbe()
    {
        yield return StartFight();
        var mon = M; if (mon == null) { log.AppendLine("no monster"); yield break; }
        yield return FreezeMonster(mon);
        string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        var shake = mon.GetComponent<MonsterHitShake>();
        var fx = Object.FindAnyObjectByType<HitBloodFx>();
        var fb = Object.FindAnyObjectByType<CombatFeedback>();
        var pc = PlayerCombat.Instance; Transform W = pc.weaponTransform; Vector3 rest = W.localPosition;
        var hips = Hips(mon);
        log.AppendLine($"== SHAKE {scene}: shake component {(shake != null)}, bone {hips.name}");
        if (shake == null) yield break;
        ResetLog(); Listen(true);
        float[] speeds = { 2.2f, 3.4f, 4.6f, 9f };
        foreach (float sp in speeds)
        {
            int s0 = shake.Shakes, b0 = fx.Bursts, p0 = fb.Pulses, n0 = sounds.Count;
            float maxOff = 0f; float lastNonZero = 0f; float t0 = -1f;
            yield return StrikeAt(mon, W, sp, (speeds.Length & 1) == 0);
            t0 = Time.time;
            while (Time.time - t0 < 0.25f) { if (shake.CurrentOffset > 0.0001f) lastNonZero = Time.time - t0; yield return null; }
            maxOff = shake.PeakOffset;
            int hitSnd = 0; for (int i = n0; i < sounds.Count; i++) if (sounds[i].cause.StartsWith("hit:") || sounds[i].cause.StartsWith("block:")) hitSnd++;
            log.AppendLine($"   swing {sp:0.0} m/s ({CombatUIController.StyleForSpeed(sp)}): shakes +{shake.Shakes - s0}, peak {maxOff * 100f:0.0} cm (set {shake.LastAmplitude * 100f:0.0} cm), length {shake.LastDuration * 1000f:0} ms (last offset seen at {lastNonZero * 1000f:0} ms); blood bursts +{fx.Bursts - b0}, hit sounds +{hitSnd}, haptic pulses +{fb.Pulses - p0}");
            yield return ReturnHome(W, rest);
            float w0 = Time.time; while (Time.time - w0 < 0.8f) yield return null;
        }
        // root and colliders: the monster root never moves; the bone children do, by the same amount (they ride the skeleton)
        foreach (bool on in new[] { false, true })
        {
            shake.shakeEnabled = on;
            Vector3 rootBefore = mon.transform.position; Quaternion rotBefore = mon.transform.rotation;
            yield return StrikeAt(mon, W, 9f, true);
            float rootMove = 0f, rotMove = 0f;
            for (int i = 0; i < 10; i++) { rootMove = Mathf.Max(rootMove, Vector3.Distance(mon.transform.position, rootBefore)); rotMove = Mathf.Max(rotMove, Quaternion.Angle(mon.transform.rotation, rotBefore)); yield return null; }
            log.AppendLine($"   root during a full swing, shake {(on ? "ON " : "OFF")}: moved {rootMove * 100f:0.0} cm, turned {rotMove:0.0} deg");
            yield return ReturnHome(W, rest);
            float w1 = Time.time; while (Time.time - w1 < 0.8f) yield return null;
        }
        shake.shakeEnabled = true;
        // toggle layers one by one on a full swing
        string[] names = { "shake", "blood", "hit sound", "haptics", "damage number" };
        for (int layer = 0; layer < names.Length; layer++)
        {
            bool sOn = true; var hdAll = Object.FindObjectsByType<HitDetection>(FindObjectsInactive.Include);
            switch (layer)
            {
                case 0: shake.shakeEnabled = false; break;
                case 1: fx.mode = HitBloodFx.Mode.Off; break;
                case 2: foreach (var h in hdAll) h.hitSounds = false; break;
                case 3: fb.haptics = false; break;
                case 4: CombatUIController.Instance.builtInDamageNumbers = false; break;
            }
            int s0 = shake.Shakes, b0 = fx.Bursts, p0 = fb.Pulses, n0 = sounds.Count;
            int nums0 = CountNumbers();
            yield return StrikeAt(mon, W, 9f, false);
            for (int i = 0; i < 6; i++) yield return null;
            int hitSnd = 0; for (int i = n0; i < sounds.Count; i++) if (sounds[i].cause.StartsWith("hit:") || sounds[i].cause.StartsWith("block:")) hitSnd++;
            log.AppendLine($"   {names[layer]} OFF, full swing: shake +{shake.Shakes - s0}, blood +{fx.Bursts - b0}, hit sound +{hitSnd}, haptic pulse +{fb.Pulses - p0}, damage number +{CountNumbers() - nums0}");
            switch (layer)
            {
                case 0: shake.shakeEnabled = true; break;
                case 1: fx.mode = HitBloodFx.Mode.Blood; break;
                case 2: foreach (var h in hdAll) h.hitSounds = true; break;
                case 3: fb.haptics = true; break;
                case 4: CombatUIController.Instance.builtInDamageNumbers = true; break;
            }
            yield return ReturnHome(W, rest);
            float w0 = Time.time; while (Time.time - w0 < 1.8f) yield return null;
        }
        // all on
        { int s0 = shake.Shakes, b0 = fx.Bursts, p0 = fb.Pulses, n0 = sounds.Count, nums0 = CountNumbers();
          yield return StrikeAt(mon, W, 9f, true);
          for (int i = 0; i < 6; i++) yield return null;
          int hitSnd = 0; for (int i = n0; i < sounds.Count; i++) if (sounds[i].cause.StartsWith("hit:") || sounds[i].cause.StartsWith("block:")) hitSnd++;
          log.AppendLine($"   all layers ON, full swing: shake +{shake.Shakes - s0}, blood +{fx.Bursts - b0}, hit sound +{hitSnd}, haptic pulse +{fb.Pulses - p0}, damage number +{CountNumbers() - nums0}");
          yield return ReturnHome(W, rest); }
        // during an attack wind-up
        var ai = mon.GetComponent<MonsterAI>();
        int idx = -1; for (int i = 0; i < ai.attackTable.Count; i++) if (ai.attackTable[i].rangedKind == MonsterAI.RangedKind.None && !ai.attackTable[i].unblockable) { idx = i; break; }
        if (idx >= 0)
        {
            var pre = new Move[1];
            yield return StrikePrepare(mon, W, 9f, false, pre);
            mon.attackCooldown = 0f; ai.DebugForceAttack(idx); mon.attackCooldown = 9999f;
            yield return Frames(2);
            float s0 = shake.LastAmplitude; int c0 = shake.Shakes; bool atk = mon.isAttacking; bool windup = ai.CurrentAttack != null;
            yield return StrikeRun(W, pre[0]);
            yield return Frames(3);
            log.AppendLine($"   during a wind-up/attack '{(ai.CurrentAttack != null ? ai.CurrentAttack.name : "(finished)")}' (isAttacking={atk}, result {W.GetComponentInChildren<HitDetection>().LastResult}): shakes +{shake.Shakes - c0}, set amplitude {shake.LastAmplitude * 100f:0.0} cm (a full swing is {shake.fullAmplitude * 100f:0.0} cm, x{shake.attackScale} while attacking)");
            yield return ReturnHome(W, rest);
        }
        Listen(false);
    }

    // ───────────────────────── regression: coverage, parry, block ─────────────────────────
    static IEnumerator StrikeCollider(MonsterController mon, Transform W, Collider target, float speed, float yawOffset = 0f, float lift = 0.15f)
    {
        Vector3 H = Head.position; Vector3 c = target.bounds.center;
        Vector3 inward = c - H; inward.y = 0f; inward.Normalize();
        inward = Quaternion.AngleAxis(yawOffset, Vector3.up) * inward;
        Vector3 a = c - inward * 0.9f + Vector3.up * lift; Vector3 b = c - inward * 0.05f;
        var mv = MakeMove(W, a, b, speed);
        phase = "windback";
        while (Vector3.Distance(W.position, a - mv.offset) > 0.005f) { W.position = Vector3.MoveTowards(W.position, a - mv.offset, 1.2f * Time.deltaTime); yield return null; }
        yield return StrikeRun(W, mv);
    }

    static IEnumerator Regress()
    {
        yield return StartFight();
        var mon = M; if (mon == null) { log.AppendLine("no monster"); yield break; }
        yield return FreezeMonster(mon);
        string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        var pc = PlayerCombat.Instance; Transform W = pc.weaponTransform; Vector3 rest = W.localPosition;
        var hd = W.GetComponentInChildren<HitDetection>();
        var health = mon.GetComponent<MonsterHealth>();
        var ai = mon.GetComponent<MonsterAI>();
        var caps = new List<CapsuleCollider>();
        foreach (var cc in mon.GetComponentsInChildren<CapsuleCollider>()) if (!cc.isTrigger && !cc.CompareTag("MonsterWeapon") && cc.gameObject != mon.gameObject) caps.Add(cc); // the root capsule is not a hit volume
        int reached = 0, ok = 0, viaAlt = 0; var miss = new StringBuilder();
        int ownerOk = 0; foreach (var cc in caps) if (cc.GetComponentInParent<MonsterController>() == mon) ownerOk++;
        log.AppendLine($"== REGRESS {scene}: {caps.Count} body capsules, {ownerOk} resolve to the monster for HitDetection");
        foreach (var cc in caps)
        {
            if (!mon.isAlive) break;
            health.currentHealth = health.maxHealth; // keep it alive for the sweep
            var before = hd.LastResult;
            hd.GetType(); int h0 = hitsTotal;
            bool hit = false; int attempt = 0;
            float[,] tries = { { 0f, 0.15f }, { 55f, 0.15f }, { -55f, 0.15f }, { 0f, 0.7f }, { 90f, 0.3f }, { -90f, 0.3f } };
            for (; attempt < tries.GetLength(0) && !hit; attempt++)
            {
                ResetLog(); Listen(true);
                yield return StrikeCollider(mon, W, cc, 7f, tries[attempt, 0], tries[attempt, 1]);
                yield return Frames(2);
                Listen(false);
                hit = hits > 0;
                yield return ReturnHome(W, rest);
                float w0 = Time.time; while (Time.time - w0 < 0.45f) yield return null; // clear hit cooldown
            }
            if (hit) { ok++; if (attempt > 1) viaAlt++; } else miss.Append(cc.name).Append("(").Append(hd.LastResult).Append(") ");
        }
        log.AppendLine($"   body coverage: {ok}/{caps.Count} hit volumes produce a landed hit when struck at 7 m/s ({viaAlt} of them needed a second approach angle: the first line ran through the monster's own weapon or the other body){(miss.Length > 0 ? "; MISSED: " + miss : "")}");
        // parry: strike the monster's weapon during its wind-up
        var weaponCol = new List<Collider>(); foreach (var c in mon.GetComponentsInChildren<Collider>()) if (c.CompareTag("MonsterWeapon") && c.enabled) weaponCol.Add(c);
        int idx = -1; for (int i = 0; i < ai.attackTable.Count; i++) if (ai.attackTable[i].rangedKind == MonsterAI.RangedKind.None && !ai.attackTable[i].unblockable && ai.attackTable[i].windUp > 0.6f) { idx = i; break; }
        if (idx >= 0 && weaponCol.Count > 0 && mon.isAlive)
        {
            health.currentHealth = health.maxHealth;
            mon.attackCooldown = 0f; ai.DebugForceAttack(idx); mon.attackCooldown = 9999f;
            ResetLog(); Listen(true);
            // chase the weapon collider for up to the wind-up, striking at it fast
            float t0 = Time.time; bool done = false; int clashes0 = clashes;
            while (!done && Time.time - t0 < 1.5f && ai.CurrentAttack != null)
            {
                var wc = weaponCol[0]; Vector3 c = wc.bounds.center; Vector3 d = c - Head.position; d.Normalize();
                Vector3 tgt = c; var mv = MakeMove(W, tgt - d * 0.6f, tgt, 8f);
                W.position = tgt - d * 0.6f - mv.offset; yield return null;
                float ss = Time.time; while (Time.time - ss < 0.12f) { W.position = Vector3.Lerp(tgt - d * 0.6f, wc.bounds.center, (Time.time - ss) / 0.12f) - mv.offset; yield return null; }
                done = clashes > clashes0;
            }
            Listen(false);
            log.AppendLine($"   parry (player weapon into the monster's weapon during '{(ai.CurrentAttack != null ? ai.CurrentAttack.name : "?")}' wind-up): clashes {clashes}, clash sounds {sounds.FindAll(x => x.cause.StartsWith("clash")).Count}, CurrentAttackParried={ai.CurrentAttackParried}, landed body hits {hits}");
            yield return ReturnHome(W, rest);
            float w1 = Time.time; while (ai.CurrentAttack != null && Time.time - w1 < 4f) yield return null;
        }
        // body block: guard up, hit the body
        if (mon.isAlive)
        {
            health.currentHealth = health.maxHealth;
            yield return Frames(20);
            if (ai.BeginGuard(2.5f, false, null, Color.clear, 0f))
            {
                yield return Frames(4);
                ResetLog(); Listen(true);
                yield return StrikeAt(mon, W, 7f, false);
                yield return Frames(3);
                Listen(false);
                log.AppendLine($"   body block (guard up, body struck): result {hd.LastResult}, block sound {sounds.FindAll(x => x.cause.StartsWith("block") || x.cause.StartsWith("monster guard absorbed")).Count}, hit sound {sounds.FindAll(x => x.cause.StartsWith("hit:")).Count}, clash {clashes}, blood bursts (see Part 3: 0 on a guarded hit)");
                yield return ReturnHome(W, rest);
            }
            else log.AppendLine("   body block: guard could not be raised");
        }
        yield return Frames(1);
    }
    static int hitsTotal;

    static IEnumerator AllocProbe()
    {
        yield return StartFight();
        var mon = M; if (mon == null) yield break;
        yield return FreezeMonster(mon);
        string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        var aura = mon.GetComponent<MonsterFireAura>();
        using (var rec = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Memory, "GC Allocated In Frame"))
        {
            var res = new List<string>();
            for (int round = 0; round < 3; round++)
            foreach (bool on in new[] { true, false })
            {
                if (aura != null) aura.auraEnabled = on;
                yield return Frames(30);
                var vals = new List<long>();
                for (int i = 0; i < 300; i++) { yield return null; vals.Add(rec.LastValue); }
                vals.Sort();
                long med = vals[vals.Count / 2], p95 = vals[(int)(vals.Count * 0.95f)]; double trimmed = 0; int tn = (int)(vals.Count * 0.95f); for (int i = 0; i < tn; i++) trimmed += vals[i];
                res.Add($"{(aura != null ? "aura " + (on ? "ON " : "OFF") : "(no aura)")} round {round + 1}: median {med} B/frame, p95 {p95} B, mean of lower 95% {trimmed / tn:0} B, aura particles {(aura != null ? aura.LiveParticles : 0)}");
                if (aura == null) break;
            }
            foreach (var r in res) log.AppendLine($"== ALLOC {scene} {r}");
        }
        if (aura != null) aura.auraEnabled = true;
    }

    static int CountNumbers()
    {
        // pooled damage-number objects that are active right now
        int n = 0;
        foreach (var t in Object.FindObjectsByType<TMPro.TMP_Text>(FindObjectsInactive.Exclude)) if (t.gameObject.name.ToLower().Contains("number") || t.gameObject.name.ToLower().Contains("dmg")) n++;
        return n;
    }

    static IEnumerator Jitter()
    {
        log.AppendLine(CombatVerification.Prepare());
        rig = Object.FindAnyObjectByType<OVRCameraRig>();
        var bm = BattleManager.Instance;
        bm.spawnOnPlayerForwardLine = !centreLegacy;
        var pc = PlayerCombat.Instance;
        if (pc.weaponSets != null && pc.weaponSets.Count > 0) pc.EquipWeaponSet(CombatVerification.l3Weapon);
        yield return Frames(3);
        if (!bm.IsBattleActive) bm.StartBattleFromInspector();
        yield return Frames(2);
        var mon = M; if (mon == null) { log.AppendLine("no monster"); yield break; }
        var entr = mon.GetComponent<MonsterEntrance>();
        float tE = Time.time; while (entr != null && !entr.Complete && Time.time - tE < 20f) yield return null;
        var def = mon.GetComponent<MonsterDefense>();
        if (def != null) { def.dampedTurn = !centreLegacy; def.recentre = !centreLegacy; def.reactiveGuard = false; def.timedGuard = false; }
        mon.attackCooldown = 9999f;
        yield return FreezeMonster(mon);
        var ps = mon.GetComponent<MonsterPersonalSpace>();
        var hips = Hips(mon);
        string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        var ts = rig.trackingSpace; Vector3 tsPos = ts.localPosition; Quaternion tsRot = ts.localRotation;
        log.AppendLine($"== JITTER {scene} {(centreLegacy ? "LEGACY turn (constant speed, no dead zone)" : "CURRENT turn (damped, 4 deg dead zone)")}: monster idle, player head sways {swayLateral * 100:0} cm / {swayYawDeg:0} deg");
        foreach (bool sway in new[] { false, true })
        {
            float t0 = Time.time, lastYaw = mon.transform.eulerAngles.y; Vector3 lastRoot = mon.transform.position, lastHips = hips.position, pRoot = lastRoot, pHips = lastHips;
            double d2Root = 0, d2Hips = 0; int n = 0; float peakYaw = 0f, yawTravel = 0f; int pushFrames = 0, reversals = 0; float lastSign = 0f; float maxPush = 0f;
            var head = Head.position;
            while (Time.time - t0 < 6f)
            {
                float t = Time.time - t0;
                if (sway) { ts.localPosition = tsPos + Vector3.right * (swayLateral * Mathf.Sin(2f * Mathf.PI * 0.25f * t)); ts.localRotation = tsRot * Quaternion.Euler(0f, swayYawDeg * Mathf.Sin(2f * Mathf.PI * 0.2f * t), 0f); }
                yield return null;
                float dt = Mathf.Max(Time.deltaTime, 1e-4f);
                Vector3 r = mon.transform.position, h = hips.position;
                // second difference of position = acceleration jitter (a smooth drift has ~0, a shake does not)
                if (n > 2) { d2Root += (r - 2f * lastRoot + pRoot).sqrMagnitude; d2Hips += (h - 2f * lastHips + pHips).sqrMagnitude; }
                pRoot = lastRoot; pHips = lastHips; lastRoot = r; lastHips = h; n++;
                float yaw = mon.transform.eulerAngles.y; float dy = Mathf.DeltaAngle(lastYaw, yaw); lastYaw = yaw;
                peakYaw = Mathf.Max(peakYaw, Mathf.Abs(dy) / dt); yawTravel += Mathf.Abs(dy);
                if (Mathf.Abs(dy / dt) > 3f) { float sg = Mathf.Sign(dy); if (lastSign != 0f && sg != lastSign) reversals++; lastSign = sg; }
                if (ps != null && ps.LastPush > 0.0005f) { pushFrames++; maxPush = Mathf.Max(maxPush, ps.LastPush); }
            }
            ts.localPosition = tsPos; ts.localRotation = tsRot;
            log.AppendLine($"   {(sway ? "head swaying" : "head still  ")}: root accel-jitter rms {Mathf.Sqrt((float)(d2Root / Mathf.Max(1, n))) * 1000f:0.00} mm/frame^2, hips {Mathf.Sqrt((float)(d2Hips / Mathf.Max(1, n))) * 1000f:0.00} mm/frame^2; yaw travel {yawTravel / 6f:0.0} deg/s, peak {peakYaw:0.0} deg/s, direction reversals {reversals}; personal-space push frames {pushFrames} (max {maxPush * 100f:0.00} cm)");
        }
    }

    // ───────────────────────── jobs ─────────────────────────
    static IEnumerator SoundFight(float seconds)
    {
        yield return StartFight();
        var mon = M; if (mon == null) { log.AppendLine("no monster"); yield break; }
        ApplyLegacy(legacy);
        var pc = PlayerCombat.Instance;
        var fb = Object.FindAnyObjectByType<CombatFeedback>();
        var health = mon.GetComponent<MonsterHealth>();
        Transform R = pc.weaponTransform, L = pc.offhandWeaponTransform != null ? pc.offhandWeaponTransform : pc.weaponTransform;
        float floorY = FloorY();
        string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        log.AppendLine($"== SOUND AUDIT {scene} {(legacy ? "LEGACY (toggles off, old whoosh values)" : "CURRENT")} {seconds:0} s, floorY={floorY:0.00}, ignoredPairs={pc.IgnoredColliderPairs}");
        ResetLog();
        int pulses0 = fb != null ? fb.Pulses : 0;
        Listen(true);
        var actionCount = new Dictionary<string, int>();
        string[] plan = { "strike", "air", "strike", "cross-slow", "strike", "floor", "air-fast", "cross-fast", "strike", "idle" };
        int step = 0;
        float tStart = Time.time;
        while (Time.time - tStart < seconds && mon != null && mon.isAlive)
        {
            if (PlayerHealth.Instance.currentHealth < PlayerHealth.Instance.maxHealth * 0.5f) PlayerHealth.Instance.FullHeal();
            if (health.currentHealth < health.maxHealth * 0.15f) health.currentHealth = health.maxHealth * 0.6f;

            string act = plan[step++ % plan.Length];
            actionCount[act] = (actionCount.TryGetValue(act, out int c) ? c : 0) + 1;
            Vector3 H = Head.position;
            Vector3 target = mon.transform.position + Vector3.up * 1.3f;
            var def = mon.GetComponent<MonsterDefense>();
            if (def != null && def.guardTarget != null) target = def.guardTarget.position;
            Vector3 inward = target - H; inward.y = 0f; inward.Normalize();
            Vector3 side = Vector3.Cross(Vector3.up, inward);
            bool right = (step & 1) == 0;
            Transform W = right ? R : L;
            switch (act)
            {
                case "strike":
                {
                    Vector3 b = target - inward * 0.15f;
                    Vector3 a = b - inward * 0.75f + ((step & 2) == 0 ? Vector3.up * 0.55f : side * (right ? 0.6f : -0.6f));
                    yield return Play(new[] { MakeMove(W, a, b, Random.Range(1.5f, 4.5f)) }, act);
                    break;
                }
                case "air":
                case "air-fast":
                {
                    // a full swing beside / behind the player, away from the monster
                    Vector3 a = H - inward * 0.3f + side * (right ? 0.7f : -0.7f) + Vector3.up * 0.2f;
                    Vector3 b = H - inward * 0.6f - side * (right ? 0.2f : -0.2f) - Vector3.up * 0.5f;
                    yield return Play(new[] { MakeMove(W, a, b, act == "air" ? 2.0f : 4.5f) }, act);
                    break;
                }
                case "cross-slow":
                case "cross-fast":
                {
                    // both weapon colliders drawn through each other in front of the chest, across the monster line
                    Vector3 c0 = H - inward * 0.25f - Vector3.up * 0.35f;
                    float sp = act == "cross-slow" ? 1.0f : 3.5f;
                    yield return Play(new[] { MakeMove(R, c0 + side * 0.45f, c0 - side * 0.45f, sp), MakeMove(L, c0 - side * 0.45f, c0 + side * 0.45f, sp) }, act);
                    break;
                }
                case "floor":
                {
                    Vector3 p = H + side * (right ? 0.5f : -0.5f) - inward * 0.2f; p.y = floorY + 0.35f;
                    Vector3 q = p - side * (right ? 0.5f : -0.5f); q.y = floorY - 0.05f;
                    yield return Play(new[] { MakeMove(W, p, q, 3.0f) }, act);
                    Vector3 q2 = q - inward * 0.3f;
                    yield return Play(new[] { MakeMove(W, q, q2, 1.0f) }, "floor-drag");
                    break;
                }
                default:
                {
                    phase = "idle"; float i0 = Time.time; while (Time.time - i0 < 1.0f) yield return null; break;
                }
            }
            float g0 = Time.time; while (Time.time - g0 < 0.25f) yield return null;
        }
        Listen(false);
        int pulses = fb != null ? fb.Pulses - pulses0 : -1;
        Report(actionCount, pulses, Time.time - tStart);
        if (legacy) ApplyLegacy(false);
    }

    static void Report(Dictionary<string, int> actions, int pulses, float secs)
    {
        var sb = new StringBuilder();
        foreach (var kv in actions) sb.Append(kv.Key).Append('=').Append(kv.Value).Append(' ');
        log.AppendLine($"ran {secs:0.0} s; actions: {sb}");
        log.AppendLine($"contact events without a real collider overlap: {unverified}");
        log.AppendLine($"events: hitsLanded={hits} clashes={clashes} tooSlow={slow} playerDamage={damageEvents} hapticPulses={pulses} (expected = hits + clashes + 2 x damage = {hits + clashes + 2 * damageEvents})");
        var byCause = new SortedDictionary<string, int>();
        var byPhase = new SortedDictionary<string, int>();
        int invalid = 0;
        var bad = new StringBuilder();
        foreach (var s in sounds)
        {
            string key = s.cause + " [" + s.clip + "]";
            byCause[key] = (byCause.TryGetValue(key, out int c) ? c : 0) + 1;
            string pk = s.phase + " / " + s.cause;
            byPhase[pk] = (byPhase.TryGetValue(pk, out int d) ? d : 0) + 1;
            string why = Validate(s);
            if (why != null) { invalid++; if (invalid <= 25) bad.AppendLine($"   INVALID t={s.t:0.00} {s.src} {s.clip} '{s.cause}' phase={s.phase}: {why}"); }
        }
        log.AppendLine($"sounds total={sounds.Count}, without a valid cause={invalid}");
        foreach (var kv in byCause) log.AppendLine($"   {kv.Value,4}  {kv.Key}");
        log.AppendLine(" by player action / cause:");
        foreach (var kv in byPhase) log.AppendLine($"   {kv.Value,4}  {kv.Key}");
        if (invalid > 0) log.Append(bad);
    }

    static string Validate(Snd s)
    {
        bool playerContact = s.cause.StartsWith("hit:") || s.cause.StartsWith("block:") || s.cause.StartsWith("clash:");
        if (playerContact)
        {
            var d = s.cause.StartsWith("clash:") ? clashByFrame : hitsByFrame;
            if (!d.TryGetValue(s.frame, out var l)) return "no matching HitLanded / WeaponClashed event this frame";
            bool found = false; foreach (var o in l) if (o != null && o.name == s.src) found = true;
            if (!found) return "event came from another weapon";
            return null;
        }
        if (s.cause.StartsWith("player took damage")) return damageFrames.Contains(s.frame) ? null : "no PlayerHealth.DamageTaken this frame";
        if (s.cause == "monster guard absorbed a landed hit") return hitsByFrame.ContainsKey(s.frame) ? null : "no landed hit this frame";
        if (s.cause.StartsWith("whoosh"))
        {
            bool fastStroke = s.phase == "strike" || s.phase == "air" || s.phase == "air-fast" || s.phase == "cross-fast" || s.phase == "floor";
            // hand speed is smoothed (0.12 s), so a whoosh may land just after a fast stroke ends
            bool justAfter = s.t - lastFastStrokeEnd < 0.3f;
            return fastStroke || justAfter ? null : "whoosh without a fast stroke (" + s.phase + ")";
        }
        return null; // monster-driven cues (attack telegraph / swing / impact, timed guard, phase, entrance) are the monster's own actions
    }

    static IEnumerator CrossOnly()
    {
        yield return StartFight();
        ApplyLegacy(legacy);
        // Monster frozen in idle (no attacks, no guard, AI paused) so only the two player weapons can touch.
        var fm = M;
        if (fm != null)
        {
            fm.attackCooldown = 9999f;
            float w0 = Time.time; while (fm.isAttacking && Time.time - w0 < 6f) yield return null;
            var fai = fm.GetComponent<MonsterAI>(); if (fai != null) fai.enabled = false;
            var fdef = fm.GetComponent<MonsterDefense>(); if (fdef != null) fdef.enabled = false;
            yield return Frames(10);
        }
        var pc = PlayerCombat.Instance;
        var fb = Object.FindAnyObjectByType<CombatFeedback>();
        var cols = pc.GetComponentsInChildren<Collider>(false);
        var sb = new StringBuilder();
        int pairs = 0, ignored = 0;
        for (int i = 0; i < cols.Length; i++) for (int j = i + 1; j < cols.Length; j++) { pairs++; if (Physics.GetIgnoreCollision(cols[i], cols[j])) ignored++; }
        log.AppendLine($"== CROSS {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}: active player colliders={cols.Length}, pairs={pairs}, ignored={ignored} (PlayerCombat.IgnoredColliderPairs={pc.IgnoredColliderPairs})");
        foreach (var c in cols) sb.Append(c.name).Append(c.isTrigger ? "(trigger) " : "(solid) ");
        log.AppendLine("   colliders: " + sb);
        ResetLog(); Listen(true);
        int p0 = fb != null ? fb.Pulses : 0;
        var mon = M; Vector3 H = Head.position;
        Vector3 inward = (mon != null ? mon.transform.position : H + rig.transform.forward) - H; inward.y = 0f; inward.Normalize();
        Vector3 side = Vector3.Cross(Vector3.up, inward);
        Vector3 c0 = H - inward * 0.25f - Vector3.up * 0.35f;
        Transform R = pc.weaponTransform, L = pc.offhandWeaponTransform;
        // 1) held overlapped (both collider centres on the same point) for 2 s, 2) dragged through each other 10x at 1-5 m/s
        var mr = MakeMove(R, c0, c0, 1f); var ml = MakeMove(L, c0, c0, 1f);
        phase = "overlap-hold";
        float t0 = Time.time;
        int overlapFrames = 0;
        dragLog.Length = 0;
        var rc = R.GetComponentInChildren<HitDetection>().GetComponent<Collider>();
        var lc = L.GetComponentInChildren<HitDetection>().GetComponent<Collider>();
        while (Time.time - t0 < 2f)
        {
            R.position = c0 + Random.insideUnitSphere * 0.03f - mr.offset; L.position = c0 + Random.insideUnitSphere * 0.03f - ml.offset;
            if (rc.bounds.Intersects(lc.bounds)) overlapFrames++;
            yield return null;
        }
        R.localPosition = mr.restLocal; L.localPosition = ml.restLocal;
        for (int k = 0; k < 10; k++)
        {
            float sp = 1f + 0.45f * k;
            maxR = maxL = 0f; int n0 = sounds.Count;
            yield return Play(new[] { MakeMove(R, c0 + side * 0.45f, c0 - side * 0.45f, sp), MakeMove(L, c0 - side * 0.45f, c0 + side * 0.45f, sp) }, "cross");
            dragLog.AppendLine($"   drag {k} at {sp:0.00} m/s: peak hand speed R {maxR:0.00} L {maxL:0.00}, sounds {sounds.Count - n0}");
        }
        Listen(false);
        log.Append(dragLog);
        int contactSounds = 0, whooshes = 0;
        foreach (var s in sounds) { if (s.cause.StartsWith("whoosh")) whooshes++; else if (s.cause.StartsWith("hit:") || s.cause.StartsWith("clash:") || s.cause.StartsWith("block:")) contactSounds++; }
        foreach (var s in sounds) log.AppendLine($"   sound t={s.t:0.00} {s.src} '{s.cause}' [{s.clip}]");
        log.AppendLine($"   overlap frames={overlapFrames}; contact sounds={contactSounds}, whooshes={whooshes} (from the fast drags), damage events={hits}, clashes={clashes}, too-slow={slow}, haptic pulses={(fb != null ? fb.Pulses - p0 : -1)}");
    }
}
