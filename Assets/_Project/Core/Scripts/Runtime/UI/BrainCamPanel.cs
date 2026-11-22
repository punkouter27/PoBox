using UnityEngine;
using UnityEngine.UIElements;
using PoBox.Mj;
using PoBox.Sim;
using static PoBox.UI.Ui;

namespace PoBox.UI
{
    /// <summary>
    /// The brain cam, off until the settings' BRAIN (DEBUG tab) switches it on: a title (whose brain, and the punch it has just
    /// thrown or the arm it is loading) over a bar a joint, grouped legs, torso, left arm, right arm. A bar's
    /// height is the size of that joint's action (-1 to 1), pale when it is negative. A tap swaps boxers.
    /// </summary>
    public class BrainCamPanel
    {
        /// <summary>The groups of the 21 joints, in the order drawn.</summary>
        static readonly string[] Groups = { "LEGS", "TORSO", "L ARM", "R ARM" };

        readonly VisualElement _brain;
        readonly Label _title;
        VisualElement[] _bars = new VisualElement[0];
        int[] _group = new int[0];

        /// <summary>Whose brain is shown: 0 red, 1 blue.</summary>
        public int Corner { get; private set; }

        public bool On => _brain != null && !_brain.ClassListContains("hidden");

        /// <summary>Built into the document's safe area, hidden. With no such element there is no brain cam.</summary>
        public BrainCamPanel(VisualElement safe)
        {
            if (safe == null) return;
            _brain = new VisualElement { name = "brain" };
            _brain.AddToClassList("brain");
            _brain.AddToClassList("hidden");
            _title = new Label("BRAIN") { pickingMode = PickingMode.Ignore };
            _title.AddToClassList("brain-title");
            _brain.Add(_title);
            var bars = new VisualElement { name = "brain-bars", pickingMode = PickingMode.Ignore };
            bars.AddToClassList("brain-bars");
            _brain.Add(bars);
            _brain.RegisterCallback<ClickEvent>(_ => { Corner = 1 - Corner; _bars = new VisualElement[0]; });
            safe.Add(_brain);
        }

        public void Show(bool on) => _brain?.EnableInClassList("hidden", !on);

        /// <summary>One bar a joint of this boxer's, in its group by the joint's name (the config's joint_order).</summary>
        void BuildBars(MjBoxer body)
        {
            VisualElement host = _brain.Q<VisualElement>("brain-bars");
            host.Clear();
            string[] joints = body.Config.joint_order;
            _bars = new VisualElement[body.Action.Length];
            _group = new int[body.Action.Length];
            for (int g = 0; g < Groups.Length; g++)
            {
                var group = new VisualElement { pickingMode = PickingMode.Ignore };
                group.AddToClassList("brain-group");
                for (int j = 0; j < _bars.Length; j++)
                {
                    if (GroupOf(j < joints.Length ? joints[j] : "") != g) continue;
                    var slot = new VisualElement { pickingMode = PickingMode.Ignore };
                    slot.AddToClassList("brain-slot");
                    var bar = new VisualElement { pickingMode = PickingMode.Ignore };
                    bar.AddToClassList("brain-bar");
                    slot.Add(bar);
                    group.Add(slot);
                    _bars[j] = bar;
                    _group[j] = g;
                }
                var key = new Label(Groups[g]) { pickingMode = PickingMode.Ignore };
                key.AddToClassList("brain-key");
                var column = new VisualElement { pickingMode = PickingMode.Ignore };
                column.AddToClassList("brain-column");
                column.Add(group);
                column.Add(key);
                host.Add(column);
            }
        }

        /// <summary>A joint's group by its name: 0 legs, 1 torso, 2 left arm, 3 right arm (FightLog counts effort by these too).</summary>
        internal static int GroupOf(string joint)
        {
            if (joint.StartsWith("abdomen")) return 1;
            if (joint.StartsWith("shoulder") || joint.StartsWith("elbow")) return joint.EndsWith("_l") ? 2 : 3;
            return 0;
        }

        /// <summary>
        /// The bars every frame; the title only with the rest of the text. <paramref name="punch"/> is the name of the punch
        /// the shown boxer has just thrown, or null.
        /// </summary>
        public void Refresh(Bout bout, bool text, string punch)
        {
            if (_brain == null || bout == null) return;
            Fighter f = Corner == 0 ? bout.red : bout.blue;
            MjBoxer body = f != null ? f.boxer : null;
            if (body == null || !body.Bound || body.Action == null) return;
            if (_bars.Length != body.Action.Length) BuildBars(body);
            float left = 0f, right = 0f;
            for (int j = 0; j < _bars.Length; j++)
            {
                float v = Mathf.Clamp((float)body.Action[j], -1f, 1f);
                _bars[j].style.height = Length.Percent(Mathf.Abs(v) * 100f);
                _bars[j].EnableInClassList("brain-bar--neg", v < 0f);
                if (_group[j] == 2) left = Mathf.Max(left, Mathf.Abs(v));
                if (_group[j] == 3) right = Mathf.Max(right, Mathf.Abs(v));
            }
            if (!text) return;
            string what = punch ?? (Mathf.Max(left, right) > 0.6f ? (left > right ? "LOADING LEFT" : "LOADING RIGHT") : "GUARD");
            Set(_title, $"BRAIN · {f.displayName} · {what}");
            _title.EnableInClassList("brain-title--blue", Corner == 1);
        }
    }
}
