using UnityEngine;
using UnityEngine.Audio;
using PoBox.Broadcast;
using PoBox.Fx;
using PoBox.Mj;
using PoBox.Sim;

namespace PoBox.Audio
{
    /// <summary>The boxers' own voices: breath on a punch, pain, groans and panting.</summary>
    public partial class AudioDirector
    {
        VoiceSet Of(Fighter f) => System.Array.IndexOf(highVoices, f.displayName) >= 0 ? highVoice : lowVoice;

        /// <summary>
        /// A boxer's voice, from its head. One sound at a time a boxer (a new one waits out <paramref name="hold"/>
        /// seconds of the last, unless it must be heard). Heavier boxers sound a little lower, and no two alike.
        /// </summary>
        void Voice(Fighter f, AudioClip[] takes, float volume, float hold, bool must = false)
        {
            if (f == null || takes == null || takes.Length == 0) return;
            float now = Time.time;
            if (!must && _voiceFree.TryGetValue(f, out float free) && now < free) return;
            _voiceFree[f] = now + hold;
            float pitch = Mathf.Clamp(Mathf.Sqrt(80f / Mathf.Max(40f, f.totalMass)), 0.85f, 1.15f)
                          * (1f + 0.04f * ((f.displayName.GetHashCode() & 0xff) / 127.5f - 1f)) * Random.Range(0.96f, 1.04f);
            PlayAt(Pick(takes), f.HeadPosition, volume, pitch);
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
                AudioClip[] takes;
                float interval, volume;
                if (f.IsDown)
                {
                    if (!_downAt.TryGetValue(f, out float since)) _downAt[f] = since = now;
                    if (now - since > groanSeconds) continue;
                    takes = Of(f).groan;
                    interval = Random.Range(2.2f, 3.8f);
                    volume = groanVolume * 0.75f;
                }
                else
                {
                    _downAt.Remove(f);
                    float tired = Tiredness(f);
                    if (tired < tiredFrom) { _nextBreath.Remove(f); continue; }
                    float k = Mathf.InverseLerp(tiredFrom, 1f, tired);
                    takes = k > 0.5f && Random.value < 0.25f ? Of(f).groan : Of(f).pant;
                    interval = Mathf.Lerp(2.4f, 0.8f, k) * Random.Range(0.8f, 1.2f);
                    volume = Mathf.Lerp(0.15f, pantVolume, k);
                }
                if (!_nextBreath.TryGetValue(f, out float next)) { _nextBreath[f] = now + interval * Random.value; continue; }
                if (now < next) continue;
                _nextBreath[f] = now + interval;
                Voice(f, takes, volume, 0.4f);
            }
        }
    }
}
