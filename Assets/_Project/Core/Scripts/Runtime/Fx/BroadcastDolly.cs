using Unity.Cinemachine;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;
using PoBox.Sim;

namespace PoBox.Fx
{
    /// <summary>
    /// A camera on a rail round the ring: drives the <see cref="CinemachineSplineDolly"/> on the same camera
    /// along its <see cref="SplineContainer"/> (Unity Splines; the rail is knots placed in the scene).
    ///
    /// Two ways to move, picked in the inspector:
    /// <list type="bullet">
    /// <item><b>Sweep</b>: end to end and back, eased at the ends, one pass in <see cref="sweepSeconds"/>: the
    /// walk-on and between-rounds shot.</item>
    /// <item><b>Follow</b>: the dolly slides to the point of the rail nearest the middle of the two boxers
    /// (plus <see cref="lead"/> along the rail, so the camera sits off their line), no faster than
    /// <see cref="followSpeed"/>: the live shot that never cuts.</item>
    /// </list>
    /// The camera's aim is its own Rotation Control (Rotation Composer at a target group, say); this only
    /// moves it. <c>BroadcastDirector</c> needs nothing new: the camera is one more CinemachineCamera to
    /// put on air, and <see cref="Restart"/> starts a sweep from the near end when it cuts to it.
    /// It reads the boxers' places only; it never writes to the fight.
    /// </summary>
    [RequireComponent(typeof(CinemachineSplineDolly))]
    public class BroadcastDolly : MonoBehaviour
    {
        public enum Mode { Sweep, Follow }

        [SerializeField] Mode mode = Mode.Sweep;
        [Tooltip("Seconds for one pass end to end.")]
        [SerializeField] float sweepSeconds = 9f;
        [Tooltip("Follow: how far along the rail past the nearest point the camera sits (0..1 of the rail), and the fastest it slides, rail lengths a second.")]
        [SerializeField] float lead = 0.04f, followSpeed = 0.12f;
        [Tooltip("Follow without a bout (the free-for-all): what to follow. Empty with a bout: the two boxers' heads.")]
        [SerializeField] Transform[] targets = new Transform[0];

        CinemachineSplineDolly _dolly;
        float _t, _dir = 1f;

        /// <summary>Where the camera is on its rail, 0..1.</summary>
        public float Position => _t;

        void Awake()
        {
            _dolly = GetComponent<CinemachineSplineDolly>();
            _dolly.PositionUnits = PathIndexUnit.Normalized;
            // This drives the position; Cinemachine's own automatic dolly would fight it.
            _dolly.AutomaticDolly.Enabled = false;
        }

        /// <summary>Starts a sweep from whichever end of the rail the camera is nearer.</summary>
        public void Restart()
        {
            _t = _t > 0.5f ? 1f : 0f;
            _dir = _t > 0.5f ? -1f : 1f;
            if (_dolly != null) _dolly.CameraPosition = Ease(_t);
        }

        static float Ease(float t) => t * t * (3f - 2f * t);

        void LateUpdate()
        {
            if (_dolly == null || _dolly.Spline == null) return;
            float dt = Time.unscaledDeltaTime;
            if (mode == Mode.Sweep)
            {
                _t += _dir * dt / Mathf.Max(0.5f, sweepSeconds);
                if (_t >= 1f) { _t = 1f; _dir = -1f; }
                else if (_t <= 0f) { _t = 0f; _dir = 1f; }
                _dolly.CameraPosition = Ease(_t);
                return;
            }

            if (!Middle(out Vector3 middle)) return;
            SplineContainer rail = _dolly.Spline;
            float3 local = rail.transform.InverseTransformPoint(middle);
            SplineUtility.GetNearestPoint(rail.Spline, local, out float3 _, out float nearest, 4, 2);
            float want = Mathf.Clamp01(nearest + lead);
            _t = Mathf.MoveTowards(_t, want, followSpeed * dt);
            _dolly.CameraPosition = _t;
        }

        bool Middle(out Vector3 middle)
        {
            middle = Vector3.zero;
            int n = 0;
            Bout bout = Bout.Instance;
            if (targets.Length == 0 && bout != null)
            {
                if (bout.red != null) { middle += bout.red.HeadPosition; n++; }
                if (bout.blue != null) { middle += bout.blue.HeadPosition; n++; }
            }
            for (int i = 0; i < targets.Length; i++)
                if (targets[i] != null) { middle += targets[i].position; n++; }
            if (n == 0) return false;
            middle /= n;
            return true;
        }
    }
}
