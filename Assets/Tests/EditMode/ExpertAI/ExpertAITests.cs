// ExpertAITests.cs
// Purpose: EditMode tests for Level 3 Expert mode: golden-vector parity of the Unity model run (input -> output within 1e-4), the
//          feature builder and normalisation against the golden raw window, the fallback rules (NaN, slow inference, missing model),
//          the fairness governor caps, the never-on-Levels-1-and-2 rule (rule + prefabs), the chosen axis mapping, the head-only
//          output rule (no hand output and nothing past 0.75 s is read unless the config allows it), the steady-state allocations and
//          the diagnostics helpers.
// Dependencies: StrikeFit.ExpertAI, Unity.InferenceEngine, NUnit
// Attach to: nothing (Test Runner, EditMode)

using System;
using NUnit.Framework;
using Unity.InferenceEngine;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.TestTools;

namespace StrikeFit.ExpertAI.Tests
{
    public class ExpertAITests
    {
        const string Dir = "Assets/Models/ExpertAI/";
        const float Tol = 1e-4f;

        static string Golden => System.IO.File.ReadAllText(Dir + "golden_vector.json");
        static NormStats Norm()
        {
            Assert.IsTrue(NormStats.TryParse(System.IO.File.ReadAllText(Dir + "norm.json"), out NormStats n, out string err), err);
            return n;
        }
        static float[] Read(string key, int n)
        {
            var a = new float[n];
            Assert.IsTrue(NormStats.Read(Golden, key, a, out string err), err);
            return a;
        }
        static ModelAsset Model() => AssetDatabase.LoadAssetAtPath<ModelAsset>(Dir + "motion_forecaster.onnx");

        static float MaxDiff(float[] a, float[] b)
        {
            float m = 0f;
            for (int i = 0; i < a.Length; i++) m = Mathf.Max(m, Mathf.Abs(a[i] - b[i]));
            return m;
        }

        // ───────────────────────── parity ─────────────────────────
        [Test]
        public void GoldenVector_ModelOutputMatchesPython()
        {
            var x = Read("features_normalised", 440); var y = Read("expected_output_normalised", 36);
            using (var f = new MotionForecaster(Model(), Norm()))
            {
                var o = new float[36];
                f.RunNormalised(x, o);
                TestContext.WriteLine("golden: model output max |Unity - PyTorch| = " + MaxDiff(o, y).ToString("E2"));
                Assert.LessOrEqual(MaxDiff(o, y), Tol, "Unity Inference Engine output vs the PyTorch / ONNX golden output");
            }
        }

        [Test]
        public void FeatureBuilder_MatchesGoldenRawWindow()
        {
            var raw = Read("raw_window_m", 200); var expected = Read("features_raw", 440);
            var feat = new float[440];
            Forecast.BuildFeatures(raw, 20f, feat);
            TestContext.WriteLine("golden: features max |Unity - Python| = " + MaxDiff(feat, expected).ToString("E2"));
            Assert.LessOrEqual(MaxDiff(feat, expected), Tol);
        }

        [Test]
        public void Normalisation_MatchesGolden()
        {
            var feat = Read("features_raw", 440); var expected = Read("features_normalised", 440);
            var n = new float[440];
            Forecast.Normalise(feat, Norm(), n);
            Assert.LessOrEqual(MaxDiff(n, expected), Tol);
        }

        [Test]
        public void EndToEnd_RawWindowToMetres_MatchesGolden()
        {
            var raw = Read("raw_window_m", 200); var expected = Read("expected_offsets_m", 36);
            using (var f = new MotionForecaster(Model(), Norm()))
            {
                Assert.IsTrue(f.Run(raw));
                TestContext.WriteLine("golden: raw window -> metres max |Unity - Python| = " + MaxDiff(f.Offsets, expected).ToString("E2") + " m");
                Assert.LessOrEqual(MaxDiff(f.Offsets, expected), Tol);
            }
        }

        [Test]
        public void SpreadOverFrames_GivesTheSameAnswer()
        {
            var raw = Read("raw_window_m", 200); var expected = Read("expected_offsets_m", 36);
            using (var f = new MotionForecaster(Model(), Norm()))
            {
                f.Begin(raw);
                int steps = 1;
                while (!f.Step(64)) steps++;
                Assert.Greater(steps, 2, "378 layers at 64 per frame take several frames");
                Assert.IsTrue(f.Finish());
                Assert.LessOrEqual(MaxDiff(f.Offsets, expected), Tol);
            }
        }

        [Test]
        public void Window_IsOldestFirst_AndFixedSize()
        {
            var w = new MotionWindow();
            for (int i = 0; i < 25; i++) w.Push(new Vector3(i, 0f, 0f), Vector3.zero, Vector3.zero, 1f);
            Assert.IsTrue(w.Full);
            var raw = new float[200];
            w.CopyOrdered(raw);
            Assert.AreEqual(5f, raw[0], "frame 0 is the oldest kept (25 pushed, 20 kept)");
            Assert.AreEqual(24f, raw[190], "frame 19 is the newest");
        }

        // ───────────────────────── fallback ─────────────────────────
        [Test]
        public void NaNOutput_DisablesExpert()
        {
            var h = new ExpertHealth(3f);
            Assert.IsTrue(h.Report(0.8, true));
            Assert.IsFalse(h.Report(0.8, false));
            Assert.IsTrue(h.Disabled);
            StringAssert.Contains("NaN", h.Reason);
        }

        [Test]
        public void SlowInference_DisablesExpert_OnlyOnTheMovingAverage()
        {
            var h = new ExpertHealth(3f, 10);
            Assert.IsTrue(h.Report(9.0, true), "one slow inference alone is not enough");
            for (int i = 0; i < 8; i++) h.Report(0.5, true);
            Assert.IsFalse(h.Disabled, "average (9 + 8 x 0.5) / 9 = 1.4 ms");
            for (int i = 0; i < 10; i++) h.Report(4.0, true);
            Assert.IsTrue(h.Disabled, "a moving average above 3 ms");
            StringAssert.Contains("averaged", h.Reason);
            Assert.IsFalse(h.Report(0.1, true), "stays off for the rest of the fight");
        }

        [Test]
        public void MissingModel_Throws_SoThePredictorFallsBack()
        {
            Assert.Throws<ArgumentNullException>(() => new MotionForecaster(null, Norm()));
            Assert.IsFalse(NormStats.TryParse("", out _, out string err));
            Assert.IsNotEmpty(err);
        }

        [Test]
        public void Predictor_WithoutModel_SwitchesItselfOff_WithOneLogLine()
        {
            var type = Type.GetType("ExpertMotionPredictor, Assembly-CSharp");
            Assert.IsNotNull(type);
            var go = new GameObject("PredictorTest");
            try
            {
                var p = go.AddComponent(type);
                var cfg = ScriptableObject.CreateInstance<ExpertAIConfig>();   // no model
                type.GetField("config").SetValue(p, cfg);
                LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(@"\[ExpertAI\] Expert mode switched off.*model is missing"));
                type.GetMethod("Start", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(p, null);
                Assert.IsFalse((bool)type.GetProperty("Healthy").GetValue(p));
                Assert.AreEqual(1, (int)type.GetProperty("Fallbacks").GetValue(p));
                UnityEngine.Object.DestroyImmediate(cfg);
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        // ───────────────────────── governor ─────────────────────────
        [Test]
        public void Governor_CapsAimAndLead()
        {
            var g = new ExpertGovernor(25f, 0.6f, 12f, 0.15f);
            Assert.AreEqual(25f, g.ClampAim(40f));
            Assert.AreEqual(-25f, g.ClampAim(-90f));
            Assert.AreEqual(10f, g.ClampAim(10f));
            Assert.AreEqual(0.6f, g.ClampLead(new Vector3(1.5f, 0.4f, 0f)).magnitude, 1e-4f);
            Assert.AreEqual(0f, g.ClampLead(new Vector3(0f, 1f, 0f)).magnitude, 1e-6f, "lead is horizontal only");
        }

        [Test]
        public void Governor_StopsPredictionsAtTheHitCap_ThenReleases()
        {
            var g = new ExpertGovernor(25f, 0.6f, 12f, 0.15f);   // e.g. Normal 10 hits/min + 20 %
            for (int i = 0; i < 11; i++) g.RegisterHitOnPlayer(i * 2f);
            Assert.IsTrue(g.MayUsePrediction(30f, true, 1f, true), "11 hits in the last minute < 12");
            g.RegisterHitOnPlayer(31f);
            Assert.IsFalse(g.MayUsePrediction(32f, true, 1f, true), "12 hits: no prediction-driven choices");
            Assert.IsTrue(g.MayUsePrediction(95f, true, 1f, true), "a minute later the old hits no longer count");
        }

        [Test]
        public void Governor_RefusesUnsurePredictions()
        {
            var g = new ExpertGovernor(25f, 0.6f, 12f, 0.15f);
            Assert.IsFalse(g.MayUsePrediction(0f, false, 1f, true), "window not full");
            Assert.IsFalse(g.MayUsePrediction(0f, true, 0.05f, true), "player nearly still");
            Assert.IsFalse(g.MayUsePrediction(0f, true, 1f, false), "model unhealthy / no fresh prediction");
            Assert.IsTrue(g.MayUsePrediction(0f, true, 1f, true));
        }

        [Test]
        public void AimedAttack_NeverChains_SoItsRecoveryStaysPunishable()
        {
            Assert.IsFalse(ExpertGovernor.AllowChainAfter(true));
            Assert.IsTrue(ExpertGovernor.AllowChainAfter(false));
        }

        // ───────────────────────── where the model may run ─────────────────────────
        [Test]
        public void Levels1And2_NeverUseTheModel()
        {
            foreach (var mode in new[] { MonsterAIMode.Normal, MonsterAIMode.Expert })
            {
                Assert.IsFalse(MonsterAIModeRules.UseModel(1, mode));
                Assert.IsFalse(MonsterAIModeRules.UseModel(2, mode));
            }
            Assert.IsFalse(MonsterAIModeRules.UseModel(3, MonsterAIMode.Normal), "Normal stays the default behaviour");
            Assert.IsTrue(MonsterAIModeRules.UseModel(3, MonsterAIMode.Expert));
        }

        [Test]
        public void Prefabs_OnlyLevel3CarriesTheExpertBrain()
        {
            string[] paths = { "Assets/Prefabs/Monsters/Monster_Easy_HipHop.prefab", "Assets/Prefabs/Monsters/Monster_Medium_VampireLust.prefab", "Assets/Prefabs/Monsters/Monster_Hard_TwinAxeBrute.prefab" };
            for (int i = 0; i < 3; i++)
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(paths[i]);
                bool brain = go.GetComponent("MonsterExpertBrain") != null, predictor = go.GetComponent("ExpertMotionPredictor") != null;
                if (i < 2) { Assert.IsFalse(brain, paths[i]); Assert.IsFalse(predictor, paths[i]); }
                else Assert.IsTrue(brain, paths[i]);
            }
        }

        // ───────────────────────── axis mapping ─────────────────────────
        [Test]
        public void Mapping_RoundTrips_AndKeepsUp()
        {
            foreach (var m in AxisMapping.Candidates())
            {
                var u = new Vector3(0.3f, 1.6f, -0.7f);
                Assert.Less((m.ToUnity(m.ToDataset(u)) - u).magnitude, 1e-6f, m.Name);
                Assert.AreEqual(1.6f, m.ToDataset(u).z, 1e-6f, "Unity up is dataset z");
            }
        }

        [Test]
        public void ChosenMapping_ContinuesSyntheticWalks_AndBeatsItsMirror()
        {
            var cfg = AssetDatabase.LoadAssetAtPath<ExpertAIConfig>("Assets/Settings/Defense/L3_HarborTyrant_Expert.asset");
            Assert.IsNotNull(cfg);
            var m = cfg.mapping;
            var mirror = new AxisMapping(m.swapHorizontal, -m.xSign, m.ySign);
            using (var f = new MotionForecaster(Model(), Norm()))
            {
                foreach (var d in new[] { Vector3.forward, Vector3.back, Vector3.left, Vector3.right })
                {
                    Vector3 p = Walk(f, m, d);
                    Assert.Greater(Vector3.Dot(p.normalized, d), 0.7f, "walk " + d + " predicted " + p);
                }
                float side = Vector3.Dot(Walk(f, m, Vector3.left).normalized, Vector3.left) + Vector3.Dot(Walk(f, m, Vector3.right).normalized, Vector3.right);
                float sideMirror = Vector3.Dot(Walk(f, mirror, Vector3.left).normalized, Vector3.left) + Vector3.Dot(Walk(f, mirror, Vector3.right).normalized, Vector3.right);
                Assert.Greater(side, sideMirror, "a handedness mistake predicts sideways walks worse");
            }
        }

        static Vector3 Walk(MotionForecaster f, AxisMapping m, Vector3 dir)
        {
            var w = new MotionWindow();
            Vector3 head0 = new Vector3(0f, 1.53f, 0f), rh = new Vector3(0.25f, -0.45f, 0.35f), lh = new Vector3(-0.25f, -0.45f, 0.35f);
            for (int t = 0; t < Forecast.Frames; t++)
            {
                Vector3 p = head0 + dir * (0.6f * t / Forecast.SampleRateHz);
                w.Push(m.ToDataset(p), m.ToDataset(p + rh), m.ToDataset(p + lh), 1f);
            }
            var raw = new float[200];
            w.CopyOrdered(raw);
            f.Run(raw);
            return m.ToUnity(Forecast.Offset(f.Offsets, 1, Forecast.Point.Head));
        }
        // ───────────────────────── which outputs the game may read (head only, up to 0.75 s) ─────────────────────────
        [Test]
        public void Outputs_HeadOnly_UpTo075s_ByDefault()
        {
            Assert.AreEqual(2, ExpertOutputs.MaxHorizonIndex(0.75f), "0.75 s is horizon index 2");
            Assert.AreEqual(3, ExpertOutputs.MaxHorizonIndex(1f));
            Assert.AreEqual(0, ExpertOutputs.MaxHorizonIndex(0.1f), "never below the first horizon");
            for (int h = 0; h < Forecast.Horizons; h++)
            {
                Assert.AreEqual(h <= 2, ExpertOutputs.MayRead(Forecast.Point.Head, h, false, 0.75f), "head at horizon " + h);
                Assert.IsFalse(ExpertOutputs.MayRead(Forecast.Point.Controlling, h, false, 0.75f), "controlling hand, flag off, horizon " + h);
                Assert.IsFalse(ExpertOutputs.MayRead(Forecast.Point.Secondary, h, false, 0.75f), "secondary hand, flag off, horizon " + h);
                Assert.AreEqual(h <= 2, ExpertOutputs.MayRead(Forecast.Point.Controlling, h, true, 0.75f), "hands with the flag, horizon " + h);
            }
        }

        [Test]
        public void Governor_ClampsTheHorizon_AndCarriesTheHandFlag()
        {
            var g = new ExpertGovernor(25f, 0.6f, 12f, 0.15f, 0.75f, false);
            Assert.AreEqual(2, g.ClampHorizon(3), "a long wind-up still reads 0.75 s at most");
            Assert.AreEqual(1, g.ClampHorizon(1));
            Assert.AreEqual(0, g.ClampHorizon(-1));
            Assert.IsFalse(g.UseHands);
            Assert.IsTrue(new ExpertGovernor(25f, 0.6f, 12f, 0.15f, 1f, true).UseHands);
            Assert.AreEqual(3, new ExpertGovernor(25f, 0.6f, 12f, 0.15f, 1f, true).ClampHorizon(3));
        }

        [Test]
        public void Config_DefaultsAreHeadOnly_And075s()
        {
            var fresh = ScriptableObject.CreateInstance<ExpertAIConfig>();
            try
            {
                Assert.IsFalse(fresh.useHandPrediction);
                Assert.AreEqual(0.75f, fresh.maxHorizonSeconds, 1e-6f);
            }
            finally { UnityEngine.Object.DestroyImmediate(fresh); }
            var cfg = AssetDatabase.LoadAssetAtPath<ExpertAIConfig>("Assets/Settings/Defense/L3_HarborTyrant_Expert.asset");
            Assert.IsNotNull(cfg);
            Assert.IsFalse(cfg.useHandPrediction, "the Level 3 config reads the head only");
            Assert.AreEqual(0.75f, cfg.maxHorizonSeconds, 1e-6f);
            Assert.LessOrEqual(ExpertOutputs.ClampHorizon(cfg.aimHorizon, cfg.maxHorizonSeconds), 2);
        }

        [Test]
        public void Predictor_ReadsNoHandOutput_WhenTheFlagIsOff()
        {
            var type = Type.GetType("ExpertMotionPredictor, Assembly-CSharp");
            Assert.IsNotNull(type);
            var go = new GameObject("PredictorGateTest");
            var cfg = ScriptableObject.CreateInstance<ExpertAIConfig>();
            try
            {
                var p = go.AddComponent(type);
                type.GetField("config").SetValue(p, cfg);
                var offsets = new Vector3[Forecast.Horizons * 3];
                for (int i = 0; i < offsets.Length; i++) offsets[i] = new Vector3(0.1f + i, 0.2f, 0.3f);   // every output non-zero
                type.GetMethod("PublishForTest").Invoke(p, new object[] { offsets, 0f });
                var local = type.GetMethod("LocalOffset");
                Func<Forecast.Point, int, Vector3> read = (pt, h) => (Vector3)local.Invoke(p, new object[] { pt, h });

                for (int h = 0; h < Forecast.Horizons; h++)
                {
                    Assert.AreEqual(Vector3.zero, read(Forecast.Point.Controlling, h), "controlling hand, horizon " + h);
                    Assert.AreEqual(Vector3.zero, read(Forecast.Point.Secondary, h), "secondary hand, horizon " + h);
                    if (h <= 2) Assert.AreEqual(offsets[h * 3], read(Forecast.Point.Head, h), "head, horizon " + h);
                    else Assert.AreEqual(Vector3.zero, read(Forecast.Point.Head, h), "head past 0.75 s");
                }
                Assert.AreEqual(4 * 2 + 1, (int)type.GetProperty("RefusedReads").GetValue(p), "every refused read is counted");

                cfg.useHandPrediction = true;
                Assert.AreEqual(offsets[0 * 3 + 1], read(Forecast.Point.Controlling, 0), "with the flag the hands are readable");
                Assert.AreEqual(Vector3.zero, read(Forecast.Point.Controlling, 3), "but still not past maxHorizonSeconds");
            }
            finally { UnityEngine.Object.DestroyImmediate(go); UnityEngine.Object.DestroyImmediate(cfg); }
        }

        // ───────────────────────── allocations and disposal ─────────────────────────
        // Managed allocations made by `action` on this thread, counted the way the Test Framework's Is.Not.AllocatingGCMemory() does
        // (a "GC.Alloc" profiler recorder filtered to the current thread). GC.GetAllocatedBytesForCurrentThread is not implemented on
        // Unity's Mono runtime (it always reads 0), so it cannot be used here.
        static int Allocations(Action action)
        {
            var recorder = Recorder.Get("GC.Alloc");
            recorder.enabled = false;
            recorder.FilterToCurrentThread();
            recorder.enabled = true;
            try { action(); }
            finally { recorder.enabled = false; recorder.CollectFromAllThreads(); }
            return recorder.sampleBlockCount;
        }

        static object sink;
        static bool AllocationCounterWorks() => Allocations(() => sink = new object[64]) > 0;

        [Test]
        public void SteadyState_FeaturePath_AllocatesNothing_AndAnInferenceAlmostNothing()
        {
            Assert.IsTrue(AllocationCounterWorks(), "the GC.Alloc recorder must see a known allocation, or this test proves nothing");
            var norm = Norm();
            var w = new MotionWindow();
            var raw = new float[200]; var feat = new float[440]; var nrm = new float[440]; var y = new float[36]; var metres = new float[36];
            var g = new ExpertGovernor(25f, 0.6f, 12f, 0.15f);
            void FeaturePath(int i)
            {
                w.Push(new Vector3(i * 0.01f, 0f, 1.5f), new Vector3(0.3f, 0.2f, 1.1f), new Vector3(-0.3f, 0.2f, 1.1f), 1f);
                w.CopyOrdered(raw);
                w.MotionEnergy();
                Forecast.BuildFeatures(raw, 20f, feat);
                Forecast.Normalise(feat, norm, nrm);
                Forecast.Denormalise(y, norm, metres);
                Forecast.AllFinite(metres, 36);
                g.MayUsePrediction(i, w.Full, 1f, true);
                g.RegisterHitOnPlayer(i);
                ExpertOutputs.MayRead(Forecast.Point.Head, 1, false, 0.75f);
            }
            for (int i = 0; i < 30; i++) FeaturePath(i);   // warm-up (JIT)
            Assert.AreEqual(0, Allocations(() => { for (int i = 30; i < 230; i++) FeaturePath(i); }), "sampling, features, normalisation, governor: no allocation");

            using (var f = new MotionForecaster(Model(), norm))
            {
                for (int i = 0; i < 3; i++) f.Run(raw);   // warm-up
                const int runs = 20;
                int begin = 0, stepFinish = 0;
                for (int i = 0; i < runs; i++)
                {
                    begin += Allocations(() => f.Begin(raw));
                    stepFinish += Allocations(() => { while (!f.Step(128)) { } f.Finish(); });
                }
                TestContext.WriteLine("managed allocations per spread inference: Begin " + (begin / (float)runs) + ", Step + Finish " + (stepFinish / (float)runs));
                Assert.AreEqual(0, stepFinish, "advancing and reading an inference allocates nothing");
                // ScheduleIterable returns a C# iterator: one small object per inference (10 per second) is the floor with this API.
                Assert.LessOrEqual(begin, runs * 2, "starting an inference allocates at most the scheduling iterator");
            }
        }

        [Test]
        public void Forecaster_DisposeMidInference_IsSafe_AndIdempotent()
        {
            var raw = Read("raw_window_m", 200);
            var f = new MotionForecaster(Model(), Norm());
            f.Begin(raw);
            f.Step(16);
            Assert.IsTrue(f.Busy, "still in flight");
            f.Dispose();
            Assert.IsTrue(f.Disposed);
            Assert.DoesNotThrow(() => f.Dispose(), "a second Dispose is a no-op");
        }

        // ───────────────────────── diagnostics helpers ─────────────────────────
        [Test]
        public void Diagnostics_RollingStats_UseOnlyTheLastFiveSeconds()
        {
            var r = new RollingStats(8, 5f);
            r.Add(0f, 100f);
            r.Add(4f, 2f);
            r.Add(6f, 4f);
            Assert.IsTrue(r.Compute(6f, out float avg, out float max, out int n));
            Assert.AreEqual(2, n, "the sample at t=0 is older than 5 s");
            Assert.AreEqual(3f, avg, 1e-6f);
            Assert.AreEqual(4f, max, 1e-6f);
            for (int i = 0; i < 20; i++) r.Add(7f, 1f);   // capacity 8: the oldest are dropped
            r.Compute(7f, out avg, out max, out n);
            Assert.AreEqual(8, n); Assert.AreEqual(1f, max, 1e-6f);
            Assert.IsFalse(r.Compute(100f, out _, out _, out _), "nothing in the window");
        }

        [Test]
        public void Diagnostics_HoldLatch_FiresOnceAfterTwoSeconds_UntilReleased()
        {
            var l = new HoldLatch(2f);
            int fired = 0;
            for (int i = 0; i < 19; i++) if (l.Update(true, 0.1f)) fired++;
            Assert.AreEqual(0, fired, "1.9 s is not enough");
            for (int i = 0; i < 30; i++) if (l.Update(true, 0.1f)) fired++;
            Assert.AreEqual(1, fired, "fires once while held");
            Assert.IsFalse(l.Update(false, 0.1f));
            for (int i = 0; i < 20; i++) if (l.Update(true, 0.1f)) fired++;
            Assert.AreEqual(2, fired, "fires again after a release and a new 2 s hold");
        }

        [Test]
        public void Diagnostics_Numbers_MatchToString_WithoutAllocating()
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var sb = new System.Text.StringBuilder(64);
            foreach (float v in new[] { 0f, 1.234f, 12.5f, 0.005f, 1.999f, 1234.5678f, -3.25f })
                for (int d = 0; d <= 3; d++)
                {
                    sb.Clear(); TextNumber.AppendFixed(sb, v, d);
                    string want = ((decimal)Math.Round((double)v, d, MidpointRounding.AwayFromZero)).ToString(d == 0 ? "0" : "0." + new string('0', d), inv);
                    Assert.AreEqual(want, sb.ToString(), v + " with " + d + " decimals");
                }
            sb.Clear(); TextNumber.AppendInt(sb, -40213); Assert.AreEqual("-40213", sb.ToString());
            sb.Clear(); TextNumber.AppendFixed(sb, float.NaN, 2); Assert.AreEqual("-", sb.ToString());
            Assert.IsTrue(AllocationCounterWorks());
            for (int i = 0; i < 10; i++) { sb.Clear(); TextNumber.AppendFixed(sb, i * 1.37f, 2); TextNumber.AppendInt(sb, i); }   // warm-up
            Assert.AreEqual(0, Allocations(() => { for (int i = 0; i < 500; i++) { sb.Clear(); TextNumber.AppendFixed(sb, i * 1.37f, 2).Append(' '); TextNumber.AppendInt(sb, i * 7); } }));
        }
    }
}
