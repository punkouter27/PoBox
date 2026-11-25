using System;
using UnityEngine;

namespace PoBox.Audio
{
    /// <summary>
    /// How one kind of contact sounds (2026-11-25): the modes of what was struck and the slap of the contact, and
    /// how the measured contact maps onto them. Force (the solver's impulse over the first 50 ms of the touch, N s,
    /// as SimBus.Hit and FloorImpact carry it) picks how hard a body to play, which is lower, longer and brighter
    /// in its upper modes the harder it is, and how loud; speed (the glove's at first touch, m/s, from Hit and
    /// Block) picks the contact's crack, which is brighter, louder and higher the faster it arrived.
    /// </summary>
    [Serializable]
    public class ModalContactSet
    {
        [Header("What was struck")]
        public ModalMode[] modes = new ModalMode[0];
        [Tooltip("Seconds of the rendered body.")]
        public float length = 0.3f;
        [Tooltip("Mode frequencies times this, soft to hard: a harder hit sinks lower.")]
        public Vector2 sink = new Vector2(1.08f, 0.88f);
        [Tooltip("Mode decays times this, soft to hard: a harder hit rings longer.")]
        public Vector2 decayScale = new Vector2(0.8f, 1.3f);
        [Tooltip("How far the upper modes come up at full force (0 none, 1 the top mode doubled): brightness from force.")]
        [Range(0f, 2f)] public float brightnessTilt = 0.6f;
        [Tooltip("Random spread of each mode's frequency between variants, a share.")]
        [Range(0f, 0.2f)] public float detune = 0.04f;

        [Header("The contact")]
        [Tooltip("Seconds the surfaces are in contact, soft to hard (padding crushed): the slap lasts about twice that.")]
        public Vector2 contactSeconds = new Vector2(0.006f, 0.014f);
        [Tooltip("The slap: band-pass centre (Hz), Q and level.")]
        public float slapHz = 2000f, slapQ = 1.1f, slapGain = 0.55f;
        [Tooltip("The crack laid over it by speed: centre (Hz) at no speed (it climbs to 1.5x at full), and seconds long.")]
        public float crackHz = 5000f, crackSeconds = 0.006f;
        [Tooltip("Short noisy rattles after the hit at full force (sprung boards under the canvas).")]
        [Range(0, 12)] public int rattles;
        public float rattleHz = 420f;

        [Header("Measured contact to sound")]
        [Tooltip("Impulse (N s) from which the body is heard at its softest, and at which it is at its hardest.")]
        public Vector2 forceRange = new Vector2(2f, 22f);
        [Tooltip("Contact speed (m/s) from which the crack is heard, and at which it is as bright as it gets.")]
        public Vector2 speedRange = new Vector2(2f, 9f);
        [Tooltip("The body's volume, softest to hardest.")]
        public Vector2 bodyVolume = new Vector2(0.4f, 1f);
        [Tooltip("The crack's volume, slowest to fastest.")]
        public Vector2 crackVolume = new Vector2(0.05f, 0.45f);
        [Tooltip("The crack's playback pitch, slowest to fastest.")]
        public Vector2 crackPitch = new Vector2(0.9f, 1.25f);
        [Tooltip("The body's playback pitch, slowest to fastest contact (a quick one rings a touch higher).")]
        public Vector2 bodyPitch = new Vector2(0.97f, 1.04f);

        /// <summary>A skull and jaw: high, short modes.</summary>
        public static ModalContactSet Head() => new ModalContactSet
        {
            modes = new[] { new ModalMode(190f, 0.55f, 0.07f), new ModalMode(410f, 0.45f, 0.05f), new ModalMode(870f, 0.25f, 0.03f), new ModalMode(1450f, 0.12f, 0.02f) },
            length = 0.28f, slapHz = 2600f, crackHz = 5200f,
        };

        /// <summary>A chest and belly: a low, longer cavity thud.</summary>
        public static ModalContactSet Torso() => new ModalContactSet
        {
            modes = new[] { new ModalMode(68f, 0.9f, 0.16f), new ModalMode(112f, 0.6f, 0.12f), new ModalMode(185f, 0.35f, 0.07f), new ModalMode(320f, 0.15f, 0.04f) },
            length = 0.42f, slapHz = 1700f, crackHz = 4200f, brightnessTilt = 0.4f,
        };

        /// <summary>Padding on padding: a dull pop, little top.</summary>
        public static ModalContactSet GloveOnGlove() => new ModalContactSet
        {
            modes = new[] { new ModalMode(240f, 0.6f, 0.05f), new ModalMode(520f, 0.25f, 0.03f) },
            length = 0.22f, contactSeconds = new Vector2(0.008f, 0.016f), slapHz = 650f, slapQ = 0.9f, slapGain = 0.7f,
            crackHz = 3000f, crackSeconds = 0.003f, detune = 0.06f, brightnessTilt = 0.3f,
            forceRange = new Vector2(1f, 16f), bodyVolume = new Vector2(0.35f, 0.9f), crackVolume = new Vector2(0.03f, 0.3f),
            crackPitch = new Vector2(1.0f, 1.2f), bodyPitch = new Vector2(0.97f, 1.03f),
        };

        /// <summary>Sprung boards under foam and canvas: a low boom and a rattle of the boards.</summary>
        public static ModalContactSet Canvas() => new ModalContactSet
        {
            modes = new[] { new ModalMode(58f, 1f, 0.25f), new ModalMode(93f, 0.7f, 0.17f), new ModalMode(147f, 0.4f, 0.09f) },
            length = 0.9f, sink = new Vector2(1.04f, 0.94f), decayScale = new Vector2(0.75f, 1.3f), detune = 0.05f,
            contactSeconds = new Vector2(0.01f, 0.025f), slapHz = 900f, slapQ = 0.8f, slapGain = 0.45f,
            crackHz = 1800f, crackSeconds = 0.01f, rattles = 8, rattleHz = 420f, brightnessTilt = 0.3f,
            forceRange = new Vector2(5f, 120f), speedRange = new Vector2(0.5f, 5f),
            bodyVolume = new Vector2(0.12f, 1f), crackVolume = new Vector2(0.03f, 0.25f),
            crackPitch = new Vector2(0.9f, 1.1f), bodyPitch = new Vector2(0.97f, 1.03f),
        };
    }
}
