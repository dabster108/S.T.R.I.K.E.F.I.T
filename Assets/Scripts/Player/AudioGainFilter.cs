// AudioGainFilter.cs
// Purpose: Fixed gain on one AudioSource's output (OnAudioFilterRead), for clips that were recorded far too quietly:
//          AudioSource.volume can only turn a clip DOWN, so breathingfast.mp3 (peak 0.05, about 27 dB below the heavy-breathing
//          clip) could never reach a useful level without this. Hard-limited to +-1 so a boost can never clip into the mixer.
// Attach to: the GameObject that has the AudioSource (PlayerExertionAudio adds it to its fast-breathing source).
// Cost: one multiply per sample on the audio thread, only while the source is playing; no allocation.

using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public class AudioGainFilter : MonoBehaviour
{
    [Tooltip("Linear gain (18 = +25 dB).")]
    public float gain = 1f;

    void OnAudioFilterRead(float[] data, int channels)
    {
        float g = gain;
        if (g == 1f) return;
        for (int i = 0; i < data.Length; i++)
        {
            float x = data[i] * g;
            data[i] = x > 1f ? 1f : (x < -1f ? -1f : x);
        }
    }
}
