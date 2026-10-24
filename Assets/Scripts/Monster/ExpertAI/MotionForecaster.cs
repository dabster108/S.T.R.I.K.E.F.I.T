// MotionForecaster.cs
// Purpose: Runs the motion forecaster with Unity's Inference Engine (com.unity.ai.inference). Everything is allocated once: the input
//          tensor (1 x 20 x 22), the feature / normalised / output / offset buffers. An inference is started with Begin (features ->
//          normalise -> upload -> ScheduleIterable) and advanced a few layers per Step call, so the work is spread over frames
//          (the GRU is unrolled into 378 small layers); Finish reads the 36 outputs straight from the CPU tensor (no copy) and turns
//          them into metres. Run does all of it at once (tests, the mapping probe).
//          The CPU backend is used: the model is 19 k parameters, and the GPU path costs a shader warm-up (17.8 s on the first run in
//          the Editor) and a GPU readback for 36 numbers.
// Dependencies: Unity.InferenceEngine (ModelAsset, ModelLoader, Worker, Tensor), Forecast / NormStats
// Attach to: nothing (owned by ExpertMotionPredictor)

using System;
using System.Collections;
using System.Diagnostics;
using Unity.InferenceEngine;

namespace StrikeFit.ExpertAI
{
    public sealed class MotionForecaster : IDisposable
    {
        readonly Worker worker;
        readonly Tensor<float> input;
        readonly NormStats norm;
        readonly float[] features = new float[Forecast.Frames * Forecast.Features];
        readonly float[] normalised = new float[Forecast.Frames * Forecast.Features];
        readonly float[] output = new float[Forecast.Outputs];
        readonly Stopwatch watch = new Stopwatch();
        IEnumerator schedule;

        /// <summary>The last inference's 36 outputs in metres (dataset axes).</summary>
        public readonly float[] Offsets = new float[Forecast.Outputs];
        /// <summary>Main-thread milliseconds the last inference took, all its steps together.</summary>
        public double LastMs { get; private set; }
        public bool Busy => schedule != null;
        public int LayerCount { get; }

        public MotionForecaster(ModelAsset asset, NormStats norm, BackendType backend = BackendType.CPU)
        {
            if (asset == null) throw new ArgumentNullException(nameof(asset), "the motion forecaster model is missing");
            this.norm = norm ?? throw new ArgumentNullException(nameof(norm));
            var model = ModelLoader.Load(asset);
            LayerCount = model.layers.Count;
            worker = new Worker(model, backend);
            try { input = new Tensor<float>(new TensorShape(1, Forecast.Frames, Forecast.Features), true); }
            catch { worker.Dispose(); throw; }   // no native worker left behind when the input tensor cannot be made
        }

        /// <summary>Starts an inference on a raw window (20 x 10, dataset axes, oldest first).</summary>
        public void Begin(float[] rawWindow)
        {
            watch.Restart();
            Forecast.BuildFeatures(rawWindow, Forecast.SampleRateHz, features);
            Forecast.Normalise(features, norm, normalised);
            input.Upload(normalised);
            schedule = worker.ScheduleIterable(input);
            watch.Stop();
            LastMs = watch.Elapsed.TotalMilliseconds;
        }

        /// <summary>Advances the running inference by up to <paramref name="layers"/> layers. True when it has finished.</summary>
        public bool Step(int layers)
        {
            if (schedule == null) return true;
            watch.Restart();
            bool more = true;
            for (int i = 0; i < layers && more; i++) more = schedule.MoveNext();
            watch.Stop();
            LastMs += watch.Elapsed.TotalMilliseconds;
            if (more) return false;
            schedule = null;
            return true;
        }

        /// <summary>Reads the finished inference into Offsets (metres). False if an output is NaN or infinite.</summary>
        public bool Finish()
        {
            watch.Restart();
            var t = worker.PeekOutput() as Tensor<float>;
            t.CompleteAllPendingOperations();
            var span = t.AsReadOnlySpan();
            for (int i = 0; i < Forecast.Outputs; i++) output[i] = span[i];
            bool ok = Forecast.AllFinite(output, Forecast.Outputs);
            if (ok) Forecast.Denormalise(output, norm, Offsets);
            watch.Stop();
            LastMs += watch.Elapsed.TotalMilliseconds;
            return ok;
        }

        /// <summary>A whole inference now (tests, probes). The normalised model output is returned in <paramref name="rawOutput"/>.</summary>
        public bool Run(float[] rawWindow, float[] rawOutput = null)
        {
            Begin(rawWindow);
            while (!Step(1000)) { }
            bool ok = Finish();
            if (rawOutput != null) Array.Copy(output, rawOutput, Forecast.Outputs);
            return ok;
        }

        /// <summary>Runs the model on already-normalised features (golden-vector test).</summary>
        public void RunNormalised(float[] normalisedFeatures, float[] rawOutput)
        {
            input.Upload(normalisedFeatures);
            worker.Schedule(input);
            var t = worker.PeekOutput() as Tensor<float>;
            t.CompleteAllPendingOperations();
            var span = t.AsReadOnlySpan();
            for (int i = 0; i < Forecast.Outputs; i++) rawOutput[i] = span[i];
        }

        public bool Disposed { get; private set; }

        /// <summary>Frees the worker and the input tensor (native memory). An inference still in flight is first run to its end and its
        /// jobs completed, so no CPU job reads a buffer after it was freed. Safe to call twice.</summary>
        public void Dispose()
        {
            if (Disposed) return;
            Disposed = true;
            try
            {
                if (schedule != null) { while (schedule.MoveNext()) { } }
                (worker?.PeekOutput() as Tensor<float>)?.CompleteAllPendingOperations();
            }
            catch (Exception) { }   // disposing anyway: the worker below owns every buffer the jobs used
            schedule = null;
            input?.Dispose();
            worker?.Dispose();
        }
    }

    /// <summary>Decides when Expert must stop for the rest of the fight: a missing model, an exception, a NaN / infinite output or a
    /// moving average of the inference time above the limit. Pure logic, so EditMode tests can drive it.</summary>
    public sealed class ExpertHealth
    {
        readonly float maxAverageMs;
        readonly int window;
        readonly double[] ms;
        int head, count;
        public bool Disabled { get; private set; }
        public string Reason { get; private set; } = "";
        public int Inferences { get; private set; }
        public double AverageMs { get; private set; }
        public double MaxMs { get; private set; }
        readonly double[] all = new double[2048];
        int allCount;

        public ExpertHealth(float maxAverageMs, int window = 10)
        {
            this.maxAverageMs = maxAverageMs; this.window = Math.Max(1, window);
            ms = new double[this.window];
        }

        /// <returns>False when this report disabled Expert.</returns>
        public bool Report(double milliseconds, bool outputsFinite)
        {
            if (Disabled) return false;
            Inferences++;
            if (allCount < all.Length) all[allCount++] = milliseconds;
            MaxMs = Math.Max(MaxMs, milliseconds);
            ms[head] = milliseconds; head = (head + 1) % window; if (count < window) count++;
            double sum = 0; for (int i = 0; i < count; i++) sum += ms[i];
            AverageMs = sum / count;
            if (!outputsFinite) return Disable("model output was NaN or infinite");
            if (count >= window && AverageMs > maxAverageMs) return Disable("inference averaged " + AverageMs.ToString("0.00") + " ms over the last " + window + " (limit " + maxAverageMs.ToString("0.0") + " ms)");
            return true;
        }

        public bool Disable(string reason)
        {
            if (Disabled) return false;
            Disabled = true; Reason = reason;
            return false;
        }

        /// <summary>Percentile (0..1) of every inference time so far (verification; allocates).</summary>
        public double Percentile(double p)
        {
            if (allCount == 0) return 0;
            var s = new double[allCount]; Array.Copy(all, s, allCount); Array.Sort(s);
            return s[Math.Min(allCount - 1, (int)Math.Round((allCount - 1) * p))];
        }
    }
}
