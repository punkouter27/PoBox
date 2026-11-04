using UnityEngine;
using PoBox.Sim;

namespace PoBox.Fx
{
    /// <summary>
    /// A boxer's cornerman (SK_Cornerman), in the corner's colour. Scenery: he climbs in only for the break, once his
    /// boxer has had a moment on the stool, and stands over him at the stool's side, bent forward, working a towel.
    /// Out of the ring the rest of the time.
    /// </summary>
    public class Cornerman : MonoBehaviour
    {
        [Tooltip("Red corner (true) or blue.")]
        public bool red = true;
        public Color redShirt = new Color(0.78f, 0.12f, 0.12f), blueShirt = new Color(0.14f, 0.32f, 0.82f);
        [Tooltip("Seconds into the break before he is in.")]
        public float inAfter = 1.2f;
        [Tooltip("Metres to the stool's side, and out from the ring's middle, from the stool (negative: in towards the middle).")]
        public float aside = 0.6f, behind = -0.35f;

        FigureRig _rig;
        Renderer[] _renderers;
        bool _shown = true;

        void Start()
        {
            _rig = new FigureRig(transform);
            _renderers = GetComponentsInChildren<Renderer>(true);
            Color shirt = red ? redShirt : blueShirt;
            foreach (Renderer r in _renderers)
                foreach (Material m in r.materials)
                    if (m.name.StartsWith("M_Team")) { m.color = shirt; if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", shirt); }
            Show(false);
        }

        void LateUpdate()
        {
            Bout bout = Bout.Instance;
            if (_rig == null || bout == null || bout.red == null || bout.red.ring == null) { Show(false); return; }
            Transform seat = red ? (bout.redSeat != null ? bout.redSeat : bout.redStool) : (bout.blueSeat != null ? bout.blueSeat : bout.blueStool);
            bool on = seat != null && bout.Phase == BoutPhase.RoundBreak && bout.PhaseAge > inAfter;
            Show(on);
            if (!on) return;

            Vector2 c = bout.red.ring.centre;
            Vector3 centre = new Vector3(c.x, seat.position.y, c.y);
            Vector3 toMiddle = (centre - seat.position).normalized;
            Vector3 side = Vector3.Cross(Vector3.up, toMiddle);
            transform.position = seat.position + side * aside - toMiddle * behind;
            Vector3 look = seat.position + toMiddle * 0.2f - transform.position;
            look.y = 0f;
            if (look.sqrMagnitude > 1e-4f) transform.rotation = Quaternion.LookRotation(look);

            // Bent over his boxer, one hand working the towel up and down by the face, the other on the shoulder.
            float t = Time.time * 5f;
            _rig.Rest();
            _rig.Turn("Spine", Vector3.right, 22f);
            _rig.Turn("Chest", Vector3.right, 10f);
            _rig.Turn("Thigh.L", Vector3.right, 12f);
            _rig.Turn("Thigh.R", Vector3.right, 12f);
            _rig.Turn("Shin.L", Vector3.right, -20f);
            _rig.Turn("Shin.R", Vector3.right, -20f);
            _rig.Turn("UpperArm.R", Vector3.forward, 60f + 12f * Mathf.Sin(t));
            _rig.Turn("ForeArm.R", Vector3.forward, 50f + 20f * Mathf.Sin(t + 0.6f));
            _rig.Turn("UpperArm.L", Vector3.forward, -30f);
            _rig.Turn("ForeArm.L", Vector3.forward, -40f);
        }

        void Show(bool on)
        {
            if (on == _shown || _renderers == null) return;
            _shown = on;
            foreach (Renderer r in _renderers) r.enabled = on;
        }
    }
}
