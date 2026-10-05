// Editor.cs - every action in the recording: a fast table (handles 100k+ rows), the raw text, and an inspector.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using TinyRec.Core;
using TinyRec.Overlays;
using TinyRec.Ui;

namespace TinyRec;

public partial class MainWindow
{
    public sealed class EvRow
    {
        public int Index { get; set; }
        public string N { get; set; }
        public string Time { get; set; }
        public string Gap { get; set; }
        public string Kind { get; set; }
        public string Values { get; set; }
        public Brush Fg { get; set; }
    }

    static readonly Dictionary<string, string> KindColor = new()
    {
        ["move"] = "Muted", ["click"] = "Red", ["key"] = "Accent", ["scroll"] = "Amber", ["image"] = "Green",
    };
    static readonly Dictionary<string, string> Filters = new()
    {
        ["All"] = null, ["Moves"] = "move", ["Clicks"] = "click", ["Keys"] = "key", ["Scrolls"] = "scroll", ["Images"] = "image",
    };

    bool tableDirty = true;
    DataGrid grid;
    TextBox codeBox;
    TextBlock codeMsg, edStatus;
    Segmented viewSeg, filterSeg;
    Border tableCard, textCard, logCard;
    Timeline timeline;
    TextBox edLogBox;
    Stepper insGap;
    StackPanel insGapRow;
    DispatcherTimer inspectorDebounce;
    // inspector parts (built once, updated in place)
    StackPanel insSum, insOne, insMany, insNudge;
    readonly Dictionary<string, TextBlock> insCounts = new();
    TextBlock insLen, insTitle, insErr, insManyT, insManyS, insImgCap;
    Border insChip;
    TextBox insLine;
    Button insPick;
    Border insImg;
    int insIdx = -1;

    FrameworkElement PageEditor()
    {
        var g = K.Grid("*,310", "auto,auto,auto,*,auto");

        // toolbar
        var tb = new WrapPanel();
        void Add(string icon, string text, Action act, string tip = null)
        {
            var b = text == null ? K.IconBtn(icon, act, tip, 34, "Text") : K.Btn(text, act, "ghost", icon, 34);
            if (tip != null && text != null) b.Tip(tip);
            tb.Children.Add(b.M(1, 0, 1, 0));
        }
        void Sep() => tb.Children.Add(K.Divider(true).M(6, 8, 6, 8));
        Add("plus", "Add", EdAdd);
        Add("edit", "Edit", EdEdit);
        Add("trash", "Delete", EdDelete);
        Add("copy", "Duplicate", EdDup);
        Add("target", "Clicks", EdClicks, "Add a burst of clicks on a spot you pick - e.g. 20 clicks, 10 a second");
        Sep();
        Add("undo", null, EdUndo, "Undo  (Ctrl+Z)");
        Add("redo", null, EdRedo, "Redo  (Ctrl+Y)");
        Sep();
        Add("clock", "Shift", EdShift, "Move the selected actions (or everything) earlier / later");
        Add("speed", "Scale", EdScale, "Stretch or squeeze the timing");
        Add("wand", "Thin moves", EdThin, "Remove extra mouse-move points");
        Add("image", "Image step", () => EdImage(null), "Insert a 'find this picture and click it' step");
        g.Add(K.Card(tb.M(8, 6, 8, 6)).M(0, 0, 0, 10).At(0, 0, 1, 2));

        // visual timeline
        timeline = new Timeline();
        timeline.Picked += TimelinePicked;
        g.Add(K.Card(timeline.M(6, 4, 4, 4)).M(0, 0, 0, 10).At(1, 0, 1, 2));

        // filter + view
        filterSeg = new Segmented(Filters.Keys, "All", _ => RefreshTable());
        viewSeg = new Segmented(new[] { "Table", "Text", "Run log" }, "Table", _ => SwitchView(), 64);
        var vb = K.Grid("auto,auto,*,auto,auto");
        vb.Add(K.T("Show", 13, "Muted").M(4, 0, 10, 0).At(0, 0), filterSeg.At(0, 1),
               K.T("View", 13, "Muted").M(0, 0, 10, 0).At(0, 3), viewSeg.At(0, 4));
        g.Add(vb.M(0, 0, 0, 10).At(2, 0));

        // table
        grid = new DataGrid();
        grid.SetResourceReference(StyleProperty, "Table");
        foreach (var (head, prop, w, star) in new[] { ("#", "N", 64.0, false), ("Time (s)", "Time", 90.0, false), ("+Gap", "Gap", 80.0, false),
                                                       ("Action", "Kind", 130.0, false), ("Values", "Values", 1.0, true) })
            grid.Columns.Add(new DataGridTextColumn { Header = head, Binding = new Binding(prop), Width = star ? new DataGridLength(1, DataGridLengthUnitType.Star) : new DataGridLength(w) });
        grid.SelectionChanged += (_, _) =>
        {
            timeline.SetSelection(Sel());
            inspectorDebounce ??= NewDebounce(30, RefreshInspector);
            inspectorDebounce.Stop();
            inspectorDebounce.Start();
        };
        grid.MouseDoubleClick += (_, e) => { if (grid.SelectedItems.Count == 1) EdEdit(); };
        grid.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Delete) { EdDelete(); e.Handled = true; }
            else if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control) { EdUndo(); e.Handled = true; }
            else if (e.Key == Key.Y && Keyboard.Modifiers == ModifierKeys.Control) { EdRedo(); e.Handled = true; }
        };
        // Ctrl+Z / Ctrl+Y anywhere on the Editor page (not while typing in a text box - those have their own undo)
        PreviewKeyDown += (_, e) =>
        {
            if (page != "editor" || e.Handled || Keyboard.FocusedElement is TextBox) return;
            if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control) { EdUndo(); e.Handled = true; }
            else if (e.Key == Key.Y && Keyboard.Modifiers == ModifierKeys.Control) { EdRedo(); e.Handled = true; }
        };
        tableCard = K.Card(grid.M(6), 0, 14);
        g.Add(tableCard.At(3, 0));

        // text view
        codeBox = new TextBox();
        codeBox.SetResourceReference(StyleProperty, "Code");
        codeMsg = K.T("This is exactly what's in the .txt file. Edit freely, then Apply.", 12, "Muted", null, true);
        var tbar = K.Grid("auto,auto,*");
        tbar.Add(K.Btn("Apply changes", ApplyText, "primary", "check").At(0, 0), K.Btn("Revert", RefreshText, "secondary", "undo").M(8, 0, 0, 0).At(0, 1),
                 codeMsg.M(12, 0, 0, 0).At(0, 2));
        var tg = K.Grid("*", "*,auto");
        tg.Add(codeBox.M(4, 4, 4, 0).At(0, 0), tbar.M(12, 10, 12, 12).At(1, 0));
        textCard = K.Card(tg, 0, 14);
        textCard.Visibility = Visibility.Collapsed;
        g.Add(textCard.At(3, 0));

        // run log view
        edLogBox = LogBox();
        var lbar = K.H(K.Btn("Open log file", LogOpenFile, "secondary", "log", 32).M(0, 0, 8, 0), K.Btn("All logs", () => OpenFolder(LogDir), "secondary", "folder", 32).M(0, 0, 12, 0),
                       K.T("What the last run did. Click a line to jump to that action.", 12, "Muted"));
        var lg = K.Grid("*", "*,auto");
        lg.Add(edLogBox.M(4, 4, 4, 0).At(0, 0), lbar.M(12, 10, 12, 12).At(1, 0));
        logCard = K.Card(lg, 0, 14);
        logCard.Visibility = Visibility.Collapsed;
        g.Add(logCard.At(3, 0));

        // inspector
        g.Add(K.Card(BuildInspector()).M(12, 0, 0, 0).At(2, 1, 2));
        edStatus = K.T("", 12, "Muted");
        g.Add(edStatus.M(0, 8, 0, 0).At(4, 0, 1, 2));
        return g;
    }

    DispatcherTimer NewDebounce(int ms, Action act)
    {
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
        t.Tick += (_, _) => { t.Stop(); act(); };
        return t;
    }

    FrameworkElement BuildInspector()
    {
        var v = new StackPanel();
        v.Children.Add(K.CardTitle("Inspector", "search"));
        var body = new StackPanel { Margin = new Thickness(18, 0, 18, 16) };
        v.Children.Add(body);

        insSum = new StackPanel();
        insSum.Children.Add(K.T("Nothing selected - whole recording:", 12, "Muted").M(0, 0, 0, 8));
        foreach (var (lab, key, c) in new[] { ("Mouse moves", "moves", "Muted"), ("Clicks", "clicks", "Red"), ("Key presses", "keys", "Accent"),
                                              ("Scrolls", "scrolls", "Amber"), ("Image steps", "images", "Green") })
        {
            var val = K.T("0", 13, "Text", FontWeights.SemiBold);
            insCounts[key] = val;
            var r = K.Grid("auto,*,auto");
            r.Add(K.T("●", 12, c).M(0, 0, 8, 0).At(0, 0), K.T(lab, 13).At(0, 1), val.At(0, 2));
            insSum.Children.Add(K.Box(r.M(10, 6, 12, 6), "Card2", 8).M(0, 0, 0, 5));
        }
        insLen = K.T("", 13, "Text", FontWeights.SemiBold);
        insSum.Children.Add(insLen.M(0, 8, 0, 0));
        insSum.Children.Add(K.T("Tip: click a row to edit it here. Shift/Ctrl-click to pick several.", 12, "Faint", null, true).M(0, 10, 0, 0));

        insGap = new Stepper(0, 0.5, 0, 86400, false, 64);
        insGapRow = K.V(K.T("Wait before this action", 12, "Muted").M(0, 14, 0, 4),
                        K.H(insGap, K.T("  s  ", 13, "Muted"), K.Btn("Set", InsSetGap, "secondary", null, 30)
                                .Tip("Changes the wait before this action and moves everything after it by the same amount")));
        insOne = new StackPanel();
        insChip = K.Chip("", "Muted", "Card2", 12);
        insTitle = K.T("", 13, "Text", FontWeights.SemiBold);
        insLine = K.Field("", 270, true);
        insLine.KeyDown += (_, e) => { if (e.Key == Key.Enter) InsApply(); };
        insErr = K.T("", 12, "Red", null, true);
        insPick = K.Btn("Pick new position on screen", () => PickFor(insIdx), "secondary", "target");
        insImg = new Border { Height = 90, CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 12, 0, 2) };
        insImg.SetResourceReference(Border.BackgroundProperty, "Card2");
        insImgCap = K.T("", 11, "Muted", null, true);
        foreach (var el in new FrameworkElement[] { insChip.M(0, 0, 0, 10), insTitle, K.T("Edit the line and press Enter or Apply:", 12, "Muted").M(0, 8, 0, 4),
                                                    insLine, insErr, K.Btn("Apply", InsApply, "primary", "check").Align(HorizontalAlignment.Left).M(0, 6, 0, 0),
                                                    insPick.Align(HorizontalAlignment.Left).M(0, 10, 0, 0), insImg, insImgCap, insGapRow })
            insOne.Children.Add(el);

        insMany = new StackPanel();
        insManyT = K.T("", 15, "Text", FontWeights.SemiBold);
        insManyS = K.T("", 12, "Muted", null, true);
        insMany.Children.Add(insManyT);
        insMany.Children.Add(insManyS.M(0, 2, 0, 0));

        insNudge = new StackPanel();
        insNudge.Children.Add(K.T("Nudge timing", 12, "Muted").M(0, 14, 0, 4));
        var nr = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var d in new[] { -0.1, -0.01, 0.01, 0.1 })
        {
            var b = K.Btn((d > 0 ? "+" : "") + d.ToString("0.##", CultureInfo.InvariantCulture) + "s", () => Nudge(d), "secondary", null, 30);
            b.Padding = new Thickness(8, 0, 8, 0);
            b.FontWeight = FontWeights.SemiBold;
            nr.Children.Add(b.M(0, 0, 4, 0));
        }
        insNudge.Children.Add(nr);

        body.Children.Add(insSum);
        body.Children.Add(insOne);
        body.Children.Add(insMany);
        body.Children.Add(insNudge);
        return new ScrollViewer { Content = v, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false };
    }

    void SwitchView()
    {
        var v = viewSeg.Value;
        tableCard.Visibility = v == "Table" ? Visibility.Visible : Visibility.Collapsed;
        textCard.Visibility = v == "Text" ? Visibility.Visible : Visibility.Collapsed;
        logCard.Visibility = v == "Run log" ? Visibility.Visible : Visibility.Collapsed;
        if (v == "Text") RefreshText();
        if (v == "Run log") LogRefresh(true);
    }

    /// A mark on the timeline was clicked: select that action in the table.
    void TimelinePicked(int index)
    {
        if (index < 0 || index >= rec.Events.Count) return;
        if (viewSeg.Value != "Table") viewSeg.Set("Table");
        EdSelectIndex(index);
    }

    void EdSelectIndex(int index)
    {
        var rows = grid.ItemsSource as List<EvRow>;
        var row = rows?.FirstOrDefault(r => r.Index == index);
        if (row == null && filterSeg.Value != "All")                // filtered out: show everything again
        {
            filterSeg.Set("All");
            rows = grid.ItemsSource as List<EvRow>;
            row = rows?.FirstOrDefault(r => r.Index == index);
        }
        if (row == null) return;
        grid.SelectedItems.Clear();
        grid.SelectedItem = row;
        grid.ScrollIntoView(row);
    }

    void InsSetGap()
    {
        if (insIdx < 0 || insIdx >= rec.Events.Count) return;
        var target = rec.Events[insIdx];
        double want = insGap.Value;
        PushHistory();
        rec.Sort();
        int i = rec.Events.IndexOf(target);
        double delta = Edits.SetGap(rec.Events, i, want);
        if (delta == 0)
        {
            history.RemoveAt(history.Count - 1);
            Toast("That's already the wait before this action.");
            return;
        }
        int after = rec.Events.Count - i - 1;
        Edited(new[] { target });
        Toast($"Wait set to {Fmt.Num(want)} s - this action and the {after:N0} after it moved {(delta > 0 ? "later" : "earlier")} by {Fmt.Num(Math.Abs(delta))} s", "ok", 4500);
    }

    void RefreshTable()
    {
        if (grid == null) return;
        tableDirty = false;
        var want = Filters.TryGetValue(filterSeg.Value ?? "All", out var f) ? f : null;
        var brushes = KindColor.ToDictionary(kv => kv.Key, kv => Theme.B(kv.Value));
        var rows = new List<EvRow>(rec.Events.Count);
        double? prev = null;
        for (int i = 0; i < rec.Events.Count; i++)
        {
            var e = rec.Events[i];
            var cls = Edits.KindClass(e.Kind);
            if (want != null && cls != want) { prev = e.T; continue; }
            rows.Add(new EvRow
            {
                Index = i, N = (i + 1).ToString(CultureInfo.InvariantCulture), Time = e.T.ToString("0.000", CultureInfo.InvariantCulture),
                Gap = prev == null ? "" : "+" + (e.T - prev.Value).ToString("0.000", CultureInfo.InvariantCulture),
                Kind = e.Kind, Values = string.Join(" ", e.Args.Concat(e.Opts.Select(o => o.Key + "=" + o.Value))), Fg = brushes[cls],
            });
            prev = e.T;
        }
        grid.ItemsSource = rows;
        timeline?.SetEvents(rec.Events);
        RefreshInspector();
    }

    void RefreshText()
    {
        if (codeBox == null) return;
        codeBox.Text = rec.ToText();
        codeMsg.Text = "This is exactly what's in the .txt file. Edit freely, then Apply.";
        codeMsg.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
    }

    void ApplyText()
    {
        Recording r;
        try { r = Recording.FromText(codeBox.Text); }
        catch (FormatError e)
        {
            codeMsg.Text = "Not applied - " + e.Message;
            codeMsg.SetResourceReference(TextBlock.ForegroundProperty, "Red");
            return;
        }
        PushHistory();
        r.Meta = r.Meta.Count > 0 ? r.Meta : rec.Meta;
        rec = r;
        dirty = true;
        RefreshEverything();
        Toast("Text changes applied", "ok");
    }

    List<int> Sel() => grid.SelectedItems.OfType<EvRow>().Select(r => r.Index).OrderBy(i => i).ToList();

    void ShowIns(StackPanel which)
    {
        foreach (var p in new[] { insSum, insOne, insMany }) p.Visibility = p == which ? Visibility.Visible : Visibility.Collapsed;
        insNudge.Visibility = which == insSum ? Visibility.Collapsed : Visibility.Visible;
    }

    void RefreshInspector()
    {
        if (grid == null) return;
        var sel = Sel();
        var ev = rec.Events;
        if (sel.Count == 0)
        {
            ShowIns(insSum);
            var c = Edits.Count(ev);
            insCounts["moves"].Text = c.Moves.ToString("N0");
            insCounts["clicks"].Text = c.Clicks.ToString("N0");
            insCounts["keys"].Text = c.Keys.ToString("N0");
            insCounts["scrolls"].Text = c.Scrolls.ToString("N0");
            insCounts["images"].Text = c.Images.ToString("N0");
            insLen.Text = "Length: " + Fmt.Dur(rec.Duration);
            return;
        }
        if (sel.Count == 1 && sel[0] < ev.Count)
        {
            ShowIns(insOne);
            var i = insIdx = sel[0];
            var e = ev[i];
            K.SetChip(insChip, e.Kind, KindColor[Edits.KindClass(e.Kind)]);
            insTitle.Text = $"Action #{i + 1} at {e.T.ToString("0.000", CultureInfo.InvariantCulture)} s";
            insLine.Text = e.ToLine();
            insErr.Text = "";
            insGap.Value = Math.Round(e.T - (i > 0 ? ev[i - 1].T : 0), 3);
            insPick.Visibility = Edits.IsMouse(e.Kind) ? Visibility.Visible : Visibility.Collapsed;
            if (e.IsImage)
            {
                var (img, cap) = ThumbImage(e.Args[0], 260, 90);
                insImg.Child = img;
                insImg.Visibility = img == null ? Visibility.Collapsed : Visibility.Visible;
                insImgCap.Text = cap;
                insImgCap.SetResourceReference(TextBlock.ForegroundProperty, img == null ? "Red" : "Muted");
                insImgCap.Visibility = Visibility.Visible;
            }
            else
            {
                insImg.Visibility = Visibility.Collapsed;
                insImgCap.Visibility = Visibility.Collapsed;
            }
            return;
        }
        ShowIns(insMany);
        double span = ev[sel[^1]].T - ev[sel[0]].T;
        insManyT.Text = $"{sel.Count:N0} actions selected";
        insManyS.Text = $"From {ev[sel[0]].T:0.000} s to {ev[sel[^1]].T:0.000} s  ({span:0.000} s)";
    }

    void InsApply()
    {
        if (insIdx < 0 || insIdx >= rec.Events.Count) return;
        Event e;
        try { e = Format.ParseLine(insLine.Text); }
        catch (FormatError ex)
        {
            insErr.Text = ex.Message;
            return;
        }
        PushHistory();
        rec.Events[insIdx] = e;
        Edited(new[] { e });
    }

    void EditorStatus()
    {
        if (edStatus == null) return;
        edStatus.Text = $"{rec.Events.Count:N0} actions  ·  {Fmt.Dur(rec.Duration)}  ·  {(dirty ? "unsaved changes" : "saved")}  ·  Delete, Ctrl+Z and Ctrl+Y work in the table";
    }

    // ---------------------------------------------------------------- edit operations
    void PushHistory()
    {
        history.Add(rec.Copy());
        if (history.Count > 100) history.RemoveAt(0);
        future.Clear();
    }

    void Edited(IEnumerable<Event> select = null)
    {
        rec.Sort();
        dirty = true;
        RefreshInfo();
        RefreshTable();
        if (viewSeg.Value == "Text") RefreshText();
        if (select == null) return;
        var want = select.ToList();
        var rows = ((List<EvRow>)grid.ItemsSource).Where(r => want.Any(w => ReferenceEquals(w, rec.Events[r.Index]))).ToList();
        grid.SelectedItems.Clear();
        foreach (var r in rows) grid.SelectedItems.Add(r);
        if (rows.Count > 0) grid.ScrollIntoView(rows[0]);
    }

    void EdUndo()
    {
        if (history.Count == 0) return;
        future.Add(rec.Copy());
        rec = history[^1];
        history.RemoveAt(history.Count - 1);
        dirty = true;
        RefreshEverything();
        Toast("Undone");
    }

    void EdRedo()
    {
        if (future.Count == 0) return;
        history.Add(rec.Copy());
        rec = future[^1];
        future.RemoveAt(future.Count - 1);
        dirty = true;
        RefreshEverything();
        Toast("Redone");
    }

    Event LineDialog(string title, string initial)
    {
        var d = new Dialog(TopWindow, title, "One action per line:  <seconds> <ACTION> <values>", "edit");
        var box = K.Field(initial, 540, true);
        var templates = new (string name, string line)[]
        {
            ("Mouse move", "{0} MOVE 500 400"), ("Left button down", "{0} LEFT_DOWN 500 400"), ("Left button up", "{0} LEFT_UP 500 400"),
            ("Right button down", "{0} RIGHT_DOWN 500 400"), ("Right button up", "{0} RIGHT_UP 500 400"),
            ("Scroll", "{0} SCROLL 500 400 0 -1"), ("Key down", "{0} KEY_DOWN a"), ("Key up", "{0} KEY_UP a"),
            ("Click an image", "{0} IMAGE_CLICK image.png conf=0.9 timeout=10 button=left onfail=skip"),
            ("Wait for an image", "{0} IMAGE_WAIT image.png conf=0.9 timeout=30 onfail=stop"),
        };
        Button tplBtn = null;
        tplBtn = K.Btn("Start from a template…", () =>
        {
            var t0 = initial.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "0.000";
            K.Menu(templates.Select(t => (t.name, (Action)(() => box.Text = string.Format(CultureInfo.InvariantCulture, t.line, t0)))).ToArray()).Popup(tplBtn);
        }, "secondary", "plus", 32);
        d.Body.Children.Add(tplBtn.Align(HorizontalAlignment.Left).M(0, 4, 0, 8));
        d.Body.Children.Add(box);
        Event result = null;
        d.AddButton("Cancel", null);
        d.AddAction("OK", () =>
        {
            try
            {
                result = Format.ParseLine(box.Text);
                d.Finish(true);
            }
            catch (FormatError e) { d.Error.Text = e.Message; }
        });
        return d.Run(box) is true ? result : null;
    }

    void EdAdd()
    {
        var sel = Sel();
        double t = Math.Ceiling((sel.Count > 0 ? rec.Events[sel[^1]].T : rec.Duration) * 1000 - 1e-6) / 1000;   // round up: new step lands after, not before
        var e = LineDialog("Add an action", $"{t.ToString("0.000", CultureInfo.InvariantCulture)} MOVE 500 400");
        if (e == null) return;
        PushHistory();
        rec.Events.Add(e);
        Edited(new[] { e });
    }

    void EdEdit()
    {
        var sel = Sel();
        if (sel.Count != 1) { Toast("Select exactly one row to edit.", "warn"); return; }
        var e = LineDialog("Edit action", rec.Events[sel[0]].ToLine());
        if (e == null) return;
        PushHistory();
        rec.Events[sel[0]] = e;
        Edited(new[] { e });
    }

    void EdDelete()
    {
        var sel = Sel();
        if (sel.Count == 0) return;
        PushHistory();
        foreach (var i in sel.OrderByDescending(i => i)) rec.Events.RemoveAt(i);
        Edited();
        Toast($"Deleted {sel.Count:N0} action{(sel.Count == 1 ? "" : "s")}  ·  Ctrl+Z to undo");
    }

    void EdDup()
    {
        var sel = Sel();
        if (sel.Count == 0) return;
        PushHistory();
        var copies = sel.Select(i => rec.Events[i].Copy()).ToList();
        rec.Events.AddRange(copies);
        Edited(copies);
    }

    List<int> Targets()
    {
        var s = Sel();
        return s.Count > 0 ? s : Enumerable.Range(0, rec.Events.Count).ToList();
    }

    void EdShift()
    {
        var v = Dialog.AskNumber(TopWindow, "Shift times", "Move the selected actions by how many seconds?\nNegative = earlier. Nothing selected = everything.", "0.5");
        if (v == null || rec.Events.Count == 0) return;
        PushHistory();
        var moved = Targets().Select(i => rec.Events[i]).ToList();
        foreach (var e in moved) e.T = Math.Max(0, e.T + v.Value);
        Edited(Sel().Count > 0 ? moved : null);
    }

    void EdScale()
    {
        var v = Dialog.AskNumber(TopWindow, "Scale times", "Multiply the times by:\n0.5 = twice as fast, 2 = twice as slow. Nothing selected = everything.", "0.5", 0.01, 100);
        if (v == null || rec.Events.Count == 0) return;
        PushHistory();
        foreach (var i in Targets()) rec.Events[i].T *= v.Value;
        Edited();
    }

    void EdThin()
    {
        int before = rec.Events.Count;
        PushHistory();
        rec.Events = Edits.ThinMoves(rec.Events);
        Edited();
        Toast($"Removed {before - rec.Events.Count:N0} extra mouse-move points. Clicks, keys and scrolls untouched.", "ok");
    }

    void Nudge(double d)
    {
        var sel = Sel();
        if (sel.Count == 0) return;
        PushHistory();
        var moved = sel.Select(i => rec.Events[i]).ToList();
        foreach (var e in moved) e.T = Math.Max(0, Math.Round(e.T + d, 4));
        Edited(moved);
    }

    void PickFor(int idx)
    {
        if (idx < 0 || idx >= rec.Events.Count) return;
        var pts = Hidden(() => Picker.Pick("Click the new position", false));
        if (pts.Count == 0) return;
        PushHistory();
        var e = rec.Events[idx];
        e.Args[0] = pts[0].x.ToString(CultureInfo.InvariantCulture);
        e.Args[1] = pts[0].y.ToString(CultureInfo.InvariantCulture);
        Edited(new[] { e });
    }

    void EdImage(string name)
    {
        var imgs = ImageNames();
        if (imgs.Count == 0)
        {
            Toast("Your image library is empty - snip one on the Images page first.", "warn");
            ShowPage("images");
            return;
        }
        var sel = Sel();
        double t0 = Math.Ceiling((sel.Count > 0 ? rec.Events[sel[^1]].T : rec.Duration) * 1000 - 1e-6) / 1000;
        var d = new Dialog(TopWindow, "Image step", "At this point in the timeline, TinyRec looks for the picture on screen, waits for it (the rest of the recording waits too), then clicks its centre.", "image");
        var f = K.Grid("auto,*", "auto,auto,auto,auto,auto,auto,auto,auto,auto,auto");
        FrameworkElement L(string t, int r) => K.T(t, 13, "Muted").M(0, 6, 16, 6).At(r, 0);
        var time = K.Field(t0.ToString("0.000", CultureInfo.InvariantCulture), 100);
        var doSeg = new Segmented(new[] { "Click it", "Press a key", "Click + key", "Only wait" }, "Click it");
        var chosen = name ?? imgs[0];
        var orList = new List<string>();
        var orText = K.T("none", 12, "Muted", null, true);
        orText.MaxWidth = 250;
        void OrShow() => orText.Text = orList.Count == 0 ? "none" : string.Join(", ", orList);
        var prev = new Border { Width = 240, Height = 90, CornerRadius = new CornerRadius(8) };
        prev.SetResourceReference(Border.BackgroundProperty, "Card2");
        var cap = K.T("", 11, "Muted");
        void ShowImg(string n)
        {
            chosen = n;
            var (img, c) = ThumbImage(n, 240, 90);
            prev.Child = img;
            cap.Text = c;
            if (orList.Remove(n)) OrShow();
        }
        Button pickImg = null;
        pickImg = K.Btn(chosen, () => K.Menu(imgs.Select(n => (n, (Action)(() => { ShowImg(n); K.SetBtnContent(pickImg, n, "image"); }))).ToArray()).Popup(pickImg), "secondary", "image", 32);
        var conf = K.Slide(0.5, 1.0, 0.9, null, 200);
        var confLab = K.T("0.90", 13, "Text", FontWeights.SemiBold);
        conf.ValueChanged += (_, e) => confLab.Text = e.NewValue.ToString("0.00", CultureInfo.InvariantCulture);
        var timeout = new Stepper(10, 1, 0, 86400, false, 60);
        var btnSeg = new Segmented(new[] { "Left", "Right", "Double" }, "Left");
        var btnLab = L("Click with", 4);
        var keyBox = K.Field("", 90);
        var keyRow = K.H(keyBox, K.T("   e, space, enter, f5, 1 …", 12, "Faint"));
        var keyLab = L("Key to press", 5);
        var failSeg = new Segmented(new[] { "Keep waiting", "Skip the step", "Stop playback" }, "Keep waiting");
        Button orAdd = null;
        orAdd = K.Btn("Add", () =>
        {
            var free = imgs.Where(n => n != chosen && !orList.Contains(n)).ToList();
            if (free.Count == 0) return;
            K.Menu(free.Select(n => (n, (Action)(() => { orList.Add(n); OrShow(); }))).ToArray()).Popup(orAdd);
        }, "secondary", "plus", 28);
        var orRow = K.H(orAdd, K.IconBtn("close", () => { orList.Clear(); OrShow(); }, "Clear the list", 28).M(4, 0, 8, 0), orText)
                     .Tip("If one of these pictures shows instead, playback carries on without clicking anything");
        void Sync()
        {
            var clk = doSeg.Value is "Click it" or "Click + key" ? Visibility.Visible : Visibility.Collapsed;
            btnSeg.Visibility = clk;
            btnLab.Visibility = clk;
            var ky = doSeg.Value is "Press a key" or "Click + key" ? Visibility.Visible : Visibility.Collapsed;
            keyRow.Visibility = ky;
            keyLab.Visibility = ky;
        }
        doSeg.Changed += _ => Sync();
        f.Add(L("At time (s)", 0), time.Align(HorizontalAlignment.Left).M(0, 4, 0, 4).At(0, 1),
              L("Image", 1), pickImg.Align(HorizontalAlignment.Left).M(0, 4, 0, 4).At(1, 1),
              K.V(prev, cap.M(0, 2, 0, 0)).M(0, 2, 0, 6).At(2, 1),
              L("When it shows", 3), doSeg.M(0, 4, 0, 4).At(3, 1),
              btnLab, btnSeg.M(0, 4, 0, 4).At(4, 1),
              keyLab, keyRow.M(0, 4, 0, 4).At(5, 1),
              L("Match strictness", 6), K.H(conf, confLab.M(10, 0, 0, 0)).M(0, 4, 0, 4).At(6, 1),
              L("Look for up to (s)", 7), K.H(timeout, K.T("   0 = no time limit", 12, "Faint")).M(0, 4, 0, 4).At(7, 1),
              L("If not found by then", 8), failSeg.M(0, 4, 0, 4).At(8, 1),
              L("Or carry on if", 9), orRow.M(0, 4, 0, 4).At(9, 1));
        ShowImg(chosen);
        Sync();
        d.Body.Children.Add(f);
        Event made = null;
        d.AddButton("Cancel", null);
        d.AddAction("Insert step", () =>
        {
            bool click = doSeg.Value is "Click it" or "Click + key", wantKey = doSeg.Value is "Press a key" or "Click + key";
            var key = keyBox.Text.Trim().ToLowerInvariant();
            if (wantKey && !Keys.IsKnown(key))
            {
                d.Error.Text = key.Length == 0 ? "Type the key to press (for example e)." : $"“{key}” isn't a key name. Try e, space, enter, tab, f5, 1, up…";
                return;
            }
            var opts = new List<string> { "conf=" + conf.Value.ToString("0.00", CultureInfo.InvariantCulture), "timeout=" + timeout.Value.ToString("0.##", CultureInfo.InvariantCulture) };
            if (click) opts.Add("button=" + btnSeg.Value.ToLowerInvariant());
            if (wantKey) opts.Add("key=" + key);
            opts.Add("onfail=" + (failSeg.Value.StartsWith("Keep") ? "wait" : failSeg.Value.StartsWith("Skip") ? "skip" : "stop"));
            if (orList.Count > 0) opts.Add("or=" + string.Join(",", orList));
            try
            {
                made = Format.ParseLine($"{time.Text.Trim()} {(click ? "IMAGE_CLICK" : "IMAGE_WAIT")} {chosen} {string.Join(" ", opts)}");
                d.Finish(true);
            }
            catch (FormatError e) { d.Error.Text = e.Message; }
        });
        if (d.Run() is not true || made == null) return;
        PushHistory();
        rec.Events.Add(made);
        ShowPage("editor");
        Edited(new[] { made });
        Toast("Image step added", "ok");
    }

    /// Add a run of clicks on one spot to the recording: N clicks at a steady pace, starting at a time you choose.
    void EdClicks()
    {
        if (state != "idle") return;
        var sel = Sel();
        double start = sel.Count > 0 ? rec.Events[sel[^1]].T + 0.1 : rec.Duration + (rec.Events.Count > 0 ? 0.2 : 0);
        var d = new Dialog(TopWindow, "Add clicks", "Adds a run of clicks on one spot. They are ordinary clicks afterwards: you can move, edit or delete them like any other action.", "target");
        (int x, int y)? spot = null;
        var where = K.T("No spot picked yet.", 13, "Muted");
        var pick = K.Btn("Pick the spot on screen", null, "primary", "target", 32);
        pick.Click += (_, _) =>
        {
            double left = d.Left;
            d.Left = -30000;
            List<(int x, int y)> got;
            try { got = Hidden(() => Picker.Pick("Click the spot to click on", false)); }
            finally { d.Left = left; d.Activate(); }
            if (got.Count == 0) return;
            spot = got[0];
            where.Text = $"x {got[0].x},  y {got[0].y}";
            where.SetResourceReference(TextBlock.ForegroundProperty, "Text");
            d.Error.Text = "";
        };
        var at = new Stepper(Math.Round(start, 2), 0.5, 0, 86400, false, 72);
        var count = new Stepper(10, 1, 1, 5000, true, 64);
        var every = new Stepper(100, 10, 20, 600000, true, 72);
        var button = new Segmented(new[] { "Left", "Right", "Middle" }, "Left", null, 64);
        var sum = K.T("", 12, "Faint");
        void Sum() => sum.Text = $"Takes {Fmt.Dur(Math.Max(0, count.Value - 1) * every.Value / 1000)} - about {1000 / Math.Max(1, every.Value):0.#} clicks a second.";
        count.Changed += _ => Sum();
        every.Changed += _ => Sum();
        Sum();
        var g = K.Grid("auto,*", "auto,auto,auto,auto,auto,auto");
        FrameworkElement L(string t, int r) => K.T(t, 13, "Muted").M(0, 6, 16, 6).At(r, 0);
        g.Add(L("Where", 0), K.H(pick, where.M(12, 0, 0, 0)).M(0, 4, 0, 4).At(0, 1),
              L("Start at", 1), K.H(at, K.T("  seconds into the recording", 13, "Muted")).M(0, 4, 0, 4).At(1, 1),
              L("Clicks", 2), count.Align(HorizontalAlignment.Left).M(0, 4, 0, 4).At(2, 1),
              L("One every", 3), K.H(every, K.T("  ms", 13, "Muted")).M(0, 4, 0, 4).At(3, 1),
              L("Button", 4), button.Align(HorizontalAlignment.Left).M(0, 4, 0, 4).At(4, 1),
              sum.M(0, 6, 0, 0).At(5, 0, 1, 2));
        d.Body.Children.Add(g);
        d.AddButton("Cancel", null);
        d.AddAction("Add clicks", () =>
        {
            if (spot == null) { d.Error.Text = "Pick the spot first."; return; }
            d.Finish(true);
        });
        if (d.Run() is not true || spot == null) return;
        string b = button.Value.ToUpperInvariant();
        double gap = every.Value / 1000, held = Math.Min(0.04, gap / 2);
        var made = new List<Event>();
        for (int k = 0; k < (int)count.Value; k++)
        {
            double t = Math.Round(at.Value + k * gap, 4);
            made.Add(Event.Of(t, b + "_DOWN", spot.Value.x, spot.Value.y));
            made.Add(Event.Of(Math.Round(t + held, 4), b + "_UP", spot.Value.x, spot.Value.y));
        }
        PushHistory();
        rec.Events.AddRange(made);
        ShowPage("editor");
        Edited(made);
        Toast($"{(int)count.Value} clicks added", "ok");
    }
}
