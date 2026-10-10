// AdaptiveGuardVerification.cs
// Purpose: Play-mode probes for the adaptive guard (Editor only, never in a build). Driven over Unity MCP.
//          Jobs: "fight:varied" / "fight:repeat"  100 s fight: the real player weapon is driven through the real HitDetection
//                         path in bursts of 2-3 swings, one every 1.0-1.5 s, with pauses; speeds from the live per-level
//                         histogram (backend, 2026-10-09). "varied" cycles all seven classes, "repeat" swings HORIZONTAL_LEFT
//                         every time. Reports per class, checks the fairness rules every frame and traces Animator transitions.
//                "alloc"  GC allocated per frame with the adaptive guard + hand IK on vs off (alternating rounds, swings running).
//                "look"   each guard pose raised against a held swing, captured from the player's head with the IK on and
//                         off; weapon-in-body samples, elbow swivel and IK miss distance.
// Dependencies: MonsterDefense, MonsterGuardIK, MonsterAI, HitDetection, PlayerCombat, BattleManager, CombatVerification
// Attach to: nothing (static)

using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class AdaptiveGuardVerification
{
    // ───────────────────────── job runner ─────────────────────────
    static IEnumerator job;
    static readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
    static readonly StringBuilder log = new StringBuilder();
    static int lastFrame = -1;
    static string jobName = "";
    public static string captureDir = "Captures/AdaptiveGuard";
    public static float seconds = 100f;
    /// <summary>Per-frame hook inside the fight loop (DragonVerification: staged HP, monitors). Null = off.</summary>
    public static System.Action<float> frameHook;
    /// <summary>Appended to the fight report (DragonVerification). Null = nothing.</summary>
    public static System.Func<string> reportHook;

    public static bool Running => job != null;
    public static string Status() => (job != null ? $"RUNNING {jobName} t={Time.time:0.0}\n" : "IDLE\n") + log;

    public static string Run(string name)
    {
        if (!EditorApplication.isPlaying) return "not playing";
        Stop();
        log.Length = 0;
        jobName = name;
        if (name.StartsWith("fight:")) job = Fight(name.Substring(6));
        else if (name == "look") job = Look();
        else if (name == "alloc") job = Alloc();
        else return "unknown job " + name;
        EditorApplication.update += Tick;
        return "started " + name;
    }

    public static void Stop()
    {
        job = null; stack.Clear();
        EditorApplication.update -= Tick;
        Subscribe(false);
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
    static IEnumerator Seconds(float s) { float end = Time.time + s; while (Time.time < end) yield return null; }

    // ───────────────────────── setup ─────────────────────────
    static OVRCameraRig rig;
    static Transform Head => rig != null ? rig.centerEyeAnchor : null;
    static MonsterController M => BattleManager.Instance != null ? BattleManager.Instance.ActiveMonster : null;

    static IEnumerator StartFight()
    {
        CombatVerification.frameDt = 1f / 30f;
        CombatVerification.Prepare();
        rig = Object.FindAnyObjectByType<OVRCameraRig>();
        var pc = PlayerCombat.Instance;
        if (pc != null && pc.weaponSets != null && pc.weaponSets.Count > 0) pc.EquipWeaponSet(CombatVerification.l3Weapon);
        yield return Frames(3);
        if (M == null || !M.isAlive) BattleManager.Instance.StartBattleFromInspector();
        float w0 = Time.time;
        while (Time.time - w0 < 20f)
        {
            var m = M;
            if (m != null && m.isAlive)
            {
                var e = m.GetComponent<MonsterEntrance>();
                if (e == null || e.Complete) break;
            }
            yield return null;
        }
        yield return Frames(5);
    }

    // Live hit swing speeds per level (backend /api/events?type=hit, 2026-10-09: Easy 83, Medium 257, Hard 209 hits), 0.5 m/s bins,
    // the 9 m/s bin holds everything faster (the editor drive is capped there).
    static readonly Dictionary<string, float[][]> speedMix = new Dictionary<string, float[][]>
    {
        { "Easy", new[] { new[] { 1.0f, 1 }, new[] { 1.5f, 8 }, new[] { 2.0f, 8 }, new[] { 2.5f, 4 }, new[] { 3.0f, 4 }, new[] { 3.5f, 1 }, new[] { 4.0f, 7 }, new[] { 4.5f, 5 }, new[] { 5.0f, 8 }, new[] { 5.5f, 3 }, new[] { 6.0f, 2 }, new[] { 6.5f, 5 }, new[] { 7.0f, 2 }, new[] { 7.5f, 4 }, new[] { 8.0f, 3 }, new[] { 8.5f, 2 }, new[] { 9.0f, 16 } } },
        { "Medium", new[] { new[] { 1.0f, 13 }, new[] { 1.5f, 82 }, new[] { 2.0f, 42 }, new[] { 2.5f, 35 }, new[] { 3.0f, 27 }, new[] { 3.5f, 25 }, new[] { 4.0f, 12 }, new[] { 4.5f, 11 }, new[] { 5.0f, 5 }, new[] { 5.5f, 2 }, new[] { 6.0f, 1 }, new[] { 9.0f, 2 } } },
        { "Hard", new[] { new[] { 1.5f, 1 }, new[] { 2.0f, 11 }, new[] { 2.5f, 20 }, new[] { 3.0f, 10 }, new[] { 3.5f, 13 }, new[] { 4.0f, 18 }, new[] { 4.5f, 3 }, new[] { 5.0f, 13 }, new[] { 5.5f, 9 }, new[] { 6.0f, 5 }, new[] { 6.5f, 2 }, new[] { 7.0f, 5 }, new[] { 7.5f, 8 }, new[] { 8.5f, 2 }, new[] { 9.0f, 89 } } },
    };

    static float DrawSpeed(string level)
    {
        if (!speedMix.TryGetValue(level, out var mix)) return 4f;
        float total = 0f; foreach (var b in mix) total += b[1];
        float r = Random.value * total;
        foreach (var b in mix) { r -= b[1]; if (r <= 0f) return Mathf.Clamp(b[0] + Random.Range(-0.25f, 0.25f), 1f, 9f); }
        return 4f;
    }

    // ───────────────────────── swing shapes ─────────────────────────
    // Each class: end point on the body (height fraction f, lateral x in monster space, + = the player's left) and the start
    // offset from it in monster space (x lateral, y up, z toward the player).
    struct Shape { public float f, x; public Vector3 from; }

    static Shape ShapeFor(MonsterDefense.SwingClass c, int alt)
    {
        float s = alt % 2 == 0 ? 1f : -1f;
        switch (c)
        {
            case MonsterDefense.SwingClass.Overhead: return new Shape { f = 0.78f, x = 0f, from = new Vector3(0f, 0.65f, 0.45f) };
            case MonsterDefense.SwingClass.Diagonal: return new Shape { f = 0.68f, x = 0.05f * s, from = new Vector3(0.55f * s, 0.55f, 0.25f) };
            case MonsterDefense.SwingClass.HorizontalLeft: return new Shape { f = 0.6f, x = 0f, from = new Vector3(0.8f, 0.02f, 0.3f) };
            case MonsterDefense.SwingClass.HorizontalRight: return new Shape { f = 0.6f, x = 0f, from = new Vector3(-0.8f, 0.02f, 0.3f) };
            case MonsterDefense.SwingClass.LowSweep: return new Shape { f = 0.3f, x = 0f, from = new Vector3(0.8f * s, 0.05f, 0.3f) };
            case MonsterDefense.SwingClass.Uppercut: return new Shape { f = 0.55f, x = 0f, from = new Vector3(0.1f * s, -0.6f, 0.3f) };
            default: return new Shape { f = 0.65f, x = 0f, from = new Vector3(0f, 0f, 0.85f) };
        }
    }

    struct Swing { public Transform root; public Vector3 restLocal; public Quaternion restRot; public Vector3 offset, a, b; public float t0, dur; public int phase; public MonsterDefense.SwingClass cls; }

    static Vector3 BodyPoint(MonsterController m, MonsterDefense def, float f, float x, float inFront)
    {
        Vector3 toPlayer = Head.position - m.transform.position; toPlayer.y = 0f; toPlayer.Normalize();
        Vector3 right = m.transform.right;
        return m.transform.position + Vector3.up * (f * def.Height) + right * x + toPlayer * inFront;
    }

    static Vector3 Offset(MonsterController m, Vector3 local)
    {
        Vector3 toPlayer = Head.position - m.transform.position; toPlayer.y = 0f; toPlayer.Normalize();
        return m.transform.right * local.x + Vector3.up * local.y + toPlayer * local.z;
    }

    // ───────────────────────── events ─────────────────────────
    static readonly List<MonsterDefense.GuardReport> reports = new List<MonsterDefense.GuardReport>();
    static int openingEvents, hitsLanded, hitsBlocked, clashes, attacksStarted;
    static float openingBonusEvents;
    static readonly List<float> counterTelegraphs = new List<float>();
    static float lastGuardEndT = -99f;
    static int countersAtLastGuardEnd;

    static void OnGuardEnded(MonsterDefense d, MonsterDefense.GuardReport r) { reports.Add(r); lastGuardEndT = Time.time; countersAtLastGuardEnd = d.Counters; }
    static void OnOpening(MonsterDefense d, MonsterDefense.SwingClass c, float bonus) { openingEvents++; openingBonusEvents += bonus; }
    static void OnHit(HitDetection src, float speed, bool defended) { hitsLanded++; if (defended) hitsBlocked++; }
    static void OnClash(HitDetection src, float speed) { clashes++; }
    static void OnAttack(MonsterAI ai, string name, string tier)
    {
        attacksStarted++;
        var def = ai.GetComponent<MonsterDefense>();
        if (def != null && def.Counters > countersAtLastGuardEnd && ai.CurrentAttack != null)
        {
            countersAtLastGuardEnd = def.Counters;
            counterTelegraphs.Add(ai.CurrentAttack.windUp / Mathf.Max(0.25f, ai.tempoScale));
        }
    }

    static void Subscribe(bool on)
    {
        MonsterDefense.GuardEnded -= OnGuardEnded; MonsterDefense.OpeningLanded -= OnOpening;
        HitDetection.HitLanded -= OnHit; HitDetection.WeaponClashed -= OnClash; MonsterAI.AttackStarted -= OnAttack;
        if (!on) return;
        MonsterDefense.GuardEnded += OnGuardEnded; MonsterDefense.OpeningLanded += OnOpening;
        HitDetection.HitLanded += OnHit; HitDetection.WeaponClashed += OnClash; MonsterAI.AttackStarted += OnAttack;
    }

    // ───────────────────────── fight ─────────────────────────
    static IEnumerator Fight(string pattern)
    {
        yield return StartFight();
        var mon = M; if (mon == null) { log.AppendLine("no monster"); yield break; }
        string level = LevelManager.Instance != null ? LevelManager.Instance.sceneDifficulty.ToString() : "Easy";
        var ai = mon.GetComponent<MonsterAI>();
        var def = mon.GetComponent<MonsterDefense>();
        var health = mon.GetComponent<MonsterHealth>();
        var phases = mon.GetComponent<BossPhaseController>();
        var an = mon.GetComponent<MonsterAnimator>().animator;
        var gik = an.GetComponent<MonsterGuardIK>();
        var pc = PlayerCombat.Instance;
        bool repeat = pattern.StartsWith("repeat");
        bool idle = pattern.EndsWith("-idle");
        if (idle)
        {
            // Attacks paused (the adaptive boost on its own): MonsterAI.UpdateTableCooldown rewrites attackCooldown from a cached
            // base, and Level 3 chains skip it, so both are stopped.
            mon.attackCooldown = 9999f;
            var baseCd = typeof(MonsterAI).GetField("baseAttackCooldown", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (baseCd != null) baseCd.SetValue(ai, 9999f);
            ai.chainChanceByPhase = Vector3.zero;
            float w0 = Time.time; while (mon.isAttacking && Time.time - w0 < 6f) yield return null;
        }
        log.AppendLine($"== {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name} ({level}) {seconds:0} s, pattern {pattern}, " +
                       $"base chance {def.guardChance:0.00} (+{def.guardChancePerPhase:0.00}/phase), delay {def.reactionDelay:0.00} s, gap {def.minGuardGap} s, " +
                       $"uptime cap {def.maxGuardUptime:0.00}, break at {def.guardBreakHits}, counter {def.counterChance:0.00}, IK {(gik != null && gik.ikIntercept)}, side/low {def.sideGuards}/{def.lowGuard}");

        // state names for the Animator trace
        var names = new Dictionary<int, string>();
        var ctrl = an.runtimeAnimatorController as UnityEditor.Animations.AnimatorController;
        foreach (var cs in ctrl.layers[0].stateMachine.states) names[cs.state.nameHash] = cs.state.name;
        foreach (var cs in ctrl.layers[0].stateMachine.states) names[Animator.StringToHash(cs.state.name)] = cs.state.name;
        var transitions = new Dictionary<string, int>();
        string lastState = null;
        var hips = an.GetBoneTransform(HumanBodyBones.Hips);
        float idleHipsMax = 0f, defendHipsMin = 99f;

        def.ResetStats();
        reports.Clear(); openingEvents = hitsLanded = hitsBlocked = clashes = attacksStarted = 0; openingBonusEvents = 0f;
        counterTelegraphs.Clear(); countersAtLastGuardEnd = 0;
        Subscribe(true);

        // fairness monitors
        int vAttack = 0, vStagger = 0, vPhase = 0, vEntrance = 0, vFlinchStart = 0, vLockout = 0, guardStarts = 0;
        float minGap = 99f, minDur = 99f, maxDur = 0f, lastEnd = -99f, guardStart = -1f, breakAt = -99f;
        int lastBreaks = 0; bool wasDef = false;
        int hitsWhileAttacking = 0, attackCancelled = 0;
        int preHits = 0; MonsterAI.AttackDefinition pendingAtk = null; int pendingFrames = 0;
        float boostMax = 0f; int boostedFrames = 0, frames = 0;
        float ikWeightMax = 0f, ikShiftMax = 0f;
        int freeFrames = 0;

        var classes = (MonsterDefense.SwingClass[])System.Enum.GetValues(typeof(MonsterDefense.SwingClass));
        var swung = new int[MonsterDefense.ClassCount];
        int ci = 0, hand = 0, alt = 0, burstLeft = 0;
        float tStart = Time.time, nextSwing = Time.time + 0.5f;
        var sw = new Swing { phase = 0 };
        float hpStart = health.currentHealth;

        while (Time.time - tStart < seconds && mon != null && mon.isAlive)
        {
            frames++;
            float dt = Time.deltaTime;
            if (PlayerHealth.Instance.currentHealth < PlayerHealth.Instance.maxHealth * 0.5f) PlayerHealth.Instance.FullHeal();
            if (health.currentHealth < health.maxHealth * 0.12f) health.currentHealth = health.maxHealth * 0.45f;

            if (frameHook != null) frameHook(Time.time - tStart);
            // ── fairness, every frame ──
            bool d = ai.IsDefending;
            if (d && !wasDef)
            {
                guardStarts++; guardStart = Time.time;
                if (lastEnd > 0f) minGap = Mathf.Min(minGap, Time.time - lastEnd);
                if (def.InFlinch) vFlinchStart++;
                if (Time.time - breakAt < def.staggerSeconds + def.guardLockoutAfterBreak - 0.05f) vLockout++;
            }
            if (!d && wasDef)
            {
                lastEnd = Time.time;
                if (def.GuardBreaks == lastBreaks) { float dur = Time.time - guardStart; minDur = Mathf.Min(minDur, dur); maxDur = Mathf.Max(maxDur, dur); }
            }
            if (def.GuardBreaks != lastBreaks) { lastBreaks = def.GuardBreaks; breakAt = Time.time; }
            if (d && mon.isAttacking) vAttack++;
            if (d && ai.IsStaggered) vStagger++;
            if (d && phases != null && phases.InPhaseShift) vPhase++;
            var ent = mon.GetComponent<MonsterEntrance>();
            if (d && ent != null && !ent.Complete) vEntrance++;
            wasDef = d;
            if (!mon.isAttacking && !ai.IsBusy && !ai.holdActions && !def.InFlinch && !def.IsStaggered) freeFrames++;

            // a player hit landing during a wind-up / attack must not cancel it
            if (hitsLanded != preHits)
            {
                if (mon.isAttacking && ai.CurrentAttack != null) { hitsWhileAttacking++; pendingAtk = ai.CurrentAttack; pendingFrames = 3; }
                preHits = hitsLanded;
            }
            if (pendingFrames > 0 && --pendingFrames == 0)
            {
                var st = an.GetCurrentAnimatorStateInfo(0);
                if (st.IsName("Hit") || an.GetNextAnimatorStateInfo(0).IsName("Hit")) attackCancelled++;
            }

            // adaptive boost
            float bnow = 0f; for (int k = 0; k < MonsterDefense.ClassCount; k++) bnow = Mathf.Max(bnow, def.Boost((MonsterDefense.SwingClass)k));
            bnow = Mathf.Max(bnow, Mathf.Max(def.SideBoost(-1), def.SideBoost(1)));
            boostMax = Mathf.Max(boostMax, bnow); if (bnow > 0f) boostedFrames++;
            if (gik != null) { ikWeightMax = Mathf.Max(ikWeightMax, gik.CurrentWeight); ikShiftMax = Mathf.Max(ikShiftMax, gik.LastShift); }

            // animator trace
            var cur = an.GetCurrentAnimatorStateInfo(0);
            string sn = names.TryGetValue(cur.shortNameHash, out var nm) ? nm : cur.shortNameHash.ToString();
            if (lastState != null && sn != lastState) { string k = lastState + " -> " + sn; transitions[k] = (transitions.TryGetValue(k, out int c) ? c : 0) + 1; }
            lastState = sn;
            if (hips != null)
            {
                float hy = hips.position.y - mon.transform.position.y;
                if (sn == "Idle" || sn == "IdleEnraged") idleHipsMax = Mathf.Max(idleHipsMax, hy);
                if (sn == "Defend" && !an.IsInTransition(0)) defendHipsMin = Mathf.Min(defendHipsMin, hy);
            }

            // ── player swings: bursts of 2-3, one every 1.0-1.5 s, pauses of 1.5-3.5 s ──
            if (sw.phase == 0 && Time.time >= nextSwing)
            {
                if (burstLeft <= 0) burstLeft = Random.Range(2, 4);
                var cls = repeat ? MonsterDefense.SwingClass.HorizontalLeft : classes[ci++ % classes.Length];
                Transform root = hand == 0 || pc.offhandWeaponTransform == null || !pc.offhandWeaponTransform.gameObject.activeInHierarchy ? pc.weaponTransform : pc.offhandWeaponTransform;
                var hd = root.GetComponentInChildren<HitDetection>();
                var col = hd.GetComponent<Collider>();
                var shape = ShapeFor(cls, alt++);
                Vector3 b = BodyPoint(mon, def, shape.f, shape.x, 0.05f);
                Vector3 a = b + Offset(mon, shape.from);
                float speed = Mathf.Max(DrawSpeed(level), pc.minSwingVelocity + 0.2f);
                sw = new Swing { root = root, restLocal = root.localPosition, restRot = root.localRotation, offset = col.bounds.center - root.position, a = a, b = b, cls = cls, phase = 3, t0 = Time.time };
                swung[(int)cls]++;
                hand = 1 - hand;
                burstLeft--;
                float gapS = Random.Range(1.0f, 1.5f);
                nextSwing = Time.time + gapS + (burstLeft <= 0 ? Random.Range(1.5f, 3.5f) : 0f);
                sw.dur = Vector3.Distance(a, b) / speed;
            }
            else if (sw.phase == 3)
            {
                // wind-back to the start below every speed gate (no fake teleport velocity)
                Vector3 target = sw.a - sw.offset;
                sw.root.position = Vector3.MoveTowards(sw.root.position, target, 2.0f * dt);
                if (Vector3.Distance(sw.root.position, target) < 0.005f || Time.time - sw.t0 > 0.6f) { sw.root.position = target; sw.phase = 1; sw.t0 = Time.time; }
            }
            else if (sw.phase == 1)
            {
                float k = Mathf.Clamp01((Time.time - sw.t0) / Mathf.Max(0.02f, sw.dur));
                sw.root.position = Vector3.Lerp(sw.a, sw.b, k) - sw.offset;
                if (k >= 1f) { sw.phase = 2; sw.t0 = Time.time; }
            }
            else if (sw.phase == 2)
            {
                Vector3 rest = sw.root.parent.TransformPoint(sw.restLocal);
                sw.root.position = Vector3.MoveTowards(sw.root.position, rest, 0.9f * dt);
                if (Vector3.Distance(sw.root.position, rest) < 0.01f || Time.time - sw.t0 > 0.5f)
                {
                    sw.root.localPosition = sw.restLocal; sw.root.localRotation = sw.restRot; sw.phase = 0;
                }
            }
            yield return null;
        }
        Subscribe(false);
        if (sw.root != null) { sw.root.localPosition = sw.restLocal; sw.root.localRotation = sw.restRot; }
        float fight = Time.time - tStart;

        // ── report ──
        log.AppendLine($"fight {fight:0.0} s, {frames} frames; player hits landed {hitsLanded} (body-blocked {hitsBlocked}), clashes {clashes}, monster attacks {attacksStarted}; monster HP {hpStart:0} -> {health.currentHealth:0}");
        log.AppendLine($"guards {def.ReactiveGuards + def.TimedGuards} (reactive {def.ReactiveGuards}, timed {def.TimedGuards}) from {def.IncomingSwings} detected swings / {def.GuardRolls} rolls; " +
                       $"poses HIGH {def.PoseGuards[0]} SIDE_L {def.PoseGuards[1]} SIDE_R {def.PoseGuards[2]} LOW {def.PoseGuards[3]}; uptime {def.Uptime * 100f:0.0}% (cap {def.maxGuardUptime * 100f:0}%); " +
                       $"guard breaks {def.GuardBreaks}; counters {def.Counters}/{def.CounterRolls} rolls; flinches {def.Flinches}");
        var sb = new StringBuilder();
        sb.AppendLine("  class            | swung | detected | rolls | mean chance | guards (boosted) | blocked / dmg prevented | openings / bonus dmg");
        for (int k = 0; k < MonsterDefense.ClassCount; k++)
        {
            float mc = def.ClassRolls[k] > 0 ? def.ClassChanceSum[k] / def.ClassRolls[k] : 0f;
            sb.AppendLine($"  {MonsterDefense.ClassNames[k],-16} | {swung[k],5} | {def.ClassSwings[k],8} | {def.ClassRolls[k],5} | {mc * 100f,10:0.0}% | {def.ClassGuards[k],6} ({def.ClassBoostedGuards[k]}) | {def.ClassBlocked[k],7} / {def.ClassPrevented[k],6:0} | {def.ClassOpenings[k],8} / {def.ClassOpeningBonus[k],5:0.0}");
        }
        log.Append(sb);
        var rsb = new StringBuilder();
        for (int k = 0; k < def.RefusedAtDetection.Length; k++) if (def.RefusedAtDetection[k] > 0) rsb.Append($"{MonsterDefense.RefusalNames[k]} {def.RefusedAtDetection[k]}; ");
        log.AppendLine($"no roll at detection: {rsb}; rolls won but refused when due {def.PendingRefused}; monster free (not attacking / busy / flinching / staggered) {(frames > 0 ? 100f * freeFrames / frames : 0f):0}% of frames");
        log.AppendLine($"adaptive: max boost {boostMax * 100f:0} pts, boost active {(frames > 0 ? 100f * boostedFrames / frames : 0f):0}% of frames");
        int rb = 0, ro = 0; float rp = 0f; foreach (var r in reports) { rb += r.blocked; ro += r.openings; rp += r.damagePrevented; }
        log.AppendLine($"events: GuardEnded {reports.Count} (blocked {rb}, openings {ro}, prevented {rp:0}), OpeningLanded {openingEvents} (bonus {openingBonusEvents:0.0})");
        log.AppendLine($"fairness: guard during attack {vAttack} frames, stagger {vStagger}, phase shift {vPhase}, entrance {vEntrance}; guard started in a flinch {vFlinchStart}; guard inside break lockout {vLockout}; " +
                       $"min gap {(minGap < 99f ? minGap.ToString("0.00") : "-")} s (rule {def.minGuardGap}); guard length {(minDur < 99f ? $"{minDur:0.00}-{maxDur:0.00}" : "-")} s (rule {def.guardDuration.x}-{def.guardDuration.y}); " +
                       $"max blocks in a row {def.MaxBlocksInARowSeen} (rule {def.maxBlocksInARow}), forced openings {def.ForcedOpenings}; counter telegraphs {(counterTelegraphs.Count > 0 ? string.Join(", ", counterTelegraphs.ConvertAll(x => x.ToString("0.00"))) : "-")} s (rule >= {def.counterMinTelegraph})");
        log.AppendLine($"wind-up: player hits landed while the monster attacked {hitsWhileAttacking}, attack cancelled into Hit {attackCancelled}");
        log.AppendLine($"IK: max weight {ikWeightMax:0.00}, max hand shift {ikShiftMax:0.00} m");
        log.AppendLine($"hips: idle max {idleHipsMax:0.00} m, defend min {defendHipsMin:0.00} m (sitting check: defend/idle {(idleHipsMax > 0 ? defendHipsMin / idleHipsMax : 0f):0.00})");
        var tsb = new StringBuilder();
        foreach (var kv in transitions) tsb.Append($"{kv.Key} x{kv.Value}; ");
        log.AppendLine("animator transitions seen: " + tsb);
        if (reportHook != null) log.AppendLine(reportHook());
    }

    // ───────────────────────── alloc (guard features on vs off) ─────────────────────────
    static IEnumerator Alloc()
    {
        yield return StartFight();
        var mon = M; if (mon == null) { log.AppendLine("no monster"); yield break; }
        var def = mon.GetComponent<MonsterDefense>();
        var gik = mon.GetComponent<MonsterAnimator>().animator.GetComponent<MonsterGuardIK>();
        var ai = mon.GetComponent<MonsterAI>();
        mon.attackCooldown = 9999f;
        var baseCd = typeof(MonsterAI).GetField("baseAttackCooldown", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (baseCd != null) baseCd.SetValue(ai, 9999f);
        ai.chainChanceByPhase = Vector3.zero;
        def.minGuardGap = 0.5f; def.maxGuardUptime = 1f; def.guardChance = def.guardChanceEnraged = 1f; def.maxGuardChance = 1f;
        var pc = PlayerCombat.Instance;
        string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        bool ikWas = gik != null && gik.ikIntercept;
        using (var rec = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Memory, "GC Allocated In Frame"))
        {
            for (int round = 0; round < 3; round++)
            foreach (bool on in new[] { true, false })
            {
                def.swingClasses = on; def.adaptiveGuard = on; if (gik != null) gik.ikIntercept = on && ikWas;
                var vals = new List<long>();
                int guards0 = def.ReactiveGuards; float ikMax = 0f;
                // 240 frames, a horizontal swing every 40 frames (detection, class, memory, guard + IK all exercised)
                var root = pc.weaponTransform; var hd = root.GetComponentInChildren<HitDetection>(); var col = hd.GetComponent<Collider>();
                Vector3 restLocal = root.localPosition;
                for (int i = 0; i < 240; i++)
                {
                    int k = i % 40;
                    var shape = ShapeFor(MonsterDefense.SwingClass.HorizontalLeft, 0);
                    Vector3 b = BodyPoint(mon, def, shape.f, shape.x, 0.3f), a = b + Offset(mon, shape.from);
                    Vector3 off = col.bounds.center - root.position;
                    if (k < 6) root.position = Vector3.Lerp(a, b, k / 6f) - off;   // ~6 m/s, stops short of the body
                    else root.localPosition = restLocal;
                    yield return null;
                    if (i >= 20) vals.Add(rec.LastValue);
                    if (gik != null) ikMax = Mathf.Max(ikMax, gik.CurrentWeight);
                }
                root.localPosition = restLocal;
                vals.Sort();
                long med = vals[vals.Count / 2];
                double mean = 0; int tn = (int)(vals.Count * 0.95f); for (int i = 0; i < tn; i++) mean += vals[i];
                log.AppendLine($"== ALLOC {scene} guard features {(on ? "ON " : "OFF")} round {round + 1}: median {med} B/frame, mean of lower 95% {mean / tn:0} B, guards raised {def.ReactiveGuards - guards0}, IK weight max {ikMax:0.00}");
            }
        }
        def.swingClasses = true; def.adaptiveGuard = true; if (gik != null) gik.ikIntercept = ikWas;
    }

    // ───────────────────────── look (IK on / off) ─────────────────────────
    static IEnumerator Look()
    {
        yield return StartFight();
        var mon = M; if (mon == null) { log.AppendLine("no monster"); yield break; }
        var ai = mon.GetComponent<MonsterAI>();
        var def = mon.GetComponent<MonsterDefense>();
        var an = mon.GetComponent<MonsterAnimator>().animator;
        var gik = an.GetComponent<MonsterGuardIK>();
        var pc = PlayerCombat.Instance;
        mon.attackCooldown = 9999f; def.timedGuard = false;
        // MonsterAI.UpdateTableCooldown rewrites attackCooldown from its cached base, and Level 3 chains skip it: stop both.
        var baseCd = typeof(MonsterAI).GetField("baseAttackCooldown", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (baseCd != null) baseCd.SetValue(ai, 9999f);
        ai.chainChanceByPhase = Vector3.zero; def.minGuardGap = 0.5f; def.maxGuardUptime = 1f; def.guardBreakEnabled = false;
        def.counterEnabled = false; def.maxBlocksInARow = 0;
        float chance0 = def.guardChance; def.guardChance = 1f; def.guardChanceEnraged = 1f; def.maxGuardChance = 1f; def.adaptiveGuard = false;
        string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        System.IO.Directory.CreateDirectory(captureDir);
        hitsLanded = 0; Subscribe(true);
        var body = new List<CapsuleCollider>();
        foreach (var c in mon.GetComponentsInChildren<CapsuleCollider>(true)) if (!c.CompareTag("MonsterWeapon") && c.transform != mon.transform) body.Add(c);
        var weapons = new List<BoxCollider>();
        foreach (var c in mon.GetComponentsInChildren<BoxCollider>(true)) if (c.CompareTag("MonsterWeapon")) weapons.Add(c);
        var rUp = an.GetBoneTransform(HumanBodyBones.RightUpperArm); var rLow = an.GetBoneTransform(HumanBodyBones.RightLowerArm); var rHand = an.GetBoneTransform(HumanBodyBones.RightHand);
        var lUp = an.GetBoneTransform(HumanBodyBones.LeftUpperArm); var lLow = an.GetBoneTransform(HumanBodyBones.LeftLowerArm); var lHand = an.GetBoneTransform(HumanBodyBones.LeftHand);

        var cases = new[] { MonsterDefense.SwingClass.Overhead, MonsterDefense.SwingClass.HorizontalLeft, MonsterDefense.SwingClass.HorizontalRight,
                            MonsterDefense.SwingClass.LowSweep, MonsterDefense.SwingClass.Uppercut, MonsterDefense.SwingClass.Thrust, MonsterDefense.SwingClass.Diagonal };
        foreach (bool ikOn in new[] { true, false })
        {
            if (gik != null) gik.ikIntercept = ikOn;
            foreach (var cls in cases)
            {
                // wait until free
                float w0 = Time.time;
                while ((ai.IsBusy || ai.IsDefending || mon.isAttacking) && Time.time - w0 < 6f) yield return null;
                yield return Seconds(0.7f);
                var root = pc.weaponTransform; var hd = root.GetComponentInChildren<HitDetection>(); var col = hd.GetComponent<Collider>();
                var shape = ShapeFor(cls, 0);
                Vector3 b = BodyPoint(mon, def, shape.f, shape.x, 0.05f);
                Vector3 a = b + Offset(mon, shape.from);
                Vector3 off = col.bounds.center - root.position;
                Vector3 restLocal = root.localPosition; Quaternion restRot = root.localRotation;
                // slow wind-back, then a 7 m/s swing (reads ~4 m/s through the editor's hand smoothing) that stops 40 % of the way in and holds
                float t0 = Time.time;
                while (Vector3.Distance(root.position, a - off) > 0.005f && Time.time - t0 < 1.5f) { root.position = Vector3.MoveTowards(root.position, a - off, 2f * Time.deltaTime); yield return null; }
                int pr0 = def.PendingRefused; int g0 = def.ReactiveGuards, in0 = def.IncomingSwings, r0 = def.GuardRolls, fl0 = def.Flinches, h0 = hitsLanded;
                bool canAtStart = def.CanGuardNow(true);
                string why = $"busy={ai.IsBusy} def={ai.IsDefending} atk={mon.isAttacking} flinch={def.InFlinch} stag={def.IsStaggered} hold={ai.holdActions}";
                float dur = Vector3.Distance(a, b) / 7f; t0 = Time.time;
                while (Time.time - t0 < dur * 0.4f) { root.position = Vector3.Lerp(a, b, (Time.time - t0) / dur) - off; yield return null; }
                Vector3 hold = root.position;
                t0 = Time.time;
                while (Time.time - t0 < 0.7f) { root.position = hold; yield return null; }   // guard up + IK blended in
                bool raised = def.ReactiveGuards > g0 && ai.IsDefending;
                // samples: weapon inside body, elbow swivel
                int inBody = 0, samples = 0; var parts = new Dictionary<string, int>();
                foreach (var w in weapons)
                {
                    bool leftWeapon = lHand != null && w.transform.IsChildOf(lHand);
                    for (int ix = -1; ix <= 1; ix++) for (int iy = -1; iy <= 1; iy++) for (int iz = -2; iz <= 2; iz++)
                    {
                        Vector3 p = w.transform.TransformPoint(w.center + Vector3.Scale(w.size * 0.5f, new Vector3(ix, iy, iz * 0.5f)));
                        samples++;
                        foreach (var c in body)
                        {
                            // the gripping hand / forearm hold the handle by design
                            if (c.transform == rHand || c.transform == rLow || c.transform == lHand || c.transform == lLow) continue;
                            if (c.transform.IsChildOf(rHand) || c.transform.IsChildOf(lHand)) continue;
                            bool inside; CombatVerification.CapsuleClosest(c, p, out inside);
                            if (inside) { inBody++; parts[c.name] = (parts.TryGetValue(c.name, out int pc0) ? pc0 : 0) + 1; break; }
                        }
                    }
                }
                float elbowR = Vector3.Angle(rUp.position - rLow.position, rHand.position - rLow.position);
                float elbowL = Vector3.Angle(lUp.position - lLow.position, lHand.position - lLow.position);
                Vector3 swivelR = rLow.position - Vector3.Project(rLow.position - rUp.position, rHand.position - rUp.position) - rUp.position;
                string file = $"{captureDir}/{scene}_{(ikOn ? "IK" : "clip")}_{MonsterDefense.ClassNames[(int)cls]}.png";
                Capture(file, mon);
                log.AppendLine($"{(ikOn ? "IK  " : "clip")} {MonsterDefense.ClassNames[(int)cls],-16}: detected {def.IncomingSwings - in0} as {MonsterDefense.ClassNames[(int)def.LastClass]}, rolls {def.GuardRolls - r0}, refused {def.PendingRefused - pr0} ({def.LastRefusal}), flinches {def.Flinches - fl0}, hits {hitsLanded - h0}, canGuard {canAtStart} ({why}); guard {(raised ? "up" : "NOT up")} pose {MonsterDefense.PoseNames[(int)def.ActivePose]}, " +
                               $"IK weight {(gik != null ? gik.CurrentWeight : 0f):0.00} shift {(gik != null ? gik.LastShift : 0f):0.00} m miss {(gik != null ? gik.LastMissDistance : 0f):0.00} m, " +
                               $"weapon samples in body {inBody}/{samples} [{string.Join(" ", new List<string>(System.Linq.Enumerable.Select(parts, kv => kv.Key.Replace("mixamorig:", "") + ":" + kv.Value)))}], elbow R {elbowR:0} deg L {elbowL:0} deg, R elbow swivel local {mon.transform.InverseTransformDirection(swivelR).normalized.ToString("F2")} -> {file}");
                // release
                t0 = Time.time;
                Vector3 restW = root.parent.TransformPoint(restLocal);
                while (Vector3.Distance(root.position, restW) > 0.01f && Time.time - t0 < 1.5f) { restW = root.parent.TransformPoint(restLocal); root.position = Vector3.MoveTowards(root.position, restW, 1f * Time.deltaTime); yield return null; }
                root.localPosition = restLocal; root.localRotation = restRot;
                w0 = Time.time; while (ai.IsDefending && Time.time - w0 < 3f) yield return null;
            }
        }
        if (gik != null) gik.ikIntercept = true;
        def.guardChance = chance0;
        Subscribe(false);
    }

    static void Capture(string file, MonsterController mon)
    {
        Capture(file, mon, false);
        Capture(file.Replace(".png", "_side.png"), mon, true);
    }

    /// <summary>Render from the player's head (or a 3/4 side view) with a capture-only key light (destroyed straight after).</summary>
    static void Capture(string file, MonsterController mon, bool side)
    {
        var go = new GameObject("AGV_Cam");
        var lightGo = new GameObject("AGV_CaptureLight");
        try
        {
            var lt = lightGo.AddComponent<Light>(); lt.type = LightType.Directional; lt.intensity = 1.2f; lt.shadows = LightShadows.None;
            lightGo.transform.rotation = Quaternion.LookRotation(mon.transform.position - Head.position + Vector3.down * 1.2f);
            var cam = go.AddComponent<Camera>();
            var main = Head.GetComponent<Camera>();
            if (main != null) cam.CopyFrom(main);
            Vector3 look = mon.transform.position + Vector3.up * 1.1f;
            Vector3 eye = Head.position;
            if (side) { Vector3 toP = Head.position - mon.transform.position; toP.y = 0f; eye = mon.transform.position + Quaternion.Euler(0f, 55f, 0f) * toP.normalized * 2.6f + Vector3.up * 1.5f; }
            go.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye));
            cam.fieldOfView = 75f;
            var rt = new RenderTexture(720, 720, 24);
            cam.targetTexture = rt; cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(720, 720, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 720, 720), 0, 0); tex.Apply();
            RenderTexture.active = null;
            System.IO.File.WriteAllBytes(file, tex.EncodeToPNG());
            cam.targetTexture = null;
            Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
        }
        finally { Object.DestroyImmediate(go); Object.DestroyImmediate(lightGo); }
    }
}
