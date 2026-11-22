using System.Text;
using PoBox.Diag;
using PoBox.Mj;
using UnityEngine;
using UnityEngine.UIElements;

namespace PoBox.UI
{
    /// <summary>
    /// The in-game diagnostics overlay: Tab (or the corner button on a touch screen) shows, on an opaque panel,
    /// the frame rate, each boxer's reward this episode (the training config's penalty terms, <see cref="RewardWeights"/>),
    /// the brains' inference time (ms a frame, rolling average) and the curriculum stage each boxer's running brain
    /// comes from (footwork R0-R2, get-up R3, boxing R4-R7). Its benchmark button measures training speed as far as this
    /// device can (<see cref="SimBenchmark"/>: the running fight's MuJoCo world stepped flat out for a few seconds; real
    /// training is MuJoCo Warp + PPO on a GPU and cannot run here). In demo mode it also carries the one-line caption.
    /// Drawn on its own document over every other (<see cref="ShowcaseSettings"/>), made at runtime by DemoMode.
    /// </summary>
    public sealed class DiagOverlay : MonoBehaviour
    {
        public static DiagOverlay Instance { get; private set; }
        public bool Visible { get; private set; }

        static readonly Color Panel = new Color32(0x12, 0x18, 0x26, 0xF5);
        static readonly Color Text = new Color32(0xE6, 0xE9, 0xF2, 0xFF);
        static readonly Color Muted = new Color32(0x9A, 0xA3, 0xB5, 0xFF);
        static readonly Color Accent = new Color32(0x63, 0x66, 0xF1, 0xFF);

        UIDocument _doc;
        VisualElement _root, _panel;
        Button _button, _bench;
        Label _body, _caption, _benchResult;
        SimBenchmark _benchmark;

        /// <summary>The overlay's current readout (what the panel shows).</summary>
        public string ReadoutText => Readout();
        float _fps = 60f, _nextRefresh;
        bool _touched;
        readonly StringBuilder _sb = new StringBuilder(256);

        void Awake()
        {
            Instance = this;
            ShowcaseSettings s = ShowcaseSettings.Current;
            if (s.panelSettings == null) { Debug.LogWarning("[DiagOverlay] ShowcaseSettings has no panel settings"); enabled = false; return; }
            _doc = gameObject.AddComponent<UIDocument>();
            _doc.panelSettings = s.panelSettings;
            _doc.sortingOrder = s.sortingOrder;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Build()
        {
            _root = _doc.rootVisualElement;
            if (_root == null) return;
            _root.Clear();
            _root.pickingMode = PickingMode.Ignore;
            _root.style.position = Position.Absolute;
            _root.style.left = 0; _root.style.top = 0; _root.style.right = 0; _root.style.bottom = 0;

            _button = new Button(Toggle) { name = "diag-toggle", text = "i", tooltip = "Diagnostics (Tab)" };
            _button.style.position = Position.Absolute;
            _button.style.backgroundColor = Panel;
            _button.style.color = Text;
            _button.style.unityFontStyleAndWeight = FontStyle.Bold;
            _button.style.borderTopLeftRadius = _button.style.borderTopRightRadius = _button.style.borderBottomLeftRadius = _button.style.borderBottomRightRadius = 8;
            _button.style.borderLeftColor = _button.style.borderRightColor = _button.style.borderTopColor = _button.style.borderBottomColor = Accent;
            _button.style.marginLeft = _button.style.marginTop = 0;
            _root.Add(_button);

            _panel = new VisualElement { name = "diag-panel", pickingMode = PickingMode.Ignore };
            _panel.style.position = Position.Absolute;
            _panel.style.backgroundColor = Panel;
            _panel.style.borderTopLeftRadius = _panel.style.borderTopRightRadius = _panel.style.borderBottomLeftRadius = _panel.style.borderBottomRightRadius = 10;
            _panel.style.borderLeftColor = _panel.style.borderRightColor = _panel.style.borderTopColor = _panel.style.borderBottomColor = Accent;
            var title = new Label("DIAGNOSTICS · Tab") { pickingMode = PickingMode.Ignore };
            title.style.color = Muted;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            _panel.Add(title);
            _body = new Label("") { name = "diag-body", pickingMode = PickingMode.Ignore };
            _body.style.color = Text;
            _body.style.whiteSpace = WhiteSpace.Normal;
            _panel.Add(_body);
            _bench = new Button(() => StartBenchmark()) { name = "diag-benchmark", text = "Benchmark training speed (3 s)" };
            _bench.style.backgroundColor = (Color)new Color32(0x0B, 0x0F, 0x17, 0xFF);
            _bench.style.color = Text;
            _bench.style.borderLeftColor = _bench.style.borderRightColor = _bench.style.borderTopColor = _bench.style.borderBottomColor = Accent;
            _bench.style.marginLeft = _bench.style.marginRight = 0;
            _panel.Add(_bench);
            _benchResult = new Label("Steps this device's copy of the fight's MuJoCo world flat out; not a training run.") { name = "diag-benchmark-result", pickingMode = PickingMode.Ignore };
            _benchResult.style.color = Muted;
            _benchResult.style.whiteSpace = WhiteSpace.Normal;
            _panel.Add(_benchResult);
            _root.Add(_panel);

            _caption = new Label("DEMO · press any key or tap to play") { name = "diag-demo", pickingMode = PickingMode.Ignore };
            _caption.style.position = Position.Absolute;
            _caption.style.backgroundColor = Panel;
            _caption.style.color = Text;
            _caption.style.unityTextAlign = TextAnchor.MiddleCenter;
            _caption.style.unityFontStyleAndWeight = FontStyle.Bold;
            _root.Add(_caption);
            Layout();
        }

        /// <summary>Sizes in panel units for a given size in device-independent pixels (CSS px in a browser).</summary>
        float Px(float px)
        {
            float pixelsPerPoint = _root?.panel != null ? _root.panel.scaledPixelsPerPoint : 1f;
            float dpi = Screen.dpi > 0f ? Screen.dpi : 96f;
            float device = Application.platform == RuntimePlatform.WebGLPlayer ? Mathf.Max(1f, dpi / 96f) : Mathf.Max(1f, dpi / 160f);
            return px * device / Mathf.Max(0.01f, pixelsPerPoint);
        }

        void Layout()
        {
            if (_root == null) return;
            Rect sa = SafeArea.Current;
            // The corner button sits in the band above the menu's cards (they start ~38 px down), so it never covers them.
            float left = Px(6f) + sa.xMin / Mathf.Max(0.01f, _root.panel?.scaledPixelsPerPoint ?? 1f);
            float top = Px(2f) + (Screen.height - sa.yMax) / Mathf.Max(0.01f, _root.panel?.scaledPixelsPerPoint ?? 1f);
            float size = Px(34f);
            _button.style.left = left; _button.style.top = top;
            _button.style.width = size; _button.style.height = size;
            _button.style.fontSize = Px(18f);
            _button.style.borderLeftWidth = _button.style.borderRightWidth = _button.style.borderTopWidth = _button.style.borderBottomWidth = Px(1.5f);
            _panel.style.left = left;
            _panel.style.top = top + size + Px(6f);
            _panel.style.maxWidth = Px(340f);
            _panel.style.paddingLeft = _panel.style.paddingRight = Px(12f);
            _panel.style.paddingTop = _panel.style.paddingBottom = Px(10f);
            _panel.style.borderLeftWidth = _panel.style.borderRightWidth = _panel.style.borderTopWidth = _panel.style.borderBottomWidth = Px(1f);
            foreach (VisualElement child in _panel.Children()) child.style.fontSize = Px(14f);
            _bench.style.marginTop = Px(8f);
            _bench.style.minHeight = Px(36f);
            _bench.style.borderLeftWidth = _bench.style.borderRightWidth = _bench.style.borderTopWidth = _bench.style.borderBottomWidth = Px(1f);
            _benchResult.style.fontSize = Px(12f);
            _caption.style.left = 0; _caption.style.right = 0;
            _caption.style.bottom = Px(12f);
            _caption.style.marginLeft = _caption.style.marginRight = StyleKeyword.Auto;
            _caption.style.alignSelf = Align.Center;
            _caption.style.fontSize = Px(15f);
            _caption.style.paddingTop = _caption.style.paddingBottom = Px(8f);
            _caption.style.paddingLeft = _caption.style.paddingRight = Px(16f);
        }

        public void Toggle() => Visible = !Visible;

        /// <summary>Starts the bounded sim-speed benchmark (or returns the one running). Its result shows under the button.</summary>
        public SimBenchmark StartBenchmark()
        {
            if (_benchmark != null && _benchmark.Running) return _benchmark;
            _benchmark = new SimBenchmark(3f);
            _benchmark.Begin();
            if (_benchResult != null) _benchResult.text = _benchmark.Summary();
            if (_benchmark.Done) Debug.Log("[DiagOverlay] " + _benchmark.Summary());
            return _benchmark;
        }

        void OnDisable()
        {
            if (_benchmark != null && _benchmark.Running) _benchmark.End();
        }

        void Update()
        {
            if (_doc == null) return;
            if (_root == null || _root != _doc.rootVisualElement || _button == null || _button.parent != _root) Build();
            if (_root == null) return;
            if (Input.GetKeyDown(KeyCode.Tab)) Toggle();

            float dt = Time.unscaledDeltaTime;
            if (dt > 0f) _fps = Mathf.Lerp(_fps, 1f / dt, 0.05f);
            if (_benchmark != null && _benchmark.Running && !_benchmark.Tick())
            {
                Debug.Log("[DiagOverlay] " + _benchmark.Summary());
                if (_benchResult != null) _benchResult.text = _benchmark.Summary();
            }

            // The corner button is for touch screens (a phone, or once a touch is seen); a keyboard has Tab.
            _touched |= Input.touchCount > 0;
            bool touch = Application.isMobilePlatform || _touched;
            _button.style.display = touch || Visible ? DisplayStyle.Flex : DisplayStyle.None;
            _panel.style.display = Visible ? DisplayStyle.Flex : DisplayStyle.None;
            _caption.style.display = DemoMode.Active ? DisplayStyle.Flex : DisplayStyle.None;

            if (Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + 0.25f;
            Layout();
            if (Visible) _body.text = Readout();
            if (Visible && _benchmark != null && _benchResult != null) _benchResult.text = _benchmark.Summary();
        }

        string Readout()
        {
            PerfTelemetry perf = PerfTelemetry.Instance;
            _sb.Clear();
            _sb.Append("FPS  ").Append((perf != null && perf.Fps > 0f ? perf.Fps : _fps).ToString("0")).Append('\n');
            _sb.Append("Inference  ").Append(perf != null ? perf.InferMs.ToString("0.00") + " ms / frame (avg)" : "no brains running").Append('\n');
            MjBoxer[] boxers = FindObjectsByType<MjBoxer>();
            int shown = 0;
            foreach (MjBoxer b in boxers)
            {
                if (b == null || !b.Bound || !b.isActiveAndEnabled) continue;
                if (shown++ >= 8) break;
                string who = b.Config != null && !string.IsNullOrEmpty(b.Config.name) ? b.Config.name.ToUpperInvariant() : b.name;
                _sb.Append(who).Append("  reward ").Append(b.EpisodeReward.ToString("0.0"))
                   .Append(" · ").Append(Stage(b)).Append('\n');
                double[] t = b.EpisodeTerms;
                if (shown <= 2) _sb.Append("   torque ").Append(t[0].ToString("0.0")).Append(" · jvel ").Append(t[1].ToString("0.0"))
                   .Append(" · angvel ").Append(t[2].ToString("0.0")).Append(" · jitter ").Append(t[3].ToString("0.0"))
                   .Append(" · slip ").Append(t[4].ToString("0.0")).Append('\n');
            }
            if (shown == 0) _sb.Append("Episode reward  no bout running\nCurriculum stage  -\n");
            _sb.Append("Demo  ").Append(DemoMode.Active ? "on" : DemoMode.Allowed ? $"in {Mathf.Max(0f, ShowcaseSettings.Current.idleSeconds - DemoMode.IdleSeconds):0} s idle" : "off in the editor");
            return _sb.ToString();
        }

        /// <summary>The rung of the training curriculum the running brain was trained and examined at.</summary>
        static string Stage(MjBoxer b)
        {
            if (b.Running == null) return "stage: no brain";
            if (b.Running == b.getUpPolicy) return "stage R3 get-up";
            if (b.Running == b.matchPolicy) return "stage R4-R7 boxing";
            if (b.Running == b.policy) return "stage R0-R2 footwork";
            return "stage: " + b.Running.name;
        }
    }
}
