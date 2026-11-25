using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace PoBox.EditorTools
{
    /// <summary>
    /// Adaptive Probe Volumes for the arena (2026-11-25), in one place because it cannot be finished headless: the
    /// lighting settings flag has no public API and probe volumes only exist once they are baked, which needs a GPU.
    /// Enabling the flag without baked probes ships a flat arena (URP falls back to ambient), so this refuses to
    /// touch anything from batchmode and does the whole job in one interactive command instead.
    ///
    /// Run it in the editor with the Arena scene closed or open: it opens the scene, turns APV on in the lighting
    /// settings, adds a probe volume over the ring if the scene has none, and starts the bake.
    /// </summary>
    public static class GraphicsProbeVolumes
    {
        const string ScenePath = "Assets/_Project/Scenes/Arena.unity";
        const string LightingPath = "Assets/_Project/Data/Settings/PoBox.lighting";

        [MenuItem("PoBox/Graphics/Adaptive Probe Volumes (arena)")]
        public static void Enable()
        {
            if (Application.isBatchMode)
            {
                Debug.LogWarning("[PoBox] Adaptive Probe Volumes need a GPU bake: open the editor and run PoBox/Graphics/Adaptive Probe Volumes (arena).");
                return;
            }

            var settings = AssetDatabase.LoadAssetAtPath<LightingSettings>(LightingPath);
            if (settings == null)
            {
                Debug.LogError($"[PoBox] No lighting settings at {LightingPath}.");
                return;
            }

            // The lighting settings' own asset: APV is a serialized field with no public accessor, so it is set there.
            var so = new SerializedObject(settings);
            SerializedProperty apv = so.FindProperty("m_UseAdaptiveProbeVolumes");
            if (apv == null)
            {
                Debug.LogWarning("[PoBox] This Unity does not serialize Adaptive Probe Volumes on LightingSettings; enable them from the Lighting window instead.");
            }
            else
            {
                apv.boolValue = true;
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssets();
            }

            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            if (Object.FindAnyObjectByType<ProbeVolume>() == null)
            {
                var go = new GameObject("Arena Probe Volume");
                var volume = go.AddComponent<ProbeVolume>();
                volume.transform.position = new Vector3(0f, 1.2f, 0f);
                // A single volume over the hall: the ring first, the stands coarse.
                volume.size = new Vector3(26f, 12f, 26f);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                Debug.Log("[PoBox] Added a probe volume over the arena.");
            }

            // The bake itself: baking sets, probes and lightmaps, on this machine's GPU.
            Lightmapping.BakeAsync();
            Debug.Log("[PoBox] Adaptive Probe Volumes on; baking. Save the scene when the bake finishes.");
        }
    }
}
