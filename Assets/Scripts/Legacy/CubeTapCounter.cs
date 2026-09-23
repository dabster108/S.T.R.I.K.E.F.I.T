using UnityEngine;
#if UNITY_EDITOR
using UnityEngine.InputSystem;
#endif

public class CubeTapCounter : MonoBehaviour
{
    [Tooltip("Minimum time between counted taps, guards against physics jitter double-counting one hit.")]
    public float tapCooldown = 0.05f;

    private float lastTapTime = -1f;

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("PlayerHand")) return;
        if (Time.time - lastTapTime < tapCooldown) return;

        lastTapTime = Time.time;
        RoundManager.Instance.RegisterTap();
    }

#if UNITY_EDITOR
    void Update()
    {
        if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            RoundManager.Instance.RegisterTap();
        }
    }
#endif
}