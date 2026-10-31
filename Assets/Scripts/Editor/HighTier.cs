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
            // STP upscaling from 80% scale replaces MSAA 4x + FXAA: sharper, temporally stable, and it pays for the rest.
            so.FindProperty("m_MSAA").intValue = 1;
            so.FindProperty("m_RenderScale").floatValue = 0.8f;
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
            AssetDatabase.SaveAssets();

            AddLevel(rp);
            Debug.Log("[HighTier] High_RPAsset / High_Renderer made; quality levels: " + string.Join(", ", QualitySettings.names));
        }

        static T Copy<T>(string from, string to) where T : ScriptableObject
        {
            string src = Dir + from + ".asset", dst = Dir + to + ".asset";
            if (AssetDatabase.LoadAssetAtPath<T>(dst) != null) AssetDatabase.DeleteAsset(dst);
            if (!AssetDatabase.CopyAsset(src, dst)) throw new Exception($"could not copy {src} to {dst}");
            AssetDatabase.ImportAsset(dst);
            return AssetDatabase.LoadAssetAtPath<T>(dst);
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
