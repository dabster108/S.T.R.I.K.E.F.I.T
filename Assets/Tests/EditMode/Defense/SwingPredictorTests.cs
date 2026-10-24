// SwingPredictorTests.cs
// Purpose: EditMode tests for the swing predictor: direction / zone classification (left and right as the player sees them, high,
//          low, thrust) from 72 Hz controller samples, the 1.2 m/s detection gate, the look-ahead window, frame independence and the
//          fixed-size buffer.
// Dependencies: StrikeFit.DefenseCore, NUnit
// Attach to: nothing (Test Runner, EditMode)

using NUnit.Framework;
using UnityEngine;

namespace StrikeFit.Defense.Tests
{
    public class SwingPredictorTests
    {
        const float Hz = 72f;

        /// <summary>Monster at the origin facing a player at z = -2 (the player looks +z, so the player's left is world -x).</summary>
        static BodyFrame Body(float yaw = 0f)
        {
            Quaternion q = Quaternion.Euler(0f, yaw, 0f);
            Vector3 fwd = q * new Vector3(0f, 0f, -1f);
            return new BodyFrame { feet = Vector3.zero, right = Vector3.Cross(Vector3.up, fwd), forward = fwd, height = 2f, radius = 0.42f };
        }

        /// <summary>Feeds a straight swing (start, velocity) at 72 Hz until the predictor projects it into the body.</summary>
        static SwingPrediction Drive(in BodyFrame body, Vector3 start, Vector3 vel, out int samples, out bool everSwinging)
        {
            var p = new SwingPredictor(); var s = PredictorSettings.Default;
            everSwinging = false;
            for (samples = 1; samples <= 120; samples++)
            {
                float t = samples / Hz;
                p.Push(start + vel * t, t);
                var r = p.Predict(body, s);
                everSwinging |= r.swinging;
                if (r.willHit) return r;
            }
            return default;
        }

        static Vector3 W(in BodyFrame b, float x, float y, float z) => b.feet + b.right * x + Vector3.up * y + b.forward * z;   // monster space -> world

        [TestCase(1.2f)] [TestCase(2f)] [TestCase(4f)] [TestCase(6f)]
        public void SwingFromThePlayersLeft_IsLeft(float speed)
        {
            var b = Body();
            // the player's left is the monster's +x: the swing starts there and moves toward -x at chest height
            var r = Drive(b, W(b, 1.6f, 1.25f, 0.25f), -b.right * speed, out _, out _);
            Assert.IsTrue(r.willHit, "projected into the body");
            Assert.AreEqual(SwingZone.Left, r.zone);
        }

        [TestCase(1.2f)] [TestCase(2f)] [TestCase(4f)] [TestCase(6f)]
        public void SwingFromThePlayersRight_IsRight(float speed)
        {
            var b = Body();
            var r = Drive(b, W(b, -1.6f, 1.25f, 0.25f), b.right * speed, out _, out _);
            Assert.IsTrue(r.willHit);
            Assert.AreEqual(SwingZone.Right, r.zone);
        }

        [Test]
        public void Overhead_IsHigh()
        {
            var b = Body();
            var r = Drive(b, W(b, 0.05f, 2.9f, 0.3f), (Vector3.down * 0.95f - b.forward * 0.3f).normalized * 3f, out _, out _);
            Assert.IsTrue(r.willHit);
            Assert.AreEqual(SwingZone.High, r.zone);
        }

        [Test]
        public void Thrust_IsThrust()
        {
            var b = Body();
            var r = Drive(b, W(b, 0.02f, 1.3f, 1.6f), -b.forward * 3f, out _, out _);
            Assert.IsTrue(r.willHit);
            Assert.AreEqual(SwingZone.Thrust, r.zone);
        }

        [Test]
        public void LowSweep_IsLow()
        {
            var b = Body();
            var r = Drive(b, W(b, 1.6f, 0.45f, 0.2f), -b.right * 3f, out _, out _);
            Assert.IsTrue(r.willHit);
            Assert.AreEqual(SwingZone.Low, r.zone);
        }

        [TestCase(90f)] [TestCase(-135f)] [TestCase(30f)]
        public void ClassificationFollowsTheMonstersFacing(float yaw)
        {
            var b = Body(yaw);
            Assert.AreEqual(SwingZone.Left, Drive(b, W(b, 1.6f, 1.25f, 0.25f), -b.right * 2.5f, out _, out _).zone);
            Assert.AreEqual(SwingZone.Right, Drive(b, W(b, -1.6f, 1.25f, 0.25f), b.right * 2.5f, out _, out _).zone);
        }

        [Test]
        public void BelowTheDetectionSpeed_IsNotASwing()
        {
            var b = Body();
            Drive(b, W(b, 1.6f, 1.25f, 0.25f), -b.right * 1.0f, out _, out bool swinging);
            Assert.IsFalse(swinging, "1.0 m/s is under the 1.2 m/s gate");
        }

        [TestCase(1.5f)] [TestCase(3f)] [TestCase(6f)]
        public void ImpactIsPredictedInsideTheLookAhead_AndMatchesTheTruth(float speed)
        {
            var b = Body();
            Vector3 start = W(b, 2.5f, 1.25f, 0f);
            var r = Drive(b, start, -b.right * speed, out int n, out _);
            Assert.IsTrue(r.willHit);
            Assert.LessOrEqual(r.timeToImpact, PredictorSettings.Default.maxHorizon + 1e-4f);
            // truth: the swing enters the 0.42 m capsule at x = 0.42
            float now = n / Hz, truth = (2.5f - 0.42f) / speed - now;
            Assert.AreEqual(truth, r.timeToImpact, 1f / 120f + 1e-3f, "projection error under one projection step");
            Assert.Greater(r.confidence, 0.9f, "a clean straight stroke is a confident prediction");
        }

        [Test]
        public void SwingThatMissesTheBody_IsNotAnImpact()
        {
            var b = Body();
            // passes 0.9 m in front of the chest
            Drive(b, W(b, 1.8f, 1.25f, 0.9f), -b.right * 3f, out int n, out bool swinging);
            Assert.IsTrue(swinging);
            Assert.AreEqual(121, n, "no frame projected it into the body");
        }

        [TestCase(2.3f)] [TestCase(4f)]
        public void StrokeFromRest_IsJudgedOnItsOwnSamples(float speed)
        {
            // Found in Play mode: still samples before the stroke made a clean swing read as a wobbly one (confidence ~0.3).
            var b = Body(); var s = PredictorSettings.Default; var p = new SwingPredictor();
            Vector3 start = W(b, 2.0f, 1.25f, 0.4f), v = -b.right * speed;
            int i = 0;
            for (; i < 8; i++) p.Push(start, i / Hz);   // resting
            SwingPrediction r = default;
            for (int k = 1; k <= 120 && !r.willHit; k++, i++) { p.Push(start + v * (k / Hz), i / Hz); r = p.Predict(b, s); }
            Assert.IsTrue(r.willHit);
            Assert.AreEqual(SwingZone.Left, r.zone);
            Assert.Greater(r.confidence, 0.9f);
            Assert.AreEqual(speed, r.speed, 0.05f);
        }

        [Test]
        public void JitteryPath_HasLowerConfidence()
        {
            var b = Body(); var s = PredictorSettings.Default;
            var clean = new SwingPredictor(); var shaky = new SwingPredictor();
            var rnd = new System.Random(7);
            Vector3 start = W(b, 1.6f, 1.25f, 0.25f), v = -b.right * 2.5f;
            SwingPrediction rc = default, rs = default;
            for (int i = 1; i <= 8; i++)
            {
                float t = i / Hz;
                clean.Push(start + v * t, t);
                var noise = new Vector3((float)rnd.NextDouble() - 0.5f, (float)rnd.NextDouble() - 0.5f, (float)rnd.NextDouble() - 0.5f) * 0.08f;
                shaky.Push(start + v * t + noise, t);
                rc = clean.Predict(b, s); rs = shaky.Predict(b, s);
            }
            Assert.Less(rs.confidence, rc.confidence);
        }

        [Test]
        public void BufferIsFixedSize()
        {
            var p = new SwingPredictor();
            for (int i = 0; i < 100; i++) p.Push(new Vector3(i, 0f, 0f), i * 0.01f);
            Assert.AreEqual(SwingPredictor.Capacity, p.Count);
            Assert.IsTrue(p.Fit(6, out _, out Vector3 vel, out _, out _));
            Assert.AreEqual(100f, vel.x, 0.01f, "fits the newest samples");
        }
    }
}
