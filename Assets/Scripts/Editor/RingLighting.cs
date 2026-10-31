using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace PoBox.EditorTools
{
    /// <summary>
    /// The broadcast lighting of 2026-10-31 on the arena scenes (PoBox/Graphics/Light The Ring): the four ring spots
    /// cast soft shadows (a pool of light with the boxers' shadows in it, which the haze on High then shows as shafts);
    /// the six house lamps stop casting them (36 cube faces that shared one atlas, each too small to be anything but a
    /// blur); and a box-projected reflection probe over the ring, rendered once when the scene starts, so the canvas,
    /// posts and skin reflect the hall and its lights instead of the sky. A tier without additional-light shadows or
    /// realtime probes (Mobile) simply ignores both.
    /// </summary>
    public static class RingLighting
    {
        [MenuItem("PoBox/Graphics/Light The Ring")]
        public static void LightAll()
        {
            foreach (string path in new[] { "Assets/Scenes/Arena.unity", "Assets/Scenes/Melee.unity" })
                Light(path);
        }

        public static void Light(string path)
        {
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            int spots = 0, house = 0;
            Vector3 centre = Vector3.zero;
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (l.name.StartsWith("Spot_"))
                {
                    l.shadows = LightShadows.Soft;
                    l.shadowStrength = 0.85f;
                    l.shadowResolution = UnityEngine.Rendering.LightShadowResolution.High;
                    l.shadowNearPlane = 0.5f;
                    EditorUtility.SetDirty(l);
                    centre += l.transform.position;
                    spots++;
                }
                else if (l.name.StartsWith("House Light"))
                {
                    l.shadows = LightShadows.None;
                    EditorUtility.SetDirty(l);
                    house++;
                }
            }
            if (spots > 0) centre /= spots;

            var probe = GameObject.Find("Ring Probe");
            if (probe == null)
            {
                probe = new GameObject("Ring Probe");
                var lighting = GameObject.Find("Lighting");
                if (lighting != null) probe.transform.SetParent(lighting.transform, false);
            }
            // Over the canvas at a boxer's chest height, the box the hall around the ring.
            probe.transform.position = new Vector3(centre.x, 1.3f, centre.z);
            var rp = probe.GetComponent<ReflectionProbe>();
            if (rp == null) rp = probe.AddComponent<ReflectionProbe>();
            rp.mode = ReflectionProbeMode.Realtime;
            rp.refreshMode = ReflectionProbeRefreshMode.OnAwake;
            rp.timeSlicingMode = ReflectionProbeTimeSlicingMode.IndividualFaces;
            rp.boxProjection = true;
            rp.size = new Vector3(16f, 9f, 16f);
            rp.center = new Vector3(0f, 2.5f, 0f);
            rp.resolution = 256;
            rp.hdr = true;
            rp.importance = 1;
            rp.intensity = 1f;
            rp.clearFlags = ReflectionProbeClearFlags.Skybox;
            EditorUtility.SetDirty(rp);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[RingLighting] {path}: {spots} ring spots shadowed, {house} house lamps unshadowed, probe at {probe.transform.position}");
        }
    }
}
