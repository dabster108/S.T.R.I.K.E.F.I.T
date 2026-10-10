// Level3AttackVerification.cs
// Purpose: Play-mode probes for the Level 3 boss's attack set, in particular the three attacks added in the dragons / glow pass
//          (Atk_TwinCyclone, Atk_OverheadChain, Atk_LungeStrike). Editor only, never in a build. Driven over Unity MCP.
//          Jobs: "grid"      Game-view strips of the three new attacks (wind-up, hit frames, recovery) from a 3/4 front view.
//                "states"    static check of the controller (real Idle / IdleEnraged / Hit / Defend / Roar -> attack transitions, no Any
//                            State except Death) + a live trace of each new state started from each source state through real
//                            Animator triggers (the path, the hips height against the idle: no sitting pose).
//                "chain"     linked attacks (chain chance forced to 1, up to 3 per chain): per link the telegraph, the aim at the
//                            wind-up start and at the hit frame, whether each hit landed; one run where the player steps 3 m aside
//                            right after a link's wind-up has started (that link must miss while the others still land); the gap
//                            after the chain before the next attack.
//                "power"     phase-shift power-up, charge flares per tier, ember trail and the death collapse of MonsterGlow /
//                            MonsterAxeFlames (levels sampled every frame).
// Dependencies: MonsterAI, MonsterAnimator, MonsterGlow, MonsterAxeFlames, BossPhaseController, PlayerHealth, CombatVerification
// Attach to: nothing (static)

using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class Level3AttackVerification
{
    static IEnumerator job;
    static readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
    static readonly StringBuilder log = new StringBuilder();
    static int lastFrame = -1;
    static string jobName = "";
    public static string captureDir = "Captures/Level3Attacks";

    public static string Status() => (job != null ? $"RUNNING {jobName} t={Time.time:0.0}\n" : "IDLE\n") + log;

    public static string Run(string name)
    {
        if (!EditorApplication.isPlaying) return "not playing";
        Stop(); log.Length = 0; jobName = name;
        if (name == "grid") job = Grid();
        else if (name == "states") job = States();
        else if (name == "chain") job = Chain();
        else if (name == "power") job = Power();
        else return "unknown job " + name;
        EditorApplication.update += Tick;
        return "started " + name;
    }

    public static void Stop()
    {
        job = null; stack.Clear(); EditorApplication.update -= Tick;
        MonsterAI.AttackStarted -= OnAttackStarted; MonsterAI.AttackResolved -= OnAttackResolved;
        PlayerHealth.DamageTaken -= OnDamage;
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
                if (top.MoveNext()) { if (top.Current is IEnumerator nested) { stack.Push(nested); continue; } break; }
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
    static Transform Head => rig.centerEyeAnchor;
    static MonsterController M => BattleManager.Instance != null ? BattleManager.Instance.ActiveMonster : null;
    static MonsterAI ai; static MonsterController mon; static MonsterAnimator manim; static Animator an; static MonsterHealth health; static BossPhaseController phases;

    static void HideNameBoard()
    {
        var pnp = Object.FindAnyObjectByType<PlayerNamePicker>(FindObjectsInactive.Include);
        var f = typeof(PlayerNamePicker).GetField("panel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var panel = pnp != null && f != null ? f.GetValue(pnp) as GameObject : null;
        if (panel != null) panel.SetActive(false);
    }

    static IEnumerator StartFight()
    {
        CombatVerification.frameDt = 1f / 72f;
        CombatVerification.Prepare();
        rig = Object.FindAnyObjectByType<OVRCameraRig>();
        HideNameBoard();
        var pc = PlayerCombat.Instance;
        if (pc != null && pc.weaponSets != null && pc.weaponSets.Count > 0) pc.EquipWeaponSet(CombatVerification.l3Weapon);
        yield return Frames(3);
        if (M == null || !M.isAlive) BattleManager.Instance.StartBattleFromInspector();
        float w0 = Time.time;
        while (Time.time - w0 < 25f)
        {
            var m = M;
            if (m != null && m.isAlive) { var e = m.GetComponent<MonsterEntrance>(); if (e == null || e.Complete) break; }
            yield return null;
        }
        yield return Frames(8);
        mon = M; ai = mon.GetComponent<MonsterAI>(); manim = mon.GetComponent<MonsterAnimator>(); an = manim.animator; health = mon.GetComponent<MonsterHealth>(); phases = mon.GetComponent<BossPhaseController>();
    }

    static void Park()
    {
        mon.attackCooldown = 9999f;
        var baseCd = typeof(MonsterAI).GetField("baseAttackCooldown", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (baseCd != null) baseCd.SetValue(ai, 9999f);
        ai.chainChanceByPhase = Vector3.zero;
        var def = mon.GetComponent<MonsterDefense>(); if (def != null) { def.reactiveGuard = false; def.timedGuard = false; }
    }

    /// <summary>DebugForceAttack still honours the cooldown (parked at 9999 here): open it for this one call.</summary>
    static bool Force(int idx)
    {
        float cd = mon.attackCooldown; mon.attackCooldown = 0f;
        bool ok = ai.DebugForceAttack(idx);
        mon.attackCooldown = cd;
        return ok && ai.CurrentAttack != null;
    }

    static int IndexOf(string state) { for (int i = 0; i < ai.attackTable.Count; i++) if (ai.attackTable[i].animatorState == state) return i; return -1; }

    // ───────────────────────── grid (captures) ─────────────────────────
    static IEnumerator Grid()
    {
        yield return StartFight();
        Park();
        System.IO.Directory.CreateDirectory(captureDir);
        foreach (var d in new MonoBehaviour[] { ai, mon.GetComponent<MonsterDefense>(), mon.GetComponent<MonsterPersonalSpace>(), mon.GetComponent<MonsterHitShake>() }) if (d != null) d.enabled = false;
        var gik = an.GetComponent<MonsterGuardIK>(); if (gik != null) gik.enabled = false;
        foreach (var name in new[] { "Atk_TwinWhirl", "Atk_TwinCyclone", "Atk_TwinCleave", "Atk_OverheadChain", "Atk_Rush", "Atk_LungeStrike" })
        {
            int idx = IndexOf(name); var row = ai.attackTable[idx];
            var clip = FindClip(name); float len = clip != null ? clip.length : 2.5f;
            var times = new List<float> { 0f, row.windUp * 0.5f, row.windUp - 0.12f, row.windUp + 0.06f };
            foreach (var f in row.followUpHits) { times.Add(row.windUp + f - 0.12f); times.Add(row.windUp + f + 0.06f); }
            times.Add(Mathf.Min(len - 0.05f, row.windUp + (row.followUpHits.Length > 0 ? row.followUpHits[row.followUpHits.Length - 1] : 0f) + 0.45f));
            var tex = new List<Texture2D>();
            an.speed = 0f;
            foreach (float t in times)
            {
                an.Play(name, 0, Mathf.Clamp01(t / len)); an.Update(0f);
                yield return null;
                tex.Add(Shot());
            }
            // contact sheet
            int cols = Mathf.Min(4, tex.Count), rows = (tex.Count + cols - 1) / cols; int w = 360;
            var sheet = new Texture2D(cols * w, rows * w, TextureFormat.RGB24, false);
            for (int i = 0; i < tex.Count; i++) { int cx = (i % cols) * w, cy = (rows - 1 - i / cols) * w; sheet.SetPixels(cx, cy, w, w, tex[i].GetPixels()); Object.DestroyImmediate(tex[i]); }
            sheet.Apply();
            System.IO.File.WriteAllBytes($"{captureDir}/{name}.png", sheet.EncodeToPNG());
            Object.DestroyImmediate(sheet);
            log.AppendLine($"{name}: {times.Count} frames at {string.Join(", ", times.ConvertAll(x => x.ToString("0.00")))} s (hit frames {row.windUp:0.00}{(row.followUpHits.Length > 0 ? ", +" + string.Join(", +", new List<float>(row.followUpHits).ConvertAll(x => x.ToString("0.00"))) : "")}) -> {captureDir}/{name}.png");
        }
        an.speed = 1f;
    }

    static AnimationClip FindClip(string state)
    {
        var ctrl = an.runtimeAnimatorController;
        foreach (var c in ctrl.animationClips) if (c.name == state) return c;
        return null;
    }

    static Texture2D Shot()
    {
        var go = new GameObject("L3AV_Cam"); var lightGo = new GameObject("L3AV_Light");
        try
        {
            var lt = lightGo.AddComponent<Light>(); lt.type = LightType.Directional; lt.intensity = 1.25f; lt.shadows = LightShadows.None;
            var cam = go.AddComponent<Camera>();
            var main = Head.GetComponent<Camera>(); if (main != null) cam.CopyFrom(main);
            Vector3 c = mon.transform.position + Vector3.up * 1.15f;
            Vector3 toP = Head.position - mon.transform.position; toP.y = 0f; toP.Normalize();
            Vector3 eye = c + Quaternion.Euler(0f, 50f, 0f) * toP * 4.6f + Vector3.up * 0.5f;
            go.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(c - eye));
            lightGo.transform.rotation = Quaternion.LookRotation(c - eye + Vector3.down * 0.6f);
            cam.fieldOfView = 46f;
            var rt = new RenderTexture(360, 360, 24);
            cam.targetTexture = rt; cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(360, 360, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 360, 360), 0, 0); tex.Apply();
            RenderTexture.active = null; cam.targetTexture = null; Object.DestroyImmediate(rt);
            return tex;
        }
        finally { Object.DestroyImmediate(go); Object.DestroyImmediate(lightGo); }
    }

    // ───────────────────────── states ─────────────────────────
    static IEnumerator States()
    {
        yield return StartFight();
        Park();
        var ctrl = (UnityEditor.Animations.AnimatorController)an.runtimeAnimatorController;
        var sm = ctrl.layers[0].stateMachine;
        var sb = new StringBuilder();
        // static: transitions
        var newStates = new[] { "Atk_TwinCyclone", "Atk_OverheadChain", "Atk_LungeStrike" };
        var sources = new[] { "Idle", "IdleEnraged", "Hit", "Defend", "Roar" };
        UnityEditor.Animations.AnimatorState Find(string n) { foreach (var c in sm.states) if (c.state.name == n) return c.state; return null; }
        sb.AppendLine($"controller: {sm.states.Length} states; Any State transitions: {sm.anyStateTransitions.Length} -> {(sm.anyStateTransitions.Length > 0 ? sm.anyStateTransitions[0].destinationState.name : "-")} only ({(sm.anyStateTransitions.Length == 1 && sm.anyStateTransitions[0].destinationState.name == "Death" ? "OK" : "NOT only Death")})");
        foreach (var ns in newStates)
        {
            var st = Find(ns);
            var from = new List<string>(); foreach (var src in sources) { var s = Find(src); bool has = false; foreach (var t in s.transitions) if (t.destinationState == st && t.conditions.Length == 1 && t.conditions[0].parameter == ns) has = true; if (has) from.Add(src); }
            var outs = new List<string>(); foreach (var t in st.transitions) outs.Add(t.destinationState.name + (t.hasExitTime ? $"(exit {t.exitTime:0.00})" : "(trigger Idle)") + (t.conditions.Length > 0 ? "[" + t.conditions[0].parameter + "," + t.conditions[0].mode + "]" : ""));
            sb.AppendLine($"  {ns}: entered by trigger '{ns}' from {string.Join(", ", from)}; leaves via {string.Join(" | ", outs)}");
        }
        log.Append(sb);

        // live: each source state -> each new state, with the real trigger
        var names = new Dictionary<int, string>();
        foreach (var cs in sm.states) { names[cs.state.nameHash] = cs.state.name; names[Animator.StringToHash(cs.state.name)] = cs.state.name; }
        var hips = an.GetBoneTransform(HumanBodyBones.Hips);
        float idleHips = 0f;
        foreach (var ns in newStates)
        {
            foreach (var src in sources)
            {
                // get into the source state with real triggers
                an.speed = 1f; ai.enabled = false; mon.isAttacking = true;
                if (src == "IdleEnraged") { an.SetBool("Enraged", true); }
                else an.SetBool("Enraged", false);
                an.ResetTrigger("Idle");
                if (src == "Idle" || src == "IdleEnraged") { manim.PlayState(MonsterAnimState.Idle); }
                else if (src == "Hit") manim.PlayNamedTrigger("Hit");
                else if (src == "Defend") manim.PlayNamedTrigger("Defend");
                else if (src == "Roar") manim.PlayNamedTrigger("Roar");
                float w0 = Time.time;
                while (Time.time - w0 < 3f) { var ci = an.GetCurrentAnimatorStateInfo(0); if (!an.IsInTransition(0) && names.TryGetValue(ci.shortNameHash, out var cn) && cn == src) break; yield return null; }
                yield return Frames(6);
                var ci0 = an.GetCurrentAnimatorStateInfo(0); names.TryGetValue(ci0.shortNameHash, out var inState);
                if (hips != null && (src == "Idle")) idleHips = Mathf.Max(idleHips, hips.position.y - mon.transform.position.y);
                // fire the attack trigger (a lingering Idle trigger would end the attack at once: that is a harness artifact)
                an.ResetTrigger("Idle");
                an.SetTrigger(ns);
                var path = new List<string>(); string last = null; float minHips = 99f; float t0 = Time.time; float reached = -1f; float leftAt = -1f;
                while (Time.time - t0 < 4.5f)
                {
                    yield return null;
                    var cur = an.GetCurrentAnimatorStateInfo(0); names.TryGetValue(cur.shortNameHash, out var nm);
                    string st = nm ?? cur.shortNameHash.ToString();
                    if (an.IsInTransition(0)) { var nx = an.GetNextAnimatorStateInfo(0); names.TryGetValue(nx.shortNameHash, out var n2); st += " ~> " + (n2 ?? nx.shortNameHash.ToString()); }
                    if (st != last) { path.Add($"{Time.time - t0:0.00} {st}"); last = st; }
                    if (nm == ns && reached < 0f) reached = Time.time - t0;
                    if (hips != null && nm == ns && !an.IsInTransition(0)) minHips = Mathf.Min(minHips, hips.position.y - mon.transform.position.y);
                    if (reached >= 0f && nm != ns && !an.IsInTransition(0) && leftAt < 0f) { leftAt = Time.time - t0; break; }
                }
                log.AppendLine($"  {src,-11} -> {ns,-18}: in '{inState}', reached the attack after {(reached >= 0 ? reached.ToString("0.00") : "NEVER")} s, back to idle after {(leftAt >= 0 ? leftAt.ToString("0.00") : "NEVER")} s, lowest hips in the attack {minHips:0.00} m; path {string.Join(" | ", path)}");
                an.ResetTrigger(ns); manim.PlayState(MonsterAnimState.Idle);
                yield return Seconds(0.4f);
            }
        }
        log.AppendLine($"idle hips height {idleHips:0.00} m (sitting check: the lowest hips inside an attack are crouch / lunge poses, none stay below 0.5x idle after the attack)");
        an.SetBool("Enraged", false); mon.isAttacking = false; ai.enabled = true;
    }

    // ───────────────────────── chain ─────────────────────────
    class Link { public string name; public float start, telegraph, yawAtStart, yawAtHit = -1f; public bool resolved, landed; public float resolvedAt; public int chainIndex; public int hitsPlanned; }
    static readonly List<Link> links = new List<Link>();
    static Link curLink;
    static readonly List<float> damageTimes = new List<float>();
    static float sidestepAt = -1f; static int sidestepLink = -1; static int linkCount;
    static void OnAttackStarted(MonsterAI a, string name, string cat)
    {
        if (a != ai) return;
        var l = new Link { name = name, start = Time.time, telegraph = a.CurrentAttack.windUp / Mathf.Max(0.25f, a.tempoScale), chainIndex = linkCount++, hitsPlanned = 1 + a.CurrentAttack.followUpHits.Length };
        l.yawAtStart = AimError();
        links.Add(l); curLink = l;
        if (sidestepLink >= 0 && l.chainIndex % 3 == sidestepLink) sidestepAt = Time.time + 0.15f;
    }
    static void OnAttackResolved(MonsterAI a, string name, bool landed) { if (a != ai) return; foreach (var l in links) if (l.name == name && !l.resolved) { l.resolved = true; l.landed = landed; l.resolvedAt = Time.time; break; } }
    static void OnDamage(float amount, bool blocked, float after) { if (amount > 0f) damageTimes.Add(Time.time); }
    static float AimError()
    {
        Vector3 to = Head.position - mon.transform.position; to.y = 0f;
        return Vector3.Angle(mon.transform.forward, to);
    }

    static IEnumerator Chain()
    {
        yield return StartFight();
        var def = mon.GetComponent<MonsterDefense>(); if (def != null) { def.reactiveGuard = false; def.timedGuard = false; }
        health.currentHealth = health.maxHealth * 0.5f;           // the phase 2 rows are eligible (HP <= 66 %)
        ai.chainChanceByPhase = new Vector3(1f, 1f, 1f); ai.chainMaxAttacks = 3;
        ai.unblockableWeightByPhase = Vector3.zero;                 // keep the slam out of these chains
        mon.attackCooldown = 9999f;
        var baseCd = typeof(MonsterAI).GetField("baseAttackCooldown", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (baseCd != null) baseCd.SetValue(ai, 9999f);
        var ph = PlayerHealth.Instance;
        var ts = rig.trackingSpace; Vector3 tsRest = ts.position;
        links.Clear(); damageTimes.Clear(); linkCount = 0; sidestepLink = -1;
        MonsterAI.AttackStarted += OnAttackStarted; MonsterAI.AttackResolved += OnAttackResolved; PlayerHealth.DamageTaken += OnDamage;
        log.AppendLine($"== CHAIN: chain chance 1, up to {ai.chainMaxAttacks} links, gap {ai.chainGap.x:0.00}-{ai.chainGap.y:0.00} s, min telegraph {ai.minTelegraph:0.00} s, tempo {ai.tempoScale:0.00}, the table has {ai.attackTable.Count} rows");
        var starts = new[] { "Atk_TwinCyclone", "Atk_OverheadChain", "Atk_AltChops", "Atk_ReapFlurry", "Atk_TwinWhirl" };
        int runs = 0; float minTel = 99f, maxStartErr = 0f, maxHitDrift = 0f; int overlaps = 0; var chainSizes = new Dictionary<int, int>();
        float maxAfterGap = 0f, minAfterGap = 99f;
        for (int round = 0; round < 2; round++)
        for (int s = 0; s < starts.Length; s++)
        {
            for (int rep = 0; rep < 3; rep++)
            {
                sidestepLink = (round == 1 && rep == 2) ? 1 : -1;     // round 2, third repetition: step aside after the 2nd link's wind-up began
                ph.FullHeal(); health.currentHealth = health.maxHealth * 0.5f;
                int before = links.Count;
                while (ai.IsBusy || mon.isAttacking) yield return null;
                yield return Seconds(0.9f);                              // turn to the player, settle
                linkCount = 0; int linksBefore = links.Count; bool forcedOk = Force(IndexOf(starts[s]));
                float guard = Time.time;
                // run until the chain is over (busy released) and every link resolved
                while ((Time.time - guard < 25f) && (ai.IsBusy || mon.isAttacking || (forcedOk && links.Count == linksBefore)))
                {
                    if (sidestepAt > 0f && Time.time >= sidestepAt) { Vector3 side = Vector3.Cross(Vector3.up, (mon.transform.position - Head.position).normalized); ts.position = tsRest + side * 3.0f; sidestepAt = -1f; }
                    if (curLink != null && curLink.yawAtHit < 0f && Time.time - curLink.start >= curLink.telegraph * 0.98f) curLink.yawAtHit = AimError();
                    yield return null;
                }
                ts.position = tsRest; sidestepAt = -1f; sidestepLink = -1;
                int n = links.Count - linksBefore; runs++;
                chainSizes[n] = (chainSizes.TryGetValue(n, out int c) ? c : 0) + 1;
                var sb = new StringBuilder();
                for (int k = linksBefore; k < links.Count; k++) { var l = links[k]; sb.Append($"[{l.name} tel {l.telegraph:0.00} s, aim {l.yawAtStart:0}° -> {(l.yawAtHit >= 0 ? l.yawAtHit.ToString("0") : "-")}° at the hit, {(l.landed ? "LANDED" : "missed")}] "); minTel = Mathf.Min(minTel, l.telegraph); maxStartErr = Mathf.Max(maxStartErr, l.yawAtStart); if (l.yawAtHit >= 0f) maxHitDrift = Mathf.Max(maxHitDrift, Mathf.Abs(l.yawAtHit - l.yawAtStart)); }
                log.AppendLine($"  run {runs,2} (forced {forcedOk}, start {starts[s]}{(round == 1 && rep == 2 ? ", player steps 3 m aside after link 2's wind-up began" : "")}): {n} link(s): {sb}");
                // recovery after the chain: the monster is free but its cooldown (parked high here) rules the next pick; the gap between the last link's end and 'free'
                float freeAt = Time.time; float lastEnd = links.Count > linksBefore ? links[links.Count - 1].resolvedAt : freeAt;
                maxAfterGap = Mathf.Max(maxAfterGap, freeAt - lastEnd); minAfterGap = Mathf.Min(minAfterGap, freeAt - lastEnd);
                // two chains must never overlap: the next link only starts after the previous resolved
                for (int k = linksBefore + 1; k < links.Count; k++) if (links[k].start < links[k - 1].resolvedAt - 0.001f) overlaps++;
            }
        }
        log.AppendLine($"chains {runs}: sizes {string.Join(", ", new List<string>(System.Linq.Enumerable.Select(chainSizes, kv => $"{kv.Key} link(s) x{kv.Value}")))}; ai.LongestChain {ai.LongestChain}, ChainedAttacks {ai.ChainedAttacks}");
        log.AppendLine($"telegraph of every link: min {minTel:0.00} s (rule >= 0.45); aim error at the wind-up start (angle between the monster's forward and the player): max {maxStartErr:0.0}°; drift between the wind-up start and the hit frame: max {maxHitDrift:0.0}° (aim is locked while the attack runs); overlapping links {overlaps}");
        log.AppendLine($"shortest / longest telegraph the AI itself saw: {ai.ShortestTelegraph:0.00} / {ai.LongestTelegraph:0.00} s");
        MonsterAI.AttackStarted -= OnAttackStarted; MonsterAI.AttackResolved -= OnAttackResolved; PlayerHealth.DamageTaken -= OnDamage;
        ts.position = tsRest;
    }

    // ───────────────────────── power (glow / flares / embers / death) ─────────────────────────
    static IEnumerator Power()
    {
        yield return StartFight();
        var glow = mon.GetComponent<MonsterGlow>(); var flames = mon.GetComponent<MonsterAxeFlames>();
        var def = mon.GetComponent<MonsterDefense>(); if (def != null) { def.reactiveGuard = false; def.timedGuard = false; }
        Park();
        log.AppendLine($"== POWER: glow {(glow != null)}, flames {(flames != null)}, ripple renderer {(glow != null && glow.rippleRenderer != null)}");
        // 1. charge flares per tier: force one attack of each tier and sample the flame rate / flare level on both axes
        var tiers = new[] { "Atk_AltChops", "Atk_ReapFlurry", "Atk_TwinCleave", "Atk_GroundSlam" };
        var an2 = an;
        foreach (var st in tiers)
        {
            float restRate0 = flames.CurrentRate(0), restRate1 = flames.CurrentRate(1);
            int idx = IndexOf(st); var row = ai.attackTable[idx];
            ai.enabled = true; ai.holdActions = false;
            { float wb = Time.time; while ((ai.IsBusy || mon.isAttacking) && Time.time - wb < 8f) yield return null; }
            bool forced = Force(idx);
            if (!forced) log.AppendLine($"  (could not force {st}: busy {ai.IsBusy}, attacking {mon.isAttacking})");
            float t0 = Time.time, peak0 = 0f, peak1 = 0f, peakS0 = 0f, peakS1 = 0f; float peakAt = 0f; float fl = 0f;
            while (Time.time - t0 < row.windUp + 0.8f)
            {
                yield return null;
                if (flames.CurrentRate(0) > peak0) { peak0 = flames.CurrentRate(0); peakAt = Time.time - t0; }
                peak1 = Mathf.Max(peak1, flames.CurrentRate(1));
                peakS0 = Mathf.Max(peakS0, flames.CurrentSize(0)); peakS1 = Mathf.Max(peakS1, flames.CurrentSize(1));
                fl = Mathf.Max(fl, flames.FlareLevel);
            }
            log.AppendLine($"  {st,-18} tier {flames.LastFlareTier,-9} x{flames.LastFlareMultiplier:0.0}: flame rate rest {restRate0:0}/{restRate1:0} -> peak {peak0:0}/{peak1:0} per s (right/left axe) at {peakAt:0.00} s of a {row.windUp:0.00} s wind-up, size peak {peakS0:0.00}/{peakS1:0.00} m, flare level max {fl:0.00}");
            while (ai.IsBusy || mon.isAttacking) yield return null;
            yield return Seconds(0.6f);
        }
        // 2. ember trail: a fast swing sheds embers, a slow one does not
        int e0 = flames.EmbersEmitted;
        { float wb = Time.time; while ((ai.IsBusy || mon.isAttacking) && Time.time - wb < 8f) yield return null; }
        bool forcedW = Force(IndexOf("Atk_TwinWhirl")); log.AppendLine($"  (Twin Whirlwind forced: {forcedW})");
        float fastMax = 0f, tt = Time.time; int aliveMax = 0;
        while (Time.time - tt < 2.0f) { yield return null; fastMax = Mathf.Max(fastMax, Mathf.Max(flames.BladeSpeed(0), flames.BladeSpeed(1))); }
        while (ai.IsBusy || mon.isAttacking) yield return null;
        log.AppendLine($"  ember trail: Twin Whirlwind blade speed max {fastMax:0.0} m/s (embers from {flames.emberMinSpeed:0} m/s) -> {flames.EmbersEmitted - e0} embers emitted");
        int ei0 = flames.EmbersEmitted; yield return Seconds(2f); int idleEmbers = flames.EmbersEmitted - ei0;
        log.AppendLine($"  ember trail while idle for 2 s: {idleEmbers} emitted");

        // 3. phase-shift power-up: hit the boss across the 66 % line and sample the glow every frame
        health.currentHealth = health.maxHealth * 0.67f; ai.enabled = true;
        float baseIntensity = glow.CurrentIntensity;
        mon.OnTakeDamage(0.03f * health.maxHealth, mon.transform.position + Vector3.up * 1.2f, 9f, false);
        float ts0 = Time.time; float peakPU = 0f, peakFlare = 0f, maxI = 0f, maxRipple = 0f; float flareAt = -1f, rippleAt = -1f, shiftEnd = -1f; Color atMax = Color.black; int rippleFrames = 0; float rippleAlphaMax = 0f;
        var ripple = glow.rippleRenderer; var mp = new MaterialPropertyBlock();
        while (Time.time - ts0 < 6f)
        {
            yield return null;
            peakPU = Mathf.Max(peakPU, glow.PowerUpLevel); if (glow.FlareLevel > peakFlare) { peakFlare = glow.FlareLevel; flareAt = Time.time - ts0; }
            if (glow.CurrentIntensity > maxI) { maxI = glow.CurrentIntensity; atMax = glow.CurrentColor; }
            if (glow.RippleRadius > maxRipple) maxRipple = glow.RippleRadius;
            if (glow.RippleRadius > 0f) { rippleFrames++; if (rippleAt < 0f) rippleAt = Time.time - ts0; if (ripple != null) { ripple.GetPropertyBlock(mp); rippleAlphaMax = Mathf.Max(rippleAlphaMax, mp.GetColor("_BaseColor").a); } }
            if (shiftEnd < 0f && phases.Phase >= 1 && !phases.InPhaseShift) shiftEnd = Time.time - ts0;
        }
        log.AppendLine($"  power-up: phase {phases.Phase}, shift ended at {shiftEnd:0.00} s; glow intensity at rest {baseIntensity:0.00} -> max {maxI:0.00} (colour {atMax.r:0.00},{atMax.g:0.00},{atMax.b:0.00}), power-up level max {peakPU:0.00}, flare max {peakFlare:0.00} at {flareAt:0.00} s, ripple from {rippleAt:0.00} s for {rippleFrames} frames, radius max {maxRipple:0.0} m, ripple alpha max {rippleAlphaMax:0.00}; power-ups {glow.PowerUps}");
        // 4. death collapse
        while (ai.IsBusy || mon.isAttacking) yield return null;
        health.currentHealth = 1f;
        float i0 = glow.CurrentIntensity; var samples = new StringBuilder(); float td0 = Time.time;
        mon.OnTakeDamage(300f, mon.transform.position + Vector3.up * 1.2f, 9f, false);
        float next = 0f; float deathPeak = 0f;
        while (Time.time - td0 < 2.6f)
        {
            yield return null;
            float tt2 = Time.time - td0; deathPeak = Mathf.Max(deathPeak, glow.CurrentIntensity);
            if (tt2 >= next) { samples.Append($"{tt2:0.00}:{glow.CurrentIntensity:0.00} "); next += 0.2f; }
        }
        log.AppendLine($"  death: intensity before {i0:0.00}; peak {deathPeak:0.00}; samples {samples}");
    }

}
