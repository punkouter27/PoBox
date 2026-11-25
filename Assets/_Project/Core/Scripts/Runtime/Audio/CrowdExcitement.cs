using UnityEngine;
using PoBox.Sim;

namespace PoBox.Audio
{
    /// <summary>
    /// How excited the hall is, 0..1 (2026-11-25, the reactive crowd), from what it can see happen: the punch rate
    /// over the last few seconds (thrown and, more, landed), big hits (well over what these two usually land), a
    /// boxer out on his feet, knockdowns and getting up, a count, the bell at either end of a round and the
    /// result. Up quickly, down slowly. The camera's <see cref="Excitement"/> is a different reading (it is
    /// deliberately blind to the usual trade of punches); this one is what a crowd does.
    ///
    /// <see cref="AudioDirector"/> hears it (the bed's level, a roar layer, a swell as it peaks) and the crowd
    /// shader sees it: every frame it sets the global _PoBoxCrowdExcite (Crowd.shader scales the figures' sway,
    /// bounce and jump by it; 0 leaves the shader as it was). Tunables in <see cref="CrowdExcitementSettings"/>.
    /// No allocation per frame: the punch window is a fixed ring of times.
    /// </summary>
    public sealed class CrowdExcitement : MonoBehaviour
    {
        public static CrowdExcitement Instance { get; private set; }
        public static readonly int ExciteId = Shader.PropertyToID("_PoBoxCrowdExcite");

        [SerializeField] CrowdExcitementSettings settings;
        [Tooltip("Empty: Bout.Instance.")]
        [SerializeField] Bout bout;

        /// <summary>The excitement, 0..1.</summary>
        public float Value { get; private set; }
        /// <summary>How fast it is rising, 0..1 a second against its own attack (2026-11-25): the bed lifts on a surge.</summary>
        public float Surge { get; private set; }
        /// <summary>Punches a second over the window (landed ones weighted).</summary>
        public float Rate { get; private set; }
        /// <summary>The bed's share (0..1 between quiet and loud) and the roar layer's level (0..1), from the curves.</summary>
        public float Bed => _s.bed.Evaluate(Value);
        public float Roar => _s.roar.Evaluate(Value);
        public float Motion { get; private set; }

        const int Ring = 128;
        readonly float[] _at = new float[Ring], _weight = new float[Ring];
        int _head;
        float _spike, _lastSwell = -99f;
        bool _swell, _wasAbove;
        Fighter[] _fighters = new Fighter[0];
        CrowdExcitementSettings _s;

        void Awake()
        {
            Instance = this;
            _s = settings != null ? settings : ScriptableObject.CreateInstance<CrowdExcitementSettings>();
            for (int i = 0; i < Ring; i++) _at[i] = -999f;
        }

        void OnEnable()
        {
            SimBus.PunchThrown += OnPunch;
            SimBus.Hit += OnHit;
            SimBus.Knockdown += OnKnockdown;
            SimBus.GotUp += OnGotUp;
            SimBus.PhaseChanged += OnPhase;
        }

        void OnDisable()
        {
            SimBus.PunchThrown -= OnPunch;
            SimBus.Hit -= OnHit;
            SimBus.Knockdown -= OnKnockdown;
            SimBus.GotUp -= OnGotUp;
            SimBus.PhaseChanged -= OnPhase;
            Shader.SetGlobalFloat(ExciteId, 0f);
            if (Instance == this) Instance = null;
        }

        void Start() => _fighters = FindObjectsByType<Fighter>(FindObjectsInactive.Exclude);

        /// <summary>True once each time the hall boils over (crossing <see cref="CrowdExcitementSettings.swellAt"/>).</summary>
        public bool TakeSwell()
        {
            bool s = _swell;
            _swell = false;
            return s;
        }

        void Count(float weight)
        {
            _at[_head] = Time.unscaledTime;
            _weight[_head] = weight;
            _head = (_head + 1) % Ring;
        }

        void OnPunch(Fighter f, PunchType type, int hand) => Count(1f);

        void OnHit(HitEvent e)
        {
            if (!e.clean) return;
            Count(_s.landedWeight - 1f);   // the throw was counted already
            float usual = Excitement.Instance != null ? Excitement.Instance.UsualImpulse * _s.bigHitOverUsual : _s.bigHitImpulse;
            float over = Mathf.Clamp01((e.impulse - usual) / Mathf.Max(1f, usual));
            if (over > 0f) _spike = Mathf.Max(_spike, Mathf.Min(1.5f, _spike + _s.bigHitSpike * over));
        }

        void OnKnockdown(Fighter f, HitEvent cause) => _spike = Mathf.Max(_spike, _s.knockdownSpike);
        void OnGotUp(Fighter f) => _spike = Mathf.Max(_spike, _s.gotUpSpike);

        void OnPhase(BoutPhase from, BoutPhase to)
        {
            if (to == BoutPhase.Fight && from == BoutPhase.Intro) _spike = Mathf.Max(_spike, _s.roundStartSpike);
            else if (to == BoutPhase.RoundBreak) _spike = Mathf.Max(_spike, _s.roundEndSpike);
            else if (to == BoutPhase.Results) _spike = Mathf.Max(_spike, _s.resultSpike);
            else if (to == BoutPhase.WalkOn)
            {
                _fighters = FindObjectsByType<Fighter>(FindObjectsInactive.Exclude);
                for (int i = 0; i < Ring; i++) _at[i] = -999f;
                _spike = 0f;
            }
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime, now = Time.unscaledTime;
            Bout b = bout != null ? bout : Bout.Instance;
            BoutPhase phase = b != null ? b.Phase : BoutPhase.Fight;

            // Punch rate over the window.
            float window = Mathf.Max(0.5f, _s.windowSeconds), sum = 0f;
            for (int i = 0; i < Ring; i++) if (now - _at[i] <= window) sum += _weight[i];
            Rate = sum / window;
            float rate = Mathf.Clamp01(Rate / Mathf.Max(0.1f, _s.fullRate));

            // The worse-off boxer still on his feet.
            float daze = 0f;
            for (int i = 0; i < _fighters.Length; i++)
            {
                Fighter f = _fighters[i];
                if (f == null || !f.isActiveAndEnabled || f.IsDown) continue;
                daze = Mathf.Max(daze, f.Daze01);
            }
            daze = Mathf.Clamp01((daze - _s.dazeFrom) / Mathf.Max(0.05f, 1f - _s.dazeFrom));

            _spike *= Mathf.Exp(-dt / Mathf.Max(0.1f, _s.spikeDecaySeconds));
            float target = phase == BoutPhase.Fight || phase == BoutPhase.Count
                ? _s.idleLevel + _s.rateWeight * rate + _s.dazeWeight * daze * daze + _spike
                : _s.idleLevel + _spike;
            if (phase == BoutPhase.Count) target = Mathf.Max(target, _s.countFloor);
            target = Mathf.Clamp01(target);
            float speed = target > Value ? _s.attackPerSecond : _s.releasePerSecond;
            float previous = Value;
            Value += (target - Value) * Mathf.Clamp01(dt * speed);
            // How fast it is rising, against its own attack: a knockdown lifts the bed at once, a plateau does not.
            Surge = Mathf.Clamp01((Value - previous) / Mathf.Max(1e-4f, dt) / Mathf.Max(0.5f, _s.attackPerSecond));

            bool above = Value >= _s.swellAt;
            if (above && !_wasAbove && now - _lastSwell > _s.swellCooldown) { _swell = true; _lastSwell = now; }
            _wasAbove = above;

            Motion = Mathf.Clamp01(_s.motion.Evaluate(Value));
            Shader.SetGlobalFloat(ExciteId, Motion);
        }
    }
}
