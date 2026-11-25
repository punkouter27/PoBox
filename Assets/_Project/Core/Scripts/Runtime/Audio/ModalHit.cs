using UnityEngine;

namespace PoBox.Audio
{
    /// <summary>
    /// One modal impact to play (2026-11-25): the body of what was struck, its level and pitch from the contact's
    /// force, and the bright transient of the contact itself, its level and pitch from the contact's speed.
    /// </summary>
    public struct ModalHit
    {
        public AudioClip body, crack;
        public float bodyVolume, bodyPitch, crackVolume, crackPitch;
    }
}
