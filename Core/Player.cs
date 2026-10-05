// Player.cs - drift-free playback and the auto clicker.
//
// Every action is scheduled against ONE fixed anchor time, so delays never add up. Changing speed moves the
// anchor to "now" at the current position. Waits sleep coarsely, then spin for the last ~2 ms.
using System.Diagnostics;

namespace TinyRec.Core;

/// What playback needs from the operating system (Windows implementation lives in the app).
public interface IInput
{
    void Move(int x, int y);
    void Button(string name, bool down);
    void Click(string name, int count);
    /// Move to a spot and click it "like a person": let the pointer settle, nudge it, hold the press a moment.
    /// Games (Roblox etc.) often ignore a click that lands in the same instant the pointer arrives.
    void ClickAt(int x, int y, string name, int count) { Move(x, y); Click(name, count); }
    void Scroll(double dx, double dy);
    void Key(string token, bool down);
    /// Where the pointer is right now (null = unknown).
    (int x, int y)? Pointer() => null;
    /// Bring the window whose title contains `title` to the front. False if there is no such window.
    bool Focus(string title) => false;
}

public sealed class Match
{
    public bool Found;
    public int X, Y;
    public double Score;
}

public interface IImageFinder
{
    /// region = (x, y, w, h) in screen pixels, or null for the whole screen. Throws FileNotFoundException
    /// if the image isn't in the library.
    Match Locate(string name, double conf, (int x, int y, int w, int h)? region);
}

public static class Timing
{
    public static double Now => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

    /// Sleep until Now == target. Returns false if stop was set first.
    public static bool SleepUntil(double target, CancellationToken stop)
    {
        while (true)
        {
            double rem = target - Now;
            if (rem <= 0) return true;
            if (stop.IsCancellationRequested) return false;
            if (rem > 0.004) Thread.Sleep((int)Math.Max(1, Math.Min((rem - 0.003) * 1000, 20)));   // Sleep can wake ~1-2 ms late,
            else Thread.SpinWait(40);                                                               // so spin the last 3-4 ms
        }
    }
}

/// A time limit that doesn't count paused time: cancels `cts` once `seconds` of un-paused time have gone by.
public sealed class PausableLimit : IDisposable
{
    readonly object gate = new();
    readonly Timer timer;
    double used, last = Timing.Now;
    bool done;

    public PausableLimit(CancellationTokenSource cts, double seconds, Func<bool> paused)
    {
        timer = new Timer(_ =>
        {
            lock (gate)
            {
                if (done) return;
                double now = Timing.Now;
                if (paused?.Invoke() != true) used += now - last;
                last = now;
                if (used < seconds) return;
                done = true;
                try { cts.Cancel(); } catch (ObjectDisposedException) { }
            }
        }, null, 50, 50);
    }

    public void Dispose()
    {
        lock (gate) done = true;
        timer.Dispose();
    }
}

sealed class Clock
{
    readonly Func<double> getSpeed;
    public double Speed, AnchorReal, AnchorRec;

    public Clock(Func<double> getSpeed)
    {
        this.getSpeed = getSpeed;
        Speed = Read();
        AnchorReal = Timing.Now;
        AnchorRec = 0;
    }

    double Read()
    {
        try { return Math.Clamp(getSpeed(), 0.05, 20); } catch { return 1; }
    }

    public void Refresh(double now)
    {
        var s = Read();
        if (s != Speed)
        {
            AnchorRec += (now - AnchorReal) * Speed;
            AnchorReal = now;
            Speed = s;
        }
    }

    public double RealFor(double t) => AnchorReal + (t - AnchorRec) / Speed;
    public void Reanchor(double t, double now) { AnchorReal = now; AnchorRec = t; }
}

public sealed class PlayStats
{
    public int Events, Loops, ImageHits, ImageMisses;
    public double MaxLateMs, MeanLateMs;
    public bool Stopped;
    public string Message = "";
}

/// What an image step should wait for, and what to do when it shows.
public sealed class ImageWait
{
    public string Name = "";
    public double Conf = 0.9;
    public double Timeout;                    // seconds; 0 = no limit
    public (int, int, int, int)? Region;      // null = the app's default search area
    public string Button;                     // left / right / double; null = don't click
    public string Key;                        // a key to press when it shows; null = none
    public List<string> Or = new();           // other pictures that also end the wait (nothing is clicked or pressed for those)
    public int Dx, Dy;                        // click this far from the picture's centre (pixels)

    public string Does =>
        Button != null && !string.IsNullOrEmpty(Key) ? $"clicked it and pressed {Key}"
        : Button != null ? "clicked it" : !string.IsNullOrEmpty(Key) ? $"pressed {Key}" : "carried on";
}

/// Which actions of a recording count - for playback ("Play back") and for recording ("Record").
///
/// The setting is a short text:
///   ""       everything
///   "keys"   keyboard only. Picture steps still wait for their picture (and press their key), but don't click it.
///   "mouse"  mouse only. Picture steps still wait and click, but don't press their key.
///   custom   any of  moves  clicks  scroll  keys  images  joined with "+", e.g. "clicks+keys" (no pointer travel
///            and no scrolling). Leave "images" out to drop picture steps completely.
/// Any of them can be followed by keys to leave out, each with a minus: "keys -tab -esc", " -f5" (everything but F5).
public static class Only
{
    public const string Keys = "keys", Mouse = "mouse";
    public static readonly string[] Parts = { "moves", "clicks", "scroll", "keys", "images" };

    public sealed class Spec
    {
        public bool Moves = true, Clicks = true, Scroll = true, KeysOn = true, Images = true;
        public HashSet<string> Skip = new();
        public bool All => Moves && Clicks && Scroll && KeysOn && Images && Skip.Count == 0;

        /// Is this key one of the keys to leave out? "ctrl" covers both Ctrl keys, and so on.
        public bool Skips(string token)
        {
            if (Skip.Count == 0 || string.IsNullOrEmpty(token)) return false;
            if (Skip.Contains(token)) return true;
            var mod = TinyRec.Core.Keys.ModOf(token);
            return mod != null && Skip.Contains(mod);
        }

        public bool Keeps(Event e)
        {
            var k = e.Kind;
            if (k.StartsWith("KEY_")) return KeysOn && !Skips(e.Args.Count > 0 ? e.Args[0] : "");
            if (e.IsImage) return Images;
            if (k == "MOVE") return Moves;
            if (k == "SCROLL") return Scroll;
            return Clicks;                                                  // a mouse button going down or up
        }
    }

    static readonly Spec Everything = new();

    static readonly Dictionary<string, string> Alias = new()
    {
        ["escape"] = "esc", ["control"] = "ctrl", ["return"] = "enter", ["win"] = "cmd", ["windows"] = "cmd", ["del"] = "delete",
        ["spacebar"] = "space", ["pgup"] = "page_up", ["pgdn"] = "page_down", ["pageup"] = "page_up", ["pagedown"] = "page_down",
        ["ins"] = "insert", ["capslock"] = "caps_lock", ["bksp"] = "backspace",
    };

    /// The name a key to leave out is kept under ("Escape" -> "esc").
    public static string KeyName(string k)
    {
        k = (k ?? "").Trim().ToLowerInvariant();
        return Alias.TryGetValue(k, out var a) ? a : k;
    }

    public static Spec Parse(string s)
    {
        s = (s ?? "").Trim().ToLowerInvariant();
        if (s.Length == 0) return Everything;
        var words = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var skip = words.Where(w => w.StartsWith("-") && w.Length > 1).Select(w => KeyName(w[1..])).Where(w => w.Length > 0).ToHashSet();
        var main = words.FirstOrDefault(w => !w.StartsWith("-")) ?? "";
        var sp = new Spec { Skip = skip };
        if (main == "none") sp.Moves = sp.Clicks = sp.Scroll = sp.KeysOn = sp.Images = false;
        else if (main == Keys) sp.Moves = sp.Clicks = sp.Scroll = false;
        else if (main == Mouse) sp.KeysOn = false;
        else if (main.Length > 0)
        {
            var on = main.Split('+', StringSplitOptions.RemoveEmptyEntries).Where(Parts.Contains).ToHashSet();
            if (on.Count > 0)
            {
                sp.Moves = on.Contains("moves");
                sp.Clicks = on.Contains("clicks");
                sp.Scroll = on.Contains("scroll");
                sp.KeysOn = on.Contains("keys");
                sp.Images = on.Contains("images");
            }
        }
        return sp;
    }

    /// The tidy form of a setting ("" when it means everything).
    public static string Clean(string s)
    {
        var sp = Parse(s);
        if (sp.All) return "";
        bool mouseAll = sp.Moves && sp.Clicks && sp.Scroll, mouseNone = !sp.Moves && !sp.Clicks && !sp.Scroll;
        string main = mouseAll && sp.KeysOn && sp.Images ? ""
                    : mouseNone && sp.KeysOn && sp.Images ? Keys
                    : mouseAll && !sp.KeysOn && sp.Images ? Mouse
                    : string.Join("+", new[] { sp.Moves ? "moves" : null, sp.Clicks ? "clicks" : null, sp.Scroll ? "scroll" : null,
                                               sp.KeysOn ? "keys" : null, sp.Images ? "images" : null }.Where(x => x != null).DefaultIfEmpty("none"));
        if (main == Keys && !sp.Images) main = "+" + Keys;                  // just "keys" would read back as the Keyboard-only preset
        string skip = sp.KeysOn ? string.Join(" ", sp.Skip.OrderBy(k => k).Select(k => "-" + k)) : "";
        return (main + " " + skip).Trim();
    }

    public static bool IsKey(Event e) => e.Kind.StartsWith("KEY_");
    public static bool Keeps(string only, Event e) => Parse(only).Keeps(e);
    public static List<Event> Of(IEnumerable<Event> events, string only)
    {
        var sp = Parse(only);
        return events.Where(sp.Keeps).ToList();
    }

    public static string Name(string only)
    {
        var sp = Parse(only);
        string main = sp.All ? "mouse and keyboard"
                    : !sp.Moves && !sp.Clicks && !sp.Scroll && sp.KeysOn && sp.Images ? "keyboard only"
                    : sp.Moves && sp.Clicks && sp.Scroll && !sp.KeysOn && sp.Images ? "mouse only"
                    : sp.Moves && sp.Clicks && sp.Scroll && sp.KeysOn && sp.Images ? "everything"
                    : "only " + string.Join(", ", new[] { sp.Moves ? "pointer moves" : null, sp.Clicks ? "clicks" : null, sp.Scroll ? "scrolling" : null,
                                                        sp.KeysOn ? "keys" : null, sp.Images ? "image steps" : null }.Where(x => x != null).DefaultIfEmpty("nothing"));
        if (sp.KeysOn && sp.Skip.Count > 0) main += " (not " + string.Join(", ", sp.Skip.OrderBy(k => k)) + ")";
        return main;
    }
}

public sealed class Player
{
    readonly IInput input;
    readonly IImageFinder finder;
    readonly Func<double> getSpeed;
    CancellationTokenSource cts = new();
    volatile bool paused;
    (int x, int y)? lastPos;

    public Player(IInput input, IImageFinder finder, Func<double> getSpeed)
    {
        this.input = input;
        this.finder = finder;
        this.getSpeed = () => getSpeed() * Volatile.Read(ref speedScale);
    }

    /// An extra speed factor on top of the app's speed setting (a sequence sets it per step; 1 = no change).
    public double SpeedScale
    {
        get => Volatile.Read(ref speedScale);
        set => Volatile.Write(ref speedScale, double.IsFinite(value) && value > 0 ? Math.Clamp(value, 0.05, 20) : 1);
    }
    double speedScale = 1;

    public bool CanSeeImages => finder != null;

    /// One look at the screen for a picture, right now (no waiting). Safe to call from another thread while a
    /// recording plays. Throws FileNotFoundException if the picture isn't in the library.
    public Match Peek(string name, double conf) => finder.Locate(name, conf, null);

    /// Do what an image step does once its picture is on screen: click it and / or press the key.
    public void ActOn(Match m, ImageWait w)
    {
        if (w.Button != null)
            lock (ClickGate) input.ClickAt(m.X + w.Dx, m.Y + w.Dy, w.Button == "double" ? "left" : w.Button, w.Button == "double" ? 2 : 1);
        if (!string.IsNullOrEmpty(w.Key))
        {
            if (w.Button != null) Thread.Sleep(80);
            input.Key(w.Key, true);
            Thread.Sleep(60);                                // held for a few frames so games notice it
            input.Key(w.Key, false);
        }
    }

    /// One "go there and click" at a time (shared with every clicker).
    public static object ClickGate => ClickEngine.Gate;

    /// Set by the app: true when a screen point is on one of TinyRec's own windows. Clicks "where the pointer is"
    /// are held back there, so they can never press TinyRec's own Stop button.
    public Func<int, int, bool> IsOwn;

    /// True while clicks "where the pointer is" are being held back because the pointer is on TinyRec itself.
    public volatile bool ClicksHeldOff;

    /// Click according to `plan` until it is done or `stop` is set. Safe to run on its own thread while a recording
    /// plays (the clicks "alongside"). Pausing playback pauses the clicks too, and the paused time doesn't count.
    /// Returns how many clicks were made. Throws FileNotFoundException if a picture the plan needs is missing.
    public int Clicks(ClickPlan plan, CancellationToken stop, Action<int> onClick = null) =>
        ClickEngine.Run(plan, new ClickEngine.Env
        {
            Input = input, Finder = finder, Paused = () => paused, IsOwn = (x, y) => IsOwn?.Invoke(x, y) == true,
            HeldOff = v => ClicksHeldOff = v, OnClick = onClick,
        }, stop);

    /// Bring a window to the front (false = no window with that text in its title).
    public bool Focus(string title) => input.Focus(title);

    /// Press a key or a combination ("e", "ctrl+shift+a") `times` times. Returns false if stopped first.
    public bool PressKeys(string combo, int times, double everyMs, double holdMs, CancellationToken stop)
    {
        var parts = (combo ?? "").Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(k => k.ToLowerInvariant()).ToList();
        if (parts.Count == 0) return true;
        double next = Timing.Now;
        for (int k = 0; k < Math.Max(1, times); k++)
        {
            if (!SleepPausable(next, stop)) return false;
            var down = new List<string>();
            try
            {
                foreach (var key in parts) { input.Key(key, true); down.Add(key); }
                Timing.SleepUntil(Timing.Now + Math.Max(10, holdMs) / 1000, stop);
            }
            finally
            {
                down.Reverse();
                foreach (var key in down) input.Key(key, false);
            }
            next = Math.Max(next + Math.Max(1, everyMs) / 1000, Timing.Now);
        }
        return !stop.IsCancellationRequested;
    }

    /// Type text one character at a time. Returns false if stopped first.
    public bool TypeText(string text, double perCharMs, CancellationToken stop)
    {
        double next = Timing.Now;
        foreach (char ch in text ?? "")
        {
            if (!SleepPausable(next, stop)) return false;
            string tok = ch == '\n' ? "enter" : ch == '\t' ? "tab" : Keys.CharToken(ch);
            input.Key(tok, true);
            input.Key(tok, false);
            next = Timing.Now + Math.Max(1, perCharMs) / 1000;
        }
        return !stop.IsCancellationRequested;
    }

    /// Wait until `target`, holding still (and not counting the time) while paused. False if stopped.
    bool SleepPausable(double target, CancellationToken stop)
    {
        while (true)
        {
            if (stop.IsCancellationRequested) return false;
            if (paused)
            {
                double p0 = Timing.Now;
                while (paused && !stop.IsCancellationRequested) stop.WaitHandle.WaitOne(25);
                target += Timing.Now - p0;
                continue;
            }
            double rem = target - Timing.Now;
            if (rem <= 0) return true;
            if (rem > 0.03) stop.WaitHandle.WaitOne(25);
            else if (!Timing.SleepUntil(target, stop)) return false;
        }
    }

    /// Thread-safe: callable from any thread (e.g. the panic hotkey).
    public void Stop()
    {
        try { cts.Cancel(); } catch (ObjectDisposedException) { }
    }

    /// Pause freezes playback where it is (anything held down is let go); Resume picks up at the same spot.
    public bool Paused => paused;
    public void Pause() => paused = true;
    public void Resume() => paused = false;

    /// Set while playback is held waiting for a picture (null otherwise) - the app shows it as the status.
    public volatile string WaitingFor;
    public double WaitingSince;

    /// Optional diary of what happened. LogStep tags lines with the sequence step that is playing (-1 = not in a sequence).
    public RunLog Log;
    public int LogStep = -1;
    HashSet<string> nowButtons, nowKeys;                // what the recording being played is holding down right now

    sealed class StopPlayback : Exception
    {
        public StopPlayback(string m) : base(m) { }
    }

    /// loops: 0 = forever. onProgress(eventIndex, eventCount, loopNumber) is called from the playback thread.
    /// external: an extra stop switch (a sequence passes its own, so stopping the sequence stops the recording too).
    public PlayStats Play(Recording rec, int loops = 1, double loopDelay = 0, (int dx, int dy) offset = default,
                          Action<int, int, int> onProgress = null, CancellationToken external = default, string only = null,
                          double loopRandom = 0)
    {
        var before = cts;
        cts = CancellationTokenSource.CreateLinkedTokenSource(external);
        if (!ReferenceEquals(before, cts)) before?.Dispose();          // a long sequence plays thousands of recordings
        var stop = cts.Token;
        var events = rec.Events.Select((e, i) => (e, i)).OrderBy(p => p.e.T).ThenBy(p => p.i).Select(p => p.e).ToList();
        // keyboard only / mouse only: the other actions are left out, but the recording keeps its full length
        double fullLength = events.Count > 0 ? events[^1].T : 0;
        var spec = Only.Parse(only);
        playOnly = spec;
        if (!spec.All) events = events.Where(spec.Keeps).ToList();
        bool padEnd = !spec.All && fullLength > (events.Count > 0 ? events[^1].T : 0) + 0.0005;
        var st = new PlayStats();
        double lateSum = 0, lastReport = 0;
        var heldButtons = new HashSet<string>();
        var heldKeys = new HashSet<string>();
        lastPos = null;
        nowButtons = heldButtons;
        nowKeys = heldKeys;
        try
        {
            while (!stop.IsCancellationRequested && (events.Count > 0 || padEnd))
            {
                var clock = new Clock(getSpeed);
                double loopStart = Timing.Now, loopWorst = 0;
                for (int i = 0; i < events.Count; i++)
                {
                    var ev = events[i];
                    var late = Wait(clock, ev.T, stop, heldButtons, heldKeys);
                    if (late == null) break;
                    st.MaxLateMs = Math.Max(st.MaxLateMs, late.Value * 1000);
                    loopWorst = Math.Max(loopWorst, late.Value * 1000);
                    lateSum += Math.Abs(late.Value);
                    Dispatch(ev, st, heldButtons, heldKeys, offset, stop);
                    st.Events++;
                    if (ev.IsImage) clock.Reanchor(ev.T, Timing.Now);   // don't rush to "catch up" after waiting for a picture
                    if (onProgress != null && Timing.Now - lastReport > 0.05)
                    {
                        lastReport = Timing.Now;
                        onProgress(i + 1, events.Count, st.Loops + 1);
                    }
                    if (stop.IsCancellationRequested) break;
                }
                if (stop.IsCancellationRequested) break;
                if (padEnd && Wait(clock, fullLength, stop, heldButtons, heldKeys) == null) break;
                st.Loops++;
                if (LogStep < 0)
                    Log?.Add($"loop {st.Loops} finished in {Fmt.Dur(Timing.Now - loopStart)} - {events.Count:N0} actions, worst timing {loopWorst:0.0} ms");
                if (loops > 0 && st.Loops >= loops) break;
                double between = loopDelay + (loopRandom > 0 ? Random.Shared.NextDouble() * loopRandom : 0);
                if (between > 0 && !Sleep(between, stop)) break;
            }
        }
        catch (StopPlayback e)
        {
            st.Message = e.Message;
            cts.Cancel();
        }
        finally
        {
            nowButtons = nowKeys = null;
            foreach (var b in heldButtons) input.Button(b, false);      // never leave anything held down
            foreach (var k in heldKeys) input.Key(k, false);
        }
        st.Stopped = cts.IsCancellationRequested;
        if (st.Events > 0) st.MeanLateMs = lateSum / st.Events * 1000;
        return st;
    }

    /// Wait until recorded time t. Returns how late we are (seconds), or null if stopped.
    double? Wait(Clock clock, double t, CancellationToken stop, HashSet<string> heldB, HashSet<string> heldK)
    {
        while (true)
        {
            if (paused && !HoldWhilePaused(clock, stop, heldB, heldK)) return null;
            double now = Timing.Now;
            clock.Refresh(now);
            double rem = clock.RealFor(t) - now;
            if (rem <= 0) return Math.Max(0, -rem);
            if (stop.IsCancellationRequested) return null;
            if (rem > 0.004) Thread.Sleep((int)Math.Max(1, Math.Min((rem - 0.003) * 1000, 20)));   // Sleep can wake ~1-2 ms late,
            else Thread.SpinWait(40);                                                               // so spin the last 3-4 ms
        }
    }

    /// Sit out a pause: let go of anything held, wait, take hold again, and move the clock so no time was "lost".
    /// Returns false if playback was stopped while paused.
    bool HoldWhilePaused(Clock clock, CancellationToken stop, HashSet<string> heldB, HashSet<string> heldK)
    {
        double p0 = Timing.Now;
        foreach (var b in heldB) input.Button(b, false);
        foreach (var k in heldK) input.Key(k, false);
        Log?.Add("paused", LogStep);
        while (paused && !stop.IsCancellationRequested) stop.WaitHandle.WaitOne(25);
        if (stop.IsCancellationRequested) return false;
        if (heldB.Count > 0 && lastPos is { } p) input.Move(p.x, p.y);     // the pointer may have been moved meanwhile
        foreach (var k in heldK) input.Key(k, true);
        foreach (var b in heldB) input.Button(b, true);
        clock.AnchorReal += Timing.Now - p0;
        Log?.Add($"resumed after {Fmt.Dur(Timing.Now - p0)}", LogStep);
        return true;
    }

    /// A sleep that stops counting while paused. Returns false if stopped first.
    public bool Sleep(double seconds, CancellationToken stop)
    {
        double left = seconds, last = Timing.Now;
        while (true)
        {
            if (stop.IsCancellationRequested) return false;
            double now = Timing.Now;
            if (!paused) left -= now - last;
            last = now;
            if (left <= 0) return true;
            stop.WaitHandle.WaitOne(paused ? 25 : (int)Math.Clamp(left * 1000, 1, 40));
        }
    }

    void Dispatch(Event ev, PlayStats st, HashSet<string> heldB, HashSet<string> heldK, (int dx, int dy) off, CancellationToken stop)
    {
        var k = ev.Kind;
        if (k == "MOVE")
        {
            lastPos = (ev.IntArg(0) + off.dx, ev.IntArg(1) + off.dy);
            input.Move(lastPos.Value.x, lastPos.Value.y);
        }
        else if (k == "SCROLL")
        {
            lastPos = (ev.IntArg(0) + off.dx, ev.IntArg(1) + off.dy);
            input.Move(lastPos.Value.x, lastPos.Value.y);
            input.Scroll(ev.NumArg(2), ev.NumArg(3));
        }
        else if (k.StartsWith("KEY_"))
        {
            bool down = k == "KEY_DOWN";
            input.Key(ev.Args[0], down);
            if (down) heldK.Add(ev.Args[0]); else heldK.Remove(ev.Args[0]);
        }
        else if (k.StartsWith("IMAGE_"))
        {
            ImageStep(ev, st, stop);
        }
        else
        {
            var parts = k.Split('_');
            var name = parts[0].ToLowerInvariant();
            bool down = parts[1] == "DOWN";
            lastPos = (ev.IntArg(0) + off.dx, ev.IntArg(1) + off.dy);
            input.Move(lastPos.Value.x, lastPos.Value.y);
            input.Button(name, down);
            if (down) heldB.Add(name); else heldB.Remove(name);
        }
    }

    /// Read an IMAGE_CLICK / IMAGE_WAIT line into "what to wait for and what to do".
    public static ImageWait ImageWaitOf(Event ev)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var w = new ImageWait
        {
            Name = ev.Args[0],
            Conf = double.Parse(ev.Opt("conf", "0.9"), inv),
            Timeout = double.Parse(ev.Opt("timeout", "10"), inv),
            Button = ev.Kind == "IMAGE_CLICK" ? ev.Opt("button", "left") : null,
            Key = ev.Opt("key"),
        };
        var r = ev.Opt("region");
        if (r != null)
        {
            var p = r.Split(',').Select(int.Parse).ToArray();
            w.Region = (p[0], p[1], p[2], p[3]);
        }
        var or = ev.Opt("or");
        if (or != null) w.Or = or.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (ev.Opt("onfail", "skip") == "wait") w.Timeout = 0;           // "keep waiting" = no limit
        return w;
    }

    Only.Spec playOnly = Only.Parse("");                // the filter of the recording that is playing

    void ImageStep(Event ev, PlayStats st, CancellationToken stop)
    {
        var w = ImageWaitOf(ev);
        if (!playOnly.Clicks) w.Button = null;              // keyboard only: wait for the picture, but leave the mouse alone
        if (!playOnly.KeysOn) w.Key = null;
        var res = WaitImage(w, stop);
        if (res.Error != null) throw new StopPlayback(res.Error);
        if (stop.IsCancellationRequested) return;
        string at = $"at {Fmt.Dur(ev.T)}: ";
        if (res.Found)
        {
            st.ImageHits++;
            Log?.Add($"{at}waited {Fmt.Dur(res.Waited)} for {w.Name}, {w.Does}", LogStep, ev.T);
        }
        else if (res.Other != null)
        {
            st.ImageHits++;
            Log?.Add($"{at}{res.Other} showed instead of {w.Name} after {Fmt.Dur(res.Waited)} - carried on", LogStep, ev.T);
        }
        else
        {
            st.ImageMisses++;
            bool halt = ev.Opt("onfail", "skip") == "stop";
            Log?.Add($"{at}{w.Name} not found after {Fmt.Dur(res.Waited)} (best match {res.Score:0.00}) - {(halt ? "stopped" : "skipped")}", LogStep, ev.T);
            if (halt) throw new StopPlayback($"'{w.Name}' not found (best match {res.Score:0.00}) - stopped");
        }
    }

    /// Found: the picture showed (and was clicked / the key pressed). Other: one of the "or" pictures showed instead.
    /// Neither, with Error null: the time limit ran out, or playback was stopped.
    public readonly record struct ImageResult(bool Found, double Score, string Error, string Other, double Waited);

    /// Look for a picture until it shows up, an "or" picture shows, the time limit runs out, or `stop` is set.
    /// Time spent paused doesn't count towards the limit.
    public ImageResult WaitImage(ImageWait w, CancellationToken stop)
    {
        if (finder == null) return new(false, 0, "image step found but image recognition is not available", null, 0);
        if (!string.IsNullOrEmpty(w.Key) && !Keys.IsKnown(w.Key) && !Keys.IsChar(w.Key))
            return new(false, 0, $"image '{w.Name}': '{w.Key}' is not a key name", null, 0);
        double t0 = Timing.Now, pausedFor = 0;
        var m = new Match();
        string other = null;
        int failures = 0;
        WaitingSince = t0;
        WaitingFor = w.Name;
        try
        {
            while (true)
            {
                if (paused)
                {
                    double p0 = Timing.Now;
                    var hb = nowButtons?.ToList() ?? new List<string>();
                    var hk = nowKeys?.ToList() ?? new List<string>();
                    foreach (var b in hb) input.Button(b, false);            // let go of anything held while paused
                    foreach (var k in hk) input.Key(k, false);
                    Log?.Add("paused", LogStep);
                    while (paused && !stop.IsCancellationRequested) stop.WaitHandle.WaitOne(25);
                    pausedFor += Timing.Now - p0;
                    WaitingSince = t0 + pausedFor;
                    if (stop.IsCancellationRequested) break;
                    foreach (var k in hk) input.Key(k, true);
                    foreach (var b in hb) input.Button(b, true);
                    Log?.Add($"resumed after {Fmt.Dur(Timing.Now - p0)}", LogStep);
                }
                try
                {
                    m = finder.Locate(w.Name, w.Conf, w.Region);
                    if (!m.Found)
                        foreach (var o in w.Or)
                            if (finder.Locate(o, w.Conf, w.Region).Found)
                            {
                                other = o;
                                break;
                            }
                }
                catch (FileNotFoundException e) { return new(false, 0, e.Message, null, 0); }
                catch (InvalidDataException e) { return new(false, 0, e.Message, null, 0); }
                catch (Exception e)
                {
                    // the screen can't be read for a moment (lock screen, a permission prompt, the display changing):
                    // keep trying for a while instead of ending a long run over one bad look
                    if (++failures >= 25) return new(false, 0, $"image '{w.Name}': {e.Message}", null, 0);
                    if (stop.WaitHandle.WaitOne(400)) break;
                    continue;
                }
                failures = 0;
                if (stop.IsCancellationRequested) break;
                if ((m.Found || other != null) && paused)                 // paused while looking: don't act, look again after
                {
                    other = null;
                    m = new Match();
                    continue;
                }
                if (m.Found || other != null) break;
                if (w.Timeout > 0 && Timing.Now - t0 - pausedFor >= w.Timeout) break;
                if (stop.WaitHandle.WaitOne(120)) break;      // a few looks per second is plenty, and easy on the CPU
            }
        }
        finally { WaitingFor = null; }
        double waited = Timing.Now - t0 - pausedFor;
        if (stop.IsCancellationRequested) return new(false, m.Score, null, null, waited);
        if (other != null) return new(false, m.Score, null, other, waited);
        if (!m.Found) return new(false, m.Score, null, null, waited);
        ActOn(m, w);
        return new(true, m.Score, null, null, waited);
    }
}

public sealed class AutoClickConfig
{
    public string Button { get; set; } = "left";      // left / right / middle
    public bool Double { get; set; }
    public double IntervalMs { get; set; } = 100;
    public double JitterMs { get; set; }              // each gap becomes interval +/- jitter
    public double HoldMs { get; set; }                // > 0 = hold each press this long
    public int Count { get; set; }                    // 0 = until stopped
    public double Seconds { get; set; }               // > 0 = stop after this many seconds
    public string Mode { get; set; } = "cursor";      // cursor / fixed / points
    public List<int[]> Points { get; set; } = new();  // [[x, y], ...]
    public string Order { get; set; } = "sequence";   // sequence / random / pingpong
    public double StartDelay { get; set; }
    public ClickMore More { get; set; } = new();      // the finer settings (Jitter / Hold / Order / StartDelay above win over its own)

    public AutoClickConfig Copy()
    {
        var c = (AutoClickConfig)MemberwiseClone();
        c.Points = Points.Select(p => p.ToArray()).ToList();
        c.More = (More ?? new ClickMore()).Copy();
        return c;
    }

    public ClickPlan ToPlan()
    {
        var m = (More ?? new ClickMore()).Copy().Clean();
        m.Jitter = JitterMs;
        m.Hold = HoldMs;
        m.Order = Order is "random" or "pingpong" ? Order : "sequence";
        m.StartDelay = StartDelay;
        if (m.Style == "auto") m.Style = "fast";          // the Auto Clicker page has always clicked without settling first
        m.LimitClicks = 0;
        m.LimitSecs = 0;
        return new ClickPlan
        {
            Points = Mode == "cursor" ? new() : Mode == "fixed" ? Points.Take(1).Select(p => p.ToArray()).ToList() : Points.Select(p => p.ToArray()).ToList(),
            Button = Button, Double = Double, IntervalMs = IntervalMs, Count = Count, Seconds = Seconds, More = m,
        };
    }
}

public sealed class AutoClicker
{
    readonly IInput input;
    readonly IImageFinder finder;
    CancellationTokenSource cts = new();
    Thread thread;
    int clicks;

    public AutoClicker(IInput input, IImageFinder finder = null)
    {
        this.input = input;
        this.finder = finder;
    }

    public int Clicks => Volatile.Read(ref clicks);
    public bool Running => thread != null && thread.IsAlive;

    public void Halt() => cts.Cancel();

    /// While true the clicker holds still (playback it runs alongside is paused).
    public volatile bool Hold;

    /// Optional: true when a point is on one of TinyRec's own windows. "Follow cursor" clicks are held back there
    /// (the app sets it for the clicker that runs alongside playback).
    public volatile Func<int, int, bool> IsOwn;

    /// Why the last run ended early ("" = it didn't): a missing picture, for example.
    public volatile string Error = "";

    public void Start(AutoClickConfig cfg, Action onDone = null)
    {
        if (Running) return;
        if (cfg.Mode != "cursor" && cfg.Points.Count == 0 && string.IsNullOrEmpty(cfg.More?.Target)) throw new ArgumentException("no click spots set");
        string key = (cfg.More?.Key ?? "").Trim().ToLowerInvariant();
        if (key.Length > 0 && !Keys.IsKnown(key) && !Keys.IsChar(key)) throw new ArgumentException($"“{key}” isn't a key name (try e, space, enter, f5)");
        if (finder == null && cfg.More != null && cfg.More.Images().Any()) throw new ArgumentException("it uses pictures, but image recognition is off");
        cts = new CancellationTokenSource();
        clicks = 0;
        Hold = false;
        Error = "";
        var token = cts.Token;
        var plan = cfg.ToPlan();
        thread = new Thread(() => Run(plan, token, onDone)) { IsBackground = true, Priority = ThreadPriority.AboveNormal };
        thread.Start();
    }

    void Run(ClickPlan plan, CancellationToken stop, Action onDone)
    {
        try
        {
            ClickEngine.Run(plan, new ClickEngine.Env
            {
                Input = input, Finder = finder, Paused = () => Hold, IsOwn = (x, y) => IsOwn?.Invoke(x, y) == true,
                OnClick = n => Volatile.Write(ref clicks, n),
            }, stop);
        }
        catch (Exception e) { Error = e.Message; }
        finally
        {
            onDone?.Invoke();
        }
    }
}
