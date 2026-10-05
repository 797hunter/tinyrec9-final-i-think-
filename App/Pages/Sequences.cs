// Sequences.cs - chain recordings together. Each step either waits for a picture (and clicks it), plays a saved
// recording, pauses, or repeats the steps above it. A recording never starts until the picture before it was clicked.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TinyRec.Core;
using TinyRec.Overlays;
using TinyRec.Ui;

namespace TinyRec;

public partial class MainWindow
{
    Sequence seq;                                   // the sequence open on this page (saved automatically on every change)
    string seqPath;
    readonly StackPanel seqList = new();
    readonly List<Border> seqRowBoxes = new();
    readonly List<TextBlock> seqRowLive = new();          // the "what it's doing right now" line of each step row
    readonly List<Border> seqRowRuns = new();             // the "x3" chip of each step row (how often it ran this run)
    TextBlock seqNote;
    CheckBox seqStaySwitch;
    bool seqLoading;
    Button seqPickBtn, seqRunBtn;
    Segmented seqLoopSeg;
    Stepper seqLoopCount;
    FrameworkElement seqLoopCountRow;
    CheckBox seqHotkeySwitch;
    TextBlock seqStatus, seqResult, seqLoopHint;
    int seqShown = -2;
    readonly Dictionary<string, (DateTime mtime, Files.Summary sum)> seqScanCache = new();

    FrameworkElement PageSequences()
    {
        SeqLoadInitial();
        var page = K.Grid("*,340", "*");

        // ---- left: the steps
        var left = K.Grid("*", "auto,*,auto");
        seqPickBtn = K.Btn("", SeqPick, "secondary", "loop", 36);
        seqPickBtn.FontWeight = FontWeights.SemiBold;
        seqPickBtn.Tip("Switch to another sequence");
        var bar = K.Grid("auto,*,auto", "auto,auto");
        seqNote = K.T("", 12, "Muted", null, true);
        bar.Add(seqPickBtn.At(0, 0),
                K.H(K.Btn("New", SeqNew, "secondary", "plus", 34).M(0, 0, 6, 0),
                    K.Btn("Rename", SeqRename, "secondary", "rename", 34).M(0, 0, 6, 0),
                    K.Btn("Duplicate", SeqDuplicate, "secondary", "copy", 34).M(0, 0, 6, 0),
                    K.IconBtn("settings", SeqSettingsDialog, "Settings for this sequence", 34, "Muted", "Card2").M(0, 0, 6, 0),
                    K.IconBtn("trash", SeqDelete, "Delete this sequence", 34, "Muted", "Card2")).At(0, 2),
                seqNote.M(2, 8, 0, 0).At(1, 0, 1, 3));
        left.Add(bar.M(18, 16, 18, 12).At(0, 0));
        var sv = new ScrollViewer { Content = seqList, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false };
        left.Add(sv.M(12, 0, 8, 0).At(1, 0));
        Button seqMoreBtn = null;
        seqMoreBtn = K.Btn("More  ▾", () => K.Menu(("Jump to another step…", () => SeqAdd("goto")),
                                                    ("End the run here…", () => SeqAdd("stop")),
                                                    ("Bring a window to the front…", () => SeqAdd("focus")),
                                                    ("Alert (message and sound)…", () => SeqAdd("alert"))).Popup(seqMoreBtn), "secondary", "plus", 34);
        var add = new WrapPanel();
        foreach (UIElement kid in new UIElement[] { K.T("Add a step", 13, "Muted").M(0, 0, 12, 0),
                      K.Btn("Wait for image", () => SeqAdd("image"), "primary", "image", 34).M(0, 0, 6, 0),
                      K.Btn("Play recording", () => SeqAdd("play"), "secondary", "play", 34).M(0, 0, 6, 0),
                      K.Btn("Click", () => SeqAdd("click"), "secondary", "target", 34).M(0, 0, 6, 0),
                      K.Btn("Keys", () => SeqAdd("key"), "secondary", "keyboard", 34).M(0, 0, 6, 0),
                      K.Btn("Pause", () => SeqAdd("wait"), "secondary", "clock", 34).M(0, 0, 6, 0),
                      K.Btn("Repeat", () => SeqAdd("repeat"), "secondary", "loop", 34).M(0, 0, 6, 0),
                      seqMoreBtn })
            add.Children.Add(kid is FrameworkElement fe ? fe.M(fe.Margin.Left, 0, fe.Margin.Right, 6) : kid);
        left.Add(add.M(18, 12, 18, 10).At(2, 0));
        // the run log sits under the steps, full width, so whole lines fit
        var logG = K.Grid("*", "auto,auto");
        var logHead = K.Grid("*,auto");
        logHead.Add(K.CardTitle("Run log", "log").At(0, 0),
                    K.H(K.Btn("Open file", LogOpenFile, "secondary", "log", 28).M(0, 0, 6, 0), K.Btn("All logs", () => OpenFolder(LogDir), "secondary", "folder", 28))
                     .M(0, 10, 14, 0).At(0, 1));
        logG.Add(logHead.At(0, 0));
        seqLogBox = LogBox();
        seqLogBox.Height = 150;
        seqLogBox.SetResourceReference(BackgroundProperty, "Card2");
        seqLogBox.Tip("Click a line to jump to its step; double-click to edit that step");
        logG.Add(new Border { CornerRadius = new CornerRadius(10), ClipToBounds = true, Child = seqLogBox }.M(14, 0, 14, 14).At(1, 0));
        var leftCol = K.Grid("*", "*,auto");
        leftCol.Add(K.Card(left).M(0, 0, 12, 12).At(0, 0), K.Card(logG).M(0, 0, 12, 0).At(1, 0));
        page.Add(leftCol.At(0, 0));

        // ---- right: run + help
        var run = K.Grid("*", "auto,auto,auto,auto,auto,auto,auto,auto");
        run.Add(K.CardTitle("Run", "play").At(0, 0));
        seqRunBtn = K.Btn("", () => ToggleSequence(), "primary", "play", 48);
        seqRunBtn.FontSize = 15;
        seqRunBtn.Focusable = false;                  // replayed keys must never land on Run / Stop
        run.Add(seqRunBtn.M(18, 2, 18, 14).At(1, 0));
        seqLoopSeg = new Segmented(new[] { "Once", "Repeat", "Forever" }, seq.Loops == 0 ? "Forever" : seq.Loops == 1 ? "Once" : "Repeat",
                                   _ => SeqLoopChanged(), 64);
        seqLoopCount = new Stepper(seq.Loops > 1 ? seq.Loops : Math.Max(2, seq.LoopCount), 1, 2, 1000000, true, 52, _ => SeqLoopChanged());
        seqLoopCountRow = K.H(seqLoopCount, K.T("  times", 13, "Muted"));
        seqLoopHint = K.T("", 12, "Faint", null, true);
        run.Add(K.V(K.T("Whole sequence", 12, "Muted").M(0, 0, 0, 6), seqLoopSeg, seqLoopCountRow.M(0, 8, 0, 0), seqLoopHint.M(0, 8, 0, 0))
                 .M(18, 0, 18, 12).At(2, 0));
        seqHotkeySwitch = K.Switch("", cfg.SeqHotkey, v =>
        {
            cfg.SeqHotkey = v;
            RefreshButtons();
        });
        run.Add(seqHotkeySwitch.M(18, 0, 18, 10).At(3, 0));
        seqStaySwitch = K.Switch("Keep this window open while it runs", seq.Show == "stay", v =>
        {
            if (seqLoading) return;
            seq.Show = v ? "stay" : "";
            SeqSave();
            SeqNoteShow();
        });
        seqStaySwitch.Tip("The step that is running lights up in the list and shows what it is doing. Best with the game on your other monitor.");
        run.Add(seqStaySwitch.M(18, 0, 18, 10).At(4, 0));
        run.Add(K.Btn("Settings for this sequence", SeqSettingsDialog, "secondary", "settings", 32).Align(HorizontalAlignment.Left).M(18, 0, 18, 12).At(5, 0));
        seqStatus = K.T("", 13, "Text", FontWeights.SemiBold, true);
        seqResult = K.T("Not run yet.", 12, "Muted", null, true);
        run.Add(K.Box(K.V(seqStatus, seqResult), "Card2", 10, 12, 10).M(18, 0, 18, 18).At(6, 0));

        var help = K.Grid("*", "auto,auto");
        help.Add(K.CardTitle("How it works", "info").At(0, 0));
        var tips = new StackPanel();
        foreach (var t in new[]
                 {
                     "Steps run from top to bottom.",
                     "“Wait for image” holds everything until that picture is on screen, clicks it, then lets the next step start - however long that takes.",
                     "“Play recording” plays one from your Library. Record each part of the run separately (place units, walk to the gate…).",
                     "“Repeat” runs the steps above it again - e.g. the last 3 steps, 6 times.",
                     "A recording step can watch for an image the whole time it plays, and only move on once that image has been seen.",
                     "A recording step can play its keyboard part only (or its mouse part only), and auto click spots you pick the whole time it plays.",
                     "“Click” clicks one or more spots a number of times (or for a number of seconds), then carries on. It can also press a key, click wherever a picture is, or only click while a picture is on screen.",
                     "“Keys” presses a key or types text. Under “More”: jump to another step (always, or only if a picture is on screen), end the run, bring a window to the front, show an alert.",
                     "Any step can run only some of the time: by chance, only on the first full run, or every Nth run (the pencil, “When it runs”).",
                     "The cog also has: what to do when a step fails (stop, start again, carry on), a time limit, and Always-watch - pictures looked for the whole run (click a pop-up away, restart on “You died”).",
                     "Every step has its own name, wait before, wait after and on/off switch (the pencil). The cog sets the gap between all steps.",
                     "The small play button on a step starts the run from that step - handy for testing the end without the start.",
                     "Recordings are read from the Library when the sequence starts, so re-recording one updates every sequence that uses it.",
                 })
            tips.Children.Add(K.T("•  " + t, 12, "Muted", null, true).M(0, 0, 0, 7));
        help.Add(tips.M(18, 0, 18, 12).At(1, 0));

        var right = K.Grid("*", "auto,auto");
        right.Add(K.Card(run).M(0, 0, 0, 12).At(0, 0), K.Card(help).At(1, 0));
        page.Add(Scroll(right).At(0, 1));

        SeqLoopChanged();
        SeqRefresh();
        return page;
    }

    // ================================================================ files
    List<string> SeqFiles()
    {
        try { return Directory.EnumerateFiles(SeqDir, "*.json").OrderByDescending(p => File.GetLastWriteTimeUtc(p)).ToList(); }
        catch (IOException) { return new List<string>(); }
    }

    void SeqLoadInitial()
    {
        seq = null;
        seqPath = null;
        var want = string.IsNullOrEmpty(cfg.LastSequence) ? null : Path.Combine(SeqDir, Path.GetFileName(cfg.LastSequence));
        foreach (var p in new[] { want }.Concat(SeqFiles()))
        {
            if (p == null || !File.Exists(p)) continue;
            try
            {
                seq = Sequence.Load(p);
                seqPath = p;
                break;
            }
            catch { }
        }
        seq ??= new Sequence { Name = "My sequence" };
        SeqAdopt(seq);
    }

    /// A sequence that has never had its own repeat choice takes the one the app used to keep for all of them.
    void SeqAdopt(Sequence q)
    {
        if (q.Loops >= 0) return;
        q.Loops = Math.Max(0, cfg.SeqLoops);
        q.LoopCount = Math.Max(2, q.Loops > 1 ? q.Loops : cfg.SeqRepeatCount);
    }

    void SeqSave()
    {
        try
        {
            seqPath ??= Files.UniquePath(SeqDir, Files.Slug(seq.Name) + ".json");
            seq.Save(seqPath);
            cfg.LastSequence = Path.GetFileName(seqPath);
        }
        catch (Exception e) { Toast($"Couldn't save the sequence: {e.Message}", "error", 6000); }
    }

    /// A recording was renamed in the Library: point every sequence that plays it at the new file.
    void SeqRecordingRenamed(string oldFile, string newFile)
    {
        bool same(SeqStep st) => st.Kind == "play" && string.Equals(st.Recording, oldFile, StringComparison.OrdinalIgnoreCase);
        foreach (var p in SeqFiles())
        {
            if (seqPath != null && SameFile(p, seqPath)) continue;
            try
            {
                var other = Sequence.Load(p);
                if (!other.Steps.Any(same)) continue;
                foreach (var st in other.Steps.Where(same)) st.Recording = newFile;
                other.Save(p);
            }
            catch { }
        }
        if (seq != null && seq.Steps.Any(same))
        {
            foreach (var st in seq.Steps.Where(same)) st.Recording = newFile;
            SeqSave();
        }
        seqShown = -2;
        if (seqPickBtn != null) SeqRefresh();
    }

    void SeqChanged()
    {
        SeqSave();
        SeqRefresh();
        RefreshButtons();
    }

    void SeqPick()
    {
        if (SeqBusy()) return;
        var items = new List<(string text, Action act)>();
        foreach (var p in SeqFiles())
        {
            string name;
            try { name = Sequence.Load(p).Name; }
            catch { continue; }
            var path = p;
            items.Add(((SameFile(path, seqPath) ? "✓  " : "     ") + name, () => SeqOpen(path)));
        }
        if (items.Count > 0) items.Add(("-", null));
        items.Add(("New sequence…", SeqNew));
        K.Menu(items.ToArray()).Popup(seqPickBtn);
    }

    void SeqOpen(string path)
    {
        try
        {
            seq = Sequence.Load(path);
            SeqAdopt(seq);
            seqPath = path;
            cfg.LastSequence = Path.GetFileName(path);
        }
        catch (Exception e)
        {
            Toast($"Couldn't open that sequence: {e.Message}", "error", 6000);
            return;
        }
        SeqRefresh();
        RefreshButtons();
    }

    static string SeqNameError(string t) => string.IsNullOrWhiteSpace(t) ? "Give it a name." : null;

    void SeqNew()
    {
        if (SeqBusy()) return;
        var name = Dialog.AskString(TopWindow, "New sequence", "Name it after what it does:", "", SeqNameError, "Create");
        if (name == null) return;
        seq = new Sequence { Name = name.Trim() };
        SeqAdopt(seq);
        seqPath = null;
        SeqChanged();
    }

    void SeqRename()
    {
        if (SeqBusy()) return;
        var name = Dialog.AskString(TopWindow, "Rename sequence", "New name:", seq.Name, SeqNameError, "Rename");
        if (name == null) return;
        seq.Name = name.Trim();
        SeqChanged();
    }

    void SeqDuplicate()
    {
        if (SeqBusy()) return;
        seq = seq.Copy();
        seq.Name += " (copy)";
        seqPath = null;
        SeqChanged();
        Toast("Duplicated - you're now editing the copy", "ok");
    }

    void SeqDelete()
    {
        if (SeqBusy()) return;
        if (!Dialog.Confirm(TopWindow, "Delete sequence?", $"“{seq.Name}” will be moved to data\\deleted. The recordings it uses are not touched.", "Delete", true)) return;
        try
        {
            if (seqPath != null && File.Exists(seqPath))
            {
                Directory.CreateDirectory(DelDir);
                File.Move(seqPath, Files.UniquePath(DelDir, Path.GetFileName(seqPath)));
            }
        }
        catch (Exception e)
        {
            Toast($"Couldn't delete it: {e.Message}", "error", 6000);
            return;
        }
        cfg.LastSequence = "";
        SeqLoadInitial();
        SeqRefresh();
        RefreshButtons();
    }

    // ================================================================ the list of steps
    Files.Summary SeqScan(string path)
    {
        var mt = File.GetLastWriteTimeUtc(path);
        if (seqScanCache.TryGetValue(path, out var hit) && hit.mtime == mt) return hit.sum;
        var sum = Files.Scan(path);
        seqScanCache[path] = (mt, sum);
        return sum;
    }

    void SeqRefresh()
    {
        if (seqPickBtn == null || seq == null) return;
        K.SetBtnContent(seqPickBtn, seq.Name + "   ▾", "loop");
        SeqSettingsShow();
        seqList.Children.Clear();
        seqRowBoxes.Clear();
        seqRowLive.Clear();
        seqRowRuns.Clear();
        seqShown = -2;
        if (seq.Steps.Count == 0)
        {
            seqList.Children.Add(K.V(K.Icon("loop", 40, "Faint").Align(HorizontalAlignment.Center),
                                     K.T("No steps yet", 16, "Text", FontWeights.SemiBold).Align(HorizontalAlignment.Center).M(0, 12, 0, 4),
                                     K.T("Add “Wait for image”, then “Play recording” - the recording won't start until the picture has been clicked.",
                                         13, "Muted", null, true).Align(HorizontalAlignment.Center)).M(40, 60, 40, 0));
            return;
        }
        var covered = new HashSet<int>();
        for (int i = 0; i < seq.Steps.Count; i++)
            if (seq.Steps[i].Kind == "repeat" && !seq.Steps[i].Off)
                for (int j = Math.Max(0, i - seq.Steps[i].Back); j <= i; j++) covered.Add(j);
        for (int i = 0; i < seq.Steps.Count; i++)
        {
            var row = SeqRow(i, covered.Contains(i));
            seqRowBoxes.Add(row);
            seqList.Children.Add(row.M(0, 0, 6, 6));
        }
    }

    Border SeqRow(int i, bool inRepeat)
    {
        var s = seq.Steps[i];
        string icon = "loop", color = "Accent", soft = "AccentSoft", title = "", sub = "";
        bool bad = false;
        FrameworkElement thumb = null;
        if (s.Kind == "image")
        {
            icon = "image";
            color = "Green";
            soft = "GreenSoft";
            bool none = s.Image.Length == 0;
            bad = none || !File.Exists(Path.Combine(ImgDir, s.Image));
            bool key = s.Key.Length > 0;
            title = none ? "Wait for an image - none chosen yet"
                  : bad ? $"Wait for {s.Image} - this image is missing"
                  : s.Gone ? $"Wait until {s.Image} has gone"
                  : s.Click && key ? $"Wait for {s.Image}, then click it and press {Keys.Pretty(s.Key)}"
                  : s.Click ? $"Wait for {s.Image}, then click it"
                  : key ? $"Wait for {s.Image}, then press {Keys.Pretty(s.Key)}" : $"Wait for {s.Image}";
            sub = s.Timeout <= 0 ? "No time limit - waits as long as it takes"
                                 : $"Gives up after {Fmt.Num(s.Timeout)} s, then {(s.OnFail == "skip" ? "skips to the next step" : "stops the sequence")}";
            if (s.Click && s.Button != "left") sub += $"  ·  {s.Button} click";
            if (s.Click && (s.Dx != 0 || s.Dy != 0)) sub += $"  ·  clicks {s.Dx:+0;-0;0}, {s.Dy:+0;-0;0} px off centre";
            if (s.Or.Count > 0) sub += $"  ·  also moves on if {string.Join(" or ", s.Or)} shows";
            if (s.Or.Any(o => !File.Exists(Path.Combine(ImgDir, o)))) bad = true;
            if (!bad)
            {
                var (img, _) = ThumbImage(s.Image, 84, 34);
                thumb = img;
            }
        }
        else if (s.Kind == "play")
        {
            icon = "play";
            var path = Path.Combine(RecDir, s.Recording);
            bool none = s.Recording.Length == 0;
            bad = none || !File.Exists(path);
            if (bad)
            {
                title = none ? "Play a recording - none chosen yet" : $"Play {s.Recording} - this recording is missing";
                sub = "Edit this step and pick a recording from the Library";
            }
            else
            {
                var sum = SeqScan(path);
                bool watch = s.Until.Length > 0;
                title = $"Play “{(sum.Name.Length > 0 ? sum.Name : s.Recording)}”" + (s.Times > 1 && !(watch && s.UntilLoop) && s.ForSecs <= 0 ? $"   × {s.Times}" : "")
                        + (watch ? $"   until {s.Until} shows" : "");
                sub = $"{sum.Actions:N0} actions  ·  {Fmt.Dur(sum.Duration)}" + (s.Speed > 0 ? $"  ·  {Fmt.Num(s.Speed)}× speed" : "")
                      + (s.From > 0 || s.To > 0 ? $"  ·  from {Fmt.Num(s.From)} s" + (s.To > 0 ? $" to {Fmt.Num(s.To)} s" : "") : "")
                      + (s.ForSecs > 0 && !watch ? $"  ·  replays for {Fmt.Num(s.ForSecs)} s" : "")
                      + (s.SpeedJitter > 0 ? $"  ·  speed ± {Fmt.Num(s.SpeedJitter)}%" : "")
                      + (s.Only.Length > 0 ? $"  ·  {Only.Name(s.Only)}" : "")
                      + (s.Ac ? "  ·  auto-clicks " + SeqClicksWhere(s) + $" every {SeqEvery(s.AcEvery)}" : "")
                      + (watch ? "  ·  " + SeqWatchText(s) : $"  ·  {s.Recording}");
                if (watch && !File.Exists(Path.Combine(ImgDir, s.Until)))
                {
                    bad = true;
                    title = $"Play {s.Recording} - the image it watches for ({s.Until}) is missing";
                }
                else if (watch)
                {
                    var (img, _) = ThumbImage(s.Until, 84, 34);
                    thumb = img;
                }
            }
        }
        else if (s.Kind == "click")
        {
            icon = "target";
            string howLong = s.ClickForever ? $"until {s.More.Until} shows" : s.ClickSecs > 0 ? $"for {Fmt.Num(s.ClickSecs)} s" : $"{s.Clicks:N0} time{(s.Clicks == 1 ? "" : "s")}";
            title = (s.More.KeyOnly ? $"Press {Keys.Pretty(s.More.Key)}" : $"Click {SeqClicksWhere(s)}") + $"   ·   {howLong}";
            sub = (s.More.KeyOnly ? "Every " : $"{(s.AcButton == "double" ? "Double" : s.AcButton == "right" ? "Right" : "Left")} click every ") + $"{SeqEvery(s.AcEvery)}, then carries on";
            var extra = ClickMoreText(s.More, false);
            if (extra.Length > 0) sub += "  ·  " + extra;
            bad = s.More.Images().Any(n => !File.Exists(Path.Combine(ImgDir, n)));
            if (s.More.Target.Length > 0 && !bad)
            {
                var (img, _) = ThumbImage(s.More.Target, 84, 34);
                thumb = img;
            }
        }
        else if (s.Kind == "key")
        {
            icon = "keyboard";
            title = s.KeyMode == "type" ? $"Type “{(s.Text.Length > 40 ? s.Text[..40] + "…" : s.Text)}”"
                  : $"Press {Keys.Pretty(s.Combo)}" + (s.KeyTimes > 1 ? $"   × {s.KeyTimes}" : "");
            sub = s.KeyMode == "type" ? $"{s.Text.Length} characters, {Fmt.Num(s.TypeDelay)} ms each"
                : (s.KeyTimes > 1 ? $"One every {SeqEvery(s.KeyEvery)}, each held {Fmt.Num(s.KeyHold)} ms" : $"Held {Fmt.Num(s.KeyHold)} ms") + ", then carries on";
            bad = s.KeyMode == "type" ? s.Text.Length == 0 : ComboError(s.Combo).Length > 0;
        }
        else if (s.Kind is "goto" or "stop")
        {
            icon = s.Kind == "goto" ? "redo" : "stop";
            color = "Amber";
            soft = "AmberSoft";
            string cond = s.IfMode == "always" ? "" : $"   if {s.IfImage} is {(s.IfMode == "hidden" ? "not " : "")}on screen";
            if (s.Kind == "goto")
            {
                title = $"Jump to step {s.GoTo}{cond}";
                sub = (s.IfMode == "always" ? "Always" : "Otherwise carries on with the next step") + (s.MaxJumps > 0 ? $"  ·  at most {s.MaxJumps} time{(s.MaxJumps == 1 ? "" : "s")} per full run" : "");
                bad = s.GoTo > seq.Steps.Count || s.GoTo == i + 1;
            }
            else
            {
                title = (s.StopMode == "loop" ? "End this full run and start the next" : "End the run here") + cond;
                sub = s.IfMode == "always" ? "Steps below this one are not reached" : "Otherwise carries on with the next step";
            }
            if (s.IfMode != "always")
            {
                if (s.IfImage.Length == 0 || !File.Exists(Path.Combine(ImgDir, s.IfImage))) bad = true;
                else
                {
                    var (img, _) = ThumbImage(s.IfImage, 84, 34);
                    thumb = img;
                }
            }
        }
        else if (s.Kind == "focus")
        {
            icon = "expand";
            title = s.WindowTitle.Length > 0 ? $"Bring “{s.WindowTitle}” to the front" : "Bring a window to the front - none named yet";
            sub = s.OnFail == "stop" ? "Stops the run if that window isn't open" : "Carries on if that window isn't open";
            bad = s.WindowTitle.Length == 0;
        }
        else if (s.Kind == "alert")
        {
            icon = "info";
            color = "Amber";
            soft = "AmberSoft";
            title = "Alert:  " + (s.Message.Length > 0 ? s.Message : "(no message)");
            sub = s.Sound ? "Shows the message and plays a sound, then carries on" : "Shows the message, then carries on";
        }
        else if (s.Kind == "wait")
        {
            icon = "clock";
            color = "Amber";
            soft = "AmberSoft";
            title = s.Random > 0 ? $"Pause for {Fmt.Num(s.Seconds)} to {Fmt.Num(s.Seconds + s.Random)} s" : $"Pause for {Fmt.Num(s.Seconds)} s";
            sub = s.Random > 0 ? "A different length every time, then carries on" : "Does nothing, then carries on";
        }
        else
        {
            int first = Math.Max(0, i - s.Back);
            bad = i == 0;
            title = bad ? "Repeat - needs steps above it"
                        : $"Repeat {(i - first == 1 ? $"step {first + 1}" : $"steps {first + 1} to {i}")}   ·   {s.Count} times in total";
            sub = "Goes back up and runs them again until they've run that many times";
        }

        // spacing, your own name, and the off switch apply to every kind of step
        if (!s.IsControl)
        {
            if (s.Before > 0) sub = $"waits {Fmt.Num(s.Before)} s first  ·  " + sub;
            if (s.AfterStep > 0) sub += $"  ·  then waits {Fmt.Num(s.AfterStep)} s";
        }
        if (s.When == "first") sub = "first full run only  ·  " + sub;
        else if (s.When == "notfirst") sub = "not on the first full run  ·  " + sub;
        else if (s.When == "every") sub = $"every {s.EveryN}{(s.EveryN == 2 ? "nd" : s.EveryN == 3 ? "rd" : "th")} full run  ·  " + sub;
        if (s.Chance < 100) sub = $"{Fmt.Num(s.Chance)}% of the time  ·  " + sub;
        if (s.Label.Length > 0)
        {
            sub = title + "  ·  " + sub;
            title = s.Label;
        }
        if (s.Off) bad = false;                                    // a switched-off step can't hold anything up

        var g = K.Grid("auto,auto,auto,*,auto,auto");
        var mark = new Border { Width = 3, CornerRadius = new CornerRadius(2), Margin = new Thickness(6, 9, 0, 9) };
        if (inRepeat) mark.SetResourceReference(Border.BackgroundProperty, "Accent");
        g.Add(mark.At(0, 0));
        var num = K.T((i + 1).ToString(CultureInfo.InvariantCulture), 12, "Faint", FontWeights.SemiBold);
        num.Width = 20;
        num.TextAlignment = TextAlignment.Center;
        g.Add(num.M(6, 0, 6, 0).At(0, 1));
        var ic = K.Box(K.Icon(icon, 16, bad ? "Red" : color).Align(HorizontalAlignment.Center), bad ? "RedSoft" : soft, 10);
        ic.Width = ic.Height = 38;
        g.Add(ic.M(0, 9, 12, 9).At(0, 2));
        var tt = K.T(title, 13.5, bad ? "Red" : "Text", FontWeights.SemiBold);
        tt.TextTrimming = TextTrimming.CharacterEllipsis;
        var st = K.T(sub, 12, "Muted");
        st.TextTrimming = TextTrimming.CharacterEllipsis;
        var live = K.T("", 12, "Accent", FontWeights.SemiBold);      // filled in while this step is the one running
        live.TextTrimming = TextTrimming.CharacterEllipsis;
        live.Visibility = Visibility.Collapsed;
        seqRowLive.Add(live);
        var runs = K.Chip("", "Accent", "AccentSoft", 10, false);
        runs.Visibility = Visibility.Collapsed;
        runs.Margin = new Thickness(8, 0, 0, 0);
        seqRowRuns.Add(runs);
        var head = K.Grid("*,auto,auto");                          // the title gives way (…) so the chips always fit
        head.Add(tt.At(0, 0));
        if (s.Off) head.Add(K.Chip("OFF", "Muted", "Card", 10, false).M(8, 0, 0, 0).At(0, 1));
        head.Add(runs.At(0, 2));
        g.Add(K.V(head, st.M(0, 2, 0, 0), live.M(0, 3, 0, 0)).Align(HorizontalAlignment.Stretch).At(0, 3));
        if (thumb != null) g.Add(new Border { Child = thumb, Margin = new Thickness(10, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center }.At(0, 4));
        int idx = i;
        var up = K.IconBtn("up", () => SeqMove(idx, -1), "Move up", 30);
        var down = K.IconBtn("down", () => SeqMove(idx, 1), "Move down", 30);
        up.IsEnabled = i > 0;
        down.IsEnabled = i < seq.Steps.Count - 1;
        g.Add(K.H(K.IconBtn("play", () => ToggleSequence(null, idx), "Run the sequence starting from this step", 30, "Accent"),
                  K.IconBtn(s.Off ? "close" : "check", () => SeqToggleOff(idx), s.Off ? "Switched off - click to switch this step back on" : "Switched on - click to make the sequence skip this step",
                            30, s.Off ? "Faint" : "Green"),
                  up, down, K.IconBtn("edit", () => SeqEdit(idx), "Edit this step", 30),
                  K.IconBtn("copy", () => SeqCopyStep(idx), "Duplicate this step", 30),
                  K.IconBtn("trash", () => SeqRemove(idx), "Remove this step", 30)).M(6, 0, 8, 0).At(0, 5));
        var box = K.Box(g, "Card2", 12);
        box.BorderThickness = new Thickness(1.5);
        box.BorderBrush = Brushes.Transparent;
        if (s.Off) g.Opacity = 0.5;
        box.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2) SeqEdit(idx);
        };
        return box;
    }

    void SeqToggleOff(int i)
    {
        if (i < 0 || i >= seq.Steps.Count || SeqBusy()) return;
        seq.Steps[i].Off = !seq.Steps[i].Off;
        SeqChanged();
    }

    /// Light up the step that is running (-1 = none) and bring it into view.
    void SeqHighlight(int i)
    {
        seqShown = i;
        for (int k = 0; k < seqRowBoxes.Count; k++)
        {
            bool on = k == i;
            seqRowBoxes[k].SetResourceReference(Border.BackgroundProperty, on ? "AccentSoft" : "Card2");
            if (on) seqRowBoxes[k].SetResourceReference(Border.BorderBrushProperty, "Accent");
            else seqRowBoxes[k].BorderBrush = Brushes.Transparent;
            if (k < seqRowLive.Count && !on) seqRowLive[k].Visibility = Visibility.Collapsed;
        }
        if (i >= 0 && i < seqRowBoxes.Count && IsVisible) seqRowBoxes[i].BringIntoView();
    }

    /// While a run is on: the running step says what it is doing, and every step shows how often it has run.
    void SeqLive(int i, string what)
    {
        if (i >= 0 && i < seqRowLive.Count)
        {
            seqRowLive[i].Text = "▶  " + what;
            seqRowLive[i].Visibility = Visibility.Visible;
        }
        var runs = runner.Runs;
        for (int k = 0; k < seqRowRuns.Count && k < runs.Length; k++)
        {
            int n = runs[k];
            if (n <= 0) continue;
            K.SetChip(seqRowRuns[k], seq != null && k < seq.Steps.Count && seq.Steps[k].Kind == "repeat" ? $"round {n}" : $"done ×{n}");
            seqRowRuns[k].Visibility = Visibility.Visible;
        }
    }

    // ================================================================ editing steps
    /// Steps can't be changed while a sequence runs (the list has to keep matching what is running).
    bool SeqBusy()
    {
        if (state == "idle") return false;
        Toast("Stop the run first, then change the sequence.", "warn");
        return true;
    }

    void SeqAdd(string kind)
    {
        if (SeqBusy()) return;
        var s = new SeqStep { Kind = kind };
        if (!SeqStepDialog(s, true, seq.Steps.Count)) return;
        seq.Steps.Add(s);
        SeqChanged();
    }

    void SeqEdit(int i)
    {
        if (i < 0 || i >= seq.Steps.Count || SeqBusy()) return;
        var s = seq.Steps[i].Copy();
        if (!SeqStepDialog(s, false, i)) return;
        seq.Steps[i] = s;
        SeqChanged();
    }

    /// Jump steps name their target by number: keep them pointing at the same step when steps move about.
    void SeqRetarget(Func<int, int> map)
    {
        foreach (var s in seq.Steps)
            if (s.Kind == "goto") s.GoTo = Math.Clamp(map(s.GoTo), 1, Math.Max(1, seq.Steps.Count));
    }

    void SeqMove(int i, int dir)
    {
        int j = i + dir;
        if (i < 0 || i >= seq.Steps.Count || j < 0 || j >= seq.Steps.Count || SeqBusy()) return;
        (seq.Steps[i], seq.Steps[j]) = (seq.Steps[j], seq.Steps[i]);
        SeqRetarget(n => n == i + 1 ? j + 1 : n == j + 1 ? i + 1 : n);
        SeqChanged();
    }

    void SeqCopyStep(int i)
    {
        if (i < 0 || i >= seq.Steps.Count || SeqBusy()) return;
        seq.Steps.Insert(i + 1, seq.Steps[i].Copy());
        SeqRetarget(n => n > i + 1 ? n + 1 : n);
        SeqChanged();
    }

    void SeqRemove(int i)
    {
        if (i < 0 || i >= seq.Steps.Count || SeqBusy()) return;
        seq.Steps.RemoveAt(i);
        SeqRetarget(n => n > i + 1 ? n - 1 : n);                    // (a jump to the removed step now lands on the one after it)
        SeqChanged();
    }

    /// The settings every kind of step has: its own name, the waits around it, when it runs, and the on/off switch.
    (FrameworkElement view, Action apply) SeqCommon(SeqStep s, bool waits = true)
    {
        var label = K.Field(s.Label, 300);
        label.Tip("Optional. Shown in the list instead of the automatic title, e.g. “Gate 3 walk”");
        var before = new Stepper(s.Before, 0.5, 0, 86400, false, 56);
        var after = new Stepper(s.Kind == "image" ? s.After : s.Pause, 0.5, 0, 86400, false, 56);
        var on = K.Switch("This step is switched on (off = the sequence skips it)", !s.Off);
        var g = K.Grid("auto,*", "auto,auto,auto,auto");
        FrameworkElement L(string t, int r) => K.T(t, 13, "Muted").M(0, 6, 16, 6).At(r, 0);
        g.Add(L("Name", 0), label.Align(HorizontalAlignment.Left).M(0, 4, 0, 4).At(0, 1));
        if (waits)
            g.Add(L("Wait before", 1), K.H(before, K.T("  seconds, then this step starts", 13, "Muted")).M(0, 4, 0, 4).At(1, 1),
                  L("Wait after", 2), K.H(after, K.T("  seconds, then the next step starts", 13, "Muted")).M(0, 4, 0, 4).At(2, 1));
        g.Add(on.M(0, 8, 0, 0).At(3, 0, 1, 2));

        // when it runs: every time, only some full runs, or by chance
        var when = new Segmented(new[] { "Every full run", "First run only", "Not the first", "Every Nth" },
                                 s.When switch { "first" => "First run only", "notfirst" => "Not the first", "every" => "Every Nth", _ => "Every full run" });
        var nth = new Stepper(s.EveryN, 1, 2, 1000000, true, 52);
        var nthRow = K.H(K.T("   N = ", 13, "Muted"), nth);
        void WhenSync() => nthRow.Visibility = when.Value == "Every Nth" ? Visibility.Visible : Visibility.Collapsed;
        when.Changed += _ => WhenSync();
        WhenSync();
        var chance = new Stepper(s.Chance, 5, 0, 100, false, 56);
        var wg = K.Grid("auto,*", "auto,auto");
        wg.Add(K.T("Runs on", 13, "Muted").M(0, 6, 16, 6).At(0, 0), K.H(when, nthRow).M(0, 4, 0, 4).At(0, 1),
               K.T("Chance", 13, "Muted").M(0, 6, 16, 6).At(1, 0),
               K.H(chance, K.T("  % of the time   (100 = always)", 13, "Muted")).M(0, 4, 0, 4).At(1, 1));
        var whenFold = Fold("When it runs", wg, s.When.Length > 0 || s.Chance < 100, "Run this step only on some full runs, or only some of the time at random");

        var view = K.V(K.Divider(false, 12), K.T("SPACING AND NAME", 11, "Faint", FontWeights.SemiBold).M(0, 0, 0, 4), g, whenFold.M(0, 6, 0, 0));
        return (view, () =>
        {
            s.Label = label.Text.Trim();
            s.Off = on.IsChecked != true;
            s.When = when.Value switch { "First run only" => "first", "Not the first" => "notfirst", "Every Nth" => "every", _ => "" };
            s.EveryN = (int)nth.Value;
            s.Chance = chance.Value;
            if (!waits) return;
            s.Before = before.Value;
            if (s.Kind == "image") s.After = after.Value;
            else s.Pause = after.Value;
        });
    }

    /// One line saying what a play step's watch does (for the dialog and the step list).
    static string SeqWatchText(SeqStep s)
    {
        if (s.Until.Length == 0) return "";
        var t = $"watches for {s.Until}: " + (s.UntilMode == "finish" ? "finishes the recording, then moves on" : "stops the recording the moment it shows");
        if (s.UntilClick && s.UntilKey.Length > 0) t += $", clicks it and presses {Keys.Pretty(s.UntilKey)}";
        else if (s.UntilClick) t += ", clicks it";
        else if (s.UntilKey.Length > 0) t += $", presses {Keys.Pretty(s.UntilKey)}";
        t += s.UntilLoop ? "  ·  replays until then" : "  ·  plays, then waits";
        if (s.UntilTimeout > 0) t += $"  ·  gives up after {Fmt.Num(s.UntilTimeout)} s";
        return t;
    }

    /// Set up "watch for a picture the whole time this recording plays".
    bool SeqWatchDialog(SeqStep s, Window owner)
    {
        var imgs = ImageNames();
        if (imgs.Count == 0)
        {
            Toast("Your image library is empty - snip the button or popup on the Images page first.", "warn", 5000);
            return false;
        }
        var d = new Dialog(owner, "Watch for an image while it plays",
                           "TinyRec keeps looking for this picture the whole time the recording plays. The sequence only moves on to the next step once it has been seen.", "image");
        var f = K.Grid("auto,*", "auto,auto,auto,auto,auto,auto,auto,auto,auto");
        FrameworkElement L(string t, int r) => K.T(t, 13, "Muted").M(0, 6, 16, 6).At(r, 0);
        var chosen = imgs.Contains(s.Until) ? s.Until : imgs[0];
        var prev = new Border { Width = 260, Height = 80, CornerRadius = new CornerRadius(8), HorizontalAlignment = HorizontalAlignment.Left };
        prev.SetResourceReference(Border.BackgroundProperty, "Card2");
        void ShowImg(string n)
        {
            chosen = n;
            var (img, _) = ThumbImage(n, 260, 80);
            prev.Child = img;
        }
        Button pick = null;
        pick = K.Btn(chosen, () => K.Menu(imgs.Select(n => (n, (Action)(() => { ShowImg(n); K.SetBtnContent(pick, n, "image"); }))).ToArray()).Popup(pick),
                     "secondary", "image", 32);
        var modeSeg = new Segmented(new[] { "Stop the recording", "Finish it first" }, s.UntilMode == "finish" ? "Finish it first" : "Stop the recording");
        var modeHint = K.T("", 12, "Faint", null, true);
        modeHint.MaxWidth = 380;
        var loopSeg = new Segmented(new[] { "Play it again", "Just wait" }, s.UntilLoop ? "Play it again" : "Just wait");
        var click = K.Switch("Click the picture", s.UntilClick);
        var keyBox = K.Field(s.UntilKey, 80);
        var conf = K.Slide(0.5, 1.0, s.UntilConf, null, 200);
        var confLab = K.T(s.UntilConf.ToString("0.00", CultureInfo.InvariantCulture), 13, "Text", FontWeights.SemiBold);
        conf.ValueChanged += (_, e) => confLab.Text = e.NewValue.ToString("0.00", CultureInfo.InvariantCulture);
        var limitSeg = new Segmented(new[] { "No limit", "Give up after" }, s.UntilTimeout > 0 ? "Give up after" : "No limit");
        var limit = new Stepper(s.UntilTimeout > 0 ? s.UntilTimeout : 120, 10, 1, 604800, false, 64);
        var limitRow = K.H(limit.M(10, 0, 0, 0), K.T("  seconds", 13, "Muted"));
        var failSeg = new Segmented(new[] { "Stop the sequence", "Move on anyway" }, s.OnFail == "skip" ? "Move on anyway" : "Stop the sequence");
        var failLab = L("If it never shows", 8);
        void Sync()
        {
            modeHint.Text = modeSeg.Value == "Finish it first"
                ? "The recording plays to its end, then the sequence moves on. If the picture is to be clicked, TinyRec looks for it again at that point."
                : "The recording is cut off right there (anything held down is let go), then the sequence moves on.";
            var lim = limitSeg.Value == "Give up after" ? Visibility.Visible : Visibility.Collapsed;
            limitRow.Visibility = lim;
            failSeg.Visibility = lim;
            failLab.Visibility = lim;
        }
        modeSeg.Changed += _ => Sync();
        limitSeg.Changed += _ => Sync();
        f.Add(L("Image", 0), pick.Align(HorizontalAlignment.Left).M(0, 4, 0, 4).At(0, 1),
              prev.M(0, 2, 0, 6).At(1, 1),
              L("When it shows", 2), modeSeg.M(0, 4, 0, 2).At(2, 1),
              modeHint.M(0, 0, 0, 6).At(3, 1),
              L("Recording ends first", 4), K.H(loopSeg, K.T("   until the picture shows", 12, "Faint")).M(0, 4, 0, 4).At(4, 1),
              L("Then", 5), K.H(click, K.T("      and / or press  ", 13, "Muted"), keyBox, K.T("   (blank = no key)", 12, "Faint")).M(0, 4, 0, 4).At(5, 1),
              L("Match strictness", 6), K.H(conf, confLab.M(10, 0, 0, 0)).M(0, 4, 0, 4).At(6, 1),
              L("Time limit", 7), K.H(limitSeg, limitRow).M(0, 4, 0, 4).At(7, 1),
              failLab, failSeg.M(0, 4, 0, 4).At(8, 1));
        ShowImg(chosen);
        Sync();
        d.Body.Children.Add(f);
        d.AddButton("Cancel", null);
        d.AddAction("Use this", () =>
        {
            var key = keyBox.Text.Trim().ToLowerInvariant();
            if (key.Length > 0 && !Keys.IsKnown(key))
            {
                d.Error.Text = $"“{key}” isn't a key name. Try e, space, enter, tab, f5, 1, up… or leave it blank.";
                return;
            }
            s.Until = chosen;
            s.UntilMode = modeSeg.Value == "Finish it first" ? "finish" : "stop";
            s.UntilLoop = loopSeg.Value == "Play it again";
            s.UntilClick = click.IsChecked == true;
            s.UntilKey = key;
            s.UntilConf = Math.Round(conf.Value, 2);
            s.UntilTimeout = limitSeg.Value == "No limit" ? 0 : limit.Value;
            s.OnFail = failSeg.Value.StartsWith("Move", StringComparison.Ordinal) ? "skip" : "stop";
            d.Finish(true);
        });
        return d.Run() is true;
    }

    bool SeqStepDialog(SeqStep s, bool isNew, int index) => s.Kind switch
    {
        "image" => SeqImageDialog(s, isNew),
        "play" => SeqPlayDialog(s, isNew),
        "wait" => SeqWaitDialog(s),
        "click" => SeqClicksDialog(s, TopWindow, false, isNew),
        "key" => SeqKeyDialog(s, isNew),
        "goto" => SeqGotoDialog(s, isNew, index),
        "stop" => SeqStopDialog(s, isNew),
        "focus" => SeqFocusDialog(s, isNew),
        "alert" => SeqAlertDialog(s, isNew),
        _ => SeqRepeatDialog(s, isNew, index),
    };

    static string SeqEvery(double ms) => ms >= 1000 ? $"{Fmt.Num(Math.Round(ms / 1000, 2))} s" : $"{Fmt.Num(Math.Round(ms))} ms";

    static string SeqClicksWhere(SeqStep s) =>
        s.More.Target.Length > 0 ? $"{s.More.Target} wherever it is" :
        s.AcPoints.Count == 0 ? "where the pointer is" : s.AcPoints.Count == 1 ? $"the spot at {s.AcPoints[0][0]}, {s.AcPoints[0][1]}" : $"{s.AcPoints.Count} spots" + (s.More.Order == "random" ? " at random" : s.More.Order == "pingpong" ? ", there and back" : " in turn");

    /// Pick spots on the real screen from inside a dialog: the dialogs step aside while the screen is dimmed.
    List<(int x, int y)> SeqPickSpots(Window from)
    {
        var moved = new List<(Window w, double left)>();
        for (var w = from; w != null && w != this && w != mini; w = w.Owner)
        {
            moved.Add((w, w.Left));
            w.Left = -30000;
        }
        try { return Hidden(() => Picker.Pick("Click each spot, then press Done", true)); }
        finally
        {
            foreach (var (w, left) in moved) w.Left = left;
            from.Activate();
        }
    }

    /// The clicks of a Click step, or the ones that go alongside a recording step. Changes `s` only when saved.
    bool SeqClicksDialog(SeqStep s, Window owner, bool alongside, bool isNew = false)
    {
        var d = new Dialog(owner, alongside ? "Auto click while it plays" : isNew ? "Click" : "Edit click step",
                           alongside ? "TinyRec clicks these spots at a steady pace for as long as this step runs, then stops when the step ends."
                                     : "Clicks one or more spots a number of times, then the sequence carries on.", "target");
        var pts = s.AcPoints.Select(p => (x: p[0], y: p[1])).ToList();
        var where = K.T("", 13, "Text", null, true);
        where.MaxWidth = 480;
        var hint = K.T("", 12, "Faint", null, true);
        hint.MaxWidth = 480;
        var every = new Stepper(s.AcEvery, 50, 1, 3600000, true, 72);
        var rate = K.T("", 13, "Muted");
        var button = new Segmented(new[] { "Left", "Right", "Double" }, s.AcButton == "right" ? "Right" : s.AcButton == "double" ? "Double" : "Left", null, 64);
        void WhereShow()
        {
            where.Text = pts.Count == 0 ? "No spots: it clicks wherever the pointer is at that moment (or on a picture, if you pick one under More click settings)."
                       : (pts.Count == 1 ? "1 spot:  " : $"{pts.Count} spots:  ") + string.Join("   ", pts.Take(8).Select(p => $"({p.x}, {p.y})")) + (pts.Count > 8 ? "   …" : "");
            hint.Text = !alongside ? ""
                      : pts.Count == 0 ? "The recording's own mouse moves decide where the clicks land. With “Keyboard only” they land wherever you leave the pointer."
                      : "Tip: set this step to “Keyboard only” so the recording's own mouse moves don't fight the clicks.";
            hint.Visibility = hint.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            RateShow();
        }
        var pick = K.Btn("Pick spots on screen", () =>
        {
            var got = SeqPickSpots(d);
            if (got.Count == 0) return;
            pts.AddRange(got);
            WhereShow();
        }, "primary", "target", 32);
        pick.Tip("The screen is dimmed and your clicks only mark spots - nothing underneath gets clicked");
        var clear = K.Btn("Clear", () => { pts.Clear(); WhereShow(); }, "secondary", "trash", 32);
        var undo = K.Btn("Remove last", () => { if (pts.Count > 0) pts.RemoveAt(pts.Count - 1); WhereShow(); }, "secondary", null, 32);
        void RateShow()
        {
            // moving to a different spot and clicking it "like a person" takes time; staying on one spot is quick
            bool dbl = button.Value == "Double";
            double cost = pts.Count > 1 ? (dbl ? 300 : 170) : (dbl ? 70 : 15);
            double real = Math.Max(every.Value, cost);
            rate.Text = $"  ms   ≈ {1000 / real:0.#} clicks a second" + (every.Value < cost ? $"  (the fastest it can go{(pts.Count > 1 ? " between spots" : "")})" : "");
        }
        every.Changed += _ => RateShow();
        button.Changed += _ => RateShow();
        var stopSeg = new Segmented(new[] { "Number of clicks", "Seconds", "Its picture shows" },
                                    s.ClickForever ? "Its picture shows" : s.ClickSecs > 0 ? "Seconds" : "Number of clicks");
        stopSeg.Tip("“Its picture shows” keeps clicking until the picture set under More click settings > Stop when is on screen");
        var (moreView, moreApply) = ClickMoreEditor(s.More, alongside);
        var moreFold = Fold("More click settings", moreView, !s.More.IsPlain,
                            "Order, random variation, bursts, hold, a key to press, click wherever a picture is, only click while a picture is on screen …");
        var count = new Stepper(s.Clicks, 1, 1, 100000000, true, 72);
        var secs = new Stepper(s.ClickSecs > 0 ? s.ClickSecs : 5, 1, 0.1, 86400, false, 64);
        var countRow = K.H(count.M(10, 0, 0, 0), K.T("  clicks", 13, "Muted"));
        var secsRow = K.H(secs.M(10, 0, 0, 0), K.T("  seconds", 13, "Muted"));
        void StopSync()
        {
            countRow.Visibility = stopSeg.Value == "Number of clicks" ? Visibility.Visible : Visibility.Collapsed;
            secsRow.Visibility = stopSeg.Value == "Seconds" ? Visibility.Visible : Visibility.Collapsed;
        }
        stopSeg.Changed += _ => StopSync();
        StopSync();
        WhereShow();

        var g = K.Grid("auto,*", "auto,auto,auto,auto,auto");
        FrameworkElement L(string t, int r) => K.T(t, 13, "Muted").M(0, 6, 16, 6).At(r, 0);
        g.Add(L("Where", 0), K.V(where, K.H(pick, undo.M(6, 0, 6, 0), clear).M(0, 8, 0, 0), hint.M(0, 8, 0, 0)).M(0, 6, 0, 8).At(0, 1),
              L("Click every", 1), K.H(every, rate).M(0, 4, 0, 4).At(1, 1),
              L("Button", 2), button.Align(HorizontalAlignment.Left).M(0, 4, 0, 4).At(2, 1));
        if (!alongside) g.Add(L("Stop after", 3), K.H(stopSeg, countRow, secsRow).M(0, 4, 0, 4).At(3, 1));
        d.Body.Children.Add(g);
        d.Body.Children.Add(moreFold.M(0, 6, 0, 0));
        Action commonApply = null;
        if (!alongside)
        {
            var (commonView, apply) = SeqCommon(s);
            commonApply = apply;
            d.Body.Children.Add(commonView);
        }
        d.AddButton("Cancel", null);
        d.AddAction(alongside ? "Use this" : isNew ? "Add step" : "Save", () =>
        {
            var more = new ClickMore();
            moreApply(more);
            if (more.Key.Length > 0 && !KeyNameOk(more.Key)) { d.Error.Text = $"“{more.Key}” isn't a key name. Try e, space, enter, f …"; return; }
            if (!alongside && stopSeg.Value == "Its picture shows" && more.Until.Length == 0)
            {
                d.Error.Text = "Open “More click settings” and pick the picture under “Stop when”.";
                return;
            }
            s.More = more;
            s.AcPoints = pts.Select(p => new[] { p.x, p.y }).ToList();
            s.AcEvery = Math.Max(1, every.Value);
            s.AcButton = button.Value == "Right" ? "right" : button.Value == "Double" ? "double" : "left";
            if (alongside) s.Ac = true;
            else
            {
                s.Clicks = Math.Max(1, (int)count.Value);
                s.ClickSecs = stopSeg.Value == "Seconds" ? Math.Max(0.1, secs.Value) : 0;
                s.ClickForever = stopSeg.Value == "Its picture shows";
                commonApply();
            }
            d.Finish(true);
        });
        return d.Run() is true;
    }

    bool SeqImageDialog(SeqStep s, bool isNew)
    {
        var imgs = ImageNames();
        if (imgs.Count == 0)
        {
            Toast("Your image library is empty - snip the button or popup on the Images page first.", "warn", 5000);
            ShowPage("images");
            return false;
        }
        var d = new Dialog(TopWindow, isNew ? "Wait for an image" : "Edit image step",
                           "The sequence holds here until this picture is on screen. Nothing below it starts before that.", "image");
        var f = K.Grid("auto,*", "auto,auto,auto,auto,auto,auto,auto,auto,auto,auto,auto");
        FrameworkElement L(string t, int r) => K.T(t, 13, "Muted").M(0, 6, 16, 6).At(r, 0);
        var chosen = imgs.Contains(s.Image) ? s.Image : imgs[0];
        var prev = new Border { Width = 260, Height = 72, CornerRadius = new CornerRadius(8), HorizontalAlignment = HorizontalAlignment.Left };
        prev.SetResourceReference(Border.BackgroundProperty, "Card2");
        var cap = K.T("", 11, "Muted");
        var orList = s.Or.Where(imgs.Contains).ToList();
        var orText = K.T("", 12, "Muted", null, true);
        orText.MaxWidth = 250;
        void OrShow() => orText.Text = orList.Count == 0 ? "none" : string.Join(", ", orList);
        void ShowImg(string n)
        {
            chosen = n;
            var (img, c) = ThumbImage(n, 260, 72);
            prev.Child = img;
            cap.Text = c;
            if (orList.Remove(n)) OrShow();
        }
        Button pick = null;
        pick = K.Btn(chosen, () => K.Menu(imgs.Select(n => (n, (Action)(() => { ShowImg(n); K.SetBtnContent(pick, n, "image"); }))).ToArray()).Popup(pick),
                     "secondary", "image", 32);
        var doSeg = new Segmented(new[] { "Click it", "Press a key", "Click + key", "Only wait" },
                                  s.Click && s.Key.Length > 0 ? "Click + key" : s.Click ? "Click it" : s.Key.Length > 0 ? "Press a key" : "Only wait");
        var btnSeg = new Segmented(new[] { "Left", "Right", "Double" }, s.Button == "right" ? "Right" : s.Button == "double" ? "Double" : "Left");
        var btnLab = L("Click with", 3);
        var keyBox = K.Field(s.Key, 90);
        var keyRow = K.H(keyBox, K.T("   e, space, enter, f5, 1 …", 12, "Faint"));
        var keyLab = L("Key to press", 4);
        Button orAdd = null;
        orAdd = K.Btn("Add", () =>
        {
            var free = imgs.Where(n => n != chosen && !orList.Contains(n)).ToList();
            if (free.Count == 0) return;
            K.Menu(free.Select(n => (n, (Action)(() => { orList.Add(n); OrShow(); }))).ToArray()).Popup(orAdd);
        }, "secondary", "plus", 28);
        var orRow = K.H(orAdd, K.IconBtn("close", () => { orList.Clear(); OrShow(); }, "Clear the list", 28).M(4, 0, 8, 0), orText)
                     .Tip("If one of these pictures shows instead (for example the screen that means the round is over), the sequence moves on without clicking anything");
        OrShow();
        var conf = K.Slide(0.5, 1.0, s.Conf, null, 200);
        var confLab = K.T(s.Conf.ToString("0.00", CultureInfo.InvariantCulture), 13, "Text", FontWeights.SemiBold);
        conf.ValueChanged += (_, e) => confLab.Text = e.NewValue.ToString("0.00", CultureInfo.InvariantCulture);
        var limitSeg = new Segmented(new[] { "No limit", "Give up after" }, s.Timeout > 0 ? "Give up after" : "No limit");
        var limit = new Stepper(s.Timeout > 0 ? s.Timeout : 60, 5, 1, 86400, false, 60);
        var limitRow = K.H(limit.M(10, 0, 0, 0), K.T("  seconds", 13, "Muted"));
        var failSeg = new Segmented(new[] { "Stop the sequence", "Skip this step" }, s.OnFail == "skip" ? "Skip this step" : "Stop the sequence");
        var failLab = K.T("If it never shows", 13, "Muted").M(0, 6, 16, 6).At(7, 0);
        var offX = new Stepper(s.Dx, 5, -5000, 5000, true, 52);
        var offY = new Stepper(s.Dy, 5, -5000, 5000, true, 52);
        var offRow = K.H(offX, K.T("  right     ", 13, "Muted"), offY, K.T("  down   (pixels from the centre; minus = left / up)", 12, "Faint"));
        var offLab = L("Click offset", 8);
        var (commonView, commonApply) = SeqCommon(s);
        var gone = K.Switch("Wait until it has GONE instead (a loading screen, a countdown) - nothing is clicked", s.Gone);
        var doLab = L("When it shows", 2);
        var orLab = L("Or move on if", 9);
        gone.Checked += (_, _) => Sync();
        gone.Unchecked += (_, _) => Sync();
        void Sync()
        {
            bool away = gone.IsChecked == true;
            doSeg.Visibility = doLab.Visibility = orRow.Visibility = orLab.Visibility = away ? Visibility.Collapsed : Visibility.Visible;
            failLab.Text = away ? "If it never goes" : "If it never shows";
            var lim = limitSeg.Value == "Give up after" ? Visibility.Visible : Visibility.Collapsed;
            limitRow.Visibility = lim;
            failSeg.Visibility = lim;
            failLab.Visibility = lim;
            var clk = !away && doSeg.Value is "Click it" or "Click + key" ? Visibility.Visible : Visibility.Collapsed;
            btnSeg.Visibility = clk;
            btnLab.Visibility = clk;
            offRow.Visibility = clk;
            offLab.Visibility = clk;
            var ky = !away && doSeg.Value is "Press a key" or "Click + key" ? Visibility.Visible : Visibility.Collapsed;
            keyRow.Visibility = ky;
            keyLab.Visibility = ky;
        }
        limitSeg.Changed += _ => Sync();
        doSeg.Changed += _ => Sync();
        f.Add(L("Image", 0), pick.Align(HorizontalAlignment.Left).M(0, 4, 0, 4).At(0, 1),
              K.V(prev, cap.M(0, 2, 0, 0)).M(0, 2, 0, 6).At(1, 1),
              doLab, doSeg.M(0, 4, 0, 4).At(2, 1),
              btnLab, btnSeg.M(0, 4, 0, 4).At(3, 1),
              keyLab, keyRow.M(0, 4, 0, 4).At(4, 1),
              L("Match strictness", 5), K.H(conf, confLab.M(10, 0, 0, 0)).M(0, 4, 0, 4).At(5, 1),
              L("Time limit", 6), K.H(limitSeg, limitRow).M(0, 4, 0, 4).At(6, 1),
              failLab, failSeg.M(0, 4, 0, 4).At(7, 1),
              offLab, offRow.M(0, 4, 0, 4).At(8, 1),
              orLab, orRow.M(0, 4, 0, 4).At(9, 1),
              gone.M(0, 6, 0, 0).At(10, 0, 1, 2));
        ShowImg(chosen);
        Sync();
        d.Body.Children.Add(f);
        d.Body.Children.Add(commonView);
        d.AddButton("Cancel", null);
        d.AddAction(isNew ? "Add step" : "Save", () =>
        {
            bool away = gone.IsChecked == true;
            bool wantKey = !away && doSeg.Value is "Press a key" or "Click + key";
            var key = keyBox.Text.Trim().ToLowerInvariant();
            if (wantKey && !Keys.IsKnown(key))
            {
                d.Error.Text = key.Length == 0 ? "Type the key to press (for example e)." : $"“{key}” isn't a key name. Try e, space, enter, tab, f5, 1, up…";
                return;
            }
            s.Image = chosen;
            s.Gone = away;
            s.Click = !away && doSeg.Value is "Click it" or "Click + key";
            s.Key = wantKey ? key : "";
            s.Or = away ? new List<string>() : orList.Where(n => n != chosen).ToList();
            s.Button = btnSeg.Value.ToLowerInvariant();
            s.Conf = Math.Round(conf.Value, 2);
            s.Timeout = limitSeg.Value == "No limit" ? 0 : limit.Value;
            s.OnFail = failSeg.Value.StartsWith("Skip", StringComparison.Ordinal) ? "skip" : "stop";
            s.Dx = s.Click ? (int)(offX.Parsed ?? 0) : 0;            // (an empty box means "no offset", not the lowest value)
            s.Dy = s.Click ? (int)(offY.Parsed ?? 0) : 0;
            commonApply();
            d.Finish(true);
        });
        return d.Run() is true;
    }

    List<(string file, string name, Files.Summary sum, DateTime mtime)> SeqRecordings()
    {
        var list = new List<(string file, string name, Files.Summary sum, DateTime mtime)>();
        try
        {
            foreach (var p in Directory.EnumerateFiles(RecDir, "*.txt"))
            {
                var sum = SeqScan(p);
                var file = Path.GetFileName(p);
                list.Add((file, sum.Name.Length > 0 ? sum.Name : file, sum, File.GetLastWriteTimeUtc(p)));
            }
        }
        catch (IOException) { }
        return list.OrderByDescending(x => cfg.Favorites.Contains(x.file, StringComparer.OrdinalIgnoreCase)).ThenByDescending(x => x.mtime).ToList();
    }

    bool SeqPlayDialog(SeqStep s, bool isNew)
    {
        var recs = SeqRecordings();
        if (recs.Count == 0)
        {
            Toast("The Library is empty - record something first.", "warn", 5000);
            return false;
        }
        var d = new Dialog(TopWindow, isNew ? "Play a recording" : "Edit recording step",
                           "Pick the recording this step plays. It uses the saved version from the Library (newest first).", "play");
        string chosen = recs.Any(x => x.file == s.Recording) ? s.Recording : null;
        var search = K.Field("", 500);
        search.Tip("Type to filter by name");
        var list = new StackPanel();
        var sv = new ScrollViewer { Content = list, Height = 132, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false };
        var rows = new Dictionary<string, Border>();
        var times = new Stepper(s.Times, 1, 1, 100000, true, 52);
        var speedSeg = new Segmented(new[] { "Normal", "Its own speed" }, s.Speed > 0 ? "Its own speed" : "Normal");
        var speedStep = new Stepper(s.Speed > 0 ? s.Speed : 1, 0.25, 0.1, 10, false, 52);
        var speedRow = K.H(speedStep.M(10, 0, 0, 0), K.T("  ×", 13, "Muted"));
        void SpeedSync() => speedRow.Visibility = speedSeg.Value == "Normal" ? Visibility.Collapsed : Visibility.Visible;
        speedSeg.Changed += _ => SpeedSync();
        SpeedSync();
        var watchText = K.T("", 12, "Muted", null, true);
        watchText.MaxWidth = 500;
        Button watchBtn = null, watchOff = null;
        void WatchShow()
        {
            bool on = s.Until.Length > 0;
            watchText.Text = on ? "It " + SeqWatchText(s) + "."
                                : "Off. Turn it on to look for a picture the whole time this recording plays, and only move on once it has been seen.";
            K.SetBtnContent(watchBtn, on ? "Change" : "Watch for an image…", "image");
            watchOff.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        }
        var (onlyView, onlyGet) = OnlyEditor(s.Only, "Everything", true);
        onlyView.Tip("Keyboard only leaves the recording's mouse moves and clicks out (image steps in it still wait, but don't click). Custom lets you tick exactly what is sent. The recording keeps its length either way.");
        var from = new Stepper(s.From, 0.5, 0, 86400, false, 60);
        var to = new Stepper(s.To, 0.5, 0, 86400, false, 60);
        var forSecs = new Stepper(s.ForSecs, 5, 0, 86400 * 7, false, 64);
        var speedVar = new Stepper(s.SpeedJitter, 5, 0, 90, false, 52);
        var pg = K.Grid("auto,*", "auto,auto,auto");
        pg.Add(K.T("Only this part", 13, "Muted").M(0, 6, 16, 6).At(0, 0),
               K.H(K.T("from  ", 13, "Muted"), from, K.T("  s   to  ", 13, "Muted"), to, K.T("  s   (0 = the end)", 13, "Muted")).M(0, 4, 0, 4).At(0, 1),
               K.T("Keep replaying", 13, "Muted").M(0, 6, 16, 6).At(1, 0),
               K.H(K.T("for  ", 13, "Muted"), forSecs, K.T("  seconds, instead of a number of times   (0 = off)", 13, "Muted")).M(0, 4, 0, 4).At(1, 1),
               K.T("Vary the speed", 13, "Muted").M(0, 6, 16, 6).At(2, 0),
               K.H(K.T("± ", 13, "Muted"), speedVar, K.T("  % - every play a little faster or slower, at random", 13, "Muted")).M(0, 4, 0, 4).At(2, 1));
        var playFold = Fold("More playback settings", pg, s.From > 0 || s.To > 0 || s.ForSecs > 0 || s.SpeedJitter > 0,
                            "Play only part of the recording, replay it for a set time, or vary its speed a little every time");
        var acText = K.T("", 12, "Muted", null, true);
        acText.MaxWidth = 500;
        Button acBtn = null, acOff = null;
        void AcShow()
        {
            var extra = s.Ac ? ClickMoreText(s.More, true) : "";
            acText.Text = s.Ac ? $"It {(s.More.KeyOnly ? "presses " + s.More.Key : "clicks " + SeqClicksWhere(s))} every {SeqEvery(s.AcEvery)} for as long as this step runs."
                                 + (extra.Length > 0 ? "\n" + extra : "")
                               : "Off. Turn it on to click spots you pick, at a steady pace, the whole time this recording plays.";
            K.SetBtnContent(acBtn, s.Ac ? "Change" : "Auto click…", "target");
            acOff.Visibility = s.Ac ? Visibility.Visible : Visibility.Collapsed;
        }
        acBtn = K.Btn("", () => { if (SeqClicksDialog(s, d, true)) AcShow(); }, "secondary", "target", 30);
        acOff = K.Btn("Turn off", () => { s.Ac = false; AcShow(); }, "ghost", null, 30);
        AcShow();
        watchBtn = K.Btn("", () => { if (SeqWatchDialog(s, d)) WatchShow(); }, "secondary", "image", 30);
        watchOff = K.Btn("Turn off", () => { s.Until = ""; WatchShow(); }, "ghost", null, 30);
        WatchShow();
        var (commonView, commonApply) = SeqCommon(s);

        void Ok()
        {
            if (chosen == null)
            {
                d.Error.Text = "Pick a recording first.";
                return;
            }
            s.Recording = chosen;
            s.Times = (int)times.Value;
            s.Speed = speedSeg.Value == "Normal" ? 0 : speedStep.Value;
            s.Only = onlyGet();
            if (to.Value > 0 && to.Value <= from.Value)
            {
                d.Error.Text = "“Only this part”: the end has to be later than the start (or 0 for the end of the recording).";
                return;
            }
            s.From = from.Value;
            s.To = to.Value;
            s.ForSecs = forSecs.Value;
            s.SpeedJitter = speedVar.Value;
            commonApply();
            d.Finish(true);
        }

        void Mark()
        {
            foreach (var kv in rows) kv.Value.SetResourceReference(Border.BackgroundProperty, kv.Key == chosen ? "AccentSoft" : "Card2");
        }

        void Fill()
        {
            list.Children.Clear();
            rows.Clear();
            var q = search.Text.Trim();
            foreach (var x in recs)
            {
                if (q.Length > 0 && !x.name.Contains(q, StringComparison.OrdinalIgnoreCase) && !x.file.Contains(q, StringComparison.OrdinalIgnoreCase)) continue;
                var g = K.Grid("*,auto");
                var nm = K.T((cfg.Favorites.Contains(x.file, StringComparer.OrdinalIgnoreCase) ? "★  " : "") + x.name, 13, "Text", FontWeights.SemiBold);
                nm.TextTrimming = TextTrimming.CharacterEllipsis;
                g.Add(nm.At(0, 0), K.T($"{Fmt.Dur(x.sum.Duration)}  ·  {x.mtime.ToLocalTime():MMM dd, HH:mm}", 12, "Muted").M(12, 0, 0, 0).At(0, 1));
                var row = K.Box(K.V(g, K.T($"{x.file}  ·  {x.sum.Actions:N0} actions", 11, "Faint").M(0, 2, 0, 0)), "Card2", 9, 12, 7);
                row.Cursor = Cursors.Hand;
                var file = x.file;
                row.MouseLeftButtonDown += (_, e) =>
                {
                    e.Handled = true;                      // don't let the dialog start dragging
                    chosen = file;
                    d.Error.Text = "";
                    Mark();
                    if (e.ClickCount == 2) Ok();
                };
                rows[file] = row;
                list.Children.Add(row.M(0, 0, 6, 4));
            }
            if (list.Children.Count == 0) list.Children.Add(K.T("No recording matches that.", 13, "Muted").M(4, 8, 0, 0));
            Mark();
        }

        search.TextChanged += (_, _) => Fill();
        Fill();
        d.Body.Children.Add(search.Align(HorizontalAlignment.Left).M(0, 2, 0, 8));
        d.Body.Children.Add(sv);
        d.Body.Children.Add(K.H(K.T("Play it  ", 13, "Muted"), times, K.T("  time(s) in a row          Speed  ", 13, "Muted"), speedSeg, speedRow).M(0, 12, 0, 0));
        var onlyRow = K.Grid("auto,*");
        onlyRow.Add(K.T("Play back  ", 13, "Muted").Align(HorizontalAlignment.Left, VerticalAlignment.Top).M(0, 7, 0, 0).At(0, 0), onlyView.At(0, 1));
        d.Body.Children.Add(onlyRow.M(0, 10, 0, 0));
        d.Body.Children.Add(playFold.M(0, 6, 0, 0));
        d.Body.Children.Add(K.Divider(false, 12));
        d.Body.Children.Add(K.T("AUTO CLICK WHILE IT PLAYS", 11, "Faint", FontWeights.SemiBold).M(0, 0, 0, 4));
        d.Body.Children.Add(acText);
        d.Body.Children.Add(K.H(acBtn, acOff.M(6, 0, 0, 0)).M(0, 8, 0, 0));
        d.Body.Children.Add(K.Divider(false, 12));
        d.Body.Children.Add(K.T("WATCH WHILE IT PLAYS", 11, "Faint", FontWeights.SemiBold).M(0, 0, 0, 4));
        d.Body.Children.Add(watchText);
        d.Body.Children.Add(K.H(watchBtn, watchOff.M(6, 0, 0, 0)).M(0, 8, 0, 0));
        d.Body.Children.Add(commonView);
        d.AddButton("Cancel", null);
        d.AddAction(isNew ? "Add step" : "Save", Ok);
        return d.Run(search) is true;
    }

    bool SeqWaitDialog(SeqStep s)
    {
        var d = new Dialog(TopWindow, "Pause", "The sequence does nothing here for a while, then carries on.", "clock");
        var secs = new Stepper(s.Seconds, 1, 0, 86400, false, 64);
        var extra = new Stepper(s.Random, 0.5, 0, 86400, false, 64);
        var (commonView, commonApply) = SeqCommon(s, false);
        d.Body.Children.Add(K.H(K.T("Wait  ", 13, "Muted"), secs, K.T("  seconds", 13, "Muted")).M(0, 4, 0, 8));
        d.Body.Children.Add(K.H(K.T("plus up to  ", 13, "Muted"), extra, K.T("  more, a different amount every time (0 = always the same)", 13, "Muted")).M(0, 0, 0, 4));
        d.Body.Children.Add(commonView);
        d.AddButton("Cancel", null);
        d.AddAction("Save", () =>
        {
            s.Seconds = secs.Value;
            s.Random = extra.Value;
            commonApply();
            d.Finish(true);
        });
        return d.Run() is true;
    }

    bool SeqRepeatDialog(SeqStep s, bool isNew, int index)
    {
        if (index <= 0)
        {
            Toast("Add the steps you want repeated first, then put Repeat below them.", "warn", 5000);
            return false;
        }
        var d = new Dialog(TopWindow, isNew ? "Repeat steps" : "Edit repeat",
                           "When the sequence reaches this step it goes back up and runs the steps above it again.", "loop");
        var back = new Stepper(Math.Clamp(s.Back, 1, index), 1, 1, index, true, 48);
        var count = new Stepper(Math.Max(2, s.Count), 1, 2, 1000000, true, 60);
        var hint = K.T("", 12, "Faint", null, true);
        void Sync()
        {
            int b = (int)back.Value, first = index - b + 1;
            hint.Text = (b == 1 ? $"Step {index}" : $"Steps {first} to {index}") + $" will run {(int)count.Value} times, then the sequence moves on.";
        }
        back.Changed += _ => Sync();
        count.Changed += _ => Sync();
        Sync();
        d.Body.Children.Add(K.H(K.T("Repeat the last  ", 13, "Muted"), back, K.T("  step(s) above", 13, "Muted")).M(0, 4, 0, 8));
        d.Body.Children.Add(K.H(count, K.T("  times in total", 13, "Muted")).M(0, 0, 0, 10));
        d.Body.Children.Add(hint);
        var (commonView, commonApply) = SeqCommon(s, false);
        d.Body.Children.Add(commonView);
        d.AddButton("Cancel", null);
        d.AddAction(isNew ? "Add step" : "Save", () =>
        {
            s.Back = (int)back.Value;
            s.Count = (int)count.Value;
            commonApply();
            d.Finish(true);
        });
        return d.Run() is true;
    }

    // ================================================================ running
    int SeqLoopsSetting() => seqLoopSeg?.Value switch
    {
        "Forever" => 0,
        "Repeat" => Math.Max(1, (int)(seqLoopCount?.Value ?? 2)),
        _ => 1,
    };

    void SeqLoopChanged()
    {
        if (seqLoopSeg == null) return;
        var m = seqLoopSeg.Value;
        seqLoopCountRow.Visibility = m == "Repeat" ? Visibility.Visible : Visibility.Collapsed;
        seqLoopHint.Text = m switch
        {
            "Once" => "Runs every step one time, then stops.",
            "Repeat" => $"Runs the whole list {SeqLoopsSetting()} times. Stop early with {Hint("play_toggle")} or {Hint("panic")}.",
            _ => $"Starts again from step 1 until you press {Hint("play_toggle")} or {Hint("panic")}.",
        };
        SeqCollectCfg();
    }

    /// The repeat choice belongs to the sequence: store it there (each sequence remembers its own).
    void SeqCollectCfg()
    {
        if (seqLoopSeg == null || seq == null || seqLoading) return;
        int loops = SeqLoopsSetting(), count = seqLoopSeg.Value == "Repeat" ? loops : seq.LoopCount;
        if (loops == seq.Loops && count == seq.LoopCount) return;
        seq.Loops = loops;
        seq.LoopCount = count;
        SeqSave();
    }

    /// Show the open sequence's own settings in the Run card and under its name.
    void SeqSettingsShow()
    {
        if (seqLoopSeg == null || seq == null) return;
        seqLoading = true;
        seqLoopSeg.Set(seq.Loops == 0 ? "Forever" : seq.Loops == 1 ? "Once" : "Repeat", false);
        seqLoopCount.Value = seq.Loops > 1 ? seq.Loops : Math.Max(2, seq.LoopCount);
        seqStaySwitch.IsChecked = seq.Show == "stay";
        seqLoading = false;
        SeqLoopChanged();
        SeqNoteShow();
    }

    void SeqNoteShow()
    {
        if (seqNote == null || seq == null) return;
        var bits = new List<string>();
        if (seq.Gap > 0) bits.Add($"{Fmt.Num(seq.Gap)} s between steps");
        if (seq.Speed > 0) bits.Add($"{Fmt.Num(seq.Speed)}× speed");
        if (seq.LoopDelay > 0) bits.Add($"{Fmt.Num(seq.LoopDelay)} s between full runs");
        if (seq.Window.Length > 0) bits.Add($"looks only in “{seq.Window}”");
        if (seq.Show.Length > 0) bits.Add(seq.Show == "stay" ? "window stays open" : seq.Show == "hide" ? "window hides" : "shrinks to the mini bar");
        if (seq.Clicker) bits.Add("auto clicker runs alongside");
        if (seq.GapRandom > 0) bits.Add($"+ up to {Fmt.Num(seq.GapRandom)} s at random between steps");
        if (seq.StartDelay > 0) bits.Add($"waits {Fmt.Num(seq.StartDelay)} s before it starts");
        if (seq.MaxMinutes > 0) bits.Add($"ends after {Fmt.Num(seq.MaxMinutes)} min");
        if (seq.OnError == "restart") bits.Add("starts again if a step fails");
        if (seq.OnError == "skip") bits.Add("carries on if a step fails");
        if (seq.Front.Length > 0) bits.Add($"brings “{seq.Front}” to the front");
        int watched = seq.Guards.Count(x => !x.Off);
        if (watched > 0) bits.Add($"always watches {watched} picture{(watched == 1 ? "" : "s")}");
        var text = seq.Note;
        if (bits.Count > 0) text += (text.Length > 0 ? "\n" : "") + string.Join("  ·  ", bits);
        seqNote.Text = text;
        seqNote.Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// Everything that belongs to one sequence rather than to the app.
    void SeqSettingsDialog()
    {
        if (seq == null || SeqBusy()) return;
        var d = new Dialog(TopWindow, "Settings for “" + seq.Name + "”", "These are saved with this sequence. Every sequence keeps its own.", "settings");
        var note = K.Field(seq.Note, 380);
        var gap = new Stepper(seq.Gap, 0.5, 0, 3600, false, 56);
        var gapRandom = new Stepper(seq.GapRandom, 0.5, 0, 3600, false, 56);
        var delay = new Stepper(seq.LoopDelay, 1, 0, 86400, false, 56);
        var loopRandom = new Stepper(seq.LoopRandom, 1, 0, 86400, false, 56);
        var startDelay = new Stepper(seq.StartDelay, 1, 0, 86400, false, 56);
        var maxMin = new Stepper(seq.MaxMinutes, 5, 0, 60 * 24 * 30, false, 64);
        var speedSeg = new Segmented(new[] { "The app's speed", "Its own" }, seq.Speed > 0 ? "Its own" : "The app's speed");
        var speed = new Stepper(seq.Speed > 0 ? seq.Speed : 1, 0.25, 0.1, 10, false, 52);
        var speedRow = K.H(speed.M(10, 0, 0, 0), K.T("  ×", 13, "Muted"));
        var areaSeg = new Segmented(new[] { "The app's setting", "One window" }, seq.Window.Length > 0 ? "One window" : "The app's setting");
        var (winRow, win) = WindowField(seq.Window, d);
        var (frontRow, front) = WindowField(seq.Front, d);
        front.Tip("Leave empty to leave the windows as they are");
        var showSeg = new Segmented(new[] { "The app's setting", "Stay open", "Mini bar", "Hide" },
                                    seq.Show switch { "stay" => "Stay open", "mini" => "Mini bar", "hide" => "Hide", _ => "The app's setting" });
        var errSeg = new Segmented(new[] { "Stop the run", "Start again from step 1", "Carry on" },
                                   seq.OnError == "restart" ? "Start again from step 1" : seq.OnError == "skip" ? "Carry on" : "Stop the run");
        errSeg.Tip("What happens when a step can't do its job - an image that never shows within its time limit, a window that isn't open. “Start again” is what unattended farming wants: 25 failed runs in a row still stop it.");
        var clickerSwitch = K.Switch("Run the auto clicker for as long as this sequence runs", seq.Clicker);
        clickerSwitch.Tip("Uses the setup on the Auto Clicker page (spots, speed, button). For clicks during one recording only, edit that step instead.");
        var sound = K.Switch("Play a sound when the run ends", seq.DoneSound);
        var guards = seq.Guards.Select(x => x.Copy()).ToList();
        var guardText = K.T("", 12, "Muted", null, true);
        guardText.MaxWidth = 380;
        void GuardShow() => guardText.Text = guards.Count == 0 ? "Nothing is watched. Add a picture to click pop-ups away, or to start again when a “You died” screen shows."
                                                               : string.Join("\n", guards.Select(GuardText));
        var guardBtn = K.Btn("Always-watch…", null, "secondary", "image", 30);
        guardBtn.Click += (_, _) => { if (SeqGuardsDialog(guards, d)) GuardShow(); };
        GuardShow();
        void Sync()
        {
            speedRow.Visibility = speedSeg.Value == "Its own" ? Visibility.Visible : Visibility.Collapsed;
            winRow.Visibility = areaSeg.Value == "One window" ? Visibility.Visible : Visibility.Collapsed;
        }
        speedSeg.Changed += _ => Sync();
        areaSeg.Changed += _ => Sync();
        Sync();

        var f = K.Grid("auto,*");
        int row = 0;
        TextBlock Small(string t) => K.T(t, 13, "Muted");
        void Row(string label, FrameworkElement what)
        {
            f.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            f.Add(K.T(label, 13, "Muted").Align(HorizontalAlignment.Left, VerticalAlignment.Top).M(0, 9, 16, 6).At(row, 0), what.M(0, 4, 0, 4).At(row, 1));
            row++;
        }
        void Head(string t)
        {
            f.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            f.Add(K.T(t, 11, "Faint", FontWeights.SemiBold).M(0, row == 0 ? 0 : 12, 0, 2).At(row, 0, 1, 2));
            row++;
        }
        Row("Note", note.Align(HorizontalAlignment.Left));
        Head("TIMING");
        Row("Before it starts", K.H(startDelay, Small("  seconds, once, before the first step")));
        Row("Gap between steps", K.H(gap, Small("  s,  plus up to  "), gapRandom, Small("  s random")));
        Row("Between full runs", K.H(delay, Small("  s,  plus up to  "), loopRandom, Small("  s random")));
        Row("Time limit", K.H(maxMin, Small("  minutes, then it ends   (0 = no limit)")));
        Row("Recording speed", K.H(speedSeg, speedRow));
        Head("WHEN SOMETHING GOES WRONG");
        Row("If a step fails", errSeg.Align(HorizontalAlignment.Left));
        Row("Always-watch", K.V(guardBtn.Align(HorizontalAlignment.Left), guardText.M(0, 6, 0, 0)));
        Head("WINDOWS");
        Row("Bring to the front", K.H(frontRow, Small("   when the run starts")));
        Row("Image steps look in", K.H(areaSeg, winRow.M(10, 0, 0, 0)));
        Row("TinyRec while it runs", showSeg.Align(HorizontalAlignment.Left));
        Head("EXTRAS");
        Row("Auto clicker", clickerSwitch);
        Row("Sound", sound);
        var scroll = new ScrollViewer { Content = f, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = Math.Max(300, Math.Min(900, SystemParameters.WorkArea.Height - 330)), Focusable = false, Padding = new Thickness(0, 0, 10, 0) };
        d.Body.Children.Add(scroll);
        d.Body.Children.Add(K.T("“Stay open” keeps this window up and lights the running step. How many times the whole list runs is set in the Run card.", 12, "Faint", null, true).M(0, 8, 0, 0));
        d.AddButton("Cancel", null);
        d.AddAction("Save", () =>
        {
            if (areaSeg.Value == "One window" && win.Text.Trim().Length == 0)
            {
                d.Error.Text = "Type part of the window's title (for example Roblox), or use Pick.";
                return;
            }
            seq.Note = note.Text.Trim();
            seq.Gap = gap.Value;
            seq.GapRandom = gapRandom.Value;
            seq.LoopDelay = delay.Value;
            seq.LoopRandom = loopRandom.Value;
            seq.StartDelay = startDelay.Value;
            seq.MaxMinutes = maxMin.Value;
            seq.Speed = speedSeg.Value == "Its own" ? speed.Value : 0;
            seq.Window = areaSeg.Value == "One window" ? win.Text.Trim() : "";
            seq.Front = front.Text.Trim();
            seq.Show = showSeg.Value switch { "Stay open" => "stay", "Mini bar" => "mini", "Hide" => "hide", _ => "" };
            seq.OnError = errSeg.Value == "Start again from step 1" ? "restart" : errSeg.Value == "Carry on" ? "skip" : "";
            seq.Clicker = clickerSwitch.IsChecked == true;
            seq.DoneSound = sound.IsChecked == true;
            seq.Guards = guards;
            d.Finish(true);
        });
        if (d.Run() is not true) return;
        SeqSave();
        SeqSettingsShow();
        SeqRefresh();
    }

    void SeqButtons()
    {
        if (seqRunBtn == null) return;
        bool on = state is "playing" or "countdown";
        K.SetBtnContent(seqRunBtn, on ? $"Stop   ({Hint("panic")})" : "Run sequence", on ? "stop" : "play");
        seqHotkeySwitch.Content = $"{Hint("play_toggle")} and the mini bar run this sequence";
    }

    public void ToggleSequence(int? countdown = null, int startAt = 0)
    {
        if (state is "playing" or "countdown") { Panic(); return; }
        if (state != "idle" || seq == null) return;
        SeqCollectCfg();                                             // a count typed but not confirmed yet still counts
        var s = seq.Copy();
        var runPath = seqPath;
        var problems = s.Problems(n => File.Exists(Path.Combine(RecDir, n)), n => File.Exists(Path.Combine(ImgDir, n)));
        if (problems.Count > 0)
        {
            Dialog.Info(TopWindow, "This sequence can't run yet", string.Join("\n", problems.Take(8)), "warn");
            if (IsVisible) ShowPage("sequences");
            return;
        }
        bool pictures = s.Guards.Any(g => !g.Off) || s.Steps.Any(x => !x.Off && (x.Kind == "image"
                            || (x.Kind == "play" && (x.Until.Length > 0 || (x.Ac && x.More.Images().Any())))
                            || (x.Kind == "click" && x.More.Images().Any())
                            || (x.Kind is "goto" or "stop" && x.IfMode != "always")));
        if (finder == null && pictures)
        {
            Dialog.Info(TopWindow, "Image recognition is off", "This sequence uses pictures (image steps, picture conditions or always-watch), but OpenCV couldn't start.", "error");
            return;
        }
        if (dirty && recPath != null && s.Steps.Any(x => x.Kind == "play" && !x.Off && SameFile(Path.Combine(RecDir, x.Recording), recPath)))
            Toast("Heads up: the open recording has unsaved edits - the sequence plays the saved version.", "warn", 5000);
        if (cfg.RelativeToWindow)
            foreach (var name in s.Steps.Where(x => x.Kind == "play" && !x.Off).Select(x => x.Recording).Distinct())
            {
                try { PlayOffset(Files.LoadText(Path.Combine(RecDir, name))); }
                catch (InvalidOperationException e) { Dialog.Info(TopWindow, "Can't follow the window", $"“{name}”: {e.Message}", "warn"); return; }
                catch (Exception) { }                                   // unreadable file: the run reports it properly
            }
        int loops = Math.Max(0, s.Loops);
        draw.Stop();
        startAt = Math.Clamp(startAt, 0, s.Steps.Count - 1);
        runModeOverride = s.Show.Length > 0 ? s.Show : null;             // this sequence's own choice for the window
        bool stays = (runModeOverride ?? cfg.DuringRun) == "stay";
        if (stays && IsVisible && WindowState != WindowState.Minimized) ShowPage("sequences");
        Countdown(CountdownSecs(countdown), "Starting", () => BeginSequence(s, loops, startAt, runPath));
    }

    void BeginSequence(Sequence s, int loops, int startAt, string runPath)
    {
        state = "playing";
        System.Windows.Input.Keyboard.ClearFocus();   // so a replayed Space or Enter can't press the button that was just clicked
        seqRun = s;
        seqRunPath = runPath;
        foreach (var chip in seqRowRuns) chip.Visibility = Visibility.Collapsed;
        runner.Prepare();
        playRec = null;
        paused = false;
        player.Resume();
        playStart = Timing.Now;
        StartLog("sequence “" + s.Name + "”", "seq",
                 $"running {(loops == 0 ? "until stopped" : loops == 1 ? "once" : $"{loops} times")} - {s.Steps.Count} steps");
        var log = runLog;
        loopsTotal = loops;
        showPlayBadge = false;                       // the badge would sit in the screenshots the image steps take
        if (cfg.HideDrawingsWhilePlaying) draw.Suspend();
        seqResult.Text = "Running…";
        seqResult.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        if (s.Clicker) SideClickerStart();
        RefreshButtons();
        var th = new Thread(() =>
        {
            try
            {
                var st = runner.Run(s, loops, s.LoopDelay, startAt, log);
                Dispatcher.BeginInvoke(new Action(() => OnSeqDone(st, null)));
            }
            catch (Exception e)
            {
                App.Log(e.ToString());
                Dispatcher.BeginInvoke(new Action(() => OnSeqDone(null, $"{e.GetType().Name}: {e.Message}")));
            }
        }) { IsBackground = true, Priority = ThreadPriority.Highest, Name = "TinyRec sequence" };
        th.Start();
    }

    double pausedShown;
    string seqRunPath;                                    // the file of the sequence that is running
    /// The sequence that is running is the one shown in the list (so step numbers line up with the rows).
    bool seqRunIsOpen => seqRun != null && seq != null && seqRunPath == seqPath && seq.Steps.Count == seqRun.Steps.Count;

    void SeqTick()
    {
        int n = runner.StepCount, i = runner.StepIndex;
        if (seqRun == null || i < 0 || n == 0) return;
        string phase = runner.Phase, detail = runner.Detail;
        double frac = 0;
        string what, brief;
        if (!paused) pausedShown = Timing.Now - runner.RunStart;         // the clock on the mini bar holds still while paused
        if (phase == "image")
        {
            what = $"waiting for {detail}  ({Fmt.Dur(Timing.Now - runner.PhaseStart)})";
            brief = $"{i + 1}/{n} waiting for image";
        }
        else if (phase == "play")
        {
            frac = Math.Min(1.0, runner.EventI / (double)Math.Max(1, runner.EventN));
            int pct = (int)(frac * 100);
            var watch = runner.Watching;
            what = $"playing {detail}  {pct}%" + (runner.PlayOf > 1 ? $"  (run {runner.PlayNo} of {runner.PlayOf})" : runner.PlayOf == 0 && runner.PlayNo > 1 ? $"  (pass {runner.PlayNo})" : "")
                   + (runner.Clicking ? "  ·  auto clicking" : "")
                   + (string.IsNullOrEmpty(watch) ? "" : $"  ·  watching for {watch}");
            brief = $"{i + 1}/{n} playing {pct}%" + (string.IsNullOrEmpty(watch) ? "" : " · watching");
        }
        else if (phase is "key" or "focus")
        {
            what = phase == "key" ? (detail == "typing" ? "typing" : $"pressing {detail}") : $"bringing {detail} to the front";
            brief = $"{i + 1}/{n} {(phase == "key" ? "keys" : "window")}";
        }
        else if (phase == "click")
        {
            int of = runner.ClickN, done = runner.ClickI;
            frac = of > 0 ? Math.Min(1.0, done / (double)of) : 0;
            what = player.ClicksHeldOff ? "waiting to click - move the pointer off TinyRec"
                 : of > 0 ? $"clicking  {done:N0} of {of:N0}" : $"clicking  ({done:N0} so far)";
            brief = $"{i + 1}/{n} clicking";
        }
        else
        {
            double left = 0;
            double.TryParse(detail, NumberStyles.Float, CultureInfo.InvariantCulture, out left);
            left = Math.Max(0, left - (Timing.Now - runner.PhaseStart));
            what = left >= 1 ? $"waiting {Fmt.Dur(left)}" : "waiting";
            brief = $"{i + 1}/{n} waiting";
        }
        if (paused)
        {
            what = "paused - " + what;
            brief = $"{Fmt.Dur(pausedShown)} · {i + 1}/{n} paused";
        }
        else brief = $"{Fmt.Dur(Timing.Now - runner.RunStart)} · {brief}";
        var round = runner.RoundOf > 0 ? $"  ·  round {runner.Round} of {runner.RoundOf}" : "";
        var loops = loopsTotal == 1 ? "" : $"  ·  loop {runner.Loop}" + (loopsTotal > 0 ? $" of {loopsTotal}" : "");
        var line = $"Step {i + 1} of {n}{round}{loops}";
        ftName.Text = $"Sequence: {seqRun.Name}";
        ftTime.Text = $"{line}  ·  {what}";
        ftBar.Value = Math.Min(1, (i + frac) / n);
        SetStatus($"{line}  ·  {what}", null, brief);
        seqStatus.Text = line + "\n" + what;
        int shown = seqRunIsOpen ? i : -1;
        if (shown != seqShown) SeqHighlight(shown);
        if (shown >= 0) SeqLive(i, what);
    }

    void OnSeqDone(SeqStats st, string error)
    {
        SideClickerStop();
        if (seqRun?.DoneSound == true) Beep();
        state = "idle";
        badge.HideBadge();
        draw.Resume();
        LeaveRunMode();
        seqRun = null;
        paused = false;
        player.Resume();
        runLog?.Add(error != null ? $"error: {error}" : !string.IsNullOrEmpty(st.Message) ? st.Message
                    : $"{(st.Stopped ? "stopped" : st.TimedOut ? "time limit reached" : "finished")} after {Fmt.Dur(Timing.Now - playStart)} - {st.Loops} full run{(st.Loops == 1 ? "" : "s")}, "
                      + $"{st.Plays} recording{(st.Plays == 1 ? "" : "s")} played, {st.ImageHits} image{(st.ImageHits == 1 ? "" : "s")} found"
                      + (st.Clicks > 0 ? $", {st.Clicks:N0} click{(st.Clicks == 1 ? "" : "s")}" : "")
                      + (st.Restarts > 0 ? $", started again {st.Restarts} time{(st.Restarts == 1 ? "" : "s")}" : ""));
        SeqHighlight(-1);
        seqStatus.Text = "";
        if (error != null)
        {
            SetStatus("Sequence failed", "error");
            seqResult.Text = $"{DateTime.Now:HH:mm:ss}  ·  {error}";
            seqResult.SetResourceReference(TextBlock.ForegroundProperty, "Red");
            Dialog.Info(TopWindow, "Sequence stopped", $"An error stopped the sequence:\n{error}", "error");
        }
        else
        {
            bool failed = !string.IsNullOrEmpty(st.Message);
            var msg = failed ? st.Message
                    : $"{(st.Stopped ? "Stopped" : st.TimedOut ? "Time limit reached" : "Finished")}  ·  {st.Loops} full run{(st.Loops == 1 ? "" : "s")}  ·  "
                      + $"{st.Plays} recording{(st.Plays == 1 ? "" : "s")} played  ·  {st.ImageHits} image{(st.ImageHits == 1 ? "" : "s")} found"
                      + (st.Clicks > 0 ? $"  ·  {st.Clicks:N0} click{(st.Clicks == 1 ? "" : "s")}" : "")
                      + (st.Restarts > 0 ? $"  ·  started again {st.Restarts} time{(st.Restarts == 1 ? "" : "s")}" : "")
                      + (st.GuardHits > 0 ? $"  ·  always-watch fired {st.GuardHits} time{(st.GuardHits == 1 ? "" : "s")}" : "");
            seqResult.Text = $"{DateTime.Now:HH:mm:ss}  ·  {msg}";
            seqResult.SetResourceReference(TextBlock.ForegroundProperty, failed ? "Red" : "Muted");
            if (failed) Dialog.Info(TopWindow, "Sequence stopped", st.Message, "warn");
            else Toast(msg, "ok", 5000);
        }
        RefreshInfo();
        RefreshButtons();
    }
}
