using System.Collections.Generic;
using UnityEngine;

namespace PoBox.Audio
{
    /// <summary>
    /// The voice matrix's last tier: a boxer's voice made here, for when there is no recording. A source-filter
    /// voice: a glottal pulse train (its pitch moving as the emotion has it, with jitter, shimmer and a growl an
    /// octave down for rasp) plus breath noise, through three formant resonators that glide between vowels. Each
    /// emotion is a shape: a grunt is short and falls, pain jumps up and falls, a groan is long and low, a dazed moan
    /// wobbles, a taunt is two or three short laughs, triumph rises and holds, defeat sighs down into breath; the
    /// exhale and the panting are breath only. Several takes of each, from different seeds, so no two are alike.
    /// 16 kHz mono, made once a boxer and emotion, the first time it is heard.
    /// </summary>
    public static class VoiceSynth
    {
        public const int Rate = 16000;

        // Vowel formants (Hz, an adult man's; scaled by the profile): ah, uh, oh, eh, and the breath's "hh".
        static readonly Vector3 Ah = new Vector3(730f, 1090f, 2440f), Uh = new Vector3(640f, 1190f, 2390f),
            Oh = new Vector3(500f, 870f, 2400f), Eh = new Vector3(560f, 1800f, 2500f), Hh = new Vector3(800f, 1500f, 2600f);

        /// <summary>Takes of one emotion in this voice; <paramref name="takes"/> of them, each its own.</summary>
        public static AudioClip[] Make(VoiceProfile v, Emotion e, string name, int takes = 3)
        {
            var clips = new AudioClip[takes];
            for (int t = 0; t < takes; t++)
            {
                var rng = new System.Random((name ?? "").GetHashCode() * 31 + (int)e * 7919 + t * 104729);
                float[] data = Render(v, e, rng);
                AudioClip clip = AudioClip.Create($"Voice {name} {e} {t + 1}", data.Length, 1, Rate, false);
                clip.SetData(data, 0);
                clips[t] = clip;
            }
            return clips;
        }

        /// <summary>
        /// Takes of one emotion said at an intensity (0 barely, 1 all out) and length (1 the emotion's own), for the
        /// 5x5 layer (<see cref="VoiceMatrix.Map"/>): the harder, the higher, rougher, louder and less breathy.
        /// </summary>
        public static AudioClip[] Make(VoiceProfile v, Emotion e, string name, int takes, float intensity, float length)
        {
            intensity = Mathf.Clamp01(intensity);
            VoiceProfile s = Shade(v, intensity);
            float peak = Mathf.Lerp(0.45f, 0.85f, intensity);
            var clips = new AudioClip[takes];
            for (int t = 0; t < takes; t++)
            {
                var rng = new System.Random((name ?? "").GetHashCode() * 31 + (int)e * 7919 + t * 104729 + Mathf.RoundToInt(intensity * 1000f) * 613 + Mathf.RoundToInt(length * 100f));
                float[] data = Render(s, e, rng, length, peak);
                AudioClip clip = AudioClip.Create($"Voice {name} {e} {intensity:0.00} {t + 1}", data.Length, 1, Rate, false);
                clip.SetData(data, 0);
                clips[t] = clip;
            }
            return clips;
        }

        /// <summary>A voice pushed to an intensity: pitch up to +15%, more rasp, less breath (a soft one the reverse).</summary>
        public static VoiceProfile Shade(VoiceProfile v, float intensity)
        {
            intensity = Mathf.Clamp01(intensity);
            v.f0 *= Mathf.Lerp(0.92f, 1.15f, intensity);
            v.rasp = Mathf.Clamp01(v.rasp + Mathf.Lerp(-0.05f, 0.3f, intensity));
            v.breath = Mathf.Clamp01(v.breath + Mathf.Lerp(0.15f, -0.05f, intensity));
            return v;
        }

        /// <summary>One take, normalised to a peak of <paramref name="peak"/> (0.8), <paramref name="length"/> times its emotion's length.</summary>
        public static float[] Render(VoiceProfile v, Emotion e, System.Random rng, float length = 1f, float peak = 0.8f)
        {
            float R() => (float)rng.NextDouble();
            float vary = (0.9f + 0.2f * R()) * Mathf.Clamp(length, 0.3f, 2f);   // this take's length
            float lift = 0.94f + 0.12f * R();                   // and pitch
            var parts = new List<float[]>();
            switch (e)
            {
                case Emotion.Exhale:
                    parts.Add(Syllable(v, rng, 0.16f * vary, 0f, 0f, 0f, Hh, Hh, 0f, 1f, 0.004f, 3f));
                    break;
                case Emotion.Effort:
                    parts.Add(Syllable(v, rng, 0.28f * vary, 1.12f * lift, 0.88f * lift, 0.7f, Uh, Uh, 0.15f, 0.25f, 0.008f, 4f));
                    break;
                case Emotion.Pain:
                    parts.Add(Syllable(v, rng, 0.42f * vary, 1.45f * lift, 0.8f * lift, 0.85f, Ah, Uh, 0.2f, 0.25f, 0.006f, 2.2f));
                    break;
                case Emotion.Groan:
                    parts.Add(Syllable(v, rng, 1.0f * vary, 0.95f * lift, 0.72f * lift, 0.75f, Oh, Uh, 0.35f, 0.4f, 0.06f, 1.2f, vibrato: 0.03f));
                    break;
                case Emotion.Pant:
                    parts.Add(Syllable(v, rng, 0.2f * vary, 0f, 0f, 0f, Hh, Hh, 0f, 0.8f, 0.03f, 2f));
                    parts.Add(Silence(0.12f * vary));
                    parts.Add(Syllable(v, rng, 0.24f * vary, 0f, 0f, 0f, Hh, Hh, 0f, 1f, 0.01f, 2.5f));
                    break;
                case Emotion.Dazed:
                    parts.Add(Syllable(v, rng, 0.95f * vary, 1.0f * lift, 0.82f * lift, 0.6f, Uh, Oh, 0.4f, 0.55f, 0.08f, 1.0f, vibrato: 0.09f));
                    break;
                case Emotion.Taunt:
                    int laughs = 2 + rng.Next(2);
                    for (int i = 0; i < laughs; i++)
                    {
                        float p = (1.25f + 0.08f * i) * lift;
                        parts.Add(Syllable(v, rng, 0.11f * vary, p, p * 0.92f, 0.9f, Ah, Ah, 0.05f, 0.35f, 0.004f, 5f));
                        parts.Add(Silence(0.07f * vary));
                    }
                    break;
                case Emotion.Triumph:
                    parts.Add(Syllable(v, rng, 1.1f * vary, 1.05f * lift, 1.55f * lift, 1f, Eh, Ah, 0.12f, 0.2f, 0.05f, 1.3f, rise: true));
                    break;
                case Emotion.Defeat:
                    parts.Add(Syllable(v, rng, 0.9f * vary, 1.0f * lift, 0.62f * lift, 0.55f, Oh, Uh, 0.3f, 0.5f, 0.1f, 1.0f));
                    parts.Add(Syllable(v, rng, 0.4f * vary, 0f, 0f, 0f, Hh, Hh, 0f, 0.5f, 0.05f, 2f));
                    break;
            }
            int n = 0;
            foreach (float[] p in parts) n += p.Length;
            var data = new float[n];
            int at = 0;
            foreach (float[] p in parts) { System.Array.Copy(p, 0, data, at, p.Length); at += p.Length; }
            float top = 1e-4f;
            for (int i = 0; i < n; i++) top = Mathf.Max(top, Mathf.Abs(data[i]));
            float g = peak / top;
            for (int i = 0; i < n; i++) data[i] *= g;
            return data;
        }

        static float[] Silence(float seconds) => new float[Mathf.Max(1, Mathf.RoundToInt(seconds * Rate))];

        /// <summary>
        /// One voiced (or, with <paramref name="voiced"/> 0, breathed) sound: pitch from <paramref name="p0"/> to
        /// <paramref name="p1"/> times the voice's own, the vowel gliding from <paramref name="v0"/> to <paramref name="v1"/>,
        /// with this much extra rasp and breath, an attack of <paramref name="attack"/> seconds and a decay shape (higher, sooner gone).
        /// </summary>
        static float[] Syllable(VoiceProfile v, System.Random rng, float seconds, float p0, float p1, float voiced,
                                Vector3 v0, Vector3 v1, float rasp, float breath, float attack, float decay,
                                float vibrato = 0f, bool rise = false)
        {
            int n = Mathf.Max(1, Mathf.RoundToInt(seconds * Rate));
            var o = new float[n];
            float R() => (float)rng.NextDouble();
            float roughness = Mathf.Clamp01(v.rasp + rasp), air = Mathf.Clamp01(v.breath + breath);
            var f1 = new Resonator(); var f2 = new Resonator(); var f3 = new Resonator();
            float phase = 0f, jitter = 0f, shimmer = 1f, lowNoise = 0f, tremorRate = 4.5f + R(), vibRate = 5f + R();
            int cycle = 0;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)n, s = i / (float)Rate;
                // Pitch: the emotion's contour (a rise eases in and holds; everything else falls), the voice's tremor and vibrato.
                float shape = rise ? Mathf.SmoothStep(0f, 1f, Mathf.Min(1f, t * 1.8f)) : t * t * (3f - 2f * t);
                float pitch = Mathf.Lerp(p0, p1, shape) * v.f0
                              * (1f + v.tremor * 0.04f * Mathf.Sin(2f * Mathf.PI * tremorRate * s))
                              * (1f + vibrato * Mathf.Sin(2f * Mathf.PI * vibRate * s))
                              * (1f + jitter);
                float source = 0f;
                if (voiced > 0f && pitch > 1f)
                {
                    phase += pitch / Rate;
                    if (phase >= 1f)
                    {
                        // A new glottal cycle: rasp jitters its length and shimmers its strength.
                        phase -= 1f;
                        cycle++;
                        jitter = (R() * 2f - 1f) * 0.035f * roughness;
                        shimmer = 1f + (R() * 2f - 1f) * 0.35f * roughness;
                    }
                    // A Rosenberg pulse: opening, closing, then shut; its derivative is what the throat sends up.
                    float open = phase < 0.4f ? 0.5f * (1f - Mathf.Cos(Mathf.PI * phase / 0.4f)) : phase < 0.6f ? Mathf.Cos(Mathf.PI * (phase - 0.4f) / 0.4f) : 0f;
                    float sub = 1f + v.growl * ((cycle & 1) == 0 ? 0.6f : -0.6f);   // every other cycle louder: the octave-down growl
                    source = (open - 0.25f) * shimmer * sub * voiced;
                }
                // Breath: noise, louder on the open part of the cycle when voiced, all of it when not.
                lowNoise += 0.5f * ((R() * 2f - 1f) - lowNoise);
                float breathAmt = voiced > 0f ? air * 0.35f * (0.4f + (phase < 0.6f ? 0.6f : 0f)) : 1f;
                source += lowNoise * breathAmt;

                Vector3 f = Vector3.Lerp(v0, v1, t) * v.formants;
                float y = f1.Run(source, f.x, 90f) * 1.0f + f2.Run(source, f.y, 110f) * 0.6f + f3.Run(source, f.z, 160f) * 0.3f;
                float env = Mathf.Min(1f, s / Mathf.Max(0.001f, attack)) * Mathf.Exp(-t * decay) * (1f - Mathf.Pow(t, 6f));
                o[i] = y * env;
            }
            return o;
        }

        /// <summary>A two-pole resonance (a formant): its centre and bandwidth may move every sample.</summary>
        struct Resonator
        {
            float _y1, _y2;
            public float Run(float x, float hz, float bandwidth)
            {
                float r = Mathf.Exp(-Mathf.PI * bandwidth / Rate);
                float c = 2f * r * Mathf.Cos(2f * Mathf.PI * Mathf.Min(hz, Rate * 0.45f) / Rate);
                float y = (1f - r) * x + c * _y1 - r * r * _y2;
                _y2 = _y1;
                _y1 = y;
                return y;
            }
        }
    }
}
