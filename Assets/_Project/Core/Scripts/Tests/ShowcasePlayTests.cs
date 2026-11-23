using System.Collections;
using NUnit.Framework;
using PoBox.Mj;
using PoBox.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PoBox.Tests
{
    /// <summary>
    /// The Po game standard's runtime pieces, run for real: every reward penalty term is charged while a boxer stands
    /// and is shoved on the Testbed; the Tab overlay shows FPS, reward, inference time and curriculum stage and its
    /// benchmark button measures local sim speed; demo (attract) mode starts after 30 s without input and any input ends it.
    /// </summary>
    public class ShowcasePlayTests
    {
        const string Testbed = "Assets/_Project/Scenes/Testbed.unity";
        const string Menu = "Assets/_Project/Scenes/Menu.unity";

        [UnityTearDown]
        public IEnumerator LeaveNothingBehind() => TestScenes.Leave();

        static IEnumerator Load(string scene)
        {
#if UNITY_EDITOR
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(scene, new LoadSceneParameters(LoadSceneMode.Single));
#endif
            yield return null;
            yield return new WaitForFixedUpdate();
        }

        static IEnumerator Matt()
        {
            MjTestbed.Pick = "Matt";
            yield return Load(Testbed);
            var testbed = Object.FindAnyObjectByType<MjTestbed>();
            Assert.IsTrue(testbed != null && testbed.Ready, "the testbed's boxer is not bound to the model");
        }

        [UnityTest]
        public IEnumerator EveryRewardPenaltyTermIsCharged()
        {
            yield return Matt();
            var testbed = Object.FindAnyObjectByType<MjTestbed>();
            testbed.shoves = true;
            MjBoxer boxer = testbed.boxer;
            boxer.ResetEpisodeReward();
            float until = Time.time + 4f;
            while (Time.time < until) yield return null;
            double[] t = boxer.EpisodeTerms;
            for (int i = 0; i < MjBoxer.PenaltyTerms.Length; i++)
                Debug.Log($"[ShowcasePlayTests] {MjBoxer.PenaltyTerms[i]} = {t[i]:E3} over {boxer.PenaltySteps} control steps");
            Assert.Greater(boxer.PenaltySteps, 50, "control steps charged");
            for (int i = 0; i < MjBoxer.PenaltyTerms.Length; i++)
            {
                Assert.IsTrue(double.IsFinite(t[i]), MjBoxer.PenaltyTerms[i]);
                Assert.Less(t[i], 0.0, MjBoxer.PenaltyTerms[i] + " was never charged");
            }
            double sum = 0; foreach (double x in t) sum += x;
            Assert.AreEqual(sum, boxer.EpisodeReward, 1e-9 * System.Math.Max(1.0, System.Math.Abs(sum)), "the episode reward is the sum of its terms");
        }

        [UnityTest]
        public IEnumerator TabOverlayShowsTheReadoutAndBenchmarksTheSim()
        {
            yield return Matt();
            DiagOverlay overlay = DiagOverlay.Instance;
            Assert.IsNotNull(overlay, "DemoMode boots the overlay with the first scene");
            if (!overlay.Visible) overlay.Toggle();
            float until = Time.unscaledTime + 1f;
            while (Time.unscaledTime < until) yield return null;
            string text = overlay.ReadoutText;
            Debug.Log("[ShowcasePlayTests] overlay:\n" + text);
            StringAssert.Contains("FPS", text);
            StringAssert.Contains("Inference", text);
            StringAssert.Contains("reward", text);
            StringAssert.Contains("stage R0-R2", text);

            SimBenchmark bench = overlay.StartBenchmark();
            Assert.IsNotNull(bench);
            until = Time.unscaledTime + bench.Seconds + 5f;
            while (!bench.Done && Time.unscaledTime < until) yield return null;
            Debug.Log("[ShowcasePlayTests] " + bench.Summary());
            Assert.IsTrue(bench.Done, "the benchmark is bounded");
            Assert.IsNull(bench.Error, bench.Error);
            Assert.Greater(bench.StepsPerSecond, 100.0, "physics steps a second");
            overlay.Toggle();
        }

        [UnityTest]
        public IEnumerator DemoModeStartsAfterThirtySecondsIdleAndInputEndsIt()
        {
            ShowcaseSettings s = ShowcaseSettings.Current;
            bool was = s.runInEditor;
            DemoMode.Touched();     // the idle clock from now, so the demo does not start the moment it is allowed
            s.runInEditor = true;
            try
            {
                yield return Load(Menu);
                Assert.AreEqual(30f, s.idleSeconds, 1e-6f);
                DemoMode.Touched();
                float start = Time.unscaledTime, limit = s.idleSeconds + 60f;     // real time, for a stalled clock
                // The idle clock counts frames of up to DemoMode.LongFrameSeconds, so it is read rather than the wall clock.
                while (DemoMode.IdleSeconds < s.idleSeconds - 1f && Time.unscaledTime - start < limit) { Assert.IsFalse(DemoMode.Active, $"demo started after {DemoMode.IdleSeconds:0.0} s idle"); yield return null; }
                while (!DemoMode.Active && DemoMode.IdleSeconds < s.idleSeconds + 1f && Time.unscaledTime - start < limit) yield return null;
                Assert.IsTrue(DemoMode.Active, $"demo did not start after 30 s idle (idle clock {DemoMode.IdleSeconds:0.0} s)");
                Debug.Log($"[ShowcasePlayTests] demo on after {DemoMode.IdleSeconds:0.0} s idle ({Time.unscaledTime - start:0.0} s real)");
                DemoMode.Touched();
                Assert.IsFalse(DemoMode.Active, "input ends the demo");
            }
            finally
            {
                s.runInEditor = was;
                DemoMode.Stop();
            }
        }
    }
}
