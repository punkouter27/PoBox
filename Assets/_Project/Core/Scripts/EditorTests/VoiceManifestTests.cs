using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using PoBox.Audio;
using UnityEngine;

namespace PoBox.EditorTests
{
    /// <summary>
    /// The voice manifest (Resources/Voices/voices.json) and the procedural imitation behind every WAV not yet
    /// recorded: the manifest lists every roster boxer by 5 emotions by 5 levels with the file rule, a voice and
    /// true exists flags; the imitation is non-silent, grows with the level, differs by boxer and is deterministic.
    /// </summary>
    public class VoiceManifestTests
    {
        static string ProjectPath(params string[] parts) => Path.GetFullPath(Path.Combine(Application.dataPath, "..", Path.Combine(parts)));

        static VoiceManifest OnDisk()
        {
            string path = ProjectPath(VoiceManifest.Root, "voices.json");
            Assert.IsTrue(File.Exists(path), path);
            VoiceManifest m = VoiceManifest.Parse(File.ReadAllText(path));
            Assert.IsNotNull(m, "voices.json is not a " + VoiceManifest.Schema);
            return m;
        }

        /// <summary>The roster: every folder under Agents/Boxers with a config.json, as the generator reads it.</summary>
        static List<string> Roster()
        {
            var names = new List<string>();
            foreach (string dir in Directory.GetDirectories(ProjectPath("Assets", "_Project", "Agents", "Boxers")))
                if (File.Exists(Path.Combine(dir, "config.json"))) names.Add(Path.GetFileName(dir));
            return names;
        }

        [Test]
        public void TheManifestListsEveryBoxerByFiveEmotionsByFiveLevels()
        {
            VoiceManifest m = OnDisk();
            List<string> roster = Roster();
            Assert.Greater(roster.Count, 0, "no roster found");
            Assert.AreEqual(roster.Count, m.characters.Count, "the manifest's characters are not the roster (BYE is not a character)");
            var rule = new Regex(@"^[a-z0-9]+/(angry|happy|hurt|insult|celebrate)-[1-5]\.wav$");
            var voices = new HashSet<string>();
            foreach (string boxer in roster)
            {
                VoiceManifest.Character c = m.Find(boxer);
                Assert.IsNotNull(c, $"{boxer} is not in voices.json");
                Assert.AreEqual(boxer.ToLowerInvariant(), c.id);
                Assert.AreEqual(boxer.ToUpperInvariant(), c.name);
                Assert.IsNotNull(c.voice, $"{boxer} has no voice");
                Assert.Greater(c.voice.pitchHz, 40f, $"{boxer}'s voice has no pitch");
                Assert.Greater(c.voice.formantScale, 0.5f, $"{boxer}'s voice has no formant scale");
                Assert.IsFalse(string.IsNullOrEmpty(c.voice.timbre), $"{boxer}'s voice has no timbre");
                Assert.IsTrue(voices.Add($"{c.voice.pitchHz}|{c.voice.formantScale}|{c.voice.rasp}|{c.voice.breath}|{c.voice.tempo}"), $"{boxer} sounds like another boxer");
                foreach (Emotion5 e in System.Enum.GetValues(typeof(Emotion5)))
                    for (int level = 1; level <= 5; level++)
                    {
                        VoiceManifest.Clip cell = c.Cell(e, level);
                        Assert.IsNotNull(cell, $"{boxer} {e} {level} is missing");
                        Assert.AreEqual(VoiceManifest.FileFor(c.id, e, level), cell.file);
                        StringAssert.IsMatch(rule.ToString(), cell.file);
                        bool there = File.Exists(ProjectPath(VoiceManifest.Root, cell.file));
                        Assert.AreEqual(there, cell.exists, $"{cell.file}: exists is {cell.exists} but the file is {(there ? "" : "not ")}there: run PoBox/Voices/Rebuild voices.json");
                    }
            }
            Assert.AreSame(m.Find("MATT"), m.Find("MATT II"), "a twin does not share its boxer's entry");
        }

        [Test]
        public void TheGeneratorKeepsVoicesAndFlagsWhatExists()
        {
            var old = VoiceManifest.Rebuild(null, new List<(string, float)> { ("Matt", 80f) }, _ => false, "then");
            old.characters[0].voice.pitchHz = 99f;
            VoiceManifest m = VoiceManifest.Rebuild(old, new List<(string, float)> { ("Matt", 80f), ("Newcomer", 95f) }, f => f == "matt/hurt-3.wav", "now");
            Assert.AreEqual(2, m.characters.Count);
            Assert.AreEqual(99f, m.Find("MATT").voice.pitchHz, "the voice already in the file was not kept");
            Assert.Greater(m.Find("NEWCOMER").voice.pitchHz, 40f, "a new boxer got no voice");
            Assert.IsTrue(m.Find("matt").Cell(Emotion5.Hurt, 3).exists);
            Assert.IsFalse(m.Find("matt").Cell(Emotion5.Hurt, 4).exists);
            StringAssert.StartsWith("1 of 50 clips recorded; missing: ", m.Summary());
            Assert.IsNull(VoiceManifest.Parse("{}"), "a file with no schema is not a manifest");
            Assert.IsNull(VoiceManifest.Parse("not json"));
        }

        static float Peak(float[] d) { float p = 0f; foreach (float x in d) p = Mathf.Max(p, Mathf.Abs(x)); return p; }

        static float Rms(float[] d) { double s = 0; foreach (float x in d) s += x * x; return d.Length == 0 ? 0f : (float)System.Math.Sqrt(s / d.Length); }

        [Test]
        public void TheImitationIsAVoiceThatGrowsWithTheLevel()
        {
            VoiceProfile matt = VoiceProfile.For("MATT", 80f, false), kim = VoiceProfile.For("KIM", 55f, true);
            foreach (Emotion5 e in System.Enum.GetValues(typeof(Emotion5)))
            {
                float[] one = VoiceSynth.Render5(matt, e, 1, 0, "matt"), five = VoiceSynth.Render5(matt, e, 5, 0, "matt");
                foreach (float[] d in new[] { one, five })
                    foreach (float x in d) Assert.IsFalse(float.IsNaN(x) || float.IsInfinity(x), $"{e} is not finite");
                Assert.Greater(Rms(one), 0.02f, $"{e} level 1 is silent");
                Assert.Greater(Rms(five), 0.02f, $"{e} level 5 is silent");
                Assert.Greater(Peak(five), Peak(one), $"{e}: level 5 is not louder than level 1");
                Assert.Greater(five.Length, one.Length, $"{e}: level 5 is not longer than level 1");
                Assert.Less(five.Length, VoiceSynth.Rate * 4, $"{e} level 5 is too long");

                CollectionAssert.AreEqual(five, VoiceSynth.Render5(matt, e, 5, 0, "matt"), $"{e}: the same request did not sound the same");
                CollectionAssert.AreNotEqual(VoiceSynth.Render5(matt, e, 3, 0, "matt"), VoiceSynth.Render5(kim, e, 3, 0, "kim"), $"{e}: MATT and KIM sound the same");
                CollectionAssert.AreNotEqual(VoiceSynth.Render5(matt, e, 3, 0, "matt"), VoiceSynth.Render5(matt, e, 3, 1, "matt"), $"{e}: two takes are identical");
            }
            Assert.AreEqual(VoiceSynth.Seed("MATT", Emotion5.Hurt, 2, 0), VoiceSynth.Seed("matt", Emotion5.Hurt, 2, 0));
        }
    }
}
