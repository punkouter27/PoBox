using System.IO;
using UnityEditor;

namespace PoBox.EditorTools
{
    /// <summary>Where the editor tools find the scenes and the folders they write into.</summary>
    public static class Paths
    {
        public const string ArenaScene = "Assets/_Project/Scenes/Arena.unity";
        public const string MenuScene = "Assets/_Project/Scenes/Menu.unity";
        public const string MeleeScene = "Assets/_Project/Scenes/Melee.unity";
        public const string TestbedScene = "Assets/_Project/Scenes/Testbed.unity";
        public const string SettingsDir = "Assets/_Project/Data/Settings";
        public const string LeagueDir = "Assets/_Project/Data/League";
        public const string PortraitDir = "Assets/_Project/UI/Portraits";

        public static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
