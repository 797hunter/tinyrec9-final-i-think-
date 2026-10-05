// MainWindow.cs - the heart of the app: state (idle / countdown / recording / playing), record, play, panic,
// hotkeys, status, speed, files. The pages live in Pages/*.cs (same class, split into files).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TinyRec.Core;
using TinyRec.Overlays;
using TinyRec.Ui;
using TinyRec.Win;

namespace TinyRec;

public partial class MainWindow : Window
{
    public const string AppName = "TinyRec 9";
    public const string Version = "9.0";

    // ---------------------------------------------------------------- paths (data\ lives next to the .exe)
    public static readonly string AppDir = AppContext.BaseDirectory;
    public static readonly string Data = Path.Combine(AppDir, "data");
    public static readonly string RecDir = Path.Combine(Data, "recordings");
    public static readonly string ImgDir = Path.Combine(Data, "images");
    public static readonly string SeqDir = Path.Combine(Data, "sequences");
    public static readonly string LogDir = Path.Combine(Data, "logs");
    public static readonly string DelDir = Path.Combine(Data, "deleted");
    public static readonly string ConfigPath = Path.Combine(Data, "config.json");
    public static readonly string DrawingsPath = Path.Combine(Data, "drawings.json");
    public static readonly string ErrorLog = Path.Combine(Data, "error.log");

    public static readonly (string key, string title, string icon, string sub)[] Pages =
    {
        ("home", "Record & Play", "home", "Record, play back and control how it loops"),
        ("sequences", "Sequences", "loop", "Chain recordings together - each one waits for its picture before it starts"),
        ("library", "Library", "library", "Every recording you've saved"),
        ("editor", "Editor", "edit", "Change any action - timing, positions, keys, image steps"),
        ("clicker", "Auto Clicker", "cursor", "Click on a timer, at the cursor or at spots you record"),
        ("images", "Images", "image", "Pictures TinyRec can find and click on screen"),
        ("draw", "Draw", "draw", "Mark buttons and areas on your screen"),
        ("settings", "Settings", "settings", "Hotkeys, appearance and behaviour"),
    };

    // ---------------------------------------------------------------- engine
    Settings cfg;
    readonly WinInput input = new();
    ImageFinder finder;
    Player player;
    SequenceRunner runner;
    AutoClicker auto;
    HookThread hooks;
    readonly HotkeyTable table = new();
    HotkeyRouter router;
    readonly Recorder recorder = new();
    DrawLayer draw;
    FloatPanel spotPanel;
    Markers markers;
    Badge badge;
    MiniBar mini;
    Toasts toasts;

    // ---------------------------------------------------------------- state
    Recording rec = new();
    string recPath;
    bool dirty;
    readonly List<Recording> history = new(), future = new();
    string state = "idle";                 // idle | countdown | recording | playing
    double speed = 1.0;
    int cdId;
    (string title, int left, int top)? fgAtRecord;
    double tRec;
    Recording playRec;
    Sequence seqRun;                       // the sequence being run (null while a single recording plays)
    bool paused;                           // recording or playback is paused
    RunLog runLog;                         // diary of the current / most recent run
    string runLogPath, runLogKind = "";    // kind: "rec" or "seq"
    volatile bool logDirty;
    double playStart;
    int loopsTotal = 1;
    bool showPlayBadge;
    int progI, progN, progLoop;
    string runRestore;
    string runModeOverride;                // a sequence's own "what the window does while it runs" (null = the app setting)
    bool spotRec;
    List<(int x, int y)> spotBackup = new();
    List<(int x, int y)> acPoints = new();
    double autoT0;
    string pendingRebind;
    string page;
    bool loadingMeta;
    DispatcherTimer tick;
    int themeCheck;
    bool themeSeenDark;
    bool quitting;

    // ---------------------------------------------------------------- shell widgets
    readonly Grid pageHost = new();
    readonly Dictionary<string, FrameworkElement> pageViews = new();
    readonly Dictionary<string, RadioButton> nav = new();
    TextBlock hTitle, hSub, sbState, sbDetail, sbDot, ftName, ftTime, ftSpeed, ftLoop;
    Border sbKeys;
    ProgressBar ftBar;
    Button ftRec, ftPlay, ftPause;
    CheckBox topmostSwitch;
    Grid root;
    PulseFrame frame;                      // the coloured frame around the window
    System.Windows.Shapes.Ellipse ftRecRing;

    /// While something plays, keys that land on TinyRec's own windows do nothing. A recording that presses Space or Enter
    /// must not press whichever TinyRec button has the focus (it used to press Stop and end the run). Hotkeys are not
    /// affected: they come through the keyboard hook, not through the window.
    public void IgnoreKeysWhilePlaying(Window w)
    {
        w.PreviewKeyDown += (_, e) => { if (state is "playing" or "countdown") e.Handled = true; };
        w.PreviewKeyUp += (_, e) => { if (state is "playing" or "countdown") e.Handled = true; };
    }

    public MainWindow()
    {
        foreach (var d in new[] { RecDir, ImgDir, SeqDir, LogDir }) Directory.CreateDirectory(d);
        cfg = Settings.Load(ConfigPath);
        IgnoreKeysWhilePlaying(this);
        ApplyTheme();
        WinInput.Warm();
        Native.timeBeginPeriod(1);            // 1 ms timer: precise playback

        Title = AppName;
        Width = 1240;
        Height = 820;
        MinWidth = 1000;
        MinHeight = 640;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        FontSize = 13;
        SetResourceReference(BackgroundProperty, "Bg");
        try { Icon = BitmapFrame.Create(new Uri("pack://application:,,,/Assets/tinyrec.ico")); } catch { }
        ApplyFx();
        SourceInitialized += (_, _) => TitleBar();
        Theme.Changed += () =>
        {
            TitleBar();
            UpdateBorder();
        };
        IsVisibleChanged += (_, _) => UpdateBorder();          // nothing animates while the window is hidden or minimised
        StateChanged += (_, _) => UpdateBorder();

        try { finder = new ImageFinder(ImgDir) { Area = ImageSearchArea }; } catch { finder = null; }
        player = new Player(input, finder, () => Volatile.Read(ref speed));
        player.IsOwn = OnOwnWindow;
        runner = new SequenceRunner(player, name => Files.LoadText(Path.Combine(RecDir, name)), PlayOffset);
        auto = new AutoClicker(input, finder);
        runner.Notify = (msg, sound) => Dispatcher.BeginInvoke(new Action(() =>
        {
            Toast(msg, "warn", 9000);
            if (sound) Beep();
        }));
        draw = new DrawLayer(DrawingsPath) { Tool = cfg.DrawTool, InkColor = cfg.DrawColor, InkWidth = cfg.DrawWidth };
        draw.AskText = () => Dialog.AskString(TopWindow, "Add text", "Text to place on the screen:", "",
                                              t => string.IsNullOrWhiteSpace(t) ? "Type something first." : null, "Place");
        draw.Changed += () =>
        {
            if (router != null) router.GrabEscape = draw.Drawing;
            Dispatcher.BeginInvoke(new Action(OnDrawChanged));
        };
        spotPanel = new FloatPanel();
        markers = new Markers();
        badge = new Badge();

        BuildShell();
        mini = new MiniBar(this);
        Closing += (_, e) =>
        {
            if (quitting) return;
            e.Cancel = true;
            Dispatcher.BeginInvoke(new Action(Quit));          // after this close attempt has finished (can't Close() while closing)
        };
    }

    /// Called once by App after construction.
    public void Start()
    {
        BindHotkeys();
        router = new HotkeyRouter(table) { Block = cfg.BlockHotkeys, Recorder = recorder };
        router.PanicNow = PanicNow;
        router.EscapePressed = () => Dispatcher.BeginInvoke(new Action(() => draw?.Stop()));
        router.Fired = a => Dispatcher.BeginInvoke(new Action(() => OnHotkey(a)));
        router.Captured = c => Dispatcher.BeginInvoke(new Action(() => OnCaptured(c)));
        hooks = new HookThread { OnKey = router.OnKey, OnMouse = OnMouseHook };
        hooks.Start();

        if (!string.IsNullOrEmpty(cfg.LastRecording) && File.Exists(cfg.LastRecording))
        {
            try { SetRecording(Files.LoadText(cfg.LastRecording), cfg.LastRecording); } catch { }
        }
        Application.Current.MainWindow = this;
        Show();
        ShowPage("home");
        RefreshEverything();
        if (finder == null) Toast("Image recognition couldn't start (OpenCV missing).", "warn", 6000);
        if (!hooks.KeyboardActive) Toast("Hotkeys couldn't start - try running TinyRec as administrator.", "warn", 8000);
        if (cfg.StartMini) SetMini(true);
        draw.Resume();                                             // bring back the saved drawings

        tick = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(120) };
        tick.Tick += (_, _) => Tick();
        tick.Start();
        lastCfgJson = System.Text.Json.JsonSerializer.Serialize(cfg);
        var autosave = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(4) };
        autosave.Tick += (_, _) => AutoSaveCfg();
        autosave.Start();
        // build the Library and Images pages quietly in the background so opening them later is instant
        Dispatcher.BeginInvoke(new Action(() => { RefreshLibrary(); RefreshImages(); }), DispatcherPriority.ApplicationIdle);
    }

    // ================================================================ plumbing
    public void Toast(string text, string kind = "info", int ms = 3200) => toasts?.Show(text, kind, ms);

    public Window TopWindow => mini != null && mini.IsVisible ? mini : this;

    string Hint(string action) => Keys.Pretty(cfg.Hotkeys.TryGetValue(action, out var v) ? v : "");

    void BindHotkeys()
    {
        try { table.Set(cfg.Hotkeys); }
        catch (ArgumentException)
        {
            cfg.Hotkeys = new Dictionary<string, string>(HotkeyTable.Defaults);
            table.Set(cfg.Hotkeys);
        }
    }

    void OnMouseHook(MouseInfo m)
    {
        recorder.Mouse(m);                                         // hook thread
        if (spotRec && m.Msg == Native.WM_LBUTTONDOWN && !m.Injected)
        {
            int x = m.X, y = m.Y;
            Dispatcher.BeginInvoke(new Action(() => SpotAdd(x, y)));
        }
    }

    /// Screen rectangles of TinyRec's own windows (excluded from recordings / spots).
    (int l, int t, int r, int b)[] OwnRects()
    {
        var list = new List<(int, int, int, int)>();
        foreach (Window w in new Window[] { this, mini })
        {
            if (w == null || !w.IsVisible) continue;
            var h = new WindowInteropHelper(w).Handle;
            if (h == IntPtr.Zero) continue;
            Native.GetWindowRect(h, out var r);
            list.Add((r.Left, r.Top, r.Right, r.Bottom));
        }
        if (spotPanel.ScreenRect() is { } sp) list.Add(sp);
        return list.ToArray();
    }

    // ================================================================ shell: sidebar, header, pages, footer
    void BuildShell()
    {
        root = K.Grid("236,*");
        Content = root;
        ApplyScale();

        // ---- sidebar
        var sb = K.Grid("*", "auto,auto,*,auto,auto");
        var side = new Border { Child = sb };
        side.SetResourceReference(Border.BackgroundProperty, "Sidebar");
        root.Add(side.At(0, 0));
        var brand = K.H(K.Icon("record", 20, "Red"), K.T(" TinyRec", 20, "Text", FontWeights.Bold),
                        K.Chip("9", "Accent", "AccentSoft", 12, false).M(8, 0, 0, 0));
        sb.Add(brand.M(22, 24, 12, 22).At(0, 0));
        var navStack = new StackPanel();
        foreach (var (key, title, icon, _) in Pages)
        {
            var rb = new RadioButton { GroupName = "nav", Content = K.H(K.Icon(icon, 18).M(0, 0, 12, 0), new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center }) };
            rb.SetResourceReference(StyleProperty, "Nav");
            var k = key;
            rb.Checked += (_, _) => ShowPage(k);
            nav[key] = rb;
            navStack.Children.Add(rb.M(14, 2, 14, 2));
        }
        sb.Add(navStack.At(1, 0));

        sbDot = K.T("●", 15, "Green");
        sbState = K.T("Ready", 14, "Text", FontWeights.SemiBold);
        sbDetail = K.T("", 12, "Muted", null, true);
        var keyRows = new StackPanel();
        foreach (var (action, lab) in new[] { ("record_toggle", "Record"), ("play_toggle", "Play"), ("pause_toggle", "Pause"), ("panic", "Stop all") })
            keyRows.Children.Add(K.H(K.Chip("", "Muted", "Card", 11).M(0, 2, 8, 2), K.T(lab, 12, "Muted")));
        sbKeys = new Border { Child = keyRows };
        var status = K.Box(K.V(K.H(sbDot.M(0, 0, 8, 0), sbState), sbDetail.M(0, 3, 0, 10), sbKeys), "Card2", 12, 14, 12);
        sb.Add(status.M(14, 0, 14, 10).At(3, 0));
        sb.Add(K.T($"v{Version} · runs 100% offline", 11, "Faint").Align(HorizontalAlignment.Center).M(0, 0, 0, 14).At(4, 0));

        // ---- content column
        var content = K.Grid("*", "auto,*,auto");
        root.Add(content.At(0, 1));
        var head = K.Grid("*,auto,auto", "auto,auto");
        hTitle = K.T("", 26, "Text", FontWeights.Bold);
        hSub = K.T("", 13, "Muted");
        topmostSwitch = K.Switch("Always on top", cfg.TopmostFull, v => { cfg.TopmostFull = v; Topmost = v; });
        Topmost = cfg.TopmostFull;
        head.Add(hTitle.At(0, 0), hSub.At(1, 0), topmostSwitch.M(0, 0, 14, 0).At(0, 1, 2),
                 K.Btn("Mini bar", () => SetMini(true), "secondary", "mini").At(0, 2, 2));
        content.Add(head.M(28, 20, 28, 12 - PagePad).At(0, 0));
        content.Add(pageHost.M(28 - PagePad, 0, 28 - PagePad, 0).At(1, 0));
        BuildPages();
        content.Add(BuildFooter().M(28, 14 - PagePad, 28, 20).At(2, 0));

        var toastHost = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Right,
                                         Margin = new Thickness(0, 0, 24, 96), Width = 420 };
        toasts = new Toasts(toastHost);
        content.Add(toastHost.At(0, 0, 3));

        frame = new PulseFrame();
        root.Add(frame.At(0, 0, 1, 2));
    }

    /// Colours from Settings -> the app. Everything on screen follows at once.
    void ApplyTheme()
    {
        Theme.Apply(cfg.Appearance, cfg.Accent, cfg.Custom, cfg.Animations && cfg.AnimHover);
        themeSeenDark = Theme.Dark;
    }

    void ApplyFx()
    {
        Fx.Enabled = cfg.Animations;
        Fx.Ring = cfg.AnimRing;
        Fx.Pages = cfg.AnimPages;
    }

    void TitleBar()
    {
        var h = new WindowInteropHelper(this).Handle;
        if (h == IntPtr.Zero) return;
        Native.DarkTitleBar(h, Theme.Dark);
        Native.TitleBarColor(h, Theme.Neon || Theme.Custom ? Theme.Col("Bg") : null);
    }

    /// True while the colour picker for the border is open: the border shows your colours even if it is switched off.
    bool BorderPreview;

    /// Decide what the frame around the window shows right now (colours, moving or steady, or nothing).
    public void UpdateBorder()
    {
        if (frame == null) return;
        bool recOn = state is "recording" or "spots", playOn = state is "playing" or "countdown";
        bool onScreen = IsVisible && WindowState != WindowState.Minimized;
        bool show = onScreen && (BorderPreview || (cfg.BorderOn && (cfg.BorderWhen != "running" || recOn || playOn)));
        Fx.RingOn(ftRecRing, state == "recording" && !paused && IsVisible && WindowState != WindowState.Minimized);
        Fx.RingOn(heroRecRing, state == "recording" && !paused && IsVisible && WindowState != WindowState.Minimized);
        if (!show) { frame.Stop(); return; }
        List<Color> colors;
        if (cfg.BorderByState && recOn && !BorderPreview) colors = new() { Theme.Col("Red") };
        else if (cfg.BorderByState && playOn && !BorderPreview) colors = new() { Theme.Col("Accent") };
        else
        {
            colors = cfg.BorderColors.Where(Settings.IsHex).Select(Theme.C).ToList();
            if (colors.Count == 0) colors = new() { Theme.Col("Accent"), Theme.Col("Accent2") };
        }
        double seconds = cfg.BorderSpeed switch { "slow" => 5.0, "fast" => 1.4, _ => 2.8 };
        frame.Run(colors, seconds, cfg.Animations && cfg.AnimBorder && !paused);
    }

    void ApplyScale()
    {
        double s = Math.Clamp(cfg.UiScale, 0.8, 1.5);
        root.LayoutTransform = Math.Abs(s - 1) < 0.001 ? null : new ScaleTransform(s, s);
    }

    void BuildPages()
    {
        var builders = new Dictionary<string, Func<FrameworkElement>>
        {
            ["home"] = PageHome, ["sequences"] = PageSequences, ["library"] = PageLibrary, ["editor"] = PageEditor, ["clicker"] = PageClicker,
            ["images"] = PageImages, ["draw"] = PageDraw, ["settings"] = PageSettings,
        };
        foreach (var (key, _, _, _) in Pages)
        {
            var v = builders[key]();
            var padded = v is ScrollViewer sc && sc.Content is FrameworkElement inner ? inner : v;      // inside the scroller, or it gets clipped
            padded.Margin = new Thickness(PagePad);
            v.Visibility = Visibility.Collapsed;
            pageViews[key] = v;
            pageHost.Children.Add(v);
        }
    }

    /// Wrap a page so it scrolls only when the window is too small for it.
    /// Room around every page for the glow that Neon cards draw just outside their edge.
    const double PagePad = 6;

    static ScrollViewer Scroll(FrameworkElement inner) => new()
    {
        Content = inner,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        Focusable = false,
    };

    public void ShowPage(string key)
    {
        if (!pageViews.ContainsKey(key)) return;
        bool switched = page != key && IsLoaded;
        page = key;
        if (switched) Fx.FadeIn(pageViews[key]);
        if (nav[key].IsChecked != true) nav[key].IsChecked = true;
        foreach (var (k, v) in pageViews) v.Visibility = k == key ? Visibility.Visible : Visibility.Collapsed;
        var p = Pages.First(x => x.key == key);
        hTitle.Text = p.title;
        hSub.Text = p.sub;
        if (key == "editor" && tableDirty) { RefreshTable(); if (viewSeg.Value == "Text") RefreshText(); }
        if (key == "library") RefreshLibrary();
        if (key == "images") RefreshImages();
        if (key == "sequences") SeqRefresh();
        if (key is "sequences" or "editor") LogRefresh();
    }

    FrameworkElement BuildFooter()
    {
        var g = K.Grid("auto,auto,*,auto", "auto,auto");
        ftRec = K.Round("record", () => ToggleRecord(), "Red", 42, "Record / stop recording");
        ftPlay = K.Round("play", () => PlayTarget(), "Accent", 42, "Play / stop");
        ftPause = K.Round("pause", TogglePause, null, 42, "Pause / continue");
        var btns = K.H(Fx.WithRing(ftRec, "Red", out ftRecRing).M(0, 0, 6, 0), ftPlay.M(0, 0, 6, 0), ftPause.M(0, 0, 6, 0), K.Round("stop", Panic, null, 42, "Stop everything"));
        g.Add(btns.M(14, 12, 6, 12).At(0, 0, 2));
        g.Add(K.Divider(true, 10).M(10, 14, 10, 14).At(0, 1, 2));
        ftName = K.T("No recording loaded", 14, "Text", FontWeights.SemiBold);
        ftTime = K.T("", 12, "Muted").Align(HorizontalAlignment.Right);
        var top = K.Grid("*,auto");
        top.Add(ftName.At(0, 0), ftTime.At(0, 1));
        g.Add(top.M(0, 14, 12, 4).At(0, 2));
        ftBar = new ProgressBar { Height = 6, Style = (Style)FindResource("Bar"), Minimum = 0, Maximum = 1, Value = 0, BorderThickness = new Thickness(0), Margin = new Thickness(0, 0, 12, 14) };
        ftBar.SetResourceReference(ForegroundProperty, "AccentFill");
        ftBar.SetResourceReference(BackgroundProperty, "Hover");
        g.Add(ftBar.At(1, 2));
        ftLoop = K.T("", 13, "Muted", FontWeights.SemiBold);
        ftSpeed = K.T("1.00×", 13, "Text", FontWeights.SemiBold);
        ftSpeed.Width = 54;
        ftSpeed.TextAlignment = TextAlignment.Center;
        var spd = K.Box(K.H(K.IconBtn("minus", () => StepSpeed(-1), "Slower", 30), ftSpeed, K.IconBtn("plus", () => StepSpeed(1), "Faster", 30)), "Card2", 10, 2, 2);
        g.Add(K.H(K.Icon("loop", 15, "Muted").M(0, 0, 5, 0), ftLoop.M(0, 0, 14, 0), spd).M(6, 0, 14, 0).At(0, 3, 2));
        return K.Card(g);
    }

    // ================================================================ status
    static readonly Dictionary<string, (string color, string title)> StateStyle = new()
    {
        ["idle"] = ("Green", "Ready"), ["countdown"] = ("Amber", "Get ready"), ["recording"] = ("Red", "Recording"),
        ["playing"] = ("Accent", "Playing"), ["auto"] = ("Accent", "Auto clicking"), ["error"] = ("Red", "Error"),
        ["spots"] = ("Green", "Recording spots"), ["paused"] = ("Amber", "Paused"),
    };

    string lastStatus;

    public void SetStatus(string detail = "", string st = null, string shortText = null)
    {
        st ??= paused && state is "recording" or "playing" ? "paused" : state != "idle" ? state : (auto.Running ? "auto" : "idle");
        var key = $"{st}|{detail}|{shortText}";
        if (key == lastStatus) return;
        lastStatus = key;
        var (color, title) = StateStyle[st];
        sbDot.SetResourceReference(TextBlock.ForegroundProperty, color);
        sbState.Text = title;
        sbDetail.Text = detail;
        var m = shortText ?? detail;
        mini?.SetStatus(color, st is "recording" or "playing" or "countdown" or "spots" or "paused" && !string.IsNullOrEmpty(m) ? m : title);
    }

    void RefreshButtons()
    {
        bool recOn = state == "recording", playOn = state is "playing" or "countdown";
        K.SetRoundIcon(ftRec, recOn ? "stop" : "record");
        K.SetRoundIcon(ftPlay, playOn ? "stop" : "play");
        bool canPause = state is "recording" or "playing";
        K.SetRoundIcon(ftPause, paused ? "play" : "pause");
        ftPause.IsEnabled = canPause;
        mini?.SetRunning(recOn, playOn, canPause, paused);
        HomeButtons(recOn, playOn);
        var chips = ((StackPanel)sbKeys.Child).Children.OfType<StackPanel>().ToList();
        string[] acts = { "record_toggle", "play_toggle", "pause_toggle", "panic" };
        for (int i = 0; i < chips.Count && i < acts.Length; i++) K.SetChip((Border)chips[i].Children[0], Hint(acts[i]));
        ClickerButtons();
        DrawButtons();
        HotkeyChips();
        SeqButtons();
        UpdateBorder();
        if (state == "idle" && !spotRec)
            SetStatus(auto.Running ? $"{auto.Clicks:N0} clicks so far"
                      : cfg.SeqHotkey && seq != null ? $"{Hint("play_toggle")} runs the sequence “{seq.Name}”" : "Waiting for you");
    }

    void RefreshInfo()
    {
        var c = Edits.Count(rec.Events);
        HomeInfo(c);
        var name = rec.GetMeta("name");
        ftName.Text = (string.IsNullOrEmpty(name) ? "Untitled" : name) + (dirty ? "  •" : "");
        if (state != "playing")
        {
            ftTime.Text = $"{rec.Events.Count:N0} actions  ·  {Fmt.Dur(rec.Duration)}";
            ftBar.Value = 0;
        }
        EditorStatus();
    }

    void RefreshEverything()
    {
        loadingMeta = true;
        HomeMeta();
        loadingMeta = false;
        RefreshInfo();
        RefreshButtons();
        tableDirty = true;
        if (page == "editor") { RefreshTable(); if (viewSeg.Value == "Text") RefreshText(); }
        if (page == "library") RefreshLibrary(true);
        RefreshLoopBadge();
    }

    // ================================================================ speed
    public void SetSpeed(double v, bool fromSlider = false)
    {
        v = Math.Round(Math.Clamp(v, 0.1, 10), 2);
        Volatile.Write(ref speed, v);
        var txt = $"{v:0.00}×";
        ftSpeed.Text = txt;
        mini?.SetSpeed(txt);
        HomeSpeed(txt, v, fromSlider);
    }

    static readonly double[] SpeedSteps = { 0.1, 0.25, 0.5, 0.75, 1, 1.25, 1.5, 2, 3, 4, 5, 10 };

    public void StepSpeed(int dir)
    {
        var s = speed;
        var next = dir > 0 ? SpeedSteps.FirstOrDefault(x => x > s + 1e-6, SpeedSteps[^1])
                           : SpeedSteps.LastOrDefault(x => x < s - 1e-6, SpeedSteps[0]);
        SetSpeed(next);
    }

    // ================================================================ timer tick
    void Tick()
    {
        if (state == "recording" && cfg.RecMaxSecs > 0 && recorder.Elapsed >= cfg.RecMaxSecs)
        {
            StopRecord();                                           // the recording's own time limit
            Toast($"Recording stopped by itself after {Fmt.Num(cfg.RecMaxSecs)} s (Record & Play > Record).", "ok", 6000);
        }
        if (state == "recording")
        {
            recorder.Exclude = OwnRects();
            double el = recorder.Elapsed;
            int n = recorder.Count;
            if (paused)
            {
                SetStatus($"Recording paused at {Fmt.Dur(el)} · {Hint("pause_toggle")} to continue", null, $"paused {Fmt.Dur(el)}");
                ftTime.Text = $"Recording paused  ·  {Fmt.Dur(el)}  ·  {n:N0} actions";
                if (cfg.ShowIndicator) badge.ShowText($"❚❚  PAUSED  {Fmt.Dur(el)}   ·   {Hint("pause_toggle")} to continue", "#c27c00");
            }
            else
            {
                SetStatus($"{Fmt.Dur(el)} · {n:N0} actions", null, Fmt.Dur(el));
                ftTime.Text = $"Recording  ·  {Fmt.Dur(el)}  ·  {n:N0} actions";
                if (cfg.ShowIndicator) badge.ShowText($"●  REC  {Fmt.Dur(el)}   ·   {Hint("record_toggle")} to stop");
            }
        }
        if (state == "playing") ownRects = OwnRects();
        if (state == "playing" && playRec != null)
        {
            int i = Volatile.Read(ref progI), loop = Math.Max(1, Volatile.Read(ref progLoop));
            var shown = playShown ?? playRec.Events;                // with "keyboard only" the player counts the kept actions
            double t = shown.Count > 0 ? shown[Math.Clamp(i - 1, 0, shown.Count - 1)].T : 0;
            double dur = Math.Max(playRec.Duration, 1e-9);
            var loops = loopsTotal == 1 ? "" : $"  ·  loop {loop}" + (loopsTotal > 0 ? $" of {loopsTotal}" : " (continuous)");
            int pct = (int)(100 * Math.Min(1, t / dur));
            ftBar.Value = Math.Min(1, t / dur);
            var clockText = $"{Fmt.Dur(t)} / {Fmt.Dur(dur)}";
            var loopShort = loopsTotal == 1 ? "" : $" · loop {loop}";
            ftTime.Text = $"{clockText}{loops}  ·  {speed:0.00}×";
            timeline?.SetPlayhead(t);
            if (paused)
            {
                ftTime.Text = $"Paused at {clockText}{loops}";
                SetStatus($"Paused at {clockText}{loops} · {Hint("pause_toggle")} to continue", null, $"paused {clockText}");
            }
            else if (player.WaitingFor is { } wf)
            {
                var w = $"waiting for {wf} ({Fmt.Dur(Timing.Now - player.WaitingSince)})";
                ftTime.Text = $"{w}{loops}";
                SetStatus($"{w}{loops}", null, $"{Fmt.Dur(t)} waiting for image");
            }
            else SetStatus($"{clockText}  ·  {pct}%{loops}", null, clockText + loopShort);
            if (showPlayBadge)
                badge.ShowText(paused ? $"❚❚  PAUSED  {pct}%{loops}   ·   {Hint("pause_toggle")} to continue"
                                      : $"▶  PLAYING  {pct}%{loops}   ·   {Hint("panic")} to stop", paused ? "#c27c00" : "#3f58e0");
        }
        if (state == "playing" && seqRun != null) SeqTick();
        if (logDirty)
        {
            logDirty = false;
            LogRefresh();
        }
        if (auto.Running) ClickerTick();
        if (++themeCheck % 25 == 0 && cfg.Appearance == "system" && Theme.WindowsIsDark() != themeSeenDark) ThemeChanged(true);
    }

    // ================================================================ hotkeys
    void OnHotkey(string action)
    {
        if (spotRec)                              // while recording spots, the clicker key (or panic) means "done"
        {
            if (action is "autoclick_toggle" or "panic") SpotFinish(true);
            return;
        }
        switch (action)
        {
            case "record_toggle": ToggleRecord(0); break;
            case "play_toggle": PlayTarget(0); break;
            case "pause_toggle": TogglePause(); break;
            case "autoclick_toggle": ToggleAuto(); break;
            case "draw_toggle": draw.Toggle(); break;
            case "panic": Panic(); break;
            case "speed_up": StepSpeed(1); break;
            case "speed_down": StepSpeed(-1); break;
            case "speed_reset": SetSpeed(1); break;
            case "mini_toggle": SetMini(!mini.IsVisible); break;
        }
        RefreshButtons();
    }

    void Rebind(string action)
    {
        pendingRebind = action;
        router.Capture = true;
        HotkeyChips();
        Toast($"Press the new key or combo for “{HotkeyTable.LabelOf(action)}”");
    }

    void OnCaptured(string combo)
    {
        var action = pendingRebind;
        pendingRebind = null;
        if (action == null) return;
        var trial = new Dictionary<string, string>(cfg.Hotkeys) { [action] = combo };
        try { table.Set(trial); }
        catch (ArgumentException e)
        {
            table.Set(cfg.Hotkeys);
            RefreshButtons();
            Toast($"Not changed: {e.Message}", "error", 5000);
            return;
        }
        cfg.Hotkeys = trial;
        RefreshButtons();
        LoopModeChanged();
        Toast($"{HotkeyTable.LabelOf(action)} → {Keys.Pretty(combo)}", "ok");
    }

    void ResetHotkeys()
    {
        cfg.Hotkeys = new Dictionary<string, string>(HotkeyTable.Defaults);
        table.Set(cfg.Hotkeys);
        RefreshButtons();
        LoopModeChanged();
        Toast("Hotkeys reset", "ok");
    }

    // ================================================================ mini bar + run mode
    public void SetMini(bool on)
    {
        if (on)
        {
            mini.ShowBar(cfg.MiniPos);
            Hide();
        }
        else
        {
            mini.Hide();
            Show();
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate();
        }
    }

    public void MiniMoved(int x, int y) => cfg.MiniPos = new[] { x, y };

    public void SetMiniTopmost(bool on)
    {
        cfg.TopmostMini = on;
        mini.Topmost = on;
        mini.RefreshPin();
        SettingsSync();
    }

    public bool MiniTopmost => cfg.TopmostMini;

    void EnterRunMode()
    {
        var mode = runModeOverride ?? cfg.DuringRun;
        if (mode == "mini" && !mini.IsVisible)
        {
            runRestore = "full";
            SetMini(true);
        }
        else if (mode == "hide")
        {
            runRestore = mini.IsVisible ? "mini" : "full";
            Hide();
            mini.Hide();
        }
        else runRestore = null;
    }

    void LeaveRunMode()
    {
        var r = runRestore;
        runRestore = null;
        runModeOverride = null;
        if (r == "full") SetMini(false);
        else if (r == "mini") SetMini(true);
    }

    void Countdown(int n, string label, Action then)
    {
        state = "countdown";
        int mine = ++cdId;
        EnterRunMode();
        RefreshButtons();
        void Step(int k)
        {
            if (mine != cdId) return;
            if (k <= 0)
            {
                badge.HideBadge();
                then();
                return;
            }
            SetStatus($"{label} in {k}…", "countdown");
            if (cfg.ShowIndicator) badge.ShowText($"{label} in {k}", "#c27c00", true);
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            t.Tick += (_, _) => { t.Stop(); Step(k - 1); };
            t.Start();
        }
        Step(n);
    }

    int CountdownSecs(int? overrideSecs) => overrideSecs ?? cfg.Countdown;

    // ================================================================ recording
    public void ToggleRecord(int? countdown = null)
    {
        if (state == "recording") { StopRecord(); return; }
        if (state != "idle") { Panic(); return; }
        if (!ConfirmDiscard()) return;
        draw.Stop();
        Countdown(CountdownSecs(countdown), "Recording", BeginRecord);
    }

    void BeginRecord()
    {
        CollectHomeExtras();
        fgAtRecord = ScreenGrab.Foreground();
        recorder.Exclude = OwnRects();
        recorder.What = Only.Parse(cfg.RecordWhat);
        recorder.Start();
        hooks.SetMouse(true);
        tRec = Timing.Now;
        paused = false;
        state = "recording";
        RefreshButtons();
        Tick();
    }

    void StopRecord()
    {
        var events = recorder.Stop(cfg.TrimStart, Keys.TokensOf(cfg.Hotkeys["record_toggle"]));
        if (!spotRec) hooks.SetMouse(false);
        state = "idle";
        paused = false;
        badge.HideBadge();
        int raw = events.Count;
        if (!cfg.RecordMoves) events = Edits.DropMoves(events);
        if (cfg.TrimEnd) events = Edits.TrimTrailingMoves(events);
        if (cfg.AutoThin) events = Edits.ThinMoves(events);
        var now = DateTime.Now;
        var r = new Recording { Events = events };
        r.SetMeta("name", now.ToString("'Recording' MMM dd HH.mm.ss"));
        r.SetMeta("created", now.ToString("yyyy-MM-dd HH:mm:ss"));
        var (_, _, vw, vh) = Native.VirtualScreen();
        r.SetMeta("screen", $"{vw}x{vh}");
        if (fgAtRecord is { } fg && fg.title.Length > 0 && !fg.title.Contains("TinyRec", StringComparison.OrdinalIgnoreCase))
        {
            r.SetMeta("window", fg.title);
            r.SetMeta("origin", $"{fg.left} {fg.top}");
        }
        var path = Path.Combine(RecDir, "rec_" + now.ToString("yyyyMMdd_HHmmss") + ".txt");
        try { Files.SaveRecording(r, path); }
        catch (Exception e)
        {
            path = null;
            Dialog.Info(TopWindow, "Auto-save failed", $"The recording is loaded but wasn't saved: {e.Message}\nUse Save as… to keep it.", "error");
        }
        LeaveRunMode();
        SetRecording(r, path, path == null);
        RefreshEverything();
        int dropped = raw - events.Count;
        Toast($"Recorded {events.Count:N0} actions ({Fmt.Dur(r.Duration)})" + (path != null ? " · saved to Library" : "")
              + (dropped > 0 ? $" · {dropped:N0} extra points trimmed" : ""), "ok");
    }

    // ================================================================ playback
    public void TogglePlay(int? countdown = null)
    {
        if (state is "playing" or "countdown") { Panic(); return; }
        if (state != "idle") return;
        if (rec.Events.Count == 0) { Toast("Nothing to play yet - record something or open one from the Library.", "warn"); return; }
        (int, int) offset;
        try { offset = PlayOffset(rec); }
        catch (InvalidOperationException e) { Dialog.Info(TopWindow, "Can't follow the window", e.Message, "warn"); return; }
        if (rec.Events.Any(e => e.IsImage) && finder == null)
        {
            Dialog.Info(TopWindow, "Image recognition is off", "This recording has image steps, but OpenCV couldn't start.", "error");
            return;
        }
        int loops = LoopsSetting();
        double delay = Math.Max(0, loopDelayStep?.Value ?? cfg.LoopDelay);
        draw.Stop();
        Countdown(CountdownSecs(countdown), "Playing", () => BeginPlay(loops, delay, offset));
    }

    /// What the Play hotkey, the mini bar and the footer button start: the sequence (if switched on) or the recording.
    public void PlayTarget(int? countdown = null)
    {
        if (state == "idle" && cfg.SeqHotkey && seq != null) ToggleSequence(countdown);
        else TogglePlay(countdown);
    }

    (int, int) PlayOffset(Recording r)
    {
        if (!cfg.RelativeToWindow) return (0, 0);
        var title = r.GetMeta("window");
        var origin = r.GetMeta("origin");
        if (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(origin))
            throw new InvalidOperationException("This recording has no window position saved. Turn off 'Follow recorded window', or record it again with the target window in front.");
        var info = ScreenGrab.FindWindow(title) ?? throw new InvalidOperationException($"No open window has “{title}” in its title. Open it and try again.");
        var p = origin.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return (info.left - int.Parse(p[0]), info.top - int.Parse(p[1]));
    }

    void BeginPlay(int loops, double delay, (int, int) offset)
    {
        state = "playing";
        System.Windows.Input.Keyboard.ClearFocus();   // so a replayed Space or Enter can't press the button that was just clicked
        seqRun = null;
        paused = false;
        player.Resume();
        loopsTotal = loops;
        playRec = rec.Copy();
        string only = Only.Clean(cfg.PlayOnly);
        int sent = only.Length > 0 ? playRec.Events.Count(e => Only.Keeps(only, e)) : playRec.Events.Count;
        playStart = Timing.Now;
        StartLog(rec.GetMeta("name") is { Length: > 0 } nm ? nm : "Untitled", "rec",
                 $"playing {(loops == 0 ? "until stopped" : loops == 1 ? "once" : $"{loops} times")} at {speed:0.00}× - {sent:N0} actions, {Fmt.Dur(playRec.Duration)}"
                 + (only.Length > 0 ? $" ({Only.Name(only)})" : ""));
        player.Log = runLog;
        player.LogStep = -1;
        progI = 0;
        progN = Math.Max(1, sent);
        playShown = only.Length > 0 ? Only.Of(playRec.Events.OrderBy(e => e.T), only) : null;
        ownRects = OwnRects();
        progLoop = 1;
        if (cfg.PlayClicker) SideClickerStart();
        showPlayBadge = cfg.ShowIndicator && !playRec.Events.Any(e => e.IsImage);
        if (cfg.HideDrawingsWhilePlaying) draw.Suspend();
        RefreshButtons();
        var r = playRec;
        CollectHomeExtras();
        double loopRandom = cfg.LoopDelayRandom, maxMinutes = cfg.PlayMaxMinutes;
        var th = new Thread(() =>
        {
            try
            {
                using var limit = new CancellationTokenSource();
                using var limiter = maxMinutes > 0 ? new PausableLimit(limit, maxMinutes * 60, () => player.Paused) : null;   // paused time doesn't count
                var st = player.Play(r, loops, delay, offset, (i, n, l) =>
                {
                    Volatile.Write(ref progI, i);
                    Volatile.Write(ref progLoop, l);
                }, limit.Token, only, loopRandom);
                if (limit.IsCancellationRequested && string.IsNullOrEmpty(st.Message))
                {
                    st.Stopped = false;                              // its own time limit is a normal end, not a stop
                    st.Message = "";
                    Dispatcher.BeginInvoke(new Action(() => runLog?.Add($"the {Fmt.Num(maxMinutes)} minute time limit was reached")));
                }
                Dispatcher.BeginInvoke(new Action(() => OnPlayDone(st, null)));
            }
            catch (Exception e)
            {
                App.Log(e.ToString());
                Dispatcher.BeginInvoke(new Action(() => OnPlayDone(null, $"{e.GetType().Name}: {e.Message}")));
            }
        }) { IsBackground = true, Priority = ThreadPriority.Highest, Name = "TinyRec playback" };
        th.Start();
    }

    // ---- TinyRec's own windows, for the click threads (which must never click TinyRec's own Stop button)
    volatile (int l, int t, int r, int b)[] ownRects = Array.Empty<(int, int, int, int)>();

    bool OnOwnWindow(int x, int y)
    {
        foreach (var (l, t, r, b) in ownRects)
            if (x >= l && x < r && y >= t && y < b) return true;
        return false;
    }

    // ---- the auto clicker "alongside": it runs for as long as a recording or sequence plays
    bool sideClicker;
    List<Event> playShown;                              // the actions being sent when playback is filtered (else null)

    /// The Auto Clicker page's setup, as it is right now - but it runs until playback ends, and starts at once.
    AutoClickConfig SideConfig()
    {
        var c = (acTypeSeg != null ? ClickerConfig() : cfg.Autoclick).Copy();
        c.Count = 0;
        c.Seconds = 0;
        c.StartDelay = 0;
        return c;
    }

    void SideClickerStart()
    {
        sideClicker = false;
        if (auto.Running) return;                       // already clicking by hand: leave it alone
        try
        {
            ownRects = OwnRects();
            auto.IsOwn = OnOwnWindow;
            auto.Start(SideConfig(), () => Dispatcher.BeginInvoke(new Action(RefreshButtons)));
            autoT0 = Timing.Now;
            sideClicker = true;
            runLog?.Add("the auto clicker runs alongside");
        }
        catch (ArgumentException e)
        {
            Toast($"The auto clicker isn't clicking alongside: {e.Message}. Fix it on the Auto Clicker page.", "warn", 6000);
        }
    }

    void SideClickerStop()
    {
        if (!sideClicker) return;
        sideClicker = false;
        int n = auto.Clicks;
        auto.Halt();
        auto.Hold = false;
        auto.IsOwn = null;
        runLog?.Add($"the auto clicker made {n:N0} click{(n == 1 ? "" : "s")} alongside" + (auto.Error.Length > 0 ? $" (it stopped early: {auto.Error})" : ""));
    }

    void OnPlayDone(PlayStats st, string error)
    {
        SideClickerStop();
        if (cfg.DoneSound) Beep();
        state = "idle";
        paused = false;
        player.Resume();
        timeline?.SetPlayhead(-1);
        runLog?.Add(error != null ? $"error: {error}" : !string.IsNullOrEmpty(st.Message) ? st.Message
                    : $"{(st.Stopped ? "stopped" : "finished")} after {Fmt.Dur(Timing.Now - playStart)} - {st.Loops} loop{(st.Loops == 1 ? "" : "s")}, {st.Events:N0} actions");
        badge.HideBadge();
        draw.Resume();
        LeaveRunMode();
        playRec = null;
        if (error != null)
        {
            SetStatus("Playback failed", "error");
            Dialog.Info(TopWindow, "Playback stopped", $"An error stopped playback:\n{error}", "error");
        }
        else
        {
            HomeLastRun(st);
            var msg = !string.IsNullOrEmpty(st.Message) ? st.Message
                    : $"{(st.Stopped ? "Stopped" : "Finished")} · {st.Loops} loop{(st.Loops == 1 ? "" : "s")} · worst timing {st.MaxLateMs:0.0} ms";
            Toast(msg, string.IsNullOrEmpty(st.Message) ? "ok" : "warn");
        }
        RefreshInfo();
        RefreshButtons();
    }

    // ================================================================ panic
    /// Runs on the keyboard-hook thread: only flips thread-safe switches.
    void PanicNow()
    {
        Interlocked.Increment(ref cdId);
        runner.Stop();                      // also stops the player
        auto.Halt();
    }

    /// Pause or continue whatever is running: a recording in progress, a playback or a sequence.
    public void TogglePause()
    {
        if (state == "recording")
        {
            if (recorder.Paused) recorder.Resume(); else recorder.Pause();
            paused = recorder.Paused;
        }
        else if (state == "playing")
        {
            if (player.Paused) player.Resume(); else player.Pause();
            paused = player.Paused;
            if (sideClicker) auto.Hold = paused;
        }
        else return;
        RefreshButtons();
        Tick();
    }

    /// Where image steps look when they don't say: one window (Settings), or null for the whole desktop.
    (int x, int y, int w, int h)? ImageSearchArea()
    {
        var own = seqRun?.Window;                                      // a running sequence can name its own window
        if (!string.IsNullOrWhiteSpace(own)) return ScreenGrab.FindWindowRect(own.Trim());
        if (cfg.ImageArea != "window" || string.IsNullOrWhiteSpace(cfg.ImageWindow)) return null;
        return ScreenGrab.FindWindowRect(cfg.ImageWindow.Trim());      // window not open -> null -> whole desktop
    }

    // ================================================================ run log
    void StartLog(string title, string kind, string firstLine)
    {
        var log = new RunLog(title);
        string path = null;
        if (cfg.KeepLogs)
        {
            try
            {
                Directory.CreateDirectory(LogDir);
                path = Files.UniquePath(LogDir, $"run_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
                File.WriteAllText(path, $"# TinyRec run log - {title}\r\n");
                foreach (var old in Directory.EnumerateFiles(LogDir, "run_*.txt").OrderByDescending(f => f).Skip(60).ToList())
                    try { File.Delete(old); } catch { }
            }
            catch { path = null; }
        }
        log.OnAdd = e =>
        {
            logDirty = true;
            if (path != null)
                try { File.AppendAllText(path, RunLog.Line(e) + "\r\n"); } catch (IOException) { }
        };
        runLog = log;
        runLogPath = path;
        runLogKind = kind;
        log.Add(firstLine);
    }

    public void Panic()
    {
        if (spotRec) SpotFinish(true);
        var was = state;
        paused = false;
        PanicNow();
        if (was == "recording") StopRecord();
        else if (was == "countdown")
        {
            state = "idle";
            badge.HideBadge();
            LeaveRunMode();
        }
        draw.Stop();
        RefreshButtons();
    }

    // ================================================================ files
    bool ConfirmDiscard()
    {
        if (!dirty || rec.Events.Count == 0) return true;
        var r = Dialog.Ask(TopWindow, "Unsaved changes", "Save the current recording first?",
                           new[] { ("Cancel", (object)null, "secondary"), ("Don't save", (object)"no", "secondary"), ("Save", (object)"yes", "primary") }, "save");
        if (r == null) return false;
        return (string)r != "yes" || Save();
    }

    void SetRecording(Recording r, string path, bool isDirty = false)
    {
        rec = r;
        recPath = path;
        dirty = isDirty;
        history.Clear();
        future.Clear();
        cfg.LastRecording = path ?? "";
        tableDirty = true;
    }

    public void NewRecording()
    {
        if (state != "idle" || !ConfirmDiscard()) return;
        var r = new Recording();
        r.SetMeta("name", "Untitled");
        SetRecording(r, null);
        RefreshEverything();
        ShowPage("home");
    }

    public void OpenDialog()
    {
        if (state != "idle" || !ConfirmDiscard()) return;
        var d = new Microsoft.Win32.OpenFileDialog
        {
            InitialDirectory = RecDir, Title = "Open or import a recording",
            Filter = "TinyRec recordings (*.txt;*.zip)|*.txt;*.zip|All files (*.*)|*.*",
        };
        if (d.ShowDialog(this) == true) OpenPath(d.FileName);
    }

    bool OpenPath(string p)
    {
        Recording r;
        List<string> notes;
        try { (r, notes) = Files.LoadAny(p, ImgDir); }
        catch (Exception e)
        {
            Dialog.Info(TopWindow, "Couldn't open that file", e.Message, "error");
            return false;
        }
        bool inside = SameDir(Path.GetDirectoryName(p), RecDir) && p.EndsWith(".txt", StringComparison.OrdinalIgnoreCase);
        if (!inside)                                          // imports get their own copy in the library right away
        {
            var name = r.GetMeta("name");
            var dest = Files.UniquePath(RecDir, Files.Slug(string.IsNullOrEmpty(name) ? Path.GetFileNameWithoutExtension(p) : name) + ".txt");
            try
            {
                Files.SaveRecording(r, dest);
                p = dest;
                inside = true;
            }
            catch { }
        }
        SetRecording(r, inside ? p : null, !inside);
        RefreshEverything();
        var shown = r.GetMeta("name");
        Toast($"Opened “{(string.IsNullOrEmpty(shown) ? Path.GetFileName(p) : shown)}” · {r.Events.Count:N0} actions", "ok");
        foreach (var n in notes) Toast(n, "warn", 6000);
        return true;
    }

    static bool SameDir(string a, string b) =>
        string.Equals(Path.GetFullPath(a ?? "").TrimEnd('\\', '/'), Path.GetFullPath(b).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);

    static bool SameFile(string a, string b) =>
        a != null && b != null && string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    public bool Save()
    {
        if (recPath == null) return SaveAs();
        try { Files.SaveRecording(rec, recPath); }
        catch (Exception e)
        {
            Dialog.Info(TopWindow, "Save failed", e.Message, "error");
            return false;
        }
        dirty = false;
        cfg.LastRecording = recPath;
        RefreshInfo();
        Toast("Saved", "ok", 1800);
        return true;
    }

    public bool SaveAs()
    {
        var d = new Microsoft.Win32.SaveFileDialog
        {
            InitialDirectory = RecDir, DefaultExt = ".txt", Filter = "TinyRec recording (*.txt)|*.txt",
            FileName = Files.Slug(rec.GetMeta("name")) + ".txt",
        };
        if (d.ShowDialog(this) != true) return false;
        var stem = Path.GetFileNameWithoutExtension(d.FileName);
        if (!string.Equals(Files.Slug(rec.GetMeta("name")), stem, StringComparison.OrdinalIgnoreCase))
        {
            rec.SetMeta("name", stem);                       // you typed a new file name: use it as the recording's name too
            HomeMeta();                                      // (before saving, so the name box doesn't flag it unsaved again)
        }
        try { Files.SaveRecording(rec, d.FileName); }
        catch (Exception e)
        {
            Dialog.Info(TopWindow, "Save failed", e.Message, "error");
            return false;
        }
        recPath = d.FileName;
        dirty = false;
        cfg.LastRecording = recPath;
        RefreshInfo();
        Toast($"Saved as {Path.GetFileName(recPath)}", "ok");
        return true;
    }

    void Export(Recording r, string kind)
    {
        if (r.Events.Count == 0) { Toast("Nothing to export.", "warn"); return; }
        var name = Files.Slug(r.GetMeta("name"));
        var d = new Microsoft.Win32.SaveFileDialog
        {
            FileName = name + (kind == "txt" ? ".txt" : ".zip"),
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),   // not inside the library
            Filter = kind == "txt" ? "Text (*.txt)|*.txt" : "Zip bundle (*.zip)|*.zip",
            Title = kind == "txt" ? "Export as readable text" : "Export bundle (recording + its images)",
        };
        if (d.ShowDialog(this) != true) return;
        try
        {
            if (kind == "txt")
            {
                Files.SaveRecording(r, d.FileName);
                Toast("Exported - it's plain text, open it in Notepad to see every action.", "ok", 4500);
            }
            else
            {
                var missing = Files.ExportBundle(r, ImgDir, d.FileName);
                Toast("Bundle exported" + (missing.Count > 0 ? $" - missing images: {string.Join(", ", missing)}" : ""), missing.Count > 0 ? "warn" : "ok", 5000);
            }
        }
        catch (Exception e) { Dialog.Info(TopWindow, "Export failed", e.Message, "error"); }
    }

    void SetMeta(string key, string value)
    {
        if (loadingMeta || (rec.GetMeta(key) ?? "") == (value ?? "")) return;
        rec.SetMeta(key, value);
        dirty = true;
        RefreshInfo();
    }

    /// Run something with TinyRec's own windows out of the way (snips, image tests, pickers).
    T Hidden<T>(Func<T> fn)
    {
        bool wasMini = mini.IsVisible, wasMain = IsVisible;
        draw.Suspend();
        Hide();
        mini.Hide();
        Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
        Thread.Sleep(250);
        try { return fn(); }
        finally
        {
            if (wasMini) mini.Show();
            else if (wasMain) { Show(); Activate(); }
            draw.Resume();
        }
    }

    void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception e) { Toast(e.Message, "error"); }
    }

    // ================================================================ quit
    /// Numbers typed on the Record & Play page but not confirmed with Enter yet still count.
    void CollectHomeExtras()
    {
        if (loopRandomStep != null) cfg.LoopDelayRandom = loopRandomStep.Value;
        if (maxMinStep != null) cfg.PlayMaxMinutes = maxMinStep.Value;
        if (recMaxStep != null) cfg.RecMaxSecs = recMaxStep.Value;
    }

    void CollectCfg()
    {
        cfg.Loops = LoopsSetting();
        CollectHomeExtras();
        if (loopCountStep != null) cfg.RepeatCount = (int)loopCountStep.Value;
        if (loopDelayStep != null) cfg.LoopDelay = loopDelayStep.Value;
        if (countdownStep != null) cfg.Countdown = (int)countdownStep.Value;
        cfg.DrawTool = draw.Tool;
        cfg.DrawColor = draw.InkColor;
        cfg.DrawWidth = (int)Math.Round(draw.InkWidth);
        try { cfg.Autoclick = ClickerConfig(); } catch (ArgumentException) { }
        SeqCollectCfg();
    }

    void SaveCfg()
    {
        CollectCfg();
        try
        {
            cfg.Save(ConfigPath);
            lastCfgJson = System.Text.Json.JsonSerializer.Serialize(cfg);
        }
        catch { }
    }

    string lastCfgJson;

    /// Every few seconds: save settings if anything changed, so nothing is lost if Windows shuts down or TinyRec is killed.
    void AutoSaveCfg()
    {
        if (quitting) return;
        try
        {
            CollectCfg();
            var j = System.Text.Json.JsonSerializer.Serialize(cfg);
            if (j == lastCfgJson) return;
            cfg.Save(ConfigPath);
            lastCfgJson = j;
        }
        catch { }
    }

    public void Quit()
    {
        if (state == "recording") StopRecord();
        if (!ConfirmDiscard()) return;
        PanicNow();
        if (spotRec) SpotFinish(true);
        SaveCfg();
        hooks?.Dispose();
        Native.timeEndPeriod(1);
        quitting = true;
        if (mini != null) mini.Quitting = true;
        foreach (Window w in Application.Current.Windows.Cast<Window>().ToList())
            if (w != this) try { w.Close(); } catch { }
        Close();
        Application.Current.Shutdown();
    }

    public void Restart()
    {
        if (!ConfirmDiscard()) return;
        SaveCfg();
        try
        {
            var exe = Environment.ProcessPath;
            if (exe != null) Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = AppDir });
        }
        catch { }
        dirty = false;
        Quit();
    }
}
