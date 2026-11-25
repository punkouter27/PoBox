using System.Collections.Generic;
using UnityEngine;

namespace PoBox.Audio
{
    /// <summary>
    /// The mouth stream of one voice clip (2026-11-25, lip-sync): for every block of <see cref="HopSamples"/>
    /// samples (10 ms of a <see cref="VoiceSynth"/> take), how open the mouth is (0..1) and how wide (0 rounded,
    /// 1 spread). Made once, when the clip is made, from what the synthesiser knows it said (the vowels' formants,
    /// the consonants, the closures: <see cref="MouthShape"/>) scaled by the take's own loudness in that block, so
    /// a fading vowel closes the mouth with it. Read on the main thread by the clip's playing position
    /// (AudioSource.timeSamples): nothing crosses from the audio thread, nothing is allocated per frame, and it
    /// works on WebGL, where Unity has no audio-thread callback at all.
    /// </summary>
    public sealed class VisemeTrack
    {
        /// <summary>Samples a block at <see cref="VoiceSynth.Rate"/>: 10 ms.</summary>
        public const int DefaultHop = 160;

        readonly byte[] _open, _width;

        public int HopSamples { get; }
        public int Blocks => _open.Length;

        public VisemeTrack(byte[] open, byte[] width, int hopSamples)
        {
            _open = open;
            _width = width;
            HopSamples = Mathf.Max(1, hopSamples);
        }

        /// <summary>The mouth at a sample of the clip, between the two nearest blocks. Past the end it is shut.</summary>
        public void Sample(int sample, out float open, out float width)
        {
            open = 0f;
            width = 0.5f;
            if (_open.Length == 0 || sample < 0) return;
            float at = sample / (float)HopSamples - 0.5f;
            int a = Mathf.FloorToInt(at);
            if (a >= _open.Length) return;
            float u = at - a;
            int i0 = Mathf.Clamp(a, 0, _open.Length - 1), i1 = Mathf.Clamp(a + 1, 0, _open.Length - 1);
            open = Mathf.Lerp(_open[i0], _open[i1], u) / 255f;
            width = Mathf.Lerp(_width[i0], _width[i1], u) / 255f;
        }

        /// <summary>The block's openness, 0..1 (for tests and the debug readout).</summary>
        public float OpenAt(int block) => block >= 0 && block < _open.Length ? _open[block] / 255f : 0f;

        /// <summary>
        /// From a finished take and the shapes it was said with (their lengths summing to the take's; any shortfall
        /// is read as shut). <paramref name="shapes"/> null: loudness alone, with a middling vowel.
        /// </summary>
        public static VisemeTrack Build(float[] data, List<MouthShape> shapes, int hop = DefaultHop)
        {
            int n = data != null ? data.Length : 0;
            int blocks = Mathf.Max(1, (n + hop - 1) / hop);
            var rms = new float[blocks];
            float top = 1e-5f;
            for (int b = 0; b < blocks; b++)
            {
                int s0 = b * hop, s1 = Mathf.Min(n, s0 + hop);
                double sum = 0;
                for (int i = s0; i < s1; i++) sum += data[i] * data[i];
                rms[b] = s1 > s0 ? Mathf.Sqrt((float)(sum / (s1 - s0))) : 0f;
                top = Mathf.Max(top, rms[b]);
            }

            var open = new byte[blocks];
            var width = new byte[blocks];
            int seg = 0, segStart = 0;
            for (int b = 0; b < blocks; b++)
            {
                int centre = b * hop + hop / 2;
                float a = 0.7f, w = 0.5f;
                if (shapes != null)
                {
                    while (seg < shapes.Count && centre >= segStart + shapes[seg].samples) { segStart += shapes[seg].samples; seg++; }
                    if (seg < shapes.Count)
                    {
                        MouthShape m = shapes[seg];
                        float u = m.samples > 0 ? Mathf.Clamp01((centre - segStart) / (float)m.samples) : 0f;
                        if (m.reach > 0f && m.reach < 1f) u = Mathf.Clamp01(u / m.reach);
                        a = Mathf.Lerp(m.open0, m.open1, u);
                        w = Mathf.Lerp(m.width0, m.width1, u);
                    }
                    else a = 0f;
                }
                // Loudness against the take's loudest block, square-rooted (the jaw is not a meter), gated near silence.
                float loud = rms[b] / top;
                float o = loud < 0.04f ? 0f : a * Mathf.Sqrt(loud);
                open[b] = (byte)Mathf.RoundToInt(Mathf.Clamp01(o) * 255f);
                width[b] = (byte)Mathf.RoundToInt(Mathf.Clamp01(w) * 255f);
            }
            return new VisemeTrack(open, width, hop);
        }
    }
}
