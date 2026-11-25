using UnityEngine;
using PoBox.Sim;

namespace PoBox.Fx
{
    /// <summary>
    /// The picture bending as a screen changes, on the ring shader that already bends the picture under a heavy
    /// punch (<see cref="ImpactVfx"/> → PoBox/ScreenRipple via the renderer feature): a ring that runs out from
    /// one side of the screen as the next screen comes in. It is the same pass, so a wipe costs nothing that a
    /// ripple does not already.
    ///
    /// Starts itself with no scene wiring: after a scene loads it puts a hidden, never-destroyed runner in it and
    /// leaves the global the shader reads at zero, so a scene that never wipes pays nothing (the feature is
    /// enabled on PC and High only). Off with <see cref="PictureMotion.Reduced"/>.
    /// </summary>
    public sealed class ScreenWipe : MonoBehaviour
    {
        static readonly int RippleId = Shader.PropertyToID("_PoBoxRipple");
        static readonly int AspectId = Shader.PropertyToID("_PoBoxRippleAspect");

        [Tooltip("Seconds a wipe takes to cross the picture.")]
        public float seconds = 0.45f;

        static ScreenWipe _runner;
        Vector2 _at = new Vector2(0.5f, 0.5f);
        float _t = 1f, _bend = 0.06f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (_runner != null) return;
            var go = new GameObject("Screen Wipe") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(go);
            _runner = go.AddComponent<ScreenWipe>();
        }

        void Awake()
        {
            Push();     // a rest frame: no bend, so the feature is a straight copy until something wipes
        }

        void OnEnable() => SimBus.PhaseChanged += OnPhase;

        void OnDisable() => SimBus.PhaseChanged -= OnPhase;

        /// <summary>The screens of the night change without a cut: into the fight from the walk-on, and into the result from the bell.</summary>
        void OnPhase(BoutPhase from, BoutPhase to)
        {
            if (to == BoutPhase.Fight) Wipe(true);
            else if (to == BoutPhase.Results) Wipe(false);
        }

        void OnDestroy()
        {
            if (_runner == this) _runner = null;
            Push();
        }

        /// <summary>A ripple out from <paramref name="at01"/> (0..1 across the picture), bending it by <paramref name="bend"/> screen heights.</summary>
        public static void Play(Vector2 at01, float bend = 0.06f)
        {
            if (_runner == null || PictureMotion.Reduced) return;
            _runner._at = at01;
            _runner._bend = Mathf.Clamp(bend, 0f, 0.2f);
            _runner._t = 0f;
            _runner.Push();
        }

        /// <summary>A wipe across the picture, from the side the next screen comes in from.</summary>
        public static void Wipe(bool fromLeft = true) =>
            Play(new Vector2(fromLeft ? 0.12f : 0.88f, 0.5f), fromLeft ? 0.075f : -0.075f);

        void Update()
        {
            if (_t >= 1f) return;
            _t = Mathf.Min(1f, _t + Time.unscaledDeltaTime / Mathf.Max(0.05f, seconds));
            Push();
        }

        void Push()
        {
            float aspect = Screen.height > 0 ? (float)Screen.width / Screen.height : 1f;
            Shader.SetGlobalVector(RippleId, new Vector4(_at.x, _at.y, _t, _t >= 1f ? 0f : _bend));
            Shader.SetGlobalFloat(AspectId, aspect);
        }
    }
}
