using System.Collections.Generic;
using PoBox.Broadcast;
using PoBox.Sim;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace PoBox.UI
{
    /// <summary>
    /// Demo (attract) mode: after <see cref="ShowcaseSettings.idleSeconds"/> with no key, click, touch, mouse move or
    /// gamepad button (from load, or from the last input) the HUD steps aside and the game shows itself. On the boxer
    /// screen it starts a bout between two random boxers; in a bout the broadcast cuts between the showcase shots
    /// (<see cref="ShowcaseSettings.cameras"/>, <see cref="ShowcaseSettings.cameraDwellSeconds"/> each); a finished
    /// bout's result stays up <see cref="ShowcaseSettings.resultSeconds"/>, then the next random pair fights. Every
    /// boxer is its trained brains as always (inference only). Any input ends it: the HUD comes back and the player
    /// has the game where it is. Only the Tab overlay (<see cref="DiagOverlay"/>) and a one-line caption stay up.
    /// In the editor it runs only with <see cref="ShowcaseSettings.runInEditor"/>, so tests are never taken over.
    /// </summary>
    public static class DemoMode
    {
        public static bool Active { get; private set; }
        /// <summary>Idle seconds since the last input (or since the game started), counted frame by frame.</summary>
        public static float IdleSeconds => _idle;
        public static bool Allowed => !Application.isEditor || ShowcaseSettings.Current.runInEditor;

        /// <summary>A frame longer than this (a load, a stall, a tab in the background) does not count as idle time.</summary>
        public const float LongFrameSeconds = 0.25f;

        static float _idle;

        /// <summary>One frame of no input: its length joins the idle clock unless it is a long frame.</summary>
        public static void Advance(float frameSeconds)
        {
            if (frameSeconds > 0f && frameSeconds <= LongFrameSeconds) _idle += frameSeconds;
        }

        // Play mode may start without a domain reload: statics from the last session would still say "demo".
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Active = false;
            _idle = 0f;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Object.FindAnyObjectByType<ShowcaseRunner>() != null) return;
            var go = new GameObject("PoBox Showcase (demo mode, Tab overlay)");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<ShowcaseRunner>();
            go.AddComponent<DiagOverlay>();
        }

        /// <summary>Input this frame: the idle clock starts again, and a running demo ends.</summary>
        public static void Touched()
        {
            _idle = 0f;
            if (Active) Stop();
        }

        public static void Begin()
        {
            if (Active) return;
            Active = true;
            Debug.Log($"[DemoMode] on after {IdleSeconds:0} s idle ({SceneManager.GetActiveScene().name})");
        }

        public static void Stop()
        {
            if (!Active) return;
            Active = false;
            Debug.Log("[DemoMode] off (input)");
        }
    }

    /// <summary>The demo's clock and hands (DontDestroyOnLoad, made by <see cref="DemoMode"/>).</summary>
    [DefaultExecutionOrder(-50)]
    public sealed class ShowcaseRunner : MonoBehaviour
    {
        Vector3 _mouse;
        bool _mouseKnown;
        float _shotAt = -1f;
        int _shot;
        bool _leaving;
        float _hideAt;
        readonly List<(UIDocument doc, DisplayStyle was)> _hidden = new List<(UIDocument, DisplayStyle)>();

        void OnEnable() => SceneManager.sceneLoaded += Loaded;

        void OnDisable()
        {
            SceneManager.sceneLoaded -= Loaded;
            ShowHud();
        }

        void Loaded(Scene scene, LoadSceneMode mode)
        {
            _hidden.Clear();
            _shotAt = -1f;
            _hideAt = 0f;
            _leaving = false;
        }

        void Update()
        {
            if (AnyInput()) DemoMode.Touched();
            else DemoMode.Advance(Time.unscaledDeltaTime);
            ShowcaseSettings s = ShowcaseSettings.Current;
            if (!DemoMode.Active)
            {
                if (_hidden.Count > 0) ShowHud();
                if (_shotAt >= 0f) { BroadcastDirector.Instance?.Pin(null); _shotAt = -1f; }
                if (DemoMode.Allowed && DemoMode.IdleSeconds >= s.idleSeconds) DemoMode.Begin();
                return;
            }
            if (Time.unscaledTime >= _hideAt) { HideHud(); _hideAt = Time.unscaledTime + 0.5f; }
            if (_leaving) return;

            var menu = FindAnyObjectByType<MenuView>();
            if (menu != null && menu.boxers != null && menu.boxers.Length >= 2)
            {
                int red = Random.Range(0, menu.boxers.Length), blue = Random.Range(0, menu.boxers.Length - 1);
                if (blue >= red) blue++;
                _leaving = true;
                Debug.Log($"[DemoMode] bout: {menu.boxers[red].name} v {menu.boxers[blue].name}");
                menu.FightPair(red, blue);
                return;
            }

            BroadcastDirector director = BroadcastDirector.Instance;
            if (director != null && s.cameras != null && s.cameras.Length > 0 && Time.unscaledTime - _shotAt >= s.cameraDwellSeconds)
            {
                director.Pin(s.cameras[_shot++ % s.cameras.Length]);
                _shotAt = Time.unscaledTime;
            }

            Bout bout = Bout.Instance;
            if (bout != null && bout.Phase == BoutPhase.Results && bout.PhaseAge >= s.resultSeconds && MatchSelection.HasMenu)
            {
                _leaving = true;
                SceneManager.LoadSceneAsync(MatchSelection.MenuScene);     // the boxer screen starts the next pair
            }
        }

        bool AnyInput()
        {
            bool moved = false;
            Vector3 mouse = Input.mousePosition;
            if (_mouseKnown && (mouse - _mouse).sqrMagnitude > 4f) moved = true;
            _mouse = mouse;
            _mouseKnown = true;
            // anyKeyDown covers the keyboard, the mouse buttons and gamepad (joystick) buttons
            return moved || Input.anyKeyDown || Input.touchCount > 0 || Input.mouseScrollDelta.sqrMagnitude > 0f;
        }

        /// <summary>Every UI document but the overlay's out of sight, remembering how each was.</summary>
        void HideHud()
        {
            foreach (UIDocument doc in FindObjectsByType<UIDocument>())
            {
                if (doc == null || doc.gameObject == gameObject || doc.rootVisualElement == null) continue;
                bool known = false;
                foreach (var h in _hidden) if (h.doc == doc) { known = true; break; }
                if (known) continue;
                _hidden.Add((doc, doc.rootVisualElement.resolvedStyle.display));
                doc.rootVisualElement.style.display = DisplayStyle.None;
            }
        }

        void ShowHud()
        {
            foreach (var (doc, _) in _hidden)
                if (doc != null && doc.rootVisualElement != null) doc.rootVisualElement.style.display = StyleKeyword.Null;
            _hidden.Clear();
        }
    }
}
