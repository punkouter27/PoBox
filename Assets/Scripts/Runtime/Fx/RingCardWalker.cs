using UnityEngine;
using PoBox.Mj;
using PoBox.Sim;

namespace PoBox.Fx
{
    /// <summary>
    /// The ring-card walker: a body on the MuJoCo plugin with a footwork brain of her own (<see cref="MjCrew"/>,
    /// training/models/crew/cardwalker, trained to walk with her right arm up, the card in her hand beside her head).
    /// In the break before a round she is stood at one side of the ring and walks across it through the middle, the
    /// round coming up on the card (Assets/Art/Exports/Textures/T_RoundCard_N.png on its M_Card face). The rest of the
    /// time she waits out of sight, well away from the ring on MuJoCo's floor, not drawn. On a collision layer of her own.
    /// </summary>
    public class RingCardWalker : MonoBehaviour
    {
        public MjCrew crew;
        [Tooltip("The card faces for rounds 2, 3, ... in order (index 0 is round 2).")]
        public Texture2D[] cards = new Texture2D[0];
        [Tooltip("Seconds into the break she sets off, and her walking speed, m/s.")]
        public float startAfter = 1.5f, speed = 0.8f;
        [Tooltip("Where she waits off duty, metres from the ring's middle (Unity x, z): out of sight, on MuJoCo's floor plane.")]
        public Vector2 offDuty = new Vector2(0f, 12f);

        Renderer[] _renderers;
        Material _face;
        bool _shown = true, _walking, _parked;
        int _round = -1;

        void Start()
        {
            _renderers = GetComponentsInChildren<Renderer>(true);
            foreach (Renderer r in _renderers)
                foreach (Material m in r.materials)
                    if (m.name.StartsWith("M_Card") && !m.name.StartsWith("M_CardBack")) _face = m;
            Show(false);
        }

        void FixedUpdate()
        {
            Bout bout = Bout.Instance;
            if (crew == null || !crew.Ready || bout == null || bout.red == null || bout.red.ring == null) return;
            Vector2 c = bout.red.ring.centre;
            float half = bout.red.ring.ringHalf - 0.7f;
            // From one side of the ring to the other through the middle, between the corners (the referee waits in a neutral one).
            Vector3 from = new Vector3(c.x - half, 0f, c.y + 0.3f), to = new Vector3(c.x + half, 0f, c.y - 0.3f);
            Vector3 park = new Vector3(c.x + offDuty.x, 0f, c.y + offDuty.y);
            if (!_parked) { crew.Place(park, Vector3.forward); _parked = true; }
            int next = bout.Round + 1;
            bool due = bout.Phase == BoutPhase.RoundBreak && next <= bout.rounds && bout.PhaseAge > startAfter;

            if (due && !_walking && _round != next)
            {
                // On: stood at the start, facing across, the card for the next round up.
                _round = next;
                SetCard(next);
                crew.Place(from, to - from);
                _walking = true;
                Show(true);
            }
            if (_walking)
            {
                bool there = crew.GoTo(to, to + (to - from), speed, 0.4f);
                if (there || bout.Phase != BoutPhase.RoundBreak)
                {
                    // Off: back where nobody sees her.
                    _walking = false;
                    crew.Place(park, Vector3.forward);
                    Show(false);
                }
            }
            else if (bout.Phase != BoutPhase.RoundBreak) _round = -1;
        }

        void SetCard(int round)
        {
            int i = round - 2;
            if (_face == null || i < 0 || i >= cards.Length || cards[i] == null) return;
            _face.mainTexture = cards[i];
            if (_face.HasProperty("_BaseMap")) _face.SetTexture("_BaseMap", cards[i]);
            if (_face.HasProperty("_BaseColor")) _face.SetColor("_BaseColor", Color.white);
        }

        void Show(bool on)
        {
            if (on == _shown || _renderers == null) return;
            _shown = on;
            foreach (Renderer r in _renderers) r.enabled = on;
        }
    }
}
