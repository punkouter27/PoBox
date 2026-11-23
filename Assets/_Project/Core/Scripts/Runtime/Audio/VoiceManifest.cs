using System;
using System.Collections.Generic;
using UnityEngine;

namespace PoBox.Audio
{
    /// <summary>
    /// The voice manifest, Assets/_Project/Data/Resources/Voices/voices.json (schema po-voice-manifest/1): every
    /// boxer of the roster by the five <see cref="Emotion5"/>s by levels 1..5, each cell's WAV
    /// (&lt;id&gt;/&lt;emotion&gt;-&lt;level&gt;.wav under the Voices folder) and whether it exists, and the boxer's
    /// procedural voice. The editor's PoBox/Voices/Rebuild voices.json writes it (and the exists flags); the game
    /// only reads it. A twin ("MATT II") is its boxer's entry.
    /// </summary>
    [Serializable]
    public class VoiceManifest
    {
        public const string Schema = "po-voice-manifest/1";
        /// <summary>Where it sits: Resources/Voices/voices.json, loaded as the TextAsset "Voices/voices".</summary>
        public const string ResourcePath = "Voices/voices";
        public const string Root = "Assets/_Project/Data/Resources/Voices";
        public static readonly string[] Emotions = { "angry", "happy", "hurt", "insult", "celebrate" };

        [Serializable]
        public class Voice
        {
            public float pitchHz, formantScale, breath, rasp, vibratoHz, vibratoDepth, tempo = 1f, tremor, growl;
            public string timbre;

            public VoiceProfile Profile() => new VoiceProfile(pitchHz, formantScale, rasp, breath, tremor, growl, vibratoHz, vibratoDepth, tempo > 0f ? tempo : 1f);

            public static Voice From(VoiceProfile v, string timbre) => new Voice
            {
                pitchHz = v.f0, formantScale = v.formants, breath = v.breath, rasp = v.rasp, vibratoHz = v.vibratoHz,
                vibratoDepth = v.vibratoDepth, tempo = v.tempo, tremor = v.tremor, growl = v.growl, timbre = timbre,
            };
        }

        [Serializable]
        public class Clip
        {
            public int level;
            public string file;
            public bool exists;
        }

        /// <summary>One list a <see cref="Emotion5"/>, five clips each (levels 1..5).</summary>
        [Serializable]
        public class Clips
        {
            public Clip[] angry = new Clip[0], happy = new Clip[0], hurt = new Clip[0], insult = new Clip[0], celebrate = new Clip[0];

            public Clip[] Of(Emotion5 e)
            {
                switch (e)
                {
                    case Emotion5.Angry: return angry;
                    case Emotion5.Happy: return happy;
                    case Emotion5.Hurt: return hurt;
                    case Emotion5.Insult: return insult;
                    default: return celebrate;
                }
            }

            public void Set(Emotion5 e, Clip[] c)
            {
                switch (e)
                {
                    case Emotion5.Angry: angry = c; break;
                    case Emotion5.Happy: happy = c; break;
                    case Emotion5.Hurt: hurt = c; break;
                    case Emotion5.Insult: insult = c; break;
                    default: celebrate = c; break;
                }
            }
        }

        [Serializable]
        public class Character
        {
            /// <summary>Lower case, the folder of its WAVs ("matt").</summary>
            public string id;
            /// <summary>The name on the scoreboard ("MATT").</summary>
            public string name;
            public Voice voice;
            public Clips clips = new Clips();

            /// <summary>The cell for an emotion and level; null if the manifest has none.</summary>
            public Clip Cell(Emotion5 e, int level)
            {
                Clip[] list = clips?.Of(e);
                if (list == null) return null;
                foreach (Clip c in list) if (c != null && c.level == level) return c;
                return null;
            }
        }

        public string schema = Schema;
        public string game = "PoBox";
        public string generated = "";
        public string root = Root;
        public string[] emotions = Emotions;
        public int[] levels = { 1, 2, 3, 4, 5 };
        public string fallback = "procedural";
        public List<Character> characters = new List<Character>();

        /// <summary>A cell's file, relative to the Voices folder: "matt/hurt-3.wav".</summary>
        public static string FileFor(string id, Emotion5 e, int level) => $"{id.ToLowerInvariant()}/{Emotions[(int)e]}-{level}.wav";

        /// <summary>The boxer a name is (a twin's is its boxer's, "MATT II" is MATT), by id or name, case not minded; null if not listed.</summary>
        public Character Find(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (name.EndsWith(" II")) name = name.Substring(0, name.Length - 3);
            foreach (Character c in characters)
                if (c != null && (string.Equals(c.id, name, StringComparison.OrdinalIgnoreCase) || string.Equals(c.name, name, StringComparison.OrdinalIgnoreCase)))
                    return c;
            return null;
        }

        /// <summary>
        /// The manifest the generator writes: every boxer of <paramref name="roster"/> (scoreboard name, mass in kg) by
        /// 5 emotions by 5 levels, <paramref name="exists"/> asked of each file (relative to the Voices folder), the
        /// voices already in <paramref name="old"/> kept and a new boxer's made from <see cref="VoiceProfile.For"/>.
        /// </summary>
        public static VoiceManifest Rebuild(VoiceManifest old, IList<(string name, float mass)> roster, Func<string, bool> exists, string generated)
        {
            var m = new VoiceManifest { generated = generated ?? "" };
            foreach (var (rawName, mass) in roster)
            {
                string name = rawName.ToUpperInvariant(), id = rawName.ToLowerInvariant();
                Character was = old?.Find(name);
                Voice voice = was != null && was.voice != null && was.voice.pitchHz > 0f ? was.voice : null;
                if (voice == null)
                {
                    VoiceProfile p = VoiceProfile.For(name, mass, false);
                    voice = Voice.From(p, VoiceProfile.Timbre(name, p));
                }
                var c = new Character { id = id, name = name, voice = voice };
                foreach (Emotion5 e in (Emotion5[])Enum.GetValues(typeof(Emotion5)))
                {
                    var cells = new Clip[5];
                    for (int level = 1; level <= 5; level++)
                    {
                        string file = FileFor(id, e, level);
                        cells[level - 1] = new Clip { level = level, file = file, exists = exists != null && exists(file) };
                    }
                    c.clips.Set(e, cells);
                }
                m.characters.Add(c);
            }
            return m;
        }

        /// <summary>"N of M clips recorded; missing: matt/angry-1.wav, ..." (the first few missing, and how many more).</summary>
        public string Summary(int show = 8)
        {
            int all = 0, have = 0;
            var missing = new List<string>();
            foreach (Character c in characters)
                foreach (Emotion5 e in (Emotion5[])Enum.GetValues(typeof(Emotion5)))
                    foreach (Clip clip in c.clips.Of(e))
                    {
                        all++;
                        if (clip.exists) have++;
                        else missing.Add(clip.file);
                    }
            string list = missing.Count == 0 ? "none" : string.Join(", ", missing.GetRange(0, Mathf.Min(show, missing.Count)))
                                                       + (missing.Count > show ? $" and {missing.Count - show} more" : "");
            return $"{have} of {all} clips recorded; missing: {list}";
        }

        public static VoiceManifest Parse(string json)
        {
            try
            {
                // Start from no schema: FromJson would keep the field's initializer, so "{}" would pass for a manifest.
                var m = new VoiceManifest { schema = null };
                JsonUtility.FromJsonOverwrite(json, m);
                return m.schema == Schema && m.characters != null ? m : null;
            }
            catch (Exception) { return null; }
        }

        static VoiceManifest _loaded;
        static bool _tried;

        /// <summary>
        /// The game's manifest, read once. Missing or not readable: an empty one (every cell procedural), and one warning.
        /// </summary>
        public static VoiceManifest Loaded
        {
            get
            {
                if (_tried) return _loaded;
                _tried = true;
                var text = Resources.Load<TextAsset>(ResourcePath);
                _loaded = text != null ? Parse(text.text) : null;
                if (_loaded == null)
                {
                    Debug.LogWarning("[VoiceManifest] Resources/Voices/voices.json is missing or not a " + Schema + ": every voice is procedural.");
                    _loaded = new VoiceManifest();
                }
                return _loaded;
            }
        }

        /// <summary>Read the file again (the editor's generator, after writing it).</summary>
        public static void Reload() { _tried = false; _loaded = null; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Reload();
    }
}
