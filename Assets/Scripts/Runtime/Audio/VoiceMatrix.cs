using System;
using System.Collections.Generic;
using UnityEngine;

namespace PoBox.Audio
{
    /// <summary>
    /// The boxers' voices, as a matrix: a boxer (by the name on the scoreboard, a twin by its boxer's) by an
    /// <see cref="Emotion"/>. Each cell is filled from the first tier that has something for it:
    ///
    ///   1. Recorded: the boxer's own clips, anywhere under a Resources/Voices folder, named
    ///      A_Voice_&lt;Boxer&gt;_&lt;Emotion&gt;_&lt;n&gt; (A_Voice_Matt_Pain_01.wav). Drop clips in and they are used,
    ///      cell by cell; nothing else changes.
    ///   2. The archetype: the shared low (a man's) or high (a woman's) takes (Assets/Audio/Synth/voice_*.wav)
    ///      for the five emotions they have (exhale, effort, pain, groan, pant), mixed with tier 3 by
    ///      <see cref="proceduralShare"/>, so each boxer still sounds like itself.
    ///   3. Procedural: <see cref="VoiceSynth"/> in the boxer's own <see cref="VoiceProfile"/>, made the first
    ///      time a cell is wanted and kept. Always there, so no cell is ever silent.
    /// </summary>
    [Serializable]
    public class VoiceMatrix
    {
        public enum Tier { Recorded, Archetype, Procedural }

        [Tooltip("The share of picks from the procedural voice where the archetype has takes too: 0 archetype only, 1 procedural only.")]
        [Range(0f, 1f)] public float proceduralShare = 0.5f;
        [Tooltip("Procedural takes made per boxer and emotion.")]
        [Range(1, 6)] public int takes = 3;

        readonly Dictionary<string, Dictionary<Emotion, AudioClip[]>> _recorded = new Dictionary<string, Dictionary<Emotion, AudioClip[]>>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<(string, Emotion), AudioClip[]> _made = new Dictionary<(string, Emotion), AudioClip[]>();
        bool _indexed;

        /// <summary>Which tier the last <see cref="Pick"/> came from (for the debug readout and the tests).</summary>
        public Tier LastTier { get; private set; }

        /// <summary>Recorded clips found under Resources/Voices, by boxer, e.g. "MATT: Pain 2, Triumph 1".</summary>
        public string Inventory()
        {
            Index();
            if (_recorded.Count == 0) return "none recorded";
            var lines = new List<string>();
            foreach (var b in _recorded)
            {
                var cells = new List<string>();
                foreach (var e in b.Value) cells.Add($"{e.Key} {e.Value.Length}");
                lines.Add(b.Key.ToUpperInvariant() + ": " + string.Join(", ", cells));
            }
            return string.Join("\n", lines);
        }

        /// <summary>
        /// One take for this boxer and emotion. <paramref name="archetype"/> is the shared set's takes of it (empty if
        /// it has none); <paramref name="voice"/> the boxer's make, for the procedural tier.
        /// </summary>
        public AudioClip Pick(string boxer, Emotion e, AudioClip[] archetype, VoiceProfile voice)
        {
            Index();
            if (_recorded.TryGetValue(boxer ?? "", out var mine) && mine.TryGetValue(e, out AudioClip[] rec) && rec.Length > 0)
            {
                LastTier = Tier.Recorded;
                return rec[UnityEngine.Random.Range(0, rec.Length)];
            }
            bool hasArchetype = archetype != null && archetype.Length > 0;
            if (hasArchetype && UnityEngine.Random.value >= proceduralShare)
            {
                LastTier = Tier.Archetype;
                return archetype[UnityEngine.Random.Range(0, archetype.Length)];
            }
            AudioClip[] made = Made(boxer, e, voice);
            if (made.Length > 0)
            {
                LastTier = Tier.Procedural;
                return made[UnityEngine.Random.Range(0, made.Length)];
            }
            LastTier = Tier.Archetype;
            return hasArchetype ? archetype[UnityEngine.Random.Range(0, archetype.Length)] : null;
        }

        /// <summary>Makes the procedural cells for a boxer ahead of time (at the walk-on, not in the middle of a punch).</summary>
        public void Warm(string boxer, VoiceProfile voice, params Emotion[] emotions)
        {
            foreach (Emotion e in emotions) Made(boxer, e, voice);
        }

        AudioClip[] Made(string boxer, Emotion e, VoiceProfile voice)
        {
            var key = (boxer ?? "", e);
            if (_made.TryGetValue(key, out AudioClip[] clips)) return clips;
            try { clips = VoiceSynth.Make(voice, e, boxer, takes); }
            catch (Exception ex) { Debug.LogWarning($"[VoiceMatrix] {boxer} {e}: {ex.Message}"); clips = new AudioClip[0]; }
            _made[key] = clips;
            return clips;
        }

        /// <summary>Reads Resources/Voices once: A_Voice_&lt;Boxer&gt;_&lt;Emotion&gt;_&lt;n&gt;, case not minded; other names are passed over.</summary>
        void Index()
        {
            if (_indexed) return;
            _indexed = true;
            var found = new Dictionary<(string, Emotion), List<AudioClip>>();
            foreach (AudioClip clip in Resources.LoadAll<AudioClip>("Voices"))
            {
                if (!Parse(clip.name, out string boxer, out Emotion e)) { Debug.LogWarning($"[VoiceMatrix] {clip.name}: not A_Voice_<Boxer>_<Emotion>_<n>, passed over."); continue; }
                var key = (boxer.ToUpperInvariant(), e);
                if (!found.TryGetValue(key, out var list)) found[key] = list = new List<AudioClip>();
                list.Add(clip);
            }
            foreach (var kv in found)
            {
                if (!_recorded.TryGetValue(kv.Key.Item1, out var row)) _recorded[kv.Key.Item1] = row = new Dictionary<Emotion, AudioClip[]>();
                row[kv.Key.Item2] = kv.Value.ToArray();
            }
        }

        /// <summary>"A_Voice_Matt_Pain_01" is MATT's Pain; "A_Voice_Grandma_Triumph" too, with no number.</summary>
        public static bool Parse(string name, out string boxer, out Emotion emotion)
        {
            boxer = null;
            emotion = Emotion.Effort;
            if (string.IsNullOrEmpty(name)) return false;
            string[] p = name.Split('_');
            if (p.Length < 4 || !p[0].Equals("A", StringComparison.OrdinalIgnoreCase) || !p[1].Equals("Voice", StringComparison.OrdinalIgnoreCase)) return false;
            if (!Enum.TryParse(p[3], true, out emotion)) return false;
            boxer = p[2];
            return boxer.Length > 0;
        }
    }
}
