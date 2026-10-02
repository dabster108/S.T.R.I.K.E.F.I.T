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
    [Tooltip("Weapon turning this far (deg) from its direction at the last whoosh re-arms it.")]
    public float reversalAngle = 150f;

    private bool armed = true;
    private float lastTime = -999f;
    private Vector3 lastPos, dir, firedDir;

    void Start()
    {
        if (audioSource == null)
            audioSource = GetComponent<AudioSource>();
        lastPos = transform.position;
    }

    void Update()
    {
        if (PlayerCombat.Instance == null || audioSource == null || swingClips == null || swingClips.Length == 0) return;
        if (BattleManager.Instance != null && !BattleManager.Instance.IsBattleActive) return;

        float speed = hand == Hand.Left
            ? PlayerCombat.Instance.CurrentOffhandSwingSpeed
            : PlayerCombat.Instance.CurrentSwingSpeed;

        // Same re-arm rule as PlayerExertionAudio: slowing down OR turning back re-arms, so fluid back-and-forth
        // slashing (which never drops below rearmSpeed) still whooshes on every swing.
        Vector3 vel = (transform.position - lastPos) / Mathf.Max(Time.deltaTime, 0.0001f);
        lastPos = transform.position;
        dir = Vector3.Lerp(dir, vel, 1f - Mathf.Exp(-Time.deltaTime / 0.05f));
        if (!armed)
        {
            bool turned = Time.time - lastTime >= minInterval && dir.sqrMagnitude > 0.01f && firedDir.sqrMagnitude > 0f &&
                          Vector3.Angle(dir, firedDir) >= reversalAngle;
            if (speed < rearmSpeed || turned) armed = true;
            else return;
        }

        if (speed >= triggerSpeed && Time.time - lastTime >= minInterval)
        {
            armed = false;
            lastTime = Time.time;
            firedDir = dir;
            var clip = swingClips[Random.Range(0, swingClips.Length)];
            float t = Mathf.InverseLerp(triggerSpeed, fullSpeed, speed);
            if (clip != null)
                audioSource.PlayOneShot(clip, volume * Mathf.Lerp(0.55f, 1f, t));
        }
    }
}
