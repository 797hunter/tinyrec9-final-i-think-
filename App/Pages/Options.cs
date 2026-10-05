// Options.cs - small editors that several pages and dialogs share: pick a picture, choose which parts of a
// recording count (keyboard / mouse / a custom mix), the finer click settings, and a fold-away section.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using TinyRec.Core;
using TinyRec.Ui;

namespace TinyRec;

public partial class MainWindow
{
    /// A button that picks one of the library's pictures, or none.
    (Button btn, Func<string> get) ImagePicker(string initial, string none = "No picture", Action changed = null)
    {
        string cur = initial ?? "";
        Button b = K.Btn("", null, "secondary", "image", 30);
        void Show() => K.SetBtnContent(b, cur.Length > 0 ? cur : none, "image");
        b.Click += (_, _) =>
        {
            var items = new List<(string, Action)> { (none, () => { cur = ""; Show(); changed?.Invoke(); }) };
            foreach (var n in ImageNames())
            {
                var name = n;
                items.Add((name, () => { cur = name; Show(); changed?.Invoke(); }));
            }
            K.Menu(items.ToArray()).Popup(b);
        };
        Show();
        return (b, () => cur);
    }

    /// A section that folds away under its own heading.
    FrameworkElement Fold(string title, FrameworkElement body, bool open, string hint = null)
    {
        Button head = K.Btn("", null, "ghost", null, 28);
        head.HorizontalAlignment = HorizontalAlignment.Left;
        head.Padding = new Thickness(6, 0, 10, 0);
        void Show()
        {
            body.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            K.SetBtnContent(head, (open ? "▾  " : "▸  ") + title, null);
        }
        head.Click += (_, _) => { open = !open; Show(); };
        if (hint != null) head.Tip(hint);
        Show();
        return K.V(head, body.M(8, 4, 0, 4));
    }

    /// Which parts of a recording count: everything, keyboard only, mouse only, or a custom mix (with keys to leave out).
    /// `changed` is told the new setting every time something is touched.
    (FrameworkElement view, Func<string> get) OnlyEditor(string initial, string allLabel, bool withImages, Action<string> changed = null)
    {
        var sp = Only.Parse(initial);
        string clean = Only.Clean(initial);
        string mode = clean.Length == 0 ? allLabel : clean == Only.Keys ? "Keyboard only" : clean == Only.Mouse ? "Mouse only" : "Custom";
        Segmented seg = null;
        CheckBox moves = null, clicks = null, scroll = null, keys = null, images = null;
        TextBox skip = null;
        StackPanel custom = null;
        bool ready = false;

        string Get()
        {
            if (seg.Value == allLabel) return "";
            if (seg.Value == "Keyboard only") return Only.Keys;
            if (seg.Value == "Mouse only") return Only.Mouse;
            var parts = new List<string>();
            if (moves.IsChecked == true) parts.Add("moves");
            if (clicks.IsChecked == true) parts.Add("clicks");
            if (scroll.IsChecked == true) parts.Add("scroll");
            if (keys.IsChecked == true) parts.Add("keys");
            if (!withImages || images.IsChecked == true) parts.Add("images");
            var leave = skip.Text.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(k => "-" + k.Trim().ToLowerInvariant());
            return Only.Clean((parts.Count == 0 ? "none" : string.Join("+", parts)) + " " + string.Join(" ", leave));
        }
        TextBlock warn = null;
        void Touched()
        {
            if (!ready) return;
            var odd = skip.Text.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(Only.KeyName).Where(k => !KeyNameOk(k)).ToList();
            warn.Text = odd.Count == 0 ? "" : $"Not a key name: {string.Join(", ", odd)}   (try tab, esc, space, enter, f5, ctrl, shift, a…)";
            warn.Visibility = odd.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            custom.Visibility = seg.Value == "Custom" ? Visibility.Visible : Visibility.Collapsed;
            changed?.Invoke(Get());
        }

        seg = new Segmented(new[] { allLabel, "Keyboard only", "Mouse only", "Custom" }, mode, _ => Touched());
        moves = K.Switch("Pointer moves", sp.Moves, _ => Touched());
        clicks = K.Switch("Clicks", sp.Clicks, _ => Touched());
        scroll = K.Switch("Scrolling", sp.Scroll, _ => Touched());
        keys = K.Switch("Keys", sp.KeysOn, _ => Touched());
        images = K.Switch("Image steps", sp.Images, _ => Touched());
        skip = K.Field(string.Join(", ", sp.Skip.OrderBy(k => k)), 200);
        skip.Tip("Keys to leave out, separated by commas - for example: tab, esc, f5");
        skip.TextChanged += (_, _) => Touched();
        warn = K.T("", 12, "Amber", null, true).M(0, 4, 0, 0);
        warn.Visibility = Visibility.Collapsed;
        var row2 = K.H(keys.M(0, 0, 16, 0));
        if (withImages) row2.Children.Add(images);
        custom = K.V(K.H(moves.M(0, 0, 16, 0), clicks.M(0, 0, 16, 0), scroll).M(0, 6, 0, 0), row2.M(0, 6, 0, 0),
                     K.H(K.T("Leave out these keys  ", 13, "Muted"), skip).M(0, 6, 0, 0), warn);
        custom.Visibility = mode == "Custom" ? Visibility.Visible : Visibility.Collapsed;
        ready = true;
        return (K.V(seg.Align(HorizontalAlignment.Left), custom), Get);
    }

    /// The finer click settings. `plain` leaves out the ones the Auto Clicker page already has controls for;
    /// `alongside` adds "stop after" limits for clicks that run next to a recording.
    (FrameworkElement view, Action<ClickMore> apply) ClickMoreEditor(ClickMore m, bool alongside, bool plain = false)
    {
        m = (m ?? new ClickMore()).Copy().Clean();
        var g = K.Grid("auto,*");
        int row = 0;
        void Row(string label, FrameworkElement what, string tip = null)
        {
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var l = K.T(label, 13, "Muted").M(0, 6, 16, 6);
            if (tip != null) { l.Tip(tip); what.Tip(tip); }
            g.Add(l.At(row, 0), what.M(0, 4, 0, 4).At(row, 1));
            row++;
        }
        TextBlock Small(string t) => K.T(t, 13, "Muted");

        var order = new Segmented(new[] { "In turn", "Random", "There and back" }, m.Order == "random" ? "Random" : m.Order == "pingpong" ? "There and back" : "In turn");
        var perSpot = new Stepper(m.PerSpot, 1, 1, 100000, true, 52);
        var jitter = new Stepper(m.Jitter, 10, 0, 3600000, true, 64);
        var hold = new Stepper(m.Hold, 10, 0, 600000, true, 64);
        var spread = new Stepper(m.Spread, 1, 0, 2000, true, 52);
        var delay = new Stepper(m.StartDelay, 0.5, 0, 86400, false, 56);
        var burst = new Stepper(m.Burst, 1, 0, 100000000, true, 60);
        var rest = new Stepper(m.Rest, 0.5, 0, 86400, false, 56);
        var style = new Segmented(new[] { "Auto", "Careful", "Fast" }, m.Style == "careful" ? "Careful" : m.Style == "fast" ? "Fast" : "Auto");
        var ret = K.Switch("Put the pointer back where it was after each click", m.Return);
        var key = K.Field(m.Key, 90);
        var keyOnly = K.Switch("instead of clicking", m.KeyOnly);
        var (targetBtn, targetGet) = ImagePicker(m.Target, "No - use the spots");
        var (ifBtn, ifGet) = ImagePicker(m.If, "Always");
        var ifSeg = new Segmented(new[] { "is on screen", "is not on screen" }, m.IfNot ? "is not on screen" : "is on screen");
        var (untilBtn, untilGet) = ImagePicker(m.Until, "No");
        var conf = new Stepper(m.Conf, 0.05, 0.5, 1, false, 56);
        var limClicks = new Stepper(m.LimitClicks, 10, 0, 100000000, true, 72);
        var limSecs = new Stepper(m.LimitSecs, 5, 0, 86400 * 7, false, 64);

        if (!plain)
        {
            Row("Order", K.H(order, Small("     ")), "With several spots: go round them in turn, pick one at random each time, or go there and back");
            Row("Wait first", K.H(delay, Small("  seconds before the first click")));
            Row("Vary the pace", K.H(Small("± "), jitter, Small("  ms, at random")), "Adds or takes away a random amount from every gap so the rhythm isn't robotic");
            Row("Hold each click", K.H(hold, Small("  ms  (0 = a normal quick click)")));
        }
        Row("Clicks per spot", K.H(perSpot, Small("  before moving on to the next spot")));
        Row("Spread", K.H(Small("± "), spread, Small("  pixels around each spot, at random")), "Each click lands a little off the spot, a different way every time");
        Row("Bursts", K.H(Small("after  "), burst, Small("  clicks, rest  "), rest, Small("  s")), "For example 10 clicks, a 2 second rest, 10 clicks, a rest ...");
        Row("Click style", style, "Careful lets the pointer settle before pressing (what games need, slower). Fast just presses. Auto is careful only when it moves to a different spot.");
        Row("Pointer", ret, "Useful when a recording that moves the mouse plays at the same time");
        Row("Press a key", K.H(key, Small("   "), keyOnly), "A key to press with every click (e, space, f ...). Tick \"instead of clicking\" to turn this into a key presser.");
        Row("Click a picture", K.H(targetBtn, Small("   clicks wherever it is on screen, each time")), "Instead of fixed spots: the picture is looked for before every click, and nothing is clicked while it isn't there");
        Row("Only while", K.H(ifBtn, Small("  "), ifSeg), "Clicks only while this picture is on screen (or only while it is not). The rest of the time it waits.");
        Row("Stop when", K.H(untilBtn, Small("   shows (the clicking ends for good)")));
        Row("Picture match", K.H(conf, Small("  how strict (0.85 - 0.95 is usual)")));
        if (alongside)
            Row("Stop after", K.H(limClicks, Small("  clicks, or  "), limSecs, Small("  seconds   (0 = keep going with the recording)")));

        return (g, o =>
        {
            if (!plain)
            {
                o.Order = order.Value == "Random" ? "random" : order.Value == "There and back" ? "pingpong" : "sequence";
                o.StartDelay = delay.Value;
                o.Jitter = jitter.Value;
                o.Hold = hold.Value;
            }
            o.PerSpot = (int)perSpot.Value;
            o.Spread = (int)spread.Value;
            o.Burst = (int)burst.Value;
            o.Rest = rest.Value;
            o.Style = style.Value.ToLowerInvariant();
            o.Return = ret.IsChecked == true;
            o.Key = key.Text.Trim().ToLowerInvariant();
            o.KeyOnly = keyOnly.IsChecked == true && o.Key.Length > 0;
            o.Target = targetGet();
            o.If = ifGet();
            o.IfNot = ifSeg.Value == "is not on screen";
            o.Until = untilGet();
            o.Conf = conf.Value;
            if (alongside)
            {
                o.LimitClicks = (int)limClicks.Value;
                o.LimitSecs = limSecs.Value;
            }
            o.Clean();
        });
    }

    /// One line that says what the finer click settings add ("" when they add nothing).
    static string ClickMoreText(ClickMore m, bool alongside)
    {
        if (m == null) return "";
        var bits = new List<string>();
        if (m.KeyOnly && m.Key.Length > 0) bits.Add($"presses {m.Key} instead of clicking");
        else if (m.Key.Length > 0) bits.Add($"presses {m.Key} too");
        if (m.Target.Length > 0) bits.Add($"clicks {m.Target} wherever it is");
        if (m.If.Length > 0) bits.Add($"only while {m.If} is {(m.IfNot ? "not " : "")}on screen");
        if (m.Until.Length > 0) bits.Add($"stops when {m.Until} shows");
        if (m.Order == "random") bits.Add("random order");
        if (m.Order == "pingpong") bits.Add("there and back");
        if (m.PerSpot > 1) bits.Add($"{m.PerSpot} clicks per spot");
        if (m.Jitter > 0) bits.Add($"± {Fmt.Num(m.Jitter)} ms");
        if (m.Spread > 0) bits.Add($"± {m.Spread} px");
        if (m.Hold > 0) bits.Add($"held {Fmt.Num(m.Hold)} ms");
        if (m.Burst > 0 && m.Rest > 0) bits.Add($"{m.Burst} then a {Fmt.Num(m.Rest)} s rest");
        if (m.StartDelay > 0) bits.Add($"starts after {Fmt.Num(m.StartDelay)} s");
        if (m.Return) bits.Add("pointer goes back");
        if (m.Style != "auto") bits.Add(m.Style);
        if (alongside && m.LimitClicks > 0) bits.Add($"at most {m.LimitClicks:N0} clicks");
        if (alongside && m.LimitSecs > 0) bits.Add($"for {Fmt.Num(m.LimitSecs)} s");
        return string.Join("  ·  ", bits);
    }

    /// A sound for "look at me": an alert step, an always-watch, the end of a run.
    static void Beep()
    {
        try { System.Media.SystemSounds.Exclamation.Play(); } catch { }
    }
}
