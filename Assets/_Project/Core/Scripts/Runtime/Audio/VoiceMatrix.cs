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
    /// hurt, insult, celebrate) by a level 1..5, as the voice manifest (Resources/Voices/voices.json,
    /// <see cref="VoiceManifest"/>) lists it: its WAV if the manifest says it exists, else the boxer's own recording
    /// of the older emotion it maps to (<see cref="Map"/>), else the procedural imitation (VoiceSynth.Render5) in
    /// the boxer's manifest voice.
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
        /// The 5x5 layer's cell for this boxer (a twin is its boxer), emotion and level (clamped to 1..5):
        ///   1. the WAV the voice manifest lists for it, if the manifest says it exists (a file it says is missing is
        ///      never asked for);
        ///   2. else the boxer's own recording of the older emotion the cell maps to (<see cref="Map"/>,
        ///      A_Voice_&lt;Boxer&gt;_&lt;Emotion&gt;_&lt;n&gt;), if there is one;
        ///   3. else the procedural imitation (<see cref="VoiceSynth.Render5"/>) in the boxer's voice.
        /// </summary>
        public AudioClip Pick(string boxer, Emotion5 e, int level, VoiceProfile voice)
        {
            level = Mathf.Clamp(level, 1, Levels);
            VoiceManifest.Character who = Manifest.Find(boxer);
            AudioClip wav = Wav(who, e, level);
            if (wav != null)
            {
                LastTier = Tier.Recorded;
                return wav;
            }
            Index();
            string name = who != null ? who.name : boxer ?? "";
            if (_recorded.TryGetValue(name, out var mine) && mine.TryGetValue(Map(e, level), out AudioClip[] rec) && rec.Length > 0)
            {
                LastTier = Tier.Recorded;
                return rec[UnityEngine.Random.Range(0, rec.Length)];
            }
            AudioClip[] made = Made(who != null ? who.id : (boxer ?? "").ToLowerInvariant(), e, level, voice);
            LastTier = Tier.Procedural;
            return made.Length > 0 ? made[UnityEngine.Random.Range(0, made.Length)] : null;
        }

        /// <summary>The manifest this matrix reads: the game's (Resources/Voices/voices.json) unless a test sets one.</summary>
        public VoiceManifest Manifest
        {
            get => _manifest ?? VoiceManifest.Loaded;
            set => _manifest = value;
        }
        VoiceManifest _manifest;
        readonly Dictionary<string, AudioClip> _wavs = new Dictionary<string, AudioClip>(StringComparer.OrdinalIgnoreCase);

        /// <summary>A boxer's voice from the manifest; <paramref name="otherwise"/> if the manifest does not list it.</summary>
        public VoiceProfile ProfileFor(string boxer, VoiceProfile otherwise)
        {
            VoiceManifest.Character who = Manifest.Find(boxer);
            return who != null && who.voice != null && who.voice.pitchHz > 0f ? who.voice.Profile() : otherwise;
        }

        /// <summary>The cell's recorded WAV, loaded once, if the manifest says it exists; null otherwise.</summary>
        AudioClip Wav(VoiceManifest.Character who, Emotion5 e, int level)
        {
            VoiceManifest.Clip cell = who?.Cell(e, level);
            if (cell == null || !cell.exists || string.IsNullOrEmpty(cell.file)) return null;
            if (_wavs.TryGetValue(cell.file, out AudioClip clip)) return clip;
            string path = "Voices/" + cell.file;
            int dot = path.LastIndexOf('.');
            if (dot > 0) path = path.Substring(0, dot);
            clip = Resources.Load<AudioClip>(path);
            if (clip == null) Debug.LogWarning($"[VoiceMatrix] voices.json lists {cell.file} as recorded, but Resources has no {path}: run PoBox/Voices/Rebuild voices.json.");
            _wavs[cell.file] = clip;
            return clip;
        }

        /// <summary>
        /// The older emotion a 5x5 cell is played as when the boxer has a recording of that one and not of the cell:
        /// angry an effort grunt, happy and celebrate a triumph, hurt pain (levels 1-3) or a groan (4-5), insult a taunt.
        /// </summary>
        public static Emotion Map(Emotion5 e, int level)
        {
            switch (e)
            {
                case Emotion5.Angry: return Emotion.Effort;
                case Emotion5.Hurt: return level <= 3 ? Emotion.Pain : Emotion.Groan;
                case Emotion5.Insult: return Emotion.Taunt;
                default: return Emotion.Triumph;
            }
        }

        /// <summary>A 0..1 strength as a level, 1..5.</summary>
        public static int Level(float strength01) => 1 + Mathf.RoundToInt(Mathf.Clamp01(strength01) * (Levels - 1));

        /// <summary>The 5x5 layer's procedural cells for a boxer, every cell not recorded, made ahead of time.</summary>
        public void Warm5(string boxer, VoiceProfile voice)
        {
            VoiceManifest.Character who = Manifest.Find(boxer);
            string id = who != null ? who.id : (boxer ?? "").ToLowerInvariant();
            foreach (Emotion5 e in (Emotion5[])Enum.GetValues(typeof(Emotion5)))
                for (int level = 1; level <= Levels; level++)
                {
                    VoiceManifest.Clip cell = who?.Cell(e, level);
                    if (cell == null || !cell.exists) Made(id, e, level, voice);
                }
        }

        AudioClip[] Made(string id, Emotion5 e, int level, VoiceProfile voice)
        {
            var key = (id, e, level);
            if (_made5.TryGetValue(key, out AudioClip[] clips)) return clips;
            try { clips = VoiceSynth.Make5(voice, e, level, id, gridTakes); }
            catch (Exception ex) { Debug.LogWarning($"[VoiceMatrix] {id} {e} {level}: {ex.Message}"); clips = new AudioClip[0]; }
            _made5[key] = clips;
            return clips;
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
                if (Parse5(clip.name, out _, out _)) continue;   // a 5x5 cell (Voices/<id>/<emotion>-<level>): voices.json lists those
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
