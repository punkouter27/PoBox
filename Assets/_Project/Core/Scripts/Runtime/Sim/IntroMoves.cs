using System;
using Mujoco;
using PoBox.Mj;
using UnityEngine;

namespace PoBox.Sim
{
    /// <summary>
    /// The boxers move in the walk-on: each plays a few seconds of shadow boxing (<see cref="IntroClips"/>) where it
    /// stands in its corner, instead of standing still. Nothing is trained and nothing is simulated: the walk-on has
    /// MuJoCo stopped (Bout.SetPhase), so each frame the clip's pose is written into qpos, the kinematics run and the
    /// scene's objects follow, as the instant replay (MjReplay) does. Nothing can be hit or knocked over by it.
    ///
    /// The clip is turned to face the way the boxer faces in its corner, its root scaled by the boxer's legs and its
    /// lower sole kept on the floor (MjBoxer.ShowPose). It eases in from the guard over <see cref="blendIn"/>. When the
    /// walk-on ends, the bout places the boxers on their marks in their guards (Fighter.Respawn), which overwrites
    /// every number written here, and the picture cuts at that moment.
    ///
    /// The winner's celebration (owner, 2026-11-23) is the same clips' arms and trunk: from the result until the referee
    /// raises its hand (MjBrain.Celebrating), the winner is held standing where it won (MjRing.HoldPose, re-placed every
    /// step while the loser is still simulated), and each frame the held pose becomes the gloves-up one with the arms and
    /// trunk of a celebrate_ clip (MjRing.ChangeHeldPose). The referee's raise puts its own pose over it and ends it.
    /// </summary>
    public class IntroMoves : MonoBehaviour
    {
        /// <summary>A boxer's own walk-on move.</summary>
        [Serializable]
        public class BoxerMove
        {
            [Tooltip("The boxer's name as in its config.json (matt, zombie, grandma ...), any case.")]
            public string boxer;
            [Tooltip("The clip's name in IntroClips.bytes (shadowbox_a ...). Empty: one from the pool.")]
            public string clip;
            [Tooltip("How fast this boxer plays it, times the speed below. 0: the boxer's own speed (config.json, 0.75 for Grandma and Grandpa).")]
            [Min(0f)] public float speed;

            public BoxerMove() { }
            public BoxerMove(string boxer, string clip, float speed = 0f) { this.boxer = boxer; this.clip = clip; this.speed = speed; }
        }

        /// <summary>
        /// Each shipped boxer's own move (owner, 2026-11-23: a signature walk-on each). The quick combinations go to the
        /// heavy hitters, the busiest shadow boxing to the young ones, the calmest to the grandparents at their own pace.
        /// What a new component starts with; edited in the scene after that.
        /// </summary>
        public static BoxerMove[] Signatures() => new[]
        {
            new BoxerMove("matt", "shadowbox_a"),
            new BoxerMove("sokol", "shadowbox_b"),
            new BoxerMove("kim", "shadowbox_d"),
            new BoxerMove("nick", "shadowbox_e"),
            new BoxerMove("timmy", "shadowbox_e", 1.15f),
            new BoxerMove("trump", "combos_a"),
            new BoxerMove("soldat", "combos_b"),
            new BoxerMove("zombie", "combos_b", 0.7f),
            new BoxerMove("grandma", "shadowbox_c"),
            new BoxerMove("grandpa", "shadowbox_c"),
        };

        [Tooltip("Off: the boxers stand in their guards through the walk-on, as before.")]
        public bool play = true;
        [Tooltip("Boxers that always walk on with the same move. Anyone not listed gets one from the pool.")]
        public BoxerMove[] byBoxer = Signatures();
        [Tooltip("The clips drawn from for boxers not listed above. Empty: every walk-on clip in the file (not the celebrations).")]
        public string[] pool = new string[0];
        [Tooltip("Real seconds from the guard to the clip's pose.")]
        [Min(0f)] public float blendIn = 0.6f;
        [Tooltip("How fast every clip plays, times each boxer's own: 1 is as captured.")]
        [Range(0.25f, 2f)] public float speed = 1f;

        [Header("The winner's celebration")]
        [Tooltip("Off: the winner holds both gloves up until the referee raises its hand, as before.")]
        public bool celebrate = true;
        [Tooltip("The clips a winner celebrates with, arms and trunk only. Empty: every celebrate_ clip in the file.")]
        public string[] celebrations = new string[0];

        /// <summary>Clips whose name starts so are celebrations, not walk-on moves (Training/tools/intro_clips.py).</summary>
        public const string CelebratePrefix = "celebrate_";
        /// <summary>The joints a celebration moves; the rest stay in the winner's stance.</summary>
        static readonly string[] UpperBody = { "abdomen", "shoulder", "elbow" };

        /// <summary>The clip each corner is playing (red, blue), or null: what the tests and the Tab overlay can read.</summary>
        public string RedClip => _slots[0].clip;
        public string BlueClip => _slots[1].clip;
        /// <summary>The winner's celebration playing now, or null.</summary>
        public string CelebrationClip => _cel.clip;

        class Slot
        {
            public MjBoxer boxer;
            public string clip;
            public float[] rows;
            public int[] map;                     // the boxer's joint i is the clip's joint map[i], or -1
            public double x, y, yaw, leg;
            public double[] guard;
            public double rate, start;            // clip frames a real second; the frame it starts from
        }

        IntroClips _clips;
        readonly Slot[] _slots = { new Slot(), new Slot() };
        bool _active;
        float _t;
        readonly Slot _cel = new Slot();
        MjBrain _celBrain;
        float _celT;
        double[] _celPose;
        bool[] _celUpper;
        readonly double[] _pos = new double[3], _quat = new double[4], _joints = new double[64], _a = new double[64], _b = new double[64];
        readonly double[] _p3 = new double[3], _q4 = new double[4], _l3 = new double[3], _w3 = new double[3];

        void Awake()
        {
            _clips = IntroClips.Load();
            if (_clips == null) Debug.LogWarning($"IntroMoves: no Resources/{IntroClips.ResourceName}.bytes; the boxers stand through the walk-on");
        }

        void Update()
        {
            var bout = Bout.Instance;
            Celebrate(bout);
            bool walkOn = play && _clips != null && bout != null && bout.Phase == BoutPhase.WalkOn && MjReplay.Playing == false;
            if (!walkOn)
            {
                if (_active) { _active = false; _slots[0].clip = _slots[1].clip = null; }
                return;
            }
            if (!_active)
            {
                _active = true;
                _t = 0f;
                Begin(_slots[0], bout.red, null);
                Begin(_slots[1], bout.blue, _slots[0].clip);
            }
            else _t += Mathf.Min(Time.unscaledDeltaTime, 0.1f);

            bool any = false;
            foreach (Slot s in _slots) any |= Show(s);
            if (any && MjScene.InstanceExists) MjScene.Instance.SyncUnityToMjState();
        }

        void Begin(Slot s, Fighter f, string avoid)
        {
            s.boxer = f != null ? f.boxer : null;
            s.clip = null; s.rows = null;
            if (s.boxer == null || !s.boxer.Bound) return;
            s.clip = Pick(s.boxer.Config.name, avoid, out float own);
            s.rows = s.clip != null ? _clips.Rows(s.clip) : null;
            if (s.rows == null) { s.clip = null; return; }
            float boxerSpeed = own > 0f ? own : (float)(s.boxer.Config.speed > 0 ? s.boxer.Config.speed : 1.0);
            s.rate = speed * boxerSpeed * _clips.Fps;
            // Two corners with the same signature do not move in step: the second starts half-way through.
            s.start = s.clip == avoid ? 0.5 * (s.rows.Length / _clips.Width - 1) : 0.0;

            string[] order = s.boxer.Config.joint_order;
            s.map = new int[order.Length];
            for (int i = 0; i < order.Length; i++) s.map[i] = Array.IndexOf(_clips.Joints, order[i]);
            s.guard = (double[])s.boxer.Config.default_joint_pos.Clone();
            s.leg = s.boxer.LegLength;
            // Where the walk-on put it: its corner, facing the way it faces there.
            s.boxer.RootState(_p3, _q4, _l3, _w3);
            s.x = _p3[0]; s.y = _p3[1];
            s.yaw = Math.Atan2(2 * (_q4[0] * _q4[3] + _q4[1] * _q4[2]), 1 - 2 * (_q4[2] * _q4[2] + _q4[3] * _q4[3]));
        }

        /// <summary>The boxer's own move, or one from the pool other than the other corner's when there is a choice.</summary>
        string Pick(string boxer, string avoid, out float own)
        {
            own = 0f;
            foreach (BoxerMove m in byBoxer)
                if (m != null && string.Equals(m.boxer, boxer, StringComparison.OrdinalIgnoreCase))
                {
                    own = m.speed;
                    if (!string.IsNullOrEmpty(m.clip) && _clips.Rows(m.clip) != null) return m.clip;
                    break;
                }
            var from = new System.Collections.Generic.List<string>();
            bool all = pool == null || pool.Length == 0;
            foreach (string c in all ? _clips.Names.ToArray() : pool)
                if (_clips.Rows(c) != null && c != avoid && !(all && c.StartsWith(CelebratePrefix, StringComparison.Ordinal))) from.Add(c);
            if (from.Count == 0 && avoid != null && _clips.Rows(avoid) != null) from.Add(avoid);
            return from.Count > 0 ? from[UnityEngine.Random.Range(0, from.Count)] : null;
        }

        bool Show(Slot s)
        {
            if (s.rows == null || s.boxer == null || !s.boxer.Bound) return false;
            int w = _clips.Width, frames = s.rows.Length / w;
            if (frames < 2) return false;

            // Looped, in case the walk-on outlasts the clip.
            double at = (s.start + _t * s.rate) % (frames - 1);
            int i = (int)at;
            double t = at - i;
            int a = i * w, b = a + w;
            double ease = blendIn > 0f ? Smooth(_t / blendIn) : 1.0;

            // The clip's root, scaled and turned to the corner.
            double cx = Lerp(s.rows[a], s.rows[b], t) * s.leg, cy = Lerp(s.rows[a + 1], s.rows[b + 1], t) * s.leg;
            double cos = Math.Cos(s.yaw), sin = Math.Sin(s.yaw);
            _pos[0] = s.x + ease * (cos * cx - sin * cy);
            _pos[1] = s.y + ease * (sin * cx + cos * cy);
            _pos[2] = Lerp(s.rows[a + 2], s.rows[b + 2], t) * s.leg;    // ShowPose puts the lower sole on the floor

            // Its turn: the corner's yaw, then the clip's, eased in from the guard's upright yaw.
            for (int k = 0; k < 4; k++) _a[k] = Lerp(s.rows[a + 3 + k], s.rows[b + 3 + k], t);
            double hc = Math.Cos(s.yaw / 2), hs = Math.Sin(s.yaw / 2);
            // (hc, 0, 0, hs) x (w, x, y, z)
            _b[0] = hc * _a[0] - hs * _a[3];
            _b[1] = hc * _a[1] - hs * _a[2];
            _b[2] = hc * _a[2] + hs * _a[1];
            _b[3] = hc * _a[3] + hs * _a[0];
            double dot = hc * _b[0] + hs * _b[3];
            double sign = dot < 0 ? -1 : 1;
            _quat[0] = (1 - ease) * hc + ease * sign * _b[0];
            _quat[1] = ease * sign * _b[1];
            _quat[2] = ease * sign * _b[2];
            _quat[3] = (1 - ease) * hs + ease * sign * _b[3];
            double n = Math.Sqrt(_quat[0] * _quat[0] + _quat[1] * _quat[1] + _quat[2] * _quat[2] + _quat[3] * _quat[3]);
            for (int k = 0; k < 4; k++) _quat[k] /= n;

            double[] joints = s.map.Length <= _joints.Length ? _joints : new double[s.map.Length];
            for (int j = 0; j < s.map.Length; j++)
            {
                int c = s.map[j];
                double clip = c >= 0 ? Lerp(s.rows[a + 7 + c], s.rows[b + 7 + c], t) : s.guard[j];
                joints[j] = s.guard[j] + ease * (clip - s.guard[j]);
            }
            s.boxer.ShowPose(_pos, _quat, joints);
            return true;
        }

        void Celebrate(Bout bout)
        {
            Fighter w = celebrate && _clips != null && bout != null && bout.Phase == BoutPhase.Results ? bout.Winner : null;
            MjBrain brain = w != null ? w.GetComponentInChildren<MjBrain>() : null;
            if (brain == null || !brain.Celebrating || brain.ring == null || w.boxer == null || !w.boxer.Bound || MjReplay.Playing)
            {
                _cel.clip = null; _cel.rows = null; _celBrain = null;
                return;
            }
            MjBoxer b = w.boxer;
            if (_celBrain != brain)
            {
                _celBrain = brain;
                _celT = 0f;
                _cel.boxer = b;
                var from = new System.Collections.Generic.List<string>();
                bool chosen = celebrations != null && celebrations.Length > 0;
                foreach (string c in chosen ? celebrations : _clips.Names.ToArray())
                    if (_clips.Rows(c) != null && (chosen || c.StartsWith(CelebratePrefix, StringComparison.Ordinal))) from.Add(c);
                _cel.clip = from.Count > 0 ? from[UnityEngine.Random.Range(0, from.Count)] : null;
                _cel.rows = _cel.clip != null ? _clips.Rows(_cel.clip) : null;
                string[] order = b.Config.joint_order;
                _cel.map = new int[order.Length];
                _celUpper = new bool[order.Length];
                for (int i = 0; i < order.Length; i++)
                {
                    _cel.map[i] = Array.IndexOf(_clips.Joints, order[i]);
                    _celUpper[i] = Array.Exists(UpperBody, p => order[i].StartsWith(p, StringComparison.Ordinal));
                }
                // The gloves-up stance the winner was put in; the celebration eases out of it.
                _cel.guard = b.Pose(MjBoxer.Victory);
                _cel.rate = speed * (b.Config.speed > 0 ? b.Config.speed : 1.0) * _clips.Fps;
                _celPose = (double[])_cel.guard.Clone();
            }
            else _celT += Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            if (_cel.rows == null) return;

            int width = _clips.Width, frames = _cel.rows.Length / width;
            if (frames < 2) return;
            double at = (_celT * _cel.rate) % (frames - 1);
            int i0 = (int)at;
            double t = at - i0, ease = blendIn > 0f ? Smooth(_celT / blendIn) : 1.0;
            int a = i0 * width, c1 = a + width;
            for (int j = 0; j < _celPose.Length; j++)
            {
                int m = _cel.map[j];
                double target = _celUpper[j] && m >= 0 ? Lerp(_cel.rows[a + 7 + m], _cel.rows[c1 + 7 + m], t) : _cel.guard[j];
                double q = _cel.guard[j] + ease * (target - _cel.guard[j]);
                _celPose[j] = Math.Max(b.Config.lower[j], Math.Min(b.Config.upper[j], q));
            }
            brain.ring.ChangeHeldPose(b, _celPose);
        }

        static double Lerp(float a, float b, double t) => a + (b - a) * t;
        static double Smooth(double x) { x = Math.Max(0, Math.Min(1, x)); return x * x * (3 - 2 * x); }
    }
}
