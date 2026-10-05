// Home.cs - the Record & Play page: big transport buttons, current recording, playback options, last run.
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using TinyRec.Core;
using TinyRec.Ui;

namespace TinyRec;

public partial class MainWindow
{
    Button heroRec, heroPlay, heroPause;
    System.Windows.Shapes.Ellipse heroRecRing;
    TextBlock heroRecLab, heroPlayLab, heroPauseLab;
    Border heroRecChip, heroPlayChip, heroStopChip, heroPauseChip;
    TextBox nameBox, windowBox;
    TextBlock homeFile, loopHint, homeSpeed, runMsg;
    readonly Dictionary<string, TextBlock> homeTiles = new(), runTiles = new();
    Segmented loopSeg;
    CheckBox playClickerSwitch;
    Stepper loopCountStep, loopDelayStep, countdownStep, loopRandomStep, maxMinStep, recMaxStep;
    StackPanel loopCountRow;
    Slider speedSlider;
    bool speedFromCode;

    FrameworkElement PageHome()
    {
        var page = K.Grid("3*,2*", "auto,auto");

        // ---- hero: transport + current recording
        var hero = K.Grid("auto,auto,*");
        var tr = K.Grid("auto,auto,auto,auto", "auto,auto,auto");
        heroRec = K.Round("record", () => ToggleRecord(), "Red", 62);
        heroPlay = K.Round("play", () => TogglePlay(), "Accent", 62);
        var heroStop = K.Round("stop", Panic, null, 62);
        heroPause = K.Round("pause", TogglePause, null, 62);
        heroPauseLab = K.T("Pause", 13, "Text", FontWeights.SemiBold).Align(HorizontalAlignment.Center);
        heroPauseChip = K.Chip("");
        heroRecLab = K.T("Record", 13, "Text", FontWeights.SemiBold).Align(HorizontalAlignment.Center);
        heroPlayLab = K.T("Play", 13, "Text", FontWeights.SemiBold).Align(HorizontalAlignment.Center);
        heroRecChip = K.Chip("");
        heroPlayChip = K.Chip("");
        heroStopChip = K.Chip("");
        tr.Add(Fx.WithRing(heroRec, "Red", out heroRecRing).M(8, 0, 8, 0).At(0, 0), heroPlay.M(8, 0, 8, 0).At(0, 1), heroPause.M(8, 0, 8, 0).At(0, 2), heroStop.M(8, 0, 8, 0).At(0, 3),
               heroRecLab.M(0, 8, 0, 3).At(1, 0), heroPlayLab.M(0, 8, 0, 3).At(1, 1), heroPauseLab.M(0, 8, 0, 3).At(1, 2),
               K.T("Stop all", 13, "Text", FontWeights.SemiBold).Align(HorizontalAlignment.Center).M(0, 8, 0, 3).At(1, 3),
               heroRecChip.Align(HorizontalAlignment.Center).At(2, 0), heroPlayChip.Align(HorizontalAlignment.Center).At(2, 1),
               heroPauseChip.Align(HorizontalAlignment.Center).At(2, 2), heroStopChip.Align(HorizontalAlignment.Center).At(2, 3));
        hero.Add(tr.M(16, 20, 8, 20).Align(HorizontalAlignment.Left, VerticalAlignment.Top).At(0, 0));
        hero.Add(K.Divider(true).M(14, 18, 14, 18).At(0, 1));

        var cur = K.Grid("*", "auto,auto,auto,auto");
        var top = K.Grid("*,auto");
        homeFile = K.T("", 12, "Muted");
        top.Add(K.T("CURRENT RECORDING", 11, "Faint", FontWeights.SemiBold).At(0, 0), homeFile.At(0, 1));
        cur.Add(top.At(0, 0));
        nameBox = new TextBox { FontSize = 20, FontWeight = FontWeights.SemiBold, BorderThickness = new Thickness(0), Background = System.Windows.Media.Brushes.Transparent, Padding = new Thickness(0) };
        nameBox.SetResourceReference(ForegroundProperty, "Text");
        nameBox.SetResourceReference(TextBox.CaretBrushProperty, "Text");
        nameBox.SetResourceReference(TextBox.SelectionBrushProperty, "Accent");
        nameBox.TextChanged += (_, _) => SetMeta("name", nameBox.Text.Trim());
        nameBox.Tip("Click to rename");
        cur.Add(nameBox.M(0, 4, 0, 0).At(1, 0));
        var tiles = K.Grid("*,*,*,*,*");
        int col = 0;
        foreach (var (key, cap) in new[] { ("actions", "Actions"), ("length", "Length"), ("clicks", "Clicks"), ("keys", "Key presses"), ("images", "Image steps") })
        {
            var (t, v) = K.Tile(cap);
            homeTiles[key] = v;
            tiles.Add(t.M(col == 0 ? 0 : 8, 0, 0, 0).At(0, col++));
        }
        cur.Add(tiles.M(0, 10, 0, 12).At(2, 0));
        Button exportBtn = null;
        exportBtn = K.Btn("Export", () => K.Menu(("Readable text (.txt)…", () => Export(rec, "txt")),
                                                 ("Bundle with images (.zip)…", () => Export(rec, "zip"))).Popup(exportBtn), "secondary", "export", 34);
        cur.Add(K.H(K.Btn("New", NewRecording, "secondary", "plus", 34).M(0, 0, 6, 0), K.Btn("Open", OpenDialog, "secondary", "folder", 34).M(0, 0, 6, 0),
                    K.Btn("Save", () => Save(), "primary", "save", 34).M(0, 0, 6, 0), K.Btn("Save as…", () => SaveAs(), "secondary", null, 34).M(0, 0, 6, 0),
                    exportBtn).At(3, 0));
        hero.Add(cur.M(8, 18, 22, 18).At(0, 2));
        page.Add(K.Card(hero).M(0, 0, 0, 12).At(0, 0, 1, 2));

        // ---- playback options
        var pb = K.Grid("auto,*", "auto,auto,auto,auto,auto,auto,auto,auto,auto,auto,auto,auto");
        pb.Add(K.CardTitle("Playback", "loop").At(0, 0, 1, 2));
        FrameworkElement L(string t, int r) => K.T(t, 13, "Muted").M(20, 6, 14, 6).At(r, 0);

        loopSeg = new Segmented(new[] { "Once", "Repeat", "Forever" }, cfg.Loops == 0 ? "Forever" : cfg.Loops == 1 ? "Once" : "Repeat",
                                _ => LoopModeChanged(), 70);
        loopCountStep = new Stepper(cfg.Loops > 1 ? cfg.Loops : Math.Max(2, cfg.RepeatCount), 1, 1, 1000000, true, 60, _ => LoopModeChanged());
        loopCountRow = K.H(loopCountStep.M(12, 0, 6, 0), K.T("times", 13, "Muted"));
        pb.Add(L("Repeat", 1), K.H(loopSeg, loopCountRow).M(0, 6, 16, 6).At(1, 1));
        loopHint = K.T("", 12, "Faint", null, true);
        pb.Add(loopHint.M(0, 0, 16, 4).At(2, 1));

        loopDelayStep = new Stepper(cfg.LoopDelay, 0.5, 0, 3600, false, 56, v => cfg.LoopDelay = v);
        pb.Add(L("Pause between", 3), K.H(loopDelayStep, K.T("  seconds between loops", 13, "Muted")).M(0, 6, 16, 6).At(3, 1));

        speedSlider = K.Slide(-1, 1, 0, v => { if (!speedFromCode) SetSpeed(Math.Pow(10, v), true); }, 170);
        homeSpeed = K.T("1.00×", 14, "Text", FontWeights.SemiBold);
        homeSpeed.Width = 58;
        homeSpeed.TextAlignment = TextAlignment.Center;
        var sp = K.H(speedSlider, homeSpeed.M(4, 0, 4, 0));
        foreach (var s in new[] { 0.5, 1, 2, 4 })
        {
            var b = K.Btn($"{s:0.##}×", () => SetSpeed(s), "secondary", null, 28);
            b.Padding = new Thickness(8, 0, 8, 0);
            b.FontWeight = FontWeights.SemiBold;
            sp.Children.Add(b.M(2, 0, 2, 0));
        }
        pb.Add(L("Speed", 4), sp.M(0, 6, 16, 6).At(4, 1));

        countdownStep = new Stepper(cfg.Countdown, 1, 0, 30, true, 44, v => cfg.Countdown = (int)v);
        pb.Add(L("Countdown", 5), K.H(countdownStep, K.T("  seconds (buttons only - hotkeys start instantly)", 12, "Faint")).M(0, 6, 16, 6).At(5, 1));

        windowBox = K.Field("", 170);
        windowBox.TextChanged += (_, _) => SetMeta("window", windowBox.Text.Trim());
        windowBox.Tip("Window title to follow - filled in automatically when you record");
        pb.Add(L("Window", 6), K.H(K.Switch("Follow recorded window", cfg.RelativeToWindow, v => cfg.RelativeToWindow = v), windowBox.M(12, 0, 0, 0))
                                .M(0, 6, 16, 6).At(6, 1));

        // what a recording sends when it plays, extras for long runs, the clicker that can run next to it
        var (playOnlyView, _) = OnlyEditor(cfg.PlayOnly, "Everything", true, v => cfg.PlayOnly = v);
        playOnlyView.Tip("Keyboard only leaves the recording's mouse moves and clicks out (your pointer stays free). Image steps still wait for their picture, they just don't click it. Custom lets you tick exactly what is sent. The recording keeps its length.");
        pb.Add(L("Play back", 7).Align(HorizontalAlignment.Left, VerticalAlignment.Top), playOnlyView.M(0, 6, 16, 6).At(7, 1));
        playClickerSwitch = K.Switch("Run the auto clicker while it plays", cfg.PlayClicker, v => cfg.PlayClicker = v);
        playClickerSwitch.Tip("Starts the auto clicker when playback starts and stops it when playback ends, using the Auto Clicker page's setup (its click limit and start delay are ignored). Pairs well with Keyboard only.");
        pb.Add(L("Auto clicker", 8), K.H(playClickerSwitch, K.Btn("Set up", () => ShowPage("clicker"), "ghost", null, 28).M(10, 0, 0, 0)).M(0, 6, 16, 6).At(8, 1));

        var loopRandom = loopRandomStep = new Stepper(cfg.LoopDelayRandom, 0.5, 0, 86400, false, 56, v => cfg.LoopDelayRandom = v);
        var maxMin = maxMinStep = new Stepper(cfg.PlayMaxMinutes, 5, 0, 60 * 24 * 30, false, 64, v => cfg.PlayMaxMinutes = v);
        var doneSound = K.Switch("Play a sound when playback ends", cfg.DoneSound, v => cfg.DoneSound = v);
        var lg = K.Grid("auto,*", "auto,auto,auto");
        lg.Add(K.T("Between loops", 13, "Muted").M(0, 6, 14, 6).At(0, 0),
               K.H(K.T("plus up to  ", 13, "Muted"), loopRandom, K.T("  s more, at random", 13, "Muted")).M(0, 4, 0, 4).At(0, 1),
               K.T("Time limit", 13, "Muted").M(0, 6, 14, 6).At(1, 0),
               K.H(maxMin, K.T("  minutes, then playback ends by itself   (0 = none)", 13, "Muted")).M(0, 4, 0, 4).At(1, 1),
               doneSound.M(0, 6, 0, 0).At(2, 0, 1, 2));
        pb.Add(Fold("More playback settings", lg, cfg.LoopDelayRandom > 0 || cfg.PlayMaxMinutes > 0 || cfg.DoneSound).M(14, 2, 16, 4).At(9, 0, 1, 2));

        // what a new recording captures
        var (recWhatView, _) = OnlyEditor(cfg.RecordWhat, "Mouse + keyboard", false, v => cfg.RecordWhat = v);
        recWhatView.Tip("What the next recording captures. Keyboard only ignores the mouse completely; Mouse only ignores the keys; Custom lets you tick pointer moves, clicks, scrolling and keys one by one, and name keys to ignore.");
        pb.Add(L("Record", 10).Align(HorizontalAlignment.Left, VerticalAlignment.Top), recWhatView.M(0, 6, 16, 6).At(10, 1));
        var recMax = recMaxStep = new Stepper(cfg.RecMaxSecs, 5, 0, 86400, false, 64, v => cfg.RecMaxSecs = v);
        pb.Add(L("Stop recording", 11), K.H(K.T("by itself after  ", 13, "Muted"), recMax, K.T("  seconds   (0 = when I press stop)", 13, "Muted")).M(0, 6, 16, 18).At(11, 1));
        page.Add(K.Card(pb).M(0, 0, 12, 0).Align(HorizontalAlignment.Stretch, VerticalAlignment.Stretch).At(1, 0));

        // ---- last run
        var lr = K.Grid("*", "auto,auto,auto");
        lr.Add(K.CardTitle("Last run", "clock").At(0, 0));
        var list = new StackPanel();
        int i = 0;
        foreach (var (key, cap) in new[] { ("events", "Actions played"), ("loops", "Loops completed"), ("worst", "Worst timing"),
                                           ("avg", "Average timing"), ("found", "Images found"), ("missed", "Images missed") })
        {
            if (i++ > 0) list.Children.Add(K.Divider().M(12, 0, 12, 0));
            var v = K.T("—", 13, "Text", FontWeights.SemiBold);
            runTiles[key] = v;
            var row = K.Grid("*,auto");
            row.Add(K.T(cap, 13, "Muted").At(0, 0), v.At(0, 1));
            list.Children.Add(row.M(14, 7, 14, 7));
        }
        lr.Add(K.Box(list, "Card2", 10).M(18, 0, 18, 0).At(1, 0));
        runMsg = K.T("Nothing played yet.", 12, "Muted", null, true);
        lr.Add(runMsg.M(20, 10, 20, 16).At(2, 0));
        page.Add(K.Card(lr).Align(HorizontalAlignment.Stretch, VerticalAlignment.Stretch).At(1, 1));

        LoopModeChanged();
        return Scroll(page);
    }

    int LoopsSetting() => loopSeg?.Value switch
    {
        "Forever" => 0,
        "Repeat" => Math.Max(1, (int)(loopCountStep?.Value ?? 2)),
        _ => 1,
    };

    void LoopModeChanged()
    {
        if (loopSeg == null) return;
        var m = loopSeg.Value;
        loopCountRow.Visibility = m == "Repeat" ? Visibility.Visible : Visibility.Collapsed;
        loopHint.Text = m switch
        {
            "Once" => "Plays one time, then stops.",
            "Repeat" => $"Plays {LoopsSetting()} times, then stops. Stop early with {Hint("play_toggle")} or {Hint("panic")}.",
            _ => $"Loops until you press {Hint("play_toggle")} or {Hint("panic")}.",
        };
        cfg.Loops = LoopsSetting();
        if (m == "Repeat") cfg.RepeatCount = LoopsSetting();
        RefreshLoopBadge();
    }

    void RefreshLoopBadge()
    {
        var n = LoopsSetting();
        var t = n == 0 ? "∞" : $"×{n}";
        if (ftLoop != null) ftLoop.Text = t;
        mini?.SetLoop(t);
    }

    void HomeButtons(bool recOn, bool playOn)
    {
        if (heroRec == null) return;
        K.SetRoundIcon(heroRec, recOn ? "stop" : "record");
        K.SetRoundIcon(heroPlay, playOn ? "stop" : "play");
        heroRecLab.Text = recOn ? "Stop recording" : "Record";
        heroPlayLab.Text = playOn ? "Stop" : "Play";
        K.SetChip(heroRecChip, Hint("record_toggle"));
        K.SetChip(heroPlayChip, Hint("play_toggle"));
        K.SetChip(heroStopChip, Hint("panic"));
        K.SetChip(heroPauseChip, Hint("pause_toggle"));
        K.SetRoundIcon(heroPause, paused ? "play" : "pause");
        heroPauseLab.Text = paused ? "Continue" : "Pause";
        heroPause.IsEnabled = state is "recording" or "playing";
    }

    void HomeInfo(Edits.Counts c)
    {
        if (homeFile == null) return;
        homeTiles["actions"].Text = rec.Events.Count.ToString("N0");
        homeTiles["length"].Text = Fmt.Dur(rec.Duration);
        homeTiles["clicks"].Text = c.Clicks.ToString("N0");
        homeTiles["keys"].Text = c.Keys.ToString("N0");
        homeTiles["images"].Text = c.Images.ToString("N0");
        if (recPath == null)
            homeFile.Text = rec.Events.Count == 0 ? "nothing recorded yet" : "not saved yet";
        else
            homeFile.Text = $"{System.IO.Path.GetFileName(recPath)}  ·  {(dirty ? "unsaved changes" : "saved")}";
        homeFile.SetResourceReference(TextBlock.ForegroundProperty, dirty ? "Amber" : "Muted");
    }

    void HomeMeta()
    {
        if (nameBox == null) return;
        var n = rec.GetMeta("name") ?? "";
        if (nameBox.Text != n) nameBox.Text = n;
        var w = rec.GetMeta("window") ?? "";
        if (windowBox.Text != w) windowBox.Text = w;
    }

    void HomeSpeed(string txt, double v, bool fromSlider)
    {
        if (homeSpeed == null) return;
        homeSpeed.Text = txt;
        if (!fromSlider)
        {
            speedFromCode = true;
            speedSlider.Value = Math.Log10(v);
            speedFromCode = false;
        }
    }

    void HomeLastRun(PlayStats st)
    {
        runTiles["events"].Text = st.Events.ToString("N0");
        runTiles["loops"].Text = st.Loops.ToString("N0");
        runTiles["worst"].Text = $"{st.MaxLateMs:0.0} ms";
        runTiles["avg"].Text = $"{st.MeanLateMs:0.00} ms";
        runTiles["found"].Text = st.ImageHits.ToString();
        runTiles["missed"].Text = st.ImageMisses.ToString();
        runTiles["missed"].SetResourceReference(TextBlock.ForegroundProperty, st.ImageMisses > 0 ? "Red" : "Text");
        var msg = !string.IsNullOrEmpty(st.Message) ? st.Message : st.Stopped ? "Stopped early." : "Finished.";
        runMsg.Text = $"{DateTime.Now:HH:mm:ss}  ·  {msg}";
        runMsg.SetResourceReference(TextBlock.ForegroundProperty, string.IsNullOrEmpty(st.Message) ? "Muted" : "Red");
    }
}
