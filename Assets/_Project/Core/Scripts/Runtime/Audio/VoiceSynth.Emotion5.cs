using System.Collections.Generic;
using UnityEngine;

namespace PoBox.Audio
{
    /// <summary>
    /// The 5x5 layer's procedural imitation (the voice manifest's fallback for every WAV not recorded yet): a short
    /// human vocalisation in the boxer's own <see cref="VoiceProfile"/>, not a beep.
    ///
    ///   * Source: a Rosenberg glottal pulse, its derivative sent up the throat, at the boxer's pitch with jitter
    ///     (0.5-1.5% a cycle) and shimmer (3-6%), a subharmonic (every other cycle louder) for rasp and growl, and
    ///     aspiration noise mixed in by breath, strongest while the glottis is open.
    ///   * Filter: four formant resonators gliding between vowel targets (a, e, i, o, u, schwa) scaled by the
    ///     profile's formants; a glide (w, y, r) bends the vowel's start, a nasal (n) raises F2 and murmurs first.
    ///   * Syllables: one to four, with consonant onsets and codas: noise for h/s/sh/f, a closure and a burst for
    ///     k/t/p/g. "hup!", "rraah", "heh-heh", "oof", "ugh", "nyah-nyah", "pfft-heh", "yeah!", "woo-hoo!".
    ///   * Prosody per emotion (see <see cref="Plan"/>), and the level (1..5) scaling loudness, length, pitch range,
    ///     rasp, breath, syllable count and vibrato.
    ///   * Deterministic: seeded from (boxer id, emotion, level, take), so the same request always sounds the same.
    ///     The voice matrix caches what it makes and makes it at the walk-on.
    /// </summary>
    public static partial class VoiceSynth
    {
        // Vowel targets (F1, F2, F3 Hz, an adult man's; scaled by the profile's formants).
        static readonly Vector3 VA = new Vector3(730f, 1090f, 2440f), VE = new Vector3(530f, 1840f, 2480f),
            VI = new Vector3(300f, 2250f, 2950f), VO = new Vector3(570f, 840f, 2410f), VU = new Vector3(320f, 870f, 2240f),
            VSchwa = new Vector3(500f, 1500f, 2500f), VNasal = new Vector3(260f, 1150f, 2500f);
        const float F4 = 3400f;

        enum Cons { None, H, S, Sh, F, K, T, P, G, N }
        enum Glide { None, W, Y, R }
        enum Contour { Fall, Rise, Arch }

        /// <summary>One syllable: consonant onset, a vowel gliding v0 to v1 at pitch p0 to p1 (times the voice's own), a coda, then a gap.</summary>
        class Syl
        {
            public Cons onset, coda;
            public Glide glide;
            public Vector3 v0 = VSchwa, v1 = VSchwa;
            public float dur = 0.2f, p0 = 1f, p1 = 1f, voiced = 1f, rasp, breath, vibrato, am, amp = 1f, gap;
            public float attack = 0.01f, decay = 1.5f;
            public Contour contour = Contour.Fall;
            /// <summary>Where (0..1) the pitch breaks down, and by how much (0.7 = down 30%); 0 for none.</summary>
            public float breakAt, breakBy = 1f;
            public bool nasal, intake;
        }

        /// <summary>A stable hash (FNV-1a), the same on every run and platform, unlike string.GetHashCode.</summary>
        public static int Seed(string id, Emotion5 e, int level, int take)
        {
            unchecked
            {
                uint h = 2166136261;
                foreach (char c in $"{(id ?? "").ToLowerInvariant()}|{(int)e}|{level}|{take}") { h ^= c; h *= 16777619; }
                return (int)(h & 0x7fffffff);
            }
        }

        /// <summary>Takes of a 5x5 cell in this voice (take 0, 1, ...), each its own but the same every time.</summary>
        public static AudioClip[] Make5(VoiceProfile v, Emotion5 e, int level, string id, int takes)
        {
            var clips = new AudioClip[Mathf.Max(1, takes)];
            for (int t = 0; t < clips.Length; t++)
            {
                float[] data = Render5(v, e, level, t, id, out VisemeTrack mouth);
                AudioClip clip = AudioClip.Create($"Voice {id} {e} {level} {t + 1}", data.Length, 1, Rate, false);
                clip.SetData(data, 0);
                VisemeLibrary.Register(clip, mouth);
                clips[t] = clip;
            }
            return clips;
        }

        /// <summary>One take of a 5x5 cell, its peak 0.5 (level 1) to 0.9 (level 5), so it sits with recorded clips.</summary>
        public static float[] Render5(VoiceProfile v, Emotion5 e, int level, int take, string id) => Render5(v, e, level, take, id, out _);

        /// <summary>One take of a 5x5 cell and the mouth that says it, block by block (lip-sync, 2026-11-25).</summary>
        public static float[] Render5(VoiceProfile v, Emotion5 e, int level, int take, string id, out VisemeTrack mouth)
        {
            level = Mathf.Clamp(level, 1, 5);
            float k = (level - 1) / 4f;
            var rng = new System.Random(Seed(id, e, level, take));
            List<Syl> plan = Plan(e, level, rng);

            float R() => (float)rng.NextDouble();
            float tempo = v.tempo > 0.1f ? v.tempo : 1f;
            float len = Mathf.Lerp(0.75f, 1.35f, k) / tempo * (0.95f + 0.1f * (float)rng.NextDouble());
            float range = Mathf.Lerp(0.6f, 1.3f, k);
            float lift = 0.97f + 0.06f * R();

            var said = new Utterance();
            List<float[]> parts = said.parts;
            foreach (Syl s in plan)
            {
                s.dur *= len;
                s.gap *= len;
                s.p0 = (1f + (s.p0 - 1f) * range) * lift;
                s.p1 = (1f + (s.p1 - 1f) * range) * lift;
                s.rasp += 0.3f * k;
                s.vibrato = s.vibrato * Mathf.Lerp(0.6f, 1.4f, k) + v.vibratoDepth;
                Onset(said, v, s, rng);
                said.Add(Vowel(v, s, rng), MouthOf(s));
                Coda(said, v, s, rng);
                if (s.gap > 0f) said.Add(Silence(s.gap), MouthShape.Closed(0));
            }

            int n = 0;
            foreach (float[] p in parts) n += p.Length;
            var data = new float[n];
            int at = 0;
            foreach (float[] p in parts) { System.Array.Copy(p, 0, data, at, p.Length); at += p.Length; }
            float top = 1e-4f;
            for (int i = 0; i < n; i++) top = Mathf.Max(top, Mathf.Abs(data[i]));
            float g = Mathf.Lerp(0.5f, 0.9f, k) / top;
            for (int i = 0; i < n; i++) data[i] *= g;
            mouth = VisemeTrack.Build(data, said.shapes);
            return data;
        }

        /// <summary>The pieces of a take as they are joined, each with the mouth it is said with (lengths kept equal).</summary>
        sealed class Utterance
        {
            public readonly List<float[]> parts = new List<float[]>();
            public readonly List<MouthShape> shapes = new List<MouthShape>();

            public void Add(float[] piece, MouthShape m)
            {
                m.samples = piece.Length;
                parts.Add(piece);
                shapes.Add(m);
            }
        }

        /// <summary>A syllable's vowel as the mouth makes it: from the glide's shape (w rounded, y spread) to its last vowel.</summary>
        static MouthShape MouthOf(Syl s)
        {
            Vector3 from = s.glide == Glide.W ? VU : s.glide == Glide.Y ? VI : s.v0;
            MouthShape m = MouthShape.Vowel(0, from, s.v1, s.voiced);
            if (s.nasal) { m.open0 *= 0.8f; m.open1 *= 0.8f; }
            if (s.glide != Glide.None) m.reach = 0.3f; // a glide (the y of "nyah", the w of "wah") is over early in the vowel
            return m;
        }

        /// <summary>
        /// The syllables of an emotion at a level, before the level's length, range and rasp are applied:
        /// angry "hup!" (1-2) or "rraah" (3+, "rraah-hah" from 4), tense and falling with amplitude roughness;
        /// happy "heh" then laughs, bright vowels, rising-falling; hurt "oof" (1), a gasp then "ugh" with a pitch break
        /// (2-4), a gasp then a long cry (5); insult "pfft-heh" (1-2) or a sing-song "nyah-nyah" (3+), F2 raised;
        /// celebrate "yeah!" (1-2) or a rising "woo-hoo!" held with wide vibrato (3+), and a laugh after it (4+).
        /// </summary>
        static List<Syl> Plan(Emotion5 e, int level, System.Random rng)
        {
            float R() => (float)rng.NextDouble();
            var p = new List<Syl>();
            switch (e)
            {
                case Emotion5.Angry:
                    if (level <= 2)
                        p.Add(new Syl { onset = Cons.H, v0 = VU, v1 = VSchwa, dur = 0.16f, p0 = 1.15f, p1 = 0.9f, rasp = 0.2f, am = 0.2f, coda = Cons.P, attack = 0.004f, decay = 2f });
                    else
                    {
                        p.Add(new Syl { glide = Glide.R, v0 = VA, v1 = VA, dur = 0.34f, p0 = 1.3f, p1 = 0.8f, rasp = 0.45f, am = 0.45f, attack = 0.006f, decay = 1.2f, gap = level >= 4 ? 0.05f : 0f });
                        if (level >= 4) p.Add(new Syl { onset = Cons.H, v0 = VA, v1 = VSchwa, dur = 0.16f, p0 = 1.2f, p1 = 0.85f, rasp = 0.4f, am = 0.35f, attack = 0.004f, decay = 2.2f });
                    }
                    break;

                case Emotion5.Happy:
                {
                    int n = level <= 1 ? 1 : level == 2 ? 2 : level == 3 ? 3 : 4;
                    for (int i = 0; i < n; i++)
                    {
                        float arch = 1.12f + 0.2f * Mathf.Sin(Mathf.PI * (i + 0.5f) / n);   // rising, then falling
                        p.Add(new Syl
                        {
                            onset = Cons.H, v0 = level >= 3 ? VA : VE, v1 = level >= 3 ? VA : VE, dur = n == 1 ? 0.18f : 0.11f,
                            p0 = arch, p1 = arch * 0.94f, contour = n == 1 ? Contour.Arch : Contour.Fall, vibrato = 0.015f,
                            breath = 0.1f, attack = 0.006f, decay = 3f, gap = i < n - 1 ? 0.06f + 0.02f * R() : 0f,
                        });
                    }
                    break;
                }

                case Emotion5.Hurt:
                    if (level == 1)
                    {
                        p.Add(new Syl { v0 = VO, v1 = VU, dur = 0.2f, p0 = 1.05f, p1 = 0.85f, breath = 0.2f, coda = Cons.F, attack = 0.008f, decay = 2f });
                        break;
                    }
                    // The intake: unvoiced, through an "ih", swelling.
                    p.Add(new Syl { voiced = 0f, intake = true, v0 = VI, v1 = VSchwa, dur = 0.08f + 0.02f * level, breath = 1f, amp = 0.55f, attack = 0.06f, decay = 0.3f, gap = 0.035f });
                    if (level < 5)
                        p.Add(new Syl { v0 = VSchwa, v1 = VU, dur = 0.22f + 0.05f * level, p0 = 1.35f, p1 = 0.8f, breakAt = 0.35f, breakBy = 0.72f, breath = 0.3f, rasp = 0.25f, coda = level >= 3 ? Cons.G : Cons.None, attack = 0.01f, decay = 1.6f });
                    else
                        p.Add(new Syl { v0 = VA, v1 = VO, dur = 1.0f, p0 = 1.5f, p1 = 0.8f, breakAt = 0.3f, breakBy = 0.75f, breath = 0.3f, rasp = 0.3f, vibrato = 0.04f, attack = 0.02f, decay = 1.0f });
                    break;

                case Emotion5.Insult:
                    if (level <= 2)
                    {
                        p.Add(new Syl { onset = Cons.P, voiced = 0f, v0 = VU, v1 = VU, dur = 0.1f, breath = 1f, amp = 0.7f, coda = Cons.T, attack = 0.004f, decay = 1f, gap = 0.06f });
                        p.Add(new Syl { onset = Cons.H, v0 = VE, v1 = VE, dur = 0.14f, p0 = 1.2f, p1 = 1.05f, nasal = true, attack = 0.006f, decay = 2.5f });
                    }
                    else
                    {
                        int n = level >= 5 ? 4 : level == 4 ? 3 : 2;
                        for (int i = 0; i < n; i++)
                        {
                            float hi = (i % 2 == 0) ? 1.35f : 1.12f;   // sing-song
                            bool last = i == n - 1;
                            p.Add(new Syl
                            {
                                onset = Cons.N, glide = Glide.Y, v0 = VA, v1 = VA, dur = last ? 0.28f : 0.18f, p0 = hi, p1 = hi * (last ? 0.9f : 0.97f),
                                nasal = true, vibrato = 0.01f, attack = 0.01f, decay = last ? 1.5f : 2.2f, gap = last ? 0f : 0.05f,
                            });
                        }
                    }
                    break;

                default:   // Celebrate
                    if (level <= 2)
                        p.Add(new Syl { glide = Glide.Y, v0 = VE, v1 = VA, dur = 0.36f, p0 = 1.05f, p1 = 1.35f, contour = Contour.Arch, vibrato = 0.02f, attack = 0.01f, decay = 1.2f });
                    else
                    {
                        p.Add(new Syl { glide = Glide.W, v0 = VU, v1 = VU, dur = 0.22f, p0 = 1.0f, p1 = 1.45f, contour = Contour.Rise, attack = 0.01f, decay = 0.6f, gap = 0.03f });
                        p.Add(new Syl { onset = Cons.H, v0 = VU, v1 = VU, dur = 0.45f + 0.1f * level, p0 = 1.5f, p1 = 1.62f, contour = Contour.Rise, vibrato = 0.045f, attack = 0.01f, decay = 0.7f, gap = level >= 4 ? 0.08f : 0f });
                        if (level >= 4)
                            for (int i = 0; i < 2; i++)
                                p.Add(new Syl { onset = Cons.H, v0 = VA, v1 = VA, dur = 0.1f, p0 = 1.3f - 0.06f * i, p1 = 1.22f - 0.06f * i, breath = 0.1f, attack = 0.005f, decay = 3f, gap = i == 0 ? 0.06f : 0f });
                    }
                    break;
            }
            return p;
        }

        /// <summary>The consonant before the vowel: noise for h/s/sh/f, a closure and a burst for k/t/p/g, a nasal murmur for n.</summary>
        static void Onset(Utterance said, VoiceProfile v, Syl s, System.Random rng)
        {
            // The mouth: an h is already the vowel's shape, half open; s spread on the teeth; sh rounded; f lip on teeth.
            switch (s.onset)
            {
                case Cons.H: said.Add(Noise(rng, 0.05f, 1500f * v.formants, 1800f, 0.35f, 0.02f), MouthShape.Held(0, 0.35f, 0.5f)); break;
                case Cons.S: said.Add(Noise(rng, 0.08f, 5200f, 1800f, 0.5f, 0.015f), MouthShape.Held(0, 0.12f, 0.85f)); break;
                case Cons.Sh: said.Add(Noise(rng, 0.09f, 2800f, 1200f, 0.5f, 0.015f), MouthShape.Held(0, 0.15f, 0.1f)); break;
                case Cons.F: said.Add(Noise(rng, 0.07f, 4000f, 3500f, 0.25f, 0.01f), MouthShape.Held(0, 0.08f, 0.55f)); break;
                case Cons.K: case Cons.T: case Cons.P: case Cons.G: Plosive(said, rng, s.onset, 1f); break;
                case Cons.N:
                    var murmur = new Syl { v0 = VNasal, v1 = VNasal, dur = 0.06f, p0 = s.p0, p1 = s.p0, amp = 0.45f, rasp = s.rasp, attack = 0.01f, decay = 0.2f };
                    said.Add(Vowel(v, murmur, rng), MouthShape.Held(0, 0.15f, 0.5f));
                    break;
            }
        }

        /// <summary>The consonant after the vowel: "oof"'s f, "hup"'s p, "pfft"'s t, "ugh"'s g.</summary>
        static void Coda(Utterance said, VoiceProfile v, Syl s, System.Random rng)
        {
            switch (s.coda)
            {
                case Cons.F: said.Add(Noise(rng, 0.09f, 4000f, 3500f, 0.22f, 0.01f), MouthShape.Held(0, 0.08f, 0.55f)); break;
                case Cons.S: said.Add(Noise(rng, 0.1f, 5200f, 1800f, 0.35f, 0.01f), MouthShape.Held(0, 0.12f, 0.85f)); break;
                case Cons.K: case Cons.T: case Cons.P: case Cons.G: Plosive(said, rng, s.coda, 0.6f); break;
            }
        }

        /// <summary>A stop: a closure (silence), a burst of noise where the consonant is made, a breath of aspiration.</summary>
        static void Plosive(Utterance said, System.Random rng, Cons c, float strength)
        {
            float centre = c == Cons.K ? 1800f : c == Cons.T ? 4200f : c == Cons.P ? 900f : 1500f;
            // A p shuts the lips; k, t and g shut the mouth further back, the jaw nearly closed.
            said.Add(Silence(0.025f), c == Cons.P ? MouthShape.Closed(0) : MouthShape.Held(0, 0.08f, 0.5f));
            said.Add(Noise(rng, 0.014f, centre, centre * 0.6f, (c == Cons.G ? 0.5f : 0.9f) * strength, 0.001f), MouthShape.Held(0, 0.3f, 0.5f));
            if (c != Cons.G) said.Add(Noise(rng, 0.02f, 1500f, 2000f, 0.2f * strength, 0.002f), MouthShape.Held(0, 0.3f, 0.5f));
        }

        /// <summary>Band-limited noise (a fricative or a burst): <paramref name="seconds"/> long, around <paramref name="hz"/>.</summary>
        static float[] Noise(System.Random rng, float seconds, float hz, float bandwidth, float amp, float attack)
        {
            int n = Mathf.Max(1, Mathf.RoundToInt(seconds * Rate));
            var o = new float[n];
            var f = new Resonator();
            float gain = 1f + bandwidth / 400f;   // a narrow band passes less: lift it back
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)n, s = i / (float)Rate;
                float env = Mathf.Min(1f, s / Mathf.Max(0.0005f, attack)) * (1f - t * t);
                o[i] = f.Run((float)rng.NextDouble() * 2f - 1f, hz, bandwidth) * gain * amp * env * 4f;
            }
            return o;
        }

        /// <summary>
        /// The voiced (or, with voiced 0, breathed) vowel: the glottal pulse's derivative with jitter, shimmer and a
        /// subharmonic, aspiration noise, four formants gliding (from the glide's vowel, then v0 to v1), the pitch on
        /// its contour with vibrato, tremor and any break, amplitude roughness, and the envelope.
        /// </summary>
        static float[] Vowel(VoiceProfile v, Syl s, System.Random rng)
        {
            int n = Mathf.Max(1, Mathf.RoundToInt(s.dur * Rate));
            var o = new float[n];
            float R() => (float)rng.NextDouble();
            float rough = Mathf.Clamp01(v.rasp + s.rasp), air = Mathf.Clamp01(v.breath + s.breath);
            float jitterAmt = Mathf.Lerp(0.005f, 0.015f, rough), shimmerAmt = Mathf.Lerp(0.03f, 0.06f, rough);
            float sub = Mathf.Clamp01(v.growl + 0.5f * Mathf.Max(0f, rough - 0.3f));
            var f1 = new Resonator(); var f2 = new Resonator(); var f3 = new Resonator(); var f4 = new Resonator();
            float phase = 0f, jitter = 0f, shimmer = 1f, lowNoise = 0f, prevPulse = 0f;
            float vibHz = v.vibratoHz > 0f ? v.vibratoHz : 5f, tremorHz = 4f + R(), vibPhase = R() * 6.28f;
            int cycle = 0;
            Vector3 glideFrom = s.glide == Glide.W ? VU : s.glide == Glide.Y ? VI : s.glide == Glide.R ? new Vector3(s.v0.x * 0.8f, s.v0.y * 0.9f, 1600f) : s.v0;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)n, sec = i / (float)Rate;
                float shape = s.contour == Contour.Rise ? Mathf.SmoothStep(0f, 1f, Mathf.Min(1f, t * 1.8f))
                            : s.contour == Contour.Arch ? Mathf.Sin(Mathf.PI * Mathf.Min(1f, t * 1.15f))
                            : t * t * (3f - 2f * t);
                float pitch = (s.contour == Contour.Arch ? Mathf.Lerp(s.p0, s.p1, shape) * (1f - 0.08f * Mathf.Max(0f, t - 0.6f)) : Mathf.Lerp(s.p0, s.p1, shape)) * v.f0;
                if (s.breakAt > 0f) pitch *= Mathf.Lerp(1f, s.breakBy, Mathf.Clamp01((t - s.breakAt) * 25f));
                pitch *= (1f + s.vibrato * Mathf.Sin(2f * Mathf.PI * vibHz * sec + vibPhase))
                         * (1f + v.tremor * 0.04f * Mathf.Sin(2f * Mathf.PI * tremorHz * sec))
                         * (1f + jitter);

                float source = 0f;
                if (s.voiced > 0f && pitch > 1f)
                {
                    phase += pitch / Rate;
                    if (phase >= 1f)
                    {
                        phase -= 1f;
                        cycle++;
                        jitter = (R() * 2f - 1f) * jitterAmt;
                        shimmer = 1f + (R() * 2f - 1f) * shimmerAmt;
                    }
                    // Rosenberg pulse (opening 40%, closing 20%, shut); its derivative is what the throat sends up.
                    float g = phase < 0.4f ? 0.5f * (1f - Mathf.Cos(Mathf.PI * phase / 0.4f)) : phase < 0.6f ? Mathf.Cos(Mathf.PI * (phase - 0.4f) / 0.4f) : 0f;
                    float d = (g - prevPulse) * (Rate / Mathf.Max(60f, pitch)) * 0.3f;
                    prevPulse = g;
                    float subharmonic = 1f + sub * ((cycle & 1) == 0 ? 0.6f : -0.6f);
                    source = d * shimmer * subharmonic * s.voiced;
                }
                lowNoise += 0.55f * ((R() * 2f - 1f) - lowNoise);
                float breathAmt = s.voiced > 0f ? air * 0.4f * (0.35f + (phase < 0.6f ? 0.65f : 0f)) : 1f;
                source += lowNoise * breathAmt;

                Vector3 f = (t < 0.25f && s.glide != Glide.None ? Vector3.Lerp(glideFrom, s.v0, t / 0.25f) : Vector3.Lerp(s.v0, s.v1, t)) * v.formants;
                if (s.nasal) f.y *= 1.15f;   // the sneer: F2 up
                float widen = 1f + air;
                float y = f1.Run(source, f.x, 80f * widen) * (s.nasal ? 0.7f : 1f) + f2.Run(source, f.y, 100f * widen) * 0.6f
                        + f3.Run(source, f.z, 140f * widen) * 0.32f + f4.Run(source, F4 * v.formants, 220f * widen) * 0.18f;
                float env = s.intake ? Mathf.Pow(t, 1.5f) * (1f - Mathf.Pow(t, 8f))
                          : Mathf.Min(1f, sec / Mathf.Max(0.001f, s.attack)) * Mathf.Exp(-t * s.decay) * (1f - Mathf.Pow(t, 6f));
                if (s.am > 0f) env *= 1f - s.am * 0.5f * (1f + Mathf.Sin(2f * Mathf.PI * 32f * sec));
                o[i] = y * env * s.amp;
            }
            return o;
        }
    }
}
