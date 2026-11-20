using PoBox.Broadcast;
using UnityEngine;
using UnityEngine.UIElements;

namespace PoBox.UI
{
    /// <summary>
    /// The editor-tunable side of demo mode and the Tab diagnostics overlay (<see cref="DemoMode"/>,
    /// <see cref="DiagOverlay"/>): how long without input before the demo starts, which broadcast shots it cycles and
    /// for how long each, how long a demo bout's result stays up before the next bout, and the panel the overlay draws
    /// on. Loaded from Resources/ShowcaseSettings; without the asset the defaults below hold.
    /// </summary>
    [CreateAssetMenu(menuName = "PoBox/Showcase Settings", fileName = "ShowcaseSettings")]
    public sealed class ShowcaseSettings : ScriptableObject
    {
        [Header("Demo mode")]
        [Tooltip("Seconds without a key, click, touch, mouse move or gamepad button, from load or from the last input, before the demo starts.")]
        [Min(5f)] public float idleSeconds = 30f;
        [Tooltip("The broadcast shots the demo cuts between, in order.")]
        public Shot[] cameras = { Shot.Wide, Shot.Orbit, Shot.HighCorner, Shot.ShoulderRed, Shot.LowRopes, Shot.ShoulderBlue, Shot.Overhead };
        [Tooltip("Seconds each demo shot is held.")]
        [Min(2f)] public float cameraDwellSeconds = 7f;
        [Tooltip("Seconds a demo bout's result stays up before the next demo bout (a random pair).")]
        [Min(2f)] public float resultSeconds = 8f;
        [Tooltip("Also start the demo in the editor's play mode. Off: tests and editing sessions are never taken over.")]
        public bool runInEditor;

        [Header("Diagnostics overlay (Tab)")]
        [Tooltip("The panel the overlay is drawn on (the HUD's).")]
        public PanelSettings panelSettings;
        [Tooltip("Drawn over every other document.")]
        public int sortingOrder = 100;

        static ShowcaseSettings _loaded;

        public static ShowcaseSettings Current
        {
            get
            {
                if (_loaded == null) _loaded = Resources.Load<ShowcaseSettings>("ShowcaseSettings");
                if (_loaded == null) _loaded = CreateInstance<ShowcaseSettings>();
                return _loaded;
            }
        }
    }
}
