using System.IO;
using NUnit.Framework;
using PoBox.Mj;
using PoBox.UI;
using UnityEngine;

namespace PoBox.EditorTests
{
    /// <summary>
    /// The editor-tunable assets behind the Tab overlay and demo mode: the reward weights are the trainer's
    /// (Training/config/reward.json, boxing set), and the showcase settings hold what the Po game standard asks.
    /// </summary>
    public class ShowcaseTests
    {
        [System.Serializable]
        class Weights { public float torque_sq = -1f, joint_vel_sq = -1f, ang_vel_sq = -1f, action_rate = -1f, foot_slip = -1f; }

        [System.Serializable]
        class TrainingConfig { public Weights boxing; }

        [Test]
        public void RewardWeightsAreTheTrainingConfigs()
        {
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Training", "config", "reward.json"));
            Assert.IsTrue(File.Exists(path), path);
            Weights want = JsonUtility.FromJson<TrainingConfig>(File.ReadAllText(path)).boxing;
            RewardWeights have = Resources.Load<RewardWeights>("RewardWeights");
            Assert.IsNotNull(have, "Resources/RewardWeights.asset");
            Assert.AreEqual(want.torque_sq, have.torqueSq, 1e-9, "torque_sq");
            Assert.AreEqual(want.joint_vel_sq, have.jointVelSq, 1e-9, "joint_vel_sq");
            Assert.AreEqual(want.ang_vel_sq, have.angVelSq, 1e-9, "ang_vel_sq");
            Assert.AreEqual(want.action_rate, have.actionRate, 1e-9, "action_rate");
            Assert.AreEqual(want.foot_slip, have.footSlip, 1e-9, "foot_slip");
        }

        [Test]
        public void ShowcaseSettingsMeetTheStandard()
        {
            ShowcaseSettings s = Resources.Load<ShowcaseSettings>("ShowcaseSettings");
            Assert.IsNotNull(s, "Resources/ShowcaseSettings.asset");
            Assert.AreEqual(30f, s.idleSeconds, 1e-6, "idle threshold");
            Assert.That(s.cameraDwellSeconds, Is.InRange(6f, 8f), "each showcase shot is held 6-8 s");
            Assert.GreaterOrEqual(s.cameras.Length, 3, "the demo cycles several shots");
            Assert.IsNotNull(s.panelSettings, "the overlay's panel");
            Assert.IsFalse(s.runInEditor, "the demo must not take over editor sessions and tests");
        }

        [Test]
        public void DemoIdleClockIgnoresLongFrames()
        {
            DemoMode.Touched();
            try
            {
                for (int i = 0; i < 10; i++) DemoMode.Advance(0.1f);
                Assert.AreEqual(1f, DemoMode.IdleSeconds, 1e-4f, "ordinary frames count");
                DemoMode.Advance(5f);     // a 5 s hitch: a scene load, a stall, a tab in the background
                Assert.AreEqual(1f, DemoMode.IdleSeconds, 1e-4f, "a long frame counted as idle time");
                DemoMode.Advance(DemoMode.LongFrameSeconds);
                Assert.AreEqual(1f + DemoMode.LongFrameSeconds, DemoMode.IdleSeconds, 1e-4f, "a 0.25 s frame still counts");
                DemoMode.Touched();
                for (int i = 0; i < 200; i++) DemoMode.Advance(0.3f);     // 60 s of 0.3 s frames
                Assert.AreEqual(0f, DemoMode.IdleSeconds, 1e-6f, "long frames alone never reach the 30 s threshold");
                for (int i = 0; i < 1800; i++) DemoMode.Advance(1f / 60f);
                Assert.AreEqual(30f, DemoMode.IdleSeconds, 1e-2f, "30 s of 60 fps frames is 30 s idle");
                DemoMode.Touched();
                Assert.AreEqual(0f, DemoMode.IdleSeconds, "input restarts the clock");
            }
            finally { DemoMode.Touched(); }
        }
    }
}
