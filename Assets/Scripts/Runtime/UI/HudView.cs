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
    /// <summary>
    /// The whole interface, in one document, on one portrait screen, with nothing that scrolls.
    ///
    /// The game's name top-left, the menu top-right and the version (the build's day, YYMMDD) bottom-right never move;
    /// with the settings' STATS on (Ui.Frame), nor do the frame rate top-centre and DEBUG bottom-left. Between them: a scoreboard of three
    /// rows (the names written on the health bars with the clock between them, a hit leaving a pale chunk on the bar
    /// that drains a moment later; a tug-of-war win-probability bar; the punches landed, and with STATS the hardest
    /// hit) and the picture.
    ///
    /// Over the picture: a pod in each top corner showing what that fighter has left in the tank (for a boxer
    /// trained to tire, what its muscles have left), what it is doing this moment, and ON THE ROPES or TIRED when it is (with
    /// STATS also where it has been hit and where its balance is); the commentary as a caption
    /// along the bottom that fades when it has nothing to say; during the walk-on each fighter's name across
    /// the lower third; between rounds the three cards and the round's numbers (who held the centre of the
    /// ring among them); during a count an inset of
    /// the boxer left standing; and with STATS, at a hard clean punch, the glove's speed, written where it landed. A
    /// sideways drag on the picture takes the orbit camera and turns it.
    ///
    /// Everything else opens over that rather than replacing it, docked at the bottom where a thumb is and
    /// one at a time: the menu is a sheet with three tabs (the last of them the settings, which have three of their
    /// own), the debug readings are a panel that grows out of the DEBUG chip (the frame-rate chip opens and closes it
    /// too, and so do three fingers or a long press on the frame rate), and a tap
    /// anywhere outside either puts it away; the end of a bout is a single sheet carrying the result, what the pick
    /// paid, the three judges' cards, both fighters' numbers, and one row of four chips: REPLAY, SHARE, NEXT (or AGAIN) and MENU. MENU, and CHANGE BOXERS in
    /// the menu, go back to the first screen, loading it in the background with the button dimmed meanwhile.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public partial class HudView : MonoBehaviour
    {
        public Bout bout;
        public LeagueTable league;
        public BroadcastDirector director;

        const int LeagueRows = 8;
        const int ResultRows = 4;

        UIDocument _doc;
        VisualElement _root, _safe;
        readonly SafeArea _safeArea = new SafeArea();

        // frame
        Button _fps, _menu, _debug;
        Label _version, _debugText;
        VisualElement _debugDot, _debugPanel;
        Glyph _menuGlyph;
        bool _leaving;
        Label[] _debugValues = new Label[0];

        // board
        Label _redName, _blueName, _redBadge, _blueBadge, _round, _clock, _redHpText, _blueHpText, _momRed, _momBlue;
        VisualElement _redHp, _blueHp, _momFill, _board;
        Label _landedRed, _landedBlue, _peakRed, _peakBlue;

        // picture overlays
        Label _banner, _walkCorner, _walkName, _walkDetail;
        VisualElement _walkOn;
        HitMap _hitRed, _hitBlue;
        BalanceGauge _balRed, _balBlue;
        AudioDirector _audio;
        int _walkShown = -1;

        // pods: the tank, the action badge and the two flags
        readonly VisualElement[] _tank = new VisualElement[2], _ghost = new VisualElement[2];
        readonly Label[] _act = new Label[2], _ropes = new Label[2], _tired = new Label[2];
        // what has just changed, swollen until the next tick (Theme.uss .pop)
        readonly List<VisualElement> _popped = new List<VisualElement>(16);
        readonly PunchType[] _punch = new PunchType[2];
        readonly float[] _punchAt = { -99f, -99f };
        static readonly string[] PunchNames = { "JAB", "CROSS", "HOOK", "UPPER", "BODY" };
        /// <summary>Seconds a punch's name stays on the badge.</summary>
        const float PunchShown = 0.45f;

        // between rounds, the inset, the speed of a punch
        VisualElement _break, _pip, _picture;
        Label _breakTitle, _flash;
        readonly Label[] _breakLanded = new Label[2], _breakBlocked = new Label[2], _breakCentre = new Label[2], _breakSplit = new Label[2];
        Label _breakBest;
        // The round's clean punches landed, head and body, by corner, and the hardest of them.
        readonly int[] _roundHead = new int[2], _roundBody = new int[2];
        HitEvent _roundBestHit;
        readonly VisualElement[] _breakCards = new VisualElement[Judges.Count];
        readonly Label[] _breakScores = new Label[Judges.Count];
        readonly FighterStats[] _roundStart = new FighterStats[2];
        [Tooltip("A clean punch this many times harder than these two usually land has its glove speed written where it landed.")]
        public float flashOverUsual = 1.25f;
        [Tooltip("Without STATS the glove speed is still written for a punch this many times harder than usual: the radar gun for everyone.")]
        public float radarOverUsual = 1.6f;
        const float FlashSeconds = 0.9f;
        Vector3 _flashPoint;
        float _flashAt = -99f, _dragX;
        bool _dragging;

        // the caption, and the bottom row the picture ends at
        VisualElement _frameBottom;
        Label _ticker;
        float _tickerAt = -99f;
        int _tickerPriority;

        // results
        VisualElement _results;
        Label _resWinner, _resMethod, _resElo;
        Button _resNext;
        Label _resNextWord;
        readonly Label[] _resRed = new Label[ResultRows], _resBlue = new Label[ResultRows];
        readonly VisualElement[] _judgeCards = new VisualElement[Judges.Count];
        readonly Label[] _judgeScores = new Label[Judges.Count];

        // menu
        VisualElement _menuSheet;
        readonly Button[] _tabs = new Button[3];
        readonly VisualElement[] _pages = new VisualElement[3];
        readonly Button[] _camButtons = new Button[3], _speedButtons = new Button[2];
        SettingsPage _settings;
        readonly Label[][] _leagueCells = new Label[LeagueRows][];
        readonly VisualElement[] _leagueRows = new VisualElement[LeagueRows];
        // CHANGE BOXERS in the menu and MENU on the result card, and their words, which trail dots while the first screen loads
        readonly Button[] _boxers = new Button[2];
        readonly Label[] _boxersWords = new Label[2];
        readonly string[] _boxersBase = new string[2];
        static readonly string[] BusyDots = { " ·", " ··", " ···" };

        // the features: the instant replay, SHARE, the pick before the bell, chat plays, the brain cam
        Button _resReplay, _resShare, _predictRed, _predictBlue;
        Label _resExtra, _chat, _predictCoins;
        VisualElement _predict;
        BrainCamPanel _brainCam;
        readonly TwoFingerTap _statsTap = new TwoFingerTap();
        bool _replaying, _autoReplayed;
        [Tooltip("After a knockout the punch that did it is replayed by itself, once, this many seconds into the result (the winner's moment first).")]
        public float autoReplayAfter = 4f;
        TwitchChat _twitch;
        /// <summary>A joint's brain cam group by its name (<see cref="BrainCamPanel.GroupOf"/>).</summary>
        internal static int GroupOf(string joint) => BrainCamPanel.GroupOf(joint);

        // the bout's win probability, for the three graphs (WinLine.cs)
        readonly WinLine.Record _winRecord = new WinLine.Record();
        WinLine _breakWin, _resWin, _stripWin;
        float _nextWinSample;
        RingMap _ringMap;
        int _winRound;
        const float WinSampleSeconds = 0.5f;

        float _nextFast, _nextSlow;
        int _lastBout = -1;
        BoutPhase _lastPhase = (BoutPhase)(-1);

        public bool MenuOpen => _menuSheet != null && !_menuSheet.ClassListContains("hidden");
        public bool DebugOpen => _debugPanel != null && !_debugPanel.ClassListContains("hidden");
        public bool ResultsOpen => _results != null && !_results.ClassListContains("hidden");
        public SettingsPage Settings => _settings;
        public VisualElement Root => _root;

        /// <summary>A pretend notch for the editor and the tests, in screen pixels, for every document (<see cref="SafeArea"/>). Null is the real safe area.</summary>
        public static Rect? SafeAreaOverride;

        // ------------------------------------------------------------ build

        void OnEnable()
        {
            SimBus.Line += OnLine;
            SimBus.PunchThrown += OnPunch;
            SimBus.Hit += OnHit;
            SimBus.Knockdown += OnKnockdown;
            _twitch ??= TwitchChat.FromPrefs();
            BuildIfNew();
        }

        /// <summary>
        /// Built and hooked once a document: enabled again on the same tree, the buttons keep the handlers they have.
        /// A tree that is not there yet, or a new one, is built on the first frame it is (as MenuView and MeleeView do).
        /// </summary>
        void BuildIfNew()
        {
            if (_doc == null) _doc = GetComponent<UIDocument>();
            VisualElement root = _doc.rootVisualElement;
            if (root != null && root != _root) Build(root);
        }

        void Build(VisualElement root)
        {
            _root = root;
            _safe = _root.Q<VisualElement>("safe");

            _fps = _root.Q<Button>("fps");
            _menu = _root.Q<Button>("menu");
            _menuGlyph = _root.Q<Glyph>("menu-glyph");
            _debug = _root.Q<Button>("debug");
            _version = _root.Q<Label>("version");
            _debugText = _root.Q<Label>("debug-text");
            _debugDot = _root.Q<VisualElement>("debug-dot");
            _debugPanel = _root.Q<VisualElement>("debug-panel");

            _board = _root.Q<VisualElement>("board");
            _redName = _root.Q<Label>("red-name"); _blueName = _root.Q<Label>("blue-name");
            _redBadge = _root.Q<Label>("red-badge"); _blueBadge = _root.Q<Label>("blue-badge");
            _round = _root.Q<Label>("round"); _clock = _root.Q<Label>("clock");
            _redHp = _root.Q<VisualElement>("red-hp"); _blueHp = _root.Q<VisualElement>("blue-hp");
            _redHpText = _root.Q<Label>("red-hp-text"); _blueHpText = _root.Q<Label>("blue-hp-text");
            _momFill = _root.Q<VisualElement>("momentum-fill");
            _momRed = _root.Q<Label>("momentum-red"); _momBlue = _root.Q<Label>("momentum-blue");
            _landedRed = _root.Q<Label>("landed-red"); _landedBlue = _root.Q<Label>("landed-blue");
            _peakRed = _root.Q<Label>("peak-red"); _peakBlue = _root.Q<Label>("peak-blue");

            _banner = _root.Q<Label>("banner");
            _walkOn = _root.Q<VisualElement>("walkon");
            _walkCorner = _root.Q<Label>("walkon-corner");
            _walkName = _root.Q<Label>("walkon-name");
            _walkDetail = _root.Q<Label>("walkon-detail");
            _hitRed = _root.Q<HitMap>("hit-red"); _hitBlue = _root.Q<HitMap>("hit-blue");
            _balRed = _root.Q<BalanceGauge>("bal-red"); _balBlue = _root.Q<BalanceGauge>("bal-blue");

            string[] corners = { "red", "blue" };
            for (int i = 0; i < 2; i++)
            {
                _tank[i] = _root.Q<VisualElement>("tank-" + corners[i]);
                _ghost[i] = _root.Q<VisualElement>(corners[i] + "-ghost");
                _act[i] = _root.Q<Label>("act-" + corners[i]);
                _ropes[i] = _root.Q<Label>("ropes-" + corners[i]);
                _tired[i] = _root.Q<Label>("tired-" + corners[i]);
                _breakLanded[i] = _root.Q<Label>("break-landed-" + corners[i]);
                _breakBlocked[i] = _root.Q<Label>("break-blocked-" + corners[i]);
                _breakCentre[i] = _root.Q<Label>("break-centre-" + corners[i]);
                _breakSplit[i] = _root.Q<Label>("break-split-" + corners[i]);
            }
            _break = _root.Q<VisualElement>("break");
            _breakBest = _root.Q<Label>("break-best");
            _breakTitle = _root.Q<Label>("break-title");
            _pip = _root.Q<VisualElement>("pip");
            _flash = _root.Q<Label>("flash");
            _picture = _root.Q<VisualElement>("picture");
            _breakWin = _root.Q<WinLine>("break-winline");
            _resWin = _root.Q<WinLine>("res-winline");
            _stripWin = _root.Q<WinLine>("strip-winline");
            _ringMap = _root.Q<RingMap>("ringmap");
            if (_pip != null && director != null && director.pipCam != null && director.pipCam.targetTexture != null)
                _pip.style.backgroundImage = Background.FromRenderTexture(director.pipCam.targetTexture);

            _frameBottom = _root.Q<VisualElement>("frame-bottom");
            _ticker = _root.Q<Label>("ticker");

            _results = _root.Q<VisualElement>("results");
            _resWinner = _root.Q<Label>("res-winner"); _resMethod = _root.Q<Label>("res-method");
            _resElo = _root.Q<Label>("res-elo");
            _resNext = _root.Q<Button>("res-next");
            _resNextWord = _root.Q<Label>("res-next-word");
            _resReplay = _root.Q<Button>("res-replay");
            _resShare = _root.Q<Button>("res-share");
            _resExtra = _root.Q<Label>("res-extra");
            _chat = _root.Q<Label>("chat");
            _predict = _root.Q<VisualElement>("predict");
            _predictRed = _root.Q<Button>("predict-red");
            _predictBlue = _root.Q<Button>("predict-blue");
            _predictCoins = _root.Q<Label>("predict-coins");

            _menuSheet = _root.Q<VisualElement>("menu-sheet");
            string[] tabNames = { "match", "league", "settings" };
            for (int i = 0; i < 3; i++)
            {
                int tab = i;
                _tabs[i] = _root.Q<Button>("tab-" + tabNames[i]);
                _pages[i] = _root.Q<VisualElement>("page-" + tabNames[i]);
                if (_tabs[i] != null) _tabs[i].clicked += () => ShowTab(tab);
            }

            Frame(_root);

            Hook(_menu, ToggleMenu);
            Hook(_debug, ToggleDebug);
            // The frame rate, a long press on it and three fingers: the debug panel.
            DebugPanel.Hook(_root, _fps, ToggleDebug, ToggleDebug);
            if (_board != null) _board.RegisterCallback<ClickEvent>(_ => CloseOverlays());

            BuildResultsTape();
            BuildJudges("res-judges", _judgeCards, _judgeScores);
            BuildJudges("break-judges", _breakCards, _breakScores);
            _debugValues = DebugPanel.Build(_debugPanel, DebugKeys);
            BuildLeagueRows();
            HookMenu();
            HookResults();
            HookPicture();
            HookPredict();
            _brainCam = new BrainCamPanel(_safe);

            ShowTab(0);
            SettingsPage.Load();
            Frame(_root);
            RefreshMenu();
            _safeArea.Fit(_root, _safe);
        }

        void OnDisable()
        {
            SimBus.Line -= OnLine;
            SimBus.PunchThrown -= OnPunch;
            SimBus.Hit -= OnHit;
            SimBus.Knockdown -= OnKnockdown;
            _twitch?.Dispose();
            _twitch = null;
        }

        static void Hook(Button b, System.Action action)
        {
            if (b != null) b.clicked += action;
        }

        /// <summary>A knockdown: the replay keeps the seconds around it.</summary>
        void OnKnockdown(Fighter f, HitEvent cause)
        {
            if (cause.attacker != null && MjReplay.Instance != null) MjReplay.Instance.Mark();
        }

        /// <summary>A new bout: no replay of the last one, its pick given back if it was never settled, chat's votes cleared.</summary>
        void OnNewBout()
        {
            if (MjReplay.Instance != null) MjReplay.Instance.Clear();
            _autoReplayed = false;
            Prediction.Reset();
            _twitch?.ClearVotes();
            _winRecord.Clear();
            _winRound = 0;
            _lastMapRound = 0;
            _ringMap?.Clear();
        }

        void Update()
        {
            BuildIfNew();
            if (_root == null) return;
            _safeArea.Fit(_root, _safe);
            if (Input.GetKeyDown(KeyCode.Escape)) Back();
            if (Input.GetKeyDown(KeyCode.Tab)) SettingsPage.ToggleStats();
            float now = Time.unscaledTime;
            if (_statsTap.Poll(now)) SettingsPage.ToggleStats();
            if (_leaving)
            {
                string dots = BusyDots[(int)(now * 3f) % BusyDots.Length];
                for (int i = 0; i < _boxersWords.Length; i++) Set(_boxersWords[i], _boxersBase[i] + dots);
            }
            if (bout == null) return;
            // The brain cam is the settings' (DEBUG, BRAIN); it gives way to the pick before the bell and to the result.
            _brainCam?.Show(SettingsPage.BrainCam && bout.Phase != BoutPhase.Results && (_predict == null || _predict.ClassListContains("hidden")));

            if (bout.BoutNumber != _lastBout || bout.Phase != _lastPhase)
            {
                if (bout.BoutNumber != _lastBout) OnNewBout();
                _lastBout = bout.BoutNumber;
                OnPhaseChanged(bout.Phase);
                _lastPhase = bout.Phase;
            }

            RefreshBars();
            RefreshOverlays(now);
            SampleWin(now);
            RefreshMap(now);
            // The replay over: the card back. One still playing as the result runs out is stopped first, so the
            // next bout is placed on the live state and not under the replay's.
            if (_replaying && !MjReplay.Playing) { _replaying = false; ShowResults(); }
            if (!_autoReplayed && !_replaying && bout.Phase == BoutPhase.Results && bout.PhaseAge > autoReplayAfter
                && (bout.Method.StartsWith("KO") || bout.Method.StartsWith("TKO") || bout.Method.StartsWith("DOUBLE KO")))
            {
                _autoReplayed = true;
                PlayReplay();
            }
            if (MjReplay.Playing && (bout.Phase != BoutPhase.Results || (bout.autoAdvance && bout.ResultsTimeLeft < 0.5f))) StopReplay();
            bool brain = _brainCam != null && _brainCam.On;
            if (brain) _brainCam.Refresh(bout, false, null);

            if (now >= _nextFast)
            {
                _nextFast = now + 0.1f;
                Unpop();
                RefreshClock();
                RefreshTape();
                RefreshPods();
                if (ResultsOpen) RefreshNext();
                RefreshPredict();
                if (brain) _brainCam.Refresh(bout, true, PunchWord(_brainCam.Corner));
                if (_twitch != null) RefreshChat();
            }

            if (now >= _nextSlow)
            {
                _nextSlow = now + 0.25f;
                Frame(_root);
                FrameRate(_fps, _debugDot);
                if (DebugOpen) RefreshDebug();
                ReportPictureBand();
                if (MenuOpen && _settings != null && _settings.Armed) _settings.Refresh();
            }

            // The caption is there while it is news, and gone after: five seconds, then a second of fading. Never
            // under anything open over the picture.
            if (_ticker != null)
            {
                float opacity = ResultsOpen || MenuOpen || DebugOpen || string.IsNullOrEmpty(_ticker.text) ? 0f : Mathf.Clamp01(6f - (now - _tickerAt));
                if (!Mathf.Approximately(opacity, _tickerOpacity)) { _tickerOpacity = opacity; _ticker.style.opacity = opacity; }
            }
        }

        float _tickerOpacity = -1f;

        /// <summary>
        /// Tells the cameras which part of the screen is not under interface: from the bottom of the
        /// scoreboard to the bottom row, or to the top of the results card or the menu when one is up.
        /// </summary>
        void ReportPictureBand()
        {
            if (director == null || _board == null || _root.panel == null) return;
            float height = _root.panel.visualTree.layout.height;
            if (!(height > 1f)) return;
            VisualElement below = ResultsOpen ? _results : MenuOpen ? _menuSheet : _frameBottom;
            if (below == null) return;
            float top = _board.worldBound.yMax / height, bottom = below.worldBound.yMin / height;
            if (bottom > top + 0.1f) director.SetPictureBand(top, bottom);
        }

        void OnPhaseChanged(BoutPhase phase)
        {
            Fighter red = bout.red, blue = bout.blue;
            if (red != null && _redName != null)
            {
                _redName.text = red.displayName;
                if (_redBadge != null) _redBadge.text = Badge(red);
            }
            if (blue != null && _blueName != null)
            {
                _blueName.text = blue.displayName;
                if (_blueBadge != null) _blueBadge.text = Badge(blue);
            }

            bool results = phase == BoutPhase.Results;
            ShowResults();
            // The pods give way to the card.
            if (_hitRed != null && _hitRed.parent != null) _hitRed.parent.EnableInClassList("hidden", results);
            if (_hitBlue != null && _hitBlue.parent != null) _hitBlue.parent.EnableInClassList("hidden", results);

            // A round starts: what each has done so far, so the break can show the round alone.
            if (phase == BoutPhase.Intro)
            {
                if (red != null) _roundStart[0] = red.stats;
                if (blue != null) _roundStart[1] = blue.stats;
                _roundHead[0] = _roundHead[1] = _roundBody[0] = _roundBody[1] = 0;
                _roundBestHit = default;
            }
            if (_break != null) _break.EnableInClassList("hidden", phase != BoutPhase.RoundBreak);
            if (phase == BoutPhase.RoundBreak) FillBreak();
            if (_pip != null)
            {
                _pip.EnableInClassList("hidden", phase != BoutPhase.Count || director == null || director.pipCam == null);
                // The frame is the colour of the corner of the boxer in it: the one left standing.
                _pip.EnableInClassList("pip--blue", bout.Downed == red);
            }

            if (results) { FillResults(); SettleExtras(); }
            if (_resReplay != null) _resReplay.EnableInClassList("hidden", MjReplay.Instance == null);
            if (MenuOpen && _pages[1] != null && !_pages[1].ClassListContains("hidden")) RefreshLeague();
        }
    }
}
