using System;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;
using PoBox.Sim;
using PoBox.UI;

namespace PoBox.Online
{
    /// <summary>
    /// Chat's corner: between rounds Twitch chat votes on one instruction for a boxer's next round ("!jab", "!body",
    /// "!defend", "!pressure"; the words and what they do are a <see cref="ChatVoteSettings"/> asset). The vote opens a
    /// moment into the break and closes just before the bell; the tally is in the HUD (a UI Toolkit Label named by the
    /// settings) for the whole break, and the winner is said on the broadcast and applied to that boxer for the next
    /// round only.
    ///
    /// Applying it: MjBrain (execution order -40) copies the fighter's drive strength into MjBoxer.DriveScale every
    /// physics step; this runs after it (-30) and before the MuJoCo scene steps (order 0, where MjRing drives the
    /// joints), and reshapes that number by the instruction. It touches nothing while the boxer is down or getting up,
    /// nor if MjBrain has not written this step (so it can never compound). No chat channel set: it does nothing.
    /// Steady frames allocate nothing: the HUD text is rebuilt only when the tally or the seconds left change.
    /// </summary>
    [DefaultExecutionOrder(-30)]
    public class ChatCorner : MonoBehaviour
    {
        [SerializeField] Bout bout;
        [SerializeField] HudView hud;
        [SerializeField] ChatVoteSettings settings;

        ChatVote _vote;
        TwitchChat _chat;
        Label _label;
        VisualElement _labelRoot;
        readonly StringBuilder _text = new StringBuilder(128);
        int _shownVersion = -1, _shownSeconds = -1;

        Fighter _target, _applyTo;
        int _applied = -1, _applyRound, _votedRound = -1;
        bool _decided, _lookedForHud;

        /// <summary>The instruction in force (an index into the settings' options) and for whom; -1 and null when none.</summary>
        public int Active => _applied;
        public Fighter ActiveFor => _applyTo;

        void OnEnable()
        {
            // No asset assigned: the defaults (the four words, as ChatVoteSettings has them).
            if (settings == null) settings = ScriptableObject.CreateInstance<ChatVoteSettings>();
            var words = new string[settings.options.Length];
            for (int i = 0; i < words.Length; i++) words[i] = (settings.options[i].word ?? "").Trim().TrimStart('!').ToLowerInvariant();
            _vote = new ChatVote(words);
        }

        void OnDisable()
        {
            if (_chat != null && _chat.Vote == _vote) _chat.Vote = null;
            _chat = null;
            Show(false);
        }

        void Update()
        {
            if (bout == null) bout = Bout.Instance;
            if (hud == null && !_lookedForHud)
            {
                _lookedForHud = true;
                hud = FindAnyObjectByType<HudView>();
            }
            if (bout == null || settings == null || _vote == null) return;
            TwitchChat chat = hud != null ? hud.Chat : null;
            if (chat != _chat)
            {
                if (_chat != null && _chat.Vote == _vote) _chat.Vote = null;
                _chat = chat;
                if (_chat != null) _chat.Vote = _vote;
            }
            if (_chat == null) { Show(false); return; }
            _chat.Poll();

            BoutPhase phase = bout.Phase;
            if (phase == BoutPhase.Results || phase == BoutPhase.WalkOn)
            {
                Clear();
                _votedRound = -1; // a new bout counts its rounds from 1 again
            }
            // What chat chose holds for the round it was chosen for, and ends with it.
            if (_applied >= 0 && bout.Round != _applyRound && phase != BoutPhase.RoundBreak) Clear();

            bool breakOn = phase == BoutPhase.RoundBreak && bout.Round < bout.rounds && bout.red != null && bout.blue != null;
            if (!breakOn)
            {
                // The bell came before the vote closed (a long frame): it is decided now, for the round starting.
                if (_vote.IsOpen) Decide();
                Show(false);
                return;
            }
            float left = bout.breakSeconds - bout.PhaseAge;
            if (!_vote.IsOpen && _votedRound != bout.Round && bout.PhaseAge >= settings.openAfter && left > settings.closeBefore)
                Open();
            if (_vote.IsOpen && left <= settings.closeBefore) Decide();
            Draw(left);
        }

        void Open()
        {
            _votedRound = bout.Round;
            _decided = false;
            _target = PickTarget();
            _vote.Open();
            SimBus.Say($"Chat's corner for {_target.displayName}: {Words()}", 0);
        }

        void Decide()
        {
            _vote.Close();
            _decided = true;
            int won = _vote.Winner(settings.minVotes);
            if (won < 0 || _target == null) return;
            _applied = won;
            _applyTo = _target;
            _applyRound = _votedRound + 1;
            string line = settings.options[won].line;
            SimBus.Say(string.IsNullOrEmpty(line) ? $"Chat tells {_target.displayName}: {settings.options[won].label}" : string.Format(line, _target.displayName), 1);
        }

        void Clear()
        {
            _applied = -1;
            _applyTo = null;
        }

        Fighter PickTarget()
        {
            Fighter red = bout.red, blue = bout.blue;
            switch (settings.target)
            {
                case ChatVoteSettings.Target.Red: return red;
                case ChatVoteSettings.Target.Blue: return blue;
                case ChatVoteSettings.Target.ChatFavourite:
                {
                    int r = _chat.Votes("red") + _chat.Votes(red.displayName.ToLowerInvariant());
                    int b = _chat.Votes("blue") + _chat.Votes(blue.displayName.ToLowerInvariant());
                    if (r != b) return r > b ? red : blue;
                    break;
                }
            }
            return red.stats.landed <= blue.stats.landed ? red : blue;
        }

        string Words()
        {
            _text.Clear();
            foreach (ChatVoteSettings.Option o in settings.options) _text.Append('!').Append(o.word).Append(' ');
            return _text.ToString().TrimEnd();
        }

        /// <summary>The tally line, rebuilt only when a count or the whole seconds left change.</summary>
        void Draw(float left)
        {
            if (!Bind()) return;
            int seconds = Mathf.Max(0, Mathf.CeilToInt(left - settings.closeBefore));
            if (_vote.Version == _shownVersion && seconds == _shownSeconds && !_label.ClassListContains("hidden")) return;
            _shownVersion = _vote.Version;
            _shownSeconds = seconds;
            _text.Clear();
            _text.Append("CHAT CORNER · ").Append(_target != null ? _target.displayName : "-");
            if (_decided)
            {
                _text.Append(" · ");
                if (_applied >= 0 && _applyTo == _target) _text.Append(settings.options[_applied].label).Append(" NEXT ROUND");
                else _text.Append("NO CALL");
            }
            else
            {
                for (int i = 0; i < settings.options.Length; i++)
                    _text.Append(" · !").Append(settings.options[i].word).Append(' ').Append(_vote.Count(i));
                _text.Append(" · ").Append(seconds).Append('s');
            }
            _label.text = _text.ToString();
            Show(true);
        }

        bool Bind()
        {
            VisualElement root = hud != null ? hud.Root : null;
            if (root == null) return false;
            if (root != _labelRoot)
            {
                _labelRoot = root;
                _label = root.Q<Label>(settings.hudLabel);
                _shownVersion = -1;
            }
            return _label != null;
        }

        void Show(bool on)
        {
            if (_label == null || _label.ClassListContains("hidden") == !on) return;
            _label.EnableInClassList("hidden", !on);
            if (!on) _shownVersion = -1;
        }

        void FixedUpdate()
        {
            if (_applied < 0 || _applyTo == null || bout == null || bout.Phase != BoutPhase.Fight || bout.Round != _applyRound) return;
            Fighter f = _applyTo;
            Mj.MjBoxer body = f.boxer;
            if (f.IsDown || body == null || !body.Bound) return;
            // Only on top of what MjBrain wrote this step: never on a number already reshaped.
            if (body.DriveScale != f.DriveScale) return;
            ChatVoteSettings.Option o = settings.options[_applied];
            double s = body.DriveScale;
            s += (1.0 - s) * o.hurtOverride;
            body.DriveScale = s * o.driveScale;
            if (o.reservePerSecond != 0f && body.Config.fatigue_j > 0.0)
                body.Reserve = Math.Min(1.0, Math.Max(0.0, body.Reserve + o.reservePerSecond * Time.fixedDeltaTime));
        }
    }
}
