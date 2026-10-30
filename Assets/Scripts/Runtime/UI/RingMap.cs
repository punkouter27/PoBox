using UnityEngine;
using UnityEngine.UIElements;

namespace PoBox.UI
{
    /// <summary>
    /// The ring from above: the canvas inside the ropes, each boxer a dot in its corner's colour with a fading trail
    /// of the last few seconds, and the centre circle filled by whoever has held it this round (the one nearer the
    /// middle by more than a step, the same idea as the judges' centre share). World x runs across and z up the map.
    /// The settings' MAP (VIEW tab) puts it over the picture, bottom-right.
    /// Drawn with the vector painter: no texture.
    /// </summary>
    [UxmlElement]
    public partial class RingMap : VisualElement
    {
        const int Trail = 24;                     // points kept a boxer, one every TrailSeconds
        const float TrailSeconds = 0.125f;

        public Color red = new Color(0.89f, 0.24f, 0.24f, 1f);
        public Color blue = new Color(0.20f, 0.49f, 0.94f, 1f);
        public Color canvas = new Color(1f, 1f, 1f, 0.06f);
        public Color ropes = new Color(1f, 1f, 1f, 0.35f);

        readonly Vector2[,] _trail = new Vector2[2, Trail];
        readonly bool[] _down = new bool[2];
        int _head, _count;
        float _half = 3.05f, _nextPoint, _redHeld, _blueHeld;
        Vector2 _centre;

        public RingMap()
        {
            generateVisualContent += Draw;
            pickingMode = PickingMode.Ignore;
        }

        /// <summary>A new bout or round: no trail, no one holding the centre.</summary>
        public void Clear()
        {
            _count = 0;
            _redHeld = _blueHeld = 0f;
            MarkDirtyRepaint();
        }

        /// <summary>Where the two are (world x, z), whether each is down, the ring's middle and half-width; dt is the fight's own time.</summary>
        public void Set(Vector2 redAt, bool redDown, Vector2 blueAt, bool blueDown, Vector2 centre, float half, float dt, float now)
        {
            _centre = centre;
            _half = Mathf.Max(0.5f, half);
            _down[0] = redDown;
            _down[1] = blueDown;
            float r = (redAt - centre).magnitude, b = (blueAt - centre).magnitude;
            if (!redDown && r < b - 0.3f) _redHeld += dt;
            else if (!blueDown && b < r - 0.3f) _blueHeld += dt;
            if (now >= _nextPoint || _count == 0)
            {
                _nextPoint = now + TrailSeconds;
                _head = (_head + 1) % Trail;
                _count = Mathf.Min(Trail, _count + 1);
            }
            _trail[0, _head] = redAt;
            _trail[1, _head] = blueAt;
            MarkDirtyRepaint();
        }

        /// <summary>Red's share of the time either held the centre this round, 0..1; a half when neither has.</summary>
        public float RedCentreShare => _redHeld + _blueHeld > 0.01f ? _redHeld / (_redHeld + _blueHeld) : 0.5f;

        void Draw(MeshGenerationContext ctx)
        {
            Rect area = contentRect;
            if (area.width < 16f || area.height < 16f) return;
            Painter2D p = ctx.painter2D;
            float side = Mathf.Min(area.width, area.height) - 8f;
            var box = new Rect(area.center.x - side * 0.5f, area.center.y - side * 0.5f, side, side);
            // World (x, z) to the map: x across, z up the screen.
            Vector2 M(Vector2 w)
            {
                Vector2 u = (w - _centre) / (2f * _half) + new Vector2(0.5f, 0.5f);
                return new Vector2(box.xMin + Mathf.Clamp01(u.x) * box.width, box.yMax - Mathf.Clamp01(u.y) * box.height);
            }

            p.fillColor = canvas;
            p.strokeColor = ropes;
            p.lineWidth = 3f;
            p.BeginPath();
            p.MoveTo(new Vector2(box.xMin, box.yMin));
            p.LineTo(new Vector2(box.xMax, box.yMin));
            p.LineTo(new Vector2(box.xMax, box.yMax));
            p.LineTo(new Vector2(box.xMin, box.yMax));
            p.ClosePath();
            p.Fill();
            p.Stroke();

            // The centre circle, filled by whoever has held it, as strongly as they have.
            float share = RedCentreShare, lead = Mathf.Abs(share - 0.5f) * 2f;
            Color hold = share >= 0.5f ? red : blue;
            p.fillColor = new Color(hold.r, hold.g, hold.b, 0.08f + 0.32f * lead);
            p.strokeColor = ropes;
            p.lineWidth = 2f;
            p.BeginPath();
            p.Arc(box.center, side * 0.17f, 0f, 360f);
            p.Fill();
            p.Stroke();

            if (_count == 0) return;
            p.lineCap = LineCap.Round;
            for (int k = 0; k < 2; k++)
            {
                Color c = k == 0 ? red : blue;
                // The trail, oldest faintest.
                for (int i = 1; i < _count; i++)
                {
                    int a = (_head - i + 1 + Trail) % Trail, b = (_head - i + Trail) % Trail;
                    float fade = 1f - i / (float)_count;
                    p.strokeColor = new Color(c.r, c.g, c.b, 0.6f * fade);
                    p.lineWidth = 2f + 3f * fade;
                    p.BeginPath();
                    p.MoveTo(M(_trail[k, a]));
                    p.LineTo(M(_trail[k, b]));
                    p.Stroke();
                }
                // The boxer: a dot, hollow while it is down.
                Vector2 at = M(_trail[k, _head]);
                p.BeginPath();
                p.Arc(at, side * 0.045f, 0f, 360f);
                if (_down[k])
                {
                    p.strokeColor = c;
                    p.lineWidth = 3f;
                    p.Stroke();
                }
                else
                {
                    p.fillColor = c;
                    p.Fill();
                }
            }
        }
    }
}
