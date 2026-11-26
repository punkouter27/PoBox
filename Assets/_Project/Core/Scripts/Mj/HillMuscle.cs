using System;

namespace PoBox.Mj
{
    /// <summary>
    /// Training/envs/muscle.py's Hill muscle for one joint, number for number (its hill_target_scalar is the reference, and
    /// the EditMode test HillMuscleTests holds both to the same values). Once a control step: the excitation is what the PD
    /// target asks of the muscle, kp (target - q) / (F peak) held to [-1, 1]; the activation follows it with a time constant
    /// tau_s x tau_scale; the torque is activation x F x peak x force-length(q) x force-velocity(qdot) x scale; and the
    /// target given to the drive is q + torque / kp, its damping untouched.
    /// </summary>
    public static class HillMuscle
    {
        /// <summary>Strength over the joint's range: 1 at the middle, exp(-(1 / width)^2) at either end.</summary>
        public static double ForceLength(double q, double lo, double hi, double width)
        {
            double x = (2.0 * q - lo - hi) / Math.Max(hi - lo, 1e-6);
            return Math.Exp(-(x / width) * (x / width));
        }

        /// <summary>v: joint speed along the torque over the speed limit, positive shortening. Nothing at the limit.</summary>
        public static double ForceVelocity(double v, double k, double ecc)
        {
            if (v >= 0.0) return Math.Max(0.0, (1.0 - v) / (1.0 + v / k));
            double w = Math.Min(-v, 1.0);
            return ecc - (ecc - 1.0) * (1.0 - w) / (1.0 + w / k);
        }

        /// <summary>The drive's target for this control step; <paramref name="activation"/> is the joint's state, advanced by dt.</summary>
        public static double Target(double want, double q, double qd, ref double activation, double kp, double force, double vmax,
            double lo, double hi, double scale, double dt, MuscleConfig p)
        {
            double top = force * p.peak;
            double u = Math.Max(-1.0, Math.Min(1.0, kp * (want - q) / top));
            activation += (u - activation) * (1.0 - Math.Exp(-dt / (p.tau_s * p.tau_scale)));
            double sign = activation > 0.0 ? 1.0 : activation < 0.0 ? -1.0 : 0.0;   // Math.Sign throws on NaN
            double v = sign * qd / Math.Max(vmax, 1e-3);
            double torque = activation * top * ForceLength(q, lo, hi, p.fl_width) * ForceVelocity(v, p.fv_k, p.fv_ecc) * scale;
            return q + torque / kp;
        }
    }
}
