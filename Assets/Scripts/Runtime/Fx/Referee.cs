using UnityEngine;
using PoBox.Mj;
using PoBox.Sim;

namespace PoBox.Fx
{
    /// <summary>
    /// The third man in the ring: a body on the MuJoCo plugin with a footwork brain of his own (<see cref="MjCrew"/>,
    /// training/models/crew/referee, trained to stand, walk and turn on command as the boxers' footwork was). This part
    /// decides where he goes; his brain walks him there. He physically collides with active boxers and actively keeps
    /// clear of the boxing action in open ring space without crossing between the fighters or trapping himself on the
    /// ropes. Over a boxer on the canvas he steps clear to count, his right arm coming down with every number.
    /// </summary>
    public class Referee : MonoBehaviour
    {
        public MjCrew crew;
        [Tooltip("Preferred metres off the line between the boxers while they box.")]
        public float offLine = 2.15f;
        [Tooltip("Target minimum clearance from either boxer, metres; inside this he actively backs away.")]
        public float keepClear = 1.85f;
        [Tooltip("Walking speed, m/s (what the brain was taught: 0.3 to 1.0).")]
        public float speed = 0.65f;
        [Tooltip("His spot is moved only when the fight has moved it this far, metres: a brain asked somewhere new every frame never settles.")]
        public float settle = 0.45f;

        int _lastCount = -1;
        float _countAt;
        bool _placed;
        Vector3 _goal;
        float _downSince = -1f;
        bool _hasVel;
        Vector3 _prevR, _prevB, _velR, _velB;
        readonly Vector3[] _candidates = new Vector3[20];

        void Awake()
        {
            if (crew != null) crew.collideWithBoxers = true;
        }

        void FixedUpdate()
        {
            Bout bout = Bout.Instance;
            if (crew == null) return;
            crew.collideWithBoxers = true;
            if (!crew.Ready || bout == null || bout.red == null || bout.blue == null || bout.red.ring == null) return;
            Vector2 c = bout.red.ring.centre;
            // Well inside the ropes: his brain learned to walk on an open floor.
            float half = Mathf.Max(1.0f, bout.red.ring.ringHalf - 0.70f);
            Vector3 centre = new Vector3(c.x, 0f, c.y);
            Vector3 r = Flat(bout.red.pelvis.transform.position), b = Flat(bout.blue.pelvis.transform.position);
            TrackBoxerVelocities(r, b);
            Vector3 neutral = SafestNeutral(centre, half, r, b);
            if (!_placed) { crew.Place(neutral, centre - neutral); _placed = true; _goal = neutral; }
            // Down (he has no get-up brain): stood back in the safest open corner once nobody is watching, or after 4 s.
            bool fallen = crew.body.transform.Find(crew.body.prefix + "pelvis").position.y < 0.5f;
            if (!fallen) _downSince = -1f;
            else if (_downSince < 0f) _downSince = Time.time;
            bool quiet = bout.Phase == BoutPhase.WalkOn || bout.Phase == BoutPhase.Intro || bout.Phase == BoutPhase.RoundBreak;
            if (fallen && (quiet || Time.time - _downSince > 4f)) { crew.Place(neutral, centre - neutral); _goal = neutral; _downSince = -1f; return; }

            Vector3 me = Flat(crew.Position);
            Vector3 pr = ClampRing(r + _velR * 0.45f, centre, half + 0.35f);
            Vector3 pb = ClampRing(b + _velB * 0.45f, centre, half + 0.35f);
            Vector3 target, look;
            switch (bout.Phase)
            {
                case BoutPhase.Count when bout.Downed != null:
                {
                    // Close enough to count while keeping clear of both the standing boxer and the downed boxer's un-ghost space.
                    Vector3 down = Flat(bout.Downed.pelvis.transform.position);
                    Vector3 standing = bout.Downed == bout.red ? b : r;
                    target = PickCountSpot(down, standing, centre, half);
                    look = down;
                    break;
                }
                case BoutPhase.RoundBreak:
                case BoutPhase.WalkOn:
                    target = neutral;
                    look = centre;
                    break;
                case BoutPhase.Results when bout.Winner != null:
                {
                    Vector3 win = Flat(bout.Winner.pelvis.transform.position);
                    Vector3 toward = centre - win;
                    target = ClampRing(win + Vector3.Cross(Vector3.up, toward.sqrMagnitude > 0.01f ? toward.normalized : Vector3.forward) * 0.9f, centre, half);
                    look = Camera.main != null ? Flat(Camera.main.transform.position) : centre;
                    break;
                }
                default:
                {
                    target = PickOpenSpot(me, r, b, pr, pb, centre, half);
                    look = (r + b) * 0.5f;
                    break;
                }
            }
            float nearestNow = Mathf.Min(FlatDist(me, r), FlatDist(me, b));
            bool crowded = bout.Phase == BoutPhase.Fight && nearestNow < 1.55f;
            if (crowded || FlatDist(target, _goal) > settle) _goal = target;
            double moveSpeed = crowded ? System.Math.Max(speed, 0.85) : speed;
            crew.GoTo(_goal, look, moveSpeed, crowded ? 0.32f : 0.4f, crowded ? 0.12f : 0.25f, crowded ? 1.25f : 1.05f);
            Gesture(bout);
        }

        /// <summary>The right arm, over the brain: the count's stroke, the winner's arm up, or given back.</summary>
        void Gesture(Bout bout)
        {
            if (bout.Phase == BoutPhase.Count && bout.Downed != null)
            {
                // Swung forward and up between numbers, down hard on each.
                if (bout.Count != _lastCount) { _lastCount = bout.Count; _countAt = Time.time; }
                float since = Time.time - _countAt;
                float up = since < 0.18f ? Mathf.Lerp(1.7f, 0.5f, since / 0.18f) : Mathf.Lerp(0.5f, 1.7f, Mathf.Clamp01((since - 0.18f) / 0.5f));
                crew.Hold("shoulder_z_r", up);
                crew.Hold("elbow_r", 0.3);
                return;
            }
            _lastCount = -1;
            if (bout.Phase == BoutPhase.Results && bout.Winner != null)
            {
                // Up beside the winner: as high as the shoulder lets a hand go (the boxers' victory pose).
                crew.Hold("shoulder_x_r", 1.51);
                crew.Hold("shoulder_z_r", 1.92);
                crew.Hold("elbow_r", 1.2);
                return;
            }
            crew.ReleaseAll();
        }

        static Vector3 Flat(Vector3 p) => new Vector3(p.x, 0f, p.z);
        static float FlatDist(Vector3 a, Vector3 b) { float dx = a.x - b.x, dz = a.z - b.z; return Mathf.Sqrt(dx * dx + dz * dz); }

        static Vector3 ClampRing(Vector3 p, Vector3 centre, float half) =>
            new Vector3(Mathf.Clamp(p.x, centre.x - half, centre.x + half), 0f, Mathf.Clamp(p.z, centre.z - half, centre.z + half));

        void TrackBoxerVelocities(Vector3 r, Vector3 b)
        {
            float dt = Time.fixedDeltaTime;
            if (_hasVel && dt > 1e-4f)
            {
                Vector3 rawR = Vector3.ClampMagnitude((r - _prevR) / dt, 2.5f);
                Vector3 rawB = Vector3.ClampMagnitude((b - _prevB) / dt, 2.5f);
                _velR = Vector3.Lerp(_velR, rawR, 0.25f);
                _velB = Vector3.Lerp(_velB, rawB, 0.25f);
            }
            _prevR = r;
            _prevB = b;
            _hasVel = true;
        }

        /// <summary>Picks whichever neutral corner keeps him farthest from both boxers, breaking ties by proximity.</summary>
        Vector3 SafestNeutral(Vector3 centre, float half, Vector3 r, Vector3 b)
        {
            Vector3 c0 = centre + new Vector3(half, 0f, -half) * 0.85f;
            Vector3 c1 = centre + new Vector3(-half, 0f, half) * 0.85f;
            float d0 = Mathf.Min(FlatDist(c0, r), FlatDist(c0, b));
            float d1 = Mathf.Min(FlatDist(c1, r), FlatDist(c1, b));
            if (Mathf.Abs(d0 - d1) > 0.35f) return d0 > d1 ? c0 : c1;
            Vector3 at = Flat(crew.Position);
            return FlatDist(at, c0) <= FlatDist(at, c1) ? c0 : c1;
        }

        Vector3 PickCountSpot(Vector3 down, Vector3 standing, Vector3 centre, float half)
        {
            Vector3 dir = standing - down;
            Vector3 perp = Vector3.Cross(Vector3.up, dir.sqrMagnitude > 0.01f ? dir.normalized : Vector3.right);
            Vector3 a = ClampRing(down + perp * 1.45f, centre, half);
            Vector3 bSpot = ClampRing(down - perp * 1.45f, centre, half);
            return FlatDist(a, standing) >= FlatDist(bSpot, standing) ? a : bSpot;
        }

        /// <summary>
        /// Evaluates open spots around the ring and selects one well outside punch range while never cutting between
        /// the boxers to switch sides.
        /// </summary>
        Vector3 PickOpenSpot(Vector3 me, Vector3 r, Vector3 b, Vector3 pr, Vector3 pb, Vector3 centre, float half)
        {
            Vector3 mid = (r + b) * 0.5f, line = b - r;
            Vector3 perp = Vector3.Cross(Vector3.up, line.sqrMagnitude > 0.01f ? line.normalized : Vector3.right);
            Vector3 nearBoxer = FlatDist(me, r) <= FlatDist(me, b) ? r : b;
            Vector3 away = me - nearBoxer;
            away = away.sqrMagnitude > 0.01f ? away.normalized : perp;

            int k = 0;
            _candidates[k++] = mid + perp * offLine;
            _candidates[k++] = mid - perp * offLine;
            _candidates[k++] = mid + perp * (offLine + 0.65f);
            _candidates[k++] = mid - perp * (offLine + 0.65f);
            _candidates[k++] = me + away * 1.45f;
            _candidates[k++] = me + Quaternion.Euler(0f, 42f, 0f) * away * 1.45f;
            _candidates[k++] = me + Quaternion.Euler(0f, -42f, 0f) * away * 1.45f;
            float corner = half * 0.92f, quad = half * 0.55f, side = half * 0.90f;
            _candidates[k++] = centre + new Vector3(corner, 0f, corner);
            _candidates[k++] = centre + new Vector3(corner, 0f, -corner);
            _candidates[k++] = centre + new Vector3(-corner, 0f, corner);
            _candidates[k++] = centre + new Vector3(-corner, 0f, -corner);
            _candidates[k++] = centre + new Vector3(side, 0f, 0f);
            _candidates[k++] = centre + new Vector3(-side, 0f, 0f);
            _candidates[k++] = centre + new Vector3(0f, 0f, side);
            _candidates[k++] = centre + new Vector3(0f, 0f, -side);
            _candidates[k++] = centre + new Vector3(quad, 0f, quad);
            _candidates[k++] = centre + new Vector3(-quad, 0f, -quad);
            _candidates[k++] = me;
            _candidates[k++] = _goal;

            Vector3 camDir = Camera.main != null ? Flat(Camera.main.transform.position - mid).normalized : Vector3.back;
            Vector3 best = ClampRing(_candidates[0], centre, half);
            float bestScore = float.NegativeInfinity;
            for (int i = 0; i < k; i++)
            {
                Vector3 c = ClampRing(_candidates[i], centre, half);
                float dNow = Mathf.Min(FlatDist(c, r), FlatDist(c, b));
                float dPred = Mathf.Min(FlatDist(c, pr), FlatDist(c, pb));
                float score = dNow < keepClear ? -(keepClear - dNow) * 14f : Mathf.Min(dNow, 3.0f) * 2.0f;
                score += dPred < keepClear ? -(keepClear - dPred) * 8f : Mathf.Min(dPred, 3.0f) * 0.9f;

                float segDist = Mathf.Sqrt(PointSegDistSqXZ(c, r, b));
                score += Mathf.Min(segDist, 2.2f) * 1.1f;
                if (segDist < 1.1f) score -= (1.1f - segDist) * 5.0f;

                float pathClear = Mathf.Min(Mathf.Sqrt(PointSegDistSqXZ(r, me, c)), Mathf.Sqrt(PointSegDistSqXZ(b, me, c)));
                if (pathClear < 1.35f) score -= (1.35f - pathClear) * 8.0f;
                if (SegmentsCrossXZ(me, c, r, b)) score -= 12.0f;

                Vector3 fromMid = c - mid;
                if (fromMid.sqrMagnitude > 0.01f) score += 0.35f * Vector3.Dot(fromMid.normalized, -camDir);
                score -= 0.35f * FlatDist(me, c);
                if (FlatDist(c, _goal) < 0.35f) score += 0.70f;

                if (score > bestScore) { bestScore = score; best = c; }
            }
            return best;
        }

        static float PointSegDistSqXZ(Vector3 p, Vector3 a, Vector3 b)
        {
            float abx = b.x - a.x, abz = b.z - a.z;
            float lenSq = abx * abx + abz * abz;
            if (lenSq < 1e-6f) { float dx = p.x - a.x, dz = p.z - a.z; return dx * dx + dz * dz; }
            float t = Mathf.Clamp01(((p.x - a.x) * abx + (p.z - a.z) * abz) / lenSq);
            float cx = a.x + abx * t - p.x, cz = a.z + abz * t - p.z;
            return cx * cx + cz * cz;
        }

        static bool SegmentsCrossXZ(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            float d1 = CrossXZ(c, d, a), d2 = CrossXZ(c, d, b);
            float d3 = CrossXZ(a, b, c), d4 = CrossXZ(a, b, d);
            return ((d1 > 0f && d2 < 0f) || (d1 < 0f && d2 > 0f)) && ((d3 > 0f && d4 < 0f) || (d3 < 0f && d4 > 0f));
        }

        static float CrossXZ(Vector3 o, Vector3 p, Vector3 q) => (p.x - o.x) * (q.z - o.z) - (p.z - o.z) * (q.x - o.x);
    }
}
