using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using PoBox.Audio;
using PoBox.Broadcast;
using PoBox.Diag;
using PoBox.Fx;
using PoBox.League;
using PoBox.Mj;
using PoBox.Online;
using PoBox.Sim;
using static PoBox.UI.Ui;

namespace PoBox.UI
{
    /// <summary>The optional pieces: the pick before the bell, chat plays, the ring map, the win line's samples and the debug readings.</summary>
    public partial class HudView
    {
        static readonly string[] DebugKeys =
        {
            "FRAME", "WORST", "PHYSICS", "SETPASS", "TRIS", "GC", "MEMORY", "SCALE", "SHOT", "VOICES", "CLOSING", "STRESS",
            "DAZE", "CRITIC", "SOUND", "USUAL", "BRAINS", "GPU",
        };

        /// <summary>The pick before the first bell: either corner, at the ladder's odds (League/Prediction.cs).</summary>
        void HookPredict()
        {
            Hook(_predictRed, () => Pick(0));
            Hook(_predictBlue, () => Pick(1));
        }

        void Pick(int corner)
        {
            if (bout == null || !SettingsPage.Bets || !PickOpen) return;
            Prediction.Pick(corner, Prediction.Odds(Chance(corner)));
            RefreshPredict();
        }

        bool PickOpen => bout.Round == 1 && (bout.Phase == BoutPhase.WalkOn || bout.Phase == BoutPhase.Intro) && bout.red != null && bout.blue != null;

        /// <summary>The ladder's chance of this corner winning (an even chance off the ladder).</summary>
        float Chance(int corner)
        {
            if (league == null || bout.red == null || bout.blue == null) return 0.5f;
            float red = league.Expected(bout.red.displayName, bout.blue.displayName);
            return corner == 0 ? red : 1f - red;
        }

        void RefreshPredict()
        {
            if (_predict == null) return;
            bool open = SettingsPage.Bets && PickOpen;
            _predict.EnableInClassList("hidden", !open);
            if (!open) return;
            bool picked = Prediction.Picked >= 0;
            _predictRed?.EnableInClassList("hidden", picked);
            _predictBlue?.EnableInClassList("hidden", picked);
            if (picked)
                Set(_predictCoins, $"YOUR PICK {(Prediction.Picked == 0 ? bout.red : bout.blue).displayName} · {Prediction.Coins} COINS");
            else
            {
                Set(_predictRed, $"{bout.red.displayName} x{Prediction.Odds(Chance(0)):0.0}");
                Set(_predictBlue, $"{bout.blue.displayName} x{Prediction.Odds(Chance(1)):0.0}");
                Set(_predictCoins, $"PICK · {Prediction.Stake} OF {Prediction.Coins}");
            }
        }

        /// <summary>Chat plays (Net/TwitchChat.cs): the corners' votes on a line under the scoreboard, and a cube thrown when chat asks.</summary>
        void RefreshChat()
        {
            if (_chat == null) return;
            _twitch.Poll();
            _chat.RemoveFromClassList("hidden");
            Fighter red = bout.red, blue = bout.blue;
            if (red == null || blue == null) return;
            int r = _twitch.Votes("red") + _twitch.Votes(red.displayName.ToLowerInvariant());
            int b = _twitch.Votes("blue") + _twitch.Votes(blue.displayName.ToLowerInvariant());
            Set(_chat, _twitch.Connected ? $"CHAT · {red.displayName} {r} · {blue.displayName} {b}" : $"CHAT · #{_twitch.Channel} ···");
            if (Bout.Fighting && _twitch.TakeCube())
            {
                Fighter at = Random.value < 0.5f ? red : blue;
                if (TwitchChat.ThrowCube(at)) SimBus.Say($"Chat throws a cube at {at.displayName}!", 1);
            }
        }

        /// <summary>
        /// The settings' MAP: up while there is a fight to watch (not under the pick before the bell, the result or anything
        /// open), fed every frame; a new round starts its hold on the centre again.
        /// </summary>
        void RefreshMap(float now)
        {
            if (_ringMap == null) return;
            BoutPhase phase = bout.Phase;
            bool show = SettingsPage.Map && bout.red != null && bout.blue != null && !MenuOpen && !DebugOpen
                && (phase == BoutPhase.Fight || phase == BoutPhase.Count || phase == BoutPhase.Intro || phase == BoutPhase.RoundBreak)
                && (_predict == null || _predict.ClassListContains("hidden"))
                // The knockdown picture and the punch card use the same corner (2026-10-31 browse): the map steps aside.
                && (_pip == null || _pip.ClassListContains("hidden"))
                && !(_root.Q<VisualElement>("punch-card") is VisualElement card && !card.ClassListContains("hidden"));
            _ringMap.EnableInClassList("hidden", !show);
            if (phase == BoutPhase.Intro && _lastMapRound != bout.Round) { _lastMapRound = bout.Round; _ringMap.Clear(); }
            if (!show) return;
            Fighter red = bout.red, blue = bout.blue;
            Vector3 r = red.CenterOfMass, b = blue.CenterOfMass;
            float dt = phase == BoutPhase.Fight ? Time.deltaTime : 0f;
            _ringMap.Set(new Vector2(r.x, r.z), red.IsDown, new Vector2(b.x, b.z), blue.IsDown,
                new Vector2(red.ringCentre.x, red.ringCentre.z), red.ringHalf > 0f ? red.ringHalf : 3.05f, dt, now);
        }

        int _lastMapRound;

        /// <summary>The win probability twice a second while the fight is on, and where each round began.</summary>
        void SampleWin(float now)
        {
            if (bout.Phase != BoutPhase.Fight || now < _nextWinSample) return;
            _nextWinSample = now + WinSampleSeconds;
            if (bout.Round != _winRound)
            {
                _winRound = bout.Round;
                _winRecord.roundStarts.Add(_winRecord.share.Count);
            }
            _winRecord.share.Add(Excitement.RedShare);
            _winRecord.excitement.Add(Excitement.Value);
            if (SettingsPage.Stats) _stripWin?.Show(_winRecord);
        }

        void RefreshDebug()
        {
            PerfTelemetry perf = PerfTelemetry.Instance;
            if (perf == null || _debugValues.Length < DebugKeys.Length) return;
            Fighter red = bout != null ? bout.red : null, blue = bout != null ? bout.blue : null;
            if (_audio == null) _audio = FindAnyObjectByType<AudioDirector>();
            AudioDirector audio = _audio;
            Excitement ex = Excitement.Instance;

            _debugValues[0].text = $"{perf.FrameMs:0.0} ms";
            _debugValues[1].text = $"{perf.WorstMs:0.0} ms";
            _debugValues[2].text = $"{perf.PhysicsMs:0.00} ms";
            _debugValues[3].text = perf.SetPass.ToString();
            _debugValues[4].text = perf.Triangles >= 1000 ? $"{perf.Triangles / 1000f:0.0}k" : perf.Triangles.ToString();
            // Averaged: the single-frame figure jumps whenever the editor itself is asked for anything.
            _debugValues[5].text = $"{perf.GcAverageKb:0.0} kB";
            _debugValues[6].text = $"{perf.MemoryMb:0} MB";
            _debugValues[7].text = $"{perf.RenderScale:0.00}";
            _debugValues[8].text = director != null ? director.Current.ToString() : "-";
            _debugValues[9].text = audio != null ? audio.ActiveVoices.ToString() : "-";
            _debugValues[10].text = Excitement.Instance != null ? $"{Excitement.Instance.ClosingSpeed:0.0} m/s" : "-";
            _debugValues[11].text = red != null && blue != null ? $"{red.PeakStress * 100f:0}% / {blue.PeakStress * 100f:0}%" : "-";
            _debugValues[12].text = red != null && blue != null ? $"{red.Daze:0} / {blue.Daze:0}" : "-";
            _debugValues[13].text = ex != null && ex.HasCritic ? $"{ex.CriticEdge:+0.00;-0.00}" : "none";
            _debugValues[14].text = audio != null ? $"{audio.PeakDb:0} dB" : "-";
            _debugValues[15].text = ex != null ? $"{ex.UsualImpulse:0.0} N·s" : "-";
            _debugValues[16].text = $"{perf.InferMs:0.00} ms";
            _debugValues[17].text = perf.GpuMs > 0f ? $"{perf.GpuMs:0.0} ms" : "n/a";
        }
    }
}
