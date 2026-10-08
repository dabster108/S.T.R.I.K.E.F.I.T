// MonsterEntrance.cs
// Purpose: Staged boss entrance (Level 3). When the monster spawns: a summoning circle glows on the ground at the
//          spawn point, the monster rises from below the floor through it (Y driven by a coroutine over exactly the
//          Rise clip's length, eased like a heavy body standing up), then roars (animation + sound together) and holds
//          a beat; the circle fades out as the roar starts (fully risen). Until the sequence finishes the monster cannot attack, be damaged or start
//          its phase logic, so the moment lands before the fight begins.
// Dependencies: MonsterAI, MonsterController, MonsterAnimator, GroundRuneVFX, BossPhaseController (optional)
// Attach to: monster root. riseOffset = child between the root and the animated body.

using System;
using System.Collections;
using UnityEngine;

public class MonsterEntrance : MonoBehaviour
{
    [Header("Rig")]
    [Tooltip("Child that is moved down/up for the rise (the Animator animates the body below it).")]
    public Transform riseOffset;
    [Tooltip("How far below the floor the monster starts (m). Taller than the monster so nothing pokes through.")]
    public float startDepth = 2.6f;

    [Header("Animator")]
    public string riseTrigger = "Rise";
    public string riseStateClip = "Exec_Rise";
    public string roarTrigger = "Roar";
    public string roarStateClip = "Exec_Roar";
    [Tooltip("Used if the Rise clip is not found on the controller.")]
    public float fallbackRiseSeconds = 3f;
    [Tooltip("Normalised Y over the rise (0 = start depth, 1 = floor). Ease-out: fast lunge up, heavy settle.")]
    public AnimationCurve riseCurve = new AnimationCurve(new Keyframe(0f, 0f, 0f, 0.4f), new Keyframe(0.55f, 0.82f, 1.2f, 1.2f), new Keyframe(1f, 1f, 0.15f, 0f));

    [Header("Summoning circle")]
    public GroundRuneVFX summoningCircle;
    public float circleFadeIn = 0.8f;
    [Tooltip("Circle charges this long before the monster starts to rise.")]
    public float preRiseDelay = 1.4f;
    public float circleFadeOut = 1.2f;

    [Header("Roar")]
    [Tooltip("Hold after the roar starts before the fight begins (the roar must be heard and seen).")]
    public float roarHoldSeconds = 2.2f;

    [Header("Audio")]
    public AudioSource audioSource;
    public AudioClip summonClip;
    [Range(0f, 1f)] public float summonVolume = 0.9f;
    public AudioClip riseClip;
    [Range(0f, 1f)] public float riseVolume = 1f;
    public AudioClip roarClip;
    [Range(0f, 1f)] public float roarVolume = 1f;

    [Header("Runtime (read-only)")]
    [SerializeField] private bool inProgress;
    [SerializeField] private bool complete;
    public bool InProgress => inProgress;
    public bool Complete => complete;
    public float RiseSeconds { get; private set; }

    public event Action EntranceComplete;

    private MonsterAI ai;
    private MonsterAnimator anim;
    private Collider[] colliders;

    void Awake()
    {
        ai = GetComponent<MonsterAI>();
        anim = GetComponent<MonsterAnimator>();
        if (audioSource == null) audioSource = GetComponent<AudioSource>();
        // Start hidden under the floor and inert from the very first frame.
        if (riseOffset != null) riseOffset.localPosition = new Vector3(0f, -startDepth, 0f);
        if (ai != null) ai.enabled = false;
    }

    void Start()
    {
        StartCoroutine(Sequence());
    }

    IEnumerator Sequence()
    {
        inProgress = true;
        colliders = GetComponentsInChildren<Collider>(true);
        foreach (var c in colliders) if (c != null) c.enabled = false;

        if (summoningCircle != null)
        {
            summoningCircle.transform.SetParent(null, true);
            summoningCircle.transform.position = new Vector3(transform.position.x, transform.position.y + 0.02f, transform.position.z);
            summoningCircle.Show(circleFadeIn);
        }
        if (summonClip != null && audioSource != null) { audioSource.PlayOneShot(summonClip, summonVolume); SfxTrace.Note(this, summonClip, "entrance: summon"); }

        yield return new WaitForSeconds(preRiseDelay);

        // --- Rise: Y follows the Rise clip's real length ---
        RiseSeconds = ClipLength(riseStateClip, fallbackRiseSeconds);
        if (anim != null) anim.PlayNamedTrigger(riseTrigger);
        if (riseClip != null && audioSource != null) { audioSource.PlayOneShot(riseClip, riseVolume); SfxTrace.Note(this, riseClip, "entrance: rise"); }
        float t = 0f;
        while (t < RiseSeconds)
        {
            t += Time.deltaTime;
            float k = riseCurve.Evaluate(Mathf.Clamp01(t / RiseSeconds));
            if (riseOffset != null) riseOffset.localPosition = new Vector3(0f, Mathf.Lerp(-startDepth, 0f, k), 0f);
            yield return null;
        }
        if (riseOffset != null) riseOffset.localPosition = Vector3.zero;

        // --- Roar: animation and sound start on the same frame, then a held beat. The circle has done its job once
        //     the monster is fully risen: it fades out during the roar. ---
        if (anim != null) anim.PlayNamedTrigger(roarTrigger);
        if (roarClip != null && audioSource != null) { audioSource.PlayOneShot(roarClip, roarVolume); SfxTrace.Note(this, roarClip, "entrance: roar"); }
        if (summoningCircle != null) summoningCircle.Hide(circleFadeOut);
        float roarLen = ClipLength(roarStateClip, roarHoldSeconds);
        yield return new WaitForSeconds(Mathf.Max(roarHoldSeconds, roarLen));

        foreach (var c in colliders) if (c != null) c.enabled = true;
        if (ai != null) ai.enabled = true;
        inProgress = false;
        complete = true;
        EntranceComplete?.Invoke();
    }

    float ClipLength(string clipName, float fallback)
    {
        var a = anim != null ? anim.animator : GetComponentInChildren<Animator>();
        if (a == null || a.runtimeAnimatorController == null) return fallback;
        foreach (var clip in a.runtimeAnimatorController.animationClips)
            if (clip != null && clip.name == clipName) return clip.length;
        return fallback;
    }

    void OnDestroy()
    {
        if (summoningCircle != null && summoningCircle.transform.parent == null)
            Destroy(summoningCircle.gameObject);
    }
}
