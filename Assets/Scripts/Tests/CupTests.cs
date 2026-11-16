using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using PoBox.Audio;
using PoBox.League;
using PoBox.Sim;

namespace PoBox.Tests
{
    /// <summary>
    /// The sixteen-slot cup (League/Career.cs): BYEs in the empty slots, the draw keeping the top two apart, a cup
    /// played out to its champion, a boxer added later taking a BYE; the free-for-all's placings on the ladder; and
    /// the voice matrix's procedural tier and its clip names. No scene: the owner's cup, ladder and titles are kept
    /// aside and put back.
    /// </summary>
    public class CupTests
    {
        const string LadderKey = "pobox.league.v1", TitlesKey = "pobox.cup.titles";
        string _cupFile, _cupSaved, _ladder, _titles;

        static readonly string[] Roster = { "Matt", "Zombie", "Nick", "Trump", "Grandma", "Grandpa", "Kim", "Sokol" };

        [SetUp]
        public void KeepAside()
        {
            _cupFile = Path.Combine(Application.persistentDataPath, "career.json");
            _cupSaved = File.Exists(_cupFile) ? File.ReadAllText(_cupFile) : null;
            _ladder = PlayerPrefs.GetString(LadderKey, null);
            _titles = PlayerPrefs.GetString(TitlesKey, null);
            PlayerPrefs.DeleteKey(LadderKey);
            PlayerPrefs.DeleteKey(TitlesKey);
        }

        [TearDown]
        public void PutBack()
        {
            if (_cupSaved != null) File.WriteAllText(_cupFile, _cupSaved);
            else if (File.Exists(_cupFile)) File.Delete(_cupFile);
            if (_ladder != null) PlayerPrefs.SetString(LadderKey, _ladder); else PlayerPrefs.DeleteKey(LadderKey);
            if (_titles != null) PlayerPrefs.SetString(TitlesKey, _titles); else PlayerPrefs.DeleteKey(TitlesKey);
            PlayerPrefs.Save();
            Career.Live = false;
        }

        static List<(string, string)> Entrants(int n)
        {
            var list = new List<(string, string)>();
            for (int i = 0; i < n; i++)
            {
                string name = i < Roster.Length ? Roster[i] : "Extra" + i;
                list.Add((name, name.ToUpperInvariant()));
            }
            return list;
        }

        /// <summary>Plays the cup out, red winning every bout; the number of bouts fought.</summary>
        static int PlayOut(Career c)
        {
            int bouts = 0;
            while (c.Next(out _, out string red, out string blue))
            {
                Assert.IsTrue(c.Report(red, blue, 0), $"{red} v {blue} was not taken");
                Assert.Less(++bouts, Career.Matches + 1, "the cup never ended");
            }
            return bouts;
        }

        [Test]
        public void EightBoxersGetByesAndFightFromTheQuarterFinals()
        {
            Career c = Career.New(Entrants(8));
            Assert.AreEqual(16, c.slots.Length);
            int byes = 0;
            foreach (string s in c.slots) if (string.IsNullOrEmpty(s)) byes++;
            Assert.AreEqual(8, byes, "eight empty slots should be BYEs");
            for (int m = 0; m < Career.FirstRound; m++) Assert.IsTrue(c.IsWalkover(m), $"R16 match {m} is not a walkover");
            Assert.IsTrue(c.Next(out int first, out _, out _));
            Assert.AreEqual("QF", Career.RoundName(first), "the first bout fought should be a quarter-final");
            StringAssert.StartsWith("R16  BYES: ", c.ByesLine());
            Assert.AreEqual(7, PlayOut(c), "eight boxers is seven bouts");
            Assert.IsNotNull(c.Champion);
            Assert.AreEqual(1, Career.Titles(c.Shown(c.Champion)), "the champion's cup was not counted");
            Assert.IsFalse(c.auto, "a won cup should stop running itself");
        }

        [Test]
        public void SixteenBoxersFightFifteenBoutsAndTheTopTwoMeetOnlyInTheFinal()
        {
            Career c = Career.New(Entrants(16));
            Assert.AreEqual("", c.ByesLine(), "a full cup has no byes");
            // Seeds 1 and 2 (the ladder's best two) sit in opposite halves of the draw: slot 0 at the top, slot 14 in the bottom half.
            float s1 = LeagueTable.SavedElo(c.shown[0]), s2 = LeagueTable.SavedElo(c.shown[14]);
            for (int k = 0; k < Career.Size; k++)
            {
                float r = LeagueTable.SavedElo(c.shown[k]);
                Assert.GreaterOrEqual(s1, r, $"slot {k} ({c.shown[k]}) is rated above the top seed");
                if (k != 0) Assert.GreaterOrEqual(s2, r, $"slot {k} ({c.shown[k]}) is rated above the second seed");
            }
            Assert.AreEqual(15, PlayOut(c));
        }

        [Test]
        public void ABoxerAddedLaterTakesTheTopSeedsBye()
        {
            Career c = Career.New(Entrants(8));
            string topSeed = c.slots[0];
            Assert.IsTrue(c.Fill(Entrants(9)), "the ninth boxer did not come in");
            Assert.AreEqual("Extra8", c.slots[1], "the newcomer should face the top seed");
            Assert.IsTrue(c.Next(out int m, out string red, out string blue));
            Assert.AreEqual(0, m, "the newcomer's bout comes first");
            Assert.AreEqual(topSeed, red);
            Assert.IsFalse(c.Fill(Entrants(9)), "nobody new: nothing should change");
            // Once the top seed has fought on, its slot is closed to newcomers.
            PlayOut(c);
            Assert.IsFalse(c.Fill(Entrants(10)), "a played cup took a newcomer");
            // And it all survives a save and a load.
            Career back = Career.Load();
            Assert.IsNotNull(back);
            Assert.AreEqual(c.Champion, back.Champion);
        }

        [Test]
        public void AnOldEightSlotCupIsDropped()
        {
            File.WriteAllText(_cupFile, "{\"slots\":[\"a\",\"b\",\"\",\"\",\"\",\"\",\"\",\"\"],\"shown\":[\"A\",\"B\",\"\",\"\",\"\",\"\",\"\",\"\"],\"won\":[\"\",\"\",\"\",\"\",\"\",\"\",\"\"]}");
            Assert.IsNull(Career.Load());
        }

        [Test]
        public void FreeForAllPlacingsMoveTheLadderOnce()
        {
            var placed = new List<string> { "MATT", "KIM", "MATT", "ZOMBIE" };   // MATT's twin placed third: counted once, at first
            float matt0 = LeagueTable.SavedElo("MATT"), zombie0 = LeagueTable.SavedElo("ZOMBIE");
            Dictionary<string, float> d = LeagueTable.ReportPlacings(placed, 32f);
            Assert.AreEqual(3, d.Count);
            Assert.Greater(d["MATT"], 0f);
            Assert.Less(d["ZOMBIE"], 0f);
            float sum = 0f;
            foreach (float v in d.Values) sum += v;
            Assert.AreEqual(0f, sum, 1e-3f, "ratings should only change hands");
            Assert.AreEqual(matt0 + d["MATT"], LeagueTable.SavedElo("MATT"), 1e-2f, "MATT's change was not saved");
            Assert.AreEqual(zombie0 + d["ZOMBIE"], LeagueTable.SavedElo("ZOMBIE"), 1e-2f, "ZOMBIE's change was not saved");
            Assert.AreEqual("MATT", FreeForAll.BaseName("MATT II"));
        }

        [Test]
        public void EveryEmotionHasAProceduralVoiceForEveryBoxer()
        {
            foreach (string name in new[] { "MATT", "ZOMBIE", "NICK", "TRUMP", "GRANDMA", "GRANDPA", "KIM", "SOKOL", "NEWCOMER" })
            {
                VoiceProfile v = VoiceProfile.For(name, 80f, name == "KIM" || name == "GRANDMA");
                foreach (Emotion e in System.Enum.GetValues(typeof(Emotion)))
                {
                    float[] data = VoiceSynth.Render(v, e, new System.Random(1));
                    Assert.Greater(data.Length, VoiceSynth.Rate / 20, $"{name} {e} is too short");
                    Assert.Less(data.Length, VoiceSynth.Rate * 3, $"{name} {e} is too long");
                    float peak = 0f;
                    foreach (float x in data) { Assert.IsFalse(float.IsNaN(x) || float.IsInfinity(x), $"{name} {e} is not finite"); peak = Mathf.Max(peak, Mathf.Abs(x)); }
                    Assert.AreEqual(0.8f, peak, 1e-3f, $"{name} {e} is not normalised");
                }
            }
        }

        [Test]
        public void RecordedVoiceClipNamesAreRead()
        {
            Assert.IsTrue(VoiceMatrix.Parse("A_Voice_Matt_Pain_01", out string b, out Emotion e));
            Assert.AreEqual("Matt", b);
            Assert.AreEqual(Emotion.Pain, e);
            Assert.IsTrue(VoiceMatrix.Parse("a_voice_grandma_triumph", out b, out e));
            Assert.AreEqual(Emotion.Triumph, e);
            Assert.IsFalse(VoiceMatrix.Parse("voice_pain_low_1", out _, out _));
            Assert.IsFalse(VoiceMatrix.Parse("A_Voice_Matt_Sneeze_01", out _, out _));
            // With nothing recorded, every cell is still filled.
            var m = new VoiceMatrix { proceduralShare = 1f, takes = 1 };
            Assert.IsNotNull(m.Pick("MATT", Emotion.Taunt, null, VoiceProfile.For("MATT", 80f, false)));
            Assert.AreEqual(VoiceMatrix.Tier.Procedural, m.LastTier);
        }
    }
}
