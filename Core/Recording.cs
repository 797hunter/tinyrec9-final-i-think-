// Recording.cs - the TinyRec text format: one action per line, readable in Notepad.
//
//   # TinyRec v1
//   # name: Login demo
//   0.000 MOVE 640 380
//   0.482 LEFT_DOWN 640 380          (also RIGHT_ / MIDDLE_  DOWN / UP)
//   0.900 SCROLL 700 420 0 -1        (x y sideways vertical, in wheel notches)
//   1.200 KEY_DOWN a                 (a, 5, enter, shift, f5, vk:186 ...; u:00e9 = a typed character)
//   2.000 IMAGE_CLICK login.png conf=0.9 timeout=10 button=left onfail=skip
//   3.000 IMAGE_WAIT  loaded.png conf=0.9 timeout=30 onfail=stop
//
// Identical to the Python TinyRec versions, so old recordings open unchanged.
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace TinyRec.Core;

public sealed class FormatError : Exception
{
    public FormatError(string msg) : base(msg) { }
}

public sealed class Event
{
    public double T;
    public string Kind;
    public List<string> Args;
    public List<KeyValuePair<string, string>> Opts;

    public Event(double t, string kind, IEnumerable<string> args = null, IEnumerable<KeyValuePair<string, string>> opts = null)
    {
        T = t;
        Kind = kind;
        Args = args?.ToList() ?? new List<string>();
        Opts = opts?.ToList() ?? new List<KeyValuePair<string, string>>();
    }

    public static Event Of(double t, string kind, params object[] args) =>
        new(t, kind, args.Select(a => Fmt.Num(a)));

    public bool IsImage => Kind.StartsWith("IMAGE_");

    public string Opt(string key, string fallback = null)
    {
        foreach (var kv in Opts)
            if (kv.Key == key) return kv.Value;
        return fallback;
    }

    public string ToLine()
    {
        var sb = new StringBuilder();
        sb.Append(T.ToString("0.000", CultureInfo.InvariantCulture)).Append(' ').Append(Kind);
        foreach (var a in Args) sb.Append(' ').Append(a);
        foreach (var kv in Opts) sb.Append(' ').Append(kv.Key).Append('=').Append(kv.Value);
        return sb.ToString();
    }

    public Event Copy() => new(T, Kind, Args, Opts);

    public int IntArg(int i) => int.Parse(Args[i], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
    public double NumArg(int i) => double.Parse(Args[i], NumberStyles.Float, CultureInfo.InvariantCulture);

    public bool SameAs(Event o) =>
        o != null && Math.Abs(T - o.T) < 0.0005 && Kind == o.Kind && Args.SequenceEqual(o.Args) &&
        Opts.Select(k => k.Key + "=" + k.Value).SequenceEqual(o.Opts.Select(k => k.Key + "=" + k.Value));

    public override string ToString() => ToLine();
}

public static class Fmt
{
    public static string Num(object v) => v switch
    {
        double d when d == Math.Floor(d) && Math.Abs(d) < 1e15 => ((long)d).ToString(CultureInfo.InvariantCulture),
        double d => d.ToString("0.####", CultureInfo.InvariantCulture),
        float f => Num((double)f),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => v?.ToString() ?? ""
    };

    public static string Dur(double s)
    {
        s = Math.Max(0, s);
        if (s < 60) return s.ToString("0.0", CultureInfo.InvariantCulture) + " s";
        var m = Math.Floor(s / 60);
        var sec = s - m * 60;
        if (m < 60) return $"{(int)m}:{sec.ToString("00.0", CultureInfo.InvariantCulture)}";
        var h = Math.Floor(m / 60);
        m -= h * 60;
        return $"{(int)h}:{(int)m:00}:{(int)sec:00}";
    }
}

public static class Format
{
    public const string Header = "# TinyRec v1";
    public static readonly string[] MetaOrder = { "name", "created", "window", "origin", "screen", "note" };
    public static readonly string[] Buttons = { "left", "right", "middle" };

    public static readonly Regex KeyToken = new(@"^([a-z0-9_]{1,24}|vk:\d{1,3}|u:[0-9a-f]{4})$", RegexOptions.Compiled);
    public static readonly Regex ImageName = new(@"^[A-Za-z0-9_\-]+(\.[A-Za-z0-9_\-]+)*\.(png|jpg|jpeg|bmp)$",
                                                 RegexOptions.Compiled | RegexOptions.IgnoreCase);
    static readonly Regex MetaLine = new(@"^#\s*([a-z_]+):\s*(.*)$", RegexOptions.Compiled);

    // argument kinds: i = whole number, n = number, k = key name, m = image file name
    static readonly Dictionary<string, string> Specs = new()
    {
        ["MOVE"] = "ii", ["SCROLL"] = "iinn", ["KEY_DOWN"] = "k", ["KEY_UP"] = "k",
        ["IMAGE_CLICK"] = "m", ["IMAGE_WAIT"] = "m",
        ["LEFT_DOWN"] = "ii", ["LEFT_UP"] = "ii", ["RIGHT_DOWN"] = "ii", ["RIGHT_UP"] = "ii",
        ["MIDDLE_DOWN"] = "ii", ["MIDDLE_UP"] = "ii",
    };

    public static IEnumerable<string> Kinds => Specs.Keys;

    static bool IsInt(string s) => int.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _);
    static bool IsNum(string s) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && !double.IsNaN(d) && !double.IsInfinity(d);

    static readonly Dictionary<string, Func<string, bool>> ImageOpts = new()
    {
        ["conf"] = v => IsNum(v) && D(v) > 0 && D(v) <= 1,
        ["timeout"] = v => IsNum(v) && D(v) >= 0,
        ["button"] = v => v is "left" or "right" or "double",
        ["onfail"] = v => v is "skip" or "stop" or "wait",                 // wait = keep waiting (no time limit)
        ["key"] = v => KeyToken.IsMatch(v),                                // press this key when the picture shows
        ["or"] = v => v.Split(',') is var n && n.Length > 0 && n.All(x => ImageName.IsMatch(x)),   // also carry on if one of these shows
        ["region"] = v => v.Split(',') is var p && p.Length == 4 && p.All(IsInt),
    };

    static double D(string s) => double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

    public static void Validate(Event ev)
    {
        if (!Specs.TryGetValue(ev.Kind, out var spec)) throw new FormatError($"unknown action '{ev.Kind}'");
        if (ev.T < 0 || double.IsNaN(ev.T) || double.IsInfinity(ev.T)) throw new FormatError("time must be 0 or more");
        if (ev.Args.Count != spec.Length) throw new FormatError($"{ev.Kind} needs {spec.Length} value(s), got {ev.Args.Count}");
        for (int i = 0; i < spec.Length; i++)
        {
            var a = ev.Args[i];
            switch (spec[i])
            {
                case 'i' when !IsInt(a): throw new FormatError($"'{a}' is not a whole number");
                case 'n' when !IsNum(a): throw new FormatError($"'{a}' is not a number");
                case 'k' when !KeyToken.IsMatch(a): throw new FormatError($"'{a}' is not a valid key name");
                case 'm' when !ImageName.IsMatch(a):
                    throw new FormatError($"'{a}' is not a valid image file name (letters/digits/_/-/. and .png/.jpg/.bmp)");
            }
        }
        if (ev.IsImage)
        {
            foreach (var kv in ev.Opts)
            {
                if (!ImageOpts.TryGetValue(kv.Key, out var ok)) throw new FormatError($"unknown option '{kv.Key}'");
                if (!ok(kv.Value)) throw new FormatError($"bad value for {kv.Key}: '{kv.Value}'");
            }
        }
        else if (ev.Opts.Count > 0) throw new FormatError($"{ev.Kind} takes no key=value options");
    }

    public static Event ParseLine(string line)
    {
        var tok = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
        if (tok.Length < 2) throw new FormatError("expected: <seconds> <ACTION> ...");
        if (!IsNum(tok[0])) throw new FormatError($"'{tok[0]}' is not a time in seconds");
        var args = new List<string>();
        var opts = new List<KeyValuePair<string, string>>();
        foreach (var p in tok.Skip(2))
        {
            int eq = p.IndexOf('=');
            if (eq >= 0) opts.Add(new(p[..eq], p[(eq + 1)..]));
            else args.Add(p);
        }
        var ev = new Event(D(tok[0]), tok[1].ToUpperInvariant(), args, opts);
        Validate(ev);
        return ev;
    }

    internal static bool TryMeta(string line, out string key, out string value)
    {
        var m = MetaLine.Match(line);
        key = m.Success ? m.Groups[1].Value : null;
        value = m.Success ? m.Groups[2].Value.Trim() : null;
        return m.Success;
    }
}

public sealed class Recording
{
    public List<Event> Events = new();
    public List<KeyValuePair<string, string>> Meta = new();   // keeps file order

    public double Duration => Events.Count == 0 ? 0 : Events.Max(e => e.T);

    public string GetMeta(string key) => Meta.FirstOrDefault(k => k.Key == key).Value;

    public void SetMeta(string key, string value)
    {
        int i = Meta.FindIndex(k => k.Key == key);
        if (string.IsNullOrWhiteSpace(value))
        {
            if (i >= 0) Meta.RemoveAt(i);
            return;
        }
        value = value.Replace('\n', ' ').Replace('\r', ' ').Trim();
        if (i >= 0) Meta[i] = new(key, value);
        else Meta.Add(new(key, value));
    }

    /// Stable sort: actions at the same time keep their order.
    public void Sort()
    {
        var sorted = Events.Select((e, i) => (e, i)).OrderBy(p => p.e.T).ThenBy(p => p.i).Select(p => p.e).ToList();
        Events = sorted;
    }

    public List<string> ImageNames() =>
        Events.Where(e => e.IsImage)
              .SelectMany(e => new[] { e.Args[0] }.Concat((e.Opt("or") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)))
              .Distinct().ToList();

    public Recording Copy() => new()
    {
        Events = Events.Select(e => e.Copy()).ToList(),
        Meta = Meta.ToList(),
    };

    public string ToText()
    {
        var sb = new StringBuilder();
        sb.Append(Format.Header).Append('\n');
        var keys = Format.MetaOrder.Where(k => Meta.Any(m => m.Key == k))
                         .Concat(Meta.Select(m => m.Key).Where(k => !Format.MetaOrder.Contains(k)).OrderBy(k => k, StringComparer.Ordinal));
        foreach (var k in keys)
        {
            var v = GetMeta(k)?.Replace('\n', ' ').Trim();
            if (!string.IsNullOrEmpty(v)) sb.Append("# ").Append(k).Append(": ").Append(v).Append('\n');
        }
        sb.Append('\n');
        foreach (var e in Events) sb.Append(e.ToLine()).Append('\n');
        return sb.ToString();
    }

    public static Recording FromText(string text)
    {
        var rec = new Recording();
        var problems = new List<string>();
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (int n = 0; n < lines.Length; n++)
        {
            var line = lines[n].Trim().TrimStart('﻿');
            if (line.Length == 0) continue;
            if (line.StartsWith('#'))
            {
                if (Format.TryMeta(line, out var k, out var v)) rec.SetMetaRaw(k, v);
                continue;
            }
            try { rec.Events.Add(Format.ParseLine(line)); }
            catch (FormatError e) { problems.Add($"line {n + 1}: {e.Message}"); }
        }
        if (problems.Count > 0)
        {
            var more = problems.Count > 5 ? $" (+{problems.Count - 5} more)" : "";
            throw new FormatError(string.Join("; ", problems.Take(5)) + more);
        }
        rec.Sort();
        return rec;
    }

    void SetMetaRaw(string k, string v)
    {
        int i = Meta.FindIndex(m => m.Key == k);
        if (i >= 0) Meta[i] = new(k, v);
        else Meta.Add(new(k, v));
    }
}

/// Small helpers the editor and recorder use.
public static class Edits
{
    public static string KindClass(string kind) =>
        kind == "MOVE" ? "move" : kind == "SCROLL" ? "scroll" : kind.StartsWith("KEY_") ? "key" :
        kind.StartsWith("IMAGE_") ? "image" : "click";

    public static bool IsMouse(string kind) =>
        kind is "MOVE" or "SCROLL" || kind.StartsWith("LEFT_") || kind.StartsWith("RIGHT_") || kind.StartsWith("MIDDLE_");

    public sealed class Counts
    {
        public int Moves, Clicks, Keys, Scrolls, Images;
    }

    public static Counts Count(IEnumerable<Event> events)
    {
        var c = new Counts();
        foreach (var e in events)
        {
            var k = e.Kind;
            if (k == "MOVE") c.Moves++;
            else if (k == "SCROLL") c.Scrolls++;
            else if (k.StartsWith("KEY_")) { if (k == "KEY_DOWN") c.Keys++; }
            else if (k.StartsWith("IMAGE_")) c.Images++;
            else if (k.EndsWith("_DOWN")) c.Clicks++;
        }
        return c;
    }

    /// Drop MOVE points closer together than minGap seconds (clicks, keys, scrolls are kept).
    public static List<Event> ThinMoves(IEnumerable<Event> events, double minGap = 0.016)
    {
        var outp = new List<Event>();
        double last = -1e9;
        foreach (var e in events)
        {
            if (e.Kind == "MOVE")
            {
                if (e.T - last < minGap) continue;
                last = e.T;
            }
            outp.Add(e);
        }
        return outp;
    }

    public static List<Event> DropMoves(IEnumerable<Event> events) => events.Where(e => e.Kind != "MOVE").ToList();

    /// Remove the mouse trip to the Stop button at the end of a recording.
    public static List<Event> TrimTrailingMoves(List<Event> events)
    {
        int last = events.FindLastIndex(e => e.Kind != "MOVE");
        return last >= 0 ? events.Take(last + 1).ToList() : events;
    }
    /// Make the wait before events[index] exactly `gap` seconds by moving it and everything after it.
    /// The list must be in time order. Returns how far things moved (seconds; negative = earlier).
    public static double SetGap(List<Event> events, int index, double gap)
    {
        if (index < 0 || index >= events.Count) return 0;
        double prev = index > 0 ? events[index - 1].T : 0;
        double delta = Math.Round(prev + Math.Max(0, gap) - events[index].T, 3);
        if (delta == 0) return 0;
        for (int i = index; i < events.Count; i++) events[i].T = Math.Round(events[i].T + delta, 3);
        return delta;
    }
}
