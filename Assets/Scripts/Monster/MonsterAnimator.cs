// MonsterAnimator.cs
// Purpose: Drive Animator triggers/states for idle, attack, hit, death. Safe without Animator.
// Dependencies: Animator (optional), MonsterController
// Attach to: Monster root. Wire dance.controller for Hip Hop L1.

using UnityEngine;

public class MonsterAnimator : MonoBehaviour
{
    [Header("Animator")]
    public Animator animator;

    [Header("Trigger Names (match your Animator Controller)")]
    public string idleTrigger = "Idle";
    public string attackTrigger = "Attack";
    public string attackAltTrigger = "AttackAlt";
    public string attackHeavyTrigger = "AttackHeavy";
    public string attackSpecialTrigger = "AttackSpecial";
    public string defendTrigger = "Defend";
    public string hitTrigger = "Hit";
    public string deathTrigger = "Death";

    [Header("Bool / State Names (optional alternative)")]
    public string speedFloat = "Speed";
    public bool useTriggers = true;

    [Header("Fallback Visual (no Animator)")]
    public bool pulseOnHit = true;
    public Renderer fallbackRenderer;

    [Header("Debug")]
    [Tooltip("When true, logs which attack trigger was fired. Keep OFF for builds.")]
    public bool verboseAttackLogs = false;

    private MaterialPropertyBlock mpb;
    private Color baseColor = Color.white;
    private MonsterAnimState lastState;

    void Awake()
    {
        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        if (fallbackRenderer == null)
            fallbackRenderer = GetComponentInChildren<Renderer>();

        mpb = new MaterialPropertyBlock();
    }

    public void PlayState(MonsterAnimState state)
    {
        lastState = state;

        if (animator != null && animator.runtimeAnimatorController != null)
        {
            if (useTriggers)
            {
                // Reset other triggers to avoid sticky states
                ResetAllTriggers();

                switch (state)
                {
                    case MonsterAnimState.Idle:
                        SafeSetTrigger(idleTrigger);
                        break;
                    case MonsterAnimState.Attack:
                        SafeSetTrigger(attackTrigger);
                        break;
                    case MonsterAnimState.Hit:
                        SafeSetTrigger(hitTrigger);
                        break;
                    case MonsterAnimState.Death:
                        SafeSetTrigger(deathTrigger);
                        break;
                }
            }
            else
            {
                animator.Play(state.ToString());
            }
        }
        else
        {
            // Cube / placeholder feedback
            HandleFallback(state);
        }
    }

    public void PlayAttackVariant(MonsterAI.AttackPattern pattern)
    {
        if (animator == null || animator.runtimeAnimatorController == null)
        {
            HandleFallback(MonsterAnimState.Attack);
            return;
        }

        ResetAllTriggers();

        string chosen = attackTrigger;
        if (pattern == MonsterAI.AttackPattern.Heavy) chosen = attackHeavyTrigger;
        else if (pattern == MonsterAI.AttackPattern.Special) chosen = attackSpecialTrigger;
        else if (pattern == MonsterAI.AttackPattern.Slash)
        {
            // Only use AttackAlt when BOTH the trigger param AND a real state exist.
            // Level 1 Monster_Easy has AttackAlt param but NO AttackAlt state — selecting it
            // used to consume the trigger with no transition (silent no-op / stuck Idle).
            if (HasTrigger(attackAltTrigger) &&
                animator.HasState(0, Animator.StringToHash(attackAltTrigger)) &&
                Random.value < 0.45f)
            {
                chosen = attackAltTrigger;
            }
        }

        bool ok = TrySetTrigger(chosen);
        if (!ok)
            ok = TrySetTrigger(attackTrigger);

        if (verboseAttackLogs)
            Debug.Log($"[MonsterAnimator] PlayAttackVariant pattern={pattern} trigger={chosen} ok={ok}");
    }

    bool HasTrigger(string name)
    {
        if (string.IsNullOrEmpty(name) || animator == null) return false;
        foreach (var p in animator.parameters)
        {
            if (p.type == AnimatorControllerParameterType.Trigger && p.name == name)
                return true;
        }
        return false;
    }

    public void PlayDefend()
    {
        if (animator == null || animator.runtimeAnimatorController == null) return;
        ResetAllTriggers();
        TrySetTrigger(defendTrigger);
    }

    /// <summary>
    /// Attack-table path (Level 2+): fire one explicit attack trigger. Every trigger in the controller is
    /// reset first so a stale trigger can never fire a second, unintended transition later.
    /// </summary>
    public bool PlayAttackTrigger(string trigger)
    {
        if (animator == null || animator.runtimeAnimatorController == null)
        {
            HandleFallback(MonsterAnimState.Attack);
            return false;
        }
        ResetAllTriggers();
        bool ok = HasTrigger(trigger) && TrySetTrigger(trigger);
        // A table attack whose trigger is missing would still deal damage with no animation: always report it.
        if (!ok)
            Debug.LogWarning($"[MonsterAnimator] Attack trigger '{trigger}' is not a trigger of '{animator.runtimeAnimatorController.name}'; this attack plays no animation.");
        if (verboseAttackLogs)
            Debug.Log($"[MonsterAnimator] PlayAttackTrigger {trigger} ok={ok}");
        return ok;
    }

    /// <summary>Resets every trigger parameter the controller has (not only the named defaults).</summary>
    void ResetAllTriggers()
    {
        if (animator == null) return;
        foreach (var p in animator.parameters)
        {
            if (p.type == AnimatorControllerParameterType.Trigger)
                animator.ResetTrigger(p.name);
        }
    }

    void SafeSetTrigger(string name)
    {
        TrySetTrigger(name);
    }

    bool TrySetTrigger(string name)
    {
        if (string.IsNullOrEmpty(name) || animator == null) return false;
        foreach (var p in animator.parameters)
        {
            if (p.type == AnimatorControllerParameterType.Trigger && p.name == name)
            {
                animator.SetTrigger(name);
                return true;
            }
        }
        // Parameter missing — try cross-fade by state name
        if (animator.HasState(0, Animator.StringToHash(name)))
        {
            animator.CrossFade(name, 0.1f);
            return true;
        }
        return false;
    }

    void SafeResetTrigger(string name)
    {
        if (string.IsNullOrEmpty(name) || animator == null) return;
        foreach (var p in animator.parameters)
        {
            if (p.type == AnimatorControllerParameterType.Trigger && p.name == name)
            {
                animator.ResetTrigger(name);
                return;
            }
        }
    }

    void HandleFallback(MonsterAnimState state)
    {
        if (!pulseOnHit || fallbackRenderer == null) return;

        switch (state)
        {
            case MonsterAnimState.Hit:
                StartCoroutine(Flash(Color.red, 0.2f));
                break;
            case MonsterAnimState.Attack:
                StartCoroutine(Flash(new Color(1f, 0.5f, 0f), 0.15f));
                break;
            case MonsterAnimState.Death:
                StartCoroutine(Flash(Color.black, 0.5f));
                transform.localScale *= 0.85f;
                break;
        }
    }

    System.Collections.IEnumerator Flash(Color c, float duration)
    {
        fallbackRenderer.GetPropertyBlock(mpb);
        mpb.SetColor("_BaseColor", c);
        mpb.SetColor("_Color", c);
        fallbackRenderer.SetPropertyBlock(mpb);
        yield return new WaitForSeconds(duration);
        mpb.SetColor("_BaseColor", baseColor);
        mpb.SetColor("_Color", baseColor);
        fallbackRenderer.SetPropertyBlock(mpb);
    }

    /// <summary>
    /// Hook these from Animation Events on attack clips if you add them later.
    /// </summary>
    public void AnimEvent_AttackHit()
    {
        var c = GetComponent<MonsterController>();
        if (c != null) c.PerformAttackHit();
    }

    public void AnimEvent_AttackEnd()
    {
        var c = GetComponent<MonsterController>();
        if (c != null) c.EndAttack();
    }
}
