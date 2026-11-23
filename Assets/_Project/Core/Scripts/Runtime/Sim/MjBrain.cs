using Unity.InferenceEngine;
using PoBox.Mj;
using UnityEngine;

namespace PoBox.Sim
{
    /// <summary>
    /// What a trained boxer's fighter needs from its body on the MuJoCo plugin, around the policy that
    /// <see cref="MjRing"/> runs for it:
    ///   * the fighter's hurt goes into the drives (its DriveScale), and whether it is down into the ring,
    ///     which then lets the other boxer pass through it;
    ///   * while the referee counts over the other boxer, this one is held in its guard turned towards them,
    ///     and let go when they are up: the first moment of a training episode;
    ///   * a boxer down for long enough gets up with its get-up policy if it has one, shown a stand-in of its
    ///     opponent where the opponent stands (Training/envs/getup.py), and the match policy has it back once it
    ///     has stood long enough (the fighter decides when);
    ///   * punches are recognised as they are thrown (a glove moving at the opponent's head faster than a guard
    ///     moves), and the ring's measured landings, stops, footsteps and bodies meeting the canvas become the
    ///     fight's events.
    /// </summary>
    [DefaultExecutionOrder(-40)]
    public class MjBrain : MonoBehaviour
    {
        public Fighter fighter;
        public MjBoxer boxer;
        public MjRing ring;
        [Tooltip("A glove closing on the opponent's head faster than this, m/s, is a punch being thrown.")]
        public float punchSpeed = 3.5f;

        public bool GettingUp { get; private set; }
        /// <summary>The critic's value of the present state, in the trainer's reward units: what the win-probability bar reads.</summary>
        public float Value => boxer != null ? boxer.Value : 0f;
        public bool HasValue => boxer != null && boxer.Running != null;

        bool _holding, _releaseNext, _raised, _refRaised;
        Vector3 _resultFacing;

        /// <summary>
        /// The way the winner faces for the result, flat: towards the camera that was live when the result came up, kept
        /// through the cuts after it (the referee stands beside it facing the same way). Zero before a result.
        /// </summary>
        public Vector3 ResultFacing => _raised ? _resultFacing : Vector3.zero;

        /// <summary>
        /// The referee is at the winner's left and lifts its arm (Fx/Referee): from both gloves up to the left one held high
        /// beside his, the right one in the guard. Once a result.
        /// </summary>
        /// <summary>The winner, gloves up and held where it won, before the referee raises its hand: the time for its
        /// celebration (IntroMoves), which changes the held pose (MjRing.ChangeHeldPose). The referee's raise ends it.</summary>
        public bool Celebrating => _raised && !_refRaised && fighter != null && !fighter.IsDown;

        public void HandRaisedByReferee()
        {
            if (!_raised || _refRaised || fighter.IsDown) return;
            _refRaised = true;
            ring.HoldPose(boxer, fighter.pelvis.transform.position, _resultFacing, boxer.Pose(MjBoxer.RaisedLeft));
        }
        readonly float[] _punchNotedAt = { -9f, -9f };
        Transform[] _gloves;

        void OnEnable()
        {
            if (ring == null) return;
            ring.PunchLanded += OnLanded;
            ring.PunchStopped += OnStopped;
            ring.FootStep += OnFoot;
            ring.BodyLanded += OnBodyLanded;
        }

        void OnDisable()
        {
            if (ring == null) return;
            ring.PunchLanded -= OnLanded;
            ring.PunchStopped -= OnStopped;
            ring.FootStep -= OnFoot;
            ring.BodyLanded -= OnBodyLanded;
        }

        /// <summary>Coming out of the corner on the footwork policy (a free-for-all's opening): walking, or squaring up. Not yet boxing.</summary>
        public bool Walking => _out != Out.None;
        enum Out { None, Walk, Square, Corner, Seated, Leaving, Gone }

        /// <summary>Walking out of the ring after the result, or gone from it (LeaveRing): the referee lets go of its hand.</summary>
        public bool LeftRing => _out == Out.Leaving || _out == Out.Gone;
        [Tooltip("After the result: how long the boxers stay for the referee's raised hand before both walk back to the dressing room, seconds into the result.")]
        public float leaveAfter = 8f;
        [Tooltip("The walk back to the dressing room, m/s: slow (the footwork policy walks 0.3 to 1.0).")]
        public float leaveSpeed = 0.35f;
        [Tooltip("Past the ring's edge by this much (m, beyond the ropes, at the apron's edge) the boxer has gone: hidden and set aside.")]
        public float goneBeyondRopes = 0.5f;
        Vector3 _exit, _via;
        bool _leaveStart, _pastVia;
        Vector3 Heading => _pastVia ? _exit : _via;
        System.Collections.Generic.Dictionary<Renderer, bool> _shown;
        Out _out;
        float _squareFor;
        Fighter _aimedAt;
        ModelAsset _footwork;
        [Tooltip("Squaring up after the walk: the match policy takes the body once it faces its opponent this closely (radians) and moves this slowly (m/s), as a training episode starts.")]
        public float squareFacing = 0.26f, squareSpeed = 0.3f;
        [Tooltip("At the bell: the walk back to the corner on the footwork policy, m/s, and the longest it may take before the boxer is sat down on its stool anyway, seconds.")]
        public float cornerSpeed = 0.8f, cornerSeconds = 8f;
        Vector3 _seat, _front;
        float _toCorner;

        /// <summary>
        /// Walks straight ahead at a speed (m/s, the footwork policy's 0.3 to 1.0) on the policy taught to walk on
        /// command: how a boxer comes out of its corner.
        /// </summary>
        public void WalkOut(double speed)
        {
            if (_footwork == null || fighter.IsDown) return;
            _out = Out.Walk;
            boxer.SetPolicy(_footwork);
            boxer.SetEpisodeAt(MjBoxer.Walk, speed, 0, 0, 0, 0);
        }

        /// <summary>
        /// The walk is over: the boxer stops and turns to face its opponent, still on the footwork policy, and the
        /// match policy has it once it stands square and still. Handed over mid-stride, the match policy (taught from
        /// a standing start a step and a half from its opponent) fell over within seconds, nearly every time.
        /// </summary>
        public void EndWalk()
        {
            if (_out != Out.Walk) return;
            // Down during the walk: nothing to square up to. The get-up hands the body back to the bout.
            if (fighter.IsDown) { _out = Out.None; return; }
            _out = Out.Square;
            _squareFor = 0f;
            AimAt();
        }

        /// <summary>The stand-in put on the opponent where it stands, its head speed started again: a new episode's first moment.</summary>
        void AimAt()
        {
            _aimedAt = fighter.opponent;
            AimAtOpponent();
            boxer.SetEpisodeAt(MjBoxer.Turn, 0, 0, 0, boxer.Opp[0], boxer.Opp[1]);
        }

        void AimAtOpponent()
        {
            if (fighter.opponent == null || fighter.opponent.pelvis == null) return;
            Vector3 at = fighter.opponent.pelvis.transform.position;
            boxer.Opp[0] = at.x;        // MuJoCo's x and y are Unity's x and z
            boxer.Opp[1] = at.z;
        }

        void Squaring()
        {
            if (fighter.IsDown) { _out = Out.None; return; }
            _squareFor += Time.fixedDeltaTime;
            // Turned on another opponent while squaring up: the stand-in moves there without a jump in its speed.
            if (fighter.opponent != _aimedAt) AimAt();
            else AimAtOpponent();
            double vx = boxer.Obs[0], vy = boxer.Obs[1];
            bool square = boxer.FacingError < squareFacing && System.Math.Sqrt(vx * vx + vy * vy) < squareSpeed && _squareFor > 0.5f;
            // Hit while squaring up, or taking too long about it: box.
            if (square || _squareFor > 3f || fighter.TimeSinceHit < 0.3f) BackToTheBout();
        }

        void Start()
        {
            _footwork = boxer.policy;
            if (boxer.matchPolicy != null) boxer.SetPolicy(boxer.matchPolicy);
            _gloves = new[] { boxer.transform.FindDeep(boxer.prefix + "glove_l"), boxer.transform.FindDeep(boxer.prefix + "glove_r") };
        }

        /// <summary>A glove (0 left, 1 right): what the camera and the excitement read it by.</summary>
        public Transform Glove(int hand) => _gloves != null ? _gloves[hand] : null;

        void OnLanded(MjBoxer attacker, int hand, bool head, float speed, Vector3 at)
        {
            if (attacker == boxer) fighter.OnPunchLanded(hand == 0 ? -1 : 1, head, speed, at, ring.GloveVelocity(boxer, hand));
        }

        void OnStopped(MjBoxer defender, float speed, Vector3 at)
        {
            if (defender != boxer || !Bout.Fighting) return;
            fighter.stats.blocked++;
            SimBus.RaiseBlock(fighter, at, speed);
        }

        void OnFoot(MjBoxer whose, Vector3 at, float speed)
        {
            if (whose == boxer && !fighter.IsDown) SimBus.RaiseFootStep(at, speed);
        }

        void OnBodyLanded(MjBoxer whose, Vector3 at, float impulse)
        {
            if (whose == boxer) SimBus.RaiseFloorImpact(at, impulse);
        }

        /// <summary>Sent by the fighter when its time on the canvas is up. Answers only if there is a policy for getting up.</summary>
        public void BeginGetUp()
        {
            if (boxer.getUpPolicy == null) return;
            fighter.AcceptGetUp();
            GettingUp = true;
            _out = Out.None;
            boxer.SetPolicy(boxer.getUpPolicy);
            boxer.SetSpeedShare(boxer.Config.getup_speed_share);
            // The get-up stage trained with the opponent in the ring (frozen in its guard, as the referee holds this
            // one's): it is shown that opponent as it is, as the trainer's spar env shows it.
            Vector3 them = fighter.opponent != null ? fighter.opponent.pelvis.transform.position : boxer.transform.position + boxer.Forward;
            boxer.SetEpisodeAt(MjBoxer.GetUp, 0, 0, 0, them.x, them.z);
        }

        /// <summary>Sent by the fighter when it is back on its feet: the match policy takes the body over where the get-up left it.</summary>
        public void EndGetUp() => BackToTheBout();

        /// <summary>Called when the fighter is stood back up: the policy starts from a clean slate, as an episode does.</summary>
        public void ResetBrain() => BackToTheBout();

        /// <summary>
        /// The bell: back to the corner on the footwork policy, turning to a spot just in front of the stool and walking
        /// there, then sat down on it (Sit). Down, or too long about it, it is sat there anyway.
        /// </summary>
        public void GoToCorner(Vector3 stool)
        {
            _seat = stool;
            Vector3 middle = new Vector3(ring.centre.x, stool.y, ring.centre.y) - stool;
            _front = stool + (middle.sqrMagnitude > 0.01f ? middle.normalized : Vector3.zero) * 0.55f;
            _holding = _releaseNext = false;
            if (_footwork == null || fighter.IsDown) { Sit(stool); return; }
            _out = Out.Corner;
            _toCorner = 0f;
            boxer.SetPolicy(_footwork);
            boxer.SetSpeedShare(1.0);
            boxer.SetEpisodeAt(MjBoxer.Turn, 0, 0, 0, _front.x, _front.z);
        }

        void Cornering()
        {
            _toCorner += Time.fixedDeltaTime;
            Vector3 to = _front - fighter.pelvis.transform.position;
            to.y = 0f;
            // Arrived, too long about it, or stumbling: sat down.
            bool stumbling = fighter.PelvisHeight < fighter.standingPelvisHeight * 0.7f || fighter.Upright < 0.6f;
            if (to.magnitude < 0.35f || _toCorner > cornerSeconds || fighter.IsDown || stumbling) { Sit(_seat); return; }
            // The bell gives a boxer its legs back for the walk: full strength, whatever the round took.
            boxer.DriveScale = 1.0;
            float error = Mathf.Abs(Vector3.SignedAngle(boxer.Forward, to, Vector3.up)) * Mathf.Deg2Rad;
            // Turned to the spot, walk; off it by more than a third of a right angle, turn to it again.
            if (boxer.Kind == MjBoxer.Turn)
            {
                boxer.Opp[0] = _front.x;
                boxer.Opp[1] = _front.z;
                if (error < 0.25f) boxer.SetEpisodeAt(MjBoxer.Walk, cornerSpeed, 0, 0, 0, 0);
            }
            else if (error > 0.6f) boxer.SetEpisodeAt(MjBoxer.Turn, 0, 0, 0, _front.x, _front.z);
        }

        /// <summary>The minute between rounds: sat on the stool at this spot, facing the middle of the ring, until the bout places it again.</summary>
        public void Sit(Vector3 stool)
        {
            Vector3 facing = new Vector3(ring.centre.x - stool.x, 0f, ring.centre.y - stool.z);
            ring.HoldPose(boxer, stool, facing, boxer.Pose(MjBoxer.Sitting));
            _holding = _releaseNext = false;
            _out = Out.Seated;
        }

        /// <summary>
        /// The bout is over: back to the dressing room, slowly, on the footwork policy, out over the side of the ring
        /// behind its own corner. It turns to the exit, walks, goes out through the ropes (MjRing.SetLeaving: the floor
        /// is all it meets), and once past the apron's edge it is hidden and set aside until the next bout places it.
        /// </summary>
        public void LeaveRing(Vector3 corner)
        {
            if (LeftRing || fighter.IsDown) return;
            if (_footwork == null) { Gone(); return; }
            Vector3 c = new Vector3(ring.centre.x, 0f, ring.centre.y);
            Vector3 side = corner - c;
            side.y = 0f;
            // First to its own corner (a metre in from the post), then out over the side beside it (the one the corner's
            // longer offset points at), aiming well past the ropes.
            float inside = Mathf.Max(0.5f, ring.ringHalf - 1.0f);
            _via = c + new Vector3(Mathf.Clamp(side.x, -inside, inside), 0f, Mathf.Clamp(side.z, -inside, inside));
            Vector3 exit = Mathf.Abs(side.z) >= Mathf.Abs(side.x)
                ? new Vector3(Mathf.Clamp(side.x, -inside, inside), 0f, Mathf.Sign(side.z) * (ring.ringHalf + 3f))
                : new Vector3(Mathf.Sign(side.x) * (ring.ringHalf + 3f), 0f, Mathf.Clamp(side.z, -inside, inside));
            _exit = c + exit;
            Vector3 fromVia = fighter.pelvis.transform.position - _via;
            fromVia.y = 0f;
            _pastVia = fromVia.magnitude < 0.6f;
            _out = Out.Leaving;
            _toCorner = 0f;
            GettingUp = false;
            _holding = _releaseNext = false;
            // From the victory pose or the guard it was held in: stood in its guard for a step, turned to the exit,
            // then let go on the footwork policy, as the walk to the stool starts.
            Vector3 to = _exit - fighter.pelvis.transform.position;
            to.y = 0f;
            ring.Release(boxer);
            ring.Hold(boxer, to);
            _leaveStart = true;
            ring.SetLeaving(boxer, true);
            boxer.SetPolicy(_footwork);
            boxer.SetSpeedShare(1.0);
        }

        void Leaving()
        {
            if (_leaveStart)
            {
                _leaveStart = false;
                ring.Release(boxer);
                boxer.SetEpisodeAt(MjBoxer.Turn, 0, 0, 0, Heading.x, Heading.z);
                return;
            }
            _toCorner += Time.fixedDeltaTime;
            Vector3 at = fighter.pelvis.transform.position;
            float out_ = Mathf.Max(Mathf.Abs(at.x - ring.centre.x), Mathf.Abs(at.z - ring.centre.y));
            bool stumbling = fighter.PelvisHeight < fighter.standingPelvisHeight * 0.6f || fighter.Upright < 0.5f;
            // Out past the apron, or still not there after a minute: gone.
            if (out_ > ring.ringHalf + goneBeyondRopes || _toCorner > 60f) { Gone(); return; }
            // Down on the way out (the legs of a beaten boxer): stood up in its guard where it is, and on again.
            if (stumbling && _toCorner > 1f)
            {
                // Already at the ropes, it would be stood back inside the ring (MjRing.Hold keeps a step in): gone instead.
                if (out_ > ring.ringHalf - 0.6f) { Gone(); return; }
                Vector3 face = Heading - at;
                face.y = 0f;
                ring.Hold(boxer, face);
                _leaveStart = true;
                return;
            }
            boxer.DriveScale = 1.0;
            Vector3 to = Heading - at;
            to.y = 0f;
            // At its corner: turned out to the side, and on.
            if (!_pastVia && to.magnitude < 0.45f)
            {
                _pastVia = true;
                boxer.SetEpisodeAt(MjBoxer.Turn, 0, 0, 0, _exit.x, _exit.z);
                return;
            }
            float error = Mathf.Abs(Vector3.SignedAngle(boxer.Forward, to, Vector3.up)) * Mathf.Deg2Rad;
            if (boxer.Kind == MjBoxer.Turn)
            {
                boxer.Opp[0] = Heading.x;
                boxer.Opp[1] = Heading.z;
                if (error < 0.25f) boxer.SetEpisodeAt(MjBoxer.Walk, leaveSpeed, 0, 0, 0, 0);
            }
            else if (error > 0.6f) boxer.SetEpisodeAt(MjBoxer.Turn, 0, 0, 0, Heading.x, Heading.z);
        }

        /// <summary>In the dressing room: hidden, its brain stopped, held far off the canvas until the next bout.</summary>
        void Gone()
        {
            _out = Out.Gone;
            ring.HoldPose(boxer, new Vector3(ring.centre.x + 60f + 2f * transform.GetSiblingIndex(), 0f, ring.centre.y), Vector3.forward, null);
            ring.SetIdle(boxer, true);
            _shown ??= new System.Collections.Generic.Dictionary<Renderer, bool>();
            foreach (Renderer r in fighter.GetComponentsInChildren<Renderer>(true))
            {
                if (!_shown.ContainsKey(r)) _shown[r] = r.enabled;
                r.enabled = false;
            }
        }

        /// <summary>Back from the dressing room (the next bout placing it): shown, run, solid again.</summary>
        void Returned()
        {
            ring.SetLeaving(boxer, false);
            ring.SetIdle(boxer, false);
            if (_shown == null) return;
            foreach (var kv in _shown) if (kv.Key != null) kv.Key.enabled = kv.Value;
            _shown.Clear();
        }

        void BackToTheBout()
        {
            if (LeftRing) Returned();
            GettingUp = false;
            _raised = false;
            _refRaised = false;
            boxer.SetSpeedShare(1.0);
            _out = Out.None;
            // A boxer with no match policy boxes on its footwork policy (Sokol, until his boxing is trained).
            ModelAsset bout = boxer.matchPolicy != null ? boxer.matchPolicy : _footwork;
            if (bout != null) boxer.SetPolicy(bout);
            boxer.SetEpisodeAt(MjBoxer.Match, 0, 0, 0, 0, 0);
        }

        void FixedUpdate()
        {
            if (!ring.Ready || fighter == null) return;
            boxer.DriveScale = fighter.DriveScale;
            boxer.DazeShare = fighter.dazeRule ? fighter.Daze / Mathf.Max(1f, fighter.LegsAt) : 0.0;
            Fighter foe = fighter.opponent;
            boxer.OpponentDazeShare = foe != null && foe.dazeRule ? foe.Daze / Mathf.Max(1f, foe.LegsAt) : 0.0;
            ring.SetDown(boxer, fighter.IsDown);
            ring.SetRising(boxer, fighter.IsDown && fighter.IsRising);
            if (!Bout.SimRunning) return;
            if (_out == Out.Corner) { Cornering(); return; }
            if (_out == Out.Seated || _out == Out.Gone) return;
            if (_out == Out.Leaving) { Leaving(); return; }
            if (_out == Out.Square) Squaring();

            Fighter them = fighter.opponent;
            Bout bout = Bout.Instance;
            // The result: the winner's gloves go up where it stands; the loser on its feet, and both after a draw,
            // wait in the guard. A loser on the canvas is left to its get-up (Fighter.Beaten).
            if (bout != null && bout.Phase == BoutPhase.Results)
            {
                if (fighter.IsDown) return;
                // The hand has been up long enough: both go back to the dressing room, each out behind its own corner.
                if (bout.PhaseAge > leaveAfter)
                {
                    Transform corner = fighter == bout.red ? bout.redCorner : bout.blueCorner;
                    LeaveRing(corner != null ? corner.position : fighter.pelvis.transform.position);
                    return;
                }
                Vector3 facing = them != null ? them.pelvis.transform.position - fighter.pelvis.transform.position : boxer.Forward;
                if (fighter != bout.Winner) ring.Hold(boxer, facing);
                else if (!_raised)
                {
                    // Turned to the camera that is live now, the way the referee will stand with it.
                    Camera cam = Camera.main;
                    Vector3 toCam = cam != null ? cam.transform.position - fighter.pelvis.transform.position : facing;
                    toCam.y = 0f;
                    _resultFacing = toCam.sqrMagnitude > 0.01f ? toCam.normalized : facing.normalized;
                    ring.HoldPose(boxer, fighter.pelvis.transform.position, _resultFacing, boxer.Pose(MjBoxer.Victory));
                    _raised = true;
                    _refRaised = false;
                }
                return;
            }

            // While the referee counts over the other fighter this one stands in its guard, turned towards them,
            // and waits, through the referee's look at them once they are up. Then it is let go from that stance,
            // with a clear head.
            if (them != null && !fighter.IsDown)
            {
                Vector3 towards = them.pelvis.transform.position - fighter.pelvis.transform.position;
                if (them.IsDown || (bout != null && bout.Phase == BoutPhase.Count && bout.Downed == them))
                {
                    ring.Hold(boxer, towards);
                    _holding = true;
                    return;
                }
                if (_holding)
                {
                    ring.Hold(boxer, towards);      // one last look at where they have been stood, then go
                    _holding = false;
                    _releaseNext = true;
                    return;
                }
                if (_releaseNext)
                {
                    _releaseNext = false;
                    ring.Release(boxer);
                    ResetBrain();
                }
            }
            NotePunches();
        }

        /// <summary>
        /// A policy announces nothing: a punch is recognised by what it is, a glove moving at the opponent's head
        /// or body faster than a guard moves, landed or not (a miss is a punch thrown too). Named by how the glove
        /// travels: straight, up into it, round at it, or low.
        /// </summary>
        void NotePunches()
        {
            if (fighter.IsDown || fighter.opponent == null || _gloves == null) return;
            Vector3 head = fighter.opponent.HeadPosition;
            Vector3 body = fighter.opponent.torso != null ? fighter.opponent.torso.transform.position : head - 0.5f * Vector3.up;
            for (int hand = 0; hand < 2; hand++)
            {
                Transform glove = _gloves[hand];
                if (glove == null || Time.time - _punchNotedAt[hand] < 0.35f) continue;
                Vector3 v = ring.GloveVelocity(boxer, hand);
                float closing = Mathf.Max(Closing(head - glove.position, v), Closing(body - glove.position, v));
                if (closing < punchSpeed) continue;
                _punchNotedAt[hand] = Time.time;
                float speed = Mathf.Max(0.01f, v.magnitude);
                PunchType type;
                if (glove.position.y < body.y + 0.12f) type = PunchType.Body;
                else if (v.y / speed > 0.6f) type = PunchType.Uppercut;
                else if (closing / speed < 0.75f) type = PunchType.Hook;
                else type = hand == 0 ? PunchType.Jab : PunchType.Cross;
                fighter.NotePunch(hand == 0 ? -1 : 1, type);
            }
        }

        /// <summary>A glove's speed towards a target this far away, m/s; 0 out of reach (beyond 1.3 m) or already on it.</summary>
        static float Closing(Vector3 to, Vector3 v)
        {
            float distance = to.magnitude;
            return distance < 0.05f || distance > 1.3f ? 0f : Vector3.Dot(v, to / distance);
        }
    }

    public static class TransformSearch
    {
        /// <summary>A descendant by name, however deep.</summary>
        public static Transform FindDeep(this Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t)
            {
                Transform found = c.FindDeep(name);
                if (found != null) return found;
            }
            return null;
        }
    }
}
