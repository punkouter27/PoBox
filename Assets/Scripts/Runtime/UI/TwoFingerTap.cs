using UnityEngine;

namespace PoBox.UI
{
    /// <summary>
    /// Two fingers down together and lifted quickly without sliding: STATS on or off, TAB's twin on a phone. Three
    /// fingers are the debug panel's (DebugPanel.Hook), so a touch that ever had three is not this.
    /// </summary>
    public class TwoFingerTap
    {
        int _most;
        float _at;
        bool _moved;

        /// <summary>Asked once a frame: true on the frame the fingers of such a tap have all lifted.</summary>
        public bool Poll(float now)
        {
            int n = Input.touchCount;
            if (n > 0)
            {
                if (_most == 0) { _at = now; _moved = false; }
                _most = Mathf.Max(_most, n);
                for (int i = 0; i < n; i++) if (Input.GetTouch(i).deltaPosition.sqrMagnitude > 400f) _moved = true;
                return false;
            }
            bool tap = _most == 2 && !_moved && now - _at < 0.35f;
            _most = 0;
            return tap;
        }
    }
}
