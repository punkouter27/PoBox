using UnityEngine;
using PoBox.Sim;
using PoBox.UI;

namespace PoBox.Fx
{
    /// <summary>
    /// What a boxer means to do next, drawn in the ring, for the settings' INTENT (VIEW tab).
    ///
    /// On the canvas, an arrow from under its centre of mass along the way the body is travelling, as long as where it
    /// will be in <see cref="lookAhead"/> seconds at this speed: where it is going, before it gets there. And when its
    /// brain is driving one arm hard (the same reading as the brain cam's LOADING: the largest action among that arm's
    /// joints over <see cref="loadAt"/>), a line from that shoulder to the other boxer's head, brighter the harder:
    /// the punch it is getting ready to throw. Both read the physics and the policy's own output; nothing here
    /// steers anything. Nothing is drawn for a boxer that is down. One of these a corner, its lines in the scene.
    /// </summary>
    public class IntentArrows : MonoBehaviour
    {
        public static bool On;

        public Bout bout;
        [Tooltip("0 red corner, 1 blue corner.")]
        public int corner;
        [Tooltip("The way it is going: tail, tip and the two barbs of the head (5 points), lying flat on the canvas.")]
        public LineRenderer step;
        [Tooltip("The punch it is loading: shoulder to the other's head (2 points).")]
        public LineRenderer punch;
        [Tooltip("Height the arrow is drawn at: just over the canvas and the balance marks.")]
        public float y = 0.03f;
        [Tooltip("Seconds ahead the arrow reaches at the current speed.")]
        public float lookAhead = 0.6f;
        [Tooltip("Slower than this (m/s) and no arrow: a boxer settling on its feet is not going anywhere.")]
        public float minSpeed = 0.15f;
        [Tooltip("An arm whose largest action is over this is loading a punch.")]
        public float loadAt = 0.6f;
        public Color colour = Color.white;

        Vector3 _vel;
        int[] _group = new int[0];

        void LateUpdate()
        {
            Fighter f = bout == null ? null : corner == 1 ? bout.blue : bout.red;
            bool show = On && f != null && Bout.SimRunning && !f.IsDown;
            if (!show) { Hide(); return; }

            // Smoothed, so the arrow shows the way the body is going and not every wobble of a step.
            Vector3 v = f.CenterOfMassVelocity;
            v.y = 0f;
            _vel = Vector3.Lerp(_vel, v, 1f - Mathf.Exp(-Time.deltaTime * 6f));
            float speed = _vel.magnitude;
            if (speed > minSpeed)
            {
                Vector3 tail = new Vector3(f.CenterOfMass.x, y, f.CenterOfMass.z);
                Vector3 dir = _vel / speed, tip = tail + _vel * lookAhead;
                Vector3 back = -dir * Mathf.Min(0.15f, speed * lookAhead * 0.4f), side = new Vector3(-dir.z, 0f, dir.x) * 0.08f;
                step.enabled = true;
                step.positionCount = 5;
                step.SetPosition(0, tail);
                step.SetPosition(1, tip);
                step.SetPosition(2, tip + back + side);
                step.SetPosition(3, tip);
                step.SetPosition(4, tip + back - side);
                step.startColor = step.endColor = colour;
            }
            else step.enabled = false;

            float left = 0f, right = 0f;
            Mj.MjBoxer body = f.boxer;
            if (body != null && body.Bound && body.Action != null)
            {
                if (_group.Length != body.Action.Length)
                {
                    string[] joints = body.Config.joint_order;
                    _group = new int[body.Action.Length];
                    for (int j = 0; j < _group.Length; j++) _group[j] = HudView.GroupOf(j < joints.Length ? joints[j] : "");
                }
                for (int j = 0; j < _group.Length; j++)
                {
                    float a = Mathf.Abs((float)body.Action[j]);
                    if (_group[j] == 2) left = Mathf.Max(left, a);
                    else if (_group[j] == 3) right = Mathf.Max(right, a);
                }
            }
            float load = Mathf.Max(left, right);
            Fighter other = bout.Other(f);
            BodyPart shoulder = left >= right ? f.upperArmL : f.upperArmR;
            if (load > loadAt && shoulder != null && other != null && other.head != null)
            {
                float k = Mathf.InverseLerp(loadAt, 1f, load);
                punch.enabled = true;
                punch.positionCount = 2;
                punch.SetPosition(0, shoulder.transform.position);
                punch.SetPosition(1, other.head.transform.position);
                Color c = colour;
                c.a = 0.25f + 0.6f * k;
                punch.startColor = c;
                c.a *= 0.3f;
                punch.endColor = c;
            }
            else punch.enabled = false;
        }

        void Hide()
        {
            if (step != null) step.enabled = false;
            if (punch != null) punch.enabled = false;
        }

        void OnDisable() => Hide();
    }
}
