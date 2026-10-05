// Overlays.cs - everything TinyRec draws on top of your screen:
//   DrawLayer  - the Draw menu (boxes, arrows, pen, markers, text). Clicks pass through unless you're drawing.
//   FloatPanel - the small toolbar at the top of the screen (drawing, Record spots, picker)
//   Markers    - numbered rings showing auto-click spots
//   Badge      - the REC / PLAYING pill
//   Picker     - click spots on a dimmed screen
//   Snipper    - drag a box on a frozen screenshot to save an image
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using TinyRec.Win;
using Drawing = System.Drawing;
using Imaging = System.Drawing.Imaging;

namespace TinyRec.Overlays;

/// A borderless, transparent window covering every monitor.
public class OverlayWindow : Window
{
    public readonly Canvas Cv = new() { Background = Brushes.Transparent };
    public IntPtr Hwnd { get; private set; }
    bool clickThrough;

    public OverlayWindow(bool clickThrough, Brush background = null)
    {
        this.clickThrough = clickThrough;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = background ?? Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        ShowActivated = !clickThrough;
        Left = Top = 0;
        Width = Height = 200;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        Content = Cv;
        SourceInitialized += (_, _) =>
        {
            Hwnd = new WindowInteropHelper(this).Handle;
            Native.SetClickThrough(Hwnd, this.clickThrough);
            Cover();
        };
        Loaded += (_, _) => Cover();
    }

    /// Stretch over all monitors, in real pixels.
    public void Cover()
    {
        if (Hwnd == IntPtr.Zero) return;
        var (x, y, w, h) = Native.VirtualScreen();
        Native.SetWindowPos(Hwnd, Native.HWND_TOPMOST, x, y, w, h, Native.SWP_NOACTIVATE);
    }

    public void SetClickThrough(bool on)
    {
        clickThrough = on;
        if (Hwnd != IntPtr.Zero) Native.SetClickThrough(Hwnd, on);
    }

    public Point ToLocal(double x, double y) => Cv.PointFromScreen(new Point(x, y));

    public (int x, int y) ToScreen(Point p)
    {
        var s = Cv.PointToScreen(p);
        return ((int)Math.Round(s.X), (int)Math.Round(s.Y));
    }

    public void ShowQuiet()
    {
        if (!IsVisible) Show();
        Cover();
    }
}

/// The dark bar at the top-centre of the screen with a status text and buttons.
public sealed class FloatPanel : Window
{
    static readonly Brush Bg = Fz("#171a20"), Edge = Fz("#2c313b"), Fg = Fz("#e9ebf1");
    static readonly Dictionary<string, (Brush bg, Brush hover, Brush fg)> Kinds = new()
    {
        ["normal"] = (Fz("#262b34"), Fz("#323845"), Fz("#e9ebf1")),
        ["primary"] = (Fz("#4f6bff"), Fz("#6f8bff"), Fz("#ffffff")),
        ["danger"] = (Fz("#e5484d"), Fz("#ff5a63"), Fz("#ffffff")),
        ["tool"] = (Fz("#171a20"), Fz("#262b34"), Fz("#b9c0cc")),
        ["tool-on"] = (Fz("#2a3358"), Fz("#34406e"), Fz("#9fb2ff")),
    };

    static Brush Fz(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }

    readonly TextBlock title = new() { Foreground = Fg, FontWeight = FontWeights.SemiBold, FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock dot = new() { Text = "●", FontSize = 14, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 8, 0) };
    readonly StackPanel row = new() { Orientation = Orientation.Horizontal };
    double y = 14;
    IntPtr hwnd;

    public FloatPanel()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        Content = new Border
        {
            Background = Bg, BorderBrush = Edge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12),
            Padding = new Thickness(12, 7, 8, 7), Child = row,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 16, ShadowDepth = 2, Opacity = 0.4, Color = Colors.Black },
        };
        SourceInitialized += (_, _) =>
        {
            hwnd = new WindowInteropHelper(this).Handle;
            Native.NoActivate(hwnd);
        };
        SizeChanged += (_, _) => Place();
    }

    void Place()
    {
        Left = Math.Max(0, (SystemParameters.PrimaryScreenWidth - ActualWidth) / 2);
        Top = y;
    }

    public void Setup(string text, IEnumerable<(string text, Action act, string kind)> buttons, string dotHex = "#3ccf8e", double top = 14)
    {
        y = top;
        row.Children.Clear();
        dot.Foreground = Fz(dotHex);
        title.Text = text;
        row.Children.Add(dot);
        row.Children.Add(title);
        row.Children.Add(new Border { Width = 12 });
        foreach (var (t, act, kind) in buttons)
        {
            if (kind == "sep")
            {
                row.Children.Add(new Border { Width = 1, Height = 22, Background = Edge, Margin = new Thickness(8, 0, 8, 0) });
                continue;
            }
            var (bg, hover, fg) = Kinds.TryGetValue(kind, out var k) ? k : Kinds["normal"];
            var b = new Border
            {
                Background = bg, CornerRadius = new CornerRadius(7), Padding = new Thickness(11, 5, 11, 6), Margin = new Thickness(2, 0, 2, 0),
                Cursor = Cursors.Hand,
                Child = new TextBlock { Text = t, Foreground = fg, FontWeight = FontWeights.SemiBold, FontSize = 12 },
            };
            b.MouseEnter += (_, _) => b.Background = hover;
            b.MouseLeave += (_, _) => b.Background = bg;
            if (act != null) b.MouseLeftButtonUp += (_, e) => { e.Handled = true; act(); };
            row.Children.Add(b);
        }
        if (!IsVisible) Show();
        UpdateLayout();
        Place();
    }

    public void SetText(string text) => title.Text = text;

    public (int l, int t, int r, int b)? ScreenRect()
    {
        if (!IsVisible || hwnd == IntPtr.Zero) return null;
        Native.GetWindowRect(hwnd, out var r);
        return (r.Left, r.Top, r.Right, r.Bottom);
    }

    public void Lift()
    {
        if (!IsVisible) return;
        Topmost = false;
        Topmost = true;
    }
}

/// Numbered rings on a see-through, click-through layer.
public sealed class Markers : OverlayWindow
{
    public Markers() : base(true) { }

    public void ShowPoints(IList<(int x, int y)> points, string hex = "#3ccf8e")
    {
        ShowQuiet();
        UpdateLayout();
        Cv.Children.Clear();
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        for (int i = 0; i < points.Count; i++)
        {
            var p = ToLocal(points[i].x, points[i].y);
            var ring = new Ellipse { Width = 26, Height = 26, Stroke = brush, StrokeThickness = 3 };   // ring only: the exact spot stays clear
            Canvas.SetLeft(ring, p.X - 13);
            Canvas.SetTop(ring, p.Y - 13);
            var label = new TextBlock { Text = (i + 1).ToString(), Foreground = brush, FontWeight = FontWeights.Bold, FontSize = 14 };
            Canvas.SetLeft(label, p.X + 16);
            Canvas.SetTop(label, p.Y - 26);
            Cv.Children.Add(ring);
            Cv.Children.Add(label);
        }
    }

    public void HideAll()
    {
        Cv.Children.Clear();
        if (IsVisible) Hide();
    }
}

/// The REC / PLAYING pill at the top of the screen. Click-through, never takes focus.
public sealed class Badge : Window
{
    readonly Border pill;
    readonly TextBlock text = new() { Foreground = Brushes.White, FontWeight = FontWeights.Bold };
    string last;

    public Badge()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        pill = new Border { CornerRadius = new CornerRadius(20), Child = text, Opacity = 0.94 };
        Content = pill;
        SourceInitialized += (_, _) => Native.SetClickThrough(new WindowInteropHelper(this).Handle, true);
        SizeChanged += (_, _) => Left = Math.Max(0, (SystemParameters.PrimaryScreenWidth - ActualWidth) / 2);
    }

    public void ShowText(string t, string hex = "#e5484d", bool big = false)
    {
        var key = t + hex + big;
        if (key == last && IsVisible) return;
        last = key;
        text.Text = t;
        text.FontSize = big ? 22 : 13;
        pill.Padding = big ? new Thickness(28, 12, 28, 14) : new Thickness(16, 6, 16, 7);
        pill.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        Top = 14;
        if (!IsVisible) Show();
    }

    public void HideBadge()
    {
        last = null;
        if (IsVisible) Hide();
    }
}

/// The Draw menu. Drawings are stored in screen pixels (same drawings.json as the Python version).
public sealed class DrawLayer : OverlayWindow
{
    public sealed class Shape
    {
        public string tool { get; set; }
        public string color { get; set; }
        public double width { get; set; }
        public List<double> pts { get; set; } = new();
        public string text { get; set; }
    }

    public static readonly string[] Tools = { "rect", "ellipse", "line", "arrow", "freehand", "marker", "text" };
    public static readonly Dictionary<string, string> Labels = new()
    {
        ["rect"] = "Box", ["ellipse"] = "Circle", ["line"] = "Line", ["arrow"] = "Arrow", ["freehand"] = "Pen", ["marker"] = "Marker", ["text"] = "Text",
    };

    public List<Shape> Shapes = new();
    public string Tool = "rect", InkColor = "#ff4d5e";
    public double InkWidth = 3;
    public bool ShowDrawings = true;
    public bool Drawing { get; private set; }
    public Func<string> AskText;
    public event Action Changed;

    readonly string savePath;
    readonly FloatPanel panel = new();
    Shape cur;
    readonly List<UIElement> temp = new();
    readonly Brush catchAll = new SolidColorBrush(System.Windows.Media.Color.FromArgb(1, 0, 0, 0));   // 1/255 - invisible, but clickable

    public DrawLayer(string savePath) : base(true)
    {
        this.savePath = savePath;
        Load();
        Cv.MouseLeftButtonDown += Down;
        Cv.MouseMove += Drag;
        Cv.MouseLeftButtonUp += Up;
        Cv.MouseRightButtonDown += (_, _) => Stop();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Stop();
            else if (e.Key == Key.Z && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) Undo();
        };
        Cv.SizeChanged += (_, _) => Redraw();
    }

    void Load()
    {
        try
        {
            Shapes = JsonSerializer.Deserialize<List<Shape>>(File.ReadAllText(savePath)) ?? new();
            Shapes = Shapes.Where(s => s != null && Tools.Contains(s.tool) && s.pts != null && s.pts.Count >= 2).ToList();
        }
        catch { Shapes = new(); }
    }

    void Save()
    {
        try { Core.Files.AtomicWrite(savePath, JsonSerializer.SerializeToUtf8Bytes(Shapes), false); } catch { }
        Changed?.Invoke();
    }

    void Sync()
    {
        bool want = Drawing || (ShowDrawings && Shapes.Count > 0);
        if (want) ShowQuiet();
        else if (IsVisible) Hide();
    }

    public void Redraw()
    {
        if (!IsVisible) return;
        foreach (var el in Cv.Children.OfType<FrameworkElement>().Where(e => (string)e.Tag == "shape").ToList()) Cv.Children.Remove(el);
        if (ShowDrawings || Drawing)
            foreach (var s in Shapes)
                foreach (var el in Build(s)) { el.Tag = "shape"; Cv.Children.Add(el); }
    }

    List<FrameworkElement> Build(Shape s)
    {
        var outp = new List<FrameworkElement>();
        var brush = new SolidColorBrush(ParseColor(s.color));
        var pts = new List<Point>();
        for (int i = 0; i + 1 < s.pts.Count; i += 2) pts.Add(ToLocal(s.pts[i], s.pts[i + 1]));
        double w = Math.Max(1, s.width);
        switch (s.tool)
        {
            case "rect":
            case "ellipse":
            {
                if (pts.Count < 2) break;
                FrameworkElement r = s.tool == "rect" ? new Rectangle { Stroke = brush, StrokeThickness = w } : new Ellipse { Stroke = brush, StrokeThickness = w };
                r.Width = Math.Abs(pts[1].X - pts[0].X);
                r.Height = Math.Abs(pts[1].Y - pts[0].Y);
                Canvas.SetLeft(r, Math.Min(pts[0].X, pts[1].X));
                Canvas.SetTop(r, Math.Min(pts[0].Y, pts[1].Y));
                outp.Add(r);
                break;
            }
            case "line":
            case "arrow":
            {
                if (pts.Count < 2) break;
                outp.Add(new Line { X1 = pts[0].X, Y1 = pts[0].Y, X2 = pts[1].X, Y2 = pts[1].Y, Stroke = brush, StrokeThickness = w,
                                    StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
                if (s.tool == "arrow")
                {
                    var d = pts[1] - pts[0];
                    if (d.Length > 1)
                    {
                        d.Normalize();
                        var n = new Vector(-d.Y, d.X);
                        double len = 16 + w * 2, half = 7 + w;
                        var tip = pts[1];
                        var baseP = tip - d * len;
                        outp.Add(new Polygon { Fill = brush, Points = new PointCollection { tip, baseP + n * half, baseP - n * half } });
                    }
                }
                break;
            }
            case "freehand":
                if (pts.Count >= 2)
                    outp.Add(new Polyline { Points = new PointCollection(pts), Stroke = brush, StrokeThickness = w,
                                            StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
                break;
            case "marker":
            {
                var c = pts[0];
                double r = 14 + w;
                var ring = new Ellipse { Width = r * 2, Height = r * 2, Stroke = brush, StrokeThickness = w };
                Canvas.SetLeft(ring, c.X - r);
                Canvas.SetTop(ring, c.Y - r);
                outp.Add(ring);
                outp.Add(new Line { X1 = c.X - r - 8, Y1 = c.Y, X2 = c.X + r + 8, Y2 = c.Y, Stroke = brush, StrokeThickness = Math.Max(1, w - 1) });
                outp.Add(new Line { X1 = c.X, Y1 = c.Y - r - 8, X2 = c.X, Y2 = c.Y + r + 8, Stroke = brush, StrokeThickness = Math.Max(1, w - 1) });
                break;
            }
            case "text":
            {
                var t = new TextBlock { Text = s.text ?? "", Foreground = brush, FontWeight = FontWeights.Bold, FontSize = 12 + w * 2 };
                Canvas.SetLeft(t, pts[0].X);
                Canvas.SetTop(t, pts[0].Y);
                outp.Add(t);
                break;
            }
        }
        return outp;
    }

    static System.Windows.Media.Color ParseColor(string hex)
    {
        try { return (System.Windows.Media.Color)ColorConverter.ConvertFromString(hex); }
        catch { return Colors.Red; }
    }

    // ---- mouse
    void Down(object sender, MouseButtonEventArgs e)
    {
        if (!Drawing) return;
        var (x, y) = ToScreen(e.GetPosition(Cv));
        if (Tool == "text")
        {
            AddText(x, y);
            return;
        }
        cur = new Shape { tool = Tool, color = InkColor, width = InkWidth, pts = new List<double> { x, y, x, y } };
        if (Tool == "marker")
        {
            Commit();
            return;
        }
        Cv.CaptureMouse();
    }

    void Drag(object sender, MouseEventArgs e)
    {
        if (!Drawing || cur == null) return;
        var (x, y) = ToScreen(e.GetPosition(Cv));
        if (cur.tool == "freehand") { cur.pts.Add(x); cur.pts.Add(y); }
        else { cur.pts[2] = x; cur.pts[3] = y; }
        foreach (var el in temp) Cv.Children.Remove(el);
        temp.Clear();
        foreach (var el in Build(cur)) { temp.Add(el); Cv.Children.Add(el); }
    }

    void Up(object sender, MouseButtonEventArgs e)
    {
        if (Cv.IsMouseCaptured) Cv.ReleaseMouseCapture();
        if (cur != null && Drawing) Commit();
    }

    void Commit()
    {
        var s = cur;
        cur = null;
        foreach (var el in temp) Cv.Children.Remove(el);
        temp.Clear();
        if (s.tool is "rect" or "ellipse" or "line" or "arrow" && Math.Abs(s.pts[0] - s.pts[2]) + Math.Abs(s.pts[1] - s.pts[3]) < 4) return;
        Shapes.Add(s);
        Save();
        Redraw();
        PanelText();
    }

    void AddText(int x, int y)
    {
        if (AskText == null) return;
        Topmost = false;
        string t;
        try { t = AskText(); }
        finally { if (Drawing) { Topmost = true; panel.Lift(); Activate(); } }
        if (string.IsNullOrWhiteSpace(t)) return;
        Shapes.Add(new Shape { tool = "text", color = InkColor, width = InkWidth, pts = new List<double> { x, y }, text = t.Trim() });
        Save();
        Redraw();
        PanelText();
    }

    // ---- toolbar
    void PanelText()
    {
        if (Drawing) panel.SetText($"Drawing  ·  {Shapes.Count} shape{(Shapes.Count == 1 ? "" : "s")}");
    }

    void ShowPanel()
    {
        var items = Tools.Select(t => (Labels[t], (Action)(() => SetTool(t)), t == Tool ? "tool-on" : "tool")).ToList();
        items.Add(("", null, "sep"));
        items.Add(("Undo", Undo, "normal"));
        items.Add(("Done  (Esc)", Stop, "primary"));
        panel.Setup("Drawing", items, InkColor);
        PanelText();
    }

    public void SetTool(string t)
    {
        Tool = t;
        if (Drawing) ShowPanel();
        Changed?.Invoke();
    }

    // ---- public controls
    public void Start()
    {
        if (Drawing) return;
        Drawing = true;
        ShowQuiet();
        if (panel.Owner != this) panel.Owner = this;          // owned => the toolbar always stays above the drawing layer
        SetClickThrough(false);
        Cv.Background = catchAll;
        Cv.Cursor = Cursors.Cross;
        Redraw();
        ShowPanel();
        Activate();
        Focus();
        panel.Lift();
        Changed?.Invoke();
    }

    public void Stop()
    {
        if (!Drawing) return;
        Drawing = false;
        cur = null;
        foreach (var el in temp) Cv.Children.Remove(el);
        temp.Clear();
        Cv.Background = Brushes.Transparent;
        Cv.Cursor = null;
        SetClickThrough(true);
        panel.Hide();
        Redraw();
        Sync();
        Changed?.Invoke();
    }

    public void Toggle()
    {
        if (Drawing) Stop(); else Start();
    }

    public void SetVisible(bool v)
    {
        ShowDrawings = v;
        Sync();
        Redraw();
    }

    public void Suspend()
    {
        Stop();
        if (IsVisible) Hide();
    }

    public void Resume()
    {
        Sync();
        Redraw();
    }

    public void Undo()
    {
        if (Shapes.Count == 0) return;
        Shapes.RemoveAt(Shapes.Count - 1);
        Save();
        Redraw();
        Sync();
        PanelText();
    }

    public void Clear()
    {
        Shapes.Clear();
        Save();
        Redraw();
        Sync();
        PanelText();
    }

    /// Briefly ring a point on screen (not saved).
    public void Flash(int x, int y, string label, int ms = 2500)
    {
        ShowQuiet();
        UpdateLayout();
        var p = ToLocal(x, y);
        var brush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x00, 0xe6, 0x76));
        var ring = new Ellipse { Width = 52, Height = 52, Stroke = brush, StrokeThickness = 4 };
        Canvas.SetLeft(ring, p.X - 26);
        Canvas.SetTop(ring, p.Y - 26);
        var t = new TextBlock { Text = label, Foreground = brush, FontWeight = FontWeights.Bold, FontSize = 15 };
        Canvas.SetLeft(t, p.X + 32);
        Canvas.SetTop(t, p.Y - 11);
        Cv.Children.Add(ring);
        Cv.Children.Add(t);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Cv.Children.Remove(ring);
            Cv.Children.Remove(t);
            Sync();
        };
        timer.Start();
    }
}

public static class Picker
{
    /// Click on the (dimmed) screen to choose spot(s). Nothing underneath gets clicked.
    /// Single mode: the first click ends it. Multi mode: click as many as you like, then Done / Enter / right-click.
    public static List<(int x, int y)> Pick(string prompt, bool multi)
    {
        var w = new OverlayWindow(false, new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x4D, 0, 0, 0))) { Cursor = Cursors.Cross };
        var pts = new List<(int x, int y)>();
        var panel = new FloatPanel();
        bool ok = false, done = false;
        var frame = new DispatcherFrame();
        var green = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x3c, 0xcf, 0x8e));

        void Redraw()
        {
            w.Cv.Children.Clear();
            for (int i = 0; i < pts.Count; i++)
            {
                var p = w.ToLocal(pts[i].x, pts[i].y);
                var ring = new Ellipse { Width = 24, Height = 24, Stroke = green, StrokeThickness = 3 };
                Canvas.SetLeft(ring, p.X - 12);
                Canvas.SetTop(ring, p.Y - 12);
                var t = new TextBlock { Text = (i + 1).ToString(), Foreground = green, FontSize = 15, FontWeight = FontWeights.Bold };
                Canvas.SetLeft(t, p.X + 15);
                Canvas.SetTop(t, p.Y - 26);
                w.Cv.Children.Add(ring);
                w.Cv.Children.Add(t);
            }
            panel.SetText(multi ? $"{prompt}  ·  {pts.Count} spot{(pts.Count == 1 ? "" : "s")}" : prompt);
        }

        void Finish(bool k)
        {
            if (done) return;
            done = true;
            ok = k;
            panel.Close();
            w.Close();
            frame.Continue = false;
        }

        void Undo()
        {
            if (pts.Count == 0) return;
            pts.RemoveAt(pts.Count - 1);
            Redraw();
        }

        w.MouseLeftButtonDown += (_, e) =>
        {
            pts.Add(w.ToScreen(e.GetPosition(w.Cv)));
            if (!multi) Finish(true);
            else Redraw();
        };
        w.MouseRightButtonDown += (_, _) => Finish(multi);
        w.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Finish(false);
            else if (e.Key == Key.Enter) Finish(true);
            else if (e.Key == Key.Back) Undo();
        };
        w.Closed += (_, _) => { if (!done) { done = true; frame.Continue = false; panel.Close(); } };
        w.Show();
        panel.Owner = w;                                      // keep the bar above the dimmed layer
        w.Activate();
        w.Focus();
        var buttons = multi
            ? new List<(string, Action, string)> { ("Undo", Undo, "normal"), ("Cancel", () => Finish(false), "normal"), ("Done  (Enter)", () => Finish(true), "primary") }
            : new List<(string, Action, string)> { ("Cancel  (Esc)", () => Finish(false), "normal") };
        panel.Setup(prompt, buttons, "#3ccf8e", 60);
        Redraw();
        Dispatcher.PushFrame(frame);
        return ok ? pts : new List<(int x, int y)>();
    }
}

public static class Snipper
{
    /// Freeze the screen and let you drag a box. Returns the cropped picture, or null if cancelled.
    public static Drawing.Bitmap Snip()
    {
        var (shot, left, top) = ScreenGrab.GrabBitmap();
        BitmapSource src;
        var rect = new Drawing.Rectangle(0, 0, shot.Width, shot.Height);
        var data = shot.LockBits(rect, Imaging.ImageLockMode.ReadOnly, Imaging.PixelFormat.Format32bppArgb);
        try
        {
            src = BitmapSource.Create(shot.Width, shot.Height, 96, 96, PixelFormats.Bgr32, null, data.Scan0, data.Stride * shot.Height, data.Stride);
            src.Freeze();
        }
        finally { shot.UnlockBits(data); }

        var w = new OverlayWindow(false, Brushes.Black) { Cursor = Cursors.Cross };
        var img = new Image { Source = src, Stretch = Stretch.Fill };
        var dim = new Rectangle { Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x70, 0, 0, 0)) };
        var sel = new Rectangle { Stroke = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x3c, 0xcf, 0x8e)), StrokeThickness = 2,
                                  Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x22, 0x3c, 0xcf, 0x8e)), Visibility = Visibility.Collapsed };
        var hint = new Border
        {
            Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0xE6, 0x17, 0x1a, 0x20)), CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16, 8, 16, 9),
            Child = new TextBlock { Text = "Drag a box around the button or picture to match  ·  Esc to cancel", Foreground = Brushes.White, FontSize = 14, FontWeight = FontWeights.SemiBold },
        };
        w.Cv.Children.Add(img);
        w.Cv.Children.Add(dim);
        w.Cv.Children.Add(sel);
        w.Cv.Children.Add(hint);
        w.Cv.SizeChanged += (_, _) =>
        {
            img.Width = dim.Width = w.Cv.ActualWidth;
            img.Height = dim.Height = w.Cv.ActualHeight;
            hint.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var primary = w.ToLocal(Native.GetSystemMetrics(0) / 2.0, 0);
            Canvas.SetLeft(hint, primary.X - hint.DesiredSize.Width / 2);
            Canvas.SetTop(hint, primary.Y + 24);
        };

        Point? start = null;
        Drawing.Bitmap result = null;
        bool done = false;
        var frame = new DispatcherFrame();
        void Finish()
        {
            if (done) return;
            done = true;
            w.Close();
            frame.Continue = false;
        }
        w.MouseLeftButtonDown += (_, e) => { start = e.GetPosition(w.Cv); w.Cv.CaptureMouse(); };
        w.MouseMove += (_, e) =>
        {
            if (start is not { } s0) return;
            var p = e.GetPosition(w.Cv);
            sel.Visibility = Visibility.Visible;
            Canvas.SetLeft(sel, Math.Min(s0.X, p.X));
            Canvas.SetTop(sel, Math.Min(s0.Y, p.Y));
            sel.Width = Math.Abs(p.X - s0.X);
            sel.Height = Math.Abs(p.Y - s0.Y);
        };
        w.MouseLeftButtonUp += (_, e) =>
        {
            w.Cv.ReleaseMouseCapture();
            if (start is not { } s0) return;
            var a = w.ToScreen(s0);
            var b = w.ToScreen(e.GetPosition(w.Cv));
            start = null;
            int x0 = Math.Min(a.x, b.x) - left, y0 = Math.Min(a.y, b.y) - top;
            int x1 = Math.Max(a.x, b.x) - left, y1 = Math.Max(a.y, b.y) - top;
            x0 = Math.Clamp(x0, 0, shot.Width); x1 = Math.Clamp(x1, 0, shot.Width);
            y0 = Math.Clamp(y0, 0, shot.Height); y1 = Math.Clamp(y1, 0, shot.Height);
            if (x1 - x0 < 4 || y1 - y0 < 4)
            {
                sel.Visibility = Visibility.Collapsed;
                return;
            }
            result = shot.Clone(new Drawing.Rectangle(x0, y0, x1 - x0, y1 - y0), Imaging.PixelFormat.Format24bppRgb);
            Finish();
        };
        w.MouseRightButtonDown += (_, _) => Finish();
        w.KeyDown += (_, e) => { if (e.Key == Key.Escape) Finish(); };
        w.Closed += (_, _) => { if (!done) { done = true; frame.Continue = false; } };
        w.Show();
        w.Activate();
        w.Focus();
        Dispatcher.PushFrame(frame);
        shot.Dispose();
        return result;
    }
}
