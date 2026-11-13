using System.Collections;
using System.Collections.Generic;
using Mujoco;
using NUnit.Framework;
using PoBox.Fx;
using PoBox.Mj;
using PoBox.Sim;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PoBox.Tests
{
    public unsafe class RefereeTests
    {
        [UnityTearDown]
        public IEnumerator LeaveNothingBehind() => TestScenes.Leave();

        [UnityTest]
        public IEnumerator RefereeCollidesWithActiveBoxerAndHasRealisticGeoms()
        {
            yield return SceneManager.LoadSceneAsync(MatchSelection.ArenaScene, LoadSceneMode.Single);
            for (float t = 0f; (Bout.Instance == null || Bout.Instance.Phase != BoutPhase.Fight) && t < 30f; t += Time.unscaledDeltaTime) yield return null;
            Assert.IsNotNull(Bout.Instance, "Arena scene has no Bout");

            Referee referee = Object.FindFirstObjectByType<Referee>();
            Assert.IsNotNull(referee, "Arena scene has no Referee");
            Assert.IsNotNull(referee.crew, "Referee has no MjCrew");
            for (float t = 0f; !referee.crew.Ready && t < 10f; t += Time.unscaledDeltaTime) yield return null;
            Assert.IsTrue(referee.crew.Ready, "Referee MjCrew did not bind");
            Assert.IsTrue(referee.crew.collideWithBoxers, "Referee MjCrew.collideWithBoxers must be true");

            foreach (MjGeom g in referee.crew.body.GetComponentsInChildren<MjGeom>())
            {
                if (g.name.EndsWith("glove_l") || g.name.EndsWith("glove_r"))
                    Assert.LessOrEqual(g.Sphere.Radius, 0.05f, $"{g.name} should match bare hand size");
                if (g.name.EndsWith("head_geom"))
                    Assert.LessOrEqual(g.Sphere.Radius, 0.105f, $"{g.name} should match bare human head size");
            }

            var refGeoms = new HashSet<int>(referee.crew.GeomIds);
            var redGeoms = new HashSet<int>();
            foreach (MjGeom g in Bout.Instance.red.GetComponentsInChildren<MjGeom>())
                if (g.MujocoId >= 0) redGeoms.Add(g.MujocoId);

            int contactsObserved = 0;
            double maxPenetration = 0.0;
            void CountContacts(object sender, MjStepArgs a)
            {
                for (int c = 0; c < a.data->ncon; c++)
                {
                    int g1 = a.data->contact[c].geom1, g2 = a.data->contact[c].geom2;
                    if ((refGeoms.Contains(g1) && redGeoms.Contains(g2)) || (refGeoms.Contains(g2) && redGeoms.Contains(g1)))
                    {
                        contactsObserved++;
                        double depth = -a.data->contact[c].dist;
                        if (depth > maxPenetration) maxPenetration = depth;
                    }
                }
            }

            referee.enabled = false;
            MjScene.Instance.postUpdateEvent += CountContacts;
            try
            {
                MjRing ring = Bout.Instance.red.ring;
                Vector3 c = new Vector3(ring.centre.x, 0f, ring.centre.y);
                ring.HoldPose(Bout.Instance.red.boxer, c, Vector3.right, null);
                ring.HoldPose(Bout.Instance.blue.boxer, c + new Vector3(-2f, 0f, -2f), Vector3.forward, null);
                Vector3 startPos = c + new Vector3(0.65f, 0f, 0f);
                referee.crew.Place(startPos, c - startPos);

                Bout.Instance.userTimeScale = 4f;
                float minDist = float.MaxValue;
                float deadline = Time.time + 2.5f;
                while (Time.time < deadline)
                {
                    referee.crew.Walk(0.9);
                    Vector3 refPos = referee.crew.Position;
                    Vector3 redPos = Bout.Instance.red.pelvis.transform.position;
                    refPos.y = 0f; redPos.y = 0f;
                    minDist = Mathf.Min(minDist, Vector3.Distance(refPos, redPos));
                    yield return new WaitForFixedUpdate();
                }

                Debug.Log($"Referee collision test: contactsObserved={contactsObserved}, maxPenetration={maxPenetration * 100:F1} cm, minDist={minDist:F3} m");
                Assert.Greater(contactsObserved, 0, "Expected physical MuJoCo contact between Referee and standing boxer");
                Assert.Less(maxPenetration, 0.10, $"Referee passed too deeply into boxer ({maxPenetration * 100:F1} cm)");
                Assert.Greater(minDist, 0.15f, $"Referee passed through boxer trunk center (minDist={minDist:F3} m)");
            }
            finally
            {
                MjScene.Instance.postUpdateEvent -= CountContacts;
                Bout.Instance.userTimeScale = 1f;
            }
        }

        [UnityTest]
        public IEnumerator RefereeStandsAwayFromBoxingAction()
        {
            yield return SceneManager.LoadSceneAsync(MatchSelection.ArenaScene, LoadSceneMode.Single);
            for (float t = 0f; (Bout.Instance == null || Bout.Instance.Phase != BoutPhase.Fight) && t < 30f; t += Time.unscaledDeltaTime) yield return null;
            Assert.IsNotNull(Bout.Instance, "Arena scene has no Bout");

            Referee referee = Object.FindFirstObjectByType<Referee>();
            Assert.IsNotNull(referee, "Arena scene has no Referee");
            for (float t = 0f; !referee.crew.Ready && t < 10f; t += Time.unscaledDeltaTime) yield return null;

            Bout.Instance.autoAdvance = true;
            Bout.Instance.resultsSeconds = 0.5f;
            Bout.Instance.userTimeScale = 4f;

            try
            {
                int samples = 0, clearSamples = 0;
                double sumNearest = 0.0;
                float simEnd = Time.time + 12.0f;
                float maxExtent = 0f;
                int uprightSamples = 0;
                MjRing ring = Bout.Instance.red.ring;
                Vector3 centre = new Vector3(ring.centre.x, 0f, ring.centre.y);

                // Warm up 1.5s after Place() so he reaches his fight stance first.
                Transform refPelvis = referee.crew.body.transform.Find(referee.crew.body.prefix + "pelvis");
                float warmEnd = Time.time + 1.5f;
                while (Time.time < warmEnd) yield return new WaitForFixedUpdate();

                while (Time.time < simEnd)
                {
                    yield return new WaitForFixedUpdate();
                    if (Bout.Instance.Phase != BoutPhase.Fight) continue;

                    Vector3 r = Bout.Instance.red.pelvis.transform.position;
                    Vector3 b = Bout.Instance.blue.pelvis.transform.position;
                    Vector3 p = referee.crew.Position;
                    if (refPelvis != null && refPelvis.position.y > 0.60f) uprightSamples++;
                    r.y = 0f; b.y = 0f; p.y = 0f;

                    float nearest = Mathf.Min(Vector3.Distance(p, r), Vector3.Distance(p, b));
                    sumNearest += nearest;
                    if (nearest >= 1.25f) clearSamples++;
                    maxExtent = Mathf.Max(maxExtent, Mathf.Abs(p.x - centre.x), Mathf.Abs(p.z - centre.z));
                    samples++;
                }

                Assert.Greater(samples, 50, "Expected sufficient Fight samples");
                double meanNearest = sumNearest / samples;
                double clearFraction = (double)clearSamples / samples;
                Debug.Log($"Referee clearance: mean={meanNearest:F2} m, >=1.25m fraction={clearFraction:P1}, upright={(double)uprightSamples / samples:P1}, maxExtent={maxExtent:F2} m (ringHalf={ring.ringHalf:F2} m)");

                Assert.Greater(meanNearest, 1.45, $"Referee hovered too close to fighters (mean nearest={meanNearest:F2} m)");
                Assert.Greater(clearFraction, 0.70, $"Referee spent too long near fighters (>=1.25m fraction={clearFraction:P1})");
                Assert.Greater((double)uprightSamples / samples, 0.85, "Referee fell too frequently while avoiding action");
                Assert.Less(maxExtent, ring.ringHalf, "Referee exited the ring bounds");
            }
            finally
            {
                Bout.Instance.userTimeScale = 1f;
            }
        }
    }
}
