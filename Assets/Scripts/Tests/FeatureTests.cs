using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using PoBox.League;
using PoBox.Online;

namespace PoBox.Tests
{
    /// <summary>
    /// The pieces of the 2026-10-30 features that are plain logic: the leaderboard's row as the Azure Table takes it and
    /// the addresses it will post to, and the ladder's win and loss streaks. Nothing here touches the device's saved
    /// ladder or the network.
    /// </summary>
    public class FeatureTests
    {
        [Test]
        public void LeaderboardRowIsTheTablesJsonNewestFirst()
        {
            var row = new Leaderboard.Row { red = "MATT", blue = "KIM \"K\"", winner = "MATT", method = "KO 2", knockout = true, rounds = 2, seconds = 61.4f, redLanded = 30 };
            var early = new DateTime(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);
            string a = Leaderboard.Json(row, early, "dev1", "261030");
            string b = Leaderboard.Json(row, early.AddMinutes(1), "dev1", "261030");

            StringAssert.Contains("\"PartitionKey\":\"bout\"", a);
            StringAssert.Contains("\"Blue\":\"KIM \\\"K\\\"\"", a);        // quotes escaped
            StringAssert.Contains("\"Knockout\":true", a);
            StringAssert.Contains("\"Seconds\":61,", a);
            StringAssert.Contains("\"When\":\"2026-10-30T12:00:00Z\"", a);
            Assert.DoesNotThrow(() => JsonUtility.FromJson<Shape>(a), "the row parses as JSON");
            // The row key counts down, so the table lists the newest bout first.
            Assert.Less(string.CompareOrdinal(Key(b), Key(a)), 0);
        }

        [Serializable] class Shape { public string PartitionKey, RowKey, Red, Blue; public bool Knockout; }

        static string Key(string json) => JsonUtility.FromJson<Shape>(json).RowKey;

        [Test]
        public void LeaderboardPostsOnlyToHttps()
        {
            Assert.IsNull(Leaderboard.Endpoint(""));
            LogAssert.Expect(LogType.Warning, "BOARD must be an https:// Azure Table address with a SAS token.");
            Assert.IsNull(Leaderboard.Endpoint("http://acct.table.core.windows.net/pobox?sp=a&sig=x"));
            Assert.AreEqual("https://acct.table.core.windows.net/pobox?sp=a&sig=x", Leaderboard.Endpoint("https://acct.table.core.windows.net/pobox?sp=a&sig=x"));
        }

        [Test]
        public void StreaksCountRunsAndADrawOrALossEndsThem()
        {
            var a = new LeagueTable.Row { name = "A" };
            var b = new LeagueTable.Row { name = "B" };
            LeagueTable.Streak(a, b);
            LeagueTable.Streak(a, b);
            LeagueTable.Streak(a, b);
            Assert.AreEqual(3, a.streak);
            Assert.AreEqual(-3, b.streak);
            Assert.AreEqual("W3", LeagueTable.StreakText(a.streak));
            Assert.AreEqual("L3", LeagueTable.StreakText(b.streak));
            LeagueTable.Streak(b, a);
            Assert.AreEqual(1, b.streak);
            Assert.AreEqual(-1, a.streak);
            Assert.AreEqual("", LeagueTable.StreakText(1), "one in a row is not a streak");
        }
    }
}
