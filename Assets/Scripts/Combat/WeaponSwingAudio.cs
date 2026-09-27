// WeaponSwingAudio.cs
// Purpose: Whoosh when a held weapon is swung fast — every committed swing is audible, hit or miss.
// Dependencies: PlayerCombat (per-hand swing speed), BattleManager (only during battle), AudioSource
// Attach to: Player weapon GameObject (e.g. PlayerSword / PlayerSword_Left), next to HitDetection

using UnityEngine;

public class WeaponSwingAudio : MonoBehaviour
{
    [Tooltip("Which controller's speed drives this weapon (Right = primary, Left = offhand).")]
    public Hand hand = Hand.Right;
    public AudioSource audioSource;
    [Tooltip("One is picked at random per swing.")]
    public AudioClip[] swingClips;
    [Range(0f, 1f)] public float volume = 0.6f;

    [Header("Speed thresholds (m/s)")]
    [Tooltip("Swing speed that fires a whoosh.")]
    public float triggerSpeed = 2.2f;
    [Tooltip("Hand must slow below this before the next whoosh can fire (one whoosh per swing).")]
    public float rearmSpeed = 1.2f;
    [Tooltip("Speed at which the whoosh reaches full volume.")]
    public float fullSpeed = 6f;
    public float minInterval = 0.3f;

    private bool armed = true;
    private float lastTime = -999f;

    void Start()
    {
        if (audioSource == null)
            audioSource = GetComponent<AudioSource>();
    }

    void Update()
    {
        if (PlayerCombat.Instance == null || audioSource == null || swingClips == null || swingClips.Length == 0) return;
        if (BattleManager.Instance != null && !BattleManager.Instance.IsBattleActive) return;

        float speed = hand == Hand.Left
            ? PlayerCombat.Instance.CurrentOffhandSwingSpeed
            : PlayerCombat.Instance.CurrentSwingSpeed;

        if (!armed)
        {
            if (speed < rearmSpeed) armed = true;
            return;
        }

        if (speed >= triggerSpeed && Time.time - lastTime >= minInterval)
        {
            armed = false;
            lastTime = Time.time;
            var clip = swingClips[Random.Range(0, swingClips.Length)];
            float t = Mathf.InverseLerp(triggerSpeed, fullSpeed, speed);
            if (clip != null)
                audioSource.PlayOneShot(clip, volume * Mathf.Lerp(0.55f, 1f, t));
        }
    }
}
