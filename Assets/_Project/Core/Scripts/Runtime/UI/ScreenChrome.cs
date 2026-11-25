using UnityEngine.UIElements;

namespace PoBox.UI
{
    /// <summary>
    /// The part of a screen every view was writing out for itself: the docked panels (the settings sheet, the debug
    /// grid, the cup) sit in the same place, so one is open at a time, and opening one hides the others. The
    /// copies had drifted: the menu hid all three, the melee and testbed only the one opposite, so a screen could
    /// leave two panels docked in the same corner.
    /// </summary>
    public static class ScreenChrome
    {
        /// <summary>Opens or closes one docked panel, hiding every other one in <paramref name="others"/> when it opens.</summary>
        public static void Show(VisualElement which, bool open, params VisualElement[] others)
        {
            if (which == null) return;
            if (open)
                foreach (VisualElement other in others)
                    if (other != null && other != which) other.AddToClassList("hidden");
            which.EnableInClassList("hidden", !open);
        }

        /// <summary>The phone's back button on a screen with no history: puts every open panel away; true if anything was open.</summary>
        public static bool HideAll(params VisualElement[] which)
        {
            bool any = false;
            foreach (VisualElement panel in which)
            {
                if (panel == null || panel.ClassListContains("hidden")) continue;
                panel.AddToClassList("hidden");
                any = true;
            }
            return any;
        }
    }
}
