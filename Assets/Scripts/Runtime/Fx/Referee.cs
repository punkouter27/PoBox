using UnityEngine;
using PoBox.Mj;
using PoBox.Sim;

namespace PoBox.Fx
{
    /// <summary>
    /// The third man in the ring: a body on the MuJoCo plugin with a footwork brain of his own (<see cref="MjCrew"/>,
    /// training/models/crew/referee, trained to stand, walk and turn on command as the boxers' footwork was). This part
    /// decides where he goes; his brain walks him there. While they box he keeps to one side of the two, a step and a
    /// half off the line between them, on the side away from the camera. Over a boxer on the canvas he stands close and
    /// counts, his right arm coming down with every number. In the break he waits in a neutral corner; at the result he
    /// stands by the winner with his arm up. His arm's gestures are joint targets set over the brain's (MjCrew.Hold).
    /// He never touches a boxer: he is on a collision layer of his own.
    /// </summary>
    public class Referee : MonoBehaviour
    {
        public MjCrew crew;
        [Tooltip("Metres off the line between the boxers while they box.")]
        public float offLine = 1.5f;
        [Tooltip("Nearer than this to either boxer, metres, he steps away.")]
        public float keepClear = 1.0f;
        [Tooltip("Walking speed, m/s (what the brain was taught: 0.3 to 1.0).")]
        public float speed = 0.9f;

        int _lastCount = -1;
        float _countAt;
        bool _placed;

        void FixedUpdate()
        {
            Bout bout = Bout.Instance;
            if (crew == null || !crew.Ready || bout == null || bout.red == null || bout.blue == null || bout.red.ring == null) return;
            Vector2 c = bout.red.ring.centre;
            float half = bout.red.ring.ringHalf - 0.5f;
            Vector3 centre = new Vector3(c.x, 0f, c.y);
            Vector3 r = Flat(bout.red.pelvis.transform.position), b = Flat(bout.blue.pelvis.transform.position);
            Vector3 neutral = centre + new Vector3(Side(centre) * half, 0f, -Side(centre) * half) * 0.85f;
            if (!_placed) { crew.Place(neutral, centre - neutral); _placed = true; }

            Vector3 target, look;
            switch (bout.Phase)
            {
                case BoutPhase.Count when bout.Downed != null:
                {
                    // Close over the one down, between them and the other.
                    Vector3 down = Flat(bout.Downed.pelvis.transform.position);
                    Vector3 away = (bout.Downed == bout.red ? b : r) - down;
                    target = down + (away.sqrMagnitude > 0.01f ? away.normalized : Vector3.forward) * 1.1f;
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
                    target = win + Vector3.Cross(Vector3.up, toward.sqrMagnitude > 0.01f ? toward.normalized : Vector3.forward) * 0.8f;
                    look = Camera.main != null ? Flat(Camera.main.transform.position) : centre;
                    break;
                }
                default:
                {
                    // Off the line between them, on the side away from the camera so he is not in the picture's way.
                    Vector3 mid = (r + b) * 0.5f, line = b - r;
                    Vector3 perp = Vector3.Cross(Vector3.up, line.sqrMagnitude > 0.01f ? line.normalized : Vector3.right);
                    Vector3 cam = Camera.main != null ? Camera.main.transform.position - mid : perp;
                    if (Vector3.Dot(perp, cam) > 0f) perp = -perp;
                    target = mid + perp * offLine;
                    look = mid;
                    break;
                }
            }
            target = Clear(target, r, b);
            target.x = Mathf.Clamp(target.x, centre.x - half, centre.x + half);
            target.z = Mathf.Clamp(target.z, centre.z - half, centre.z + half);
            crew.GoTo(target, look, speed);
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

        /// <summary>The neutral corner he takes: whichever he is nearer now.</summary>
        float Side(Vector3 centre)
        {
            Vector3 at = crew.Position;
            return (at.x - centre.x) - (at.z - centre.z) >= 0f ? 1f : -1f;
        }

        /// <summary>A spot pushed out until it is clear of both boxers.</summary>
        Vector3 Clear(Vector3 p, Vector3 r, Vector3 b)
        {
            for (int k = 0; k < 2; k++)
            {
                Vector3 o = k == 0 ? r : b, d = p - o;
                d.y = 0f;
                if (d.magnitude < keepClear) p = o + (d.sqrMagnitude > 1e-4f ? d.normalized : Vector3.right) * keepClear;
            }
            return p;
        }
    }
}
