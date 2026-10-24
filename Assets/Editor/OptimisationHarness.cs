// OptimisationHarness.cs
// Purpose: Editor measurements for the Quest optimisation pass (docs/performance-audit.md, "measured in the Editor, not on a
//          Quest"). In Play mode on a level: Measure() starts a fight, keeps the idle player alive (test only, nothing saved),
//          and records 60 s of game time: main-thread and PlayerLoop ms, batches, draw calls, SetPass calls, triangles, shadow
//          casters, realtime lights, live particles and GC allocated per frame. Shots() renders fixed views from the player's
//          head (four directions before the fight, the monster in the fight, Level 3 also enraged) for before/after look checks.
//          Telemetry: arm EditorTelemetryGuard before entering Play mode (every SessionReporter goes to a dead local address).
// Attach to: nothing (static, Editor only). Poll Status() until DONE.

using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.Profiling;
using UnityEditor;
using UnityEngine;

public static class OptimisationHarness
{
    static IEnumerator job;
    static readonly StringBuilder log = new StringBuilder();
    static int lastFrame = -1;
    public static string Status() => (job != null ? "RUNNING\n" : "IDLE\n") + log;

    [MenuItem("StrikeFit/Test/Arm telemetry guard (dead local reporter on every Play)")]
    public static string ArmGuard() { EditorTelemetryGuard.Arm(); return "telemetry guard armed: " + EditorTelemetryGuard.DeadUrl; }
    [MenuItem("StrikeFit/Test/Disarm telemetry guard")]
    public static string DisarmGuard() { EditorTelemetryGuard.Disarm(); return "telemetry guard disarmed"; }

    public static string Measure(float seconds = 60f, string weapon = "Flail", string tag = "")
    {
        if (!EditorApplication.isPlaying) return "not playing";
        return Start(MeasureJob(seconds, weapon, tag));
    }
    public static string Shots(string dir, string tag, string weapon = "Flail")
    {
        if (!EditorApplication.isPlaying) return "not playing";
        return Start(ShotsJob(dir, tag, weapon));
    }

    public static string MonsterLook()
    {
        if (!EditorApplication.isPlaying) return "not playing";
        return Start(MonsterLookJob());
    }

    // mean luminance (0..255) of the monster's own pixels: monster layer only, black background, same camera pose
    static string MonsterLum(MonsterController m, Transform head)
    {
        var layers = 0; foreach (var r in m.GetComponentsInChildren<Renderer>()) if (r.enabled) layers |= 1 << r.gameObject.layer;
        var go = new GameObject("ZZOptLumCam"); var cam = go.AddComponent<Camera>();
        if (Camera.main != null) { cam.CopyFrom(Camera.main); }
        cam.cullingMask = layers; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black; cam.fieldOfView = 40f;
        go.transform.SetPositionAndRotation(head.position, Quaternion.LookRotation((m.transform.position + Vector3.up * 1.2f) - head.position));
        var rt = new RenderTexture(320, 320, 24, RenderTextureFormat.ARGB32); cam.targetTexture = rt; cam.Render(); cam.Render();
        var prev = RenderTexture.active; RenderTexture.active = rt;
        var tex = new Texture2D(320, 320, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, 320, 320), 0, 0); tex.Apply(); RenderTexture.active = prev;
        double sum = 0, rs = 0, gs = 0, bs = 0; int n = 0;
        foreach (var c in tex.GetPixels32()) { int l = c.r + c.g + c.b; if (l < 6) continue; n++; sum += 0.2126 * c.r + 0.7152 * c.g + 0.0722 * c.b; rs += c.r; gs += c.g; bs += c.b; }
        cam.targetTexture = null; rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(tex); Object.DestroyImmediate(go);
        return n == 0 ? "no pixels" : $"lum {sum / n:0.0} rgb ({rs / n:0},{gs / n:0},{bs / n:0}) px {n}";
    }

    // Same frame, same pose: the monster as baked, then with every baked light switched back to realtime and light probes off on
    // the monster (the pre-bake lighting). Runtime only; nothing is saved.
    static string AB(List<Light> baked, Transform head)
    {
        string withBake = MonsterLum(mon, head);
        var outs = new LightBakingOutput[baked.Count];
        for (int i = 0; i < baked.Count; i++)
        {
            outs[i] = baked[i].bakingOutput;
            baked[i].bakingOutput = new LightBakingOutput { isBaked = false, lightmapBakeType = LightmapBakeType.Realtime, mixedLightingMode = MixedLightingMode.IndirectOnly, occlusionMaskChannel = -1, probeOcclusionLightIndex = -1 };
        }
        var rends = mon.GetComponentsInChildren<Renderer>();
        var usage = new UnityEngine.Rendering.LightProbeUsage[rends.Length];
        for (int i = 0; i < rends.Length; i++) { usage[i] = rends[i].lightProbeUsage; rends[i].lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off; }
        string withoutBake = MonsterLum(mon, head);
        for (int i = 0; i < baked.Count; i++) baked[i].bakingOutput = outs[i];
        for (int i = 0; i < rends.Length; i++) rends[i].lightProbeUsage = usage[i];
        return $"baked: {withBake} || pre-bake lighting: {withoutBake}";
    }

    static IEnumerator MonsterLookJob()
    {
        var b = BeginFight("Sword", true); while (b.MoveNext()) yield return b.Current;
        if (mon == null) { log.AppendLine("no monster"); yield break; }
        var head = Head();
        var baked = new List<Light>();
        foreach (var l in Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            if (l.bakingOutput.isBaked && l.bakingOutput.lightmapBakeType == LightmapBakeType.Baked) baked.Add(l);
        log.AppendLine($"baked lights {baked.Count}, lightmaps {LightmapSettings.lightmaps.Length}, probes {(LightmapSettings.lightProbes != null ? LightmapSettings.lightProbes.count : 0)}");
        var ai = mon.GetComponent<MonsterAI>(); if (ai != null) ai.enabled = false; // hold the idle pose for the idle sample
        for (int i = 0; i < 3; i++) yield return null;
        log.AppendLine("idle      " + AB(baked, head));
        if (ai != null) ai.enabled = true;
        int heals = 0; float t0 = Time.time; int got = 0;
        while (Time.time - t0 < 40f && got < 3)
        {
            yield return null; KeepPlayerAlive(ref heals);
            if (mon.isAttacking) { for (int i = 0; i < 6; i++) yield return null; log.AppendLine("telegraph " + AB(baked, head)); got++; while (mon.isAttacking) yield return null; }
        }
    }

    /// <summary>Who allocates GC memory inside PlayerLoop: self allocation per marker path, summed over `seconds` of fight.</summary>
    public static string GcTop(float seconds = 10f, string weapon = "Sword")
    {
        if (!EditorApplication.isPlaying) return "not playing";
        return Start(GcTopJob(seconds, weapon));
    }

    static IEnumerator GcTopJob(float seconds, string weapon)
    {
        var b = BeginFight(weapon, true); while (b.MoveNext()) yield return b.Current;
        UnityEditorInternal.ProfilerDriver.ClearAllFrames();
        UnityEditorInternal.ProfilerDriver.profileEditor = false;
        UnityEditorInternal.ProfilerDriver.enabled = true;
        float end = Time.time + seconds; int heals = 0;
        while (Time.time < end) { yield return null; KeepPlayerAlive(ref heals); }
        UnityEditorInternal.ProfilerDriver.enabled = false;
        var self = new Dictionary<string, double>(); var calls = new Dictionary<string, int>(); int frames = 0; double total = 0;
        double worst = -1; int worstFrame = -1;
        for (int f = UnityEditorInternal.ProfilerDriver.firstFrameIndex; f >= 0 && f <= UnityEditorInternal.ProfilerDriver.lastFrameIndex; f++)
        {
            using (var v = UnityEditorInternal.ProfilerDriver.GetHierarchyFrameDataView(f, 0, UnityEditor.Profiling.HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName, UnityEditor.Profiling.HierarchyFrameDataView.columnGcMemory, false))
            {
                if (v == null || !v.valid) continue;
                frames++;
                var kids = new List<int>(); v.GetItemChildren(v.GetRootItemID(), kids);
                foreach (var id in kids) if (v.GetItemName(id) == "PlayerLoop")
                {
                    double g = v.GetItemColumnDataAsFloat(id, UnityEditor.Profiling.HierarchyFrameDataView.columnGcMemory);
                    total += g; if (g > worst) { worst = g; worstFrame = f; }
                    Walk(v, id, "", self, calls);
                }
            }
        }
        var list = new List<KeyValuePair<string, double>>(self); list.Sort((x, y) => y.Value.CompareTo(x.Value));
        log.AppendLine($"== GC in PlayerLoop over {frames} frames: {total / Mathf.Max(1, frames):0} B/frame. Top self allocations (B/frame, frames seen):");
        for (int i = 0; i < Mathf.Min(18, list.Count); i++) log.AppendLine($"  {list[i].Value / frames,8:0}  {calls[list[i].Key],5}  {list[i].Key}");
        if (worstFrame < 0) yield break;
        var ws = new Dictionary<string, double>(); var wc = new Dictionary<string, int>();
        using (var v = UnityEditorInternal.ProfilerDriver.GetHierarchyFrameDataView(worstFrame, 0, UnityEditor.Profiling.HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName, UnityEditor.Profiling.HierarchyFrameDataView.columnGcMemory, false))
        {
            var kids = new List<int>(); v.GetItemChildren(v.GetRootItemID(), kids);
            foreach (var id in kids) if (v.GetItemName(id) == "PlayerLoop") Walk(v, id, "", ws, wc);
        }
        var wl = new List<KeyValuePair<string, double>>(ws); wl.Sort((x, y) => y.Value.CompareTo(x.Value));
        log.AppendLine($"== worst frame ({worst:0} B):");
        for (int i = 0; i < Mathf.Min(8, wl.Count); i++) log.AppendLine($"  {wl[i].Value,8:0}  {wl[i].Key}");
    }

    static void Walk(UnityEditor.Profiling.HierarchyFrameDataView v, int id, string path, Dictionary<string, double> self, Dictionary<string, int> calls)
    {
        string name = v.GetItemName(id);
        string p = path.Length == 0 ? name : path + " > " + name;
        double gc = v.GetItemColumnDataAsFloat(id, UnityEditor.Profiling.HierarchyFrameDataView.columnGcMemory);
        if (gc <= 0) return;
        var kids = new List<int>(); v.GetItemChildren(id, kids);
        double childGc = 0; foreach (var k in kids) childGc += v.GetItemColumnDataAsFloat(k, UnityEditor.Profiling.HierarchyFrameDataView.columnGcMemory);
        double own = gc - childGc;
        if (own > 0.5)
        {
            // keep the last three levels of the path: enough to name the script and the call
            var parts = p.Split(new[] { " > " }, System.StringSplitOptions.None);
            string key = string.Join(" > ", parts, Mathf.Max(0, parts.Length - 3), Mathf.Min(3, parts.Length));
            self[key] = (self.TryGetValue(key, out var a) ? a : 0) + own;
            calls[key] = (calls.TryGetValue(key, out var c) ? c : 0) + 1;
        }
        foreach (var k in kids) Walk(v, k, p, self, calls);
    }

    /// <summary>Colour system: during a real attack wind-up, fire the strongest hit flash and check, every frame of the flash, that the
    /// monster's body colour stays closer (in RGB direction) to the telegraph than to the flash. Saves the frame at the flash peak.</summary>
    public static string ColorCheck(string dir, string weapon)
    {
        if (!EditorApplication.isPlaying) return "not playing";
        return Start(ColorCheckJob(dir, weapon));
    }

    static float Cos(Color a, Color b)
    {
        var x = new Vector3(a.r, a.g, a.b); var y = new Vector3(b.r, b.g, b.b);
        float d = x.magnitude * y.magnitude; return d < 1e-5f ? 0f : Vector3.Dot(x, y) / d;
    }

    static IEnumerator ColorCheckJob(string dir, string weapon)
    {
        string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        var b = BeginFight(weapon, true); while (b.MoveNext()) yield return b.Current;
        if (mon == null) { log.AppendLine("no monster"); yield break; }
        var comp = mon.GetComponent<MonsterColorCompositor>();
        if (comp == null) { log.AppendLine("no MonsterColorCompositor"); yield break; }
        var head = Head(); int heals = 0; float t0 = Time.time;
        RenderMonsterOnly($"{dir}/{scene}_idle_monster_only.png", head);
        while (Time.time - t0 < 30f && !comp.Stack.IsActive(StrikeFit.ColorFx.ColorLayer.Telegraph, Time.time)) { yield return null; KeepPlayerAlive(ref heals); }
        if (!comp.Stack.IsActive(StrikeFit.ColorFx.ColorLayer.Telegraph, Time.time)) { log.AppendLine("no telegraph within 30 s"); yield break; }
        for (int i = 0; i < 3; i++) yield return null;
        comp.TryGetGroupResult(StrikeFit.ColorFx.ColorGroup.Body, out var tele);
        comp.palette.Tier(StrikeFit.ColorFx.SwingTierFx.Full, out var hitColor, out var hitI, out var hitS);
        int hitsBefore = comp.Hits;
        comp.NotifyDamage(CombatUIController.DamageStyle.Full);
        int frames = 0, teleWins = 0; float worstMargin = 1f; bool shot = false;
        while (comp.Stack.IsActive(StrikeFit.ColorFx.ColorLayer.Hit, Time.time) && comp.Stack.IsActive(StrikeFit.ColorFx.ColorLayer.Telegraph, Time.time) && frames < 60)
        {
            yield return null;
            comp.TryGetGroupResult(StrikeFit.ColorFx.ColorGroup.Body, out var r);
            float toTele = Cos(r.emission, tele.emission), toHit = Cos(r.emission, hitColor);
            frames++; if (toTele >= toHit) teleWins++;
            worstMargin = Mathf.Min(worstMargin, toTele - toHit);
            if (!shot)
            {
                Render($"{dir}/{scene}_telegraph_during_hitflash.png", head.position, Quaternion.LookRotation((mon.transform.position + Vector3.up * 1.2f) - head.position), 45f);
                RenderMonsterOnly($"{dir}/{scene}_telegraph_during_hitflash_monster_only.png", head);
                shot = true;
            }
        }
        log.AppendLine($"== COLOUR {scene} {mon.name}: telegraph emission {ColorUtility.ToHtmlStringRGB(tele.emission)} tint {ColorUtility.ToHtmlStringRGB(tele.baseTint)}, full-power flash colour {ColorUtility.ToHtmlStringRGB(hitColor)} ({hitS:0.00} s), hits counted {comp.Hits - hitsBefore}");
        log.AppendLine($"  frames with telegraph + flash together: {frames}; body colour closer to the telegraph than to the flash in {teleWins}/{frames} (worst margin {worstMargin:0.000}, cosine of RGB direction)");
    }

    static string Start(IEnumerator j)
    {
        Stop(); log.Length = 0; job = j;
        EditorApplication.update += Tick;
        return "started";
    }
    public static void Stop() { job = null; EditorApplication.update -= Tick; }
    static void Tick()
    {
        if (job == null || !EditorApplication.isPlaying) { Stop(); return; }
        if (Time.frameCount == lastFrame) return;
        lastFrame = Time.frameCount;
        try { if (!job.MoveNext()) { log.AppendLine("DONE"); Stop(); } }
        catch (System.Exception e) { log.AppendLine("EXCEPTION " + e); Stop(); }
    }

    // ── fight set-up shared by both jobs ──
    static MonsterController mon;
    static IEnumerator BeginFight(string weapon, bool start)
    {
        Application.runInBackground = true;
        Time.captureDeltaTime = 1f / 30f;
        var pnp = Object.FindAnyObjectByType<PlayerNamePicker>(FindObjectsInactive.Include);
        if (pnp != null)
        {
            pnp.enabled = false;
            var f = typeof(PlayerNamePicker).GetField("panel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var panel = f != null ? f.GetValue(pnp) as GameObject : null;
            if (panel != null) panel.SetActive(false);
        }
        foreach (var r in Object.FindObjectsByType<SessionReporter>(FindObjectsInactive.Include))
            if (r.baseUrl != EditorTelemetryGuard.DeadUrl) { log.AppendLine("WARNING reporter was " + r.baseUrl + " (guard not armed?) -> redirected now"); r.baseUrl = EditorTelemetryGuard.DeadUrl; }
        var pc = PlayerCombat.Instance;
        if (pc != null && pc.weaponSets != null && pc.weaponSets.Count > 0 && System.Enum.TryParse(weapon, out WeaponType wt))
            log.AppendLine("equip " + wt + " = " + pc.EquipWeaponSet(wt));
        yield return null; yield return null;
        if (!start) yield break;
        BattleManager.Instance.StartBattleFromInspector();
        float w0 = Time.time;
        mon = null;
        while (Time.time - w0 < 30f)
        {
            mon = BattleManager.Instance.ActiveMonster;
            if (mon != null && mon.isAlive) { var e = mon.GetComponent<MonsterEntrance>(); if (e == null || e.Complete) break; }
            yield return null;
        }
        for (int i = 0; i < 15; i++) yield return null;
        log.AppendLine("fight ready: monster=" + (mon != null ? mon.name : "none") + " t=" + Time.time.ToString("0.0"));
    }

    static void KeepPlayerAlive(ref int heals)
    {
        var ph = PlayerHealth.Instance;
        if (ph != null && ph.currentHealth < ph.maxHealth * 0.4f) { ph.FullHeal(); heals++; }
    }

    static int RealtimeLights(out int mixed, out int baked)
    {
        int rt = 0; mixed = 0; baked = 0;
        foreach (var l in Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (!l.enabled || l.intensity <= 0f) continue;
            if (l.lightmapBakeType == LightmapBakeType.Baked) { baked++; continue; }
            if (l.lightmapBakeType == LightmapBakeType.Mixed) mixed++;
            rt++;
        }
        return rt;
    }

    static IEnumerator MeasureJob(float seconds, string weapon, string tag)
    {
        string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        var b = BeginFight(weapon, true); while (b.MoveNext()) yield return b.Current;
        if (mon == null) { log.AppendLine("no monster"); yield break; }

        int attacks = 0;
        System.Action<MonsterAI, string, string> onAtk = (a, x, y) => attacks++;
        MonsterAI.AttackStarted += onAtk;
        var main = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 1);
        var loop = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "PlayerLoop", 1);
        var gc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", 1);
        string[] dcNames = { "Standard Draw Calls Count", "Standard Indirect Draw Calls Count", "Standard Instanced Draw Calls Count", "SRP Batcher Draw Calls Count", "BRG Draw Calls Count", "BRG Indirect Draw Calls Count" };
        var rDcs = new ProfilerRecorder[dcNames.Length];
        for (int i = 0; i < dcNames.Length; i++) rDcs[i] = ProfilerRecorder.StartNew(ProfilerCategory.Render, dcNames[i], 1);
        var rSrp = rDcs[3];
        var rGpu = ProfilerRecorder.StartNew(ProfilerCategory.Render, "GPU Frame Time", 1);
        var rRt = ProfilerRecorder.StartNew(ProfilerCategory.Render, "CPU Render Thread Frame Time", 1);
        var gpuMs = new List<double>(); var rtMs = new List<double>();
        UnityEditorInternal.ProfilerDriver.ClearAllFrames();
        UnityEditorInternal.ProfilerDriver.profileEditor = false;
        UnityEditorInternal.ProfilerDriver.enabled = true;
        int rendered = 0;
        var rSp = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count", 1);
        var rTri = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count", 1);
        var rSc = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Shadow Casters Count", 1);
        var mainMs = new List<double>(); var loopMs = new List<double>(); var gcB = new List<long>();
        double batches = 0, dc = 0, sp = 0, tris = 0, sc = 0; int n = 0, heals = 0, maxParts = 0, rtMax = 0, rtMin = 999, mixedMax = 0, bakedMax = 0;
        double maxBatches = 0, maxSp = 0, maxTris = 0, maxSc = 0;
        float end = Time.time + seconds;
        long frames0 = Time.frameCount; double wall0 = EditorApplication.timeSinceStartup;
        yield return null;
        while (Time.time < end)
        {
            yield return null;
            KeepPlayerAlive(ref heals);
            if (!BattleManager.Instance.IsBattleActive) { log.AppendLine("battle ended early at t=" + Time.time.ToString("0.0")); break; }
            n++;
            if (main.Valid && main.LastValue > 0) mainMs.Add(main.LastValue / 1e6);
            if (loop.Valid && loop.LastValue > 0) loopMs.Add(loop.LastValue / 1e6);
            if (gc.Valid) gcB.Add(gc.LastValue);
            long vd = 0; foreach (var r in rDcs) if (r.Valid) vd += r.LastValue;
            long vb = rSrp.Valid ? rSrp.LastValue : 0, vs = rSp.LastValue, vt = rTri.LastValue, vc = rSc.LastValue;
            if (rGpu.Valid && rGpu.LastValue > 0) gpuMs.Add(rGpu.LastValue / 1e6);
            if (rRt.Valid && rRt.LastValue > 0) rtMs.Add(rRt.LastValue / 1e6);
            if (vt <= 0) continue; // a frame the Game view did not render
            rendered++;
            batches += vb; dc += vd; sp += vs; tris += vt; sc += vc;
            maxBatches = System.Math.Max(maxBatches, vb); maxSp = System.Math.Max(maxSp, vs);
            maxTris = System.Math.Max(maxTris, vt); maxSc = System.Math.Max(maxSc, vc);
            if (n % 15 == 1)
            {
                int mx, bk; int rt = RealtimeLights(out mx, out bk);
                rtMax = Mathf.Max(rtMax, rt); rtMin = Mathf.Min(rtMin, rt); mixedMax = Mathf.Max(mixedMax, mx); bakedMax = Mathf.Max(bakedMax, bk);
                int pc = 0; foreach (var ps in Object.FindObjectsByType<ParticleSystem>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)) pc += ps.particleCount;
                maxParts = Mathf.Max(maxParts, pc);
            }
        }
        MonsterAI.AttackStarted -= onAtk;
        UnityEditorInternal.ProfilerDriver.enabled = false;
        // game-only GC and time: the PlayerLoop entry of each captured profiler frame (the Editor UI is outside it)
        var plGc = new List<double>(); var plMs = new List<double>();
        for (int f = UnityEditorInternal.ProfilerDriver.firstFrameIndex; f >= 0 && f <= UnityEditorInternal.ProfilerDriver.lastFrameIndex; f++)
        {
            using (var v = UnityEditorInternal.ProfilerDriver.GetHierarchyFrameDataView(f, 0, UnityEditor.Profiling.HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName, UnityEditor.Profiling.HierarchyFrameDataView.columnGcMemory, false))
            {
                if (v == null || !v.valid) continue;
                var kids = new List<int>(); v.GetItemChildren(v.GetRootItemID(), kids);
                foreach (var id in kids)
                    if (v.GetItemName(id) == "PlayerLoop") { plGc.Add(v.GetItemColumnDataAsFloat(id, UnityEditor.Profiling.HierarchyFrameDataView.columnGcMemory)); plMs.Add(v.GetItemColumnDataAsFloat(id, UnityEditor.Profiling.HierarchyFrameDataView.columnTotalTime)); }
            }
        }
        log.AppendLine($"  recorders valid: main {main.Valid} loop {loop.Valid} gc {gc.Valid} srp {rSrp.Valid} setpass {rSp.Valid} tris {rTri.Valid} shadows {rSc.Valid} gpu {rGpu.Valid} renderThread {rRt.Valid}; rendered frames {rendered}/{n}; profiler frames {plGc.Count}");
        main.Dispose(); loop.Dispose(); gc.Dispose(); foreach (var r in rDcs) r.Dispose(); rSp.Dispose(); rTri.Dispose(); rSc.Dispose(); rGpu.Dispose(); rRt.Dispose();
        double wall = EditorApplication.timeSinceStartup - wall0;

        string Stat(List<double> v)
        {
            if (v.Count == 0) return "n/a";
            var s = new List<double>(v); s.Sort();
            double sum = 0; foreach (var x in s) sum += x;
            return $"avg {sum / s.Count:0.00} / p95 {s[(int)(s.Count * 0.95)]:0.00} / worst {s[s.Count - 1]:0.00} ms";
        }
        long gcSum = 0, gcMax = 0; int gcFrames = 0; foreach (var g in gcB) { gcSum += g; gcMax = System.Math.Max(gcMax, g); if (g > 0) gcFrames++; }
        string A(double v) => (rendered > 0 ? v / rendered : 0).ToString("0");
        double plGcAvg = 0, plGcMax = 0; int plGcFrames = 0; foreach (var g in plGc) { plGcAvg += g; plGcMax = System.Math.Max(plGcMax, g); if (g > 0) plGcFrames++; }
        if (plGc.Count > 0) plGcAvg /= plGc.Count;
        log.AppendLine($"== MEASURE {scene} {tag} weapon={weapon}: {n} frames over {seconds:0} s game time ({wall:0} s wall, dt 1/30), monster attacks {attacks}, player kept alive {heals}x");
        log.AppendLine($"  main thread: {Stat(mainMs)}");
        log.AppendLine($"  PlayerLoop : {Stat(loopMs)}");
        log.AppendLine($"  GPU frame  : {Stat(gpuMs)} | render thread: {Stat(rtMs)}");
        log.AppendLine($"  PlayerLoop (profiler, last {plMs.Count} frames): {Stat(plMs)}");
        log.AppendLine($"  draw calls avg {A(dc)} (SRP-batched {A(batches)}) | SetPass avg {A(sp)} max {maxSp:0} | triangles avg {A(tris)} max {maxTris:0} | shadow casters avg {A(sc)} max {maxSc:0}");
        log.AppendLine($"  GC in PlayerLoop (game only, profiler): avg {plGcAvg:0} B/frame, max {plGcMax:0} B, frames with any alloc {plGcFrames}/{plGc.Count}");
        log.AppendLine($"  realtime lights {rtMin}-{rtMax} (of which mixed {mixedMax}), baked {bakedMax} | live particles max {maxParts}");
        log.AppendLine($"  GC whole Editor process per frame: avg {(gcB.Count > 0 ? gcSum / gcB.Count : 0)} B, max {gcMax} B, frames with any alloc {gcFrames}/{gcB.Count}");
        log.AppendLine($"CSV|{scene}|{tag}|{n}|loop {Stat(loopMs)}|gpu {Stat(gpuMs)}|dc {A(dc)}|setpass {A(sp)}|tris {A(tris)}|shadowcasters {A(sc)}|lights {rtMin}-{rtMax}|gc {plGcAvg:0}/{plGcMax:0}|attacks {attacks}");
    }

    // ── fixed views from the player's head ──
    static Transform Head()
    {
        var rig = Object.FindAnyObjectByType<OVRCameraRig>();
        if (rig != null && rig.centerEyeAnchor != null) return rig.centerEyeAnchor;
        return Camera.main != null ? Camera.main.transform : null;
    }

    static void Render(string file, Vector3 pos, Quaternion rot, float fov = 70f)
    {
        var src = Camera.main;
        var go = new GameObject("ZZOptShotCam");
        var cam = go.AddComponent<Camera>();
        if (src != null) { cam.CopyFrom(src); }
        cam.fieldOfView = fov; cam.nearClipPlane = 0.05f; cam.farClipPlane = 300f;
        go.transform.SetPositionAndRotation(pos, rot);
        var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        cam.targetTexture = rt;
        cam.Render(); cam.Render(); // the first render of a new variant can be transient
        var prev = RenderTexture.active; RenderTexture.active = rt;
        var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); tex.Apply();
        RenderTexture.active = prev;
        Directory.CreateDirectory(Path.GetDirectoryName(file));
        File.WriteAllBytes(file, tex.EncodeToPNG());
        cam.targetTexture = null; rt.Release();
        Object.DestroyImmediate(rt); Object.DestroyImmediate(tex); Object.DestroyImmediate(go);
    }

    // The monster's own layers on black from the head (the player's glowing weapons can fill the normal view).
    static void RenderMonsterOnly(string file, Transform head)
    {
        int layers = 0; foreach (var r in mon.GetComponentsInChildren<Renderer>()) if (r.enabled) layers |= 1 << r.gameObject.layer;
        var go = new GameObject("ZZOptMonCam"); var cam = go.AddComponent<Camera>();
        if (Camera.main != null) cam.CopyFrom(Camera.main);
        cam.cullingMask = layers; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black; cam.fieldOfView = 45f;
        go.transform.SetPositionAndRotation(head.position, Quaternion.LookRotation((mon.transform.position + Vector3.up * 1.2f) - head.position));
        var rt = new RenderTexture(960, 720, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 }; cam.targetTexture = rt; cam.Render(); cam.Render();
        var prev = RenderTexture.active; RenderTexture.active = rt;
        var tex = new Texture2D(960, 720, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, 960, 720), 0, 0); tex.Apply(); RenderTexture.active = prev;
        File.WriteAllBytes(file, tex.EncodeToPNG());
        cam.targetTexture = null; rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(tex); Object.DestroyImmediate(go);
    }

    static IEnumerator ShotsJob(string dir, string tag, string weapon)
    {
        string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        var b = BeginFight(weapon, false); while (b.MoveNext()) yield return b.Current;
        for (int i = 0; i < 20; i++) yield return null;
        var head = Head();
        if (head == null) { log.AppendLine("no head"); yield break; }
        Vector3 p = head.position; float yaw = head.eulerAngles.y;
        string[] names = { "front", "right", "back", "left" };
        for (int k = 0; k < 4; k++) Render($"{dir}/{scene}_{tag}_{names[k]}.png", p, Quaternion.Euler(8f, yaw + 90f * k, 0f));
        Render($"{dir}/{scene}_{tag}_up.png", p, Quaternion.Euler(-35f, yaw, 0f), 80f);
        log.AppendLine("arena shots done");

        var f = BeginFight(weapon, true); while (f.MoveNext()) yield return f.Current;
        if (mon == null) { log.AppendLine("no monster"); yield break; }
        int heals = 0;
        // the monster as the player sees it: wait for an attack wind-up (telegraph colour), then shoot
        var ai = mon.GetComponent<MonsterAI>();
        float t0 = Time.time; bool shotAtk = false;
        while (Time.time - t0 < 25f && !shotAtk)
        {
            yield return null; KeepPlayerAlive(ref heals);
            if (mon.isAttacking) { for (int i = 0; i < 6; i++) yield return null; Render($"{dir}/{scene}_{tag}_telegraph.png", head.position, Quaternion.LookRotation((mon.transform.position + Vector3.up * 1.3f) - head.position)); shotAtk = true; }
        }
        Render($"{dir}/{scene}_{tag}_monster.png", head.position, Quaternion.LookRotation((mon.transform.position + Vector3.up * 1.2f) - head.position));
        if (scene == "Level3_Hard")
        {
            var h = mon.GetComponent<MonsterHealth>();
            float d = h.currentHealth - h.maxHealth * 0.3f;
            if (d > 0f) h.TakeDamage(d, mon.transform.position + Vector3.up);
            float t1 = Time.time; while (Time.time - t1 < 8f) { yield return null; KeepPlayerAlive(ref heals); }
            Render($"{dir}/{scene}_{tag}_enraged.png", head.position, Quaternion.LookRotation((mon.transform.position + Vector3.up * 1.2f) - head.position));
            Render($"{dir}/{scene}_{tag}_enraged_back.png", head.position, Quaternion.Euler(8f, yaw + 180f, 0f));
        }
        log.AppendLine("fight shots done (telegraph shot " + shotAtk + ")");
    }
}
