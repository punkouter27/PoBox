using UnityEngine;

namespace PoBox.Audio
{
    /// <summary>
    /// The make of one boxer's voice, for <see cref="VoiceSynth"/>: how high it is, how big the head it comes out
    /// of (formants scaled), and how rough (rasp: jitter, shimmer and a growl an octave down), breathy and shaky
    /// (tremor) it is; its vibrato and its tempo (how quickly it says things). The voice manifest
    /// (Resources/Voices/voices.json, <see cref="VoiceManifest"/>) carries one of these per boxer; the table in
    /// <see cref="For"/> is what a boxer new to the manifest starts from.
    /// </summary>
    public struct VoiceProfile
    {
        public float f0, formants, rasp, breath, tremor, growl;
        /// <summary>Vibrato rate (Hz) and depth (a share of the pitch), and tempo (1 the usual pace, higher quicker).</summary>
        public float vibratoHz, vibratoDepth, tempo;

        public VoiceProfile(float f0, float formants, float rasp, float breath, float tremor = 0f, float growl = 0f,
                            float vibratoHz = 5f, float vibratoDepth = 0.015f, float tempo = 1f)
        {
            this.f0 = f0; this.formants = formants; this.rasp = rasp; this.breath = breath; this.tremor = tremor; this.growl = growl;
            this.vibratoHz = vibratoHz; this.vibratoDepth = vibratoDepth; this.tempo = tempo;
        }

        /// <summary>
        /// The roster's voices, by the name on the scoreboard. A boxer not here gets one from its build: lower the
        /// heavier, a woman's if <paramref name="high"/>, and a touch of its own from its name.
        /// </summary>
        public static VoiceProfile For(string name, float mass, bool high)
        {
            switch (name)
            {
                case "MATT": return new VoiceProfile(115f, 1.00f, 0.12f, 0.15f, 0f, 0f, 5.2f, 0.015f, 1.00f);
                case "NICK": return new VoiceProfile(128f, 1.03f, 0.08f, 0.15f, 0f, 0f, 5.6f, 0.012f, 1.08f);
                case "SOKOL": return new VoiceProfile(108f, 0.98f, 0.15f, 0.12f, 0f, 0f, 4.8f, 0.010f, 0.95f);
                case "TRUMP": return new VoiceProfile(104f, 0.96f, 0.28f, 0.12f, 0f, 0f, 4.6f, 0.020f, 0.90f);
                case "ZOMBIE": return new VoiceProfile(78f, 0.86f, 0.75f, 0.30f, 0.10f, 0.65f, 3.5f, 0.030f, 0.75f);
                case "GRANDPA": return new VoiceProfile(98f, 0.96f, 0.38f, 0.30f, 0.45f, 0f, 4.2f, 0.035f, 0.85f);
                case "GRANDMA": return new VoiceProfile(205f, 1.14f, 0.22f, 0.35f, 0.50f, 0f, 5.0f, 0.040f, 0.90f);
                case "KIM": return new VoiceProfile(222f, 1.18f, 0.06f, 0.22f, 0f, 0f, 6.0f, 0.020f, 1.12f);
                case "TIMMY": return new VoiceProfile(132f, 1.03f, 0.10f, 0.18f, 0f, 0f, 5.8f, 0.015f, 1.15f);   // young, a little higher and cleaner than Matt
                case "SOLDAT": return new VoiceProfile(102f, 0.97f, 0.22f, 0.12f, 0f, 0f, 4.5f, 0.008f, 1.00f);  // low, clipped, a little gravel
            }
            float k = Mathf.Clamp(Mathf.Sqrt(80f / Mathf.Max(40f, mass)), 0.85f, 1.15f);
            float own = 1f + 0.06f * (((name ?? "").GetHashCode() & 0xff) / 127.5f - 1f);
            return high ? new VoiceProfile(210f * k * own, 1.16f, 0.1f, 0.25f, 0f, 0f, 5.5f * own, 0.02f, 1.05f)
                        : new VoiceProfile(112f * k * own, 1.0f, 0.15f, 0.15f, 0f, 0f, 5f * own, 0.015f, 1f);
        }

        /// <summary>The manifest's word for a voice: "adult-male", "adult-female", "elderly-...", "young-male", "undead".</summary>
        public static string Timbre(string name, VoiceProfile v)
        {
            switch (name)
            {
                case "ZOMBIE": return "undead";
                case "GRANDPA": return "elderly-male";
                case "GRANDMA": return "elderly-female";
                case "TIMMY": return "young-male";
            }
            return v.f0 >= 170f ? "adult-female" : "adult-male";
        }
    }
}
