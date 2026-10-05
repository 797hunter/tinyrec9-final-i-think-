// Settings.cs - everything TinyRec remembers, saved as data/config.json (snake_case, human readable).
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TinyRec.Core;

/// The "Custom" theme: every colour is yours. A colour that isn't listed falls back to the Neon one.
public sealed class CustomTheme
{
    public Dictionary<string, string> Colors { get; set; } = new();   // Bg, Sidebar, Card ... -> "#rrggbb"
    public string Accent { get; set; } = "";            // only used by saved looks (the live accent is Settings.Accent)
    public bool Sweep { get; set; } = true;             // buttons and sliders fade from the accent to a second colour
    public string Accent2 { get; set; } = "";           // that second colour; "" = worked out from the accent
    public int Glow { get; set; } = 55;                 // glow strength around cards, 0-100 (0 = none)
    public int GlowSize { get; set; } = 5;              // how far it spreads, 0-8 px

    public static readonly string[] Keys = { "Bg", "Sidebar", "Card", "Card2", "Field", "Hover", "Line", "Track", "Text", "Muted", "Faint", "Red", "Green", "Amber" };

    public CustomTheme Copy() => new()
    {
        Colors = new Dictionary<string, string>(Colors), Accent = Accent, Sweep = Sweep, Accent2 = Accent2, Glow = Glow, GlowSize = GlowSize,
    };

    public void Clean()
    {
        Colors = (Colors ?? new()).Where(kv => Keys.Contains(kv.Key) && Settings.IsHex(kv.Value)).ToDictionary(kv => kv.Key, kv => kv.Value.ToLowerInvariant());
        Accent ??= "";
        Accent2 = Settings.IsHex(Accent2) ? Accent2.ToLowerInvariant() : "";
        Glow = Math.Clamp(Glow, 0, 100);
        GlowSize = Math.Clamp(GlowSize, 0, 8);
    }
}

public sealed class Settings
{
    public Dictionary<string, string> Hotkeys { get; set; } = new(HotkeyTable.Defaults);
    public bool TopmostFull { get; set; }
    public bool TopmostMini { get; set; } = true;
    public bool StartMini { get; set; }
    public int Countdown { get; set; } = 3;
    public bool TrimStart { get; set; } = true;
    public bool HideDrawingsWhilePlaying { get; set; } = true;
    public string LastRecording { get; set; } = "";
    public int Loops { get; set; } = 1;                 // 0 = forever
    public int RepeatCount { get; set; } = 5;           // remembered value for "Repeat N times"
    public double LoopDelay { get; set; }
    public bool RelativeToWindow { get; set; }
    public AutoClickConfig Autoclick { get; set; } = new();
    public string Appearance { get; set; } = "neon";    // dark / light / system / neon / custom
    public CustomTheme Custom { get; set; } = new();    // the colours of the Custom theme
    public Dictionary<string, CustomTheme> Looks { get; set; } = new();   // custom themes saved under a name
    public bool AnimRing { get; set; } = true;          // each animation has its own switch (Animations is the master)
    public bool AnimHover { get; set; } = true;
    public bool AnimPages { get; set; } = true;
    public bool AnimBorder { get; set; } = true;
    public string Accent { get; set; } = "violet";      // blue violet teal green orange pink, or any "#rrggbb"
    public bool Animations { get; set; } = true;        // pulsing record ring, page fades, moving border
    public bool BorderOn { get; set; } = true;          // the glowing frame around the main window
    public List<string> BorderColors { get; set; } = new() { "#8b5cff", "#3d7bff", "#ff3d9a" };
    public string BorderSpeed { get; set; } = "medium"; // slow / medium / fast
    public string BorderWhen { get; set; } = "always";  // always / running (only while recording or playing)
    public bool BorderByState { get; set; } = true;     // pink while recording, accent colour while playing
    public double UiScale { get; set; } = 1.0;
    public bool RecordMoves { get; set; } = true;
    public string RecordWhat { get; set; } = "";        // what a new recording captures: "" everything / keys / mouse / a custom mix (see Only)
    public string PlayOnly { get; set; } = "";          // what playback sends, same choices
    public bool PlayClicker { get; set; }               // run the auto clicker for as long as a recording plays
    public double LoopDelayRandom { get; set; }         // plus up to this many extra seconds between loops, different every time
    public double PlayMaxMinutes { get; set; }          // stop playback after this many minutes (0 = no limit)
    public double RecMaxSecs { get; set; }              // stop a recording by itself after this many seconds (0 = no limit)
    public bool DoneSound { get; set; }                 // play a sound when playback ends
    public bool TrimEnd { get; set; } = true;
    public bool AutoThin { get; set; }
    public bool ShowIndicator { get; set; } = true;
    public string DuringRun { get; set; } = "mini";     // stay / mini / hide
    public int[] MiniPos { get; set; }
    public string LibrarySort { get; set; } = "newest";
    public string DrawColor { get; set; } = "#ff4d5e";
    public int DrawWidth { get; set; } = 3;
    public string DrawTool { get; set; } = "rect";
    public bool AcAutostart { get; set; }
    public bool AcShowSpots { get; set; } = true;
    public bool BlockHotkeys { get; set; } = true;      // hotkeys don't reach the app you're in
    public string LastSequence { get; set; } = "";      // file name inside data/sequences
    public int SeqLoops { get; set; } = 1;              // 0 = forever
    public int SeqRepeatCount { get; set; } = 5;
    public bool SeqHotkey { get; set; }                 // the Play hotkey / mini bar run the sequence instead of the recording
    public List<string> Favorites { get; set; } = new(); // recording file names starred in the Library
    public bool LibraryFavOnly { get; set; }
    public string ImageArea { get; set; } = "screen";   // where image steps look: screen / window
    public string ImageWindow { get; set; } = "";       // ...the window whose title contains this
    public bool KeepLogs { get; set; } = true;          // write a run log file for every run

    static readonly JsonSerializerOptions Opts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,   // keep "ctrl+alt+up" readable
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    static readonly string[] Spare = { "ctrl+alt+p", "ctrl+alt+o", "ctrl+alt+i", "ctrl+alt+u", "ctrl+alt+y", "ctrl+shift+f1", "ctrl+shift+f2",
                                       "ctrl+shift+f3", "ctrl+shift+f4", "ctrl+shift+f5", "ctrl+shift+f6", "ctrl+shift+f7" };

    static bool SameCombo(string a, string b)
    {
        try { return Keys.Parse(a) == Keys.Parse(b); }
        catch (ArgumentException) { return false; }
    }

    public static Settings Load(string path)
    {
        Settings s;
        try
        {
            s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(path), Opts) ?? new Settings();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException or NotSupportedException)
        {
            s = new Settings();
        }
        // fill in anything missing / unknown
        var hk = new Dictionary<string, string>(HotkeyTable.Defaults);
        if (s.Hotkeys != null)
            foreach (var kv in s.Hotkeys)
                if (hk.ContainsKey(kv.Key) && !string.IsNullOrWhiteSpace(kv.Value)) hk[kv.Key] = kv.Value;
        // a hotkey added in a newer version must not collide with one the user already moved onto that key
        foreach (var action in HotkeyTable.Defaults.Keys)
            if ((s.Hotkeys == null || !s.Hotkeys.ContainsKey(action)) && hk.Any(kv => kv.Key != action && SameCombo(kv.Value, hk[action])))
                hk[action] = Spare.First(c => !hk.Values.Any(v => SameCombo(v, c)));
        s.Hotkeys = hk;
        s.Favorites = (s.Favorites ?? new()).Where(f => !string.IsNullOrWhiteSpace(f)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (s.ImageArea is not ("screen" or "window")) s.ImageArea = "screen";
        if (s.Appearance is not ("dark" or "light" or "system" or "neon" or "custom")) s.Appearance = "neon";
        s.Custom ??= new CustomTheme();
        s.Custom.Clean();
        s.Looks = (s.Looks ?? new()).Where(kv => !string.IsNullOrWhiteSpace(kv.Key) && kv.Value != null).ToDictionary(kv => kv.Key.Trim(), kv => kv.Value);
        foreach (var look in s.Looks.Values) look.Clean();
        if (string.IsNullOrWhiteSpace(s.Accent)) s.Accent = "violet";
        if (s.BorderSpeed is not ("slow" or "medium" or "fast")) s.BorderSpeed = "medium";
        if (s.BorderWhen is not ("always" or "running")) s.BorderWhen = "always";
        s.BorderColors = (s.BorderColors ?? new()).Where(IsHex).Select(c => c.ToLowerInvariant()).Take(4).ToList();
        s.ImageWindow ??= "";
        s.Autoclick ??= new AutoClickConfig();
        s.Autoclick.Points ??= new List<int[]>();
        s.Autoclick.Points = s.Autoclick.Points.Where(p => p != null && p.Length >= 2).ToList();
        s.Countdown = Math.Clamp(s.Countdown, 0, 30);
        s.UiScale = Math.Clamp(s.UiScale, 0.8, 1.5);
        if (s.Loops < 0) s.Loops = 1;
        s.RecordWhat = Only.Clean(s.RecordWhat);
        s.PlayOnly = Only.Clean(s.PlayOnly);
        s.LoopDelayRandom = double.IsFinite(s.LoopDelayRandom) ? Math.Clamp(s.LoopDelayRandom, 0, 86400) : 0;
        s.PlayMaxMinutes = double.IsFinite(s.PlayMaxMinutes) ? Math.Clamp(s.PlayMaxMinutes, 0, 60 * 24 * 30) : 0;
        s.RecMaxSecs = double.IsFinite(s.RecMaxSecs) ? Math.Clamp(s.RecMaxSecs, 0, 86400) : 0;
        s.Autoclick.More = (s.Autoclick.More ?? new ClickMore()).Clean();
        if (s.Autoclick.Order is not ("random" or "pingpong")) s.Autoclick.Order = "sequence";
        s.Autoclick.Seconds = double.IsFinite(s.Autoclick.Seconds) ? Math.Clamp(s.Autoclick.Seconds, 0, 86400 * 7) : 0;
        return s;
    }

    /// "#rrggbb"
    public static bool IsHex(string c) => c != null && c.Length == 7 && c[0] == '#' && c.Skip(1).All(Uri.IsHexDigit);

    public void Save(string path) =>
        Files.AtomicWrite(path, new UTF8Encoding(false).GetBytes(JsonSerializer.Serialize(this, Opts)), keepBackup: false);
}
