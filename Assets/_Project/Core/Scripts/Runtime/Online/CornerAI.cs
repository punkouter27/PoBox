using System;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace PoBox.Online
{
    /// <summary>
    /// The cornerman's line from Claude, through the proxy that holds the key (server/corner_proxy.py), never from
    /// the phone straight to the API: a key shipped inside an app is anyone's key. The game posts the fight's own
    /// numbers as JSON and gets one line back; no proxy set, the proxy down, or no answer in time, and the caller
    /// says its rule-based line instead (Commentary.CornerTip).
    ///
    /// The address is the settings' CORNER field (DEBUG tab), kept in PlayerPrefs; empty (off) until one is typed, in
    /// the editor too: a default that pointed at a proxy nobody had started cost 4 failed posts a bout.
    /// http://127.0.0.1:8787/corner is the proxy on this PC. A network proxy must use HTTPS and a token; put the token
    /// in the address fragment (#token=...) so it is sent in x-pobox-token and never in the HTTP request URL. Legacy
    /// ?token= settings are stripped from the request URL and sent in the same header.
    /// </summary>
    public static class CornerAI
    {
        public const string PrefKey = "pobox.corner";

        public static string Url => PlayerPrefs.GetString(PrefKey, "").Trim();

        [System.Serializable]
        class Reply { public string text; }

        /// <summary>Sends the numbers; null when there is no proxy to ask. Poll <see cref="Answer"/> until it is not pending.</summary>
        public static UnityWebRequest Ask(string json)
        {
            string configuredUrl = Url;
            if (string.IsNullOrEmpty(configuredUrl)) return null;
            if (!Uri.TryCreate(configuredUrl, UriKind.Absolute, out Uri uri))
            {
                Debug.LogWarning("CORNER address is not a valid absolute URL.");
                return null;
            }

            bool loopbackHttp = uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback;
            if (uri.Scheme != Uri.UriSchemeHttps && !loopbackHttp)
            {
                Debug.LogWarning("CORNER requires HTTPS for network addresses; plain HTTP is allowed only on loopback.");
                return null;
            }

            var endpoint = new UriBuilder(uri);
            string token = ExtractToken(endpoint.Query) ?? ExtractToken(endpoint.Fragment);
            endpoint.Query = "";
            endpoint.Fragment = "";
            var r = new UnityWebRequest(endpoint.Uri.AbsoluteUri, "POST")
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json)),
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = 10,
            };
            r.SetRequestHeader("content-type", "application/json");
            if (!string.IsNullOrEmpty(token)) r.SetRequestHeader("x-pobox-token", token);
            r.SendWebRequest();
            return r;
        }

        static string ExtractToken(string queryOrFragment)
        {
            string value = queryOrFragment.TrimStart('?', '#');
            string found = null;
            foreach (string pair in value.Split('&'))
            {
                int equals = pair.IndexOf('=');
                string key = Uri.UnescapeDataString(equals < 0 ? pair : pair.Substring(0, equals).Replace("+", "%20"));
                if (!string.Equals(key, "token", StringComparison.OrdinalIgnoreCase)) continue;
                if (found != null) return null;
                found = Uri.UnescapeDataString((equals < 0 ? "" : pair.Substring(equals + 1)).Replace("+", "%20"));
            }
            return found;
        }

        /// <summary>Whether the request is still out.</summary>
        public static bool Pending(UnityWebRequest r) => r != null && !r.isDone;

        /// <summary>The line, or null if there is none (no request, still out, failed, empty). Disposes a finished request.</summary>
        public static string Answer(ref UnityWebRequest r)
        {
            if (r == null || !r.isDone) return null;
            string text = null;
            if (r.result == UnityWebRequest.Result.Success)
            {
                try { text = JsonUtility.FromJson<Reply>(r.downloadHandler.text)?.text; }
                catch (System.Exception) { text = null; }
            }
            r.Dispose();
            r = null;
            return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        }

        public static void Drop(ref UnityWebRequest r)
        {
            r?.Dispose();
            r = null;
        }
    }
}
