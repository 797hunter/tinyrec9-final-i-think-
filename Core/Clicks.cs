// Clicks.cs - one click engine for everything that clicks on a timer: the Auto Clicker page, a sequence's Click
// step, and the clicks that go alongside a recording.
//
// A plan says WHERE (spots in some order, the pointer, or wherever a picture is), HOW (button, hold, key, careful
// or fast), WHEN (pace, random variation, bursts with rests, only while a picture is / isn't on screen) and
// HOW LONG (a number of clicks, a number of seconds, until a picture shows, or until it is stopped).
namespace TinyRec.Core;

/// The finer click settings. Every one of them is optional: the defaults give a plain steady click.
public sealed class ClickMore
{
    public double Jitter { get; set; }                 // ms: each gap becomes the pace +/- up to this much
    public double Hold { get; set; }                   // ms: hold each press this long (0 = a normal quick click)
    public int Spread { get; set; }                    // px: each click lands up to this far from its spot
    public int PerSpot { get; set; } = 1;              // clicks on a spot before moving on to the next
    public string Order { get; set; } = "sequence";    // sequence / random / pingpong (there and back)
    public double StartDelay { get; set; }             // seconds before the first click
    public int Burst { get; set; }                     // after every `Burst` clicks ...
    public double Rest { get; set; }                   // ... rest this many seconds (either at 0 = no bursts)
    public bool Return { get; set; }                   // put the pointer back where it was after each click
    public string Key { get; set; } = "";              // also press this key with each click ("" = none)
    public bool KeyOnly { get; set; }                  // press the key INSTEAD of clicking
    public string Target { get; set; } = "";           // click wherever this picture is (instead of fixed spots)
    public string If { get; set; } = "";               // only click while this picture is on screen ...
    public bool IfNot { get; set; }                    // ... or only while it is NOT on screen
    public string Until { get; set; } = "";            // stop for good the moment this picture shows
    public double Conf { get; set; } = 0.9;            // how strict the picture matches are
    public string Style { get; set; } = "auto";        // auto / careful (settle first - games) / fast
    public int LimitClicks { get; set; }               // clicks alongside a recording: stop after this many (0 = no limit)
    public double LimitSecs { get; set; }              // ... or after this many seconds (0 = no limit)

    public ClickMore Copy() => (ClickMore)MemberwiseClone();

    static double Num(double v, double lo, double hi, double fallback) => double.IsFinite(v) ? Math.Clamp(v, lo, hi) : fallback;

    public ClickMore Clean()
    {
        Jitter = Num(Jitter, 0, 3600000, 0);
        Hold = Num(Hold, 0, 600000, 0);
        Spread = Math.Clamp(Spread, 0, 2000);
        PerSpot = Math.Clamp(PerSpot, 1, 100000);
        if (Order is not ("sequence" or "random" or "pingpong")) Order = "sequence";
        StartDelay = Num(StartDelay, 0, 86400, 0);
        Burst = Math.Clamp(Burst, 0, 100000000);
        Rest = Num(Rest, 0, 86400, 0);
        Key = (Key ?? "").Trim().ToLowerInvariant();
        if (Key.Length == 0) KeyOnly = false;
        Target = (Target ?? "").Trim();
        If = (If ?? "").Trim();
        Until = (Until ?? "").Trim();
        Conf = Num(Conf, 0.5, 1, 0.9);
        if (Style is not ("auto" or "careful" or "fast")) Style = "auto";
        LimitClicks = Math.Clamp(LimitClicks, 0, 100000000);
        LimitSecs = Num(LimitSecs, 0, 86400 * 7, 0);
        return this;
    }

    /// The pictures this uses (to check they exist before a run).
    public IEnumerable<string> Images() => new[] { Target, If, Until }.Where(n => !string.IsNullOrEmpty(n));

    /// True when nothing here differs from a plain click.
    public bool IsPlain =>
        Jitter == 0 && Hold == 0 && Spread == 0 && PerSpot == 1 && Order == "sequence" && StartDelay == 0 && (Burst == 0 || Rest == 0) && !Return &&
        Key.Length == 0 && Target.Length == 0 && If.Length == 0 && Until.Length == 0 && Style == "auto" && LimitClicks == 0 && LimitSecs == 0;
}

/// What the engine runs.
public sealed class ClickPlan
{
    public List<int[]> Points = new();        // [[x, y], ...]; empty = click where the pointer is (unless More.Target is set)
    public string Button = "left";            // left / right / middle / double (= left, twice)
    public bool Double;                       // press twice each time, whichever button it is
    public double IntervalMs = 500;
    public int Count;                         // 0 = no limit
    public double Seconds;                    // 0 = no limit
    public ClickMore More = new();
}

public static class ClickEngine
{
    /// One "go there and click" at a time: two clickers (or a clicker and an image step) must never interleave their
    /// move and their press.
    public static readonly object Gate = new();

    public sealed class Env
    {
        public IInput Input;
        public IImageFinder Finder;                   // null = pictures can't be looked for (they count as not on screen)
        public Func<bool> Paused;                     // true while everything should hold still
        public Func<int, int, bool> IsOwn;            // true when a point is on one of TinyRec's own windows
        public Action<bool> HeldOff;                  // told when pointer clicks are being held back (pointer on TinyRec)
        public Action<int> OnClick;                   // called after every click with the count so far
    }

    /// Runs the plan until it is done or `stop` is set. Returns how many clicks (or key presses) were made.
    /// Throws FileNotFoundException if a picture it needs isn't in the library.
    public static int Run(ClickPlan plan, Env env, CancellationToken stop)
    {
        var m = plan.More ?? new ClickMore();
        var input = env.Input;
        var rnd = new Random();
        bool Paused() => env.Paused?.Invoke() == true;

        int n = 0, cur = 0, dir = 1, onSpot = 0;
        double gap = Math.Max(1, double.IsFinite(plan.IntervalMs) ? plan.IntervalMs : 500) / 1000;
        double next = Timing.Now + Math.Max(0, m.StartDelay);
        double end = plan.Seconds > 0 ? next + plan.Seconds : double.MaxValue;
        bool dbl = plan.Double || plan.Button == "double";
        string button = plan.Button is "right" or "middle" ? plan.Button : "left";
        bool hasKey = !string.IsNullOrEmpty(m.Key);
        bool noClick = hasKey && m.KeyOnly;
        int spots = plan.Points.Count;
        if (m.Order == "random" && spots > 1) cur = rnd.Next(spots);
        (int, int)? last = null;
        bool untilSeen = false;
        double lookAt = 0;

        // pictures: one look serves every question asked within a fifth of a second
        var seen = new Dictionary<string, (double at, Match m)>();
        Match Look(string name)
        {
            if (env.Finder == null) return new Match();
            if (seen.TryGetValue(name, out var hit) && Timing.Now - hit.at < 0.2) return hit.m;
            Match found;
            try { found = env.Finder.Locate(name, m.Conf, null); }
            catch (FileNotFoundException) { throw; }
            catch (InvalidDataException e) { throw new FileNotFoundException(e.Message); }
            catch (Exception) { found = new Match(); }                 // the screen can't be read for a moment: not there
            seen[name] = (Timing.Now, found);
            return found;
        }

        try
        {
            while (!stop.IsCancellationRequested)
            {
                // wait for the next click (or the end) in short naps, so a pause is noticed at once
                double target = Math.Min(next, end);
                while (!stop.IsCancellationRequested)
                {
                    if (Paused())
                    {
                        double p0 = Timing.Now;
                        while (Paused() && !stop.IsCancellationRequested) stop.WaitHandle.WaitOne(25);
                        double held = Timing.Now - p0;                   // paused time doesn't count: everything shifts
                        next += held;
                        if (end != double.MaxValue) end += held;
                        target += held;
                        continue;
                    }
                    double rem = target - Timing.Now;
                    if (rem <= 0) break;
                    if (m.Until.Length > 0 && rem > 0.25 && Timing.Now >= lookAt)      // a long wait: keep an eye out for the "stop" picture
                    {
                        lookAt = Timing.Now + 0.25;
                        if (Look(m.Until).Found) { untilSeen = true; break; }
                        continue;
                    }
                    if (rem > 0.03) stop.WaitHandle.WaitOne(25);
                    else if (!Timing.SleepUntil(target, stop)) break;
                }
                if (untilSeen || stop.IsCancellationRequested || Timing.Now >= end) break;

                // conditions
                if (m.Until.Length > 0 && Look(m.Until).Found) break;
                if (m.If.Length > 0 && Look(m.If).Found == m.IfNot)
                {
                    next = Timing.Now + Math.Min(Math.Max(gap, 0.05), 0.25);     // not now: look again shortly
                    continue;
                }

                // where
                (int x, int y)? at = null;
                if (m.Target.Length > 0)
                {
                    var t = Look(m.Target);
                    if (!t.Found)
                    {
                        next = Timing.Now + Math.Min(Math.Max(gap, 0.05), 0.25);
                        continue;
                    }
                    at = (t.X, t.Y);
                }
                else if (spots > 0)
                {
                    var p = plan.Points[Math.Clamp(cur, 0, spots - 1)];
                    at = (p[0], p[1]);
                }
                else
                {
                    var ptr = input.Pointer();
                    if (!noClick && ptr != null && env.IsOwn?.Invoke(ptr.Value.x, ptr.Value.y) == true)
                    {
                        env.HeldOff?.Invoke(true);                       // the pointer is on TinyRec itself: wait for it to leave
                        next = Timing.Now + Math.Clamp(gap, 0.05, 0.1);
                        continue;
                    }
                    env.HeldOff?.Invoke(false);
                }
                var spot = at;
                if (at != null && m.Spread > 0)
                    at = (at.Value.x + rnd.Next(-m.Spread, m.Spread + 1), at.Value.y + rnd.Next(-m.Spread, m.Spread + 1));

                // how
                var back = m.Return && at != null && !noClick ? input.Pointer() : null;
                lock (Gate)
                {
                    bool plainPress = !noClick && m.Hold <= 0;
                    if (at != null && !noClick)
                    {
                        // moving to a different spot "like a person" (settle, nudge, press) is what games need; staying on
                        // the same spot, or the Fast style, just presses
                        bool careful = m.Style == "careful" || (m.Style == "auto" && last != spot);
                        if (careful && plainPress)
                        {
                            input.ClickAt(at.Value.x, at.Value.y, button, dbl ? 2 : 1);
                            plainPress = false;                          // done
                        }
                        else
                        {
                            input.Move(at.Value.x, at.Value.y);
                            if (careful) stop.WaitHandle.WaitOne(60);
                            if (plainPress) { input.Click(button, dbl ? 2 : 1); plainPress = false; }
                        }
                    }
                    if (plainPress) input.Click(button, dbl ? 2 : 1);     // pointer mode
                    else if (!noClick && m.Hold > 0)
                    {
                        input.Button(button, true);
                        try { Timing.SleepUntil(Timing.Now + m.Hold / 1000, stop); }
                        finally { input.Button(button, false); }
                    }
                    if (hasKey)
                    {
                        input.Key(m.Key, true);
                        try { Timing.SleepUntil(Timing.Now + Math.Max(m.Hold, 30) / 1000, stop); }
                        finally { input.Key(m.Key, false); }
                    }
                    if (back != null) input.Move(back.Value.x, back.Value.y);
                }
                last = back != null ? null : spot;                     // the pointer went back: the next visit is a fresh arrival
                n++;
                env.OnClick?.Invoke(n);
                if (plan.Count > 0 && n >= plan.Count) break;

                // next spot
                if (spots > 1 && ++onSpot >= Math.Max(1, m.PerSpot))
                {
                    onSpot = 0;
                    if (m.Order == "random") cur = rnd.Next(spots);
                    else if (m.Order == "pingpong")
                    {
                        if (cur + dir < 0 || cur + dir >= spots) dir = -dir;
                        cur += dir;
                    }
                    else cur = (cur + 1) % spots;
                }

                // when
                double g = gap + (m.Jitter > 0 ? (rnd.NextDouble() * 2 - 1) * m.Jitter / 1000 : 0);
                next += Math.Max(m.Jitter > 0 ? Math.Max(0.001, gap * 0.1) : 0.001, g);   // a big variation never means back-to-back clicks
                if (m.Burst > 0 && m.Rest > 0 && n % m.Burst == 0) next = Timing.Now + m.Rest;
                else if (next < Timing.Now - 0.5) next = Timing.Now;     // fell far behind (PC slept?) - don't machine-gun
                else if (next < Timing.Now && gap >= 0.05) next = Timing.Now;   // a slow click: don't bunch up
            }
        }
        finally { env.HeldOff?.Invoke(false); }
        return n;
    }
}
