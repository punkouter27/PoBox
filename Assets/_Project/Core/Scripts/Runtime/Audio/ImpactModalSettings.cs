using UnityEngine;

namespace PoBox.Audio
{
    /// <summary>
    /// The modal impact sounds, data-driven (2026-11-25): one <see cref="ModalContactSet"/> per kind of contact
    /// (head, torso, glove on glove, canvas). <see cref="ImpactSynth.BuildModal"/> renders each set's banks at
    /// load (bodies by force, cracks by speed); <see cref="AudioDirector"/> picks and scales them from the contact
    /// SimBus reports (impulse N s and glove speed m/s, both measured from MuJoCo's contact). A new asset starts
    /// from the values the synthesised punches had before (ImpactSynth's Punch, BlockHit, FloorHit).
    /// </summary>
    [CreateAssetMenu(menuName = "PoBox/Audio/Impact Modal Settings", fileName = "ImpactModalSettings")]
    public class ImpactModalSettings : ScriptableObject
    {
        public ModalContactSet head = ModalContactSet.Head();
        public ModalContactSet torso = ModalContactSet.Torso();
        public ModalContactSet gloveOnGlove = ModalContactSet.GloveOnGlove();
        public ModalContactSet canvas = ModalContactSet.Canvas();

        [Tooltip("Steps each bank is rendered at: bodies by force, cracks by speed.")]
        [Range(2, 8)] public int levels = 4;
        [Tooltip("Variants a step, so no two hits in a row are the same.")]
        [Range(1, 4)] public int variants = 3;
        [Tooltip("Seed of the variants: the same asset always renders the same sounds.")]
        public int seed = 20261125;

        public ModalContactSet For(ContactKind kind)
        {
            switch (kind)
            {
                case ContactKind.Head: return head;
                case ContactKind.Torso: return torso;
                case ContactKind.GloveOnGlove: return gloveOnGlove;
                default: return canvas;
            }
        }
    }
}
