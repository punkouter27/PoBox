using UnityEngine;

namespace PoBox.Audio
{
    /// <summary>
    /// One contact kind's rendered sounds (2026-11-25): bodies by force step and cracks by speed step, each with its
    /// variants, and the set that maps a measured contact onto them. Made by <see cref="ImpactSynth.BuildModal"/>.
    /// </summary>
    public sealed class ModalBank
    {
        public readonly ModalContactSet set;
        public readonly AudioClip[] bodies, cracks;
        public readonly int levels, variants;

        public ModalBank(ModalContactSet set, AudioClip[] bodies, AudioClip[] cracks, int levels, int variants)
        {
            this.set = set;
            this.bodies = bodies;
            this.cracks = cracks;
            this.levels = levels;
            this.variants = variants;
        }

        /// <summary>
        /// The sound of a contact of <paramref name="force"/> N s at <paramref name="speed"/> m/s. A negative one is
        /// not known (a block carries only its speed, the canvas only its impulse) and is read from the other.
        /// </summary>
        public ModalHit Pick(float force, float speed)
        {
            float f = force >= 0f ? Mathf.InverseLerp(set.forceRange.x, set.forceRange.y, force) : -1f;
            float s = speed >= 0f ? Mathf.InverseLerp(set.speedRange.x, set.speedRange.y, speed) : -1f;
            if (f < 0f) f = Mathf.Max(0f, s);
            if (s < 0f) s = f;
            return new ModalHit
            {
                body = Of(bodies, f),
                bodyVolume = Mathf.Lerp(set.bodyVolume.x, set.bodyVolume.y, f),
                bodyPitch = Mathf.Lerp(set.bodyPitch.x, set.bodyPitch.y, s) * Random.Range(0.985f, 1.015f),
                crack = Of(cracks, s),
                crackVolume = Mathf.Lerp(set.crackVolume.x, set.crackVolume.y, s * s),
                crackPitch = Mathf.Lerp(set.crackPitch.x, set.crackPitch.y, s) * Random.Range(0.97f, 1.03f),
            };
        }

        AudioClip Of(AudioClip[] bank, float k)
        {
            if (bank == null || bank.Length == 0) return null;
            int step = Mathf.Clamp(Mathf.FloorToInt(k * levels), 0, levels - 1);
            return bank[step * variants + Random.Range(0, variants)];
        }
    }
}
