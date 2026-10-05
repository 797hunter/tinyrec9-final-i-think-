// Sequence.cs - a chain of steps: wait for a picture (and click it), play a recording, pause, repeat a group.
//
// This is what makes "the game takes a different time every round" work: each recording only starts once the
// picture before it has shown up and been clicked, however long that takes.
//
// Saved as data/sequences/<name>.json, for example:
//   { "name": "boss rush", "steps": [
//       { "kind": "play",   "recording": "place units.txt" },
//       { "kind": "image",  "image": "ability.png" },
//       { "kind": "play",   "recording": "walk to gate.txt" },
//       { "kind": "repeat", "back": 2, "count": 6 },
//       { "kind": "play",   "recording": "boss.txt" } ] }
using System.Text;
using System.Text.Json;

namespace TinyRec.Core;

public sealed class SeqStep
{
    public static readonly string[] Kinds = { "image", "play", "wait", "repeat", "click", "key", "goto", "stop", "focus", "alert" };

    public string Kind { get; set; } = "play";       // one of Kinds

    // every kind
    public string Label { get; set; } = "";           // your own name for the step (shown instead of the automatic title)
    public bool Off { get; set; }                     // switched off: the sequence skips it
    public double Before { get; set; }                // seconds to wait before this step starts
    public double Pause { get; set; }                 // seconds to wait after it (play / wait steps; image steps use After)
    public double Chance { get; set; } = 100;         // run this step only this % of the time (100 = always)
    public string When { get; set; } = "";            // "" every full run / first (only the first) / notfirst / every (every Nth)
    public int EveryN { get; set; } = 2;              // the N of "every Nth full run"

    // image: wait until this picture is on screen, then click it
    public string Image { get; set; } = "";
    public bool Click { get; set; } = true;           // click it when it shows
    public string Button { get; set; } = "left";      // left / right / double
    public string Key { get; set; } = "";             // also (or instead) press this key when it shows; "" = none
    public List<string> Or { get; set; } = new();     // other pictures that also end the wait - nothing is clicked for those
    public double Conf { get; set; } = 0.9;
    public double Timeout { get; set; }               // seconds; 0 = no limit
    public string OnFail { get; set; } = "stop";      // when the time limit runs out: stop / skip
    public double After { get; set; } = 0.5;          // short breather after the click, before the next step
    public int Dx { get; set; }                       // click this far right (+) / left (-) of the picture's centre
    public int Dy { get; set; }                       // ... and this far down (+) / up (-)
    public bool Gone { get; set; }                    // the other way round: wait until the picture is NOT on screen any more

    // play: play a saved recording
    public string Recording { get; set; } = "";       // file name inside data/recordings
    public int Times { get; set; } = 1;
    public double Speed { get; set; }                 // playback speed for this step; 0 = the sequence's / app's speed

    public string Only { get; set; } = "";            // what to send: "" everything / keys / mouse / a custom mix (see Only)
    public double From { get; set; }                  // play only from this second of the recording ...
    public double To { get; set; }                    // ... to this second (0 = to the end)
    public double ForSecs { get; set; }               // above 0: keep replaying it for this many seconds (instead of Times)
    public double SpeedJitter { get; set; }           // each play is up to this % faster or slower, at random

    // play + auto click: click these spots at a steady pace the whole time the recording plays
    public bool Ac { get; set; }
    public List<int[]> AcPoints { get; set; } = new();   // [[x, y], ...]; none = click wherever the pointer is
    public double AcEvery { get; set; } = 500;        // milliseconds between clicks
    public string AcButton { get; set; } = "left";    // left / right / double

    // click: a burst of clicks on its own (uses AcPoints / AcEvery / AcButton above)
    public int Clicks { get; set; } = 10;             // how many clicks ...
    public double ClickSecs { get; set; }             // ... or, if above 0, keep clicking for this many seconds instead
    public bool ClickForever { get; set; }            // ... or keep clicking until More.Until's picture shows
    public ClickMore More { get; set; } = new();      // the finer click settings (click steps and clicks alongside)

    // key: press a key or combination a number of times, or type some text
    public string KeyMode { get; set; } = "press";    // press / type
    public string Combo { get; set; } = "";           // "e", "space", "ctrl+shift+a" ...
    public int KeyTimes { get; set; } = 1;
    public double KeyEvery { get; set; } = 100;       // ms between presses
    public double KeyHold { get; set; } = 40;         // ms each press is held
    public string Text { get; set; } = "";            // the text to type
    public double TypeDelay { get; set; } = 30;       // ms per character

    // goto / stop: jump to another step, or end the run - always, or only if a picture is / isn't on screen
    public int GoTo { get; set; } = 1;                // the step to jump to (1 = the first)
    public string IfMode { get; set; } = "always";    // always / shown / hidden
    public string IfImage { get; set; } = "";
    public double IfConf { get; set; } = 0.9;
    public int MaxJumps { get; set; }                 // at most this many jumps per full run (0 = no limit)
    public string StopMode { get; set; } = "run";     // run (end everything) / loop (end this full run, start the next)

    // focus: bring a window to the front
    public string WindowTitle { get; set; } = "";

    // alert: show a message (and play a sound)
    public string Message { get; set; } = "";
    public bool Sound { get; set; } = true;

    // play + watch: look for a picture the whole time the recording plays, and only move on once it has been seen
    public string Until { get; set; } = "";           // the picture to watch for ("" = don't watch)
    public double UntilConf { get; set; } = 0.9;
    public string UntilMode { get; set; } = "stop";   // when it's seen: stop (cut the recording short) / finish (let this pass end first)
    public bool UntilLoop { get; set; } = true;       // not seen when the recording ends: play it again (false = just wait)
    public bool UntilClick { get; set; }              // click the picture once it has been seen
    public string UntilKey { get; set; } = "";        // ... and / or press this key
    public double UntilTimeout { get; set; }          // give up after this many seconds (0 = no limit); OnFail says what then

    // wait: do nothing for a while
    public double Seconds { get; set; } = 1;
    public double Random { get; set; }                // plus up to this many extra seconds, different every time

    // repeat: run the `Back` steps above this one again, until they've run `Count` times in total
    public int Back { get; set; } = 1;
    public int Count { get; set; } = 2;

    public SeqStep Copy()
    {
        var c = (SeqStep)MemberwiseClone();
        c.Or = Or.ToList();
        c.AcPoints = AcPoints.Select(p => p.ToArray()).ToList();
        c.More = (More ?? new ClickMore()).Copy();
        return c;
    }

    public ImageWait ToWait() => new()
    {
        Name = Image, Conf = Conf, Timeout = Timeout, Button = Click ? Button : null,
        Key = string.IsNullOrEmpty(Key) ? null : Key, Or = Or.ToList(), Dx = Dx, Dy = Dy,
    };

    /// What to do with the watched picture of a play step once it has been seen.
    public ImageWait ToWatch() => new()
    {
        Name = Until, Conf = UntilConf, Button = UntilClick ? "left" : null, Key = string.IsNullOrEmpty(UntilKey) ? null : UntilKey,
    };

    /// The clicks of a click step, or the ones that go alongside a play step.
    public ClickPlan ToClicks(bool alongside)
    {
        var m = (More ?? new ClickMore()).Copy().Clean();
        return new ClickPlan
        {
            Points = AcPoints.Select(p => p.ToArray()).ToList(), Button = AcButton, IntervalMs = AcEvery, More = m,
            Count = alongside ? m.LimitClicks : ClickForever || ClickSecs > 0 ? 0 : Clicks,
            Seconds = alongside ? m.LimitSecs : ClickForever ? 0 : ClickSecs,
        };
    }

    /// Steps that only steer the run (they send nothing and take no time).
    public bool IsControl => Kind is "repeat" or "goto" or "stop";

    /// The wait after this step (image steps keep their own "then pause" setting).
    public double AfterStep => Kind == "image" ? After : IsControl ? 0 : Pause;
}

public sealed class SeqStats
{
    public int Loops, Steps, Plays, Events, ImageHits, ImageMisses, Clicks, Restarts, Failures, GuardHits;
    public bool TimedOut;                              // the sequence's own time limit ended it
    public double MaxLateMs;
    public bool Stopped;
    public string Message = "";
}

public sealed class Sequence
{
    public string Name { get; set; } = "Untitled";
    public List<SeqStep> Steps { get; set; } = new();

    // settings that belong to this sequence (each sequence keeps its own)
    public string Note { get; set; } = "";            // a reminder to yourself, shown under the name
    public int Loops { get; set; } = -1;              // how many times the whole list runs; 0 = until stopped; -1 = not chosen yet
    public int LoopCount { get; set; } = 5;           // the remembered number for "Repeat N times"
    public double LoopDelay { get; set; }             // seconds between one full run and the next
    public double Gap { get; set; }                   // seconds between every step and the next (on top of each step's own waits)
    public double Speed { get; set; }                 // playback speed for every recording in it; 0 = the app's speed
    public string Window { get; set; } = "";          // where image steps look: a window whose title has this text ("" = the app's setting)
    public string Show { get; set; } = "";            // the TinyRec window during a run: "" = the app's setting, stay / mini / hide
    public bool Clicker { get; set; }                 // run the Auto Clicker page's clicker for as long as this sequence runs
    public double GapRandom { get; set; }             // plus up to this many extra seconds after every step, different every time
    public double LoopRandom { get; set; }            // plus up to this many extra seconds between full runs
    public double StartDelay { get; set; }            // seconds to wait before the first step
    public double MaxMinutes { get; set; }            // end the whole run after this many minutes (0 = no limit)
    public string OnError { get; set; } = "";         // a step fails: "" = stop the run, restart = start again from step 1, skip = carry on
    public string Front { get; set; } = "";           // bring the window with this text in its title to the front when the run starts
    public bool DoneSound { get; set; }               // play a sound when the run ends
    public List<SeqGuard> Guards { get; set; } = new();   // pictures watched for the whole run (pop-ups, disconnect screens ...)

    static readonly JsonSerializerOptions Opts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public string ToJson() => JsonSerializer.Serialize(this, Opts);

    public static Sequence FromJson(string json)
    {
        var s = JsonSerializer.Deserialize<Sequence>(json, Opts) ?? new Sequence();
        s.Name = string.IsNullOrWhiteSpace(s.Name) ? "Untitled" : s.Name.Trim();
        s.Steps = (s.Steps ?? new()).Where(x => x != null && SeqStep.Kinds.Contains(x.Kind)).ToList();
        foreach (var x in s.Steps) Clean(x);
        s.Note = (s.Note ?? "").Trim();
        s.Loops = Math.Clamp(s.Loops, -1, 1000000);
        s.LoopCount = Math.Clamp(s.LoopCount, 2, 1000000);
        s.LoopDelay = Secs(s.LoopDelay, 0, 86400, 0);
        s.Gap = Secs(s.Gap, 0, 3600, 0);
        s.Speed = double.IsFinite(s.Speed) && s.Speed > 0 ? Math.Clamp(s.Speed, 0.1, 10) : 0;
        s.Window = (s.Window ?? "").Trim();
        if (s.Show is not ("stay" or "mini" or "hide")) s.Show = "";
        s.GapRandom = Secs(s.GapRandom, 0, 3600, 0);
        s.LoopRandom = Secs(s.LoopRandom, 0, 86400, 0);
        s.StartDelay = Secs(s.StartDelay, 0, 86400, 0);
        s.MaxMinutes = Secs(s.MaxMinutes, 0, 60 * 24 * 30, 0);
        if (s.OnError is not ("restart" or "skip")) s.OnError = "";
        s.Front = (s.Front ?? "").Trim();
        s.Guards = (s.Guards ?? new()).Where(g => g != null && !string.IsNullOrWhiteSpace(g.Image)).Take(20).ToList();
        foreach (var g in s.Guards) g.Clean();
        return s;
    }

    static double Secs(double v, double lo, double hi, double fallback) => double.IsFinite(v) ? Math.Clamp(v, lo, hi) : fallback;

    public static void CleanStep(SeqStep x) => Clean(x);

    static void CleanExtras(SeqStep x)
    {
        x.Label = (x.Label ?? "").Trim();
        x.Before = Secs(x.Before, 0, 86400, 0);
        x.Pause = Secs(x.Pause, 0, 86400, 0);
        x.Random = Secs(x.Random, 0, 86400, 0);
        x.Dx = Math.Clamp(x.Dx, -5000, 5000);
        x.Dy = Math.Clamp(x.Dy, -5000, 5000);
        x.Speed = double.IsFinite(x.Speed) && x.Speed > 0 ? Math.Clamp(x.Speed, 0.1, 10) : 0;
        x.Until ??= "";
        x.UntilConf = double.IsFinite(x.UntilConf) ? Math.Clamp(x.UntilConf, 0.5, 1) : 0.9;
        if (x.UntilMode is not ("stop" or "finish")) x.UntilMode = "stop";
        x.UntilKey = (x.UntilKey ?? "").Trim().ToLowerInvariant();
        x.UntilTimeout = Secs(x.UntilTimeout, 0, 86400 * 7, 0);
        x.Only = Core.Only.Clean(x.Only);
        x.AcPoints = (x.AcPoints ?? new()).Where(p => p != null && p.Length >= 2).Select(p => new[] { p[0], p[1] }).Take(200).ToList();
        x.AcEvery = double.IsFinite(x.AcEvery) ? Math.Clamp(x.AcEvery, 1, 3600000) : 500;
        if (x.AcButton is not ("left" or "right" or "double")) x.AcButton = "left";
        x.Clicks = Math.Clamp(x.Clicks, 1, 100000000);
        x.ClickSecs = Secs(x.ClickSecs, 0, 86400, 0);
        x.More = (x.More ?? new ClickMore()).Clean();
        if (x.ClickForever && x.More.Until.Length == 0) x.ClickForever = false;      // "until a picture shows" needs the picture
        x.Chance = Secs(x.Chance, 0, 100, 100);
        if (x.When is not ("first" or "notfirst" or "every")) x.When = "";
        x.EveryN = Math.Clamp(x.EveryN, 2, 1000000);
        x.From = Secs(x.From, 0, 86400, 0);
        x.To = Secs(x.To, 0, 86400, 0);
        if (x.To > 0 && x.To <= x.From) x.To = 0;
        x.ForSecs = Secs(x.ForSecs, 0, 86400 * 7, 0);
        x.SpeedJitter = Secs(x.SpeedJitter, 0, 90, 0);
        if (x.KeyMode is not ("press" or "type")) x.KeyMode = "press";
        x.Combo = (x.Combo ?? "").Trim().ToLowerInvariant();
        x.KeyTimes = Math.Clamp(x.KeyTimes, 1, 1000000);
        x.KeyEvery = Secs(x.KeyEvery, 1, 3600000, 100);
        x.KeyHold = Secs(x.KeyHold, 1, 600000, 40);
        x.Text ??= "";
        x.TypeDelay = Secs(x.TypeDelay, 1, 10000, 30);
        x.GoTo = Math.Max(1, x.GoTo);
        if (x.IfMode is not ("shown" or "hidden")) x.IfMode = "always";
        x.IfImage = (x.IfImage ?? "").Trim();
        x.IfConf = Secs(x.IfConf, 0.5, 1, 0.9);
        x.MaxJumps = Math.Clamp(x.MaxJumps, 0, 1000000);
        if (x.StopMode is not ("run" or "loop")) x.StopMode = "run";
        x.WindowTitle = (x.WindowTitle ?? "").Trim();
        x.Message = (x.Message ?? "").Trim();
    }

    static void Clean(SeqStep x)
    {
        CleanExtras(x);
        x.Image ??= "";
        x.Recording ??= "";
        x.Key = (x.Key ?? "").Trim().ToLowerInvariant();
        x.Or = (x.Or ?? new()).Where(n => !string.IsNullOrWhiteSpace(n) && n != x.Image).Distinct().ToList();
        if (x.Button is not ("left" or "right" or "double")) x.Button = "left";
        if (x.OnFail is not ("stop" or "skip")) x.OnFail = "stop";
        x.Conf = double.IsFinite(x.Conf) ? Math.Clamp(x.Conf, 0.5, 1) : 0.9;
        x.Timeout = double.IsFinite(x.Timeout) ? Math.Max(0, x.Timeout) : 0;
        x.After = double.IsFinite(x.After) ? Math.Clamp(x.After, 0, 600) : 0.5;
        x.Seconds = double.IsFinite(x.Seconds) ? Math.Clamp(x.Seconds, 0, 86400) : 1;
        x.Times = Math.Clamp(x.Times, 1, 1000000);
        x.Back = Math.Max(1, x.Back);
        x.Count = Math.Clamp(x.Count, 1, 1000000);
    }

    public void Save(string path) => Files.AtomicWrite(path, Encoding.UTF8.GetBytes(ToJson()), false);

    public static Sequence Load(string path) => FromJson(File.ReadAllText(path, Encoding.UTF8));

    public Sequence Copy() => new()
    {
        Name = Name, Steps = Steps.Select(s => s.Copy()).ToList(), Note = Note, Loops = Loops, LoopCount = LoopCount, LoopDelay = LoopDelay,
        Gap = Gap, Speed = Speed, Window = Window, Show = Show, Clicker = Clicker, GapRandom = GapRandom, LoopRandom = LoopRandom,
        StartDelay = StartDelay, MaxMinutes = MaxMinutes, OnError = OnError, Front = Front, DoneSound = DoneSound,
        Guards = Guards.Select(g => g.Copy()).ToList(),
    };

    /// Everything that would stop this sequence from running (empty list = good to go).
    public List<string> Problems(Func<string, bool> recordingExists, Func<string, bool> imageExists)
    {
        var p = new List<string>();
        if (Steps.Count == 0) p.Add("It has no steps yet.");
        if (Steps.Count > 0 && Steps.All(s => s.Off || s.IsControl || s.Kind == "wait")) p.Add("It needs at least one step that does something (image, recording, click, keys ...) and is switched on.");
        static bool KeyOk(string k) => Keys.IsKnown(k) || Keys.IsChar(k);
        foreach (var g in Guards.Where(g => !g.Off))
        {
            if (!imageExists(g.Image)) p.Add($"Always-watch: the image “{g.Image}” isn't in the Images library any more.");
            if (g.Key.Length > 0 && !KeyOk(g.Key)) p.Add($"Always-watch “{g.Image}”: “{g.Key}” isn't a key name (try e, space, enter, f5…).");
            if (g.Action == "key" && g.Key.Length == 0) p.Add($"Always-watch “{g.Image}”: no key chosen.");
        }
        for (int i = 0; i < Steps.Count; i++)
        {
            var s = Steps[i];
            if (s.Off) continue;                                            // a switched-off step can't stop the run
            string n = $"Step {i + 1}";
            if (s.Kind == "image")
            {
                if (s.Image.Length == 0) p.Add($"{n}: no image chosen.");
                else if (!imageExists(s.Image)) p.Add($"{n}: the image “{s.Image}” isn't in the Images library any more.");
                foreach (var o in s.Or.Where(o => !imageExists(o))) p.Add($"{n}: the image “{o}” isn't in the Images library any more.");
                if (s.Key.Length > 0 && !Keys.IsKnown(s.Key)) p.Add($"{n}: “{s.Key}” isn't a key name (try e, space, enter, f5…).");
            }
            else if (s.Kind == "play")
            {
                if (s.Recording.Length == 0) p.Add($"{n}: no recording chosen.");
                else if (!recordingExists(s.Recording)) p.Add($"{n}: the recording “{s.Recording}” isn't in the Library any more.");
                if (s.Until.Length > 0 && !imageExists(s.Until)) p.Add($"{n}: the image it watches for, “{s.Until}”, isn't in the Images library any more.");
                if (s.Until.Length > 0 && s.UntilKey.Length > 0 && !Keys.IsKnown(s.UntilKey)) p.Add($"{n}: “{s.UntilKey}” isn't a key name (try e, space, enter, f5…).");
            }
            else if (s.Kind == "repeat" && i == 0) p.Add($"{n}: a Repeat step needs steps above it.");
            else if (s.Kind == "key")
            {
                if (s.KeyMode == "type") { if (s.Text.Length == 0) p.Add($"{n}: no text to type."); }
                else if (s.Combo.Length == 0) p.Add($"{n}: no key chosen.");
                else foreach (var k in s.Combo.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(k => !KeyOk(k)))
                    p.Add($"{n}: “{k}” isn't a key name (try e, space, enter, f5, ctrl+c…).");
            }
            else if (s.Kind == "goto" && (s.GoTo > Steps.Count || s.GoTo == i + 1)) p.Add($"{n}: it jumps to step {s.GoTo}, which {(s.GoTo == i + 1 ? "is itself" : "doesn't exist")}.");
            else if (s.Kind == "focus" && s.WindowTitle.Length == 0) p.Add($"{n}: no window named.");
            if (s.Kind is "goto" or "stop" && s.IfMode != "always")
            {
                if (s.IfImage.Length == 0) p.Add($"{n}: no image chosen for its condition.");
                else if (!imageExists(s.IfImage)) p.Add($"{n}: the image “{s.IfImage}” isn't in the Images library any more.");
            }
            if (s.Kind == "click" || (s.Kind == "play" && s.Ac))
            {
                foreach (var im in s.More.Images().Where(im => !imageExists(im))) p.Add($"{n}: the image “{im}” (click settings) isn't in the Images library any more.");
                if (s.More.Key.Length > 0 && !KeyOk(s.More.Key)) p.Add($"{n}: “{s.More.Key}” (click settings) isn't a key name.");
            }
        }
        return p;
    }
}

/// A picture a sequence keeps an eye out for the whole time it runs, and what to do whenever it shows:
/// click it away (a pop-up, "Reconnect"), press a key, start the run again from step 1, stop the run, or just alert.
public sealed class SeqGuard
{
    public string Image { get; set; } = "";
    public double Conf { get; set; } = 0.9;
    public string Action { get; set; } = "click";     // click / key / restart / stop / alert
    public string Key { get; set; } = "";             // the key for "key" (and optionally pressed after a click)
    public double Every { get; set; } = 1;            // seconds between looks
    public double Cooldown { get; set; } = 3;         // after it fires, leave it alone for this long
    public int Dx { get; set; }                       // click this far from the picture's centre
    public int Dy { get; set; }
    public bool Off { get; set; }

    public SeqGuard Copy() => (SeqGuard)MemberwiseClone();

    public void Clean()
    {
        Image = (Image ?? "").Trim();
        Conf = double.IsFinite(Conf) ? Math.Clamp(Conf, 0.5, 1) : 0.9;
        if (Action is not ("click" or "key" or "restart" or "stop" or "alert")) Action = "click";
        Key = (Key ?? "").Trim().ToLowerInvariant();
        Every = double.IsFinite(Every) ? Math.Clamp(Every, 0.2, 3600) : 1;
        Cooldown = double.IsFinite(Cooldown) ? Math.Clamp(Cooldown, 0, 86400) : 3;
        Dx = Math.Clamp(Dx, -5000, 5000);
        Dy = Math.Clamp(Dy, -5000, 5000);
    }

    public string Does => Action switch
    {
        "key" => $"press {Key}",
        "restart" => "start again from step 1",
        "stop" => "stop the run",
        "alert" => "alert you",
        _ => Key.Length > 0 ? $"click it and press {Key}" : "click it",
    };
}

/// Runs a Sequence. One instance per app; Stop() is safe from any thread.
public sealed class SequenceRunner
{
    readonly Player player;
    readonly Func<string, Recording> load;                    // recording file name -> recording (throws if it can't)
    readonly Func<Recording, (int dx, int dy)> offsetOf;      // "follow recorded window" offset for a recording
    CancellationTokenSource cts = new();

    // live status, read by the app's timer (plain fields on purpose: single writer, display only)
    public volatile int StepIndex = -1, StepCount, Loop = 1, EventI, EventN, PlayNo, PlayOf, Round, RoundOf, ClickI, ClickN;
    public volatile bool Clicking;                            // a play step's alongside clicker is running
    public volatile string Phase = "";                        // "" | image | play | wait | click
    public volatile string Detail = "";
    public volatile string Watching = "";                     // the picture a play step is watching for right now ("" = none)
    public volatile int[] Runs = Array.Empty<int>();          // how many times each step has run in this run (display only)
    public double PhaseStart, RunStart;
    readonly Random rng = new();

    public SequenceRunner(Player player, Func<string, Recording> load, Func<Recording, (int, int)> offsetOf = null)
    {
        this.player = player;
        this.load = load;
        this.offsetOf = offsetOf ?? (_ => (0, 0));
    }

    public void Stop()
    {
        cts.Cancel();
        player.Stop();
    }

    /// Call on the thread that starts the run, before the worker thread exists, so a Stop() in the first
    /// milliseconds isn't lost.
    public void Prepare()
    {
        cts = new CancellationTokenSource();
        prepared = true;
    }
    volatile bool prepared;

    void Enter(string phase, string detail)
    {
        Detail = detail;
        PhaseStart = Timing.Now;
        Phase = phase;
    }

    /// Told when a step (or an always-watch) wants your attention: (message, play a sound). Called on a run thread.
    public Action<string, bool> Notify;

    /// The full run that is going on right now: its own stop switch (so "start again from step 1" can end just this
    /// one), and why it is being started again, if it is.
    sealed class Lap
    {
        readonly object gate = new();
        CancellationTokenSource cts;
        public volatile string Restart;                 // why this full run is being abandoned (null = it isn't)
        public volatile string StopMessage;             // an always-watch asked for the whole run to stop

        public CancellationToken Begin(CancellationToken stop)
        {
            lock (gate)
            {
                Restart = null;
                cts = CancellationTokenSource.CreateLinkedTokenSource(stop);
                return cts.Token;
            }
        }

        public void End()
        {
            lock (gate)
            {
                cts?.Dispose();
                cts = null;
            }
        }

        public void RestartNow(string why)
        {
            lock (gate)
            {
                if (cts == null) return;
                Restart ??= why;
                cts.Cancel();
            }
        }
    }

    /// What a step asks the run to do next.
    struct Outcome
    {
        public int Jump;            // go to this step (0-based); -1 = carry on with the next one
        public bool EndLoop;        // this full run is over: start the next one
        public bool EndRun;         // the whole run is over
        public static Outcome Next => new() { Jump = -1 };
    }

    static Recording Part(Recording rec, double from, double to)
    {
        if (from <= 0 && to <= 0) return rec;
        var cut = rec.Copy();
        cut.Events = cut.Events.Where(e => e.T >= from && (to <= 0 || e.T <= to)).ToList();
        foreach (var e in cut.Events) e.T = Math.Max(0, e.T - from);
        return cut;
    }

    static bool Due(string when, int everyN, int loopsDone)
    {
        if (when == "first" && loopsDone > 0) return false;
        if (when == "notfirst" && loopsDone == 0) return false;
        if (when == "every" && (loopsDone + 1) % Math.Max(2, everyN) != 0) return false;
        return true;
    }

    bool Due(SeqStep s, int loopsDone)
    {
        if (!Due(s.When, s.EveryN, loopsDone)) return false;
        if (s.Chance < 100 && rng.NextDouble() * 100 >= s.Chance) return false;
        return true;
    }

    /// The "if a picture is / isn't on screen" of a Jump or Stop step.
    bool Holds(SeqStep s, int i)
    {
        if (s.IfMode == "always") return true;
        if (!player.CanSeeImages) throw new Halt($"Step {i + 1}: it checks for “{s.IfImage}”, but image recognition is not available.", true);
        bool shown;
        try { shown = player.Peek(s.IfImage, s.IfConf).Found; }
        catch (Exception e) when (e is FileNotFoundException or InvalidDataException) { throw new Halt($"Step {i + 1}: {e.Message}", true); }
        catch (Exception) { shown = false; }                       // the screen can't be read for a moment: not there
        return s.IfMode == "shown" ? shown : !shown;
    }

    /// loops: 0 = forever. startAt: the step (0-based) the first loop begins at - later loops start from the top.
    public SeqStats Run(Sequence seq, int loops = 1, double loopDelay = 0, int startAt = 0, RunLog log = null)
    {
        if (!prepared) cts = new CancellationTokenSource();
        prepared = false;
        var st = new SeqStats();
        var steps = seq.Steps.Select(s => s.Copy()).ToList();
        var recs = new Recording[steps.Count];
        var runs = new int[steps.Count];
        Runs = runs;
        StepCount = steps.Count;
        Loop = 1;
        RunStart = Timing.Now;
        player.Log = log;
        var lap = new Lap();
        var limit = new CancellationTokenSource();                 // the sequence's own time limit
        var all = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, limit.Token);
        var stop = all.Token;                                      // stopped by you, by a failure, or by the time limit
        var guardsOff = new CancellationTokenSource();
        Thread guardThread = null;
        PausableLimit limiter = null;
        try
        {
            var loaded = new Dictionary<string, Recording>();
            for (int k = 0; k < steps.Count; k++)                  // load everything up front: fail before anything moves
            {
                var s = steps[k];
                if (s.Kind != "play" || s.Off) continue;
                if (!loaded.TryGetValue(s.Recording, out var whole))
                {
                    try { loaded[s.Recording] = whole = load(s.Recording); }
                    catch (Exception e) { throw new Halt($"Couldn't open “{s.Recording}”: {e.Message}", true); }
                }
                recs[k] = Part(whole, s.From, s.To);
            }
            if (!steps.Any(x => !x.Off && !x.IsControl)) return st;        // nothing to do (and never spin on it)

            if (seq.Front.Length > 0)
                log?.Add(player.Focus(seq.Front) ? $"brought “{seq.Front}” to the front" : $"no window with “{seq.Front}” in its title - carried on");
            if (seq.MaxMinutes > 0) limiter = new PausableLimit(limit, seq.MaxMinutes * 60, () => player.Paused);   // paused time doesn't count
            if (seq.StartDelay > 0)
            {
                Enter("wait", Fmt.Num(seq.StartDelay));
                player.Sleep(seq.StartDelay, stop);
            }
            var guards = seq.Guards.Where(g => !g.Off && g.Image.Length > 0).Select(g => g.Copy()).ToList();
            if (guards.Count > 0 && player.CanSeeImages)
            {
                var gtok = CancellationTokenSource.CreateLinkedTokenSource(stop, guardsOff.Token).Token;
                guardThread = new Thread(() => GuardLoop(guards, lap, st, log, gtok)) { IsBackground = true, Name = "TinyRec always-watch" };
                guardThread.Start();
            }

            int first = Math.Clamp(startAt, 0, Math.Max(0, steps.Count - 1));
            if (first > 0) log?.Add($"starting at step {first + 1}", first);
            int failedInARow = 0;
            bool endRun = false;
            while (!stop.IsCancellationRequested && !endRun)
            {
                var tok = lap.Begin(stop);
                string failed = null;
                double lapStart = Timing.Now;
                int stepsBefore = st.Steps, quick = 0;
                try
                {
                    var rounds = new int[steps.Count];
                    var jumps = new int[steps.Count];
                    int i = first, control = 0;
                    first = 0;
                    while (i < steps.Count && !tok.IsCancellationRequested)
                    {
                        var s = steps[i];
                        if (s.Off || !Due(s, st.Loops))                    // switched off, or not this time: as if it wasn't there
                        {
                            i++;
                            continue;
                        }
                        StepIndex = i;
                        player.LogStep = i;
                        Round = RoundOf = 0;
                        for (int j = i + 1; j < steps.Count; j++)           // "round 2 of 6" for the nearest Repeat that covers this step
                            if (steps[j].Kind == "repeat" && !steps[j].Off && j - steps[j].Back <= i)
                            {
                                Round = rounds[j] + 1;
                                RoundOf = steps[j].Count;
                                break;
                            }
                        if (!s.IsControl) control = 0;
                        else if (s.Kind == "goto" && ++control > 2000)          // (a Repeat always ends by itself)
                            throw new Halt($"The steps around step {i + 1} keep jumping in a circle with nothing in between - stopped.", true);
                        if (!s.IsControl && s.Before > 0)
                        {
                            Enter("wait", Fmt.Num(s.Before));
                            if (!player.Sleep(s.Before, tok)) break;
                        }
                        Outcome o;
                        double stepStart = Timing.Now;
                        try { o = Exec(seq, steps, recs, i, st, log, rounds, jumps, tok); }
                        catch (Halt h) when (!h.Fatal && seq.OnError.Length > 0 && !tok.IsCancellationRequested)
                        {
                            st.Failures++;
                            if (seq.OnError == "restart")
                            {
                                failed = h.Message;
                                break;
                            }
                            log?.Add($"{h.Message}  Carrying on with the next step.", i);
                            o = Outcome.Next;
                        }
                        if (tok.IsCancellationRequested) break;
                        if (Timing.Now - stepStart >= 0.002) quick = 0;
                        else if (++quick >= 200)                           // hundreds of steps that took no time: never spin a core
                        {
                            quick = 0;
                            tok.WaitHandle.WaitOne(50);
                        }
                        if (o.EndRun) { endRun = true; break; }
                        if (o.EndLoop) break;
                        if (o.Jump >= 0)
                        {
                            st.Steps++;
                            runs[i]++;
                            i = o.Jump;
                            continue;
                        }
                        st.Steps++;
                        runs[i]++;
                        double rest = s.AfterStep + (s.IsControl ? 0 : seq.Gap + (seq.GapRandom > 0 ? rng.NextDouble() * seq.GapRandom : 0));
                        bool later = false;                                // is there anything after this step in this full run?
                        for (int j = i + 1; j < steps.Count && !later; j++)
                            later = !steps[j].Off && (steps[j].When.Length == 0 || Due(steps[j].When, steps[j].EveryN, st.Loops));
                        bool more = later || loops == 0 || st.Loops + 1 < loops;   // nothing follows the very last step
                        if (rest > 0 && more)
                        {
                            Enter("wait", Fmt.Num(rest));
                            if (!player.Sleep(rest, tok)) break;
                        }
                        i++;
                    }
                }
                finally { lap.End(); }
                if (stop.IsCancellationRequested) break;
                string again = failed ?? lap.Restart;
                if (again != null)                                         // a failed step, or an always-watch: go back to step 1
                {
                    st.Restarts++;
                    log?.Add($"{again}  Starting again from step 1.");
                    failedInARow = failed != null ? failedInARow + 1 : failedInARow;
                    if (failedInARow >= 25) throw new Halt($"25 full runs in a row failed - stopped. The last one: {failed}", true);
                    Enter("wait", "1");
                    if (!player.Sleep(1, stop)) break;
                    continue;
                }
                if (endRun) break;
                failedInARow = 0;
                st.Loops++;
                bool empty = st.Steps == stepsBefore;                      // nothing was due this time round
                if (!empty) log?.Add($"full run {st.Loops} finished ({Fmt.Dur(Timing.Now - RunStart)} since the start)");
                Loop = st.Loops + 1;
                if (loops > 0 && st.Loops >= loops) break;
                double tooQuick = (empty ? 0.25 : 0.05) - (Timing.Now - lapStart);     // a full run that took no time must not spin
                if (tooQuick > 0 && !player.Sleep(tooQuick, stop)) break;
                double between = loopDelay + (seq.LoopRandom > 0 ? rng.NextDouble() * seq.LoopRandom : 0);
                if (between > 0)
                {
                    Enter("wait", Fmt.Num(between));
                    if (!player.Sleep(between, stop)) break;
                }
            }
        }
        catch (Halt h)
        {
            st.Message = h.Message;
            cts.Cancel();
        }
        finally
        {
            limiter?.Dispose();
            guardsOff.Cancel();
            guardThread?.Join(3000);
            Phase = "";
            Watching = "";
            Clicking = false;
            StepIndex = -1;
            player.LogStep = -1;
            player.SpeedScale = 1;
        }
        if (lap.StopMessage != null && st.Message.Length == 0) st.Message = lap.StopMessage;
        st.Stopped = cts.IsCancellationRequested;
        st.TimedOut = !st.Stopped && limit.IsCancellationRequested;
        if (st.TimedOut) log?.Add($"the {Fmt.Num(seq.MaxMinutes)} minute time limit was reached");
        return st;
    }

    /// Run one step. `tok` ends it early (you stopped the run, or this full run is being started again).
    Outcome Exec(Sequence seq, List<SeqStep> steps, Recording[] recs, int i, SeqStats st, RunLog log, int[] rounds, int[] jumps, CancellationToken tok)
    {
        var s = steps[i];
        switch (s.Kind)
        {
            case "image" when s.Gone:
            {
                Enter("image", s.Image + " to go");
                var (gone, waited) = WaitGone(s, i, tok);
                if (tok.IsCancellationRequested) break;
                if (gone)
                {
                    st.ImageHits++;
                    log?.Add($"{s.Image} was gone after {Fmt.Dur(waited)}", i);
                }
                else
                {
                    st.ImageMisses++;
                    if (s.OnFail != "stop") log?.Add($"{s.Image} was still there after {Fmt.Num(s.Timeout)} s - skipped", i);
                    else
                    {
                        if (seq.OnError.Length == 0) log?.Add($"{s.Image} was still there after {Fmt.Num(s.Timeout)} s - stopped", i);
                        throw new Halt($"Step {i + 1}: “{s.Image}” was still on screen after {Fmt.Num(s.Timeout)} s - stopped.");
                    }
                }
                break;
            }
            case "image":
            {
                Enter("image", s.Image);
                var w = s.ToWait();
                var r = player.WaitImage(w, tok);
                if (r.Error != null) throw new Halt(r.Error, true);
                if (tok.IsCancellationRequested) break;
                if (r.Found)
                {
                    st.ImageHits++;
                    log?.Add($"waited {Fmt.Dur(r.Waited)} for {s.Image}, {w.Does}", i);
                }
                else if (r.Other != null)
                {
                    st.ImageHits++;
                    log?.Add($"{r.Other} showed instead of {s.Image} after {Fmt.Dur(r.Waited)} - moved on", i);
                }
                else
                {
                    st.ImageMisses++;
                    bool halt = s.OnFail == "stop";
                    if (!halt) log?.Add($"{s.Image} didn't show within {Fmt.Num(s.Timeout)} s (best match {r.Score:0.00}) - skipped", i);
                    else
                    {
                        if (seq.OnError.Length == 0) log?.Add($"{s.Image} didn't show within {Fmt.Num(s.Timeout)} s (best match {r.Score:0.00}) - stopped", i);
                        throw new Halt($"Step {i + 1}: “{s.Image}” didn't show up within {Fmt.Num(s.Timeout)} s (best match {r.Score:0.00}) - stopped.");
                    }
                }
                break;
            }
            case "play":
            {
                var rec = recs[i];
                (int, int) off;
                try { off = offsetOf(rec); }
                catch (InvalidOperationException e) { throw new Halt($"Step {i + 1} (“{s.Recording}”): {e.Message}", true); }
                double speed = (s.Speed > 0 ? s.Speed : 1) * (seq.Speed > 0 ? seq.Speed : 1);
                void SetSpeed() => player.SpeedScale = s.SpeedJitter > 0 ? speed * (1 + (rng.NextDouble() * 2 - 1) * s.SpeedJitter / 100) : speed;
                SetSpeed();
                var side = s.Ac ? StartSide(s, st, tok) : null;             // the clicks that go alongside
                try
                {
                    if (s.Until.Length > 0) PlayWatching(s, i, rec, off, st, log, tok);
                    else if (s.ForSecs > 0)                                 // keep replaying for a set time
                    {
                        using var timer = CancellationTokenSource.CreateLinkedTokenSource(tok);
                        timer.CancelAfter(TimeSpan.FromSeconds(s.ForSecs));
                        PlayOf = 0;
                        int passes = 0;
                        while (!timer.IsCancellationRequested)
                        {
                            PlayNo = ++passes;
                            SetSpeed();
                            var ps = PlayOnce(s, i, rec, off, st, timer.Token);
                            if (ps.Took < 0.05) timer.Token.WaitHandle.WaitOne(50);          // a very short recording must not spin
                        }
                        if (!tok.IsCancellationRequested) log?.Add($"played {s.Recording} for {Fmt.Num(s.ForSecs)} s ({passes} pass{(passes == 1 ? "" : "es")})", i);
                    }
                    else
                    {
                        PlayOf = s.Times;
                        for (int k = 0; k < s.Times && !tok.IsCancellationRequested; k++)
                        {
                            PlayNo = k + 1;
                            if (k > 0) SetSpeed();
                            var ps = PlayOnce(s, i, rec, off, st, tok);
                            if (ps.Stats.Events == 0 && ps.Took < 0.05 && s.Times > 1) tok.WaitHandle.WaitOne(50);   // nothing to send: don't spin
                            if (!ps.Stopped)
                                log?.Add($"played {s.Recording} in {Fmt.Dur(ps.Took)}" + (s.Times > 1 ? $" (run {k + 1} of {s.Times})" : "")
                                         + $", worst timing {ps.Stats.MaxLateMs:0.0} ms", i);
                        }
                    }
                }
                finally
                {
                    player.SpeedScale = 1;
                    int made = side?.Finish() ?? 0;
                    if (side?.Error != null) log?.Add($"the clicks alongside stopped early: {side.Error.Message}", i);
                    if (side != null && !tok.IsCancellationRequested)
                        log?.Add($"auto-clicked {made:N0} time{(made == 1 ? "" : "s")} alongside {s.Recording}", i);
                }
                break;
            }
            case "click":
            {
                Enter("click", s.ClickForever ? "" : s.ClickSecs > 0 ? Fmt.Num(s.ClickSecs) + " s" : s.Clicks.ToString());
                ClickI = 0;
                ClickN = s.ClickForever || s.ClickSecs > 0 ? 0 : s.Clicks;
                int made;
                try { made = player.Clicks(s.ToClicks(false), tok, n => ClickI = n); }
                catch (FileNotFoundException e) { throw new Halt($"Step {i + 1}: {e.Message}", true); }
                st.Clicks += made;
                if (!tok.IsCancellationRequested)
                    log?.Add($"{(s.More.KeyOnly ? "pressed " + s.More.Key : "clicked")} {made:N0} time{(made == 1 ? "" : "s")}"
                             + (s.More.Target.Length > 0 ? $" on {s.More.Target}"
                                : s.AcPoints.Count > 0 ? $" on {s.AcPoints.Count} spot{(s.AcPoints.Count == 1 ? "" : "s")}" : s.More.KeyOnly ? "" : " where the pointer was"), i);
                break;
            }
            case "key":
            {
                if (s.KeyMode == "type")
                {
                    Enter("key", "typing");
                    if (player.TypeText(s.Text, s.TypeDelay, tok)) log?.Add($"typed {s.Text.Length} character{(s.Text.Length == 1 ? "" : "s")}", i);
                }
                else
                {
                    Enter("key", s.Combo);
                    if (player.PressKeys(s.Combo, s.KeyTimes, s.KeyEvery, s.KeyHold, tok))
                        log?.Add($"pressed {s.Combo}" + (s.KeyTimes > 1 ? $" {s.KeyTimes} times" : ""), i);
                }
                break;
            }
            case "focus":
            {
                Enter("focus", s.WindowTitle);
                if (player.Focus(s.WindowTitle))
                {
                    log?.Add($"brought “{s.WindowTitle}” to the front", i);
                    player.Sleep(0.3, tok);                                // give the window a moment to come up
                }
                else if (s.OnFail == "stop") throw new Halt($"Step {i + 1}: no window with “{s.WindowTitle}” in its title - stopped.");
                else log?.Add($"no window with “{s.WindowTitle}” in its title - carried on", i);
                break;
            }
            case "alert":
            {
                string msg = s.Message.Length > 0 ? s.Message : $"Step {i + 1} reached";
                log?.Add($"alert: {msg}", i);
                try { Notify?.Invoke(msg, s.Sound); } catch { }
                break;
            }
            case "wait":
            {
                double secs = s.Seconds + (s.Random > 0 ? rng.NextDouble() * s.Random : 0);
                Enter("wait", Fmt.Num(Math.Round(secs, 1)));
                if (player.Sleep(secs, tok)) log?.Add($"paused {Fmt.Num(Math.Round(secs, 2))} s", i);
                break;
            }
            case "goto":
            {
                if (!Holds(s, i)) break;
                int to = Math.Clamp(s.GoTo, 1, steps.Count) - 1;
                if (s.MaxJumps > 0 && jumps[i] >= s.MaxJumps)
                {
                    log?.Add($"already jumped {s.MaxJumps} time{(s.MaxJumps == 1 ? "" : "s")} this run - carried on", i);
                    break;
                }
                jumps[i]++;
                log?.Add($"jumped to step {to + 1}" + (s.IfMode == "always" ? "" : $" ({s.IfImage} {(s.IfMode == "shown" ? "is on screen" : "is not on screen")})"), i);
                return new Outcome { Jump = to };
            }
            case "stop":
            {
                if (!Holds(s, i)) break;
                string why = s.IfMode == "always" ? "" : $" ({s.IfImage} {(s.IfMode == "shown" ? "is on screen" : "is not on screen")})";
                st.Steps++;
                Runs[i]++;
                if (s.StopMode == "loop")
                {
                    log?.Add($"ended this full run early{why}", i);
                    return new Outcome { Jump = -1, EndLoop = true };
                }
                log?.Add($"ended the run here{why}", i);
                return new Outcome { Jump = -1, EndRun = true };
            }
            case "repeat":
                if (++rounds[i] < s.Count)
                {
                    log?.Add($"round {rounds[i]} of {s.Count} done - going back to step {Math.Max(0, i - s.Back) + 1}", i);
                    return new Outcome { Jump = Math.Max(0, i - s.Back) };
                }
                log?.Add($"all {s.Count} rounds done", i);
                rounds[i] = 0;                                  // done: ready for next time (outer repeats, next loop)
                break;
        }
        return Outcome.Next;
    }

    /// Wait until a picture is no longer on screen (a loading screen, a countdown). Paused time doesn't count.
    (bool gone, double waited) WaitGone(SeqStep s, int i, CancellationToken tok)
    {
        if (!player.CanSeeImages) throw new Halt($"Step {i + 1}: it waits for “{s.Image}” to go, but image recognition is not available.", true);
        double t0 = Timing.Now, pausedFor = 0;
        int failures = 0;
        while (!tok.IsCancellationRequested)
        {
            if (player.Paused)
            {
                double p0 = Timing.Now;
                while (player.Paused && !tok.IsCancellationRequested) tok.WaitHandle.WaitOne(25);
                pausedFor += Timing.Now - p0;
                continue;
            }
            try
            {
                if (!player.Peek(s.Image, s.Conf).Found) return (true, Timing.Now - t0 - pausedFor);
                failures = 0;
            }
            catch (Exception e) when (e is FileNotFoundException or InvalidDataException) { throw new Halt($"Step {i + 1}: {e.Message}", true); }
            catch (Exception e)
            {
                if (++failures >= 25) throw new Halt($"Step {i + 1}: {e.Message}", true);       // the screen can't be read at all
            }
            if (s.Timeout > 0 && Timing.Now - t0 - pausedFor >= s.Timeout) return (false, Timing.Now - t0 - pausedFor);
            tok.WaitHandle.WaitOne(150);
        }
        return (false, Timing.Now - t0 - pausedFor);
    }

    /// The pictures a sequence watches for the whole time it runs. Each is looked for every so often; when one
    /// shows, its action happens, then it is left alone for its cool-down.
    void GuardLoop(List<SeqGuard> guards, Lap lap, SeqStats st, RunLog log, CancellationToken stop)
    {
        var due = new double[guards.Count];
        try
        {
            while (!stop.IsCancellationRequested)
            {
                if (stop.WaitHandle.WaitOne(100)) break;
                if (player.Paused) continue;
                for (int k = 0; k < guards.Count && !stop.IsCancellationRequested; k++)
                {
                    var g = guards[k];
                    if (Timing.Now < due[k]) continue;
                    due[k] = Timing.Now + g.Every;
                    Match m;
                    try { m = player.Peek(g.Image, g.Conf); }
                    catch (Exception) { continue; }                        // a look that failed is just a miss
                    if (!m.Found || player.Paused || stop.IsCancellationRequested) continue;
                    Interlocked.Increment(ref st.GuardHits);
                    due[k] = Timing.Now + Math.Max(g.Cooldown, g.Every);
                    switch (g.Action)
                    {
                        case "stop":
                            log?.Add($"always-watch: {g.Image} showed - stopping the run");
                            lap.StopMessage = $"Stopped because “{g.Image}” showed (an always-watch of this sequence).";
                            cts.Cancel();
                            player.Stop();
                            return;
                        case "restart":
                            log?.Add($"always-watch: {g.Image} showed");
                            lap.RestartNow($"“{g.Image}” showed.");
                            break;
                        case "alert":
                            log?.Add($"always-watch: {g.Image} showed - alert");
                            try { Notify?.Invoke($"“{g.Image}” showed", true); } catch { }
                            break;
                        default:
                        {
                            var w = new ImageWait { Name = g.Image, Button = g.Action == "key" ? null : "left", Key = g.Key.Length > 0 ? g.Key : null, Dx = g.Dx, Dy = g.Dy };
                            player.ActOn(m, w);
                            log?.Add($"always-watch: {g.Image} showed - {w.Does}");
                            break;
                        }
                    }
                }
            }
        }
        catch (ObjectDisposedException) { }
        catch (Exception e) { log?.Add($"always-watch stopped: {e.Message}"); }
    }

    /// Play the recording once and add its numbers to the run's totals. `cut` stops this one pass early.
    (PlayStats Stats, double Took, bool Stopped) PlayOnce(SeqStep s, int i, Recording rec, (int, int) off, SeqStats st, CancellationToken cut)
    {
        EventI = 0;
        EventN = rec.Events.Count;
        Enter("play", s.Recording);
        double p0 = Timing.Now;
        var ps = player.Play(rec, 1, 0, off, (ei, en, _) => { EventI = ei; EventN = en; }, cut, s.Only);
        st.Plays++;
        st.Events += ps.Events;
        st.ImageHits += ps.ImageHits;
        st.ImageMisses += ps.ImageMisses;
        st.MaxLateMs = Math.Max(st.MaxLateMs, ps.MaxLateMs);
        if (!string.IsNullOrEmpty(ps.Message)) throw new Halt($"Step {i + 1} (“{s.Recording}”): {ps.Message}");
        return (ps, Timing.Now - p0, ps.Stopped);
    }

    /// A play step that watches for a picture the whole time: the recording plays (again and again, if asked) while a
    /// second thread keeps looking at the screen. The sequence only moves on once the picture has been seen.
    void PlayWatching(SeqStep s, int i, Recording rec, (int, int) off, SeqStats st, RunLog log, CancellationToken stop)
    {
        if (!player.CanSeeImages) throw new Halt($"Step {i + 1}: it watches for “{s.Until}”, but image recognition is not available.", true);
        // (not disposed on purpose: the watcher may still be inside a slow look at the screen when this step ends)
        var cut = CancellationTokenSource.CreateLinkedTokenSource(stop);            // ends the pass that is playing
        var done = new CancellationTokenSource();                                   // ends the watcher
        var doneTok = done.Token;
        var w8 = new Watch();
        double t0 = Timing.Now, pausedFor = 0;
        Watching = s.Until;
        var watcher = new Thread(() =>
        {
            try
            {
                int failures = 0;
                double last = Timing.Now;
                while (!doneTok.IsCancellationRequested && !stop.IsCancellationRequested)
                {
                    double now = Timing.Now;
                    if (player.Paused) pausedFor += now - last;                 // time spent paused doesn't count towards the limit
                    else
                    {
                        try
                        {
                            var m = player.Peek(s.Until, s.UntilConf);
                            failures = 0;
                            if (m.Found && !player.Paused)
                            {
                                w8.Seen = m;
                                if (s.UntilMode == "stop") cut.Cancel();
                                return;
                            }
                        }
                        catch (Exception e) when (e is FileNotFoundException or InvalidDataException)
                        {
                            w8.Error = e.Message;
                            cut.Cancel();
                            return;
                        }
                        catch (Exception e)                                     // the screen can't be read for a moment: keep trying
                        {
                            if (++failures >= 25)
                            {
                                w8.Error = $"image '{s.Until}': {e.Message}";
                                cut.Cancel();
                                return;
                            }
                        }
                        if (s.UntilTimeout > 0 && now - t0 - pausedFor >= s.UntilTimeout)
                        {
                            w8.TimedOut = true;
                            cut.Cancel();
                            return;
                        }
                    }
                    last = now;
                    doneTok.WaitHandle.WaitOne(180);
                }
            }
            catch (ObjectDisposedException) { }                         // the step ended while this was looking
        }) { IsBackground = true, Name = "TinyRec image watch" };
        watcher.Start();
        int passes = 0;
        try
        {
            PlayOf = s.UntilLoop ? 0 : s.Times;
            while (!stop.IsCancellationRequested && !w8.Over)
            {
                if (!s.UntilLoop && passes >= s.Times)                      // played its share: now just wait for the picture
                {
                    Enter("image", s.Until);
                    while (!stop.IsCancellationRequested && !w8.Over) stop.WaitHandle.WaitOne(60);
                    break;
                }
                PlayNo = passes + 1;
                var pass = PlayOnce(s, i, rec, off, st, cut.Token);
                passes++;
                // a very short recording must not be replayed thousands of times a second
                if (pass.Took < 0.05 && !cut.IsCancellationRequested) cut.Token.WaitHandle.WaitOne(50);
            }
        }
        finally
        {
            done.Cancel();
            watcher.Join(3000);
            Watching = "";
        }
        if (stop.IsCancellationRequested) return;
        if (w8.Error != null) throw new Halt($"Step {i + 1}: {w8.Error}");
        double took = Timing.Now - t0;
        string where = $"during {s.Recording}" + (passes > 1 ? $" (pass {passes})" : "");
        if (w8.TimedOut)
        {
            st.ImageMisses++;
            bool halt = s.OnFail == "stop";
            log?.Add($"{s.Until} didn't show within {Fmt.Num(s.UntilTimeout)} s {where} - {(halt ? "stopped" : "moved on")}", i);
            if (halt) throw new Halt($"Step {i + 1}: “{s.Until}” didn't show up within {Fmt.Num(s.UntilTimeout)} s while {s.Recording} played - stopped.");
            return;
        }
        st.ImageHits++;
        var m0 = w8.Seen;
        var w = s.ToWatch();
        string did = s.UntilMode == "stop" ? "cut the recording short" : "let the recording finish";
        if (w.Button != null || w.Key != null)
        {
            var at = m0;
            if (s.UntilMode == "finish")                                   // it was seen a while ago: look again before clicking
            {
                try { at = player.Peek(s.Until, s.UntilConf); } catch { at = new Match(); }
            }
            if (at.Found)
            {
                player.ActOn(at, w);
                did += ", " + w.Does;
            }
            else did += ", but it was gone by then so nothing was clicked";
        }
        log?.Add($"saw {s.Until} after {Fmt.Dur(took)} {where} - {did}", i);
    }

    /// The clicks that go alongside a play step, on their own thread. Finish() stops them and says how many were made.
    sealed class Side
    {
        public CancellationTokenSource Cts;
        public Thread Thread;
        public int Made;
        public SeqStats Stats;
        public volatile Exception Error;
        public int Finish()
        {
            Cts.Cancel();
            Thread.Join(3000);
            int n = Volatile.Read(ref Made);
            Stats.Clicks += n;
            return n;
        }
    }

    Side StartSide(SeqStep s, SeqStats st, CancellationToken stop)
    {
        var side = new Side { Cts = CancellationTokenSource.CreateLinkedTokenSource(stop), Stats = st };
        var plan = s.ToClicks(true);
        var tok = side.Cts.Token;
        Clicking = true;
        side.Thread = new Thread(() =>
        {
            try { player.Clicks(plan, tok, n => Volatile.Write(ref side.Made, n)); }
            catch (ObjectDisposedException) { }
            catch (Exception e) { side.Error = e; }               // never take the whole app down from this thread
            finally { Clicking = false; }
        }) { IsBackground = true, Name = "TinyRec clicks alongside" };
        side.Thread.Start();
        return side;
    }

    /// What the watcher thread of a play step has found out (written there, read on the playing thread).
    sealed class Watch
    {
        public volatile Match Seen;
        public volatile string Error;
        public volatile bool TimedOut;
        public bool Over => Seen != null || Error != null || TimedOut;
    }

    /// Ends the run with a message. Fatal ones can't be turned into "start again" / "carry on" by the sequence's
    /// own "if a step fails" setting (a missing file, steps going round in circles ...).
    sealed class Halt : Exception
    {
        public readonly bool Fatal;
        public Halt(string m, bool fatal = false) : base(m) => Fatal = fatal;
    }
}
