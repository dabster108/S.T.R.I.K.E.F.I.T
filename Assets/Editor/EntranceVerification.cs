// EntranceVerification.cs
// Purpose: Play-mode probes for the Level 2 drop entrance (Editor only, never in a build). Driven over Unity MCP.
//          Jobs: "full"  start the battle, log every step's real time, capture the Game view from the player's head at each
//                        step (scene lighting, no extra light), sweep the real player weapon through the monster during the
//                        sequence (no damage, no hit / clash sound allowed), torch intensities before / dimmed / after, live
//                        particles and paired FX on/off draw calls, landing distance, personal-space minimum, controls free.
//                "retry" flag a Retry and reload the scene, start the battle, time the short version.
// Dependencies: MonsterEntrance, HitDetection, SfxTrace, PlayerCombat, BattleManager, CombatVerification.Prepare
// Attach to: nothing (static)

using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class EntranceVerification
{
    static IEnumerator job;
    static readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
    static readonly StringBuilder log = new StringBuilder();
    static int lastFrame = -1;
    public static string captureDir = "Captures/Level2Entrance";

    public static string Status() => (job != null ? $"RUNNING t={Time.time:0.0}\n" : "IDLE\n") + log;

    public static string Run(string name)
    {
        if (!EditorApplication.isPlaying) return "not playing";
        Stop(); log.Length = 0;
        if (name == "full") job = Full(false);
        else if (name == "retry") job = Retry();
        else return "unknown job " + name;
        EditorApplication.update += Tick;
        return "started " + name;
    }

    public static void Stop()
    {
        job = null; stack.Clear(); EditorApplication.update -= Tick;
        HitDetection.HitLanded -= OnHit; HitDetection.WeaponClashed -= OnClash; SfxTrace.Played -= OnSound;
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

    static int hits, clashes; static readonly List<string> sounds = new List<string>();
    static MonsterEntrance watched; static float watchT0;
    static bool During => watched != null && !watched.Complete;
    static void OnHit(HitDetection s, float v, bool d) { if (During) hits++; }
    static void OnClash(HitDetection s, float v) { if (During) clashes++; }
    static void OnSound(Object src, AudioClip clip, string why) { if (During) sounds.Add($"{Time.time - watchT0:0.00} s {(clip != null ? clip.name : "?")} ({why})"); }

    static IEnumerator Retry()
    {
        GameStateManager.FlagRetry(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
        UnityEngine.SceneManagement.SceneManager.LoadScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
        for (int i = 0; i < 10; i++) yield return null;
        yield return Full(true);
    }

    static IEnumerator Full(bool retry)
    {
        CombatVerification.frameDt = 1f / 72f;
        CombatVerification.Prepare();
        System.IO.Directory.CreateDirectory(captureDir);
        var rig = Object.FindAnyObjectByType<OVRCameraRig>();
        var head = rig.centerEyeAnchor;
        // Baseline before the battle: torches, draw calls.
        var lights = new List<Light>();
        foreach (var l in Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude)) if (l.enabled && (l.type == LightType.Point || l.type == LightType.Spot)) lights.Add(l);
        var before = new float[lights.Count];
        for (int i = 0; i < lights.Count; i++) before[i] = lights[i].intensity;
        yield return null;
        int dcBefore = UnityEditor.UnityStats.drawCalls;
        hits = clashes = 0; sounds.Clear();
        HitDetection.HitLanded += OnHit; HitDetection.WeaponClashed += OnClash; SfxTrace.Played += OnSound;

        var pcs = PlayerCombat.Instance;
        if (pcs != null && pcs.weaponSets != null && pcs.weaponSets.Count > 0) { pcs.EquipWeaponSet(CombatVerification.l3Weapon); yield return null; }   // Level 3: the grabbed weapon
        float t0 = Time.time;
        BattleManager.Instance.StartBattleFromInspector();
        MonsterController mon = null;
        while (Time.time - t0 < 3f) { mon = BattleManager.Instance.ActiveMonster; if (mon != null && mon.GetComponent<MonsterEntrance>() != null) break; yield return null; }
        var e = mon != null ? mon.GetComponent<MonsterEntrance>() : null;
        if (e == null) { log.AppendLine("no MonsterEntrance on the monster"); yield break; }
        var health = mon.GetComponent<MonsterHealth>();
        var ps = mon.GetComponent<MonsterPersonalSpace>();
        float hp0 = health.currentHealth, hpAtEnd = hp0;
        watched = e; watchT0 = t0;
        log.AppendLine($"{(retry ? "RETRY" : "FULL")} entrance, style {e.style}, short version {e.ShortVersion}; torches found {lights.Count}; draw calls before battle {dcBefore}");

        // Weapon sweep through the monster body during the sequence (real HitDetection path), from the drop on.
        var pc = PlayerCombat.Instance;
        var root = pc.weaponTransform; var hd = root.GetComponentInChildren<HitDetection>(); var col = hd.GetComponent<Collider>();
        Vector3 restLocal = root.localPosition; Quaternion restRot = root.localRotation;
        bool eyesShot = false, burstShot = false, glowShot = false, flareShot = false; int maxFire = 0;
        Transform[] bones = null; var minBone = new[] { 99f, 99f, 99f };
        var anr = mon.GetComponentInChildren<Animator>();
        var names = new Dictionary<int, string>();
        var ac = anr.runtimeAnimatorController as UnityEditor.Animations.AnimatorController;
        if (ac != null) foreach (var cs in ac.layers[0].stateMachine.states) names[cs.state.nameHash] = cs.state.name;
        var path = new List<string>(); string lastSt = null;
        int lastStep = -1, sweeps = 0; float minHead = 99f, minLevel = 99f;
        int fxOn = 0, fxOff = 0; long dcOn = 0, dcOff = 0; int maxFx = 0, frame = 0;
        bool controlsOk = true;
        while (!e.Complete && Time.time - t0 < 20f)
        {
            frame++;
            // step captures
            for (int s = 0; s < MonsterEntrance.StepNames.Length - 1; s++)
                if (s > lastStep && e.StepTimes[s] > 0f && Time.time - t0 >= e.StepTimes[s])
                {
                    lastStep = s;
                    string tag = $"{captureDir}/{(retry ? "retry" : "full")}_{s}_{MonsterEntrance.StepNames[s].Replace(' ', '_').Replace("+", "")}";
                    bool ground = s == 0 || s >= 3;   // the mist and the landing are on the floor; the eyes and the drop are up high
                    Capture(tag + ".png", head, mon, ground ? 0.9f : 2.6f);
                }
            // extra moments: eyes fully lit, landing burst spreading, mid glow-up (axe igniting), eye flare
            if (!eyesShot && e.StepTimes[1] > 0f && Time.time - t0 >= e.StepTimes[1] + 0.7f && e.StepTimes[2] <= 0f) { eyesShot = true; Capture($"{captureDir}/full_1b_eyes_glowing.png", head, mon, 2.6f); }
            if (!burstShot && e.StepTimes[3] > 0f && Time.time - t0 >= e.StepTimes[3] + 0.15f) { burstShot = true; Capture($"{captureDir}/{(retry ? "retry" : "full")}_3b_landing_burst.png", head, mon, 0.6f); }
            if (!glowShot && e.StepTimes[5] > 0f && Time.time - t0 >= e.StepTimes[5] + 1.0f) { glowShot = true; Capture($"{captureDir}/full_5b_glow_mid.png", head, mon, 1.1f); if (e.weaponFire != null) CaptureAt($"{captureDir}/full_5b_axe_fire.png", head, e.weaponFire.transform.position); }
            if (!flareShot && e.StepTimes[5] > 0f && Time.time - t0 >= e.StepTimes[5] + 1.95f) { flareShot = true; Capture($"{captureDir}/full_5c_eye_flare.png", head, mon, 1.3f); }
            if (!e.Complete) hpAtEnd = health.currentHealth;
            if (e.weaponFire != null) maxFire = Mathf.Max(maxFire, e.weaponFire.particleCount);
            // torches
            float level = 0f; int n = 0;
            for (int i = 0; i < lights.Count; i++) if (before[i] > 0f) { level += lights[i].intensity / before[i]; n++; }
            if (n > 0) { level /= n; minLevel = Mathf.Min(minLevel, level); }
            // particles + paired draw calls (FX renderer on / off on alternate frames while particles are alive)
            int live = e.LiveFxParticles; maxFx = Mathf.Max(maxFx, live);
            var fxGo = GameObject.Find("EntranceDropFX");
            if (fxGo != null && live > 0)
            {
                var r = fxGo.GetComponent<ParticleSystemRenderer>();
                if (frame % 2 == 0) { dcOn += UnityEditor.UnityStats.drawCalls; fxOn++; r.enabled = false; }
                else { dcOff += UnityEditor.UnityStats.drawCalls; fxOff++; r.enabled = true; }
            }
            // personal space: the root, and the monster's head and hands against the player's head (3-D)
            Vector3 d = mon.transform.position - head.position; d.y = 0f; minHead = Mathf.Min(minHead, d.magnitude);
            if (bones == null) { var an = mon.GetComponentInChildren<Animator>(); bones = new[] { an.GetBoneTransform(HumanBodyBones.Head), an.GetBoneTransform(HumanBodyBones.LeftHand), an.GetBoneTransform(HumanBodyBones.RightHand) }; }
            for (int b = 0; b < bones.Length; b++) if (bones[b] != null) minBone[b] = Mathf.Min(minBone[b], Vector3.Distance(bones[b].position, head.position));
            // sweep the weapon through the chest every 0.5 s once the monster is down (8 m/s)
            if (e.StepTimes[3] > 0f && Time.time - t0 > e.StepTimes[3] + 0.1f && (frame % 36) == 0)
            {
                Vector3 chest = mon.transform.position + Vector3.up * 1.2f;
                Vector3 side = Vector3.Cross(Vector3.up, (chest - head.position).normalized);
                Vector3 off = col.bounds.center - root.position;
                for (int k = 0; k <= 8; k++) { root.position = Vector3.Lerp(chest + side * 0.6f, chest - side * 0.6f, k / 8f) - off; yield return null; }
                root.localPosition = restLocal; root.localRotation = restRot;
                sweeps++;
            }
            controlsOk &= pc.enabled && root.gameObject.activeInHierarchy;
            var cur = anr.GetCurrentAnimatorStateInfo(0);
            string st = names.TryGetValue(cur.shortNameHash, out var nm) ? nm : cur.shortNameHash.ToString();
            if (anr.IsInTransition(0)) { var nx = anr.GetNextAnimatorStateInfo(0); st += " ~> " + (names.TryGetValue(nx.shortNameHash, out var n2) ? n2 : nx.shortNameHash.ToString()); }
            if (st != lastSt) { path.Add($"{Time.time - t0:0.00} {st}"); lastSt = st; }
            yield return null;
        }
        var fxGo2 = GameObject.Find("EntranceDropFX"); if (fxGo2 != null) fxGo2.GetComponent<ParticleSystemRenderer>().enabled = true;
        float total = Time.time - t0;
        Capture($"{captureDir}/{(retry ? "retry" : "full")}_7_fight_starts.png", head, mon, 1.1f);
        for (int s = 0; s < MonsterEntrance.StepNames.Length; s++) log.AppendLine($"  step {s} {MonsterEntrance.StepNames[s]} at {e.StepTimes[s]:0.00} s");
        log.AppendLine($"  complete at {total:0.00} s (entrance's own clock {e.StepTimes[7]:0.00} s); AI enabled {mon.GetComponent<MonsterAI>().enabled}");
        log.AppendLine($"  landing distance {e.LandDistance:0.00} m (min head-to-root during the sequence {minHead:0.00} m); closest to the player's head: monster head {minBone[0]:0.00} m, left hand {minBone[1]:0.00} m, right hand {minBone[2]:0.00} m (personal-space limits head {(ps != null ? ps.headClearance : 0f):0.00} / hand {(ps != null ? ps.handClearance : 0f):0.00} m)");
        log.AppendLine($"  invulnerable: {sweeps} weapon sweeps through the chest during the sequence, hits {hits}, clashes {clashes}, monster HP {hp0:0} -> {hpAtEnd:0} at the end of the sequence");
        log.AppendLine($"  sounds during the sequence: {(sounds.Count > 0 ? string.Join("; ", sounds) : "none")}");
        log.AppendLine($"  torches: lowest {minLevel * 100f:0}% of their level");
        // keep tracing the Animator for 1.2 s after the fight starts (the Retry version hands back to Idle on its last frame)
        float tEnd = Time.time + 1.2f;
        while (Time.time < tEnd)
        {
            var cur = anr.GetCurrentAnimatorStateInfo(0);
            string st = names.TryGetValue(cur.shortNameHash, out var nm) ? nm : cur.shortNameHash.ToString();
            if (anr.IsInTransition(0)) { var nx = anr.GetNextAnimatorStateInfo(0); st += " ~> " + (names.TryGetValue(nx.shortNameHash, out var n2) ? n2 : nx.shortNameHash.ToString()); }
            if (st != lastSt) { path.Add($"{Time.time - t0:0.00} {st}"); lastSt = st; }
            yield return null;
        }
        log.AppendLine($"  animator path incl. 1.2 s after the start of the fight: {string.Join(" | ", path)}");
        float back = 0f; int nb = 0;
        for (int i = 0; i < lights.Count; i++) if (before[i] > 0f) { back += lights[i].intensity / before[i]; nb++; }
        log.AppendLine($"  torches after: {(nb > 0 ? back / nb * 100f : 0f):0.0}% (each: {Levels(lights, before)})");
        log.AppendLine($"  axe fire during the glow-up: max live particles {maxFire}");
        log.AppendLine($"  FX particles max live {maxFx}; draw calls with the FX renderer on {(fxOn > 0 ? dcOn / (float)fxOn : 0f):0.0} vs off {(fxOff > 0 ? dcOff / (float)fxOff : 0f):0.0} ({fxOn}/{fxOff} frames)");
        log.AppendLine($"  animator path (state, ~> = in transition): {string.Join(" | ", path)}");
        log.AppendLine($"  controls free (PlayerCombat enabled, weapon active) the whole time: {controlsOk}");
        HitDetection.HitLanded -= OnHit; HitDetection.WeaponClashed -= OnClash; SfxTrace.Played -= OnSound;
        watched = null;
    }

    static IEnumerator Seconds(float s) { float end = Time.time + s; while (Time.time < end) yield return null; }

    static string Levels(List<Light> lights, float[] before)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < lights.Count; i++) sb.Append($"{lights[i].name} {before[i]:0.00}->{lights[i].intensity:0.00} ");
        return sb.ToString();
    }

    static void Capture(string file, Transform head, MonsterController mon, float lookHeight) => CaptureAt(file, head, mon.transform.position + Vector3.up * lookHeight);

    static void CaptureAt(string file, Transform head, Vector3 look)
    {
        // The pre-battle name board is still up because the harness skips the name pick: hide it for this render only.
        var pnp = Object.FindAnyObjectByType<PlayerNamePicker>(FindObjectsInactive.Include);
        var panelField = typeof(PlayerNamePicker).GetField("panel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var panel = pnp != null && panelField != null ? panelField.GetValue(pnp) as GameObject : null;
        bool panelWas = panel != null && panel.activeSelf;
        if (panel != null) panel.SetActive(false);
        var go = new GameObject("EV_Cam");
        try
        {
            var cam = go.AddComponent<Camera>();
            var main = head.GetComponent<Camera>();
            if (main != null) cam.CopyFrom(main);
            go.transform.SetPositionAndRotation(head.position, Quaternion.LookRotation(look - head.position));
            cam.fieldOfView = 80f;
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
        finally { Object.DestroyImmediate(go); if (panel != null) panel.SetActive(panelWas); }
    }
}
