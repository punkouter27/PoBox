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
    /// <summary>The menu sheet, the ladder page, the debug panel's toggle, the drag on the picture, and the way back to the first screen.</summary>
    public partial class HudView
    {
        void BuildLeagueRows()
        {
            VisualElement host = _pages[1];
            if (host == null) return;
            host.Clear();
            string[] classes = { "lg-rank", "lg-name", "lg-badge", "lg-elo", "lg-record" };
            for (int r = 0; r < LeagueRows; r++)
            {
                var row = new VisualElement { pickingMode = PickingMode.Ignore };
                row.AddToClassList("lg-row");
                _leagueCells[r] = new Label[classes.Length];
                for (int c = 0; c < classes.Length; c++)
                {
                    var l = new Label("") { pickingMode = PickingMode.Ignore };
                    l.AddToClassList("lg-cell");
                    l.AddToClassList(classes[c]);
                    row.Add(l);
                    _leagueCells[r][c] = l;
                }
                host.Add(row);
                _leagueRows[r] = row;
            }
        }

        void HookMenu()
        {
            Hook(_root.Q<Button>("m-restart"), () => { StopReplay(); if (bout != null) bout.Restart(); CloseMenu(); });
            Hook(_root.Q<Button>("m-next"), () => { StopReplay(); if (bout != null) bout.NewBout(); CloseMenu(); });
            HookBoxers(0, _root.Q<Button>("m-boxers"), _root.Q<Label>("m-boxers-word"));

            string[] cams = { "cam-auto", "cam-wide", "cam-orbit" };
            Shot?[] shots = { null, Shot.Wide, Shot.Orbit };
            for (int i = 0; i < cams.Length; i++)
            {
                int index = i;
                _camButtons[i] = _root.Q<Button>(cams[i]);
                Hook(_camButtons[i], () => { if (director != null) director.Pin(shots[index]); RefreshMenu(); });
            }

            string[] speeds = { "spd-one", "spd-two" };
            float[] scales = { 1f, 2f };
            for (int i = 0; i < speeds.Length; i++)
            {
                int index = i;
                _speedButtons[i] = _root.Q<Button>(speeds[i]);
                Hook(_speedButtons[i], () => { if (bout != null) bout.userTimeScale = scales[index]; RefreshMenu(); });
            }

            // The settings the first screen has too, with the ladder to reset and the brain cam.
            _settings = new SettingsPage(_root, league, ToggleDebug, brain: true);
        }

        /// <summary>A sideways drag on the picture turns the orbit camera, taking it on air first. AUTO in the menu gives the cameras back.</summary>
        void HookPicture()
        {
            if (_picture == null) return;
            _picture.RegisterCallback<PointerDownEvent>(e =>
            {
                // The menu and the debug panel lie over the picture: a tap on what is left of it puts them away.
                if (CloseOverlays()) return;
                _dragX = e.position.x;
                _dragging = true;
                _picture.CapturePointer(e.pointerId);
            });
            _picture.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (!_dragging || director == null) return;
                float dx = e.position.x - _dragX;
                // A tap, or a finger resting, is not a drag: the cameras are only taken by a real one.
                if (director.Pinned != Shot.Orbit && Mathf.Abs(dx) < 24f) return;
                _dragX = e.position.x;
                director.TurnOrbit(-dx * 0.25f);
            });
            _picture.RegisterCallback<PointerUpEvent>(e => EndDrag(e.pointerId));
            // A touch the system cancels, or a capture lost to anything else, ends the drag: otherwise the next
            // touch anywhere would turn the camera.
            _picture.RegisterCallback<PointerCancelEvent>(e => EndDrag(e.pointerId));
            _picture.RegisterCallback<PointerCaptureOutEvent>(_ => _dragging = false);
        }

        void EndDrag(int pointerId)
        {
            if (!_dragging) return;
            _dragging = false;
            if (_picture.HasPointerCapture(pointerId)) _picture.ReleasePointer(pointerId);
            RefreshMenu();
        }

        /// <summary>Back to the first screen to choose another pair. Not offered when the build has no such screen.</summary>
        void HookBoxers(int slot, Button b, Label word)
        {
            _boxers[slot] = b;
            _boxersWords[slot] = word;
            _boxersBase[slot] = word != null ? word.text : "";
            if (b == null) return;
            b.EnableInClassList("hidden", !MatchSelection.HasMenu);
            b.clicked += ToBoxers;
        }

        /// <summary>
        /// Leaves for the first screen, once: a second tap while the scene is loading would load it twice. It
        /// loads in the background; meanwhile both BOXERS buttons are dimmed and their word trails dots.
        /// </summary>
        public void ToBoxers()
        {
            if (_leaving || !MatchSelection.HasMenu) return;
            _leaving = true;
            foreach (Button b in _boxers) if (b != null) b.AddToClassList("btn--busy");
            SceneManager.LoadSceneAsync(MatchSelection.MenuScene);
        }

        /// <summary>The phone's back button (Escape): puts away what is open, then the replay, then goes back to the first screen.</summary>
        public void Back()
        {
            if (CloseOverlays()) return;
            if (MjReplay.Playing) { StopReplay(); return; }
            ToBoxers();
        }

        /// <summary>The one overlay rule: a tap outside the menu or the debug panel puts it away. True when there was one to put away.</summary>
        bool CloseOverlays()
        {
            if (MenuOpen) { CloseMenu(); return true; }
            if (DebugOpen) { ToggleDebug(); return true; }
            return false;
        }

        public void ToggleMenu()
        {
            if (MenuOpen) CloseMenu(); else OpenMenu(0);
        }

        public void OpenMenu(int tab)
        {
            if (_menuSheet == null) return;
            // One thing open at a time: the three of them are docked in the same place.
            if (DebugOpen) ToggleDebug();
            _menuSheet.RemoveFromClassList("hidden");
            if (_menuGlyph != null) _menuGlyph.kind = GlyphKind.Close;
            ShowResults();
            ShowTab(tab);
        }

        /// <summary>The results card is up when the bout is over and neither the menu nor the debug panel is in its place.</summary>
        void ShowResults()
        {
            if (_results != null) _results.EnableInClassList("hidden", bout == null || bout.Phase != BoutPhase.Results || MenuOpen || DebugOpen || MjReplay.Playing);
        }

        /// <summary>The menu's SETTINGS tab, open on one of the settings' own tabs (0 SOUND, 1 DISPLAY, 2 DEBUG).</summary>
        public void OpenSettings(int tab)
        {
            OpenMenu(2);
            _settings?.ShowTab(tab);
        }

        public void CloseMenu()
        {
            if (_menuSheet == null) return;
            _menuSheet.AddToClassList("hidden");
            if (_menuGlyph != null) _menuGlyph.kind = GlyphKind.Menu;
            ShowResults();
        }

        public void ShowTab(int tab)
        {
            for (int i = 0; i < 3; i++)
            {
                if (_tabs[i] != null) _tabs[i].EnableInClassList("seg-btn--on", i == tab);
                if (_pages[i] != null) _pages[i].EnableInClassList("hidden", i != tab);
            }
            if (tab == 1) RefreshLeague();
            RefreshMenu();
        }

        public void ToggleDebug()
        {
            if (_debugPanel == null) return;
            if (!DebugOpen) CloseMenu();
            _debugPanel.ToggleInClassList("hidden");
            ShowResults();
            RefreshDebug();
        }

        void RefreshMenu()
        {
            if (_root == null) return;
            Shot? pinned = director != null ? director.Pinned : null;
            Shot?[] shots = { null, Shot.Wide, Shot.Orbit };
            for (int i = 0; i < _camButtons.Length; i++)
                if (_camButtons[i] != null) _camButtons[i].EnableInClassList("btn--on", pinned == shots[i]);

            float scale = bout != null ? bout.userTimeScale : 1f;
            float[] scales = { 1f, 2f };
            for (int i = 0; i < _speedButtons.Length; i++)
                if (_speedButtons[i] != null) _speedButtons[i].EnableInClassList("btn--on", Mathf.Approximately(scale, scales[i]));

            if (_settings != null) _settings.Refresh();
        }

        void RefreshLeague()
        {
            if (league == null) return;
            List<LeagueTable.Row> rows = league.Sorted();
            for (int r = 0; r < LeagueRows; r++)
            {
                bool has = r < rows.Count;
                if (_leagueRows[r] != null) _leagueRows[r].style.visibility = has ? Visibility.Visible : Visibility.Hidden;
                if (!has || _leagueCells[r] == null) continue;
                LeagueTable.Row row = rows[r];
                PolicyProfile p = league.ProfileFor(row.name);
                _leagueCells[r][0].text = (r + 1).ToString();
                _leagueCells[r][1].text = row.name;
                // What the rating started from: the boxer's record in the CPU's bouts (tools/elo.py).
                string cpu = LeagueTable.CpuRecord(row.name);
                _leagueCells[r][2].text = cpu.Length > 0 ? "CPU " + cpu : p != null ? p.Badge : "";
                _leagueCells[r][3].text = row.elo.ToString("0");
                _leagueCells[r][4].text = $"{row.wins}-{row.losses}-{row.draws}";
                bool live = bout != null && ((bout.red != null && bout.red.displayName == row.name) || (bout.blue != null && bout.blue.displayName == row.name));
                _leagueRows[r].EnableInClassList("lg-row--live", live);
            }
        }
    }
}
