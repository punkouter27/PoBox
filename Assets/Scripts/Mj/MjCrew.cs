using System.Collections.Generic;
using Mujoco;
using UnityEngine;

namespace PoBox.Mj
{
    /// <summary>
    /// One of the ring's people who are not boxers (the referee, the card walker) on the MuJoCo plugin: a body of its
    /// own (training/models/crew, tools/build_crew.py) with a footwork brain trained like a boxer's (train_box.py --stage
    /// footwork: stand, walk and turn on command), stepped here as <see cref="MjRing"/> steps a boxer: observe, infer and
    /// drive once a control step (four physics steps), its targets held in between, its stand-in brought up to date
    /// after. It is not in the fight: no punches are measured on it, it tires of nothing, and it is on a collision
    /// layer of its own (4) that meets the floor and the ropes but never a boxer. What it is asked to do comes from the
    /// game (Fx/Referee, Fx/RingCardWalker): stand facing a point, turn to one, or walk ahead; and a joint or two set
    /// from outside the policy for a gesture (the referee's count).
    /// </summary>
    [DefaultExecutionOrder(-30)]
    public unsafe class MjCrew : MonoBehaviour
    {
        public MjBoxer body;
        [Tooltip("True for officials who physically interact with standing boxers (e.g. the referee); false for non-bout crew like ring card walkers.")]
        public bool collideWithBoxers;
        /// <summary>The collision bit reserved for crew (boxers are on 1 and 2, FreeForAll states on 1..5, the world on both 1 and 2 plus crew).</summary>
        public const int Layer = 8;

        MujocoLib.mjModel_* _m;
        MujocoLib.mjData_* _d;
        int _sub, _decimation = 4;
        double _dt;
        bool _bound, _layered;
        int[] _geoms;
        readonly Dictionary<int, double> _override = new Dictionary<int, double>();

        public bool Ready => _bound;
        public int[] GeomIds => _geoms;

        void Awake()
        {
            var scene = MjScene.Instance;
            scene.postInitEvent += OnModel;
            scene.preUpdateEvent += BeforePhysics;
            scene.postUpdateEvent += AfterPhysics;
        }

        void OnDestroy()
        {
            if (!MjScene.InstanceExists) return;
            var scene = MjScene.Instance;
            scene.postInitEvent -= OnModel;
            scene.preUpdateEvent -= BeforePhysics;
            scene.postUpdateEvent -= AfterPhysics;
        }

        void OnModel(object sender, MjStepArgs a)
        {
            _m = a.model; _d = a.data;
            _bound = false;
            if (_m == null || body == null || !body.isActiveAndEnabled) return;
            MjRing ring = FindAnyObjectByType<MjRing>();
            if (ring != null) body.ringCentre = ring.centre;
            body.Bind(_m, _d);
            _decimation = System.Math.Max(1, body.Config.control_decimation);
            _dt = _m->opt.timestep * _decimation;
            _sub = 0;
            _layered = false;
            var geoms = new List<int>();
            foreach (MjGeom g in body.GetComponentsInChildren<MjGeom>()) if (g.MujocoId >= 0) geoms.Add(g.MujocoId);
            _geoms = geoms.ToArray();
            body.SetEpisodeAt(MjBoxer.Stand, 0, 0, 0, body.transform.position.x, body.transform.position.z + 1.3);
            _bound = true;
        }

        /// <summary>
        /// Crew layers: every crew geom sits on bit 8 (or 1 | 8 when collideWithBoxers is true so standing boxers meet it while
        /// downed ghost boxers on bit 2 pass clear), and world geoms (contype & 3 == 3) accept bit 8 as well.
        /// </summary>
        void SetLayers()
        {
            var mine = new HashSet<int>();
            if (_geoms != null) foreach (int g in _geoms) mine.Add(g);
            else foreach (MjGeom g in body.GetComponentsInChildren<MjGeom>()) if (g.MujocoId >= 0) mine.Add(g.MujocoId);
            int bits = collideWithBoxers ? (1 | Layer) : Layer;
            for (int g = 0; g < _m->ngeom; g++)
            {
                if (mine.Contains(g)) { _m->geom_contype[g] = bits; _m->geom_conaffinity[g] = bits; }
                else if ((_m->geom_contype[g] & 3) == 3) _m->geom_conaffinity[g] |= Layer;
            }
            _layered = true;
        }

        void BeforePhysics(object sender, MjStepArgs a)
        {
            if (!_bound) return;
            if (!_layered) SetLayers();
            if (_sub == 0)
            {
                body.Observe();
                body.Infer();
                body.Drive();
            }
            else body.HoldTargets();
            foreach (var kv in _override) body.SetTarget(kv.Key, kv.Value);
        }

        void AfterPhysics(object sender, MjStepArgs a)
        {
            if (!_bound) return;
            _sub = (_sub + 1) % _decimation;
            if (_sub == 0) body.AfterStep(_dt);
        }

        // ---------------------------------------------------------------- what the game asks

        /// <summary>Standing, facing a point on the floor (Unity): the footwork brain's stand episode, the point its stand-in.</summary>
        public void Stand(Vector3 face) => Ask(MjBoxer.Stand, 0, face);
        /// <summary>Turning on the spot to face a point (Unity).</summary>
        public void Turn(Vector3 face) => Ask(MjBoxer.Turn, 0, face);
        /// <summary>Walking straight ahead at this speed, m/s (0.3 to 1.0, what it was taught).</summary>
        public void Walk(double speed) => Ask(MjBoxer.Walk, speed, Vector3.zero);

        public int Kind => body != null ? body.Kind : MjBoxer.Stand;

        void Ask(int kind, double speed, Vector3 face)
        {
            if (!_bound) return;
            if (kind == body.Kind && kind != MjBoxer.Walk)
            {
                // The same episode: only its stand-in moves, without a jump in its speed.
                body.Opp[0] = face.x;
                body.Opp[1] = face.z;
                return;
            }
            if (kind == body.Kind && kind == MjBoxer.Walk && body.Cmd[0] == speed) return;
            body.SetEpisodeAt(kind, speed, 0, 0, face.x, face.z);
        }

        /// <summary>
        /// Goes to a spot on the floor (Unity) and stands there facing a point. Each change of episode is one the brain met in
        /// training, from standing: it turns to the spot, walks while the spot stays ahead, and stops (stands) before it turns
        /// again; a walk is never turned out of mid-stride (seen 2026-11-03: the referee fell every time it was). True once there.
        /// </summary>
        public bool GoTo(Vector3 spot, Vector3 face, double speed, float near = 0.35f, float settleDelay = 0.6f, float stopAngle = 1.0f)
        {
            Vector3 to = spot - Position;
            to.y = 0f;
            float dist = to.magnitude;
            float off = Mathf.Abs(Vector3.SignedAngle(Forward, to, Vector3.up)) * Mathf.Deg2Rad;
            switch (Kind)
            {
                case MjBoxer.Walk:
                    // Arrived, or the spot has gone off to the side: stop, and decide from standing.
                    if (dist < near || off > stopAngle) { Stand(Ahead(face)); _stoppedAt = Time.time; }
                    return false;
                case MjBoxer.Turn:
                    if (dist < near * 2f) { Stand(Ahead(face)); return true; }
                    if (off < 0.25f) Walk(speed);
                    else Turn(spot);
                    return false;
                default:
                    // Standing: still near enough, face the point; otherwise, once settled from a walk, turn to the spot.
                    if (dist < near * 2f) { Stand(Ahead(face)); return true; }
                    if (Time.time - _stoppedAt > settleDelay) Turn(spot);
                    return false;
            }
        }

        float _stoppedAt = -9f;

        /// <summary>A point to face put a step and a half off, as the stand-in stood in training (one too close turns it on itself).</summary>
        Vector3 Ahead(Vector3 face)
        {
            Vector3 d = face - Position;
            d.y = 0f;
            return Position + (d.sqrMagnitude > 1e-4f ? d.normalized : Forward) * 1.5f;
        }

        /// <summary>Stood on a spot (Unity), facing a way, nothing moving: as the bout places a boxer.</summary>
        public void Place(Vector3 floorPoint, Vector3 facing)
        {
            if (!_bound) return;
            body.ResetToGuard(floorPoint.x, floorPoint.z, System.Math.Atan2(facing.z, facing.x));
            body.SetEpisodeAt(MjBoxer.Stand, 0, 0, 0, floorPoint.x + facing.x, floorPoint.z + facing.z);
            MjScene.Instance.SyncUnityToMjState();
        }

        /// <summary>A joint (by the trainer's name, "shoulder_x_r") held at a target from outside the brain; NaN gives it back.</summary>
        public void Hold(string joint, double target)
        {
            if (body == null || body.Config == null) return;
            int i = System.Array.IndexOf(body.Config.joint_order, joint);
            if (i < 0) return;
            if (double.IsNaN(target)) _override.Remove(i);
            else _override[i] = target;
        }

        public void ReleaseAll() => _override.Clear();

        /// <summary>Where it stands (its pelvis on the floor, Unity) and which way it faces.</summary>
        public Vector3 Position
        {
            get
            {
                Transform p = body != null ? body.transform.Find(body.prefix + "pelvis") : null;
                Vector3 at = p != null ? p.position : transform.position;
                at.y = 0f;
                return at;
            }
        }

        public Vector3 Forward => body != null && _bound ? body.Forward : transform.forward;
    }
}
