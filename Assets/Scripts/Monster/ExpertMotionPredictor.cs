// ExpertMotionPredictor.cs
// Purpose: Level 3 Expert mode only. Feeds the trained motion forecaster (Assets/Models/ExpertAI) and publishes where the player's
//          head and hands are predicted to be 0.25 / 0.5 / 0.75 / 1.0 s ahead.
//          - Samples the head and both controllers at exactly 20 Hz (a time accumulator, not per frame), in metres: horizontal axes in
//            TRACKING SPACE (relative to OVRCameraRig.trackingSpace), height above the arena floor (the rigs use an eye-level tracking
//            origin, and the model's head-height feature is from the floor), mapped to the dataset axes by ExpertAIConfig.mapping, into
//            a fixed 20-frame ring. The dataset's controlling hand is the player's dominant hand (config, default right).
//          - Grip (ASSUMPTION, see ExpertAIConfig.gripThreshold): 1 while that hand's grip button reads above the threshold or the hand
//            holds a weapon (it always does in a fight); the recordings have grip = 1 throughout, so this matches them.
//          - Runs an inference 10 times a second, spread over frames (layersPerFrame layers each), never every frame; the work is timed
//            on the main thread. Offsets come back in dataset axes and are turned into Unity vectors with the same mapping.
//          - A pause (game time stops) or a frame longer than 0.1 s restarts the window: the player kept moving in real time, and the
//            training data was cut at every time jump over 0.1 s (ml-services preprocessing, max_gap_s). An inference started before the
//            gap is dropped, not published. Until the window holds 20 frames again there is no prediction.
//          - What the game may read (ExpertOutputs): WorldOffset gives the head only, at horizons up to config.maxHorizonSeconds, and the
//            hands only with config.useHandPrediction; anything else reads as zero ("no prediction") and is counted in RefusedReads.
//            RawWorldOffset (every output) is for measurement and the debug drawing only.
//          - A missing model, a norm.json that does not parse, an exception, a NaN / infinite output or a moving average above
//            maxAverageMs disables Expert for the rest of the fight: one log line, Fallbacks + 1, Disabled event, the worker and its
//            tensors are freed at once; Level 3 then plays exactly like Normal.
//          - Debug overlay (Editor / development builds, off by default): spheres at the predicted positions, inference ms, the
//            window state and the axis mapping.
//          Known limits: the model learnt from 12 minutes of lab sword strikes, positions only, about 31 cm mean error over 1 s.
//          Its output is a hint; MonsterExpertBrain never trusts it without the fairness governor.
// Dependencies: StrikeFit.ExpertAI (MotionForecaster, MotionWindow, ExpertHealth, ExpertAIConfig), OVRCameraRig, OVRInput, PlayerCombat
// Attach to: the Level 3 monster (added by MonsterExpertBrain when Expert is on)

using StrikeFit.ExpertAI;
using UnityEngine;

/// <summary>Where the player's head and hands are (metres): horizontal axes in tracking space, height above the arena floor
/// (<paramref name="floorY"/>, world). The game reads the OVR rig; the harness scripts it.</summary>
public interface IPlayerPoseSource
{
    Transform Space { get; }
    bool TryGet(float floorY, out Vector3 head, out Vector3 rightHand, out Vector3 leftHand, out float rightGrip, out float leftGrip);
}

public sealed class OvrPoseSource : IPlayerPoseSource
{
    readonly OVRCameraRig rig;
    public OvrPoseSource(OVRCameraRig rig) { this.rig = rig; }
    public Transform Space => rig != null ? rig.trackingSpace : null;

    public bool TryGet(float floorY, out Vector3 head, out Vector3 rightHand, out Vector3 leftHand, out float rightGrip, out float leftGrip)
    {
        head = rightHand = leftHand = Vector3.zero; rightGrip = leftGrip = 0f;
        // The anchors are created by the rig itself and can be missing for a frame while it (re)builds them.
        if (rig == null || rig.trackingSpace == null || rig.centerEyeAnchor == null || rig.rightHandAnchor == null || rig.leftHandAnchor == null) return false;
        head = Local(rig.centerEyeAnchor.position, floorY);
        rightHand = Local(rig.rightHandAnchor.position, floorY);
        leftHand = Local(rig.leftHandAnchor.position, floorY);
        rightGrip = OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger, OVRInput.Controller.RTouch);
        leftGrip = OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger, OVRInput.Controller.LTouch);
        return true;
    }

    /// <summary>Tracking-space x / z, height above the floor: the rigs here use an eye-level tracking origin, where the tracking-space
    /// height of the head is about 0, and the model's head-height feature (mean 1.53 m) is measured from the floor.</summary>
    public Vector3 Local(Vector3 world, float floorY)
    {
        Vector3 l = rig.trackingSpace.InverseTransformPoint(world);
        l.y = world.y - floorY;
        return l;
    }
}

public class ExpertMotionPredictor : MonoBehaviour
{
    public ExpertAIConfig config;
    [Tooltip("Spheres at the predicted positions, inference ms, window state and mapping (Editor / development builds only).")]
    public bool debugOverlay = false;

    /// <summary>Raised once when Expert switches itself off for the rest of the fight (reason).</summary>
    public event System.Action<string> Disabled;

    public bool ModelLoaded { get; private set; }
    public bool Healthy => ModelLoaded && health != null && !health.Disabled;
    public string DisabledReason => health != null ? health.Reason : "not started";
    public int Fallbacks { get; private set; }
    public int Inferences => health != null ? health.Inferences : 0;
    public double AverageMs => health != null ? health.AverageMs : 0;
    public double MaxMs => health != null ? health.MaxMs : 0;
    public double P95Ms => health != null ? health.Percentile(0.95) : 0;
    public int OverBudget { get; private set; }
    /// <summary>Three warm-up inferences at load (not counted in the averages).</summary>
    public double WarmUpMs { get; private set; }
    public bool WindowFull => window.Full;
    public int WindowCount => window.Count;
    public float MotionEnergy { get; private set; }
    public bool HasPrediction { get; private set; }
    /// <summary>Main-thread ms of the last finished inference (diagnostics).</summary>
    public double LastInferenceMs { get; private set; }
    /// <summary>Reads of an output the config does not allow (a hand without useHandPrediction, a horizon past maxHorizonSeconds).</summary>
    public int RefusedReads { get; private set; }
    /// <summary>Times the window was restarted after a pause or a long frame.</summary>
    public int WindowRestarts { get; private set; }
    /// <summary>Time.time at which the published prediction's "now" frame was sampled.</summary>
    public float PredictionTime { get; private set; }

    /// <summary>The pose source (the harness swaps in scripted motion).</summary>
    public IPlayerPoseSource Source { get; set; }

    readonly MotionWindow window = new MotionWindow();
    readonly float[] raw = new float[Forecast.Frames * Forecast.RawColumns];
    readonly Vector3[] published = new Vector3[Forecast.Horizons * 3];   // Unity tracking-space offsets: [horizon * 3 + point]
    MotionForecaster forecaster;
    ExpertHealth health;
    float sampleClock, lastStart = -999f, pendingSampleTime;
    bool started, gap, dropPending;
    const float MaxGapSeconds = 0.1f;   // = ml-services configs/motion_forecaster.yaml data.max_gap_s
    static readonly RaycastHit[] floorHits = new RaycastHit[16];

    void Start()
    {
        if (Source == null)
        {
            var rig = FindAnyObjectByType<OVRCameraRig>();
            if (rig != null) Source = new OvrPoseSource(rig);
        }
        health = new ExpertHealth(config != null ? config.maxAverageMs : 3f);
        Load();
        started = true;
    }

    void Load()
    {
        if (config == null || config.model == null) { Disable("the model is missing (ExpertAIConfig.model)"); return; }
        if (config.norm == null || !NormStats.TryParse(config.norm.text, out NormStats norm, out string err)) { Disable("norm.json is missing or does not parse"); return; }
        try
        {
            forecaster = new MotionForecaster(config.model, norm);
            // Warm-up while the monster's entrance plays: the first runs in a session cost ~30 ms (backend set-up). They are not
            // counted in the fallback average.
            for (int i = 0; i < Forecast.Frames; i++) { int o = i * Forecast.RawColumns; raw[o + 2] = 1.53f; raw[o + 5] = 1.1f; raw[o + 8] = 0.9f; raw[o + 9] = 1f; }
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 3; i++) forecaster.Run(raw);
            WarmUpMs = sw.Elapsed.TotalMilliseconds;
            ModelLoaded = true;
        }
        catch (System.Exception e) { Disable("the model failed to load: " + e.GetType().Name + ": " + e.Message); }
    }

    void OnDestroy() => ReleaseModel();

    void ReleaseModel() { forecaster?.Dispose(); forecaster = null; }

    /// <summary>Switch Expert off for the rest of the fight (also used by tests through the harness).</summary>
    bool notified;

    public void Disable(string reason)
    {
        if (health == null) health = new ExpertHealth(3f);
        health.Disable(reason);   // no-op if the health monitor already disabled itself (slow average, NaN)
        if (notified) return;
        notified = true;
        Fallbacks++;
        HasPrediction = false;
        ReleaseModel();   // Expert stays off for this fight: free the worker and its tensors now, not at the end of the scene
        Debug.LogWarning("[ExpertAI] Expert mode switched off for the rest of the fight: " + reason + ". Level 3 continues with the normal behaviour.");
        Disabled?.Invoke(reason);
    }

    void Update()
    {
        if (!started || !Healthy || Source == null || forecaster == null) return;
        // Game time stops while paused, the player does not: the frames before and after must not join into one window.
        if (GamePause.IsPaused || Time.timeScale <= 0f) { gap = true; return; }
        try
        {
            if (gap || Time.unscaledDeltaTime > MaxGapSeconds) RestartWindow();

            // 1. Sample at exactly sampleRateHz on game time (an accumulator, so the rate does not depend on the frame rate).
            float period = 1f / Mathf.Max(1f, config.sampleRateHz);
            sampleClock += Time.deltaTime;
            while (sampleClock >= period) { sampleClock -= period; Sample(); }

            // 2. Advance a running inference a few layers, or start one at inferenceHz.
            if (forecaster.Busy)
            {
                if (forecaster.Step(Mathf.Max(1, config.layersPerFrame))) Complete();
            }
            else if (window.Full && Time.time - lastStart >= 1f / Mathf.Max(0.5f, config.inferenceHz))
            {
                lastStart = Time.time;
                window.CopyOrdered(raw);
                pendingSampleTime = lastSampleTime;
                forecaster.Begin(raw);
            }
        }
        catch (System.Exception e) { Disable("exception: " + e.GetType().Name + ": " + e.Message); }
    }

    /// <summary>After a pause or a long frame: empty window, no prediction, and the inference in flight (if any) is not published.</summary>
    void RestartWindow()
    {
        gap = false;
        window.Clear();
        sampleClock = 0f;
        HasPrediction = false;
        dropPending = forecaster.Busy;
        WindowRestarts++;
    }

    float lastSampleTime;
    float floorY = float.NaN;
    OVRCameraRig rigCache;

    /// <summary>Height of the arena floor under the player, found once per fight by a ray down from the head (the monster's feet if
    /// nothing is hit). The monster's own height is not used: it leaves the ground in its entrance and its leaps.</summary>
    float FloorY()
    {
        if (!float.IsNaN(floorY)) return floorY;
        if (rigCache == null) rigCache = FindAnyObjectByType<OVRCameraRig>();
        floorY = transform.position.y;
        if (rigCache != null)
        {
            // The lowest hit under the head: the player's own weapons and hands can be in the way of the first hit. Once per fight.
            if (rigCache.centerEyeAnchor == null) return floorY;
            int n = Physics.RaycastNonAlloc(rigCache.centerEyeAnchor.position, Vector3.down, floorHits, 4f, ~0, QueryTriggerInteraction.Ignore);
            float lowest = float.MaxValue;
            for (int i = 0; i < n; i++) if (floorHits[i].point.y < lowest) lowest = floorHits[i].point.y;
            if (n > 0) floorY = lowest;
        }
        return floorY;
    }
    /// <summary>The floor height used (verification).</summary>
    public float Floor => float.IsNaN(floorY) ? 0f : floorY;

    void Sample()
    {
        if (!Source.TryGet(FloorY(), out Vector3 h, out Vector3 r, out Vector3 l, out float gr, out float gl)) return;
        bool rightDominant = config.dominantHand == HandSide.Right;
        Vector3 ctrl = rightDominant ? r : l, sec = rightDominant ? l : r;
        float grip = (rightDominant ? gr : gl) >= config.gripThreshold || PlayerCombat.Instance != null ? 1f : 0f;   // weapons are always held
        var m = config.mapping;
        window.Push(m.ToDataset(h), m.ToDataset(ctrl), m.ToDataset(sec), grip);
        MotionEnergy = window.MotionEnergy();
        lastSampleTime = Time.time;
    }

    void Complete()
    {
        bool finite = forecaster.Finish();
        double ms = forecaster.LastMs;
        LastInferenceMs = ms;
        if (ms > config.budgetMs) OverBudget++;
        if (!health.Report(ms, finite)) { Disable(health.Reason); return; }
        if (dropPending) { dropPending = false; return; }   // started before a pause / long frame: describes a window that was thrown away
        var m = config.mapping;
        for (int hz = 0; hz < Forecast.Horizons; hz++)
            for (int p = 0; p < 3; p++)
                published[hz * 3 + p] = m.ToUnity(Forecast.Offset(forecaster.Offsets, hz, (Forecast.Point)p));
        HasPrediction = true;
        PredictionTime = pendingSampleTime;
    }

    /// <summary>Predicted displacement (world metres) of a point at a horizon (0..3), measured from where it was when the prediction's
    /// window ended. Zero when there is no prediction, and for every output the config does not allow (ExpertOutputs): the game's
    /// only way to read the forecast.</summary>
    public Vector3 WorldOffset(Forecast.Point p, int horizon)
    {
        Vector3 local = LocalOffset(p, horizon);
        if (local == Vector3.zero || Source == null || Source.Space == null) return Vector3.zero;
        return Source.Space.TransformVector(local);
    }

    /// <summary>The same in tracking space (no rig needed): the gate itself, so tests can drive it.</summary>
    public Vector3 LocalOffset(Forecast.Point p, int horizon)
    {
        if (!HasPrediction) return Vector3.zero;
        if (config == null || !ExpertOutputs.MayRead(p, horizon, config.useHandPrediction, config.maxHorizonSeconds)) { RefusedReads++; return Vector3.zero; }
        return published[horizon * 3 + (int)p];
    }

    /// <summary>Every output, ungated: measurement (ExpertHarness) and the debug drawing only, never gameplay.</summary>
    public Vector3 RawWorldOffset(Forecast.Point p, int horizon)
    {
        if (!HasPrediction || Source == null || Source.Space == null) return Vector3.zero;
        return Source.Space.TransformVector(published[Mathf.Clamp(horizon, 0, 3) * 3 + (int)p]);
    }

    /// <summary>Tests: publish a prediction (tracking-space offsets, [horizon * 3 + point]) without running the model.</summary>
    public void PublishForTest(Vector3[] offsets, float sampleTime)
    {
        System.Array.Copy(offsets, published, published.Length);
        HasPrediction = true; PredictionTime = sampleTime;
    }

    /// <summary>Hand index used by the game (0 = right, 1 = left) to the forecaster's point.</summary>
    public Forecast.Point PointForHand(int hand)
    {
        bool right = hand == 0;
        return (config.dominantHand == HandSide.Right) == right ? Forecast.Point.Controlling : Forecast.Point.Secondary;
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    void OnDrawGizmos()
    {
        if (!debugOverlay || !HasPrediction || Source == null || Source.Space == null) return;
        var rig = FindAnyObjectByType<OVRCameraRig>();
        if (rig == null) return;
        Transform[] now = { rig.centerEyeAnchor, config.dominantHand == HandSide.Right ? rig.rightHandAnchor : rig.leftHandAnchor, config.dominantHand == HandSide.Right ? rig.leftHandAnchor : rig.rightHandAnchor };
        Color[] col = { Color.cyan, Color.yellow, new Color(1f, 0.5f, 0f) };
        for (int p = 0; p < 3; p++)
        {
            Vector3 prev = now[p].position;
            for (int hz = 0; hz < Forecast.Horizons; hz++)
            {
                Vector3 at = now[p].position + RawWorldOffset((Forecast.Point)p, hz);
                Gizmos.color = col[p]; Gizmos.DrawWireSphere(at, 0.03f + 0.01f * hz); Gizmos.DrawLine(prev, at); prev = at;
            }
        }
    }

    void OnGUI()
    {
        if (!debugOverlay) return;
        GUI.Label(new Rect(10, 10, 760, 22), "Expert AI: " + (Healthy ? "running" : "OFF (" + DisabledReason + ")") + "  window " + WindowCount + "/20  energy " +
                  MotionEnergy.ToString("0.00") + " m/s  inference avg " + AverageMs.ToString("0.00") + " ms max " + MaxMs.ToString("0.00") + " ms  (" + Inferences + ")");
        GUI.Label(new Rect(10, 30, 760, 22), "mapping: " + (config != null ? config.mapping.Name : "-"));
    }
#endif
}
