// PlayerCombat.cs
// Purpose: Velocity-based hammer swing damage, stamina, functional shield block,
//          swing-angle tracking (stretch goal — flagged).
// Dependencies: RoundManager, OVRInput (Meta XR), HitDetection, PlayerHealth, WeaponType, XPManager
// Attach to: Player root / XR Rig (OVRCameraRig)

using UnityEngine;

public enum Hand { Right, Left }

/// <summary>
/// One selectable dual-wield pair (Level 3+): the right/left weapon objects already parented to the hand anchors
/// (same attachment pattern as the L1 hammers / L2 swords) plus the swing stats that become active when it is equipped.
/// Per-hit damage, cooldown and hit window live on each weapon's own HitDetection.
/// </summary>
[System.Serializable]
public class WeaponSet
{
    public WeaponType type = WeaponType.None;
    public GameObject rightObject;
    public GameObject leftObject;
    [Tooltip("Swing-speed gate (m/s) while this pair is held. Copied to PlayerCombat.minSwingVelocity on equip.")]
    public float minSwingVelocity = 1.2f;
    [Tooltip("Speed at which velocity damage scaling caps.")]
    public float maxSwingVelocity = 6f;
    [Tooltip("Damage multiplier reached at maxSwingVelocity.")]
    public float maxVelocityDamageMultiplier = 2.5f;
    [Tooltip("Weapon flavour multiplier applied after fitness modifiers (replaces the hammer/sword switch).")]
    public float damageMultiplier = 1f;
}

public class PlayerCombat : MonoBehaviour
{
    public static PlayerCombat Instance;

    /// <summary>Raised when a weapon pair is equipped through EquipWeaponSet (Level 3 pickup).</summary>
    public event System.Action<WeaponType> WeaponSetEquipped;

    [Header("Weapons (set per level via LevelManager / Inspector)")]
    [Tooltip("Primary attack weapon for this level.")]
    public WeaponType primaryWeapon = WeaponType.Hammer;
    [Tooltip("Offhand (e.g. Shield). Use None if single-weapon.")]
    public WeaponType offhandWeapon = WeaponType.None;

    [Header("Weapon Transforms")]
    [Tooltip("Active swing / hit transform for PRIMARY hand (hammer head, sword blade, etc.).")]
    public Transform weaponTransform;
    [Tooltip("Active swing / hit transform for OFFHAND (second hammer). Null if single-weapon.")]
    public Transform offhandWeaponTransform;
    [Tooltip("Optional shield transform for block VFX/pose (disabled in Level 1 — no shield).")]
    public Transform shieldTransform;

    [Header("Weapon Prefab Roots (enable/disable by type)")]
    public GameObject hammerObject;
    [Tooltip("Second hammer for dual-wield (Level 1 loadout). Null if single-weapon.")]
    public GameObject offhandHammerObject;
    public GameObject swordObject;
    [Tooltip("Second sword for dual-wield (Level 2 loadout). Null if single-weapon.")]
    public GameObject offhandSwordObject;
    public GameObject shieldObject; // disabled in Level 1 — no shield

    [Header("Selectable Weapon Pairs (Level 3+; empty in Levels 1-2)")]
    public System.Collections.Generic.List<WeaponSet> weaponSets = new System.Collections.Generic.List<WeaponSet>();

    [Header("Weapon surface proxy (visual only)")]
    [Tooltip("Draw each weapon resting on the monster's surface instead of buried in it (WeaponSurfaceProxy). The logical weapon " +
             "(collider, swing speed, damage, hit and clash events) is never moved. Off = drawn at the controller.")]
    public bool weaponSurfaceProxy = true;
    [Tooltip("Faint unlit material for the ghost of the real controller pose (M_WeaponGhost).")]
    public Material weaponGhostMaterial;
    [Tooltip("Show the ghost of the real pose when the drawn weapon is more than 6 cm from the controller.")]
    public bool weaponGhost = true;

    [Header("Own-weapon collisions")]
    [Tooltip("Physics.IgnoreCollision between every pair of active colliders under this rig (both held weapons, any hand " +
             "colliders), re-applied each time weapons are shown, equipped or swapped. The player's two weapons never touch " +
             "each other or the player's own rig.")]
    public bool ignoreOwnWeaponCollisions = true;
    /// <summary>Collider pairs ignored by the last IgnoreOwnWeaponCollisions call (verification).</summary>
    public int IgnoredColliderPairs { get; private set; }
    private readonly System.Collections.Generic.List<Collider> ownColliders = new System.Collections.Generic.List<Collider>();
    [Tooltip("Show the equipped pair before the battle starts (Level 3: the player picks it up in the arena). " +
             "Off = weapons appear only once the battle is active (Levels 1-2).")]
    public bool showWeaponsBeforeBattle = false;

    [Header("Controller")]
    public OVRInput.Controller swingController = OVRInput.Controller.RTouch;
    [Tooltip("Controller for offhand hammer (dual-wield).")]
    public OVRInput.Controller offhandSwingController = OVRInput.Controller.LTouch;
    public OVRInput.Controller shieldController = OVRInput.Controller.LTouch; // unused in Level 1 (no shield)

    [Header("Hand Assignment (configurable — flip without code)")]
    [Tooltip("Which hand holds the primary weapon (hammer/sword). Shield goes to the other hand. " +
             "Auto-sets swingController/shieldController. To physically reparent the weapon objects, " +
             "move them under the matching HandAnchor in the scene (or ask MCP to do it).")]
    public Hand weaponHand = Hand.Right;

    [Header("Swing")]
    public float attackCooldown = 0.15f;

    [Header("Swing — Velocity Damage")]
    [Tooltip("Below this controller speed (m/s) a hit does NOT register as an attack. Anti-spam.")]
    public float minSwingVelocity = 1.2f;
    [Tooltip("Speed at which damage scaling reaches its max multiplier.")]
    public float maxSwingVelocity = 6.0f;
    [Tooltip("Damage multiplier at maxSwingVelocity (1x at minSwingVelocity).")]
    public float maxVelocityDamageMultiplier = 2.5f;
    public float velocitySmooth = 0.12f;

    [Header("Swing — Angle (STRETCH GOAL — partially implemented)")]
    [Tooltip("Enable swing-arc tracking as a secondary damage multiplier. " +
             "STRETCH GOAL: basic angular velocity tracking is implemented; full overhead-vs-flick " +
             "classification is pending. Currently uses angular speed as a small bonus.")]
    public bool useAngleMultiplier = true;
    [Tooltip("Angular speed (deg/s) at which the angle bonus caps out.")]
    public float maxAngularSpeed = 900f;
    [Tooltip("Max bonus from swing angle (added on top of velocity damage).")]
    public float maxAngleBonus = 0.4f;

    [Header("Stamina / Energy")]
    public float maxStamina = 100f;
    public float currentStamina = 100f;
    public float staminaCostPerHit = 8f;
    public float staminaRegenPerSecond = 12f;

    [Header("Fitness → Damage")]
    [Tooltip("Extra damage per RoundManager tap accumulated this round.")]
    public float damagePerTapBonus = 0.35f;
    [Tooltip("Bonus damage from swing speed (legacy — kept for compat; velocity scaling above is primary).")]
    public float swingSpeedDamageScale = 0.08f;

    [Header("Block — Functional Shield")]
    public bool blockEnabled = true;
    [Tooltip("Fraction of incoming damage negated while blocking (0.7 = 70% reduction, 0.9 = 90%).")]
    [Range(0f, 1f)] public float blockDamageReduction = 0.8f;
    [Tooltip("Button used to raise the shield. Default: left hand trigger (grip).")]
    public OVRInput.Button blockButton = OVRInput.Button.PrimaryHandTrigger;
    [Tooltip("If true, shield stays raised while button held. If false, toggle on press.")]
    public bool holdToBlock = true;

    [Header("Runtime (read-only)")]
    public float CurrentSwingSpeed { get; private set; }
    [Tooltip("Swing speed of the offhand (left) hammer. Used by HitDetection on the second hammer.")]
    public float CurrentOffhandSwingSpeed { get; private set; }
    public float CurrentAngularSpeed { get; private set; }
    public bool IsBlocking { get; private set; }
    /// <summary>True only on the frame a block successfully absorbed a hit (for XP/SFX).</summary>
    public bool BlockSucceededThisFrame { get; private set; }

    // Back-compat alias used by older HitDetection wiring
    public Transform hammerTransform
    {
        get => weaponTransform;
        set => weaponTransform = value;
    }

    private float lastAttackTime = -999f;
    private Vector3 lastWeaponPos;
    private Vector3 lastOffhandWeaponPos;
    private Quaternion lastWeaponRot;
    private Vector3 smoothedVelocity;
    private Vector3 smoothedOffhandVelocity;

    void Awake()
    {
        Instance = this;
        currentStamina = maxStamina;
        ApplyHandAssignment();
        ResolveWeaponReferences();
        // Ensure weapons are hidden before first frame (Start UI + laser first).
        if (hammerObject != null) hammerObject.SetActive(false);
        if (offhandHammerObject != null) offhandHammerObject.SetActive(false);
        if (swordObject != null) swordObject.SetActive(false);
        if (offhandSwordObject != null) offhandSwordObject.SetActive(false);
        if (shieldObject != null) shieldObject.SetActive(false);
        foreach (var set in weaponSets)
        {
            if (set == null) continue;
            if (set.rightObject != null) set.rightObject.SetActive(false);
            if (set.leftObject != null) set.leftObject.SetActive(false);
        }
    }

    void Start()
    {
        ResolveWeaponReferences();

        if (weaponTransform == null)
        {
            var tagged = GameObject.FindGameObjectWithTag("Hammer");
            if (tagged != null)
                weaponTransform = tagged.transform;
        }

        if (weaponTransform != null)
        {
            lastWeaponPos = weaponTransform.position;
            lastWeaponRot = weaponTransform.rotation;
        }
        if (offhandWeaponTransform != null)
            lastOffhandWeaponPos = offhandWeaponTransform.position;

        ApplyWeaponVisuals();
    }

    void Update()
    {
        UpdateSwingVelocity();
        RegenerateStamina();
        UpdateBlockInput();

#if UNITY_EDITOR
        if (UnityEngine.InputSystem.Keyboard.current != null &&
            UnityEngine.InputSystem.Keyboard.current.hKey.wasPressedThisFrame)
        {
            if (RoundManager.Instance != null && RoundManager.Instance.roundActive)
                RoundManager.Instance.RegisterTap();
        }

        // Editor: hold Left Shift to simulate block (for testing without headset)
        if (UnityEngine.InputSystem.Keyboard.current != null &&
            UnityEngine.InputSystem.Keyboard.current.leftShiftKey.isPressed &&
            offhandWeapon == WeaponType.Shield)
        {
            OnBlock(true);
        }
        else if (IsBlocking && UnityEngine.InputSystem.Keyboard.current != null &&
                 !UnityEngine.InputSystem.Keyboard.current.leftShiftKey.isPressed)
        {
            OnBlock(false);
        }
#endif
    }

    void LateUpdate()
    {
        // Reset per-frame flag after all damage routing for this frame is done
        BlockSucceededThisFrame = false;
    }

    /// <summary>
    /// Called by LevelManager when the scene loadout is applied.
    /// </summary>
    public void SetLoadout(WeaponType primary, WeaponType offhand)
    {
        primaryWeapon = primary;
        offhandWeapon = offhand;
        ApplySetStats(FindSet(primary));
        ApplyWeaponVisuals();
    }

    public WeaponSet FindSet(WeaponType type)
    {
        if (type == WeaponType.None || weaponSets == null) return null;
        foreach (var set in weaponSets)
            if (set != null && set.type == type) return set;
        return null;
    }

    /// <summary>True when the current primary weapon is one of the selectable pairs.</summary>
    public bool HasWeaponSetEquipped => FindSet(primaryWeapon) != null;

    /// <summary>
    /// Level 3 pickup: equip BOTH hands with this pair, replacing whatever was held, and make its swing stats active.
    /// Also written to LevelManager so StartBattle's loadout step keeps the choice.
    /// </summary>
    public bool EquipWeaponSet(WeaponType type)
    {
        var set = FindSet(type);
        if (set == null) return false;
        if (LevelManager.Instance != null)
        {
            LevelManager.Instance.playerPrimaryWeapon = type;
            LevelManager.Instance.playerOffhandWeapon = type;
        }
        weaponTransform = null;
        offhandWeaponTransform = null;
        SetLoadout(type, type);
        WeaponSetEquipped?.Invoke(type);
        return true;
    }

    void ApplySetStats(WeaponSet set)
    {
        if (set == null) return;
        minSwingVelocity = set.minSwingVelocity;
        maxSwingVelocity = set.maxSwingVelocity;
        maxVelocityDamageMultiplier = set.maxVelocityDamageMultiplier;
    }

    /// <summary>
    /// Sets OVRInput.Controller fields based on weaponHand enum.
    /// Right: hammer on RTouch, shield on LTouch. Left: hammer on LTouch, shield on RTouch.
    /// This makes hand-swapping a one-field Inspector change (controller tracking flips automatically).
    /// Note: physically reparenting weapon objects under the matching HandAnchor is a scene step.
    /// </summary>
    public void ApplyHandAssignment()
    {
        if (weaponHand == Hand.Right)
        {
            swingController = OVRInput.Controller.RTouch;
            shieldController = OVRInput.Controller.LTouch;
        }
        else
        {
            swingController = OVRInput.Controller.LTouch;
            shieldController = OVRInput.Controller.RTouch;
        }
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        // Auto-flip controllers when weaponHand changes in Inspector
        ApplyHandAssignment();
    }
#endif

    /// <summary>
    /// Shows/hides hammer/sword/shield GameObjects based on WeaponType fields.
    /// Level 1 loadout: dual hammers (hammerObject + offhandHammerObject), no shield.
    /// </summary>
    public void ApplyWeaponVisuals()
    {
        ResolveWeaponReferences();

        // Hammers/swords stay hidden until battle starts (laser + Start UI first).
        bool battleLive = BattleManager.Instance != null && BattleManager.Instance.IsBattleActive;

        // Selectable pairs (Level 3): shown as soon as one is picked up when showWeaponsBeforeBattle is on.
        bool setsVisible = battleLive || showWeaponsBeforeBattle;
        WeaponSet activeSet = null;
        foreach (var set in weaponSets)
        {
            if (set == null) continue;
            bool on = setsVisible && set.type == primaryWeapon;
            if (set.rightObject != null) set.rightObject.SetActive(on);
            if (set.leftObject != null) set.leftObject.SetActive(on && offhandWeapon == set.type);
            if (set.type == primaryWeapon) activeSet = set;
        }
        if (activeSet != null)
        {
            if (activeSet.rightObject != null) weaponTransform = activeSet.rightObject.transform;
            if (activeSet.leftObject != null && offhandWeapon == activeSet.type) offhandWeaponTransform = activeSet.leftObject.transform;
        }

        // Primary hammer (right hand)
        if (hammerObject != null)
            hammerObject.SetActive(battleLive && primaryWeapon == WeaponType.Hammer);
        // Offhand hammer (left hand) — dual-wield for Level 1
        if (offhandHammerObject != null)
            offhandHammerObject.SetActive(battleLive && offhandWeapon == WeaponType.Hammer);
        // Offhand sword (left hand) — dual-wield for Level 2
        if (offhandSwordObject != null)
            offhandSwordObject.SetActive(battleLive && offhandWeapon == WeaponType.Sword);
        if (swordObject != null)
            swordObject.SetActive(battleLive && (primaryWeapon == WeaponType.Sword || offhandWeapon == WeaponType.Sword));
        // Shield disabled in Level 1 (no shield loadout) — kept for future re-enable
        if (shieldObject != null)
            shieldObject.SetActive(battleLive && (primaryWeapon == WeaponType.Shield || offhandWeapon == WeaponType.Shield));

        // Prefer an active weapon transform for swing tracking (primary hand)
        if (primaryWeapon == WeaponType.Hammer && hammerObject != null)
            weaponTransform = hammerObject.transform;
        else if (primaryWeapon == WeaponType.Sword && swordObject != null)
            weaponTransform = swordObject.transform;
        else if (primaryWeapon == WeaponType.Shield && shieldObject != null)
            weaponTransform = shieldObject.transform;

        // Offhand weapon transform (second hammer)
        if (offhandWeapon == WeaponType.Hammer && offhandHammerObject != null)
            offhandWeaponTransform = offhandHammerObject.transform;
        else if (offhandWeapon == WeaponType.Sword && offhandSwordObject != null)
            offhandWeaponTransform = offhandSwordObject.transform;

        if (shieldObject != null)
            shieldTransform = shieldObject.transform;

        if (weaponTransform != null)
        {
            lastWeaponPos = weaponTransform.position;
            lastWeaponRot = weaponTransform.rotation;
        }
        if (offhandWeaponTransform != null)
            lastOffhandWeaponPos = offhandWeaponTransform.position;

        IgnoreOwnWeaponCollisions();
        EnsureWeaponProxies();
    }

    /// <summary>Puts a WeaponSurfaceProxy on every held weapon (every HitDetection under the rig) and applies the toggles. Runs at
    /// equip / battle start with the other weapon set-up, never per frame; a weapon that is inactive picks it up when shown.</summary>
    public void EnsureWeaponProxies()
    {
        foreach (var hd in GetComponentsInChildren<HitDetection>(true))
        {
            var proxy = hd.GetComponent<WeaponSurfaceProxy>();
            if (proxy == null) proxy = hd.gameObject.AddComponent<WeaponSurfaceProxy>();
            proxy.proxyEnabled = weaponSurfaceProxy;
            proxy.ghostEnabled = weaponGhost;
            if (weaponGhostMaterial != null) proxy.ghostMaterial = weaponGhostMaterial;
        }
    }

    /// <summary>
    /// IgnoreCollision only takes active colliders and the state is lost when a collider is deactivated, so this runs after
    /// every visibility change (battle start, Level 3 equip, weapon swap). Runs at equip time only, never per frame.
    /// </summary>
    public void IgnoreOwnWeaponCollisions()
    {
        IgnoredColliderPairs = 0;
        if (!ignoreOwnWeaponCollisions) return;
        GetComponentsInChildren(false, ownColliders);
        for (int i = 0; i < ownColliders.Count; i++)
        {
            var a = ownColliders[i];
            if (!a.enabled) continue;
            for (int j = i + 1; j < ownColliders.Count; j++)
            {
                var b = ownColliders[j];
                if (!b.enabled) continue;
                Physics.IgnoreCollision(a, b, true);
                IgnoredColliderPairs++;
            }
        }
        ownColliders.Clear();
    }

    /// <summary>
    /// Defensive reference recovery for scenes/prefabs where one hammer wasn't assigned in Inspector.
    /// Keeps dual-wield loadout resilient across scene reloads and prefab updates.
    /// </summary>
    void ResolveWeaponReferences()
    {
        if (hammerObject == null)
        {
            var right = transform.Find("TrackingSpace/RightHandAnchor/PlayerHammer");
            if (right != null) hammerObject = right.gameObject;
        }

        if (offhandHammerObject == null)
        {
            var left = transform.Find("TrackingSpace/LeftHandAnchor/PlayerHammer_Left");
            if (left != null) offhandHammerObject = left.gameObject;
        }

        if (swordObject == null)
        {
            var rightSword = transform.Find("TrackingSpace/RightHandAnchor/PlayerSword");
            if (rightSword != null) swordObject = rightSword.gameObject;
        }

        if (offhandSwordObject == null)
        {
            var leftSword = transform.Find("TrackingSpace/LeftHandAnchor/PlayerSword_Left");
            if (leftSword != null) offhandSwordObject = leftSword.gameObject;
        }

        if (weaponTransform == null && hammerObject != null)
            weaponTransform = hammerObject.transform;

        if (offhandWeaponTransform == null && offhandHammerObject != null)
            offhandWeaponTransform = offhandHammerObject.transform;
        if (offhandWeaponTransform == null && offhandSwordObject != null)
            offhandWeaponTransform = offhandSwordObject.transform;
    }

    void UpdateBlockInput()
    {
        if (!blockEnabled || offhandWeapon != WeaponType.Shield)
        {
            if (IsBlocking) OnBlock(false);
            return;
        }

        bool buttonDown = OVRInput.Get(blockButton, shieldController);

        if (holdToBlock)
        {
            if (buttonDown != IsBlocking)
                OnBlock(buttonDown);
        }
        else
        {
            if (buttonDown && !IsBlocking)
                OnBlock(true);
            else if (buttonDown && IsBlocking)
                OnBlock(false);
        }

        // Visual: raise shield while blocking (simple local rotation)
        if (shieldTransform != null)
        {
            Quaternion raised = Quaternion.Euler(-60f, 0f, 0f);
            Quaternion lowered = Quaternion.Euler(0f, 0f, 0f);
            float t = IsBlocking ? 1f : 0f;
            shieldTransform.localRotation = Quaternion.Slerp(
                shieldTransform.localRotation,
                IsBlocking ? raised : lowered,
                Time.deltaTime * 12f);
        }
    }

    /// <summary>
    /// Functional shield block. Sets raised/lowered state. Damage reduction applied in ModifyIncomingDamage.
    /// </summary>
    public void OnBlock(bool blocking)
    {
        IsBlocking = blocking;
    }

    /// <summary>
    /// If blocking with a shield, reduce incoming damage. Used by PlayerHealth.
    /// Sets BlockSucceededThisFrame when it actually absorbs damage (caller checks > 0).
    /// </summary>
    public float ModifyIncomingDamage(float amount)
    {
        if (IsBlocking && offhandWeapon == WeaponType.Shield && amount > 0f)
        {
            BlockSucceededThisFrame = true;
            return amount * (1f - Mathf.Clamp01(blockDamageReduction));
        }
        return amount;
    }

    void UpdateSwingVelocity()
    {
        Vector3 controllerVel = OVRInput.GetLocalControllerVelocity(swingController);
        float ovrSpeed = controllerVel.magnitude;

        if (weaponTransform != null)
        {
            Vector3 raw = (weaponTransform.position - lastWeaponPos) / Mathf.Max(Time.deltaTime, 0.0001f);
            lastWeaponPos = weaponTransform.position;
            smoothedVelocity = Vector3.Lerp(smoothedVelocity, raw, 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(0.01f, velocitySmooth)));
            CurrentSwingSpeed = Mathf.Max(ovrSpeed, smoothedVelocity.magnitude);

            // Angular speed (deg/s) — stretch-goal angle tracking
            if (useAngleMultiplier)
            {
                float angleDelta = Quaternion.Angle(lastWeaponRot, weaponTransform.rotation);
                lastWeaponRot = weaponTransform.rotation;
                CurrentAngularSpeed = angleDelta / Mathf.Max(Time.deltaTime, 0.0001f);
            }
        }
        else
        {
            CurrentSwingSpeed = ovrSpeed;
            CurrentAngularSpeed = 0f;
        }

        // Offhand (left) hammer velocity tracking — independent from primary hand
        if (offhandWeaponTransform != null)
        {
            Vector3 offControllerVel = OVRInput.GetLocalControllerVelocity(offhandSwingController);
            float ovrOffSpeed = offControllerVel.magnitude;
            Vector3 rawOff = (offhandWeaponTransform.position - lastOffhandWeaponPos) / Mathf.Max(Time.deltaTime, 0.0001f);
            lastOffhandWeaponPos = offhandWeaponTransform.position;
            smoothedOffhandVelocity = Vector3.Lerp(smoothedOffhandVelocity, rawOff, 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(0.01f, velocitySmooth)));
            CurrentOffhandSwingSpeed = Mathf.Max(ovrOffSpeed, smoothedOffhandVelocity.magnitude);
        }
        else
        {
            CurrentOffhandSwingSpeed = 0f;
        }
    }

    void RegenerateStamina()
    {
        if (currentStamina < maxStamina)
        {
            currentStamina = Mathf.Min(maxStamina, currentStamina + staminaRegenPerSecond * Time.deltaTime);
            if (CombatUIController.Instance != null)
                CombatUIController.Instance.UpdateStamina(currentStamina, maxStamina);
        }
    }

    public bool TrySpendAttackStamina()
    {
        if (Time.time < lastAttackTime + attackCooldown)
            return currentStamina > 0f;

        lastAttackTime = Time.time;

        if (currentStamina < staminaCostPerHit * 0.25f)
            return false;

        currentStamina = Mathf.Max(0f, currentStamina - staminaCostPerHit);
        if (CombatUIController.Instance != null)
            CombatUIController.Instance.UpdateStamina(currentStamina, maxStamina);
        return true;
    }

    /// <summary>
    /// Velocity-based damage multiplier. 1x at minSwingVelocity, maxVelocityDamageMultiplier at maxSwingVelocity.
    /// </summary>
    public float GetVelocityDamageMultiplier(float swingSpeed)
    {
        float t = Mathf.InverseLerp(minSwingVelocity, maxSwingVelocity, swingSpeed);
        return Mathf.Lerp(1f, maxVelocityDamageMultiplier, t);
    }

    /// <summary>
    /// Stretch-goal angle bonus. Caps at maxAngleBonus when angular speed >= maxAngularSpeed.
    /// </summary>
    public float GetAngleBonusMultiplier(float angularSpeed)
    {
        if (!useAngleMultiplier) return 1f;
        float t = Mathf.InverseLerp(0f, maxAngularSpeed, angularSpeed);
        return 1f + Mathf.Lerp(0f, maxAngleBonus, t);
    }

    public float ApplyFitnessModifiers(float baseDamage, float swingSpeed)
    {
        int taps = 0;
        if (RoundManager.Instance != null)
            taps = RoundManager.Instance.currentTapCount;

        float tapBonus = taps * damagePerTapBonus;
        float speedBonus = swingSpeed * swingSpeedDamageScale;

        // Small weapon flavor multipliers (still Inspector-tunable via WeaponType choice)
        var set = FindSet(primaryWeapon);
        float weaponMul = set != null ? set.damageMultiplier : primaryWeapon switch
        {
            WeaponType.Hammer => 1.15f,
            WeaponType.Sword => 1.0f,
            WeaponType.Shield => 0.6f, // bash
            _ => 0.85f                 // unarmed
        };

        return (baseDamage + tapBonus + speedBonus) * weaponMul;
    }

    public void ResetStamina()
    {
        currentStamina = maxStamina;
        if (CombatUIController.Instance != null)
            CombatUIController.Instance.UpdateStamina(currentStamina, maxStamina);
    }
}
