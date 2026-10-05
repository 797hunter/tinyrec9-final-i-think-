// Timeline.cs - the visual timeline: a time ruler with clicks (dots), drags and held keys (bars) and image steps (diamonds).
// Drawn by hand (OnRender) so even a recording with tens of thousands of mouse moves stays instant.
//   wheel = zoom at the pointer     drag = slide left/right     click a mark = select that action     double-click = fit everything
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using TinyRec.Core;

namespace TinyRec.Ui;

public sealed class Timeline : FrameworkElement
{
    sealed class Mark
    {
        public double T0, T1;
        public int Lane, Index;                 // Index = position of the action in the recording (-1 for mouse-move bands)
        public string Kind = "", Label = "";    // click | rclick | drag | key | scroll | image | move
    }

    const double Gutter = 62, RulerH = 22, LaneH = 24;
    static readonly string[] LaneNames = { "Mouse", "Keys", "Images" };
    static readonly double[] TickSteps = { 0.05, 0.1, 0.2, 0.5, 1, 2, 5, 10, 15, 30, 60, 120, 300, 600, 1800, 3600 };
    static readonly Typeface Face = new("Segoe UI");

    readonly List<Mark> marks = new();
    readonly HashSet<int> selected = new();
    double duration = 1, pxPerSec, offset, playhead = -1;
    bool fit = true, dragging, moved;
    Point downAt;
    double downOffset;

    /// A mark was clicked: the index of that action in the recording.
    public event Action<int> Picked;

    public Timeline()
    {
        Height = RulerH + LaneH * LaneNames.Length + 6;
        ClipToBounds = true;
        Cursor = Cursors.Hand;
        ToolTip = "Wheel = zoom  ·  drag = slide  ·  click a mark = select it  ·  double-click = show everything";
        SizeChanged += (_, _) =>
        {
            Clamp();
            InvalidateVisual();
        };
        Theme.Changed += InvalidateVisual;
    }

    double PlotW => Math.Max(10, ActualWidth - Gutter - 8);
    double X(double t) => Gutter + (t - offset) * pxPerSec;
    double TimeAt(double x) => offset + (x - Gutter) / Math.Max(1e-9, pxPerSec);

    void Clamp()
    {
        double fitPps = PlotW / Math.Max(0.001, duration);
        if (fit || pxPerSec <= fitPps)
        {
            fit = true;
            pxPerSec = fitPps;
            offset = 0;
            return;
        }
        pxPerSec = Math.Min(pxPerSec, 4000);
        offset = Math.Clamp(offset, 0, Math.Max(0, duration - PlotW / pxPerSec));
    }

    // ---------------------------------------------------------------- data
    public void SetEvents(IList<Event> ev)
    {
        marks.Clear();
        duration = Math.Max(0.5, ev.Count > 0 ? ev.Max(e => e.T) : 0.5) * 1.02;
        var keyDown = new Dictionary<string, (double t, int i)>();
        var btnDown = new Dictionary<string, (double t, int i, int x, int y)>();
        double moveStart = -1, moveLast = -1;
        void EndMoves()
        {
            if (moveStart >= 0) marks.Add(new Mark { T0 = moveStart, T1 = moveLast, Lane = 0, Index = -1, Kind = "move" });
            moveStart = -1;
        }
        for (int i = 0; i < ev.Count; i++)
        {
            var e = ev[i];
            var k = e.Kind;
            if (k == "MOVE")
            {
                if (moveStart < 0) moveStart = e.T;
                else if (e.T - moveLast > 0.25)
                {
                    EndMoves();
                    moveStart = e.T;
                }
                moveLast = e.T;
            }
            else if (k == "SCROLL") marks.Add(new Mark { T0 = e.T, T1 = e.T, Lane = 0, Index = i, Kind = "scroll" });
            else if (k == "KEY_DOWN") keyDown[e.Args[0]] = (e.T, i);
            else if (k == "KEY_UP")
            {
                if (keyDown.Remove(e.Args[0], out var d))
                    marks.Add(new Mark { T0 = d.t, T1 = e.T, Lane = 1, Index = d.i, Kind = "key", Label = KeyLabel(e.Args[0]) });
            }
            else if (e.IsImage)
            {
                var act = e.Opt("key") != null ? (k == "IMAGE_CLICK" ? "click + " : "press ") + e.Opt("key") : k == "IMAGE_CLICK" ? "click" : "wait";
                marks.Add(new Mark { T0 = e.T, T1 = e.T, Lane = 2, Index = i, Kind = "image", Label = $"{e.Args[0]}  ({act})" });
            }
            else if (k.EndsWith("_DOWN", StringComparison.Ordinal)) btnDown[k[..^5]] = (e.T, i, e.IntArg(0), e.IntArg(1));
            else if (k.EndsWith("_UP", StringComparison.Ordinal))
            {
                var b = k[..^3];
                if (btnDown.Remove(b, out var d))
                {
                    bool drag = e.T - d.t > 0.3 || Math.Abs(e.IntArg(0) - d.x) + Math.Abs(e.IntArg(1) - d.y) > 8;
                    marks.Add(new Mark { T0 = d.t, T1 = e.T, Lane = 0, Index = d.i, Kind = drag ? "drag" : b == "LEFT" ? "click" : "rclick" });
                }
            }
        }
        EndMoves();
        foreach (var (key, d) in keyDown) marks.Add(new Mark { T0 = d.t, T1 = d.t, Lane = 1, Index = d.i, Kind = "key", Label = KeyLabel(key) });
        foreach (var d in btnDown.Values) marks.Add(new Mark { T0 = d.t, T1 = d.t, Lane = 0, Index = d.i, Kind = "click" });
        marks.Sort((a, b) => a.T0.CompareTo(b.T0));
        Clamp();
        InvalidateVisual();
    }

    static string KeyLabel(string token) => token.StartsWith("u:", StringComparison.Ordinal) && token.Length == 6
        ? ((char)Convert.ToInt32(token[2..], 16)).ToString()
        : token.Length == 1 ? token.ToUpperInvariant() : token;

    public void SetSelection(IEnumerable<int> indices)
    {
        selected.Clear();
        foreach (var i in indices) selected.Add(i);
        InvalidateVisual();
    }

    /// Seconds into the recording, or a negative number to hide the line.
    public void SetPlayhead(double t)
    {
        if (Math.Abs(t - playhead) < 0.004) return;
        bool both = t >= 0 && playhead >= 0 && pxPerSec > 0;
        bool samePixel = both && Math.Round(t * pxPerSec) == Math.Round(playhead * pxPerSec);
        playhead = t;
        if (samePixel) return;
        if (t >= 0 && !fit && pxPerSec > 0 && (t < offset || t > offset + PlotW / pxPerSec))     // keep it in view when zoomed in
        {
            offset = Math.Max(0, t - PlotW / pxPerSec * 0.1);
            Clamp();
        }
        InvalidateVisual();
    }

    public void ShowAll()
    {
        fit = true;
        Clamp();
        InvalidateVisual();
    }

    // ---------------------------------------------------------------- drawing
    static string TickText(double t, double step)
    {
        if (step < 1) return t.ToString("0.0#", CultureInfo.InvariantCulture) + "s";
        if (t < 60 && step < 60) return ((int)Math.Round(t)).ToString(CultureInfo.InvariantCulture) + "s";
        int s = (int)Math.Round(t);
        return s >= 3600 ? $"{s / 3600}:{s / 60 % 60:00}:{s % 60:00}" : $"{s / 60}:{s % 60:00}";
    }

    FormattedText Text(string s, double size, Brush b, bool bold = false) =>
        new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            bold ? new Typeface(Face.FontFamily, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal) : Face,
            size, b, VisualTreeHelper.GetDpi(this).PixelsPerDip);

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < Gutter + 20) return;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));          // so the whole strip takes the mouse
        if (pxPerSec <= 0) Clamp();
        Brush muted = Theme.B("Muted"), faint = Theme.B("Faint"), line = Theme.B("Line"), text = Theme.B("Text"),
              red = Theme.B("Red"), amber = Theme.B("Amber"), accent = Theme.B("Accent"), green = Theme.B("Green"), track = Theme.B("Card2");
        var linePen = new Pen(line, 1);

        // lanes
        for (int i = 0; i < LaneNames.Length; i++)
        {
            double top = RulerH + i * LaneH;
            dc.DrawRoundedRectangle(track, null, new Rect(Gutter, top + 2, w - Gutter - 8, LaneH - 4), 5, 5);
            var ft = Text(LaneNames[i], 11, muted);
            dc.DrawText(ft, new Point(Gutter - 10 - ft.Width, top + (LaneH - ft.Height) / 2));
        }

        dc.PushClip(new RectangleGeometry(new Rect(Gutter, 0, w - Gutter - 8, h)));

        // ruler
        double step = TickSteps.FirstOrDefault(s => s * pxPerSec >= 64, TickSteps[^1]);
        double t0 = Math.Floor(offset / step) * step, tEnd = offset + PlotW / pxPerSec;
        for (double t = t0; t <= tEnd + step; t += step)
        {
            double x = Math.Round(X(t)) + 0.5;
            if (x < Gutter - 1) continue;
            dc.DrawLine(linePen, new Point(x, RulerH - 6), new Point(x, RulerH + LaneH * LaneNames.Length));
            dc.DrawText(Text(TickText(t, step), 10.5, faint), new Point(x + 4, 3));
        }

        // marks
        var selPen = new Pen(text, 1.5);
        var lastLabelEnd = new double[LaneNames.Length];
        foreach (var m in marks)
        {
            double x0 = X(m.T0), x1 = X(m.T1);
            if (x1 < Gutter - 30 || x0 > w) continue;
            double top = RulerH + m.Lane * LaneH, mid = top + LaneH / 2;
            bool sel = m.Index >= 0 && selected.Contains(m.Index);
            switch (m.Kind)
            {
                case "move":
                    dc.DrawRectangle(faint, null, new Rect(x0, top + LaneH - 7, Math.Max(1.5, x1 - x0), 2));
                    break;
                case "scroll":
                    dc.DrawRectangle(amber, sel ? selPen : null, new Rect(x0 - 1.5, mid - 6, 3, 12));
                    break;
                case "click":
                case "rclick":
                    dc.DrawEllipse(m.Kind == "click" ? red : amber, sel ? selPen : null, new Point(x0, mid - 1), 4.5, 4.5);
                    break;
                case "drag":
                    dc.DrawRoundedRectangle(red, sel ? selPen : null, new Rect(x0 - 3, mid - 5.5, Math.Max(7, x1 - x0 + 6), 9), 4.5, 4.5);
                    break;
                case "key":
                {
                    double bw = Math.Max(5, x1 - x0);
                    dc.DrawRoundedRectangle(accent, sel ? selPen : null, new Rect(x0, mid - 6, bw, 12), 3, 3);
                    double lx = x0 + bw + 4;
                    if (lx > lastLabelEnd[m.Lane] && pxPerSec > 12)
                    {
                        var ft = Text(m.Label, 10, text, true);
                        dc.DrawText(ft, new Point(lx, mid - ft.Height / 2));
                        lastLabelEnd[m.Lane] = lx + ft.Width + 6;
                    }
                    break;
                }
                case "image":
                {
                    var g = new StreamGeometry();
                    using (var c = g.Open())
                    {
                        c.BeginFigure(new Point(x0, mid - 7), true, true);
                        c.LineTo(new Point(x0 + 7, mid), false, false);
                        c.LineTo(new Point(x0, mid + 7), false, false);
                        c.LineTo(new Point(x0 - 7, mid), false, false);
                    }
                    dc.DrawGeometry(green, sel ? selPen : null, g);
                    var ft = Text(m.Label, 10.5, text);
                    double lx = x0 + 11;
                    if (lx > lastLabelEnd[m.Lane])
                    {
                        dc.DrawText(ft, new Point(lx, mid - ft.Height / 2));
                        lastLabelEnd[m.Lane] = lx + ft.Width + 10;
                    }
                    break;
                }
            }
        }

        // playhead
        if (playhead >= 0)
        {
            double x = Math.Round(X(playhead)) + 0.5;
            if (Theme.Neon || (Theme.Custom && Theme.GlowRings > 0))      // a soft glow either side of the line
            {
                var c = Theme.Col("Accent");
                for (int i = 4; i >= 1; i--)
                    dc.DrawLine(new Pen(new SolidColorBrush(Theme.Alpha(c, 0.16 - i * 0.03)), 2 + i * 3), new Point(x, 2), new Point(x, h - 2));
            }
            dc.DrawLine(new Pen(accent, 2), new Point(x, 2), new Point(x, h - 2));
        }
        dc.Pop();
    }

    // ---------------------------------------------------------------- mouse
    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        double x = Math.Max(Gutter, e.GetPosition(this).X), t = TimeAt(x);
        fit = false;
        pxPerSec *= e.Delta > 0 ? 1.3 : 1 / 1.3;
        pxPerSec = Math.Min(pxPerSec, 4000);
        offset = t - (x - Gutter) / pxPerSec;
        Clamp();
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ShowAll();
            return;
        }
        dragging = true;
        moved = false;
        downAt = e.GetPosition(this);
        downOffset = offset;
        CaptureMouse();
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        dragging = false;
        base.OnLostMouseCapture(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!dragging) return;
        double dx = e.GetPosition(this).X - downAt.X;
        if (Math.Abs(dx) > 4) moved = true;
        if (!moved || fit) return;
        offset = downOffset - dx / pxPerSec;
        Clamp();
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (!dragging) return;
        dragging = false;
        ReleaseMouseCapture();
        if (moved) return;
        var p = e.GetPosition(this);
        int lane = (int)Math.Floor((p.Y - RulerH) / LaneH);
        Mark best = null;
        double bestD = 9;                                            // pixels
        foreach (var m in marks)
        {
            if (m.Index < 0 || (lane >= 0 && lane < LaneNames.Length && m.Lane != lane)) continue;
            double x0 = X(m.T0), x1 = X(m.T1);
            double d = p.X < x0 ? x0 - p.X : p.X > x1 ? p.X - x1 : 0;
            if (d < bestD)
            {
                bestD = d;
                best = m;
            }
        }
        if (best != null) Picked?.Invoke(best.Index);
    }
}
