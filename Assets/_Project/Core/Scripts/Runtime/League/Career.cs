using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace PoBox.League
{
    /// <summary>
    /// The cup: sixteen slots, single elimination, seeded by the ladder's ratings in the usual draw (1 v 16, 8 v 9,
    /// ... so the top two can only meet in the final). Slots with nobody in them are BYEs: with eight boxers the
    /// round of sixteen is all walkovers and the fighting starts at the quarter-finals. The bracket is kept in
    /// Application.persistentDataPath/career.json and carried on from bout to bout, across sessions.
    ///
    /// Slots fill themselves (<see cref="Fill"/>): a boxer added to the game after the cup was drawn takes a BYE
    /// slot whose walkover has not yet been used, so the seed it faces fights it instead.
    ///
    /// The first screen starts or continues it and fights its next bout (setting <see cref="Live"/>); the arena,
    /// at the result of that bout, reports it (<see cref="Report"/>). With <see cref="auto"/> on (a new cup's
    /// default) the arena goes back to the bracket on its own after the result, and the bracket fights the next
    /// bout on its own after a countdown, until there is a champion. A draw decides nothing: the bout is fought
    /// again. Damage and fatigue are not carried from one bout to the next. Cups won are kept per boxer
    /// (<see cref="Titles"/>); the ratings are the ladder's (<see cref="LeagueTable"/>), which every bout moves.
    /// </summary>
    [Serializable]
    public class Career
    {
        public const int Size = 16, Matches = Size - 1, FirstRound = Size / 2;
        // Slot k holds seed SeedSlot[k] + 1: 1 v 16, 8 v 9, 5 v 12, 4 v 13, 6 v 11, 3 v 14, 7 v 10, 2 v 15.
        static readonly int[] SeedSlot = { 0, 15, 7, 8, 4, 11, 3, 12, 5, 10, 2, 13, 6, 9, 1, 14 };
        // Where each round's matches start: the round of 16 is 0-7, the quarter-finals 8-11, the semis 12-13, the final 14.
        static readonly int[] RoundStart = { 0, 8, 12, 14, 15 };
        static readonly string[] RoundNames = { "R16", "QF", "SF", "FINAL" };

        /// <summary>The bracket's first round, by slot: a boxer's folder name, or empty for a bye.</summary>
        public string[] slots = new string[Size];
        /// <summary>What each name is shown as.</summary>
        public string[] shown = new string[Size];
        /// <summary>The winner of each match (see <see cref="RoundStart"/>), empty until fought, "-" when two byes met.</summary>
        public string[] won = new string[Matches];
        /// <summary>Runs itself: the arena returns to the bracket after each result, the bracket fights the next bout after a countdown.</summary>
        public bool auto = true;

        /// <summary>A bout of the cup's is being fought: the arena reports its result.</summary>
        public static bool Live;

        static string FilePath => Path.Combine(Application.persistentDataPath, "career.json");
        const string TitlesKey = "pobox.cup.titles";

        public static Career Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return null;
                var c = JsonUtility.FromJson<Career>(File.ReadAllText(FilePath));
                // An eight-slot cup from before 2026-11-16 does not fit: it is dropped, NEW CUP draws a new one.
                return c != null && c.slots != null && c.slots.Length == Size && c.shown != null && c.shown.Length == Size
                       && c.won != null && c.won.Length == Matches ? c : null;
            }
            catch (Exception) { return null; }
        }

        public void Save()
        {
            try { File.WriteAllText(FilePath, JsonUtility.ToJson(this)); }
            catch (Exception e) { Debug.LogWarning("[Career] not saved: " + e.Message); }
        }

        public static void Delete()
        {
            try { if (File.Exists(FilePath)) File.Delete(FilePath); }
            catch (Exception) { }
        }

        /// <summary>A new cup from these boxers (folder name, shown name), best rated first; the first sixteen are in it.</summary>
        public static Career New(IList<(string name, string shown)> boxers)
        {
            var seeded = new List<(string name, string shown)>(boxers);
            seeded.Sort((a, b) => LeagueTable.SavedElo(b.shown).CompareTo(LeagueTable.SavedElo(a.shown)));
            var c = new Career();
            for (int k = 0; k < Size; k++)
            {
                int seed = SeedSlot[k];
                c.slots[k] = seed < seeded.Count ? seeded[seed].name : "";
                c.shown[k] = seed < seeded.Count ? seeded[seed].shown : "";
            }
            for (int m = 0; m < Matches; m++) c.won[m] = "";
            c.Resolve();
            c.Save();
            return c;
        }

        /// <summary>
        /// Boxers not in the cup yet take BYE slots whose walkover has not been used (the seed through on it has not
        /// fought its next bout). Best rated first, into the slot of the best seed still waiting. True if any came in.
        /// </summary>
        public bool Fill(IList<(string name, string shown)> boxers)
        {
            var fresh = new List<(string name, string shown)>();
            foreach (var b in boxers) if (Array.IndexOf(slots, b.name) < 0) fresh.Add(b);
            if (fresh.Count == 0) return false;
            fresh.Sort((a, b) => LeagueTable.SavedElo(b.shown).CompareTo(LeagueTable.SavedElo(a.shown)));
            bool any = false;
            // Through the slots in seed order, so the top seed loses its bye first (its walkover is the one a 16th seed would fill).
            for (int seed = Size - 1; seed >= 0 && fresh.Count > 0; seed--)
            {
                int k = Array.IndexOf(SeedSlot, seed);
                if (!string.IsNullOrEmpty(slots[k])) continue;
                int m = k / 2, parent = Parent(m);
                if (parent >= 0 && !string.IsNullOrEmpty(won[parent])) continue;   // the walkover has been used
                slots[k] = fresh[0].name;
                shown[k] = fresh[0].shown;
                fresh.RemoveAt(0);
                won[m] = "";
                any = true;
            }
            if (!any) return false;
            Resolve();
            Save();
            return true;
        }

        static int RoundOf(int match)
        {
            for (int r = 0; r < RoundNames.Length; r++) if (match < RoundStart[r + 1]) return r;
            return RoundNames.Length - 1;
        }

        /// <summary>The match a match's winner goes on to; -1 for the final.</summary>
        static int Parent(int match)
        {
            int r = RoundOf(match);
            return r + 1 >= RoundNames.Length ? -1 : RoundStart[r + 1] + (match - RoundStart[r]) / 2;
        }

        /// <summary>The two sides of a match: names, or empty for a bye, or null while not yet known.</summary>
        public void Sides(int match, out string a, out string b)
        {
            int r = RoundOf(match);
            if (r == 0) { a = slots[2 * match]; b = slots[2 * match + 1]; return; }
            int first = RoundStart[r - 1] + (match - RoundStart[r]) * 2;
            a = Known(won[first]); b = Known(won[first + 1]);
        }

        static string Known(string w) => string.IsNullOrEmpty(w) ? null : w == "-" ? "" : w;

        /// <summary>A side with nobody against it goes through; two byes put "-" (nobody) through.</summary>
        void Resolve()
        {
            for (int m = 0; m < Matches; m++)
            {
                if (!string.IsNullOrEmpty(won[m])) continue;
                Sides(m, out string a, out string b);
                if (a == null || b == null) continue;
                if (a.Length == 0) won[m] = b.Length == 0 ? "-" : b;
                else if (b.Length == 0) won[m] = a;
            }
        }

        /// <summary>A walkover: a match with a BYE on one side (or both).</summary>
        public bool IsWalkover(int match)
        {
            Sides(match, out string a, out string b);
            return a != null && b != null && (a.Length == 0 || b.Length == 0);
        }

        /// <summary>The next bout to fight, red and blue (folder names). False when the cup is won.</summary>
        public bool Next(out int match, out string red, out string blue)
        {
            for (match = 0; match < Matches; match++)
            {
                if (!string.IsNullOrEmpty(won[match])) continue;
                Sides(match, out red, out blue);
                if (!string.IsNullOrEmpty(red) && !string.IsNullOrEmpty(blue)) return true;
            }
            red = blue = null;
            return false;
        }

        /// <summary>The cup's winner (folder name), or null while it runs.</summary>
        public string Champion => Known(won[Matches - 1]);

        /// <summary>A bout's result: counted if it is the cup's next bout. Winner 0 red, 1 blue, anything else a draw (fought again).</summary>
        public bool Report(string red, string blue, int winner)
        {
            if (!Next(out int m, out string r, out string b) || r != red || b != blue || (winner != 0 && winner != 1)) return false;
            won[m] = winner == 0 ? red : blue;
            Resolve();
            if (!string.IsNullOrEmpty(Champion)) { AddTitle(Shown(Champion)); auto = false; }
            Save();
            return true;
        }

        public string Shown(string name)
        {
            if (string.IsNullOrEmpty(name)) return "BYE";
            for (int k = 0; k < Size; k++) if (slots[k] == name) return shown[k];
            return name.ToUpperInvariant();
        }

        public static string RoundName(int match) => RoundNames[RoundOf(match)];

        /// <summary>The bracket, one line a match: "QF  MATT v ZOMBIE  > MATT".</summary>
        public string Line(int match)
        {
            Sides(match, out string a, out string b);
            var s = new StringBuilder(RoundName(match)).Append("  ");
            s.Append(a == null ? "?" : Shown(a)).Append(" v ").Append(b == null ? "?" : Shown(b));
            string w = Known(won[match]);
            if (!string.IsNullOrEmpty(w)) s.Append("  > ").Append(Shown(w));
            return s.ToString();
        }

        /// <summary>The round of 16's walkovers in one line, "R16  8 BYES · ALL THROUGH" (or "3 BYES"); empty when there are none.</summary>
        public string ByesLine()
        {
            int through = 0, real = 0;
            for (int m = 0; m < FirstRound; m++)
            {
                if (IsWalkover(m)) { if (!string.IsNullOrEmpty(Known(won[m]))) through++; }
                else real++;
            }
            if (through == 0) return "";
            return $"R16  {through} BYE{(through == 1 ? "" : "S")}" + (real == 0 ? " · ALL THROUGH" : "");
        }

        // ------------------------------------------------------------ titles

        [Serializable] class Titles_ { public List<string> names = new List<string>(); public List<int> cups = new List<int>(); }

        /// <summary>Cups won by a boxer (its shown name), on this device.</summary>
        public static int Titles(string shownName)
        {
            Titles_ t = LoadTitles();
            int i = t.names.IndexOf(shownName);
            return i >= 0 ? t.cups[i] : 0;
        }

        static void AddTitle(string shownName)
        {
            Titles_ t = LoadTitles();
            int i = t.names.IndexOf(shownName);
            if (i < 0) { t.names.Add(shownName); t.cups.Add(1); }
            else t.cups[i]++;
            PlayerPrefs.SetString(TitlesKey, JsonUtility.ToJson(t));
            PlayerPrefs.Save();
        }

        static Titles_ LoadTitles()
        {
            try
            {
                var t = JsonUtility.FromJson<Titles_>(PlayerPrefs.GetString(TitlesKey, ""));
                if (t != null && t.names != null && t.cups != null && t.names.Count == t.cups.Count) return t;
            }
            catch (Exception) { }
            return new Titles_();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ClearLive() => Live = false;
    }
}
