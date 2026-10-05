// MiniBar.cs - the small floating bar: record / play / stop, status, speed, loop, pin, back to the full window.
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using TinyRec.Ui;

namespace TinyRec;

public sealed class MiniBar : Window
{
    readonly MainWindow app;
    readonly Button rec, play, pause, pin;
    readonly TextBlock dot, status, speedText, loopText;
    public bool Quitting;

    public MiniBar(MainWindow app)
    {
        this.app = app;
        app.IgnoreKeysWhilePlaying(this);
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        ShowInTaskbar = true;
        Title = "TinyRec 9";
        Topmost = app.MiniTopmost;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        FontSize = 13;
        try { Icon = app.Icon; } catch { }

        var grip = new Border { Width = 22, Height = 34, Background = Brushes.Transparent, Cursor = Cursors.SizeAll, Child = K.Icon("grip", 16, "Faint") };
        rec = K.Round("record", () => app.ToggleRecord(), "Red", 34, "Record");
        play = K.Round("play", () => app.PlayTarget(), "Accent", 34, "Play");
        pause = K.Round("pause", app.TogglePause, null, 34, "Pause / continue");
        pause.IsEnabled = false;
        var stop = K.Round("stop", app.Panic, null, 34, "Stop everything");
        dot = K.T("●", 13, "Green");
        status = K.T("Ready", 12, "Text", FontWeights.SemiBold);
        status.TextTrimming = TextTrimming.CharacterEllipsis;
        var statusBox = new Border { Width = 215, Background = Brushes.Transparent, Child = K.H(dot.M(0, 0, 6, 0), status) };
        loopText = K.T("", 12, "Muted", FontWeights.SemiBold);
        speedText = K.T("1.00×", 12, "Text", FontWeights.SemiBold);
        speedText.Width = 46;
        speedText.TextAlignment = TextAlignment.Center;
        var spd = K.Box(K.H(K.IconBtn("minus", () => app.StepSpeed(-1), "Slower", 26), speedText, K.IconBtn("plus", () => app.StepSpeed(1), "Faster", 26)), "Card2", 9, 1, 1);
        pin = K.IconBtn("pin", () => app.SetMiniTopmost(!app.MiniTopmost), "Keep on top", 30);
        var expand = K.IconBtn("expand", () => app.SetMini(false), "Open the full window", 30);
        var row = K.H(grip.M(4, 0, 2, 0), rec.M(3, 0, 3, 0), play.M(3, 0, 3, 0), pause.M(3, 0, 3, 0), stop.M(3, 0, 3, 0), statusBox.M(10, 0, 6, 0),
                      K.Icon("loop", 13, "Muted").M(0, 0, 4, 0), loopText.M(0, 0, 8, 0), spd, pin.M(6, 0, 0, 0), expand.M(0, 0, 4, 0));
        var card = new Border
        {
            CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1), Padding = new Thickness(4, 6, 4, 6), Child = row, Margin = new Thickness(12),
            Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 3, Opacity = 0.35, Color = Colors.Black },
        };
        card.SetResourceReference(Border.BackgroundProperty, "Card");
        card.SetResourceReference(Border.BorderBrushProperty, "Line");
        Content = card;

        void Drag(object s, MouseButtonEventArgs e)
        {
            if (e.ButtonState != MouseButtonState.Pressed) return;
            try { DragMove(); } catch { }
            app.MiniMoved((int)Left, (int)Top);
        }
        grip.MouseLeftButtonDown += Drag;
        statusBox.MouseLeftButtonDown += Drag;
        var menu = K.Menu(("Open full window", () => app.SetMini(false)), ("Keep on top", () => app.SetMiniTopmost(!app.MiniTopmost)),
                          ("-", null), ("Quit TinyRec", app.Quit));
        grip.ContextMenu = menu;
        statusBox.ContextMenu = menu;
        Closing += (_, e) =>
        {
            if (Quitting) return;
            e.Cancel = true;                  // closing the bar = back to the full window
            app.SetMini(false);
        };
        RefreshPin();
    }

    public void ShowBar(int[] pos)
    {
        if (pos is { Length: 2 })
        {
            Left = pos[0];
            Top = pos[1];
        }
        else
        {
            Show();
            UpdateLayout();
            Left = (SystemParameters.PrimaryScreenWidth - ActualWidth) / 2;
            Top = 90;                          // below the REC badge
        }
        Topmost = app.MiniTopmost;
        Show();
        RefreshPin();
    }

    public void SetStatus(string color, string text)
    {
        dot.SetResourceReference(TextBlock.ForegroundProperty, color);
        status.Text = text;
    }

    public void SetRunning(bool recOn, bool playOn, bool canPause, bool paused)
    {
        K.SetRoundIcon(rec, recOn ? "stop" : "record");
        K.SetRoundIcon(play, playOn ? "stop" : "play");
        K.SetRoundIcon(pause, paused ? "play" : "pause");
        pause.IsEnabled = canPause;
    }

    public void SetSpeed(string t) => speedText.Text = t;
    public void SetLoop(string t) => loopText.Text = t;

    public void RefreshPin()
    {
        bool on = app.MiniTopmost;
        Topmost = on;
        ((Ico)pin.Content).SetResourceReference(Ico.ForegroundProperty, on ? "Accent" : "Faint");
        pin.SetResourceReference(BackgroundProperty, on ? "AccentSoft" : "Card");
    }

    public void ThemeChanged() => RefreshPin();
}
