// CombatVerification.cs
// Purpose: Editor-only Play-mode test harness used to measure and verify the fights (spacing / personal space,
//          forced attack sweeps, simulated fights with real HitDetection contacts, rendering + script cost).
//          Driven from Unity MCP execute_code: CombatVerification.Run("<job>") then poll CombatVerification.Status().
//          Never runs in a build (Editor folder) and never saves anything; the backend is redirected to a dead port.
// Dependencies: BattleManager, MonsterAI, MonsterController, PlayerCombat, HitDetection, OVRCameraRig

using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine;

public static class CombatVerification
{
    // ───────────────────────── job runner ─────────────────────────
    static IEnumerator job;
    static readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
    static string jobName = "";
    static readonly StringBuilder log = new StringBuilder();
    static int lastFrame = -1;
    public static bool Busy => job != null;

    public static string Status()
    {
        return (job != null ? $"RUNNING {jobName} t={Time.time:0.0} frame={Time.frameCount}\n" : "IDLE\n") + log;
    }

    public static void ClearLog() { log.Length = 0; }

    public static string Run(string name)
    {
        if (!EditorApplication.isPlaying) return "not playing";
        Stop();
        log.Length = 0;
        jobName = name;
        switch (name)
        {
            case "baseline": job = Baseline(); break;
            case "sim": job = SimFight(simSeconds); break;
            case "sweep": job = SweepOnly(); break;
            case "rush": job = RushTest(); break;
            case "windup": job = WindUpTest(); break;
            case "perf": job = PerfOnly(); break;
            default: return "unknown job " + name;
        }
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        return "started " + name;
    }

    public static void Stop()
    {
        job = null;
        stack.Clear();
        EditorApplication.update -= Tick;
        EndProfiler();
    }

    static void Tick()
    {
        if (job == null || !EditorApplication.isPlaying) { Stop(); return; }
        if (Time.frameCount == lastFrame) return; // one step per game frame
        lastFrame = Time.frameCount;
        try
        {
            // Nested IEnumerators run like coroutines: a yielded IEnumerator is pushed and stepped first.
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
        catch (System.Exception e)
        {
            log.AppendLine("EXCEPTION " + e);
            Stop();
        }
    }

    static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }
    static IEnumerator Seconds(float s) { float end = Time.time + s; while (Time.time < end) yield return null; }

    // ───────────────────────── setup ─────────────────────────
    public static WeaponType l3Weapon = WeaponType.Mace;
    public static float frameDt = 1f / 30f;
    public static float simSeconds = 100f;
    /// <summary>Where SessionReporter posts during a harness run. Default: a dead port, so nothing reaches a backend.
    /// Set to a local backend ("http://localhost:4100") to read a simulated session back.</summary>
    public static string backendUrl = "http://127.0.0.1:9";

    static OVRCameraRig rig;
    static Transform Head => rig != null ? rig.centerEyeAnchor : null;
    static MonsterController M => BattleManager.Instance != null ? BattleManager.Instance.ActiveMonster : null;

    public static string Prepare()
    {
        Application.runInBackground = true;
        Time.captureDeltaTime = frameDt;
        if (SessionReporter.Instance != null) SessionReporter.Instance.baseUrl = backendUrl;
        var pnp = Object.FindAnyObjectByType<PlayerNamePicker>();
        if (pnp != null) pnp.enabled = false; // component only: the picker lives ON BattleSystems
        rig = Object.FindAnyObjectByType<OVRCameraRig>();
        return $"prepared dt={frameDt:0.000} rig={(rig != null)}";
    }

    static IEnumerator StartFight()
    {
        Prepare();
        var pc = PlayerCombat.Instance;
        if (pc != null && pc.weaponSets != null && pc.weaponSets.Count > 0) pc.EquipWeaponSet(l3Weapon);
        yield return Frames(3);
        BattleManager.Instance.StartBattleFromInspector();
        yield return Frames(2);
        if (M == null) { log.AppendLine("no monster spawned"); yield break; }
        log.AppendLine($"battle active={BattleManager.Instance.IsBattleActive} monster={M.name} spawnDist={HorizDist(M.transform.position, Head.position):0.000}");
    }

    static bool FightReady()
    {
        var m = M;
        if (m == null || !m.isAlive) return false;
        var e = m.GetComponent<MonsterEntrance>();
        return e == null || e.Complete;
    }

    // ───────────────────────── geometry (analytic, works on disabled colliders) ─────────────────────────
    static readonly List<CapsuleCollider> body = new List<CapsuleCollider>();
    static MonsterController bodyOwner;

    static void CollectBody(MonsterController m)
    {
        if (bodyOwner == m && body.Count > 0) return;
        body.Clear(); bodyOwner = m;
        foreach (var c in m.GetComponentsInChildren<CapsuleCollider>(true))
            if (!c.isTrigger && !c.CompareTag("MonsterWeapon")) body.Add(c);
    }

    static float HorizDist(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }

    /// <summary>Nearest point on a capsule's surface to p (p itself when inside).</summary>
    public static Vector3 CapsuleClosest(CapsuleCollider c, Vector3 p, out bool inside)
    {
        Transform t = c.transform; Vector3 ls = t.lossyScale;
        int dir = c.direction;
        Vector3 axis = dir == 0 ? Vector3.right : dir == 1 ? Vector3.up : Vector3.forward;
        float axisScale = Mathf.Abs(dir == 0 ? ls.x : dir == 1 ? ls.y : ls.z);
        float radScale = dir == 0 ? Mathf.Max(Mathf.Abs(ls.y), Mathf.Abs(ls.z)) : dir == 1 ? Mathf.Max(Mathf.Abs(ls.x), Mathf.Abs(ls.z)) : Mathf.Max(Mathf.Abs(ls.x), Mathf.Abs(ls.y));
        float r = c.radius * radScale;
        float half = Mathf.Max(0f, c.height * axisScale * 0.5f - r);
        Vector3 center = t.TransformPoint(c.center);
        Vector3 a = t.TransformDirection(axis).normalized;
        Vector3 p0 = center - a * half, p1 = center + a * half;
        Vector3 seg = p1 - p0;
        float k = seg.sqrMagnitude > 1e-8f ? Mathf.Clamp01(Vector3.Dot(p - p0, seg) / seg.sqrMagnitude) : 0f;
        Vector3 q = p0 + seg * k;
        Vector3 d = p - q;
        float dm = d.magnitude;
        inside = dm <= r;
        if (inside) return p;
        return q + d / dm * r;
    }

    /// <summary>3-D and horizontal distance from p to the nearest body surface.</summary>
    static float BodyDist(Vector3 p, out float horiz, out string part)
    {
        float best = float.MaxValue; horiz = float.MaxValue; part = "-";
        foreach (var c in body)
        {
            if (c == null) continue;
            Vector3 cp = CapsuleClosest(c, p, out bool inside);
            float d = inside ? 0f : Vector3.Distance(cp, p);
            if (d < best) { best = d; horiz = HorizDist(cp, p); part = c.name; }
        }
        return best;
    }

    static readonly List<Vector3> pts = new List<Vector3>();

    /// <summary>Sample points over a weapon's hit collider (box corners + face centres, sphere/capsule extremes).</summary>
    static void ColliderPoints(Collider col, List<Vector3> into, out float shrink)
    {
        into.Clear(); shrink = 0f;
        var t = col.transform;
        if (col is BoxCollider b)
        {
            Vector3 h = b.size * 0.5f;
            for (int x = -1; x <= 1; x++) for (int y = -1; y <= 1; y++) for (int z = -1; z <= 1; z++)
                into.Add(t.TransformPoint(b.center + Vector3.Scale(h, new Vector3(x, y, z))));
        }
        else if (col is SphereCollider s)
        {
            into.Add(t.TransformPoint(s.center));
            Vector3 ls = t.lossyScale;
            shrink = s.radius * Mathf.Max(Mathf.Abs(ls.x), Mathf.Abs(ls.y), Mathf.Abs(ls.z));
        }
        else if (col is CapsuleCollider c)
        {
            Vector3 axis = c.direction == 0 ? Vector3.right : c.direction == 1 ? Vector3.up : Vector3.forward;
            for (int i = 0; i <= 4; i++) into.Add(t.TransformPoint(c.center + axis * (c.height * (i / 4f - 0.5f))));
            Vector3 ls = t.lossyScale;
            shrink = c.radius * Mathf.Max(Mathf.Abs(ls.x), Mathf.Abs(ls.y), Mathf.Abs(ls.z));
        }
        else into.Add(col.bounds.center);
    }

    static float WeaponBodyDist(Collider col)
    {
        ColliderPoints(col, pts, out float shrink);
        float best = float.MaxValue;
        foreach (var p in pts) best = Mathf.Min(best, BodyDist(p, out _, out _));
        return Mathf.Max(0f, best - shrink);
    }

    static readonly List<HitDetection> weapons = new List<HitDetection>();
    static void CollectWeapons()
    {
        weapons.Clear();
        foreach (var hd in Object.FindObjectsByType<HitDetection>())
            if (hd.isActiveAndEnabled) weapons.Add(hd);
    }

    /// <summary>Distance from the hand anchor to the far end of the weapon's hit collider (weapon length past the grip).</summary>
    static float WeaponExtension(HitDetection hd, Transform hand)
    {
        var col = hd.GetComponent<Collider>();
        ColliderPoints(col, pts, out float shrink);
        float best = 0f;
        foreach (var p in pts) best = Mathf.Max(best, Vector3.Distance(p, hand.position) + shrink);
        return best;
    }

    // Per-window minimum distances (head / hands / weapons to the body; root to head).
    struct Mins
    {
        public float head, headH, hand, weapon, rootH, rootHMax, nearestH, nearestHMax, nearestHSum, vertNearest, vertNearestMax, vertNearestSum, monsterWeapon; public int n; public string headPart; public bool weaponOverlap;
        public void Reset() { head = headH = hand = weapon = rootH = nearestH = vertNearest = monsterWeapon = 99f; rootHMax = nearestHMax = nearestHSum = vertNearestMax = vertNearestSum = 0f; n = 0; headPart = "-"; weaponOverlap = false; }
    }

    static void Sample(ref Mins m)
    {
        var mon = M; if (mon == null || Head == null) return;
        CollectBody(mon);
        float d = BodyDist(Head.position, out float h, out string part);
        if (d < m.head) { m.head = d; m.headPart = part; }
        m.headH = Mathf.Min(m.headH, h);
        // Nearest body surface, horizontal, from the head (the "spacing" number).
        m.nearestH = Mathf.Min(m.nearestH, h); m.nearestHMax = Mathf.Max(m.nearestHMax, h); m.nearestHSum += h; m.n++;
        foreach (var hand in new[] { rig.leftHandAnchor, rig.rightHandAnchor })
            m.hand = Mathf.Min(m.hand, BodyDist(hand.position, out _, out _));
        foreach (var w in weapons)
        {
            if (w == null || !w.isActiveAndEnabled) continue;
            float wd = WeaponBodyDist(w.GetComponent<Collider>());
            m.weapon = Mathf.Min(m.weapon, wd);
            if (wd <= 0.0001f) m.weaponOverlap = true;
        }
        float rh = HorizDist(mon.transform.position, Head.position);
        m.rootH = Mathf.Min(m.rootH, rh); m.rootHMax = Mathf.Max(m.rootHMax, rh);
        // Spacing metric: horizontal distance from the head's vertical line to the nearest body surface (any height).
        float v = VerticalLineNearest(Head.position);
        m.vertNearest = Mathf.Min(m.vertNearest, v); m.vertNearestMax = Mathf.Max(m.vertNearestMax, v); m.vertNearestSum += v;
        // How close the monster's weapons come to the head (does the attack visibly reach?).
        foreach (var c in mon.GetComponentsInChildren<Collider>())
        {
            if (!c.CompareTag("MonsterWeapon")) continue;
            ColliderPoints(c, pts, out float shrink);
            foreach (var p in pts) m.monsterWeapon = Mathf.Min(m.monsterWeapon, Mathf.Max(0f, Vector3.Distance(p, Head.position) - shrink));
        }
    }

    /// <summary>Horizontal distance from the vertical line through p to the nearest body capsule surface.</summary>
    static float VerticalLineNearest(Vector3 p)
    {
        float best = float.MaxValue;
        foreach (var c in body)
        {
            if (c == null) continue;
            Transform t = c.transform; Vector3 ls = t.lossyScale;
            Vector3 axis = c.direction == 0 ? Vector3.right : c.direction == 1 ? Vector3.up : Vector3.forward;
            float axisScale = Mathf.Abs(c.direction == 0 ? ls.x : c.direction == 1 ? ls.y : ls.z);
            float r = c.radius * Mathf.Max(Mathf.Abs(ls.x), Mathf.Abs(ls.y), Mathf.Abs(ls.z));
            float half = Mathf.Max(0f, c.height * axisScale * 0.5f - r);
            Vector3 ctr = t.TransformPoint(c.center), a = t.TransformDirection(axis).normalized;
            Vector2 p0 = new Vector2(ctr.x - a.x * half, ctr.z - a.z * half), p1 = new Vector2(ctr.x + a.x * half, ctr.z + a.z * half), q = new Vector2(p.x, p.z);
            Vector2 seg = p1 - p0;
            float k = seg.sqrMagnitude > 1e-8f ? Mathf.Clamp01(Vector2.Dot(q - p0, seg) / seg.sqrMagnitude) : 0f;
            best = Mathf.Min(best, Mathf.Max(0f, Vector2.Distance(q, p0 + seg * k) - r));
        }
        return best;
    }

    static string Fmt(Mins m) =>
        $"head {m.head:0.000} (horiz {m.headH:0.000}, {m.headPart}) | hands {m.hand:0.000} | weapons {m.weapon:0.000}{(m.weaponOverlap ? " OVERLAP" : "")} | root {m.rootH:0.000}-{m.rootHMax:0.000} | body from head line {m.vertNearest:0.000}-{m.vertNearestMax:0.000} (mean {m.vertNearestSum / Mathf.Max(1, m.n):0.000}) | monster weapon to head {m.monsterWeapon:0.000}";

    // ───────────────────────── reach ─────────────────────────
    /// <summary>Arm reach assumed: hand anchor 0.60 m in front of the head at full extension (measured on 2026-10-04).</summary>
    public static float armReach = 0.60f;

    static string ReachReport()
    {
        var sb = new StringBuilder();
        var pc = PlayerCombat.Instance;
        if (pc == null || rig == null) return "no player";
        var sets = new List<WeaponType>();
        if (pc.weaponSets != null && pc.weaponSets.Count > 0) foreach (var s in pc.weaponSets) sets.Add(s.type);
        else sets.Add(WeaponType.None);
        foreach (var type in sets)
        {
            if (type != WeaponType.None) pc.EquipWeaponSet(type);
            // Any HitDetection under each hand anchor (L1/L2 weapons are inactive before the battle; geometry still reads).
            foreach (var hand in new[] { rig.rightHandAnchor, rig.leftHandAnchor })
            {
                foreach (var hd in hand.GetComponentsInChildren<HitDetection>(true))
                {
                    if (type != WeaponType.None)
                    {
                        var set = pc.FindSet(type);
                        bool mine = set != null && (hd.transform.IsChildOf(set.rightObject.transform) || hd.transform.IsChildOf(set.leftObject.transform));
                        if (!mine) continue;
                    }
                    float ext = WeaponExtension(hd, hand);
                    sb.AppendLine($"  reach {(type == WeaponType.None ? "" : type + " ")}{hd.name} ({hand.name}): weapon past grip {ext:0.000} m -> max reach {armReach + ext:0.000} m from head");
                }
            }
        }
        if (sets[0] != WeaponType.None) pc.EquipWeaponSet(l3Weapon);
        return sb.ToString();
    }

    // ───────────────────────── baseline job ─────────────────────────
    static IEnumerator Baseline()
    {
        Prepare();
        log.AppendLine("== " + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
        log.Append(ReachReport());
        yield return StartFight();
        var mon = M; if (mon == null) yield break;
        CollectBody(mon);
        CollectWeapons();

        // Entrance (L3) / spawn: closest the body gets to the head before the fight starts.
        var mins = new Mins(); mins.Reset();
        float t0 = Time.time;
        while (!FightReady() && Time.time - t0 < 15f) { Sample(ref mins); yield return null; }
        if (mon.GetComponent<MonsterEntrance>() != null)
            log.AppendLine($"entrance ({Time.time - t0:0.00} s): {Fmt(mins)}");
        // Right after the entrance / spawn (monster settling).
        mins.Reset(); yield return SampleFor(2f, () => Sample(ref mins));
        log.AppendLine($"first 2 s of fight: {Fmt(mins)}");

        yield return IdleAndStep();
        var psc = M.GetComponent<MonsterPersonalSpace>(); if (psc != null) psc.ResetStats();
        yield return SweepAll();
        if (psc != null) log.AppendLine($"sweep clamp stats: min head {psc.MinHeadDistance:0.000} (limit {psc.headClearance}), min hand {psc.MinHandDistanceMonsterCaused:0.000} (limit {psc.handClearance}), min weapon {psc.MinWeaponDistanceMonsterCaused:0.000} (limit {psc.weaponClearance}), breaches head/hand/weapon {psc.HeadBreaches}/{psc.HandBreaches}/{psc.WeaponBreaches}, max push {psc.MaxPush:0.000}");
        yield return Perf();
        log.AppendLine($"console errors: see read_console");
    }

    static IEnumerator SweepOnly()
    {
        Prepare();
        if (!FightReady()) { yield return StartFight(); float t0 = Time.time; while (!FightReady() && Time.time - t0 < 15f) yield return null; }
        CollectBody(M); CollectWeapons();
        yield return SweepAll();
    }

    static IEnumerator PerfOnly()
    {
        Prepare();
        if (!FightReady()) { yield return StartFight(); float t0 = Time.time; while (!FightReady() && Time.time - t0 < 15f) yield return null; }
        yield return Perf();
    }

    static IEnumerator SampleFor(float s, System.Action sample)
    {
        float end = Time.time + s;
        while (Time.time < end) { sample(); yield return null; }
    }

    static IEnumerator IdleAndStep()
    {
        var mon = M; var ai = mon.GetComponent<MonsterAI>();
        float cd = mon.attackCooldown;
        bool def = ai.canDefend;
        mon.attackCooldown = 999f; ai.canDefend = false;   // idle only: no attacks, no guard
        yield return WaitIdle(ai);
        var mins = new Mins(); mins.Reset();
        yield return SampleFor(4f, () => { if (AiIdle(ai)) Sample(ref mins); });
        log.AppendLine($"idle 4 s ({mins.n} idle frames): {Fmt(mins)} | nearest body surface (horiz) min {mins.nearestH:0.000} / mean {mins.nearestHSum / Mathf.Max(1, mins.n):0.000} / max {mins.nearestHMax:0.000}");

        // Player steps 0.30 m toward the monster and holds 2 s.
        Vector3 standoff = mon.transform.position;
        Vector3 fwd = mon.transform.position - Head.position; fwd.y = 0f; fwd.Normalize();
        rig.transform.position += fwd * 0.30f;
        mins.Reset();
        yield return SampleFor(2f, () => { if (!mon.isAttacking) Sample(ref mins); });
        log.AppendLine($"player stepped 0.30 m in, 2 s: {Fmt(mins)}");
        rig.transform.position -= fwd * 0.30f;
        yield return Seconds(1.0f);
        mon.transform.position = standoff;   // the clamp pushed it back while the player stood in: restore the stand-off for the sweep
        yield return Frames(3);
        mon.attackCooldown = cd; ai.canDefend = def;
    }

    static bool AiIdle(MonsterAI ai)
    {
        var mon = ai.GetComponent<MonsterController>();
        var busy = (bool)typeof(MonsterAI).GetField("busy", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(ai);
        var an = ai.GetComponent<MonsterAnimator>().animator;
        var st = an.GetCurrentAnimatorStateInfo(0);
        bool idleState = st.IsName("Idle") || st.IsName("IdleEnraged");
        return !busy && !mon.isAttacking && idleState && !an.IsInTransition(0) && !ai.IsDefending;
    }

    static IEnumerator WaitIdle(MonsterAI ai, float timeout = 8f)
    {
        float t0 = Time.time;
        while (!AiIdle(ai) && Time.time - t0 < timeout) yield return null;
    }

    static IEnumerator SweepAll()
    {
        var mon = M; var ai = mon.GetComponent<MonsterAI>();
        var an = mon.GetComponent<MonsterAnimator>().animator;
        Transform hips = an.GetBoneTransform(HumanBodyBones.Hips);
        float cd = mon.attackCooldown;
        bool def = ai.canDefend;
        ai.canDefend = false;
        ai.enabled = false;           // no AI picks / walking during the sweep; attack coroutines still run
        mon.attackCooldown = 0f;
        log.AppendLine("forced attacks (each once, AI selection off, min over every frame):");
        Vector3 home = mon.transform.position;
        int rushIndex = -1;
        for (int i = 0; i < ai.attackTable.Count; i++)
        {
            var a = ai.attackTable[i];
            if (a.rangedKind == MonsterAI.RangedKind.Rush) { rushIndex = i; continue; }
            yield return Attack(ai, an, hips, i, cd, home);
        }
        if (rushIndex >= 0)
        {
            // Rush starts from range: step the player back so the monster is ~3.8 m away, as the AI would choose it.
            Vector3 back = Head.position - mon.transform.position; back.y = 0f; back.Normalize();
            float need = 3.8f - HorizDist(mon.transform.position, Head.position);
            rig.transform.position += back * need;
            yield return Frames(3);
            yield return Attack(ai, an, hips, rushIndex, cd, home);
            rig.transform.position -= back * need;
            mon.transform.position = home;
        }
        mon.attackCooldown = cd; ai.canDefend = def;
        ai.enabled = true;
    }

    static IEnumerator Attack(MonsterAI ai, Animator an, Transform hips, int i, float cd, Vector3 home)
    {
        var mon = ai.GetComponent<MonsterController>();
        var a = ai.attackTable[i];
        yield return WaitIdle(ai);
        Vector3 rootStart = mon.transform.position;
        float startDist = HorizDist(rootStart, Head.position);
        bool ok = ai.DebugForceAttack(i);
        if (!ok) { log.AppendLine($"  [{i}] {a.name}: force FAILED"); yield break; }
        var mins = new Mins(); mins.Reset();
        float maxHipTravel = 0f, maxRootTravel = 0f, t0 = Time.time;
        int hash = Animator.StringToHash(a.animatorState);
        float reached = -1f;
        yield return null;
        while ((ai.CurrentAttack != null || mon.isAttacking) && Time.time - t0 < 12f)
        {
            Sample(ref mins);
            Vector3 hl = mon.transform.InverseTransformPoint(hips.position); hl.y = 0f;
            maxHipTravel = Mathf.Max(maxHipTravel, hl.z);
            maxRootTravel = Mathf.Max(maxRootTravel, HorizDist(mon.transform.position, rootStart));
            if (reached < 0f && (an.GetCurrentAnimatorStateInfo(0).shortNameHash == hash || an.GetNextAnimatorStateInfo(0).shortNameHash == hash)) reached = Time.time - t0;
            yield return null;
        }
        log.AppendLine($"  [{i}] {a.name} (from {startDist:0.00} m): {Fmt(mins)} | hips fwd of root max {maxHipTravel:0.000} | root travel {maxRootTravel:0.000} | anim reached {reached:0.000} s | landed={ai.LastHitLanded}");
        if (a.rangedKind == MonsterAI.RangedKind.None) mon.transform.position = rootStart; // keep the sweep at the same stand-off
    }

    // ───────────────────────── perf ─────────────────────────
    static bool profiling;
    static void EndProfiler() { if (profiling) { ProfilerDriver.enabled = false; profiling = false; } }

    static IEnumerator Perf()
    {
        // Rendering: UnityStats over 60 frames of normal fighting (AI choosing).
        float dc = 0, tri = 0, sp = 0, sh = 0; int n = 0;
        for (int i = 0; i < 60; i++)
        {
            yield return null;
            dc += UnityStats.drawCalls; tri += UnityStats.triangles; sp += UnityStats.setPassCalls; sh += UnityStats.shadowCasters; n++;
        }
        log.AppendLine($"render (mean of {n} frames): drawCalls {dc / n:0.0} | triangles {tri / n:0} | setPass {sp / n:0.0} | shadowCasters {sh / n:0.0}");

        // Scripts: profiler hierarchy, mean of 60 frames.
        ProfilerDriver.ClearAllFrames();
        ProfilerDriver.enabled = true; profiling = true;
        int first = -1;
        yield return Frames(5);
        first = ProfilerDriver.lastFrameIndex;
        yield return Frames(62);
        int last = ProfilerDriver.lastFrameIndex;
        var totals = new Dictionary<string, double>();
        var gc = new Dictionary<string, double>();
        int frames = 0;
        for (int f = Mathf.Max(first, ProfilerDriver.firstFrameIndex); f <= last; f++)
        {
            using (var view = ProfilerDriver.GetHierarchyFrameDataView(f, 0, HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName, HierarchyFrameDataView.columnTotalTime, false))
            {
                if (view == null || !view.valid) continue;
                frames++;
                Walk(view, view.GetRootItemID(), totals, gc, 0);
            }
        }
        EndProfiler();
        var sb = new StringBuilder();
        double scripts = 0;
        foreach (var k in new[] { "Update.ScriptRunBehaviourUpdate", "PreLateUpdate.ScriptRunBehaviourLateUpdate", "FixedUpdate.ScriptRunBehaviourFixedUpdate", "Update.ScriptRunDelayedDynamicFrameRate" })
            if (totals.TryGetValue(k, out double v)) { scripts += v; sb.Append($"{k.Split('.')[1]} {v / frames:0.000} ms  "); }
        log.AppendLine($"scripts (mean of {frames} frames): total {scripts / Mathf.Max(1, frames):0.000} ms | {sb}");
        var list = new List<KeyValuePair<string, double>>();
        foreach (var kv in totals) if (IsGameMarker(kv.Key)) list.Add(kv);
        list.Sort((x, y) => y.Value.CompareTo(x.Value));
        var top = new StringBuilder();
        foreach (var kv in list) top.Append($"{kv.Key} {kv.Value / frames:0.0000} ms ({(gc.TryGetValue(kv.Key, out double g) ? g / frames : 0):0} B)  ");
        log.AppendLine("  per script: " + top);
    }

    static readonly string[] gameScripts = { "MonsterAI", "MonsterController", "MonsterHealth", "MonsterAnimator", "HitDetection", "PlayerCombat", "PlayerHealth",
        "XPManager", "CombatUIController", "SessionReporter", "BattleManager", "RoundManager", "PlayerExertionAudio", "MonsterGlow", "MonsterAxeFlames",
        "MonsterSerpentAura", "BossPhaseController", "MonsterWeakPoint", "ArenaMoodController", "MonsterPersonalSpace", "MonsterDefense", "CombatHaptics", "PlayerLowHealthFeedback", "FlailChainSim", "TwoHandedWeaponIK" };

    static bool IsGameMarker(string name)
    {
        foreach (var s in gameScripts) if (name.StartsWith(s + ".")) return true;
        return false;
    }

    static void Walk(HierarchyFrameDataView view, int id, Dictionary<string, double> totals, Dictionary<string, double> gc, int depth)
    {
        var children = new List<int>();
        view.GetItemChildren(id, children);
        foreach (int c in children)
        {
            string name = view.GetItemName(c);
            double ms = view.GetItemColumnDataAsDouble(c, HierarchyFrameDataView.columnTotalTime);
            double alloc = view.GetItemColumnDataAsDouble(c, HierarchyFrameDataView.columnGcMemory);
            if (name.StartsWith("Update.Script") || name.StartsWith("PreLateUpdate.Script") || name.StartsWith("FixedUpdate.Script") || IsGameMarker(name))
            {
                totals[name] = (totals.TryGetValue(name, out double v) ? v : 0) + ms;
                gc[name] = (gc.TryGetValue(name, out double g) ? g : 0) + alloc;
            }
            if (depth < 6) Walk(view, c, totals, gc, depth + 1);
        }
    }

    // ───────────────────────── simulated fight ─────────────────────────
    /// <summary>Swing speeds (m/s) drawn for the simulated player: the live backend histogram per level (bin centres, counts).</summary>
    static readonly Dictionary<string, float[][]> speedMix = new Dictionary<string, float[][]>
    {
        // Easy: 62 real swings (GET /api/analytics, 2026-10-05); clamped to 9 m/s below.
        { "Easy", new[] { new[] { 1.25f, 3 }, new[] { 1.75f, 5 }, new[] { 2.25f, 7 }, new[] { 2.75f, 1 }, new[] { 3.25f, 2 }, new[] { 3.75f, 3 }, new[] { 4.25f, 4 }, new[] { 4.75f, 7 }, new[] { 5.25f, 2 }, new[] { 5.75f, 2 }, new[] { 6.25f, 2 }, new[] { 6.75f, 3 }, new[] { 7.25f, 1 }, new[] { 7.75f, 3 }, new[] { 8.5f, 19 } } },
        { "Medium", new[] { new[] { 1.25f, 60 }, new[] { 1.75f, 48 }, new[] { 2.25f, 37 }, new[] { 2.75f, 22 }, new[] { 3.25f, 18 }, new[] { 3.75f, 12 }, new[] { 4.25f, 5 }, new[] { 4.75f, 1 }, new[] { 5.25f, 1 } } },
        { "Hard", new[] { new[] { 2.25f, 16 }, new[] { 2.75f, 11 }, new[] { 3.25f, 3 }, new[] { 3.75f, 11 }, new[] { 4.25f, 4 }, new[] { 4.75f, 1 }, new[] { 5.25f, 1 }, new[] { 5.75f, 1 }, new[] { 6.25f, 2 }, new[] { 6.75f, 1 }, new[] { 7.25f, 2 }, new[] { 7.75f, 2 }, new[] { 8.5f, 6 } } },
    };
    public static float swingInterval = 1.3f;
    public static bool simKeepMonsterAlive = true;
    public static bool simForceAllAttacks = true;
    public static bool simNoAttacks = false;   // monster never attacks: measures the guard against an idle monster

    static float DrawSpeed(string level, float gate)
    {
        if (!speedMix.TryGetValue(level, out var mix)) return 4f;
        float total = 0f; foreach (var b in mix) total += b[1];
        float r = Random.value * total;
        foreach (var b in mix) { r -= b[1]; if (r <= 0f) return Mathf.Clamp(b[0] + Random.Range(-0.25f, 0.25f), gate + 0.15f, 9f); }
        return 4f;
    }

    // Event counters (static events, subscribed only while the sim runs).
    static int hitsLanded, hitsDefended, clashes, tooSlow, frontNew, frontOld, attacksStarted;
    static float dmgTaken, dmgDealt;
    static readonly Dictionary<string, int> attackCounts = new Dictionary<string, int>();
    static readonly Dictionary<string, float> attackDelayMax = new Dictionary<string, float>();
    static readonly Dictionary<string, float> attackDelayMin = new Dictionary<string, float>();
    static string pendingAttack; static float pendingAttackT;
    static readonly List<float> tierDamage = new List<float>();

    static void OnAttackStarted(MonsterAI ai, string name, string tier) { attacksStarted++; pendingAttack = name; pendingAttackT = Time.time; attackCounts[name] = (attackCounts.TryGetValue(name, out int c) ? c : 0) + 1; }
    static void OnHit(HitDetection src, float speed, bool defended)
    {
        hitsLanded++; if (defended) hitsDefended++;
        var m = src.LastMonster;
        if (m != null)
        {
            dmgDealt += m.LastDamageTaken;
            if (src.LastWasFrontHit) frontNew++;
            Vector3 d3 = (src.LastHitPoint - m.transform.position).normalized;   // the pre-fix 3-D formula
            if (Vector3.Dot(m.transform.forward, d3) > 0.35f) frontOld++;
        }
    }
    static void OnClash(HitDetection src, float speed) { clashes++; }
    static void OnSlow(HitDetection src, float speed, float min) { tooSlow++; }
    static readonly Dictionary<string, float> dmgByAttack = new Dictionary<string, float>();
    static MonsterAI simAi;
    static void OnDamage(float amount, bool blocking, float hp)
    {
        dmgTaken += amount;
        string n = simAi != null && simAi.CurrentAttack != null ? simAi.CurrentAttack.name : "(thrown/other)";
        dmgByAttack[n] = (dmgByAttack.TryGetValue(n, out float v) ? v : 0f) + amount;
    }

    static void Subscribe(bool on)
    {
        MonsterAI.AttackStarted -= OnAttackStarted; HitDetection.HitLanded -= OnHit; HitDetection.WeaponClashed -= OnClash;
        HitDetection.SwingTooSlow -= OnSlow; PlayerHealth.DamageTaken -= OnDamage;
        if (!on) return;
        MonsterAI.AttackStarted += OnAttackStarted; HitDetection.HitLanded += OnHit; HitDetection.WeaponClashed += OnClash;
        HitDetection.SwingTooSlow += OnSlow; PlayerHealth.DamageTaken += OnDamage;
    }

    struct Swing { public Transform root; public Vector3 restLocal; public Quaternion restRot; public Vector3 offset; public Vector3 a, b; public float t0, dur; public int phase; }

    static IEnumerator SimFight(float seconds)
    {
        Prepare();
        string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        string level = LevelManager.Instance != null ? LevelManager.Instance.sceneDifficulty.ToString() : "Easy";
        log.AppendLine($"== SIM {scene} ({level}) {seconds:0} s, swing every {swingInterval:0.0} s, speeds from the live {level} histogram" + (PlayerCombat.Instance.weaponSets.Count > 0 ? $", weapon {l3Weapon}" : ""));
        if (!FightReady()) { yield return StartFight(); float w0 = Time.time; while (!FightReady() && Time.time - w0 < 15f) yield return null; }
        var mon = M; if (mon == null) yield break;
        var ai = mon.GetComponent<MonsterAI>();
        var def = mon.GetComponent<MonsterDefense>();
        var ps = mon.GetComponent<MonsterPersonalSpace>();
        var phases = mon.GetComponent<BossPhaseController>();
        var health = mon.GetComponent<MonsterHealth>();
        var an = mon.GetComponent<MonsterAnimator>().animator;
        var pc = PlayerCombat.Instance;
        CollectBody(mon); CollectWeapons();
        if (simNoAttacks) { mon.attackCooldown = 9999f; simForceAllAttacks = false; }
        if (def != null) def.ResetStats();
        if (ps != null) ps.ResetStats();
        hitsLanded = hitsDefended = clashes = tooSlow = frontNew = frontOld = attacksStarted = 0; dmgTaken = dmgDealt = 0f;
        attackCounts.Clear(); attackDelayMax.Clear(); attackDelayMin.Clear(); pendingAttack = null;
        dmgByAttack.Clear(); simAi = ai;
        Subscribe(true);
        var used = new HashSet<string>();
        float tStart = Time.time, nextSwing = Time.time + 0.5f, defendTime = 0f, maxFreeYawRate = 0f, maxAttackYaw = 0f;
        int hand = 0, dirToggle = 0, frames = 0, heals = 0, rangedTests = 0, sidestepTests = 0, sidestepDodged = 0;
        float lastYaw = mon.transform.eulerAngles.y; bool wasAttacking = false; float attackYaw0 = 0f;
        var mins = new Mins(); mins.Reset();
        int guardsSeen = 0; bool wasDef = false; float guardRiseMin = 99f, guardRiseMax = 0f; int lastIncoming = 0; float lastIncomingT = -1f;
        float hpStart = health.currentHealth;
        var sw = new Swing { phase = 0 };
        int phaseShiftResist = 0;
        while (Time.time - tStart < seconds && mon != null && mon.isAlive)
        {
            frames++;
            float dt = Time.deltaTime;
            // keep both sides alive for the full window
            if (PlayerHealth.Instance.currentHealth < PlayerHealth.Instance.maxHealth * 0.5f) { PlayerHealth.Instance.FullHeal(); heals++; }
            if (simKeepMonsterAlive && health.currentHealth < health.maxHealth * 0.12f) health.currentHealth = health.maxHealth * 0.45f;

            // turn-rate bookkeeping
            float yaw = mon.transform.eulerAngles.y;
            float dyaw = Mathf.Abs(Mathf.DeltaAngle(lastYaw, yaw)); lastYaw = yaw;
            if (mon.isAttacking) { if (!wasAttacking) attackYaw0 = yaw; maxAttackYaw = Mathf.Max(maxAttackYaw, Mathf.Abs(Mathf.DeltaAngle(attackYaw0, yaw))); }
            else if (dt > 0f) maxFreeYawRate = Mathf.Max(maxFreeYawRate, dyaw / dt);
            wasAttacking = mon.isAttacking;

            // trigger -> animation delay
            if (pendingAttack != null && ai.CurrentAttack != null)
            {
                int hash = Animator.StringToHash(ai.CurrentAttack.animatorState);
                int first = ai.CurrentAttack.animatorState.EndsWith("_Strike") ? Animator.StringToHash(ai.CurrentAttack.animatorState.Replace("_Strike", "_Raise")) : hash;
                if (an.GetCurrentAnimatorStateInfo(0).shortNameHash == first || an.GetNextAnimatorStateInfo(0).shortNameHash == first)
                {
                    float d = Time.time - pendingAttackT;
                    attackDelayMax[pendingAttack] = Mathf.Max(attackDelayMax.TryGetValue(pendingAttack, out float x) ? x : 0f, d);
                    attackDelayMin[pendingAttack] = Mathf.Min(attackDelayMin.TryGetValue(pendingAttack, out float y) ? y : 99f, d);
                    pendingAttack = null;
                }
            }

            // guard reaction timing (detection -> guard up)
            if (def != null && def.IncomingSwings != lastIncoming) { lastIncoming = def.IncomingSwings; lastIncomingT = Time.time; }
            if (ai.IsDefending) defendTime += dt;
            if (ai.IsDefending && !wasDef && ai.DefendIsReactive && lastIncomingT > 0f)
            {
                guardsSeen++; float r = Time.time - lastIncomingT; guardRiseMin = Mathf.Min(guardRiseMin, r); guardRiseMax = Mathf.Max(guardRiseMax, r);
            }
            wasDef = ai.IsDefending;
            if (phases != null && phases.InPhaseShift) phaseShiftResist++;

            Sample(ref mins);

            // force every table attack at least once (melee in place; ranged rows from range)
            if (simForceAllAttacks && sw.phase == 0 && AiIdle(ai) && mon.CanAttack() && Time.time - tStart > 3f && !(phases != null && phases.InPhaseShift))
            {
                for (int i = 0; i < ai.attackTable.Count; i++)
                {
                    var a = ai.attackTable[i];
                    if (used.Contains(a.name) || attackCounts.ContainsKey(a.name)) { used.Add(a.name); continue; }
                    if (a.maxHpFraction < 1f && health.Normalized > a.maxHpFraction) continue; // phase-gated: wait for it
                    if (a.rangedKind != MonsterAI.RangedKind.None)
                    {
                        yield return RangedTest(ai, i, a.rangedKind == MonsterAI.RangedKind.Rush);
                        rangedTests++;
                        if (a.rangedKind == MonsterAI.RangedKind.Rush) { sidestepTests++; if (!ai.LastHitLanded) sidestepDodged++; }
                    }
                    else ai.DebugForceAttack(i);
                    used.Add(a.name);
                    break;
                }
            }

            // player swings
            if (sw.phase == 0 && Time.time >= nextSwing)
            {
                Transform root = hand == 0 ? pc.weaponTransform : pc.offhandWeaponTransform;
                if (root == null) root = pc.weaponTransform;
                var hd = root.GetComponentInChildren<HitDetection>();
                var col = hd.GetComponent<Collider>();
                Vector3 target = def != null && def.guardTarget != null ? def.guardTarget.position : mon.transform.position + Vector3.up * 1.3f;
                Vector3 inward = target - Head.position; inward.y = 0f; inward.Normalize();
                // aim at the front surface of the chest, 0.1 m in
                CollectBody(mon);
                Vector3 b = target - inward * 0.15f;
                bool overhead = (dirToggle++ % 2) == 0;
                Vector3 side = Vector3.Cross(Vector3.up, inward) * (hand == 0 ? 1f : -1f);
                Vector3 a0 = b - inward * 0.75f + (overhead ? Vector3.up * 0.55f : side * 0.6f);
                float speed = DrawSpeed(level, pc.minSwingVelocity);
                sw = new Swing { root = root, restLocal = root.localPosition, restRot = root.localRotation, offset = col.bounds.center - root.position, a = a0, b = b, t0 = Time.time, dur = Vector3.Distance(a0, b) / speed, phase = 1 };
                root.position = a0 - sw.offset; // wind-back (teleport while out of reach; the next frame starts the swing)
                hand = 1 - hand;
                nextSwing = Time.time + swingInterval;
            }
            else if (sw.phase == 1)
            {
                float k = Mathf.Clamp01((Time.time - sw.t0) / Mathf.Max(0.02f, sw.dur));
                sw.root.position = Vector3.Lerp(sw.a, sw.b, k) - sw.offset;
                if (k >= 1f) { sw.phase = 2; sw.t0 = Time.time; }
            }
            else if (sw.phase == 2)
            {
                // slow return to the hand (below the speed gate: never a second hit)
                Vector3 rest = sw.root.parent.TransformPoint(sw.restLocal);
                sw.root.position = Vector3.MoveTowards(sw.root.position, rest, 0.9f * dt);
                if (Vector3.Distance(sw.root.position, rest) < 0.01f || Time.time - sw.t0 > 1.2f)
                {
                    sw.root.localPosition = sw.restLocal; sw.root.localRotation = sw.restRot; sw.phase = 0;
                }
            }
            yield return null;
        }
        Subscribe(false);
        if (sw.root != null) { sw.root.localPosition = sw.restLocal; sw.root.localRotation = sw.restRot; }

        float fight = Time.time - tStart;
        log.AppendLine($"fight {fight:0.0} s, {frames} frames, player healed {heals}x (kept alive)");
        log.AppendLine($"player hits: landed {hitsLanded} ({hitsLanded / fight:0.00}/s), of which body-blocked {hitsDefended}; weapon clashes {clashes}; too slow {tooSlow}; damage dealt {dmgDealt:0}");
        log.AppendLine($"front-hit bonus: fired on {frontNew}/{hitsLanded} hits with the horizontal fix; the old 3-D formula would have fired on {frontOld}/{hitsLanded}");
        if (def != null)
            log.AppendLine($"guards: {def.ReactiveGuards + def.TimedGuards} (reactive {def.ReactiveGuards}, timed {def.TimedGuards}; high {def.HighGuards}, side {def.SideGuards}) from {def.IncomingSwings} incoming swings / {def.GuardRolls} rolls | uptime {def.Uptime * 100f:0.0}% (component) / {defendTime / fight * 100f:0.0}% (measured) | guard breaks {def.GuardBreaks} | flinches {def.Flinches} | detection->guard {(guardsSeen > 0 ? $"{guardRiseMin:0.00}-{guardRiseMax:0.00} s" : "-")}");
        if (ps != null)
            log.AppendLine($"personal space: min head {ps.MinHeadDistance:0.000} m (limit {ps.headClearance}), min hand (monster-caused) {ps.MinHandDistanceMonsterCaused:0.000} m (limit {ps.handClearance}), min player-weapon (monster-caused) {ps.MinWeaponDistanceMonsterCaused:0.000} m (limit {ps.weaponClearance}), breaches head {ps.HeadBreaches} / hand {ps.HandBreaches} / weapon {ps.WeaponBreaches}, max push {ps.MaxPush:0.000} m/frame, min monster-weapon-to-head {ps.MinWeaponHeadDistance:0.000} m (clearance {ps.weaponHeadClearance})");
        log.AppendLine($"independent sampler: {Fmt(mins)}");
        log.AppendLine($"turning: max free turn rate {maxFreeYawRate:0} deg/s (cap {(def != null ? def.turnSpeed : 0):0}), max yaw change inside one attack {maxAttackYaw:0.0} deg");
        if (phases != null) log.AppendLine($"phases: phase {phases.Phase}, phase shifts {phases.PhaseShifts}, resisted hits {phases.ResistedHits}, enraged {phases.IsEnraged}");
        log.AppendLine($"monster: capped hits {mon.CappedHits}, HP {hpStart:0} -> {health.currentHealth:0}; player damage taken {dmgTaken:0}");
        log.AppendLine($"ranged/rush tests {rangedTests}, rush sidestep dodged {sidestepDodged}/{sidestepTests}");
        var sb = new StringBuilder();
        foreach (var kv in attackCounts)
            sb.Append($"{kv.Key} x{kv.Value} [{(attackDelayMin.TryGetValue(kv.Key, out float mn) ? mn : -1):0.000}-{(attackDelayMax.TryGetValue(kv.Key, out float mx) ? mx : -1):0.000} s]; ");
        log.AppendLine($"attacks {attacksStarted}: {sb}");
        var dsb = new StringBuilder();
        foreach (var kv in dmgByAttack) dsb.Append($"{kv.Key} {kv.Value:0}; ");
        log.AppendLine($"player damage taken by attack (total {dmgTaken:0}, {dmgTaken / fight:0.0} HP/s): {dsb}");
        var missing = new StringBuilder();
        foreach (var a in ai.attackTable) if (!attackCounts.ContainsKey(a.name)) missing.Append(a.name + "; ");
        log.AppendLine("attacks never seen: " + (missing.Length > 0 ? missing.ToString() : "none"));
    }

    static IEnumerator WindUpTest()
    {
        Prepare();
        if (!FightReady()) { yield return StartFight(); float w0 = Time.time; while (!FightReady() && Time.time - w0 < 15f) yield return null; }
        var mon = M; var ai = mon.GetComponent<MonsterAI>(); var an = mon.GetComponent<MonsterAnimator>().animator;
        var def = mon.GetComponent<MonsterDefense>();
        ai.canDefend = false; def.guardChance = 0f; def.timedGuard = false;
        ai.enabled = false; mon.attackCooldown = 0f;
        float hp0 = mon.GetComponent<MonsterHealth>().currentHealth;
        for (int i = 0; i < ai.attackTable.Count; i++)
        {
            var a = ai.attackTable[i];
            if (a.rangedKind != MonsterAI.RangedKind.None) continue;
            yield return WaitIdle(ai);
            if (!ai.DebugForceAttack(i)) { log.AppendLine($"  {a.name}: force failed"); continue; }
            yield return Seconds(Mathf.Min(0.25f, a.windUp * 0.5f));
            int stateBefore = an.GetCurrentAnimatorStateInfo(0).shortNameHash;
            bool inWind = ai.CurrentAttack != null;
            float before = mon.GetComponent<MonsterHealth>().currentHealth;
            mon.OnTakeDamage(25f, mon.transform.position + Vector3.up * 1.3f, 9f, false);   // full-tier hit mid wind-up
            yield return null; yield return null;
            int stateAfter = an.GetCurrentAnimatorStateInfo(0).shortNameHash;
            bool hitState = an.GetCurrentAnimatorStateInfo(0).IsName("Hit") || an.GetNextAnimatorStateInfo(0).IsName("Hit");
            bool damaged = mon.GetComponent<MonsterHealth>().currentHealth < before;
            float t0 = Time.time;
            while ((ai.CurrentAttack != null || mon.isAttacking) && Time.time - t0 < 10f) yield return null;
            log.AppendLine($"  {a.name}: wind-up {inWind}, state same {stateBefore == stateAfter}, Hit state entered {hitState}, damage applied {damaged}, attack still resolved (landed={ai.LastHitLanded})");
        }
        mon.GetComponent<MonsterHealth>().currentHealth = Mathf.Max(hp0, 1f);
        ai.enabled = true;
    }

    static IEnumerator RushTest()
    {
        Prepare();
        if (!FightReady()) { yield return StartFight(); float w0 = Time.time; while (!FightReady() && Time.time - w0 < 15f) yield return null; }
        var mon = M; var ai = mon.GetComponent<MonsterAI>();
        mon.GetComponent<MonsterDefense>().guardChance = 0f; ai.canDefend = false;
        int idx = ai.attackTable.FindIndex(a => a.rangedKind == MonsterAI.RangedKind.Rush);
        if (idx < 0) { log.AppendLine("no rush row"); yield break; }
        ai.enabled = false; mon.attackCooldown = 0f;
        CollectBody(mon);
        for (int trial = 0; trial < 6; trial++)
        {
            bool side = trial % 2 == 1;
            yield return WaitIdle(ai);
            Vector3 home = mon.transform.position;
            Vector3 back = Head.position - mon.transform.position; back.y = 0f; back.Normalize();
            float need = 3.8f - HorizDist(mon.transform.position, Head.position);
            rig.transform.position += back * need;
            yield return Frames(3);
            float yaw0 = mon.transform.eulerAngles.y;
            ai.DebugForceAttack(idx);
            Vector3 sideStep = Vector3.Cross(Vector3.up, back) * 1.3f;
            if (side) { yield return Seconds(0.3f); rig.transform.position += sideStep; }
            var mins = new Mins(); mins.Reset();
            float t0 = Time.time;
            while ((ai.CurrentAttack != null || mon.isAttacking) && Time.time - t0 < 10f) { Sample(ref mins); yield return null; }
            log.AppendLine($"  rush trial {trial} {(side ? "SIDESTEP 1.3 m at 0.3 s" : "stood still       ")}: landed={ai.LastHitLanded} rushed {ai.LastRushDistance:0.00} m, yaw change {Mathf.Abs(Mathf.DeltaAngle(yaw0, mon.transform.eulerAngles.y)):0.0} deg, min head {mins.head:0.000} m, min body-from-head-line {mins.vertNearest:0.000} m");
            if (side) rig.transform.position -= sideStep;
            rig.transform.position -= back * need;
            mon.transform.position = home;
            yield return Seconds(0.5f);
        }
        ai.enabled = true;
    }

    /// <summary>Ranged row: step the player back to 3.6 m, force it, and for a Rush sidestep 1.3 m once the wind-up starts.</summary>
    static IEnumerator RangedTest(MonsterAI ai, int index, bool sidestep)
    {
        var mon = ai.GetComponent<MonsterController>();
        Vector3 home = mon.transform.position;
        Vector3 back = Head.position - mon.transform.position; back.y = 0f; back.Normalize();
        float need = 3.6f - HorizDist(mon.transform.position, Head.position);
        rig.transform.position += back * need;
        yield return Frames(2);
        if (!ai.DebugForceAttack(index)) { rig.transform.position -= back * need; yield break; }
        if (sidestep)
        {
            yield return Seconds(0.25f);
            rig.transform.position += Vector3.Cross(Vector3.up, back) * 1.3f;
        }
        float t0 = Time.time;
        while ((ai.CurrentAttack != null || mon.isAttacking) && Time.time - t0 < 10f) yield return null;
        if (sidestep) rig.transform.position -= Vector3.Cross(Vector3.up, back) * 1.3f;
        rig.transform.position -= back * need;
        mon.transform.position = home;
        yield return Frames(2);
    }
}
