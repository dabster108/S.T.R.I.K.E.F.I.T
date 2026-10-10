// Level1AnimationVerification.cs
// Purpose: Play-mode probes for the Level 1 cosmetic animations (Editor only, never in a build). Driven over Unity MCP.
//          Jobs: "fight"  the 100 s varied-swing fight of AdaptiveGuardVerification with extra monitors: every Animator transition,
//                         per-attack trigger-to-animation delay (AttackStarted -> first frame the Animator is in or blending to the
//                         Atk_ state), idle variants / taunts / hit variants played, a player hit landing during an attack, Animator
//                         and MonsterCosmetics cost per frame with the cosmetics component on and off (alternating 5 s windows).
//                "calm"   no swings, attacks paused: idle variants and the taunt at their real timings; then attacks forced while a
//                         variant is playing (delay from trigger to the Atk_ state, which variant was interrupted).
// Dependencies: AdaptiveGuardVerification, CombatVerification, MonsterCosmetics, MonsterAI, MonsterAnimator
// Attach to: nothing (static)

using System.Collections;
using System.Collections.Generic;
using System.Text;
using Unity.Profiling;
using UnityEditor;
using UnityEngine;

public static class Level1AnimationVerification
{
    static IEnumerator job;
    static readonly StringBuilder log = new StringBuilder();
    static int lastFrame = -1;
    static string jobName = "";

    public static string Status() => AdaptiveGuardVerification.Running || job != null ? "RUNNING " + jobName + "\n" + log : "IDLE\n" + AdaptiveGuardVerification.Status() + log;

    struct Pending { public float t; public int frame; public string name; public string from; }
    static readonly List<Pending> pending = new List<Pending>();
    static readonly List<string> delays = new List<string>();
    static readonly List<float> delaySeconds = new List<float>();
    static readonly Dictionary<string, List<float>> delayByFrom = new Dictionary<string, List<float>>();
    static readonly Dictionary<string, int> entries = new Dictionary<string, int>();
    static Dictionary<int, string> names;
    static string prevState = "";
    static int attacksSeen; static bool onlyHits;
    static float lastNorm; static Transform hipsT;
    static readonly List<string> exits = new List<string>();
    static readonly Dictionary<string, Vector2> hips = new Dictionary<string, Vector2>();

    static MonsterController M => BattleManager.Instance != null ? BattleManager.Instance.ActiveMonster : null;

    static void OnAttack(MonsterAI ai, string name, string tier)
    {
        attacksSeen++;
        pending.Add(new Pending { t = Time.time, frame = Time.frameCount, name = name, from = prevState });
    }

    static string StateName(Animator an, bool next)
    {
        var st = next ? an.GetNextAnimatorStateInfo(0) : an.GetCurrentAnimatorStateInfo(0);
        return names != null && names.TryGetValue(st.shortNameHash, out var n) ? n : "?";
    }

    // One call per frame: transitions, attack delays, state entries.
    static void Observe(Animator an)
    {
        string cur = StateName(an, false);
        string nxt = an.IsInTransition(0) ? StateName(an, true) : null;
        if (cur != prevState)
        {
            entries[cur] = (entries.TryGetValue(cur, out int c) ? c : 0) + 1;
            if (prevState.StartsWith("Atk_") && !cur.StartsWith("Atk_") && cur != "Idle")
                exits.Add($"{prevState} -> {cur} at clip {lastNorm:0.00} (isAttacking={(M != null && M.isAttacking)})");
        }
        lastNorm = an.GetCurrentAnimatorStateInfo(0).normalizedTime;
        var hp = hipsT; var root = M != null ? M.transform : null;
        if (hp != null && root != null && !an.IsInTransition(0))
        {
            float hy = hp.position.y - root.position.y;
            if (!hips.TryGetValue(cur, out var r)) r = new Vector2(99f, -99f);
            hips[cur] = new Vector2(Mathf.Min(r.x, hy), Mathf.Max(r.y, hy));
        }
        for (int i = pending.Count - 1; i >= 0; i--)
        {
            var p = pending[i];
            string want = p.name;
            bool reached = cur.StartsWith("Atk_") || (nxt != null && nxt.StartsWith("Atk_"));
            if (reached)
            {
                float d = Time.time - p.t;
                delaySeconds.Add(d);
                if (!delayByFrom.TryGetValue(p.from, out var l)) delayByFrom[p.from] = l = new List<float>();
                l.Add(d);
                delays.Add($"{p.name} from {p.from}: {d * 1000f:0} ms ({Time.frameCount - p.frame} frames) -> {(cur.StartsWith("Atk_") ? cur : nxt)}");
                pending.RemoveAt(i);
            }
            else if (Time.time - p.t > 3f) { delays.Add($"{p.name} from {p.from}: NO ANIMATION within 3 s (animator {cur})"); pending.RemoveAt(i); }
        }
        prevState = cur;
    }

    static Dictionary<int, string> NameMap(Animator an)
    {
        var map = new Dictionary<int, string>();
        var ctrl = an.runtimeAnimatorController as UnityEditor.Animations.AnimatorController;
        foreach (var cs in ctrl.layers[0].stateMachine.states) { map[cs.state.nameHash] = cs.state.name; map[Animator.StringToHash(cs.state.name)] = cs.state.name; }
        return map;
    }

    // ───────────────────────── fight ─────────────────────────
    static ProfilerRecorder recAnim, recJob, recIk, recCos, recLate;
    static MonsterCosmetics cos;
    static readonly double[] sum = new double[2]; static readonly int[] frames = new int[2];
    static readonly double[] animSum = new double[2], jobSum = new double[2], ikSum = new double[2], cosSum = new double[2], lateSum = new double[2];
    static int win; static float winStart;
    static bool hooksOn;
    static int hitsDuringAttack;

    static void FrameHook(float t)
    {
        var mon = M; if (mon == null) return;
        var an = mon.GetComponent<MonsterAnimator>().animator;
        if (names == null) { names = NameMap(an); hipsT = an.GetBoneTransform(HumanBodyBones.Hips); cos = mon.GetComponent<MonsterCosmetics>(); recAnim = ProfilerRecorder.StartNew(ProfilerCategory.Animation, "Animators.Update"); recJob = ProfilerRecorder.StartNew(ProfilerCategory.Animation, "Animators.ProcessAnimationsJob"); recIk = ProfilerRecorder.StartNew(ProfilerCategory.Animation, "Animator.EvaluateIK"); recCos = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "MonsterCosmetics.Update"); recLate = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "MonsterCosmetics.LateUpdate"); winStart = Time.time; }
        Observe(an);
        // alternating windows: cosmetics component ON (even) / OFF (odd)
        if (cos != null)
        {
            if (Time.time - winStart >= 5f) { winStart = Time.time; win = 1 - win; cos.enabled = win == 0; }
            if (frames[win] > 2) { }
            animSum[win] += recAnim.LastValue; jobSum[win] += recJob.LastValue; ikSum[win] += recIk.LastValue; cosSum[win] += recCos.LastValue; lateSum[win] += recLate.LastValue; frames[win]++;
        }
    }

    static string Report()
    {
        var sb = new StringBuilder();
        sb.AppendLine("-- Level 1 animation monitors --");
        sb.AppendLine($"monster attacks started {attacksSeen}; with an animation reached {delaySeconds.Count}");
        if (delaySeconds.Count > 0)
        {
            delaySeconds.Sort();
            float mean = 0f; foreach (var d in delaySeconds) mean += d; mean /= delaySeconds.Count;
            sb.AppendLine($"trigger -> Atk_ state: min {delaySeconds[0] * 1000f:0} ms, median {delaySeconds[delaySeconds.Count / 2] * 1000f:0} ms, mean {mean * 1000f:0} ms, max {delaySeconds[delaySeconds.Count - 1] * 1000f:0} ms");
            foreach (var kv in delayByFrom)
            {
                var l = kv.Value; l.Sort(); float mx = l[l.Count - 1], mn = l[0];
                sb.AppendLine($"   attack started while the Animator was in {kv.Key}: {l.Count} x, {mn * 1000f:0}-{mx * 1000f:0} ms");
            }
        }
        foreach (var d in delays) sb.AppendLine("   " + d);
        foreach (var e in exits) sb.AppendLine("   attack state left to a non-Idle state: " + e);
        var hb = new StringBuilder(); foreach (var kv in hips) hb.Append($"{kv.Key} {kv.Value.x:0.00}-{kv.Value.y:0.00} m; ");
        sb.AppendLine("hips height above the monster root per state (not blending): " + hb);
        var eb = new StringBuilder(); foreach (var kv in entries) eb.Append($"{kv.Key} x{kv.Value}; ");
        sb.AppendLine("state entries: " + eb);
        if (cos != null)
            sb.AppendLine($"cosmetics counters: idle variants {cos.IdleVariantsPlayed}, taunts {cos.TauntsPlayed}, directional hits {cos.DirectionalHits}, heavy hits {cos.HeavyHits}, last hit trigger {cos.LastHitTrigger}");
        for (int w = 0; w < 2; w++)
            if (frames[w] > 0)
                sb.AppendLine($"cost per frame, cosmetics {(w == 0 ? "ON " : "OFF")} ({frames[w]} frames): Animators.Update {animSum[w] / frames[w] / 1000.0:0.0} us, ProcessAnimationsJob {jobSum[w] / frames[w] / 1000.0:0.0} us, EvaluateIK {ikSum[w] / frames[w] / 1000.0:0.0} us, MonsterCosmetics.Update {cosSum[w] / frames[w] / 1000.0:0.0} us, LateUpdate {lateSum[w] / frames[w] / 1000.0:0.0} us");
        return sb.ToString();
    }

    static void ResetMonitors()
    {
        pending.Clear(); delays.Clear(); delaySeconds.Clear(); delayByFrom.Clear(); entries.Clear();
        names = null; prevState = ""; attacksSeen = 0; exits.Clear(); hips.Clear(); hipsT = null; win = 0; winStart = 0f;
        for (int i = 0; i < 2; i++) { frames[i] = 0; animSum[i] = jobSum[i] = ikSum[i] = cosSum[i] = lateSum[i] = 0; }
        log.Length = 0;
    }

    public static string Run(string name)
    {
        if (!EditorApplication.isPlaying) return "not playing";
        Stop();
        ResetMonitors();
        jobName = name;
        if (name == "fight")
        {
            MonsterAI.AttackStarted += OnAttack; hooksOn = true;
            AdaptiveGuardVerification.frameHook = FrameHook;
            AdaptiveGuardVerification.reportHook = Report;
            AdaptiveGuardVerification.seconds = 100f;
            return AdaptiveGuardVerification.Run("fight:varied");
        }
        if (name == "calm" || name == "hits") { onlyHits = name == "hits"; job = Calm(); EditorApplication.update += Tick; return "started " + name; }
        return "unknown job";
    }

    public static void Stop()
    {
        job = null; EditorApplication.update -= Tick;
        if (hooksOn) { MonsterAI.AttackStarted -= OnAttack; hooksOn = false; }
        if (AdaptiveGuardVerification.frameHook == FrameHook) { AdaptiveGuardVerification.frameHook = null; AdaptiveGuardVerification.reportHook = null; }
        if (recAnim.Valid) { recAnim.Dispose(); recJob.Dispose(); recIk.Dispose(); recCos.Dispose(); recLate.Dispose(); }
        if (cos != null) cos.enabled = true;
    }

    static void Tick()
    {
        if (job == null || !EditorApplication.isPlaying) { Stop(); return; }
        if (Time.frameCount == lastFrame) return;
        lastFrame = Time.frameCount;
        try { if (!job.MoveNext()) { log.AppendLine("DONE"); Stop(); } }
        catch (System.Exception e) { log.AppendLine("EXCEPTION " + e); Stop(); }
    }

    static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }

    // ───────────────────────── calm ─────────────────────────
    static IEnumerator Calm()
    {
        CombatVerification.frameDt = 1f / 30f;
        CombatVerification.Prepare();
        yield return Frames(3);
        if (M == null || !M.isAlive) BattleManager.Instance.StartBattleFromInspector();
        float w0 = Time.time;
        while (Time.time - w0 < 20f)
        {
            var m0 = M;
            if (m0 != null && m0.isAlive) { var e = m0.GetComponent<MonsterEntrance>(); if (e == null || e.Complete) break; }
            yield return null;
        }
        yield return Frames(5);
        var mon = M; if (mon == null) { log.AppendLine("no monster"); yield break; }
        var ai = mon.GetComponent<MonsterAI>();
        var an = mon.GetComponent<MonsterAnimator>().animator;
        cos = mon.GetComponent<MonsterCosmetics>();
        names = NameMap(an); hipsT = an.GetBoneTransform(HumanBodyBones.Hips);
        MonsterAI.AttackStarted += OnAttack; hooksOn = true;
        var baseCd = typeof(MonsterAI).GetField("baseAttackCooldown", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        float origBase = (float)baseCd.GetValue(ai); if (origBase < 0f) origBase = mon.attackCooldown;
        float origCd = origBase;
        log.AppendLine($"== calm: scene {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}, attack cooldown {origCd:0.0} s, variants every {cos.idleVariantInterval.x:0}-{cos.idleVariantInterval.y:0} s, taunt after {cos.tauntAfterNoHit.x:0}-{cos.tauntAfterNoHit.y:0} s without a hit");
        // Phase 1: attacks paused, nobody hits it: real timings for 60 s.
        mon.attackCooldown = 9999f; baseCd.SetValue(ai, 9999f);
        float w1 = Time.time; float t0 = Time.time;
        var seen = new List<string>();
        string last = "";
        float stateStart = Time.time;
        var lengths = new Dictionary<string, float>();
        while (Time.time - t0 < (onlyHits ? 0f : 60f))
        {
            string cur = StateName(an, false);
            if (an.IsInTransition(0)) cur = StateName(an, true);
            if (cur != last)
            {
                if (last == "IdleB" || last == "IdleC" || last == "Taunt") lengths[last + "@" + (stateStart - t0).ToString("0.0")] = Time.time - stateStart;
                if (cur == "IdleB" || cur == "IdleC" || cur == "Taunt") seen.Add($"{cur} at t={Time.time - t0:0.0}s (idle for {Time.time - stateStart:0.0}s before)");
                last = cur; stateStart = Time.time;
            }
            yield return null;
        }
        log.AppendLine($"calm 60 s: idle variants {cos.IdleVariantsPlayed}, taunts {cos.TauntsPlayed}; events: " + string.Join("; ", seen));
        foreach (var kv in lengths) log.AppendLine($"   {kv.Key}: played {kv.Value:0.00} s");

        // Phase 2: force an attack while each kind of variant plays (compressed timings so the variants come quickly).
        cos.idleVariantInterval = new Vector2(1.5f, 2.5f); cos.tauntAfterNoHit = new Vector2(3f, 4f); cos.tauntCooldown = 3f;
        var forced = new Dictionary<string, int>();
        for (int trial = 0; trial < (onlyHits ? 0 : 8); trial++)
        {
            // wait for a variant state to be playing for 0.35 s (mid-blend-in covered by the very short trial 1,4,7)
            float wait0 = Time.time; string inVar = null;
            while (Time.time - wait0 < 25f)
            {
                string c = StateName(an, false);
                if (c == "IdleB" || c == "IdleC" || c == "Taunt") { inVar = c; break; }
                if (trial % 3 == 0 && an.IsInTransition(0)) { string n = StateName(an, true); if (n == "IdleB" || n == "IdleC" || n == "Taunt") { inVar = n + "(blend-in)"; break; } }
                yield return null;
            }
            if (inVar == null) { log.AppendLine($"trial {trial}: no variant within 25 s"); continue; }
            if (trial % 3 != 0) { float h = Time.time; while (Time.time - h < 0.35f + 0.2f * trial) yield return null; }
            // release the attack: cooldown elapsed now
            prevState = StateName(an, false);
            mon.attackCooldown = 0.01f; baseCd.SetValue(ai, 0.01f);
            float f0 = Time.time; int guard = 0;
            while (attacksSeen == trialAttacks(trial, forced) && Time.time - f0 < 4f && guard++ < 400) { Observe(an); yield return null; }
            forced[inVar + "#" + trial] = attacksSeen;
            // let the attack finish, pause attacks again
            mon.attackCooldown = 9999f; baseCd.SetValue(ai, 9999f);
            float f1 = Time.time; while ((mon.isAttacking || ai.IsBusy) && Time.time - f1 < 6f) { Observe(an); yield return null; }
            for (int i = 0; i < 10; i++) { Observe(an); yield return null; }
            log.AppendLine($"trial {trial}: attack forced while {inVar}; Animator after: {StateName(an, false)}");
        }
        // Phase 3: the real damage path with controlled hit points / speeds: which reaction plays, how fast.
        mon.attackCooldown = 9999f; baseCd.SetValue(ai, 9999f);
        cos.idleVariants = false; cos.taunt = false;      // keep the Animator in plain Idle for the probes
        var health = mon.GetComponent<MonsterHealth>();
        float full = XPManager.Instance != null ? XPManager.Instance.fullSwingSpeed : 4f;
        var probes = new[]
        {
            new { label = "centre, medium", dx = 0f, speed = XPManager.Instance.mediumSwingSpeed + 0.2f, wait = 1.0f },
            new { label = "player's left side, medium", dx = -0.5f, speed = XPManager.Instance.mediumSwingSpeed + 0.2f, wait = 1.0f },
            new { label = "player's right side, medium", dx = 0.5f, speed = XPManager.Instance.mediumSwingSpeed + 0.2f, wait = 1.0f },
            new { label = "centre, FULL", dx = 0f, speed = full + 1f, wait = 1.0f },
            new { label = "centre, FULL 1.5 s later (inside the 6 s heavy cooldown: plain Hit expected)", dx = 0f, speed = full + 1f, wait = 1.5f },
            new { label = "player's left side, FULL 7 s later (heavy again)", dx = -0.5f, speed = full + 1f, wait = 7f },
        };
        int pi = 0;
        foreach (var pr in probes)
        {
            float wq = Time.time; while (Time.time - wq < 8f && (StateName(an, false) != "Idle" || an.IsInTransition(0) || ai.IsBusy || mon.isAttacking)) yield return null;
            float cd = Time.time; while (Time.time - cd < pr.wait) yield return null;
            health.currentHealth = health.maxHealth;
            Vector3 toPlayer = (Camera.main != null ? Camera.main.transform.position : mon.transform.position + mon.transform.forward) - mon.transform.position; toPlayer.y = 0f; toPlayer.Normalize();
            Vector3 point = mon.transform.position + Vector3.up * 1.2f + mon.transform.right * pr.dx + toPlayer * 0.3f;
            prevState = "Idle"; float tHit = Time.time; string reached = "-"; float tReach = -1f;
            int flinches0 = mon.GetComponent<MonsterDefense>().Flinches;
            mon.OnTakeDamage(1f, point, pr.speed, false);
            bool flinched = mon.GetComponent<MonsterDefense>().Flinches > flinches0;
            for (int i = 0; i < 60; i++)
            {
                yield return null;
                string c = StateName(an, false), n = an.IsInTransition(0) ? StateName(an, true) : null;
                string hitState = c.StartsWith("Hit") ? c : (n != null && n.StartsWith("Hit") ? n : null);
                if (hitState != null) { reached = hitState; tReach = Time.time - tHit; break; }
            }
            float tLeft = -1f;
            if (reached != "-")
            {
                float p0 = Time.time;
                while (Time.time - p0 < 3f && !StateName(an, false).StartsWith("Hit")) yield return null;   // blend-in done
                while (Time.time - p0 < 6f && StateName(an, false).StartsWith("Hit")) yield return null;
                tLeft = Time.time - tHit;
            }
            log.AppendLine($"hit probe '{pr.label}' (speed {pr.speed:0.0}): flinch {(flinched ? "yes" : "no (flinch cooldown)")}, trigger {(flinched ? cos.LastHitTrigger : "-")}, Animator reached {reached} after {(tReach >= 0f ? (tReach * 1000f).ToString("0") + " ms" : "never")}, hit state over after {(tLeft >= 0f ? tLeft.ToString("0.00") + " s" : "-")}");
            pi++;
        }
        // Phase 4: a hit while an attack is winding up must not change the animation.
        for (int k = 0; k < 3; k++)
        {
            float wq = Time.time; while (Time.time - wq < 8f && (StateName(an, false) != "Idle" || an.IsInTransition(0) || ai.IsBusy || mon.isAttacking)) yield return null;
            mon.attackCooldown = 0.01f; baseCd.SetValue(ai, 0.01f);
            float wa = Time.time; while (!mon.isAttacking && Time.time - wa < 4f) { Observe(an); yield return null; }
            mon.attackCooldown = 9999f; baseCd.SetValue(ai, 9999f);
            string atk = StateName(an, false);
            for (int i = 0; i < 6; i++) { Observe(an); yield return null; }       // ~0.2 s into the wind-up
            string before = StateName(an, false);
            health.currentHealth = health.maxHealth;
            Vector3 toPlayer = (Camera.main != null ? Camera.main.transform.position : mon.transform.position + mon.transform.forward) - mon.transform.position; toPlayer.y = 0f; toPlayer.Normalize();
            bool wasAttacking = mon.isAttacking;
            mon.OnTakeDamage(1f, mon.transform.position + Vector3.up * 1.2f + toPlayer * 0.3f, full + 1f, false);
            bool hitState = false; string changed = "-"; float wa2 = Time.time;
            while (mon.isAttacking && Time.time - wa2 < 5f)
            {
                Observe(an);
                string c = StateName(an, false), n = an.IsInTransition(0) ? StateName(an, true) : null;
                if (c.StartsWith("Hit") || (n != null && n.StartsWith("Hit"))) { hitState = true; changed = c + "/" + n; }
                yield return null;
            }
            log.AppendLine($"wind-up probe {k}: attacking={wasAttacking} in {before}; full-speed hit landed; Hit state entered during the attack: {hitState} {changed}; attack ran {Time.time - wa2 + 0.2f:0.0} s");
        }
        mon.attackCooldown = origCd; baseCd.SetValue(ai, origBase);
        cos.idleVariants = true; cos.taunt = true;
        log.Append(Report());
    }

    static int trialAttacks(int trial, Dictionary<string, int> forced)
    {
        int max = 0; foreach (var kv in forced) max = Mathf.Max(max, kv.Value); return max;
    }
}
