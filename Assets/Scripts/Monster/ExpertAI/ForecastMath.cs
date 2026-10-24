// ForecastMath.cs
// Purpose: The input side of the trained motion forecaster (ml-services/models/motion_forecaster.onnx), reproduced exactly from
//          ml-services/src/strikefit_ml/feature_engineering.py and the model card:
//            - MotionWindow: a fixed ring of the last 20 frames at 20 Hz, each frame head xyz, controlling-hand xyz, secondary-hand xyz
//              and grip (metres, in the DATASET axes: z up). No allocation after construction.
//            - BuildFeatures: the 22 features per frame (positions relative to the head in the LAST frame, velocities by
//              neighbouring-frame difference x 20 Hz with the first frame repeating the second, both hand speeds, the absolute head
//              height z, grip), then (x - mean) / std with norm.json; Denormalise turns the 36 outputs back into metres
//              (horizon 0.25 / 0.5 / 0.75 / 1.0 s, then head / controlling hand / secondary hand, then x y z).
//            - AxisMapping: the one place where Unity axes (y up, left-handed) become dataset axes (z up) and back. Unity y is always
//              the dataset z (up is kept); the two horizontal axes are a permutation with signs, chosen by test (ExpertMappingProbe).
//            - NormStats: norm.json parsed without allocation at run time.
// Known limits (model card): trained on 12 minutes of VR sword strikes from very few people, positions only, mean error about 31 cm
//          over 1 s, never tested in Unity or on a Quest before this. The output is a hint, never a certainty.
// Dependencies: none (UnityEngine maths)
// Attach to: nothing

using UnityEngine;

namespace StrikeFit.ExpertAI
{
    public static class Forecast
    {
        public const int Frames = 20, Features = 22, Outputs = 36, RawColumns = 10, Horizons = 4;
        public const float SampleRateHz = 20f;
        public static readonly float[] HorizonSeconds = { 0.25f, 0.5f, 0.75f, 1f };
        public enum Point { Head = 0, Controlling = 1, Secondary = 2 }

        /// <summary>Offset (dataset axes, metres) of one tracked point at one horizon from the 36 de-normalised outputs.</summary>
        public static Vector3 Offset(float[] offsets36, int horizon, Point p)
        {
            int i = horizon * 9 + (int)p * 3;
            return new Vector3(offsets36[i], offsets36[i + 1], offsets36[i + 2]);
        }

        /// <summary>The 22 features for every frame of a raw window (20 x 10, oldest first), exactly as feature_engineering.window_features.</summary>
        public static void BuildFeatures(float[] raw, float rateHz, float[] features)
        {
            const int T = Frames, C = RawColumns, F = Features;
            int last = (T - 1) * C;
            float hx = raw[last], hy = raw[last + 1], hz = raw[last + 2];   // head now
            for (int t = 0; t < T; t++)
            {
                int r = t * C, o = t * F;
                // positions relative to the head in the last frame
                for (int e = 0; e < 3; e++)
                {
                    features[o + e * 3] = raw[r + e * 3] - hx;
                    features[o + e * 3 + 1] = raw[r + e * 3 + 1] - hy;
                    features[o + e * 3 + 2] = raw[r + e * 3 + 2] - hz;
                }
                // velocities: (p[t] - p[t-1]) * rate, frame 0 repeats frame 1
                int a = t == 0 ? C : r, b = t == 0 ? 0 : r - C;
                for (int k = 0; k < 9; k++) features[o + 9 + k] = (raw[a + k] - raw[b + k]) * rateHz;
                features[o + 18] = Mathf.Sqrt(Sq(features[o + 12]) + Sq(features[o + 13]) + Sq(features[o + 14]));   // controlling-hand speed
                features[o + 19] = Mathf.Sqrt(Sq(features[o + 15]) + Sq(features[o + 16]) + Sq(features[o + 17]));   // secondary-hand speed
                features[o + 20] = raw[r + 2];   // head height (z, absolute)
                features[o + 21] = raw[r + 9];   // grip
            }
        }

        static float Sq(float v) => v * v;

        public static void Normalise(float[] features, NormStats n, float[] output)
        {
            for (int t = 0; t < Frames; t++)
                for (int f = 0; f < Features; f++)
                {
                    int i = t * Features + f;
                    output[i] = (features[i] - n.xMean[f]) / n.xStd[f];
                }
        }

        public static void Denormalise(float[] y, NormStats n, float[] metres)
        {
            for (int i = 0; i < Outputs; i++) metres[i] = y[i] * n.yStd[i] + n.yMean[i];
        }

        public static bool AllFinite(float[] v, int count)
        {
            for (int i = 0; i < count; i++) if (float.IsNaN(v[i]) || float.IsInfinity(v[i])) return false;
            return true;
        }
    }

    /// <summary>norm.json: x_mean, x_std (22 each), y_mean, y_std (36 each).</summary>
    public sealed class NormStats
    {
        public readonly float[] xMean = new float[Forecast.Features], xStd = new float[Forecast.Features];
        public readonly float[] yMean = new float[Forecast.Outputs], yStd = new float[Forecast.Outputs];

        public static bool TryParse(string json, out NormStats stats, out string error)
        {
            stats = new NormStats(); error = null;
            if (string.IsNullOrEmpty(json)) { error = "norm.json is empty"; return false; }
            if (!Read(json, "x_mean", stats.xMean, out error) || !Read(json, "x_std", stats.xStd, out error) ||
                !Read(json, "y_mean", stats.yMean, out error) || !Read(json, "y_std", stats.yStd, out error)) return false;
            for (int i = 0; i < stats.xStd.Length; i++) if (stats.xStd[i] <= 0f) { error = "x_std[" + i + "] is not positive"; return false; }
            return true;
        }

        /// <summary>The flat numbers of the JSON array (any nesting) stored under <paramref name="key"/>. Load time only.</summary>
        public static bool Read(string json, string key, float[] into, out string error)
        {
            error = null;
            int k = json.IndexOf("\"" + key + "\"", System.StringComparison.Ordinal);
            if (k < 0) { error = key + " missing"; return false; }
            int a = json.IndexOf('[', k);
            if (a < 0) { error = key + " has no array"; return false; }
            int depth = 0, n = 0, i = a;
            var num = new System.Text.StringBuilder();
            for (; i < json.Length; i++)
            {
                char c = json[i];
                if (c == '[') { depth++; continue; }
                if (c == ']' || c == ',' || char.IsWhiteSpace(c))
                {
                    if (num.Length > 0)
                    {
                        if (n >= into.Length) { error = key + " has more than " + into.Length + " values"; return false; }
                        into[n++] = float.Parse(num.ToString(), System.Globalization.CultureInfo.InvariantCulture);
                        num.Length = 0;
                    }
                    if (c == ']' && --depth == 0) break;
                    continue;
                }
                num.Append(c);
            }
            if (n != into.Length) { error = key + " has " + n + " values, expected " + into.Length; return false; }
            return true;
        }
    }

    /// <summary>Unity (x right, y up, z forward; left-handed) to dataset (z up) and back. Dataset z = Unity y always; dataset x and y
    /// are Unity x / z in some order with signs. Eight candidates, four of them mirror images (a handedness mistake).</summary>
    [System.Serializable]
    public struct AxisMapping
    {
        [Tooltip("Dataset x takes Unity x (false) or Unity z (true); dataset y takes the other one.")]
        public bool swapHorizontal;
        public float xSign, ySign;

        public AxisMapping(bool swap, float xs, float ys) { swapHorizontal = swap; xSign = xs; ySign = ys; }

        public Vector3 ToDataset(Vector3 u)
        {
            float a = swapHorizontal ? u.z : u.x, b = swapHorizontal ? u.x : u.z;
            return new Vector3(xSign * a, ySign * b, u.y);
        }

        public Vector3 ToUnity(Vector3 d)
        {
            float a = d.x * xSign, b = d.y * ySign;   // signs are +-1, so they are their own inverse
            return swapHorizontal ? new Vector3(b, d.z, a) : new Vector3(a, d.z, b);
        }

        /// <summary>Determinant of the horizontal part is -1: this candidate is the mirror image of the det +1 ones. Which family is
        /// right depends on the dataset's handedness, which the mapping probe settles by test.</summary>
        public bool IsMirror => (swapHorizontal ? -1f : 1f) * xSign * ySign < 0f;

        public string Name => "dataset x = " + (xSign < 0 ? "-" : "+") + (swapHorizontal ? "Unity z" : "Unity x") + ", dataset y = " +
                              (ySign < 0 ? "-" : "+") + (swapHorizontal ? "Unity x" : "Unity z") + ", dataset z = +Unity y" + (IsMirror ? " (mirror)" : "");

        public static AxisMapping[] Candidates()
        {
            var list = new AxisMapping[8]; int n = 0;
            foreach (bool s in new[] { false, true })
                foreach (float xs in new[] { 1f, -1f })
                    foreach (float ys in new[] { 1f, -1f })
                        list[n++] = new AxisMapping(s, xs, ys);
            return list;
        }
    }

    /// <summary>The last 20 frames (dataset axes, metres), oldest first when copied out. Fixed arrays, no allocation.</summary>
    public sealed class MotionWindow
    {
        readonly float[] ring = new float[Forecast.Frames * Forecast.RawColumns];
        int head, count;

        public int Count => count;
        public bool Full => count >= Forecast.Frames;
        public void Clear() { head = 0; count = 0; }

        public void Push(Vector3 headPos, Vector3 controlling, Vector3 secondary, float grip)
        {
            int o = head * Forecast.RawColumns;
            ring[o] = headPos.x; ring[o + 1] = headPos.y; ring[o + 2] = headPos.z;
            ring[o + 3] = controlling.x; ring[o + 4] = controlling.y; ring[o + 5] = controlling.z;
            ring[o + 6] = secondary.x; ring[o + 7] = secondary.y; ring[o + 8] = secondary.z;
            ring[o + 9] = grip;
            head = (head + 1) % Forecast.Frames;
            if (count < Forecast.Frames) count++;
        }

        /// <summary>The window, oldest frame first, into <paramref name="raw"/> (20 x 10).</summary>
        public void CopyOrdered(float[] raw)
        {
            int start = Full ? head : 0;
            for (int t = 0; t < Forecast.Frames; t++)
            {
                int src = ((start + t) % Forecast.Frames) * Forecast.RawColumns, dst = t * Forecast.RawColumns;
                System.Array.Copy(ring, src, raw, dst, Forecast.RawColumns);
            }
        }

        /// <summary>Mean speed (m/s) of the head and both hands over the window: how much the player is moving.</summary>
        public float MotionEnergy()
        {
            if (count < 2) return 0f;
            float sum = 0f; int n = 0;
            int start = Full ? head : 0;
            for (int t = 1; t < count; t++)
            {
                int a = ((start + t) % Forecast.Frames) * Forecast.RawColumns, b = ((start + t - 1) % Forecast.Frames) * Forecast.RawColumns;
                for (int e = 0; e < 3; e++)
                {
                    float dx = ring[a + e * 3] - ring[b + e * 3], dy = ring[a + e * 3 + 1] - ring[b + e * 3 + 1], dz = ring[a + e * 3 + 2] - ring[b + e * 3 + 2];
                    sum += Mathf.Sqrt(dx * dx + dy * dy + dz * dz) * Forecast.SampleRateHz; n++;
                }
            }
            return n > 0 ? sum / n : 0f;
        }
    }
}
