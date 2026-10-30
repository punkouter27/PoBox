using System;
using System.Collections;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace PoBox.Online
{
    /// <summary>
    /// The online leaderboard: every bout the fight log keeps is also sent, as one row, to an Azure Table that every
    /// device writing to it shares. The page that reads it (Assets/WebGLTemplates/Po/leaderboard.html, served beside the
    /// WebGL build) replays the rows in order and gives each fighter an Elo of its own across every device, which the
    /// ladder on one phone (LeagueTable) cannot; training/tools/leaderboard.py does the same from the table or from
    /// fight logs copied off phones.
    ///
    /// The address is the settings' BOARD field (MORE tab), kept in PlayerPrefs, empty (off) until one is typed: the
    /// table's URL with a SAS token that may only add rows (sp=a), so a phone that leaks it can add bouts and nothing
    /// else. It is never in the repo. HTTPS only. A post that fails is dropped, not retried: the fight log on the
    /// device still has the bout.
    /// </summary>
    public static class Leaderboard
    {
        public const string PrefKey = "pobox.board";
        const string DeviceKey = "pobox.device";

        public static string Url => PlayerPrefs.GetString(PrefKey, "").Trim();

        /// <summary>One bout as the table keeps it. Flat: a table row has no nesting.</summary>
        public struct Row
        {
            public string red, blue, winner, method, redBrain, blueBrain;
            public bool knockout;
            public int rounds, redLanded, blueLanded, redThrown, blueThrown, redKnockdowns, blueKnockdowns;
            public float seconds;
        }

        /// <summary>An anonymous id for this device, made once, so the page can tell a hundred bouts from one phone from a hundred phones.</summary>
        static string Device
        {
            get
            {
                string id = PlayerPrefs.GetString(DeviceKey, "");
                if (id.Length == 0)
                {
                    id = Guid.NewGuid().ToString("N").Substring(0, 12);
                    PlayerPrefs.SetString(DeviceKey, id);
                    PlayerPrefs.Save();
                }
                return id;
            }
        }

        /// <summary>The address to post to, or null (and why, once, in the log) when there is none or it is not HTTPS.</summary>
        public static string Endpoint(string configured)
        {
            if (string.IsNullOrEmpty(configured)) return null;
            if (!Uri.TryCreate(configured, UriKind.Absolute, out Uri uri) || uri.Scheme != Uri.UriSchemeHttps)
            {
                Debug.LogWarning("BOARD must be an https:// Azure Table address with a SAS token.");
                return null;
            }
            return uri.AbsoluteUri;
        }

        /// <summary>The row as the Azure Table service takes it (JSON, no metadata). Newest first: the row key counts down.</summary>
        public static string Json(in Row r, DateTime utc, string device, string build)
        {
            var s = new StringBuilder(512);
            s.Append('{');
            Field(s, "PartitionKey", "bout");
            Field(s, "RowKey", $"{(DateTime.MaxValue.Ticks - utc.Ticks):D19}-{device}");
            Field(s, "When", utc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
            Field(s, "Red", r.red); Field(s, "Blue", r.blue); Field(s, "Winner", r.winner ?? "");
            Field(s, "Method", r.method ?? ""); Field(s, "RedBrain", r.redBrain ?? ""); Field(s, "BlueBrain", r.blueBrain ?? "");
            Field(s, "Knockout", r.knockout);
            Field(s, "Rounds", r.rounds); Field(s, "Seconds", Mathf.Round(r.seconds));
            Field(s, "RedLanded", r.redLanded); Field(s, "BlueLanded", r.blueLanded);
            Field(s, "RedThrown", r.redThrown); Field(s, "BlueThrown", r.blueThrown);
            Field(s, "RedKnockdowns", r.redKnockdowns); Field(s, "BlueKnockdowns", r.blueKnockdowns);
            Field(s, "Device", device); Field(s, "Build", build, last: true);
            s.Append('}');
            return s.ToString();
        }

        static void Field(StringBuilder s, string key, string value, bool last = false)
        {
            s.Append('"').Append(key).Append("\":\"");
            foreach (char c in value ?? "")
            {
                if (c == '"' || c == '\\') s.Append('\\').Append(c);
                else if (c < ' ') s.Append("\\u").Append(((int)c).ToString("x4"));
                else s.Append(c);
            }
            s.Append('"');
            if (!last) s.Append(',');
        }

        static void Field(StringBuilder s, string key, bool value) => s.Append('"').Append(key).Append("\":").Append(value ? "true" : "false").Append(',');
        static void Field(StringBuilder s, string key, float value) => s.Append('"').Append(key).Append("\":").Append(value.ToString(CultureInfo.InvariantCulture)).Append(',');

        /// <summary>Posts one bout; nothing happens with no BOARD set. Run it as a coroutine on anything alive.</summary>
        public static IEnumerator Post(Row row)
        {
            string url = Endpoint(Url);
            if (url == null) yield break;
            string build = Application.version;
            using var r = new UnityWebRequest(url, "POST")
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(Json(row, DateTime.UtcNow, Device, build))),
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = 15,
            };
            r.SetRequestHeader("Content-Type", "application/json");
            r.SetRequestHeader("Accept", "application/json;odata=nometadata");
            r.SetRequestHeader("Prefer", "return-no-content");
            r.SetRequestHeader("x-ms-version", "2019-02-02");
            yield return r.SendWebRequest();
            if (r.result != UnityWebRequest.Result.Success)
                Debug.LogWarning($"leaderboard: the bout was not posted ({r.responseCode} {r.error})");
        }
    }
}
