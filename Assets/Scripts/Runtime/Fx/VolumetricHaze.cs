using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace PoBox.Fx
{
    /// <summary>
    /// The arena's haze, lit for real (2026-10-31, High tier only): a ray from the camera to whatever the pixel shows is
    /// marched through a thin fog that thins with height; at each step the key light and every spot and lamp that
    /// reaches it (Forward+'s light list, with their shadow maps and cookies) scatter towards the camera, forward-heavy
    /// (Henyey-Greenstein). So the ring spots stand as shafts, cut by the boxers' shadows. Marched at half resolution
    /// with a jittered start, then laid over the frame: colour x transmittance + scattered light. On High_Renderer only
    /// (PoBox/Graphics/Make High Tier adds it); the other tiers keep LightBeam's additive cones.
    /// </summary>
    public class VolumetricHaze : ScriptableRendererFeature
    {
        [Serializable]
        public class Settings
        {
            [Tooltip("Extinction per metre at the floor.")] public float density = 0.03f;
            [Tooltip("How fast it thins with height, per metre.")] public float heightFalloff = 0.1f;
            [Range(-0.9f, 0.9f)] public float anisotropy = 0.6f;
            public float intensity = 1f;
            public float maxDistance = 28f;
            [Range(8, 96)] public int steps = 40;
            public bool halfResolution = true;
        }

        public Settings settings = new Settings();
        [SerializeField] Shader shader;
        Material _material;
        HazePass _pass;

        public override void Create()
        {
            _pass = new HazePass { renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            CameraType type = renderingData.cameraData.cameraType;
            if (type == CameraType.Preview || type == CameraType.Reflection) return;
            if (shader == null) shader = Shader.Find("PoBox/VolumetricHaze");
            if (shader == null) return;
            if (_material == null) _material = CoreUtils.CreateEngineMaterial(shader);
            _pass.Setup(_material, settings);
            _pass.ConfigureInput(ScriptableRenderPassInput.Depth);
            renderer.EnqueuePass(_pass);
        }

        protected override void Dispose(bool disposing) => CoreUtils.Destroy(_material);

        class HazePass : ScriptableRenderPass
        {
            static readonly int Params = Shader.PropertyToID("_HazeParams"), Params2 = Shader.PropertyToID("_HazeParams2");
            Material _m;
            Settings _s;

            class PassData
            {
                public Material material;
                public TextureHandle haze;
            }

            public void Setup(Material m, Settings s) { _m = m; _s = s; }

            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frame)
            {
                var resources = frame.Get<UniversalResourceData>();
                var camera = frame.Get<UniversalCameraData>();
                if (resources.isActiveTargetBackBuffer) return;

                RenderTextureDescriptor desc = camera.cameraTargetDescriptor;
                desc.depthBufferBits = 0;
                desc.msaaSamples = 1;
                desc.graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat;
                if (_s.halfResolution) { desc.width = Mathf.Max(1, desc.width / 2); desc.height = Mathf.Max(1, desc.height / 2); }
                TextureHandle haze = UniversalRenderer.CreateRenderGraphTexture(graph, desc, "_PoBoxHaze", false, FilterMode.Bilinear);

                _m.SetVector(Params, new Vector4(_s.density, _s.heightFalloff, _s.anisotropy, _s.intensity));
                _m.SetVector(Params2, new Vector4(_s.maxDistance, _s.steps, Time.frameCount % 64, 0f));

                using (var builder = graph.AddRasterRenderPass<PassData>("PoBox Haze March", out var data))
                {
                    data.material = _m;
                    builder.SetRenderAttachment(haze, 0);
                    if (resources.cameraDepthTexture.IsValid()) builder.UseTexture(resources.cameraDepthTexture);
                    if (resources.mainShadowsTexture.IsValid()) builder.UseTexture(resources.mainShadowsTexture);
                    if (resources.additionalShadowsTexture.IsValid()) builder.UseTexture(resources.additionalShadowsTexture);
                    builder.AllowGlobalStateModification(true);
                    builder.SetRenderFunc((PassData d, RasterGraphContext ctx) => Blitter.BlitTexture(ctx.cmd, new Vector4(1, 1, 0, 0), d.material, 0));
                }

                using (var builder = graph.AddRasterRenderPass<PassData>("PoBox Haze Composite", out var data))
                {
                    data.material = _m;
                    data.haze = haze;
                    builder.UseTexture(haze);
                    builder.SetRenderAttachment(resources.activeColorTexture, 0);
                    builder.SetRenderFunc((PassData d, RasterGraphContext ctx) => Blitter.BlitTexture(ctx.cmd, d.haze, new Vector4(1, 1, 0, 0), d.material, 1));
                }
            }
        }
    }
}
