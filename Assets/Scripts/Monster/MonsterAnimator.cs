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

    /// <summary>A variant of the Hit trigger for the next PlayState(Hit) (MonsterCosmetics: HitLeft / HitRight / HitHeavy). Consumed
    /// once; ignored when the controller has no such trigger, so Levels 2 and 3 always play the plain Hit.</summary>
    [System.NonSerialized] public string NextHitTrigger;

    private MaterialPropertyBlock mpb;

    // Trigger and bool parameters of the current controller, as name hashes. animator.parameters builds a new array of new
    // objects on every call, and it used to be read on every trigger set and reset (several times per attack, hit and guard);
    // now it is read once per controller. An empty read (Animator not initialised yet) is not cached, so it is retried.
    private RuntimeAnimatorController paramController;
    private Animator paramAnimator;
    private int[] triggerHashes = new int[0];
    private int triggerCount;
    private readonly System.Collections.Generic.HashSet<int> triggerSet = new System.Collections.Generic.HashSet<int>();
    private readonly System.Collections.Generic.HashSet<int> boolSet = new System.Collections.Generic.HashSet<int>();

    void EnsureParamCache()
    {
        var rc = animator != null ? animator.runtimeAnimatorController : null;
        if (rc == paramController && animator == paramAnimator) return;
        triggerSet.Clear(); boolSet.Clear(); triggerCount = 0;
        paramController = null; paramAnimator = null;
        if (rc == null) return;
        var ps = animator.parameters;
        if (ps.Length == 0) return;
        if (triggerHashes.Length < ps.Length) triggerHashes = new int[ps.Length];
        foreach (var p in ps)
        {
            if (p.type == AnimatorControllerParameterType.Trigger) { if (triggerSet.Add(p.nameHash)) triggerHashes[triggerCount++] = p.nameHash; }
            else if (p.type == AnimatorControllerParameterType.Bool) boolSet.Add(p.nameHash);
        }
        paramController = rc; paramAnimator = animator;
    }
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
                        if (!string.IsNullOrEmpty(NextHitTrigger) && HasTrigger(NextHitTrigger)) SafeSetTrigger(NextHitTrigger);
                        else SafeSetTrigger(hitTrigger);
                        NextHitTrigger = null;
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
            DevLog.Log($"[MonsterAnimator] PlayAttackVariant pattern={pattern} trigger={chosen} ok={ok}");
    }

    /// <summary>Does the current controller have this trigger parameter? Cached per controller (no allocation).</summary>
    public bool HasTrigger(string name)
    {
        if (string.IsNullOrEmpty(name) || animator == null) return false;
        EnsureParamCache();
        return triggerSet.Contains(Animator.StringToHash(name));
    }

    public void PlayDefend()
    {
        PlayDefend(null);
    }

    /// <summary>Guard with a specific trigger (high / side guard); empty = defendTrigger.</summary>
    public void PlayDefend(string trigger)
    {
        if (animator == null || animator.runtimeAnimatorController == null) return;
        ResetAllTriggers();
        TrySetTrigger(string.IsNullOrEmpty(trigger) ? defendTrigger : trigger);
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
            DevLog.Log($"[MonsterAnimator] PlayAttackTrigger {trigger} ok={ok}");
        return ok;
    }

    /// <summary>Fire one named trigger (entrance Rise / Roar, enrage roar) after clearing every other trigger.</summary>
    public bool PlayNamedTrigger(string trigger)
    {
        if (animator == null || animator.runtimeAnimatorController == null) return false;
        ResetAllTriggers();
        return HasTrigger(trigger) && TrySetTrigger(trigger);
    }

    public void SetBoolIfPresent(string name, bool value)
    {
        if (animator == null || animator.runtimeAnimatorController == null || string.IsNullOrEmpty(name)) return;
        EnsureParamCache();
        int h = Animator.StringToHash(name);
        if (boolSet.Contains(h)) animator.SetBool(h, value);
    }

    /// <summary>Resets every trigger parameter the controller has (not only the named defaults).</summary>
    void ResetAllTriggers()
    {
        if (animator == null) return;
        EnsureParamCache();
        for (int i = 0; i < triggerCount; i++) animator.ResetTrigger(triggerHashes[i]);
    }

    void SafeSetTrigger(string name)
    {
        TrySetTrigger(name);
    }

    bool TrySetTrigger(string name)
    {
        if (string.IsNullOrEmpty(name) || animator == null) return false;
        EnsureParamCache();
        int h = Animator.StringToHash(name);
        if (triggerSet.Contains(h))
        {
            animator.SetTrigger(h);
            return true;
        }
        // Parameter missing — try cross-fade by state name
        if (animator.HasState(0, h))
        {
            animator.CrossFade(name, 0.1f);
            return true;
        }
        return false;
    }

    void SafeResetTrigger(string name)
    {
        if (string.IsNullOrEmpty(name) || animator == null) return;
        EnsureParamCache();
        int h = Animator.StringToHash(name);
        if (triggerSet.Contains(h)) animator.ResetTrigger(h);
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
        var colors = GetComponent<MonsterColorCompositor>();
        if (colors != null) { colors.Cue(c, duration); yield break; }   // the compositor owns the body's property blocks
        fallbackRenderer.GetPropertyBlock(mpb);
        mpb.SetColor(ShaderIds.BaseColor, c);
        mpb.SetColor(ShaderIds.Color, c);
        fallbackRenderer.SetPropertyBlock(mpb);
        yield return new WaitForSeconds(duration);
        mpb.SetColor(ShaderIds.BaseColor, baseColor);
        mpb.SetColor(ShaderIds.Color, baseColor);
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
