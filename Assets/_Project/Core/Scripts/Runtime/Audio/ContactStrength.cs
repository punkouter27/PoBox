using UnityEngine;

namespace PoBox.Audio
{
    /// <summary>
    /// How hard a contact was, as one number, 0 to 1 (2026-11-25). The sound, the spray of sweat and the buzz of
    /// the phone each used to scale a punch by their own denominator, so a shot could sound heavy and feel light.
    /// This is the one reading they share: the impulse against the impulse that counts as a full-power hit.
    ///
    /// A surface can make a contact read harder than its impulse alone says (<see cref="SurfaceMaterial"/>); with no
    /// surface assets in the project it does not, and every call reads as it did before.
    /// </summary>
    public static class ContactStrength
    {
        /// <summary>Impulse (N s) of a hit that counts as full power. The shipped modal set's force range tops out at 22.</summary>
        public const float Full = 22f;

        /// <summary>0..1 of a hit of <paramref name="impulse"/> N s.</summary>
        public static float Of(float impulse) => Of(impulse, Full);

        /// <summary>0..1 against a denominator of the caller's own (a system tuned to a different ceiling).</summary>
        public static float Of(float impulse, float full) => Mathf.Clamp01(impulse / Mathf.Max(1f, full));

        /// <summary>0..1 of a punch landing on <paramref name="kind"/>, read through that surface's hardness.</summary>
        public static float On(ContactKind kind, float impulse) => Mathf.Clamp01(Of(impulse) * SurfaceMaterial.Hardness(kind));
    }
}
