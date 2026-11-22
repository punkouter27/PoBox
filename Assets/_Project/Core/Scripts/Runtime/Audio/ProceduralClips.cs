using UnityEngine;

namespace PoBox.Audio
{
    /// <summary>The sounds <see cref="AudioDirector"/> makes itself, once at start: noise loops (boots, air, ropes), a squeak, a twang, the timekeeper's sticks and a heartbeat.</summary>
    internal static class ProceduralClips
    {
        /// <summary>A second of cloth being rubbed: white noise with the top and the bottom taken off, its end faded into its beginning.</summary>
        internal static AudioClip ScrapeLoop()
        {
            const int rate = 22050, fade = 2048;
            var rng = new System.Random(7);
            var raw = new float[rate + fade];
            float low = 0f, slow = 0f;
            for (int i = 0; i < raw.Length; i++)
            {
                float white = (float)rng.NextDouble() * 2f - 1f;
                low += 0.35f * (white - low);           // off above about 2 kHz
                slow += 0.02f * (low - slow);           // what is under about 70 Hz
                raw[i] = (low - slow) * 1.6f;
            }
            return Loop("Scrape", raw, rate, fade);
        }

        /// <summary>A second of moving air: noise with its lows and its highs taken off (a breathy band round 1 kHz), slowly swelling.</summary>
        internal static AudioClip AirLoop()
        {
            const int rate = 22050, fade = 2048;
            var rng = new System.Random(13);
            var raw = new float[rate + fade];
            float a = 0f, b = 0f;
            for (int i = 0; i < raw.Length; i++)
            {
                float white = (float)rng.NextDouble() * 2f - 1f;
                a += 0.28f * (white - a);               // off above about 1.5 kHz
                b += 0.06f * (a - b);                   // what is under about 200 Hz
                raw[i] = (a - b) * 2.2f * (0.8f + 0.2f * Mathf.Sin(i * 2f * Mathf.PI * 3f / rate));
            }
            return Loop("Air", raw, rate, fade);
        }

        /// <summary>A rubber sole squeaking on the canvas: a chirp from 1.7 to 2.6 kHz, stuttered by stick-slip, an eighth of a second.</summary>
        internal static AudioClip SqueakClip()
        {
            const int rate = 22050;
            int n = rate / 8;
            var data = new float[n];
            var rng = new System.Random(17);
            double phase = 0.0;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)n;
                double hz = 1700.0 + 900.0 * t;
                phase += 2.0 * System.Math.PI * hz / rate;
                float stick = 0.6f + 0.4f * Mathf.Sign(Mathf.Sin(i * 2f * Mathf.PI * 70f / rate));
                float env = Mathf.Sin(Mathf.PI * t) * (1f - 0.5f * t);
                data[i] = (float)(System.Math.Sin(phase) + 0.35 * System.Math.Sin(2.0 * phase) + 0.15 * (rng.NextDouble() - 0.5)) * stick * env * 0.5f;
            }
            AudioClip clip = AudioClip.Create("Squeak", n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>A second of rope being stretched: stick-slip, a ragged train of little knocks each ringing at its own pitch, swelling and falling.</summary>
        internal static AudioClip CreakLoop()
        {
            const int rate = 22050, fade = 2048;
            var rng = new System.Random(11);
            var raw = new float[rate + fade];
            float ring = 0f, ringV = 0f, w = 0.1f, next = 0f;
            for (int i = 0; i < raw.Length; i++)
            {
                if (i >= next)
                {
                    // A slip: a knock, and the next one a fortieth of a second or so later.
                    ringV += 0.03f + (float)rng.NextDouble() * 0.02f;
                    w = 2f * Mathf.PI * (260f + (float)rng.NextDouble() * 220f) / rate;
                    next = i + rate / (28f + (float)rng.NextDouble() * 24f);
                }
                // A damped resonator: the rope's own ring under each knock.
                ringV -= w * w * ring + 0.004f * ringV;
                ring += ringV;
                raw[i] = ring * (0.55f + 0.45f * Mathf.Sin(2f * Mathf.PI * 3f * i / rate));
            }
            return Loop("Creak", raw, rate, fade);
        }

        /// <summary>The timekeeper's sticks: two pieces of hardwood struck together, a bright knock that is gone in a few hundredths of a second.</summary>
        internal static AudioClip Clack()
        {
            const int rate = 22050, n = rate / 8;
            var rng = new System.Random(17);
            var data = new float[n];
            float a = 0f, av = 0f, b = 0f, bv = 0f;
            float wa = 2f * Mathf.PI * 1850f / rate, wb = 2f * Mathf.PI * 2930f / rate;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                // The strike: a two-millisecond burst of noise into two wooden resonances.
                float hit = t < 0.002f ? (float)rng.NextDouble() * 2f - 1f : 0f;
                av += hit * 0.5f - wa * wa * a - 0.02f * av; a += av;
                bv += hit * 0.4f - wb * wb * b - 0.03f * bv; b += bv;
                data[i] = (a + 0.6f * b) * Mathf.Exp(-t * 55f) * 0.9f;
            }
            AudioClip clip = AudioClip.Create("Clack", n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>A rope hit hard: a low thrum that sags in pitch as it dies, with the slap of the body on it at the front.</summary>
        internal static AudioClip Twang()
        {
            const int rate = 22050, n = rate * 7 / 10;
            var rng = new System.Random(5);
            var data = new float[n];
            float phase = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                phase += 2f * Mathf.PI * 78f * (1f + 0.06f * Mathf.Exp(-t * 8f)) / rate;
                float tone = Mathf.Sin(phase) + 0.5f * Mathf.Sin(2f * phase) + 0.25f * Mathf.Sin(3f * phase + 0.4f);
                float slap = t < 0.03f ? ((float)rng.NextDouble() * 2f - 1f) * (1f - t / 0.03f) * 0.45f : 0f;
                data[i] = tone * Mathf.Exp(-t * 5f) * Mathf.Min(1f, t * 400f) * 0.4f + slap;
            }
            AudioClip clip = AudioClip.Create("Rope Twang", n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>A heartbeat at 75 a minute, looped: lub, dub, rest; each beat a low sine that drops as it dies.</summary>
        internal static AudioClip Heartbeat()
        {
            const int rate = 22050, n = rate * 4 / 5;
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                data[i] = Beat(t, 52f, 0.9f) + Beat(t - 0.28f, 64f, 0.6f);
            }
            AudioClip clip = AudioClip.Create("Heartbeat", n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        static float Beat(float t, float hz, float level)
        {
            if (t < 0f || t > 0.2f) return 0f;
            float pitch = hz * (1f + 0.4f * Mathf.Exp(-t * 30f));
            return Mathf.Sin(2f * Mathf.PI * pitch * t) * Mathf.Min(1f, t / 0.006f) * Mathf.Exp(-t * 22f) * level;
        }

        /// <summary>The first <paramref name="rate"/> samples as a one-second loop: the samples after them are faded into its beginning.</summary>
        static AudioClip Loop(string name, float[] raw, int rate, int fade)
        {
            var data = new float[rate];
            for (int i = 0; i < rate; i++) data[i] = i < fade ? Mathf.Lerp(raw[rate + i], raw[i], i / (float)fade) : raw[i];
            AudioClip clip = AudioClip.Create(name, rate, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
