// ExpertHarness.cs
// Purpose: Play-mode comparison of Level 3 Normal and Expert. One 60 s fight per call with the same scripted player: the rig walks
//          (strafe left / right, lunge in, retreat, stand still, mixed with sword swings at the monster), the monster's HP is held in
//          phase 1 (0-20 s), phase 2 (20-40 s) and enraged (40-60 s), and the player is healed so the fight never ends early.
//          Reports: prediction error (cm) per horizon for the head and the dominant hand against what the scripted player actually did
//          (and the "stay still" baseline on the same moments), inference time (average, p95, max, over budget), allocations per
//          inference, decisions the prediction changed, attack frequency, damaging hits on the player per minute, block rate per phase
//          (cap 55 %) and fallback events.
//          The predictor reads the scripted player through a pose source fixed to the tracking space at the start, so walking the rig
//          looks to it exactly like a player walking in the room.
//          Driven from Unity MCP: ExpertHarness.Run("expert") / Run("normal"), poll Status(), Save(name).
// Dependencies: MonsterExpertBrain, ExpertMotionPredictor, MonsterTactics, MonsterAI, BattleManager, CombatVerification, StrikeFit.ExpertAI
// Attach to: nothing (static, Editor only)

using System.Collections;
using System.Collections.Generic;
using System.Text;
using StrikeFit.ExpertAI;
using UnityEditor;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;

public static class ExpertHarness
{
    static IEnumerator job;
    static readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
    static readonly StringBuilder log = new StringBuilder();
    static int lastFrame = -1;
    public static float seconds = 60f;
    public static bool Running => job != null;
    public static string Status() => (job != null ? "RUNNING t=" + Time.time.ToString("0.0") + "\n" : "IDLE\n") + log;

    public static string Save(string name)
    {
        System.IO.Directory.CreateDirectory("Captures/ExpertAI");
        System.IO.File.WriteAllText("Captures/ExpertAI/" + name + ".txt", log.ToString());
        return "Captures/ExpertAI/" + name + ".txt";
    }

    /// <summary>"expert" or "normal". Sets the saved mode for this fight (restored afterwards) and starts the battle.</summary>
    public static string Run(string mode)
    {
        if (!EditorApplication.isPlaying) return "not playing";
        if (BattleManager.Instance != null && BattleManager.Instance.IsBattleActive) return "a battle is already running: enter Play mode again (the mode is fixed at battle start)";
        log.Length = 0; stack.Clear();
        job = Main(mode == "expert");
        Install(true);
        return "started " + mode;
    }

    struct Step { }
    static void Install(bool on)
    {
        var loop = PlayerLoop.GetCurrentPlayerLoop();
        for (int i = 0; i < loop.subSystemList.Length; i++)
        {
            if (loop.subSystemList[i].type != typeof(PreUpdate)) continue;
            var list = new List<PlayerLoopSystem>(loop.subSystemList[i].subSystemList ?? new PlayerLoopSystem[0]);
            list.RemoveAll(x => x.type == typeof(Step));
            if (on) list.Add(new PlayerLoopSystem { type = typeof(Step), updateDelegate = Tick });
            loop.subSystemList[i].subSystemList = list.ToArray();
        }
        PlayerLoop.SetPlayerLoop(loop);
    }

    static void Tick()
    {
        if (job == null || !EditorApplication.isPlaying) { job = null; Install(false); return; }
        if (Time.frameCount == lastFrame) return;
        lastFrame = Time.frameCount;
        try
        {
            while (true)
            {
                var top = stack.Count > 0 ? stack.Peek() : job;
                if (top.MoveNext()) { if (top.Current is IEnumerator n) { stack.Push(n); continue; } break; }
                if (stack.Count > 0) { stack.Pop(); continue; }
                log.AppendLine("DONE"); job = null; Install(false); Time.captureDeltaTime = 0f; RestoreMode(); return;
            }
        }
        catch (System.Exception e) { log.AppendLine("EXCEPTION " + e); job = null; Install(false); Time.captureDeltaTime = 0f; RestoreMode(); }
    }

    static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }

    static bool hadPref; static int oldPref;
    static void SetMode(bool expert)
    {
        hadPref = PlayerPrefs.HasKey(MonsterAIModeSetting.PrefKey); oldPref = PlayerPrefs.GetInt(MonsterAIModeSetting.PrefKey, 0);
        PlayerPrefs.SetInt(MonsterAIModeSetting.PrefKey, expert ? 1 : 0);
    }
    static void RestoreMode()
    {
        if (hadPref) PlayerPrefs.SetInt(MonsterAIModeSetting.PrefKey, oldPref); else PlayerPrefs.DeleteKey(MonsterAIModeSetting.PrefKey);
        PlayerPrefs.Save();
    }

    /// <summary>Head and hands in the tracking space as it was when the run started: moving the rig reads as walking.</summary>
    sealed class FrozenSpaceSource : IPlayerPoseSource
    {
        readonly OVRCameraRig rig; readonly Matrix4x4 w2l;
        public FrozenSpaceSource(OVRCameraRig rig) { this.rig = rig; w2l = rig.trackingSpace.worldToLocalMatrix; }
        public Transform Space => rig.trackingSpace;
        public bool TryGet(float floorY, out Vector3 head, out Vector3 r, out Vector3 l, out float gr, out float gl)
        {
            var pc = PlayerCombat.Instance;
            head = Local(rig.centerEyeAnchor.position, floorY);
            r = Local(pc != null && pc.weaponTransform != null ? pc.weaponTransform.position : rig.rightHandAnchor.position, floorY);
            l = Local(pc != null && pc.offhandWeaponTransform != null ? pc.offhandWeaponTransform.position : rig.leftHandAnchor.position, floorY);
            gr = gl = 1f;
            return true;
        }

        Vector3 Local(Vector3 world, float floorY) { Vector3 v = w2l.MultiplyPoint3x4(world); v.y = world.y - floorY; return v; }
    }

    // scripted body path: displacement (world, horizontal) from the start position, by time
    static Vector3 fwd, right;   // fwd = toward the monster
    static Vector3 Body(float t)
    {
        float c = t % 16f;
        // 0-2 strafe left, 2-4 strafe right back, 4-6 strafe right, 6-8 strafe left back (0.6 m/s): lateral -1.2..+1.2 m
        if (c < 2f) return -right * (0.6f * c);
        if (c < 4f) return -right * (1.2f - 0.6f * (c - 2f));
        if (c < 6f) return right * (0.6f * (c - 4f));
        if (c < 8f) return right * (1.2f - 0.6f * (c - 6f));
        // 8-8.5 lunge in 0.5 m (1 m/s), 8.5-9.5 retreat 1.0 m, 9.5-10 back in 0.5 m
        if (c < 8.5f) return fwd * (1.0f * (c - 8f));
        if (c < 9.5f) return fwd * (0.5f - 1.0f * (c - 8.5f));
        if (c < 10f) return fwd * (-0.5f + 1.0f * (c - 9.5f));
        // 10-12 stand still
        if (c < 12f) return Vector3.zero;
        // 12-16 mixed: a slow weave
        float u = c - 12f;
        return right * (0.35f * Mathf.Sin(u * 2.4f)) + fwd * (0.25f * Mathf.Sin(u * 1.3f));
    }
    /// <summary>Mixed part (12-16 s of each 16 s cycle), two strokes: the sword moves slowly (below the swing gate) to the start of the
    /// stroke, waits, then sweeps across in 0.3 s, so every stroke starts from rest like a real one.</summary>
    static bool InMixed(float t) => t % 16f >= 12f;

    struct Pending { public float t0; public Vector3 head0, hand0; public Vector3[] predHead, predHand; }

    static IEnumerator Main(bool expert)
    {
        SetMode(expert);
        CombatVerification.frameDt = 1f / 72f;
        CombatVerification.Prepare();
        var pnp = Object.FindAnyObjectByType<PlayerNamePicker>();
        var pf = typeof(PlayerNamePicker).GetField("panel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (pnp != null && pf != null && pf.GetValue(pnp) is GameObject panel && panel != null) panel.SetActive(false);
        var pc = PlayerCombat.Instance;
        if (pc != null && pc.weaponSets != null && pc.weaponSets.Count > 0) pc.EquipWeaponSet(CombatVerification.l3Weapon);
        yield return Frames(3);
        BattleManager.Instance.StartBattleFromInspector();
        float w0 = Time.time;
        while (Time.time - w0 < 30f)
        {
            var mm = BattleManager.Instance.ActiveMonster;
            if (mm != null && mm.isAlive) { var e = mm.GetComponent<MonsterEntrance>(); if (e == null || e.Complete) break; }
            yield return null;
        }
        var mon = BattleManager.Instance.ActiveMonster;
        var ai = mon.GetComponent<MonsterAI>(); var tac = mon.GetComponent<MonsterTactics>(); var hp = mon.GetComponent<MonsterHealth>();
        var brain = mon.GetComponent<MonsterExpertBrain>(); var phases = mon.GetComponent<BossPhaseController>();
        var rig = Object.FindAnyObjectByType<OVRCameraRig>();
        yield return Frames(3);
        var pred = mon.GetComponent<ExpertMotionPredictor>();
        if (pred != null) pred.Source = new FrozenSpaceSource(rig);
        log.AppendLine("== " + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name + " " + (expert ? "EXPERT" : "NORMAL") + " | FightMode " + MonsterAIModeSetting.FightMode +
                       ", ExpertThisFight " + MonsterAIModeSetting.ExpertThisFight + ", brain active " + (brain != null && brain.Active) + ", predictor " + (pred != null ? (pred.Healthy ? "running" : "off: " + pred.DisabledReason) : "none") +
                       ", advisor set " + (ai.advisor != null) + ", early warning set " + (tac != null && tac.earlyWarning != null));
        if (pred != null) log.AppendLine("   mapping: " + pred.config.mapping.Name + " | head height above the arena floor " + (rig.centerEyeAnchor.position.y - mon.transform.position.y).ToString("0.00") +
                                         " m (training mean 1.53 +- 0.06), floor used by the predictor at runtime: see below | warm-up " + pred.WarmUpMs.ToString("0.0") + " ms for 3 runs at load");

        Vector3 to = mon.transform.position - rig.centerEyeAnchor.position; to.y = 0f; fwd = to.normalized; right = Vector3.Cross(Vector3.up, fwd);
        Vector3 rigStart = rig.transform.position;
        Transform weapon = pc.weaponTransform; Vector3 weaponRest = weapon.localPosition;
        if (tac != null) tac.ResetStats();
        int attacks0 = ai.TableAttackCount, hits = 0;
        System.Action<float, bool, float> onHurt = (a, b, h) => { if (a > 0.001f) hits++; };
        PlayerHealth.DamageTaken += onHurt;
        var pending = new List<Pending>();
        int H = Forecast.Horizons;
        var errHead = new double[H]; var errHand = new double[H]; var stillHead = new double[H]; var stillHand = new double[H]; var nErr = new int[H];
        float lastPredTime = -1f; float t0 = Time.time;
        var headHist = new List<KeyValuePair<float, Vector3>>(); var handHist = new List<KeyValuePair<float, Vector3>>();
        int phaseIdx = -1;
        float[] hpAt = { 0.95f, 0.55f, 0.22f };
        long allocBefore = System.GC.GetAllocatedBytesForCurrentThread();
        while (Time.time - t0 < seconds && mon.isAlive)
        {
            float t = Time.time - t0;
            int ph = Mathf.Min(2, (int)(t / (seconds / 3f)));
            if (ph != phaseIdx) { phaseIdx = ph; }
            hp.currentHealth = hp.maxHealth * hpAt[ph];
            if (PlayerHealth.Instance.currentHealth < PlayerHealth.Instance.maxHealth * 0.5f) PlayerHealth.Instance.FullHeal();
            // walk
            rig.transform.position = rigStart + Body(t);
            // swing in the mixed part: the right sword sweeps across in front of the monster
            if (InMixed(t))
            {
                float u = ((t % 16f) - 12f) % 2.0f;
                Vector3 chest = mon.transform.position + Vector3.up * 1.2f;
                Vector3 a = chest - fwd * 0.6f + right * 0.9f, b = chest - fwd * 0.1f - right * 0.4f;
                if (u < 1.7f) weapon.position = Vector3.MoveTowards(weapon.position, a, 1.0f * Time.deltaTime);   // to the start, below the gate
                else weapon.position = Vector3.Lerp(a, b, (u - 1.7f) / 0.3f);                               // stroke (~4.6 m/s)
            }
            else weapon.localPosition = weaponRest;
            // truth history (world)
            headHist.Add(new KeyValuePair<float, Vector3>(Time.time, rig.centerEyeAnchor.position));
            handHist.Add(new KeyValuePair<float, Vector3>(Time.time, weapon.position));
            // new prediction -> remember it
            if (pred != null && pred.HasPrediction && pred.PredictionTime != lastPredTime)
            {
                lastPredTime = pred.PredictionTime;
                var p = new Pending { t0 = pred.PredictionTime, head0 = At(headHist, pred.PredictionTime), hand0 = At(handHist, pred.PredictionTime), predHead = new Vector3[H], predHand = new Vector3[H] };
                for (int h = 0; h < H; h++) { p.predHead[h] = pred.RawWorldOffset(Forecast.Point.Head, h); p.predHand[h] = pred.RawWorldOffset(pred.PointForHand(0), h); }   // measurement: every output, ungated
                pending.Add(p);
            }
            // score predictions whose horizon has passed
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                var p = pending[i]; bool all = true;
                for (int h = 0; h < H; h++)
                {
                    float due = p.t0 + Forecast.HorizonSeconds[h];
                    if (Time.time < due) { all = false; continue; }
                    if (p.predHead[h].x == float.MaxValue) continue;
                    Vector3 trueHead = At(headHist, due) - p.head0, trueHand = At(handHist, due) - p.hand0;
                    errHead[h] += (p.predHead[h] - trueHead).magnitude; stillHead[h] += trueHead.magnitude;
                    errHand[h] += (p.predHand[h] - trueHand).magnitude; stillHand[h] += trueHand.magnitude;
                    nErr[h]++;
                    p.predHead[h].x = float.MaxValue;   // scored
                }
                if (all) pending.RemoveAt(i); else pending[i] = p;
            }
            yield return null;
        }
        long allocAfter = System.GC.GetAllocatedBytesForCurrentThread();
        PlayerHealth.DamageTaken -= onHurt;
        rig.transform.position = rigStart; weapon.localPosition = weaponRest;
        float span = Time.time - t0;
        int attacks = ai.TableAttackCount - attacks0;
        log.AppendLine("  " + span.ToString("0") + " s: monster attacks " + attacks + " (" + (attacks * 60f / span).ToString("0.0") + "/min), damaging hits on the player " + hits + " (" + (hits * 60f / span).ToString("0.0") + "/min)");
        if (tac != null)
            for (int p = 0; p < 3; p++)
            {
                int inc = tac.IncomingByPhase[p], rai = tac.RaisedByPhase[p];
                log.AppendLine("  phase " + (p + 1) + ": incoming swings " + inc + ", guards " + rai + " -> block rate " + (inc > 0 ? (100f * rai / inc).ToString("0") : "-") + " % (cap 55 %)" + (inc > 0 && (float)rai / inc > 0.55f + 1e-4f ? " OVER CAP" : ""));
            }
        if (pred != null)
        {
            var sb = new StringBuilder("  prediction error (cm), model vs stay-still baseline on the same moments:\n");
            for (int h = 0; h < H; h++)
                if (nErr[h] > 0)
                    sb.AppendLine("    " + Forecast.HorizonSeconds[h].ToString("0.00") + " s (n=" + nErr[h] + "): head " + (100 * errHead[h] / nErr[h]).ToString("0.0") + " vs " + (100 * stillHead[h] / nErr[h]).ToString("0.0") +
                                  " | dominant hand " + (100 * errHand[h] / nErr[h]).ToString("0.0") + " vs " + (100 * stillHand[h] / nErr[h]).ToString("0.0"));
            log.Append(sb);
            log.AppendLine("  inference: " + pred.Inferences + " runs, avg " + pred.AverageMs.ToString("0.000") + " ms, p95 " + pred.P95Ms.ToString("0.000") + " ms, max " + pred.MaxMs.ToString("0.000") +
                           " ms, over the 1.5 ms budget " + pred.OverBudget + " | fallbacks " + pred.Fallbacks + (pred.Healthy ? "" : " (" + pred.DisabledReason + ")"));
            log.AppendLine("  managed allocations on the main thread during the run (whole game, not only the predictor): " + ((allocAfter - allocBefore) / 1024) + " KB");
        }
        if (brain != null && brain.Active)
            log.AppendLine("  decisions the prediction changed: " + brain.DecisionsChanged + " (held strikes " + brain.HeldStrikes + ", swapped to a sweep " + brain.SwappedAttacks + ", aimed wind-ups " + brain.AimedAttacks +
                           ", led throws " + brain.LeadThrows + ") | early warnings used by the guard " + (tac != null ? tac.EarlyWarnings : 0) + " | governor: prediction blocked by the hit cap " + brain.Governor.BlockedByHitCap +
                           " times, hit cap " + brain.config.HitsPerMinuteCap.ToString("0.0") + "/min");
        if (pred != null) log.AppendLine("  floor height used " + pred.Floor.ToString("0.00") + " m -> head height fed to the model " + (rig.centerEyeAnchor.position.y - pred.Floor).ToString("0.00") + " m");
        log.AppendLine("  HUD enemy line: " + HudLine());
    }

    static string HudLine()
    {
        var ui = CombatUIController.Instance;
        if (ui == null || ui.monsterHealthText == null) return "-";
        return ui.monsterHealthText.text;
    }

    /// <summary>Linear interpolation in a time-ordered history.</summary>
    static Vector3 At(List<KeyValuePair<float, Vector3>> hist, float time)
    {
        if (hist.Count == 0) return Vector3.zero;
        int lo = 0, hi = hist.Count - 1;
        if (time <= hist[0].Key) return hist[0].Value;
        if (time >= hist[hi].Key) return hist[hi].Value;
        while (hi - lo > 1) { int mid = (lo + hi) / 2; if (hist[mid].Key <= time) lo = mid; else hi = mid; }
        float k = (time - hist[lo].Key) / Mathf.Max(1e-6f, hist[hi].Key - hist[lo].Key);
        return Vector3.Lerp(hist[lo].Value, hist[hi].Value, k);
    }

    /// <summary>Allocations of the forecaster alone: bytes per inference over <paramref name="n"/> runs spread over frames (Editor).</summary>
    public static string AllocPerInference(int n)
    {
        var asset = AssetDatabase.LoadAssetAtPath<Unity.InferenceEngine.ModelAsset>(ExpertAISetup.ModelPath);
        NormStats.TryParse(AssetDatabase.LoadAssetAtPath<TextAsset>(ExpertAISetup.NormPath).text, out NormStats norm, out _);
        var raw = new float[200];
        for (int i = 0; i < 20; i++) { raw[i * 10 + 2] = 1.5f; raw[i * 10 + 5] = 1.0f; raw[i * 10 + 8] = 1.0f; raw[i * 10 + 9] = 1f; }
        using (var f = new MotionForecaster(asset, norm))
        {
            f.Begin(raw); while (!f.Step(128)) { } f.Finish();   // warm-up
            long a0 = System.GC.GetAllocatedBytesForCurrentThread();
            double sum = 0, max = 0;
            for (int k = 0; k < n; k++) { f.Begin(raw); while (!f.Step(128)) { } f.Finish(); sum += f.LastMs; max = System.Math.Max(max, f.LastMs); }
            long a1 = System.GC.GetAllocatedBytesForCurrentThread();
            return n + " inferences in 128-layer steps: " + ((a1 - a0) / (double)n).ToString("0") + " bytes allocated per inference (none per frame in between), avg " + (sum / n).ToString("0.000") + " ms, max " + max.ToString("0.000") + " ms";
        }
    }
}
