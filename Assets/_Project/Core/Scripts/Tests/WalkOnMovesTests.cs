using System.Collections;
using System.IO;
using NUnit.Framework;
using PoBox.Mj;
using PoBox.Sim;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PoBox.Tests
{
    /// <summary>
    /// The walk-on moves (Sim/IntroMoves.cs) in the real Arena: through the walk-on both boxers move their arms away
    /// from the guard and keep moving, stay in their corners on their feet, and when the walk-on ends the bout has
    /// them back in their guards on their marks, as before. Writes the picture at 1600x900 and 450x800 to
    /// Build/Shots/walkon_*.png (not versioned) for looking at.
    /// </summary>
    public class WalkOnMovesTests
    {
        [UnityTearDown]
        public IEnumerator LeaveNothingBehind() => TestScenes.Leave();

        [UnityTest]
        public IEnumerator BoxersShadowBoxThroughTheWalkOnAndStartTheRoundInTheirGuards()
        {
            yield return SceneManager.LoadSceneAsync("Arena", LoadSceneMode.Single);
            Bout bout = null;
            for (int i = 0; i < 600 && (bout == null || bout.Phase != BoutPhase.WalkOn); i++) { yield return null; bout = Bout.Instance; }
            Assert.IsNotNull(bout, "the Arena scene has no Bout");
            Assert.AreEqual(BoutPhase.WalkOn, bout.Phase, "the bout did not open with a walk-on");
            IntroMoves moves = Object.FindAnyObjectByType<IntroMoves>();
            Assert.IsNotNull(moves, "the Arena scene has no IntroMoves");
            bout.walkOnSeconds = Mathf.Max(bout.walkOnSeconds, 4f);

            Fighter[] fighters = { bout.red, bout.blue };
            var start = new Vector3[2];
            for (int k = 0; k < 2; k++) start[k] = Pelvis(fighters[k]);

            // Past the blend in: the arms are off the guard, and a second on they are somewhere else again.
            yield return Real(1.2f, bout);
            Assert.AreEqual(BoutPhase.WalkOn, bout.Phase, "the walk-on ended too soon to look at");
            Assert.IsNotNull(moves.RedClip, "red has no walk-on clip");
            Assert.IsNotNull(moves.BlueClip, "blue has no walk-on clip");
            var first = new double[2][];
            for (int k = 0; k < 2; k++)
            {
                first[k] = Arms(fighters[k].boxer);
                Assert.Greater(Off(first[k], Guard(fighters[k].boxer)), 0.15, fighters[k].displayName + " is still in its guard");
            }
            Shot(bout, "walkon_red_1600x900", 1600, 900);
            Shot(bout, "walkon_red_450x800", 450, 800);
            yield return Real(1.0f, bout);
            Shot(bout, "walkon_blue_1600x900", 1600, 900);
            Shot(bout, "walkon_blue_450x800", 450, 800);
            for (int k = 0; k < 2; k++)
            {
                Fighter f = fighters[k];
                Assert.Greater(Off(Arms(f.boxer), first[k]), 0.05, f.displayName + " stopped moving");
                Vector3 now = Pelvis(f);
                Assert.Less(new Vector2(now.x - start[k].x, now.z - start[k].z).magnitude, 0.8f, f.displayName + " left its corner");
                Assert.Greater(f.PelvisHeight, f.standingPelvisHeight * 0.6f, f.displayName + " is not on its feet");
                Assert.Less(f.PelvisHeight, f.standingPelvisHeight * 1.25f, f.displayName + " has left the canvas");
            }

            // The walk-on over: the round starts from the guard, as it did before there were moves.
            for (int i = 0; i < 1200 && bout.Phase == BoutPhase.WalkOn; i++) yield return null;
            Assert.AreNotEqual(BoutPhase.WalkOn, bout.Phase, "the walk-on never ended");
            Assert.IsNull(moves.RedClip, "the moves went on past the walk-on");
            foreach (Fighter f in fighters)
            {
                Assert.Less(Off(Arms(f.boxer), Guard(f.boxer)), 0.1, f.displayName + " did not start the round in its guard");
                Assert.IsFalse(f.IsDown, f.displayName + " started the round down");
            }
        }

        static IEnumerator Real(float seconds, Bout bout)
        {
            float until = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < until && bout.Phase == BoutPhase.WalkOn) yield return null;
        }

        static Vector3 Pelvis(Fighter f) => f.pelvis != null ? f.pelvis.transform.position : f.transform.position;

        static readonly string[] ArmJoints = { "shoulder_x_l", "shoulder_z_l", "elbow_l", "shoulder_x_r", "shoulder_z_r", "elbow_r" };

        static double[] Arms(MjBoxer b)
        {
            var q = new double[ArmJoints.Length];
            for (int i = 0; i < q.Length; i++) q[i] = b.JointPosition(System.Array.IndexOf(b.Config.joint_order, ArmJoints[i]));
            return q;
        }

        static double[] Guard(MjBoxer b)
        {
            var q = new double[ArmJoints.Length];
            for (int i = 0; i < q.Length; i++) q[i] = b.Config.default_joint_pos[System.Array.IndexOf(b.Config.joint_order, ArmJoints[i])];
            return q;
        }

        /// <summary>The largest gap between two sets of arm angles, radians.</summary>
        static double Off(double[] a, double[] b)
        {
            double most = 0;
            for (int i = 0; i < a.Length; i++) most = System.Math.Max(most, System.Math.Abs(a[i] - b[i]));
            return most;
        }

        static void Shot(Bout bout, string name, int w, int h)
        {
            Camera cam = Camera.main;
            if (cam == null) return;
            var rt = new RenderTexture(w, h, 24);
            RenderTexture before = cam.targetTexture;
            float aspect = cam.aspect;
            cam.targetTexture = rt;
            cam.aspect = (float)w / h;
            cam.Render();
            cam.targetTexture = before;
            cam.aspect = aspect;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            string dir = Path.Combine(Application.dataPath, "..", "Build", "Shots");
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
            Object.Destroy(tex);
            rt.Release();
            Object.Destroy(rt);
        }
    }
}
