using UnityEngine;
using UnityEngine.Audio;
using PoBox.Sim;

namespace PoBox.Audio
{
    /// <summary>
    /// The 2026-11-25 layer: punches, blocks and falls as modal sounds chosen by what was struck and scaled by the
    /// measured contact (<see cref="ImpactModalSettings"/>); the crowd's bed and a roar layer riding the reactive
    /// crowd (<see cref="CrowdExcitement"/>); and each boxer's voice said through its own mouth (<see cref="LipSync"/>).
    /// Each is off, and the director sounds as before, while its asset or component is missing.
    /// </summary>
    public partial class AudioDirector
    {
        [Header("Modal impacts (2026-11-25)")]
        [Tooltip("Modal sets per contact (head, torso, glove on glove, canvas). Empty: the fixed synthesised banks.")]
        public ImpactModalSettings impactModes;

        [Header("Reactive crowd (2026-11-25)")]
        [Tooltip("The hall's excitement. Empty: CrowdExcitement.Instance, if the scene has one.")]
        public CrowdExcitement crowdExcitement;
        [Tooltip("A looping roar laid over the bed as the hall boils over. Empty: the synthesised one.")]
        public AudioClip crowdRoarLoop;
        [Range(0f, 1f)] public float roarVolume = 0.55f;
        [Tooltip("The swell played as the hall boils over.")]
        [Range(0f, 1f)] public float roarSwellVolume = 0.7f;

        [Header("Lip-sync (2026-11-25)")]
        [Tooltip("A boxer with a LipSync says its voice through it, and its mouth moves; without one, the shared pool as before.")]
        public bool lipSync = true;

        AudioSource _roar;
        AudioMixerGroup _hitsGroup;

        CrowdExcitement CrowdNow => crowdExcitement != null ? crowdExcitement : CrowdExcitement.Instance;

        /// <summary>Awake's part: the modal banks and the roar layer's source.</summary>
        void SetUpReactive(AudioMixerGroup hits, AudioMixerGroup crowd)
        {
            _hitsGroup = hits;
            if (synth) ImpactSynth.BuildModal(impactModes);
            _roar = Source2D(crowd);
            _roar.clip = crowdRoarLoop != null ? crowdRoarLoop : ImpactSynth.CrowdRoarLoop;
            _roar.loop = true;
            _roar.volume = 0f;
        }

        /// <summary>The modal sound of a contact at <paramref name="at"/>; false (and nothing played) without modal banks.</summary>
        bool PlayModal(ContactKind kind, float force, float speed, Vector3 at, float gain = 1f)
        {
            if (!synth || impactModes == null || !ImpactSynth.Modal(kind, force, speed, out ModalHit hit)) return false;
            PlayAt(hit.body, at, hit.bodyVolume * gain, hit.bodyPitch);
            if (hit.crackVolume > 0.01f) PlayAt(hit.crack, at, hit.crackVolume * gain, hit.crackPitch);
            return true;
        }

        /// <summary>The bed's target share (0..1 between quiet and loud): the louder of the camera's reading and the hall's,
        /// with a surge (how fast the hall is rising) counted on top, so a knockdown lifts the crowd at once.</summary>
        float BedShare(float fromExcitement)
        {
            CrowdExcitement c = CrowdNow;
            return c != null ? Mathf.Max(fromExcitement, Mathf.Clamp01(c.Bed + 0.2f * c.Surge)) : fromExcitement;
        }

        /// <summary>The roar layer, on the crowd bus, and a swell as the hall boils over. <paramref name="scale"/>: the bus gain without a mixer.</summary>
        void RoarLayer(float scale, float dt)
        {
            CrowdExcitement c = CrowdNow;
            if (_roar == null) return;
            float level = c != null ? Mathf.Clamp01(c.Roar) * roarVolume * scale : 0f;
            // Up over a third of a second, away over two: a hall does not stop cheering at once.
            float rate = level > _roar.volume ? dt * 3f : dt * 0.5f;
            Fade(_roar, level, rate);
            if (c != null && c.TakeSwell() && synth) PlayCrowd(ImpactSynth.CrowdRoar, roarSwellVolume);
        }

        /// <summary>Says a voice through the boxer's own mouth, if it has one. False: play it from the pool.</summary>
        bool SayThroughMouth(Fighter f, AudioClip clip, float volume, float pitch)
        {
            if (!lipSync || !LipSync.TryGet(f, out LipSync mouth)) return false;
            return mouth.Speak(clip, volume * _hitsK, pitch, _hitsGroup);
        }

        /// <summary>At the walk-on: the shared archetype takes get a mouth stream from their loudness, where the platform can read them.</summary>
        void WarmMouths()
        {
            if (!lipSync) return;
            foreach (VoiceSet set in new[] { lowVoice, highVoice })
                foreach (AudioClip[] takes in new[] { set.exhale, set.effort, set.pain, set.groan, set.pant })
                    if (takes != null)
                        foreach (AudioClip c in takes) VisemeLibrary.TryAddFromSamples(c);
        }
    }
}
