using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;
using PoBox.Fx;
using PoBox.Sim;

namespace PoBox.Audio
{
    /// <summary>
    /// A boxer's mouth moves with its own voice (2026-11-25). <see cref="AudioDirector"/> says the boxer's voice
    /// through this component's <see cref="voice"/> source (at the head) instead of its shared pool, and the mouth
    /// follows what is playing:
    ///
    ///   * a procedural take: its <see cref="VisemeTrack"/> (how open and how wide, every 10 ms, from the phonemes
    ///     the synthesiser said), read at the source's playing position a little ahead, so the picture is not late;
    ///   * a recording with no track: the level the audio thread measured (<see cref="VisemeTap"/>, lock-free);
    ///   * neither (WebGL has no audio-thread callback): mouthed, a syllable at a time, while it plays.
    ///
    /// What it moves is the first of these the boxer's mesh has: a blend shape (an "open" one, plus "wide" and
    /// "round" ones if they exist), else a jaw bone turned about <see cref="jawAxis"/>, else a mouth transform
    /// scaled. Of the shipped meshes (Assets/_Project/Agents/Boxers/*, Assets/Art/Exports/GLB) none has a usable
    /// blend shape (ZOMBIE carries one empty "V_None"); TIMMY and ZOMBIE (the CC rigs) have CC_Base_JawRoot; the
    /// others have no face bone, so they need a mouth transform assigned to show anything.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(100)]   // after FighterSkin has posed the neck this frame
    public sealed class LipSync : MonoBehaviour
    {
        [Tooltip("The boxer whose voice this is. Empty: the Fighter on this GameObject or above it.")]
        [SerializeField] Fighter fighter;
        [Tooltip("The voice's own source (3D, on a child that follows the head). Empty: one is made at Awake.")]
        [SerializeField] AudioSource voice;

        [Header("Blend shapes (first match by name, case-insensitive)")]
        [Tooltip("Empty: the boxer's skinned meshes are searched for the names below.")]
        [SerializeField] SkinnedMeshRenderer face;
        [SerializeField] string[] openShapes = { "jawOpen", "Jaw_Open", "mouthOpen", "Mouth_Open", "V_Open", "viseme_aa", "Open" };
        [SerializeField] string[] wideShapes = { "mouthStretch", "Mouth_Widen", "V_Wide", "viseme_E", "viseme_I" };
        [SerializeField] string[] roundShapes = { "mouthPucker", "Mouth_Pucker", "V_Tight_O", "viseme_O", "viseme_U" };
        [Range(0f, 100f)] [SerializeField] float shapeWeight = 100f;

        [Header("Jaw bone (used when there is no open blend shape)")]
        [Tooltip("Empty: the first bone under the boxer named as below (CC rigs: CC_Base_JawRoot).")]
        [SerializeField] Transform jaw;
        [SerializeField] string[] jawNames = { "CC_Base_JawRoot", "JawRoot", "jaw", "Jaw", "mixamorig:Jaw" };
        [Tooltip("The jaw's local axis it opens about. Flip the sign if the jaw opens upwards.")]
        [SerializeField] Vector3 jawAxis = new Vector3(0f, 0f, 1f);
        [Tooltip("Degrees the jaw drops fully open.")]
        [SerializeField] float jawDegrees = 16f;

        [Header("Mouth transform (used when there is neither)")]
        [SerializeField] Transform mouth;
        [Tooltip("The mouth's local scale fully open, as a multiple of its rest scale; width spreads x towards wideScale.")]
        [SerializeField] Vector3 openScale = new Vector3(1f, 2.2f, 1f);
        [SerializeField] float wideScale = 1.25f;

        [Header("Motion")]
        [Tooltip("Seconds to open and to close: the jaw has mass, a meter does not.")]
        [SerializeField] float attackSeconds = 0.03f, releaseSeconds = 0.07f;
        [Tooltip("The voice is read this far ahead of what is heard (a picture a frame late reads as out of sync).")]
        [SerializeField] float lookAheadSeconds = 0.04f;
        [Tooltip("A recording's audio-thread level that opens the mouth fully (until it has been louder).")]
        [SerializeField] float tapFullLevel = 0.12f;
        [Tooltip("Syllables a second when a recording can only be mouthed.")]
        [SerializeField] float syllablesPerSecond = 5.5f;

        static readonly Dictionary<Fighter, LipSync> ByFighter = new Dictionary<Fighter, LipSync>();

        /// <summary>The lip-sync of a boxer, if it has one.</summary>
        public static bool TryGet(Fighter f, out LipSync sync)
        {
            sync = null;
            return f != null && ByFighter.TryGetValue(f, out sync) && sync != null && sync.isActiveAndEnabled;
        }

        /// <summary>The mouth stream, smoothed: how open 0..1, how wide 0 (rounded) .. 1 (spread).</summary>
        public float Open { get; private set; }
        public float Width { get; private set; } = 0.5f;
        /// <summary>What drives the face: "blend shape", "jaw", "mouth" or "none".</summary>
        public string Driving { get; private set; } = "none";

        VisemeTap _tap;
        VisemeTrack _track;
        int _openIndex = -1, _wideIndex = -1, _roundIndex = -1;
        Quaternion _jawRest;
        Vector3 _mouthRest;
        float _tapPeak, _phase;
        int _tapBlocks, _tapStill;

        void Awake()
        {
            if (fighter == null) fighter = GetComponentInParent<Fighter>();
            if (voice == null)
            {
                var go = new GameObject("Voice (lip sync)");
                go.transform.SetParent(transform, false);
                voice = go.AddComponent<AudioSource>();
                voice.playOnAwake = false;
                voice.spatialBlend = 1f;
                voice.rolloffMode = AudioRolloffMode.Linear;
                voice.minDistance = 3f;
                voice.maxDistance = 30f;
                voice.dopplerLevel = 0f;
            }
            _tap = voice.GetComponent<VisemeTap>();
            if (_tap == null) _tap = voice.gameObject.AddComponent<VisemeTap>();
            FindFace();
        }

        void OnEnable() { if (fighter != null) ByFighter[fighter] = this; }

        void OnDisable()
        {
            if (fighter != null && ByFighter.TryGetValue(fighter, out LipSync s) && s == this) ByFighter.Remove(fighter);
            Open = 0f;
            Apply();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() => ByFighter.Clear();

        /// <summary>Says <paramref name="clip"/> through the boxer's mouth. False if there is nothing to say it with.</summary>
        public bool Speak(AudioClip clip, float volume, float pitch, AudioMixerGroup group)
        {
            if (clip == null || voice == null) return false;
            if (fighter != null) voice.transform.position = fighter.HeadPosition;
            voice.outputAudioMixerGroup = group;
            voice.clip = clip;
            voice.volume = volume;
            voice.pitch = pitch;
            voice.Play();
            VisemeLibrary.TryGet(clip, out _track);
            _tapPeak = tapFullLevel;
            return true;
        }

        void FindFace()
        {
            var meshes = new List<SkinnedMeshRenderer>();
            if (face != null) meshes.Add(face);
            Transform root = fighter != null ? fighter.transform : transform;
            meshes.AddRange(root.GetComponentsInChildren<SkinnedMeshRenderer>(true));
            var skin = root.GetComponent<FighterSkin>();
            if (skin != null)
                foreach (Renderer r in skin.extraRenderers)
                    if (r is SkinnedMeshRenderer smr) meshes.Add(smr);

            foreach (SkinnedMeshRenderer m in meshes)
            {
                if (m == null || m.sharedMesh == null) continue;
                int open = Shape(m.sharedMesh, openShapes);
                if (open < 0) continue;
                face = m;
                _openIndex = open;
                _wideIndex = Shape(m.sharedMesh, wideShapes);
                _roundIndex = Shape(m.sharedMesh, roundShapes);
                Driving = "blend shape";
                return;
            }

            if (jaw == null)
                foreach (SkinnedMeshRenderer m in meshes)
                {
                    if (m == null || m.bones == null) continue;
                    foreach (string name in jawNames)
                    {
                        foreach (Transform b in m.bones)
                            if (b != null && b.name.Equals(name, StringComparison.OrdinalIgnoreCase)) { jaw = b; break; }
                        if (jaw != null) break;
                    }
                    if (jaw != null) break;
                }
            if (jaw != null)
            {
                _jawRest = jaw.localRotation;
                Driving = "jaw";
                return;
            }
            if (mouth != null)
            {
                _mouthRest = mouth.localScale;
                Driving = "mouth";
            }
        }

        static int Shape(Mesh mesh, string[] names)
        {
            int count = mesh.blendShapeCount;
            if (count == 0) return -1;
            foreach (string want in names)
                for (int i = 0; i < count; i++)
                    if (string.Equals(mesh.GetBlendShapeName(i), want, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            float targetOpen = 0f, targetWidth = 0.5f;
            if (voice != null && voice.isPlaying && voice.clip != null)
            {
                if (fighter != null) voice.transform.position = fighter.HeadPosition;
                AudioClip clip = voice.clip;
                if (_track != null)
                {
                    int ahead = Mathf.RoundToInt(lookAheadSeconds * clip.frequency * Mathf.Abs(voice.pitch));
                    _track.Sample(voice.timeSamples + ahead, out targetOpen, out targetWidth);
                }
                else
                {
                    // The audio thread's level, while it is coming (a counter that moved within the last few frames).
                    int blocks = _tap != null ? _tap.Blocks : 0;
                    _tapStill = blocks != _tapBlocks ? 0 : _tapStill + 1;
                    _tapBlocks = blocks;
                    if (blocks > 0 && _tapStill < 4)
                    {
                        float level = _tap.Level / Mathf.Max(1e-3f, voice.volume);
                        _tapPeak = Mathf.Max(_tapPeak, level);
                        targetOpen = Mathf.Sqrt(Mathf.Clamp01(level / _tapPeak));
                    }
                    else
                    {
                        // Mouthed: a syllable at a time, its size wandering a little.
                        _phase += dt * syllablesPerSecond * Mathf.Abs(voice.pitch);
                        float s = Mathf.Sin(_phase * Mathf.PI * 2f);
                        targetOpen = Mathf.Max(0f, s) * (0.55f + 0.35f * Mathf.Sin(_phase * 1.7f + 1f));
                    }
                }
            }
            float rate = targetOpen > Open ? attackSeconds : releaseSeconds;
            float k = 1f - Mathf.Exp(-dt / Mathf.Max(1e-3f, rate));
            Open += (targetOpen - Open) * k;
            Width += (targetWidth - Width) * k;
            Apply();
        }

        void Apply()
        {
            if (_openIndex >= 0 && face != null)
            {
                face.SetBlendShapeWeight(_openIndex, Open * shapeWeight);
                if (_wideIndex >= 0) face.SetBlendShapeWeight(_wideIndex, Open * Mathf.Clamp01(Width * 2f - 1f) * shapeWeight);
                if (_roundIndex >= 0) face.SetBlendShapeWeight(_roundIndex, Open * Mathf.Clamp01(1f - Width * 2f) * shapeWeight);
            }
            else if (jaw != null)
            {
                Vector3 axis = jawAxis.sqrMagnitude > 1e-6f ? jawAxis.normalized : Vector3.forward;
                jaw.localRotation = _jawRest * Quaternion.AngleAxis(Open * jawDegrees, axis);
            }
            else if (mouth != null)
            {
                Vector3 s = Vector3.Lerp(Vector3.one, openScale, Open);
                s.x *= Mathf.Lerp(1f, Mathf.Lerp(1f / wideScale, wideScale, Width), Open);
                mouth.localScale = Vector3.Scale(_mouthRest, s);
            }
        }
    }
}
