// Dialogs.cs - toasts (little notifications) and themed pop-up dialogs.
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace TinyRec.Ui;

public sealed class Toasts
{
    readonly StackPanel host;

    public Toasts(StackPanel host) => this.host = host;

    public void Show(string text, string kind = "info", int ms = 3200)
    {
        var (icon, color) = kind switch
        {
            "ok" => ("check", "Green"),
            "warn" => ("warn", "Amber"),
            "error" => ("warn", "Red"),
            _ => ("info", "Accent"),
        };
        var close = K.IconBtn("close", null, null, 22, "Muted");
        var row = K.Grid("auto,*,auto");
        row.Add(K.Icon(icon, 16, color).M(0, 0, 10, 0).At(0, 0),
                K.T(text, 13, "Text", null, true).At(0, 1),
                close.M(8, 0, 0, 0).At(0, 2));
        var card = new Border
        {
            CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Padding = new Thickness(14, 10, 8, 10),
            Margin = new Thickness(0, 8, 0, 0), MaxWidth = 420, Child = row, Opacity = 0,
            Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 3, Opacity = 0.25, Color = Colors.Black },
        };
        card.SetResourceReference(Border.BackgroundProperty, "Card");
        card.SetResourceReference(Border.BorderBrushProperty, color);
        host.Children.Add(card);
        while (host.Children.Count > 4) host.Children.RemoveAt(0);           // never let them pile up
        card.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(160)));
        void Remove()
        {
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(220));
            fade.Completed += (_, _) => host.Children.Remove(card);
            card.BeginAnimation(UIElement.OpacityProperty, fade);
        }
        close.Click += (_, _) => Remove();
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
        t.Tick += (_, _) => { t.Stop(); if (host.Children.Contains(card)) Remove(); };
        t.Start();
    }
}

public sealed class Dialog : Window
{
    public readonly StackPanel Body = new();
    public readonly TextBlock Error;
    readonly StackPanel buttons = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
    object result;
    object defaultValue;
    bool hasDefault;
    Action defaultAction;

    public Dialog(Window owner, string title, string message = "", string icon = "info", string iconColor = "Accent")
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        ShowInTaskbar = false;
        Topmost = true;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        Title = title;
        if (owner != null && owner.IsVisible)
        {
            Owner = owner;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var head = K.H(K.Icon(icon, 22, iconColor).M(0, 0, 12, 0), K.T(title, 17, "Text", FontWeights.SemiBold));
        var stack = new StackPanel { MinWidth = 380, MaxWidth = 560 };
        stack.Children.Add(head);
        if (!string.IsNullOrEmpty(message)) stack.Children.Add(K.T(message, 13, "Muted", null, true).M(0, 10, 0, 0));
        // a tall dialog (folds opened) scrolls inside itself instead of running off the bottom of the screen
        var scroll = new ScrollViewer
        {
            Content = Body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            MaxHeight = Math.Max(240, SystemParameters.WorkArea.Height - 250), Focusable = false,
        };
        stack.Children.Add(scroll.M(0, 8, 0, 0));
        SizeChanged += (_, _) =>
        {
            if (!IsLoaded) return;
            var wa = SystemParameters.WorkArea;
            if (Top + ActualHeight > wa.Bottom) Top = Math.Max(wa.Top, wa.Bottom - ActualHeight);
        };
        Error = K.T("", 12, "Red", null, true);
        stack.Children.Add(Error.M(0, 4, 0, 0));
        stack.Children.Add(buttons.M(0, 16, 0, 0));
        var card = new Border
        {
            CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1), Padding = new Thickness(24, 22, 24, 20),
            Margin = new Thickness(18), Child = stack,
            Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 4, Opacity = 0.35, Color = Colors.Black },
        };
        card.SetResourceReference(Border.BackgroundProperty, "Card");
        card.SetResourceReference(Border.BorderBrushProperty, "Line");
        card.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) try { DragMove(); } catch { } };
        Content = card;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Finish(null); e.Handled = true; }
            else if (e.Key == Key.Enter && Keyboard.FocusedElement is not Button)
            {
                if (defaultAction != null) { defaultAction(); e.Handled = true; }
                else if (hasDefault) { Finish(defaultValue); e.Handled = true; }
            }
        };
    }

    public Button AddButton(string text, object value, string kind = "secondary", bool isDefault = false)
    {
        var b = K.Btn(text, () => Finish(value), kind).M(8, 0, 0, 0);
        b.MinWidth = 84;
        buttons.Children.Add(b);
        if (isDefault) { defaultValue = value; hasDefault = true; }
        return b;
    }

    public Button AddAction(string text, Action act, string kind = "primary", bool isDefault = true)
    {
        var b = K.Btn(text, act, kind).M(8, 0, 0, 0);
        b.MinWidth = 84;
        buttons.Children.Add(b);
        if (isDefault) defaultAction = act;
        return b;
    }

    public void Finish(object value)
    {
        result = value;
        Close();
    }

    public object Run(IInputElement focus = null)
    {
        Loaded += (_, _) =>
        {
            Activate();
            (focus ?? buttons.Children.OfType<Button>().LastOrDefault())?.Focus();
            if (focus is TextBox tb) tb.SelectAll();
        };
        ShowDialog();
        return result;
    }

    // ------------------------------------------------------------ helpers
    public static object Ask(Window owner, string title, string message, (string text, object value, string kind)[] buttons,
                             string icon = "info", string iconColor = "Accent")
    {
        var d = new Dialog(owner, title, message, icon, iconColor);
        for (int i = 0; i < buttons.Length; i++)
            d.AddButton(buttons[i].text, buttons[i].value, buttons[i].kind, i == buttons.Length - 1);
        return d.Run();
    }

    public static bool Confirm(Window owner, string title, string message, string ok = "OK", bool danger = false) =>
        Ask(owner, title, message, new[] { ("Cancel", (object)null, "secondary"), (ok, (object)true, danger ? "danger" : "primary") },
            danger ? "warn" : "info", danger ? "Red" : "Accent") is true;

    public static void Info(Window owner, string title, string message, string kind = "info")
    {
        var (ic, col) = kind switch { "error" => ("warn", "Red"), "warn" => ("warn", "Amber"), "ok" => ("check", "Green"), _ => ("info", "Accent") };
        Ask(owner, title, message, new[] { ("OK", (object)true, "primary") }, ic, col);
    }

    /// validate(text) -> error message, or null if fine.
    public static string AskString(Window owner, string title, string message, string initial = "",
                                   Func<string, string> validate = null, string okText = "OK")
    {
        var d = new Dialog(owner, title, message, "rename");
        var box = K.Field(initial, 440);
        d.Body.Children.Add(box);
        d.AddButton("Cancel", null);
        d.AddAction(okText, () =>
        {
            var err = validate?.Invoke(box.Text);
            if (err != null) d.Error.Text = err;
            else d.Finish(box.Text);
        });
        return d.Run(box) as string;
    }

    public static double? AskNumber(Window owner, string title, string message, string initial, double? lo = null, double? hi = null)
    {
        var r = AskString(owner, title, message, initial, t =>
        {
            if (!double.TryParse(t.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v))
                return "Enter a number.";
            if ((lo != null && v < lo) || (hi != null && v > hi)) return $"Must be between {lo} and {hi}.";
            return null;
        });
        return r == null ? null : double.Parse(r.Replace(',', '.'), System.Globalization.CultureInfo.InvariantCulture);
    }
}
