using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace PoBox.EditorTools
{
    /// <summary>
    /// The High quality tier (2026-10-31, owner: "more impressive until it comes down to 30 fps"): PC's pipeline with
    /// everything turned up, for desktop only. PoBox/Graphics/Make High Tier copies PC_RPAsset and PC_Renderer to
    /// High_RPAsset and High_Renderer, sets them up, and adds (or refreshes) a "High" level after "PC" in the quality
    /// settings, excluded from Android and WebGL (STP needs compute) and the desktop default. Run it again after
    /// changing PC's assets: High is always PC plus the settings below.
    /// </summary>
    public static class HighTier
    {
        const string Dir = "Assets/Settings/";

        [MenuItem("PoBox/Graphics/Make High Tier")]
        public static void Make()
        {
            var rp = Copy<UniversalRenderPipelineAsset>("PC_RPAsset", "High_RPAsset");
            var renderer = Copy<UniversalRendererData>("PC_Renderer", "High_Renderer");

            var so = new SerializedObject(rp);
            so.FindProperty("m_RendererDataList").GetArrayElementAtIndex(0).objectReferenceValue = renderer;
            // STP from 85% replaces MSAA 4x + FXAA: temporal anti-aliasing, sharper and stable. Measured on the RTX 2060 in a
            // fight, 2026-10-31 (the owner: "until it comes down to 30 fps"): everything on, 1.0 = 21-25 fps, 0.85 = 32-34.
            so.FindProperty("m_MSAA").intValue = 1;
            so.FindProperty("m_RenderScale").floatValue = 0.85f;
            so.FindProperty("m_UpscalingFilter").intValue = (int)UpscalingFilterSelection.STP;
            // Sharper shadows: the ring spots now cast them (Lighting: Arena's ring spots), so the atlas grows with them.
            so.FindProperty("m_MainLightShadowmapResolution").intValue = 4096;
            so.FindProperty("m_AdditionalLightsShadowmapResolution").intValue = 4096;
            so.FindProperty("m_SoftShadowQuality").intValue = 3;
            // The GPU Resident Drawer stays off: it needs every BatchRendererGroup variant kept in the build, for a
            // scene whose crowd is one draw call already.
            so.ApplyModifiedPropertiesWithoutUndo();

            // Contact shadows that read: full resolution, more samples, high blur, a wider radius.
            var data = new SerializedObject(renderer);
            SerializedProperty features = data.FindProperty("m_RendererFeatures");
            for (int i = 0; i < features.arraySize; i++)
            {
                var f = features.GetArrayElementAtIndex(i).objectReferenceValue;
                if (f == null || f.GetType().Name != "ScreenSpaceAmbientOcclusion") continue;
                var ao = new SerializedObject(f);
                ao.FindProperty("m_Settings.Downsample").boolValue = false;
                ao.FindProperty("m_Settings.Samples").intValue = 0;          // High
                ao.FindProperty("m_Settings.BlurQuality").intValue = 0;      // High
                ao.FindProperty("m_Settings.Radius").floatValue = 0.4f;
                ao.FindProperty("m_Settings.Intensity").floatValue = 1.4f;
                ao.ApplyModifiedPropertiesWithoutUndo();
            }
            data.ApplyModifiedPropertiesWithoutUndo();
            var haze = AddFeature<PoBox.Fx.VolumetricHaze>(renderer, "Volumetric Haze");
            // Quarter resolution, 16 steps, hard shadow taps (measured: full-res 64 steps with soft taps cost 400 ms).
            haze.settings.halfResolution = true;
            haze.settings.quarterResolution = true;
            haze.settings.steps = 16;
            EditorUtility.SetDirty(haze);
            AssetDatabase.SaveAssets();

            AddLevel(rp);
            Debug.Log("[HighTier] High_RPAsset / High_Renderer made; quality levels: " + string.Join(", ", QualitySettings.names));
        }

        /// <summary>A renderer feature as a sub-asset of <paramref name="renderer"/>, in its list and its id map.</summary>
        static T AddFeature<T>(ScriptableRendererData renderer, string name) where T : ScriptableRendererFeature
        {
            // Any earlier one goes first: the command can be run again.
            var old = new SerializedObject(renderer);
            SerializedProperty oldList = old.FindProperty("m_RendererFeatures"), oldMap = old.FindProperty("m_RendererFeatureMap");
            for (int i = oldList.arraySize - 1; i >= 0; i--)
            {
                if (!(oldList.GetArrayElementAtIndex(i).objectReferenceValue is T stale)) continue;
                oldList.GetArrayElementAtIndex(i).objectReferenceValue = null;
                oldList.DeleteArrayElementAtIndex(i);
                oldMap.DeleteArrayElementAtIndex(i);
                old.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.RemoveObjectFromAsset(stale);
                UnityEngine.Object.DestroyImmediate(stale, true);
            }
            var feature = ScriptableObject.CreateInstance<T>();
            feature.name = name;
            AssetDatabase.AddObjectToAsset(feature, renderer);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out string _, out long id);
            var data = new SerializedObject(renderer);
            SerializedProperty list = data.FindProperty("m_RendererFeatures"), map = data.FindProperty("m_RendererFeatureMap");
            list.InsertArrayElementAtIndex(list.arraySize);
            list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = feature;
            map.InsertArrayElementAtIndex(map.arraySize);
            map.GetArrayElementAtIndex(map.arraySize - 1).longValue = id;
            data.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(renderer);
            return feature;
        }

        static T Copy<T>(string from, string to) where T : ScriptableObject
        {
            // Once made, the asset keeps its GUID (the quality level and scenes point at it): PC's settings are copied
            // over it, its own sub-assets (the features) stay, and are reset below.
            string src = Dir + from + ".asset", dst = Dir + to + ".asset";
            T made = AssetDatabase.LoadAssetAtPath<T>(dst);
            if (made == null)
            {
                if (!AssetDatabase.CopyAsset(src, dst)) throw new Exception($"could not copy {src} to {dst}");
                AssetDatabase.ImportAsset(dst);
                return AssetDatabase.LoadAssetAtPath<T>(dst);
            }
            if (typeof(T) == typeof(UniversalRenderPipelineAsset))
            {
                string name = made.name;
                EditorUtility.CopySerialized(AssetDatabase.LoadAssetAtPath<T>(src), made);
                made.name = name;
            }
            return made;
        }

        static void AddLevel(UniversalRenderPipelineAsset rp)
        {
            var q = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset")[0]);
            SerializedProperty levels = q.FindProperty("m_QualitySettings");
            int pc = -1, high = -1;
            for (int i = 0; i < levels.arraySize; i++)
            {
                string n = levels.GetArrayElementAtIndex(i).FindPropertyRelative("name").stringValue;
                if (n == "PC") pc = i;
                if (n == "High") high = i;
            }
            if (pc < 0) throw new Exception("no PC quality level to copy");
            if (high < 0)
            {
                levels.InsertArrayElementAtIndex(pc);           // the copy lands at pc + 1
                high = pc + 1;
            }
            SerializedProperty h = levels.GetArrayElementAtIndex(high);
            h.FindPropertyRelative("name").stringValue = "High";
            h.FindPropertyRelative("customRenderPipeline").objectReferenceValue = rp;
            h.FindPropertyRelative("shadowDistance").floatValue = 30f;
            h.FindPropertyRelative("lodBias").floatValue = 2f;
            h.FindPropertyRelative("antiAliasing").intValue = 0;
            h.FindPropertyRelative("realtimeReflectionProbes").boolValue = true;
            SerializedProperty excluded = h.FindPropertyRelative("excludedTargetPlatforms");
            excluded.ClearArray();
            foreach (string platform in new[] { "Android", "iPhone", "WebGL" })
            {
                excluded.InsertArrayElementAtIndex(excluded.arraySize);
                excluded.GetArrayElementAtIndex(excluded.arraySize - 1).stringValue = platform;
            }
            // The desktop starts on High; phones and the browser keep their own defaults.
            SerializedProperty per = q.FindProperty("m_PerPlatformDefaultQuality");
            for (int i = 0; i < per.arraySize; i++)
            {
                SerializedProperty e = per.GetArrayElementAtIndex(i);
                if (e.FindPropertyRelative("first").stringValue == "Standalone") e.FindPropertyRelative("second").intValue = high;
            }
            q.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
