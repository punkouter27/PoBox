using System.Collections.Generic;
using UnityEngine;

namespace PoBox.Audio
{
    /// <summary>
    /// One surface a boxer's body meets (2026-11-25): the canvas, a glove, a head, a trunk. <see cref="ContactKind"/>
    /// used to be an audio-only enum; this is the one place the surface's physical and visual numbers live, so the
    /// sound, the dust and the buzz cannot drift apart when a surface is retuned.
    ///
    /// The assets are optional and are found by their <see cref="kind"/> (one asset per kind). With none in the
    /// project every surface reads as it did: <see cref="hardness"/> 1 and white dust.
    /// </summary>
    [CreateAssetMenu(menuName = "PoBox/Audio/Surface Material", fileName = "Surface")]
    public class SurfaceMaterial : ScriptableObject
    {
        [Tooltip("Which contact this describes. One asset per kind.")]
        public ContactKind kind;

        [Tooltip("How much harder a contact with this surface reads than its impulse alone says (1: the impulse is the whole story). Head and glove surfaces are firmer than the canvas.")]
        [Range(0.25f, 4f)] public float hardness = 1f;

        [Tooltip("The colour of what comes off this surface when it is struck (the canvas raises a pale dust).")]
        public Color dustColour = Color.white;

        static readonly Dictionary<ContactKind, SurfaceMaterial> ByKind = new Dictionary<ContactKind, SurfaceMaterial>();

        /// <summary>The asset for <paramref name="kind"/>, or null when the project has none (read as it always did).</summary>
        public static SurfaceMaterial Of(ContactKind kind) => ByKind.TryGetValue(kind, out SurfaceMaterial m) ? m : null;

        /// <summary>How much harder a contact with <paramref name="kind"/> reads than its impulse says (1 with no asset).</summary>
        public static float Hardness(ContactKind kind) => Of(kind) != null ? Of(kind).hardness : 1f;

        void OnEnable() => ByKind[kind] = this;

        void OnDisable()
        {
            if (ByKind.TryGetValue(kind, out SurfaceMaterial m) && m == this) ByKind.Remove(kind);
        }
    }
}
