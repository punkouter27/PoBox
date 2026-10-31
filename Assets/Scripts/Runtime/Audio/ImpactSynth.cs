using UnityEngine;

namespace PoBox.Audio
{
    /// <summary>
    /// Sounds built at load from what makes them, not recorded (2026-10-31): a punch is a leather slap (a burst of noise
    /// as long as the glove is in contact) over the modes of what it hit (a skull rings high and short, a trunk is a
    /// low, longer cavity thud); a block is padding on padding, muffled; the bell is struck brass, a few inharmonic
    /// partials that beat; the ring floor is sprung boards under canvas, a low boom with a rattle. Each kind is a bank
    /// of variants, harder ones lower and longer, picked by how hard the hit was, so no two in a row are the same.
    /// Plain AudioClip.Create, so it plays on every platform (WebGL included).
    /// </summary>
    public static class ImpactSynth
    {
        public const int Rate = 44100;
        public const int Hardness = 4;          // banks run soft to hard
        public const int PerHardness = 3;       // variants a step

        public static AudioClip[] Head, Body, Block, Floor, Step;
        public static AudioClip Bell, BellEnd;
        static bool _built;

        public static void Build()
        {
            if (_built) return;
            _built = true;
            var rng = new System.Random(20261031);
            Head = Bank("Synth Head", rng, (h, r) => Punch(r, h, head: true));
            Body = Bank("Synth Body", rng, (h, r) => Punch(r, h, head: false));
            Block = Bank("Synth Block", rng, BlockHit);
            Floor = Bank("Synth Floor", rng, FloorHit);
            Step = Bank("Synth Step", rng, StepHit);
            Bell = Clip("Synth Bell", BellStrikes(new System.Random(7), 1));
            BellEnd = Clip("Synth Bell End", BellStrikes(new System.Random(8), 3));
        }

        /// <summary>A variant of <paramref name="bank"/> for a hardness 0..1.</summary>
        public static AudioClip Pick(AudioClip[] bank, float hardness)
        {
            if (bank == null || bank.Length == 0) return null;
            int h = Mathf.Clamp(Mathf.FloorToInt(Mathf.Clamp01(hardness) * Hardness), 0, Hardness - 1);
            return bank[h * PerHardness + Random.Range(0, PerHardness)];
        }

        delegate float[] Maker(float hardness, System.Random rng);

        static AudioClip[] Bank(string name, System.Random rng, Maker make)
        {
            var bank = new AudioClip[Hardness * PerHardness];
            for (int h = 0; h < Hardness; h++)
                for (int v = 0; v < PerHardness; v++)
                    bank[h * PerHardness + v] = Clip($"{name} {h}.{v}", make((h + 0.5f) / Hardness, rng));
            return bank;
        }

        static AudioClip Clip(string name, float[] data)
        {
            Normalise(data, 0.9f);
            AudioClip c = AudioClip.Create(name, data.Length, 1, Rate, false);
            c.SetData(data, 0);
            return c;
        }

        // ------------------------------------------------------------ the sounds

        static float[] Punch(System.Random rng, float hard, bool head)
        {
            // Contact time: a hard punch stays on longer (the glove's padding is crushed), 6 to 14 ms.
            float contact = Mathf.Lerp(0.006f, 0.014f, hard) * Jitter(rng, 0.15f);
            float len = head ? 0.28f : 0.42f;
            var o = new float[(int)(len * Rate)];
            // The slap: leather on skin, bright, as long as the contact.
            Noise(o, rng, 0f, contact * 2.2f, head ? 2600f : 1700f, 1.1f, 0.55f, contact * 0.6f);
            // A crisp top on a fast one.
            Noise(o, rng, 0f, 0.004f, 5200f, 2f, 0.18f * hard, 0.0015f);
            // What it hit: modes of the skull and jaw, or of the chest and belly.
            float[] f = head ? new[] { 190f, 410f, 870f, 1450f } : new[] { 68f, 112f, 185f, 320f };
            float[] a = head ? new[] { 0.55f, 0.45f, 0.25f, 0.12f } : new[] { 0.9f, 0.6f, 0.35f, 0.15f };
            float[] d = head ? new[] { 0.07f, 0.05f, 0.03f, 0.02f } : new[] { 0.16f, 0.12f, 0.07f, 0.04f };
            float sink = Mathf.Lerp(1.08f, 0.88f, hard);              // harder sinks lower
            for (int i = 0; i < f.Length; i++)
                Mode(o, 0.001f, f[i] * sink * Jitter(rng, 0.04f), a[i] * Mathf.Lerp(0.5f, 1.15f, hard), d[i] * Mathf.Lerp(0.8f, 1.3f, hard), contact);
            return o;
        }

        static float[] BlockHit(float hard, System.Random rng)
        {
            var o = new float[(int)(0.22f * Rate)];
            float contact = Mathf.Lerp(0.008f, 0.016f, hard);
            // Padding on padding: a dull pop, little top.
            Noise(o, rng, 0f, contact * 2.5f, 650f, 0.9f, 0.7f, contact);
            Mode(o, 0f, 240f * Jitter(rng, 0.06f), 0.6f, 0.05f, contact);
            Mode(o, 0f, 520f * Jitter(rng, 0.06f), 0.25f, 0.03f, contact);
            Noise(o, rng, 0f, 0.003f, 3000f, 1.5f, 0.12f * hard, 0.001f);   // the leather's tick
            return o;
        }

        static float[] FloorHit(float hard, System.Random rng)
        {
            var o = new float[(int)(0.9f * Rate)];
            // Sprung boards under foam and canvas: a low boom and a short rattle of the boards.
            Mode(o, 0f, 58f * Jitter(rng, 0.05f), 1f, Mathf.Lerp(0.18f, 0.32f, hard), 0.012f);
            Mode(o, 0f, 93f * Jitter(rng, 0.05f), 0.7f, Mathf.Lerp(0.12f, 0.22f, hard), 0.01f);
            Mode(o, 0f, 147f * Jitter(rng, 0.05f), 0.4f, 0.09f, 0.008f);
            Noise(o, rng, 0f, 0.05f, 900f, 0.8f, 0.45f, 0.02f);                    // the canvas slap
            for (int k = 0; k < 3 + (int)(hard * 5); k++)                           // the boards
                Noise(o, rng, 0.02f + (float)rng.NextDouble() * 0.12f, 0.012f, 420f * Jitter(rng, 0.3f), 1.4f, 0.18f * hard, 0.004f);
            return o;
        }

        static float[] StepHit(float hard, System.Random rng)
        {
            var o = new float[(int)(0.16f * Rate)];
            Mode(o, 0f, 88f * Jitter(rng, 0.08f), 0.8f, 0.045f, 0.006f);              // the board under the step
            Noise(o, rng, 0f, 0.03f, 420f, 0.7f, 0.5f, 0.01f);                        // sole on canvas
            Noise(o, rng, 0.004f, 0.01f, 2200f, 1.2f, 0.08f * hard, 0.004f);          // a scuff
            return o;
        }

        static float[] BellStrikes(System.Random rng, int strikes)
        {
            float gap = 0.32f, ring = 2.6f;
            var o = new float[(int)((gap * (strikes - 1) + ring) * Rate)];
            // A brass ring bell: inharmonic partials, the first two in a close pair that beats.
            float f0 = 812f;
            float[] r = { 1f, 1.0045f, 2.32f, 2.92f, 4.25f, 5.71f, 7.34f };
            float[] a = { 0.6f, 0.45f, 0.5f, 0.3f, 0.22f, 0.12f, 0.08f };
            float[] d = { 1.6f, 1.5f, 0.9f, 0.7f, 0.45f, 0.3f, 0.2f };
            for (int s = 0; s < strikes; s++)
            {
                float t0 = s * gap, hit = s == strikes - 1 ? 1f : 0.85f;
                Noise(o, rng, t0, 0.006f, 3500f, 1.2f, 0.35f * hit, 0.002f);   // the hammer
                for (int i = 0; i < r.Length; i++)
                    Mode(o, t0, f0 * r[i], a[i] * hit, d[i], 0.0008f);
            }
            return o;
        }

        // ------------------------------------------------------------ parts

        static float Jitter(System.Random rng, float amount) => 1f + ((float)rng.NextDouble() * 2f - 1f) * amount;

        /// <summary>A decaying sine from <paramref name="at"/> s: a mode of what was struck.</summary>
        static void Mode(float[] o, float at, float freq, float amp, float decay, float attack)
        {
            int i0 = (int)(at * Rate);
            float w = 2f * Mathf.PI * freq / Rate, phase = 0f;
            int n = Mathf.Min(o.Length - i0, (int)(decay * 7f * Rate));
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float env = Mathf.Min(1f, t / Mathf.Max(1e-4f, attack)) * Mathf.Exp(-t / decay);
                o[i0 + i] += amp * env * Mathf.Sin(phase);
                phase += w;
            }
        }

        /// <summary>A burst of noise through a band-pass at <paramref name="centre"/> Hz (Q <paramref name="q"/>), decaying over <paramref name="decay"/> s.</summary>
        static void Noise(float[] o, System.Random rng, float at, float decay, float centre, float q, float amp, float attack)
        {
            int i0 = (int)(at * Rate);
            int n = Mathf.Min(o.Length - i0, (int)(decay * 6f * Rate));
            // RBJ band-pass, constant peak gain.
            float w0 = 2f * Mathf.PI * Mathf.Min(centre, Rate * 0.45f) / Rate, alpha = Mathf.Sin(w0) / (2f * q);
            float b0 = alpha, b2 = -alpha, a0 = 1f + alpha, a1 = -2f * Mathf.Cos(w0), a2 = 1f - alpha;
            float x1 = 0, x2 = 0, y1 = 0, y2 = 0;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float x = (float)rng.NextDouble() * 2f - 1f;
                float y = (b0 * x + b2 * x2 - a1 * y1 - a2 * y2) / a0;
                x2 = x1; x1 = x; y2 = y1; y1 = y;
                float env = Mathf.Min(1f, t / Mathf.Max(1e-4f, attack)) * Mathf.Exp(-t / decay);
                o[i0 + i] += amp * 3f * env * y;
            }
        }

        static void Normalise(float[] o, float peak)
        {
            float m = 1e-6f;
            for (int i = 0; i < o.Length; i++) m = Mathf.Max(m, Mathf.Abs(o[i]));
            float k = peak / m;
            for (int i = 0; i < o.Length; i++) o[i] *= k;
            // A short fade at the end, so a cut tail does not click.
            int f = Mathf.Min(o.Length, Rate / 100);
            for (int i = 0; i < f; i++) o[o.Length - 1 - i] *= i / (float)f;
        }
    }
}
