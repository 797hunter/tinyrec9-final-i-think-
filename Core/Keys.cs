// Keys.cs - key names used in recordings and hotkeys, and Windows virtual-key codes.
// The names match what the Python TinyRec wrote (pynput names), so old recordings replay the same keys.
using System.Text.RegularExpressions;

namespace TinyRec.Core;

public static class Keys
{
    static readonly Dictionary<int, string> VkToName = new();
    static readonly Dictionary<string, int> NameToVk = new();

    static void Add(int vk, string name, bool primary = true)
    {
        if (primary && !VkToName.ContainsKey(vk)) VkToName[vk] = name;
        NameToVk.TryAdd(name, vk);
    }

    static Keys()
    {
        for (int c = 'A'; c <= 'Z'; c++) Add(c, ((char)(c + 32)).ToString());
        for (int c = '0'; c <= '9'; c++) Add(c, ((char)c).ToString());
        for (int i = 1; i <= 24; i++) Add(0x6F + i, "f" + i);
        for (int i = 0; i <= 9; i++) Add(0x60 + i, "num" + i);
        Add(0x08, "backspace"); Add(0x09, "tab"); Add(0x0D, "enter"); Add(0x13, "pause"); Add(0x14, "caps_lock");
        Add(0x1B, "esc"); Add(0x20, "space"); Add(0x21, "page_up"); Add(0x22, "page_down"); Add(0x23, "end");
        Add(0x24, "home"); Add(0x25, "left"); Add(0x26, "up"); Add(0x27, "right"); Add(0x28, "down");
        Add(0x2C, "print_screen"); Add(0x2D, "insert"); Add(0x2E, "delete");
        Add(0x5B, "cmd"); Add(0x5B, "cmd_l", false); Add(0x5C, "cmd_r"); Add(0x5D, "menu");
        Add(0x90, "num_lock"); Add(0x91, "scroll_lock");
        // Low-level hooks report left/right specific modifier codes. Names match pynput on Windows.
        Add(0xA0, "shift"); Add(0xA0, "shift_l", false); Add(0xA1, "shift_r");
        Add(0xA2, "ctrl_l"); Add(0xA3, "ctrl_r");
        Add(0xA4, "alt_l"); Add(0xA5, "alt_r"); Add(0xA5, "alt_gr", false);
        Add(0x10, "shift_any", false); Add(0x11, "ctrl"); Add(0x12, "alt");
        Add(0xAD, "media_volume_mute"); Add(0xAE, "media_volume_down"); Add(0xAF, "media_volume_up");
        Add(0xB0, "media_next"); Add(0xB1, "media_previous"); Add(0xB3, "media_play_pause");
        Add(186, "semicolon"); Add(187, "equals"); Add(188, "comma"); Add(189, "minus"); Add(190, "period");
        Add(191, "slash"); Add(192, "grave"); Add(219, "lbracket"); Add(220, "backslash"); Add(221, "rbracket");
        Add(222, "quote"); Add(106, "num_multiply"); Add(107, "num_add"); Add(109, "num_subtract");
        Add(110, "num_decimal"); Add(111, "num_divide");
    }

    public static string Name(int vk) => VkToName.TryGetValue(vk, out var n) ? n : $"vk:{vk}";

    public static int Vk(string token)
    {
        if (token.StartsWith("vk:") && int.TryParse(token[3..], out var v) && v is > 0 and < 256) return v;
        if (NameToVk.TryGetValue(token, out var vk)) return vk;
        throw new ArgumentException($"unknown key '{token}'");
    }

    /// Characters typed by on-screen keyboards, AutoHotkey, password managers etc. arrive as "packets"
    /// with no key code - they're recorded as u:XXXX (the character's code in hex) and typed back the same way.
    public static bool IsChar(string token) => token.StartsWith("u:") && token.Length == 6;
    public static string CharToken(int code) => $"u:{code & 0xFFFF:x4}";
    public static int CharCode(string token) => Convert.ToInt32(token[2..], 16);

    public static bool IsKnown(string token)
    {
        try { Vk(token); return true; } catch (ArgumentException) { return false; }
    }

    // ------------------------------------------------------------------ hotkey combos
    public static readonly string[] Mods = { "ctrl", "alt", "shift", "cmd" };
    static readonly Dictionary<string, string> ModAlias = new() { ["control"] = "ctrl", ["win"] = "cmd", ["super"] = "cmd", ["windows"] = "cmd" };

    /// "ctrl_l" -> "ctrl", "alt_gr" -> "alt", "a" -> null
    public static string ModOf(string token)
    {
        if (token == null) return null;
        if (token.StartsWith("alt_gr")) return "alt";
        if (token == "shift_any") return "shift";
        foreach (var m in Mods)
            if (token == m || token.StartsWith(m + "_")) return m;
        return null;
    }

    public readonly record struct Combo(string Mods, string Main)
    {
        public override string ToString() => Mods.Length == 0 ? Main : Mods + "+" + Main;
    }

    static string NormMods(IEnumerable<string> mods) => string.Join("+", Mods.Where(mods.Contains));

    public static Combo Parse(string text)
    {
        var parts = (text ?? "").Split('+').Select(p => p.Trim().ToLowerInvariant()).Where(p => p.Length > 0)
                                .Select(p => ModAlias.TryGetValue(p, out var a) ? a : p).ToList();
        var mods = parts.Where(p => Mods.Contains(p)).ToList();
        var main = parts.Where(p => !Mods.Contains(p)).ToList();
        if (main.Count != 1 || !Format.KeyToken.IsMatch(main[0]) || !IsKnown(main[0]))
            throw new ArgumentException($"'{text}' needs exactly one normal key (like f9 or ctrl+alt+r)");
        return new Combo(NormMods(mods), main[0]);
    }

    public static Combo Make(IEnumerable<string> heldMods, string main) => new(NormMods(heldMods), main);

    public static string Pretty(string combo) =>
        string.Join("+", (combo ?? "").Split('+').Select(p =>
            p.Length is >= 2 and <= 3 && p[0] == 'f' && p[1..].All(char.IsDigit) ? p.ToUpperInvariant()
            : p.Length == 0 ? p : char.ToUpperInvariant(p[0]) + p[1..]));

    /// All key names that make up a combo (used to trim the "stop recording" hotkey out of recordings).
    public static HashSet<string> TokensOf(string combo)
    {
        var set = new HashSet<string>();
        try
        {
            var c = Parse(combo);
            set.Add(c.Main);
            foreach (var m in c.Mods.Split('+', StringSplitOptions.RemoveEmptyEntries))
            {
                set.Add(m); set.Add(m + "_l"); set.Add(m + "_r");
                if (m == "shift") set.Add("shift");
            }
        }
        catch (ArgumentException) { }
        return set;
    }
}

public sealed class HotkeyTable
{
    readonly Dictionary<Keys.Combo, string> lookup = new();

    public static readonly Dictionary<string, string> Defaults = new()
    {
        ["record_toggle"] = "f8", ["play_toggle"] = "f9", ["autoclick_toggle"] = "f6", ["draw_toggle"] = "f7",
        ["panic"] = "f12", ["speed_up"] = "ctrl+alt+up", ["speed_down"] = "ctrl+alt+down",
        ["speed_reset"] = "ctrl+alt+0", ["mini_toggle"] = "f10", ["pause_toggle"] = "f11",
    };

    public static readonly (string action, string label)[] Labels =
    {
        ("record_toggle", "Start / stop recording"), ("play_toggle", "Play / stop playback"),
        ("autoclick_toggle", "Start / stop auto clicker"), ("draw_toggle", "Draw mode on / off"),
        ("panic", "PANIC - stop everything"), ("speed_up", "Playback speed up"), ("speed_down", "Playback speed down"),
        ("speed_reset", "Playback speed 1x"), ("mini_toggle", "Full window / mini bar"),
        ("pause_toggle", "Pause / continue"),
    };

    public static string LabelOf(string action) => Labels.FirstOrDefault(l => l.action == action).label ?? action;

    /// Throws ArgumentException with a readable message if a combo is invalid or used twice.
    public void Set(IDictionary<string, string> bindings)
    {
        var next = new Dictionary<Keys.Combo, string>();
        foreach (var (action, text) in bindings)
        {
            var c = Keys.Parse(text);
            if (next.TryGetValue(c, out var other))
                throw new ArgumentException($"'{Keys.Pretty(text)}' is used twice ({LabelOf(other)} and {LabelOf(action)})");
            next[c] = action;
        }
        lock (lookup)
        {
            lookup.Clear();
            foreach (var kv in next) lookup[kv.Key] = kv.Value;
        }
    }

    public string Find(Keys.Combo c)
    {
        lock (lookup) return lookup.TryGetValue(c, out var a) ? a : null;
    }
}
