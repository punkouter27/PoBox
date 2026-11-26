using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace PoBox.EditorTools
{
    /// <summary>
    /// The repo's own house standard (AGENTS.md section 2): asset prefixes, one folder per kind, and no asset that
    /// nothing reaches. Two menu items, both idempotent and both writing what they did to
    /// <c>Temp/repo-standard.txt</c>:
    ///
    ///   * <c>PoBox/Repo/Standard Report</c> lists the naming violations and, through
    ///     <see cref="AssetDatabase.GetDependencies"/>, the assets the scenes and the Resources folders do not
    ///     reach. It changes nothing. A raw GUID grep is not a substitute: the icons in <c>UI/Icons</c> are reached
    ///     through <c>project://database/...</c> URLs in a stylesheet and the Arena lightmaps through the binary
    ///     <c>LightingData.asset</c>, and both look unreferenced to a text scan.
    ///   * <c>PoBox/Repo/Apply Standard Fixes</c> renames and moves with <see cref="AssetDatabase.MoveAsset"/>, so
    ///     every reference is kept (the GUID travels with the file) and no scene needs re-saving, then deletes the
    ///     assets it is told are dead. Everything it does is a fact from the report.
    ///
    /// What it deliberately does not touch: anything under a <c>Resources</c> folder (loaded by name, so a rename
    /// breaks it), <c>Assets/Plugins</c> and <c>Assets/WebGLTemplates</c> (native and page files, not assets), the
    /// boxer prefabs and folders (MjRetrofit and the trainer find them by path — <c>{Name}/{Name} {Corner}.prefab</c>),
    /// and the portraits and icons (the HUD and the stylesheet reach them by name and by URL).
    /// </summary>
    public static class RepoStandard
    {
        const string Materials = "Assets/_Project/Environments/Materials";
        const string Props = "Assets/_Project/Environments/Prefabs/Props";
        const string League = "Assets/_Project/Data/League";
        const string OutPath = "Temp/repo-standard.txt";

        /// <summary>Folders whose first-party audio clips take the <c>A_</c> prefix, and the vendor folders that keep their own names.</summary>
        static readonly string[] ClipDirs =
        {
            "Assets/_Project/Environments/Announcer/Voice",
            "Assets/_Project/Environments/Audio/Real",
            "Assets/_Project/Environments/Audio/Synth",
        };

        static readonly string[] VendorClipDirs = { "Assets/_Project/Environments/Audio/Kenney" };

        /// <summary>Materials that live outside the materials folder, and where they belong.</summary>
        static readonly string[] StrayMaterials =
        {
            "Assets/_Project/Agents/Boxers/MjShape.mat",
            "Assets/_Project/Data/Settings/BalanceMarks.mat",
            "Assets/_Project/Data/Settings/LightBeam.mat",
            "Assets/_Project/Data/Settings/ScreenRipple.mat",
            "Assets/_Project/Environments/Announcer/Cable.mat",
        };

        /// <summary>Reached by path or by name from code or a settings file, so no scene or prefab references them.</summary>
        static readonly string[] PathOnly =
        {
            "Assets/Art/Raw/Figures/SK_Cornerman.blend",
            "Assets/Art/Raw/Figures/SK_Referee.blend",
            "Assets/Art/Raw/Figures/SK_RingCardWalker.blend",
            "Assets/Art/Raw/Figures/SM_ChampionBelt.blend",
            "Assets/_Project/RL_Models/zero_policy.onnx",
            // Shader.Find("PoBox/VolumetricHaze"), added by the High tier's renderer (HighTier.cs).
            "Assets/_Project/Environments/Shaders/VolumetricHaze.shader",
        };

        /// <summary>Per-boxer recordings and fingerprint, read by path (MjRetrofit, MjGateTests).</summary>
        static readonly string[] RecordingNames = { "fingerprint.json", "reference_trajectory.json", "reference_hold.json", "falls.json" };

        /// <summary>Assets no scene, prefab, material or Resources folder reaches. The report proves each one.</summary>
        static readonly string[] Dead =
        {
            Materials + "/Glove_Blue.mat",
            Materials + "/Glove_Red.mat",
            Materials + "/M_Canvas_Fabric.mat",
            Materials + "/Pad_Blue.mat",
            Materials + "/Pad_Red.mat",
            Materials + "/Pad_White.mat",
            Materials + "/Rope.mat",
            // Lil Matt was retired on 2026-10-16; the announcer finds a boxer's take by name, so nothing asks for his.
            "Assets/_Project/Environments/Announcer/Voice/lilmatt.wav",
        };

        /// <summary>Editor junk that is never an asset.</summary>
        const string Recovery = "Assets/_Recovery";

        static readonly string[] AssetExtensions =
        {
            ".prefab", ".mat", ".png", ".jpg", ".exr", ".wav", ".ogg", ".glb", ".blend",
            ".shader", ".asset", ".bytes", ".json", ".anim", ".controller", ".onnx", ".uss", ".uxml",
            ".cs",
        };

        [MenuItem("PoBox/Repo/Standard Report", priority = 70)]
        public static void Report()
        {
            var log = new StringBuilder();
            List<string> names = NamingViolations();
            log.AppendLine($"naming: {names.Count} file(s) off standard");
            foreach (string n in names) log.AppendLine("  " + n);

            List<string> unreached = UnreachedAssets(out long bytes);
            log.AppendLine($"unreached: {unreached.Count} asset(s), {bytes / 1024} KB");
            foreach (string u in unreached) log.AppendLine("  " + u);

            log.AppendLine("stale test scenes:");
            foreach (string s in StaleTestScenes()) log.AppendLine("  " + s);

            log.AppendLine("unused scripts (no scene/prefab/script reference):");
            foreach (string s in UnusedScripts()) log.AppendLine("  " + s);

            Write(log);
            Debug.Log($"[RepoStandard] {names.Count} naming violation(s), {unreached.Count} unreached asset(s) ({bytes / 1024} KB). {OutPath}");
        }

        [MenuItem("PoBox/Repo/Apply Standard Fixes", priority = 71)]
        public static void Fix()
        {
            if (EditorApplication.isPlaying) { Debug.LogWarning("[RepoStandard] stop play mode first."); return; }

            var log = new StringBuilder();
            DeleteDead(log);
            AssetDatabase.Refresh();

            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (string path in Files(Props, ".prefab")) Prefix(log, path, Props + "/", "PF_");
                foreach (string path in Files(Materials, ".mat")) Prefix(log, path, Materials + "/", "M_");
                foreach (string path in StrayMaterials) Prefix(log, path, Materials + "/", "M_");

                foreach (string dir in ClipDirs)
                    foreach (string path in Files(dir, ".wav").Concat(Files(dir, ".ogg")))
                        Prefix(log, path, dir + "/", "A_");

                // A repair for the first pass, which prefixed an asset that already had the prefix.
                foreach (string path in Files(Materials, ".mat"))
                    Prefix(log, path, Materials + "/", "M_", "M_M_");

                foreach (string path in Files(League, ".asset"))
                {
                    string name = Path.GetFileNameWithoutExtension(path);
                    if (name.StartsWith("Entrant_", StringComparison.Ordinal) && name == "Entrant_" + name.Substring(8).ToUpperInvariant())
                        Move(log, path, $"{League}/Entrant_{Title(name.Substring(8))}.asset");
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            Write(log);
            Debug.Log("[RepoStandard] applied:\n" + log);
        }

        /// <summary>The known-dead assets, the recovery scene and the Test Runner's leftovers, in their own batch.</summary>
        static void DeleteDead(StringBuilder log)
        {
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (string path in Dead)
                    if (File.Exists(path)) Delete(log, path);

                if (Directory.Exists(Recovery)) Delete(log, Recovery);
                foreach (string path in Directory.GetFiles("Assets", "InitTestScene*.unity", SearchOption.TopDirectoryOnly))
                    Delete(log, path);
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
        }

        /// <summary>`grandma` -> `Grandma`, `grandmaII` -> `GrandmaII`, `soldat` -> `Soldat`.</summary>
        static string Title(string lower)
        {
            if (string.IsNullOrEmpty(lower)) return lower;
            string rest = lower.Substring(1);
            int roman = rest.Length;
            while (roman > 0 && (rest[roman - 1] == 'I' || rest[roman - 1] == 'V' || rest[roman - 1] == 'X')) roman--;
            return char.ToUpperInvariant(lower[0]) + rest.Substring(0, roman).ToLowerInvariant() + rest.Substring(roman);
        }

        /// <summary>The roster's own spelling of a boxer folder name: `sokol` -> `Sokol`.</summary>
        static string BoxerTitle(string folder)
        {
            if (string.IsNullOrEmpty(folder)) return folder;
            if (folder == "grandmaII") return "GrandmaII";
            return char.ToUpperInvariant(folder[0]) + folder.Substring(1);
        }

        /// <summary>
        /// Gives a file the folder's prefix. With <paramref name="strip"/> given it is a repair: that wrong prefix is
        /// removed and <paramref name="prefix"/> put in its place. Otherwise a file that already has the prefix is left.
        /// </summary>
        static void Prefix(StringBuilder log, string path, string dir, string prefix, string strip = null)
        {
            string name = Path.GetFileName(path);
            if (strip != null)
            {
                if (!name.StartsWith(strip, StringComparison.Ordinal)) return;
                Move(log, path, dir + prefix + name.Substring(strip.Length));
                return;
            }
            if (name.StartsWith(prefix, StringComparison.Ordinal)) return;
            Move(log, path, dir + prefix + name);
        }

        static void Move(StringBuilder log, string from, string to)
        {
            from = from.Replace('\\', '/');
            to = to.Replace('\\', '/');
            if (from == to || !File.Exists(from)) return;
            // A case-only change is a rename, not a move: on Windows File.Exists(to) is true for the same file.
            if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
            {
                string name = Path.GetFileNameWithoutExtension(to);
                string renamed = AssetDatabase.RenameAsset(from, name);
                log.AppendLine(string.IsNullOrEmpty(renamed) ? $"rename {from} -> {to}" : $"FAILED {from}: {renamed}");
                return;
            }
            if (File.Exists(to)) { log.AppendLine($"skip (target exists): {to}"); return; }
            string error = AssetDatabase.MoveAsset(from, to);
            log.AppendLine(string.IsNullOrEmpty(error) ? $"move {from} -> {to}" : $"FAILED {from}: {error}");
        }

        static void Delete(StringBuilder log, string path)
        {
            if (!AssetDatabase.DeleteAsset(path)) { log.AppendLine($"FAILED to delete {path}"); return; }
            log.AppendLine($"delete {path}");
        }

        static IEnumerable<string> Files(string dir, string extension) =>
            Directory.Exists(dir)
                ? Directory.GetFiles(dir, "*" + extension, SearchOption.TopDirectoryOnly)
                    .Where(p => !p.EndsWith(".meta", StringComparison.Ordinal))
                    .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                : Array.Empty<string>();

        static IEnumerable<string> AllFiles(string dir, string extension) =>
            Directory.Exists(dir)
                ? Directory.GetFiles(dir, "*" + extension, SearchOption.AllDirectories)
                    .Where(p => !p.EndsWith(".meta", StringComparison.Ordinal))
                    .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                : Array.Empty<string>();

        static List<string> NamingViolations()
        {
            var bad = new List<string>();
            foreach (string path in Files(Props, ".prefab")) if (!Path.GetFileName(path).StartsWith("PF_", StringComparison.Ordinal)) bad.Add(path);
            foreach (string path in Files(Materials, ".mat")) if (!Path.GetFileName(path).StartsWith("M_", StringComparison.Ordinal)) bad.Add(path);
            foreach (string dir in ClipDirs) foreach (string path in Files(dir, ".wav").Concat(Files(dir, ".ogg")))
                if (!Path.GetFileName(path).StartsWith("A_", StringComparison.Ordinal)) bad.Add(path);
            foreach (string path in Files(League, ".asset"))
            {
                string name = Path.GetFileNameWithoutExtension(path);
                if (!name.StartsWith("Entrant_", StringComparison.Ordinal)) bad.Add(path);
                else if (name.Substring(8) != Title(name.Substring(8))) bad.Add(path);
            }
            return bad;
        }

        /// <summary>
        /// Everything the scenes and the Resources folders do not reach, by the engine's own dependency walk plus the
        /// stylesheet URLs it does not model. Known path-only files are listed apart so the report does not read as
        /// "delete this".
        /// </summary>
        static List<string> UnreachedAssets(out long bytes)
        {
            var reached = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var roots = new List<string>();
            foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
                if (!string.IsNullOrEmpty(scene.path) && File.Exists(scene.path)) roots.Add(scene.path);
            // A scene is a root even when it is not in the build: the tests open Testbed.unity by path.
            foreach (string scene in Files("Assets/_Project/Scenes", ".unity")) roots.Add(scene);
            foreach (string resources in Directory.GetDirectories("Assets", "Resources", SearchOption.AllDirectories))
                roots.AddRange(Directory.GetFiles(resources, "*", SearchOption.AllDirectories).Where(p => !p.EndsWith(".meta", StringComparison.Ordinal)));

            // The render pipeline assets hang off ProjectSettings, which is not an asset the walk starts from.
            foreach (string settings in new[] { "ProjectSettings/QualitySettings.asset", "ProjectSettings/GraphicsSettings.asset" })
            {
                if (!File.Exists(settings)) continue;
                foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(settings), @"guid: ([0-9a-f]{32})"))
                {
                    string path = AssetDatabase.GUIDToAssetPath(m.Groups[1].Value);
                    if (File.Exists(path)) roots.Add(path);
                }
            }

            if (roots.Count > 0)
                foreach (string path in AssetDatabase.GetDependencies(roots.ToArray(), true))
                    reached.Add(path.Replace('\\', '/'));

            // Stylesheets point at assets by project URL, which is not a dependency the walk reports.
            foreach (string style in Directory.GetFiles("Assets", "*.uss", SearchOption.AllDirectories)
                         .Concat(Directory.GetFiles("Assets", "*.uxml", SearchOption.AllDirectories)))
            {
                foreach (string url in Urls(File.ReadAllText(style)))
                {
                    string path = url.Replace("project://database/", string.Empty).Replace("%20", " ");
                    int query = path.IndexOf('?');
                    if (query >= 0) path = path.Substring(0, query);
                    if (File.Exists(path)) reached.Add(path.Replace('\\', '/'));
                }
            }

            var found = new List<string>();
            bytes = 0;
            foreach (string root in new[] { "Assets/_Project", "Assets/Art" })
            {
                if (!Directory.Exists(root)) continue;
                foreach (string path in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
                {
                    string asset = path.Replace('\\', '/');
                    if (!AssetExtensions.Contains(Path.GetExtension(asset).ToLowerInvariant())) continue;
                    if (asset.Contains("/Resources/")) continue;
                    if (reached.Contains(asset) || PathOnly.Contains(asset)) continue;
                    if (RecordingNames.Contains(Path.GetFileName(asset)) && asset.Contains("/Agents/")) continue;
                    if (File.Exists(asset + ".meta") && AssetDatabase.AssetPathToGUID(asset) == string.Empty) continue;
                    found.Add(asset);
                    bytes += new FileInfo(asset).Length;
                }
            }
            found.Sort(StringComparer.OrdinalIgnoreCase);
            return found;
        }

        static IEnumerable<string> Urls(string text)
        {
            const string Key = "project://database/";
            int at = text.IndexOf(Key, StringComparison.Ordinal);
            while (at >= 0)
            {
                int end = at;
                while (end < text.Length && !char.IsWhiteSpace(text[end]) && text[end] != '"' && text[end] != ')' && text[end] != '\'') end++;
                yield return text.Substring(at, end - at);
                at = text.IndexOf(Key, end, StringComparison.Ordinal);
            }
        }

        static IEnumerable<string> UnusedScripts()
        {
            var referenced = new HashSet<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:Scene t:Prefab t:ScriptableObject", new[] { "Assets/_Project" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!File.Exists(path)) continue;
                foreach (Match m in Regex.Matches(File.ReadAllText(path), "guid: ([0-9a-f]{32})"))
                    referenced.Add(m.Groups[1].Value);
            }

            var scripts = new Dictionary<string, string>();
            foreach (string guid in AssetDatabase.FindAssets("t:MonoScript", new[] { "Assets/_Project" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith(".cs")) scripts[guid] = path;
            }

            foreach (var kv in scripts)
            {
                string path = kv.Value;
                string name = Path.GetFileNameWithoutExtension(path);
                if (referenced.Contains(kv.Key)) continue;
                if (path.Contains("/Editor/") || path.Contains("/Tests/") || path.Contains("/EditorTests/")) continue;
                if (path.Contains("/Resources/")) continue;
                int uses = 0;
                foreach (string other in scripts.Values)
                {
                    if (other == path) continue;
                    if (Regex.IsMatch(File.ReadAllText(other), $@"\b{Regex.Escape(name)}\b")) uses++;
                }
                if (uses == 0) yield return path;
            }
        }

        static void Write(StringBuilder log)
        {
            Directory.CreateDirectory("Temp");
            File.WriteAllText(OutPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm") + "\n" + log);
        }
    }
}
