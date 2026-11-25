using System.Threading;
using UnityEngine;

namespace PoBox.Audio
{
    /// <summary>
    /// The audio thread's side of the lip-sync (2026-11-25): on the speaking AudioSource's GameObject, it measures
    /// each block of samples the mixer pulls through the source (OnAudioFilterRead, on the audio thread) and hands
    /// its level to the main thread lock-free: one int written with <see cref="Volatile.Write(ref int, int)"/>
    /// (the level in 1/65535ths) and a block counter bumped with <see cref="Interlocked.Increment(ref int)"/>, read
    /// back with <see cref="Volatile.Read(ref int)"/>. No locks, no allocations, no Unity API off the main thread.
    ///
    /// Used for a clip with no <see cref="VisemeTrack"/> (a recording). Unity calls no filter on WebGL, so there
    /// the counter never moves and <see cref="LipSync"/> mouths the clip instead.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class VisemeTap : MonoBehaviour
    {
        int _level, _blocks;

        /// <summary>The last block's RMS level, 0..1, as the mixer pulled it.</summary>
        public float Level => Volatile.Read(ref _level) / 65535f;

        /// <summary>Blocks measured so far; a main-thread reader sees it move while the tap is live.</summary>
        public int Blocks => Volatile.Read(ref _blocks);

        // Audio thread. Pass-through: the samples are only read.
        void OnAudioFilterRead(float[] data, int channels)
        {
            int n = data.Length;
            if (n == 0) return;
            double sum = 0;
            for (int i = 0; i < n; i++) sum += data[i] * data[i];
            double rms = System.Math.Sqrt(sum / n);
            int fixedPoint = (int)(System.Math.Min(1.0, rms) * 65535.0);
            Volatile.Write(ref _level, fixedPoint);
            Interlocked.Increment(ref _blocks);
        }
    }
}
