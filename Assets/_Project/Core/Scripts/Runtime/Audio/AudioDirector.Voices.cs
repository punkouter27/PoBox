using System.Collections.Generic;
using UnityEngine;
using PoBox.Sim;

namespace PoBox.Audio
{
    /// <summary>
    /// The boxers' own voices, through the voice matrix (<see cref="VoiceMatrix"/>: recorded, archetype, procedural):
    /// breath and a grunt on a punch, pain when hit, a groan going down, panting when tired, a dazed moan when rocked,
    /// a laugh at the one just dropped, a roar at a win and a sigh at a loss or at being counted out of a free-for-all.
    /// </summary>
    public partial class AudioDirector
    {
        [Header("Voice matrix (2026-11-16)")]
        public VoiceMatrix matrix = new VoiceMatrix();
        [Range(0f, 1f)] public float dazedVolume = 0.6f, tauntVolume = 0.55f, triumphVolume = 0.9f, defeatVolume = 0.7f;
        [Tooltip("How dazed (0 to 1) a boxer on its feet has to be before it moans.")]
        [Range(0f, 1f)] public float dazedFrom = 0.55f;
        [Tooltip("The share of knockdowns the one who scored it laughs at, and of punches far harder than usual.")]
        [Range(0f, 1f)] public float tauntChance = 0.6f, bigHitTauntChance = 0.25f;

        // Voices said a little later (a laugh after the fall, not with it).
        // A 5x5 cue (level 1..5) or, with level 0, an older emotion.
        readonly List<(float at, Fighter f, Emotion e, Emotion5 e5, int level, float volume)> _later = new List<(float, Fighter, Emotion, Emotion5, int, float)>();
        readonly Dictionary<Fighter, float> _dazedNext = new Dictionary<Fighter, float>();
        readonly HashSet<Fighter> _heardOut = new HashSet<Fighter>();
        readonly Dictionary<Fighter, VoiceProfile> _profiles = new Dictionary<Fighter, VoiceProfile>();

        VoiceSet Of(Fighter f) => System.Array.IndexOf(highVoices, Base(f)) >= 0 ? highVoice : lowVoice;

        /// <summary>A boxer's name for its voice: a free-for-all twin's is its boxer's ("MATT II" is MATT).</summary>
        static string Base(Fighter f) => FreeForAll.BaseName(f.displayName);

        VoiceProfile ProfileOf(Fighter f)
        {
            if (!_profiles.TryGetValue(f, out VoiceProfile v))
                // The voice manifest's (Resources/Voices/voices.json) if it lists the boxer, else made from its name and build.
                _profiles[f] = v = matrix.ProfileFor(Base(f), VoiceProfile.For(Base(f), f.totalMass, System.Array.IndexOf(highVoices, Base(f)) >= 0));
            return v;
        }

        /// <summary>The shared set's takes of an emotion; empty for the ones it has none of.</summary>
        AudioClip[] Archetype(Fighter f, Emotion e)
        {
            VoiceSet s = Of(f);
            switch (e)
            {
                case Emotion.Exhale: return s.exhale;
                case Emotion.Effort: return s.effort;
                case Emotion.Pain: return s.pain;
                case Emotion.Groan: return s.groan;
                case Emotion.Pant: return s.pant;
                default: return null;
            }
        }

        /// <summary>
        /// Every procedural cell for this scene's boxers, made before the bell: made mid-fight, the first laugh or roar
        /// would hitch a frame. The older emotions still said directly (breath, panting, dazed, defeat), and the 5x5 layer.
        /// </summary>
        void WarmVoices()
        {
            var done = new HashSet<string>();
            foreach (Fighter f in _all)
                if (f != null && done.Add(Base(f)))
                {
                    matrix.Warm(Base(f), ProfileOf(f), Emotion.Exhale, Emotion.Pant, Emotion.Dazed, Emotion.Defeat);
                    matrix.Warm5(Base(f), ProfileOf(f));
                }
        }

        /// <summary>
        /// The tournament standard's voice API: this boxer says <paramref name="e"/> at intensity
        /// <paramref name="level"/> 1..5: the WAV voices.json lists as recorded, else its own recording of the older
        /// emotion it maps to (<see cref="VoiceMatrix.Map"/>), else the procedural imitation in its voice.
        /// </summary>
        public void Play(Fighter f, Emotion5 e, int level, float volume, float hold = 0.3f, bool must = false)
        {
            if (f == null) return;
            if (!must && _voiceFree.TryGetValue(f, out float free) && Time.time < free) return;
            AudioClip clip = matrix.Pick(Base(f), e, level, ProfileOf(f));
            Say(f, clip, volume, hold, must);
        }

        /// <summary>
        /// A boxer's voice, from its head. One sound at a time a boxer (a new one waits out <paramref name="hold"/>
        /// seconds of the last, unless it must be heard). The archetype's takes are pitched for the boxer's weight
        /// (the procedural and recorded ones are its own already); a twin a little apart from its boxer.
        /// </summary>
        void Voice(Fighter f, Emotion e, float volume, float hold, bool must = false)
        {
            if (f == null) return;
            float now = Time.time;
            if (!must && _voiceFree.TryGetValue(f, out float free) && now < free) return;
            Say(f, matrix.Pick(Base(f), e, Archetype(f, e), ProfileOf(f)), volume, hold, must);
        }

        /// <summary>A picked take from the boxer's head, keeping the boxer to one sound at a time.</summary>
        void Say(Fighter f, AudioClip clip, float volume, float hold, bool must)
        {
            if (clip == null) return;
            _voiceFree[f] = Time.time + Mathf.Max(hold, must ? 0f : clip.length * 0.6f);
            float pitch = matrix.LastTier == VoiceMatrix.Tier.Archetype ? Mathf.Clamp(Mathf.Sqrt(80f / Mathf.Max(40f, f.totalMass)), 0.85f, 1.15f) : 1f;
            pitch *= (1f + 0.04f * ((f.displayName.GetHashCode() & 0xff) / 127.5f - 1f)) * Random.Range(0.96f, 1.04f);
            // Through the boxer's own mouth if it has one (lip-sync), else from the pool at its head.
            if (!SayThroughMouth(f, clip, volume, pitch)) PlayAt(clip, f.HeadPosition, volume, pitch);
        }

        void Later(float delay, Fighter f, Emotion e, float volume) => _later.Add((Time.time + delay, f, e, Emotion5.Angry, 0, volume));
        void Later(float delay, Fighter f, Emotion5 e, int level, float volume) => _later.Add((Time.time + delay, f, Emotion.Effort, e, level, volume));

        /// <summary>The emotional reactions, once a frame: what was said for later, dazed moans, and a free-for-all's eliminations.</summary>
        void Feelings()
        {
            float now = Time.time;
            for (int i = _later.Count - 1; i >= 0; i--)
            {
                if (now < _later[i].at) continue;
                var v = _later[i];
                _later.RemoveAt(i);
                if (v.f == null || !v.f.isActiveAndEnabled) continue;
                if (v.level > 0) Play(v.f, v.e5, v.level, v.volume, 0.5f, true);
                else Voice(v.f, v.e, v.volume, 0.5f, true);
            }
            if (!Bout.SimRunning) return;
            FreeForAll ffa = FreeForAll.Instance;
            foreach (Fighter f in _all)
            {
                if (f == null || !f.isActiveAndEnabled) continue;
                // Counted out of a free-for-all: one sigh, as the rest fight on.
                if (ffa != null && ffa.IsOut(f))
                {
                    if (_heardOut.Add(f)) Voice(f, Emotion.Defeat, defeatVolume * 0.8f, 1f, true);
                    continue;
                }
                // Rocked but standing: a moan now and then, while it lasts.
                if (!f.IsDown && f.Daze01 >= dazedFrom)
                {
                    if (!_dazedNext.TryGetValue(f, out float next) || now >= next)
                    {
                        _dazedNext[f] = now + Random.Range(2.5f, 4f);
                        Voice(f, Emotion.Dazed, dazedVolume * Mathf.Lerp(0.7f, 1f, f.Daze01), 0.8f);
                    }
                }
            }
        }

        /// <summary>
        /// The result: the winner celebrates (level 5 for a stoppage or the last one standing in a free-for-all, 4 on
        /// the cards), the loser sighs; a draw, both a little pleased with it (happy, level 2).
        /// </summary>
        void ResultVoices()
        {
            Fighter winner = null, loser = null;
            int level = 5;
            if (bout != null && bout.red != null && bout.blue != null)
            {
                winner = bout.Winner;
                if (winner != null) loser = bout.Other(winner);
                else
                {
                    Later(0.6f, bout.red, Emotion5.Happy, 2, triumphVolume * 0.6f);
                    Later(0.9f, bout.blue, Emotion5.Happy, 2, triumphVolume * 0.6f);
                }
                if (bout.Decision) level = 4;
            }
            else if (FreeForAll.Instance != null) winner = FreeForAll.Instance.Winner;
            if (winner != null) Later(0.6f, winner, Emotion5.Celebrate, level, triumphVolume);
            if (loser != null) Later(1.4f, loser, Emotion.Defeat, defeatVolume);
        }

        /// <summary>How spent a boxer sounds, 0 to 1: out of energy (its reserve, if it tires, or its stamina), or dazed.</summary>
        static float Tiredness(Fighter f)
        {
            float spent = 1f - f.Stamina;
            if (f.boxer != null && f.boxer.Config != null && f.boxer.Config.fatigue_j > 0.0) spent = Mathf.Max(spent, 1f - (float)f.boxer.Reserve);
            return Mathf.Max(spent, 0.8f * f.Daze01);
        }

        /// <summary>
        /// Heard between the punches: a boxer down groans for a while; one on its feet but tired pants, oftener and
        /// louder the more spent it is, and groans now and then when it is nearly done.
        /// </summary>
        void Breathing()
        {
            if (!Bout.SimRunning) return;
            float now = Time.time;
            foreach (Fighter f in _all)
            {
                if (f == null || !f.isActiveAndEnabled) continue;
                if (FreeForAll.Instance != null && !FreeForAll.Instance.InRing(f)) continue;
                Emotion e;
                float interval, volume;
                if (f.IsDown)
                {
                    if (!_downAt.TryGetValue(f, out float since)) _downAt[f] = since = now;
                    if (now - since > groanSeconds) continue;
                    e = Emotion.Groan;
                    interval = Random.Range(2.2f, 3.8f);
                    volume = groanVolume * 0.75f;
                }
                else
                {
                    _downAt.Remove(f);
                    float tired = Tiredness(f);
                    if (tired < tiredFrom) { _nextBreath.Remove(f); continue; }
                    float k = Mathf.InverseLerp(tiredFrom, 1f, tired);
                    e = k > 0.5f && Random.value < 0.25f ? Emotion.Groan : Emotion.Pant;
                    interval = Mathf.Lerp(2.4f, 0.8f, k) * Random.Range(0.8f, 1.2f);
                    volume = Mathf.Lerp(0.15f, pantVolume, k);
                }
                if (!_nextBreath.TryGetValue(f, out float next)) { _nextBreath[f] = now + interval * Random.value; continue; }
                if (now < next) continue;
                _nextBreath[f] = now + interval;
                // A groan is the 5x5 layer's hurt, level 4 (down, or nearly done); panting stays a breath.
                if (e == Emotion.Groan) Play(f, Emotion5.Hurt, 4, volume, 0.4f);
                else Voice(f, e, volume, 0.4f);
            }
        }
    }
}
