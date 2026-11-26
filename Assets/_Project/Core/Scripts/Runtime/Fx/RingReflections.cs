using UnityEngine;
using UnityEngine.Rendering;
using PoBox.Sim;

namespace PoBox.Fx
{
    /// <summary>
    /// A reflection probe over the ring, rendered by script (2026-11-25): the canvas sheen, the ropes and the belt
    /// picking up the ring instead of the sky. No bake is involved — it is a real-time probe refreshed on request, so
    /// it works from a fresh clone, on the phone and in a browser, where there is no lightmap bake to piggyback on.
    ///
    /// Only on a tier that says it has real-time reflection probes (QualitySettings.realtimeReflectionProbes: High
    /// only in this project), and never in the editor unless play is running, so the arena's tests and its normal
    /// PC tier are untouched. Four renders a bout at 128 px, not one a frame: at load, at the first bell, and after
    /// the bell that ends it.
    /// </summary>
    public sealed class RingReflections : MonoBehaviour
    {
        [Tooltip("Where the probe sits, above the canvas (the ring's middle).")]
        public Vector3 centre = new Vector3(0f, 1.1f, 0f);
        [Tooltip("The box it lights: a little wider than the ring, a little taller than a boxer.")]
        public Vector3 size = new Vector3(8f, 4.5f, 8f);
        [Tooltip("Pixels a cube face is rendered at: 128 is enough for a rough canvas and a belt buckle.")]
        public int resolution = 128;

        ReflectionProbe _probe;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (FindAnyObjectByType<RingReflections>() != null) return;
            var go = new GameObject("Ring Reflections");
            go.AddComponent<RingReflections>();
        }

        void Awake()
        {
            if (!QualitySettings.realtimeReflectionProbes) { enabled = false; return; }

            var go = new GameObject("Ring Probe");
            go.transform.SetParent(transform, false);
            go.transform.position = centre;
            _probe = go.AddComponent<ReflectionProbe>();
            _probe.mode = ReflectionProbeMode.Realtime;
            _probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
            _probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.AllFacesAtOnce;
            _probe.resolution = Mathf.Clamp(resolution, 16, 512);
            _probe.size = size;
            _probe.blendDistance = 1f;
            _probe.importance = 1;
            _probe.nearClipPlane = 0.05f;
            _probe.farClipPlane = 40f;
            _probe.clearFlags = ReflectionProbeClearFlags.Skybox;
            _probe.cullingMask = ~0;
        }

        void OnEnable()
        {
            SimBus.PhaseChanged += OnPhase;
            Render();
        }

        void OnDisable() => SimBus.PhaseChanged -= OnPhase;

        void OnPhase(BoutPhase from, BoutPhase to)
        {
            // The light in the hall changes at the first and the last bell; the room between is the room.
            if (to == BoutPhase.Fight || to == BoutPhase.Results) Render();
        }

        /// <summary>Renders the cube now (one pass over six faces, a few milliseconds).</summary>
        public void Render()
        {
            if (_probe == null) return;
            _probe.RenderProbe();
        }
    }
}
