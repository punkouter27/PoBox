using UnityEngine;
using UnityEngine.Audio;
using PoBox.Broadcast;
using PoBox.Fx;
using PoBox.Mj;
using PoBox.Sim;

namespace PoBox.Audio
{
    /// <summary>
    /// The sound of the fight, placed where it happens, on three buses: hits, crowd and ring.
    ///
    /// A punch is built from layers, each a 3D one-shot at the contact point and each set from what was
    /// measured. The slap of leather is always there, louder with the impulse and pitched up with the speed
    /// the glove arrived at. Under it, from about 6 N s, the thud of the body that took it, which grows
    /// faster than the slap and is pitched down as it gets heavier, so a hard shot is lower and longer and
    /// not just louder. A punch stopped on the gloves has a duller slap and no thud. And a punch harder than
    /// these two usually land draws a gasp from the crowd, a fraction of a second after it.
    ///
    /// A boot dragged or turned on the canvas scrapes: a loop of noise at each boxer's feet, made here, whose
    /// level and pitch follow how fast MuJoCo has the planted foot sliding. A planted foot turned fast (a pivot,
    /// MjRing.FootSpin) squeaks as well: a short rubbery chirp, made here, louder and higher the faster the turn.
    ///
    /// The air a glove moves is heard all the way through a punch, landed or missed: a loop of breathy noise at
    /// each glove, silent under <see cref="whooshSpeed"/>.x and full at .y, rising in pitch with the speed
    /// MuJoCo measures (MjRing.GloveVelocity). Pitch from the speed, not from Doppler, which a cut would jump.
    ///
    /// A punch stopped on the gloves is a leather pop at the gloves, with a crack over it that comes up with
    /// the speed it was coming at. The ropes creak as they are drawn (<see cref="RopeFlex"/>, which bows them
    /// from MuJoCo's rope load, every boxer's): louder and higher the further out the most bowed one is, from
    /// where it is. A boxer that arrives on them hard twangs them.
    ///
    /// The crowd is heard from round the hall: half of the bed as one diffuse layer, the other half from eight
    /// sections on a circle round the ring (3D, the same loop at eight places in it), so it stays where it is
    /// when the camera cuts. The sections behind the corner that just scored (ArenaMood.Side) come up, the other
    /// half goes back a little. The crowd is a looping bed whose level rides the excitement reading, with a swell laid over it on a
    /// big moment and a groan on a knockdown. So that a heavy punch is heard through it, the crowd bus is
    /// ducked for the quarter second the punch lasts and comes back over the next half.
    ///
    /// The mix follows the camera: a close shot brings the gloves and the breathing up and the crowd back, a
    /// wide one the reverse, crossfaded over a third of a second after the cut. When the boxer on camera is
    /// rocked the whole mix is muffled and a heartbeat comes in, both draining with the daze. The phone's
    /// voice (<see cref="SpeechCommentary"/>) ducks the crowd while it talks, and so does the bell as it rings.
    ///
    /// The one-shots are 3D with a linear fall-off from 3 m to 30 m (the camera is never further than that, so
    /// nothing drops out, and a close shot is louder than a wide one) and no Doppler: a cut would jump the
    /// pitch. The hall's reverb is an AudioReverbZone over the ring (PoBox/Do All/Graphics), Unity's own;
    /// Steam Audio and the like are native plugins that each need their Android build checked, for little a
    /// small ring would gain.
    ///
    /// The buses are groups of an AudioMixer when there is one (Assets/Audio/PoBox.mixer, where their levels
    /// can be set by hand), and otherwise plain gains here. <see cref="AudioMeter"/> on the listener
    /// measures and limits the sum.
    /// </summary>
    public partial class AudioDirector : MonoBehaviour
    {
        public Bout bout;
        public AudioListener listener;

        [Header("Hits")]
        [Tooltip("The slap of a glove: a clean punch.")]
        public AudioClip[] punchLight = new AudioClip[0];
        [Tooltip("The slap of a hard one.")]
        public AudioClip[] punchHeavy = new AudioClip[0];
        [Tooltip("A punch stopped on the gloves or arms.")]
        public AudioClip[] punchBlocked = new AudioClip[0];
        [Tooltip("The thud of the body that took it, laid under the slap.")]
        public AudioClip[] bodyThud = new AudioClip[0];
        [Tooltip("A glove faster than the first, m/s, is heard moving the air; at the second it is as loud as it gets.")]
        public Vector2 whooshSpeed = new Vector2(3.5f, 9f);
        [Range(0f, 1f)] public float whooshVolume = 0.35f;
        [Tooltip("A planted foot turning faster than this, rad/s, squeaks (louder up to three times it).")]
        public float squeakSpin = 4f;
        [Range(0f, 1f)] public float squeakVolume = 0.35f;
        public AudioClip bodyFall;
        public AudioClip[] footsteps = new AudioClip[0];
        [Tooltip("A planted foot sliding faster than the first, m/s, is heard; at the second it is as loud as it gets.")]
        public Vector2 scrapeSpeed = new Vector2(0.12f, 1.2f);
        [Range(0f, 1f)] public float scrapeVolume = 0.3f;
        [Tooltip("Impulse at which the heavy samples take over, N s.")]
        public float heavyImpulse = 14f;
        [Tooltip("Impulse at which the thud comes in under the slap, N s.")]
        public float thudImpulse = 6f;

        [Header("Synthesised (ImpactSynth, 2026-10-31)")]
        [Tooltip("Punches, blocks, falls, steps and the bell made from what makes them; the clips above stay as a quieter top layer.")]
        public bool synth = true;
        [Range(0f, 1f)] public float clipLayer = 0.55f;

        [Header("Ring")]
        public AudioClip bell;
        public AudioClip countBeep;
        public AudioClip sting;

        [Header("Crowd")]
        public AudioClip crowdBed;
        public AudioClip crowdSwell;
        public AudioClip crowdGroan;
        public AudioClip crowdApplause;
        [Tooltip("The short intake of breath when a punch lands harder than usual.")]
        public AudioClip crowdGasp;
        [Range(0f, 1f)] public float crowdQuiet = 0.22f;
        [Range(0f, 1f)] public float crowdLoud = 0.85f;
        [Tooltip("The eight sections' circle round the ring: radius and height, metres.")]
        public float sectionRadius = 9f, sectionHeight = 3f;
        [Tooltip("How far the scorer's half comes up (and the other half goes back by half of it), as a share of its level.")]
        [Range(0f, 1f)] public float sectionLean = 0.6f;

        [Header("Ropes")]
        [Tooltip("A rope bowed out further than the first, metres (RopeFlex.Strain), creaks; at the second it is as loud as it gets.")]
        public Vector2 ropeBow = new Vector2(0.015f, 0.1f);
        [Tooltip("Arriving on the ropes harder than this, N, twangs them.")]
        public float ropeTwang = 600f;
        [Range(0f, 1f)] public float ropeVolume = 0.35f;

        [Header("Rocked")]
        [Tooltip("Where the low-pass is when the boxer on camera is as dazed as it gets, Hz.")]
        public float rockedCutoff = 900f;
        [Range(0f, 1f)] public float heartbeatVolume = 0.7f;

        [Header("Perspective")]
        [Tooltip("A breathing loop, heard on the close shots (Assets/Audio/Real/breath.wav).")]
        public AudioClip breath;
        [Range(0f, 1f)] public float breathVolume = 0.25f;
        [Tooltip("On a close shot the hits come up and the crowd goes back by these, dB; on a wide shot the reverse, by half.")]
        public float closeHitsDb = 3f, closeCrowdDb = 6f;
        [Tooltip("How far the crowd is pulled down under the spoken commentary, dB.")]
        public float speechDuckDb = 6f;
        [Tooltip("How far the crowd is pulled down while the bell rings, dB.")]
        public float bellDuckDb = 8f;

        /// <summary>One register of the boxers' voices: takes of each sound (Assets/Audio/Synth/voice_*.wav).</summary>
        [System.Serializable]
        public class VoiceSet
        {
            public AudioClip[] exhale = new AudioClip[0], effort = new AudioClip[0], pain = new AudioClip[0], groan = new AudioClip[0], pant = new AudioClip[0];
        }

        [Header("Voices (PoBox/Audio/Write Voice WAVs)")]
        [Tooltip("A man's voice and a woman's.")]
        public VoiceSet lowVoice = new VoiceSet(), highVoice = new VoiceSet();
        [Tooltip("The share of punches thrown with a sharp breath out, as boxers breathe on their punches.")]
        [Range(0f, 1f)] public float exhaleChance = 0.85f;
        [Tooltip("The share of hooks, uppercuts and body shots (the punches thrown from the legs) with a grunt as well.")]
        [Range(0f, 1f)] public float effortChance = 0.35f;
        [Range(0f, 1f)] public float exhaleVolume = 0.3f, effortVolume = 0.45f, painVolume = 0.85f, groanVolume = 0.7f, pantVolume = 0.5f;
        [Tooltip("Boxers with a woman's voice, by the name on the scoreboard.")]
        public string[] highVoices = { "KIM", "GRANDMA" };
        [Tooltip("How tired (0 to 1: spent, out of breath or dazed) a boxer has to be before it is heard breathing.")]
        [Range(0f, 1f)] public float tiredFrom = 0.3f;
        [Tooltip("Seconds a boxer down is heard groaning before it goes quiet.")]
        public float groanSeconds = 10f;

        [Header("Mix")]
        [Tooltip("Groups Hits, Crowd and Ring, each with its volume exposed (HitsVolume, CrowdVolume, RingVolume). Empty: the gains below are used.")]
        public AudioMixer mixer;
        [Range(0f, 1f)] public float master = 0.9f;
        [Range(0f, 1.5f)] public float hitsGain = 0.45f, crowdGain = 0.55f, ringGain = 0.6f;
        [Tooltip("How far the crowd is pulled down under a heavy punch, dB.")]
        public float duckDb = 7f;
        public int voices = 14;

        public static bool Muted;
        /// <summary>Set by <see cref="SpeechCommentary"/> while the phone's voice is talking.</summary>
        public static bool Speaking;
        public int ActiveVoices { get; private set; }
        /// <summary>The finished mix's peak, dB below full scale. -100 with no meter on the listener.</summary>
        public float PeakDb => AudioMeter.Instance != null ? AudioMeter.Instance.PeakDb : -100f;
        /// <summary>How far the crowd is ducked at this moment, dB.</summary>
        public float DuckNow { get; private set; }

        AudioSource[] _pool;
        AudioSource _bed, _crowdSource, _ringSource, _heart, _breath;
        readonly AudioSource[] _sections = new AudioSource[8];
        readonly Vector2[] _sectionDir = new Vector2[8];
        readonly AudioSource[] _scrape = new AudioSource[2];
        readonly AudioSource[] _air = new AudioSource[4];      // red left, red right, blue left, blue right
        readonly float[] _squeakAt = new float[2];
        AudioClip _squeak;
        AudioSource _creak;
        float[] _lastRope = new float[0], _lastTwang = new float[0];
        readonly MjBoxer[] _pair = new MjBoxer[2];
        RopeFlex _ropes;
        AudioLowPassFilter _lowPass;
        AudioClip _twang, _clack;
        float _clacksAt = -1f;
        int _clacks;
        Fighter _victim;
        MjRing _ring;
        /// <summary>How loud each boxer's scrape is at this moment, 0 to 1.</summary>
        public float ScrapeNow(int corner) => _scrape[corner] != null ? _scrape[corner].volume : 0f;
        int _next, _lastCount;
        float _lastSwell = -99f, _duck, _gaspAt = -1f, _gaspVolume, _lastStep = -99f, _bedLevel;
        float _close, _rock, _speech, _bell, _hitsK = 1f;
        bool _mixed;
        // Every boxer in the ring, and when each is next heard.
        Fighter[] _all = new Fighter[0];
        readonly System.Collections.Generic.Dictionary<Fighter, float> _voiceFree = new System.Collections.Generic.Dictionary<Fighter, float>(),
            _nextBreath = new System.Collections.Generic.Dictionary<Fighter, float>(), _downAt = new System.Collections.Generic.Dictionary<Fighter, float>();

        void Awake()
        {
            if (synth) ImpactSynth.Build();
            AudioMixerGroup hits = Group("Hits"), crowd = Group("Crowd"), ring = Group("Ring");
            _mixed = mixer != null && hits != null && crowd != null && ring != null;

            _pool = new AudioSource[Mathf.Max(6, voices)];
            for (int i = 0; i < _pool.Length; i++)
            {
                var go = new GameObject("Voice " + i);
                go.transform.SetParent(transform, false);
                AudioSource s = go.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.spatialBlend = 1f;
                s.rolloffMode = AudioRolloffMode.Linear;
                s.minDistance = 3f;
                s.maxDistance = 30f;
                s.dopplerLevel = 0f;
                s.outputAudioMixerGroup = hits;
                _pool[i] = s;
            }

            AudioClip scrape = ProceduralClips.ScrapeLoop();
            for (int i = 0; i < 2; i++)
            {
                _scrape[i] = Instantiate(_pool[0], transform);
                _scrape[i].name = "Scrape " + i;
                _scrape[i].clip = scrape;
                _scrape[i].loop = true;
                _scrape[i].volume = 0f;
            }
            AudioClip air = ProceduralClips.AirLoop();
            for (int i = 0; i < _air.Length; i++)
            {
                _air[i] = Instantiate(_scrape[0], transform);
                _air[i].name = "Air " + i;
                _air[i].clip = air;
                _air[i].timeSamples = i * air.samples / _air.Length;   // four gloves, four places in the loop
            }
            _squeak = ProceduralClips.SqueakClip();
            _creak = Instantiate(_scrape[0], transform);
            _creak.name = "Ropes";
            _creak.clip = ProceduralClips.CreakLoop();
            _twang = ProceduralClips.Twang();
            _clack = ProceduralClips.Clack();

            _heart = Source2D(null);
            _heart.clip = ProceduralClips.Heartbeat();
            _heart.loop = true;
            _breath = Source2D(hits);
            _breath.clip = breath;
            _breath.loop = true;
            if (listener == null) listener = FindAnyObjectByType<AudioListener>();
            if (listener != null)
            {
                // On the listener the whole mix goes through it; switched off, it costs nothing.
                _lowPass = listener.GetComponent<AudioLowPassFilter>();
                if (_lowPass == null) _lowPass = listener.gameObject.AddComponent<AudioLowPassFilter>();
                _lowPass.enabled = false;
            }

            _bed = Source2D(crowd);
            _bed.clip = crowdBed;
            _bed.loop = true;
            _bed.volume = 0f;
            _crowdSource = Source2D(crowd);
            _ringSource = Source2D(ring);

            var ringNow = FindAnyObjectByType<MjRing>();
            Vector2 c = ringNow != null ? ringNow.centre : Vector2.zero;
            for (int k = 0; k < _sections.Length; k++)
            {
                float a = (k + 0.5f) * Mathf.PI * 2f / _sections.Length;
                _sectionDir[k] = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                var go = new GameObject("Crowd Section " + k);
                go.transform.SetParent(transform, false);
                go.transform.position = new Vector3(c.x + _sectionDir[k].x * sectionRadius, sectionHeight, c.y + _sectionDir[k].y * sectionRadius);
                AudioSource s = go.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.outputAudioMixerGroup = crowd;
                s.clip = crowdBed;
                s.loop = true;
                s.volume = 0f;
                s.spatialBlend = 1f;
                s.rolloffMode = AudioRolloffMode.Linear;
                s.minDistance = 3f;
                s.maxDistance = 30f;
                s.dopplerLevel = 0f;
                _sections[k] = s;
            }
        }

        AudioMixerGroup Group(string name)
        {
            if (mixer == null) return null;
            AudioMixerGroup[] found = mixer.FindMatchingGroups(name);
            return found != null && found.Length > 0 ? found[0] : null;
        }

        AudioSource Source2D(AudioMixerGroup group)
        {
            AudioSource s = gameObject.AddComponent<AudioSource>();
            s.spatialBlend = 0f;
            s.playOnAwake = false;
            s.outputAudioMixerGroup = group;
            return s;
        }

        void Start()
        {
            if (_bed.clip != null) _bed.Play();
            for (int k = 0; k < _sections.Length; k++)
            {
                if (_sections[k] == null || _sections[k].clip == null) continue;
                _sections[k].timeSamples = k * _sections[k].clip.samples / _sections.Length;   // eight places in the loop, not one crowd eight times
                _sections[k].Play();
            }
            // Whoever the scene switched on: the two of a bout, or everybody in a free-for-all.
            _all = FindObjectsByType<Fighter>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        }

        void OnEnable()
        {
            SimBus.Hit += OnHit;
            SimBus.PunchThrown += OnPunch;
            SimBus.Knockdown += OnKnockdown;
            SimBus.FloorImpact += OnFloor;
            SimBus.FootStep += OnFootStep;
            SimBus.PhaseChanged += OnPhase;
            SimBus.Block += OnBlock;
        }

        void OnDisable()
        {
            SimBus.Hit -= OnHit;
            SimBus.PunchThrown -= OnPunch;
            SimBus.Knockdown -= OnKnockdown;
            SimBus.FloorImpact -= OnFloor;
            SimBus.FootStep -= OnFootStep;
            SimBus.PhaseChanged -= OnPhase;
            SimBus.Block -= OnBlock;
        }

        // ------------------------------------------------------------ events

        // The room's own reactions (2026-10-31): an "ooh" at a big punch that just missed, boos at a fight that stalls.
        Fighter _swinger;
        float _swungAt = -9f, _lastOoh = -9f, _lastBoo = -9f, _lastLanded, _clinchFor;

        void OnPunch(Fighter f, PunchType type, int hand)
        {
            bool big = type == PunchType.Hook || type == PunchType.Uppercut || type == PunchType.Cross;
            if (big) { _swinger = f; _swungAt = Time.unscaledTime; }
            bool heavy = type == PunchType.Hook || type == PunchType.Uppercut || type == PunchType.Body;
            if (heavy && Random.value < effortChance) Voice(f, Of(f).effort, effortVolume * Random.Range(0.8f, 1f), 0.3f);
            else if (Random.value < exhaleChance) Voice(f, Of(f).exhale, exhaleVolume * Random.Range(0.8f, 1f), 0.15f);
        }

        void OnBlock(Fighter defender, Vector3 at, float speed)
        {
            _swinger = null;                // stopped, not missed
            // Glove on glove: the dull leather of the guard, and over it a pop that comes up with the speed.
            float k = Mathf.InverseLerp(2f, 9f, speed);
            float clips = synth ? clipLayer : 1f;
            if (synth) PlayAt(ImpactSynth.Pick(ImpactSynth.Block, k), at, Mathf.Lerp(0.35f, 0.9f, k), Random.Range(0.97f, 1.03f));
            PlayAt(Pick(punchBlocked), at, clips * Mathf.Lerp(0.3f, 0.85f, k), Mathf.Lerp(1.02f, 1.2f, k) * Random.Range(0.96f, 1.04f));
            PlayAt(Pick(punchLight), at, clips * Mathf.Lerp(0.08f, 0.35f, k * k), Mathf.Lerp(1.3f, 1.55f, k) * Random.Range(0.96f, 1.04f));
        }

        void OnHit(HitEvent e)
        {
            _swinger = null;
            _lastLanded = Time.unscaledTime;
            // Whoever the close-up will be on.
            BroadcastDirector director = BroadcastDirector.Instance;
            if (e.clean && e.impulse >= (director != null ? director.bigHit : 13f)) _victim = e.victim;

            float k = Mathf.Clamp01(e.impulse / 22f);
            // Pitch from how fast the glove arrived: a quick one cracks, a slow heavy one thumps.
            float snap = Mathf.Lerp(0.92f, 1.14f, Mathf.Clamp01(e.gloveSpeed / 9f)) * Random.Range(0.96f, 1.04f);
            // The head rings a little higher than the body.
            float where = e.zone == PartKind.Head ? 1.04f : 0.94f;

            float clips = synth ? clipLayer : 1f;
            if (!e.clean)
            {
                if (synth) PlayAt(ImpactSynth.Pick(ImpactSynth.Block, k), e.point, Mathf.Lerp(0.35f, 0.85f, k), snap);
                PlayAt(Pick(punchBlocked), e.point, clips * Mathf.Lerp(0.3f, 0.8f, k), snap * 0.95f);
                return;
            }

            bool heavy = e.impulse >= heavyImpulse;
            // The synthesised body of the hit: what it landed on, as hard as it landed; the clip is the slap on top.
            if (synth) PlayAt(ImpactSynth.Pick(e.zone == PartKind.Head ? ImpactSynth.Head : ImpactSynth.Body, k), e.point, Mathf.Lerp(0.4f, 1f, k), Mathf.Lerp(0.97f, 1.04f, Mathf.Clamp01(e.gloveSpeed / 9f)));
            PlayAt(Pick(heavy ? punchHeavy : punchLight), e.point, clips * Mathf.Lerp(0.35f, 0.95f, k), snap * where);
            // The air knocked out of whoever took it, a beat after the glove.
            if (e.victim != null) Voice(e.victim, Of(e.victim).pain, Mathf.Lerp(0.35f, painVolume, k), 0.25f, true);
            if (e.impulse >= thudImpulse)
            {
                // The thud grows faster than the slap, and sinks as it grows.
                float t = Mathf.InverseLerp(thudImpulse, 22f, e.impulse);
                PlayAt(Pick(bodyThud), e.point, Mathf.Lerp(0.2f, 1f, t * t), Mathf.Lerp(1.05f, 0.78f, t) * where);
            }

            // How far above what these two usually land: that is what the room reacts to.
            Excitement ex = Excitement.Instance;
            float usual = ex != null ? ex.UsualImpulse : 12f;
            float surprise = Mathf.Clamp01((e.impulse - usual * 1.1f) / Mathf.Max(1f, usual * 0.5f));
            if (heavy || surprise > 0.2f) _duck = Mathf.Max(_duck, Mathf.Lerp(0.35f, 1f, Mathf.Max(surprise, k)));
            if (surprise > 0.15f)
            {
                // The gasp comes after the punch, not with it.
                _gaspAt = Time.unscaledTime + 0.12f;
                _gaspVolume = Mathf.Max(_gaspVolume, Mathf.Lerp(0.25f, 0.8f, surprise));
            }
            if (surprise > 0.6f && Time.unscaledTime - _lastSwell > 3f)
            {
                _lastSwell = Time.unscaledTime;
                PlayCrowd(crowdSwell, Mathf.Lerp(0.35f, 0.8f, surprise));
            }
        }

        void OnKnockdown(Fighter f, HitEvent cause)
        {
            _lastSwell = Time.unscaledTime;
            _duck = 1f;
            _victim = f;
            PlayCrowd(crowdSwell, 0.95f);
            PlayCrowd(crowdGroan, 0.5f);
            if (f != null) Voice(f, Of(f).groan, groanVolume, 0.5f, true);
        }

        void OnFloor(Vector3 point, float impulse)
        {
            // A glove or a knee put down is a pat; a body landing is the whole thud, and lower.
            float k = Mathf.Clamp01(impulse / 120f);
            if (synth) PlayAt(ImpactSynth.Pick(ImpactSynth.Floor, k), point, Mathf.Lerp(0.12f, 1f, k), Random.Range(0.96f, 1.04f));
            PlayAt(bodyFall, point, (synth ? clipLayer : 1f) * Mathf.Lerp(0.08f, 1f, k), Mathf.Lerp(1.25f, 0.9f, k) * Random.Range(0.95f, 1.05f));
        }

        void OnFootStep(Vector3 point, float speed)
        {
            // Boots on canvas: quiet, and only a step that was really taken, not a shuffle.
            if (speed < 0.5f || Time.unscaledTime - _lastStep < 0.08f) return;
            _lastStep = Time.unscaledTime;
            float k = Mathf.Clamp01(speed / 3f);
            if (synth) PlayAt(ImpactSynth.Pick(ImpactSynth.Step, k), point, Mathf.Lerp(0.12f, 0.35f, k), Random.Range(0.92f, 1.08f));
            PlayAt(Pick(footsteps), point, (synth ? clipLayer : 1f) * Mathf.Lerp(0.15f, 0.45f, k), Random.Range(0.85f, 1.1f));
        }

        void OnPhase(BoutPhase from, BoutPhase to)
        {
            // A boxing bell: one ding to start a round, three to end it (the clip was an athletics lap bell).
            AudioClip start = synth && ImpactSynth.Bell != null ? ImpactSynth.Bell : bell;
            AudioClip end = synth && ImpactSynth.BellEnd != null ? ImpactSynth.BellEnd : bell;
            if (to == BoutPhase.Fight && from == BoutPhase.Intro) PlayRing(start, 0.8f);
            else if (to == BoutPhase.RoundBreak) PlayRing(end, 0.8f);
            else if (to == BoutPhase.WalkOn) PlayRing(sting, 0.5f);
            else if (to == BoutPhase.Results)
            {
                PlayRing(end, 0.7f);
                PlayRing(sting, 0.45f);
                PlayCrowd(crowdApplause, 0.6f);
            }
        }

        // ------------------------------------------------------------ per frame

        void Update()
        {
            AudioListener.volume = Muted ? 0f : master;
            float dt = Time.unscaledDeltaTime;
            TenSeconds();

            // The bed follows the excitement squared: a quiet hall most of the time, a roar when it earns one.
            float e = Excitement.Value;
            float target = Mathf.Lerp(crowdQuiet, crowdLoud, Mathf.Clamp01(e * e + ArenaMood.Burst * 0.5f));
            _bedLevel = Mathf.MoveTowards(_bedLevel, target, dt * 0.6f);

            // The duck: down at once, back over half a second.
            _duck = Mathf.MoveTowards(_duck, 0f, dt * 2f);
            DuckNow = duckDb * Mathf.Clamp01(_duck);
            // The camera's perspective: in close the gloves, out wide the room. The cut is hard, the mix is not.
            Shot shot = BroadcastDirector.Instance != null ? BroadcastDirector.Instance.Current : Shot.Wide;
            _close = Mathf.MoveTowards(_close, Closeness(shot), dt * 3f);
            float side = _close < 0f ? 0.5f : 1f;
            _speech = Mathf.MoveTowards(_speech, Speaking ? 1f : 0f, dt * 4f);
            _bell = Mathf.MoveTowards(_bell, 0f, dt / 1.5f);
            Hall(_close);
            Reactions();
            float hitsDb = _close * side * closeHitsDb, crowdDb = -_close * side * closeCrowdDb - _speech * speechDuckDb - _bell * bellDuckDb;
            float crowdLinear = Mathf.Pow(10f, (crowdDb - DuckNow) / 20f);
            _hitsK = _mixed ? 1f : hitsGain * Mathf.Pow(10f, hitsDb / 20f);
            if (_mixed)
            {
                mixer.SetFloat("HitsVolume", Db(hitsGain) + hitsDb);
                mixer.SetFloat("CrowdVolume", Db(crowdGain) - DuckNow + crowdDb);
                mixer.SetFloat("RingVolume", Db(ringGain));
                _bed.volume = _bedLevel * 0.5f;
                Sections(_bedLevel);
            }
            else
            {
                // No mixer: the same thing with the sources' own volumes.
                _bed.volume = _bedLevel * crowdGain * crowdLinear * 0.5f;
                Sections(_bedLevel * crowdGain * crowdLinear);
                _crowdSource.volume = crowdGain * crowdLinear;
                _ringSource.volume = ringGain;
            }

            Scrape(dt);
            Ropes(dt);
            Breathing();
            Rocked(shot, dt);
            Fade(_breath, Mathf.Max(0f, _close) * breathVolume * _hitsK, dt);

            if (_gaspAt > 0f && Time.unscaledTime >= _gaspAt)
            {
                PlayCrowd(crowdGasp, _gaspVolume);
                _gaspAt = -1f;
                _gaspVolume = 0f;
            }

            if (bout != null && bout.Phase == BoutPhase.Count && bout.Count != _lastCount && bout.Count > 0)
                PlayRing(countBeep, 0.5f);
            _lastCount = bout != null ? bout.Count : 0;

            int active = 0;
            for (int i = 0; i < _pool.Length; i++) if (_pool[i].isPlaying) active++;
            ActiveVoices = active;
        }

        static void Fade(AudioSource s, float level, float rate)
        {
            if (s.clip == null) return;
            s.volume = Mathf.MoveTowards(s.volume, level, rate);
            if (s.volume > 0.001f && !s.isPlaying) s.Play();
            else if (s.volume <= 0.001f && s.isPlaying) s.Pause();
        }

        static float Db(float linear) => linear > 1e-4f ? 20f * Mathf.Log10(linear) : -80f;

        static AudioClip Pick(AudioClip[] set) =>set != null && set.Length > 0 ? set[Random.Range(0, set.Length)] : null;

        void PlayAt(AudioClip clip, Vector3 position, float volume, float pitch)
        {
            if (clip == null || _pool == null) return;
            AudioSource s = _pool[_next];
            _next = (_next + 1) % _pool.Length;
            s.transform.position = position;
            s.clip = clip;
            s.volume = volume * _hitsK;
            s.pitch = pitch;
            s.Play();
        }

        void PlayCrowd(AudioClip clip, float volume)
        {
            if (clip != null && _crowdSource != null) _crowdSource.PlayOneShot(clip, volume);
        }

        void PlayRing(AudioClip clip, float volume)
        {
            if (clip == bell && clip != null) _bell = 1f;
            if (clip != null && _ringSource != null) _ringSource.PlayOneShot(clip, volume);
        }
    }
}
