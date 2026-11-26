using UnityEngine;

namespace PoBox.Audio
{
    /// <summary>
    /// What the mouth does over one stretch of a synthesised voice (2026-11-25, lip-sync): how open it is (0 shut,
    /// 1 an "ah") and how wide (0 rounded as for "oo", 1 spread as for "ee"), each gliding from the start of the
    /// stretch to its end. <see cref="VoiceSynth"/> writes one for every piece it joins into a take (a vowel, a
    /// fricative, a plosive's closure, a gap), so the pieces' lengths add up to the take's; <see cref="VisemeTrack"/>
    /// turns them, with the take's own loudness, into the mouth stream.
    /// </summary>
    public struct MouthShape
    {
        public int samples;
        public float open0, open1, width0, width1;
        /// <summary>Share of the stretch (0..1] by which the end shape is reached and then held; 0 means the whole stretch.</summary>
        public float reach;

        public MouthShape(int samples, float open0, float open1, float width0, float width1)
        {
            this.samples = samples;
            this.open0 = open0; this.open1 = open1; this.width0 = width0; this.width1 = width1;
            reach = 0f;
        }

        /// <summary>Lips together: a gap, or a stop's closure.</summary>
        public static MouthShape Closed(int samples) => new MouthShape(samples, 0f, 0f, 0.5f, 0.5f);

        /// <summary>A consonant held in one shape.</summary>
        public static MouthShape Held(int samples, float open, float width) => new MouthShape(samples, open, open, width, width);

        /// <summary>
        /// A vowel gliding between two formant targets (F1, F2, F3 Hz). Openness follows F1 (the jaw: 300 Hz for
        /// "ee"/"oo", 750 for "ah"), width follows F2 (the lips: low and rounded for "oo"/"oh", high and spread for
        /// "ee"). Breathed (unvoiced) it is a little less open.
        /// </summary>
        public static MouthShape Vowel(int samples, Vector3 from, Vector3 to, float voiced)
        {
            float k = voiced > 0f ? 1f : 0.6f;
            return new MouthShape(samples, Open(from) * k, Open(to) * k, Width(from), Width(to));
        }

        static float Open(Vector3 f) => Mathf.Lerp(0.15f, 1f, Mathf.InverseLerp(250f, 780f, f.x));
        static float Width(Vector3 f) => Mathf.InverseLerp(800f, 2300f, f.y);
    }
}
