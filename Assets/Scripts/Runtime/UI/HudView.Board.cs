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
    /// <summary>The scoreboard, the pods and what is written over the picture: bars, clock, tape, status, the break card, the banner and the radar gun.</summary>
    public partial class HudView
    {
        void OnLine(string text, int priority)
        {
            // A statistic does not talk over a knockdown call that has only just gone up.
            if (priority < _tickerPriority && Time.unscaledTime - _tickerAt < 2.5f) return;
            _tickerPriority = priority;
            _tickerAt = Time.unscaledTime;
            if (_ticker != null) _ticker.text = text;
        }

        /// <summary>The name of the punch this corner has just thrown, while it is still on the badge; otherwise null.</summary>
        string PunchWord(int corner) => Time.time - _punchAt[corner] < PunchShown ? PunchNames[(int)_punch[corner]] : null;

        void OnPunch(Fighter f, PunchType type, int hand)
        {
            if (bout == null) return;
            int corner = f == bout.blue ? 1 : 0;
            _punch[corner] = type;
            _punchAt[corner] = Time.time;
        }

        void OnHit(HitEvent e)
        {
            if (e.clean && bout != null && e.attacker != null && bout.Phase == BoutPhase.Fight)
            {
                int who = e.attacker == bout.red ? 0 : 1;
                if (e.zone == PartKind.Head) _roundHead[who]++; else _roundBody[who]++;
                if (e.impulse > _roundBestHit.impulse) _roundBestHit = e;
            }
            Excitement ex = Excitement.Instance;
            // The radar gun: with STATS every hard clean punch; without, only the rare one far over the usual.
            float over = SettingsPage.Stats ? flashOverUsual : radarOverUsual;
            if (!e.clean || ex == null || e.impulse < ex.UsualImpulse * over || _flash == null) return;
            _flashPoint = e.point;
            _flashAt = Time.unscaledTime;
            _flash.text = $"{e.gloveSpeed * 3.6f:0} km/h";
        }

        string Badge(Fighter f)
        {
            // The plate has room for the name and a rank; the rating and the rest are on the walk-on card.
            if (league != null && league.Find(f.displayName) != null) return "#" + league.RankOf(f.displayName);
            return f.profile != null && f.profile.IsTrained ? "AI" : "BOT";
        }

        /// <summary>The line under a fighter's name on the walk-on card: rank, rating, record, weight, how long it trained.</summary>
        string WalkOnDetail(Fighter f)
        {
            var s = new System.Text.StringBuilder();
            LeagueTable.Row row = league != null ? league.Find(f.displayName) : null;
            if (row != null) s.Append($"#{league.RankOf(f.displayName)} · {row.elo:0} ELO · {row.wins}-{row.losses}-{row.draws}");
            if (f.totalMass > 0f) s.Append($"{(s.Length > 0 ? " · " : "")}{f.totalMass:0} kg");
            if (f.profile != null && f.profile.IsTrained) s.Append($" · GEN {f.profile.generation}");
            return s.ToString();
        }

        void RefreshBars()
        {
            Fighter red = bout.red, blue = bout.blue;
            if (red == null || blue == null) return;
            if (_redHp != null) _redHp.style.width = Length.Percent(red.Health);
            if (_blueHp != null) _blueHp.style.width = Length.Percent(blue.Health);
            // The pale chunk behind each bar follows the health too, but its stylesheet holds it back 0.6 s first.
            if (_ghost[0] != null) _ghost[0].style.width = Length.Percent(red.Health);
            if (_ghost[1] != null) _ghost[1].style.width = Length.Percent(blue.Health);
            if (_momFill != null) _momFill.style.width = Length.Percent(Excitement.RedShare * 100f);
        }

        void RefreshClock()
        {
            if (_round != null) _round.text = "R" + bout.Round;
            if (_clock != null) _clock.text = Bout.ClockText(bout.RoundTimeLeft);
            Fighter red = bout.red, blue = bout.blue;
            if (red == null || blue == null) return;
            SetPop(_redHpText, Mathf.CeilToInt(red.Health).ToString());
            SetPop(_blueHpText, Mathf.CeilToInt(blue.Health).ToString());
            int share = Mathf.RoundToInt(Excitement.RedShare * 100f);
            if (_momRed != null) _momRed.text = share + "%";
            if (_momBlue != null) _momBlue.text = (100 - share) + "%";
        }

        void RefreshTape()
        {
            Fighter red = bout.red, blue = bout.blue;
            if (red == null || blue == null) return;
            SetPop(_landedRed, $"{red.stats.landed}/{red.stats.thrown}");
            SetPop(_landedBlue, $"{blue.stats.landed}/{blue.stats.thrown}");
            SetPop(_peakRed, $"{red.stats.peakImpulse:0} N·s");
            SetPop(_peakBlue, $"{blue.stats.peakImpulse:0} N·s");
        }

        /// <summary>The two pods: where each fighter has been hit, how dazed it is, and its balance.</summary>
        void RefreshPods()
        {
            Fighter red = bout.red, blue = bout.blue;
            if (red == null || blue == null) return;
            float scale = HitScale(red, blue);
            if (_hitRed != null) _hitRed.Set(red.taken, scale, red.Daze01);
            if (_hitBlue != null) _hitBlue.Set(blue.taken, scale, blue.Daze01);
            if (_balRed != null) _balRed.Set(red.footOffset, red.footDown, red.CaptureOffset, red.BalanceMargin, red.IsDown && !red.IsRising);
            if (_balBlue != null) _balBlue.Set(blue.footOffset, blue.footDown, blue.CaptureOffset, blue.BalanceMargin, blue.IsDown && !blue.IsRising);
            RefreshStatus(0, red);
            RefreshStatus(1, blue);
        }

        /// <summary>
        /// The tank: for a boxer whose config has it tire, its muscles' reserve (MjBoxer.Reserve, the one thing that
        /// weakens its drives), otherwise the game's stamina; the badge under it: down, getting up, dazed, the punch
        /// just thrown, or in its guard; and the two flags, ON THE ROPES and TIRED.
        /// </summary>
        void RefreshStatus(int corner, Fighter f)
        {
            Mj.MjBoxer body = f.boxer;
            bool tires = body != null && body.Bound && body.Config.fatigue_j > 0.0;
            float tank = tires ? (float)body.Reserve : f.Stamina;
            // The reserve's drives start to weaken under half and lose fatigue_weak of their strength at empty:
            // the tank goes amber where they start and TIRED goes up halfway down, a fifth short at the default 0.4.
            float low = tires ? 0.5f : 0.35f, tired = tires ? 0.25f : 0.35f;
            if (_tank[corner] != null)
            {
                _tank[corner].style.width = Length.Percent(tank * 100f);
                _tank[corner].EnableInClassList("tank-fill--low", tank < low);
            }
            Flag(_tired[corner], tank < tired);
            Flag(_ropes[corner], f.OnTheRopes);

            if (_act[corner] == null) return;
            bool punching = Time.time - _punchAt[corner] < PunchShown;
            bool alarm = f.IsDown || f.Staggered;
            Set(_act[corner], f.IsRising ? "RISING" : f.IsDown ? "DOWN" : f.Staggered ? "DAZED" : punching ? PunchNames[(int)_punch[corner]] : "GUARD");
            _act[corner].EnableInClassList("act--alarm", alarm);
            _act[corner].EnableInClassList("act--punch", punching && !alarm);
        }

        /// <summary>A flag shown or put away; one going up swells as it does.</summary>
        void Flag(Label flag, bool on)
        {
            if (flag == null || flag.ClassListContains("hidden") != on) return;
            flag.EnableInClassList("hidden", !on);
            if (on) Pop(flag);
        }

        /// <summary>Between rounds: the cards as they stand, and landed of thrown, punches stopped and the share of the centre in the round just ended.</summary>
        void FillBreak()
        {
            Set(_breakTitle, "END OF ROUND " + bout.Round);
            for (int j = 0; j < Judges.Count; j++)
            {
                if (_breakCards[j] == null) continue;
                int r = bout.judges.total[j, 0], b = bout.judges.total[j, 1];
                Set(_breakScores[j], bout.judges.Card(j));
                _breakCards[j].EnableInClassList("judge--red", r > b);
                _breakCards[j].EnableInClassList("judge--blue", b > r);
            }
            for (int i = 0; i < 2; i++)
            {
                Fighter f = i == 0 ? bout.red : bout.blue;
                if (f == null) continue;
                Set(_breakLanded[i], $"{f.stats.landed - _roundStart[i].landed}/{f.stats.thrown - _roundStart[i].thrown}");
                Set(_breakBlocked[i], (f.stats.blocked - _roundStart[i].blocked).ToString());
            }
            for (int i = 0; i < 2; i++) Set(_breakSplit[i], $"{_roundHead[i]} / {_roundBody[i]}");
            HitEvent best = _roundBestHit;
            Set(_breakBest, best.attacker == null ? "" :
                $"PUNCH OF THE ROUND  {best.attacker.displayName} · {(best.hand < 0 ? "LEFT" : "RIGHT")} {PunchNames[(int)best.punch]} · {best.gloveSpeed * 3.6f:0} km/h · {best.impulse:0} N·s");
            float centre = bout.judges.LastCentreShare;
            Set(_breakCentre[0], $"{centre * 100f:0}%");
            Set(_breakCentre[1], $"{(1f - centre) * 100f:0}%");
            _breakWin?.Show(_winRecord);
        }

        /// <summary>The impulse that is drawn fully hot: the most that has landed on any one part of either fighter, and never less than a few punches' worth.</summary>
        static float HitScale(Fighter red, Fighter blue)
        {
            float most = 40f;
            for (int i = 0; i < red.taken.Length; i++) most = Mathf.Max(most, Mathf.Max(red.taken[i], blue.taken[i]));
            return most;
        }

        /// <summary>Set, and if the number changed, swell it until the next tick.</summary>
        void SetPop(Label l, string text)
        {
            if (l == null || l.text == text) return;
            bool first = string.IsNullOrEmpty(l.text);
            l.text = text;
            if (!first) Pop(l);
        }

        void Pop(VisualElement e)
        {
            if (_popped.Contains(e)) return;
            e.AddToClassList("pop");
            _popped.Add(e);
        }

        void Unpop()
        {
            for (int i = 0; i < _popped.Count; i++) _popped[i].RemoveFromClassList("pop");
            _popped.Clear();
        }

        void RefreshOverlays(float now)
        {
            string banner = null;
            switch (bout.Phase)
            {
                case BoutPhase.Intro: banner = "ROUND " + bout.Round; break;
                case BoutPhase.Count: if (bout.Count > 0) banner = bout.Count.ToString(); break;
            }

            // The walk-on: red's name for the first half of it, blue's for the second.
            int walk = bout.Phase != BoutPhase.WalkOn ? -1 : bout.PhaseAge < bout.walkOnSeconds * 0.5f ? 0 : 1;
            if (walk != _walkShown && _walkOn != null)
            {
                _walkShown = walk;
                _walkOn.EnableInClassList("hidden", walk < 0);
                _walkOn.EnableInClassList("walkon--blue", walk == 1);
                Fighter f = walk == 0 ? bout.red : walk == 1 ? bout.blue : null;
                if (f != null)
                {
                    Set(_walkCorner, walk == 0 ? "RED CORNER" : "BLUE CORNER");
                    Set(_walkName, f.displayName);
                    Set(_walkDetail, WalkOnDetail(f));
                }
            }
            // The speed of a hard punch, where it landed, rising a little as it fades.
            if (_flash != null)
            {
                float age = now - _flashAt;
                Camera cam = director != null ? director.mainCamera : null;
                bool show = age < FlashSeconds && cam != null && _safe != null && !ResultsOpen && !MenuOpen && !DebugOpen;
                _flash.EnableInClassList("hidden", !show);
                if (show)
                {
                    Vector3 v = cam.WorldToViewportPoint(_flashPoint);
                    Rect area = _safe.layout;
                    _flash.style.left = Mathf.Clamp(v.x * area.width - 100f, 0f, area.width - 200f);
                    _flash.style.top = Mathf.Clamp((1f - v.y) * area.height - 90f - age * 60f, 0f, area.height - 48f);
                    _flash.style.opacity = Mathf.Clamp01((FlashSeconds - age) * 4f);
                }
            }
            if (_banner != null)
            {
                _banner.EnableInClassList("hidden", banner == null);
                _banner.EnableInClassList("banner--count", bout.Phase == BoutPhase.Count);
                if (banner != null) Set(_banner, banner);
            }
        }
    }
}
