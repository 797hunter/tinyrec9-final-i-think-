// SeqMore.cs - the newer kinds of sequence step (keys, jump, stop, bring a window forward, alert) and the
// "always-watch" pictures a sequence keeps an eye out for the whole time it runs.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using TinyRec.Core;
using TinyRec.Ui;

namespace TinyRec;

public partial class MainWindow
{
    static bool KeyNameOk(string k) => Keys.IsKnown(k) || Keys.IsChar(k);

    /// "" if every part of a key or combination ("e", "ctrl+shift+a") is a real key name, else what is wrong.
    static string ComboError(string combo)
    {
        var parts = (combo ?? "").Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return "Type a key first (for example e, space, enter, f5, ctrl+c).";
        var bad = parts.FirstOrDefault(k => !KeyNameOk(k.ToLowerInvariant()));
        return bad == null ? "" : $"“{bad}” isn't a key name. Try e, space, enter, tab, esc, f5, up, shift, ctrl+c…";
    }

    /// A box for part of a window's title, with a menu of the windows that are open right now.
    (FrameworkElement view, TextBox box) WindowField(string initial, Dialog d, double width = 200)
    {
        var win = K.Field(initial, width);
        Button pick = null;
        pick = K.Btn("Pick", () =>
        {
            var titles = TinyRec.Win.ScreenGrab.WindowTitles().Where(t => !t.Contains("TinyRec")).Distinct().Take(24).ToList();
            if (titles.Count == 0) { d.Error.Text = "No other windows are open right now."; return; }
            K.Menu(titles.Select(t => (t.Length > 60 ? t[..60] + "…" : t, (Action)(() => win.Text = t))).ToArray()).Popup(pick);
        }, "secondary", null, 32);
        return (K.H(win, pick.M(8, 0, 0, 0)), win);
    }

    /// "Always / only if a picture is on screen / only if it is not" - for Jump and Stop steps.
    (FrameworkElement view, Func<string> apply) SeqCondition(SeqStep s)
    {
        var seg = new Segmented(new[] { "Always", "If a picture is on screen", "If it is NOT on screen" },
                                s.IfMode == "shown" ? "If a picture is on screen" : s.IfMode == "hidden" ? "If it is NOT on screen" : "Always");
        var (btn, get) = ImagePicker(s.IfImage, "Pick the picture");
        var conf = new Stepper(s.IfConf, 0.05, 0.5, 1, false, 56);
        var row = K.H(btn, K.T("    match  ", 13, "Muted"), conf);
        void Sync() => row.Visibility = seg.Value == "Always" ? Visibility.Collapsed : Visibility.Visible;
        seg.Changed += _ => Sync();
        Sync();
        return (K.V(seg.Align(HorizontalAlignment.Left), row.M(0, 8, 0, 0)), () =>
        {
            string mode = seg.Value == "Always" ? "always" : seg.Value.StartsWith("If a picture") ? "shown" : "hidden";
            if (mode != "always" && get().Length == 0) return "Pick the picture it checks for.";
            s.IfMode = mode;
            s.IfImage = mode == "always" ? "" : get();
            s.IfConf = conf.Value;
            return "";
        });
    }

    bool SeqKeyDialog(SeqStep s, bool isNew)
    {
        var d = new Dialog(TopWindow, isNew ? "Press keys" : "Edit key step", "Presses a key (or a combination) a number of times, or types some text. Then the sequence carries on.", "keyboard");
        var mode = new Segmented(new[] { "Press a key", "Type text" }, s.KeyMode == "type" ? "Type text" : "Press a key");
        var combo = K.Field(s.Combo, 180);
        combo.Tip("One key or a combination: e, space, enter, tab, esc, f5, up, shift, ctrl+c, ctrl+shift+a");
        var times = new Stepper(s.KeyTimes, 1, 1, 1000000, true, 60);
        var every = new Stepper(s.KeyEvery, 50, 1, 3600000, true, 72);
        var hold = new Stepper(s.KeyHold, 10, 1, 600000, true, 64);
        var text = K.Field(s.Text, 420);
        var perChar = new Stepper(s.TypeDelay, 10, 1, 10000, true, 60);
        var press = K.Grid("auto,*", "auto,auto,auto,auto");
        FrameworkElement L(string t, int r) => K.T(t, 13, "Muted").M(0, 6, 16, 6).At(r, 0);
        press.Add(L("Key", 0), K.H(combo, K.T("   e, space, enter, ctrl+c …", 12, "Faint")).M(0, 4, 0, 4).At(0, 1),
                  L("Press it", 1), K.H(times, K.T("  time(s)", 13, "Muted")).M(0, 4, 0, 4).At(1, 1),
                  L("One every", 2), K.H(every, K.T("  ms", 13, "Muted")).M(0, 4, 0, 4).At(2, 1),
                  L("Hold each for", 3), K.H(hold, K.T("  ms  (games usually need 30 or more)", 13, "Muted")).M(0, 4, 0, 4).At(3, 1));
        var type = K.Grid("auto,*", "auto,auto");
        type.Add(L("Text", 0), text.Align(HorizontalAlignment.Left).M(0, 4, 0, 4).At(0, 1),
                 L("Typing speed", 1), K.H(perChar, K.T("  ms per character", 13, "Muted")).M(0, 4, 0, 4).At(1, 1));
        void Sync()
        {
            press.Visibility = mode.Value == "Press a key" ? Visibility.Visible : Visibility.Collapsed;
            type.Visibility = mode.Value == "Press a key" ? Visibility.Collapsed : Visibility.Visible;
        }
        mode.Changed += _ => Sync();
        Sync();
        var (commonView, commonApply) = SeqCommon(s);
        d.Body.Children.Add(mode.Align(HorizontalAlignment.Left).M(0, 2, 0, 8));
        d.Body.Children.Add(press);
        d.Body.Children.Add(type);
        d.Body.Children.Add(commonView);
        d.AddButton("Cancel", null);
        d.AddAction(isNew ? "Add step" : "Save", () =>
        {
            if (mode.Value == "Press a key")
            {
                var err = ComboError(combo.Text);
                if (err.Length > 0) { d.Error.Text = err; return; }
                s.KeyMode = "press";
                s.Combo = combo.Text.Trim().ToLowerInvariant();
                s.KeyTimes = (int)times.Value;
                s.KeyEvery = every.Value;
                s.KeyHold = hold.Value;
            }
            else
            {
                if (text.Text.Length == 0) { d.Error.Text = "Type the text first."; return; }
                s.KeyMode = "type";
                s.Text = text.Text;
                s.TypeDelay = perChar.Value;
            }
            commonApply();
            d.Finish(true);
        });
        return d.Run(combo) is true;
    }

    bool SeqGotoDialog(SeqStep s, bool isNew, int index)
    {
        int count = seq.Steps.Count + (isNew ? 1 : 0);
        if (count < 2)
        {
            Toast("Add the steps you want to jump between first.", "warn", 5000);
            return false;
        }
        var d = new Dialog(TopWindow, isNew ? "Jump to a step" : "Edit jump",
                           "When the sequence reaches this step it carries on from another step instead - always, or only when a picture is (or isn't) on screen.", "redo");
        var to = new Stepper(Math.Clamp(s.GoTo, 1, count), 1, 1, count, true, 52);
        var max = new Stepper(s.MaxJumps, 1, 0, 1000000, true, 60);
        var (condView, condApply) = SeqCondition(s);
        var hint = K.T("", 12, "Faint", null, true);
        hint.MaxWidth = 500;
        void Sync()
        {
            int t = (int)to.Value;
            hint.Text = t <= index ? $"Step {t} is above this one, so this makes a loop. Set a limit, or put a condition on it, so the run can get out again."
                                   : t == index + 2 ? "That is the very next step, so the jump changes nothing."
                                   : t == index + 3 ? $"Step {index + 2} is skipped whenever the jump happens."
                                   : $"Steps {index + 2} to {t - 1} are skipped whenever the jump happens.";
            if (t == index + 1) hint.Text = "That is this step itself - pick another one.";
        }
        to.Changed += _ => Sync();
        Sync();
        var g = K.Grid("auto,*", "auto,auto,auto");
        FrameworkElement L(string t, int r) => K.T(t, 13, "Muted").M(0, 6, 16, 6).At(r, 0);
        g.Add(L("Jump to step", 0), to.Align(HorizontalAlignment.Left).M(0, 4, 0, 4).At(0, 1),
              L("When", 1), condView.M(0, 4, 0, 4).At(1, 1),
              L("At most", 2), K.H(max, K.T("  times per full run   (0 = no limit)", 13, "Muted")).M(0, 4, 0, 4).At(2, 1));
        d.Body.Children.Add(g);
        d.Body.Children.Add(hint.M(0, 6, 0, 0));
        var (commonView, commonApply) = SeqCommon(s, false);
        d.Body.Children.Add(commonView);
        d.AddButton("Cancel", null);
        d.AddAction(isNew ? "Add step" : "Save", () =>
        {
            if ((int)to.Value == index + 1) { d.Error.Text = "A step can't jump to itself."; return; }
            var err = condApply();
            if (err.Length > 0) { d.Error.Text = err; return; }
            s.GoTo = (int)to.Value;
            s.MaxJumps = (int)max.Value;
            commonApply();
            d.Finish(true);
        });
        return d.Run() is true;
    }

    bool SeqStopDialog(SeqStep s, bool isNew)
    {
        var d = new Dialog(TopWindow, isNew ? "End the run here" : "Edit end step",
                           "Ends the run at this point - always, or only when a picture is (or isn't) on screen. Steps below it are not reached.", "stop");
        var mode = new Segmented(new[] { "End the whole run", "End this full run, start the next" }, s.StopMode == "loop" ? "End this full run, start the next" : "End the whole run");
        var (condView, condApply) = SeqCondition(s);
        var g = K.Grid("auto,*", "auto,auto");
        FrameworkElement L(string t, int r) => K.T(t, 13, "Muted").M(0, 6, 16, 6).At(r, 0);
        g.Add(L("What", 0), mode.Align(HorizontalAlignment.Left).M(0, 4, 0, 4).At(0, 1), L("When", 1), condView.M(0, 4, 0, 4).At(1, 1));
        d.Body.Children.Add(g);
        var (commonView, commonApply) = SeqCommon(s, false);
        d.Body.Children.Add(commonView);
        d.AddButton("Cancel", null);
        d.AddAction(isNew ? "Add step" : "Save", () =>
        {
            var err = condApply();
            if (err.Length > 0) { d.Error.Text = err; return; }
            s.StopMode = mode.Value.StartsWith("End this") ? "loop" : "run";
            commonApply();
            d.Finish(true);
        });
        return d.Run() is true;
    }

    bool SeqFocusDialog(SeqStep s, bool isNew)
    {
        var d = new Dialog(TopWindow, isNew ? "Bring a window to the front" : "Edit window step",
                           "Makes a window the active one, so the keys and clicks that follow go to it.", "expand");
        var (winView, win) = WindowField(s.WindowTitle, d, 240);
        var fail = new Segmented(new[] { "Carry on", "Stop the run" }, s.OnFail == "stop" && !isNew ? "Stop the run" : "Carry on");
        var g = K.Grid("auto,*", "auto,auto");
        FrameworkElement L(string t, int r) => K.T(t, 13, "Muted").M(0, 6, 16, 6).At(r, 0);
        g.Add(L("Window title has", 0), winView.M(0, 4, 0, 4).At(0, 1), L("If it isn't open", 1), fail.Align(HorizontalAlignment.Left).M(0, 4, 0, 4).At(1, 1));
        d.Body.Children.Add(g);
        var (commonView, commonApply) = SeqCommon(s);
        d.Body.Children.Add(commonView);
        d.AddButton("Cancel", null);
        d.AddAction(isNew ? "Add step" : "Save", () =>
        {
            if (win.Text.Trim().Length == 0) { d.Error.Text = "Type part of the window's title (for example Roblox), or use Pick."; return; }
            s.WindowTitle = win.Text.Trim();
            s.OnFail = fail.Value == "Stop the run" ? "stop" : "skip";
            commonApply();
            d.Finish(true);
        });
        return d.Run(win) is true;
    }

    bool SeqAlertDialog(SeqStep s, bool isNew)
    {
        var d = new Dialog(TopWindow, isNew ? "Alert" : "Edit alert", "Shows a message when the sequence gets here (and can play a sound), then carries on.", "info");
        var msg = K.Field(s.Message, 400);
        var sound = K.Switch("Play a sound", s.Sound);
        d.Body.Children.Add(K.H(K.T("Message  ", 13, "Muted"), msg).M(0, 4, 0, 8));
        d.Body.Children.Add(sound);
        var (commonView, commonApply) = SeqCommon(s);
        d.Body.Children.Add(commonView);
        d.AddButton("Cancel", null);
        d.AddAction(isNew ? "Add step" : "Save", () =>
        {
            s.Message = msg.Text.Trim();
            s.Sound = sound.IsChecked == true;
            commonApply();
            d.Finish(true);
        });
        return d.Run(msg) is true;
    }

    // ================================================================ always-watch
    static string GuardText(SeqGuard g) =>
        $"{g.Image}  →  {g.Does}   ·   looks every {Fmt.Num(g.Every)} s" + (g.Cooldown > 0 ? $", then leaves it for {Fmt.Num(g.Cooldown)} s" : "") + (g.Off ? "   ·   OFF" : "");

    /// The pictures this sequence watches for the whole time it runs. Edits `guards` in place; true if it was saved.
    bool SeqGuardsDialog(List<SeqGuard> guards, Window owner)
    {
        var d = new Dialog(owner, "Always-watch",
                           "Pictures TinyRec keeps looking for the whole time this sequence runs, whatever step it is on - a pop-up to click away, a “Reconnect” button, a “You died” screen.", "image");
        var work = guards.Select(g => g.Copy()).ToList();
        var list = new StackPanel { MinWidth = 500 };
        void Fill()
        {
            list.Children.Clear();
            if (work.Count == 0) list.Children.Add(K.T("Nothing is watched yet.", 13, "Muted").M(4, 6, 0, 6));
            for (int k = 0; k < work.Count; k++)
            {
                int idx = k;
                var row = K.Grid("*,auto,auto");
                var t = K.T(GuardText(work[k]), 13, work[k].Off ? "Faint" : "Text", null, true);
                row.Add(t.At(0, 0),
                        K.IconBtn("edit", () => { if (SeqGuardDialog(work[idx], d, false)) Fill(); }, "Edit", 28).M(6, 0, 0, 0).At(0, 1),
                        K.IconBtn("trash", () => { work.RemoveAt(idx); Fill(); }, "Remove", 28).At(0, 2));
                list.Children.Add(K.Box(row, "Card2", 9, 12, 7).M(0, 0, 0, 4));
            }
        }
        Fill();
        d.Body.Children.Add(list);
        d.Body.Children.Add(K.Btn("Add a picture to watch for", () =>
        {
            if (ImageNames().Count == 0) { d.Error.Text = "Your image library is empty - snip one on the Images page first."; return; }
            if (work.Count >= 20) { d.Error.Text = "20 is the most a sequence can watch."; return; }
            var g = new SeqGuard();
            if (!SeqGuardDialog(g, d, true)) return;
            work.Add(g);
            Fill();
        }, "primary", "plus", 32).Align(HorizontalAlignment.Left).M(0, 6, 0, 0));
        d.AddButton("Cancel", null);
        d.AddAction("Done", () => d.Finish(true));
        if (d.Run() is not true) return false;
        guards.Clear();
        guards.AddRange(work);
        return true;
    }

    bool SeqGuardDialog(SeqGuard g, Window owner, bool isNew)
    {
        var d = new Dialog(owner, isNew ? "Watch for a picture" : "Edit always-watch", "Whenever this picture shows up during the run, TinyRec does this.", "image");
        var (btn, get) = ImagePicker(g.Image, "Pick the picture");
        var act = new Segmented(new[] { "Click it", "Press a key", "Start again", "Stop the run", "Alert me" },
                                g.Action switch { "key" => "Press a key", "restart" => "Start again", "stop" => "Stop the run", "alert" => "Alert me", _ => "Click it" });
        act.Tip("Start again = give up on the current full run and begin again from step 1");
        var key = K.Field(g.Key, 90);
        var keyRow = K.H(key, K.T("   e, space, enter …", 12, "Faint"));
        var keyLabel = K.T("Key", 13, "Muted");
        var every = new Stepper(g.Every, 0.5, 0.2, 3600, false, 56);
        var cool = new Stepper(g.Cooldown, 1, 0, 86400, false, 56);
        var conf = new Stepper(g.Conf, 0.05, 0.5, 1, false, 56);
        var dx = new Stepper(g.Dx, 5, -5000, 5000, true, 56);
        var dy = new Stepper(g.Dy, 5, -5000, 5000, true, 56);
        var offRow = K.H(dx, K.T("  right,  ", 13, "Muted"), dy, K.T("  down from its centre", 13, "Muted"));
        var offLabel = K.T("Click offset", 13, "Muted");
        var on = K.Switch("Switched on", !g.Off);
        void Sync()
        {
            bool click = act.Value == "Click it", k = act.Value == "Press a key";
            keyLabel.Text = click ? "Then press" : "Key";
            keyRow.Visibility = keyLabel.Visibility = click || k ? Visibility.Visible : Visibility.Collapsed;
            offRow.Visibility = offLabel.Visibility = click ? Visibility.Visible : Visibility.Collapsed;
        }
        act.Changed += _ => Sync();
        Sync();
        var f = K.Grid("auto,*", "auto,auto,auto,auto,auto,auto,auto,auto");
        FrameworkElement L(string t, int r) => K.T(t, 13, "Muted").M(0, 6, 16, 6).At(r, 0);
        f.Add(L("Picture", 0), btn.Align(HorizontalAlignment.Left).M(0, 4, 0, 4).At(0, 1),
              L("When it shows", 1), act.Align(HorizontalAlignment.Left).M(0, 4, 0, 4).At(1, 1),
              keyLabel.M(0, 6, 16, 6).At(2, 0), keyRow.M(0, 4, 0, 4).At(2, 1),
              offLabel.M(0, 6, 16, 6).At(3, 0), offRow.M(0, 4, 0, 4).At(3, 1),
              L("Look every", 4), K.H(every, K.T("  seconds", 13, "Muted")).M(0, 4, 0, 4).At(4, 1),
              L("Then leave it for", 5), K.H(cool, K.T("  seconds, so it doesn't fire over and over", 13, "Muted")).M(0, 4, 0, 4).At(5, 1),
              L("Picture match", 6), K.H(conf, K.T("  how strict (0.85 - 0.95 is usual)", 13, "Muted")).M(0, 4, 0, 4).At(6, 1),
              on.M(0, 8, 0, 0).At(7, 0, 1, 2));
        d.Body.Children.Add(f);
        d.AddButton("Cancel", null);
        d.AddAction(isNew ? "Add" : "Save", () =>
        {
            if (get().Length == 0) { d.Error.Text = "Pick the picture first."; return; }
            string a = act.Value switch { "Press a key" => "key", "Start again" => "restart", "Stop the run" => "stop", "Alert me" => "alert", _ => "click" };
            string k = key.Text.Trim().ToLowerInvariant();
            if (a == "key" && k.Length == 0) { d.Error.Text = "Type the key to press."; return; }
            if (a is "key" or "click" && k.Length > 0 && !KeyNameOk(k)) { d.Error.Text = $"“{k}” isn't a key name. Try e, space, enter, esc, f5…"; return; }
            g.Image = get();
            g.Action = a;
            g.Key = a is "key" or "click" ? k : "";
            g.Every = every.Value;
            g.Cooldown = cool.Value;
            g.Conf = conf.Value;
            g.Dx = a == "click" ? (int)(dx.Parsed ?? 0) : 0;         // (an empty box means "no offset", not the lowest value)
            g.Dy = a == "click" ? (int)(dy.Parsed ?? 0) : 0;
            g.Off = on.IsChecked != true;
            g.Clean();
            d.Finish(true);
        });
        return d.Run() is true;
    }
}
