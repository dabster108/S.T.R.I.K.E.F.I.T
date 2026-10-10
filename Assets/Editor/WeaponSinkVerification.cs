// WeaponSinkVerification.cs
// Purpose: Play-mode measurement of how far the player's weapons sink into the monster (Editor only, never in a build).
//          Jobs: "sink:before" / "sink:after"  (proxy off / on) sweep every equipped weapon of the open level through the frozen
//                monster's head, torso, legs and weapon at 3, 8, 15 and 25 m/s, thrust (tip first) and flat-on, 72 Hz frames.
//                Depths come from Physics.ComputePenetration against EVERY monster collider at explicit poses, independent of the
//                proxy's own bookkeeping: the controller pose (the logical weapon) and the drawn pose (controller + proxy offset).
//                "cost"  per-frame cost of the proxy (profiler marker) and GC with the proxy on / off during swings.
//                "reach" standing reach of each weapon against the monster's nearest surface at the fight's stand-off.
// Dependencies: WeaponSurfaceProxy, PlayerCombat, HitDetection, BattleManager, CombatVerification.Prepare
// Attach to: nothing (static, driven over MCP)

using System.Collections;
using System.Collections.Generic;
using System.Text;
using Unity.Profiling;
using UnityEditor;
using UnityEngine;

public static class WeaponSinkVerification
{
    static IEnumerator job;
    static readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
    static readonly StringBuilder log = new StringBuilder();
    static int lastFrame = -1;
    public static float[] speeds = { 3f, 8f, 15f, 25f };
    public static string[] targetNames = { "head", "torso", "legs", "weapon" };

    public static string Status() => (job != null ? $"RUNNING {Time.time:0.0}\n" : "IDLE\n") + log;

    public static string Run(string name)
    {
        if (!EditorApplication.isPlaying) return "not playing";
        Stop(); log.Length = 0;
        if (name == "sink:before") job = Sink(false);
        else if (name == "sink:after") job = Sink(true);
        else if (name.StartsWith("trace:")) job = Trace(name);
        else if (name == "drift") job = Drift();
        else if (name == "cost") job = Cost();
        else if (name == "reach") job = Reach();
        else if (name == "look") job = Look();
        else return "unknown job " + name;
        EditorApplication.update += Tick;
        return "started " + name;
    }

    public static void Stop()
    {
        job = null; stack.Clear(); EditorApplication.update -= Tick;
        HitDetection.HitLanded -= OnHit; HitDetection.WeaponClashed -= OnClash;
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

    static int hits, clashes;
    static readonly StringBuilder contactLog = new StringBuilder();   // who touched what, when (printed when a sweep both hits and clashes)
    static void OnHit(HitDetection s, float v, bool d) { hits++; contactLog.Append($" H:{s.name}@f{Time.frameCount}"); }
    static void OnClash(HitDetection s, float v) { clashes++; contactLog.Append($" C:{s.name}->{(s.LastClashWeapon != null ? s.LastClashWeapon.name : "?")}@f{Time.frameCount}"); }

    // ───────────────────────── setup ─────────────────────────
    static MonsterController M => BattleManager.Instance != null ? BattleManager.Instance.ActiveMonster : null;

    static void HideNameBoard()
    {
        var pnp = Object.FindAnyObjectByType<PlayerNamePicker>(FindObjectsInactive.Include);
        var f = typeof(PlayerNamePicker).GetField("panel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var panel = pnp != null && f != null ? f.GetValue(pnp) as GameObject : null;
        if (panel != null) panel.SetActive(false);
    }

    static IEnumerator StartFight(WeaponType l3)
    {
        CombatVerification.frameDt = 1f / 72f;
        CombatVerification.Prepare();
        HideNameBoard();
        var pc = PlayerCombat.Instance;
        if (pc != null && pc.weaponSets != null && pc.weaponSets.Count > 0) pc.EquipWeaponSet(l3);
        yield return Frames(3);
        if (!BattleManager.Instance.IsBattleActive) BattleManager.Instance.StartBattleFromInspector();
        float t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < 25f)
        {
            var m = M;
            if (m != null && m.isAlive && (m.GetComponent<MonsterEntrance>() == null || m.GetComponent<MonsterEntrance>().Complete)) break;
            yield return null;
        }
        yield return Frames(10);
    }

    static void Freeze(MonsterController m)
    {
        var ai = m.GetComponent<MonsterAI>(); if (ai != null) ai.enabled = false;
        var def = m.GetComponent<MonsterDefense>(); if (def != null) def.enabled = false;
        var ps = m.GetComponent<MonsterPersonalSpace>(); if (ps != null) ps.enabled = false;
        var shake = m.GetComponent<MonsterHitShake>(); if (shake != null) shake.enabled = false;
        var fb = Object.FindAnyObjectByType<CombatFeedback>(); if (fb != null) fb.hitStop = false;   // its 70 ms hit-stop restores the animator speed afterwards
        Pin(m);
    }

    // Idle pose, animator held: called before every sweep (a hit triggers the Hit state; a pinned pose keeps runs comparable).
    static void Pin(MonsterController m)
    {
        var an = m.GetComponentInChildren<Animator>();
        if (an == null) return;
        an.speed = 1f;
        an.Play("Idle", 0, 0.25f); an.Update(0f);
        an.speed = 0f;
    }

    class Collected { public List<Collider> body = new List<Collider>(); public List<Collider> weapon = new List<Collider>(); }

    static Collected CollectMonsterColliders(MonsterController m)
    {
        var c = new Collected();
        foreach (var col in m.GetComponentsInChildren<Collider>(true))
        {
            if (col.transform == m.transform) continue;
            if (!(col is BoxCollider || col is SphereCollider || col is CapsuleCollider)) continue;
            if (col.CompareTag("MonsterWeapon")) c.weapon.Add(col); else c.body.Add(col);
        }
        return c;
    }

    // Deepest overlap of the weapon collider placed at (pos, rot) with a set of monster colliders.
    static float Depth(Collider weaponCol, Vector3 pos, Quaternion rot, List<Collider> set)
    {
        float best = 0f;
        foreach (var c in set)
        {
            if (Physics.ComputePenetration(weaponCol, pos, rot, c, c.transform.position, c.transform.rotation, out Vector3 d, out float dist) && dist > best) best = dist;
        }
        return best;
    }

    static Vector3 ShapeCentre(Collider col, Transform t, Vector3 pos, Quaternion rot)
    {
        Vector3 c = Vector3.zero;
        if (col is BoxCollider b) c = b.center; else if (col is SphereCollider s) c = s.center; else if (col is CapsuleCollider cp) c = cp.center;
        return pos + rot * Vector3.Scale(c, t.lossyScale);
    }

    static Vector3 TargetPoint(MonsterController m, string name, Collected cols)
    {
        var an = m.GetComponentInChildren<Animator>();
        Transform bone = null;
        if (name == "head") bone = an.GetBoneTransform(HumanBodyBones.Head);
        else if (name == "torso") bone = an.GetBoneTransform(HumanBodyBones.UpperChest) ?? an.GetBoneTransform(HumanBodyBones.Chest);
        else if (name == "legs") bone = an.GetBoneTransform(HumanBodyBones.RightUpperLeg);
        if (bone != null) return bone.position;
        if (name == "weapon" && cols.weapon.Count > 0)
        {
            // the monster weapon collider nearest the player (the surface the player would meet first)
            Collider best = null; float bd = 1e9f;
            foreach (var w in cols.weapon) { float d = (w.transform.position - Vector3.zero).sqrMagnitude; if (d < bd) { bd = d; best = w; } }
            return ColliderCentre(best);
        }
        return m.transform.position + Vector3.up * 1.2f;
    }

    static Vector3 ColliderCentre(Collider c)
    {
        Vector3 ctr = Vector3.zero;
        if (c is BoxCollider b) ctr = b.center; else if (c is SphereCollider s) ctr = s.center; else if (c is CapsuleCollider cp) ctr = cp.center;
        return c.transform.TransformPoint(ctr);
    }

    // ───────────────────────── the sweep ─────────────────────────
    class Result
    {
        public float trueBody, trueWeapon, drawnBody, drawnWeapon, maxOffset;
        public int hits, clashes, ticks; public float dmg, vmax; public bool ghost; public string contacts = ""; public StringBuilder dbg = new StringBuilder();
    }

    static IEnumerator SweepOne(Transform root, HitDetection hd, Collider col, WeaponSurfaceProxy proxy, bool flail, Vector3 target, float speed, bool thrust, Collected cols, Result res, float followThrough)
    {
        var rig = Object.FindAnyObjectByType<OVRCameraRig>();
        Vector3 head = rig.centerEyeAnchor.position;
        Vector3 dir = target - head; dir.y = 0f; dir.Normalize();
        Vector3 restLocal = root.localPosition; Quaternion restLocalRot = root.localRotation;
        Quaternion baseRot = root.rotation;
        Pin(M);
        Quaternion rot = thrust ? Quaternion.LookRotation(dir, Vector3.up) : Quaternion.LookRotation(Quaternion.Euler(0f, 90f, 0f) * dir, Vector3.up);
        // the collider centre must travel through the target
        float lead = flail ? 0.4f : 0f;                     // the flail ball trails its anchor
        Vector3 centreOffset = ShapeCentre(col, col.transform, Vector3.zero, rot);   // from the collider's transform to its centre
        Transform driven = root;
        Vector3 start = target - dir * 1.6f, end = target + dir * (0.45f + lead);
        float length = Vector3.Distance(start, end);
        float dt = 1f / 72f;
        // start below every speed gate: walk to the start pose slowly
        for (int i = 0; i < 12; i++) { driven.position = Vector3.Lerp(driven.position, start - centreOffset + dir * lead, 0.4f); driven.rotation = rot; yield return null; }
        driven.position = start - centreOffset + dir * lead; driven.rotation = rot;
        yield return Frames(6);
        res.hits = res.clashes = 0; hits = clashes = 0; contactLog.Length = 0;
        var mh = M != null ? M.GetComponent<MonsterHealth>() : null; float hp0 = mh != null ? mh.currentHealth : 0f;
        HitDetection.HitLanded += OnHit; HitDetection.WeaponClashed += OnClash;
        // followThrough >= 0: the controller keeps moving this far (m) past the first frame that touches any monster collider,
        // like the follow-through of a real swing; < 0: a fixed pass through the target centre and 0.45 m beyond (stress).
        if (followThrough >= 0f)
        {
            // True first contact along the straight path (1 cm steps, collider placed at explicit poses), then exactly followThrough past it.
            float s = 0f, total = Vector3.Distance(start, target + dir * 1.2f), contactS = total;
            for (; s < total; s += 0.01f)
            {
                Vector3 pos = start + dir * s - centreOffset + dir * lead;
                if (Mathf.Max(Depth(col, pos, rot, cols.body), Depth(col, pos, rot, cols.weapon)) > 0.002f) { contactS = s; break; }
            }
            end = start + dir * (contactS + followThrough) + dir * lead * 0f;
        }
        length = Vector3.Distance(start, end);
        int frames = Mathf.CeilToInt(length / (speed * dt));
        int ticks0 = proxy != null ? proxy.Ticks : 0;
        int stopAt = int.MaxValue; bool touched = false;
        for (int f = 0; f <= frames + 6 && f <= stopAt; f++)
        {
            float k = Mathf.Clamp01(f / (float)frames);
            driven.position = Vector3.Lerp(start, end, k) - centreOffset + dir * lead;
            driven.rotation = rot;
            var anp = M != null ? M.GetComponentInChildren<Animator>() : null; if (anp != null) anp.speed = 0f;
            yield return null;
            // measure what the previous frame's LateUpdate produced for this pose
            var weaponCol = col;
            Vector3 P = col.transform.position; Quaternion R = col.transform.rotation;
            Vector3 off = proxy != null && proxy.proxyEnabled ? proxy.VisualOffsetWorld : Vector3.zero;
            res.trueBody = Mathf.Max(res.trueBody, Depth(weaponCol, P, R, cols.body));
            res.trueWeapon = Mathf.Max(res.trueWeapon, Depth(weaponCol, P, R, cols.weapon));
            res.drawnBody = Mathf.Max(res.drawnBody, Depth(weaponCol, P + off, R, cols.body));
            res.drawnWeapon = Mathf.Max(res.drawnWeapon, Depth(weaponCol, P + off, R, cols.weapon));
            res.maxOffset = Mathf.Max(res.maxOffset, off.magnitude);
            res.vmax = Mathf.Max(res.vmax, hd.CurrentVelocity.magnitude);

            if (proxy != null && proxy.GhostShown) res.ghost = true;
            float dd = Depth(weaponCol, P + off, R, cols.body);
            res.dbg.AppendLine($"       f{f,3} P {P.ToString("F2")} ctrl {Depth(weaponCol, P, R, cols.body) * 100f,5:0.0}/{Depth(weaponCol, P, R, cols.weapon) * 100f,4:0.0} off {off.magnitude * 100f,5:0.0} {off.normalized.ToString("F2")} drawn {dd * 100f,5:0.0} v {hd.CurrentVelocity.ToString("F1")} appr {(proxy != null ? proxy.LastApproachUsed.ToString("F2") : "")} sink {(proxy != null ? proxy.TrueSink * 100f : 0f):0.0}");
        }
        HitDetection.HitLanded -= OnHit; HitDetection.WeaponClashed -= OnClash;
        res.hits = hits; res.clashes = clashes; res.ticks = proxy != null ? proxy.Ticks - ticks0 : 0;
        res.contacts = contactLog.ToString();
        res.dmg = mh != null ? hp0 - mh.currentHealth : 0f;
        // back to the hand, slowly (below every speed gate)
        Vector3 rest = root.parent.TransformPoint(restLocal);
        for (int i = 0; i < 40 && Vector3.Distance(root.position, rest) > 0.02f; i++) { root.position = Vector3.MoveTowards(root.position, rest, 0.02f); root.rotation = Quaternion.Slerp(root.rotation, baseRot, 0.3f); yield return null; }
        root.localPosition = restLocal; root.localRotation = restLocalRot;
        yield return Frames(8);
    }

    public static bool dumpBad = false;
    static readonly StringBuilder dbgBad = new StringBuilder();
    public static float followThrough = 0.30f;   // m past first contact; < 0 = the fixed stress pass

    static IEnumerator Sink(bool withProxy)
    {
        string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        var weapons = new List<WeaponType>();
        var pc0 = PlayerCombat.Instance;
        if (pc0.weaponSets != null && pc0.weaponSets.Count > 0) { weapons.Add(WeaponType.Mace); weapons.Add(WeaponType.Flail); weapons.Add(WeaponType.Katana); }
        else weapons.Add(WeaponType.None);   // L1 / L2: the loadout the scene already has
        log.AppendLine($"== SINK {scene} proxy {(withProxy ? "ON" : "OFF")}, {(followThrough >= 0f ? $"controller follows through {followThrough * 100f:0} cm past first contact" : "stress pass: through the target centre and 45 cm beyond")}: speeds {string.Join("/", speeds)} m/s, thrust + flat-on, depth in cm (max over the sweep) of the CONTROLLER pose and the DRAWN pose into monster body / monster weapon colliders");
        foreach (var wt in weapons)
        {
            if (M != null && BattleManager.Instance.IsBattleActive && wt != WeaponType.None) { }
            yield return StartFight(wt == WeaponType.None ? WeaponType.Mace : wt);
            var m = M; if (m == null) { log.AppendLine("no monster"); yield break; }
            Freeze(m);
            var pc = PlayerCombat.Instance;
            var root = pc.weaponTransform;
            var hd = root.GetComponentInChildren<HitDetection>(true);
            var col = hd.GetComponent<Collider>();
            var proxy = hd.GetComponent<WeaponSurfaceProxy>();
            foreach (var p in pc.GetComponentsInChildren<WeaponSurfaceProxy>(true)) p.proxyEnabled = withProxy;
            var cols = CollectMonsterColliders(m);
            string wname = root.name + (hd.transform != root ? "/" + hd.name : "");
            bool flail = hd.GetComponentInParent<FlailChainSim>() != null;
            log.AppendLine($"-- weapon {wname} ({col.GetType().Name}), proxy {(proxy != null ? "yes" : "NONE")}, monster colliders: body {cols.body.Count}, weapon {cols.weapon.Count}");
            var health = m.GetComponent<MonsterHealth>();
            foreach (bool thrust in new[] { true, false })
            foreach (var tn in targetNames)
            {
                var sb = new StringBuilder();
                foreach (float v in speeds)
                {
                    health.currentHealth = health.maxHealth;
                    var res = new Result();
                    Vector3 target = TargetPoint(m, tn, cols);
                    yield return SweepOne(root, hd, col, proxy, flail, target, v, thrust, cols, res, followThrough);
                    if (res.drawnBody > 0.05f && dumpBad) dbgBad.AppendLine($"     -- {(thrust ? "thrust" : "flat")} {tn} {v} m/s drawn {res.drawnBody * 100f:0.0}:\n" + res.dbg);
                    sb.Append($" {v,2:0}m/s: ctrl {res.trueBody * 100f,4:0.0}/{res.trueWeapon * 100f,4:0.0} drawn {res.drawnBody * 100f,4:0.0}/{res.drawnWeapon * 100f,4:0.0} off {res.maxOffset * 100f,4:0}" + (res.ghost ? "G" : " ") + $" h{res.hits}c{res.clashes}t{res.ticks} d{res.dmg:0.0} v{res.vmax:0.0}" + (res.hits > 0 && res.clashes > 0 ? " [" + res.contacts + " ]" : "") + " |");
                }
                log.AppendLine($"   {(thrust ? "thrust" : "flat  ")} {tn,-6}:{sb}");
                if (dumpBad && dbgBad.Length > 0) { log.Append(dbgBad); dbgBad.Length = 0; }
            }
            // leave the fight for the next weapon: end it quietly
            if (wt == weapons[weapons.Count - 1]) break;
            foreach (var p in pc.GetComponentsInChildren<WeaponSurfaceProxy>(true)) p.proxyEnabled = true;
            health.currentHealth = 1f; m.OnTakeDamage(60f, m.transform.position + Vector3.up * 1.2f, 9f, false);
            yield return Frames(120);
            // reload the scene for a clean next weapon
            UnityEngine.SceneManagement.SceneManager.LoadScene(scene);
            yield return Frames(20);
        }
    }

    // trace:<head|torso|legs|weapon>:<speed>:<thrust|flat>  one sweep, one line per frame
    static IEnumerator Trace(string spec)
    {
        var parts = spec.Split(':');
        string tn = parts[1]; bool thrust = parts[3] == "thrust";
        // "3,8,15": sweeps 3 and 8 first (not traced), then 15 is traced: earlier hits change the monster's state
        var speedList = parts[2].Split(',');
        float speed = float.Parse(speedList[speedList.Length - 1], System.Globalization.CultureInfo.InvariantCulture);
        yield return StartFight(WeaponType.Mace);
        var m = M; Freeze(m);
        var pc = PlayerCombat.Instance; var root = pc.weaponTransform;
        var hd = root.GetComponentInChildren<HitDetection>(true); var col = hd.GetComponent<Collider>(); var proxy = hd.GetComponent<WeaponSurfaceProxy>();
        foreach (var p in pc.GetComponentsInChildren<WeaponSurfaceProxy>(true)) p.proxyEnabled = true;
        var cols = CollectMonsterColliders(m);
        var rig = Object.FindAnyObjectByType<OVRCameraRig>();
        Vector3 target = TargetPoint(m, tn, cols);
        Vector3 dir = target - rig.centerEyeAnchor.position; dir.y = 0f; dir.Normalize();
        Quaternion rot = thrust ? Quaternion.LookRotation(dir, Vector3.up) : Quaternion.LookRotation(Quaternion.Euler(0f, 90f, 0f) * dir, Vector3.up);
        for (int w = 0; w < speedList.Length - 1; w++)
            yield return SweepOne(root, hd, col, proxy, false, target, float.Parse(speedList[w], System.Globalization.CultureInfo.InvariantCulture), thrust, cols, new Result(), 0.3f);
        target = TargetPoint(m, tn, cols);
        dir = target - rig.centerEyeAnchor.position; dir.y = 0f; dir.Normalize();
        Vector3 centreOffset = ShapeCentre(col, col.transform, Vector3.zero, rot);
        Vector3 start = target - dir * 0.9f, end = target + dir * 1.2f;
        for (int i = 0; i < 12; i++) { root.position = Vector3.Lerp(root.position, start - centreOffset, 0.4f); root.rotation = rot; yield return null; }
        root.position = start - centreOffset; yield return Frames(6);
        float dt = 1f / 72f; int frames = Mathf.CeilToInt(Vector3.Distance(start, end) / (speed * dt));
        log.AppendLine($"TRACE {tn} {speed} m/s {(thrust ? "thrust" : "flat")} target {target.ToString("F2")} dir {dir.ToString("F2")}");
        int stop = int.MaxValue; bool touched = false;
        for (int f = 0; f <= frames && f <= stop; f++)
        {
            root.position = Vector3.Lerp(start, end, f / (float)frames) - centreOffset; root.rotation = rot;
            yield return null;
            Vector3 P = col.transform.position, R3 = Vector3.zero; Quaternion R = col.transform.rotation;
            float db = Depth(col, P, R, cols.body), dw = Depth(col, P, R, cols.weapon);
            Vector3 off = proxy.VisualOffsetWorld;
            float ob = Depth(col, P + off, R, cols.body), ow = Depth(col, P + off, R, cols.weapon);
            // who is deepest at the controller pose
            string who = ""; float best = 0f; Vector3 mdir = Vector3.zero;
            foreach (var c in cols.body) if (Physics.ComputePenetration(col, P, R, c, c.transform.position, c.transform.rotation, out Vector3 d, out float dist) && dist > best) { best = dist; who = c.name; mdir = d; }
            string who2 = ""; float best2 = 0f;
            foreach (var c in cols.body) if (Physics.ComputePenetration(col, P + off, R, c, c.transform.position, c.transform.rotation, out Vector3 d, out float dist) && dist > best2) { best2 = dist; who2 = c.name; }
            Vector3 vv = hd.CurrentVelocity;
            log.AppendLine($"  f{f,3} P {P.ToString("F2")} ctrl {db * 100f,5:0.0}/{dw * 100f,4:0.0} ({who} mtv {mdir.ToString("F2")}) off {off.magnitude * 100f,5:0.0} dir {(off.sqrMagnitude > 1e-6f ? off.normalized : Vector3.zero).ToString("F2")} drawn {ob * 100f,5:0.0}/{ow * 100f,4:0.0} ({who2}) v {vv.ToString("F1")}");
            if (!touched && Mathf.Max(db, dw) > 0.01f) { touched = true; stop = f + Mathf.CeilToInt(0.3f / (speed * dt)); }
        }
    }

    // How much the "frozen" monster's colliders move over 90 frames, and which components are still running on it.
    static IEnumerator Drift()
    {
        yield return StartFight(WeaponType.Mace);
        var m = M; Freeze(m);
        yield return Frames(5);
        var cols = CollectMonsterColliders(m);
        var all = new List<Collider>(cols.body); all.AddRange(cols.weapon);
        var p0 = new Vector3[all.Count];
        for (int i = 0; i < all.Count; i++) p0[i] = all[i].transform.position;
        var rootP0 = m.transform.position;
        float maxMove = 0f; string who = "";
        for (int f = 0; f < 90; f++)
        {
            yield return null;
            for (int i = 0; i < all.Count; i++) { float d = (all[i].transform.position - p0[i]).magnitude; if (d > maxMove) { maxMove = d; who = all[i].name; } }
        }
        log.AppendLine($"DRIFT {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}: max collider movement over 90 frames {maxMove * 100f:0.0} cm ({who}); root moved {(m.transform.position - rootP0).magnitude * 100f:0.0} cm");
        var sb = new StringBuilder();
        foreach (var mb in m.GetComponents<MonoBehaviour>()) sb.Append(mb.GetType().Name + (mb.enabled ? "" : "(off)") + " ");
        log.AppendLine("components: " + sb);
        var an = m.GetComponentInChildren<Animator>();
        log.AppendLine($"animator speed {an.speed}, state {an.GetCurrentAnimatorStateInfo(0).shortNameHash}, in transition {an.IsInTransition(0)}, applyRootMotion {an.applyRootMotion}");
    }

    // ───────────────────────── reach ─────────────────────────
    // The monster closes to its fight stand-off (attacks held off), is pinned in its idle pose, and a weapon held with the arm
    // extended 0.30 / 0.45 / 0.60 m in front of the head (grip 0.25 m below the eyes, pointing at the monster) is measured against
    // it: how deep a normal and a fully extended reach already sits inside the monster before any follow-through.
    static IEnumerator Reach()
    {
        string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        var weapons = new List<WeaponType>();
        var pc0 = PlayerCombat.Instance;
        if (pc0.weaponSets != null && pc0.weaponSets.Count > 0) { weapons.Add(WeaponType.Mace); weapons.Add(WeaponType.Flail); weapons.Add(WeaponType.Katana); }
        else weapons.Add(WeaponType.None);
        foreach (var wt in weapons)
        {
            yield return StartFight(wt == WeaponType.None ? WeaponType.Mace : wt);
            var m = M;
            var ai = m.GetComponent<MonsterAI>();
            m.attackCooldown = 9999f;
            var baseCd = typeof(MonsterAI).GetField("baseAttackCooldown", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (baseCd != null) baseCd.SetValue(ai, 9999f);
            ai.chainChanceByPhase = Vector3.zero;
            var def = m.GetComponent<MonsterDefense>(); if (def != null) def.reactiveGuard = false; if (def != null) def.timedGuard = false;
            // wait for the monster to stop closing in
            Vector3 last = m.transform.position; int still = 0; float t0 = Time.realtimeSinceStartup;
            while (still < 40 && Time.realtimeSinceStartup - t0 < 30f) { yield return null; still = (m.transform.position - last).sqrMagnitude < 1e-6f ? still + 1 : 0; last = m.transform.position; }
            Freeze(m);
            yield return Frames(3);
            var rig = Object.FindAnyObjectByType<OVRCameraRig>();
            Vector3 head = rig.centerEyeAnchor.position;
            var cols = CollectMonsterColliders(m);
            float nearest = 99f; string who = "";
            foreach (var c in cols.body)
            {
                Vector3 cp = c.ClosestPoint(new Vector3(head.x, c.transform.position.y, head.z));
                float d = new Vector2(cp.x - head.x, cp.z - head.z).magnitude;
                if (d < nearest) { nearest = d; who = c.name; }
            }
            Vector3 aim = m.transform.position - head; aim.y = 0f; aim.Normalize();
            var pc = PlayerCombat.Instance; var root = pc.weaponTransform;
            var hd = root.GetComponentInChildren<HitDetection>(true); var col = hd.GetComponent<Collider>();
            var proxy = hd.GetComponent<WeaponSurfaceProxy>();
            foreach (var p in pc.GetComponentsInChildren<WeaponSurfaceProxy>(true)) p.proxyEnabled = true;
            bool flail = hd.GetComponentInParent<FlailChainSim>() != null;
            Quaternion rot = Quaternion.LookRotation(aim, Vector3.up);
            var sb = new StringBuilder();
            foreach (float e in new[] { 0.30f, 0.45f, 0.60f })
            {
                Vector3 grip = head + Vector3.down * 0.25f + aim * e;
                for (int i = 0; i < 30; i++) { root.position = grip; root.rotation = rot; yield return null; }
                Vector3 P = col.transform.position; Quaternion R = col.transform.rotation;
                float db = Depth(col, P, R, cols.body), dw = Depth(col, P, R, cols.weapon);
                Vector3 off = proxy != null ? proxy.VisualOffsetWorld : Vector3.zero;
                float tip = Vector3.Dot(col.bounds.max - head, aim);
                float far = 0f; { var bb = col.bounds; foreach (var corner in new[] { bb.min, bb.max }) far = Mathf.Max(far, Vector3.Dot(corner - head, aim)); }
                sb.Append($" arm {e:0.00} m (tip {far:0.00} m): body {db * 100f:0.0} cm, monster weapon {dw * 100f:0.0} cm, drawn offset {off.magnitude * 100f:0.0} |");
            }
            log.AppendLine($"== REACH {scene} {root.name}{(flail ? " (ball trails the hand)" : "")}: monster root {new Vector2(m.transform.position.x - head.x, m.transform.position.z - head.z).magnitude:0.00} m from the head, nearest body surface {nearest:0.00} m ({who}):{sb}");
            if (wt == weapons[weapons.Count - 1]) break;
            UnityEngine.SceneManagement.SceneManager.LoadScene(scene);
            yield return Frames(20);
        }
    }

    // ───────────────────────── cost ─────────────────────────
    static IEnumerator Cost()
    {
        yield return StartFight(WeaponType.Mace);
        var m = M; Freeze(m);
        var pc = PlayerCombat.Instance;
        var proxies = pc.GetComponentsInChildren<WeaponSurfaceProxy>(true);
        var root = pc.weaponTransform; var hd = root.GetComponentInChildren<HitDetection>(true); var col = hd.GetComponent<Collider>();
        var cols = CollectMonsterColliders(m);
        string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        Vector3 target = TargetPoint(m, "torso", cols);
        var rig = Object.FindAnyObjectByType<OVRCameraRig>();
        Vector3 dir = target - rig.centerEyeAnchor.position; dir.y = 0f; dir.Normalize();
        Quaternion rot = Quaternion.LookRotation(dir, Vector3.up);
        Vector3 off = ShapeCentre(col, col.transform, Vector3.zero, rot);
        var restLocal = root.localPosition; var restRot = root.localRotation;
        using (var rec = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "WeaponSurfaceProxy.Update"))
        using (var gc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame"))
        {
            foreach (bool on in new[] { true, false, true, false })
            {
                foreach (var p in proxies) p.proxyEnabled = on;
                var ns = new List<long>(); var gcs = new List<long>(); int near = 0, frames = 0;
                // a swing every 0.5 s: in, hold through the body, back out; the rest of the time the weapon rests near the monster
                for (int f = 0; f < 360; f++)
                {
                    int k = f % 36;
                    float s = k < 12 ? k / 12f : (k < 24 ? 1f : 1f - (k - 24) / 12f);
                    Vector3 p0 = target - dir * 0.5f, p1 = target + dir * 0.25f;
                    root.position = Vector3.Lerp(p0, p1, s) - off; root.rotation = rot;
                    yield return null;
                    if (f >= 12) { ns.Add(rec.LastValue); gcs.Add(gc.LastValue); frames++; foreach (var p in proxies) if (p.isActiveAndEnabled && p.Offset > 0.001f) { near++; break; } }
                }
                ns.Sort(); gcs.Sort();
                double mean = 0; foreach (var v in ns) mean += v; mean /= ns.Count;
                log.AppendLine($"== COST {scene} proxy {(on ? "ON " : "OFF")}: marker per frame (both hands, all proxies): median {ns[ns.Count / 2] / 1000.0:0.0} us, mean {mean / 1000.0:0.0} us, p95 {ns[(int)(ns.Count * 0.95f)] / 1000.0:0.0} us, max {ns[ns.Count - 1] / 1000.0:0.0} us; GC median {gcs[gcs.Count / 2]} B/frame; frames with the drawn weapon offset {near}/{frames}");
            }
        }
        root.localPosition = restLocal; root.localRotation = restRot;
    }

    // ───────────────────────── look: captures proxy off / on ─────────────────────────
    public static string captureDir = "Captures/WeaponProxy";
    public static float lookDepth = 0.22f;   // m the controller is pushed past first contact for the capture

    static IEnumerator Look()
    {
        System.IO.Directory.CreateDirectory(captureDir);
        string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        var weapons = new List<WeaponType>();
        var pc0 = PlayerCombat.Instance;
        if (pc0.weaponSets != null && pc0.weaponSets.Count > 0) { weapons.Add(WeaponType.Mace); weapons.Add(WeaponType.Flail); weapons.Add(WeaponType.Katana); }
        else weapons.Add(WeaponType.None);
        foreach (var wt in weapons)
        {
            yield return StartFight(wt == WeaponType.None ? WeaponType.Mace : wt);
            var m = M; if (m == null) { log.AppendLine("no monster"); yield break; }
            Freeze(m);
            var pc = PlayerCombat.Instance; var root = pc.weaponTransform;
            var hd = root.GetComponentInChildren<HitDetection>(true); var col = hd.GetComponent<Collider>(); var proxy = hd.GetComponent<WeaponSurfaceProxy>();
            var cols = CollectMonsterColliders(m);
            bool flail = hd.GetComponentInParent<FlailChainSim>() != null;
            var rig = Object.FindAnyObjectByType<OVRCameraRig>();
            Vector3 head = rig.centerEyeAnchor.position;
            string wname = root.name.Replace("/", "_");
            Vector3 restLocal = root.localPosition; Quaternion restLocalRot = root.localRotation; Quaternion baseRot = root.rotation;
            Vector3 target = TargetPoint(m, "torso", cols);
            Vector3 dir = target - head; dir.y = 0f; dir.Normalize();
            Quaternion rot = Quaternion.LookRotation(dir, Vector3.up);
            Vector3 centreOffset = ShapeCentre(col, col.transform, Vector3.zero, rot);
            float lead = flail ? 0.4f : 0f;
            Vector3 start = target - dir * 1.6f;
            float total = Vector3.Distance(start, target + dir * 1.2f), contactS = total;
            for (float sc = 0f; sc < total; sc += 0.01f)
            {
                Vector3 pos = start + dir * sc - centreOffset + dir * lead;
                if (Mathf.Max(Depth(col, pos, rot, cols.body), Depth(col, pos, rot, cols.weapon)) > 0.002f) { contactS = sc; break; }
            }
            Vector3 contactPoint = start + dir * contactS;
            log.AppendLine($"[{wname}] monster {m.transform.position.ToString("F2")} alive {m.isAlive} target {target.ToString("F2")} head {head.ToString("F2")} contactS {contactS:0.00}/{total:0.00} root {root.position.ToString("F2")} colliders {cols.body.Count}/{cols.weapon.Count}");
            foreach (bool on in new[] { false, true })
            {
                foreach (var p in pc.GetComponentsInChildren<WeaponSurfaceProxy>(true)) p.proxyEnabled = on;
                Pin(m);
                // slow walk (0.6 m/s, under every speed gate) to the start pose, then in to contact + lookDepth
                for (int i = 0; i < 12; i++) { root.position = Vector3.Lerp(root.position, start - centreOffset + dir * lead, 0.4f); root.rotation = rot; yield return null; }
                root.position = start - centreOffset + dir * lead; root.rotation = rot;
                yield return Frames(6);
                int steps = Mathf.CeilToInt((contactS + lookDepth) / (0.6f / 72f));
                for (int f = 0; f <= steps; f++)
                {
                    float k = f / (float)steps;
                    root.position = start + dir * ((contactS + lookDepth) * k) - centreOffset + dir * lead; root.rotation = rot;
                    var an = m.GetComponentInChildren<Animator>(); if (an != null) an.speed = 0f;
                    yield return null;
                }
                for (int f = 0; f < 45; f++)   // let the proxy spring and the flail pendulum settle (the pose is held every frame: PlayerCombat follows the hand)
                {
                    root.position = start + dir * (contactS + lookDepth) - centreOffset + dir * lead; root.rotation = rot;
                    var an = m.GetComponentInChildren<Animator>(); if (an != null) an.speed = 0f;
                    yield return null;
                }
                Vector3 P = col.transform.position; Quaternion R = col.transform.rotation;
                Vector3 off = proxy != null && proxy.proxyEnabled ? proxy.VisualOffsetWorld : Vector3.zero;
                float ctrl = Depth(col, P, R, cols.body), drawn = Depth(col, P + off, R, cols.body);
                string tag = $"{scene}_{wname}_{(on ? "ON" : "OFF")}";
                Vector3 look = contactPoint + dir * 0.05f + Vector3.up * 0.0f;
                CaptureLook($"{captureDir}/{tag}_side.png", look, Vector3.Cross(Vector3.up, dir) * 1.1f + Vector3.up * 0.25f);
                CaptureLook($"{captureDir}/{tag}_head.png", look, head - look, true);
                log.AppendLine($"{tag}: controller sink {ctrl * 100f:0.0} cm, drawn sink {drawn * 100f:0.0} cm, offset {off.magnitude * 100f:0.0} cm, ghost {(proxy != null && proxy.GhostShown)}");
                // back to the hand, slowly
                Vector3 rest = root.parent.TransformPoint(restLocal);
                for (int i = 0; i < 80 && Vector3.Distance(root.position, rest) > 0.02f; i++) { root.position = Vector3.MoveTowards(root.position, rest, 0.01f); root.rotation = Quaternion.Slerp(root.rotation, baseRot, 0.2f); yield return null; }
                root.localPosition = restLocal; root.localRotation = restLocalRot;
                yield return Frames(10);
            }
            foreach (var p in pc.GetComponentsInChildren<WeaponSurfaceProxy>(true)) p.proxyEnabled = true;
            if (wt == weapons[weapons.Count - 1]) break;
            var health = m.GetComponent<MonsterHealth>();
            health.currentHealth = 1f; m.OnTakeDamage(60f, m.transform.position + Vector3.up * 1.2f, 9f, false);
            yield return Frames(120);
            UnityEngine.SceneManagement.SceneManager.LoadScene(scene);
            yield return Frames(20);
        }
    }

    /// <summary>Render the contact from the side (eye = look + offset) or from the player's head (offset = head - look), capture-only key light.</summary>
    static void CaptureLook(string file, Vector3 look, Vector3 eyeOffset, bool fromHead = false)
    {
        var go = new GameObject("WSV_Cam"); var lightGo = new GameObject("WSV_CaptureLight");
        try
        {
            var lt = lightGo.AddComponent<Light>(); lt.type = LightType.Directional; lt.intensity = 1.3f; lt.shadows = LightShadows.None;
            var cam = go.AddComponent<Camera>();
            var rig = Object.FindAnyObjectByType<OVRCameraRig>();
            var main = rig.centerEyeAnchor.GetComponent<Camera>();
            if (main != null) cam.CopyFrom(main);
            Vector3 eye = look + eyeOffset;
            if (fromHead) eye = rig.centerEyeAnchor.position + Vector3.up * 0.0f;
            go.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye));
            lightGo.transform.rotation = Quaternion.LookRotation(look - eye + Vector3.down * 0.4f);
            cam.fieldOfView = fromHead ? 55f : 60f; cam.nearClipPlane = 0.05f;
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
