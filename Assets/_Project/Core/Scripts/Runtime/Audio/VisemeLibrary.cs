using System.Collections.Generic;
using UnityEngine;

namespace PoBox.Audio
{
    /// <summary>
    /// Every voice clip's mouth stream (<see cref="VisemeTrack"/>), by clip (2026-11-25, lip-sync). The procedural
    /// voices register theirs as they are made (VoiceSynth.Make, Make5); a recorded or archetype clip may be
    /// added from its samples at load (<see cref="TryAddFromSamples"/>), where the platform lets its samples be
    /// read. A clip with none is lip-synced from the audio thread's level (<see cref="VisemeTap"/>) or mouthed.
    /// </summary>
    public static class VisemeLibrary
    {
        static readonly Dictionary<AudioClip, VisemeTrack> Tracks = new Dictionary<AudioClip, VisemeTrack>();

        public static int Count => Tracks.Count;

        public static void Register(AudioClip clip, VisemeTrack track)
        {
            if (clip != null && track != null) Tracks[clip] = track;
        }

        public static bool TryGet(AudioClip clip, out VisemeTrack track)
        {
            track = null;
            return clip != null && Tracks.TryGetValue(clip, out track);
        }

        /// <summary>
        /// A track from a clip's loudness alone (no phonemes known). Allocates: call it at load, not mid-fight.
        /// False where the samples cannot be read (WebGL, a streamed or compressed-in-memory clip).
        /// </summary>
        public static bool TryAddFromSamples(AudioClip clip)
        {
            if (clip == null || Tracks.ContainsKey(clip)) return clip != null;
#if UNITY_WEBGL && !UNITY_EDITOR
            return false;
#else
            if (clip.loadType != AudioClipLoadType.DecompressOnLoad || clip.loadState != AudioDataLoadState.Loaded) return false;
            int channels = Mathf.Max(1, clip.channels);
            var raw = new float[clip.samples * channels];
            if (!clip.GetData(raw, 0)) return false;
            float[] mono = raw;
            if (channels > 1)
            {
                mono = new float[clip.samples];
                for (int i = 0; i < mono.Length; i++)
                {
                    float s = 0f;
                    for (int c = 0; c < channels; c++) s += raw[i * channels + c];
                    mono[i] = s / channels;
                }
            }
            int hop = Mathf.Max(1, clip.frequency / 100);   // 10 ms at the clip's own rate
            Tracks[clip] = VisemeTrack.Build(mono, null, hop);
            return true;
#endif
        }

        // Clips do not outlive a play session; with domain reload off the table would.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() => Tracks.Clear();
    }
}
