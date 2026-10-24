// DiagnosticsMath.cs
// Purpose: The allocation-free pieces of the Expert diagnostics overlay and log (ExpertDiagnostics), as plain logic EditMode tests can
//          check: RollingStats (average and maximum of the samples of the last N seconds, fixed ring), HoldLatch (a gesture held for
//          N seconds fires once until it is released) and TextNumber (integers and fixed-point numbers appended to a StringBuilder
//          digit by digit, because StringBuilder.Append(float) / ToString("0.00") allocate a string on Unity's runtime).
//          Development builds and the Editor only: compiled out of release builds, like the overlay.
// Dependencies: none
// Attach to: nothing

#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System.Text;

namespace StrikeFit.ExpertAI
{
    /// <summary>Average and maximum over the samples of the last <c>windowSeconds</c>. Fixed capacity: the oldest samples are dropped
    /// when it is full.</summary>
    public sealed class RollingStats
    {
        readonly float[] times, values;
        readonly float windowSeconds;
        int head, count;

        public RollingStats(int capacity, float windowSeconds)
        {
            times = new float[capacity]; values = new float[capacity];
            this.windowSeconds = windowSeconds;
        }

        public void Add(float time, float value)
        {
            times[head] = time; values[head] = value;
            head = (head + 1) % times.Length;
            if (count < times.Length) count++;
        }

        public void Clear() { head = 0; count = 0; }

        /// <summary>Samples newer than now - windowSeconds; false (and zeros) when there are none.</summary>
        public bool Compute(float now, out float average, out float max, out int samples)
        {
            float sum = 0f; max = 0f; samples = 0;
            for (int i = 0; i < count; i++)
            {
                if (now - times[i] > windowSeconds) continue;
                sum += values[i]; samples++;
                if (values[i] > max) max = values[i];
            }
            average = samples > 0 ? sum / samples : 0f;
            return samples > 0;
        }
    }

    /// <summary>Fires once when the gesture has been held for <c>holdSeconds</c>; it must be released before it can fire again.</summary>
    public sealed class HoldLatch
    {
        readonly float holdSeconds;
        float held;
        bool latched;

        public HoldLatch(float holdSeconds) { this.holdSeconds = holdSeconds; }

        public bool Update(bool down, float deltaSeconds)
        {
            if (!down) { held = 0f; latched = false; return false; }
            held += deltaSeconds;
            if (latched || held < holdSeconds) return false;
            latched = true;
            return true;
        }
    }

    public static class TextNumber
    {
        public static StringBuilder AppendInt(StringBuilder sb, int v)
        {
            if (v < 0) { sb.Append('-'); v = v == int.MinValue ? int.MaxValue : -v; }
            int div = 1;
            while (v / div >= 10) div *= 10;
            for (; div > 0; div /= 10) sb.Append((char)('0' + v / div % 10));
            return sb;
        }

        /// <summary>v rounded to <paramref name="decimals"/> places (0..3); NaN and infinities print as "-".</summary>
        public static StringBuilder AppendFixed(StringBuilder sb, float v, int decimals)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) return sb.Append('-');
            int scale = decimals <= 0 ? 1 : decimals == 1 ? 10 : decimals == 2 ? 100 : 1000;
            long n = (long)System.Math.Round((double)v * scale, System.MidpointRounding.AwayFromZero);
            if (n < 0) { sb.Append('-'); n = -n; }
            AppendInt(sb, (int)System.Math.Min(n / scale, int.MaxValue));
            if (decimals <= 0) return sb;
            sb.Append('.');
            long frac = n % scale;
            for (int d = scale / 10; d > 0; d /= 10) sb.Append((char)('0' + frac / d % 10));
            return sb;
        }
    }
}
#endif
