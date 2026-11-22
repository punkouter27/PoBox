using UnityEngine;
using UnityEngine.Audio;
using PoBox.Broadcast;
using PoBox.Fx;
using PoBox.Mj;
using PoBox.Sim;

namespace PoBox.Audio
{
    /// <summary>The sounds that follow the fight every frame: boots, gloves' air, ropes, the crowd's sections and reactions, the rocked mix, the hall, the timekeeper's sticks.</summary>
    public partial class AudioDirector
    {
        void Scrape(float dt)
        {
            if (_ring == null) _ring = FindAnyObjectByType<MjRing>();
            for (int i = 0; i < 2; i++)
            {
                AudioSource s = _scrape[i];
                MjBoxer b = _ring == null ? null : i == 0 ? _ring.red : _ring.blue;
                float k = 0f;
                if (b != null && Bout.SimRunning)
                {
                    k = Mathf.InverseLerp(scrapeSpeed.x, scrapeSpeed.y, _ring.FootSlide(b, out Vector3 at));
                    if (k > 0f) s.transform.position = at;
                }
                // Up at once, away over a fifth of a second: a scrape is short.
                float level = k * scrapeVolume * _hitsK;
                s.volume = level > s.volume ? level : Mathf.MoveTowards(s.volume, level, dt * 5f * scrapeVolume);
                s.pitch = Mathf.Lerp(0.75f, 1.35f, k);
                if (s.volume > 0.001f && !s.isPlaying) s.Play();
                else if (s.volume <= 0.001f && s.isPlaying) s.Pause();

                // A pivot: one squeak per turn, not a stream of them.
                if (b == null || !Bout.SimRunning) continue;
                float spin = _ring.FootSpin(b, out Vector3 foot);
                if (spin > squeakSpin && Time.time - _squeakAt[i] > 0.35f)
                {
                    _squeakAt[i] = Time.time;
                    PlayAt(_squeak, foot, squeakVolume * Mathf.Lerp(0.4f, 1f, Mathf.InverseLerp(squeakSpin, 3f * squeakSpin, spin)) * _hitsK,
                           Mathf.Lerp(0.9f, 1.2f, Mathf.InverseLerp(squeakSpin, 3f * squeakSpin, spin)));
                }
            }
            Air(dt);
        }

        /// <summary>
        /// The eight sections: together the other half of the bed (eight uncorrelated places in the loop add up to
        /// about the square root of eight, and the distance takes about a third off), each leaning with the scorer.
        /// </summary>
        void Sections(float level)
        {
            float side = ArenaMood.Side;                 // -1 red, +1 blue; blue's half is towards +x +z
            for (int k = 0; k < _sections.Length; k++)
            {
                if (_sections[k] == null) continue;
                float behind = Vector2.Dot(_sectionDir[k], new Vector2(0.7071f, 0.7071f)) * side;   // +1 behind the scorer
                float lean = behind > 0f ? 1f + sectionLean * behind : 1f + 0.5f * sectionLean * behind;
                _sections[k].volume = level * 0.5f * 1.5f / Mathf.Sqrt(_sections.Length) * lean;
            }
        }

        /// <summary>Each glove's air: level and pitch from the speed MuJoCo measures, placed at the glove.</summary>
        void Air(float dt)
        {
            for (int i = 0; i < _air.Length; i++)
            {
                AudioSource s = _air[i];
                if (s == null) continue;
                MjBoxer b = _ring == null ? null : i < 2 ? _ring.red : _ring.blue;
                float k = 0f;
                if (b != null && Bout.SimRunning)
                {
                    Vector3 v = _ring.GloveVelocity(b, i % 2);
                    k = Mathf.InverseLerp(whooshSpeed.x, whooshSpeed.y, v.magnitude);
                    if (k > 0f) s.transform.position = b.GlovePosition(i % 2);
                }
                float level = k * k * whooshVolume * _hitsK;
                // Up at once, away over a tenth of a second: the air stops when the glove does.
                s.volume = level > s.volume ? level : Mathf.MoveTowards(s.volume, level, dt * 10f * whooshVolume);
                s.pitch = Mathf.Lerp(0.8f, 1.5f, k);
                if (s.volume > 0.001f && !s.isPlaying) s.Play();
                else if (s.volume <= 0.001f && s.isPlaying) s.Pause();
            }
        }

        void Ropes(float dt)
        {
            // The creak: the ropes as drawn, from the most bowed one.
            if (_ropes == null) _ropes = FindAnyObjectByType<RopeFlex>();
            float k = _ropes != null && Bout.SimRunning ? Mathf.InverseLerp(ropeBow.x, ropeBow.y, _ropes.Strain) : 0f;
            if (k > 0f) _creak.transform.position = _ropes.StrainAt;
            _creak.pitch = Mathf.Lerp(0.8f, 1.25f, k);
            Fade(_creak, k * ropeVolume * _hitsK, dt * 3f * ropeVolume);

            // The twang: any boxer arriving hard, not leaning harder (the load has jumped past it from well under it).
            if (_ring == null || !_ring.Ready || !Bout.SimRunning) return;
            MjBoxer[] boxers = _ring.boxers;
            if (boxers == null || boxers.Length == 0) { _pair[0] = _ring.red; _pair[1] = _ring.blue; boxers = _pair; }
            if (_lastRope.Length != boxers.Length)
            {
                _lastRope = new float[boxers.Length];
                _lastTwang = new float[boxers.Length];
                for (int i = 0; i < boxers.Length; i++) _lastTwang[i] = -99f;
            }
            for (int i = 0; i < boxers.Length; i++)
            {
                if (boxers[i] == null) continue;
                float load = _ring.RopeLoad(boxers[i], out Vector3 at);
                if (load >= ropeTwang && _lastRope[i] < ropeTwang * 0.5f && Time.unscaledTime - _lastTwang[i] > 0.4f)
                {
                    _lastTwang[i] = Time.unscaledTime;
                    PlayAt(_twang, at, Mathf.Lerp(0.4f, 1f, Mathf.InverseLerp(ropeTwang, ropeTwang * 3f, load)) * ropeVolume * 2f, Random.Range(0.9f, 1.1f));
                }
                _lastRope[i] = load;
            }
        }

        /// <summary>1 for the close shots, -1 for the wide ones, 0 for the rest.</summary>
        static float Closeness(Shot shot)
        {
            switch (shot)
            {
                case Shot.Impact: case Shot.ShoulderRed: case Shot.ShoulderBlue: case Shot.LowRopes: return 1f;
                case Shot.Wide: case Shot.Overhead: case Shot.HighCorner: return -1f;
                default: return 0f;
            }
        }

        void Rocked(Shot shot, float dt)
        {
            // The boxer the picture is about: the close-up's, the far one over a shoulder, or with both in it
            // the worse of the two, felt at half.
            float share = 1f;
            Fighter f = null;
            if (bout != null)
            {
                int focused = BroadcastDirector.Instance != null ? BroadcastDirector.Instance.Focused : -1;
                if (shot == Shot.Impact) f = focused == 1 ? bout.blue : focused == 0 ? bout.red : _victim;
                else if (shot == Shot.ShoulderRed) f = bout.blue;
                else if (shot == Shot.ShoulderBlue) f = bout.red;
                else
                {
                    share = 0.5f;
                    f = bout.red == null || (bout.blue != null && bout.blue.Daze01 > bout.red.Daze01) ? bout.blue : bout.red;
                }
            }
            float target = f != null && f.Staggered ? f.Daze01 * share : 0f;
            // In fast, out with the daze.
            _rock = Mathf.MoveTowards(_rock, target, dt * (target > _rock ? 4f : 0.6f));
            if (_lowPass != null)
            {
                _lowPass.enabled = _rock > 0.01f;
                _lowPass.cutoffFrequency = 22000f * Mathf.Pow(rockedCutoff / 22000f, _rock);
            }
            _heart.pitch = 1f + 0.3f * _rock;
            Fade(_heart, _rock * heartbeatVolume, dt);
        }

        /// <summary>Takes a looping source towards a level, playing it only while it can be heard.</summary>
        void Reactions()
        {
            if (!synth || ImpactSynth.CrowdOoh == null) return;
            Bout b = Bout.Instance;
            float now = Time.unscaledTime;
            if (b == null || b.Phase != BoutPhase.Fight) { _lastLanded = now; _clinchFor = 0f; return; }
            // A big one thrown, nothing touched for 0.35 s: it whistled past.
            if (_swinger != null && now - _swungAt > 0.35f)
            {
                _swinger = null;
                float e = Mathf.Clamp01(Excitement.Value);
                if (now - _lastOoh > 4f && Random.value < Mathf.Lerp(0.25f, 0.7f, e))
                {
                    _lastOoh = now;
                    PlayCrowd(ImpactSynth.CrowdOoh, Mathf.Lerp(0.25f, 0.6f, e));
                }
            }
            // Chest to chest, or nothing landing: the room lets them know.
            if (b.red != null && b.blue != null && b.red.pelvis != null && b.blue.pelvis != null)
            {
                bool close = (b.red.pelvis.transform.position - b.blue.pelvis.transform.position).magnitude < 0.55f;
                _clinchFor = close ? _clinchFor + Time.unscaledDeltaTime : 0f;
            }
            bool stalled = _clinchFor > 4f || now - _lastLanded > 14f;
            if (stalled && now - _lastBoo > 12f)
            {
                _lastBoo = now;
                _lastLanded = now - 7f;     // again in a while if it goes on
                PlayCrowd(ImpactSynth.CrowdBoo, 0.45f);
            }
        }

        AudioReverbZone _hall;
        bool _hallLooked;

        /// <summary>
        /// The arena's reverb follows the shot (2026-10-31): an indoor arena of a few thousand seats, about 2.3 s, its
        /// first reflections 35 ms late; drier on the close-ups (we are at the ropes), wetter out wide (we hear the room).
        /// The zone over the ring was Unity's Auditorium preset whatever the camera did.
        /// </summary>
        void Hall(float close)
        {
            if (!_hallLooked) { _hallLooked = true; _hall = FindAnyObjectByType<AudioReverbZone>(); }
            if (_hall == null) return;
            if (_hall.reverbPreset != AudioReverbPreset.User) _hall.reverbPreset = AudioReverbPreset.User;
            float wide = Mathf.InverseLerp(1f, -1f, close);                    // 0 close, 1 wide
            _hall.room = (int)Mathf.Lerp(-1500f, -650f, wide);
            _hall.roomHF = (int)Mathf.Lerp(-1200f, -700f, wide);
            _hall.roomLF = 0;
            _hall.decayTime = Mathf.Lerp(1.9f, 2.5f, wide);
            _hall.decayHFRatio = 0.62f;
            _hall.reflections = (int)Mathf.Lerp(-1600f, -900f, wide);
            _hall.reflectionsDelay = 0.035f;
            _hall.reverb = (int)Mathf.Lerp(-900f, -250f, wide);
            _hall.reverbDelay = 0.03f;
            _hall.diffusion = 100f;
            _hall.density = 100f;
        }

        /// <summary>Ten seconds left in the round: the timekeeper knocks his sticks three times.</summary>
        void TenSeconds()
        {
            Bout b = bout != null ? bout : Bout.Instance;
            if (b == null || _clack == null) return;
            bool ten = b.Phase == BoutPhase.Fight && b.RoundTimeLeft <= 10f && b.RoundTimeLeft > 0f;
            if (!ten) { if (b.Phase != BoutPhase.Fight && b.Phase != BoutPhase.Count) _clacksAt = -1f; return; }
            if (_clacksAt < 0f) { _clacksAt = Time.unscaledTime; _clacks = 0; }
            if (_clacks < 3 && Time.unscaledTime >= _clacksAt + _clacks * 0.22f)
            {
                PlayRing(_clack, 0.8f);
                _clacks++;
            }
        }
    }
}
