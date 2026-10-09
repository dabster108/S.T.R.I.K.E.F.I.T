// ClashVerification.cs
// Purpose: Play-mode probes for the weapon-clash pass (Editor only, never in a build).
//          Jobs: "clash" = every player weapon driven through every monster weapon (3 directions x 4 speeds x 2 frame rates)
//                          through the real HitDetection path; records clash / body hit per pass.
//                "body"  = the same passes aimed at the body (control).
//                "fit"   = monster weapon collider vs visible mesh at idle and at every attack's hit frame.
//          Frame timing is set with Time.captureDeltaTime (1/72 or 1/90 s per frame); physics keeps the project's fixed step,
//          so the trigger path sees the same sampling it gets on the headset.
// Dependencies: CombatVerification.Prepare, HitDetection, PlayerCombat, BattleManager, MonsterAI
// Attach to: nothing (static, driven over MCP)

using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class ClashVerification
{
    // ───────────────────────── job runner (same pattern as PolishVerification) ─────────────────────────
    static IEnumerator job;
    static readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
    static readonly StringBuilder log = new StringBuilder();
    static int lastFrame = -1;
    static string jobName = "";

    public static string Status() => (job != null ? $"RUNNING {jobName} t={Time.time:0.0} trials={trials.Count}\n" : "IDLE\n") + log;

    public static string Run(string name)
    {
        if (!EditorApplication.isPlaying) return "not playing";
        Stop();
        log.Length = 0;
        jobName = name;
        switch (name)
        {
            case "clash": job = Passes(false); break;
            case "body": job = Passes(true); break;
            case "fit": job = Fit(); break;
            case "zones": job = Zones(zoneSeconds); break;
            case "boss": job = Boss(); break;
            case "perf": job = Perf(); break;
            default: return "unknown job " + name;
        }
        EditorApplication.update += Tick;
        return "started " + name;
    }

    public static void Stop()
    {
        job = null; stack.Clear();
        EditorApplication.update -= Tick;
        HitDetection.WeaponClashed -= OnClash; HitDetection.HitLanded -= OnHit; SfxTrace.Played -= OnSound;
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
    static IEnumerator Seconds(float s) { float t0 = Time.time; while (Time.time - t0 < s) yield return null; }

    // ───────────────────────── settings ─────────────────────────
    public static float[] speeds = { 3f, 8f, 15f, 25f };
    public static float[] rates = { 72f, 90f };
    /// <summary>Only weapons whose name contains this (empty = all).</summary>
    public static string only = "";
    /// <summary>Frames the probe rests at the start point before each pass (its speed estimate settles to zero).</summary>
    public static int settleFrames = 4;
    public static float pathHalf = 0.7f;
    /// <summary>Print every trial (true) or only the summary.</summary>
    public static bool verbose = false;

    // ───────────────────────── results ─────────────────────────
    public struct Trial
    {
        public string weapon, mode, target, dir; public float speed, rate;
        public string gtFirst;          // geometric first contact along the path: "weapon", "body" or "none"
        public int clashes, hits, sounds; public float tClash, tHit;
    }
    public static readonly List<Trial> trials = new List<Trial>();

    static HitDetection probe;
    static int clashCount, hitCount, soundCount;
    static void OnSound(Object src, AudioClip clip, string cause) { if (src == probe && cause != null && cause.StartsWith("clash")) soundCount++; }
    static float tClash, tHit;
    static void OnClash(HitDetection s, float v) { if (s == probe) { clashCount++; if (tClash < 0f) tClash = Time.time; } }
    static void OnHit(HitDetection s, float v, bool d) { if (s == probe) { hitCount++; if (tHit < 0f) tHit = Time.time; } }

    static MonsterController M => BattleManager.Instance != null ? BattleManager.Instance.ActiveMonster : null;

    static IEnumerator StartFight()
    {
        log.AppendLine(CombatVerification.Prepare());
        var pc = PlayerCombat.Instance;
        if (pc.weaponSets != null && pc.weaponSets.Count > 0) pc.EquipWeaponSet(pc.weaponSets[0].type);
        yield return Frames(3);
        if (!BattleManager.Instance.IsBattleActive) BattleManager.Instance.StartBattleFromInspector();
        yield return Frames(2);
        float t0 = Time.time;
        while (Time.time - t0 < 25f)
        {
            var m = M; var e = m != null ? m.GetComponent<MonsterEntrance>() : null;
            if (m != null && (e == null || e.Complete)) break;
            yield return null;
        }
    }

    /// <summary>Stops the monster in its idle pose: no AI, no guard, no clamp, no hit-shake, no hit-stop, endless HP.</summary>
    static IEnumerator Freeze(MonsterController m)
    {
        m.attackCooldown = 9999f;
        var ai = m.GetComponent<MonsterAI>();
        float w0 = Time.time; while ((m.isAttacking || ai.IsBusy) && Time.time - w0 < 10f) yield return null;
        var def = m.GetComponent<MonsterDefense>(); if (def != null) def.enabled = false;
        ai.enabled = false;
        var ps = m.GetComponent<MonsterPersonalSpace>(); if (ps != null) ps.enabled = false;
        var sh = m.GetComponent<MonsterHitShake>(); if (sh != null) sh.shakeEnabled = false;
        var cf = Object.FindAnyObjectByType<CombatFeedback>(); if (cf != null) cf.hitStop = false;
        m.GetComponent<MonsterHealth>().SetMaxHealth(1e7f, true);
        yield return Frames(30);
        var anim = m.GetComponentInChildren<Animator>(); if (anim != null) anim.enabled = false;
        yield return Frames(2);
    }

    // ───────────────────────── geometry helpers ─────────────────────────
    /// <summary>Monster weapon roots: MonsterWeapon-tagged colliders whose parent is not part of the same weapon.</summary>
    public static List<Transform> WeaponRoots(MonsterController m)
    {
        var roots = new List<Transform>();
        foreach (var c in m.GetComponentsInChildren<Collider>(true))
        {
            if (!c.CompareTag("MonsterWeapon")) continue;
            var t = c.transform;
            while (t.parent != null && t.parent.CompareTag("MonsterWeapon")) t = t.parent;
            if (!roots.Contains(t)) roots.Add(t);
        }
        return roots;
    }

    /// <summary>Visible mesh vertices of a weapon in its root's local space (mesh renderers only, no particles).</summary>
    public static List<Vector3> LocalVerts(Transform root)
    {
        var list = new List<Vector3>();
        foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
        {
            var r = mf.GetComponent<MeshRenderer>();
            if (r == null || mf.sharedMesh == null || !r.enabled) continue;
            bool opaque = false; foreach (var m in r.sharedMaterials) if (m != null && m.renderQueue < 2450) opaque = true;
            if (!opaque) continue; // glow planes are effects, not weapon
            var toLocal = root.worldToLocalMatrix * mf.transform.localToWorldMatrix;
            foreach (var v in mf.sharedMesh.vertices) list.Add(toLocal.MultiplyPoint3x4(v));
        }
        return list;
    }

    /// <summary>A point inside the visible weapon at fraction f of its long axis (centroid of the vertices in that slice).</summary>
    static Vector3 SliceTarget(Transform root, List<Vector3> verts, float f)
    {
        Vector3 mn = Vector3.one * 1e9f, mx = -mn;
        foreach (var v in verts) { mn = Vector3.Min(mn, v); mx = Vector3.Max(mx, v); }
        Vector3 size = mx - mn;
        int ax = size.x > size.y ? (size.x > size.z ? 0 : 2) : (size.y > size.z ? 1 : 2);
        float at = mn[ax] + size[ax] * f, band = size[ax] * 0.04f;
        Vector3 sum = Vector3.zero; int n = 0;
        foreach (var v in verts) if (Mathf.Abs(v[ax] - at) <= band) { sum += v; n++; }
        Vector3 local = n > 0 ? sum / n : (mn + mx) * 0.5f;
        return root.TransformPoint(local);
    }

    static bool OverlapsAny(Collider probeCol, Vector3 pos, Quaternion rot, List<Collider> set)
    {
        foreach (var c in set)
            if (c != null && c.enabled && c.gameObject.activeInHierarchy &&
                Physics.ComputePenetration(probeCol, pos, rot, c, c.transform.position, c.transform.rotation, out _, out _))
                return true;
        return false;
    }

    // ───────────────────────── passes ─────────────────────────
    static readonly string[] Dirs = { "lateral", "down", "thrust" };
    static readonly float[] DirFraction = { 0.6f, 0.3f, 0.9f }; // lateral -> mid blade / head, down -> lower (grip / haft), thrust -> tip

    static Vector3 DirVector(MonsterController m, int d)
    {
        var t = m.transform;
        switch (d)
        {
            case 0: return t.right;
            case 1: return Vector3.down;
            default: return (-t.forward + Vector3.down * 0.5f).normalized;
        }
    }

    static List<HitDetection> ProbeWeapons()
    {
        var pc = PlayerCombat.Instance;
        var list = new List<HitDetection>();
        foreach (var hd in pc.GetComponentsInChildren<HitDetection>(false))
            if (string.IsNullOrEmpty(only) || hd.name.Contains(only) || hd.transform.parent.name.Contains(only)) list.Add(hd);
        return list;
    }

    // ───────────────────────── keyframed probe path (collider centre + rotation over time) ─────────────────────────
    struct Key { public float t; public Vector3 c; public Quaternion r; }
    static readonly List<Key> path = new List<Key>();
    /// <summary>Modes: "straight" (rigid translation), "arc" (rotation about a pivot 0.65 m behind the contact, like a real
    /// swing: the far end moves faster than the centre), "follow" (through the monster weapon, then into the chest about
    /// 0.15 to 0.26 s later: a deflected swing that carries on).</summary>
    public static string[] modes = { "straight", "arc", "follow" };
    public static float arcRadius = 0.65f;

    static void AddKey(float t, Vector3 c, Quaternion r) { path.Add(new Key { t = t, c = c, r = r }); }

    static void BuildStraight(Vector3 a, Vector3 b, Quaternion r, float v)
    {
        path.Clear();
        float dur = Vector3.Distance(a, b) / v;
        int n = Mathf.Max(2, Mathf.CeilToInt(dur / 0.002f));
        for (int i = 0; i <= n; i++) AddKey(dur * i / n, Vector3.Lerp(a, b, i / (float)n), r);
    }

    static void BuildArc(Vector3 target, Vector3 dir, Vector3 n, Quaternion r0, float v, float phaseShift)
    {
        path.Clear();
        Vector3 pivot = target - n * arcRadius, axis = Vector3.Cross(n, dir).normalized;
        float thMax = pathHalf / arcRadius, w = v / arcRadius, th0 = -thMax - phaseShift / arcRadius;
        float dur = (thMax - th0) / w;
        int steps = Mathf.Max(2, Mathf.CeilToInt(dur / 0.002f));
        for (int i = 0; i <= steps; i++)
        {
            float th = th0 + (thMax - th0) * i / steps;
            Quaternion q = Quaternion.AngleAxis(th * Mathf.Rad2Deg, axis);
            AddKey(dur * i / steps, pivot + q * (n * arcRadius), q * r0);
        }
    }

    static void BuildFollow(Vector3 a, Vector3 target, Vector3 dir, Vector3 body, Quaternion r, float v)
    {
        path.Clear();
        Vector3 b = target + dir * 0.05f;
        float d1 = Vector3.Distance(a, b) / v;
        int n1 = Mathf.Max(2, Mathf.CeilToInt(d1 / 0.002f));
        for (int i = 0; i <= n1; i++) AddKey(d1 * i / n1, Vector3.Lerp(a, b, i / (float)n1), r);
        Vector3 into = body - b; float dist = into.magnitude;
        Vector3 end = body + into.normalized * 0.1f;
        float d2 = Mathf.Clamp(dist / 3f, 0.15f, 0.26f);              // reaches the chest 0.15 to 0.26 s after the weapon
        float d3 = d2 * Vector3.Distance(b, end) / Mathf.Max(0.01f, dist);
        int n2 = Mathf.Max(2, Mathf.CeilToInt(d3 / 0.002f));
        for (int i = 1; i <= n2; i++) AddKey(d1 + d3 * i / n2, Vector3.Lerp(b, end, i / (float)n2), r);
    }

    static void PoseAt(float t, out Vector3 c, out Quaternion r)
    {
        if (t <= path[0].t) { c = path[0].c; r = path[0].r; return; }
        for (int i = 1; i < path.Count; i++)
            if (path[i].t >= t)
            {
                float k = (t - path[i - 1].t) / Mathf.Max(1e-6f, path[i].t - path[i - 1].t);
                c = Vector3.Lerp(path[i - 1].c, path[i].c, k); r = Quaternion.Slerp(path[i - 1].r, path[i].r, k); return;
            }
        c = path[path.Count - 1].c; r = path[path.Count - 1].r;
    }

    /// <summary>First collider kind the probe overlaps along the path (every key, 2 ms apart; finer than 5 mm below 2.5 m/s).
    /// "slow" = the first contact is a monster weapon but the weapon's grip (the HitDetection transform, which is where the game
    /// measures swing speed) moves slower than the speed gate there: by the game's rules that is not a swing, so no clash is due.</summary>
    static string GroundTruthPath(Collider col, Vector3 localOffset, List<Collider> weaponCols, List<Collider> bodyCols, float gate)
    {
        for (int i = 0; i < path.Count; i++)
        {
            Vector3 p = path[i].c - path[i].r * localOffset;
            bool w = OverlapsAny(col, p, path[i].r, weaponCols), bd = OverlapsAny(col, p, path[i].r, bodyCols);
            if (i == 0 && (w || bd)) return "start";
            if (w)
            {
                int j = Mathf.Max(0, i - 1);
                Vector3 pj = path[j].c - path[j].r * localOffset;
                float dt = Mathf.Max(1e-5f, path[i].t - path[j].t);
                return (p - pj).magnitude / dt < gate ? "slow" : "weapon";
            }
            if (bd) return "body";
        }
        return "none";
    }

    static IEnumerator Passes(bool bodyOnly)
    {
        trials.Clear();
        yield return StartFight();
        var m = M;
        if (m == null) { log.AppendLine("no monster"); yield break; }
        yield return Freeze(m);
        HitDetection.WeaponClashed += OnClash; HitDetection.HitLanded += OnHit; SfxTrace.Played += OnSound;

        var pc = PlayerCombat.Instance;
        var loadouts = new List<WeaponType>();
        if (pc.weaponSets != null && pc.weaponSets.Count > 0) foreach (var s in pc.weaponSets) loadouts.Add(s.type);
        else loadouts.Add(WeaponType.None);

        var weaponCols = new List<Collider>(); var bodyCols = new List<Collider>();
        foreach (var c in m.GetComponentsInChildren<Collider>(false)) (c.CompareTag("MonsterWeapon") ? weaponCols : bodyCols).Add(c);
        var roots = WeaponRoots(m);
        Vector3 chest = BodyTarget(m, weaponCols);
        log.AppendLine($"monster {m.name}: weapon roots {roots.Count}, weapon colliders {weaponCols.Count}, body colliders {bodyCols.Count}, fixedDt {Time.fixedDeltaTime:0.000}, modes {string.Join(",", modes)}");

        foreach (var lo in loadouts)
        {
            if (lo != WeaponType.None) { pc.EquipWeaponSet(lo); yield return Frames(3); }
            foreach (var hd in ProbeWeapons())
            {
                // Only the right-hand weapon of an L3 pair (both hands are the same weapon); L1 / L2 test both hands.
                if (lo != WeaponType.None && hd.hand == Hand.Left) continue;
                var W = hd.transform;
                var col = hd.GetComponent<Collider>();
                var chain = W.GetComponentInParent<FlailChainSim>(); if (chain != null) chain.enabled = false;
                Vector3 restLocal = W.localPosition; Quaternion restRot = W.localRotation;
                Quaternion rot0 = W.rotation;
                Vector3 localOffset = Quaternion.Inverse(W.rotation) * (col.bounds.center - W.position); // collider centre in W's frame
                string wname = (W.parent != null && W.name == "Ball" ? W.parent.name + "/" : "") + W.name;

                var targets = new List<KeyValuePair<string, Vector3>>[3];
                for (int d = 0; d < 3; d++)
                {
                    targets[d] = new List<KeyValuePair<string, Vector3>>();
                    if (bodyOnly) targets[d].Add(new KeyValuePair<string, Vector3>("body", chest));
                    else foreach (var r in roots) targets[d].Add(new KeyValuePair<string, Vector3>(r.name + "@" + DirFraction[d].ToString("0.0"), SliceTarget(r, LocalVerts(r), DirFraction[d])));
                }

                foreach (string mode in modes)
                {
                    if (bodyOnly && mode == "follow") continue;
                    foreach (float rate in rates)
                    {
                        Time.captureDeltaTime = 1f / rate;
                        foreach (float v in speeds)
                            for (int d = 0; d < 3; d++)
                                foreach (var tg in targets[d])
                                {
                                    Vector3 dir = DirVector(m, d);
                                    int trialIndex = trials.Count;
                                    float phase = (v / rate) * ((trialIndex % 3) / 3f); // start a fraction of one frame's travel early
                                    Vector3 toPlayer = -m.transform.forward;
                                    Vector3 nrm = toPlayer - dir * Vector3.Dot(toPlayer, dir);
                                    if (nrm.sqrMagnitude < 0.01f) nrm = Vector3.up - dir * Vector3.Dot(Vector3.up, dir);
                                    nrm.Normalize();
                                    if (mode == "arc") BuildArc(tg.Value, dir, nrm, rot0, v, phase);
                                    else
                                    {
                                        Vector3 a = tg.Value - dir * pathHalf, b = tg.Value + dir * pathHalf;
                                        for (int k = 0; k < 10 && (OverlapsAny(col, a - rot0 * localOffset, rot0, bodyCols) || OverlapsAny(col, a - rot0 * localOffset, rot0, weaponCols)); k++) a -= dir * 0.1f;
                                        a -= dir * phase;
                                        if (mode == "follow") BuildFollow(a, tg.Value, dir, chest, rot0, v);
                                        else BuildStraight(a, b, rot0, v);
                                    }
                                    float gate = Mathf.Max(hd.minSwingSpeed, pc.minSwingVelocity);
                                    string gt = GroundTruthPath(col, localOffset, weaponCols, bodyCols, gate);
                                    if (gt == "start") continue; // the arc starts inside the monster: not a clean pass

                                    // Park at the start, still, so the gate re-arms and no contact is carried over.
                                    W.SetPositionAndRotation(path[0].c - path[0].r * localOffset, path[0].r);
                                    ResetSweep(hd);
                                    yield return Frames(settleFrames);
                                    ResetContact(hd);
                                    yield return null;
                                    probe = hd; clashCount = hitCount = soundCount = 0; tClash = tHit = -1f;
                                    float t0 = Time.time, dur = path[path.Count - 1].t;
                                    while (true)
                                    {
                                        float el = Time.time - t0;
                                        PoseAt(el, out var c, out var r);
                                        W.SetPositionAndRotation(c - r * localOffset, r);
                                        if (el >= dur) break;
                                        yield return null;
                                    }
                                    yield return Frames(3);
                                    probe = null;
                                    trials.Add(new Trial { weapon = wname, mode = mode, target = tg.Key, dir = Dirs[d], speed = v, rate = rate, gtFirst = gt, clashes = clashCount, hits = hitCount, sounds = soundCount, tClash = tClash < 0f ? -1f : tClash - t0, tHit = tHit < 0f ? -1f : tHit - t0 });
                                    // Back to the hand, still.
                                    W.localPosition = restLocal; W.localRotation = restRot;
                                    ResetSweep(hd);
                                    yield return Frames(2);
                                }
                    }
                }
                W.localPosition = restLocal; W.localRotation = restRot;
                if (chain != null) chain.enabled = true;
            }
        }
        Time.captureDeltaTime = 1f / 72f;
        log.Append(Summary(bodyOnly));
        log.Append(Counters());
    }

    /// <summary>Haptic / sound / sweep counters after a run (reflection, so the harness also builds against older code).</summary>
    static string Counters()
    {
        var sb = new StringBuilder();
        int sounds = 0, clashTrials = 0, soundNoClash = 0;
        foreach (var t in trials) { sounds += t.sounds; if (t.clashes > 0) clashTrials++; if (t.sounds > 0 && t.clashes == 0) soundNoClash++; }
        sb.AppendLine($"clash sounds {sounds} in {clashTrials} clashing passes, sounds without a clash {soundNoClash}");
        var info = System.Type.GetType("CombatDebugInfo, Assembly-CSharp");
        if (info != null)
        {
            string P(string n) { var pr = info.GetProperty(n); return pr != null ? System.Convert.ToString(pr.GetValue(null)) : "?"; }
            sb.AppendLine($"CombatDebugInfo: clashes {P("Clashes")}, haptic calls {P("HapticCalls")}, last haptic {P("LastHapticKind")} {P("LastHapticHand")} amp {P("LastHapticAmplitude")} {P("LastHapticSeconds")} s via {P("LastHapticBackend")} accepted {P("LastHapticAccepted")}, last clash sound {P("LastClashClip")} pitch {P("LastClashPitch")}");
        }
        var fb = Object.FindAnyObjectByType<CombatFeedback>();
        if (fb != null)
        {
            string F(string n) { var pr = typeof(CombatFeedback).GetProperty(n); return pr != null ? System.Convert.ToString(pr.GetValue(fb)) : "?"; }
            sb.AppendLine($"CombatFeedback: pulses {F("Pulses")}, clash pulses {F("ClashPulses")}, suppressed {F("PulsesSuppressed")}");
        }
        foreach (var hd in PlayerCombat.Instance.GetComponentsInChildren<HitDetection>(true))
        {
            string H(string n) { var pr = typeof(HitDetection).GetProperty(n); return pr != null ? System.Convert.ToString(pr.GetValue(hd)) : "?"; }
            sb.AppendLine($"  {hd.name}: sweep clashes {H("SweepClashes")}, trigger clashes {H("TriggerClashes")}, sweep body {H("SweepBodyContacts")}, trigger body {H("TriggerBodyContacts")}, body blocked by clash {H("BodyBlockedByClash")}, pair-cooldown rejects {H("PairCooldownRejects")}, hand {H("ControllerHand")}");
        }
        return sb.ToString();
    }

    static Vector3 BodyTarget(MonsterController m, List<Collider> weaponCols)
    {
        // Centre of the big body capsule above the knees that is farthest from the monster's own weapon.
        Vector3 best = m.transform.position + Vector3.up * 1.2f; float bestD = -1f;
        foreach (var cap in m.GetComponentsInChildren<CapsuleCollider>())
        {
            if (cap.isTrigger || cap.CompareTag("MonsterWeapon") || cap.transform == m.transform) continue;
            Vector3 c = cap.bounds.center;
            if (c.y < m.transform.position.y + 0.8f) continue;
            float d = 99f; foreach (var w in weaponCols) d = Mathf.Min(d, Vector3.Distance(c, w.ClosestPoint(c)));
            d += cap.radius;
            if (d > bestD) { bestD = d; best = c; }
        }
        return best;
    }

    static void ResetSweep(HitDetection hd)
    {
        var mi = typeof(HitDetection).GetMethod("ResetSweepHistory");
        if (mi != null) mi.Invoke(hd, null);
    }

    /// <summary>Every trial starts fresh: the cooldown / one-contact-per-swing / clash-lock timers of the previous pass are
    /// cleared (private fields, so this works on the old and the new HitDetection alike).</summary>
    static void ResetContact(HitDetection hd)
    {
        var bf = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        void Set(string field, object value) { var f = typeof(HitDetection).GetField(field, bf); if (f != null) f.SetValue(hd, value); }
        Set("lastHitTime", -999f); Set("lastTouchTime", -999f); Set("stayArmed", true); Set("contactTarget", null);
        Set("clashLockUntil", -999f); Set("lastContactBody", false); Set("clashedSwing", false); Set("clashedAt", -999f);
        var pairs = typeof(HitDetection).GetField("pairTime", bf);
        if (pairs != null && pairs.GetValue(hd) is float[] a) for (int i = 0; i < a.Length; i++) a[i] = -999f;
        // Speed estimates restart from the parked pose: a teleport to the start would otherwise leave a decaying fake swing
        // speed (PlayerCombat smooths over 0.12 s) that lifts early contacts over the speed gate.
        Set("lastPosition", hd.transform.position); Set("velocity", Vector3.zero);
        var pc = PlayerCombat.Instance;
        void SetPc(string field, object value) { var f = typeof(PlayerCombat).GetField(field, bf); if (f != null) f.SetValue(pc, value); }
        SetPc("smoothedVelocity", Vector3.zero); SetPc("smoothedOffhandVelocity", Vector3.zero);
        if (pc.weaponTransform != null) { SetPc("lastWeaponPos", pc.weaponTransform.position); SetPc("lastWeaponRot", pc.weaponTransform.rotation); }
        if (pc.offhandWeaponTransform != null) SetPc("lastOffhandWeaponPos", pc.offhandWeaponTransform.position);
    }

    public static string Summary(bool bodyOnly)
    {
        var sb = new StringBuilder();
        if (verbose)
            foreach (var t in trials)
                sb.AppendLine($"{t.weapon,-22} {t.mode,-8} {t.target,-18} {t.dir,-7} {t.speed,4:0} m/s {t.rate,3:0} Hz gt={t.gtFirst,-6} clash={t.clashes} hit={t.hits} snd={t.sounds} tC={t.tClash:0.000} tH={t.tHit:0.000}");
        // Per weapon / mode / speed / rate.
        var keys = new SortedDictionary<string, int[]>();
        foreach (var t in trials)
        {
            string k = $"{t.weapon} | {t.mode,-8} | {t.speed,2:0} m/s | {t.rate:0} Hz";
            if (!keys.TryGetValue(k, out var a)) keys[k] = a = new int[6];
            a[0]++;                                                     // trials
            if (t.gtFirst == (bodyOnly ? "body" : "weapon")) a[1]++;    // geometrically reaches the target kind first
            if (t.gtFirst == (bodyOnly ? "body" : "weapon") && (bodyOnly ? t.hits > 0 : t.clashes > 0)) a[2]++;
            if (t.hits > 0) a[3]++;
            if (t.clashes > 0 && t.hits > 0) a[4]++;
            if (t.clashes == 0 && t.hits > 0 && t.gtFirst == "weapon") a[5]++; // bypass: weapon was in the way, body got hit
        }
        sb.AppendLine(bodyOnly ? "weapon | mode | speed | rate : passes / body first / body hit detected / any body hit / clash+body"
                               : "weapon | mode | speed | rate : passes / weapon first / clash detected / body hit / clash AND body / bypass (weapon first, no clash, body hit)");
        foreach (var kv in keys) sb.AppendLine($"  {kv.Key} : {kv.Value[0]} / {kv.Value[1]} / {kv.Value[2]} / {kv.Value[3]} / {kv.Value[4]}" + (bodyOnly ? "" : $" / {kv.Value[5]}"));
        foreach (string mode in modes)
        {
            int n = 0, gtW = 0, det = 0, both = 0, byp = 0, bodyHit = 0, gtB = 0;
            foreach (var t in trials)
            {
                if (t.mode != mode) continue;
                n++;
                if (t.gtFirst == "weapon") { gtW++; if (t.clashes > 0) det++; if (t.clashes == 0 && t.hits > 0) byp++; }
                if (t.gtFirst == "body") { gtB++; if (t.hits > 0) bodyHit++; }
                if (t.clashes > 0 && t.hits > 0) both++;
            }
            if (n == 0) continue;
            sb.AppendLine(bodyOnly
                ? $"TOTAL {mode}: {n} passes, body first {gtB}, body hit detected {bodyHit} ({(gtB > 0 ? 100f * bodyHit / gtB : 0f):0.0} %)"
                : $"TOTAL {mode}: {n} passes, weapon first {gtW}, clash detected {det} ({(gtW > 0 ? 100f * det / gtW : 0f):0.0} %), bypass (no clash, body damaged) {byp}, clashed AND body damaged {both}, body-first {gtB}");
        }
        return sb.ToString();
    }

    static int CountClash() { int c = 0; foreach (var t in trials) if (t.clashes > 0) c++; return c; }

    // ───────────────────────── zones: live fight, HIGH / MID / LOW swings (Part 3) ─────────────────────────
    public static float zoneSeconds = 100f;
    public static float zoneSwingEvery = 1.1f;
    public static float zoneSwingSpeed = 6f;

    /// <summary>A real fight (monster AI on, attacks, guards, counters) where the probe swings every zoneSwingEvery seconds at
    /// a random zone: HIGH = downward chop at head / upper-chest height, MID = horizontal at the torso, LOW = horizontal at the
    /// legs. Reports per class: guards raised, blocked vs opening hits and damage, uptime, guard breaks, counters.</summary>
    static IEnumerator Zones(float seconds)
    {
        yield return StartFight();
        var m = M;
        if (m == null) { log.AppendLine("no monster"); yield break; }
        var def = m.GetComponent<MonsterDefense>();
        var ai = m.GetComponent<MonsterAI>();
        m.GetComponent<MonsterHealth>().SetMaxHealth(1e6f, true);
        var ph = PlayerHealth.Instance; if (ph != null) { ph.maxHealth = 1e6f; ph.currentHealth = 1e6f; }
        def.ResetStats();
        int attacks0 = ai.TableAttackCount;
        HitDetection.HitLanded += OnHit; HitDetection.WeaponClashed += OnClash;
        var pc = PlayerCombat.Instance;
        var hd = pc.weaponTransform != null ? pc.weaponTransform.GetComponentInChildren<HitDetection>() : null;
        if (hd == null) { log.AppendLine("no weapon"); yield break; }
        var W = hd.transform; var col = hd.GetComponent<Collider>();
        Vector3 restLocal = W.localPosition; Quaternion restRot = W.localRotation;
        var swings = new int[3]; var hitsBy = new int[3]; var clashBy = new int[3];
        float t0 = Time.time, h = def.Height;
        while (Time.time - t0 < seconds && m.isAlive)
        {
            int cls = Random.Range(0, 3);
            Vector3 root = m.transform.position, fwd = m.transform.forward, right = m.transform.right;
            float frac = cls == 0 ? 0.86f : cls == 1 ? 0.6f : 0.28f;
            Vector3 target = root + Vector3.up * (h * frac) + fwd * 0.12f;
            Vector3 dir = cls == 0 ? (Vector3.down * 0.85f - fwd * 0.5f).normalized : (Random.value < 0.5f ? right : -right);
            Vector3 a = target - dir * 0.8f + fwd * (cls == 0 ? 0f : 0.25f), b = target + dir * 0.35f;
            Vector3 offset = col.bounds.center - W.position;
            W.position = a - offset; ResetSweep(hd);
            yield return Frames(3);
            ResetContact(hd);
            probe = hd; clashCount = hitCount = 0;
            swings[cls]++;
            float s0 = Time.time, dur = Vector3.Distance(a, b) / zoneSwingSpeed;
            while (Time.time - s0 < dur) { W.position = Vector3.Lerp(a, b, (Time.time - s0) / dur) - offset; yield return null; }
            yield return Frames(2);
            hitsBy[cls] += hitCount; clashBy[cls] += clashCount; probe = null;
            W.localPosition = restLocal; W.localRotation = restRot; ResetSweep(hd);
            float wait = Time.time + Mathf.Max(0.1f, zoneSwingEvery - dur);
            while (Time.time < wait) yield return null;
        }
        float fight = Time.time - t0;
        string[] names = { "HIGH", "MID", "LOW" };
        var sb = new StringBuilder($"ZONES {m.name}: {fight:0} s, swing every {zoneSwingEvery:0.0} s at {zoneSwingSpeed:0} m/s, height {h:0.00} m\n");
        for (int i = 0; i < 3; i++)
        {
            int bl = def.BlockedByZone[i], op = def.OpeningsByZone[i];
            sb.AppendLine($"  {names[i]}: probe swings {swings[i]}, detected {def.SwingsByClass[i]}, {names[i]} guards raised {def.GuardsByZone[i]}, body hits landed {hitsBy[i]} (+{clashBy[i]} clashes) | while that guard was up: blocked {bl} ({def.BlockedDamageByZone[i]:0} dmg), opening {op} ({def.OpeningDamageByZone[i]:0} dmg)");
        }
        sb.AppendLine($"  guards {def.ReactiveGuards + def.TimedGuards} (reactive {def.ReactiveGuards}, timed {def.TimedGuards}), uptime {def.Uptime * 100f:0.0} %, guard breaks {def.GuardBreaks}, counters {def.Counters} of {def.CounterRolls} rolls, monster attacks {ai.TableAttackCount - attacks0}");
        log.Append(sb);
        HitDetection.HitLanded -= OnHit; HitDetection.WeaponClashed -= OnClash;
    }

    // ───────────────────────── boss: Level 3 phases, chains, telegraphs, burst limit (Part 4) ─────────────────────────
    public static float bossPhaseSeconds = 40f;
    public static float bossExpertHit = 90f;

    static IEnumerator Boss()
    {
        yield return StartFight();
        var m = M;
        if (m == null) { log.AppendLine("no monster"); yield break; }
        var ai = m.GetComponent<MonsterAI>(); var hp = m.GetComponent<MonsterHealth>(); var bp = m.GetComponent<BossPhaseController>();
        var ph = PlayerHealth.Instance;
        float pMax = ph.maxHealth;
        var sb = new StringBuilder($"BOSS {m.name}: max HP {hp.maxHealth:0}, player HP {pMax:0}, damage {m.attackDamage:0.#}, cooldown {m.attackCooldown:0.##} s, cap {m.maxSingleHitFraction:P0}, burst {m.maxDamagePerSecondFraction * hp.maxHealth:0.#}/s\n");
        float[] setHp = { 1f, 0.6f, 0.3f };
        for (int phase = 0; phase < 3; phase++)
        {
            // Put the boss in the phase (the shift roar runs), then watch it fight a player who never dodges.
            if (phase > 0) { hp.currentHealth = hp.maxHealth * setHp[phase]; m.OnTakeDamage(0.01f, m.transform.position + Vector3.up, 0f, false); }
            float w0 = Time.time; while ((bp != null && bp.InPhaseShift) && Time.time - w0 < 10f) yield return null;
            yield return Frames(5);
            int atk0 = ai.TableAttackCount, ch0 = ai.ChainedAttacks; float t0 = Time.time; float taken = 0f;
            ph.SetMaxHealth(1e6f, true); float hp0 = ph.currentHealth;
            while (Time.time - t0 < bossPhaseSeconds && m.isAlive)
            {
                if (hp.currentHealth / hp.maxHealth > setHp[phase] + 0.02f || hp.currentHealth / hp.maxHealth < setHp[phase] - 0.05f) hp.currentHealth = hp.maxHealth * setHp[phase];
                yield return null;
            }
            taken = hp0 - ph.currentHealth;
            float secs = Time.time - t0;
            sb.AppendLine($"  phase {phase + 1} (boss HP {setHp[phase]:P0}): tempo {ai.tempoScale:0.00}, cooldown {m.attackCooldown:0.00} s (phase scale {ai.phaseCooldownScale:0.00}), {ai.TableAttackCount - atk0} attacks ({ai.ChainedAttacks - ch0} chained) in {secs:0} s, damage to a never-dodge player {taken / secs:0.0} HP/s ({taken:0} in {secs:0} s)");
        }
        sb.AppendLine($"  telegraphs used: shortest {ai.ShortestTelegraph:0.000} s, longest {ai.LongestTelegraph:0.000} s; longest chain {ai.LongestChain}; chained attacks {ai.ChainedAttacks}");
        // Burst limit: an expert at 1.5 full-power hits per second for 12 s.
        hp.currentHealth = hp.maxHealth * 0.9f; yield return Frames(3);
        float b0 = hp.currentHealth, bt0 = Time.time; int lim0 = m.BurstLimitedHits; int n = 0;
        while (Time.time - bt0 < 12f)
        {
            m.OnTakeDamage(bossExpertHit, m.transform.position + Vector3.up * 1.2f, 9f, false); n++; // best realistic hit (model: mace at full speed, front, full angle)
            float next = Time.time + 1f / 1.5f; while (Time.time < next) yield return null;
        }
        float dealt = b0 - hp.currentHealth;
        sb.AppendLine($"  burst test: {n} hits of {bossExpertHit:0} in {Time.time - bt0:0.0} s -> {dealt / (Time.time - bt0):0.0} HP/s dealt (limit {m.maxDamagePerSecondFraction * hp.maxHealth:0.0}/s, {m.BurstLimitedHits - lim0} hits cut) => fastest kill about {hp.maxHealth / Mathf.Max(1f, dealt / (Time.time - bt0)):0} s");
        log.Append(sb);
    }

    // ───────────────────────── perf: sweep cost and GC allocation per frame ─────────────────────────
    static IEnumerator Perf()
    {
        yield return StartFight();
        var m = M; if (m == null) { log.AppendLine("no monster"); yield break; }
        yield return Freeze(m);
        HitDetection.WeaponClashed += OnClash; HitDetection.HitLanded += OnHit;
        var pc = PlayerCombat.Instance;
        var hd = pc.weaponTransform.GetComponentInChildren<HitDetection>(); var W = hd.transform; var col = hd.GetComponent<Collider>();
        Vector3 restLocal = W.localPosition; Quaternion restRot = W.localRotation;
        var roots = WeaponRoots(m);
        Vector3 target = SliceTarget(roots[0], LocalVerts(roots[0]), 0.6f);
        var sweepNs = new List<long>(); var allocQuiet = new List<long>(); var allocOff = new List<long>(); var allocAll = new List<long>();
        using (var sweepRec = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Scripts, "HitDetection.Sweep"))
        using (var gcRec = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Memory, "GC Allocated In Frame"))
        {
            Time.captureDeltaTime = 1f / 72f;
            int round = 0;
            foreach (float v in new[] { 8f, 25f, 8f, 25f, 8f, 25f, 8f, 25f, 8f, 25f, 8f, 25f })
            {
                bool on = (round++ / 2) % 2 == 0; // rounds of two passes, sweep on / off alternately (GC difference = the sweep's)
                hd.sweptDetection = on;
                Vector3 dir = m.transform.right, offset = col.bounds.center - W.position;
                Vector3 a = target - dir * 0.9f, b = target + dir * 0.9f;
                W.position = a - offset; ResetSweep(hd); yield return Frames(4); ResetContact(hd);
                float t0 = Time.time, dur = Vector3.Distance(a, b) / v;
                while (Time.time - t0 < dur)
                {
                    W.position = Vector3.Lerp(a, b, (Time.time - t0) / dur) - offset;
                    int ev0 = clashCount + hitCount; probe = hd;
                    yield return null;
                    if (on) sweepNs.Add(sweepRec.LastValue);
                    allocAll.Add(gcRec.LastValue);
                    if (clashCount + hitCount == ev0) (on ? allocQuiet : allocOff).Add(gcRec.LastValue);
                }
                probe = null;
                W.localPosition = restLocal; W.localRotation = restRot; ResetSweep(hd); yield return Frames(3);
            }
        }
        sweepNs.Sort(); allocQuiet.Sort(); allocOff.Sort(); allocAll.Sort(); hd.sweptDetection = true;
        long P(List<long> l, float q) => l.Count == 0 ? -1 : l[Mathf.Clamp((int)(l.Count * q), 0, l.Count - 1)];
        log.AppendLine($"PERF {m.name} ({sweepNs.Count} frames of passes at 8 / 25 m/s through the weapon, Editor Mono): sweep median {P(sweepNs, 0.5f) / 1000f:0.0} us, p95 {P(sweepNs, 0.95f) / 1000f:0.0} us, max {P(sweepNs, 1f) / 1000f:0.0} us per weapon per frame | GC alloc per frame without a contact event (Editor, includes the Editor's own): sweep ON median {P(allocQuiet, 0.5f)} B ({allocQuiet.Count} frames), sweep OFF median {P(allocOff, 0.5f)} B ({allocOff.Count} frames) => the sweep's own {P(allocQuiet, 0.5f) - P(allocOff, 0.5f)} B");
        HitDetection.WeaponClashed -= OnClash; HitDetection.HitLanded -= OnHit;
    }

    // ───────────────────────── fit: collider vs visible mesh ─────────────────────────
    static IEnumerator Fit()
    {
        yield return StartFight();
        var m = M;
        if (m == null) { log.AppendLine("no monster"); yield break; }
        yield return Freeze(m);
        var anim = m.GetComponentInChildren<Animator>();
        anim.enabled = true;
        var ai = m.GetComponent<MonsterAI>();
        var poses = new List<KeyValuePair<string, float>>(); // state name, seconds
        poses.Add(new KeyValuePair<string, float>("Idle", 0.3f));
        foreach (var a in ai.attackTable)
            if (!string.IsNullOrEmpty(a.animatorState)) poses.Add(new KeyValuePair<string, float>(a.animatorState, a.windUp));
        var roots = WeaponRoots(m);
        var sb = new StringBuilder();
        foreach (var root in roots)
        {
            var cols = new List<Collider>();
            foreach (var c in root.GetComponentsInChildren<Collider>(true)) if (c.CompareTag("MonsterWeapon") && c.enabled) cols.Add(c);
            var rends = new List<Renderer>();
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true)) { var r = mf.GetComponent<MeshRenderer>(); if (r != null && r.enabled) rends.Add(r); }
            var lverts = LocalVerts(root);
            Vector3 lmn = Vector3.one * 1e9f, lmx = -lmn; foreach (var v in lverts) { lmn = Vector3.Min(lmn, v); lmx = Vector3.Max(lmx, v); }
            float worstUncovered = 0f, worstExcess = 0f, worstAabb = 0f; string worstPose = "";
            foreach (var pose in poses)
            {
                anim.Play(pose.Key, 0, 0f); anim.Update(0f);
                float len = anim.GetCurrentAnimatorStateInfo(0).length;
                anim.Play(pose.Key, 0, len > 0f ? Mathf.Clamp01(pose.Value / len) : 0f); anim.Update(0f);
                Physics.SyncTransforms();
                // Visible vertices outside every weapon collider (m).
                float unc = 0f;
                foreach (var lv in lverts)
                {
                    Vector3 w = root.TransformPoint(lv);
                    float best = float.MaxValue;
                    foreach (var c in cols) { float d = Vector3.Distance(w, c.ClosestPoint(w)); if (d < best) best = d; }
                    if (best > unc) unc = best;
                }
                // Collider sample points outside the visible mesh's local box (m).
                float exc = 0f;
                foreach (var c in cols) foreach (var p in SamplePoints(c))
                {
                    Vector3 lp = root.InverseTransformPoint(p);
                    Vector3 cl = Vector3.Max(lmn, Vector3.Min(lmx, lp));
                    float d = Vector3.Distance(root.TransformPoint(cl), p);
                    if (d > exc) exc = d;
                }
                // World AABB of renderers vs colliders.
                Bounds rb = rends[0].bounds; foreach (var r in rends) rb.Encapsulate(r.bounds);
                Bounds cb = cols[0].bounds; foreach (var c in cols) cb.Encapsulate(c.bounds);
                float aabb = Mathf.Max((rb.min - cb.min).magnitude, (rb.max - cb.max).magnitude);
                if (unc > worstUncovered) { worstUncovered = unc; worstPose = pose.Key; }
                worstExcess = Mathf.Max(worstExcess, exc); worstAabb = Mathf.Max(worstAabb, aabb);
                if (verbose) sb.AppendLine($"   {root.name} {pose.Key,-24} uncovered {unc * 100f:0.0} cm  excess {exc * 100f:0.0} cm  aabb-corner {aabb * 100f:0.0} cm");
                yield return null;
            }
            sb.AppendLine($"{root.name}: {cols.Count} colliders, {lverts.Count} verts, {poses.Count} poses: max visible mesh outside collider {worstUncovered * 100f:0.0} cm ({worstPose}), max collider beyond mesh box {worstExcess * 100f:0.0} cm, max world-AABB corner deviation {worstAabb * 100f:0.0} cm");
        }
        anim.enabled = false;
        log.Append(sb);
    }

    static IEnumerable<Vector3> SamplePoints(Collider c)
    {
        var t = c.transform;
        if (c is BoxCollider b)
        {
            Vector3 h = b.size * 0.5f;
            for (int x = -1; x <= 1; x++) for (int y = -1; y <= 1; y++) for (int z = -1; z <= 1; z++)
                yield return t.TransformPoint(b.center + new Vector3(h.x * x, h.y * y, h.z * z));
        }
        else if (c is CapsuleCollider cc)
        {
            Vector3 axis = cc.direction == 0 ? Vector3.right : cc.direction == 1 ? Vector3.up : Vector3.forward;
            Vector3 p1 = axis == Vector3.right ? Vector3.up : Vector3.right, p2 = Vector3.Cross(axis, p1);
            for (int k = 0; k <= 4; k++)
            {
                Vector3 q = cc.center + axis * ((cc.height * 0.5f - cc.radius) * (k / 2f - 1f));
                yield return t.TransformPoint(q + p1 * cc.radius); yield return t.TransformPoint(q - p1 * cc.radius);
                yield return t.TransformPoint(q + p2 * cc.radius); yield return t.TransformPoint(q - p2 * cc.radius);
            }
            yield return t.TransformPoint(cc.center + axis * cc.height * 0.5f); yield return t.TransformPoint(cc.center - axis * cc.height * 0.5f);
        }
        else yield return c.bounds.center;
    }
}
