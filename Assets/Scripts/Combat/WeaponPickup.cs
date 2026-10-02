// WeaponPickup.cs
// Purpose: A weapon pair staged in the arena before the fight (Level 3). Reach either weapon of the pair with either
//          hand and squeeze grip: BOTH hands are equipped with this weapon type through PlayerCombat.EquipWeaponSet
//          (same hand-anchor weapons + HitDetection as Levels 1-2), the previous pair is dropped back in its spot,
//          and this pair's staged props disappear. Locked once the battle starts (the choice holds for the fight).
// Dependencies: PlayerCombat (weaponSets), BattleManager, OVRInput / OVRCameraRig
// Attach to: the pair's staging root (children = the two staged props + grab points).

using System.Collections.Generic;
using UnityEngine;

public class WeaponPickup : MonoBehaviour
{
    public static readonly List<WeaponPickup> All = new List<WeaponPickup>();

    public WeaponType weaponType = WeaponType.Mace;
    public string displayName = "MACE";
    [TextArea] public string description = "";

    [Header("Grab")]
    [Tooltip("Points on the staged props the hand must reach (one per weapon of the pair).")]
    public Transform[] grabPoints = new Transform[0];
    public float grabRadius = 0.35f;
    public OVRInput.Button grabButton = OVRInput.Button.PrimaryHandTrigger;
    [Tooltip("Also accept the index trigger (players often squeeze both).")]
    public bool alsoAcceptIndexTrigger = true;

    [Header("Staging")]
    [Tooltip("The staged props, hidden while this pair is in the player's hands.")]
    public GameObject stagedProps;
    [Tooltip("Glow shown while a hand is in reach.")]
    public GameObject reachHighlight;
    [Tooltip("Idle glow: the pair's ground ring pulses and turns slowly while the pair waits to be picked up (the props " +
             "rest on crates / in the planks, so they do not bob).")]
    public Renderer markerRing;
    public Color markerColor = new Color(1f, 0.75f, 0.3f, 0.35f);
    public float markerPulseSpeed = 0.8f;
    [Range(0f, 1f)] public float markerPulseMin = 0.45f;
    public float markerSpinSpeed = 12f;
    public TMPro.TMP_Text label;

    [Header("Feedback")]
    public AudioSource audioSource;
    public AudioClip pickupClip;
    [Range(0f, 1f)] public float pickupVolume = 0.9f;

    public bool IsHeld { get; private set; }
    public bool HandInReach { get; private set; }

    private OVRCameraRig rig;
    private MaterialPropertyBlock markerMpb;
    /// <summary>Current idle-glow brightness of the ground ring (verification).</summary>
    public float MarkerPulse { get; private set; }

    void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    void OnDisable() { All.Remove(this); }

    void Start()
    {
        if (audioSource == null) audioSource = GetComponent<AudioSource>();
        if (reachHighlight != null) reachHighlight.SetActive(false);
        if (label != null) label.text = displayName;
    }

    void Update()
    {
        bool locked = BattleManager.Instance != null && BattleManager.Instance.IsBattleActive;
        UpdateMarker(!locked && !IsHeld);
        if (locked || IsHeld || PlayerCombat.Instance == null)
        {
            SetReach(false);
            return;
        }

        if (rig == null) rig = PlayerCombat.Instance.GetComponent<OVRCameraRig>();
        bool right = rig != null && InReach(rig.rightHandAnchor);
        bool left = rig != null && InReach(rig.leftHandAnchor);
        SetReach(right || left);

        if ((right && Pressed(OVRInput.Controller.RTouch)) || (left && Pressed(OVRInput.Controller.LTouch)))
            Equip();

#if UNITY_EDITOR
        // Editor test without a headset: 1 = Mace, 2 = War Flail, 3 = Katana.
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb != null)
        {
            if ((weaponType == WeaponType.Mace && kb.digit1Key.wasPressedThisFrame) ||
                (weaponType == WeaponType.Flail && kb.digit2Key.wasPressedThisFrame) ||
                (weaponType == WeaponType.Katana && kb.digit3Key.wasPressedThisFrame))
                Equip();
        }
#endif
    }

    void UpdateMarker(bool waiting)
    {
        if (markerRing == null) return;
        if (markerRing.enabled != waiting) markerRing.enabled = waiting;
        if (!waiting) return;
        if (markerMpb == null) markerMpb = new MaterialPropertyBlock();
        // Phase offset per pair so the three rings do not pulse in lockstep.
        MarkerPulse = Mathf.Lerp(markerPulseMin, 1f, 0.5f + 0.5f * Mathf.Sin((Time.time * markerPulseSpeed + (int)weaponType * 0.37f) * Mathf.PI * 2f));
        Color c = markerColor; c.a = Mathf.Clamp01(markerColor.a * (0.6f + 1.4f * MarkerPulse));
        markerRing.GetPropertyBlock(markerMpb);
        markerMpb.SetColor("_BaseColor", c);
        markerRing.SetPropertyBlock(markerMpb);
        markerRing.transform.Rotate(0f, markerSpinSpeed * Time.deltaTime, 0f, Space.World);
    }

    bool InReach(Transform hand)
    {
        if (hand == null) return false;
        foreach (var p in grabPoints)
            if (p != null && (p.position - hand.position).sqrMagnitude <= grabRadius * grabRadius) return true;
        return false;
    }

    bool Pressed(OVRInput.Controller c)
    {
        return OVRInput.GetDown(grabButton, c) ||
               (alsoAcceptIndexTrigger && OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, c));
    }

    void SetReach(bool on)
    {
        if (HandInReach == on) return;
        HandInReach = on;
        if (reachHighlight != null) reachHighlight.SetActive(on);
    }

    /// <summary>Equip this pair in both hands (also used by tests and the editor keys).</summary>
    public bool Equip()
    {
        if (PlayerCombat.Instance == null) return false;
        if (BattleManager.Instance != null && BattleManager.Instance.IsBattleActive) return false;
        if (!PlayerCombat.Instance.EquipWeaponSet(weaponType)) return false;

        foreach (var other in All)
            if (other != this) other.SetHeld(false);
        SetHeld(true);
        if (audioSource != null && pickupClip != null) audioSource.PlayOneShot(pickupClip, pickupVolume);
        return true;
    }

    void SetHeld(bool held)
    {
        IsHeld = held;
        if (stagedProps != null) stagedProps.SetActive(!held);
        if (held) SetReach(false);
    }
}
