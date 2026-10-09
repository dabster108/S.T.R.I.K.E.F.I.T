// AudioGainFilter.cs
// Purpose: Fixed gain on one AudioSource's output (OnAudioFilterRead), for clips that were recorded far too quietly:
//          AudioSource.volume can only turn a clip DOWN. Hard-limited to +-1 so a boost can never clip into the mixer.
//          No longer needed by the breathing (its quiet clip is now levelled in the asset, breathingfast_leveled.wav, so the
//          headset does not depend on a script DSP callback); kept for other clips and for the breathing self-test, which
//          counts the callbacks to prove whether OnAudioFilterRead runs on the device.
// Attach to: the GameObject that has the AudioSource.
// Cost: one multiply per sample on the audio thread, only while the source is playing; no allocation.

using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public class AudioGainFilter : MonoBehaviour
{
    [Tooltip("Linear gain (18 = +25 dB).")]
    public float gain = 1f;

    /// <summary>Audio-thread callbacks so far (verification: 0 means the DSP callback never ran).</summary>
    public int Calls => calls;
    /// <summary>Largest output sample since the last ResetPeak (after gain and limit).</summary>
    public float PeakOut => peakOut;

    private volatile int calls;
    private volatile float peakOut;

    public void ResetPeak() { peakOut = 0f; }

    void OnAudioFilterRead(float[] data, int channels)
    {
        calls++;
        float g = gain, pk = peakOut;
        for (int i = 0; i < data.Length; i++)
        {
            float x = data[i] * g;
            x = x > 1f ? 1f : (x < -1f ? -1f : x);
            data[i] = x;
            float a = x < 0f ? -x : x;
            if (a > pk) pk = a;
        }
        peakOut = pk;
    }
}
