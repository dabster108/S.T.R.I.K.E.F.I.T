// DefenseHarness.cs
// Purpose: Play-mode harness for the Level 2 / 3 defence (MonsterTactics). A mock weapon (the player's real right weapon, moved by
//          script through the real HitDetection path at a fixed 72 Hz step) is driven along scripted straight swings, and the
//          results are tabled:
//            "matrix"     slow / medium / fast swings (0.8, 1.2, 2, 4, 8 m/s) from the left, the right, overhead and as a thrust, 8 each,
//                         with the cooldown, rate cap and meter drain switched off (to test prediction and pose, not fairness):
//                         detection rate, predicted-zone accuracy, guards raised, guard weapon on the correct side / in the right shape.
//            "fair"       the real profile: 12 swings from the same side (guard breaks), then mixed directions in each phase (block
//                         rate per phase vs the cap, counters, parries / blocks).
//            "committed"  swings while the monster is in a telegraph, a strike and a recovery: nothing may be guarded or blocked.
//            "punish"     a whiff (a swing that passes in front and hits nothing) and a retreat (the player steps back): punishes.
//            "poses"      each guard pose held, screenshot from the player's head, weapon middle and axis in monster space.
//            "fight60"    60 s hands-off fight: MonsterBehaviourTrace state log + the tactics decision log.
//            "freq:on" / "freq:off"   60 s of mixed swings every 1.2 s with MonsterTactics on / off: attacks per minute.
//          Driven from Unity MCP: DefenseHarness.Run("matrix") then poll DefenseHarness.Status() until DONE; "matrix;fair" runs both.
//          The job is stepped inside the player loop (PreUpdate, once per frame, before MonsterTactics reads the controller), not
//          from EditorApplication.update: the editor tick is not one-to-one with player frames, and a weapon that stalls a frame and
//          then jumps reads as a faster swing (a nominal 0.8 m/s swing measured 1.3 m/s that way).
// Dependencies: MonsterTactics, MonsterDefense, MonsterGuardIK, MonsterAI, MonsterController, PlayerCombat, HitDetection, BattleManager,
//               CombatVerification (Prepare), MonsterBehaviourTrace, StrikeFit.Defense
// Attach to: nothing (static, Editor only)

using System.Collections;
using System.Collections.Generic;
using System.Text;
using StrikeFit.Defense;
using UnityEditor;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;

public static class DefenseHarness
{
    static IEnumerator job;
    static readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
    static readonly StringBuilder log = new StringBuilder();
    static int lastFrame = -1;
    static string jobName = "";
    public static string captureDir = "Captures/Defense";
    public static int reps = 8;

    public static bool Running => job != null;
    public static string Status() => (job != null ? "RUNNING " + jobName + " t=" + Time.time.ToString("0.0") + "\n" : "IDLE\n") + log;

    public static string Run(string name)
    {
        if (!EditorApplication.isPlaying) return "not playing";
        Stop();
        log.Length = 0; jobName = name;
        job = Main(name);
        Install(true);
        EditorApplication.playModeStateChanged -= OnPlayMode; EditorApplication.playModeStateChanged += OnPlayMode;
        return "started " + name;
    }

    public static void Stop()
    {
        job = null; stack.Clear();
        Install(false);
        RestoreProfile();
    }

    static void OnPlayMode(PlayModeStateChange c) { if (c == PlayModeStateChange.ExitingPlayMode) { Stop(); Time.captureDeltaTime = 0f; } }

    struct HarnessStep { }

    /// <summary>Adds (or removes) the harness step at the end of PreUpdate.</summary>
    static void Install(bool on)
    {
        var loop = PlayerLoop.GetCurrentPlayerLoop();
        if (loop.subSystemList == null) return;
        for (int i = 0; i < loop.subSystemList.Length; i++)
        {
            if (loop.subSystemList[i].type != typeof(PreUpdate)) continue;
            var list = new List<PlayerLoopSystem>(loop.subSystemList[i].subSystemList ?? new PlayerLoopSystem[0]);
            list.RemoveAll(x => x.type == typeof(HarnessStep));
            if (on) list.Add(new PlayerLoopSystem { type = typeof(HarnessStep), updateDelegate = Tick });
            loop.subSystemList[i].subSystemList = list.ToArray();
        }
        PlayerLoop.SetPlayerLoop(loop);
    }

    static void Tick()
    {
        if (job == null || !EditorApplication.isPlaying) { Stop(); return; }
        if (Time.frameCount == lastFrame) return;
        lastFrame = Time.frameCount;
        try
        {
            KeepAlive();
            while (true)
            {
                var top = stack.Count > 0 ? stack.Peek() : job;
                if (top.MoveNext()) { if (top.Current is IEnumerator nested) { stack.Push(nested); continue; } break; }
                if (stack.Count > 0) { stack.Pop(); continue; }
                log.AppendLine("DONE"); job = null; Install(false); RestoreProfile();
                Time.captureDeltaTime = 0f;
                return;
            }
        }
        catch (System.Exception e) { log.AppendLine("EXCEPTION " + e); Stop(); Time.captureDeltaTime = 0f; }
    }

    /// <summary>The full log to a file (Captures/Defense/&lt;name&gt;.txt).</summary>
    public static string Save(string name)
    {
        System.IO.Directory.CreateDirectory(captureDir);
        System.IO.File.WriteAllText(captureDir + "/" + name + ".txt", log.ToString());
        return captureDir + "/" + name + ".txt";
    }

    static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }
    static IEnumerator Seconds(float s) { float end = Time.time + s; while (Time.time < end) yield return null; }

    // ───────────────────────── setup ─────────────────────────
    static MonsterController mon;
    static MonsterAI ai;
    static MonsterDefense def;
    static MonsterTactics tac;
    static MonsterGuardIK ik;
    static MonsterHealth health;
    static BossPhaseController phases;
    static Transform head;
    static DefenseProfile realProfile, testProfile;
    /// <summary>Monster HP share the harness holds (the swings land real hits): full by default, a phase's band in "fair". 0 = left alone.</summary>
    static float holdHp = 1f;

    static void KeepAlive()
    {
        if (PlayerHealth.Instance != null && PlayerHealth.Instance.currentHealth < PlayerHealth.Instance.maxHealth * 0.6f) PlayerHealth.Instance.FullHeal();
        if (holdHp > 0f && health != null && mon != null && mon.isAlive) health.currentHealth = health.maxHealth * holdHp;
    }

    static IEnumerator Prepare()
    {
        CombatVerification.frameDt = 1f / 72f;
        CombatVerification.Prepare();   // runInBackground, captureDeltaTime 1/72, backend -> 127.0.0.1:9, name gate off
        // Hide the name-picker panel (its own generated object, not BattleSystems) so screenshots show the fight.
        var pnp = Object.FindAnyObjectByType<PlayerNamePicker>();
        var pf = typeof(PlayerNamePicker).GetField("panel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (pnp != null && pf != null && pf.GetValue(pnp) is GameObject panel && panel != null) panel.SetActive(false);
        // Level 3 waits for a weapon pair to be grabbed (WeaponSelectionStage): equip one, as Level3AttackVerification does.
        var pcw = PlayerCombat.Instance;
        if (pcw != null && pcw.weaponSets != null && pcw.weaponSets.Count > 0) pcw.EquipWeaponSet(CombatVerification.l3Weapon);
        yield return Frames(3);
        if (BattleManager.Instance.ActiveMonster == null || !BattleManager.Instance.ActiveMonster.isAlive) BattleManager.Instance.StartBattleFromInspector();
        float w0 = Time.time;
        while (Time.time - w0 < 30f)
        {
            var m = BattleManager.Instance.ActiveMonster;
            if (m != null && m.isAlive) { var e = m.GetComponent<MonsterEntrance>(); if (e == null || e.Complete) break; }
            yield return null;
        }
        mon = BattleManager.Instance.ActiveMonster;
        ai = mon.GetComponent<MonsterAI>(); def = mon.GetComponent<MonsterDefense>(); tac = mon.GetComponent<MonsterTactics>();
        ik = mon.GetComponentInChildren<MonsterGuardIK>(); health = mon.GetComponent<MonsterHealth>(); phases = mon.GetComponent<BossPhaseController>();
        var rig = Object.FindAnyObjectByType<OVRCameraRig>(); head = rig != null ? rig.centerEyeAnchor : Camera.main.transform;
        if (testProfile == null) realProfile = tac != null ? tac.profile : null;   // this level's own profile, every run
        yield return Frames(5);
        log.AppendLine("== " + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name + " monster " + mon.name + ", tactics " + (tac != null && tac.Active ? tac.profile.name : "NONE") +
                       ", HP " + health.maxHealth + ", attack cooldown " + mon.attackCooldown.ToString("0.00") + " s, damage " + mon.attackDamage + ", height " + def.Height.ToString("0.00") + " m");
    }

    static void QuietMonster(bool quiet)
    {
        var f = typeof(MonsterAI).GetField("baseAttackCooldown", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var nt = typeof(MonsterAI).GetField("nextThinkTime", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (quiet) { savedCd = mon.attackCooldown; savedBase = (float)f.GetValue(ai); mon.attackCooldown = 9999f; f.SetValue(ai, 9999f); }
        else { mon.attackCooldown = savedCd; f.SetValue(ai, savedBase); nt.SetValue(ai, 0f); }
    }
    static float savedCd, savedBase;

    /// <summary>A runtime copy of the profile with the fairness limits off (prediction / pose tests only).</summary>
    static void PermissiveProfile(bool on)
    {
        if (tac == null) return;
        if (on)
        {
            testProfile = Object.Instantiate(realProfile);
            testProfile.blockCooldown = 0f; testProfile.blockRateCap = 1f; testProfile.blockRateByPhase = Vector3.one;
            testProfile.drainPerBlock = 0f; testProfile.counterChance = 0f; testProfile.strafe = false; testProfile.punishWindow = 0f;
            tac.profile = testProfile;
        }
        else RestoreProfile();
    }

    static void RestoreProfile()
    {
        if (tac != null && realProfile != null) tac.profile = realProfile;
        if (testProfile != null) { Object.Destroy(testProfile); testProfile = null; }
    }

    // ───────────────────────── the mock weapon ─────────────────────────
    public enum Dir { Left, Right, Overhead, Thrust, Low }
    static readonly string[] DirNames = { "LEFT", "RIGHT", "OVERHEAD", "THRUST", "LOW" };
    static SwingZone Expected(Dir d) => d == Dir.Left ? SwingZone.Left : d == Dir.Right ? SwingZone.Right : d == Dir.Overhead ? SwingZone.High : d == Dir.Thrust ? SwingZone.Thrust : SwingZone.Low;

    /// <summary>Start and end of the striking part in monster space (x + = the player's left, y share of height, z m toward the player).</summary>
    static void Path(Dir d, float lateral, out Vector3 a, out Vector3 b)
    {
        switch (d)
        {
            // Long enough that a swing up to 4 m/s is already at speed 0.35 s before it reaches the body (the prediction horizon).
            case Dir.Left: a = new Vector3(2.0f, 0.62f, 0.5f); b = new Vector3(-0.45f, 0.62f, 0.02f); break;
            case Dir.Right: a = new Vector3(-2.0f, 0.62f, 0.5f); b = new Vector3(0.45f, 0.62f, 0.02f); break;
            case Dir.Overhead: a = new Vector3(0.04f, 1.45f, 1.35f); b = new Vector3(0f, 0.55f, 0.0f); break;
            case Dir.Thrust: a = new Vector3(0.02f, 0.6f, 2.1f); b = new Vector3(0f, 0.6f, -0.1f); break;
            default: a = new Vector3(2.0f, 0.22f, 0.45f); b = new Vector3(-0.45f, 0.22f, 0.02f); break;
        }
        a.x += lateral; b.x += lateral;
    }

    static Vector3 World(Vector3 local)
    {
        Vector3 fwd = mon.transform.forward; fwd.y = 0f; fwd.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, fwd);
        return mon.transform.position + right * local.x + Vector3.up * (local.y * def.Height) + fwd * local.z;
    }

    public struct SwingResult
    {
        public bool detected, raised, blocked, landed, hitBeforeGuard, sideOk, shapeOk;
        public SwingZone zone;
        public MonsterDefense.GuardPose pose;
        public string refusal;
        public Vector3 weaponMid, weaponAxis;
        public bool committedAtSwing;
    }

    /// <summary>One scripted swing of the right weapon at constant speed. `lateral` shifts the whole path (a whiff passes in front).</summary>
    static IEnumerator Swing(Dir d, float speed, SwingResult[] outResult, float lateral = 0f, float forwardShift = 0f, float heightShare = -1f)
    {
        var pc = PlayerCombat.Instance;
        Transform root = pc.weaponTransform;
        var hd = root.GetComponentInChildren<HitDetection>(); var col = hd.GetComponent<Collider>();
        Vector3 restLocal = root.localPosition; Quaternion restRot = root.localRotation;
        Path(d, lateral, out Vector3 la, out Vector3 lb);
        la.z += forwardShift; lb.z += forwardShift;
        if (heightShare > 0f) { la.y = heightShare; lb.y = heightShare; }
        Vector3 a = World(la), b = World(lb);
        Vector3 offset = col.bounds.center - root.position;
        // Park at the start, still: the predictor must see a resting weapon, not a teleport. The collider is off while the weapon
        // jumps, or the hit sweep reads the jump as a very fast hit (the monster flinched and could not guard the next swing).
        Teleport(root, col, hd, a - offset);
        yield return Frames(4);
        int det0 = tac != null ? tac.Detected : 0, raised0 = Raised(), blk0 = def.BlockedByZone[0] + def.BlockedByZone[1] + def.BlockedByZone[2];
        int[] refs0 = tac != null ? (int[])tac.RefusalCounts.Clone() : null;
        int hits0 = hitCount, flin0 = def.Flinches;
        var r = new SwingResult { refusal = "", committedAtSwing = tac != null && tac.Committed };
        float total = Vector3.Distance(a, b), dur = total / speed, t0 = Time.time;
        bool guardSeenBeforeHit = false, sampled = false; float guardUpAt = -1f;
        while (true)
        {
            float k = (Time.time - t0) / dur;
            if (k >= 1f) break;
            root.position = Vector3.Lerp(a, b, k) - offset;
            yield return null;
            Watch(ref r, ref guardSeenBeforeHit, ref sampled, ref guardUpAt, d, hits0);
        }
        root.position = b - offset;
        float tEnd = Time.time;
        while (Time.time - tEnd < 0.45f) { yield return null; Watch(ref r, ref guardSeenBeforeHit, ref sampled, ref guardUpAt, d, hits0); }
        // back to rest without a second swing
        Teleport(root, col, hd, root.parent.TransformPoint(restLocal));
        root.localRotation = restRot;
        if (tac != null)
        {
            r.detected = tac.Detected > det0;
            r.zone = r.detected ? tac.LastPredictedZone : SwingZone.None;
            for (int i = 0; i < refs0.Length; i++) if (tac.RefusalCounts[i] > refs0[i]) { r.refusal = DefensePolicyNames.Refusal[i]; break; }
        }
        r.raised = Raised() > raised0;
        r.blocked = def.BlockedByZone[0] + def.BlockedByZone[1] + def.BlockedByZone[2] > blk0;
        r.landed = hitCount > hits0;
        r.hitBeforeGuard = r.landed && !guardSeenBeforeHit;
        outResult[0] = r;
        yield return Frames(2);
    }

    static void Teleport(Transform root, Collider col, HitDetection hd, Vector3 to)
    {
        bool was = col.enabled; col.enabled = false;
        root.position = to; Physics.SyncTransforms();
        hd.ResetSweepHistory();
        if (tac != null) tac.ResetHand(0);
        col.enabled = was;
    }

    static void Watch(ref SwingResult r, ref bool guardSeenBeforeHit, ref bool sampled, ref float guardUpAt, Dir d, int hits0)
    {
        if (ai.IsDefending)
        {
            if (hitCount == hits0) guardSeenBeforeHit = true;
            if (guardUpAt < 0f) { guardUpAt = Time.time; r.pose = def.ActivePose; }
            // sample the pose once the IK has blended in (0.2 s after the raise)
            if (!sampled && ik != null && Time.time - guardUpAt >= 0.2f)
            {
                sampled = true;
                int h = 0;
                if (ik.HasWeapon(1))
                {
                    // two weapons: the one that moved toward the swing's side
                    float x0 = ik.WeaponMiddleLocal(0).x, x1 = ik.WeaponMiddleLocal(1).x;
                    h = d == Dir.Left ? (x0 >= x1 ? 0 : 1) : d == Dir.Right ? (x0 <= x1 ? 0 : 1) : 0;
                }
                r.weaponMid = ik.WeaponMiddleLocal(h); r.weaponAxis = ik.WeaponAxisLocal(h);
                float H = def.Height;
                switch (r.pose)
                {
                    case MonsterDefense.GuardPose.SideLeft: r.sideOk = r.weaponMid.x > 0.12f; r.shapeOk = Mathf.Abs(r.weaponAxis.y) > 0.6f; break;
                    case MonsterDefense.GuardPose.SideRight: r.sideOk = r.weaponMid.x < -0.12f; r.shapeOk = Mathf.Abs(r.weaponAxis.y) > 0.6f; break;
                    case MonsterDefense.GuardPose.High: r.sideOk = r.weaponMid.y > 0.82f * H; r.shapeOk = Mathf.Abs(r.weaponAxis.x) > 0.7f; break;
                    default: r.sideOk = r.weaponMid.y < 0.62f * H; r.shapeOk = r.weaponAxis.y < -0.25f; break;
                }
            }
        }
    }

    static int hitCount, clashes;
    static void OnHit(HitDetection s, float speed, bool defended) { hitCount++; }
    static void OnClash(HitDetection s, float speed) { clashes++; }

    static int Raised() { if (tac == null) return 0; int n = 0; for (int i = 0; i < MonsterTactics.Phases; i++) n += tac.RaisedByPhase[i]; return n; }

    static IEnumerator WaitFree(float min)
    {
        float t0 = Time.time;
        while (Time.time - t0 < min || ((ai.IsDefending || ai.IsBusy || mon.isAttacking || def.IsStaggered || def.InFlinch || (phases != null && phases.InPhaseShift)) && Time.time - t0 < 12f)) yield return null;
    }

    // ───────────────────────── jobs ─────────────────────────
    static IEnumerator Main(string name)
    {
        HitDetection.HitLanded -= OnHit; HitDetection.HitLanded += OnHit;
        HitDetection.WeaponClashed -= OnClash; HitDetection.WeaponClashed += OnClash;
        yield return Prepare();
        if (tac == null && name != "fight60") log.AppendLine("   (no MonsterTactics on this monster: Level 1 legacy path)");
        foreach (var part in name.Split(';'))
        {
            string n = part.Trim();
            if (n == "matrix") yield return Matrix(null);
            else if (n.StartsWith("matrix:")) yield return Matrix(n.Substring(7));
            else if (n == "fair") yield return Fair();
            else if (n == "committed") yield return CommittedJob();
            else if (n == "punish") yield return PunishJob();
            else if (n == "poses") yield return Poses();
            else if (n == "fight60") yield return Fight60();
            else if (n.StartsWith("freq:")) yield return Freq(n.EndsWith("on"));
            else if (n == "legacy") yield return Legacy();
            else log.AppendLine("unknown job " + n);
        }
        HitDetection.HitLanded -= OnHit;
    }

    /// <param name="only">Comma-separated directions (e.g. "Overhead"), null = all four.</param>
    static IEnumerator Matrix(string only)
    {
        QuietMonster(true); PermissiveProfile(true); tac.ResetStats(); def.ResetStats();
        yield return WaitFree(1f);
        log.AppendLine("MATRIX (" + reps + " swings per cell; fairness limits off; reaction delay " + realProfile.reactionDelay.x.ToString("0.00") + "-" + realProfile.reactionDelay.y.ToString("0.00") +
                       " s, speed floor " + realProfile.maxBlockableSpeed.ToString("0.0") + " m/s, detection " + realProfile.predictor.detectSpeed.ToString("0.0") + " m/s)");
        log.AppendLine("  speed  dir       detected  zone-ok  guards  pose     weapon-side-ok  shape-ok  blocked  parries  landed-first  refusals");
        var res = new SwingResult[1];
        foreach (float speed in new[] { 0.8f, 1.2f, 2f, 4f, 8f })
            foreach (Dir d in new[] { Dir.Left, Dir.Right, Dir.Overhead, Dir.Thrust })
            {
                if (only != null && only.IndexOf(d.ToString(), System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                int det = 0, zok = 0, g = 0, side = 0, shape = 0, blk = 0, first = 0, par0 = tac.ParriedByPhase[0] + tac.ParriedByPhase[1] + tac.ParriedByPhase[2]; Vector3 midSum = Vector3.zero; var poses = new Dictionary<string, int>(); var refs = new Dictionary<string, int>();
                for (int i = 0; i < reps; i++)
                {
                    yield return WaitFree(0.35f);
                    yield return Swing(d, speed, res);
                    var r = res[0];
                    if (r.detected) det++;
                    if (r.detected && (r.zone == Expected(d) || (d == Dir.Thrust && r.zone == SwingZone.Low))) zok++;
                    if (r.raised) { g++; midSum += r.weaponMid; if (r.sideOk) side++; if (r.shapeOk) shape++; string p = MonsterDefense.PoseNames[(int)r.pose]; poses[p] = (poses.TryGetValue(p, out int c) ? c : 0) + 1; }
                    if (r.blocked) blk++;
                    if (r.hitBeforeGuard) first++;
                    if (!string.IsNullOrEmpty(r.refusal)) refs[r.refusal] = (refs.TryGetValue(r.refusal, out int c2) ? c2 : 0) + 1;
                }
                log.AppendLine("  " + speed.ToString("0.0").PadLeft(4) + "   " + DirNames[(int)d].PadRight(9) + (det + "/" + reps).PadLeft(6) + (zok + "/" + det).PadLeft(9) + (g + "/" + reps).PadLeft(8) + "  " + Join(poses).PadRight(9) +
                               (g > 0 ? side + "/" + g : "-").PadLeft(10) + (g > 0 ? shape + "/" + g : "-").PadLeft(10) + (blk + "/" + reps).PadLeft(9) + (tac.ParriedByPhase[0] + tac.ParriedByPhase[1] + tac.ParriedByPhase[2] - par0).ToString().PadLeft(8) + (first + "/" + reps).PadLeft(12) +
                               (g > 0 ? "  weapon mid x " + (midSum.x / g).ToString("+0.00;-0.00") + " m, y " + (midSum.y / g / def.Height).ToString("0.00") + "H" : "") + "  " + Join(refs));
            }
        PermissiveProfile(false); QuietMonster(false);
    }

    static string Join(Dictionary<string, int> d)
    {
        var sb = new StringBuilder();
        foreach (var kv in d) { if (sb.Length > 0) sb.Append(", "); sb.Append(kv.Key).Append(" x").Append(kv.Value); }
        return sb.ToString();
    }

    static readonly Dir[] Mixed = { Dir.Left, Dir.Right, Dir.Overhead, Dir.Thrust, Dir.Right, Dir.Left, Dir.Low, Dir.Overhead, Dir.Left, Dir.Thrust, Dir.Right, Dir.Right };

    static IEnumerator Fair()
    {
        QuietMonster(true); tac.ResetStats(); def.ResetStats();
        var res = new SwingResult[1];
        yield return WaitFree(1f);
        log.AppendLine("FAIR (real profile " + realProfile.name + ": cooldown " + realProfile.blockCooldown + " s, cap " + (realProfile.blockRateCap * 100f).ToString("0") + " %, drain " + realProfile.drainPerBlock + " x" + realProfile.sameSideDrainMultiplier + " same side)");
        // 1. same side, repeatedly
        int breaks0 = tac.GuardBreaks, seenBreaks = breaks0;
        var sbs = new StringBuilder();
        for (int i = 0; i < 14; i++)
        {
            yield return WaitFree(0.5f);
            yield return Swing(Dir.Left, 2.5f, res);
            var r = res[0];
            sbs.Append(r.raised ? (r.blocked ? "B" : "g") : (r.landed ? "x" : "."));
            if (tac.GuardBreaks > seenBreaks) { sbs.Append('!'); seenBreaks = tac.GuardBreaks; }
        }
        log.AppendLine("  same side x14 (LEFT, 2.5 m/s): " + sbs + "   (B = guarded + blocked, g = guarded, x = landed, ! = guard broke)  guard breaks " + (tac.GuardBreaks - breaks0) +
                       ", blocks " + Sum(tac.BlockedByPhase) + ", parries " + Sum(tac.ParriedByPhase) + ", counters " + tac.Counters);
        // 2. mixed directions in each phase
        int phaseCount = 3;
        float[] hpAt = { 0.95f, 0.55f, 0.22f };
        for (int ph = 0; ph < phaseCount; ph++)
        {
            holdHp = hpAt[ph];
            yield return Frames(3);
            yield return WaitFree(phases != null ? 3.5f : 0.5f);   // a boss phase shift roars first
            tac.ResetStats(); int c0 = tac.Counters;
            var seq = new StringBuilder();
            for (int i = 0; i < 24; i++)
            {
                yield return WaitFree(0.4f);
                Dir d = Mixed[i % Mixed.Length];
                float sp = 1.5f + (i % 4) * 0.8f;
                yield return Swing(d, sp, res);
                seq.Append(res[0].raised ? "G" : res[0].detected ? "-" : "?");
            }
            int inc = tac.IncomingByPhase[tac.Phase], rai = tac.RaisedByPhase[tac.Phase];
            float rate = inc > 0 ? (float)rai / inc : 0f, cap = realProfile.blockRateCap, target = realProfile.RateFor(tac.Phase);
            log.AppendLine("  phase " + (tac.Phase + 1) + (ai.enraged ? " (ENRAGED)" : "") + ": 24 mixed swings " + seq + " | incoming " + inc + ", guards " + rai + " -> block rate " + (rate * 100f).ToString("0") +
                           " % (target " + (target * 100f).ToString("0") + " %, cap " + (cap * 100f).ToString("0") + " %) " + (rate <= cap + 1e-4f ? "OK" : "OVER CAP") +
                           " | blocked hits " + tac.BlockedByPhase[tac.Phase] + ", parries " + tac.ParriedByPhase[tac.Phase] + ", counters " + (tac.Counters - c0) + " (rolls " + tac.CounterRolls + ")" +
                           ", guard breaks " + tac.GuardBreaks + " | refusals: " + Refusals());
            if (tac.Counters > c0) log.AppendLine("    last counter: " + tac.LastCounterAttack);
        }
        log.AppendLine("  decision log (last phase):\n" + tac.DumpLog());
        holdHp = 1f;
        QuietMonster(false);
    }

    static int Sum(int[] a) { int n = 0; foreach (var v in a) n += v; return n; }

    static string Refusals()
    {
        var sb = new StringBuilder();
        for (int i = 1; i < tac.RefusalCounts.Length; i++) if (tac.RefusalCounts[i] > 0) sb.Append(DefensePolicyNames.Refusal[i]).Append(" x").Append(tac.RefusalCounts[i]).Append("; ");
        return sb.ToString();
    }

    static IEnumerator CommittedJob()
    {
        QuietMonster(true); tac.ResetStats(); def.ResetStats();
        var res = new SwingResult[1];
        yield return WaitFree(1f);
        log.AppendLine("COMMITTED (swings during the monster's own attacks)");
        int swings = 0, guards = 0, blocks = 0, predictedWhileCommitted = 0;
        var sb = new StringBuilder();
        for (int i = 0; i < 9; i++)
        {
            yield return WaitFree(0.6f);
            int row = FastestRow();
            ai.BeginTacticalAttack(ai.attackTable[row], false, false);   // starts inside the quiet cooldown
            yield return null;
            var a = ai.CurrentAttack;
            float wind = a != null ? a.windUp / Mathf.Max(0.25f, ai.tempoScale) : 0.6f;
            // swing 1: in the telegraph, 2: across the strike, 3: in the recovery
            float delay = i % 3 == 0 ? 0.05f : i % 3 == 1 ? Mathf.Max(0f, wind - 0.35f) : wind + 0.12f;
            yield return Seconds(delay);
            int g0 = Raised(), b0 = tac.CommittedBlocks + Sum(tac.BlockedByPhase);
            bool committedNow = tac.Committed;
            yield return Swing(i % 2 == 0 ? Dir.Left : Dir.Right, 2.5f, res, 0f);
            swings++;
            if (res[0].committedAtSwing) predictedWhileCommitted += res[0].detected ? 1 : 0;
            bool g = Raised() > g0;
            if (g && res[0].committedAtSwing) guards++;
            if (tac.CommittedBlocks > 0) blocks = tac.CommittedBlocks;
            sb.Append(i % 3 == 0 ? "telegraph" : i % 3 == 1 ? "strike" : "recovery").Append(":").Append(committedNow ? "committed" : "free").Append(res[0].detected ? "/predicted" : "").Append(g ? "/GUARD" : "/no guard").Append(res[0].landed ? "/hit landed" : "").Append("  ");
        }
        log.AppendLine("  " + sb);
        log.AppendLine("  swings " + swings + ", predicted while committed " + predictedWhileCommitted + ", refused as committed " + tac.RefusalCounts[(int)DefendRefusal.Committed] +
                       ", guards raised while committed " + guards + ", hits blocked while committed " + tac.CommittedBlocks + (guards == 0 && tac.CommittedBlocks == 0 ? "  -> OK, nothing blocked during an attack" : "  -> FAIL"));
        QuietMonster(false);
    }

    static int FastestRow()
    {
        int best = 0; float w = 99f;
        for (int i = 0; i < ai.attackTable.Count; i++)
        {
            var a = ai.attackTable[i];
            if (a.unblockable || a.rangedKind != MonsterAI.RangedKind.None) continue;
            if (a.windUp < w) { w = a.windUp; best = i; }
        }
        return best;
    }

    static IEnumerator PunishJob()
    {
        tac.ResetStats(); def.ResetStats();
        var res = new SwingResult[1];
        log.AppendLine("PUNISH");
        // whiffs: the swing passes 0.95 m in front of the chest and hits nothing; the cooldown is allowed to come due
        int p0 = tac.Punishes, w0 = tac.Whiffs;
        var rig = Object.FindAnyObjectByType<OVRCameraRig>();
        for (int i = 0; i < 4; i++)
        {
            yield return FreeAfterAttack();
            int a0 = ai.TableAttackCount;
            // fast, so it ends while the monster is still free; 0.7 m in front and over the head height, so it touches neither the body
            // nor the monster's weapon (a clash is not a whiff)
            yield return Swing(i % 2 == 0 ? Dir.Left : Dir.Right, 4f, res, 0f, 0.7f, 1.25f);
            log.AppendLine("    (clashes " + clashes + ")");
            yield return Seconds(0.5f);
            log.AppendLine("  whiff " + (i + 1) + ": landed " + res[0].landed + ", whiffs seen " + (tac.Whiffs - w0) + ", punishes " + (tac.Punishes - p0) + (tac.Punishes > p0 ? " (" + tac.LastPunishAttack + ")" : "") +
                           ", attack started " + (ai.TableAttackCount > a0 ? ai.LastChosenAttack.name : "none"));
        }
        // retreats: the player steps back 1.1 m at 1.4 m/s
        int r0 = tac.Retreats; p0 = tac.Punishes;
        for (int i = 0; i < 3; i++)
        {
            yield return FreeAfterAttack();
            Vector3 start = rig.transform.position, back = -Flat(mon.transform.position - head.position).normalized;
            float d0 = Flat(mon.transform.position - head.position).magnitude;
            float t0 = Time.time; int a0 = ai.TableAttackCount;
            while (Time.time - t0 < 1.1f / 1.4f) { rig.transform.position = start + back * (1.4f * (Time.time - t0)); yield return null; }
            float t1 = Time.time; Vector3 m0 = mon.transform.position;
            while (Time.time - t1 < 1.8f) yield return null;
            log.AppendLine("  retreat " + (i + 1) + ": head moved 1.1 m back (distance " + d0.ToString("0.00") + " -> " + Flat(mon.transform.position - head.position).magnitude.ToString("0.00") + " m after), retreats seen " + (tac.Retreats - r0) +
                           ", punishes " + (tac.Punishes - p0) + (tac.Punishes > p0 ? " (" + tac.LastPunishAttack + ")" : "") + ", attack " + (ai.TableAttackCount > a0 ? ai.LastChosenAttack.name : "none") +
                           ", monster travelled " + Flat(mon.transform.position - m0).magnitude.ToString("0.00") + " m");
            float back0 = Time.time; Vector3 cur = rig.transform.position;
            while (Time.time - back0 < 1f) { rig.transform.position = Vector3.Lerp(cur, start, (Time.time - back0) / 1f); yield return null; }
            rig.transform.position = start;
        }
        log.AppendLine("  decision log:\n" + tac.DumpLog());
    }

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

    /// <summary>Waits for the end of one of the monster's own attacks: it is free, and its next attack is about a second away (the
    /// moment a whiff or a retreat can be punished without waiting for the cooldown).</summary>
    static IEnumerator FreeAfterAttack()
    {
        float t0 = Time.time;
        while (!mon.isAttacking && Time.time - t0 < 8f) yield return null;   // the next attack starts
        while ((mon.isAttacking || ai.IsBusy) && Time.time - t0 < 14f) yield return null;   // ... and ends
        yield return Frames(2);
    }

    static IEnumerator Poses()
    {
        QuietMonster(true);
        // The player's weapons out of the way (collider off, behind the player): a guard pose that swings into a held weapon is a
        // parry, and enough of them break the guard (Level 3's first pose run did exactly that).
        var pc = PlayerCombat.Instance; var parked = new List<KeyValuePair<Transform, Vector3>>(); var cols = new List<Collider>();
        foreach (var w in new[] { pc.weaponTransform, pc.offhandWeaponTransform })
        {
            if (w == null) continue;
            parked.Add(new KeyValuePair<Transform, Vector3>(w, w.localPosition));
            foreach (var c in w.GetComponentsInChildren<Collider>()) if (c.enabled) { c.enabled = false; cols.Add(c); }
            w.position = head.position - Flat(mon.transform.position - head.position).normalized * 1.2f - Vector3.up * 0.8f;
        }
        if (tac != null) tac.ResetStats();
        yield return WaitFree(1f);
        log.AppendLine("POSES (each guard held; weapon middle in monster space: x + = the player's left, y = share of height; axis = hand -> weapon middle)");
        System.IO.Directory.CreateDirectory(captureDir);
        string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        foreach (var pose in new[] { MonsterDefense.GuardPose.SideLeft, MonsterDefense.GuardPose.SideRight, MonsterDefense.GuardPose.High, MonsterDefense.GuardPose.Low })
        {
            yield return WaitFree(0.8f);
            Vector3 contact = pose == MonsterDefense.GuardPose.SideLeft ? World(new Vector3(0.42f, 0.62f, 0.1f)) : pose == MonsterDefense.GuardPose.SideRight ? World(new Vector3(-0.42f, 0.62f, 0.1f))
                : pose == MonsterDefense.GuardPose.High ? World(new Vector3(0f, 0.95f, 0.2f)) : World(new Vector3(0f, 0.5f, 0.3f));
            var cls = pose == MonsterDefense.GuardPose.SideLeft ? MonsterDefense.SwingClass.HorizontalLeft : pose == MonsterDefense.GuardPose.SideRight ? MonsterDefense.SwingClass.HorizontalRight
                : pose == MonsterDefense.GuardPose.High ? MonsterDefense.SwingClass.Overhead : MonsterDefense.SwingClass.Thrust;
            bool ok = def.RaisePlannedGuard(cls, pose, contact, 1.6f);
            yield return Seconds(0.45f);
            var sb = new StringBuilder("  " + MonsterDefense.PoseNames[(int)pose].PadRight(10) + (ok ? "" : "(refused " + def.LastRefusal + ") "));
            for (int h = 0; h < 2; h++)
                if (ik != null && ik.HasWeapon(h)) { Vector3 m = ik.WeaponMiddleLocal(h), ax = ik.WeaponAxisLocal(h); sb.Append(h == 0 ? "right hand: mid (" : "  left hand: mid (").Append(m.x.ToString("+0.00;-0.00")).Append(", ").Append((m.y / def.Height).ToString("0.00")).Append("H, ").Append(m.z.ToString("0.00")).Append(") axis (").Append(ax.x.ToString("+0.00;-0.00")).Append(", ").Append(ax.y.ToString("+0.00;-0.00")).Append(", ").Append(ax.z.ToString("+0.00;-0.00")).Append(")"); }
            log.AppendLine(sb.ToString());
            Shot(scene + "_pose_" + MonsterDefense.PoseNames[(int)pose]);
            yield return Seconds(1.4f);
        }
        foreach (var kv in parked) kv.Key.localPosition = kv.Value;
        foreach (var c in cols) c.enabled = true;
        Physics.SyncTransforms();
        QuietMonster(false);
    }

    /// <summary>Head-camera render to captureDir (Editor-side, no Game view needed).</summary>
    public static void Shot(string name)
    {
        var cam = head.GetComponent<Camera>();
        var rt = RenderTexture.GetTemporary(800, 600, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        var old = cam.targetTexture; cam.targetTexture = rt; cam.Render(); cam.targetTexture = old;
        var prev = RenderTexture.active; RenderTexture.active = rt;
        var tex = new Texture2D(800, 600, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, 800, 600), 0, 0); tex.Apply();
        RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
        System.IO.File.WriteAllBytes(captureDir + "/" + name + ".png", tex.EncodeToPNG());
        Object.Destroy(tex);
    }

    static IEnumerator Fight60()
    {
        holdHp = 0f;   // a real fight: the monster's HP is left alone (nobody hits it)
        if (tac != null) tac.ResetStats();
        float t0 = Time.time;
        log.AppendLine("FIGHT60 (hands off: the player stands still, is healed below 60 %; nothing is forced)");
        MonsterBehaviourTrace.Start(60f);
        int a0 = ai.TableAttackCount;
        while (Time.time - t0 < 60.5f && mon.isAlive) yield return null;
        holdHp = 1f;
        log.AppendLine("  attacks " + (ai.TableAttackCount - a0) + " in 60 s (" + ((ai.TableAttackCount - a0) / 1f).ToString("0") + " per minute)");
        log.AppendLine(MonsterBehaviourTrace.Report());
        log.AppendLine("  state log:\n" + MonsterBehaviourTrace.Log());
        if (tac != null) log.AppendLine("  tactics decision log:\n" + tac.DumpLog(t0));
    }

    static IEnumerator Freq(bool on)
    {
        if (tac != null) { tac.enabled = on; tac.ResetStats(); }
        def.ResetStats();
        var res = new SwingResult[1];
        float t0 = Time.time; int a0 = ai.TableAttackCount, c0 = 0, g0 = def.ReactiveGuards + def.TimedGuards;
        int i = 0;
        while (Time.time - t0 < 60f && mon.isAlive)
        {
            yield return Swing(Mixed[i++ % Mixed.Length], 1.5f + (i % 4) * 0.8f, res);
            float w = Time.time; while (Time.time - w < 0.6f) yield return null;
        }
        int attacks = ai.TableAttackCount - a0;
        log.AppendLine("FREQ tactics " + (on ? "ON " : "OFF") + ": " + i + " player swings in " + (Time.time - t0).ToString("0") + " s -> monster attacks " + attacks + " (" + (attacks * 60f / (Time.time - t0)).ToString("0.0") +
                       "/min), guards " + (def.ReactiveGuards + def.TimedGuards - g0) + (tac != null && on ? ", counters " + tac.Counters + ", punishes " + tac.Punishes + ", whiffs " + tac.Whiffs : ""));
        if (tac != null) tac.enabled = true;
    }

    /// <summary>Level 1: the legacy MonsterDefense path still runs (no tactics).</summary>
    static IEnumerator Legacy()
    {
        QuietMonster(true); def.ResetStats();
        var res = new SwingResult[1];
        yield return WaitFree(1f);
        int guards0 = def.ReactiveGuards;
        for (int i = 0; i < 12; i++) { yield return WaitFree(0.4f); yield return Swing(i % 2 == 0 ? Dir.Left : Dir.Right, 3f, res); }
        log.AppendLine("LEGACY: MonsterTactics present " + (mon.GetComponent<MonsterTactics>() != null) + ", TacticsActive " + def.TacticsActive + " | legacy detection: incoming " + def.IncomingSwings + ", rolls " + def.GuardRolls +
                       ", reactive guards " + (def.ReactiveGuards - guards0) + " (chance " + def.guardChance + ", detectSpeed " + def.detectSpeed + ", gap " + def.minGuardGap + " s), refusals at detection: " + RefusedLegacy());
        QuietMonster(false);
    }

    static string RefusedLegacy()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < def.RefusedAtDetection.Length; i++) if (def.RefusedAtDetection[i] > 0) sb.Append(MonsterDefense.RefusalNames[i]).Append(" x").Append(def.RefusedAtDetection[i]).Append("; ");
        return sb.ToString();
    }
}
