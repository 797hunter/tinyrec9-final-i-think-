// Fx.cs - the moving parts of the look: the glow around cards (Neon theme), the pulsing ring behind Record,
// page fades, and the pulsing frame around the window. Everything here is drawn with plain shapes - no blur effects -
// so it costs next to nothing and text stays sharp.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Anim = System.Windows.Media.Animation;

namespace TinyRec.Ui;

/// A Border that can draw a soft glow just outside its edge. The glow colour comes from the "Glow" resource,
/// which is transparent in the Dark and Light themes - so there the card looks exactly as before.
public sealed class GlowBorder : Border
{
    public static readonly DependencyProperty GlowProperty =
        DependencyProperty.Register(nameof(Glow), typeof(Brush), typeof(GlowBorder),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public Brush Glow { get => (Brush)GetValue(GlowProperty); set => SetValue(GlowProperty, value); }

    protected override void OnRender(DrawingContext dc)
    {
        int Rings = Theme.GlowRings;
        if (Rings > 0 && Glow is SolidColorBrush g && g.Color.A > 0 && ActualWidth > 8 && ActualHeight > 8)
        {
            double r = CornerRadius.TopLeft;
            for (int i = Rings; i >= 1; i--)
            {
                double fade = 1 - i / (double)(Rings + 1);
                var pen = new Pen(new SolidColorBrush(Theme.Alpha(g.Color, g.Color.A / 255.0 * fade * fade * 0.55)), 1);
                pen.Freeze();
                dc.DrawRoundedRectangle(null, pen, new Rect(-i + 0.5, -i + 0.5, ActualWidth + 2 * i - 1, ActualHeight + 2 * i - 1), r + i, r + i);
            }
        }
        base.OnRender(dc);
    }
}

/// The coloured frame just inside the window's edge. It is drawn once; after that only its brush colour and its
/// opacity are animated, which Windows composites without redrawing the window - so it is cheap to leave running.
public sealed class PulseFrame : FrameworkElement
{
    const int Solid = 2, Soft = 9;            // a crisp 2 px line, then 9 px of glow fading inwards
    readonly SolidColorBrush brush = new(Colors.Transparent);
    string running;

    public PulseFrame()
    {
        IsHitTestVisible = false;
        SnapsToDevicePixels = true;
        Visibility = Visibility.Collapsed;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 40 || h < 40) return;
        var pen = new Pen(brush, 1);
        for (int i = 0; i < Solid + Soft; i++)
        {
            dc.PushOpacity(i < Solid ? 1 : 0.5 * Math.Pow(1 - (i - Solid) / (double)Soft, 2));
            dc.DrawRectangle(null, pen, new Rect(i + 0.5, i + 0.5, w - 2 * i - 1, h - 2 * i - 1));
            dc.Pop();
        }
    }

    /// Show the frame. One colour "breathes"; several fade from one to the next. seconds = one breath.
    /// animate = false gives a steady frame.
    public void Run(IReadOnlyList<Color> colors, double seconds, bool animate)
    {
        if (colors == null || colors.Count == 0) { Stop(); return; }
        var sig = string.Join(",", colors.Select(Theme.Hex)) + "|" + seconds.ToString("0.##") + "|" + animate;
        Visibility = Visibility.Visible;
        if (sig == running) return;
        running = sig;
        brush.BeginAnimation(SolidColorBrush.ColorProperty, null);
        BeginAnimation(OpacityProperty, null);
        brush.Color = colors[0];
        Opacity = 1;
        if (!animate) return;
        var breathe = new DoubleAnimation(1.0, 0.38, TimeSpan.FromSeconds(seconds / 2))
        {
            AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        Anim.Timeline.SetDesiredFrameRate(breathe, 30);
        BeginAnimation(OpacityProperty, breathe);
        if (colors.Count > 1)
        {
            var cycle = new ColorAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(seconds * colors.Count), RepeatBehavior = RepeatBehavior.Forever };
            for (int i = 0; i <= colors.Count; i++)
                cycle.KeyFrames.Add(new LinearColorKeyFrame(colors[i % colors.Count], KeyTime.FromPercent(i / (double)colors.Count)));
            Anim.Timeline.SetDesiredFrameRate(cycle, 30);
            brush.BeginAnimation(SolidColorBrush.ColorProperty, cycle);
        }
    }

    public void Stop()
    {
        if (running == null && Visibility == Visibility.Collapsed) return;
        running = null;
        brush.BeginAnimation(SolidColorBrush.ColorProperty, null);
        BeginAnimation(OpacityProperty, null);
        Visibility = Visibility.Collapsed;
    }
}

public static class Fx
{
    /// Master switch, and one switch per animation (Settings > Animations).
    public static bool Enabled = true, Ring = true, Pages = true;

    /// A new page slides up a few pixels and fades in.
    public static void FadeIn(FrameworkElement e, int ms = 170)
    {
        if (!Enabled || !Pages)
        {
            e.BeginAnimation(UIElement.OpacityProperty, null);
            e.Opacity = 1;
            e.RenderTransform = null;
            return;
        }
        var move = new TranslateTransform(0, 10);
        e.RenderTransform = move;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease };
        fade.Completed += (_, _) =>
        {
            if (!ReferenceEquals(e.RenderTransform, move)) return;      // another fade has started since
            e.BeginAnimation(UIElement.OpacityProperty, null);          // let go, so nothing is left held by the animation
            e.Opacity = 1;
            e.RenderTransform = null;
        };
        e.BeginAnimation(UIElement.OpacityProperty, fade);
        move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease });
    }

    /// Put a button in a holder that has a ring behind it; RingOn() makes the ring pulse outwards (used for Record).
    public static Grid WithRing(Button b, string colorKey, out Ellipse ring)
    {
        ring = new Ellipse
        {
            Width = b.Width, Height = b.Height, StrokeThickness = 2.5, IsHitTestVisible = false, Opacity = 0, Visibility = Visibility.Collapsed,
            RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(1, 1),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        ring.SetResourceReference(Shape.StrokeProperty, colorKey);
        var g = new Grid { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        g.Children.Add(ring);
        g.Children.Add(b);
        return g;
    }

    public static void RingOn(Ellipse ring, bool on)
    {
        if (ring == null) return;
        bool want = on && Enabled && Ring, isOn = ring.Visibility == Visibility.Visible;
        if (want == isOn) return;
        var scale = (ScaleTransform)ring.RenderTransform;
        if (!want)
        {
            ring.BeginAnimation(UIElement.OpacityProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            ring.Visibility = Visibility.Collapsed;
            return;
        }
        ring.Visibility = Visibility.Visible;
        var t = TimeSpan.FromSeconds(1.25);
        var grow = new DoubleAnimation(1.0, 1.55, t) { RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
        var fade = new DoubleAnimation(0.85, 0, t) { RepeatBehavior = RepeatBehavior.Forever };
        foreach (var a in new[] { grow, fade }) Anim.Timeline.SetDesiredFrameRate(a, 30);
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        ring.BeginAnimation(UIElement.OpacityProperty, fade);
    }
}

/// Pick a colour: a grid of swatches, or type any hex code. `live` is called with every colour tried, so the
/// caller can preview it for real; the caller puts the old colour back if this returns null (cancelled).
public static class ColorPick
{
    static IEnumerable<Color> Swatches()
    {
        for (int i = 0; i < 12; i++) yield return Theme.FromHsv(0, 0, 1 - i / 11.0);                      // white -> black
        // light tints at the top, then full colour, down to the near-black shades that make good backgrounds
        var rows = new (double s, double v)[] { (0.35, 1.0), (0.6, 1.0), (0.85, 1.0), (1.0, 0.85), (1.0, 0.62), (0.9, 0.4), (0.8, 0.22), (0.75, 0.11) };
        foreach (var (s, v) in rows)
            for (int i = 0; i < 12; i++) yield return Theme.FromHsv(i * 30, s, v);
    }

    public static string Ask(Window owner, string title, string initial, Action<string> live = null)
    {
        var d = new Dialog(owner, title, "Click a colour, or type a hex code like #7c4dff.", "draw");
        string current = Theme.CleanHex(initial) ?? "#7c4dff";
        var preview = new Border { Width = 92, Height = 34, CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(1) };
        preview.SetResourceReference(Border.BorderBrushProperty, "Line");
        var box = K.Field(current, 110, true);
        box.MaxLength = 7;
        var marks = new List<(Border b, string hex)>();
        bool fromCode = false;

        void Show(string hex, bool setBox, bool notify = true)
        {
            current = hex;
            preview.Background = new SolidColorBrush(Theme.C(hex));
            foreach (var (b, h) in marks) b.BorderThickness = new Thickness(h == hex ? 2 : 0);
            if (setBox)
            {
                fromCode = true;
                box.Text = hex;
                fromCode = false;
            }
            d.Error.Text = "";
            if (notify) live?.Invoke(hex);
        }

        var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 12 };
        foreach (var c in Swatches())
        {
            var hex = Theme.Hex(c);
            var sw = new Border
            {
                Width = 30, Height = 26, CornerRadius = new CornerRadius(6), Margin = new Thickness(2), Background = new SolidColorBrush(c),
                Cursor = System.Windows.Input.Cursors.Hand, ToolTip = hex,
            };
            sw.SetResourceReference(Border.BorderBrushProperty, "Text");
            // on mouse-DOWN, and handled: the dialog itself starts a window drag on mouse-down, which would swallow the click
            sw.MouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                Show(hex, true);
            };
            marks.Add((sw, hex));
            grid.Children.Add(sw);
        }
        box.TextChanged += (_, _) =>
        {
            if (fromCode) return;
            var typed = box.Text.Trim().TrimStart('#');                 // preview full codes only: "#7c4" on the way to "#7c4dff" isn't a colour yet
            var hex = typed.Length == 6 ? Theme.CleanHex(typed) : null;
            if (hex != null) Show(hex, false);
        };
        d.Body.Children.Add(grid);
        d.Body.Children.Add(K.H(K.T("Hex code", 13, "Muted").M(2, 0, 12, 0), box, preview.M(12, 0, 0, 0)).M(0, 12, 0, 0));
        Show(current, true, false);
        d.AddButton("Cancel", null);
        d.AddAction("Use this colour", () =>
        {
            var hex = Theme.CleanHex(box.Text);
            if (hex == null) { d.Error.Text = "That isn't a hex colour. Use 6 characters, 0-9 and a-f, like #ff3d86."; return; }
            d.Finish(hex);
        });
        return d.Run(box) as string;
    }
}
