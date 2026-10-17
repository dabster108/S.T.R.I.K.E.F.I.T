// MonsterHealth.cs
// Purpose: Monster HP, damage events, death flag, UI hooks.
// Dependencies: CombatUIController (optional), MonsterController
// Attach to: Same GameObject as MonsterController

using System;
using UnityEngine;

public class MonsterHealth : MonoBehaviour
{
    [Header("Health")]
    public float maxHealth = 100f;
    public float currentHealth = 100f;

    [Header("Feedback")]
    public bool spawnDamageNumbers = true;
    public Color damageNumberColor = Color.red;
    public AudioSource deathAudioSource;
    public AudioClip deathSound;
    [Range(0f, 1f)] public float deathSoundVolume = 1f;

    public bool IsDead => currentHealth <= 0f;

    /// <summary>Args: current, max</summary>
    public event Action<float, float> OnHealthChanged;

    /// <summary>Args: damage amount, world hit point</summary>
    public event Action<float, Vector3> OnDamaged;

    public event Action OnDeath;

    void Awake()
    {
        currentHealth = maxHealth;
    }

    public void SetMaxHealth(float max, bool fill)
    {
        maxHealth = Mathf.Max(1f, max);
        if (fill)
            currentHealth = maxHealth;
        else
            currentHealth = Mathf.Min(currentHealth, maxHealth);

        OnHealthChanged?.Invoke(currentHealth, maxHealth);
        PushUI();
    }

    public void TakeDamage(float amount, Vector3 hitPoint)
    {
        TakeDamage(amount, hitPoint, CombatUIController.DamageStyle.Default);
    }

    /// <param name="style">Damage-number look (swing tier, blocked, resisted...).</param>
    public void TakeDamage(float amount, Vector3 hitPoint, CombatUIController.DamageStyle style)
    {
        if (IsDead || GamePause.IsPaused) return;   // a paused fight deals no damage, whatever called this

        amount = Mathf.Max(0f, amount);
        currentHealth = Mathf.Max(0f, currentHealth - amount);

        OnDamaged?.Invoke(amount, hitPoint);
        OnHealthChanged?.Invoke(currentHealth, maxHealth);
        PushUI();

        if (spawnDamageNumbers && CombatUIController.Instance != null)
            CombatUIController.Instance.ShowDamageNumber(hitPoint, amount, damageNumberColor, true, style);

        if (IsDead)
        {
            if (deathAudioSource != null && deathSound != null)
            {
                deathAudioSource.PlayOneShot(deathSound, deathSoundVolume);
                SfxTrace.Note(this, deathSound, "monster death");
            }
            OnDeath?.Invoke();
        }
    }

    public void Heal(float amount)
    {
        if (IsDead) return;
        currentHealth = Mathf.Min(maxHealth, currentHealth + Mathf.Max(0f, amount));
        OnHealthChanged?.Invoke(currentHealth, maxHealth);
        PushUI();
    }

    void PushUI()
    {
        if (CombatUIController.Instance != null)
            CombatUIController.Instance.UpdateMonsterHealth(currentHealth, maxHealth);
    }

    public float Normalized => maxHealth > 0f ? currentHealth / maxHealth : 0f;
}
