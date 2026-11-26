using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PoBox.Fx
{
    /// <summary>
    /// The camera's antialiasing, by quality level (2026-11-25). URP takes antialiasing from the camera itself,
    /// and QualitySettings' own MSAA value is ignored by URP entirely, so the three tiers could not be told
    /// apart: the scenes were authored once with FXAA on every broadcast camera. This sets it per scene from
    /// the tier instead — temporal antialiasing on PC and High (the boxers are skinned meshes driven from
    /// MuJoCo, so Unity writes real motion vectors for them), FXAA on Mobile, where TAA's extra pass is not
    /// affordable. TAA and MSAA are mutually exclusive in URP, so the PC and High pipeline assets carry MSAA 1.
    /// </summary>
    public static class CameraQuality
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            Apply();
            QualitySettings.activeQualityLevelChanged -= OnLevelChanged;
            QualitySettings.activeQualityLevelChanged += OnLevelChanged;
        }

        static void OnLevelChanged(int level, int previous) => Apply();

        /// <summary>The tier whose name is "Mobile" (the lowest) is the phone.</summary>
        public static bool Mobile
        {
            get
            {
                string[] names = QualitySettings.names;
                int level = QualitySettings.GetQualityLevel();
                string name = level >= 0 && level < names.Length ? names[level] : "";
                return string.Equals(name, "Mobile", System.StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>Sets every camera in the loaded scenes. Safe to call again when the tier changes.</summary>
        public static void Apply()
        {
            bool mobile = Mobile;
            AntialiasingMode mode = mobile ? AntialiasingMode.FastApproximateAntialiasing : AntialiasingMode.TemporalAntiAliasing;
            AntialiasingQuality quality = mobile ? AntialiasingQuality.Low : AntialiasingQuality.High;

            Camera[] cameras = Camera.allCameras;
            for (int i = 0; i < cameras.Length; i++)
            {
                Camera cam = cameras[i];
                if (cam == null || cam.cameraType != CameraType.Game) continue;
                // TryGet: this runs after a scene loads and must not add components to it.
                if (!cam.TryGetComponent(out UniversalAdditionalCameraData data)) continue;
                data.antialiasing = mode;
                data.antialiasingQuality = quality;
            }
        }
    }
}
