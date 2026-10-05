// Theme.cs - colours for dark / light / neon / "match Windows", six accent colours plus any colour you pick. Switches live.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using TinyRec.Core;

namespace TinyRec.Ui;

public static class Theme
{
    static readonly Dictionary<string, (string light, string dark, string neon)> Palette = new()
    {
        ["Bg"] = ("#f3f4f7", "#0d0f13", "#06071a"),
        ["Sidebar"] = ("#ffffff", "#12151a", "#090b22"),
        ["Card"] = ("#ffffff", "#171a20", "#0d1030"),
        ["Card2"] = ("#eef0f4", "#1e2229", "#151942"),
        ["Hover"] = ("#e3e6ec", "#252a33", "#1e2458"),
        ["Line"] = ("#e3e6ec", "#252932", "#262d6a"),
        ["Text"] = ("#161a22", "#e9ebf1", "#eef0ff"),
        ["Muted"] = ("#667085", "#8c94a4", "#949bd0"),
        ["Faint"] = ("#9aa1ae", "#5b6270", "#5d649c"),
        ["Field"] = ("#e9ecf1", "#1e2229", "#12163a"),
        ["Track"] = ("#cfd4dd", "#343a46", "#2b3276"),
        ["Red"] = ("#e5484d", "#ff5a63", "#ff3d86"),
        ["RedHover"] = ("#cc3b40", "#e84b54", "#e83478"),
        ["RedSoft"] = ("#fdecec", "#3a1a1e", "#3a1036"),
        ["Green"] = ("#1f9d63", "#3ccf8e", "#3de0b0"),
        ["GreenSoft"] = ("#e6f6ee", "#14301f", "#0f3034"),
        ["Amber"] = ("#c27c00", "#ffb547", "#ffc65c"),
        ["AmberSoft"] = ("#fff4e0", "#3a2c12", "#3a2c2a"),
        ["OnAccent"] = ("#ffffff", "#ffffff", "#ffffff"),
    };

    public static readonly Dictionary<string, (string[] light, string[] dark)> Accents = new()
    {
        // accent, hover, soft
        ["blue"] = (new[] { "#4f6bff", "#3f58e0", "#e9edff" }, new[] { "#6f8bff", "#5a76f0", "#1d2442" }),
        ["violet"] = (new[] { "#7c4dff", "#6a3df0", "#f0eaff" }, new[] { "#9d7bff", "#8a66f5", "#271d45" }),
        ["teal"] = (new[] { "#0f9f9a", "#0c8781", "#e2f6f5" }, new[] { "#2fd0c6", "#25b5ac", "#11302f" }),
        ["green"] = (new[] { "#1f9d63", "#198552", "#e6f6ee" }, new[] { "#3ccf8e", "#31b67b", "#15301f" }),
        ["orange"] = (new[] { "#e0690b", "#c45a06", "#fff0e3" }, new[] { "#ff9a3d", "#f08a2c", "#3a2512" }),
        ["pink"] = (new[] { "#d6336c", "#bb2a5c", "#fde8f0" }, new[] { "#ff6b9d", "#f0598b", "#3a1627" }),
    };

    public static bool Dark { get; private set; } = true;
    public static bool Neon { get; private set; }
    public static string AccentName { get; private set; } = "blue";
    public static event Action Changed;

    public static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    /// "#abc", "abc", "AABBCC", "#aabbcc" -> "#aabbcc"; anything else -> null.
    public static string CleanHex(string s)
    {
        s = (s ?? "").Trim().TrimStart('#');
        if (s.Length == 3) s = string.Concat(s[0], s[0], s[1], s[1], s[2], s[2]);
        if (s.Length != 6) return null;
        foreach (char ch in s) if (!Uri.IsHexDigit(ch)) return null;
        return "#" + s.ToLowerInvariant();
    }

    public static string Hex(Color c) => $"#{c.R:x2}{c.G:x2}{c.B:x2}";

    public static Color Mix(Color a, Color b, double t) =>
        Color.FromRgb((byte)Math.Round(a.R + (b.R - a.R) * t), (byte)Math.Round(a.G + (b.G - a.G) * t), (byte)Math.Round(a.B + (b.B - a.B) * t));

    public static Color Alpha(Color c, double a) => Color.FromArgb((byte)Math.Round(255 * Math.Clamp(a, 0, 1)), c.R, c.G, c.B);

    public static (double h, double s, double v) ToHsv(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0, max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
        double h = d == 0 ? 0 : max == r ? ((g - b) / d + 6) % 6 : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
        return (h * 60, max == 0 ? 0 : d / max, max);
    }

    public static Color FromHsv(double h, double s, double v)
    {
        h = ((h % 360) + 360) % 360;
        double c = v * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = v - c;
        var (r, g, b) = h < 60 ? (c, x, 0.0) : h < 120 ? (x, c, 0.0) : h < 180 ? (0.0, c, x) : h < 240 ? (0.0, x, c) : h < 300 ? (x, 0.0, c) : (c, 0.0, x);
        return Color.FromRgb((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }

    static SolidColorBrush Brush(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    static Brush Gradient(Color a, Color b)
    {
        var g = new LinearGradientBrush(a, b, new Point(0, 0), new Point(1, 1));
        g.Freeze();
        return g;
    }

    public static bool WindowsIsDark()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return k?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch { return true; }
    }

    /// How far card glows spread (px). 5 in Neon; your own setting in the Custom theme.
    public static int GlowRings { get; private set; } = 5;
    public static bool Custom { get; private set; }

    static double Lum(Color c) => (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255;

    /// The colours of one of the built-in themes, to start a custom theme from. which: neon / dark / light.
    public static Dictionary<string, string> Preset(string which)
    {
        var d = new Dictionary<string, string>();
        foreach (var key in CustomTheme.Keys)
        {
            var (light, dark, neon) = Palette[key];
            d[key] = which == "light" ? light : which == "dark" ? dark : neon;
        }
        return d;
    }

    /// The colour a custom theme shows for `key` right now (its own, or the Neon one it falls back to).
    public static string CustomColor(CustomTheme ct, string key) =>
        ct != null && ct.Colors.TryGetValue(key, out var hex) && CleanHex(hex) != null ? CleanHex(hex) : Palette[key].neon;

    /// appearance: dark / light / system / neon / custom.  accent: one of the six names, or any "#rrggbb".
    /// ct: the custom theme's colours (used when appearance is custom).  hoverGlow: buttons light up on hover.
    public static void Apply(string appearance, string accent, CustomTheme ct = null, bool hoverGlow = true)
    {
        Custom = appearance == "custom";
        Neon = appearance == "neon";
        ct ??= new CustomTheme();
        var res = Application.Current.Resources;
        var col = new Dictionary<string, Color>();
        if (Custom)
        {
            foreach (var (key, (_, _, neon)) in Palette) col[key] = C(neon);
            foreach (var key in CustomTheme.Keys) col[key] = C(CustomColor(ct, key));
            Dark = Lum(col["Bg"]) < 0.5;
            // the in-between shades follow the colours they belong to
            col["RedHover"] = Mix(col["Red"], Colors.Black, 0.1);
            col["RedSoft"] = Mix(col["Card"], col["Red"], 0.2);
            col["GreenSoft"] = Mix(col["Card"], col["Green"], 0.2);
            col["AmberSoft"] = Mix(col["Card"], col["Amber"], 0.2);
        }
        else
        {
            Dark = Neon || appearance == "dark" || (appearance == "system" && WindowsIsDark());
            foreach (var (key, (light, dark, neon)) in Palette) col[key] = C(Neon ? neon : Dark ? dark : light);
        }
        foreach (var (key, c) in col) res[key] = Brush(c);
        Color card = col["Card"];

        Color acc, hover, soft;
        var custom = accent != null && accent.StartsWith("#") ? CleanHex(accent) : null;
        if (custom != null)
        {
            AccentName = custom;
            acc = C(custom);
            hover = Mix(acc, Colors.Black, 0.14);
            soft = Mix(card, acc, Dark ? 0.22 : 0.13);
        }
        else
        {
            AccentName = Accents.ContainsKey(accent ?? "") ? accent : "blue";
            var a = Dark ? Accents[AccentName].dark : Accents[AccentName].light;
            acc = C(a[0]);
            hover = C(a[1]);
            soft = Neon || Custom ? Mix(card, acc, Dark ? 0.24 : 0.13) : C(a[2]);
        }
        // text on top of the accent: white, unless the chosen colour is so light that white would vanish
        res["OnAccent"] = Brush((custom != null || Custom) && Lum(acc) > 0.68 ? C("#10122a") : Colors.White);
        res["Accent"] = Brush(acc);
        res["AccentHover"] = Brush(hover);
        res["AccentSoft"] = Brush(soft);

        // Extras: buttons and sliders can be filled with a two-colour sweep, cards and hovered buttons can glow.
        // Neon has them all; Custom has what you chose; in Dark and Light they resolve to the plain accent / nothing,
        // so the same styles serve every theme.
        bool sweep = Neon || (Custom && ct.Sweep);
        double glow = Neon ? 0.55 : Custom ? ct.Glow / 100.0 : 0;
        GlowRings = Neon ? 5 : Custom ? Math.Clamp(ct.GlowSize, 0, 8) : 0;
        // fills use the deeper shade of a named accent, so white text on them stays easy to read
        Color fill = custom == null && sweep ? C(Accents[AccentName].light[0]) : acc;
        var (h, sat, v) = ToHsv(fill);
        var second = Custom && CleanHex(ct.Accent2) != null ? C(CleanHex(ct.Accent2))
                   : FromHsv(h - 42, Math.Min(1, sat * 1.05), Math.Min(1, v * 1.04));          // violet -> blue, blue -> cyan ...
        if (!sweep) second = acc;
        res["Accent2"] = Brush(second);
        res["AccentFill"] = sweep ? Gradient(fill, second) : Brush(acc);
        res["AccentFillHover"] = sweep ? Gradient(Mix(fill, Colors.White, 0.14), Mix(second, Colors.White, 0.14)) : Brush(hover);
        bool fancy = Neon || Custom;
        res["Glow"] = glow > 0 && GlowRings > 0 ? Brush(Alpha(acc, glow)) : Brushes.Transparent;
        res["GlowLine"] = fancy && hoverGlow ? Brush(Mix(acc, Dark ? Colors.White : Colors.Black, 0.18)) : Brushes.Transparent;
        res["NavBar"] = fancy ? (sweep ? Gradient(fill, second) : Brush(acc)) : Brushes.Transparent;
        Changed?.Invoke();
    }

    public static Brush B(string key) => (Brush)Application.Current.Resources[key] ?? Brushes.Magenta;
    public static Color Col(string key) => B(key) is SolidColorBrush s ? s.Color : Colors.Magenta;
}
