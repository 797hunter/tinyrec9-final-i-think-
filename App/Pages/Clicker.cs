// Clicker.cs - the auto clicker, including "Record spots": click the real screen, then stop when you choose.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using TinyRec.Core;
using TinyRec.Overlays;
using TinyRec.Ui;

namespace TinyRec;

public partial class MainWindow
{
    static readonly Dictionary<string, string> AcButton = new() { ["Left"] = "left", ["Right"] = "right", ["Middle"] = "middle" };
    static readonly Dictionary<string, string> AcMode = new() { ["Follow cursor"] = "cursor", ["Fixed spot"] = "fixed", ["Several spots"] = "points" };

    Segmented acButtonSeg, acTypeSeg, acStopSeg, acModeSeg, acOrderSeg;
    Stepper acHold, acInterval, acJitter, acCount, acDelay, acSecs;
    FrameworkElement acSecsRow;
    Action<ClickMore> acMoreApply;
    FrameworkElement acHoldRow, acCountRow;
    TextBlock acRate;
    readonly StackPanel acPointsList = new();
    readonly Dictionary<string, TextBlock> acTiles = new();
    Button acStart;
    CheckBox acAutostart, acShowSpots;

    FrameworkElement PageClicker()
    {
        var a = cfg.Autoclick;
        acPoints = a.Points.Select(p => (p[0], p[1])).ToList();
        var page = K.Grid("*,*", "auto,auto,auto");
        FrameworkElement L(string t) => K.T(t, 13, "Muted").M(20, 6, 14, 6);

        // ---- click
        var c1 = K.Grid("auto,*", "auto,auto,auto,auto");
        c1.Add(K.CardTitle("Click", "cursor").At(0, 0, 1, 2));
        acButtonSeg = new Segmented(AcButton.Keys, AcButton.FirstOrDefault(kv => kv.Value == a.Button).Key ?? "Left", null, 64);
        acTypeSeg = new Segmented(new[] { "Single", "Double", "Hold" }, a.HoldMs > 0 ? "Hold" : a.Double ? "Double" : "Single", _ => ClickerUi(), 64);
        acHold = new Stepper(a.HoldMs > 0 ? a.HoldMs : 50, 10, 1, 60000, true, 64);
        acHoldRow = K.H(acHold, K.T("  ms each click", 13, "Muted"));
        c1.Add(L("Button").At(1, 0), acButtonSeg.M(0, 6, 16, 6).At(1, 1),
               L("Type").At(2, 0), acTypeSeg.M(0, 6, 16, 6).At(2, 1),
               L("Hold for").At(3, 0), acHoldRow.M(0, 6, 16, 16).At(3, 1));
        page.Add(K.Card(c1).M(0, 0, 7, 12).At(0, 0));

        // ---- speed
        var c2 = K.Grid("auto,*", "auto,auto,auto,auto");
        c2.Add(K.CardTitle("Speed", "speed").At(0, 0, 1, 2));
        acInterval = new Stepper(a.IntervalMs, 10, 1, 3600000, true, 72, _ => ClickerUi());
        acRate = K.T("", 13, "Muted");
        c2.Add(L("Every").At(1, 0), K.H(acInterval, acRate.M(10, 0, 0, 0)).M(0, 6, 16, 6).At(1, 1));
        var presets = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (lab, ms) in new[] { ("1/s", 1000), ("5/s", 200), ("10/s", 100), ("20/s", 50), ("50/s", 20) })
        {
            var b = K.Btn(lab, () => { acInterval.Value = ms; ClickerUi(); }, "secondary", null, 28);
            b.Padding = new Thickness(9, 0, 9, 0);
            b.FontWeight = FontWeights.SemiBold;
            presets.Children.Add(b.M(0, 0, 4, 0));
        }
        c2.Add(L("Presets").At(2, 0), presets.M(0, 6, 16, 6).At(2, 1));
        acJitter = new Stepper(a.JitterMs, 5, 0, 60000, true, 64);
        c2.Add(L("Randomise ±").At(3, 0), K.H(acJitter, K.T("  ms", 13, "Muted")).Tip("Adds a random amount to each gap so the rhythm isn't robotic").M(0, 6, 16, 16).At(3, 1));
        page.Add(K.Card(c2).M(7, 0, 0, 12).At(0, 1));

        // ---- where
        var c3 = K.Grid("auto,*", "auto,auto,auto,auto,auto,auto");
        c3.Add(K.CardTitle("Where", "target").At(0, 0, 1, 2));
        acModeSeg = new Segmented(AcMode.Keys, AcMode.FirstOrDefault(kv => kv.Value == a.Mode).Key ?? "Follow cursor", _ => ClickerUi());
        acOrderSeg = new Segmented(new[] { "In order", "Random", "There and back" },
                                   a.Order == "random" ? "Random" : a.Order == "pingpong" ? "There and back" : "In order", null, 70);
        c3.Add(L("Click at").At(1, 0), acModeSeg.M(0, 6, 16, 6).At(1, 1),
               L("Order").At(2, 0), acOrderSeg.M(0, 6, 16, 6).At(2, 1));
        var pts = new ScrollViewer { Content = acPointsList, Height = 96, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false };
        c3.Add(K.Box(pts, "Card2", 10, 8, 6).M(18, 8, 18, 8).At(3, 0, 1, 2));
        var rec_ = K.Btn("Record spots", RecordSpots, "primary", "record")
                    .Tip("Click the real screen to add spots - your clicks still work normally. Press Done (or F6) when you're finished.");
        var pick = K.Btn("Pick on screen", PickSpots, "secondary", "target")
                    .Tip("Screen is dimmed and your clicks only mark spots - nothing underneath gets clicked");
        c3.Add(K.H(rec_, pick.M(6, 0, 6, 0), K.IconBtn("trash", () => { acPoints.Clear(); RefreshPoints(); }, "Clear all spots", 36, "Muted", "Card2"))
                 .M(18, 0, 18, 6).At(4, 0, 1, 2));
        acAutostart = K.Switch("Start clicking when I finish recording spots", cfg.AcAutostart, v => cfg.AcAutostart = v);
        acShowSpots = K.Switch("Show the spots on screen while clicking", cfg.AcShowSpots, v => cfg.AcShowSpots = v);
        c3.Add(K.V(acAutostart.M(0, 4, 0, 4), acShowSpots.M(0, 4, 0, 4)).M(18, 4, 18, 16).At(5, 0, 1, 2));
        page.Add(K.Card(c3).M(0, 0, 7, 0).At(1, 0));

        // ---- run
        var c4 = K.Grid("auto,*", "auto,auto,auto,auto,auto,auto,auto");
        c4.Add(K.CardTitle("Run", "play").At(0, 0, 1, 2));
        acStopSeg = new Segmented(new[] { "Until I stop it", "Number of clicks", "Seconds" },
                                  a.Count > 0 ? "Number of clicks" : a.Seconds > 0 ? "Seconds" : "Until I stop it", _ => ClickerUi());
        acCount = new Stepper(a.Count > 0 ? a.Count : 100, 10, 1, 100000000, true, 90);
        acCountRow = acCount;
        acSecs = new Stepper(a.Seconds > 0 ? a.Seconds : 30, 5, 0.1, 86400 * 7, false, 72);
        acSecsRow = K.H(acSecs, K.T("  seconds", 13, "Muted"));
        acDelay = new Stepper(a.StartDelay, 0.5, 0, 600, false, 56);
        c4.Add(L("Stop").At(1, 0), acStopSeg.M(0, 6, 16, 6).At(1, 1),
               L("Clicks").At(2, 0), acCount.M(0, 6, 16, 6).At(2, 1),
               L("For").At(3, 0), acSecsRow.M(0, 6, 16, 6).At(3, 1),
               L("Start after").At(4, 0), K.H(acDelay, K.T("  seconds", 13, "Muted")).M(0, 6, 16, 6).At(4, 1));
        var tiles = K.Grid("*,*,*");
        int col = 0;
        foreach (var (key, cap) in new[] { ("clicks", "Clicks"), ("time", "Running for"), ("rate", "Actual rate") })
        {
            var (t, v) = K.Tile(cap);
            acTiles[key] = v;
            tiles.Add(t.M(col == 0 ? 0 : 8, 0, 0, 0).At(0, col++));
        }
        c4.Add(tiles.M(18, 14, 18, 10).At(5, 0, 1, 2));
        acStart = K.Btn("", ToggleAuto, "primary", "play", 48);
        acStart.FontSize = 15;
        c4.Add(acStart.M(18, 4, 18, 18).At(6, 0, 1, 2));
        page.Add(K.Card(c4).M(7, 0, 0, 0).At(1, 1));

        // ---- more: the finer settings (shared with a sequence's Click step)
        var c5 = K.Grid("*", "auto,auto");
        c5.Add(K.CardTitle("More", "wand", "Bursts, spread, a key to press, click wherever a picture is, only click while a picture is on screen …").At(0, 0));
        var (moreView, moreApply) = ClickMoreEditor(a.More, false, true);
        acMoreApply = moreApply;
        c5.Add(moreView.M(20, 0, 18, 16).At(1, 0));
        page.Add(K.Card(c5).M(0, 12, 0, 0).At(2, 0, 1, 2));

        ClickerUi();
        return Scroll(page);
    }

    void ClickerUi()
    {
        if (acTypeSeg == null || acStopSeg == null || acModeSeg == null) return;
        var holdVis = acTypeSeg.Value == "Hold" ? Visibility.Visible : Visibility.Collapsed;
        acHoldRow.Visibility = holdVis;
        foreach (var el in ((Grid)acHoldRow.Parent).Children.OfType<FrameworkElement>().Where(e => Grid.GetRow(e) == 3)) el.Visibility = holdVis;
        var countVis = acStopSeg.Value == "Number of clicks" ? Visibility.Visible : Visibility.Collapsed;
        var secsVis = acStopSeg.Value == "Seconds" ? Visibility.Visible : Visibility.Collapsed;
        foreach (var el in ((Grid)acCountRow.Parent).Children.OfType<FrameworkElement>())
        {
            if (Grid.GetRow(el) == 2) el.Visibility = countVis;
            if (Grid.GetRow(el) == 3) el.Visibility = secsVis;
        }
        var ms = acInterval.Value;
        acRate.Text = ms > 0 ? $"ms  ≈  {1000 / ms:0.0} clicks/sec" : "ms";
        acOrderSeg.Enable(acModeSeg.Value == "Several spots");
        RefreshPoints();
    }

    void RefreshPoints()
    {
        acPointsList.Children.Clear();
        var mode = AcMode[acModeSeg.Value ?? "Follow cursor"];
        if (mode == "cursor" && !spotRec)
        {
            acPointsList.Children.Add(K.T("Clicks wherever your mouse cursor is.", 12, "Muted").M(4));
            return;
        }
        if (acPoints.Count == 0)
        {
            acPointsList.Children.Add(K.T($"No spots yet - press “Record spots”, click where you want, then Done (or {Hint("autoclick_toggle")}).", 12, "Muted", null, true).M(4));
            return;
        }
        var show = mode == "fixed" && !spotRec ? acPoints.Take(1).ToList() : acPoints;
        for (int i = 0; i < show.Count; i++)
        {
            int idx = i;
            var r = K.Grid("auto,*,auto");
            r.Add(K.Chip((i + 1).ToString(), "Accent", "AccentSoft").M(0, 0, 10, 0).At(0, 0), K.T($"x {show[i].x},  y {show[i].y}", 13).At(0, 1),
                  K.IconBtn("close", () => { if (idx < acPoints.Count) acPoints.RemoveAt(idx); RefreshPoints(); }, "Remove", 26).At(0, 2));
            acPointsList.Children.Add(r.M(0, 1, 0, 1));
        }
    }

    void ClickerButtons()
    {
        if (acStart == null) return;
        bool running = auto.Running;
        K.SetBtnContent(acStart, running ? $"Stop clicking   ({Hint("autoclick_toggle")})" : $"Start clicking   ({Hint("autoclick_toggle")})", running ? "stop" : "play");
        acStart.SetResourceReference(StyleProperty, running ? "BtnDanger" : "BtnPrimary");
    }

    void ClickerTick()
    {
        double el = Math.Max(0, Core.Timing.Now - autoT0);
        acTiles["clicks"].Text = auto.Clicks.ToString("N0");
        acTiles["time"].Text = Fmt.Dur(el);
        acTiles["rate"].Text = el > 0.3 ? $"{auto.Clicks / el:0.0}/s" : "—";
        if (state == "idle" && !spotRec) SetStatus($"{auto.Clicks:N0} clicks so far", "auto");
    }

    AutoClickConfig ClickerConfig()
    {
        var type = acTypeSeg.Value;
        var more = new ClickMore();
        acMoreApply?.Invoke(more);
        return new AutoClickConfig
        {
            More = more,
            Seconds = acStopSeg.Value == "Seconds" ? Math.Max(0.1, acSecs.Value) : 0,
            Button = AcButton[acButtonSeg.Value ?? "Left"],
            Double = type == "Double",
            IntervalMs = Math.Max(1, acInterval.Value),
            JitterMs = acJitter.Value,
            HoldMs = type == "Hold" ? Math.Max(1, acHold.Value) : 0,
            Count = acStopSeg.Value == "Number of clicks" ? Math.Max(1, (int)acCount.Value) : 0,
            Mode = AcMode[acModeSeg.Value ?? "Follow cursor"],
            Points = acPoints.Select(p => new[] { p.x, p.y }).ToList(),
            Order = acOrderSeg.Value == "Random" ? "random" : acOrderSeg.Value == "There and back" ? "pingpong" : "sequence",
            StartDelay = acDelay.Value,
        };
    }

    public void ToggleAuto()
    {
        sideClicker = false;                 // from here on the clicker is yours: a run ending won't stop it
        auto.IsOwn = null;
        auto.Hold = false;
        if (auto.Running)
        {
            auto.Halt();
            return;
        }
        AutoClickConfig c;
        try
        {
            c = ClickerConfig();
            auto.Start(c, () => Dispatcher.BeginInvoke(new Action(AutoDone)));
        }
        catch (ArgumentException e)
        {
            Toast("Can't start: " + e.Message, "error");
            return;
        }
        autoT0 = Core.Timing.Now + c.StartDelay;
        if (c.Mode != "cursor" && cfg.AcShowSpots)
            markers.ShowPoints(c.Mode == "fixed" ? acPoints.Take(1).ToList() : acPoints);
        RefreshButtons();
        Toast($"Auto clicker on  ·  {Hint("autoclick_toggle")} or {Hint("panic")} to stop");
    }

    void AutoDone()
    {
        acTiles["clicks"].Text = auto.Clicks.ToString("N0");
        if (!spotRec) markers.HideAll();
        if (auto.Error.Length > 0) Toast($"The auto clicker stopped: {auto.Error}", "error", 7000);
        else Toast($"Auto clicker stopped after {auto.Clicks:N0} clicks");
        RefreshButtons();
    }

    // ---------------------------------------------------------------- Record spots
    void RecordSpots()
    {
        if (spotRec || state != "idle") return;
        if (auto.Running) auto.Halt();
        spotBackup = acPoints.ToList();
        acPoints = new List<(int x, int y)>();
        spotRec = true;
        EnterRunMode();
        spotPanel.Setup("Recording spots  ·  click anywhere", new List<(string, Action, string)>
        {
            ("Undo", SpotUndo, "normal"), ("Cancel", () => SpotFinish(false), "normal"),
            ($"Done  ({Hint("autoclick_toggle")})", () => SpotFinish(true), "primary"),
        });
        markers.ShowPoints(acPoints);
        hooks.SetMouse(true);
        SetStatus("Click where you want the clicker to click", "spots", "Click your spots");
        RefreshPoints();
    }

    void SpotAdd(int x, int y)
    {
        if (!spotRec) return;
        foreach (var (l, t, r, b) in OwnRects())
            if (x >= l && x < r && y >= t && y < b) return;              // clicks on TinyRec itself (Done, Undo...) aren't spots
        acPoints.Add((x, y));
        SpotChanged();
    }

    void SpotUndo()
    {
        if (acPoints.Count == 0) return;
        acPoints.RemoveAt(acPoints.Count - 1);
        SpotChanged();
    }

    void SpotChanged()
    {
        int n = acPoints.Count;
        spotPanel.SetText($"Recording spots  ·  {n} spot{(n == 1 ? "" : "s")}");
        markers.ShowPoints(acPoints);
        SetStatus($"{n} spot{(n == 1 ? "" : "s")} so far - press {Hint("autoclick_toggle")} when done", "spots", $"{n} spots");
        RefreshPoints();
    }

    void SpotFinish(bool keep)
    {
        if (!spotRec) return;
        spotRec = false;
        if (state != "recording") hooks.SetMouse(false);
        spotPanel.Hide();
        markers.HideAll();
        int got = acPoints.Count;
        if (!keep || got == 0) acPoints = spotBackup;
        else acModeSeg.Set(got == 1 ? "Fixed spot" : "Several spots", false);
        LeaveRunMode();
        ClickerUi();
        RefreshButtons();
        if (keep && got > 0)
        {
            if (cfg.AcAutostart)
            {
                Toast($"{got} spot{(got == 1 ? "" : "s")} recorded - clicking starts now", "ok");
                var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
                t.Tick += (_, _) => { t.Stop(); ToggleAuto(); };
                t.Start();
            }
            else Toast($"{got} spot{(got == 1 ? "" : "s")} recorded. Press Start clicking or {Hint("autoclick_toggle")}.", "ok", 4500);
        }
        else Toast(keep ? "No spots recorded - kept your previous spots." : "Cancelled - kept your previous spots.");
    }

    void PickSpots()
    {
        bool fixedSpot = acModeSeg.Value == "Fixed spot";
        var pts = Hidden(() => fixedSpot ? Picker.Pick("Click the spot to click on", false) : Picker.Pick("Click each spot", true));
        if (pts.Count == 0) return;
        if (fixedSpot && acPoints.Count > 0) acPoints[0] = pts[0];   // fixed mode uses the first spot: replace it
        else acPoints.AddRange(pts);
        if (acModeSeg.Value == "Follow cursor") acModeSeg.Set(acPoints.Count > 1 ? "Several spots" : "Fixed spot", false);
        ClickerUi();
    }
}
