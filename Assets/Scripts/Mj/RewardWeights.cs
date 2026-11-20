using UnityEngine;

namespace PoBox.Mj
{
    /// <summary>
    /// The reward's penalty weights, the game's copy of the boxing set in training/config/reward.json (the trainer's
    /// config; RewardWeightsTests keeps the two equal). Each term is charged per control step as -weight x term:
    /// actuator torque squared, joint speed squared, the pelvis's roll/pitch angular velocity squared, the squared
    /// action change and foot speed squared while the foot is down. In the game they only feed the Tab overlay's
    /// "episode reward" (<see cref="MjBoxer.EpisodeReward"/>): changing one here does not retrain any brain.
    /// Loaded from Resources/RewardWeights.
    /// </summary>
    [CreateAssetMenu(menuName = "PoBox/Reward Weights", fileName = "RewardWeights")]
    public sealed class RewardWeights : ScriptableObject
    {
        [Tooltip("Sum over joints of actuator torque squared, (N m)^2.")]
        [Min(0f)] public float torqueSq = 1e-6f;
        [Tooltip("Sum over joints of joint speed squared, (rad/s)^2.")]
        [Min(0f)] public float jointVelSq = 1e-4f;
        [Tooltip("Pelvis roll and pitch angular velocity squared, (rad/s)^2.")]
        [Min(0f)] public float angVelSq = 0.02f;
        [Tooltip("Squared change of the action from one control step to the next.")]
        [Min(0f)] public float actionRate = 0.01f;
        [Tooltip("Foot planar speed squared while the foot is on the floor, (m/s)^2, capped at 25 a foot.")]
        [Min(0f)] public float footSlip = 0.2f;

        static RewardWeights _loaded;

        /// <summary>The asset in Resources, or the defaults above when there is none.</summary>
        public static RewardWeights Current
        {
            get
            {
                if (_loaded == null) _loaded = Resources.Load<RewardWeights>("RewardWeights");
                if (_loaded == null) _loaded = CreateInstance<RewardWeights>();
                return _loaded;
            }
        }
    }
}
