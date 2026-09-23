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
    [Tooltip("Optional secondary attack trigger for attack variety. If missing, falls back to Attack.")]
    public string attackAltTrigger = "AttackAlt";
    public string hitTrigger = "Hit";
    public string deathTrigger = "Death";

    [Header("Bool / State Names (optional alternative)")]
    public string speedFloat = "Speed";
    public bool useTriggers = true;

    [Header("Fallback Visual (no Animator)")]
    public bool pulseOnHit = true;
    public Renderer fallbackRenderer;

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
                SafeResetTrigger(idleTrigger);
                SafeResetTrigger(attackTrigger);
                SafeResetTrigger(hitTrigger);
                SafeResetTrigger(deathTrigger);

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

    /// <summary>
    /// Plays one of multiple attack trigger variants.
    /// variantIndex 0 => attackTrigger, 1 => attackAltTrigger (fallback to attackTrigger if missing).
    /// </summary>
    public void PlayAttackVariant(int variantIndex)
    {
        if (animator == null || animator.runtimeAnimatorController == null)
        {
            HandleFallback(MonsterAnimState.Attack);
            return;
        }

        SafeResetTrigger(idleTrigger);
        SafeResetTrigger(attackTrigger);
        SafeResetTrigger(attackAltTrigger);
        SafeResetTrigger(hitTrigger);
        SafeResetTrigger(deathTrigger);

        string chosen = variantIndex == 1 ? attackAltTrigger : attackTrigger;
        if (!TrySetTrigger(chosen))
            TrySetTrigger(attackTrigger);
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
