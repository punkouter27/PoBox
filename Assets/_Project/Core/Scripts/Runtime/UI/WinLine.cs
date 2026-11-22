using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace PoBox.UI
{
    /// <summary>
    /// The win probability over the whole bout as a line: the middle is even, up is red's, down is blue's, and the
    /// line takes the colour of whoever it favours. Behind it, faint, how much was happening (the excitement reading),
    /// and a tick where each round began. The same reading as the WIN % bar on the scoreboard (Excitement.RedShare),
    /// kept for the bout rather than the minute the bar's own history holds. On the round-break card, the result card
    /// and, with STATS, as a strip in the bottom-left. HudView feeds every one from one <see cref="Record"/>.
    /// Drawn with the vector painter: no texture.
    /// </summary>
    [UxmlElement]
    public partial class WinLine : VisualElement
    {
        /// <summary>A bout's readings, sampled while the fight is on.</summary>
        public sealed class Record
        {
            public readonly List<float> share = new List<float>(512), excitement = new List<float>(512);
            public readonly List<int> roundStarts = new List<int>(8);
            public void Clear() { share.Clear(); excitement.Clear(); roundStarts.Clear(); }
        }

        public Color red = new Color(0.89f, 0.24f, 0.24f, 1f);
        public Color blue = new Color(0.20f, 0.49f, 0.94f, 1f);
        public Color mid = new Color(1f, 1f, 1f, 0.22f);
        public Color busy = new Color(1f, 1f, 1f, 0.07f);

        Record _record;
        int _drawn = -1;

        public WinLine()
        {
            generateVisualContent += Draw;
            pickingMode = PickingMode.Ignore;
        }

        /// <summary>Shows this record; redrawn only when it has grown.</summary>
        public void Show(Record record)
        {
            if (record == _record && record != null && record.share.Count == _drawn) return;
            _record = record;
            _drawn = record != null ? record.share.Count : -1;
            MarkDirtyRepaint();
        }

        void Draw(MeshGenerationContext ctx)
        {
            Rect r = contentRect;
            if (r.width < 8f || r.height < 8f) return;
            Painter2D p = ctx.painter2D;
            float yMid = r.yMin + r.height * 0.5f;

            p.strokeColor = mid;
            p.lineWidth = 2f;
            p.BeginPath();
            p.MoveTo(new Vector2(r.xMin, yMid));
            p.LineTo(new Vector2(r.xMax, yMid));
            p.Stroke();

            List<float> share = _record?.share;
            int n = share != null ? share.Count : 0;
            if (n < 2) return;
            float X(int i) => r.xMin + r.width * i / (n - 1f);
            float Y(float s) => r.yMax - r.height * Mathf.Clamp01(s);

            // How much was happening: a faint fill up from the bottom edge.
            List<float> ex = _record.excitement;
            if (ex.Count == n)
            {
                p.fillColor = busy;
                p.BeginPath();
                p.MoveTo(new Vector2(r.xMin, r.yMax));
                for (int i = 0; i < n; i++) p.LineTo(new Vector2(X(i), r.yMax - r.height * Mathf.Clamp01(ex[i])));
                p.LineTo(new Vector2(r.xMax, r.yMax));
                p.ClosePath();
                p.Fill();
            }

            // Where each round after the first began.
            p.strokeColor = mid;
            p.lineWidth = 1.5f;
            foreach (int at in _record.roundStarts)
            {
                if (at <= 0 || at >= n) continue;
                p.BeginPath();
                p.MoveTo(new Vector2(X(at), r.yMin));
                p.LineTo(new Vector2(X(at), r.yMax));
                p.Stroke();
            }

            // The line, a segment at a time in the colour of the side it is on.
            p.lineWidth = 3f;
            p.lineCap = LineCap.Round;
            for (int i = 1; i < n; i++)
            {
                p.strokeColor = share[i - 1] + share[i] >= 1f ? red : blue;
                p.BeginPath();
                p.MoveTo(new Vector2(X(i - 1), Y(share[i - 1])));
                p.LineTo(new Vector2(X(i), Y(share[i])));
                p.Stroke();
            }
        }
    }
}
