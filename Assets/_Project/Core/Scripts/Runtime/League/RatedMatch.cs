using System.Collections.Generic;
using PoBox.Sim;

namespace PoBox.League
{
    /// <summary>
    /// One match's rating, recorded at most once (demo and attract bouts too: every completed match between rated
    /// boxers moves the ladder, owner 2026-11-22). A bout or free-for-all makes a new one when it starts; it records
    /// only at the result, so a match left, restarted or abandoned records nothing, and a replay or a second result
    /// cannot count it twice. A boxer never plays itself: a twin ("MATT II") is its boxer, so a boxer against its
    /// twin records nothing, and in a free-for-all a boxer counts once, at its better placing.
    /// </summary>
    public sealed class RatedMatch
    {
        public bool Recorded { get; private set; }

        /// <summary>The boxer a name on the scoreboard is: a twin's is its boxer's.</summary>
        public static string Character(string displayName) => FreeForAll.BaseName(displayName);

        /// <summary>A one-on-one result (score of A: 1 win, 0.5 draw, 0 loss) on the ladder; A's change, 0 if not recorded.</summary>
        public float Report(LeagueTable league, string a, string b, float scoreA, bool knockout)
        {
            if (Recorded || league == null) return 0f;
            Recorded = true;
            string ca = Character(a), cb = Character(b);
            if (string.IsNullOrEmpty(ca) || ca == cb) return 0f;
            return league.Report(ca, cb, scoreA, knockout);
        }

        /// <summary>
        /// A multi-player result, best first, on the ladder (<see cref="LeagueTable.ReportPlacings"/>: every pair a
        /// win for the better placed, K shared over the N - 1 opponents); each boxer's change, empty if not recorded.
        /// </summary>
        public Dictionary<string, float> ReportPlacings(IList<string> bestFirst, float k = 32f)
        {
            if (Recorded) return new Dictionary<string, float>();
            Recorded = true;
            var names = new List<string>();
            foreach (string n in bestFirst) names.Add(Character(n));
            return LeagueTable.ReportPlacings(names, k);
        }
    }
}
