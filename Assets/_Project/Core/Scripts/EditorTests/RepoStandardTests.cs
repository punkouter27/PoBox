using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using PoBox.League;
using UnityEditor;

namespace PoBox.EditorTests
{
    /// <summary>
    /// The repo's naming and folder standard, as a test instead of a note: the next <c>Pad_Red.mat</c> fails here
    /// rather than in a review. The prefix list is AGENTS.md's (`PF_` prefab, `M_` material, `A_` audio clip); an
    /// asset that is reached by name or by URL keeps its own name and is out of scope: anything under a
    /// <c>Resources</c> folder, the boxer prefabs and folders, the portraits and the icons.
    /// </summary>
    public class RepoStandardTests
    {
        const string Materials = "Assets/_Project/Environments/Materials";
        const string Props = "Assets/_Project/Environments/Prefabs/Props";
        const string League = "Assets/_Project/Data/League";
        const string Boxers = "Assets/_Project/Agents/Boxers";

        static readonly string[] ClipDirs =
        {
            "Assets/_Project/Environments/Announcer/Voice",
            "Assets/_Project/Environments/Audio/Real",
            "Assets/_Project/Environments/Audio/Synth",
        };

        [Test]
        public void EveryMaterialIsPrefixed()
        {
            Assert.IsEmpty(Unprefixed(Materials, ".mat", "M_"), "materials take M_ (Environments/Materials)");
        }

        [Test]
        public void EveryPropPrefabIsPrefixed()
        {
            Assert.IsEmpty(Unprefixed(Props, ".prefab", "PF_"), "prefabs take PF_ (Environments/Prefabs/Props)");
        }

        [Test]
        public void EveryFirstPartyClipIsPrefixed()
        {
            foreach (string dir in ClipDirs)
            {
                Assert.IsEmpty(Unprefixed(dir, ".wav", "A_"), dir);
                Assert.IsEmpty(Unprefixed(dir, ".ogg", "A_"), dir);
            }
        }

        [Test]
        public void EveryEntrantIsPascalCase()
        {
            foreach (string path in Files(League, ".asset"))
            {
                string name = Path.GetFileNameWithoutExtension(path);
                Assert.IsTrue(name.StartsWith("Entrant_", System.StringComparison.Ordinal), name);
                string who = name.Substring("Entrant_".Length);
                Assert.AreEqual(char.ToUpperInvariant(who[0]) + who.Substring(1).ToLowerInvariant(), who, name);
            }
        }

        [Test]
        public void EveryBoxerWithABrainHasAnEntrantProfile()
        {
            var profiles = AssetDatabase.FindAssets("t:PolicyProfile", new[] { League })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<PolicyProfile>)
                .Where(p => p != null)
                .Select(p => p.displayName)
                .ToList();
            Assert.IsNotEmpty(profiles, "the ladder has entrants");

            foreach (string dir in Directory.GetDirectories(Boxers))
            {
                string who = Path.GetFileName(dir);
                if (!File.Exists(Path.Combine(dir, "policy.onnx"))) continue; // a body with no brain yet
                Assert.Contains(who.ToUpperInvariant(), profiles, $"{who} has a brain but no {League}/Entrant_{who}.asset");
            }
        }

        [Test]
        public void NoRecoverySceneIsCommitted()
        {
            Assert.IsFalse(Directory.Exists("Assets/_Recovery"), "Unity's crash-recovery scene is not an asset (gitignored)");
        }

        static List<string> Unprefixed(string dir, string extension, string prefix)
        {
            if (!Directory.Exists(dir)) return new List<string>();
            return Files(dir, extension)
                .Select(Path.GetFileName)
                .Where(name => !name.StartsWith(prefix, System.StringComparison.Ordinal))
                .ToList();
        }

        static IEnumerable<string> Files(string dir, string extension) =>
            Directory.Exists(dir)
                ? Directory.GetFiles(dir, "*" + extension, SearchOption.TopDirectoryOnly).Where(p => !p.EndsWith(".meta", System.StringComparison.Ordinal))
                : Enumerable.Empty<string>();
    }
}
