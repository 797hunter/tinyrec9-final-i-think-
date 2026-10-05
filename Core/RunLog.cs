// RunLog.cs - a plain diary of one run: what happened, when, and how long each wait took.
// Each line can point back at the sequence step and/or the time in the recording it is about, so the app can jump there.
using System.Globalization;
using System.Text;

namespace TinyRec.Core;

public sealed class RunLog
{
    /// Step: sequence step (0-based), -1 = none. RecTime: seconds into the recording, -1 = none.
    public sealed record Entry(DateTime At, double Elapsed, string Text, int Step, double RecTime);

    readonly List<Entry> entries = new();
    readonly object gate = new();
    readonly double t0 = Timing.Now;
    public string Title = "";
    public Action<Entry> OnAdd;                       // called on the thread that added the line

    public RunLog(string title = "") => Title = title;

    public void Add(string text, int step = -1, double recTime = -1)
    {
        var e = new Entry(DateTime.Now, Timing.Now - t0, text, step, recTime);
        lock (gate) entries.Add(e);
        try { OnAdd?.Invoke(e); } catch { }           // a full disk must never stop a run
    }

    public int Count
    {
        get { lock (gate) return entries.Count; }
    }

    public List<Entry> Snapshot()
    {
        lock (gate) return entries.ToList();
    }

    /// "06:41:07   +0:43.2   step 2   waited 43.2 s for ability.png, clicked it"
    public static string Line(Entry e)
    {
        var step = e.Step >= 0 ? $"step {e.Step + 1}".PadRight(9) : "".PadRight(9);
        return $"{e.At.ToString("HH:mm:ss", CultureInfo.InvariantCulture)}   +{Fmt.Dur(e.Elapsed),-9} {step} {e.Text}";
    }

    public string ToText()
    {
        var sb = new StringBuilder();
        sb.Append("# TinyRec run log - ").Append(Title).Append('\n');
        foreach (var e in Snapshot()) sb.Append(Line(e)).Append('\n');
        return sb.ToString();
    }
}
