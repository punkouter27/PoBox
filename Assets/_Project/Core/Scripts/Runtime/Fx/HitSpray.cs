using UnityEngine;
using PoBox.Sim;

namespace PoBox.Fx
{
    /// <summary>
    /// What flies off a boxer besides the sweat <see cref="ImpactVfx"/> throws off the glove's path: keyed
    /// off <see cref="SimBus.Hit"/>'s glove speed and the part it landed on, and off a knockdown.
    ///
    /// <list type="bullet">
    /// <item>Spit: a hard clean punch to the head knocks a few heavy drops out of the mouth, carried the way the
    /// glove was going and arcing down. Only over <see cref="spitSpeed"/>: a jab does not do it.</item>
    /// <item>Body mist: a clean punch to the body throws a fine, slow spray off the skin, the faster the glove the
    /// more of it, round the point of contact rather than along the glove.</item>
    /// <item>Knockdown dust: the first heavy landing after a knockdown blows a ring of canvas dust out flat from
    /// where the body came down, as wide as the landing was hard.</item>
    /// </list>
    ///
    /// The three are built-in Particle Systems in the scene (which works on WebGL and Android alike; no VFX
    /// Graph, which needs compute shaders the WebGL build does not have), with emission off: this only calls
    /// <see cref="ParticleSystem.Emit(ParticleSystem.EmitParams, int)"/>, so the particle system's own buffer is
    /// the pool and nothing is allocated per hit. Their look (material, size, colour over life, gravity) is the
    /// inspector's; <see cref="CreateDefaultEmitters"/> makes a starting set as children while authoring.
    /// </summary>
    public class HitSpray : MonoBehaviour
    {
        [Tooltip("Heavy drops out of the mouth on a hard head punch (stretched billboards, gravity about 1).")]
        [SerializeField] ParticleSystem spit;
        [Tooltip("Fine spray off the skin on a clean body punch (small soft billboards, little gravity).")]
        [SerializeField] ParticleSystem bodyMist;
        [Tooltip("The ring of canvas dust where a knocked-down body lands (soft billboards, no gravity, slowing fast).")]
        [SerializeField] ParticleSystem knockdownDust;

        [Tooltip("Only for Create Default Emitters (authoring): drops (Fx_Sweat) and dust (Fx_Dust).")]
        [SerializeField] Material dropMaterial, dustMaterial;

        [Header("Spit")]
        [Tooltip("Glove speed, m/s, under which a head punch knocks no spit out, and at which the most comes.")]
        [SerializeField] float spitSpeed = 6f, fullSpitSpeed = 10f;
        [Tooltip("Drops at the threshold and at full speed.")]
        [SerializeField] Vector2Int spitCount = new Vector2Int(2, 9);
        [Tooltip("From the middle of the head to the mouth: down, and forward along the way the boxer faces, metres.")]
        [SerializeField] float mouthDown = 0.07f, mouthForward = 0.09f;

        [Header("Body mist")]
        [Tooltip("Glove speed, m/s, that throws the most mist off a body shot.")]
        [SerializeField] float fullMistSpeed = 9f;
        [Tooltip("Drops for the softest and the hardest clean body shot.")]
        [SerializeField] Vector2Int mistCount = new Vector2Int(3, 18);

        [Header("Knockdown dust")]
        [Tooltip("Seconds after a knockdown in which a floor impact is the body landing.")]
        [SerializeField] float landingWindow = 1.5f;
        [Tooltip("Floor impulse, N s, that counts as the landing, and the one that raises the most dust.")]
        [SerializeField] float landingImpulse = 25f, fullLanding = 120f;
        [Tooltip("Puffs in the ring for the lightest and the heaviest landing.")]
        [SerializeField] Vector2Int dustCount = new Vector2Int(10, 36);
        [Tooltip("Speed the ring spreads at, m/s, for the lightest and the heaviest landing.")]
        [SerializeField] Vector2 dustSpeed = new Vector2(0.6f, 2.2f);
        [Tooltip("Height of the canvas.")]
        [SerializeField] float canvasY = 0f;

        Fighter _downed;
        float _downedAt = -10f;

        void OnEnable()
        {
            SimBus.Hit += OnHit;
            SimBus.Knockdown += OnKnockdown;
            SimBus.FloorImpact += OnFloor;
        }

        void OnDisable()
        {
            SimBus.Hit -= OnHit;
            SimBus.Knockdown -= OnKnockdown;
            SimBus.FloorImpact -= OnFloor;
            _downed = null;
        }

        void OnHit(HitEvent e)
        {
            if (!e.clean || e.victim == null) return;
            Vector3 normal = e.normal.sqrMagnitude > 0.01f ? e.normal.normalized : Vector3.up;
            Vector3 along = e.velocity.sqrMagnitude > 0.25f ? e.velocity.normalized : -normal;
            if (e.zone == PartKind.Head) Spit(e, along);
            else if (e.zone == PartKind.Torso || e.zone == PartKind.Pelvis) Mist(e, normal, along);
        }

        void Spit(in HitEvent e, Vector3 along)
        {
            if (spit == null || e.gloveSpeed < spitSpeed) return;
            float k = Mathf.InverseLerp(spitSpeed, fullSpitSpeed, e.gloveSpeed);
            Mj.MjBoxer boxer = e.victim.boxer;
            Vector3 facing = boxer != null && boxer.Bound ? boxer.Forward : e.victim.transform.forward;
            Vector3 mouth = e.victim.HeadPosition + facing * mouthForward + Vector3.down * mouthDown;
            // Out of the mouth the way the head is knocked, lifted a little so it arcs.
            Vector3 dir = (along * 0.8f + Vector3.up * 0.3f).normalized;
            float speed = Mathf.Lerp(1.8f, 4.5f, k);
            int n = Mathf.RoundToInt(Mathf.Lerp(spitCount.x, spitCount.y, k));
            for (int i = 0; i < n; i++)
            {
                var drop = new ParticleSystem.EmitParams
                {
                    position = mouth + Random.insideUnitSphere * 0.015f,
                    velocity = (dir + Random.insideUnitSphere * 0.3f).normalized * speed * Random.Range(0.6f, 1f) + e.victim.HeadVelocity * 0.5f,
                };
                spit.Emit(drop, 1);
            }
        }

        void Mist(in HitEvent e, Vector3 normal, Vector3 along)
        {
            if (bodyMist == null) return;
            float k = Mathf.Clamp01(e.gloveSpeed / Mathf.Max(0.1f, fullMistSpeed));
            int n = Mathf.RoundToInt(Mathf.Lerp(mistCount.x, mistCount.y, k));
            // Round the contact: out of the surface and sideways off the glove, not carried along it.
            Vector3 side = Vector3.ProjectOnPlane(along, normal);
            for (int i = 0; i < n; i++)
            {
                Vector3 spread = Random.insideUnitSphere;
                var drop = new ParticleSystem.EmitParams
                {
                    position = e.point + normal * 0.02f + spread * 0.04f,
                    velocity = (normal * 0.8f + side * 0.4f + spread * 0.7f) * Mathf.Lerp(0.6f, 2f, k),
                    startSize = Random.Range(0.012f, 0.03f),
                };
                bodyMist.Emit(drop, 1);
            }
        }

        void OnKnockdown(Fighter f, HitEvent cause)
        {
            _downed = f;
            _downedAt = Time.time;
        }

        void OnFloor(Vector3 point, float impulse)
        {
            if (_downed == null || knockdownDust == null || impulse < landingImpulse) return;
            if (Time.time - _downedAt > landingWindow) { _downed = null; return; }
            _downed = null;          // one ring a knockdown: the first heavy landing
            float k = Mathf.Clamp01(impulse / Mathf.Max(1f, fullLanding));
            int n = Mathf.RoundToInt(Mathf.Lerp(dustCount.x, dustCount.y, k));
            float speed = Mathf.Lerp(dustSpeed.x, dustSpeed.y, k);
            float turn = Random.Range(0f, Mathf.PI * 2f);
            for (int i = 0; i < n; i++)
            {
                float a = turn + i * Mathf.PI * 2f / n + Random.Range(-0.15f, 0.15f);
                var out01 = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                var puff = new ParticleSystem.EmitParams
                {
                    position = new Vector3(point.x, canvasY + 0.03f, point.z) + out01 * 0.12f,
                    velocity = out01 * speed * Random.Range(0.7f, 1.1f) + Vector3.up * Random.Range(0.05f, 0.35f),
                    startSize = Random.Range(0.18f, 0.36f) * Mathf.Lerp(0.8f, 1.3f, k),
                    startLifetime = Random.Range(0.7f, 1.3f),
                };
                knockdownDust.Emit(puff, 1);
            }
        }

        /// <summary>
        /// Authoring help (component menu): makes the three emitters as children with a sensible starting
        /// look, using the materials given, for the scene to be saved with. Not called at run time.
        /// </summary>
        [ContextMenu("Create Default Emitters")]
        void CreateDefaultEmitters()
        {
            if (spit == null) spit = MakeEmitter("Spit", 0.9f, 0.035f, 1f, new Color(0.92f, 0.95f, 1f, 0.85f), true);
            if (bodyMist == null) bodyMist = MakeEmitter("BodyMist", 0.5f, 0.02f, 0.35f, new Color(0.9f, 0.94f, 1f, 0.55f), false);
            if (knockdownDust == null) knockdownDust = MakeEmitter("KnockdownDust", 1.1f, 0.25f, -0.02f, new Color(0.85f, 0.82f, 0.76f, 0.35f), false);
            if (knockdownDust != null)
            {
                ParticleSystem.LimitVelocityOverLifetimeModule drag = knockdownDust.limitVelocityOverLifetime;
                drag.enabled = true;
                drag.drag = 2.5f;
                ParticleSystem.SizeOverLifetimeModule grow = knockdownDust.sizeOverLifetime;
                grow.enabled = true;
                grow.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.6f));
            }
        }

        ParticleSystem MakeEmitter(string label, float life, float size, float gravity, Color colour, bool stretch)
        {
            var go = new GameObject(label);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.startLifetime = life;
            main.startSpeed = 0f;
            main.startSize = size;
            main.startColor = colour;
            main.gravityModifier = gravity;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 96;
            ParticleSystem.EmissionModule emission = ps.emission;
            emission.enabled = false;
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = false;
            ParticleSystem.ColorOverLifetimeModule fade = ps.colorOverLifetime;
            fade.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.6f), new GradientAlphaKey(0f, 1f) });
            fade.color = g;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = stretch ? ParticleSystemRenderMode.Stretch : ParticleSystemRenderMode.Billboard;
            renderer.velocityScale = stretch ? 0.04f : 0f;
            renderer.lengthScale = stretch ? 1.2f : 2f;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sharedMaterial = stretch || label == "BodyMist" ? dropMaterial : dustMaterial;
            return ps;
        }
    }
}
