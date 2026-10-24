// SwingPredictor.cs
// Purpose: Hand-written swing predictor for one controller (no ML, no allocation). Keeps a fixed ring buffer of the last controller
//          samples (position of the weapon's striking part + time), fits position = p0 + v t + a t^2 / 2 by least squares over the
//          current stroke (the newest moving samples, at most fitWindow; a straight line until it has 5), and projects that path ahead (minHorizon..maxHorizon, 0.15-0.35 s by default) to find where
//          and when it meets the monster's body (a vertical capsule). Output: the zone the swing comes from (LEFT / RIGHT as the
//          PLAYER sees it, HIGH = overhead, LOW, THRUST), the predicted contact point, the time to impact and a confidence 0..1
//          (how straight the fitted path is and how steady its direction has been).
// Dependencies: none (UnityEngine maths only) - lives in the StrikeFit.DefenseCore assembly so EditMode tests can reach it
// Attach to: nothing (owned by MonsterTactics, one per player hand)

using UnityEngine;

namespace StrikeFit.Defense
{
    /// <summary>Where a swing comes from. Left / Right as the player sees it: Left = a swing from the player's left, which meets
    /// the monster's right side.</summary>
    public enum SwingZone { None = 0, Left = 1, Right = 2, High = 3, Low = 4, Thrust = 5 }

    /// <summary>The monster's body in world space, refreshed every frame.</summary>
    public struct BodyFrame
    {
        public Vector3 feet;      // root position on the ground
        public Vector3 right;     // monster's +x (the player's left)
        public Vector3 forward;   // toward the player
        public float height;      // m
        public float radius;      // horizontal radius of the body capsule (m)

        public Vector3 ToLocal(Vector3 world)
        {
            Vector3 d = world - feet;
            Vector3 up = Vector3.Cross(forward, right);
            return new Vector3(Vector3.Dot(d, right), Vector3.Dot(d, up), Vector3.Dot(d, forward));
        }

        public Vector3 DirToLocal(Vector3 dir)
        {
            Vector3 up = Vector3.Cross(forward, right);
            return new Vector3(Vector3.Dot(dir, right), Vector3.Dot(dir, up), Vector3.Dot(dir, forward));
        }
    }

    [System.Serializable]
    public struct PredictorSettings
    {
        [Tooltip("Slowest weapon speed (m/s) that counts as a swing.")]
        public float detectSpeed;
        [Tooltip("Newest samples used for the fit (3..16).")]
        public int fitWindow;
        [Tooltip("Shortest look-ahead (s): a contact sooner than this is too late to answer (the swing beats the prediction).")]
        public float minHorizon;
        [Tooltip("Longest look-ahead (s): a contact further away is not predicted yet.")]
        public float maxHorizon;
        [Tooltip("Largest acceleration (m/s^2) the projection trusts; arms do not keep accelerating.")]
        public float maxAcceleration;
        [Tooltip("Share of the speed that must be across the monster's face (not toward it) for a cut; below it the swing is a THRUST.")]
        public float thrustAcrossRatio;
        [Tooltip("Downward share of the cross-face speed from which a swing is HIGH (overhead).")]
        public float overheadRatio;
        [Tooltip("Contact below this share of the monster's height is LOW.")]
        public float lowBelow;
        [Tooltip("Fit residual (m, RMS) at which the path counts as noise (confidence 0).")]
        public float residualScale;
        [Tooltip("Direction change (deg) between the older and the newer half of the window at which confidence reaches 0.")]
        public float wobbleDegrees;

        public static PredictorSettings Default => new PredictorSettings
        {
            detectSpeed = 1.2f, fitWindow = 6, minHorizon = 0.15f, maxHorizon = 0.35f, maxAcceleration = 40f,
            thrustAcrossRatio = 0.5f, overheadRatio = 0.75f, lowBelow = 0.45f, residualScale = 0.04f, wobbleDegrees = 60f
        };
    }

    public struct SwingPrediction
    {
        /// <summary>A swing is under way (speed >= detectSpeed).</summary>
        public bool swinging;
        /// <summary>The projected path meets the body within maxHorizon.</summary>
        public bool willHit;
        public SwingZone zone;
        public Vector3 contact;        // world
        public float timeToImpact;     // s (valid when willHit)
        public float confidence;       // 0..1
        public float speed;            // m/s (fitted, now)
        public Vector3 velocity;       // world
        public float closest;          // closest horizontal approach to the body axis within maxHorizon (m)
    }

    public sealed class SwingPredictor
    {
        public const int Capacity = 16;
        readonly Vector3[] pos = new Vector3[Capacity];
        readonly float[] time = new float[Capacity];
        int head, count;

        public int Count => count;

        public void Clear() { head = 0; count = 0; }

        public void Push(Vector3 p, float t)
        {
            if (count > 0 && t <= time[(head + Capacity - 1) % Capacity]) return;   // same frame twice
            pos[head] = p; time[head] = t;
            head = (head + 1) % Capacity;
            if (count < Capacity) count++;
        }

        /// <summary>Sample i back from the newest (0 = newest).</summary>
        void Get(int back, out Vector3 p, out float t)
        {
            int i = (head - 1 - back + Capacity * 2) % Capacity;
            p = pos[i]; t = time[i];
        }

        /// <summary>Least-squares fit of the newest n samples: p(t) = c0 + c1 t + c2 t^2 (t relative to the newest sample).
        /// Returns velocity and acceleration at the newest sample and the RMS residual (m). n >= 3.</summary>
        public bool Fit(int n, out Vector3 p0, out Vector3 vel, out Vector3 acc, out float rms)
        {
            p0 = vel = acc = Vector3.zero; rms = 0f;
            n = Mathf.Clamp(n, 3, count);
            if (count < 3) return false;
            double s0 = 0, s1 = 0, s2 = 0, s3 = 0, s4 = 0;
            double bx0 = 0, bx1 = 0, bx2 = 0, by0 = 0, by1 = 0, by2 = 0, bz0 = 0, bz1 = 0, bz2 = 0;
            Get(0, out Vector3 pn, out float tn);
            for (int k = 0; k < n; k++)
            {
                Get(k, out Vector3 p, out float t);
                double x = t - tn, x2 = x * x;
                s0 += 1; s1 += x; s2 += x2; s3 += x2 * x; s4 += x2 * x2;
                bx0 += p.x; bx1 += p.x * x; bx2 += p.x * x2;
                by0 += p.y; by1 += p.y * x; by2 += p.y * x2;
                bz0 += p.z; bz1 += p.z * x; bz2 += p.z * x2;
            }
            // 3x3 symmetric system [s0 s1 s2; s1 s2 s3; s2 s3 s4] c = b, by Cramer's rule.
            double det = s0 * (s2 * s4 - s3 * s3) - s1 * (s1 * s4 - s3 * s2) + s2 * (s1 * s3 - s2 * s2);
            if (System.Math.Abs(det) < 1e-12)
            {
                // Degenerate timing (all samples in one instant): fall back to a two-point slope.
                Get(n - 1, out Vector3 po, out float to);
                float dt = tn - to;
                if (dt <= 1e-5f) return false;
                p0 = pn; vel = (pn - po) / dt; acc = Vector3.zero; rms = 0f;
                return true;
            }
            Vector3 c0, c1, c2;
            Solve(s0, s1, s2, s3, s4, det, bx0, bx1, bx2, out c0.x, out c1.x, out c2.x);
            Solve(s0, s1, s2, s3, s4, det, by0, by1, by2, out c0.y, out c1.y, out c2.y);
            Solve(s0, s1, s2, s3, s4, det, bz0, bz1, bz2, out c0.z, out c1.z, out c2.z);
            p0 = c0; vel = c1; acc = 2f * c2;
            double e = 0;
            for (int k = 0; k < n; k++)
            {
                Get(k, out Vector3 p, out float t);
                float x = t - tn;
                e += (p - (c0 + c1 * x + c2 * (x * x))).sqrMagnitude;
            }
            rms = (float)System.Math.Sqrt(e / n);
            return true;
        }

        static void Solve(double s0, double s1, double s2, double s3, double s4, double det, double b0, double b1, double b2,
                          out float c0, out float c1, out float c2)
        {
            double d0 = b0 * (s2 * s4 - s3 * s3) - s1 * (b1 * s4 - s3 * b2) + s2 * (b1 * s3 - s2 * b2);
            double d1 = s0 * (b1 * s4 - b2 * s3) - b0 * (s1 * s4 - s3 * s2) + s2 * (s1 * b2 - b1 * s2);
            double d2 = s0 * (s2 * b2 - s3 * b1) - s1 * (s1 * b2 - b1 * s2) + b0 * (s1 * s3 - s2 * s2);
            c0 = (float)(d0 / det); c1 = (float)(d1 / det); c2 = (float)(d2 / det);
        }

        /// <summary>Samples of the current stroke: the newest run of samples that keep moving (each step faster than moveFloor m/s),
        /// plus the point it started from, at most max. A stroke that starts from rest is judged on its own samples, not on the still
        /// ones before it (those made a clean swing look like a wobbly, badly fitted one).</summary>
        public int StrokeSamples(float moveFloor, int max)
        {
            int n = 1;
            for (int k = 0; k + 1 < count && n < max; k++)
            {
                Get(k, out Vector3 a, out float ta); Get(k + 1, out Vector3 b, out float tb);
                float dt = ta - tb;
                if (dt <= 1e-5f || (a - b).magnitude / dt < moveFloor) break;
                n++;
            }
            return n;
        }

        /// <summary>Least-squares straight line through the newest n samples (n >= 2): velocity at the newest sample and RMS residual.</summary>
        public bool FitLine(int n, out Vector3 p0, out Vector3 vel, out float rms)
        {
            p0 = vel = Vector3.zero; rms = 0f;
            n = Mathf.Min(n, count);
            if (n < 2) return false;
            Get(0, out Vector3 pn, out float tn);
            double st = 0, stt = 0; Vector3 sp = Vector3.zero, stp = Vector3.zero;
            for (int k = 0; k < n; k++)
            {
                Get(k, out Vector3 p, out float t);
                float x = t - tn;
                st += x; stt += x * x; sp += p; stp += p * x;
            }
            double den = n * stt - st * st;
            if (System.Math.Abs(den) < 1e-12) return false;
            vel = (stp * n - sp * (float)st) / (float)den;
            p0 = (sp - vel * (float)st) / n;
            double e = 0;
            for (int k = 0; k < n; k++) { Get(k, out Vector3 p, out float t); e += (p - (p0 + vel * (t - tn))).sqrMagnitude; }
            rms = (float)System.Math.Sqrt(e / n);
            return true;
        }

        /// <summary>Direction of travel of the older half of the window vs the newer half (deg). 0 = a steady stroke.</summary>
        public float Wobble(int n)
        {
            n = Mathf.Clamp(n, 3, count);
            if (count < 3) return 180f;
            Get(0, out Vector3 a, out _); Get(n / 2, out Vector3 b, out _); Get(n - 1, out Vector3 c, out _);
            Vector3 newer = a - b, older = b - c;
            if (newer.sqrMagnitude < 1e-8f || older.sqrMagnitude < 1e-8f) return 180f;
            return Vector3.Angle(newer, older);
        }

        /// <summary>Predict the current swing against the body. Never allocates.</summary>
        public SwingPrediction Predict(in BodyFrame body, in PredictorSettings s)
        {
            var r = new SwingPrediction { zone = SwingZone.None, closest = float.MaxValue };
            // Fit the current stroke only: a curve once it has 5 samples, a straight line before that (2-4 samples cannot carry an
            // acceleration estimate).
            int n = StrokeSamples(0.35f * s.detectSpeed, Mathf.Clamp(s.fitWindow, 3, Capacity));
            Vector3 p0, v, a = Vector3.zero; float rms;
            if (n >= 5) { if (!Fit(n, out p0, out v, out a, out rms)) return r; }
            else if (!FitLine(Mathf.Max(2, n), out p0, out v, out rms)) return r;
            r.velocity = v; r.speed = v.magnitude;
            if (r.speed < s.detectSpeed * 0.99f) return r;   // 1 % for float noise: a swing at exactly the gate counts
            r.swinging = true;
            if (n < 3) return r;   // two samples have no direction history yet: wait one more frame before predicting the impact
            if (a.sqrMagnitude > s.maxAcceleration * s.maxAcceleration) a = a.normalized * s.maxAcceleration;
            // Do not let the projection turn the swing around: acceleration against the velocity is capped so the path
            // decelerates at most to a stop inside the horizon.
            float along = Vector3.Dot(a, v) / Mathf.Max(1e-4f, r.speed);
            if (along < 0f) { float maxDecel = r.speed / Mathf.Max(0.05f, s.maxHorizon); if (-along > maxDecel) a -= (v / r.speed) * (along + maxDecel); }

            // Project in steps of 1/120 s up to maxHorizon: first point inside the body capsule.
            const float step = 1f / 120f;
            int steps = Mathf.CeilToInt(s.maxHorizon / step);
            float r2 = body.radius * body.radius;
            float hit = -1f; Vector3 contact = Vector3.zero;
            for (int i = 0; i <= steps; i++)
            {
                float h = i * step;
                Vector3 p = p0 + v * h + a * (0.5f * h * h);
                Vector3 l = body.ToLocal(p);
                float horiz = l.x * l.x + l.z * l.z;
                if (horiz < r.closest * r.closest) r.closest = Mathf.Sqrt(horiz);
                if (l.y < 0.08f * body.height || l.y > 1.08f * body.height) continue;
                if (horiz <= r2) { hit = h; contact = p; break; }
            }
            if (hit < 0f) { r.confidence = Confidence(rms, n, s); return r; }
            r.willHit = true;
            r.timeToImpact = hit;
            r.contact = contact;
            r.zone = Classify(body, v + a * hit, contact, s);
            r.confidence = Confidence(rms, n, s);
            return r;
        }

        /// <summary>How straight the stroke's fitted path is (residual) and how steady its direction has been (older vs newer half of the
        /// stroke). A stroke of 2 samples has no direction history yet: half credit for steadiness.</summary>
        float Confidence(float rms, int strokeSamples, in PredictorSettings s)
        {
            float fit = 1f - Mathf.Clamp01(rms / Mathf.Max(1e-4f, s.residualScale));
            float steady = strokeSamples >= 3 ? 1f - Mathf.Clamp01(Wobble(strokeSamples) / Mathf.Max(1f, s.wobbleDegrees)) : 0.5f;
            return Mathf.Clamp01(fit * (0.35f + 0.65f * steady));
        }

        /// <summary>Zone of a swing from its velocity at contact (monster space) and the contact height. Public for tests.</summary>
        public static SwingZone Classify(in BodyFrame body, Vector3 velocityWorld, Vector3 contactWorld, in PredictorSettings s)
        {
            Vector3 v = body.DirToLocal(velocityWorld);
            float speed = v.magnitude;
            if (speed < 1e-4f) return SwingZone.Thrust;
            float across = Mathf.Sqrt(v.x * v.x + v.y * v.y);   // across the monster's face (x lateral, y vertical)
            float f = body.ToLocal(contactWorld).y / Mathf.Max(0.5f, body.height);
            if (across < s.thrustAcrossRatio * speed) return f < s.lowBelow ? SwingZone.Low : SwingZone.Thrust;
            if (v.y < -s.overheadRatio * across) return SwingZone.High;
            if (f < s.lowBelow) return SwingZone.Low;
            // The monster's +x is the player's left: a swing moving toward -x came in from the player's left.
            return v.x < 0f ? SwingZone.Left : SwingZone.Right;
        }
    }
}
