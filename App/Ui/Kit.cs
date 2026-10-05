// Kit.cs - small building blocks the pages are made from (cards, buttons, switches, steppers, icons...).
// Pages are built in C#, so any mistake is caught by the compiler instead of at run time.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TinyRec.Ui;

/// Attached properties used by the button template (hover colour, corner radius).
public static class Att
{
    public static readonly DependencyProperty HoverProperty =
        DependencyProperty.RegisterAttached("Hover", typeof(Brush), typeof(Att), new FrameworkPropertyMetadata(null));
    public static Brush GetHover(DependencyObject o) => (Brush)o.GetValue(HoverProperty);
    public static void SetHover(DependencyObject o, Brush v) => o.SetValue(HoverProperty, v);

    public static readonly DependencyProperty RadiusProperty =
        DependencyProperty.RegisterAttached("Radius", typeof(CornerRadius), typeof(Att), new FrameworkPropertyMetadata(new CornerRadius(10)));
    public static CornerRadius GetRadius(DependencyObject o) => (CornerRadius)o.GetValue(RadiusProperty);
    public static void SetRadius(DependencyObject o, CornerRadius v) => o.SetValue(RadiusProperty, v);
}

/// An icon drawn from a white PNG used as a mask, so it takes any colour (it follows the text colour by default).
public sealed class Ico : FrameworkElement
{
    public static readonly DependencyProperty KindProperty =
        DependencyProperty.Register(nameof(Kind), typeof(string), typeof(Ico), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner(typeof(Ico), new FrameworkPropertyMetadata(Brushes.White,
            FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));

    public string Kind { get => (string)GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public Brush Foreground { get => (Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

    static readonly Dictionary<string, ImageBrush> Masks = new();

    public static ImageBrush Mask(string kind)
    {
        if (string.IsNullOrEmpty(kind)) return null;
        if (!Masks.TryGetValue(kind, out var b))
        {
            try
            {
                var img = new BitmapImage();
                img.BeginInit();
                img.UriSource = new Uri($"pack://application:,,,/Assets/icons/{kind}.png", UriKind.Absolute);
                img.CacheOption = BitmapCacheOption.OnLoad;
                img.EndInit();
                img.Freeze();
                b = new ImageBrush(img) { Stretch = Stretch.Uniform };
                b.Freeze();
            }
            catch { b = null; }
            Masks[kind] = b;
        }
        return b;
    }

    public Ico()
    {
        Width = Height = 16;
        SnapsToDevicePixels = true;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var m = Mask(Kind);
        if (m == null) return;
        dc.PushOpacityMask(m);
        dc.DrawRectangle(Foreground, null, new Rect(RenderSize));
        dc.Pop();
    }
}

public static class K
{
    public static readonly FontFamily Mono = new("Cascadia Mono, Consolas, Courier New");

    public static T Res<T>(this T e, DependencyProperty p, string key) where T : FrameworkElement
    {
        e.SetResourceReference(p, key);
        return e;
    }

    public static T M<T>(this T e, double l, double t, double r, double b) where T : FrameworkElement
    {
        e.Margin = new Thickness(l, t, r, b);
        return e;
    }

    public static T M<T>(this T e, double all) where T : FrameworkElement
    {
        e.Margin = new Thickness(all);
        return e;
    }

    public static T Tip<T>(this T e, string tip) where T : FrameworkElement
    {
        e.ToolTip = tip;
        ToolTipService.SetInitialShowDelay(e, 450);
        return e;
    }

    public static T At<T>(this T e, int row, int col, int rowSpan = 1, int colSpan = 1) where T : UIElement
    {
        System.Windows.Controls.Grid.SetRow(e, row);             // (fully named: K has its own Grid() helper)
        System.Windows.Controls.Grid.SetColumn(e, col);
        if (rowSpan > 1) System.Windows.Controls.Grid.SetRowSpan(e, rowSpan);
        if (colSpan > 1) System.Windows.Controls.Grid.SetColumnSpan(e, colSpan);
        return e;
    }

    public static T Align<T>(this T e, HorizontalAlignment h, VerticalAlignment v = VerticalAlignment.Center) where T : FrameworkElement
    {
        e.HorizontalAlignment = h;
        e.VerticalAlignment = v;
        return e;
    }

    public static TextBlock T(string text, double size = 13, string color = "Text", FontWeight? weight = null, bool wrap = false)
    {
        var t = new TextBlock { Text = text, FontSize = size, VerticalAlignment = VerticalAlignment.Center };
        if (color != null) t.SetResourceReference(TextBlock.ForegroundProperty, color);
        if (weight != null) t.FontWeight = weight.Value;
        if (wrap) t.TextWrapping = TextWrapping.Wrap;
        return t;
    }

    public static Ico Icon(string kind, double size = 16, string color = null)
    {
        var i = new Ico { Kind = kind, Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };
        if (color != null) i.SetResourceReference(Ico.ForegroundProperty, color);
        return i;
    }

    public static StackPanel H(params UIElement[] kids)
    {
        var s = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var k in kids) if (k != null) s.Children.Add(k);
        return s;
    }

    public static StackPanel V(params UIElement[] kids)
    {
        var s = new StackPanel { Orientation = Orientation.Vertical };
        foreach (var k in kids) if (k != null) s.Children.Add(k);
        return s;
    }

    public static Grid Grid(string cols = null, string rows = null)
    {
        var g = new Grid();
        foreach (var c in (cols ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)) g.ColumnDefinitions.Add(new ColumnDefinition { Width = Len(c) });
        foreach (var r in (rows ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)) g.RowDefinitions.Add(new RowDefinition { Height = Len(r) });
        return g;
    }

    static GridLength Len(string s)
    {
        s = s.Trim();
        if (s == "auto") return GridLength.Auto;
        if (s.EndsWith("*")) return new GridLength(s.Length == 1 ? 1 : double.Parse(s[..^1], CultureInfo.InvariantCulture), GridUnitType.Star);
        return new GridLength(double.Parse(s, CultureInfo.InvariantCulture));
    }

    public static Grid Add(this Grid g, params UIElement[] kids)
    {
        foreach (var k in kids) g.Children.Add(k);
        return g;
    }

    public static Border Card(UIElement child = null, double pad = 0, double radius = 14)
    {
        var b = new GlowBorder { CornerRadius = new CornerRadius(radius), BorderThickness = new Thickness(1), Padding = new Thickness(pad), Child = child };
        b.SetResourceReference(Border.BackgroundProperty, "Card");
        b.SetResourceReference(Border.BorderBrushProperty, "Line");
        b.SetResourceReference(GlowBorder.GlowProperty, "Glow");          // transparent except in the Neon theme
        return b;
    }

    public static Border Box(UIElement child, string bg = "Card2", double radius = 10, double padH = 0, double padV = 0)
    {
        var b = new Border { CornerRadius = new CornerRadius(radius), Padding = new Thickness(padH, padV, padH, padV), Child = child };
        if (bg != null) b.SetResourceReference(Border.BackgroundProperty, bg);
        return b;
    }

    public static FrameworkElement CardTitle(string title, string icon, string subtitle = null)
    {
        var s = H(Icon(icon, 16, "Muted").M(0, 0, 9, 0), T(title, 15, "Text", FontWeights.SemiBold));
        if (subtitle != null) s.Children.Add(T("   " + subtitle, 12, "Muted"));
        s.Margin = new Thickness(18, 16, 18, 8);
        return s;
    }

    public static Border Divider(bool vertical = false, double margin = 0)
    {
        var d = vertical ? new Border { Width = 1, Margin = new Thickness(margin, 0, margin, 0) }
                         : new Border { Height = 1, Margin = new Thickness(0, margin, 0, margin) };
        d.SetResourceReference(Border.BackgroundProperty, "Line");
        return d;
    }

    static readonly Dictionary<string, string> StyleOf = new()
    {
        ["secondary"] = "Btn", ["primary"] = "BtnPrimary", ["danger"] = "BtnDanger", ["ghost"] = "BtnGhost", ["soft"] = "BtnSoft",
    };

    public static Button Btn(string text, Action click, string kind = "secondary", string icon = null, double height = 36)
    {
        var b = new Button { Height = height };
        b.SetResourceReference(FrameworkElement.StyleProperty, StyleOf[kind]);
        SetBtnContent(b, text, icon);
        if (click != null) b.Click += (_, _) => click();
        return b;
    }

    public static void SetBtnContent(Button b, string text, string icon, double iconSize = 16)
    {
        var s = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (icon != null) s.Children.Add(Icon(icon, iconSize).M(0, 0, string.IsNullOrEmpty(text) ? 0 : 8, 0));
        if (!string.IsNullOrEmpty(text)) s.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        b.Content = s;
    }

    public static Button IconBtn(string icon, Action click, string tip, double size = 34, string color = "Muted", string bg = null)
    {
        var b = new Button { Width = size, Height = size, Padding = new Thickness(0) };
        b.SetResourceReference(FrameworkElement.StyleProperty, "BtnGhost");
        if (bg != null) b.SetResourceReference(Control.BackgroundProperty, bg);
        var i = Icon(icon, Math.Round(size * 0.5));
        i.SetResourceReference(Ico.ForegroundProperty, color);
        b.Content = i;
        if (click != null) b.Click += (_, _) => click();
        if (tip != null) b.Tip(tip);
        return b;
    }

    /// A true circle. color: "Accent", "Red" or null (neutral).
    public static Button Round(string icon, Action click, string color, double size, string tip = null)
    {
        var b = new Button { Width = size, Height = size, Padding = new Thickness(0) };
        b.SetResourceReference(FrameworkElement.StyleProperty, color == null ? "Btn" : "BtnPrimary");
        if (color != null)
        {
            // the accent button takes the two-colour fill in the Neon theme (it is the plain accent in the others)
            b.SetResourceReference(Control.BackgroundProperty, color == "Accent" ? "AccentFill" : color);
            b.SetResourceReference(Att.HoverProperty, color == "Accent" ? "AccentFillHover" : color + "Hover");
        }
        Att.SetRadius(b, new CornerRadius(size / 2));
        b.Content = Icon(icon, Math.Round(size * 0.4));
        if (click != null) b.Click += (_, _) => click();
        if (tip != null) b.Tip(tip);
        return b;
    }

    public static void SetRoundIcon(Button b, string icon) => ((Ico)b.Content).Kind = icon;

    public static Border Chip(string text, string fg = "Muted", string bg = "Card2", double size = 11, bool mono = true)
    {
        var t = new TextBlock { Text = text, FontSize = size, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        if (mono) t.FontFamily = Mono;
        t.SetResourceReference(TextBlock.ForegroundProperty, fg);
        var b = new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(7, 2, 7, 3), Child = t,
                             HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        b.SetResourceReference(Border.BackgroundProperty, bg);
        return b;
    }

    public static void SetChip(Border chip, string text, string fg = null)
    {
        var t = (TextBlock)chip.Child;
        t.Text = text;
        if (fg != null) t.SetResourceReference(TextBlock.ForegroundProperty, fg);
    }

    public static CheckBox Switch(string text, bool value, Action<bool> changed = null)
    {
        var c = new CheckBox { Content = text, IsChecked = value, VerticalAlignment = VerticalAlignment.Center };
        c.SetResourceReference(FrameworkElement.StyleProperty, "Switch");
        if (changed != null)
        {
            c.Checked += (_, _) => changed(true);
            c.Unchecked += (_, _) => changed(false);
        }
        return c;
    }

    public static TextBox Field(string text = "", double width = 120, bool mono = false)
    {
        var t = new TextBox { Text = text ?? "", Width = width };
        t.SetResourceReference(FrameworkElement.StyleProperty, "FieldStyle");
        if (mono) t.FontFamily = Mono;
        return t;
    }

    public static Slider Slide(double min, double max, double value, Action<double> changed, double width = 200)
    {
        var s = new Slider { Minimum = min, Maximum = max, Value = value, Width = width, VerticalAlignment = VerticalAlignment.Center };
        s.SetResourceReference(FrameworkElement.StyleProperty, "Slide");
        if (changed != null) s.ValueChanged += (_, e) => changed(e.NewValue);
        return s;
    }

    public static (Border tile, TextBlock value) Tile(string caption, string value = "—", double size = 18)
    {
        var v = T(value, size, "Text", FontWeights.SemiBold);
        var s = V(v, T(caption, 11, "Muted"));
        var b = Box(s, "Card2", 10, 12, 8);
        return (b, v);
    }

    public static ContextMenu Menu(params (string text, Action act)[] items)
    {
        var m = new ContextMenu();
        foreach (var (text, act) in items)
        {
            if (text == "-") { m.Items.Add(new Separator()); continue; }
            var mi = new MenuItem { Header = text.Replace("_", "__") };      // a single _ would be eaten as a shortcut marker
            mi.Click += (_, _) => act();
            m.Items.Add(mi);
        }
        return m;
    }

    public static void Popup(this ContextMenu m, FrameworkElement under)
    {
        m.PlacementTarget = under;
        m.Placement = PlacementMode.Bottom;
        m.IsOpen = true;
    }
}

/// [ Once | Repeat | Forever ] - a row of options where one is chosen.
public sealed class Segmented : Border
{
    readonly Dictionary<string, RadioButton> buttons = new();
    readonly StackPanel row = new() { Orientation = Orientation.Horizontal };
    string value;
    bool quiet;
    public event Action<string> Changed;
    static int groups;

    public Segmented(IEnumerable<string> values, string selected, Action<string> changed = null, double minItemWidth = 0)
    {
        CornerRadius = new CornerRadius(10);
        Padding = new Thickness(3);
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Center;
        SetResourceReference(BackgroundProperty, "Card2");
        Child = row;
        var group = "seg" + groups++;
        foreach (var v in values)
        {
            var rb = new RadioButton { Content = v, GroupName = group, MinWidth = minItemWidth };
            rb.SetResourceReference(StyleProperty, "Seg");
            var val = v;
            rb.Checked += (_, _) =>
            {
                value = val;
                if (!quiet) Changed?.Invoke(val);
            };
            buttons[v] = rb;
            row.Children.Add(rb);
        }
        Set(selected, false);
        if (changed != null) Changed += changed;
    }

    public string Value => value;

    public void Set(string v, bool raise = true)
    {
        if (v == null || !buttons.TryGetValue(v, out var rb)) return;
        quiet = !raise;
        rb.IsChecked = true;
        value = v;
        quiet = false;
    }

    public void Enable(bool on)
    {
        IsEnabled = on;
        Opacity = on ? 1 : 0.5;
    }
}

/// [ - ]  value  [ + ]
public sealed class Stepper : Border
{
    readonly TextBox box;
    public double Step, Min, Max;
    public bool Integer;
    public event Action<double> Changed;

    public Stepper(double value, double step, double min, double max, bool integer = true, double width = 60, Action<double> changed = null)
    {
        Step = step; Min = min; Max = max; Integer = integer;
        CornerRadius = new CornerRadius(9);
        BorderThickness = new Thickness(1);
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Center;
        SetResourceReference(BackgroundProperty, "Field");
        SetResourceReference(BorderBrushProperty, "Line");
        box = new TextBox { Width = width, Height = 30, BorderThickness = new Thickness(0), TextAlignment = TextAlignment.Center, FontWeight = FontWeights.SemiBold };
        box.SetResourceReference(StyleProperty, "FieldStyle");
        box.Padding = new Thickness(2, 0, 2, 0);
        box.SetResourceReference(Control.BackgroundProperty, "Field");
        box.LostKeyboardFocus += (_, _) => Clean();
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) Clean();
            else if (e.Key == Key.Up) { Bump(1); e.Handled = true; }
            else if (e.Key == Key.Down) { Bump(-1); e.Handled = true; }
        };
        box.PreviewMouseWheel += (_, e) =>
        {
            if (!box.IsKeyboardFocusWithin) return;
            Bump(e.Delta > 0 ? 1 : -1);
            e.Handled = true;
        };
        Child = K.H(K.IconBtn("minus", () => Bump(-1), null, 28, "Muted"), box, K.IconBtn("plus", () => Bump(1), null, 28, "Muted"));
        Value = value;
        if (changed != null) Changed += changed;
    }

    string Fmt(double v) => Integer ? ((long)Math.Round(v)).ToString(CultureInfo.InvariantCulture)
                                    : v.ToString("0.###", CultureInfo.InvariantCulture);

    public double? Parsed
    {
        get
        {
            var s = box.Text.Trim().Replace(',', '.');
            if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) || double.IsNaN(v)) return null;
            v = Math.Clamp(v, Min, Max);
            return Integer ? Math.Round(v) : v;
        }
    }

    public double Value
    {
        get => Parsed ?? Min;
        set => box.Text = Fmt(Math.Clamp(value, Min, Max));
    }

    void Clean()
    {
        Value = Parsed ?? Min;
        Changed?.Invoke(Value);
    }

    void Bump(int d)
    {
        Value = Math.Round((Parsed ?? Min) + d * Step, 4);
        Changed?.Invoke(Value);
    }
}
