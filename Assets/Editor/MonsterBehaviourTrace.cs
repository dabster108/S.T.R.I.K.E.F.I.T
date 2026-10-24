// MonsterBehaviourTrace.cs
// Purpose: Watches the live monster in Play mode and writes down what it actually does, so "the AI works" is a measurement and not a
//          guess: every state change (Entrance / PhaseShift / Stagger / Guard / Attack:<name> / Approach / Retreat / Idle), every attack
//          decision (time, distance, HP %, phase), whether the monster faces the player, whether it slides while it should stand still,
//          whether it gets stuck far from the player, and whether one attack repeats.
//          Read-only: it never changes the monster. Driven from Unity MCP execute_code:
//              MonsterBehaviourTrace.Start(70f)   then poll   MonsterBehaviourTrace.Status()   until it ends with DONE
//              MonsterBehaviourTrace.Report()     the numbers;   MonsterBehaviourTrace.Log()   the full transition log
// Dependencies: MonsterController, MonsterAI, MonsterDefense, BossPhaseController, MonsterEntrance, BattleManager
// Attach to: nothing (static, Editor only)

using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class MonsterBehaviourTrace
{
    static readonly StringBuilder log = new StringBuilder();
    static bool running;
    static float endAt, startedAt;

    static MonsterController mon;
    static MonsterAI ai;
    static MonsterDefense def;
    static BossPhaseController phases;
    static MonsterEntrance entrance;
    static MonsterHealth health;
    static Transform head;

    static int lastFrame = -1;
    static string label = "";
    static float labelSince;
    static Vector3 lastPos;
    static float lastYaw;
    static int lastAttackCount;
    static string lastAttackName = "";
    static int runLength;
    static int maxRun;
    static string maxRunName = "";
    static float stuckSince = -1f;

    static readonly Dictionary<string, float> timeIn = new Dictionary<string, float>();
    static readonly Dictionary<string, float> longest = new Dictionary<string, float>();
    static readonly Dictionary<string, int> attackCounts = new Dictionary<string, int>();
    static readonly List<string> decisions = new List<string>();
    static readonly List<float> facingErr = new List<float>();
    static readonly List<float> distances = new List<float>();
    static int frames, slideFrames, stuckEvents, transitions, guardCount, staggerCount;
    static float maxSlide, maxFreeYawRate, slideWorst;
    static string slideWhere = "";
    static int lastPhase = -1;
    static float hpStart;
    static WeaponSurfaceProxy[] proxies;
    static MonsterPersonalSpace ps;
    static int ghostFrames, popFrames, chatter;
    static float maxOffset, maxPushFrame;

    public static bool Busy => running;

    public static string Status() => (running ? "RUNNING " + (Time.time - startedAt).ToString("0.0") + "s\n" : "") + (log.Length > 0 ? "log lines: " + log.ToString().Split('\n').Length : "empty");
    public static string Log() => log.ToString();

    public static string Start(float seconds)
    {
        mon = BattleManager.Instance != null ? BattleManager.Instance.ActiveMonster : null;
        if (mon == null) return "no active monster";
        ai = mon.GetComponent<MonsterAI>();
        def = mon.GetComponent<MonsterDefense>();
        phases = mon.GetComponent<BossPhaseController>();
        entrance = mon.GetComponent<MonsterEntrance>();
        health = mon.GetComponent<MonsterHealth>();
        var rig = Object.FindAnyObjectByType<OVRCameraRig>();
        head = rig != null ? rig.centerEyeAnchor : (Camera.main != null ? Camera.main.transform : null);
        if (head == null || ai == null) return "no head or no MonsterAI";

        log.Length = 0; timeIn.Clear(); longest.Clear(); attackCounts.Clear(); decisions.Clear(); facingErr.Clear(); distances.Clear();
        frames = slideFrames = stuckEvents = transitions = guardCount = staggerCount = 0;
        maxSlide = maxFreeYawRate = slideWorst = 0f; slideWhere = ""; runLength = maxRun = 0; maxRunName = ""; lastAttackName = "";
        proxies = Object.FindObjectsByType<WeaponSurfaceProxy>(FindObjectsSortMode.None); ps = mon.GetComponent<MonsterPersonalSpace>();
        ghostFrames = popFrames = chatter = 0; maxOffset = maxPushFrame = 0f;
        stuckSince = -1f; lastPhase = phases != null ? phases.Phase : -1; lastAttackCount = ai.TableAttackCount;
        label = ""; labelSince = Time.time; lastPos = mon.transform.position; lastYaw = mon.transform.eulerAngles.y;
        startedAt = Time.time; endAt = Time.time + seconds; hpStart = health != null ? health.currentHealth : 0f;
        lastFrame = -1; running = true;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        log.AppendLine("== TRACE " + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name + " monster=" + mon.name + " " + seconds.ToString("0") + "s attackRange=" + mon.attackRange.ToString("0.00") + " stopDistance=" + ai.stopDistance.ToString("0.00") + " moveSpeed=" + mon.moveSpeed.ToString("0.00"));
        return "tracing";
    }

    public static void Stop()
    {
        running = false;
        EditorApplication.update -= Tick;
        Close(label);
        log.AppendLine("DONE");
    }

    static float Horiz(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }

    static string StateLabel(float dist, float speed, float prevDist)
    {
        if (mon == null || !mon.isAlive) return "Dead";
        if (entrance != null && !entrance.Complete) return "Entrance";
        if (phases != null && phases.InPhaseShift) return "PhaseShift";
        if (ai.IsStaggered || (def != null && def.IsStaggered)) return "Stagger";
        if (ai.IsDefending) return "Guard";
        if (ai.CurrentAttack != null && (mon.isAttacking || ai.IsBusy)) return "Attack:" + ai.CurrentAttack.name;
        if (ai.IsBusy) return "Busy";
        if (speed > 0.15f) return dist < prevDist ? "Approach" : "Retreat";
        return "Idle";
    }

    static void Close(string name)
    {
        if (string.IsNullOrEmpty(name)) return;
        float d = Time.time - labelSince;
        string key = name.StartsWith("Attack:") ? "Attack" : name;
        timeIn[key] = (timeIn.TryGetValue(key, out float t) ? t : 0f) + d;
        if (!longest.TryGetValue(key, out float l) || d > l) longest[key] = d;
    }

    static float prevDistance = -1f;

    static void Tick()
    {
        if (!running) return;
        if (!EditorApplication.isPlaying || mon == null) { Stop(); return; }
        if (Time.frameCount == lastFrame) return; // one sample per game frame
        lastFrame = Time.frameCount;
        float dt = Time.deltaTime;
        if (dt <= 0f) return;
        frames++;

        Vector3 pos = mon.transform.position;
        Vector3 d3 = pos - lastPos; d3.y = 0f;
        float speed = d3.magnitude / dt;
        float dist = Horiz(pos, head.position);
        if (prevDistance < 0f) prevDistance = dist;
        string now = StateLabel(dist, speed, prevDistance);
        prevDistance = dist;
        distances.Add(dist);

        float yaw = mon.transform.eulerAngles.y;
        float yawRate = Mathf.Abs(Mathf.DeltaAngle(lastYaw, yaw)) / dt;
        lastYaw = yaw; lastPos = pos;

        bool free = now == "Idle" || now == "Approach" || now == "Retreat";
        if (free)
        {
            Vector3 to = head.position - pos; to.y = 0f;
            Vector3 fwd = mon.transform.forward; fwd.y = 0f;
            if (to.sqrMagnitude > 0.01f && fwd.sqrMagnitude > 0.01f) facingErr.Add(Vector3.Angle(fwd, to));
            maxFreeYawRate = Mathf.Max(maxFreeYawRate, yawRate);
        }

        // sliding: the root moves while the monster is attacking / guarding / staggered (a Rush attack travels on purpose)
        bool rush = ai.CurrentAttack != null && ai.CurrentAttack.name.ToLowerInvariant().Contains("rush");
        if ((now.StartsWith("Attack:") || now == "Guard" || now == "Stagger" || now == "PhaseShift") && !rush && speed > 0.35f)
        {
            slideFrames++;
            if (speed > slideWorst) { slideWorst = speed; slideWhere = now + " at " + (Time.time - startedAt).ToString("0.0") + "s"; }
        }
        maxSlide = Mathf.Max(maxSlide, speed);

        // stuck: standing free, out of reach, not closing in
        bool outOfReach = dist > mon.attackRange + 0.35f;
        if (now == "Idle" && outOfReach && ai.canMove)
        {
            if (stuckSince < 0f) stuckSince = Time.time;
            else if (Time.time - stuckSince > 2.5f) { stuckEvents++; log.AppendLine("  !! STUCK idle " + dist.ToString("0.00") + " m from the player for " + (Time.time - stuckSince).ToString("0.0") + " s at " + (Time.time - startedAt).ToString("0.0") + "s"); stuckSince = Time.time + 999f; }
        }
        else stuckSince = -1f;

        for (int i = 0; i < proxies.Length; i++) if (proxies[i] != null) { if (proxies[i].GhostShown) { ghostFrames++; break; } }
        for (int i = 0; i < proxies.Length; i++) if (proxies[i] != null) maxOffset = Mathf.Max(maxOffset, proxies[i].Offset);
        if (ps != null) { maxPushFrame = Mathf.Max(maxPushFrame, ps.LastPush); if (ps.LastPush > 0.1f) popFrames++; }

        // attack decision
        if (ai.TableAttackCount != lastAttackCount)
        {
            lastAttackCount = ai.TableAttackCount;
            string name = ai.LastChosenAttack != null ? ai.LastChosenAttack.name : "?";
            attackCounts[name] = (attackCounts.TryGetValue(name, out int c) ? c : 0) + 1;
            runLength = name == lastAttackName ? runLength + 1 : 1;
            if (runLength > maxRun) { maxRun = runLength; maxRunName = name; }
            lastAttackName = name;
            float hp = health != null && health.maxHealth > 0 ? health.currentHealth / health.maxHealth : 0f;
            string line = "  ATTACK " + (Time.time - startedAt).ToString("0.0").PadLeft(5) + "s " + name.PadRight(22) + " dist " + dist.ToString("0.00") + " hp " + (hp * 100f).ToString("0") + "% phase " + (phases != null ? phases.Phase.ToString() : "-") + (ai.enraged ? " ENRAGED" : "");
            decisions.Add(line); log.AppendLine(line);
        }

        if (phases != null && phases.Phase != lastPhase)
        {
            log.AppendLine("  PHASE " + lastPhase + " -> " + phases.Phase + " at " + (Time.time - startedAt).ToString("0.0") + "s");
            lastPhase = phases.Phase;
        }

        if (now != label)
        {
            Close(label);
            if (now == "Guard") guardCount++;
            if (now == "Stagger") staggerCount++;
            transitions++;
            if ((label == "Idle" && now == "Approach") || (label == "Approach" && now == "Idle")) chatter++;
            log.AppendLine((Time.time - startedAt).ToString("0.0").PadLeft(6) + "s  " + (string.IsNullOrEmpty(label) ? "-" : label).PadRight(26) + "-> " + now.PadRight(26) + " dist " + dist.ToString("0.00"));
            label = now; labelSince = Time.time;
        }
        if (Time.time >= endAt || !mon.isAlive) Stop();
    }

    static float Pct(List<float> v, float p)
    {
        if (v.Count == 0) return 0f;
        var s = new List<float>(v); s.Sort();
        return s[Mathf.Clamp(Mathf.RoundToInt((s.Count - 1) * p), 0, s.Count - 1)];
    }

    public static string Report()
    {
        var sb = new StringBuilder();
        float total = Mathf.Max(0.001f, Time.time - startedAt);
        sb.AppendLine("frames " + frames + ", transitions " + transitions + ", span " + total.ToString("0.0") + " s");
        sb.AppendLine("time in state:");
        foreach (var kv in timeIn) sb.AppendLine("  " + kv.Key.PadRight(11) + kv.Value.ToString("0.0").PadLeft(6) + " s (" + (100f * kv.Value / total).ToString("0") + "%)  longest single stay " + (longest.TryGetValue(kv.Key, out float l) ? l : 0f).ToString("0.0") + " s");
        int n = 0; foreach (var kv in attackCounts) n += kv.Value;
        sb.AppendLine("attacks chosen: " + n + " (" + attackCounts.Count + " different), longest run of one attack: " + maxRun + " x " + maxRunName);
        foreach (var kv in attackCounts) sb.AppendLine("  " + kv.Key.PadRight(22) + kv.Value);
        sb.AppendLine("guards " + guardCount + ", staggers " + staggerCount);
        sb.AppendLine("facing error while free: median " + Pct(facingErr, 0.5f).ToString("0") + " deg, p95 " + Pct(facingErr, 0.95f).ToString("0") + " deg, max " + Pct(facingErr, 1f).ToString("0") + " deg, fastest free turn " + maxFreeYawRate.ToString("0") + " deg/s");
        sb.AppendLine("distance to player: min " + Pct(distances, 0f).ToString("0.00") + " m, median " + Pct(distances, 0.5f).ToString("0.00") + " m, p95 " + Pct(distances, 0.95f).ToString("0.00") + " m, max " + Pct(distances, 1f).ToString("0.00") + " m");
        sb.AppendLine("sliding (moved > 0.35 m/s while attacking/guarding/staggered, not a Rush): " + slideFrames + " frames, worst " + slideWorst.ToString("0.00") + " m/s " + slideWhere);
        sb.AppendLine("stuck events: " + stuckEvents + ", top root speed " + maxSlide.ToString("0.00") + " m/s");
        sb.AppendLine("Idle<->Approach flips (stop-go at the edge of reach): " + chatter + " | sword ghost visible in " + ghostFrames + " frames, largest drawn-weapon offset " + maxOffset.ToString("0.00") + " m | monster pushed > 0.1 m in one frame: " + popFrames + " frames, largest push " + maxPushFrame.ToString("0.00") + " m");
        if (health != null) sb.AppendLine("monster HP " + hpStart.ToString("0") + " -> " + health.currentHealth.ToString("0"));
        return sb.ToString();
    }

    // ───────────────────────────── jobs (nested IEnumerators stepped once per game frame) ─────────────────────────────
    static IEnumerator job;
    static readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
    static readonly StringBuilder jobLog = new StringBuilder();
    static int jobLastFrame = -1;
    public static bool JobBusy => job != null;
    public static string JobLog() => jobLog.ToString();

    static void StartJob(IEnumerator j)
    {
        jobLog.Length = 0; stack.Clear(); job = j; jobLastFrame = -1;
        EditorApplication.update -= JobTick; EditorApplication.update += JobTick;
    }

    static void JobTick()
    {
        if (job == null || !EditorApplication.isPlaying) { job = null; stack.Clear(); EditorApplication.update -= JobTick; return; }
        if (Time.frameCount == jobLastFrame) return;
        jobLastFrame = Time.frameCount;
        try
        {
            while (true)
            {
                var top = stack.Count > 0 ? stack.Peek() : job;
                if (top.MoveNext()) { if (top.Current is IEnumerator nested) { stack.Push(nested); continue; } break; }
                if (stack.Count > 0) { stack.Pop(); continue; }
                jobLog.AppendLine("JOB DONE"); job = null; EditorApplication.update -= JobTick; break;
            }
        }
        catch (System.Exception e) { jobLog.AppendLine("JOB EXCEPTION " + e); job = null; stack.Clear(); EditorApplication.update -= JobTick; }
    }

    static IEnumerator Wait(float s) { float end = Time.time + s; while (Time.time < end) yield return null; }

    static MonsterController Mon() => BattleManager.Instance != null ? BattleManager.Instance.ActiveMonster : null;
    static Transform Head()
    {
        var rig = Object.FindAnyObjectByType<OVRCameraRig>();
        return rig != null ? rig.centerEyeAnchor : (Camera.main != null ? Camera.main.transform : null);
    }

    /// <summary>Quick player swing at the monster's chest, coming in from `side` (+1 = the player's right, -1 = the player's left), then a slow return.</summary>
    static IEnumerator Swing(int side, float speed, float back = 0.75f, float lateral = 0.6f, bool accelerate = false)
    {
        var pc = PlayerCombat.Instance; var m = Mon(); var d = m.GetComponent<MonsterDefense>(); var head = Head();
        Transform root = pc.weaponTransform;
        var hd = root.GetComponentInChildren<HitDetection>(); var col = hd.GetComponent<Collider>();
        Vector3 restLocal = root.localPosition; Quaternion restRot = root.localRotation;
        Vector3 target = d != null && d.guardTarget != null ? d.guardTarget.position : m.transform.position + Vector3.up * 1.3f;
        Vector3 inward = target - head.position; inward.y = 0f; inward.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, inward);
        Vector3 b = target - inward * 0.15f;
        Vector3 a0 = b - inward * back + right * side * lateral;
        Vector3 offset = col.bounds.center - root.position;
        // Move to the start slowly (below the swing-detection speed): a jump would read as a fake fast swing and use up the one roll per swing.
        Vector3 goal = a0 - offset; float tp = Time.time;
        while (Vector3.Distance(root.position, goal) > 0.01f && Time.time - tp < 5f) { root.position = Vector3.MoveTowards(root.position, goal, 0.8f * Time.deltaTime); yield return null; }
        yield return Wait(0.5f);
        float total = Vector3.Distance(a0, b), t0 = Time.time;
        if (accelerate)
        {
            // an arm accelerates: speed = accel * t from rest (`speed` is the acceleration in m/s^2), so the swing crosses a speed gate late in the stroke
            while (true)
            {
                float t = Time.time - t0, sdist = 0.5f * speed * t * t;
                if (sdist >= total) break;
                root.position = Vector3.Lerp(a0, b, sdist / total) - offset; yield return null;
            }
        }
        else
        {
            float dur = total / speed;
            while (Time.time - t0 < dur) { root.position = Vector3.Lerp(a0, b, (Time.time - t0) / dur) - offset; yield return null; }
        }
        root.position = b - offset;
        float t1 = Time.time;
        while (Time.time - t1 < 1.2f)
        {
            Vector3 rest = root.parent.TransformPoint(restLocal);
            root.position = Vector3.MoveTowards(root.position, rest, 0.9f * Time.deltaTime);
            if (Vector3.Distance(root.position, rest) < 0.01f) break;
            yield return null;
        }
        root.localPosition = restLocal; root.localRotation = restRot;
    }

    // ---- guard side: swing from each side, record the guard pose and where the monster's weapon hands end up ----
    public static string GuardSideStart(int swingsPerSide) { StartJob(GuardSideJob(swingsPerSide)); return "guard-side test started"; }

    /// <summary>Steps `main` (nested enumerators included) and calls `each` once per frame while it runs.</summary>
    static IEnumerator WithWatch(IEnumerator main, System.Action each)
    {
        var st = new Stack<IEnumerator>(); st.Push(main);
        while (st.Count > 0)
        {
            var top = st.Peek();
            if (top.MoveNext()) { if (top.Current is IEnumerator nested) st.Push(nested); else { each(); yield return null; } }
            else st.Pop();
        }
    }

    static int RefSum(MonsterDefense d) { int t = 0; for (int q = 0; q < d.RefusedAtDetection.Length; q++) t += d.RefusedAtDetection[q]; return t; }

    static IEnumerator GuardSideJob(int n)
    {
        var m = Mon(); var ai = m.GetComponent<MonsterAI>(); var d = m.GetComponent<MonsterDefense>(); var health = m.GetComponent<MonsterHealth>();
        var ik = m.GetComponentInChildren<MonsterGuardIK>(); var head = Head();
        m.attackCooldown = 9999f;
        var f = typeof(MonsterAI).GetField("baseAttackCooldown", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (f != null) f.SetValue(ai, 9999f);
        d.maxGuardChance = 1f; d.guardChance = 1f; d.guardChanceEnraged = 1f; d.guardChancePerPhase = 0f; d.minGuardGap = 0f; d.maxGuardUptime = 1f; d.maxBlocksInARow = 99; d.guardBreakEnabled = false; d.timedGuard = false;
        d.adaptiveGuard = false;   // isolate the side logic from the learning
        jobLog.AppendLine("== GUARD SIDE n=" + n + " per side, monster " + m.name + ", twoWeapons=" + (ik != null && ik.twoWeapons) + ", followContact=" + (ik != null ? ik.followContact.ToString("0.00") : "-"));
        float originalGate = d.detectSpeed;
        // detection gate x how hard the player accelerates (peak speed at the body = sqrt(2 * accel * 1.45 m)): 1.5 -> 2.1 m/s (a median Level 2 swing), 4 -> 3.4, 11 -> 5.6
        var plan = new List<Vector3>();
        foreach (float gate in new[] { originalGate, 1.2f }) foreach (float acc in new[] { 1.5f, 4f, 11f }) foreach (int sd0 in new[] { 1, -1 }) plan.Add(new Vector3(gate, acc, sd0));
        foreach (var combo in plan)
        {
            d.detectSpeed = combo.x; float accel = combo.y; int side = (int)combo.z; float speed = accel;
            int detectedTotal = 0, hitFirst = 0;
            int guards = 0, correct = 0; float sumToward = 0f; var poses = new Dictionary<string, int>(); var classes = new Dictionary<string, int>();
            for (int k = 0; k < n; k++)
            {
                float w0 = Time.time;
                while ((ai.IsDefending || ai.IsBusy || m.isAttacking || Time.time - w0 < 1.0f) && Time.time - w0 < 6f) { health.currentHealth = health.maxHealth; PlayerHealth.Instance.FullHeal(); yield return null; }
                Vector3 chest = d.guardTarget.position;
                Vector3 inward = chest - head.position; inward.y = 0f; inward.Normalize();
                Vector3 right = Vector3.Cross(Vector3.up, inward);
                float baseRight = ik != null && ik.rightWeapon != null ? Vector3.Dot(ik.rightWeapon.position - chest, right) : 0f;
                float baseLeft = ik != null && ik.leftWeapon != null ? Vector3.Dot(ik.leftWeapon.position - chest, right) : 0f;
                int inc0 = d.IncomingSwings, fl0 = d.Flinches;
                bool up = false, sampled = false; float upAt = 0f; string pose = "", cls = ""; float moved = -9f;
                int sd = side;
                yield return WithWatch(Swing(side, accel, 1.3f, 0.7f, true), () =>
                {
                    if (!up && ai.IsDefending) { up = true; upAt = Time.time; pose = d.ActivePose.ToString(); cls = d.LastClass.ToString(); }
                    if (up && !sampled && Time.time - upAt >= 0.45f)
                    {
                        sampled = true;
                        if (ik != null && ik.rightWeapon != null) moved = Mathf.Max(moved, sd * (Vector3.Dot(ik.rightWeapon.position - chest, right) - baseRight));
                        if (ik != null && ik.leftWeapon != null) moved = Mathf.Max(moved, sd * (Vector3.Dot(ik.leftWeapon.position - chest, right) - baseLeft));
                    }
                });
                detectedTotal += d.IncomingSwings - inc0;
                if (d.Flinches > fl0) hitFirst++;
                if (up && sampled)
                {
                    guards++; sumToward += moved; if (moved > 0f) correct++;
                    poses[pose] = (poses.TryGetValue(pose, out int pc) ? pc : 0) + 1; classes[cls] = (classes.TryGetValue(cls, out int cc) ? cc : 0) + 1;
                }
            }
            var sbp = new StringBuilder(); foreach (var kv in poses) sbp.Append(kv.Key + " x" + kv.Value + "; ");
            var sbc = new StringBuilder(); foreach (var kv in classes) sbc.Append(kv.Key + " x" + kv.Value + "; ");
            jobLog.AppendLine("  gate " + combo.x.ToString("0.0") + " m/s, accel " + accel.ToString("0.0") + " (peak " + Mathf.Sqrt(2f * accel * 1.45f).ToString("0.0") + " m/s), from the player's " + (side > 0 ? "RIGHT" : "LEFT ") + ": detected " + detectedTotal + "/" + n + ", hit before the guard rose " + hitFirst + "/" + n + ", guards " + guards + "/" + n + " | class " + sbc + "| pose " + sbp + "| weapon moved toward the swing's side in " + correct + "/" + guards + ", mean move " + (guards > 0 ? (sumToward / guards).ToString("0.00") : "-") + " m");
        }
    }

    // ---- ghost: the same swings with the faint copy on and off ----
    public static string GhostStart(int swings) { StartJob(GhostJob(swings)); return "ghost test started"; }

    static IEnumerator GhostJob(int n)
    {
        var m = Mon(); var ai = m.GetComponent<MonsterAI>(); var health = m.GetComponent<MonsterHealth>();
        m.attackCooldown = 9999f;
        var f = typeof(MonsterAI).GetField("baseAttackCooldown", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (f != null) f.SetValue(ai, 9999f);
        var pr = Object.FindObjectsByType<WeaponSurfaceProxy>(FindObjectsSortMode.None);
        jobLog.AppendLine("== GHOST proxies=" + pr.Length + " scene flag PlayerCombat.weaponGhost=" + PlayerCombat.Instance.weaponGhost);
        foreach (bool want in new[] { true, false })
        {
            foreach (var p in pr) p.ghostEnabled = want;
            int shown = 0, frames = 0; float maxOff = 0f;
            for (int k = 0; k < n; k++)
            {
                health.currentHealth = health.maxHealth; PlayerHealth.Instance.FullHeal();
                var sw = Swing(k % 2 == 0 ? 1 : -1, 5f);
                while (sw.MoveNext())
                {
                    if (sw.Current is IEnumerator nested) { while (nested.MoveNext()) { Count(pr, ref shown, ref frames, ref maxOff); yield return null; } }
                    else { Count(pr, ref shown, ref frames, ref maxOff); yield return null; }
                }
                yield return Wait(0.8f);
            }
            jobLog.AppendLine("  ghostEnabled=" + want + ": ghost visible in " + shown + " of " + frames + " frames, largest drawn-weapon offset " + maxOff.ToString("0.00") + " m");
        }
    }

    static void Count(WeaponSurfaceProxy[] pr, ref int shown, ref int frames, ref float maxOff)
    {
        frames++; bool any = false;
        foreach (var p in pr) { if (p == null) continue; if (p.GhostShown) any = true; maxOff = Mathf.Max(maxOff, p.Offset); }
        if (any) shown++;
    }

    // ---- kiting: stay at a long distance so the monster has to close it (gap-closers, throws) ----
    public static string KiteStart(float distance, float seconds) { StartJob(KiteJob(distance, seconds)); return "kite started"; }

    static IEnumerator KiteJob(float distance, float seconds)
    {
        var rig = Object.FindAnyObjectByType<OVRCameraRig>(); var m = Mon(); var head = Head();
        float t0 = Time.time;
        jobLog.AppendLine("== KITE holding " + distance.ToString("0.0") + " m from the monster for " + seconds.ToString("0") + " s");
        while (Time.time - t0 < seconds && m != null && m.isAlive)
        {
            Vector3 away = head.position - m.transform.position; away.y = 0f;
            float d = away.magnitude;
            if (d > 0.01f) rig.transform.position += away.normalized * Mathf.Clamp(distance - d, -0.05f, 0.05f);
            if (PlayerHealth.Instance.currentHealth < PlayerHealth.Instance.maxHealth * 0.5f) PlayerHealth.Instance.FullHeal();
            yield return null;
        }
    }

    // ---- phases and death: chip the monster down through 66 % and 33 % and on to death ----
    public static string PhaseStart() { StartJob(PhaseJob()); return "phase test started"; }

    static IEnumerator PhaseJob()
    {
        var m = Mon(); var health = m.GetComponent<MonsterHealth>(); var phases = m.GetComponent<BossPhaseController>();
        health.currentHealth = health.maxHealth * 0.70f;
        jobLog.AppendLine("== PHASES from 70 % HP, 1.5 % of max HP every 1.2 s");
        int lastPhase = phases != null ? phases.Phase : -1; float t0 = Time.time; bool wasShift = false;
        while (m != null && m.isAlive && Time.time - t0 < 150f)
        {
            PlayerHealth.Instance.FullHeal();
            health.TakeDamage(health.maxHealth * 0.015f, m.transform.position + Vector3.up * 1.3f);
            float tn = Time.time + 1.2f;
            while (Time.time < tn)
            {
                PlayerHealth.Instance.FullHeal();
                if (phases != null && phases.Phase != lastPhase) { jobLog.AppendLine("  " + (Time.time - t0).ToString("0.0") + "s phase " + lastPhase + " -> " + phases.Phase + " at HP " + (health.Normalized * 100f).ToString("0") + "% (phase shifts so far " + phases.PhaseShifts + ")"); lastPhase = phases.Phase; }
                if (phases != null && phases.InPhaseShift != wasShift) { jobLog.AppendLine("  " + (Time.time - t0).ToString("0.0") + "s phase-shift " + (phases.InPhaseShift ? "BEGAN" : "ENDED") + " (resisted hits " + phases.ResistedHits + ", HP " + (health.Normalized * 100f).ToString("0") + "%)"); wasShift = phases.InPhaseShift; }
                yield return null;
            }
        }
        jobLog.AppendLine("  monster alive: " + (m != null && m.isAlive) + ", phase " + (phases != null ? phases.Phase : -1) + ", phase shifts " + (phases != null ? phases.PhaseShifts : -1) + ", battle active: " + (BattleManager.Instance != null && BattleManager.Instance.IsBattleActive) + ", HP " + (health.Normalized * 100f).ToString("0") + "%");
        yield return Wait(3f);
        jobLog.AppendLine("  3 s later: battle active " + (BattleManager.Instance != null && BattleManager.Instance.IsBattleActive));
    }
}
