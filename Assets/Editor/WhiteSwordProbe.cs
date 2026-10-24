// WhiteSwordProbe.cs
// Purpose: Finds what draws white on the weapons, by measurement. Every game frame it renders the player's head camera (and a side
//          camera aimed at the monster) into a 640 x 480 texture with the UI layer culled, once in full and once with each weapon
//          group's renderers off (the monster's weapon; the player's weapons). "White drawn by a group" = pixels near-white (every
//          channel >= 200/255, spread <= 60) in the full frame and not in the frame without that group. The scenario walks through every
//          attack in the table, a guard, a blocked hit, an opening hit, a guard break, an unguarded hit and swings into the monster's
//          weapon and body. When a group draws white, the SAME frame is ablated: each weapon part, each material slot's specular
//          highlights / environment reflections / smoothness / visibility, the property blocks, every light, every Volume, the camera's
//          post-processing, the sky reflections and the reflection probes are switched off one at a time and the frame is rendered
//          again, so the table says exactly which object and property draws the white.
//          Editor only, Play mode only, driven from Unity MCP:
//              WhiteSwordProbe.Run("full", "")            then poll WhiteSwordProbe.Status() until it ends with DONE
//              WhiteSwordProbe.Run("full", "spec:MonsterAxe;light:AxeFire")   the same with those things off for the whole run
//          Toggle keys (';'-separated, matched on the hierarchy path): renderer:<text>, light:<text>, spec:<text> (specular highlights
//          + environment reflections off on those renderers' materials, runtime copies only), pb:<text> (property blocks cleared every
//          frame), post (camera post-processing off), envrefl (RenderSettings.reflectionIntensity 0), ghost:on / ghost:off,
//          set:<renderer path text>:<slot>:<float property>=<value> (a runtime copy of that material slot with the value changed).
// Dependencies: BattleManager, MonsterAI, MonsterDefense, MonsterController, PlayerCombat, PlayerHealth, CombatVerification (Prepare)
// Attach to: nothing (static, Editor only)

using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class WhiteSwordProbe
{
    const int W = 640, H = 480;
    public static string captureDir = "Captures/WhiteSword";
    /// <summary>White pixels a weapon group must add before its frame is ablated.</summary>
    public static int flashMargin = 40;

    static IEnumerator job;
    static readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
    static readonly StringBuilder log = new StringBuilder();
    static int lastFrame = -1;
    static string runTag = "";
    static readonly List<Action> restore = new List<Action>();

    static RenderTexture rt;
    static Texture2D tex;
    static Camera head, side;
    static MonsterController mon;
    static readonly List<Renderer> monsterWeapon = new List<Renderer>(), playerWeapon = new List<Renderer>();

    // per step
    static string step = "";


    static readonly StringBuilder stepLines = new StringBuilder();
    // persistent toggles that must be re-applied every frame (property blocks)
    static readonly List<Renderer> pbClear = new List<Renderer>();

    public static bool Running => job != null;
    public static string Status() => (job != null ? "RUNNING " + runTag + " step " + step + "\n" : "IDLE\n") + log;

    public static string Run(string scenario, string toggles)
    {
        if (!EditorApplication.isPlaying) return "not playing";
        Stop();
        log.Length = 0; stack.Clear(); sequenceDone[0] = sequenceDone[1] = false;
        runTag = string.IsNullOrEmpty(toggles) ? "base" : Sanitize(toggles);
        job = Scenario(scenario, toggles ?? "");
        EditorApplication.update += Tick;
        return "started " + scenario + " [" + runTag + "]";
    }

    public static void Stop()
    {
        if (job != null) log.AppendLine("stopped");
        job = null; stack.Clear();
        EditorApplication.update -= Tick;
        Restore();
        if (side != null) UnityEngine.Object.Destroy(side.gameObject);
        side = null;
    }

    static void Restore()
    {
        for (int i = restore.Count - 1; i >= 0; i--) { try { restore[i](); } catch (Exception e) { log.AppendLine("restore failed " + e.Message); } }
        restore.Clear(); pbClear.Clear();
    }

    static void Tick()
    {
        if (job == null || !EditorApplication.isPlaying) { Stop(); return; }
        if (Time.frameCount == lastFrame) return;
        lastFrame = Time.frameCount;
        try
        {
            foreach (var r in pbClear) if (r != null) ClearBlocks(r);
            while (true)
            {
                var top = stack.Count > 0 ? stack.Peek() : job;
                if (top.MoveNext()) { if (top.Current is IEnumerator nested) { stack.Push(nested); continue; } break; }
                if (stack.Count > 0) { stack.Pop(); continue; }
                log.AppendLine("DONE"); job = null; EditorApplication.update -= Tick; Restore();
                if (side != null) UnityEngine.Object.Destroy(side.gameObject);
                side = null;
                return;
            }
            if (!string.IsNullOrEmpty(step)) Measure();
        }
        catch (Exception e) { log.AppendLine("EXCEPTION " + e); Stop(); }
    }

    /// <summary>Safe file name, not shortened (Sanitize shortens to 40 characters and file names collided).</summary>
    static string FileName(string s)
    {
        var sb = new StringBuilder();
        foreach (char c in s) sb.Append(char.IsLetterOrDigit(c) ? c : '_');
        return sb.Length > 110 ? sb.ToString(0, 110) : sb.ToString();
    }

    static string Sanitize(string s)
    {
        var sb = new StringBuilder();
        foreach (char c in s) sb.Append(char.IsLetterOrDigit(c) ? c : '_');
        return sb.Length > 40 ? sb.ToString(0, 40) : sb.ToString();
    }

    static IEnumerator Seconds(float s) { float end = Time.time + s; while (Time.time < end) { KeepAlive(); yield return null; } }

    static void KeepAlive()
    {
        if (PlayerHealth.Instance != null) PlayerHealth.Instance.FullHeal();
        if (mon != null) { var h = mon.GetComponent<MonsterHealth>(); if (h != null) h.currentHealth = h.maxHealth; }
    }

    // ───────────────────────── scenario ─────────────────────────
    static IEnumerator Scenario(string scenario, string toggles)
    {
        CombatVerification.frameDt = 1f / 30f;
        CombatVerification.Prepare();
        if (BattleManager.Instance.ActiveMonster == null || !BattleManager.Instance.ActiveMonster.isAlive) BattleManager.Instance.StartBattleFromInspector();
        float w0 = Time.time;
        while (Time.time - w0 < 25f)
        {
            var mm = BattleManager.Instance.ActiveMonster;
            if (mm != null && mm.isAlive) { var e = mm.GetComponent<MonsterEntrance>(); if (e == null || e.Complete) break; }
            KeepAlive(); yield return null;
        }
        mon = BattleManager.Instance.ActiveMonster;
        if (mon == null) { log.AppendLine("no monster"); yield break; }
        var ai = mon.GetComponent<MonsterAI>(); var def = mon.GetComponent<MonsterDefense>();
        Setup();
        // Quiet monster: nothing starts on its own, so every step is one thing.
        float cd = mon.attackCooldown; mon.attackCooldown = 9999f; restore.Add(() => { if (mon != null) mon.attackCooldown = cd; });
        var bf = typeof(MonsterAI).GetField("baseAttackCooldown", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        object bv = bf.GetValue(ai); bf.SetValue(ai, 9999f); restore.Add(() => { if (ai != null) bf.SetValue(ai, bv); });
        float think = ai.thinkInterval; ai.thinkInterval = 9999f; restore.Add(() => { if (ai != null) ai.thinkInterval = think; });
        if (def != null)
        {
            bool rg = def.reactiveGuard, tg = def.timedGuard;
            def.reactiveGuard = false; def.timedGuard = false;
            restore.Add(() => { if (def != null) { def.reactiveGuard = rg; def.timedGuard = tg; } });
        }
        ApplyToggles(toggles);
        log.AppendLine("== WHITE PROBE " + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name + " monster " + mon.name + " toggles [" + toggles + "]");
        log.AppendLine("   monster weapon renderers: " + Names(monsterWeapon));
        log.AppendLine("   player weapon renderers:  " + Names(playerWeapon));
        yield return Seconds(1f);

        yield return Step("idle", Seconds(2.5f), true);

        if (scenario == "idle") yield break;

        if (ai.UsesAttackTable)
        {
            for (int i = 0; i < ai.attackTable.Count; i++)
            {
                int k = i;
                yield return Step("attack" + k + "_" + Sanitize(ai.attackTable[k].name), Attack(ai, k), false);
            }
        }
        else
        {
            foreach (MonsterAI.AttackPattern p in new[] { MonsterAI.AttackPattern.Slash, MonsterAI.AttackPattern.Heavy, MonsterAI.AttackPattern.Special })
                yield return Step("attack_" + p, LegacyAttack(ai, p), false);
        }
        if (scenario == "attacks") yield break;

        if (def != null)
        {
            yield return Step("guard", Guard(ai, def, 0), false);
            yield return Step("guard_blocked_hit", Guard(ai, def, 1), false);
            yield return Step("guard_opening_hit", Guard(ai, def, 2), false);
            yield return Step("guard_break", GuardBreak(ai, def), false);
            // Each guard pose held (Levels 2-3 turn the weapon per pose: new angles to the lights).
            foreach (var pose in new[] { MonsterDefense.GuardPose.SideLeft, MonsterDefense.GuardPose.SideRight, MonsterDefense.GuardPose.High, MonsterDefense.GuardPose.Low })
                yield return Step("pose_" + MonsterDefense.PoseNames[(int)pose], PoseGuard(ai, def, pose), false);
        }
        yield return Step("unguarded_hit", Hit(), false);
        yield return Step("swing_into_monster_weapon", SwingInto(true), false);
        yield return Step("swing_into_body", SwingInto(false), false);
    }

    static string Names(List<Renderer> rs)
    {
        var sb = new StringBuilder();
        foreach (var r in rs) sb.Append(r != null ? r.name + "(" + r.GetType().Name + ") " : "");
        return sb.ToString();
    }

    static IEnumerator Attack(MonsterAI ai, int i)
    {
        float w = Time.time;
        while ((ai.IsBusy || mon.isAttacking) && Time.time - w < 5f) { KeepAlive(); yield return null; }
        if (!ai.DebugForceAttack(i)) { log.AppendLine("   attack " + i + " refused"); yield break; }
        yield return null;
        w = Time.time;
        while ((ai.IsBusy || mon.isAttacking) && Time.time - w < 8f) { KeepAlive(); yield return null; }
        yield return Seconds(0.4f);
    }

    static IEnumerator LegacyAttack(MonsterAI ai, MonsterAI.AttackPattern p)
    {
        var m = typeof(MonsterAI).GetMethod("AttackRoutine", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        float w = Time.time;
        while ((ai.IsBusy || mon.isAttacking) && Time.time - w < 5f) { KeepAlive(); yield return null; }
        ai.StartCoroutine((IEnumerator)m.Invoke(ai, new object[] { p }));
        yield return null;
        w = Time.time;
        while ((ai.IsBusy || mon.isAttacking) && Time.time - w < 8f) { KeepAlive(); yield return null; }
        yield return Seconds(0.4f);
    }

    /// <param name="hit">0 = no hit, 1 = a hit at chest height (blocked), 2 = a hit at the shins (opening).</param>
    static IEnumerator Guard(MonsterAI ai, MonsterDefense def, int hit)
    {
        float w = Time.time;
        while (ai.IsBusy && Time.time - w < 5f) { KeepAlive(); yield return null; }
        ai.BeginGuard(1.4f, true, def.highGuardTrigger, def.guardFlashColor, def.guardFlashSeconds);
        yield return Seconds(0.35f);
        if (hit > 0)
        {
            Vector3 p = mon.transform.position + Vector3.up * (hit == 1 ? 0.7f * def.Height : 0.15f * def.Height) - mon.transform.forward * -0.2f;
            mon.OnTakeDamage(4f, p, 4f, false);
        }
        yield return Seconds(1.6f);
    }

    static IEnumerator PoseGuard(MonsterAI ai, MonsterDefense def, MonsterDefense.GuardPose pose)
    {
        float w = Time.time;
        while ((ai.IsBusy || def.IsStaggered) && Time.time - w < 8f) { KeepAlive(); yield return null; }
        float h = def.Height;
        Vector3 c = mon.transform.position + mon.transform.forward * 0.2f + Vector3.up * (pose == MonsterDefense.GuardPose.High ? 0.95f * h : pose == MonsterDefense.GuardPose.Low ? 0.5f * h : 0.62f * h)
                    + mon.transform.right * (pose == MonsterDefense.GuardPose.SideLeft ? 0.42f : pose == MonsterDefense.GuardPose.SideRight ? -0.42f : 0f);
        var cls = pose == MonsterDefense.GuardPose.High ? MonsterDefense.SwingClass.Overhead : pose == MonsterDefense.GuardPose.Low ? MonsterDefense.SwingClass.Thrust
            : pose == MonsterDefense.GuardPose.SideLeft ? MonsterDefense.SwingClass.HorizontalLeft : MonsterDefense.SwingClass.HorizontalRight;
        def.RaisePlannedGuard(cls, pose, c, 1.6f);
        yield return Seconds(1.9f);
    }

    static IEnumerator GuardBreak(MonsterAI ai, MonsterDefense def)
    {
        float w = Time.time;
        while (ai.IsBusy && Time.time - w < 5f) { KeepAlive(); yield return null; }
        ai.BeginGuard(1.4f, true, def.highGuardTrigger, def.guardFlashColor, def.guardFlashSeconds);
        yield return Seconds(0.3f);
        typeof(MonsterDefense).GetMethod("BreakGuard", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(def, null);
        yield return Seconds(def.staggerSeconds + 0.5f);
    }

    static IEnumerator Hit()
    {
        var def = mon.GetComponent<MonsterDefense>();
        float h = def != null ? def.Height : 1.8f;
        mon.OnTakeDamage(4f, mon.transform.position + Vector3.up * 0.65f * h + mon.transform.forward * 0.2f, 6f, false);
        yield return Seconds(1.2f);
    }

    /// <summary>Drives the player's right weapon fast through the monster's weapon (or chest) and back.</summary>
    static IEnumerator SwingInto(bool weapon)
    {
        var pc = PlayerCombat.Instance;
        Transform root = pc.weaponTransform;
        Vector3 restLocal = root.localPosition; Quaternion restRot = root.localRotation;
        Vector3 target;
        if (weapon && monsterWeapon.Count > 0) target = monsterWeapon[0].bounds.center;
        else { var def = mon.GetComponent<MonsterDefense>(); target = def != null && def.guardTarget != null ? def.guardTarget.position : mon.transform.position + Vector3.up * 1.3f; }
        var hd = root.GetComponentInChildren<HitDetection>(); var col = hd != null ? hd.GetComponent<Collider>() : null;
        Vector3 offset = col != null ? col.bounds.center - root.position : Vector3.zero;
        Vector3 from = head.transform.position + (target - head.transform.position) * 0.45f + head.transform.right * 0.35f;
        Vector3 to = target - head.transform.right * 0.25f;
        // slow to the start (below the swing gate), then fast through, then slow back
        while (Vector3.Distance(root.position + offset, from) > 0.01f) { root.position = Vector3.MoveTowards(root.position + offset, from, 0.8f * Time.deltaTime) - offset; KeepAlive(); yield return null; }
        float dur = Vector3.Distance(from, to) / 4f, t0 = Time.time;
        while (Time.time - t0 < dur) { root.position = Vector3.Lerp(from, to, (Time.time - t0) / dur) - offset; KeepAlive(); yield return null; }
        yield return Seconds(0.5f);   // held inside
        float tb = Time.time;
        while (Time.time - tb < 2f)
        {
            Vector3 rest = root.parent.TransformPoint(restLocal);
            root.position = Vector3.MoveTowards(root.position, rest, 0.9f * Time.deltaTime);
            if (Vector3.Distance(root.position, rest) < 0.01f) break;
            KeepAlive(); yield return null;
        }
        root.localPosition = restLocal; root.localRotation = restRot;
        yield return Seconds(0.3f);
    }

    // ───────────────────────── step bookkeeping ─────────────────────────
    // Two groups: 0 = the monster's weapon(s), 1 = the player's weapons. "White drawn by a group" = pixels that are near-white in the
    // full render and NOT near-white in the same frame rendered with that group's renderers off (UI layer culled in both).
    static readonly int[] peak = new int[4];               // [group * 2 + cam]
    static readonly Color32[][] peakPx = new Color32[4][];
    static readonly Color32[][] peakRef = new Color32[4][];
    static readonly int[] ablated = new int[2];
    static readonly int[] ablationsByGroup = new int[2];
    static readonly string[] GroupNames = { "monster weapon", "player weapons" };
    static readonly string[] CamNames = { "head", "side" };
    public static readonly List<string> Summary = new List<string>();

    static IEnumerator Step(string name, IEnumerator body, bool isIdle)
    {
        step = name; stepLines.Length = 0;
        for (int i = 0; i < 4; i++) { peak[i] = 0; peakPx[i] = null; peakRef[i] = null; }
        ablated[0] = ablated[1] = 0; ablationsByGroup[0] = ablationsByGroup[1] = 0;
        yield return body;
        string n = step; step = "";
        for (int i = 0; i < 4; i++)
            if (peakPx[i] != null && peak[i] > 0) SaveMasked(peakPx[i], peakRef[i], n + "_" + (i / 2 == 0 ? "monsterWeapon" : "playerWeapons") + "_" + CamNames[i % 2] + "_peak");
        string line = "  STEP " + n.PadRight(30) + " white px drawn by  monster weapon: head " + peak[0].ToString().PadLeft(4) + " side " + peak[1].ToString().PadLeft(4) +
                      "  |  player weapons: head " + peak[2].ToString().PadLeft(4) + " side " + peak[3].ToString().PadLeft(4);
        log.AppendLine(line); Summary.Add(runTag + " " + line.Trim());
        if (stepLines.Length > 0) log.Append(stepLines);
    }

    static void Measure()
    {
        KeepAlive();
        for (int c = 0; c < 2; c++)
        {
            var cam = c == 0 ? head : side;
            if (cam == null) continue;
            var full = Render(cam);
            for (int g = 0; g < 2; g++)
            {
                var group = g == 0 ? monsterWeapon : playerWeapon;
                var refPx = RenderWithout(cam, group);
                int n = CountAdded(full, refPx);
                int k = g * 2 + c;
                if (n > peak[k]) { peak[k] = n; peakPx[k] = full; peakRef[k] = refPx; }
                if (ablationsByGroup[g] < 2 && n > flashMargin && n > ablated[g] * 3 / 2)
                {
                    ablationsByGroup[g]++; ablated[g] = n;
                    Ablate(cam, g, full, refPx, n, step + "_" + GroupNames[g].Replace(' ', '_') + "_" + CamNames[c] + "_" + ablationsByGroup[g]);
                    full = Render(cam);   // the ablation re-rendered the camera; the frame itself is unchanged
                }
            }
        }
    }

    // ───────────────────────── capture + count ─────────────────────────
    static void Setup()
    {
        if (rt == null) { rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB); rt.Create(); }
        if (tex == null) tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        var rig = UnityEngine.Object.FindAnyObjectByType<OVRCameraRig>();
        head = rig != null ? rig.centerEyeAnchor.GetComponent<Camera>() : Camera.main;
        // side camera: to the monster's right, chest height, looking at the chest
        var go = new GameObject("WhiteProbeSideCam");
        side = go.AddComponent<Camera>();
        side.enabled = false; side.fieldOfView = 55f; side.nearClipPlane = 0.05f; side.farClipPlane = 60f;
        var hd = head.GetUniversalAdditionalCameraData(); var sd = side.GetUniversalAdditionalCameraData();
        sd.renderPostProcessing = hd.renderPostProcessing; sd.antialiasing = hd.antialiasing;
        Vector3 chest = mon.transform.position + Vector3.up * 1.3f;
        go.transform.position = chest + mon.transform.right * 1.7f + mon.transform.forward * 0.9f + Vector3.up * 0.2f;
        go.transform.LookAt(chest);

        monsterWeapon.Clear(); playerWeapon.Clear();
        var roots = new HashSet<Transform>();
        foreach (var c in mon.GetComponentsInChildren<Collider>(true))
        {
            if (!c.CompareTag("MonsterWeapon")) continue;
            Transform t = c.transform;
            while (t.parent != null && t.parent != mon.transform && !t.parent.name.ToLowerInvariant().Contains("hand")) t = t.parent;
            roots.Add(t);
        }
        foreach (var t in roots) foreach (var rr in t.GetComponentsInChildren<Renderer>(true)) if (!monsterWeapon.Contains(rr)) monsterWeapon.Add(rr);
        var pc = PlayerCombat.Instance;
        foreach (var w in new[] { pc.weaponTransform, pc.offhandWeaponTransform })
            if (w != null) foreach (var rr in w.GetComponentsInChildren<Renderer>(true)) playerWeapon.Add(rr);
    }

    static Color32[] Render(Camera cam)
    {
        var old = cam.targetTexture; int mask = cam.cullingMask;
        int ui = LayerMask.NameToLayer("UI");
        if (ui >= 0) cam.cullingMask = mask & ~(1 << ui);   // the HUD / name picker are not weapons
        cam.targetTexture = rt; cam.Render(); cam.targetTexture = old; cam.cullingMask = mask;
        var prev = RenderTexture.active; RenderTexture.active = rt;
        tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply(false);
        RenderTexture.active = prev;
        return tex.GetPixels32();
    }

    static readonly List<Renderer> tmpOff = new List<Renderer>();

    static Color32[] RenderWithout(Camera cam, List<Renderer> group)
    {
        tmpOff.Clear();
        foreach (var r in group) if (r != null && r.enabled) { r.enabled = false; tmpOff.Add(r); }
        try { return Render(cam); }
        finally { foreach (var r in tmpOff) r.enabled = true; }
    }

    static bool IsWhite(Color32 c)
    {
        int mn = Math.Min(c.r, Math.Min(c.g, c.b)), mx = Math.Max(c.r, Math.Max(c.g, c.b));
        return mn >= 200 && mx - mn <= 60;   // near-white, pale tints included (a blue-white highlight), no saturated colour
    }

    /// <summary>Pixels near-white in <paramref name="px"/> that are not near-white in the reference.</summary>
    static int CountAdded(Color32[] px, Color32[] refPx)
    {
        int n = 0;
        for (int i = 0; i < px.Length; i++) if (IsWhite(px[i]) && !IsWhite(refPx[i])) n++;
        return n;
    }

    static void Save(Color32[] px, string name)
    {
        var t = new Texture2D(W, H, TextureFormat.RGBA32, false);
        t.SetPixels32(px); t.Apply(false);
        System.IO.Directory.CreateDirectory(captureDir);
        System.IO.File.WriteAllBytes(captureDir + "/" + runTag + "_" + FileName(name) + ".png", t.EncodeToPNG());
        UnityEngine.Object.Destroy(t);
    }

    /// <summary>Saves the frame, plus a copy with the counted pixels painted magenta.</summary>
    static void SaveMasked(Color32[] px, Color32[] refPx, string name)
    {
        Save(px, name);
        if (refPx == null) return;
        var m = (Color32[])px.Clone();
        for (int i = 0; i < m.Length; i++) if (IsWhite(px[i]) && !IsWhite(refPx[i])) m[i] = new Color32(255, 0, 255, 255);
        Save(m, name + "_mask");
    }

    static string PathOf(Transform t)
    {
        var sb = new StringBuilder(t.name);
        for (var p = t.parent; p != null; p = p.parent) { sb.Insert(0, p.name + "/"); if (sb.Length > 140) break; }
        string s = sb.ToString();
        return s.Length > 90 ? "..." + s.Substring(s.Length - 87) : s;
    }

    // ───────────────────────── one-at-a-time ablation of a frozen frame ─────────────────────────
    struct Cand { public string name; public Action off, on; }

    /// <summary>Same frame, same reference (the group's renderers off): switch one thing off, render, count what the group still adds.</summary>
    static void Ablate(Camera cam, int g, Color32[] full, Color32[] refPx, int baseCount, string tag)
    {
        var group = g == 0 ? monsterWeapon : playerWeapon;
        var cands = new List<Cand>();
        foreach (var r in group)
        {
            if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
            var rr = r;
            cands.Add(new Cand { name = "renderer " + PathOf(r.transform) + " [" + MatNames(r) + "] (the whole weapon part)", off = () => rr.enabled = false, on = () => rr.enabled = true });
            if (r is ParticleSystemRenderer) continue;
            var shadow = rr.shadowCastingMode;
            cands.Add(new Cand { name = "shadow casting off on " + r.name + " (was " + shadow + ")", off = () => rr.shadowCastingMode = ShadowCastingMode.Off, on = () => rr.shadowCastingMode = shadow });
            var mats = r.sharedMaterials;
            for (int s = 0; s < mats.Length; s++)
            {
                if (mats[s] == null) continue;
                int slot = s;
                foreach (var mode in new[] { "CONTROL: unchanged material copy", "specular highlights off", "environment reflections off", "smoothness 0.35", "emission black", "slot not drawn" })
                {
                    string md = mode; Material[] saved = null; Material tmp = null;
                    cands.Add(new Cand
                    {
                        name = md + " on slot " + slot + " " + mats[slot].name + " of " + r.name,
                        off = () =>
                        {
                            saved = rr.sharedMaterials; var arr = (Material[])saved.Clone();
                            tmp = new Material(saved[slot]);
                            if (md.StartsWith("CONTROL")) { }
                            else if (md == "emission black") { tmp.SetColor("_EmissionColor", Color.black); tmp.DisableKeyword("_EMISSION"); }
                            else if (md == "specular highlights off") { tmp.SetFloat("_SpecularHighlights", 0f); tmp.EnableKeyword("_SPECULARHIGHLIGHTS_OFF"); }
                            else if (md == "environment reflections off") { tmp.SetFloat("_EnvironmentReflections", 0f); tmp.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF"); }
                            else if (md == "smoothness 0.35") tmp.SetFloat("_Smoothness", 0.35f);
                            else { tmp.SetFloat("_Surface", 1f); tmp.SetOverrideTag("RenderType", "Transparent"); tmp.renderQueue = 3000; tmp.SetColor("_BaseColor", new Color(0, 0, 0, 0)); tmp.SetFloat("_SrcBlend", 0f); tmp.SetFloat("_DstBlend", 1f); tmp.SetFloat("_ZWrite", 0f); tmp.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); tmp.SetFloat("_SpecularHighlights", 0f); tmp.EnableKeyword("_SPECULARHIGHLIGHTS_OFF"); tmp.SetFloat("_EnvironmentReflections", 0f); tmp.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF"); }
                            arr[slot] = tmp; rr.sharedMaterials = arr;
                        },
                        on = () => { rr.sharedMaterials = saved; if (tmp != null) UnityEngine.Object.Destroy(tmp); }
                    });
                }
            }
            if (rr.HasPropertyBlock())
            {
                MaterialPropertyBlock whole = new MaterialPropertyBlock(); MaterialPropertyBlock[] slots = null;
                cands.Add(new Cand
                {
                    name = "property blocks cleared on " + r.name + " (" + BlockStr(rr) + ")",
                    off = () => { rr.GetPropertyBlock(whole); slots = new MaterialPropertyBlock[rr.sharedMaterials.Length]; for (int i = 0; i < slots.Length; i++) { slots[i] = new MaterialPropertyBlock(); rr.GetPropertyBlock(slots[i], i); } ClearBlocks(rr); },
                    on = () => { rr.SetPropertyBlock(whole.isEmpty ? null : whole); for (int i = 0; i < slots.Length; i++) if (!slots[i].isEmpty) rr.SetPropertyBlock(slots[i], i); }
                });
            }
        }
        foreach (var l in UnityEngine.Object.FindObjectsByType<Light>())
        {
            if (!l.enabled || l.intensity <= 0f) continue;
            var ll = l;
            cands.Add(new Cand { name = "light " + PathOf(l.transform) + " (" + l.type + ", int " + l.intensity.ToString("0.00") + ", col " + ColorStr(l.color) + ")", off = () => ll.enabled = false, on = () => ll.enabled = true });
        }
        foreach (var v in UnityEngine.Object.FindObjectsByType<Volume>())
        {
            if (!v.enabled) continue;
            var vv = v;
            cands.Add(new Cand { name = "volume " + PathOf(v.transform) + " (" + (v.sharedProfile != null ? v.sharedProfile.name : "-") + ")", off = () => vv.enabled = false, on = () => vv.enabled = true });
        }
        var cd = cam.GetUniversalAdditionalCameraData();
        if (cd.renderPostProcessing) cands.Add(new Cand { name = "camera post-processing (" + cam.name + ")", off = () => cd.renderPostProcessing = false, on = () => cd.renderPostProcessing = true });
        float refl = RenderSettings.reflectionIntensity;
        cands.Add(new Cand { name = "RenderSettings.reflectionIntensity " + refl.ToString("0.00") + " -> 0 (sky reflections)", off = () => RenderSettings.reflectionIntensity = 0f, on = () => RenderSettings.reflectionIntensity = refl });
        foreach (var p in UnityEngine.Object.FindObjectsByType<ReflectionProbe>())
        {
            if (!p.enabled) continue;
            var pp = p;
            cands.Add(new Cand { name = "reflection probe " + PathOf(p.transform), off = () => pp.enabled = false, on = () => pp.enabled = true });
        }

        Save(full, "ablate_" + tag + "_0_baseline");
        SaveMasked(full, refPx, "ablate_" + tag + "_0_baseline");
        var lines = new List<KeyValuePair<int, string>>();
        int savedShots = 0;
        foreach (var c in cands)
        {
            int n;
            try
            {
                c.off();
                var px = Render(cam);
                n = CountAdded(px, refPx);
                if (n <= baseCount * 0.5f && savedShots < 8 && !c.name.StartsWith("renderer ")) { savedShots++; Save(px, "ablate_" + tag + "_" + savedShots + "_" + Sanitize(c.name.Length > 48 ? c.name.Substring(0, 48) : c.name)); }
            }
            finally { c.on(); }
            lines.Add(new KeyValuePair<int, string>(n, c.name));
        }
        lines.Sort((a, b) => a.Key.CompareTo(b.Key));
        stepLines.AppendLine("    ABLATE " + tag + " at t=" + Time.time.ToString("0.00") + ": " + baseCount + " white px drawn by the " + GroupNames[g] + ", " + cands.Count + " candidates, monster " + MonsterState());
        int shown = 0;
        foreach (var kv in lines)
        {
            if (shown >= 10) break;
            shown++;
            float drop = baseCount > 0 ? 100f * (baseCount - kv.Key) / baseCount : 0f;
            stepLines.AppendLine("      " + kv.Key.ToString().PadLeft(5) + " px (" + drop.ToString("0").PadLeft(4) + " % less) with OFF: " + kv.Value);
        }
        if (baseCount >= sequenceFrom && !sequenceDone[g]) { sequenceDone[g] = true; Sequence(cam, g, refPx, baseCount, tag); }
    }

    /// <summary>White a group must draw before the cumulative sequence runs (once per group per run).</summary>
    public static int sequenceFrom = 150;
    static readonly bool[] sequenceDone = new bool[2];

    /// <summary>The suspects switched off cumulatively, in the order of the bug report, one more each render, with a screenshot after each,
    /// until the group draws (almost) no white: the ghost copy, particle systems, lights on the weapon, property blocks (telegraph / hit
    /// flash), shadow casting, emission, environment reflections, then specular highlights slot by slot.</summary>
    static void Sequence(Camera cam, int g, Color32[] refPx, int baseCount, string tag)
    {
        var group = g == 0 ? monsterWeapon : playerWeapon;
        var undo = new List<Action>();
        var steps = new List<KeyValuePair<string, Action>>();
        steps.Add(new KeyValuePair<string, Action>("ghost copies hidden (WeaponSurfaceProxy WeaponGhost)", () =>
        {
            foreach (var r in group) if (r != null && r.name.StartsWith("WeaponGhost") && r.gameObject.activeSelf) { var go = r.gameObject; go.SetActive(false); undo.Add(() => go.SetActive(true)); }
        }));
        steps.Add(new KeyValuePair<string, Action>("particle systems on the weapon off", () =>
        {
            foreach (var r in group) if (r is ParticleSystemRenderer && r.enabled) { var rr = r; rr.enabled = false; undo.Add(() => rr.enabled = true); }
        }));
        steps.Add(new KeyValuePair<string, Action>("lights on the weapon off", () =>
        {
            foreach (var r in group) if (r != null) foreach (var l in r.transform.root.GetComponentsInChildren<Light>()) { if (!l.enabled || !IsUnder(l.transform, r.transform.parent)) continue; var ll = l; ll.enabled = false; undo.Add(() => ll.enabled = true); }
        }));
        steps.Add(new KeyValuePair<string, Action>("property blocks cleared (telegraph / hit flash tints and emission)", () =>
        {
            foreach (var r in group) if (r != null && r.HasPropertyBlock()) { var rr = r; var whole = new MaterialPropertyBlock(); rr.GetPropertyBlock(whole); ClearBlocks(rr); undo.Add(() => rr.SetPropertyBlock(whole.isEmpty ? null : whole)); }
        }));
        steps.Add(new KeyValuePair<string, Action>("shadow casting off", () =>
        {
            foreach (var r in group) if (r != null && !(r is ParticleSystemRenderer)) { var rr = r; var s = rr.shadowCastingMode; rr.shadowCastingMode = ShadowCastingMode.Off; undo.Add(() => rr.shadowCastingMode = s); }
        }));
        foreach (var what in new[] { "emission black", "environment reflections off" })
        {
            string w = what;
            steps.Add(new KeyValuePair<string, Action>(w + " on every slot", () =>
            {
                foreach (var r in group)
                {
                    if (r == null || r is ParticleSystemRenderer || !r.enabled) continue;
                    var rr = r; var saved = rr.sharedMaterials; var arr = new Material[saved.Length];
                    for (int i = 0; i < saved.Length; i++)
                    {
                        if (saved[i] == null) continue;
                        var m = new Material(saved[i]);
                        if (w == "emission black") { m.SetColor("_EmissionColor", Color.black); m.DisableKeyword("_EMISSION"); }
                        else { m.SetFloat("_EnvironmentReflections", 0f); m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF"); }
                        arr[i] = m;
                    }
                    rr.sharedMaterials = arr;
                    undo.Add(() => { var tmp = rr.sharedMaterials; rr.sharedMaterials = saved; foreach (var m in tmp) if (m != null) UnityEngine.Object.Destroy(m); });
                }
            }));
        }
        foreach (var r0 in group)
        {
            if (r0 == null || r0 is ParticleSystemRenderer || !r0.enabled) continue;
            for (int s = 0; s < r0.sharedMaterials.Length; s++)
            {
                var rr = r0; int slot = s; string mn = r0.sharedMaterials[s] != null ? r0.sharedMaterials[s].name : "null";
                steps.Add(new KeyValuePair<string, Action>("specular highlights off on " + rr.name + " slot " + slot + " " + mn, () =>
                {
                    var saved = rr.sharedMaterials; var arr = (Material[])saved.Clone();
                    if (arr[slot] == null) return;
                    var m = new Material(arr[slot]); m.SetFloat("_SpecularHighlights", 0f); m.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
                    arr[slot] = m; rr.sharedMaterials = arr;
                    undo.Add(() => { rr.sharedMaterials = saved; UnityEngine.Object.Destroy(m); });
                }));
            }
        }
        stepLines.AppendLine("    SEQUENCE " + tag + " (cumulative, screenshot after each):  start " + baseCount + " white px");
        try
        {
            int k = 0;
            foreach (var st in steps)
            {
                k++;
                st.Value();
                var px = Render(cam);
                int n = CountAdded(px, refPx);
                Save(px, "seq_" + tag + "_" + k.ToString("00") + "_" + n + "px_" + Sanitize(st.Key));
                stepLines.AppendLine("      " + k.ToString("00") + ". + " + st.Key.PadRight(70) + " -> " + n.ToString().PadLeft(5) + " white px");
                if (n <= Math.Max(3, baseCount / 20)) { stepLines.AppendLine("      white gone after step " + k); break; }
            }
        }
        finally { for (int i = undo.Count - 1; i >= 0; i--) undo[i](); }
    }

    static bool IsUnder(Transform t, Transform parent)
    {
        for (var p = t; p != null; p = p.parent) if (p == parent) return true;
        return false;
    }

    static string MonsterState()
    {
        if (mon == null) return "-";
        var ai = mon.GetComponent<MonsterAI>(); var def = mon.GetComponent<MonsterDefense>();
        return (mon.isAttacking ? "attacking " + (ai.CurrentAttack != null ? ai.CurrentAttack.name : "") : "") + (ai.IsDefending ? " guarding" : "") + (def != null && def.IsStaggered ? " staggered" : "") + (ai.IsBusy ? " busy" : " free");
    }

    static string MatNames(Renderer r)
    {
        var sb = new StringBuilder();
        foreach (var m in r.sharedMaterials) { if (sb.Length > 0) sb.Append(", "); sb.Append(m != null ? m.name : "null"); }
        return sb.ToString();
    }

    static string ColorStr(Color c) => c.r.ToString("0.00") + "," + c.g.ToString("0.00") + "," + c.b.ToString("0.00");

    static string BlockStr(Renderer r)
    {
        var b = new MaterialPropertyBlock(); var sb = new StringBuilder();
        r.GetPropertyBlock(b);
        if (!b.isEmpty) sb.Append("whole: base " + ColorStr(b.GetColor("_BaseColor")) + " emission " + ColorStr(b.GetColor("_EmissionColor")));
        for (int i = 0; i < r.sharedMaterials.Length; i++)
        {
            r.GetPropertyBlock(b, i);
            if (!b.isEmpty) sb.Append(" slot" + i + ": base " + ColorStr(b.GetColor("_BaseColor")) + " emission " + ColorStr(b.GetColor("_EmissionColor")));
        }
        return sb.ToString();
    }

    static void ClearBlocks(Renderer r)
    {
        r.SetPropertyBlock(null);
        for (int i = 0; i < r.sharedMaterials.Length; i++) r.SetPropertyBlock(null, i);
    }

    static Material[] SpecOff(Material[] mats)
    {
        var o = new Material[mats.Length];
        for (int i = 0; i < mats.Length; i++)
        {
            if (mats[i] == null) continue;
            var m = new Material(mats[i]);
            if (m.HasProperty("_SpecularHighlights")) m.SetFloat("_SpecularHighlights", 0f);
            if (m.HasProperty("_EnvironmentReflections")) m.SetFloat("_EnvironmentReflections", 0f);
            m.EnableKeyword("_SPECULARHIGHLIGHTS_OFF"); m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            o[i] = m;
        }
        return o;
    }

    // ───────────────────────── whole-run toggles ─────────────────────────
    static void ApplyToggles(string toggles)
    {
        foreach (var raw in toggles.Split(';'))
        {
            string t = raw.Trim(); if (t.Length == 0) continue;
            int c = t.IndexOf(':'); string key = c >= 0 ? t.Substring(0, c) : t, arg = c >= 0 ? t.Substring(c + 1) : "";
            int hits = 0;
            if (key == "renderer" || key == "spec" || key == "pb")
            {
                foreach (var r in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include))
                {
                    if (PathOf(r.transform).IndexOf(arg, StringComparison.OrdinalIgnoreCase) < 0 && FullPath(r.transform).IndexOf(arg, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    var rr = r; hits++;
                    if (key == "renderer") { bool e = rr.enabled; rr.enabled = false; restore.Add(() => { if (rr != null) rr.enabled = e; }); }
                    else if (key == "spec") { var saved = rr.sharedMaterials; rr.sharedMaterials = SpecOff(saved); restore.Add(() => { if (rr != null) rr.sharedMaterials = saved; }); }
                    else pbClear.Add(rr);
                }
            }
            else if (key == "light")
            {
                foreach (var l in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsInactive.Include))
                {
                    if (FullPath(l.transform).IndexOf(arg, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    var ll = l; bool e = ll.enabled; ll.enabled = false; hits++; restore.Add(() => { if (ll != null) ll.enabled = e; });
                }
            }
            else if (key == "post")
            {
                var cd = head.GetUniversalAdditionalCameraData(); bool e = cd.renderPostProcessing; cd.renderPostProcessing = false; hits++;
                var sd = side.GetUniversalAdditionalCameraData(); sd.renderPostProcessing = false;
                restore.Add(() => cd.renderPostProcessing = e);
            }
            else if (key == "set")
            {
                // set:<renderer path text>:<slot>:<property>=<value>   on a runtime copy of that slot's material
                var parts = arg.Split(':');
                if (parts.Length == 3)
                {
                    int slot = int.Parse(parts[1]); var kv = parts[2].Split('=');
                    float val = float.Parse(kv[1], System.Globalization.CultureInfo.InvariantCulture);
                    foreach (var r in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include))
                    {
                        if (FullPath(r.transform).IndexOf(parts[0], StringComparison.OrdinalIgnoreCase) < 0 || slot >= r.sharedMaterials.Length) continue;
                        var rr = r; var saved = rr.sharedMaterials; var arr = (Material[])saved.Clone();
                        var m = new Material(saved[slot]); m.SetFloat(kv[0], val); arr[slot] = m; rr.sharedMaterials = arr; hits++;
                        restore.Add(() => { if (rr != null) rr.sharedMaterials = saved; UnityEngine.Object.Destroy(m); });
                    }
                }
            }
            else if (key == "envrefl") { float v = RenderSettings.reflectionIntensity; RenderSettings.reflectionIntensity = 0f; hits++; restore.Add(() => RenderSettings.reflectionIntensity = v); }
            else if (key == "ghost")
            {
                bool want = arg == "on";
                foreach (var p in UnityEngine.Object.FindObjectsByType<WeaponSurfaceProxy>()) { var pp = p; bool e = pp.ghostEnabled; pp.ghostEnabled = want; hits++; restore.Add(() => { if (pp != null) pp.ghostEnabled = e; }); }
            }
            log.AppendLine("   toggle " + t + ": " + hits + " object(s)");
        }
    }

    static string FullPath(Transform t)
    {
        var sb = new StringBuilder(t.name);
        for (var p = t.parent; p != null; p = p.parent) sb.Insert(0, p.name + "/");
        return sb.ToString();
    }
}
