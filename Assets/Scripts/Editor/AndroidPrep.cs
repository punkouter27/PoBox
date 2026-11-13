using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace PoBox.EditorTools
{
    /// <summary>
    /// Getting the game onto an Android phone with its fight still running on MuJoCo.
    ///
    /// The fight is stepped by MuJoCo's own library, from the embedded package Packages/bin.mujoco
    /// (joanllobera/mujoco-bin#3.5.0, the house rule). That tag's Android library is MuJoCo 3.3.7, where the plugin
    /// and the Windows library are 3.5.0, and the two lay mjModel and mjData out differently; the one here is 3.5.0,
    /// built for arm64 with the NDK (Packages/bin.mujoco/README-PoBox.md). The report reads the version out of the
    /// library itself, so a 3.3.7 one put back by a package update is caught.
    ///
    /// <c>PoBox/Android/Prepare</c> sets the plug-ins' platforms and the player settings a phone build needs
    /// and says what is still missing. <c>PoBox/Android/Build APK</c> builds, and switches the editor to the
    /// Android target to do it (every asset is re-imported, both ways).
    /// </summary>
    public static class AndroidPrep
    {
        public const string Library = "Packages/bin.mujoco/Runtime/Plugins/Android/libmujoco.so";
        const string WindowsLibrary = "Packages/bin.mujoco/Runtime/Plugins/x86_64/mujoco.dll";

        [MenuItem("PoBox/Android/Prepare", priority = 60)]
        public static void Prepare() => Debug.Log("[PoBox] Android: " + Report(true));

        /// <summary>What is ready and what is missing, as one line. With <paramref name="apply"/>, the settings are made first.</summary>
        public static string Report(bool apply)
        {
            var missing = new System.Collections.Generic.List<string>();
            var ready = new System.Collections.Generic.List<string>();

            if (File.Exists(Library))
            {
                if (apply)
                {
                    var plugin = AssetImporter.GetAtPath(Library) as PluginImporter;
                    if (plugin != null)
                    {
                        plugin.SetCompatibleWithAnyPlatform(false);
                        plugin.SetCompatibleWithEditor(false);
                        plugin.SetCompatibleWithPlatform(BuildTarget.Android, true);
                        plugin.SetPlatformData(BuildTarget.Android, "CPU", "ARM64");
                        plugin.SaveAndReimport();
                    }
                    // The Windows library must not be offered to a phone build.
                    var windows = AssetImporter.GetAtPath(WindowsLibrary) as PluginImporter;
                    if (windows != null && windows.GetCompatibleWithPlatform(BuildTarget.Android))
                    {
                        windows.SetCompatibleWithPlatform(BuildTarget.Android, false);
                        windows.SaveAndReimport();
                    }
                }
                string version = System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(Library)).Contains("3.5.0") ? "3.5.0" : null;
                if (version != null) ready.Add($"MuJoCo {version} library for arm64 ({new FileInfo(Library).Length / (1024 * 1024)} MB)");
                else missing.Add("a MuJoCo 3.5.0 library for arm64 (" + Library + " is another version; Packages/bin.mujoco/README-PoBox.md)");
            }
            else missing.Add("the MuJoCo library (" + Library + ", Packages/bin.mujoco/README-PoBox.md)");


            if (apply)
            {
                // The library is built for 64-bit ARM and Android 12; nothing older can load it.
                PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
                PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
                if ((int)PlayerSettings.Android.minSdkVersion < 31) PlayerSettings.Android.minSdkVersion = (AndroidSdkVersions)31;
                PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android, PlayAab.APP_ID);
                PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
                // A phone starts on the Mobile quality tier.
                string[] names = QualitySettings.names;
                int mobile = Array.IndexOf(names, "Mobile");
                if (mobile >= 0)
                {
                    var quality = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset")[0]);
                    SerializedProperty per = quality.FindProperty("m_PerPlatformDefaultQuality");
                    if (per != null)
                    {
                        bool found = false;
                        for (int i = 0; i < per.arraySize; i++)
                        {
                            SerializedProperty entry = per.GetArrayElementAtIndex(i);
                            if (entry.FindPropertyRelative("first").stringValue != "Android") continue;
                            entry.FindPropertyRelative("second").intValue = mobile;
                            found = true;
                        }
                        if (!found)
                        {
                            per.arraySize++;
                            SerializedProperty entry = per.GetArrayElementAtIndex(per.arraySize - 1);
                            entry.FindPropertyRelative("first").stringValue = "Android";
                            entry.FindPropertyRelative("second").intValue = mobile;
                        }
                        quality.ApplyModifiedPropertiesWithoutUndo();
                    }
                }
                AssetDatabase.SaveAssets();
            }
            ready.Add("player settings (IL2CPP, ARM64, Android 12+, portrait, Mobile tier)");

            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android))
                missing.Add("Unity's Android Build Support module");
            // The SDK, NDK and JDK: wherever the editor has been told they are (Preferences > External Tools).
            // Asked by reflection, so this file compiles on a machine without the Android module.
            Type tools = Type.GetType("UnityEditor.Android.AndroidExternalToolsSettings, UnityEditor.Android.Extensions");
            foreach ((string property, string what) in new[] { ("sdkRootPath", "Android SDK"), ("ndkRootPath", "Android NDK"), ("jdkRootPath", "JDK") })
            {
                string path = null;
                try { path = tools?.GetProperty(property)?.GetValue(null) as string; } catch (Exception) { }
                if (!string.IsNullOrEmpty(path) && Directory.Exists(path)) ready.Add($"{what} ({path})");
                else missing.Add(what + " (Unity Hub > Installs > this editor > Add modules, or Preferences > External Tools)");
            }

            return (missing.Count == 0 ? "ready to build. " : "NOT ready: missing " + string.Join("; ", missing) + ". ") + "In place: " + string.Join("; ", ready) + ".";
        }

        [MenuItem("PoBox/Android/Build APK", priority = 61)]
        public static void Build()
        {
            // A cross-target build from another platform can strip URP and glTFast shaders (PlayAab.cs).
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android &&
                !EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android))
            {
                Debug.LogError("[PoBox] Android build aborted — failed to switch active build target to Android.");
                return;
            }
            string report = Report(true);
            if (report.StartsWith("NOT ready")) { Debug.LogError("[PoBox] Android build not started. " + report); return; }
            Directory.CreateDirectory("Build/Android");
            // The version is the day it was built, YYMMDD, shown bottom right on every screen; as the version code it
            // only ever goes up, so a phone takes each build over the last.
            string day = DateTime.Now.ToString("yyMMdd");
            PlayerSettings.bundleVersion = day;
            PlayerSettings.Android.bundleVersionCode = int.Parse(day);
            AssetDatabase.SaveAssets();
            var options = new BuildPlayerOptions
            {
                scenes = Array.ConvertAll(EditorBuildSettings.scenes, s => s.path),
                locationPathName = "Build/Android/PoBox.apk",
                target = BuildTarget.Android,
                options = BuildOptions.None,
            };
            BuildSummary summary = BuildPipeline.BuildPlayer(options).summary;
            Debug.Log($"[PoBox] Android build {summary.result}: {summary.totalSize / (1024 * 1024)} MB in {summary.totalTime.TotalMinutes:0.0} min, {summary.totalErrors} error(s). {summary.outputPath}");
        }

        /// <summary>Applies safe, lossless-to-eye/ear texture (ASTC 6x6, UI mipmaps off) and audio (Vorbis quality, mono SFX/voice, CompressedInMemory for long clips) import settings.</summary>
        [MenuItem("PoBox/Android/Optimize Assets", priority = 62)]
        public static void OptimizeAssets()
        {
            int texCount = 0, audioCount = 0;
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (string p in Directory.GetFiles("Assets/UI/Icons", "*.png"))
                    if (SetTexture(p, mipmap: false, npotNone: true, androidMax: 128, TextureImporterFormat.ASTC_6x6)) texCount++;

                foreach (string p in Directory.GetFiles("Assets/UI/Portraits", "*.png"))
                    if (SetTexture(p, mipmap: false, npotNone: true, androidMax: 1024, TextureImporterFormat.ASTC_6x6)) texCount++;

                foreach (string dir in new[] { "Assets/Textures/Generated", "Assets/Textures/Kenney" })
                {
                    if (!Directory.Exists(dir)) continue;
                    foreach (string p in Directory.GetFiles(dir, "*.png"))
                        if (SetTexture(p, mipmap: null, npotNone: false, androidMax: 1024, TextureImporterFormat.ASTC_6x6)) texCount++;
                }
                if (File.Exists("Assets/Textures/AmbientCG/Fabric030_Normal.jpg"))
                    if (SetTexture("Assets/Textures/AmbientCG/Fabric030_Normal.jpg", mipmap: null, npotNone: false, androidMax: 1024, TextureImporterFormat.ASTC_6x6)) texCount++;

                // Announcer clips call AudioClip.GetData() for jaw animation, so they must stay DecompressOnLoad.
                foreach (string p in Directory.GetFiles("Assets/Announcer/Voice", "*.wav"))
                    if (SetAudio(p, AudioClipLoadType.DecompressOnLoad, 0.6f, forceMono: true)) audioCount++;

                if (File.Exists("Assets/Audio/Real/crowdBed.wav") && SetAudio("Assets/Audio/Real/crowdBed.wav", AudioClipLoadType.Streaming, 0.5f, forceMono: false)) audioCount++;
                if (File.Exists("Assets/Audio/Real/crowdApplause.wav") && SetAudio("Assets/Audio/Real/crowdApplause.wav", AudioClipLoadType.CompressedInMemory, 0.55f, forceMono: true)) audioCount++;
                foreach (string p in new[] { "Assets/Audio/Real/crowdSwell.wav", "Assets/Audio/Real/crowdGroan.wav" })
                    if (File.Exists(p) && SetAudio(p, AudioClipLoadType.CompressedInMemory, 0.55f, forceMono: false)) audioCount++;
                foreach (string p in new[] { "Assets/Audio/Real/breath.wav", "Assets/Audio/Real/lapBell.wav" })
                    if (File.Exists(p) && SetAudio(p, AudioClipLoadType.CompressedInMemory, 0.65f, forceMono: true)) audioCount++;
                if (File.Exists("Assets/Audio/Real/sting.wav") && SetAudio("Assets/Audio/Real/sting.wav", AudioClipLoadType.DecompressOnLoad, 0.65f, forceMono: false)) audioCount++;

                if (Directory.Exists("Assets/Audio/Synth"))
                    foreach (string p in Directory.GetFiles("Assets/Audio/Synth", "*.wav"))
                        if (SetAudio(p, AudioClipLoadType.DecompressOnLoad, 0.7f, forceMono: true)) audioCount++;

                if (Directory.Exists("Assets/Audio/Kenney"))
                    foreach (string p in Directory.GetFiles("Assets/Audio/Kenney", "*.ogg"))
                        if (SetAudio(p, AudioClipLoadType.DecompressOnLoad, 0.7f, forceMono: true)) audioCount++;
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
            Debug.Log($"[PoBox] Optimized {texCount} texture(s) and {audioCount} audio clip(s).");
        }

        [MenuItem("PoBox/Android/Last Build Breakdown", priority = 63)]
        public static void LastBuildBreakdown()
        {
            if (!File.Exists("Library/LastBuild.buildreport")) { Debug.LogWarning("[PoBox] No Library/LastBuild.buildreport found."); return; }
            var objs = UnityEditorInternal.InternalEditorUtility.LoadSerializedFileAndForget("Library/LastBuild.buildreport");
            var br = objs != null && objs.Length > 0 ? objs[0] as BuildReport : null;
            if (br == null) return;
            var byFolder = new System.Collections.Generic.Dictionary<string, ulong>();
            var items = new System.Collections.Generic.List<(ulong size, string type, string path)>();
            foreach (PackedAssets pa in br.packedAssets)
                foreach (PackedAssetInfo c in pa.contents)
                {
                    items.Add((c.packedSize, c.type.ToString(), c.sourceAssetPath));
                    string[] parts = (c.sourceAssetPath ?? "").Split('/');
                    string key = parts.Length >= 2 ? parts[0] + "/" + parts[1] : (string.IsNullOrEmpty(c.sourceAssetPath) ? "(builtin)" : c.sourceAssetPath);
                    byFolder[key] = (byFolder.TryGetValue(key, out ulong v) ? v : 0) + c.packedSize;
                }
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("[PoBox] BuildReport folders (uncompressed packedSize):");
            foreach (var kv in System.Linq.Enumerable.OrderByDescending(byFolder, x => x.Value))
                if (kv.Value >= 512 * 1024) sb.AppendLine($"  {kv.Value / (1024.0 * 1024.0):F2} MB  {kv.Key}");
            sb.AppendLine("[PoBox] Top 20 packed assets:");
            foreach (var it in System.Linq.Enumerable.Take(System.Linq.Enumerable.OrderByDescending(items, x => x.size), 20))
                sb.AppendLine($"  {it.size / (1024.0 * 1024.0):F2} MB | {it.type} | {it.path}");
            Debug.Log(sb.ToString());
        }

        static bool SetTexture(string path, bool? mipmap, bool npotNone, int androidMax, TextureImporterFormat androidFormat)
        {
            var ti = AssetImporter.GetAtPath(path.Replace('\\', '/')) as TextureImporter;
            if (ti == null) return false;
            bool dirty = false;
            if (mipmap.HasValue && ti.mipmapEnabled != mipmap.Value) { ti.mipmapEnabled = mipmap.Value; dirty = true; }
            if (npotNone && ti.npotScale != TextureImporterNPOTScale.None) { ti.npotScale = TextureImporterNPOTScale.None; dirty = true; }
            TextureImporterPlatformSettings def = ti.GetDefaultPlatformTextureSettings();
            int cap = Mathf.Min(def.maxTextureSize, androidMax);
            TextureImporterPlatformSettings ps = ti.GetPlatformTextureSettings("Android");
            if (!ps.overridden || ps.maxTextureSize != cap || ps.format != androidFormat)
            {
                ps.overridden = true;
                ps.maxTextureSize = cap;
                ps.format = androidFormat;
                ti.SetPlatformTextureSettings(ps);
                dirty = true;
            }
            if (dirty) ti.SaveAndReimport();
            return dirty;
        }

        static bool SetAudio(string path, AudioClipLoadType loadType, float quality, bool forceMono)
        {
            var ai = AssetImporter.GetAtPath(path.Replace('\\', '/')) as AudioImporter;
            if (ai == null) return false;
            bool dirty = false;
            if (ai.forceToMono != forceMono) { ai.forceToMono = forceMono; dirty = true; }
            AudioImporterSampleSettings s = ai.defaultSampleSettings;
            if (s.loadType != loadType || s.compressionFormat != AudioCompressionFormat.Vorbis || !Mathf.Approximately(s.quality, quality))
            {
                s.loadType = loadType;
                s.compressionFormat = AudioCompressionFormat.Vorbis;
                s.quality = quality;
                ai.defaultSampleSettings = s;
                dirty = true;
            }
            if (dirty) ai.SaveAndReimport();
            return dirty;
        }
    }
}
