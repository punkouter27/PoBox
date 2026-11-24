using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using PoBox.Mj;
using PoBox.Sim;

namespace PoBox.Tests
{
    /// <summary>The free-for-all (Melee scene): every boxer in one MuJoCo ring, each measured against its own opponent.</summary>
    public class FreeForAllTests
    {
        /// <summary>An empty scene after every test, so nothing of this one is in the next fixture's way (TestScenes).</summary>
        [UnityEngine.TestTools.UnityTearDown]
        public System.Collections.IEnumerator LeaveNothingBehind() => TestScenes.Leave();

        // Fight time the melee gets for four boxers to land (a random melee needed 5 s to more than 91 s on 2026-11-23/24,
        // so a fixed 20 s window failed about one run in four), and the wall-clock limit around it for a busy PC.
        const float LandWithinSeconds = 240f, RealLimitSeconds = 600f;

        [UnityTest, Timeout(1500000)]
        public IEnumerator EveryBoxerFightsAndASlipIsCounted()
        {
            if (!Application.CanStreamedLevelBeLoaded(MatchSelection.MeleeScene)) Assert.Ignore("the build has no Melee scene");
            yield return SceneManager.LoadSceneAsync(MatchSelection.MeleeScene, LoadSceneMode.Single);
            var rules = Object.FindAnyObjectByType<FreeForAll>();
            Assert.IsNotNull(rules, "the Melee scene has no FreeForAll");
            rules.rated = false;        // a test's melee is not a result for the owner's ladder (FreeForAll.rated)
            // 20 s of GAME time for the intro (Time.deltaTime, capped per frame by Time.maximumDeltaTime), not wall-clock
            // time: on a busy PC the Melee scene's first load stalled for 115 s and the intro then ran at a few frames
            // a second, which used up a wall-clock budget before the intro had had its 20 s (2026-11-23).
            float introReal = Time.realtimeSinceStartup;
            for (float t = 0f; rules.Phase != FreeForAll.Stage.Fight && t < 20f && Time.realtimeSinceStartup - introReal < RealLimitSeconds; t += Time.deltaTime)
                yield return null;
            Assert.AreEqual(FreeForAll.Stage.Fight, rules.Phase, "the free-for-all never started");

            Fighter[] all = rules.fighters;
            Assert.AreEqual(FreeForAll.Players, all.Length, "a free-for-all is eight players (the tournament standard)");
            Assert.AreEqual(0, rules.WaitingCount, "nobody should wait off stage with eight seats in a ring of eight");
            Assert.AreEqual(all.Length, all.Select(f => f.boxer.prefix).Distinct().Count(), "two boxers share a prefix: their MuJoCo names collide");
            Assert.IsTrue(all.All(f => f.boxer.Bound), "a boxer was not bound to the compiled model");
            // Every brain hears the ring's punches, or the punches on that boxer are never counted (the twins, 2026-11-16).
            foreach (Fighter f in all)
                Assert.AreEqual(rules.ring, f.GetComponent<MjBrain>().ring, $"{f.displayName}'s brain is not wired to the ring: its punches are not counted");

            Time.timeScale = 4f;
            try
            {
                // Wait for the event, not a fixed window: until four different boxers have landed a punch, counting only
                // fight time (a 45 s round ends in a break, and the next round goes on), with a generous cap. A melee can
                // end early (on 2026-11-24 seven fell and were counted out in 20 s with one punch landed): then it is
                // restarted, and the boxers who landed before the restart still count, so the check stays "four boxers'
                // punches were measured against their opponents".
                float fought = 0f, realStart = Time.realtimeSinceStartup;
                var landed = new System.Collections.Generic.HashSet<Fighter>();
                int restarts = 0;
                while (Time.realtimeSinceStartup - realStart < RealLimitSeconds && fought < LandWithinSeconds)
                {
                    foreach (Fighter f in all) if (f.stats.landed > 0) landed.Add(f);
                    if (landed.Count >= 4) break;
                    if (rules.Phase == FreeForAll.Stage.Over)
                    {
                        if (restarts++ >= 3) break;
                        rules.Restart();
                    }
                    if (rules.Phase == FreeForAll.Stage.Fight) fought += Time.deltaTime;
                    yield return null;
                }
                int landing = landed.Count;
                foreach (Fighter f in all)
                    Assert.IsFalse(float.IsNaN(f.pelvis.transform.position.x), $"{f.displayName}'s body has gone to NaN");
                Debug.Log($"[FreeForAllTests] {landing} boxers landed a punch after {fought:0.0} s of fight (round {rules.Round}, {rules.Phase}, {restarts} restarts)");
                Assert.GreaterOrEqual(landing, 4, $"only {landing} boxers landed a punch in {fought:0} s of fight: punches are not being measured against each one's opponent");

                // A fall with no punch behind it is a knockdown too: the referee counts it. Always checked: in a break,
                // wait for the next round's bell (or a restarted melee's) first.
                for (float t = 0f; (rules.Phase == FreeForAll.Stage.Break || rules.Phase == FreeForAll.Stage.Intro) && t < 30f && Time.realtimeSinceStartup - realStart < RealLimitSeconds + 60f; t += Time.deltaTime)
                    yield return null;
                Assert.AreEqual(FreeForAll.Stage.Fight, rules.Phase, "no fight left to knock a boxer down in");
                Fighter victim = all.FirstOrDefault(f => !f.IsDown && !rules.IsOut(f));
                Assert.IsNotNull(victim, "nobody is left standing to knock down");
                victim.ForceDown();
                yield return null;
                Assert.IsFalse(rules.Slipped(victim), $"{victim.displayName} went down in the fight and was not counted");
                Assert.GreaterOrEqual(rules.CountOf(victim), 0, $"{victim.displayName} went down with no punch and nobody counted");
            }
            finally { Time.timeScale = 1f; }
        }
    }
}
