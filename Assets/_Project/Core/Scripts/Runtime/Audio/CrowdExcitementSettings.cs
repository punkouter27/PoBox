using UnityEngine;

namespace PoBox.Audio
{
    /// <summary>
    /// The reactive crowd's tunables (2026-11-25, <see cref="CrowdExcitement"/>): what makes the hall excited, how
    /// fast it gets there and calms down, and how that is heard (bed and roar layer) and seen (the crowd shader's
    /// motion, global _PoBoxCrowdExcite).
    /// </summary>
    [CreateAssetMenu(menuName = "PoBox/Audio/Crowd Excitement Settings", fileName = "CrowdExcitementSettings")]
    public class CrowdExcitementSettings : ScriptableObject
    {
        [Header("Punch rate")]
        [Tooltip("Seconds of punches counted.")]
        public float windowSeconds = 6f;
        [Tooltip("Punches a second (thrown, both boxers) that count as a full-on exchange.")]
        public float fullRate = 2.4f;
        [Tooltip("A landed punch counts as this many thrown ones.")]
        public float landedWeight = 2f;
        [Tooltip("Share of the excitement the punch rate can make on its own.")]
        [Range(0f, 1f)] public float rateWeight = 0.4f;

        [Header("Big hits")]
        [Tooltip("A clean hit harder than this times what these two usually land (Excitement.UsualImpulse) is big.")]
        public float bigHitOverUsual = 1.25f;
        [Tooltip("Without an Excitement reading: a clean hit this hard (N s) is big.")]
        public float bigHitImpulse = 14f;
        [Tooltip("The spike a big hit adds, at twice the threshold and above.")]
        [Range(0f, 1.5f)] public float bigHitSpike = 0.5f;

        [Header("Near knockdowns, knockdowns, the bell")]
        [Tooltip("Daze (0..1) of the worse-off boxer on its feet from which the hall smells it.")]
        [Range(0f, 1f)] public float dazeFrom = 0.45f;
        [Tooltip("Share of the excitement a boxer out on its feet makes.")]
        [Range(0f, 1f)] public float dazeWeight = 0.45f;
        [Range(0f, 1.5f)] public float knockdownSpike = 1.2f;
        [Range(0f, 1.5f)] public float gotUpSpike = 0.5f;
        [Tooltip("At least this while a count is on.")]
        [Range(0f, 1f)] public float countFloor = 0.8f;
        [Range(0f, 1.5f)] public float roundStartSpike = 0.25f;
        [Range(0f, 1.5f)] public float roundEndSpike = 0.55f;
        [Range(0f, 1.5f)] public float resultSpike = 1f;
        [Tooltip("Seconds for a spike to fall to a third.")]
        public float spikeDecaySeconds = 2.2f;

        [Header("Response")]
        [Tooltip("The hall between rounds and before the fight.")]
        [Range(0f, 1f)] public float idleLevel = 0.12f;
        [Tooltip("How quickly it rises to and falls from what is happening, a second.")]
        public float attackPerSecond = 5f, releasePerSecond = 0.35f;

        [Header("Heard")]
        [Tooltip("The crowd bed's level (0..1, between AudioDirector's crowdQuiet and crowdLoud) by excitement.")]
        public AnimationCurve bed = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.5f, 0.3f), new Keyframe(1f, 1f));
        [Tooltip("The roar layer's level (0..1 of AudioDirector's roarVolume) by excitement.")]
        public AnimationCurve roar = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.5f, 0f), new Keyframe(0.8f, 0.6f), new Keyframe(1f, 1f));
        [Tooltip("Crossing up through this fires one roar swell.")]
        [Range(0f, 1f)] public float swellAt = 0.82f;
        [Tooltip("Seconds between swells.")]
        public float swellCooldown = 5f;

        [Header("Seen")]
        [Tooltip("The crowd shader's motion reading (global _PoBoxCrowdExcite, 0..1) by excitement.")]
        public AnimationCurve motion = AnimationCurve.Linear(0f, 0f, 1f, 1f);
    }
}
