// BossFinale.cs
// Purpose: Level 3 final-boss kill moment. On the killing blow the world drops into slow motion (the death collapse,
//          the dying glow and the falling axes play slowly) with a deep final-blow boom, then eases back to normal
//          speed. BattleManager.victoryPanelDelay (real seconds) holds the Victory panel until this has played out.
//          VR-safe: only Time.timeScale changes; head and hand tracking never slow down. Physics steps are scaled
//          with it so held weapons (flail chain) stay smooth.
// Dependencies: MonsterController (OnMonsterDeath)
// Attach to: monster root (Level 3 Twin-Axe Brute).

using System.Collections;
using UnityEngine;

public class BossFinale : MonoBehaviour
{
    [Range(0.05f, 1f)] public float slowTimeScale = 0.25f;
    [Tooltip("Real seconds held at slowTimeScale.")]
    public float slowRealSeconds = 1.3f;
    [Tooltip("Real seconds to ease back to normal speed.")]
    public float rampRealSeconds = 0.6f;
    public AudioSource audioSource;
    public AudioClip finalBlowClip;
    [Range(0f, 1f)] public float finalBlowVolume = 1f;

    public bool Played { get; private set; }
    public float LowestTimeScale { get; private set; } = 1f;

    private MonsterController controller;
    private float baseFixedDelta = -1f;
    private bool slowing;

    void Awake()
    {
        controller = GetComponent<MonsterController>();
        if (controller != null) controller.OnMonsterDeath += HandleDeath;
    }

    void OnDestroy()
    {
        if (controller != null) controller.OnMonsterDeath -= HandleDeath;
        Restore();
    }

    void OnDisable() { Restore(); }

    void HandleDeath()
    {
        if (Played) return;
        Played = true;
        if (finalBlowClip != null && audioSource != null) { audioSource.PlayOneShot(finalBlowClip, finalBlowVolume); SfxTrace.Note(this, finalBlowClip, "boss finale: killing blow"); }
        StartCoroutine(SlowMotion());
    }

    IEnumerator SlowMotion()
    {
        slowing = true;
        baseFixedDelta = Time.fixedDeltaTime;
        Set(slowTimeScale);
        yield return new WaitForSecondsRealtime(slowRealSeconds);
        float t = 0f;
        while (t < rampRealSeconds)
        {
            t += Time.unscaledDeltaTime;
            Set(Mathf.Lerp(slowTimeScale, 1f, Mathf.SmoothStep(0f, 1f, t / Mathf.Max(0.01f, rampRealSeconds))));
            yield return null;
        }
        Restore();
    }

    void Set(float scale)
    {
        Time.timeScale = scale;
        Time.fixedDeltaTime = baseFixedDelta * scale;
        LowestTimeScale = Mathf.Min(LowestTimeScale, scale);
    }

    void Restore()
    {
        if (!slowing) return;
        slowing = false;
        Time.timeScale = 1f;
        if (baseFixedDelta > 0f) Time.fixedDeltaTime = baseFixedDelta;
    }
}
