using UnityEngine;

namespace PoBox.Fx
{
    /// <summary>
    /// Whether the picture is allowed to move for the sake of it (2026-11-25): the screen ripples, the wipes
    /// between screens, the camera jolts and the punch pulse. A broadcast whose whole picture shakes on every
    /// heavy shot is a real accessibility problem on a phone, so this is a setting of its own (SettingsPage,
    /// MOTION), kept on the device like the others. Turned off, everything that exists only to be dramatic
    /// stops; the sound, the fight and the picture itself are untouched.
    ///
    /// Named PictureMotion rather than Motion: UnityEngine already has a Motion (an animation clip).
    /// </summary>
    public static class PictureMotion
    {
        /// <summary>True by default: ripples, wipes, jolts and pulses play.</summary>
        public static bool Enabled = true;

        /// <summary>False when the player asked for a still picture.</summary>
        public static bool Reduced => !Enabled;
    }
}
