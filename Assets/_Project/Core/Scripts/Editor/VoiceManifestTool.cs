using System;
using System.Collections.Generic;
using System.IO;
using PoBox.Audio;
using UnityEditor;
using UnityEngine;

namespace PoBox.EditorTools
{
    /// <summary>
    /// Writes the voice manifest, Assets/_Project/Data/Resources/Voices/voices.json (<see cref="VoiceManifest"/>):
    /// every boxer of the roster (a folder under Assets/_Project/Agents/Boxers with a config.json) by 5 emotions by
    /// 5 levels, each cell's WAV and whether it is there, keeping the voices already in the file. Menu PoBox/Voices/
    /// Rebuild voices.json, and on its own whenever a WAV under the Voices folder is imported, moved or deleted.
    /// </summary>
    public class VoiceManifestTool : AssetPostprocessor
    {
        const string BoxersDir = "Assets/_Project/Agents/Boxers";
        static string ManifestFile => VoiceManifest.Root + "/voices.json";

        [Serializable] class BoxerConfig { public float total_mass_kg = 0f; }

        [MenuItem("PoBox/Voices/Rebuild voices.json")]
        public static void RebuildMenu() => Debug.Log("[Voices] " + Rebuild().Summary());

        /// <summary>The roster as the generator sees it: (folder name, mass), in folder order.</summary>
        public static List<(string name, float mass)> Roster()
        {
            var roster = new List<(string, float)>();
            if (!Directory.Exists(BoxersDir)) return roster;
            var dirs = new List<string>(Directory.GetDirectories(BoxersDir));
            dirs.Sort(StringComparer.OrdinalIgnoreCase);
            foreach (string dir in dirs)
            {
                string config = Path.Combine(dir, "config.json");
                if (!File.Exists(config)) continue;
                float mass = 80f;
                try { mass = JsonUtility.FromJson<BoxerConfig>(File.ReadAllText(config)).total_mass_kg; }
                catch (Exception) { }
                roster.Add((Path.GetFileName(dir), mass > 0f ? mass : 80f));
            }
            return roster;
        }

        /// <summary>Rewrites voices.json from the roster and the WAVs on disk; returns what it wrote.</summary>
        public static VoiceManifest Rebuild()
        {
            VoiceManifest old = File.Exists(ManifestFile) ? VoiceManifest.Parse(File.ReadAllText(ManifestFile)) : null;
            VoiceManifest m = VoiceManifest.Rebuild(old, Roster(), file => File.Exists(Path.Combine(VoiceManifest.Root, file)),
                                                    DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"));
            string json = JsonUtility.ToJson(m, true);
            if (!File.Exists(ManifestFile) || Strip(File.ReadAllText(ManifestFile)) != Strip(json))
            {
                Directory.CreateDirectory(VoiceManifest.Root);
                File.WriteAllText(ManifestFile, json);
                AssetDatabase.ImportAsset(ManifestFile);
            }
            VoiceManifest.Reload();
            return m;
        }

        /// <summary>The file without its "generated" stamp: a rebuild that changes nothing else is not written.</summary>
        static string Strip(string json)
        {
            int at = json.IndexOf("\"generated\"", StringComparison.Ordinal);
            if (at < 0) return json;
            int end = json.IndexOf('\n', at);
            return end < 0 ? json.Substring(0, at) : json.Remove(at, end - at);
        }

        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            if (Touches(imported) || Touches(deleted) || Touches(moved) || Touches(movedFrom))
                Debug.Log("[Voices] " + Rebuild().Summary());
        }

        static bool Touches(string[] paths)
        {
            foreach (string p in paths)
                if (p.StartsWith(VoiceManifest.Root + "/", StringComparison.OrdinalIgnoreCase) && p.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }
    }
}
