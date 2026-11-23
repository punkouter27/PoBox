using System.IO;
using NUnit.Framework;
using PoBox.Mj;
using PoBox.Sim;
using UnityEngine;

namespace PoBox.EditorTests
{
    /// <summary>
    /// The walk-on moves (Resources/IntroClips.bytes, Training/tools/intro_clips.py) load in the game and name every
    /// shipped boxer's joints, in its order. tests/test_intro_clips.py checks the same file against the bodies in MuJoCo.
    /// </summary>
    public class IntroClipsTests
    {
        [Test]
        public void TheClipsLoadAndFitEveryBoxer()
        {
            IntroClips clips = IntroClips.Load();
            Assert.IsNotNull(clips, $"Resources/{IntroClips.ResourceName}.bytes");
            Assert.Greater(clips.Fps, 0f);
            Assert.Greater(clips.Names.Count, 0, "no clips");
            Assert.IsTrue(clips.Names.Exists(n => n.StartsWith(IntroMoves.CelebratePrefix)), "no celebration clips");
            Assert.IsTrue(clips.Names.Exists(n => !n.StartsWith(IntroMoves.CelebratePrefix)), "no walk-on clips");
            foreach (string name in clips.Names)
            {
                Assert.GreaterOrEqual(clips.Seconds(name), 2f, name);
                foreach (float v in clips.Rows(name)) Assert.IsFalse(float.IsNaN(v) || float.IsInfinity(v), name);
            }

            string boxers = Path.Combine(Application.dataPath, "_Project", "Agents", "Boxers");
            int checkedBoxers = 0;
            foreach (string dir in Directory.GetDirectories(boxers))
            {
                string path = Path.Combine(dir, "config.json");
                if (!File.Exists(path)) continue;
                BoxerConfig cfg = JsonUtility.FromJson<BoxerConfig>(File.ReadAllText(path));
                CollectionAssert.AreEqual(cfg.joint_order, clips.Joints, $"{cfg.name}: the clips' joints");
                checkedBoxers++;
            }
            Assert.Greater(checkedBoxers, 0, "no boxer configs found under " + boxers);
        }

        [Test]
        public void EveryBoxerHasASignatureMoveThatExists()
        {
            IntroClips clips = IntroClips.Load();
            Assert.IsNotNull(clips);
            var signed = new System.Collections.Generic.HashSet<string>();
            foreach (IntroMoves.BoxerMove m in IntroMoves.Signatures())
            {
                Assert.IsNotNull(clips.Rows(m.clip), $"{m.boxer}'s signature {m.clip} is not in the clip file");
                Assert.IsFalse(m.clip.StartsWith(IntroMoves.CelebratePrefix), $"{m.boxer}'s walk-on is a celebration");
                signed.Add(m.boxer.ToLowerInvariant());
            }
            string boxers = Path.Combine(Application.dataPath, "_Project", "Agents", "Boxers");
            foreach (string dir in Directory.GetDirectories(boxers))
            {
                string path = Path.Combine(dir, "config.json");
                if (!File.Exists(path)) continue;
                string name = JsonUtility.FromJson<BoxerConfig>(File.ReadAllText(path)).name.ToLowerInvariant();
                Assert.IsTrue(signed.Contains(name), name + " has no signature walk-on move");
            }
        }

        [Test]
        public void TheArenaPlaysThem()
        {
            // The walk-on component sits in the Arena scene beside the bout (authored there, not added at runtime).
            string scene = File.ReadAllText(Path.Combine(Application.dataPath, "_Project", "Scenes", "Arena.unity"));
            string guid = File.ReadAllText(Path.Combine(Application.dataPath, "_Project", "Core", "Scripts", "Runtime", "Sim", "IntroMoves.cs.meta"));
            guid = guid.Substring(guid.IndexOf("guid: ") + 6, 32);
            StringAssert.Contains(guid, scene, "Arena.unity has no IntroMoves");
        }
    }
}
