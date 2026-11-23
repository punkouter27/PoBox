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
    ///   2. The archetype: the shared low (a man's) or high (a woman's) takes (Assets/_Project/Environments/Audio/Synth/voice_*.wav)
    ///      for the five emotions they have (exhale, effort, pain, groan, pant), mixed with tier 3 by
    ///      <see cref="proceduralShare"/>, so each boxer still sounds like itself.
    ///   3. Procedural: <see cref="VoiceSynth"/> in the boxer's own <see cref="VoiceProfile"/>, made the first
    ///      time a cell is wanted and kept. Always there, so no cell is ever silent.
    ///
    /// The tournament standard's 5x5 layer on top (2026-11-22): every boxer by an <see cref="Emotion5"/> (angry, happy,
    /// hurt, insult, celebrate) by an intensity level 1..5 (the <see cref="Emotion5"/> overload of Pick).
    /// A cell is a recording of its own if there is one, Resources/Voices/&lt;Boxer&gt;/&lt;emotion&gt;-&lt;level&gt;.wav
    /// (Voices/Matt/hurt-3.wav; more takes as hurt-3_2.wav, ...); otherwise it is played as the older emotion
    /// <see cref="Map"/> gives it, through the three tiers above, its procedural takes made at the level's intensity.
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
        readonly Dictionary<(string, Emotion5, int), AudioClip[]> _made5 = new Dictionary<(string, Emotion5, int), AudioClip[]>();
        readonly Dictionary<string, Dictionary<(Emotion5, int), AudioClip[]>> _recorded5 = new Dictionary<string, Dictionary<(Emotion5, int), AudioClip[]>>(StringComparer.OrdinalIgnoreCase);
        bool _indexed;

        /// <summary>The intensity levels of the 5x5 layer: 1 (barely) to 5 (all out).</summary>
        public const int Levels = 5;
        [Tooltip("Procedural takes made per boxer, emotion and level in the 5x5 layer.")]
        [Range(1, 6)] public int gridTakes = 2;

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

        /// <summary>
        /// The 5x5 layer's cell for this boxer, emotion and level (clamped to 1..5): its own recording
        /// (Resources/Voices/&lt;Boxer&gt;/&lt;emotion&gt;-&lt;level&gt;) if there is one; otherwise the older emotion it maps
        /// to (<see cref="Map"/>), from that emotion's recording, the archetype's takes (<paramref name="archetype"/>, may be
        /// null) or the procedural voice made at the level's intensity.
        /// </summary>
        public AudioClip Pick(string boxer, Emotion5 e, int level, Func<Emotion, AudioClip[]> archetype, VoiceProfile voice)
        {
            level = Mathf.Clamp(level, 1, Levels);
            AudioClip[] own = Recorded5(boxer, e, level);
            if (own.Length > 0)
            {
                LastTier = Tier.Recorded;
                return own[UnityEngine.Random.Range(0, own.Length)];
            }
            Index();
            Emotion old = Map(e, level).emotion;
            if (_recorded.TryGetValue(boxer ?? "", out var mine) && mine.TryGetValue(old, out AudioClip[] rec) && rec.Length > 0)
            {
                LastTier = Tier.Recorded;
                return rec[UnityEngine.Random.Range(0, rec.Length)];
            }
            AudioClip[] arch = archetype?.Invoke(old);
            bool hasArchetype = arch != null && arch.Length > 0;
            if (hasArchetype && UnityEngine.Random.value >= proceduralShare)
            {
                LastTier = Tier.Archetype;
                return arch[UnityEngine.Random.Range(0, arch.Length)];
            }
            AudioClip[] made = Made(boxer, e, level, voice);
            if (made.Length > 0)
            {
                LastTier = Tier.Procedural;
                return made[UnityEngine.Random.Range(0, made.Length)];
            }
            LastTier = Tier.Archetype;
            return hasArchetype ? arch[UnityEngine.Random.Range(0, arch.Length)] : null;
        }

        /// <summary>
        /// The older emotion a 5x5 cell is played as, with the procedural voice's intensity (0..1) and length (1 the
        /// emotion's own): angry an effort grunt, happy a soft short triumph, hurt pain (levels 1-3) or a groan (4-5),
        /// insult a taunting laugh, celebrate a full triumph.
        /// </summary>
        public static (Emotion emotion, float intensity, float length) Map(Emotion5 e, int level)
        {
            float k = (Mathf.Clamp(level, 1, Levels) - 1) / (float)(Levels - 1);
            switch (e)
            {
                case Emotion5.Angry: return (Emotion.Effort, k, Mathf.Lerp(0.85f, 1.35f, k));
                case Emotion5.Happy: return (Emotion.Triumph, 0.5f * k, Mathf.Lerp(0.4f, 0.7f, k));
                case Emotion5.Hurt: return (level <= 3 ? Emotion.Pain : Emotion.Groan, k, Mathf.Lerp(0.8f, 1.2f, k));
                case Emotion5.Insult: return (Emotion.Taunt, k, Mathf.Lerp(0.85f, 1.2f, k));
                default: return (Emotion.Triumph, k, Mathf.Lerp(0.85f, 1.3f, k));
            }
        }

        /// <summary>A 0..1 strength as a level, 1..5.</summary>
        public static int Level(float strength01) => 1 + Mathf.RoundToInt(Mathf.Clamp01(strength01) * (Levels - 1));

        /// <summary>The 5x5 layer's procedural cells for a boxer, every emotion at every level, made ahead of time.</summary>
        public void Warm5(string boxer, VoiceProfile voice)
        {
            foreach (Emotion5 e in (Emotion5[])Enum.GetValues(typeof(Emotion5)))
                for (int level = 1; level <= Levels; level++)
                    if (Recorded5(boxer, e, level).Length == 0) Made(boxer, e, level, voice);
        }

        AudioClip[] Made(string boxer, Emotion5 e, int level, VoiceProfile voice)
        {
            var key = (boxer ?? "", e, level);
            if (_made5.TryGetValue(key, out AudioClip[] clips)) return clips;
            var (old, intensity, length) = Map(e, level);
            try { clips = VoiceSynth.Make(voice, old, $"{boxer} {e}", gridTakes, intensity, length); }
            catch (Exception ex) { Debug.LogWarning($"[VoiceMatrix] {boxer} {e} {level}: {ex.Message}"); clips = new AudioClip[0]; }
            _made5[key] = clips;
            return clips;
        }

        /// <summary>
        /// A boxer's own 5x5 recordings, read once a boxer from Resources/Voices/&lt;Boxer&gt; (the name as given, in
        /// upper case or capitalised: MATT, Matt): clips named &lt;emotion&gt;-&lt;level&gt;, optionally _&lt;n&gt; for more takes.
        /// </summary>
        AudioClip[] Recorded5(string boxer, Emotion5 e, int level)
        {
            boxer ??= "";
            if (!_recorded5.TryGetValue(boxer, out var row))
            {
                _recorded5[boxer] = row = new Dictionary<(Emotion5, int), AudioClip[]>();
                if (boxer.Length > 0)
                {
                    var found = new Dictionary<(Emotion5, int), List<AudioClip>>();
                    var seen = new HashSet<AudioClip>();
                    string cap = boxer.Substring(0, 1).ToUpperInvariant() + boxer.Substring(1).ToLowerInvariant();
                    foreach (string folder in new[] { boxer, boxer.ToUpperInvariant(), cap })
                        foreach (AudioClip clip in Resources.LoadAll<AudioClip>("Voices/" + folder))
                        {
                            if (!seen.Add(clip) || !Parse5(clip.name, out Emotion5 ce, out int cl)) continue;
                            if (!found.TryGetValue((ce, cl), out var list)) found[(ce, cl)] = list = new List<AudioClip>();
                            list.Add(clip);
                        }
                    foreach (var kv in found) row[kv.Key] = kv.Value.ToArray();
                }
            }
            return row.TryGetValue((e, level), out AudioClip[] clips) ? clips : Array.Empty<AudioClip>();
        }

        /// <summary>"hurt-3" is Hurt at level 3; "Celebrate-5_02" too, another take. Levels outside 1..5 are not read.</summary>
        public static bool Parse5(string name, out Emotion5 emotion, out int level)
        {
            emotion = Emotion5.Angry;
            level = 0;
            if (string.IsNullOrEmpty(name)) return false;
            int under = name.IndexOf('_');
            string cell = under >= 0 ? name.Substring(0, under) : name;
            int dash = cell.LastIndexOf('-');
            if (dash <= 0 || dash == cell.Length - 1) return false;
            string word = cell.Substring(0, dash);
            // Enum.TryParse takes "2" as a value too: only the emotion's name counts.
            if (int.TryParse(word, out _) || !Enum.TryParse(word, true, out emotion)) return false;
            return int.TryParse(cell.Substring(dash + 1), out level) && level >= 1 && level <= Levels;
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
                if (Parse5(clip.name, out _, out _)) continue;   // a 5x5 cell (Voices/<Boxer>/<emotion>-<level>), read by Recorded5
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
