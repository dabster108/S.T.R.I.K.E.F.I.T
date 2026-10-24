// ExpertAISetup.cs
// Purpose: Level 3 Expert mode setup and the axis-mapping test.
//          - Apply: the ExpertAIConfig asset next to the Level 3 defence profile (Assets/Settings/Defense/L3_HarborTyrant_Expert.asset)
//            pointing at Assets/Models/ExpertAI, and a MonsterExpertBrain on the Level 3 prefab only (Levels 1 and 2 never get one).
//          - MappingProbe: the dataset is z-up and its horizontal axes are unknown in Unity terms, so the mapping is found by test, not
//            guessed. For each of the 8 candidates (horizontal permutation x signs, up kept), synthetic players walk at a constant
//            0.6 m/s in six Unity directions (forward, back, left, right, up, down) for the whole 1 s window; the model's predicted head
//            displacement (0.25 / 0.5 s) is mapped back to Unity and scored by how well it continues the walk (cosine, and the share
//            of the true displacement). The best candidate on the four horizontal directions wins; the mirror check compares it with
//            its left-right mirror image on the left / right walks only. Writes the mapping and the evidence into the config.
//            UNVERIFIED on real arm movement: the probe only shows which mapping makes the model's own extrapolation consistent.
// Dependencies: StrikeFit.ExpertAI, MonsterExpertBrain, CombatTuning (prefab paths), Unity.InferenceEngine
// Attach to: nothing (static, Editor only)

using System.Text;
using StrikeFit.ExpertAI;
using Unity.InferenceEngine;
using UnityEditor;
using UnityEngine;

public static class ExpertAISetup
{
    public const string ConfigPath = "Assets/Settings/Defense/L3_HarborTyrant_Expert.asset";
    public const string ModelPath = "Assets/Models/ExpertAI/motion_forecaster.onnx";
    public const string NormPath = "Assets/Models/ExpertAI/norm.json";

    [MenuItem("Tools/Combat/Apply Expert AI Setup")]
    public static void ApplyMenu() { Debug.Log(Apply()); }

    public static string Apply()
    {
        var cfg = AssetDatabase.LoadAssetAtPath<ExpertAIConfig>(ConfigPath);
        if (cfg == null)
        {
            cfg = ScriptableObject.CreateInstance<ExpertAIConfig>();
            AssetDatabase.CreateAsset(cfg, ConfigPath);
        }
        cfg.model = AssetDatabase.LoadAssetAtPath<ModelAsset>(ModelPath);
        cfg.norm = AssetDatabase.LoadAssetAtPath<TextAsset>(NormPath);
        EditorUtility.SetDirty(cfg);
        var log = new StringBuilder("config " + ConfigPath + ": model " + (cfg.model != null) + ", norm " + (cfg.norm != null) + "\n");
        string[] paths = { CombatTuning.L1Prefab, CombatTuning.L2Prefab, CombatTuning.L3Prefab };
        for (int l = 0; l < 3; l++)
        {
            var root = PrefabUtility.LoadPrefabContents(paths[l]);
            try
            {
                var b = root.GetComponent<MonsterExpertBrain>();
                if (l == 2) { if (b == null) b = root.AddComponent<MonsterExpertBrain>(); b.config = cfg; }
                else if (b != null) Object.DestroyImmediate(b, true);   // Levels 1 and 2 never use the model
                var p = root.GetComponent<ExpertMotionPredictor>();
                if (l != 2 && p != null) Object.DestroyImmediate(p, true);
                PrefabUtility.SaveAsPrefabAsset(root, paths[l]);
                log.AppendLine("L" + (l + 1) + ": MonsterExpertBrain " + (root.GetComponent<MonsterExpertBrain>() != null));
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        AssetDatabase.SaveAssets();
        return log.ToString();
    }

    // ───────────────────────── mapping probe ─────────────────────────
    static readonly string[] DirNames = { "forward", "back", "left", "right", "up", "down" };
    static readonly Vector3[] Dirs = { Vector3.forward, Vector3.back, Vector3.left, Vector3.right, Vector3.up, Vector3.down };

    /// <summary>Runs every candidate, writes the winner into the config when <paramref name="write"/>, returns the evidence table.</summary>
    public static string MappingProbe(bool write)
    {
        var cfg = AssetDatabase.LoadAssetAtPath<ExpertAIConfig>(ConfigPath);
        var asset = AssetDatabase.LoadAssetAtPath<ModelAsset>(ModelPath);
        var normText = AssetDatabase.LoadAssetAtPath<TextAsset>(NormPath);
        if (asset == null || normText == null || !NormStats.TryParse(normText.text, out NormStats norm, out string err)) return "model or norm.json missing";
        var f = new MotionForecaster(asset, norm);
        var sb = new StringBuilder();
        sb.AppendLine("Synthetic player: head 1.53 m up, right hand 0.25 m right / 0.35 m forward / 0.45 m below the head, left hand mirrored;");
        sb.AppendLine("the whole body moves at a constant 0.6 m/s for the 1 s window. Score = cosine between the predicted head displacement (0.5 s)");
        sb.AppendLine("and the walk direction, plus the share of the true 0.30 m displacement that was predicted.");
        sb.AppendLine("mapping".PadRight(72) + "  fwd    back   left   right  up     down   | horiz mean cos  share");
        var cands = AxisMapping.Candidates();
        int best = -1; float bestScore = -9f;
        var cosAll = new float[cands.Length, 6];
        for (int c = 0; c < cands.Length; c++)
        {
            var line = new StringBuilder(cands[c].Name.PadRight(72) + "  ");
            float sumCos = 0f, sumShare = 0f;
            for (int d = 0; d < 6; d++)
            {
                Vector3 pred = PredictWalk(f, cands[c], Dirs[d], 0.6f, 1);   // 0.5 s horizon
                float cos = pred.sqrMagnitude > 1e-8f ? Vector3.Dot(pred.normalized, Dirs[d]) : 0f;
                float share = Vector3.Dot(pred, Dirs[d]) / 0.30f;
                cosAll[c, d] = cos;
                line.Append(cos.ToString("+0.00;-0.00").PadRight(7));
                if (d < 4) { sumCos += cos; sumShare += share; }
            }
            float meanCos = sumCos / 4f, meanShare = sumShare / 4f;
            line.Append("| " + meanCos.ToString("+0.00;-0.00").PadRight(15) + meanShare.ToString("0.00"));
            sb.AppendLine(line.ToString());
            if (meanCos > bestScore) { bestScore = meanCos; best = c; }
        }
        var chosen = cands[best];
        // Mirror check: the same candidate with dataset x flipped (left-right mirror). A handedness mistake shows as left / right
        // walks predicted the wrong way round.
        var mirror = new AxisMapping(chosen.swapHorizontal, -chosen.xSign, chosen.ySign);
        int mi = System.Array.FindIndex(cands, m => m.swapHorizontal == mirror.swapHorizontal && m.xSign == mirror.xSign && m.ySign == mirror.ySign);
        sb.AppendLine("CHOSEN: " + chosen.Name + " (horizontal mean cosine " + bestScore.ToString("+0.00;-0.00") + ")");
        sb.AppendLine("MIRROR CHECK (left / right walks): chosen left " + cosAll[best, 2].ToString("+0.00;-0.00") + ", right " + cosAll[best, 3].ToString("+0.00;-0.00") +
                      " | its mirror left " + cosAll[mi, 2].ToString("+0.00;-0.00") + ", right " + cosAll[mi, 3].ToString("+0.00;-0.00") +
                      ((cosAll[best, 2] + cosAll[best, 3]) > (cosAll[mi, 2] + cosAll[mi, 3]) ? "  -> the chosen handedness predicts sideways walks the right way round" : "  -> WARNING: the mirror predicts sideways walks better"));
        // Short horizon too, for the record.
        Vector3 fwd25 = PredictWalk(f, chosen, Vector3.forward, 0.6f, 0), right25 = PredictWalk(f, chosen, Vector3.right, 0.6f, 0);
        sb.AppendLine("chosen at 0.25 s: forward walk -> " + fwd25.ToString("0.000") + " m, right walk -> " + right25.ToString("0.000") + " m (true 0.15 m along the walk)");
        f.Dispose();
        if (write && cfg != null)
        {
            cfg.mapping = chosen;
            cfg.mappingEvidence = "ExpertMappingProbe " + System.DateTime.Now.ToString("yyyy-MM-dd") + ": best horizontal mean cosine " + bestScore.ToString("0.00") +
                                  " of 8 candidates on synthetic 0.6 m/s walks (0.5 s horizon); mirror check passed: " +
                                  ((cosAll[best, 2] + cosAll[best, 3]) > (cosAll[mi, 2] + cosAll[mi, 3])) + ". UNVERIFIED on real arm movement.";
            EditorUtility.SetDirty(cfg); AssetDatabase.SaveAssets();
            sb.AppendLine("written to " + ConfigPath);
        }
        return sb.ToString();
    }

    /// <summary>Predicted head displacement (Unity axes) for a body walking at <paramref name="speed"/> along <paramref name="dir"/>.</summary>
    public static Vector3 PredictWalk(MotionForecaster f, AxisMapping m, Vector3 dir, float speed, int horizon)
    {
        var w = new MotionWindow();
        Vector3 head0 = new Vector3(0f, 1.53f, 0f), rh = new Vector3(0.25f, -0.45f, 0.35f), lh = new Vector3(-0.25f, -0.45f, 0.35f);
        for (int t = 0; t < Forecast.Frames; t++)
        {
            Vector3 p = head0 + dir * (speed * t / Forecast.SampleRateHz);
            w.Push(m.ToDataset(p), m.ToDataset(p + rh), m.ToDataset(p + lh), 1f);
        }
        var raw = new float[Forecast.Frames * Forecast.RawColumns];
        w.CopyOrdered(raw);
        f.Run(raw);
        return m.ToUnity(Forecast.Offset(f.Offsets, horizon, Forecast.Point.Head));
    }
}
