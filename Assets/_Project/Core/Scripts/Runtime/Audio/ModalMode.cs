using System;
using UnityEngine;

namespace PoBox.Audio
{
    /// <summary>One resonant mode of what was struck: a decaying sine (2026-11-25, <see cref="ModalContactSet"/>).</summary>
    [Serializable]
    public struct ModalMode
    {
        [Tooltip("Frequency, Hz.")]
        public float hz;
        [Tooltip("Level against the set's other modes.")]
        public float gain;
        [Tooltip("Seconds to fall to a third (e^-1).")]
        public float decay;

        public ModalMode(float hz, float gain, float decay)
        {
            this.hz = hz; this.gain = gain; this.decay = decay;
        }
    }
}
