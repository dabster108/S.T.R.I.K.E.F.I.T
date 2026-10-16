// PlayerHealth.cs
// Purpose: Player HP, block-aware damage routing, hit reaction, defeat condition.
// Dependencies: CombatUIController, BattleManager, PlayerCombat (block), XPManager (block XP)
// Attach to: Player / XR Rig (OVRCameraRig)

using System;
using UnityEngine;

public class PlayerHealth : MonoBehaviour
{
    public static PlayerHealth Instance;

    [Header("Health")]
    public float maxHealth = 100f;
    public float currentHealth = 100f;

    [Header("Hit Reaction (first-person)")]
    [Tooltip("Red screen flash image (Canvas Image) shown briefly when taking unblocked damage.")]
    public UnityEngine.UI.Image hitFlashImage;
    public float hitFlashDuration = 0.25f;
    [Tooltip("Set by PlayerDamageVignette while it draws the hit flash as a screen-edge vignette; this full-rect flash then stays off.")]
    [HideInInspector] public bool flashReplacedByVignette;
    [Tooltip("Optional camera shake transform.")]
    public Transform cameraShakeTarget;
    public float hitShakeMagnitude = 0.08f;
    public float hitShakeDuration = 0.2f;

    [Header("Audio")]
    public AudioSource audioSource;
    public AudioClip hitSound;
    [Tooltip("Optional vocal or secondary hurt cue played when taking damage.")]
    public AudioClip hurtVocalSound;
    [Tooltip("Played once when player reaches 0 HP.")]
    public AudioClip defeatSound;
    [Range(0f, 1f)] public float hitSoundVolume = 1f;
    [Range(0f, 1f)] public float hurtVocalVolume = 0.8f;
    [Range(0f, 1f)] public float defeatSoundVolume = 1f;
    public AudioClip blockSound;

    public bool IsDead => currentHealth <= 0f;

    public event Action<float, float> OnHealthChanged;
    public event Action OnDeath;

    /// <summary>Telemetry hook (SessionReporter). Args: damage taken after block, was blocking, HP after.</summary>
    public static event Action<float, bool, float> DamageTaken;

    void Awake()
    {
        Instance = this;
        currentHealth = maxHealth;
    }

    public void SetMaxHealth(float max, bool fill)
    {
        maxHealth = Mathf.Max(1f, max);
        if (fill) currentHealth = maxHealth;
        else currentHealth = Mathf.Min(currentHealth, maxHealth);
        Notify();
    }

    /// <summary>True if the last TakeDamage call was an unblockable attack (verification / debug).</summary>
    public bool LastHitWasUnblockable { get; private set; }

    public void TakeDamage(float amount)
    {
        TakeDamage(amount, false);
    }

    /// <param name="unblockable">Dodge-only attacks (Level 3 slam): skip every block reduction, take the full amount.</param>
    public void TakeDamage(float amount, bool unblockable)
    {
        if (IsDead || GamePause.IsPaused) return;   // a paused fight deals no damage, whatever called this
        if (BattleManager.Instance != null && !BattleManager.Instance.IsBattleActive)
            return;

        LastHitWasUnblockable = unblockable;
        bool wasBlocking = !unblockable && PlayerCombat.Instance != null && PlayerCombat.Instance.IsBlocking;

        // Shield block: reduce damage (functional mechanic). Unblockable attacks bypass it entirely.
        if (PlayerCombat.Instance != null && !unblockable)
            amount = PlayerCombat.Instance.ModifyIncomingDamage(amount);

        bool fullyBlocked = wasBlocking && amount <= 0.001f;
        bool partiallyBlocked = wasBlocking && amount > 0.001f;

        if (fullyBlocked)
        {
            // Successful block — reward defensive play with XP + feedback
            if (audioSource != null && blockSound != null) { audioSource.PlayOneShot(blockSound); SfxTrace.Note(this, blockSound, "player blocked a monster hit"); }
            if (XPManager.Instance != null) XPManager.Instance.AwardBlockXP();
            DamageTaken?.Invoke(0f, true, currentHealth);
            return; // no HP loss
        }

        currentHealth = Mathf.Max(0f, currentHealth - Mathf.Max(0f, amount));
        Notify();
        DamageTaken?.Invoke(Mathf.Max(0f, amount), partiallyBlocked, currentHealth);

        // Hit reaction (first-person): flash + shake + sound
        PlayHitReaction();

        if (CombatUIController.Instance != null)
            CombatUIController.Instance.ShowDamageNumber(
                transform.position + Vector3.up * 1.5f,
                amount,
                partiallyBlocked ? new Color(0.5f, 0.8f, 1f) : Color.yellow,
                countAsDealt: false);

        if (partiallyBlocked && XPManager.Instance != null)
            XPManager.Instance.AwardBlockXP();

        if (IsDead)
        {
            if (audioSource != null && defeatSound != null)
            {
                audioSource.PlayOneShot(defeatSound, defeatSoundVolume);
                SfxTrace.Note(this, defeatSound, "player defeated");
            }
            OnDeath?.Invoke();
            if (BattleManager.Instance != null)
                BattleManager.Instance.NotifyPlayerDefeated();
        }
    }

    void PlayHitReaction()
    {
        if (hitFlashImage != null && !flashReplacedByVignette)
            StartCoroutine(FlashRoutine());
        if (cameraShakeTarget != null)
            StartCoroutine(ShakeRoutine());
        if (audioSource != null && hitSound != null)
        {
            audioSource.PlayOneShot(hitSound, hitSoundVolume);
            SfxTrace.Note(this, hitSound, "player took damage");
        }
        if (audioSource != null && hurtVocalSound != null)
        {
            audioSource.PlayOneShot(hurtVocalSound, hurtVocalVolume);
            SfxTrace.Note(this, hurtVocalSound, "player took damage (vocal)");
        }
    }

    System.Collections.IEnumerator FlashRoutine()
    {
        // Always start from invisible and end invisible — prevents overlapping coroutines
        // from capturing a mid-flash alpha and leaving a permanent red tint on screen.
        Color c = hitFlashImage.color;
        c.a = 0f;
        hitFlashImage.color = c;
        float t = 0f;
        while (t < hitFlashDuration)
        {
            c.a = Mathf.Lerp(0.6f, 0f, t / hitFlashDuration);
            hitFlashImage.color = c;
            t += Time.deltaTime;
            yield return null;
        }
        c.a = 0f;
        hitFlashImage.color = c;
    }

    System.Collections.IEnumerator ShakeRoutine()
    {
        Vector3 orig = cameraShakeTarget.localPosition;
        float t = 0f;
        while (t < hitShakeDuration)
        {
            Vector3 offset = UnityEngine.Random.insideUnitSphere * hitShakeMagnitude;
            cameraShakeTarget.localPosition = orig + offset;
            t += Time.deltaTime;
            yield return null;
        }
        cameraShakeTarget.localPosition = orig;
    }

    public void Heal(float amount)
    {
        if (IsDead) return;
        currentHealth = Mathf.Min(maxHealth, currentHealth + amount);
        Notify();
    }

    public void FullHeal()
    {
        currentHealth = maxHealth;
        Notify();
    }

    void Notify()
    {
        OnHealthChanged?.Invoke(currentHealth, maxHealth);
        if (CombatUIController.Instance != null)
            CombatUIController.Instance.UpdatePlayerHealth(currentHealth, maxHealth);
    }
}
