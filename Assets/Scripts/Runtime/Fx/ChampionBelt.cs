using UnityEngine;
using PoBox.Sim;

namespace PoBox.Fx
{
    /// <summary>
    /// The championship belt (SM_ChampionBelt): round the waist of whichever of the two in the ring is top of the
    /// ladder, so the champion walks out in it and fights in it, and a boxer who takes the top spot is wearing it at the
    /// result. Scenery on the pelvis: it moves with the body and is sized to it (taller boxers, and heavier ones for
    /// their height, get a bigger loop). Hidden when neither is the champion, and outside a bout.
    /// </summary>
    public class ChampionBelt : MonoBehaviour
    {
        [Tooltip("Metres from the pelvis's centre up to the belt's.")]
        public float rise = 0.06f;
        [Tooltip("The loop's size against a 75 kg boxer with a 0.92 m standing pelvis: more on bigger boxers.")]
        public float girth = 1.0f;

        Renderer[] _renderers;
        Fighter _on;

        void Start() => _renderers = GetComponentsInChildren<Renderer>(true);

        void LateUpdate()
        {
            Bout bout = Bout.Instance;
            Fighter champ = null;
            if (bout != null && bout.league != null && bout.red != null && bout.blue != null)
            {
                if (bout.league.RankOf(bout.red.displayName) == 1) champ = bout.red;
                else if (bout.league.RankOf(bout.blue.displayName) == 1) champ = bout.blue;
            }
            if (champ != _on)
            {
                _on = champ;
                if (_renderers != null) foreach (Renderer r in _renderers) r.enabled = champ != null;
                if (champ != null) Fit(champ);
            }
            if (champ == null || champ.pelvis == null) return;
            // The pelvis's own frame: MuJoCo's forward (its x) is the transform's right, its up the transform's up.
            Transform p = champ.pelvis.transform;
            transform.SetPositionAndRotation(p.position + p.up * rise, Quaternion.LookRotation(p.right, p.up));
        }

        void Fit(Fighter f)
        {
            float tall = Mathf.Max(0.5f, f.standingPelvisHeight) / 0.92f;
            float mass = f.boxer != null ? (float)f.boxer.Mass : 75f;
            // Girth grows with weight for the height: the square root of the body-mass index against an average one.
            float round = Mathf.Sqrt(Mathf.Clamp(mass / 75f / (tall * tall), 0.6f, 2.2f));
            transform.localScale = new Vector3(tall * round, tall, tall * round) * girth;
        }
    }
}
