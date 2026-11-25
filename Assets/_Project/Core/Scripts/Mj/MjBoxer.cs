using System;
using Mujoco;
using Unity.InferenceEngine;
using UnityEngine;

namespace PoBox.Mj
{
    /// <summary>What the trainer wrote about a boxer's body (models/v2/NAME_policy_config.json).</summary>
    [Serializable]
    public class BoxerConfig
    {
        public string name;
        public string[] joint_order;
        public double[] default_joint_pos, lower, upper, velocity_limit, force_limit;
        /// <summary>The two rules of envs/boxing.py a policy may have been trained under; a config without them has neither.</summary>
        public bool speed_limit;
        /// <summary>How much of the speed limit's damping is on while the get-up brain has the body (envs/getup.py's speed_share, fixed): 1 all of it.</summary>
        public double getup_speed_share = 1.0;
        public double fatigue_j, fatigue_rec_s = 20.0, fatigue_weak = 0.4;
        /// <summary>Two more (2026-10-16): the share of a punch's impulse that pushes this boxer's trunk when it is hit (MjRing), and whether its policy reads its own state in the command's place.</summary>
        public double hit_push;
        public bool obs_state;
        /// <summary>models/v3: each joint's radians per unit of action (none: action_scale for all), and a tired drive's torque capped at what is left of its force range instead of its spring weakened (envs/boxing.py).</summary>
        public double[] action_scale_j;
        public bool fatigue_ceiling;
        public double stand_height, total_mass_kg, action_scale = 0.5, action_clip = 3.0, ring_half = 3.05, strength = 1.0, speed = 1.0;
        public int control_decimation = 4, physics_hz = 200;
        /// <summary>2026-10-30: the rules each of the three brains was trained under (envs/boxing.py's rules_&lt;brain&gt;); a
        /// config without them, every brain shipped before, has none: no filter, no delay.</summary>
        public BrainRules rules_footwork = new BrainRules(), rules_match = new BrainRules(), rules_getup = new BrainRules();
        /// <summary>2026-11-25: a Hill-type muscle on every drive (Training/envs/muscle.py), in place of the speed limit's
        /// damping; a config without the block (every brain shipped before) has none.</summary>
        public MuscleConfig muscle = new MuscleConfig();
        public bool Hill => muscle != null && muscle.Hill;
    }

    /// <summary>
    /// Two rules of one brain, as envs/boxing.py trains it. action_filter f: the drives get f x the last action + (1 - f) x the
    /// policy's, and the observation's last action is that one (0 = off). obs_delay .. obs_delay_max: the observation is that
    /// many control steps late, drawn once each time the brain takes the body or the body is reset (the last action never is).
    /// </summary>
    [Serializable]
    public class BrainRules
    {
        public double action_filter;
        public int obs_delay, obs_delay_max;
        /// <summary>2026-10-31: the brain eases in over this many control steps from the pose it took the body in (envs/boxing.py).</summary>
        public int blend_steps;
        public int LagMax => Math.Max(obs_delay, obs_delay_max);
    }

    /// <summary>
    /// A boxer whose body is MuJoCo's and nothing else's: it reads its observation out of mjData, asks its
    /// policy, and writes joint targets to mjData.ctrl. No transform, collider or rigid body of Unity's is
    /// read or written here; the MuJoCo plugin moves the GameObjects to where the bodies are.
    ///
    /// The observation is Training/tools/footwork_c.py's, number for number and in MuJoCo's own frame (z up):
    /// the match's hundred and a three-number command. So is the stand-in opponent it is shown.
    /// </summary>
    public unsafe partial class MjBoxer : MonoBehaviour
    {
        /// <summary>GetUp: off the canvas in a bout. The get-up stage is the trainer's sparring env (envs/getup.py, a
        /// BoxingEnv with the opponent frozen in it), so it is shown the real opponent and the bout's ring, as Match is.</summary>
        public const int Stand = 0, Walk = 1, Turn = 2, Match = 3, GetUp = 4;
        public const int ObservationSize = 103;
        /// <summary>A match brain that sees bodies on the canvas reads three more (envs/boxing.py, the bodies): the nearest boxer
        /// down, other than its opponent, in its own heading frame (x, y, each within 3 m), and 1 if that one is within 3 m.</summary>
        public const int BodyInputs = 3, WideObservationSize = ObservationSize + BodyInputs;
        /// <summary>The pelvis body of the nearest boxer down near this one (the ring sets it before each observation), or -1.</summary>
        [NonSerialized] public int NearestDownPelvis = -1;

        [Tooltip("In front of every name of this boxer in the model: a_ for the red corner, b_ for the blue.")]
        public string prefix = "a_";
        [Tooltip("models/v2/NAME_policy_config.json")]
        public TextAsset config;
        [Tooltip("The policy. None is the zero action: the guard, held by the joint drives.")]
        public ModelAsset policy;
        [Tooltip("The policy for a bout (Training/envs/match.py). None: the one above.")]
        public ModelAsset matchPolicy;
        [Tooltip("The policy for getting up off the canvas (envs/getup.py on v2). None: the referee stands it up.")]
        public ModelAsset getUpPolicy;
        [Tooltip("The other boxer, in a bout. Episodes of kind Match show it, not a stand-in.")]
        public MjBoxer opponent;
        [Tooltip("Where the middle of the ring is, MuJoCo's x and y: the observation's place in the ring is measured from it.")]
        public Vector2 ringCentre;

        /// <summary>The share of joint drive strength the boxer has: 1 unhurt, less when dazed, 0.04 with its legs gone (envs/boxing.py).</summary>
        [NonSerialized] public double DriveScale = 1.0;

        /// <summary>0: drive the body; 1: limp (2026-11-25). Between them the drives fade, so a boxer going down folds and
        /// lands as physics rather than holding a pose. <see cref="Runtime.Sim.MjBrain"/> sets it from the bout: 1 while a
        /// boxer is down and not yet getting up, back to 0 as the get-up policy takes the body.</summary>
        [NonSerialized] public double Limp;
        /// <summary>What a tiring boxer has left, 0 to 1 (envs/boxing.py's reserve). Stays at 1 unless its config has fatigue_j.</summary>
        [NonSerialized] public double Reserve = 1.0;
        /// <summary>Its daze and the opponent's, as shares of where the legs go: what a policy with obs_state reads (set by the game's brain).</summary>
        [NonSerialized] public double DazeShare, OpponentDazeShare;

        public BoxerConfig Config { get; private set; }
        public bool Bound { get; private set; }
        public int Joints { get; private set; }

        /// <summary>What kind of episode it is in (Stand, Walk, Turn), the command, and where the stand-in opponent stands.</summary>
        [NonSerialized] public int Kind = Stand;
        public readonly double[] Cmd = new double[3];
        public readonly double[] Opp = new double[2];

        public readonly double[] Obs = new double[WideObservationSize];
        public double[] Action { get; private set; }
        public float Value { get; private set; }
        /// <summary>Count of non-finite (NaN/Inf) observation or policy output values sanitized since load/reset.</summary>
        public int NanInfCount { get; private set; }
        public void ResetTelemetry() => NanInfCount = 0;

        /// <summary>Appends zero-initialized trailing observation features at <paramref name="dst"/>[<paramref name="baseCount"/>..] without reordering <paramref name="src"/>.</summary>
        public static void AppendZeroInitInputs(float[] src, int baseCount, float[] dst)
        {
            if (dst == null) return;
            int copy = src != null ? Math.Min(Math.Min(baseCount, src.Length), dst.Length) : 0;
            for (int i = 0; i < copy; i++) dst[i] = float.IsFinite(src[i]) ? src[i] : 0f;
            for (int i = copy; i < dst.Length; i++) dst[i] = 0f;
        }
        /// <summary>Seconds every boxer's policy has spent thinking since the last <see cref="TakeInferSeconds"/>: for the telemetry.</summary>
        static double _inferSeconds;
        public static double TakeInferSeconds() { double s = _inferSeconds; _inferSeconds = 0; return s; }

        MujocoLib.mjModel_* _m;
        MujocoLib.mjData_* _d;
        int[] _jq, _jv, _act;
        int _pelvis, _torsoBody, _head, _torso, _rootQ, _rootV;
        readonly int[] _glove = new int[2], _foot = new int[2], _forearm = new int[2], _upperBody = new int[2], _foreBody = new int[2];

        // Where the things a punch is measured against are in the model (for MjRing).
        public int HeadGeom => _head;
        public int TorsoGeom => _torso;
        public int GloveGeom(int hand) => _glove[hand];
        public int ForearmGeom(int hand) => _forearm[hand];
        public int FootGeom(int foot) => _foot[foot];
        readonly double[][] _guard = { new double[3], new double[3], new double[3], new double[3] };   // head, body, gloves
        readonly double[][] _standIn = { new double[3], new double[3], new double[3], new double[3] };
        double _theirs;
        readonly double[] _headVel = new double[3], _prevHead = new double[3];
        Worker _worker;
        Tensor<float> _input;
        float[] _obsF, _obsWide;

        void Awake()
        {
            Config = JsonUtility.FromJson<BoxerConfig>(config.text);
            Joints = Config.joint_order.Length;
            Action = new double[Joints];
            _obsF = new float[ObservationSize];
            _obsWide = new float[WideObservationSize];
            // All three brains loaded now, not the first time each is needed: the first knockdown loaded the get-up
            // brain mid-fight (28 kB and a hitch, 2026-11-16).
            Load(policy);
            Load(matchPolicy);
            Load(getUpPolicy);
            SetPolicy(policy);
        }

        readonly System.Collections.Generic.Dictionary<ModelAsset, Worker> _workers = new System.Collections.Generic.Dictionary<ModelAsset, Worker>();
        readonly System.Collections.Generic.Dictionary<ModelAsset, int> _inputSizes = new System.Collections.Generic.Dictionary<ModelAsset, int>();
        readonly System.Collections.Generic.Dictionary<int, Tensor<float>> _inputs = new System.Collections.Generic.Dictionary<int, Tensor<float>>();
        readonly System.Collections.Generic.Dictionary<ModelAsset, MlpPolicy> _mlps = new System.Collections.Generic.Dictionary<ModelAsset, MlpPolicy>();
        MlpPolicy _mlp;
        float[] _actF = new float[0];
        /// <summary>
        /// Brains run by <see cref="MlpPolicy"/> (plain C#, the same maths) instead of the Inference Engine, which spends
        /// most of a call scheduling (2026-11-16). A brain whose graph it cannot run keeps the Inference Engine.
        /// </summary>
        public static bool DirectBrains = true;
        /// <summary>The running brain is being run by <see cref="MlpPolicy"/>.</summary>
        public bool RunningDirect => DirectBrains && _mlp != null;
        int _inputSize = ObservationSize;
        /// <summary>How many numbers the running policy reads: 103, or 106 for one that sees bodies.</summary>
        public int InputSize => _inputSize;

        /// <summary>Another policy from here on. None is the zero action. Each policy's worker is made once and kept.</summary>
        public void SetPolicy(ModelAsset asset)
        {
            // A brain taking the body begins an episode of its own, as in training: its delayed observation starts afresh.
            bool change = asset != policy;
            if (change) _fresh = true;
            policy = asset;
            Rules = RulesFor(asset);
            // A brain trained with a filter began every episode from the zero action (its last action, shown and smoothed
            // from); so does it here. A brain without one sees the last brain's action, as every brain shipped before did.
            if (change && Rules.action_filter > 0.0)
                for (int i = 0; i < Joints; i++) Action[i] = _applied[i] = 0.0;
            // A brain trained with blend_steps eases in from the pose the body is in now (the trainer's episode start).
            if (change)
            {
                _blendAt = 0;
                if (Rules.blend_steps > 0 && _d != null && _jq != null)
                    for (int i = 0; i < Joints; i++) _blendFrom[i] = _d->qpos[_jq[i]];
            }
            _worker = null;
            _mlp = null;
            if (asset == null) return;
            Load(asset);
            _worker = _workers[asset];
            _mlp = _mlps[asset];
            if (_actF.Length < Joints) _actF = new float[Joints];
            _inputSize = _inputSizes[asset];
            if (!_inputs.TryGetValue(_inputSize, out _input)) _inputs[_inputSize] = _input = new Tensor<float>(new TensorShape(1, _inputSize), false);
        }

        /// <summary>A brain's worker and direct evaluator, made once and kept.</summary>
        void Load(ModelAsset asset)
        {
            if (asset == null || _workers.ContainsKey(asset)) return;
            Model model = ModelLoader.Load(asset);
            _workers[asset] = new Worker(model, BackendType.CPU);
            int size = model.inputs[0].shape.Get(model.inputs[0].shape.rank - 1);
            _inputSizes[asset] = size == WideObservationSize ? WideObservationSize : ObservationSize;
            MlpPolicy mlp = MlpPolicy.TryBuild(model, out string why);
            // The zero action's stand-in (an untrained boxer's placeholder, a single MatMul) is not a brain: no warning for it.
            if (mlp == null && asset.name != "zero_policy") Debug.LogWarning($"[MjBoxer] {name}: {asset.name} stays on the Inference Engine ({why}).");
            _mlps[asset] = mlp;
        }

        /// <summary>The policy the boxer now runs.</summary>
        public ModelAsset Running => policy;

        /// <summary>The running brain's rules (action filter, observation delay): its config's rules_getup, rules_match or rules_footwork.</summary>
        public BrainRules Rules { get; private set; } = new BrainRules();

        BrainRules RulesFor(ModelAsset asset)
        {
            if (Config == null) return new BrainRules();
            if (asset != null && asset == getUpPolicy) return Config.rules_getup ?? new BrainRules();
            if (asset != null && asset == matchPolicy) return Config.rules_match ?? new BrainRules();
            return Config.rules_footwork ?? new BrainRules();
        }

        // The observation delay: the last LagMax + 1 observations, newest at _histAt, and this episode's lag.
        double[][] _hist;
        int _histAt, _lag;
        bool _fresh = true;
        readonly System.Random _lagRng = new System.Random();
        /// <summary>How many control steps late the running brain's observation is this episode (0 without a delay).</summary>
        public int Lag => Rules.LagMax > 0 ? _lag : 0;

        /// <summary>envs/boxing.py's _delayed, for one boxer: Obs becomes the observation of Lag steps ago, but its last
        /// action stays this step's. An episode's first observation fills the history, and its lag is drawn then.</summary>
        void Delay()
        {
            int hi = Rules.LagMax, len = hi + 1;
            if (_hist == null || _hist.Length != len)
            {
                _hist = new double[len][];
                for (int s = 0; s < len; s++) _hist[s] = new double[WideObservationSize];
                _fresh = true;
            }
            if (_fresh)
            {
                for (int s = 0; s < len; s++) Array.Copy(Obs, _hist[s], WideObservationSize);
                _histAt = 0;
                int lo = Math.Min(Rules.obs_delay, hi);
                _lag = lo + _lagRng.Next(hi - lo + 1);
                _fresh = false;
            }
            else
            {
                _histAt = (_histAt + 1) % len;
                Array.Copy(Obs, _hist[_histAt], WideObservationSize);
            }
            Array.Copy(_hist[(_histAt - _lag + len) % len], Obs, WideObservationSize);
            int a0 = 9 + 2 * Joints;
            for (int i = 0; i < Joints; i++) Obs[a0 + i] = Clamp(Action[i], -100.0, 100.0);
        }

        void OnDestroy()
        {
            foreach (var t in _inputs.Values) t.Dispose();
            _inputs.Clear();
            foreach (var w in _workers.Values) w.Dispose();
            _workers.Clear();
            foreach (var m in _mlps.Values) m?.Dispose();
            _mlps.Clear();
            _mlp = null;
        }

        int Id(MujocoLib.mjtObj kind, string name)
        {
            int id = MujocoLib.mj_name2id(_m, (int)kind, prefix + name);
            if (id < 0) throw new InvalidOperationException($"the model has no {prefix}{name}");
            return id;
        }

        /// <summary>Find this boxer's joints, drives and shapes in the compiled model, and stand it in its guard at the origin.</summary>
        public void Bind(MujocoLib.mjModel_* model, MujocoLib.mjData_* data)
        {
            _m = model; _d = data;
            _jq = new int[Joints]; _jv = new int[Joints]; _act = new int[Joints];
            for (int i = 0; i < Joints; i++)
            {
                int j = Id(MujocoLib.mjtObj.mjOBJ_JOINT, Config.joint_order[i]);
                _jq[i] = _m->jnt_qposadr[j];
                _jv[i] = _m->jnt_dofadr[j];
                _act[i] = Id(MujocoLib.mjtObj.mjOBJ_ACTUATOR, Config.joint_order[i]);
                // envs/boxing.py's speed limit: a drive pulling its hardest has nothing left over at the limit.
                // The model is compiled afresh from the scene each time, so this is added once.
                // Under the Hill muscle its force-velocity curve is the speed limit, and the trainer adds no damping.
                _speedExtra[i] = Config.speed_limit && !Config.Hill ? Config.force_limit[i] / Config.velocity_limit[i] : 0.0;
                _m->dof_damping[_jv[i]] += _speedExtra[i];
                // The fatigue ceiling's numbers, from the compiled model as the trainer reads them: a position drive's force is
                // kp (target - q) - kv qdot, held inside its force range (models/v3's are not the same both ways).
                _kp[i] = _m->actuator_gainprm[_act[i] * MujocoLib.mjNGAIN];
                _kv[i] = -_m->actuator_biasprm[_act[i] * MujocoLib.mjNBIAS + 2];
                _fLo[i] = _m->actuator_forcerange[_act[i] * 2];
                _fHi[i] = _m->actuator_forcerange[_act[i] * 2 + 1];
            }
            int root = Id(MujocoLib.mjtObj.mjOBJ_JOINT, "root");
            _rootQ = _m->jnt_qposadr[root]; _rootV = _m->jnt_dofadr[root];
            _pelvis = Id(MujocoLib.mjtObj.mjOBJ_BODY, "pelvis"); _torsoBody = Id(MujocoLib.mjtObj.mjOBJ_BODY, "torso");
            _head = Id(MujocoLib.mjtObj.mjOBJ_GEOM, "head_geom"); _torso = Id(MujocoLib.mjtObj.mjOBJ_GEOM, "torso_geom");
            _glove[0] = Id(MujocoLib.mjtObj.mjOBJ_GEOM, "glove_l"); _glove[1] = Id(MujocoLib.mjtObj.mjOBJ_GEOM, "glove_r");
            _foot[0] = Id(MujocoLib.mjtObj.mjOBJ_GEOM, "foot_l_geom"); _foot[1] = Id(MujocoLib.mjtObj.mjOBJ_GEOM, "foot_r_geom");
            _forearm[0] = Id(MujocoLib.mjtObj.mjOBJ_GEOM, "forearm_l_geom"); _forearm[1] = Id(MujocoLib.mjtObj.mjOBJ_GEOM, "forearm_r_geom");
            for (int h = 0; h < 2; h++)
            {
                _upperBody[h] = Id(MujocoLib.mjtObj.mjOBJ_BODY, h == 0 ? "upper_arm_l" : "upper_arm_r");
                _foreBody[h] = Id(MujocoLib.mjtObj.mjOBJ_BODY, h == 0 ? "forearm_l" : "forearm_r");
            }
            Bound = true;

            // Where the head, the body and the gloves are from the pelvis in the guard: the stand-in's shape.
            ResetToGuard(0.0, 0.0, 0.0);
            int[] parts = { _head, _torso, _glove[0], _glove[1] };
            double yaw = Yaw(_d->xquat + 4 * _pelvis);
            for (int p = 0; p < 4; p++) Heading(_d->geom_xpos + 3 * parts[p], _d->xpos + 3 * _pelvis, yaw, _guard[p]);
            SetEpisode(Stand, 0.0, 0.0, 0.0, 1.3, 0.0);
        }

        /// <summary>Standing in the guard at (x, y), facing yaw: the first moment of every training episode, without its noise.</summary>
        public void ResetToGuard(double x, double y, double yaw)
        {
            ResetEpisodeReward();
            double* q = _d->qpos + _rootQ;
            q[0] = x; q[1] = y; q[2] = Config.stand_height + 0.002;
            q[3] = Math.Cos(yaw / 2); q[4] = 0; q[5] = 0; q[6] = Math.Sin(yaw / 2);
            for (int k = 0; k < 6; k++) _d->qvel[_rootV + k] = 0;
            for (int i = 0; i < Joints; i++)
            {
                _d->qpos[_jq[i]] = Config.default_joint_pos[i];
                _d->qvel[_jv[i]] = 0;
                _d->ctrl[_act[i]] = _held[i] = Config.default_joint_pos[i];
                Action[i] = _applied[i] = _activation[i] = 0;
            }
            _fresh = true;
            MujocoLib.mj_forward(_m, _d);
        }

        /// <summary>
        /// The poses the bout holds a boxer in (joint, angle; the rest as in the guard), found on the v3 bodies in
        /// MuJoCo, whose joints all turn the same ways. Sitting: thighs level, shins upright, the gloves on the knees.
        /// Victory: both gloves as high as the shoulders' limits let them go, about 20 cm over the head.
        /// </summary>
        public static readonly (string joint, double angle)[] Sitting =
        {
            ("hip_y_l", -1.45), ("hip_y_r", -1.45), ("knee_l", 1.45), ("knee_r", 1.45), ("ankle_y_l", 0.0), ("ankle_y_r", 0.0),
            ("hip_x_l", 0.25), ("hip_x_r", -0.25), ("abdomen_y", 0.3),
            ("shoulder_x_l", -1.64), ("shoulder_z_l", -1.01), ("elbow_l", 0.0), ("shoulder_x_r", 1.64), ("shoulder_z_r", 1.01), ("elbow_r", 0.0),
        };
        public static readonly (string joint, double angle)[] Victory =
        {
            ("shoulder_x_l", -1.51), ("shoulder_z_l", -1.92), ("elbow_l", -1.2), ("shoulder_x_r", 1.51), ("shoulder_z_r", 1.92), ("elbow_r", 1.2),
        };
        /// <summary>The referee's raise (2026-11-16): the left glove as high as Victory's, the right one kept in the guard.</summary>
        public static readonly (string joint, double angle)[] RaisedLeft =
        {
            ("shoulder_x_l", -1.51), ("shoulder_z_l", -1.92), ("elbow_l", -1.2),
        };

        /// <summary>The guard's joints with the ones named set otherwise, each inside its limits.</summary>
        public double[] Pose((string joint, double angle)[] set)
        {
            var q = (double[])Config.default_joint_pos.Clone();
            foreach (var (joint, angle) in set)
            {
                int i = Array.IndexOf(Config.joint_order, joint);
                if (i >= 0) q[i] = Math.Max(Config.lower[i], Math.Min(Config.upper[i], angle));
            }
            return q;
        }

        /// <summary>Held in a pose at (x, y), facing yaw, nothing moving: the joints as given, the body lowered or raised until the lower sole is on the floor.</summary>
        public void ResetToPose(double x, double y, double yaw, double[] joints)
        {
            ResetEpisodeReward();
            double* q = _d->qpos + _rootQ;
            q[0] = x; q[1] = y; q[2] = Config.stand_height;
            q[3] = Math.Cos(yaw / 2); q[4] = 0; q[5] = 0; q[6] = Math.Sin(yaw / 2);
            for (int k = 0; k < 6; k++) _d->qvel[_rootV + k] = 0;
            for (int i = 0; i < Joints; i++)
            {
                _d->qpos[_jq[i]] = joints[i];
                _d->qvel[_jv[i]] = 0;
                _d->ctrl[_act[i]] = _held[i] = joints[i];
                Action[i] = _applied[i] = _activation[i] = 0;
            }
            MujocoLib.mj_kinematics(_m, _d);
            q[2] += 0.002 - Math.Min(SoleBottom(0), SoleBottom(1));
            _fresh = true;
            MujocoLib.mj_forward(_m, _d);
        }

        /// <summary>Thigh plus shin, metres (the shin's and the foot's offsets): the unit a motion clip's root is in (Training/tools/retarget_clips.py).</summary>
        public double LegLength
        {
            get
            {
                double leg = 0;
                foreach (string part in new[] { "shin_l", "foot_l" })
                {
                    double* p = _m->body_pos + 3 * Id(MujocoLib.mjtObj.mjOBJ_BODY, part);
                    leg += Math.Sqrt(p[0] * p[0] + p[1] * p[1] + p[2] * p[2]);
                }
                return leg;
            }
        }

        /// <summary>
        /// Shown in a pose, not simulated (the walk-on's moves, IntroMoves): the root and the joints as given, each joint
        /// held inside its limits, nothing moving, and the body lowered or raised until the lower sole is on the floor, as
        /// retarget_clips.place does. Only the kinematics run; the caller moves the scene's objects (SyncUnityToMjState).
        /// The next placement (Fighter.Respawn) puts the boxer back in its guard.
        /// </summary>
        public void ShowPose(double[] rootPos, double[] rootQuat, double[] joints)
        {
            double* q = _d->qpos + _rootQ;
            for (int k = 0; k < 3; k++) q[k] = rootPos[k];
            for (int k = 0; k < 4; k++) q[3 + k] = rootQuat[k];
            for (int k = 0; k < 6; k++) _d->qvel[_rootV + k] = 0;
            for (int i = 0; i < Joints; i++)
            {
                _d->qpos[_jq[i]] = Math.Max(Config.lower[i], Math.Min(Config.upper[i], joints[i]));
                _d->qvel[_jv[i]] = 0;
            }
            MujocoLib.mj_kinematics(_m, _d);
            q[2] -= Math.Min(SoleBottom(0), SoleBottom(1));
            MujocoLib.mj_kinematics(_m, _d);
        }

        /// <summary>The lowest corner of a foot's sole, MuJoCo's z.</summary>
        double SoleBottom(int f)
        {
            double* gp = _d->geom_xpos + 3 * _foot[f], gm = _d->geom_xmat + 9 * _foot[f], size = _m->geom_size + 3 * _foot[f];
            double low = double.MaxValue;
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sy = -1; sy <= 1; sy += 2)
                    low = Math.Min(low, gp[2] + gm[6] * sx * size[0] + gm[7] * sy * size[1] - gm[8] * size[2]);
            return low;
        }

        /// <summary>The body put in a given state, in MuJoCo's own numbers: the root's position, turn and velocity, and the joints, at rest.</summary>
        public void SetState(double[] rootPos, double[] rootQuat, double[] rootLinVel, double[] jointPos)
        {
            for (int k = 0; k < 3; k++) { _d->qpos[_rootQ + k] = rootPos[k]; _d->qvel[_rootV + k] = rootLinVel[k]; _d->qvel[_rootV + 3 + k] = 0; }
            for (int k = 0; k < 4; k++) _d->qpos[_rootQ + 3 + k] = rootQuat[k];
            for (int i = 0; i < Joints; i++)
            {
                _d->qpos[_jq[i]] = jointPos[i];
                _d->qvel[_jv[i]] = 0;
                _d->ctrl[_act[i]] = _held[i] = jointPos[i];
                Action[i] = _applied[i] = _activation[i] = 0;
            }
            _fresh = true;
            MujocoLib.mj_forward(_m, _d);
        }

        /// <summary>What is asked of it from here: the kind of episode, the command, and the stand-in, given as a bearing and a distance from where it now stands.</summary>
        public void SetEpisode(int kind, double vx, double vy, double wz, double distance, double bearing)
        {
            double* pos = _d->xpos + 3 * _pelvis;
            double yaw = Yaw(_d->xquat + 4 * _pelvis);
            SetEpisodeAt(kind, vx, vy, wz, pos[0] + Math.Cos(yaw + bearing) * distance, pos[1] + Math.Sin(yaw + bearing) * distance);
        }

        /// <summary>The same, with the stand-in at a place in the world.</summary>
        public void SetEpisodeAt(int kind, double vx, double vy, double wz, double oppX, double oppY)
        {
            Kind = kind; Cmd[0] = vx; Cmd[1] = vy; Cmd[2] = wz;
            Opp[0] = oppX; Opp[1] = oppY;
            for (int k = 0; k < 3; k++) _headVel[k] = 0;
            StandIn();
            for (int k = 0; k < 3; k++) _prevHead[k] = _standIn[0][k];
        }

        /// <summary>
        /// A free-for-all turns the boxer on another opponent. Its episode goes on; the stand-in's head speed starts
        /// again from the new one, as at the start of an episode, rather than jumping across the ring.
        /// </summary>
        public void Face(MjBoxer other)
        {
            if (other == opponent) return;
            opponent = other;
            if (Bound) SetEpisodeAt(Kind, Cmd[0], Cmd[1], Cmd[2], Opp[0], Opp[1]);
        }

        static double Yaw(double* q) =>Math.Atan2(2.0 * (q[0] * q[3] + q[1] * q[2]), 1.0 - 2.0 * (q[2] * q[2] + q[3] * q[3]));





        public int PelvisBody => _pelvis;

        static double Clamp(double v, double lo, double hi) => v < lo ? lo : v > hi ? hi : v;

        /// <summary>
        /// The float observation this policy reads, already holding the numbers <see cref="Observe"/> last wrote. The 103
        /// base numbers are narrowed in Observe's own clamp pass, so a narrow brain (every shipped one) converts nothing
        /// here. A wide brain reads three more the ring sets after that, which are narrowed now.
        /// </summary>
        float[] Scope()
        {
            if (_inputSize != WideObservationSize) return _obsF;
            Array.Copy(_obsF, _obsWide, ObservationSize);
            for (int i = ObservationSize; i < WideObservationSize; i++) _obsWide[i] = (float)Obs[i];
            return _obsWide;
        }

        /// <summary>The policy's answer to <see cref="Obs"/>, into <see cref="Action"/>. With no policy, zeros.</summary>
        public void Infer()
        {
            if (_worker == null) { Array.Clear(Action, 0, Joints); return; }
            // Time.realtimeSinceStartupAsDouble, not Stopwatch: a WebGL Stopwatch counts whole milliseconds, which
            // read every sub-millisecond inference as 0 (the overlay showed 0.00 ms in the browser).
            double start = Time.realtimeSinceStartupAsDouble;
            float[] buffer = Scope();
            if (DirectBrains && _mlp != null)
            {
                Value = _mlp.Run(buffer, _actF);
                for (int i = 0; i < Joints; i++)
                {
                    if (float.IsFinite(_actF[i])) Action[i] = _actF[i];
                    else { NanInfCount++; Action[i] = 0.0; }
                }
                _inferSeconds += Time.realtimeSinceStartupAsDouble - start;
                Announce(Time.realtimeSinceStartupAsDouble - start, true);
                return;
            }
            _input.Upload(buffer);
            _worker.Schedule(_input);
            if (_worker.PeekOutput("value") is Tensor<float> value)
            {
                value.CompleteAllPendingOperations();
                Value = value.AsReadOnlySpan()[0];
            }
            if (_worker.PeekOutput("actions") is Tensor<float> output)
            {
                output.CompleteAllPendingOperations();
                ReadOnlySpan<float> span = output.AsReadOnlySpan();
                for (int i = 0; i < Joints; i++)
                {
                    if (float.IsFinite(span[i])) Action[i] = span[i];
                    else { NanInfCount++; Action[i] = 0.0; }
                }
            }
            _inferSeconds += Time.realtimeSinceStartupAsDouble - start;
            Announce(Time.realtimeSinceStartupAsDouble - start, false);
        }

        ModelAsset _announced;

        /// <summary>One log line the first time each brain answers: proof in the player's log (the browser console on
        /// WebGL) that the trained policy is running, not the zero action.</summary>
        void Announce(double seconds, bool direct)
        {
            if (_announced == policy) return;
            _announced = policy;
            Debug.Log($"[MjBoxer] {name}: {(policy != null ? policy.name : "no brain")} inference running ({(direct ? "direct MLP" : "Inference Engine")}, {_inputSize} in, {Joints} out, {seconds * 1000.0:0.000} ms)");
        }

        /// <summary>The policy's answer to any 103 numbers, without touching the body: for checking it against the trainer's.</summary>
        public void Infer(float[] observation, float[] actionsOut)
        {
            if (DirectBrains && _mlp != null && _mlp.InputSize == ObservationSize) { _mlp.Run(observation, actionsOut); return; }
            using var input = new Tensor<float>(new TensorShape(1, ObservationSize), observation);
            _worker.Schedule(input);
            var output = _worker.PeekOutput("actions") as Tensor<float>;
            output.CompleteAllPendingOperations();
            output.AsReadOnlySpan().CopyTo(actionsOut);
        }

        /// <summary>Joint targets from <see cref="Action"/>, into mjData.ctrl: the guard plus the action, held inside the joints' ranges.
        /// A brain trained with an action filter (envs/boxing.py's _take) gets f x the last action + (1 - f) x its own, and
        /// that is the Action the next observation shows.</summary>
        public void Drive()
        {
            double f = Rules.action_filter;
            // Limp (2026-11-25): a boxer going down fades its drives, so the body folds and lands as physics.
            double limp = 1.0 - Clamp(Limp, 0.0, 1.0);
            for (int i = 0; i < Joints; i++)
            {
                Action[i] = Clamp(Action[i], -Config.action_clip, Config.action_clip);
                if (f > 0.0) Action[i] = f * _applied[i] + (1.0 - f) * Action[i];
                _applied[i] = Action[i];
                double perUnit = Config.action_scale_j != null && Config.action_scale_j.Length > i ? Config.action_scale_j[i] : Config.action_scale;
                double want = Clamp(Config.default_joint_pos[i] + Action[i] * perUnit, Config.lower[i], Config.upper[i]);
                if (_blendAt < Rules.blend_steps)
                {
                    double b = 1.0 - (double)_blendAt / Rules.blend_steps;
                    want = b * _blendFrom[i] + (1.0 - b) * want;
                }
                double q = _d->qpos[_jq[i]];
                // Hurt or tired: the spring to the target is weakened, the damping is not. Under the ceiling, tiredness caps
                // the drive's torque instead, and only the daze weakens the spring.
                double tired = 1.0 - Config.fatigue_weak * Clamp(1.0 - Reserve / 0.5, 0.0, 1.0);
                if (Config.Hill)
                {
                    // The Hill muscle (envs/muscle.py): the daze and the reserve scale its torque; no ceiling, no spring weakening.
                    double dt = Config.control_decimation / (double)Config.physics_hz;
                    _d->ctrl[_act[i]] = _held[i] = HillMuscle.Target(want, q, _d->qvel[_jv[i]], ref _activation[i], _kp[i],
                        Config.force_limit[i], Config.velocity_limit[i], Config.lower[i], Config.upper[i], DriveScale * tired * limp, dt, Config.muscle);
                    continue;
                }
                bool ceiling = Config.fatigue_ceiling && Config.fatigue_j > 0.0;
                double scale = DriveScale * (ceiling ? 1.0 : tired) * limp;
                if (scale < 0.999) want = q + scale * (want - q);
                if (ceiling && tired < 0.999)
                {
                    double at = q + _kv[i] * _d->qvel[_jv[i]] / _kp[i];
                    want = Math.Min(Math.Max(want, at + tired * _fLo[i] / _kp[i]), at + tired * _fHi[i] / _kp[i]);
                }
                _d->ctrl[_act[i]] = _held[i] = want;
            }
            _blendAt++;
        }

        readonly double[] _held = new double[64];
        readonly double[] _blendFrom = new double[64];
        int _blendAt;
        /// <summary>The action the drives got last control step: what the action filter starts from.</summary>
        readonly double[] _applied = new double[64];
        /// <summary>Each joint's muscle activation under the Hill muscle (-1 to 1); 0 at every reset, as in training.</summary>
        readonly double[] _activation = new double[64];
        readonly double[] _speedExtra = new double[64];
        double _speedShare = 1.0;

        /// <summary>The share of the speed limit's damping on this boxer's joints: base damping plus share x force / speed limit, as envs/getup.py sets it.</summary>
        public void SetSpeedShare(double share)
        {
            if (!Bound || Math.Abs(share - _speedShare) < 1e-9) return;
            for (int i = 0; i < Joints; i++) _m->dof_damping[_jv[i]] += (share - _speedShare) * _speedExtra[i];
            _speedShare = share;
        }
        readonly double[] _kp = new double[64], _kv = new double[64], _fLo = new double[64], _fHi = new double[64];

        /// <summary>
        /// The targets of this control step written again. The plugin's MjActuator copies its own Control field
        /// over mjData.ctrl after every physics step; the trainer holds its targets for the whole control step.
        /// </summary>
        public void HoldTargets()
        {
            for (int i = 0; i < Joints; i++) _d->ctrl[_act[i]] = _held[i];
        }

        /// <summary>Once a control step of a bout is over: what its drives put out comes off the reserve, which fills again over fatigue_rec_s.</summary>
        public void Tire(double dt)
        {
            if (Config.fatigue_j <= 0.0) return;
            double watts = 0.0;
            for (int i = 0; i < Joints; i++) watts += Math.Abs(_d->actuator_force[_act[i]] * _d->qvel[_jv[i]]);
            Reserve = Clamp(Reserve + dt * ((1.0 - Reserve) / Config.fatigue_rec_s - watts / (Config.fatigue_j * Config.strength)), 0.0, 1.0);
        }

        public int TorsoBody => _torsoBody;

        /// <summary>A force and a torque about its centre of mass on the trunk, in MuJoCo's frame: a punch's push (MjRing). Zeros take it off.</summary>
        public void PushTrunk(double fx, double fy, double fz, double tx, double ty, double tz)
        {
            double* f = _d->xfrc_applied + 6 * _torsoBody;
            f[0] = fx; f[1] = fy; f[2] = fz; f[3] = tx; f[4] = ty; f[5] = tz;
        }

        /// <summary>A force on the trunk, in MuJoCo's frame; zero takes it off.</summary>
        public void Push(double fx, double fy, double fz)
        {
            double* f = _d->xfrc_applied + 6 * _torsoBody;
            f[0] = fx; f[1] = fy; f[2] = fz;
        }

        /// <summary>Once a control step is over: how fast the stand-in's head moved, which the next observation holds.</summary>
        public void AfterStep(double dt)
        {
            ChargePenalties(RewardWeights.Current);
            StandIn();
            for (int k = 0; k < 3; k++)
            {
                _headVel[k] = (_standIn[0][k] - _prevHead[k]) / dt;
                _prevHead[k] = _standIn[0][k];
            }
        }

        /// <summary>
        /// This episode's reward as far as the game can count it: the training config's penalty terms (RewardWeights,
        /// the copy of Training/config/reward.json), charged once a control step as envs/boxing.py charges them. The
        /// Tab overlay shows it; it starts again with every episode (ResetToGuard / ResetToPose).
        /// </summary>
        public double EpisodeReward { get; private set; }
        /// <summary>The training config's names of the penalty terms, in the order of <see cref="EpisodeTerms"/>.</summary>
        public static readonly string[] PenaltyTerms = { "torque_sq", "joint_vel_sq", "ang_vel_sq", "action_rate", "foot_slip" };
        /// <summary>This episode's reward term by term (each -weight x term, summed over control steps); they add up to <see cref="EpisodeReward"/>.</summary>
        public double[] EpisodeTerms { get; } = new double[5];
        /// <summary>Control steps charged this episode.</summary>
        public int PenaltySteps { get; private set; }
        public void ResetEpisodeReward() { EpisodeReward = 0.0; System.Array.Clear(EpisodeTerms, 0, EpisodeTerms.Length); PenaltySteps = 0; _rateFrom = false; }
        readonly double[] _lastAction = new double[64];
        bool _rateFrom;

        void ChargePenalties(RewardWeights w)
        {
            if (w == null || !Bound || Action == null) return;
            double torque = 0.0, speed = 0.0, rate = 0.0, slip = 0.0;
            for (int i = 0; i < Joints; i++)
            {
                double t = _d->actuator_force[_act[i]], v = _d->qvel[_jv[i]], a = Action[i];
                torque += t * t;
                speed += v * v;
                if (_rateFrom) rate += (a - _lastAction[i]) * (a - _lastAction[i]);
                _lastAction[i] = a;
            }
            _rateFrom = true;
            double wx = _d->qvel[_rootV + 3], wy = _d->qvel[_rootV + 4];   // a free joint's turn rates are in the body's frame
            double* fv = stackalloc double[6];
            for (int f = 0; f < 2; f++)
            {
                if (!FootDown(f)) continue;
                MujocoLib.mj_objectVelocity(_m, _d, (int)MujocoLib.mjtObj.mjOBJ_GEOM, _foot[f], fv, 0);
                slip += Math.Min(25.0, fv[3] * fv[3] + fv[4] * fv[4]);
            }
            double t0 = -w.torqueSq * torque, t1 = -w.jointVelSq * speed, t2 = -w.angVelSq * (wx * wx + wy * wy), t3 = -w.actionRate * rate, t4 = -w.footSlip * slip;
            double r = t0 + t1 + t2 + t3 + t4;
            if (!double.IsFinite(r)) return;
            EpisodeTerms[0] += t0; EpisodeTerms[1] += t1; EpisodeTerms[2] += t2; EpisodeTerms[3] += t3; EpisodeTerms[4] += t4;
            EpisodeReward += r;
            PenaltySteps++;
        }

        public double PelvisHeight => _d->xpos[3 * _pelvis + 2];
        /// <summary>Whether a foot (0 left, 1 right) is on the floor, as the observation counts it: its lowest corner under 5 mm.</summary>
        public bool FootDown(int f)
        {
            double* gp = _d->geom_xpos + 3 * _foot[f], gm = _d->geom_xmat + 9 * _foot[f], size = _m->geom_size + 3 * _foot[f];
            return gp[2] - (Math.Abs(gm[6]) * size[0] + Math.Abs(gm[7]) * size[1] + Math.Abs(gm[8]) * size[2]) < 0.005;
        }
        public double Upright => _d->xmat[9 * _pelvis + 8];

        /// <summary>
        /// One arm's kinetic chain this instant (hand 0 left, 1 right): how fast the hip (pelvis), the shoulder, the
        /// elbow and the glove are moving, m/s, each MuJoCo's own velocity of that point (mj_objectVelocity on the
        /// pelvis, the upper arm's and the forearm's frames, which sit on the joints, and the glove); and the floor's
        /// push up through both feet, N, from the solver's contact forces.
        /// </summary>
        public void Chain(int hand, float[] speeds, out float floorN)
        {
            double* v = stackalloc double[6];
            for (int i = 0; i < 3; i++)
            {
                MujocoLib.mj_objectVelocity(_m, _d, (int)MujocoLib.mjtObj.mjOBJ_BODY, i == 0 ? _pelvis : i == 1 ? _upperBody[hand] : _foreBody[hand], v, 0);
                speeds[i] = (float)Math.Sqrt(v[3] * v[3] + v[4] * v[4] + v[5] * v[5]);
            }
            MujocoLib.mj_objectVelocity(_m, _d, (int)MujocoLib.mjtObj.mjOBJ_GEOM, _glove[hand], v, 0);
            speeds[3] = (float)Math.Sqrt(v[3] * v[3] + v[4] * v[4] + v[5] * v[5]);
            floorN = 0f;
            for (int i = 0; i < _d->ncon; i++)
            {
                MujocoLib.mjContact_* c = _d->contact + i;
                int foot = c->geom1 == _foot[0] || c->geom1 == _foot[1] ? c->geom1 : c->geom2 == _foot[0] || c->geom2 == _foot[1] ? c->geom2 : -1;
                if (foot < 0 || c->efc_address < 0 || _m->geom_type[foot == c->geom1 ? c->geom2 : c->geom1] != 0) continue;
                MujocoLib.mj_contactForce(_m, _d, i, v);
                floorN += (float)v[0];
            }
        }
        public bool Fallen => PelvisHeight < Config.stand_height * 0.6 || Upright < 0.4;
        /// <summary>Where a glove is (0 left, 1 right), Unity's frame.</summary>
        public Vector3 GlovePosition(int hand)
        {
            double* g = _d->geom_xpos + 3 * _glove[hand];
            return new Vector3((float)g[0], (float)g[2], (float)g[1]);
        }

        /// <summary>The whole body's centre of mass, Unity's frame.</summary>
        public Vector3 CentreOfMass
        {
            get { double* c = _d->subtree_com + 3 * _pelvis; return new Vector3((float)c[0], (float)c[2], (float)c[1]); }
        }

        /// <summary>The four corners of a foot's sole (0 left, 1 right), Unity's frame, into the array given.</summary>
        public void Sole(int f, Vector3[] corners)
        {
            double* gp = _d->geom_xpos + 3 * _foot[f], gm = _d->geom_xmat + 9 * _foot[f], size = _m->geom_size + 3 * _foot[f];
            int n = 0;
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sy = -1; sy <= 1; sy += 2)
                {
                    // The box's own x and y, at the bottom of its z: MuJoCo's frame, then Unity's.
                    double lx = sx * size[0], ly = sy * size[1], lz = -size[2];
                    double x = gp[0] + gm[0] * lx + gm[1] * ly + gm[2] * lz, y = gp[1] + gm[3] * lx + gm[4] * ly + gm[5] * lz, z = gp[2] + gm[6] * lx + gm[7] * ly + gm[8] * lz;
                    corners[n++] = new Vector3((float)x, (float)z, (float)y);
                }
        }

        /// <summary>A joint's target set from outside the policy (MjCrew's gestures), held until the next decision.</summary>
        public void SetTarget(int i, double q) => _d->ctrl[_act[i]] = _held[i] = Math.Max(Config.lower[i], Math.Min(Config.upper[i], q));

        /// <summary>The whole body's mass, kg.</summary>
        public double Mass => _m != null ? _m->body_subtreemass[_pelvis] : 75.0;

        /// <summary>Which way the boxer faces, Unity's frame, flat on the floor.</summary>
        public Vector3 Forward
        {
            get { double yaw = Yaw(_d->xquat + 4 * _pelvis); return new Vector3((float)Math.Cos(yaw), 0f, (float)Math.Sin(yaw)); }
        }

        public double DistanceFromOrigin => Math.Sqrt(_d->qpos[_rootQ] * _d->qpos[_rootQ] + _d->qpos[_rootQ + 1] * _d->qpos[_rootQ + 1]);
        public double JointPosition(int i) => _d->qpos[_jq[i]];
        public double JointVelocity(int i) => _d->qvel[_jv[i]];
        public double Torque(int i) => _d->actuator_force[_act[i]];
        public double Target(int i) => _d->ctrl[_act[i]];
        public void RootState(double[] pos3, double[] quat4, double[] lin3, double[] ang3)
        {
            for (int k = 0; k < 3; k++) { pos3[k] = _d->qpos[_rootQ + k]; lin3[k] = _d->qvel[_rootV + k]; ang3[k] = _d->qvel[_rootV + 3 + k]; }
            for (int k = 0; k < 4; k++) quat4[k] = _d->qpos[_rootQ + 3 + k];
        }
        /// <summary>The chest, in MuJoCo's frame: what a cube is thrown at.</summary>
        public void Chest(double[] o)
        {
            for (int k = 0; k < 3; k++) o[k] = _d->xpos[3 * _pelvis + k];
            o[2] += 0.25;
        }
        /// <summary>How far, in radians, it is from facing its stand-in.</summary>
        public double FacingError
        {
            get
            {
                StandIn();
                double* pos = _d->xpos + 3 * _pelvis;
                double off = Math.Atan2(_standIn[1][1] - pos[1], _standIn[1][0] - pos[0]) - Yaw(_d->xquat + 4 * _pelvis);
                return Math.Abs(Math.Atan2(Math.Sin(off), Math.Cos(off)));
            }
        }
    }
}
