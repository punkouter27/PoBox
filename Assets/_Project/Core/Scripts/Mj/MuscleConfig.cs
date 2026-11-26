using System;

namespace PoBox.Mj
{
    /// <summary>
    /// A boxer config's "muscle" block (Training/envs/muscle.py, train_box.py --muscle hill): a Hill-type muscle on each
    /// joint drive. A config without the block, every brain shipped before 2026-11-25, has none: <see cref="Hill"/> is false
    /// and MjBoxer drives exactly as before.
    /// </summary>
    [Serializable]
    public class MuscleConfig
    {
        /// <summary>"hill", or empty for no muscle model.</summary>
        public string model = "";
        /// <summary>Share of force_limit the muscle gives at best, and its activation time constant (s) times tau_scale: age.</summary>
        public double peak = 1.0, tau_s = 0.03, tau_scale = 1.0;
        /// <summary>The force-length width over half the joint range, Hill's curvature, and the lengthening force over isometric.</summary>
        public double fl_width = 1.6, fv_k = 0.25, fv_ecc = 1.3;

        public bool Hill => model == "hill";
    }
}
