using UnityEngine;

namespace PoBox.Audio
{
    /// <summary>
    /// The make of one boxer's voice, for <see cref="VoiceSynth"/>: how high it is, how big the head it comes out
    /// of (formants scaled), and how rough (rasp: jitter, shimmer and a growl an octave down), breathy and shaky
    /// (tremor) it is.
    /// </summary>
    public struct VoiceProfile
    {
        public float f0, formants, rasp, breath, tremor, growl;

        public VoiceProfile(float f0, float formants, float rasp, float breath, float tremor = 0f, float growl = 0f)
        {
            this.f0 = f0; this.formants = formants; this.rasp = rasp; this.breath = breath; this.tremor = tremor; this.growl = growl;
        }

        /// <summary>
        /// The roster's voices, by the name on the scoreboard. A boxer not here gets one from its build: lower the
        /// heavier, a woman's if <paramref name="high"/>, and a touch of its own from its name.
        /// </summary>
        public static VoiceProfile For(string name, float mass, bool high)
        {
            switch (name)
            {
                case "MATT": return new VoiceProfile(115f, 1.00f, 0.12f, 0.15f);
                case "NICK": return new VoiceProfile(128f, 1.03f, 0.08f, 0.15f);
                case "SOKOL": return new VoiceProfile(108f, 0.98f, 0.15f, 0.12f);
                case "TRUMP": return new VoiceProfile(104f, 0.96f, 0.28f, 0.12f);
                case "ZOMBIE": return new VoiceProfile(78f, 0.86f, 0.75f, 0.30f, 0.10f, 0.65f);
                case "GRANDPA": return new VoiceProfile(98f, 0.96f, 0.38f, 0.30f, 0.45f);
                case "GRANDMA": return new VoiceProfile(205f, 1.14f, 0.22f, 0.35f, 0.50f);
                case "KIM": return new VoiceProfile(222f, 1.18f, 0.06f, 0.22f);
                case "TIMMY": return new VoiceProfile(132f, 1.03f, 0.10f, 0.18f);       // young, a little higher and cleaner than Matt
                case "SOLDAT": return new VoiceProfile(102f, 0.97f, 0.22f, 0.12f);      // low, clipped, a little gravel
            }
            float k = Mathf.Clamp(Mathf.Sqrt(80f / Mathf.Max(40f, mass)), 0.85f, 1.15f);
            float own = 1f + 0.06f * (((name ?? "").GetHashCode() & 0xff) / 127.5f - 1f);
            return high ? new VoiceProfile(210f * k * own, 1.16f, 0.1f, 0.25f) : new VoiceProfile(112f * k * own, 1.0f, 0.15f, 0.15f);
        }
    }
}
