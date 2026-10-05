// DrawSettings.cs - the Draw page and the Settings page.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TinyRec.Core;
using TinyRec.Overlays;
using TinyRec.Ui;
using TinyRec.Win;

namespace TinyRec;

public partial class MainWindow
{
    static readonly string[] Swatches = { "#ff4d5e", "#ff9a3d", "#ffd23f", "#3ccf8e", "#2fd0c6", "#6f8bff", "#b07bff", "#ffffff" };
    Segmented toolSeg;
    readonly List<(Border b, string hex)> swatchButtons = new();
    TextBlock widthLab, drawCount;
    Button drawBtn;
    CheckBox showDrawings;

    // ================================================================ Draw
    FrameworkElement PageDraw()
    {
        var page = K.Grid("*,*", "auto,auto");
        var a = K.Grid("auto,*", "auto,auto,auto,auto");
        a.Add(K.CardTitle("Tool", "draw").At(0, 0, 1, 2));
        toolSeg = new Segmented(DrawLayer.Tools.Select(t => DrawLayer.Labels[t]), DrawLayer.Labels.GetValueOrDefault(draw.Tool, "Box"), v =>
        {
            draw.SetTool(DrawLayer.Labels.First(kv => kv.Value == v).Key);
        });
        a.Add(toolSeg.M(18, 4, 18, 12).At(1, 0, 1, 2));
        var sw = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var hex in Swatches)
        {
            var dot = new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(14), Margin = new Thickness(0, 0, 6, 0),
                                   Background = new SolidColorBrush(Ui.Theme.C(hex)), Cursor = System.Windows.Input.Cursors.Hand, BorderThickness = new Thickness(0) };
            var h = hex;
            dot.MouseLeftButtonUp += (_, _) => { draw.InkColor = h; DrawStyle(); };
            swatchButtons.Add((dot, hex));
            sw.Children.Add(dot);
        }
        sw.Children.Add(K.IconBtn("plus", CustomColor, "Custom colour", 30));
        a.Add(K.T("Colour", 13, "Muted").M(20, 6, 14, 6).At(2, 0), sw.M(0, 6, 16, 6).At(2, 1));
        widthLab = K.T("", 13, "Text", FontWeights.SemiBold);
        var slider = K.Slide(1, 16, draw.InkWidth, v => { draw.InkWidth = Math.Round(v); DrawStyle(); }, 220);
        slider.IsSnapToTickEnabled = true;
        slider.TickFrequency = 1;
        a.Add(K.T("Thickness", 13, "Muted").M(20, 6, 14, 20).At(3, 0), K.H(slider, widthLab.M(10, 0, 0, 0)).M(0, 6, 16, 20).At(3, 1));
        page.Add(K.Card(a).M(0, 0, 7, 12).At(0, 0));

        var b = new StackPanel();
        b.Children.Add(K.CardTitle("Screen layer", "mini"));
        drawBtn = K.Btn("", () => draw.Toggle(), "primary", "draw", 48);
        drawBtn.FontSize = 15;
        b.Children.Add(drawBtn.M(18, 4, 18, 10));
        var r = K.Grid("*,*");
        r.Add(K.Btn("Undo last", draw.Undo, "secondary", "undo").M(0, 0, 4, 0).At(0, 0),
              K.Btn("Clear all", () => { if (draw.Shapes.Count > 0 && Dialog.Confirm(TopWindow, "Clear all drawings?", $"Removes all {draw.Shapes.Count} shapes from the screen.", "Clear", true)) draw.Clear(); }, "secondary", "trash").M(4, 0, 0, 0).At(0, 1));
        b.Children.Add(r.M(18, 0, 18, 0));
        showDrawings = K.Switch("Show drawings", true, v => draw.SetVisible(v));
        b.Children.Add(showDrawings.M(18, 14, 18, 6));
        drawCount = K.T("", 12, "Muted");
        b.Children.Add(drawCount.M(20, 0, 20, 16));
        page.Add(K.Card(b).M(7, 0, 0, 12).At(0, 1));

        var how = new StackPanel();
        how.Children.Add(K.CardTitle("How it works", "info"));
        foreach (var t in new[]
                 {
                     "Drawings sit on a see-through layer over your whole screen. Your clicks pass straight through it, so you can keep working.",
                     $"Press Start drawing (or {Hint("draw_toggle")}) and drag to draw. A toolbar appears at the top of the screen: switch tools, Undo, and Done. Esc or right-click also finishes.",
                     "Marker drops a crosshair where you click. Text asks for a label and places it.",
                     "Drawings are saved and come back next time. They're hidden while a recording plays, so they can't confuse image matching.",
                 })
            how.Children.Add(K.T("•  " + t, 13, "Muted", null, true).M(22, 3, 22, 3));
        how.Children.Add(new Border { Height = 12 });
        page.Add(K.Card(how).At(1, 0, 1, 2));
        DrawStyle();
        return Scroll(page);
    }

    void DrawStyle()
    {
        if (widthLab == null) return;
        widthLab.Text = ((int)draw.InkWidth).ToString();
        foreach (var (b, hex) in swatchButtons)
        {
            bool on = string.Equals(hex, draw.InkColor, StringComparison.OrdinalIgnoreCase);
            b.BorderThickness = new Thickness(on ? 3 : 0);
            b.SetResourceReference(Border.BorderBrushProperty, "Text");
        }
    }

    void CustomColor()
    {
        using var d = new System.Windows.Forms.ColorDialog { FullOpen = true };
        try { d.Color = System.Drawing.ColorTranslator.FromHtml(draw.InkColor); } catch { }
        if (d.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        draw.InkColor = $"#{d.Color.R:x2}{d.Color.G:x2}{d.Color.B:x2}";
        DrawStyle();
    }

    void OnDrawChanged()
    {
        var label = DrawLayer.Labels.GetValueOrDefault(draw.Tool);
        if (toolSeg != null && label != null && toolSeg.Value != label) toolSeg.Set(label, false);
        RefreshButtons();
    }

    void DrawButtons()
    {
        if (drawBtn == null) return;
        bool d = draw.Drawing;
        K.SetBtnContent(drawBtn, d ? $"Finish drawing   ({Hint("draw_toggle")})" : $"Start drawing   ({Hint("draw_toggle")})", d ? "check" : "draw");
        drawBtn.SetResourceReference(BackgroundProperty, d ? "Green" : "Accent");
        drawCount.Text = $"{draw.Shapes.Count} shape{(draw.Shapes.Count == 1 ? "" : "s")} on screen";
    }

    // ================================================================ Settings
    readonly Dictionary<string, Border> hkChips = new();
    CheckBox setTopMain, setTopMini;
    Segmented themeSeg, scaleSeg, runSeg;
    readonly List<(Border b, string name)> accentDots = new();
    Border customDot;
    StackPanel borderChips;
    WrapPanel customSwatches;
    TextBlock customState;
    Slider glowSlide, glowSizeSlide;
    TextBlock glowLab, glowSizeLab;
    CheckBox sweepSwitch;
    Border sweepChip;
    Button looksBtn;
    bool customLoading;

    static readonly (string key, string label)[] CustomParts =
    {
        ("Bg", "Window background"), ("Sidebar", "Sidebar"), ("Card", "Cards"), ("Card2", "Boxes inside cards"),
        ("Field", "Text boxes"), ("Hover", "Hover highlight"), ("Line", "Lines and borders"), ("Track", "Slider track"),
        ("Text", "Text"), ("Muted", "Secondary text"), ("Faint", "Faint text"),
        ("Red", "Record and errors"), ("Green", "OK and success"), ("Amber", "Warnings and paused"),
    };

    // ================================================================ the custom theme
    UIElement CustomCard()
    {
        customState = K.T("", 12, "Muted", null, true);
        customSwatches = new WrapPanel();
        looksBtn = K.Btn("Saved looks", LooksMenu, "secondary", "library", 32);
        var top = K.Grid("*,auto");
        top.Add(customState.At(0, 0),
                K.H(K.T("Start from", 12, "Muted").M(0, 0, 8, 0),
                    K.Btn("Neon", () => CustomStart("neon"), "secondary", null, 32).M(0, 0, 6, 0),
                    K.Btn("Dark", () => CustomStart("dark"), "secondary", null, 32).M(0, 0, 6, 0),
                    K.Btn("Light", () => CustomStart("light"), "secondary", null, 32).M(0, 0, 14, 0),
                    looksBtn).At(0, 1));

        sweepSwitch = K.Switch("Two-colour sweep on buttons and sliders", cfg.Custom.Sweep, v =>
        {
            if (customLoading) return;
            EditCustom(() => cfg.Custom.Sweep = v);
        });
        sweepChip = new Border { Width = 34, Height = 26, CornerRadius = new CornerRadius(7), Cursor = System.Windows.Input.Cursors.Hand, BorderThickness = new Thickness(1) };
        sweepChip.SetResourceReference(Border.BorderBrushProperty, "Line");
        sweepChip.Tip("The colour the sweep ends in - click to choose");
        sweepChip.MouseLeftButtonUp += (_, _) => CustomPickSecond();
        var sweepRow = K.H(sweepSwitch, K.T("     ends in", 12, "Muted").M(0, 0, 8, 0), sweepChip,
                           K.Btn("Auto", () => EditCustom(() => cfg.Custom.Accent2 = ""), "ghost", null, 28).Tip("Work the second colour out from the accent").M(6, 0, 0, 0));

        glowLab = K.T("", 13, "Text", FontWeights.SemiBold);
        glowSizeLab = K.T("", 13, "Text", FontWeights.SemiBold);
        glowSlide = K.Slide(0, 100, cfg.Custom.Glow, v =>
        {
            if (customLoading) return;
            EditCustom(() => cfg.Custom.Glow = (int)Math.Round(v));
        }, 180);
        glowSizeSlide = K.Slide(0, 8, cfg.Custom.GlowSize, v =>
        {
            if (customLoading) return;
            EditCustom(() => cfg.Custom.GlowSize = (int)Math.Round(v));
        }, 180);
        var glowRow = K.H(K.T("Glow strength", 13, "Muted").M(0, 0, 12, 0), glowSlide, glowLab.M(10, 0, 28, 0),
                          K.T("Glow size", 13, "Muted").M(0, 0, 12, 0), glowSizeSlide, glowSizeLab.M(10, 0, 0, 0));

        var body = K.V(top.M(20, 0, 16, 10),
                       K.T("Click any colour to change it. The app updates while you pick, and Cancel puts the old colour back.", 12, "Faint", null, true).M(20, 0, 16, 8),
                       customSwatches.M(16, 0, 12, 6), sweepRow.M(20, 6, 16, 6), glowRow.M(20, 6, 16, 6),
                       K.T("The accent colour is set under Appearance. Shades in between (soft backgrounds, hover on red) follow the colours you choose.", 12, "Faint", null, true).M(20, 4, 16, 16));
        CustomRefresh();
        return body;
    }

    /// Make the Custom theme the active one. The first time, it starts as a copy of whatever theme is showing.
    void UseCustom()
    {
        if (cfg.Appearance == "custom") return;
        if (cfg.Custom.Colors.Count == 0)
        {
            bool neon = Ui.Theme.Neon;
            cfg.Custom.Colors = Ui.Theme.Preset(neon ? "neon" : Ui.Theme.Dark ? "dark" : "light");
            cfg.Custom.Sweep = neon;
            cfg.Custom.Glow = neon ? 55 : 0;
            cfg.Custom.GlowSize = 5;
        }
        cfg.Appearance = "custom";
        themeSeg?.Set("Custom", false);
    }

    void EditCustom(Action change)
    {
        UseCustom();
        change();
        ThemeChanged(false);
        CustomRefresh();
    }

    void CustomStart(string which)
    {
        cfg.Custom.Colors = new Dictionary<string, string>();          // so UseCustom doesn't seed over it
        cfg.Appearance = "custom";
        themeSeg?.Set("Custom", false);
        cfg.Custom.Colors = Ui.Theme.Preset(which);
        cfg.Custom.Sweep = which == "neon";
        cfg.Custom.Accent2 = "";
        cfg.Custom.Glow = which == "neon" ? 55 : 0;
        cfg.Custom.GlowSize = 5;
        ThemeChanged(false);
        CustomRefresh();
        Toast($"Custom theme now starts from {Cap(which)} - change anything you like.", "ok");
    }

    void CustomRefresh()
    {
        if (customSwatches == null) return;
        bool on = cfg.Appearance == "custom";
        customState.Text = on ? "Custom is the active theme."
                              : "Not in use right now. Changing anything here switches to the Custom theme (it starts as a copy of the theme you're on).";
        customSwatches.Children.Clear();
        foreach (var (key, label) in CustomParts)
        {
            var hex = on || cfg.Custom.Colors.Count > 0 ? Ui.Theme.CustomColor(cfg.Custom, key) : Ui.Theme.Hex(Ui.Theme.Col(key));
            var sw = new Border { Width = 34, Height = 26, CornerRadius = new CornerRadius(7), Background = new SolidColorBrush(Ui.Theme.C(hex)), BorderThickness = new Thickness(1) };
            sw.SetResourceReference(Border.BorderBrushProperty, "Muted");
            var item = K.H(sw, K.T(label, 13, "Text").M(10, 0, 0, 0));
            var cell = new Border { Width = 214, Padding = new Thickness(4, 5, 4, 5), CornerRadius = new CornerRadius(8), Child = item,
                                    Cursor = System.Windows.Input.Cursors.Hand, Background = Brushes.Transparent };
            cell.Tip(hex + "  -  click to change");
            cell.MouseEnter += (_, _) => cell.SetResourceReference(Border.BackgroundProperty, "Hover");
            cell.MouseLeave += (_, _) => cell.Background = Brushes.Transparent;
            var k = key;
            var l = label;
            cell.MouseLeftButtonUp += (_, _) => CustomPick(k, l);
            customSwatches.Children.Add(cell);
        }
        customLoading = true;
        sweepSwitch.IsChecked = cfg.Custom.Sweep;
        glowSlide.Value = cfg.Custom.Glow;
        glowSizeSlide.Value = cfg.Custom.GlowSize;
        customLoading = false;
        glowLab.Text = cfg.Custom.Glow == 0 ? "off" : cfg.Custom.Glow + "%";
        glowSizeLab.Text = cfg.Custom.GlowSize == 0 ? "off" : cfg.Custom.GlowSize + " px";
        sweepChip.Background = new SolidColorBrush(on ? Ui.Theme.Col("Accent2") : Ui.Theme.Col("Accent"));
        sweepChip.Opacity = cfg.Custom.Sweep ? 1 : 0.4;
    }

    void CustomPick(string key, string label)
    {
        var before = cfg.Custom.Copy();
        var beforeLook = cfg.Appearance;
        UseCustom();
        ThemeChanged(false);
        CustomRefresh();
        var got = ColorPick.Ask(TopWindow, label, Ui.Theme.CustomColor(cfg.Custom, key), hex =>
        {
            cfg.Custom.Colors[key] = hex;
            ThemeChanged(false);
        });
        if (got == null)
        {
            cfg.Custom = before;
            cfg.Appearance = beforeLook;
            themeSeg?.Set(Cap(beforeLook), false);
        }
        else cfg.Custom.Colors[key] = got;
        ThemeChanged(false);
        CustomRefresh();
    }

    void CustomPickSecond()
    {
        var before = cfg.Custom.Copy();
        var beforeLook = cfg.Appearance;
        UseCustom();
        cfg.Custom.Sweep = true;
        ThemeChanged(false);
        var got = ColorPick.Ask(TopWindow, "Sweep - second colour", Ui.Theme.Hex(Ui.Theme.Col("Accent2")), hex =>
        {
            cfg.Custom.Accent2 = hex;
            ThemeChanged(false);
        });
        if (got == null)
        {
            cfg.Custom = before;
            cfg.Appearance = beforeLook;
            themeSeg?.Set(Cap(beforeLook), false);
        }
        else cfg.Custom.Accent2 = got;
        ThemeChanged(false);
        CustomRefresh();
    }

    /// Saved looks: keep the custom theme (with its accent) under a name, and switch between them.
    void LooksMenu()
    {
        var items = new List<(string, Action)>();
        foreach (var name in cfg.Looks.Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
        {
            var n = name;
            items.Add(("Use “" + n + "”", () =>
            {
                cfg.Custom = cfg.Looks[n].Copy();
                if (!string.IsNullOrWhiteSpace(cfg.Custom.Accent)) cfg.Accent = cfg.Custom.Accent;
                cfg.Appearance = "custom";
                themeSeg?.Set("Custom", false);
                ThemeChanged(false);
                CustomRefresh();
                Toast($"Switched to “{n}”", "ok");
            }));
        }
        if (items.Count > 0) items.Add(("-", null));
        items.Add(("Save this look as…", () =>
        {
            var name = Dialog.AskString(TopWindow, "Save this look", "Name for the current custom theme:", "",
                                        t => string.IsNullOrWhiteSpace(t) ? "Give it a name." : null, "Save");
            if (name == null) return;
            UseCustom();
            var look = cfg.Custom.Copy();
            look.Accent = cfg.Accent;
            cfg.Looks[name.Trim()] = look;
            ThemeChanged(false);
            CustomRefresh();
            Toast($"Saved as “{name.Trim()}”", "ok");
        }));
        if (cfg.Looks.Count > 0)
        {
            items.Add(("-", null));
            foreach (var name in cfg.Looks.Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
            {
                var n = name;
                items.Add(("Remove “" + n + "”", () =>
                {
                    if (!Dialog.Confirm(TopWindow, "Remove this saved look?", $"“{n}” will be removed from the list. The theme you're using now doesn't change.", "Remove", true)) return;
                    cfg.Looks.Remove(n);
                }));
            }
        }
        K.Menu(items.ToArray()).Popup(looksBtn);
    }

    FrameworkElement PageSettings()
    {
        var page = K.Grid("*,*", "auto,auto,auto,auto,auto,auto,auto");
        Border Section(string title, string icon, UIElement body, int row, int col, int span = 1)
        {
            var s = K.V(K.CardTitle(title, icon), body);
            var c = K.Card(s);
            c.Margin = new Thickness(col == 0 ? 0 : 6, 0, col == 0 && span == 1 ? 6 : 0, 12);
            page.Add(c.At(row, col, 1, span));
            return c;
        }
        FrameworkElement Row(string label, UIElement ctl)
        {
            var g = K.Grid("150,*");
            g.Add(K.T(label, 13, "Muted").At(0, 0), ((FrameworkElement)ctl).At(0, 1));
            return g.M(20, 5, 16, 5);
        }

        // appearance
        themeSeg = new Segmented(new[] { "Neon", "Dark", "Light", "System", "Custom" }, Cap(cfg.Appearance), v =>
        {
            if (v == "Custom") UseCustom();
            cfg.Appearance = v.ToLowerInvariant();
            ThemeChanged(false);
            CustomRefresh();
        }, 58);
        var acc = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (name, (_, dark)) in Ui.Theme.Accents)
        {
            var dot = new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Margin = new Thickness(0, 0, 6, 0),
                                   Background = new SolidColorBrush(Ui.Theme.C(dark[0])), Cursor = System.Windows.Input.Cursors.Hand };
            dot.SetResourceReference(Border.BorderBrushProperty, "Text");
            var n = name;
            dot.MouseLeftButtonUp += (_, _) =>
            {
                cfg.Accent = n;
                ThemeChanged(false);
            };
            accentDots.Add((dot, name));
            acc.Children.Add(dot);
        }
        // ...or any colour at all
        customDot = new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Margin = new Thickness(4, 0, 0, 0), Cursor = System.Windows.Input.Cursors.Hand,
                                 Child = K.Icon("plus", 12, "OnAccent").Align(HorizontalAlignment.Center) };
        customDot.SetResourceReference(Border.BorderBrushProperty, "Text");
        customDot.Tip("Pick any colour");
        customDot.MouseLeftButtonUp += (_, _) =>
        {
            var before = cfg.Accent;
            var start = cfg.Accent.StartsWith("#") ? cfg.Accent : Ui.Theme.Hex(Ui.Theme.Col("Accent"));
            var got = ColorPick.Ask(TopWindow, "Accent colour", start, hex => { cfg.Accent = hex; ThemeChanged(false); });
            cfg.Accent = got ?? before;
            ThemeChanged(false);
        };
        acc.Children.Add(customDot);
        scaleSeg = new Segmented(new[] { "90%", "100%", "110%", "125%" }, $"{(int)Math.Round(cfg.UiScale * 100)}%", v =>
        {
            cfg.UiScale = int.Parse(v.TrimEnd('%')) / 100.0;
            ApplyScale();
        }, 54);
        Section("Appearance", "draw", K.V(K.T("Theme", 13, "Muted").M(20, 5, 16, 6), themeSeg.M(20, 0, 16, 8), Row("Accent colour", acc), Row("Size", scaleSeg), new Border { Height = 12 }), 0, 0);

        // the frame around the window
        borderChips = new StackPanel { Orientation = Orientation.Horizontal };
        var speedSeg = new Segmented(new[] { "Slow", "Medium", "Fast" }, Cap(cfg.BorderSpeed), v => { cfg.BorderSpeed = v.ToLowerInvariant(); UpdateBorder(); }, 60);
        var whenSeg = new Segmented(new[] { "Always", "While running" }, cfg.BorderWhen == "running" ? "While running" : "Always",
                                    v => { cfg.BorderWhen = v == "Always" ? "always" : "running"; UpdateBorder(); }, 60);
        var border = K.V(K.Switch("Glowing border around this window", cfg.BorderOn, v => { cfg.BorderOn = v; UpdateBorder(); }).M(20, 5, 16, 8),
                         Row("Colours", borderChips),
                         K.T("One colour breathes brighter and dimmer. With two or more, the border fades from one to the next.", 12, "Faint", null, true).M(20, 2, 16, 8),
                         Row("Speed", speedSeg), Row("Show it", whenSeg),
                         K.Switch("Pink while recording, accent colour while playing", cfg.BorderByState, v => { cfg.BorderByState = v; UpdateBorder(); }).M(20, 8, 16, 4),
                         K.T("The border rests while the window is hidden or minimised, and the mini bar never has one.", 12, "Faint", null, true).M(20, 4, 16, 14));
        Section("Window border", "target", border, 1, 0);
        BorderChips();

        // animations: a master switch, then one switch each
        var subs = new List<CheckBox>();
        CheckBox Sub(string text, bool on, Action<bool> set)
        {
            var c = K.Switch(text, on, v =>
            {
                set(v);
                ApplyFx();
                ThemeChanged(false);
                UpdateBorder();
            });
            subs.Add(c);
            return c;
        }
        void SubsEnable()
        {
            foreach (var c in subs)
            {
                c.IsEnabled = cfg.Animations;
                c.Opacity = cfg.Animations ? 1 : 0.5;
            }
        }
        var anim = K.V(K.Switch("Animations", cfg.Animations, v =>
                       {
                           cfg.Animations = v;
                           ApplyFx();
                           ThemeChanged(false);
                           UpdateBorder();
                           SubsEnable();
                       }).M(20, 5, 16, 6),
                       K.T("The master switch. Each one below can also be turned off by itself.", 12, "Faint", null, true).M(20, 0, 16, 8),
                       Sub("Pulsing ring on Record while recording", cfg.AnimRing, v => cfg.AnimRing = v).M(34, 5, 16, 5),
                       Sub("Buttons light up on hover (Neon, Custom)", cfg.AnimHover, v => cfg.AnimHover = v).M(34, 5, 16, 5),
                       Sub("Pages fade in", cfg.AnimPages, v => cfg.AnimPages = v).M(34, 5, 16, 5),
                       Sub("The window border moves (off = steady)", cfg.AnimBorder, v => cfg.AnimBorder = v).M(34, 5, 16, 8),
                       K.T("Nothing animates while the window is hidden or minimised, so none of this costs anything during a game.", 12, "Faint", null, true).M(20, 4, 16, 14));
        SubsEnable();
        Section("Animations", "wand", anim, 1, 1);

        // the custom theme
        Section("Custom theme", "draw", CustomCard(), 2, 0, 2);

        // window
        runSeg = new Segmented(new[] { "Stay", "Mini bar", "Hide" }, cfg.DuringRun switch { "stay" => "Stay", "hide" => "Hide", _ => "Mini bar" },
                               v => cfg.DuringRun = v switch { "Stay" => "stay", "Hide" => "hide", _ => "mini" }, 64);
        setTopMain = K.Switch("Main window always on top", cfg.TopmostFull, v => { cfg.TopmostFull = v; Topmost = v; topmostSwitch.IsChecked = v; });
        setTopMini = K.Switch("Mini bar always on top", cfg.TopmostMini, v => SetMiniTopmost(v));
        var win = K.V(Row("While recording / playing", runSeg),
                      K.T("What the main window does while a recording plays or records: stay put, shrink to the mini bar, or hide completely.", 12, "Faint", null, true).M(20, 0, 16, 6),
                      setTopMain.M(20, 5, 16, 5), setTopMini.M(20, 5, 16, 5),
                      K.Switch("Open as the mini bar", cfg.StartMini, v => cfg.StartMini = v).M(20, 5, 16, 16));
        Section("Window", "mini", win, 0, 1);

        // hotkeys
        var hk = new StackPanel();
        hk.Children.Add(K.T("They work in every program, even when TinyRec is hidden or minimised.", 12, "Muted", null, true).M(20, 0, 16, 8));
        var grid = K.Grid("*,auto,auto,24,*,auto,auto");
        int half = (HotkeyTable.Labels.Length + 1) / 2;
        for (int i = 0; i < half; i++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (int i = 0; i < HotkeyTable.Labels.Length; i++)
        {
            var (action, label) = HotkeyTable.Labels[i];
            int col = i < half ? 0 : 4, row = i % half;
            var chip = K.Chip("", action == "panic" ? "Red" : "Accent", "Card2", 12);
            hkChips[action] = chip;
            var a = action;
            grid.Add(K.T(label, 13).M(0, 5, 10, 5).At(row, col), chip.M(0, 0, 8, 0).At(row, col + 1),
                     K.Btn("Change", () => Rebind(a), "ghost", null, 28).At(row, col + 2));
        }
        hk.Children.Add(grid.M(20, 0, 16, 6));
        hk.Children.Add(K.Switch("Hotkeys don't reach the program you're in (recommended)", cfg.BlockHotkeys, v => { cfg.BlockHotkeys = v; router.Block = v; }).M(20, 8, 16, 4));
        hk.Children.Add(K.Btn("Reset hotkeys to defaults", ResetHotkeys, "secondary", "undo").Align(HorizontalAlignment.Left).M(20, 8, 16, 16));
        Section("Hotkeys", "keyboard", hk, 3, 0, 2);

        // recording + playback
        Section("Recording", "record", K.V(
            K.Switch("Record mouse movement (off = clicks, keys and scrolls only)", cfg.RecordMoves, v => cfg.RecordMoves = v).M(20, 5, 16, 5),
            K.Switch("Start the recording at your first action", cfg.TrimStart, v => cfg.TrimStart = v).M(20, 5, 16, 5),
            K.Switch("Cut the mouse trip to the Stop button at the end", cfg.TrimEnd, v => cfg.TrimEnd = v).M(20, 5, 16, 5),
            K.Switch("Thin mouse-move points automatically (smaller files)", cfg.AutoThin, v => cfg.AutoThin = v).M(20, 5, 16, 16)), 4, 0);
        Section("Playback", "play", K.V(
            K.Switch("Hide drawings while playing", cfg.HideDrawingsWhilePlaying, v => cfg.HideDrawingsWhilePlaying = v).M(20, 5, 16, 5),
            K.Switch("Show a REC / PLAY badge at the top of the screen", cfg.ShowIndicator, v => cfg.ShowIndicator = v).M(20, 5, 16, 5),
            K.T("The badge is hidden automatically for recordings with image steps.", 12, "Faint", null, true).M(20, 4, 16, 16)), 4, 1);

        // image steps: where to look, and the run log
        var areaSeg = new Segmented(new[] { "Whole screen", "One window" }, cfg.ImageArea == "window" ? "One window" : "Whole screen", null, 96);
        var winBox = K.Field(cfg.ImageWindow, 200);
        winBox.Tip("Part of the window's title is enough, for example Roblox");
        var winPick = K.Btn("Pick", null, "secondary", null, 32);
        winPick.Tip("Choose from the windows that are open right now");
        winPick.Click += (_, _) =>
        {
            var titles = ScreenGrab.WindowTitles().Where(t => !t.Contains("TinyRec")).Distinct().Take(24).ToList();
            if (titles.Count == 0) { Toast("No other windows are open.", "warn"); return; }
            K.Menu(titles.Select(t => (t.Length > 60 ? t[..60] + "…" : t, (Action)(() => winBox.Text = t))).ToArray()).Popup(winPick);
        };
        var winState = K.T("", 12, "Faint", null, true);
        var winRow = Row("Window title has", K.H(winBox, winPick.M(8, 0, 0, 0)));
        void AreaSync()
        {
            bool one = cfg.ImageArea == "window";
            winRow.Visibility = one ? Visibility.Visible : Visibility.Collapsed;
            if (!one) winState.Text = "Image steps look at every monitor.";
            else if (string.IsNullOrWhiteSpace(cfg.ImageWindow)) winState.Text = "Type part of the window's title (for example Roblox). Until then the whole screen is searched.";
            else
            {
                var r = ScreenGrab.FindWindowRect(cfg.ImageWindow.Trim());
                winState.Text = r == null ? "That window isn't open right now. While it's closed, the whole screen is searched."
                                          : $"Found it: {r.Value.w} × {r.Value.h} pixels. Image steps only look inside it - faster, and nothing outside it can be clicked by mistake.";
            }
        }
        areaSeg.Changed += v =>
        {
            cfg.ImageArea = v == "One window" ? "window" : "screen";
            AreaSync();
        };
        winBox.TextChanged += (_, _) =>
        {
            cfg.ImageWindow = winBox.Text.Trim();
            AreaSync();
        };
        AreaSync();
        Section("Image steps", "image", K.V(Row("Look in", areaSeg), winRow, winState.M(20, 2, 16, 16)), 5, 0);
        Section("Run log", "log", K.V(
            K.Switch("Save a log file for every run", cfg.KeepLogs, v => cfg.KeepLogs = v).M(20, 5, 16, 5),
            K.T("Each run writes what happened and when (which image showed, how long it waited, what was played) to data\\logs. The newest 60 are kept.", 12, "Faint", null, true).M(20, 2, 16, 8),
            K.Btn("Open logs folder", () => OpenFolder(LogDir), "secondary", "folder").Align(HorizontalAlignment.Left).M(20, 0, 16, 16)), 5, 1);

        // files
        var files = K.V(K.T(Data, 13).M(20, 0, 16, 2),
                        K.T("Recordings, images, drawings and settings all live here. Copy this folder to back everything up.", 12, "Muted", null, true).M(20, 0, 16, 8),
                        K.H(K.Btn("Open data folder", () => OpenFolder(Data), "secondary", "folder"), K.Btn("Recordings", () => OpenFolder(RecDir)).M(8, 0, 0, 0),
                            K.Btn("Images", () => OpenFolder(ImgDir)).M(8, 0, 0, 0)).M(20, 0, 16, 8),
                        K.T($"{AppName} v{Version}  ·  no network, no telemetry, nothing installed outside this folder", 12, "Faint").M(20, 0, 16, 16));
        Section("Your files", "folder", files, 6, 0, 2);
        AccentDots();
        return Scroll(page);
    }

    static string Cap(string s) => string.IsNullOrEmpty(s) ? "Dark" : char.ToUpperInvariant(s[0]) + s[1..];

    void AccentDots()
    {
        foreach (var (b, name) in accentDots) b.BorderThickness = new Thickness(name == cfg.Accent ? 3 : 0);
        if (customDot == null) return;
        bool custom = cfg.Accent.StartsWith("#") && Ui.Theme.CleanHex(cfg.Accent) != null;
        customDot.BorderThickness = new Thickness(custom ? 3 : 0);
        customDot.Background = custom ? new SolidColorBrush(Ui.Theme.C(cfg.Accent))
                                      : new LinearGradientBrush(new GradientStopCollection
                                        {
                                            new(Ui.Theme.C("#ff3d86"), 0), new(Ui.Theme.C("#ffb547"), 0.3), new(Ui.Theme.C("#3de0b0"), 0.55),
                                            new(Ui.Theme.C("#3d7bff"), 0.8), new(Ui.Theme.C("#9d7bff"), 1),
                                        }, new Point(0, 0), new Point(1, 1));
    }

    /// The row of colour chips for the window border: click one to change it, x to remove it, + to add another (up to 4).
    void BorderChips()
    {
        borderChips.Children.Clear();
        for (int i = 0; i < cfg.BorderColors.Count; i++)
        {
            int idx = i;
            var hex = cfg.BorderColors[i];
            var sw = new Border { Width = 34, Height = 26, CornerRadius = new CornerRadius(7), Background = new SolidColorBrush(Ui.Theme.C(hex)),
                                  Cursor = System.Windows.Input.Cursors.Hand, BorderThickness = new Thickness(1) };
            sw.SetResourceReference(Border.BorderBrushProperty, "Line");
            sw.Tip(hex + "  -  click to change");
            sw.MouseLeftButtonUp += (_, _) => BorderPick(idx);
            borderChips.Children.Add(sw);
            borderChips.Children.Add(K.IconBtn("close", () =>
            {
                cfg.BorderColors.RemoveAt(idx);
                BorderChips();
                UpdateBorder();
            }, "Remove this colour", 22, "Faint").M(1, 0, 8, 0));
        }
        if (cfg.BorderColors.Count < 4) borderChips.Children.Add(K.Btn("Add", () => BorderPick(-1), "secondary", "plus", 28));
        if (cfg.BorderColors.Count == 0) borderChips.Children.Add(K.T("   none chosen - the accent colour is used", 12, "Faint"));
    }

    /// index -1 = add a new colour. The border previews every colour tried while the picker is open.
    void BorderPick(int index)
    {
        var before = cfg.BorderColors.ToList();
        var start = index >= 0 ? cfg.BorderColors[index] : before.Count > 0 ? before[^1] : Ui.Theme.Hex(Ui.Theme.Col("Accent"));
        if (index < 0)
        {
            cfg.BorderColors.Add(start);
            index = cfg.BorderColors.Count - 1;
        }
        int at = index;
        BorderPreview = true;
        UpdateBorder();
        var got = ColorPick.Ask(TopWindow, "Border colour", start, hex =>
        {
            cfg.BorderColors[at] = hex;
            UpdateBorder();
        });
        BorderPreview = false;
        if (got == null) cfg.BorderColors = before;
        else cfg.BorderColors[at] = got;
        BorderChips();
        UpdateBorder();
    }

    void ThemeChanged(bool fromSystem)
    {
        ApplyTheme();
        AccentDots();
        RefreshTable();                    // table row colours are resolved per row
        mini?.ThemeChanged();
    }

    void HotkeyChips()
    {
        foreach (var (a, chip) in hkChips) K.SetChip(chip, a == pendingRebind ? "Press keys…" : Hint(a));
    }

    void SettingsSync()
    {
        if (setTopMini != null && setTopMini.IsChecked != cfg.TopmostMini) setTopMini.IsChecked = cfg.TopmostMini;
    }
}
