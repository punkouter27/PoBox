using System;
using System.Collections.Generic;
using UnityEngine;

namespace PoBox.League
{
    /// <summary>
    /// The ladder. Every finished bout moves two Elo ratings, the table is kept on the device, and the next
    /// pairing is whoever has fought least against whoever is rated closest to them.
    ///
    /// Every boxer starts at 1000 (<see cref="Start"/>; the tournament standard, owner 2026-11-22: "reset to
    /// 1000"), K 32. Ratings already saved on a device are kept; a fresh profile, a new boxer or RESET starts at 1000.
    /// The CPU's league (every pair of the game's brains boxed in C MuJoCo by Training/tools/elo.py, written to
    /// Resources/cpu_elo.json) no longer seeds a rating: it is only shown, as each boxer's CPU record on the table.
    /// </summary>
    public class LeagueTable : MonoBehaviour
    {
        public PolicyProfile[] roster = new PolicyProfile[0];
        /// <summary>Every rating's start (the tournament standard).</summary>
        public const float Start = 1000f;
        public float startRating = Start;
        public float kFactor = 32f;

        [Serializable]
        public class Row
        {
            public string name;
            public float elo;
            public int wins, losses, draws, knockouts;
            /// <summary>The CPU league's rating, for reference only (0 when the CPU has not rated this boxer); ratings start at 1000.</summary>
            public float cpuElo;
            public string lastOpponent;
            /// <summary>Bouts in a row: wins counted up, losses down; a draw ends either run.</summary>
            public int streak;
            public int Bouts => wins + losses + draws;
        }

        [Serializable]
        class Save { public List<Row> rows = new List<Row>(); }

        [Serializable]
        class CpuRow { public string name; public float elo; public int wins, losses, draws; }
        [Serializable]
        class Cpu { public string when; public int bouts; public List<CpuRow> rows = new List<CpuRow>(); }

        static Cpu _cpu;
        /// <summary>The CPU ratings shipped with the game (Resources/cpu_elo.json), read once; empty if there are none.</summary>
        static Cpu CpuRatings
        {
            get
            {
                if (_cpu != null) return _cpu;
                var text = Resources.Load<TextAsset>("cpu_elo");
                try { _cpu = text != null ? JsonUtility.FromJson<Cpu>(text.text) : null; }
                catch (Exception) { _cpu = null; }
                return _cpu ??= new Cpu();
            }
        }

        /// <summary>A boxer's record in the CPU's bouts, "W-L-D"; empty if the CPU has not rated it.</summary>
        public static string CpuRecord(string name)
        {
            foreach (CpuRow r in CpuRatings.rows) if (r.name == name) return $"{r.wins}-{r.losses}-{r.draws}";
            return "";
        }

        /// <summary>A boxer's CPU rating; 0 if the CPU has not rated it.</summary>
        public static float CpuElo(string name)
        {
            foreach (CpuRow r in CpuRatings.rows) if (r.name == name) return r.elo;
            return 0f;
        }

        const string Key = "pobox.league.v1";
        Save _save = new Save();

        public IReadOnlyList<Row> Rows => _save.rows;

        void Awake() => Load();

        void Load()
        {
            string json = PlayerPrefs.GetString(Key, "");
            if (!string.IsNullOrEmpty(json))
            {
                try { _save = JsonUtility.FromJson<Save>(json) ?? new Save(); }
                catch (Exception) { _save = new Save(); }
            }
            foreach (PolicyProfile p in roster)
                if (p != null && Find(p.displayName) == null)
                {
                    _save.rows.Add(new Row { name = p.displayName, elo = startRating, cpuElo = CpuElo(p.displayName) });
                }
            // A profile that has left the roster leaves the ladder with it.
            _save.rows.RemoveAll(r => ProfileFor(r.name) == null);
        }

        void Store()
        {
            PlayerPrefs.SetString(Key, JsonUtility.ToJson(_save));
            PlayerPrefs.Save();
        }

        /// <summary>A boxer's rating as the ladder last saved it, read without the arena's table (the first screen has none); the start rating if it has none.</summary>
        public static float SavedElo(string name, float fallback = Start)
        {
            Row row = SavedRow(name);
            return row != null ? row.elo : fallback;
        }

        /// <summary>A boxer's row as the ladder last saved it (rating, record, streak), read without the arena's table; null if it has none yet.</summary>
        public static Row SavedRow(string name)
        {
            try
            {
                Save save = JsonUtility.FromJson<Save>(PlayerPrefs.GetString(Key, ""));
                if (save != null && save.rows != null) foreach (Row r in save.rows) if (r.name == name) return r;
            }
            catch (Exception) { }
            return null;
        }

        /// <summary>The streak as a card shows it: "W3" or "L2" for two or more in a row, else empty.</summary>
        public static string StreakText(int streak) => streak >= 2 ? "W" + streak : streak <= -2 ? "L" + -streak : "";

        public void ResetAll()
        {
            _save = new Save();
            PlayerPrefs.DeleteKey(Key);
            Load();
        }

        public Row Find(string name)
        {
            foreach (Row r in _save.rows) if (r.name == name) return r;
            return null;
        }

        public PolicyProfile ProfileFor(string name)
        {
            foreach (PolicyProfile p in roster) if (p != null && p.displayName == name) return p;
            return null;
        }

        /// <summary>The ladder, best first.</summary>
        public List<Row> Sorted()
        {
            var rows = new List<Row>(_save.rows);
            rows.Sort((a, b) => b.elo.CompareTo(a.elo));
            return rows;
        }

        public int RankOf(string name)
        {
            List<Row> rows = Sorted();
            for (int i = 0; i < rows.Count; i++) if (rows[i].name == name) return i + 1;
            return 0;
        }

        public static float Expected(float a, float b) => 1f / (1f + Mathf.Pow(10f, (b - a) / 400f));

        /// <summary>Chance the first name beats the second on rating alone, 0..1.</summary>
        public float Expected(string a, string b)
        {
            Row ra = Find(a), rb = Find(b);
            return ra != null && rb != null ? Expected(ra.elo, rb.elo) : 0.5f;
        }

        /// <summary>
        /// Whoever has fought least, against the closest rating that is not the fighter they met last time.
        /// </summary>
        public bool PickNext(out PolicyProfile a, out PolicyProfile b)
        {
            a = null; b = null;
            if (_save.rows.Count < 2) return false;

            Row first = null;
            foreach (Row r in _save.rows)
                if (first == null || r.Bouts < first.Bouts || (r.Bouts == first.Bouts && UnityEngine.Random.value < 0.35f))
                    first = r;

            Row second = null;
            float best = float.MaxValue;
            foreach (Row r in _save.rows)
            {
                if (r == first) continue;
                float gap = Mathf.Abs(r.elo - first.elo) + (r.name == first.lastOpponent ? 500f : 0f) + UnityEngine.Random.value * 40f;
                if (gap < best) { best = gap; second = r; }
            }
            if (second == null) return false;

            a = ProfileFor(first.name);
            b = ProfileFor(second.name);
            return a != null && b != null;
        }

        /// <summary>Records a result. <paramref name="scoreA"/> is 1 for a win by A, 0 for a loss, 0.5 for a draw.</summary>
        public float Report(string a, string b, float scoreA, bool knockout)
        {
            Row ra = Find(a), rb = Find(b);
            if (ra == null || rb == null) return 0f;

            float ea = Expected(ra.elo, rb.elo);
            float delta = kFactor * (scoreA - ea);
            ra.elo += delta;
            rb.elo -= delta;

            if (scoreA > 0.75f) { ra.wins++; rb.losses++; if (knockout) ra.knockouts++; Streak(ra, rb); }
            else if (scoreA < 0.25f) { rb.wins++; ra.losses++; if (knockout) rb.knockouts++; Streak(rb, ra); }
            else { ra.draws++; rb.draws++; ra.streak = rb.streak = 0; }

            ra.lastOpponent = b;
            rb.lastOpponent = a;
            Store();
            return delta;
        }

        /// <summary>
        /// A free-for-all's placings, best first (shown names; a name twice counts once, at its better place), on
        /// the saved ladder, without the arena's table: every pair is a game the better-placed one won, worth
        /// <paramref name="k"/> / (n - 1), so the whole field moves about as much as one bout would. Ratings only:
        /// the win-loss records are the one-on-one bouts'. Returns each name's change.
        /// </summary>
        public static Dictionary<string, float> ReportPlacings(IList<string> bestFirst, float k = 32f)
        {
            var changes = new Dictionary<string, float>();
            var names = new List<string>();
            foreach (string n in bestFirst) if (!string.IsNullOrEmpty(n) && !names.Contains(n)) names.Add(n);
            if (names.Count < 2) return changes;

            Save save = null;
            try { save = JsonUtility.FromJson<Save>(PlayerPrefs.GetString(Key, "")); }
            catch (Exception) { }
            if (save == null || save.rows == null) save = new Save();
            var rows = new List<Row>();
            foreach (string n in names)
            {
                Row r = null;
                foreach (Row x in save.rows) if (x.name == n) { r = x; break; }
                if (r == null)
                {
                    r = new Row { name = n, elo = Start, cpuElo = CpuElo(n) };
                    save.rows.Add(r);
                }
                rows.Add(r);
            }
            var before = new float[rows.Count];
            for (int i = 0; i < rows.Count; i++) before[i] = rows[i].elo;
            float kPair = k / (rows.Count - 1);
            for (int i = 0; i < rows.Count; i++)
                for (int j = i + 1; j < rows.Count; j++)
                {
                    float d = kPair * (1f - Expected(before[i], before[j]));
                    rows[i].elo += d;
                    rows[j].elo -= d;
                }
            for (int i = 0; i < rows.Count; i++) changes[rows[i].name] = rows[i].elo - before[i];
            PlayerPrefs.SetString(Key, JsonUtility.ToJson(save));
            PlayerPrefs.Save();
            return changes;
        }

        public static void Streak(Row winner, Row loser)
        {
            winner.streak = winner.streak > 0 ? winner.streak + 1 : 1;
            loser.streak = loser.streak < 0 ? loser.streak - 1 : -1;
        }
    }
}
