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
    /// <summary>The result card: its tape and judges' cards, the replay, SHARE, the pick's and the cup's line, and NEXT.</summary>
    public partial class HudView
    {
        void BuildResultsTape()
        {
            VisualElement host = _root.Q<VisualElement>("res-tape");
            if (host == null) return;
            host.Clear();
            // Landed, blocked, hardest, knockdowns: the icon is the key, as on the scoreboard's chips, with its word under it
            // (2026-10-31 browse: icons alone did not read).
            GlyphKind[] kinds = { GlyphKind.Glove, GlyphKind.Shield, GlyphKind.Burst, GlyphKind.Daze };
            string[] words = { "LANDED", "BLOCKED", "HARDEST", "DOWNS" };
            for (int i = 0; i < ResultRows; i++)
            {
                var row = new VisualElement { pickingMode = PickingMode.Ignore };
                row.AddToClassList("tape-row");
                _resRed[i] = TapeValue(true);
                _resBlue[i] = TapeValue(false);
                row.Add(_resRed[i]);
                row.Add(TapeKey(kinds[i], words[i]));
                row.Add(_resBlue[i]);
                host.Add(row);
            }
        }

        static Label TapeValue(bool left)
        {
            var l = new Label("0") { pickingMode = PickingMode.Ignore };
            l.AddToClassList("tape-val");
            l.AddToClassList(left ? "tape-val--l" : "tape-val--r");
            return l;
        }

        static VisualElement TapeKey(GlyphKind kind, string word = null)
        {
            var key = new VisualElement { pickingMode = PickingMode.Ignore };
            key.AddToClassList("tape-key");
            var g = new Glyph { kind = kind };
            g.AddToClassList("glyph");
            key.Add(g);
            if (!string.IsNullOrEmpty(word))
            {
                var l = new Label(word) { pickingMode = PickingMode.Ignore };
                l.AddToClassList("tape-key-word");
                key.Add(l);
                key.AddToClassList("tape-key--worded");
            }
            return key;
        }

        /// <summary>One card a judge: the judge's name over the score, red's first.</summary>
        void BuildJudges(string hostName, VisualElement[] cards, Label[] scores)
        {
            VisualElement host = _root.Q<VisualElement>(hostName);
            if (host == null) return;
            host.Clear();
            for (int j = 0; j < Judges.Count; j++)
            {
                var card = new VisualElement { pickingMode = PickingMode.Ignore };
                card.AddToClassList("judge");
                var name = new Label(Judges.Names[j]) { pickingMode = PickingMode.Ignore };
                name.AddToClassList("judge-name");
                var score = new Label("-") { pickingMode = PickingMode.Ignore };
                score.AddToClassList("judge-card");
                card.Add(name);
                card.Add(score);
                host.Add(card);
                cards[j] = card;
                scores[j] = score;
            }
        }

        void HookResults()
        {
            HookBoxers(1, _root.Q<Button>("res-boxers"), _root.Q<Label>("res-boxers-word"));
            Hook(_resNext, () => { StopReplay(); if (bout != null) bout.NewBout(); });
            Hook(_resReplay, PlayReplay);
            Hook(_resShare, () => StartCoroutine(Share.Shot(ShareLine())));
        }

        /// <summary>
        /// The instant replay (Mj/MjReplay.cs): the last knockdown, or the last seconds of the bout, at half speed,
        /// the card put away while it plays and back when it is over; the cameras take the wide shot.
        /// </summary>
        void PlayReplay()
        {
            MjReplay replay = MjReplay.Instance;
            if (replay == null || !replay.Play()) return;
            _replaying = true;
            ShowResults();
            SimBus.Say("REPLAY · " + (bout != null ? bout.Method : ""), 3);
        }

        static void StopReplay()
        {
            if (MjReplay.Playing) MjReplay.Instance.Stop();
        }

        string ShareLine()
        {
            if (bout == null || bout.red == null || bout.blue == null) return "PoBox";
            Fighter w = bout.Winner;
            string what = w == null ? $"{bout.red.displayName} and {bout.blue.displayName} drew" : $"{w.displayName} beat {bout.Other(w).displayName}";
            return $"{what}: {bout.Method} · PoBox";
        }

        /// <summary>At the result, once: the pick paid out, and a cup bout (League/Career.cs) counted, in a line on the card.</summary>
        void SettleExtras()
        {
            Fighter red = bout.red, blue = bout.blue;
            if (red == null || blue == null) return;
            int winner = bout.Winner == null ? -1 : bout.Winner == red ? 0 : 1;
            string pick = Prediction.Picked >= 0 ? Prediction.Settle(winner, (Prediction.Picked == 0 ? red : blue).displayName) : null;
            string cup = null;
            if (Career.Live && MatchSelection.Chosen)
            {
                Career.Live = false;
                Career c = Career.Load();
                if (c != null && c.Report(MatchSelection.Red, MatchSelection.Blue, winner))
                    cup = c.Champion != null ? $"CUP WON BY {c.Shown(c.Champion)}" : "CUP · THROUGH TO THE NEXT ROUND";
                else if (c != null && winner < 0)
                {
                    // A draw decides nothing: the bout stays the cup's, so AGAIN (or its countdown) fights it again and that one counts.
                    Career.Live = true;
                    cup = "CUP · A DRAW: FIGHT IT AGAIN";
                }
            }
            string line = pick != null && cup != null ? pick + "  ·  " + cup : pick ?? cup ?? "";
            if (_resExtra == null) return;
            Set(_resExtra, line);
            _resExtra.EnableInClassList("hidden", line.Length == 0);
        }

        void FillResults()
        {
            Fighter red = bout.red, blue = bout.blue;
            if (red == null || blue == null) return;

            Fighter w = bout.Winner;
            if (_resWinner != null)
            {
                _resWinner.text = w != null ? w.displayName + " WINS" : "DRAW";
                _resWinner.EnableInClassList("res-winner--red", w != null && w == red);
                _resWinner.EnableInClassList("res-winner--blue", w != null && w == blue);
            }
            Set(_resMethod, bout.Method);
            if (_resElo != null)
            {
                // The ladder points the winner took; on a draw, red's, with red's name.
                float delta = w == blue ? -bout.EloDelta : bout.EloDelta;
                _resElo.text = (w == null ? red.displayName + " " : "") + (delta >= 0f ? "+" : "") + delta.ToString("0") + " ELO";
            }

            // The punch count: landed of thrown and the share; the hardest punch and the fastest glove that landed.
            Set(_resRed[0], PunchStats.Landed(red));
            Set(_resBlue[0], PunchStats.Landed(blue));
            Set(_resRed[1], red.stats.blocked.ToString());
            Set(_resBlue[1], blue.stats.blocked.ToString());
            Set(_resRed[2], PunchStats.Power(red));
            Set(_resBlue[2], PunchStats.Power(blue));
            Set(_resRed[3], red.stats.knockdowns.ToString());
            Set(_resBlue[3], blue.stats.knockdowns.ToString());

            // The cards. After a decision they are the result; after a stoppage they are how it stood.
            for (int j = 0; j < Judges.Count; j++)
            {
                if (_judgeCards[j] == null) continue;
                int r = bout.judges.total[j, 0], b = bout.judges.total[j, 1];
                Set(_judgeScores[j], bout.judges.RoundsScored > 0 ? bout.judges.Card(j) : "-");
                _judgeCards[j].EnableInClassList("judge--red", r > b);
                _judgeCards[j].EnableInClassList("judge--blue", b > r);
            }

            _resWin?.Show(_winRecord);
            RefreshNext();
        }

        /// <summary>The button that starts the next bout, counting down to doing it by itself.</summary>
        void RefreshNext()
        {
            if (_resNext == null) return;
            // The same two again when the boxers were chosen; the ladder's next pair when they were not.
            string word = bout.fixedEntrants ? "AGAIN" : "NEXT";
            Set(_resNextWord, bout.autoAdvance ? $"{word} {Mathf.CeilToInt(bout.ResultsTimeLeft)}" : word);
        }
    }
}
