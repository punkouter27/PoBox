using NUnit.Framework;
using PoBox.Mj;
using UnityEngine;

namespace PoBox.EditorTests
{
    /// <summary>
    /// The game's Hill muscle (Mj/HillMuscle.cs) against the trainer's (Training/envs/muscle.py hill_target_scalar): the
    /// same four joints give the same targets and activations. tests/test_muscle.py holds the Python side to these numbers.
    /// </summary>
    public class HillMuscleTests
    {
        static MuscleConfig P(double peak = 1.0, double tauScale = 1.0, double flWidth = 1.6, double fvEcc = 1.3) =>
            new MuscleConfig { model = "hill", peak = peak, tau_scale = tauScale, fl_width = flWidth, fv_ecc = fvEcc };

        [TestCase(0.5, 0.2, 1.0, 0.0, 300, 150, 10, -1.0, 1.5, 1.0, 1.0, 1.0, 1.6, 1.3, 0.29378235189595436, 0.29194972858044477)]
        [TestCase(-0.3, 0.4, -6.0, 0.2, 200, 80, 8, -0.5, 2.0, 0.8, 0.6, 1.5, 1.6, 1.3, 0.3973164526145441, -0.23058353388405445)]
        [TestCase(0.0, 0.1, 3.0, -0.5, 250, 120, 12, -1.2, 0.9, 1.0, 1.0, 1.0, 1.6, 1.3, -0.09963550187926307, -0.35807999305117266)]
        [TestCase(1.2, -0.4, 9.0, 0.9, 400, 200, 9, -1.5, 1.5, 0.5, 1.0, 1.0, 1.2, 1.5, -0.4, 0.9486582880967408)]
        public void SameAsTheTrainer(double want, double q, double qd, double a, double kp, double force, double vmax, double lo,
            double hi, double scale, double peak, double tauScale, double flWidth, double fvEcc, double target, double activation)
        {
            double act = a;
            double got = HillMuscle.Target(want, q, qd, ref act, kp, force, vmax, lo, hi, scale, 0.02, P(peak, tauScale, flWidth, fvEcc));
            Assert.AreEqual(target, got, 1e-9, "target");
            Assert.AreEqual(activation, act, 1e-9, "activation");
        }

        [Test]
        public void NothingAtTheSpeedLimitAndNoBlockMeansNoMuscle()
        {
            Assert.AreEqual(0.0, HillMuscle.ForceVelocity(1.0, 0.25, 1.3), 1e-12);
            Assert.AreEqual(1.0, HillMuscle.ForceVelocity(0.0, 0.25, 1.3), 1e-12);
            Assert.AreEqual(1.3, HillMuscle.ForceVelocity(-1.0, 0.25, 1.3), 1e-12);
            var config = JsonUtility.FromJson<BoxerConfig>("{\"name\":\"old\",\"speed_limit\":true}");
            Assert.IsFalse(config.Hill, "a config without a muscle block drives as before");
            var hill = JsonUtility.FromJson<BoxerConfig>("{\"name\":\"new\",\"muscle\":{\"model\":\"hill\",\"peak\":0.7}}");
            Assert.IsTrue(hill.Hill);
            Assert.AreEqual(0.7, hill.muscle.peak, 1e-12);
            Assert.AreEqual(0.03, hill.muscle.tau_s, 1e-12, "a field left out keeps its default");
        }
    }
}
