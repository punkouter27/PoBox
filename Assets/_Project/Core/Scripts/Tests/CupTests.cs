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
            Assert.AreEqual("R16  8 BYES · ALL THROUGH", c.ByesLine());
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
            // Seeds 1 and 2 (the ladder's best two) sit in opposite halves of the draw: slot 0 at the top, slot 8 at the top of the bottom half.
            float s1 = LeagueTable.SavedElo(c.shown[0]), s2 = LeagueTable.SavedElo(c.shown[8]);
            for (int k = 0; k < Career.Size; k++)
            {
                float r = LeagueTable.SavedElo(c.shown[k]);
                Assert.GreaterOrEqual(s1, r, $"slot {k} ({c.shown[k]}) is rated above the top seed");
                if (k != 0) Assert.GreaterOrEqual(s2, r, $"slot {k} ({c.shown[k]}) is rated above the second seed");
            }
            Assert.AreEqual(15, PlayOut(c));
        }

        [Test]
        public void EveryBoxerStartsAtOneThousand()
        {
            // A fresh profile (the ladder cleared in KeepAside): the CPU league's ratings (cpu_elo.json, NICK about 1450)
            // no longer seed anybody; everybody starts at 1000.
            foreach (string name in new[] { "NICK", "KIM", "SOKOL", "MATT", "ZOMBIE", "NEWCOMER" })
                Assert.AreEqual(LeagueTable.Start, LeagueTable.SavedElo(name), $"{name} does not start at 1000");
            Assert.AreEqual(1000f, LeagueTable.Start);
            // A first result moves from 1000: two equal ratings, the winner of a 32-K free-for-all of two gains 16.
            Dictionary<string, float> d = LeagueTable.ReportPlacings(new List<string> { "NICK", "KIM" }, 32f);
            Assert.AreEqual(16f, d["NICK"], 1e-3f);
            Assert.AreEqual(1016f, LeagueTable.SavedElo("NICK"), 1e-3f, "the saved rating did not start from 1000");
            Assert.AreEqual(984f, LeagueTable.SavedElo("KIM"), 1e-3f);
        }

        [Test]
        public void TheDrawIsTheTournamentStandardsOrder()
        {
            Career c = Career.New(Entrants(16));
            int[,] pairs = { { 1, 16 }, { 8, 9 }, { 4, 13 }, { 5, 12 }, { 2, 15 }, { 7, 10 }, { 3, 14 }, { 6, 11 } };
            for (int m = 0; m < Career.FirstRound; m++)
            {
                Assert.AreEqual(pairs[m, 0], c.SeedOf(c.slots[2 * m]), $"R16 match {m}'s red seed");
                Assert.AreEqual(pairs[m, 1], c.SeedOf(c.slots[2 * m + 1]), $"R16 match {m}'s blue seed");
            }
        }

        [Test]
        public void ADrawGoesToTheHigherSeed()
        {
            Career c = Career.New(Entrants(16));
            // R16: seed 16 beats seed 1, then 8 v 9 is drawn: seed 8 goes through.
            Assert.IsTrue(c.Next(out _, out string red, out string blue));
            Assert.IsTrue(c.Report(red, blue, 1));
            Assert.IsTrue(c.Next(out _, out red, out blue));
            Assert.AreEqual(8, c.SeedOf(red));
            Assert.IsTrue(c.Report(red, blue, -1), "a drawn cup bout was not taken");
            Assert.AreEqual(red, c.won[1], "the draw did not go to seed 8");
            while (c.Next(out int m, out red, out blue) && m < Career.FirstRound) Assert.IsTrue(c.Report(red, blue, 0));
            // The quarter-final: seed 16 (red) v seed 8 (blue), drawn: blue, the higher seed, goes through.
            Assert.IsTrue(c.Next(out int qf, out red, out blue));
            Assert.AreEqual("QF", Career.RoundName(qf));
            Assert.AreEqual(16, c.SeedOf(red));
            Assert.AreEqual(8, c.SeedOf(blue));
            Assert.IsTrue(c.Report(red, blue, -1));
            Assert.AreEqual(blue, c.won[qf], "the quarter-final's draw did not go to the higher seed");
        }

        [Test]
        public void TwoByesSendAByeOnAndTheTopSeedWalksThrough()
        {
            Career c = Career.New(Entrants(2));
            Assert.AreEqual("BYE", c.Shown(c.slots[1]), "an empty slot is not shown as BYE");
            // Seeds 1 and 2 walk to the final; every BYE v BYE sends a BYE on and nobody else fights.
            Assert.IsTrue(c.Next(out int m, out string red, out string blue));
            Assert.AreEqual("FINAL", Career.RoundName(m));
            Assert.AreEqual(1, c.SeedOf(red));
            Assert.AreEqual(2, c.SeedOf(blue));
        }

        [Test]
        public void TheFiveByFiveVoiceGridIsFilledAndNamed()
        {
            Assert.IsTrue(VoiceMatrix.Parse5("hurt-3", out Emotion5 e, out int level));
            Assert.AreEqual(Emotion5.Hurt, e);
            Assert.AreEqual(3, level);
            Assert.IsTrue(VoiceMatrix.Parse5("Celebrate-5_02", out e, out level));
            Assert.AreEqual(Emotion5.Celebrate, e);
            Assert.AreEqual(5, level);
            Assert.IsFalse(VoiceMatrix.Parse5("hurt-6", out _, out _));
            Assert.IsFalse(VoiceMatrix.Parse5("hurt-0", out _, out _));
            Assert.IsFalse(VoiceMatrix.Parse5("2-3", out _, out _));
            Assert.IsFalse(VoiceMatrix.Parse5("A_Voice_Matt_Pain_01", out _, out _));
            Assert.AreEqual(Emotion.Pain, VoiceMatrix.Map(Emotion5.Hurt, 2));
            Assert.AreEqual(Emotion.Groan, VoiceMatrix.Map(Emotion5.Hurt, 5));
            Assert.AreEqual(Emotion.Taunt, VoiceMatrix.Map(Emotion5.Insult, 1));
            Assert.AreEqual(Emotion.Effort, VoiceMatrix.Map(Emotion5.Angry, 4));
            Assert.AreEqual(1, VoiceMatrix.Level(0f));
            Assert.AreEqual(5, VoiceMatrix.Level(1f));
            // With nothing recorded (an empty manifest), every one of the 25 cells is still filled, procedurally.
            var m = new VoiceMatrix { gridTakes = 1, Manifest = new VoiceManifest() };
            VoiceProfile v = VoiceProfile.For("MATT", 80f, false);
            foreach (Emotion5 x in System.Enum.GetValues(typeof(Emotion5)))
                for (int l = 1; l <= VoiceMatrix.Levels; l++)
                {
                    Assert.IsNotNull(m.Pick("MATT", x, l, v), $"{x} {l} is silent");
                    Assert.AreEqual(VoiceMatrix.Tier.Procedural, m.LastTier);
                }
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
