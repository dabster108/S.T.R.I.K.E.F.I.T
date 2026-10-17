// ControllerMapping.cs
// Purpose: Which controller does what in the menu, the pre-battle panels and the end panels. Default (right-handed): the right
//          controller shows the laser and clicks with its trigger; the left controller shows no laser and does not click.
//          No laser in a fight (both hands hold weapons): it shows only while a panel is open. The right thumbstick does not
//          scroll the UI. The pointing hand can be swapped (PlayerPrefs "StrikeFit.UiHand", ControlSettings.defaultHand), and
//          a second laser on the other hand is a toggle (ControlSettings.leftLaser, default off).
//          The component is created at runtime in every scene that has an OVRInputModule, so the menu and all three levels get it
//          with no scene edit beyond the one-time swap of OVRInputModule for StrikeFitInputModule.
// Dependencies: StrikeFitInputModule, UiLaser, ControlSettings, ControllerInput, BattleManager (optional), GamePause
// Attach to: created at runtime ("ControllerMapping")

using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

[DefaultExecutionOrder(-50)]
public class ControllerMapping : MonoBehaviour
{
    public const string HandPref = "StrikeFit.UiHand";
    const string Tag = "[StrikeFitControls]";

    public static ControllerMapping Instance { get; private set; }

    /// <summary>The master toggle (ControlSettings.mappingEnabled).</summary>
    public static bool Active => ControlSettings.Current.mappingEnabled;

    /// <summary>The hand that points the laser and clicks.</summary>
    public static OVRInput.Controller PointHand { get; private set; } = OVRInput.Controller.RTouch;

    /// <summary>The hand whose A / B buttons act (confirm, retry, pause, back): the right controller by default, the pointing hand
    /// when the hand was swapped. With the mapping off it is always the right controller, as before.</summary>
    public static OVRInput.Controller ButtonHand => Active ? PointHand : OVRInput.Controller.RTouch;

    /// <summary>True when the laser should be drawn: any time except a running, un-paused fight (both hands hold weapons).</summary>
    public static bool LaserWanted
    {
        get
        {
            var bm = BattleManager.Instance;
            bool fight = bm != null && bm.IsBattleActive && !GamePause.IsPaused;
            return !fight;
        }
    }

    [Header("Runtime (read-only)")]
    public StrikeFitInputModule module;
    public UiLaser laser;
    public OVRCameraRig rig;
    public Transform uiRay;

    GameObject oldLaser;
    float vibUntil;
    OVRInput.Controller vibHand;

    // ───────────────────────── hand preference ─────────────────────────

    /// <summary>Configured hand: PlayerPrefs when set, else ControlSettings.defaultHand.</summary>
    public static UiHand ConfiguredHand()
    {
        if (PlayerPrefs.HasKey(HandPref)) return PlayerPrefs.GetInt(HandPref, 0) == 1 ? UiHand.Left : UiHand.Right;
        return ControlSettings.Current.defaultHand;
    }

    /// <summary>Remember a hand and apply it now. A user setting, so it is saved.</summary>
    public static void SetHand(UiHand hand)
    {
        PlayerPrefs.SetInt(HandPref, hand == UiHand.Left ? 1 : 0);
        PlayerPrefs.Save();
        SwitchHand(hand == UiHand.Left ? OVRInput.Controller.LTouch : OVRInput.Controller.RTouch);
    }

    /// <summary>Move the laser and the click to this hand (not saved; the second-laser toggle uses it).</summary>
    public static void SwitchHand(OVRInput.Controller hand)
    {
        if (hand == PointHand) return;
        PointHand = hand;
        if (Instance != null) Instance.ApplyHand();
        Debug.Log($"{Tag} pointing hand {(hand == OVRInput.Controller.LTouch ? "left" : "right")}");
    }

    // ───────────────────────── setup ─────────────────────────

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        PointHand = ConfiguredHand() == UiHand.Left ? OVRInput.Controller.LTouch : OVRInput.Controller.RTouch;
        if (!Active) return;

        module = FindAnyObjectByType<StrikeFitInputModule>();
        rig = FindAnyObjectByType<OVRCameraRig>();
        if (module == null)
        {
            Debug.LogWarning($"{Tag} no StrikeFitInputModule in this scene (the stock OVRInputModule clicks with either hand); the laser mapping still applies.");
            var stock = FindAnyObjectByType<OVRInputModule>();
            if (stock != null) uiRay = stock.rayTransform;
        }
        else uiRay = module.rayTransform;

        BuildLaser();
        if (module != null)
        {
            module.m_Cursor = laser;
            module.useRightStickScroll = false;   // the right thumbstick does nothing in UI
        }
        ApplyHand();
    }

    void BuildLaser()
    {
        // The scene's old laser (ControllerLaserPointer: physics raycast, can be stopped by weapons, never shown on end panels).
        // Its look is reused; the object is switched off for good.
        Material mat = null;
        Color startColor = new Color(0f, 1f, 1f, 0.9f), endColor = startColor;
        float width = 0.005f;
        foreach (var old in FindObjectsByType<ControllerLaserPointer>(FindObjectsInactive.Include))
        {
            var lr = old.GetComponent<LineRenderer>();
            if (lr != null && mat == null)
            {
                mat = lr.sharedMaterial; startColor = lr.startColor; endColor = lr.endColor; width = lr.startWidth;
            }
            oldLaser = old.gameObject;
            old.enabled = false;
            old.gameObject.SetActive(false);
        }
        if (mat == null) mat = new Material(Shader.Find("Sprites/Default"));   // always included in the build

        var go = new GameObject("UiLaser");
        var line = go.AddComponent<LineRenderer>();
        line.positionCount = 2;
        line.useWorldSpace = true;
        line.sharedMaterial = mat;
        line.startColor = startColor; line.endColor = endColor;
        line.startWidth = width; line.endWidth = width;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.allowOcclusionWhenDynamic = false;
        line.enabled = false;

        var ret = new GameObject("Reticle");
        ret.transform.SetParent(go.transform, false);
        ret.AddComponent<MeshFilter>().sharedMesh = UiLaser.BuildDisc();
        var mr = ret.AddComponent<MeshRenderer>();
        var rmat = new Material(mat);
        rmat.color = new Color(1f, 1f, 1f, 0.95f);
        mr.sharedMaterial = rmat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        ret.SetActive(false);

        laser = go.AddComponent<UiLaser>();
        laser.line = line;
        laser.reticle = ret.transform;
    }

    /// <summary>Parent the ray origin and the laser under the pointing hand's anchor (same local pose).</summary>
    void ApplyHand()
    {
        if (rig == null) return;
        Transform anchor = PointHand == OVRInput.Controller.LTouch ? rig.leftHandAnchor : rig.rightHandAnchor;
        if (anchor == null) return;
        if (uiRay != null && uiRay.parent != anchor) uiRay.SetParent(anchor, false);
        if (laser != null && laser.transform.parent != anchor) laser.transform.SetParent(anchor, false);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (vibUntil > 0f) OVRInput.SetControllerVibration(0f, 0f, vibHand);
    }

    // ───────────────────────── haptic tick ─────────────────────────

    /// <summary>A short tick on the pointing hand: soft for hover, firmer for a click.</summary>
    public static void Tick(bool click)
    {
        var inst = Instance;
        if (inst == null) return;
        var s = ControlSettings.Current;
        if (!s.hapticTick) return;
        inst.Vibrate(click ? s.clickAmplitude : s.hoverAmplitude, click ? s.clickSeconds : s.hoverSeconds);
    }

    void Vibrate(float amplitude, float seconds)
    {
        vibHand = PointHand;
        OVRInput.SetControllerVibration(1f, amplitude, vibHand);
        vibUntil = Time.realtimeSinceStartup + seconds;
    }

    void Update()
    {
        // B = back one panel, whenever a panel is open (not in a fight, not while the pause menu owns B).
        if (Active && LaserWanted && !GamePause.IsPaused && ControllerInput.Down(Pad.Two, ButtonHand)) UiBack.Back();

        if (vibUntil > 0f && Time.realtimeSinceStartup >= vibUntil)
        {
            OVRInput.SetControllerVibration(0f, 0f, vibHand);
            vibUntil = 0f;
        }
    }

    // ───────────────────────── creation in every scene ─────────────────────────

    static void Ensure()
    {
        if (!Active) return;
        if (Instance != null) return;
        if (FindAnyObjectByType<OVRInputModule>() == null) return;   // a scene without the Meta UI module has no laser UI
        new GameObject("ControllerMapping").AddComponent<ControllerMapping>();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        Ensure();
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode) { Ensure(); }
}
