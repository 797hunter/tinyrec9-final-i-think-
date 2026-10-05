// Engine tests - run with:  dotnet run --project Core.Tests
// Uses a pretend mouse/keyboard, so nothing on your screen is touched.
using System.IO.Compression;
using TinyRec.Core;

int ok = 0;
void Check(bool cond, string msg)
{
    if (!cond) throw new Exception("FAILED: " + msg);
    ok++;
    Console.WriteLine("  ok   " + msg);
}

var here = AppContext.BaseDirectory;
string Fixture(string name)
{
    foreach (var d in new[] { here, Path.Combine(here, "..", "..", ".."), Directory.GetCurrentDirectory(), Path.Combine(Directory.GetCurrentDirectory(), "Core.Tests") })
        if (File.Exists(Path.Combine(d, name))) return Path.Combine(d, name);
    throw new FileNotFoundException(name);
}

var tmp = Path.Combine(Path.GetTempPath(), "tinyrec_tests_" + Guid.NewGuid().ToString("N"));
var imgs = Path.Combine(tmp, "images");
Directory.CreateDirectory(imgs);

Console.WriteLine("format");
var rec = new Recording();
rec.SetMeta("name", "demo"); rec.SetMeta("window", "Notepad");
foreach (var l in new[] { "0.000 MOVE 640 380", "0.482 LEFT_DOWN 640 380", "0.610 LEFT_UP 700 420", "0.700 RIGHT_DOWN 1 2",
                          "0.710 RIGHT_UP 1 2", "0.720 MIDDLE_DOWN -5 2", "0.730 MIDDLE_UP -5 2", "0.900 SCROLL 700 420 0 -1",
                          "1.200 KEY_DOWN a", "1.260 KEY_UP a", "1.300 KEY_DOWN vk:186", "1.310 KEY_UP vk:186",
                          "2.000 IMAGE_CLICK login.png conf=0.9 timeout=10 button=double onfail=skip",
                          "3.000 IMAGE_WAIT ok.png region=0,0,500,500" })
    rec.Events.Add(Format.ParseLine(l));
var text = rec.ToText();
var back = Recording.FromText(text);
Check(back.Events.Count == rec.Events.Count && back.Events.Zip(rec.Events).All(p => p.First.SameAs(p.Second)) &&
      back.GetMeta("name") == "demo" && back.GetMeta("window") == "Notepad", "text round-trip keeps every action and header");
Check(text.StartsWith("# TinyRec v1\n# name: demo\n# window: Notepad\n\n0.000 MOVE 640 380"), "file layout identical to the Python version");
foreach (var bad in new[] { "abc MOVE 1 2", "0.1 MOVE 1", "0.1 JUMP 1 2", "0.1 KEY_DOWN Bad Key", "0.1 IMAGE_CLICK ../x.png",
                            "0.1 IMAGE_CLICK a.png conf=5", "0.1 MOVE 1 2 conf=1", "-1 MOVE 1 2", "0.1 SCROLL 1 2 x 1" })
{
    bool threw = false;
    try { Recording.FromText(bad); } catch (FormatError) { threw = true; }
    Check(threw, $"rejects '{bad}'");
}
try { Recording.FromText("0 MOVE 1\n0 FOO\n"); Check(false, "x"); }
catch (FormatError e) { Check(e.Message.Contains("line 1") && e.Message.Contains("line 2"), "error message names the bad lines"); }
Check(Recording.FromText("﻿# TinyRec v1\r\n\r\n0 MOVE 1 2\r\n").Events.Count == 1, "Windows line endings + BOM accepted");
var unsorted = Recording.FromText("0.5 MOVE 2 2\n0.1 MOVE 1 1\n0.5 KEY_DOWN a\n");
Check(unsorted.Events.Select(e => e.Kind).SequenceEqual(new[] { "MOVE", "MOVE", "KEY_DOWN" }) && unsorted.Events[1].Args[0] == "2",
      "sorted by time, same-time actions keep their order");

Console.WriteLine("compatibility with Python TinyRec");
var py = Files.LoadText(Fixture("from_python.txt"));
Check(py.Events.Count == 12 && py.GetMeta("name") == "From Python" && py.GetMeta("origin") == "10 20", "reads a recording saved by the Python version");
Check(py.ToText() == File.ReadAllText(Fixture("from_python.txt")).Replace("\r\n", "\n"), "writes it back byte-for-byte the same");
var pycfg = Settings.Load(Fixture("python_config.json"));
Check(pycfg.Loops == 0 && pycfg.Hotkeys["panic"] == "f11" && pycfg.Hotkeys["play_toggle"] == "f9" &&
      pycfg.Autoclick.Button == "right" && pycfg.Autoclick.Points.Count == 2 && pycfg.Autoclick.Points[1][1] == 4 &&
      pycfg.Autoclick.StartDelay == 1.5 && pycfg.Autoclick.Order == "random", "reads the Python config.json (hotkeys, loops, auto clicker)");
Check(Keys.Vk("shift") == 0xA0 && Keys.Vk("alt_l") == 0xA4 && Keys.Vk("ctrl_l") == 0xA2 && Keys.Vk("semicolon") == 186 &&
      Keys.Vk("vk:186") == 186 && Keys.Vk("a") == 0x41 && Keys.Vk("5") == 0x35 && Keys.Vk("f12") == 0x7B && Keys.Vk("num3") == 0x63,
      "Python key names map to the right Windows keys");
Check(Keys.Name(0xA0) == "shift" && Keys.Name(0xA2) == "ctrl_l" && Keys.Name(0xA4) == "alt_l" && Keys.Name(0x5B) == "cmd" &&
      Keys.Name(0x41) == "a" && Keys.Name(186) == "semicolon" && Keys.Name(0xFF) == "vk:255", "Windows keys get the same names Python wrote");

Console.WriteLine("saving / bundles");
var p = Path.Combine(tmp, "r.txt");
Files.SaveRecording(rec, p);
Files.SaveRecording(rec, p);
Check(File.Exists(p + ".bak"), "previous version kept as .bak");
Check(Files.LoadText(p).Events.Zip(rec.Events).All(x => x.First.SameAs(x.Second)), "file loads back identical");
Check(!Directory.GetFiles(tmp, ".tmp_*").Any(), "no temp files left behind");
var pic = new byte[] { 137, 80, 78, 71, 1, 2, 3, 4 };
File.WriteAllBytes(Path.Combine(imgs, "login.png"), pic);
File.WriteAllBytes(Path.Combine(imgs, "ok.png"), pic.Reverse().ToArray());
var zipPath = Path.Combine(tmp, "b.zip");
Check(Files.ExportBundle(rec, imgs, zipPath).Count == 0, "bundle exported with its images");
var imgs2 = Path.Combine(tmp, "images2");
var (r2, _) = Files.ImportBundle(zipPath, imgs2);
Check(r2.Events.Count == rec.Events.Count && Directory.GetFiles(imgs2).Select(Path.GetFileName).OrderBy(x => x).SequenceEqual(new[] { "login.png", "ok.png" }),
      "bundle imports actions + images");
File.WriteAllBytes(Path.Combine(imgs2, "login.png"), new byte[] { 9, 9, 9 });
File.Delete(Path.Combine(imgs2, "ok.png"));
var (r3, notes) = Files.ImportBundle(zipPath, imgs2);
var newName = r3.Events.First(e => e.Kind == "IMAGE_CLICK").Args[0];
Check(newName != "login.png" && File.Exists(Path.Combine(imgs2, newName)) && notes.Count > 0, "name clash renamed + reference rewritten");
var evil = Path.Combine(tmp, "evil.zip");
using (var z = ZipFile.Open(evil, ZipArchiveMode.Create))
{
    foreach (var (n, c) in new[] { ("recording.txt", "0 MOVE 1 2\n"), ("images/../../evil.png", "x"), ("../evil.txt", "x"), ("images/run.exe", "x") })
        using (var w = new StreamWriter(z.CreateEntry(n).Open())) w.Write(c);
}
var before = Directory.GetFileSystemEntries(tmp).OrderBy(x => x).ToList();
Files.ImportBundle(evil, imgs2);
Check(Directory.GetFileSystemEntries(tmp).OrderBy(x => x).SequenceEqual(before) && !File.Exists(Path.Combine(imgs2, "run.exe")),
      "hostile zip cannot write outside images/");
var sum = Files.Scan(p);
Check(sum.Actions == rec.Events.Count && sum.Name == "demo" && sum.Images == 2 && Math.Abs(sum.Duration - 3) < 1e-9, "quick library scan");
Check(Files.ImagesUsed(p).SetEquals(new[] { "login.png", "ok.png" }), "finds which images a recording uses");

Console.WriteLine("timing (takes ~8 s)");
{
    var r = new Recording();
    for (int i = 0; i <= 300; i++) r.Events.Add(Event.Of(i * 0.01, "MOVE", i, i));
    r.Events.Add(Event.Of(3.05, "LEFT_DOWN", 5, 5));
    var f = new FakeInput();
    var pl = new Player(f, null, () => 1.0);
    double t0 = Timing.Now;
    var st = pl.Play(r);
    var moves = f.Log.Where(x => x.what == "move").Take(301).ToList();
    var times = moves.Select(x => x.t - t0).ToList();
    // a one-time start-up delay (.NET compiling the code on first use) shifts everything equally - that isn't
    // a timing error, so measure each action against the typical offset
    var offs = times.Select((t, i) => t - i * 0.01).OrderBy(x => x).ToList();
    var offset = offs[offs.Count / 2];
    var errs = times.Select((t, i) => Math.Abs(t - i * 0.01 - offset)).ToList();
    var med = errs.OrderBy(x => x).ElementAt(errs.Count / 2);
    var end = errs.Skip(errs.Count - 30).OrderBy(x => x).ElementAt(15);
    Check(times.Count == 301 && st.Events == 302, "all actions delivered");
    Check(med < 0.0015, $"typical timing error {med * 1000:0.00} ms");
    Check(end < 0.002, $"error at the end of 3 s still {end * 1000:0.00} ms - no drift (worst one-off {errs.Max() * 1000:0.0} ms)");
    Check(f.Log.Any(x => x.what == "button" && x.detail == "left:up"), "button still held at the end is released");
}
{
    var f = new FakeInput();
    double speed = 1;
    var pl = new Player(f, null, () => Volatile.Read(ref speed));
    var r = new Recording();
    for (int i = 0; i <= 40; i++) r.Events.Add(Event.Of(i * 0.05, "MOVE", i, 0));
    _ = Task.Delay(500).ContinueWith(_ => Volatile.Write(ref speed, 2.0));
    double t0 = Timing.Now; pl.Play(r); double dur = Timing.Now - t0;
    Check(Math.Abs(dur - 1.25) < 0.06, $"speed changed mid-run: took {dur:0.00}s (expected ~1.25)");
}
{
    var f = new FakeInput(); var pl = new Player(f, null, () => 4.0);
    var r = new Recording { Events = { Event.Of(0, "MOVE", 0, 0), Event.Of(2, "MOVE", 1, 1) } };
    double t0 = Timing.Now; pl.Play(r); double dur = Timing.Now - t0;
    Check(Math.Abs(dur - 0.5) < 0.04, $"4x speed: 2 s recording took {dur:0.00}s");
}
{
    var f = new FakeInput(); var pl = new Player(f, null, () => 1.0);
    _ = Task.Delay(300).ContinueWith(_ => pl.Stop());
    var r = new Recording { Events = { new Event(0, "KEY_DOWN", new[] { "a" }), new Event(60, "KEY_UP", new[] { "a" }) } };
    double t0 = Timing.Now; var st = pl.Play(r);
    Check(Timing.Now - t0 < 0.6 && st.Stopped, "stop is immediate");
    Check(f.Log.Any(x => x.what == "key" && x.detail == "a:up"), "key held when stopped gets released");
}
{
    var f = new FakeInput(); var pl = new Player(f, null, () => 1.0);
    var st = pl.Play(new Recording { Events = { Event.Of(0, "MOVE", 1, 1), Event.Of(0.1, "MOVE", 2, 2) } }, 3, 0.05);
    Check(st.Loops == 3 && st.Events == 6, "repeat 3 times");
    f = new FakeInput(); pl = new Player(f, null, () => 1.0);
    _ = Task.Delay(700).ContinueWith(_ => pl.Stop());
    st = pl.Play(new Recording { Events = { Event.Of(0, "MOVE", 1, 1), Event.Of(0.1, "MOVE", 2, 2) } }, 0);
    Check(st.Loops >= 4 && st.Stopped, $"loop forever until stopped ({st.Loops} loops)");
    f = new FakeInput(); pl = new Player(f, null, () => 1.0);
    pl.Play(new Recording { Events = { Event.Of(0, "MOVE", 10, 10), Event.Of(0, "LEFT_DOWN", 10, 10), new Event(0, "SCROLL", new[] { "10", "10", "0", "-2" }) } }, 1, 0, (100, 50));
    Check(f.Log[0].detail == "110,60", "follow-window offset applied");
}

Console.WriteLine("image steps");
{
    var finder = new FakeFinder();
    var f = new FakeInput();
    var pl = new Player(f, finder, () => 1.0);
    _ = Task.Delay(400).ContinueWith(_ => finder.Visible = true);
    var r = new Recording { Events = { Event.Of(0.1, "MOVE", 0, 0),
        new Event(0.2, "IMAGE_CLICK", new[] { "t.png" }, new[] { new KeyValuePair<string, string>("timeout", "3") }),
        Event.Of(0.3, "MOVE", 9, 9) } };
    double t0 = Timing.Now; var st = pl.Play(r); double dur = Timing.Now - t0;
    var click = f.Log.FirstOrDefault(x => x.what == "click");
    Check(st.ImageHits == 1 && click.detail == "left x1 @1230,185", "waited for the image, then clicked its centre");
    Check(dur is > 0.4 and < 0.75, $"next action ran 0.1 s after the click, not rushed ({dur:0.00} s)");
    finder.Visible = false;
    f = new FakeInput(); pl = new Player(f, finder, () => 1.0);
    st = pl.Play(new Recording { Events = { new Event(0, "IMAGE_CLICK", new[] { "t.png" }, new[] { new KeyValuePair<string, string>("timeout", "0.3"), new("onfail", "stop") }),
                                            Event.Of(0.1, "MOVE", 1, 1) } });
    Check(st.ImageMisses == 1 && st.Stopped && !f.Log.Any(x => x.what == "move"), "onfail=stop ends playback");
    st = new Player(f, finder, () => 1.0).Play(new Recording { Events = { new Event(0, "IMAGE_CLICK", new[] { "t.png" }, new[] { new KeyValuePair<string, string>("timeout", "0.2") }),
                                                                         Event.Of(0.1, "MOVE", 1, 1) } });
    Check(st.ImageMisses == 1 && st.Events == 2, "onfail=skip carries on");
    st = new Player(f, finder, () => 1.0).Play(new Recording { Events = { new Event(0, "IMAGE_CLICK", new[] { "missing.png" }) } });
    Check(st.Message.Contains("missing.png") && st.Stopped, "missing image stops with a clear message");
}

Console.WriteLine("auto clicker");
{
    var f = new FakeInput(); var ac = new AutoClicker(f);
    var done = new ManualResetEventSlim();
    ac.Start(new AutoClickConfig { IntervalMs = 20, Count = 50, Mode = "points", Points = new() { new[] { 1, 1 }, new[] { 2, 2 } } }, done.Set);
    done.Wait(5000);
    var cl = f.Log.Where(x => x.what == "click").ToList();
    Check(cl.Count == 50 && cl[0].detail.EndsWith("@1,1") && cl[1].detail.EndsWith("@2,2"), "50 clicks alternating between two spots");
    var span = (cl[^1].t - cl[0].t) * 1000;
    Check(Math.Abs(span - 49 * 20) < 8, $"interval holds without drift ({span:0.0} ms for 49 gaps of 20)");
    f = new FakeInput(); ac = new AutoClicker(f);
    ac.Start(new AutoClickConfig { IntervalMs = 10 }); Thread.Sleep(200); ac.Halt(); Thread.Sleep(100);
    Check(!ac.Running && ac.Clicks > 5, "stops on request");
    f = new FakeInput(); ac = new AutoClicker(f); done.Reset();
    ac.Start(new AutoClickConfig { IntervalMs = 10, Count = 3, HoldMs = 30, Button = "right" }, done.Set); done.Wait(3000);
    Check(f.Log.Count(x => x.detail == "right:down") == 3 && f.Log.Count(x => x.detail == "right:up") == 3, "hold mode presses and releases");
    bool threw = false;
    try { ac.Start(new AutoClickConfig { Mode = "points" }); } catch (ArgumentException) { threw = true; }
    Check(threw, "refuses spot mode with no spots");
}

Console.WriteLine("editing helpers");
{
    var ev = Enumerable.Range(0, 100).Select(i => Event.Of(i * 0.001, "MOVE", i, i)).Append(Event.Of(0.2, "LEFT_DOWN", 1, 1)).ToList();
    var th = Edits.ThinMoves(ev);
    Check(th.Count < 15 && th[^1].Kind == "LEFT_DOWN", $"100 moves thinned to {th.Count - 1}, click kept");
    var evs = new List<Event> { Event.Of(0, "MOVE", 1, 1), Event.Of(0.1, "LEFT_DOWN", 1, 1), Event.Of(0.2, "LEFT_UP", 5, 5), Event.Of(0.3, "MOVE", 9, 9) };
    Check(Edits.TrimTrailingMoves(evs).Count == 3, "trailing moves trimmed");
    Check(Edits.DropMoves(evs).Count == 2, "moves dropped, clicks kept");
    var c = Edits.Count(rec.Events);
    Check(c.Clicks == 3 && c.Keys == 2 && c.Images == 2 && c.Scrolls == 1 && c.Moves == 1, "action counts");
    Check(Fmt.Dur(5.25) == "5.3 s" || Fmt.Dur(5.25) == "5.2 s", "duration text");
    Check(Fmt.Dur(65.5) == "1:05.5" && Fmt.Dur(3725) == "1:02:05", "long durations");
    Check(Files.Slug("My demo #1!") == "My_demo_1" && Files.Slug("  ") == "recording", "safe file names");
}

Console.WriteLine("hotkeys & settings");
{
    var t = new HotkeyTable();
    t.Set(HotkeyTable.Defaults);
    Check(t.Find(Keys.Make(new[] { "ctrl", "alt" }, "up")) == "speed_up" && t.Find(Keys.Make(Array.Empty<string>(), "f9")) == "play_toggle", "combos found");
    Check(t.Find(Keys.Make(Array.Empty<string>(), "up")) == null, "plain arrow keys aren't hotkeys");
    bool threw = false;
    try { t.Set(new Dictionary<string, string>(HotkeyTable.Defaults) { ["panic"] = "f9" }); } catch (ArgumentException e) { threw = e.Message.Contains("twice"); }
    Check(threw && t.Find(Keys.Make(Array.Empty<string>(), "f12")) == "panic", "duplicate refused, old table kept");
    foreach (var bad in new[] { "ctrl+alt", "a+b", "", "ctrl+nope" })
    {
        threw = false;
        try { Keys.Parse(bad); } catch (ArgumentException) { threw = true; }
        Check(threw, $"bad combo '{bad}' refused");
    }
    Check(Keys.Parse("Control+Alt+Up").ToString() == "ctrl+alt+up" && Keys.Pretty("ctrl+alt+up") == "Ctrl+Alt+Up" && Keys.Pretty("f12") == "F12", "combo text");
    Check(Keys.ModOf("ctrl_l") == "ctrl" && Keys.ModOf("alt_gr") == "alt" && Keys.ModOf("a") == null, "modifier detection");
    Check(Keys.TokensOf("ctrl+alt+r").IsSupersetOf(new[] { "r", "ctrl_l", "ctrl_r", "alt_l" }), "tokens of a combo");
    var cp = Path.Combine(tmp, "c.json");
    var s = Settings.Load(cp); s.Hotkeys["panic"] = "f11"; s.Autoclick.Points.Add(new[] { 5, 6 }); s.Save(cp);
    var s2 = Settings.Load(cp);
    Check(s2.Hotkeys["panic"] == "f11" && s2.Hotkeys["play_toggle"] == "f9" && s2.Autoclick.Points[0][1] == 6, "settings round-trip, defaults filled in");
    Check(File.ReadAllText(cp).Contains("\"topmost_mini\"") && File.ReadAllText(cp).Contains("\"interval_ms\""), "settings file uses the same names as Python");
    File.WriteAllText(cp, "{ not json");
    Check(Settings.Load(cp).Countdown == 3, "broken settings file falls back to defaults");
}

// ---- typed characters (VK_PACKET) survive a save/load and play back as the same token
{
    var r = Recording.FromText("# TinyRec v1\n0.000 KEY_DOWN u:00e9\n0.010 KEY_UP u:00e9\n");
    Check(r.Events.Count == 2 && r.Events[0].Args[0] == "u:00e9" && Keys.IsChar("u:00e9") && Keys.CharCode("u:00e9") == 0xE9
          && Keys.CharToken(0xE9) == "u:00e9" && !Keys.IsKnown("u:00e9"), "typed-character keys (u:00e9) load and round-trip");
    bool bad = false;
    try { Recording.FromText("# TinyRec v1\n0.000 KEY_DOWN u:zz\n"); } catch (FormatError) { bad = true; }
    Check(bad, "malformed typed-character key is rejected");
}

Console.WriteLine("image waits with no time limit");
{
    var finder = new FakeFinder();
    var f = new FakeInput();
    _ = Task.Delay(700).ContinueWith(_ => finder.Visible = true);
    double t0 = Timing.Now;
    var st = new Player(f, finder, () => 1.0).Play(new Recording { Events = {
        new Event(0, "IMAGE_CLICK", new[] { "t.png" }, new[] { new KeyValuePair<string, string>("timeout", "0") }), Event.Of(0.05, "MOVE", 7, 7) } });
    double dur = Timing.Now - t0;
    Check(st.ImageHits == 1 && st.Events == 2 && dur is > 0.7 and < 1.1, $"timeout=0 waits as long as it takes ({dur:0.00} s)");
    finder.Visible = false;
    var pl = new Player(f, finder, () => 1.0);
    _ = Task.Delay(300).ContinueWith(_ => pl.Stop());
    t0 = Timing.Now;
    st = pl.Play(new Recording { Events = { new Event(0, "IMAGE_WAIT", new[] { "t.png" }, new[] { new KeyValuePair<string, string>("timeout", "0") }) } });
    Check(st.Stopped && Timing.Now - t0 < 0.6 && pl.WaitingFor == null, "an endless wait still stops at once when asked");
}

Console.WriteLine("sequences");
{
    Recording One(int x) => new() { Events = { Event.Of(0, "MOVE", x, x) } };
    var lib = new Dictionary<string, Recording> { ["a.txt"] = One(1), ["b.txt"] = One(2), ["c.txt"] = One(3) };
    Recording Load(string n) => lib.TryGetValue(n, out var r) ? r : throw new FileNotFoundException("no such recording");
    string Trace(FakeInput fi) { lock (fi.Log) return string.Join(" ", fi.Log.Select(x => x.what == "move" ? "m" + x.detail.Split(',')[0] : x.what == "click" ? "C" : "?")); }

    var seq = new Sequence { Name = "boss rush", Steps = {
        new SeqStep { Kind = "play", Recording = "a.txt" },
        new SeqStep { Kind = "image", Image = "t.png", After = 0 },
        new SeqStep { Kind = "play", Recording = "b.txt" },
        new SeqStep { Kind = "repeat", Back = 2, Count = 3 },
        new SeqStep { Kind = "play", Recording = "c.txt", Times = 2 } } };
    var reread = Sequence.FromJson(seq.ToJson());
    Check(reread.Name == "boss rush" && reread.Steps.Count == 5 && reread.Steps[3].Back == 2 && reread.Steps[3].Count == 3 && reread.Steps[4].Times == 2
          && seq.ToJson().Contains("\"recording\": \"a.txt\""), "sequence saves and loads (readable JSON)");
    var messy = Sequence.FromJson("{ \"name\": \" \", \"steps\": [ { \"kind\": \"nonsense\" }, null, { \"kind\": \"image\", \"image\": \"t.png\", \"conf\": 7, \"button\": \"x\", \"timeout\": -4 } ] }");
    Check(messy.Name == "Untitled" && messy.Steps.Count == 1 && messy.Steps[0].Conf == 1 && messy.Steps[0].Button == "left" && messy.Steps[0].Timeout == 0,
          "a hand-edited sequence file is cleaned up, not trusted");

    var finder = new FakeFinder { Visible = true };
    var f = new FakeInput();
    var run = new SequenceRunner(new Player(f, finder, () => 1.0), Load);
    var st = run.Run(seq);
    Check(Trace(f) == "m1 m1230 C m2 m1230 C m2 m1230 C m2 m3 m3", $"steps run in order, the repeated group 3 times ({Trace(f)})");
    Check(st.Loops == 1 && st.Plays == 6 && st.ImageHits == 3 && !st.Stopped && st.Message == "" && run.StepIndex == -1, "stats add up and it ends cleanly");

    f = new FakeInput(); run = new SequenceRunner(new Player(f, finder, () => 1.0), Load);
    st = run.Run(new Sequence { Steps = { new SeqStep { Kind = "play", Recording = "a.txt" }, new SeqStep { Kind = "play", Recording = "b.txt" },
                                         new SeqStep { Kind = "repeat", Back = 1, Count = 2 }, new SeqStep { Kind = "repeat", Back = 3, Count = 2 } } }, 2);
    Check(Trace(f) == "m1 m2 m2 m1 m2 m2 m1 m2 m2 m1 m2 m2" && st.Loops == 2, "a repeat inside a repeat, looped twice, starts fresh each time");

    // the whole point: the next recording must not start until the picture has been clicked
    finder.Visible = false;
    f = new FakeInput(); run = new SequenceRunner(new Player(f, finder, () => 1.0), Load);
    _ = Task.Delay(600).ContinueWith(_ => finder.Visible = true);
    double t0 = Timing.Now;
    st = run.Run(new Sequence { Steps = { new SeqStep { Kind = "image", Image = "t.png", After = 0.2 }, new SeqStep { Kind = "play", Recording = "b.txt" } } });
    double clickT, playT;
    lock (f.Log) { clickT = f.Log.First(x => x.what == "click").t - t0; playT = f.Log.Last(x => x.what == "move").t - t0; }
    Check(clickT > 0.6 && playT - clickT is > 0.19 and < 0.4 && st.ImageHits == 1,
          $"recording waits for the click, however long the picture takes (click at {clickT:0.00} s, recording at {playT:0.00} s)");

    finder.Visible = false;
    f = new FakeInput(); run = new SequenceRunner(new Player(f, finder, () => 1.0), Load);
    var runner2 = run;
    _ = Task.Delay(300).ContinueWith(_ => runner2.Stop());
    t0 = Timing.Now;
    st = run.Run(new Sequence { Steps = { new SeqStep { Kind = "image", Image = "t.png" }, new SeqStep { Kind = "play", Recording = "b.txt" } } }, 0);
    Check(st.Stopped && Timing.Now - t0 < 0.7 && f.Log.Count == 0 && st.Message == "", "Stop ends an endless wait and nothing after it runs");

    st = run.Run(new Sequence { Steps = { new SeqStep { Kind = "image", Image = "t.png", Timeout = 0.2 }, new SeqStep { Kind = "play", Recording = "b.txt" } } });
    Check(st.Stopped && st.Message.Contains("t.png") && st.ImageMisses == 1 && f.Log.Count == 0, "time limit + 'stop' ends the sequence with a clear message");
    st = run.Run(new Sequence { Steps = { new SeqStep { Kind = "image", Image = "t.png", Timeout = 0.2, OnFail = "skip" }, new SeqStep { Kind = "play", Recording = "b.txt" } } });
    Check(!st.Stopped && st.ImageMisses == 1 && Trace(f) == "m2", "time limit + 'skip' carries on with the next step");

    st = run.Run(new Sequence { Steps = { new SeqStep { Kind = "play", Recording = "a.txt" }, new SeqStep { Kind = "play", Recording = "gone.txt" } } });
    Check(st.Message.Contains("gone.txt") && Trace(f) == "m2", "a missing recording is caught before anything is played");

    f = new FakeInput(); run = new SequenceRunner(new Player(f, finder, () => 1.0), Load, _ => (100, 0));
    run.Run(new Sequence { Steps = { new SeqStep { Kind = "wait", Seconds = 0.05 }, new SeqStep { Kind = "play", Recording = "a.txt" } } });
    Check(Trace(f) == "m101", "follow-window offset is applied to recordings in a sequence");

    var probs = new Sequence { Steps = { new SeqStep { Kind = "repeat" }, new SeqStep { Kind = "image" }, new SeqStep { Kind = "play", Recording = "x.txt" },
                                        new SeqStep { Kind = "image", Image = "nope.png" } } }.Problems(n => n == "a.txt", n => n == "t.png");
    Check(probs.Count == 4 && probs[0].StartsWith("Step 1") && probs[2].Contains("x.txt") && new Sequence().Problems(_ => true, _ => true).Count == 1
          && seq.Problems(n => lib.ContainsKey(n), n => n == "t.png").Count == 0, "problems are listed before a run (and a good sequence has none)");
}

Console.WriteLine("pause, key presses, 'or' pictures, run log");
{
    // pause: frozen where it is, held things let go, then picked up again and nothing rushes afterwards
    var f = new FakeInput();
    var pl = new Player(f, new FakeFinder(), () => 1.0);
    var log = new RunLog("test");
    pl.Log = log;
    var r = new Recording { Events = { Event.Of(0, "LEFT_DOWN", 10, 10), new Event(0.05, "KEY_DOWN", new[] { "w" }), Event.Of(0.3, "MOVE", 50, 50),
                                      new Event(0.4, "KEY_UP", new[] { "w" }), Event.Of(0.4, "LEFT_UP", 50, 50) } };
    _ = Task.Delay(120).ContinueWith(_ => pl.Pause());
    _ = Task.Delay(620).ContinueWith(_ => pl.Resume());
    double t0 = Timing.Now; var st = pl.Play(r); double dur = Timing.Now - t0;
    string seqv; lock (f.Log) seqv = string.Join(" ", f.Log.Select(x => x.what[0] + ":" + x.detail));
    Check(dur is > 0.85 and < 1.05 && st.Events == 5, $"a 0.5 s pause makes a 0.4 s recording take 0.9 s ({dur:0.00} s)");
    Check(seqv == "m:10,10 b:left:down k:w:down b:left:up k:w:up m:10,10 k:w:down b:left:down m:50,50 k:w:up m:50,50 b:left:up",
          $"held key and button are released for the pause and pressed again after ({seqv})");
    Check(st.MaxLateMs < 15 && log.Snapshot().Select(e => e.Text.Split(' ')[0]).SequenceEqual(new[] { "paused", "resumed", "loop" }),
          $"timing stays on the beat after resuming and the log says so (worst {st.MaxLateMs:0.0} ms)");

    // key press when the picture shows
    var finder = new FakeFinder { Visible = true };
    f = new FakeInput(); pl = new Player(f, finder, () => 1.0);
    st = pl.Play(Recording.FromText("# TinyRec v1\n0.000 IMAGE_WAIT t.png key=e\n0.050 IMAGE_CLICK t.png key=space button=right\n"));
    lock (f.Log) seqv = string.Join(" ", f.Log.Where(x => x.what != "move").Select(x => x.what[0] + ":" + x.detail));
    Check(st.ImageHits == 2 && seqv == "k:e:down k:e:up c:right x1 @1230,185 k:space:down k:space:up", $"image steps can press a key, or click then press ({seqv})");
    bool bad = false;
    try { Recording.FromText("# TinyRec v1\n0.000 IMAGE_WAIT t.png key=NOT-A-KEY\n"); } catch (FormatError) { bad = true; }
    Check(bad, "a bad key name on an image step is rejected");

    // onfail=wait keeps waiting past the time limit
    finder.Visible = false;
    f = new FakeInput(); pl = new Player(f, finder, () => 1.0);
    _ = Task.Delay(500).ContinueWith(_ => finder.Visible = true);
    st = pl.Play(Recording.FromText("# TinyRec v1\n0.000 IMAGE_CLICK t.png timeout=0.1 onfail=wait\n"));
    Check(st.ImageHits == 1 && st.ImageMisses == 0, "onfail=wait keeps waiting past the time limit, then clicks");

    // "or" pictures: carry on without clicking when a different picture shows instead
    var two = new TwoFinder();
    f = new FakeInput(); pl = new Player(f, two, () => 1.0);
    log = new RunLog(); pl.Log = log;
    st = pl.Play(Recording.FromText("# TinyRec v1\n0.000 IMAGE_CLICK a.png timeout=0 or=b.png\n0.050 MOVE 3 3\n"));
    Check(st.Events == 2 && !f.Log.Any(x => x.what == "click") && log.Snapshot()[0].Text.Contains("b.png showed instead of a.png") && log.Snapshot()[0].RecTime == 0,
          "an 'or' picture ends the wait without a click, and the log points at the action");
    Check(Recording.FromText("# TinyRec v1\n0.000 IMAGE_CLICK a.png or=b.png,c.png key=e\n").ImageNames().SequenceEqual(new[] { "a.png", "b.png", "c.png" }),
          "'or' pictures count as images the recording uses");

    // sequences: start part-way, key + or on a step, log lines carry the step
    Recording One(int x) => new() { Events = { Event.Of(0, "MOVE", x, x) } };
    var lib = new Dictionary<string, Recording> { ["a.txt"] = One(1), ["b.txt"] = One(2), ["c.txt"] = One(3) };
    f = new FakeInput(); finder.Visible = true;
    var run = new SequenceRunner(new Player(f, finder, () => 1.0), n => lib[n]);
    log = new RunLog("seq");
    var seq = new Sequence { Steps = { new SeqStep { Kind = "play", Recording = "a.txt" }, new SeqStep { Kind = "play", Recording = "b.txt" },
                                       new SeqStep { Kind = "image", Image = "t.png", Click = false, Key = "e", After = 0 }, new SeqStep { Kind = "play", Recording = "c.txt" } } };
    var ss = run.Run(seq, 2, 0, 2, log);
    lock (f.Log) seqv = string.Join(" ", f.Log.Select(x => x.what == "move" ? "m" + x.detail.Split(',')[0] : x.what[0] + ":" + x.detail));
    Check(seqv == "k:e:down k:e:up m3 m1 m2 k:e:down k:e:up m3" && ss.Loops == 2, $"'start at step 3' skips the earlier steps on the first run only ({seqv})");
    var lines = log.Snapshot();
    Check(lines[0].Text == "starting at step 3" && lines.Any(e => e.Step == 2 && e.Text.Contains("pressed e")) && lines.Any(e => e.Step == 3 && e.Text.StartsWith("played c.txt"))
          && lines.Count(e => e.Text.StartsWith("full run")) == 2 && RunLog.Line(lines[1]).Contains("step 3"), "the run log has a line per step, tagged with its step");
    Check(Sequence.FromJson(seq.ToJson()).Steps[2].Key == "e" && new Sequence { Steps = { new SeqStep { Kind = "image", Image = "t.png", Key = "nope!", Or = { "x.png" } } } }
              .Problems(_ => true, n => n == "t.png").Count == 2, "step key and 'or' pictures are saved, and checked before a run");

    // pause also freezes a sequence's own waits
    f = new FakeInput(); var p2 = new Player(f, finder, () => 1.0); run = new SequenceRunner(p2, n => lib[n]);
    _ = Task.Delay(100).ContinueWith(_ => p2.Pause());
    _ = Task.Delay(500).ContinueWith(_ => p2.Resume());
    t0 = Timing.Now;
    run.Run(new Sequence { Steps = { new SeqStep { Kind = "wait", Seconds = 0.3 }, new SeqStep { Kind = "play", Recording = "a.txt" } } });
    dur = Timing.Now - t0;
    Check(dur is > 0.65 and < 0.9, $"pausing during a sequence's own wait stops its clock too ({dur:0.00} s)");

    // pause while a recording is waiting for a picture: the held key is let go, nothing is clicked until resumed
    {
        var fi = new FakeInput(); var ff = new FakeFinder(); var p3 = new Player(fi, ff, () => 1.0);
        var rq = new Recording { Events = { new Event(0, "KEY_DOWN", new[] { "w" }), Format.ParseLine("0.05 IMAGE_CLICK t.png timeout=0"), new Event(0.1, "KEY_UP", new[] { "w" }) } };
        _ = Task.Delay(150).ContinueWith(_ => p3.Pause());
        _ = Task.Delay(250).ContinueWith(_ => ff.Visible = true);          // shows while paused - must not be clicked yet
        _ = Task.Delay(600).ContinueWith(_ => p3.Resume());
        double q0 = Timing.Now; var s4 = p3.Play(rq); double qd = Timing.Now - q0;
        string got; lock (fi.Log) got = string.Join(" ", fi.Log.Select(x => x.what[0] + ":" + x.detail));
        Check(qd > 0.6 && s4.ImageHits == 1 && got.StartsWith("k:w:down k:w:up k:w:down ") && got.EndsWith("k:w:up") && got.Contains("c:left x1"),
              $"pausing during a picture wait lets go of held keys and clicks only after resuming ({qd:0.00} s: {got})");

        // one bad look at the screen doesn't end a long wait
        var flaky = new FlakyFinder();
        var p4 = new Player(new FakeInput(), flaky, () => 1.0);
        var res = p4.WaitImage(new ImageWait { Name = "t.png", Timeout = 0 }, default);
        Check(res.Found && res.Error == null && flaky.Calls == 3, "a screen grab that fails a couple of times is retried, not treated as the end of the run");
    }

    // set gap
    var ev = new List<Event> { Event.Of(0, "MOVE", 1, 1), Event.Of(1.5, "MOVE", 2, 2), Event.Of(2.0, "MOVE", 3, 3) };
    double d = Edits.SetGap(ev, 1, 6);
    Check(d == 4.5 && ev[1].T == 6 && ev[2].T == 6.5 && Edits.SetGap(ev, 0, 2) == 2 && ev[2].T == 8.5, "set gap moves the action and everything after it");

    // settings: new hotkey doesn't collide with a key the user already uses
    var cp2 = Path.Combine(tmp, "cfg2.json");
    File.WriteAllText(cp2, "{ \"hotkeys\": { \"panic\": \"f11\" }, \"favorites\": [\"a.txt\", \"A.TXT\", \"\"] }");
    var s3 = Settings.Load(cp2);
    var tbl = new HotkeyTable(); tbl.Set(s3.Hotkeys);
    Check(s3.Hotkeys["panic"] == "f11" && s3.Hotkeys["pause_toggle"] == "ctrl+alt+p" && s3.Favorites.Count == 1 && new Settings().Hotkeys["pause_toggle"] == "f11",
          "the new Pause hotkey moves out of the way if its key is already taken");
}

Console.WriteLine("sequences: spacing, switches, watching while playing");
{
    Recording Long() => new() { Events = { Event.Of(0, "MOVE", 1, 1), Event.Of(0.4, "MOVE", 2, 2), Event.Of(0.8, "MOVE", 3, 3) } };
    Recording Short(int x) => new() { Events = { Event.Of(0, "MOVE", x, x) } };
    var lib = new Dictionary<string, Recording> { ["long.txt"] = Long(), ["a.txt"] = Short(7), ["b.txt"] = Short(9) };
    string Trace(FakeInput fi) { lock (fi.Log) return string.Join(" ", fi.Log.Select(x => x.what == "move" ? "m" + x.detail.Split(',')[0] : x.what == "click" ? "C" : x.what == "key" ? "k" : "?")); }
    (SeqStats st, double dur, FakeInput fi, RunLog log) Go(Sequence q, FakeFinder ff, int loops = 1)
    {
        var fi = new FakeInput();
        var run = new SequenceRunner(new Player(fi, ff, () => 1.0), n => lib[n]);
        var log = new RunLog("t");
        double t0 = Timing.Now;
        var st = run.Run(q, loops, 0, 0, log);
        return (st, Timing.Now - t0, fi, log);
    }

    // waits before and after a step, plus the sequence's own gap between steps
    var zq1 = new Sequence { Gap = 0.1, Steps = { new SeqStep { Kind = "play", Recording = "a.txt", Before = 0.2, Pause = 0.15 }, new SeqStep { Kind = "play", Recording = "b.txt" } } };
    var zr1 = Go(zq1, new FakeFinder());
    Check(zr1.dur is > 0.42 and < 0.75 && Trace(zr1.fi) == "m7 m9" && zr1.st.Steps == 2, $"wait before + wait after + the sequence gap all add up ({zr1.dur:0.00} s)");
    var rt = Sequence.FromJson(zq1.ToJson());
    Check(rt.Gap == 0.1 && rt.Steps[0].Before == 0.2 && rt.Steps[0].Pause == 0.15 && rt.Loops == -1 && rt.Steps[0].Until == "" && rt.Steps[0].UntilLoop,
          "spacing and the new step settings survive a save");

    // a switched-off step is skipped, and can't block the run
    var zq2 = new Sequence { Steps = { new SeqStep { Kind = "play", Recording = "a.txt" }, new SeqStep { Kind = "play", Recording = "missing.txt", Off = true },
                                      new SeqStep { Kind = "image", Image = "nope.png", Off = true }, new SeqStep { Kind = "play", Recording = "b.txt" } } };
    var zr2 = Go(zq2, new FakeFinder());
    Check(Trace(zr2.fi) == "m7 m9" && zq2.Problems(n => lib.ContainsKey(n), n => n == "t.png").Count == 0 && string.IsNullOrEmpty(zr2.st.Message),
          "switched-off steps are skipped and never stop a run");

    // watch for a picture while a recording plays: cut it short the moment it shows, click it, then move on
    var ff = new FakeFinder();
    _ = Task.Delay(300).ContinueWith(_ => ff.Visible = true);
    var zq3 = new Sequence { Steps = { new SeqStep { Kind = "play", Recording = "long.txt", Until = "t.png", UntilMode = "stop", UntilClick = true }, new SeqStep { Kind = "play", Recording = "b.txt" } } };
    var zr3 = Go(zq3, ff);
    Check(Trace(zr3.fi) == "m1 m1230 C m9" && zr3.dur is > 0.28 and < 0.75 && zr3.st.ImageHits == 1 && zr3.log.Snapshot().Any(e => e.Step == 0 && e.Text.StartsWith("saw t.png") && e.Text.Contains("cut the recording short")),
          $"a watched picture cuts the recording short, is clicked, and the next step starts ({zr3.dur:0.00} s: {Trace(zr3.fi)})");

    // ...or let the pass that is playing finish first
    ff = new FakeFinder();
    _ = Task.Delay(200).ContinueWith(_ => ff.Visible = true);
    zq3.Steps[0].UntilMode = "finish";
    zq3.Steps[0].UntilClick = false;
    var zr4 = Go(zq3, ff);
    Check(Trace(zr4.fi) == "m1 m2 m3 m9" && zr4.dur is > 0.78 and < 1.3, $"'finish first' plays the pass to its end before moving on ({zr4.dur:0.00} s: {Trace(zr4.fi)})");

    // not seen yet when the recording ends: play it again until it shows
    ff = new FakeFinder();
    _ = Task.Delay(500).ContinueWith(_ => ff.Visible = true);
    var zq5 = new Sequence { Steps = { new SeqStep { Kind = "play", Recording = "a.txt", Until = "t.png", UntilLoop = true, Pause = 0 }, new SeqStep { Kind = "play", Recording = "b.txt" } } };
    var zr5 = Go(zq5, ff);
    var tr5 = Trace(zr5.fi);
    Check(tr5.StartsWith("m7 m7 m7") && tr5.EndsWith("m9") && zr5.dur is > 0.45 and < 1.0 && zr5.st.Plays > 3, $"with 'play it again', the recording repeats until the picture shows ({zr5.st.Plays} plays in {zr5.dur:0.00} s)");

    // ...or play it once and then simply wait
    ff = new FakeFinder();
    _ = Task.Delay(400).ContinueWith(_ => ff.Visible = true);
    zq5.Steps[0].UntilLoop = false;
    var zr6 = Go(zq5, ff);
    Check(Trace(zr6.fi) == "m7 m9" && zr6.dur is > 0.38 and < 0.9, $"with 'just wait', it plays once and holds until the picture shows ({zr6.dur:0.00} s)");

    // a time limit on the watch: skip or stop
    zq5.Steps[0].UntilTimeout = 0.3;
    zq5.Steps[0].OnFail = "skip";
    var zr7 = Go(zq5, new FakeFinder());
    zq5.Steps[0].OnFail = "stop";
    var zr8 = Go(zq5, new FakeFinder());
    Check(Trace(zr7.fi) == "m7 m9" && zr7.st.ImageMisses == 1 && Trace(zr8.fi) == "m7" && zr8.st.Message.Contains("didn't show up"), "a watch with a time limit skips or stops as chosen");

    // stopping a run while it watches ends it at once, and a missing picture is reported before anything moves
    var fi9 = new FakeInput(); var run9 = new SequenceRunner(new Player(fi9, new FakeFinder(), () => 1.0), n => lib[n]);
    zq5.Steps[0].UntilTimeout = 0;
    _ = Task.Delay(250).ContinueWith(_ => run9.Stop());
    double t9 = Timing.Now; var st9 = run9.Run(zq5); double d9 = Timing.Now - t9;
    var bad = new Sequence { Steps = { new SeqStep { Kind = "play", Recording = "a.txt", Until = "gone.png", UntilKey = "zz!" } } };
    Check(st9.Stopped && d9 < 0.6 && Trace(fi9) == "m7" && bad.Problems(n => lib.ContainsKey(n), n => n == "t.png").Count == 2, $"a watch stops at once when asked ({d9:0.00} s), and problems are listed up front");

    // a tiny recording set to replay isn't fired thousands of times a second
    ff = new FakeFinder();
    _ = Task.Delay(400).ContinueWith(_ => ff.Visible = true);
    var zq11 = new Sequence { Steps = { new SeqStep { Kind = "play", Recording = "a.txt", Until = "t.png", UntilLoop = true } } };
    var zr11 = Go(zq11, ff);
    Check(zr11.st.Plays is > 3 and < 20, $"a very short recording replays at a sane pace while it waits ({zr11.st.Plays} plays in {zr11.dur:0.00} s)");

    // the wait after the last step still counts when the whole list runs again
    var zq12 = new Sequence { Steps = { new SeqStep { Kind = "play", Recording = "a.txt", Pause = 0.25 } } };
    var zr12 = Go(zq12, new FakeFinder(), 2);
    Check(zr12.dur is > 0.24 and < 0.45 && Trace(zr12.fi) == "m7 m7", $"the last step's wait is kept between full runs, and dropped at the very end ({zr12.dur:0.00} s)");

    // per-step speed, and a random extra on a pause
    var zq10 = new Sequence { Steps = { new SeqStep { Kind = "play", Recording = "long.txt", Speed = 4 }, new SeqStep { Kind = "wait", Seconds = 0.1, Random = 0.1 } } };
    var zr10 = Go(zq10, new FakeFinder());
    Check(zr10.dur is > 0.28 and < 0.6 && Trace(zr10.fi) == "m1 m2 m3", $"a step's own speed applies only to that step ({zr10.dur:0.00} s for 0.8 s at 4x + a 0.1-0.2 s pause)");
}

Console.WriteLine("design settings");
{
    var d = new Settings();
    Check(d.Appearance == "neon" && d.Accent == "violet" && d.Animations && d.BorderOn && d.BorderColors.Count == 3 && d.BorderColors.All(Settings.IsHex),
          "a fresh install starts in the Neon theme with a three-colour border");
    var cp3 = Path.Combine(tmp, "cfg3.json");
    File.WriteAllText(cp3, "{ \"appearance\": \"plaid\", \"accent\": \"#12ABef\", \"border_speed\": \"warp\", \"border_when\": \"running\", " +
                           "\"border_colors\": [\"#FF00aa\", \"red\", \"#12345\", null, \"#000000\", \"#111111\", \"#222222\", \"#333333\"] }");
    var s5 = Settings.Load(cp3);
    Check(s5.Appearance == "neon" && s5.Accent == "#12ABef" && s5.BorderSpeed == "medium" && s5.BorderWhen == "running"
          && string.Join(",", s5.BorderColors) == "#ff00aa,#000000,#111111,#222222", $"odd design settings are cleaned up on load ({string.Join(",", s5.BorderColors)})");
    s5.Save(cp3);
    var s6 = Settings.Load(cp3);
    Check(s6.BorderColors.Count == 4 && s6.Accent == "#12ABef" && pycfg.Appearance == "dark",
          "design settings survive a save, and an old config keeps its own theme");
}

{
    var cp4 = Path.Combine(tmp, "cfg4.json");
    File.WriteAllText(cp4, "{ \"appearance\": \"custom\", \"custom\": { \"colors\": { \"Bg\": \"#0A0B0C\", \"Card\": \"nope\", \"Wat\": \"#111111\" }, " +
                           "\"glow\": 400, \"glow_size\": -3, \"accent2\": \"blue\", \"sweep\": false }, " +
                           "\"looks\": { \" Midnight \": { \"colors\": { \"Text\": \"#ffffff\" }, \"accent\": \"#ff00aa\" }, \"\": {}, \"gone\": null } }");
    var s7 = Settings.Load(cp4);
    Check(s7.Appearance == "custom" && s7.Custom.Colors.Count == 1 && s7.Custom.Colors["Bg"] == "#0a0b0c" && s7.Custom.Glow == 100 && s7.Custom.GlowSize == 0
          && s7.Custom.Accent2 == "" && !s7.Custom.Sweep, "a custom theme keeps its good colours and drops the bad ones");
    Check(s7.Looks.Count == 1 && s7.Looks["Midnight"].Accent == "#ff00aa" && s7.Looks["Midnight"].Colors["Text"] == "#ffffff", "saved looks are read back by name");
    var copy = s7.Custom.Copy(); copy.Colors["Bg"] = "#ffffff";
    s7.Save(cp4);
    var s8 = Settings.Load(cp4);
    Check(s7.Custom.Colors["Bg"] == "#0a0b0c" && s8.Custom.Colors["Bg"] == "#0a0b0c" && s8.Looks.ContainsKey("Midnight") && s8.AnimRing && s8.AnimHover && s8.AnimPages && s8.AnimBorder,
          "copying a look doesn't change the original, and the custom theme survives a save");
}

Console.WriteLine("keyboard only / mouse only, click bursts, clicking alongside");
{
    Recording Mixed() => new() { Events = { Event.Of(0, "MOVE", 1, 1), new Event(0.1, "KEY_DOWN", new[] { "a" }), new Event(0.2, "KEY_UP", new[] { "a" }),
                                            Event.Of(0.3, "LEFT_DOWN", 4, 4), Event.Of(0.35, "LEFT_UP", 4, 4), Event.Of(0.6, "MOVE", 6, 6) } };
    string Kinds(FakeInput fi) { lock (fi.Log) return string.Join(" ", fi.Log.Select(x => x.what == "key" ? "k" : x.what == "move" ? "m" : x.what == "button" ? "b" : x.what == "click" ? "C" : "?")); }
    (PlayStats st, double dur, FakeInput fi) PlayOnly(Recording r, string only)
    {
        var fi = new FakeInput();
        var pl = new Player(fi, null, () => 1.0);
        double t0 = Timing.Now;
        var st = pl.Play(r, 1, 0, default, null, default, only);
        return (st, Timing.Now - t0, fi);
    }
    var ko = PlayOnly(Mixed(), "keys");
    Check(Kinds(ko.fi) == "k k" && ko.st.Events == 2 && ko.dur is > 0.55 and < 0.75, $"keyboard only: just the keys are sent, and the recording keeps its length ({ko.dur:0.00} s)");
    var mo = PlayOnly(Mixed(), "mouse");
    Check(Kinds(mo.fi) == "m m b m b m" && mo.st.Events == 4, "mouse only: moves and buttons are sent, keys are left out");
    var all = PlayOnly(Mixed(), null);
    Check(all.st.Events == 6 && PlayOnly(Mixed(), "nonsense").st.Events == 6, "no filter (or an unknown one) plays everything");
    var none = PlayOnly(new Recording { Events = { Event.Of(0, "MOVE", 1, 1), Event.Of(0.3, "MOVE", 2, 2) } }, "keys");
    Check(Kinds(none.fi) == "" && none.dur is > 0.25 and < 0.45 && !none.st.Stopped, $"a filter that leaves nothing still takes the recording's time ({none.dur:0.00} s)");
    Check(Only.Keeps("keys", new Event(0, "IMAGE_WAIT", new[] { "x.png" })) && Only.Keeps("keys", new Event(0, "IMAGE_CLICK", new[] { "x.png" })) &&
          Only.Keeps("mouse", new Event(0, "IMAGE_CLICK", new[] { "x.png" })) && !Only.Keeps("keys", Event.Of(0, "SCROLL", 1, 1, 0, 1)),
          "picture steps are kept by both filters; scrolling counts as mouse");
    {
        // keyboard only: a picture click still waits for its picture (so what follows stays in step), but nothing is clicked
        var img = new Recording { Events = { Format.ParseLine("0 IMAGE_CLICK t.png conf=0.9 timeout=5 button=left onfail=stop key=e"), new Event(0.05, "KEY_DOWN", new[] { "a" }), new Event(0.1, "KEY_UP", new[] { "a" }) } };
        var ff = new FakeFinder();
        var fi = new FakeInput();
        var pl = new Player(fi, ff, () => 1.0);
        new Thread(() => { Thread.Sleep(250); ff.Visible = true; }) { IsBackground = true }.Start();
        double t0 = Timing.Now;
        var st = pl.Play(img, 1, 0, default, null, default, "keys");
        double d = Timing.Now - t0;
        Check(st.ImageHits == 1 && d > 0.24 && Kinds(fi) == "k k k k", $"keyboard only: a picture click still waits ({d:0.00} s), presses its key, and leaves the mouse alone ({Kinds(fi)})");
        var fi2x = new FakeInput();
        ff.Visible = true;
        new Player(fi2x, ff, () => 1.0).Play(img, 1, 0, default, null, default, "mouse");
        Check(Kinds(fi2x) == "m C", $"mouse only: the picture is clicked and no key is pressed ({Kinds(fi2x)})");
    }

    // click bursts
    {
        var fi = new FakeInput();
        var pl = new Player(fi, null, () => 1.0);
        double t0 = Timing.Now;
        int n = pl.Clicks(new ClickPlan { Points = { new[] { 10, 10 }, new[] { 20, 20 } }, IntervalMs = 20, Count = 5 }, default);
        double d = Timing.Now - t0;
        string where;
        lock (fi.Log) where = string.Join(" ", fi.Log.Where(x => x.what == "click").Select(x => x.detail.Split('@')[1].Split(',')[0]));
        Check(n == 5 && where == "10 20 10 20 10" && d is > 0.07 and < 0.25, $"a click burst goes round its spots at a steady pace ({d:0.00} s)");
        var yfi2 = new FakeInput();
        var ypl2 = new Player(yfi2, null, () => 1.0);
        t0 = Timing.Now;
        int yn2 = ypl2.Clicks(new ClickPlan { IntervalMs = 50, Seconds = 0.3, Button = "double" }, default);
        d = Timing.Now - t0;
        bool noMove, dbl;
        lock (yfi2.Log) { noMove = yfi2.Log.All(x => x.what == "click"); dbl = yfi2.Log.All(x => x.detail.StartsWith("left x2")); }
        Check(yn2 is >= 5 and <= 7 && d is > 0.27 and < 0.42 && noMove && dbl, $"a timed burst with no spots clicks where the pointer is ({yn2} double clicks in {d:0.00} s)");
        var yfi3 = new FakeInput();
        var ypl3 = new Player(yfi3, null, () => 1.0);
        using var yc3 = new CancellationTokenSource();
        int yn3 = 0;
        var th = new Thread(() => yn3 = ypl3.Clicks(new ClickPlan { IntervalMs = 20 }, yc3.Token));
        th.Start();
        Thread.Sleep(120);
        ypl3.Pause();
        Thread.Sleep(60);
        int atPause;
        lock (yfi3.Log) atPause = yfi3.Log.Count;
        Thread.Sleep(200);
        int afterHold;
        lock (yfi3.Log) afterHold = yfi3.Log.Count;
        ypl3.Resume();
        Thread.Sleep(120);
        yc3.Cancel();
        th.Join(1000);
        Check(atPause >= 3 && afterHold == atPause && yn3 > atPause && !th.IsAlive, $"pausing playback pauses the clicks; stopping ends them ({atPause} before, {yn3} in total)");
    }

    {
        // a timed burst: paused time doesn't count, and nothing is clicked the instant it resumes
        var fi = new FakeInput();
        var pl = new Player(fi, null, () => 1.0);
        int made = 0;
        double t0 = Timing.Now, took = 0;
        var th = new Thread(() => { made = pl.Clicks(new ClickPlan { IntervalMs = 200, Seconds = 0.5 }, default); took = Timing.Now - t0; });
        th.Start();
        Thread.Sleep(60);
        pl.Pause();
        Thread.Sleep(300);
        int during;
        lock (fi.Log) during = fi.Log.Count;
        pl.Resume();
        Thread.Sleep(60);
        int justAfter;
        lock (fi.Log) justAfter = fi.Log.Count;
        th.Join(2000);
        Check(during == 1 && justAfter == 1 && made == 3 && took is > 0.75 and < 0.95, $"paused time doesn't count towards a timed burst ({made} clicks in {took:0.00} s with a 0.3 s pause)");

        // clicks "where the pointer is" are held back while the pointer is on TinyRec itself
        var fp = new PointerInput();
        var pl2x = new Player(fp, null, () => 1.0) { IsOwn = (x, y) => x < 100 };
        int n2x = 0;
        var th2 = new Thread(() => n2x = pl2x.Clicks(new ClickPlan { IntervalMs = 20, Count = 3 }, default));
        th2.Start();
        Thread.Sleep(150);
        bool heldOff = pl2x.ClicksHeldOff;
        int heldBefore = fp.Count;
        fp.At = (500, 500);
        th2.Join(1000);
        Check(heldBefore == 0 && heldOff && n2x == 3 && fp.Count == 3 && !pl2x.ClicksHeldOff, "clicks at the pointer wait while the pointer is on TinyRec's own window");
    }

    // the auto clicker can hold still too (it runs alongside ordinary playback)
    {
        var fi = new FakeInput();
        var ac = new AutoClicker(fi);
        ac.Start(new AutoClickConfig { IntervalMs = 15 });
        Thread.Sleep(100);
        ac.Hold = true;
        Thread.Sleep(60);
        int a = ac.Clicks;
        Thread.Sleep(150);
        int b = ac.Clicks;
        ac.Hold = false;
        Thread.Sleep(100);
        int c = ac.Clicks;
        ac.Halt();
        Check(a >= 3 && b == a && c > b, $"the auto clicker holds while playback is paused and carries on after ({a}, {b}, {c})");
    }

    // sequences: a click step, clicks alongside a recording, keyboard-only steps
    {
        var lib = new Dictionary<string, Recording>
        {
            ["mixed.txt"] = Mixed(), ["b.txt"] = new Recording { Events = { Event.Of(0, "MOVE", 9, 9) } },
        };
        string Trace(FakeInput fi) { lock (fi.Log) return string.Join(" ", fi.Log.Select(x => x.what == "move" ? "m" + x.detail.Split(',')[0] : x.what == "click" ? "C" : x.what == "key" ? "k" : x.what == "button" ? "b" : "?")); }
        (SeqStats st, double dur, FakeInput fi, RunLog log) Go(Sequence q)
        {
            var fi = new FakeInput();
            var run = new SequenceRunner(new Player(fi, null, () => 1.0), n => lib[n]);
            var log = new RunLog("t");
            double t0 = Timing.Now;
            var st = run.Run(q, 1, 0, 0, log);
            return (st, Timing.Now - t0, fi, log);
        }
        var yq1 = new Sequence { Steps = { new SeqStep { Kind = "click", AcPoints = { new[] { 5, 5 } }, Clicks = 3, AcEvery = 20, Pause = 0.1 }, new SeqStep { Kind = "play", Recording = "b.txt" } } };
        var ya1 = Go(yq1);
        Check(Trace(ya1.fi) == "m5 C m5 C m5 C m9" && ya1.st.Clicks == 3 && ya1.st.Steps == 2 && yq1.Problems(n => true, n => true).Count == 0 && ya1.dur > 0.13,
              $"a click step clicks its spot, waits, and the next step follows ({Trace(ya1.fi)})");
        var only = new Sequence { Steps = { new SeqStep { Kind = "click", Clicks = 2, AcEvery = 10 } } };
        Check(only.Problems(n => true, n => true).Count == 0 && Go(only).st.Clicks == 2, "a sequence can be nothing but a click step");

        var yq2 = new Sequence { Steps = { new SeqStep { Kind = "play", Recording = "mixed.txt", Only = "keys", Ac = true, AcPoints = { new[] { 30, 30 } }, AcEvery = 100 },
                                          new SeqStep { Kind = "play", Recording = "b.txt" } } };
        var ya2 = Go(yq2);
        string yt2 = Trace(ya2.fi);
        int yclicks2 = yt2.Split(' ').Count(x => x == "C");
        Check(yt2.EndsWith("m9") && !yt2.Contains("m1") && !yt2.Contains(" b") && yt2.Split(' ').Count(x => x == "k") == 2 && yclicks2 is >= 5 and <= 8 && ya2.st.Clicks == yclicks2 &&
              ya2.dur is > 0.55 and < 0.9,
              $"keyboard only + clicks alongside: 2 keys, {yclicks2} clicks on the spot, no recorded mouse, and the clicks end with the step ({ya2.dur:0.00} s)");
        Check(ya2.log.Snapshot().Any(l => l.Text.Contains("auto-clicked")), "the run log says how many clicks went alongside");

        var yrt = Sequence.FromJson(yq2.ToJson());
        var yold = Sequence.FromJson("{ \"name\": \"yold\", \"steps\": [ { \"kind\": \"play\", \"recording\": \"b.txt\" }, { \"kind\": \"click\", \"ac_points\": [[1,2],[3]], \"ac_every\": -5 } ] }");
        Check(yrt.Steps[0].Only == "keys" && yrt.Steps[0].Ac && yrt.Steps[0].AcPoints.Count == 1 && yrt.Steps[0].AcPoints[0][1] == 30 && yrt.Steps[0].AcEvery == 100 &&
              yold.Steps.Count == 2 && !yold.Steps[0].Ac && yold.Steps[0].Only == "" && !yold.Clicker && yold.Steps[1].AcPoints.Count == 1 && yold.Steps[1].AcEvery == 1 && yold.Steps[1].Clicks == 10,
              "the new settings survive a save, old sequences open with them off, and odd values are tidied");

        // a step whose filter leaves nothing to send, repeated many times, must not spin
        lib["dot.txt"] = new Recording { Events = { Event.Of(0, "MOVE", 1, 1) } };
        var spin = Go(new Sequence { Steps = { new SeqStep { Kind = "play", Recording = "dot.txt", Only = "keys", Times = 6 } } });
        Check(spin.dur is > 0.25 and < 0.6 && Trace(spin.fi) == "" && spin.st.Plays == 6, $"an empty step repeated 6 times is paced, not spun ({spin.dur:0.00} s)");

        // stopping while a step clicks ends it at once
        var yq3 = new Sequence { Steps = { new SeqStep { Kind = "click", ClickSecs = 30, AcEvery = 20 } } };
        var yfi3 = new FakeInput();
        var yrun3 = new SequenceRunner(new Player(yfi3, null, () => 1.0), n => lib[n]);
        new Thread(() => { Thread.Sleep(150); yrun3.Stop(); }) { IsBackground = true }.Start();
        double yt3 = Timing.Now;
        var yst3 = yrun3.Run(yq3);
        Check(yst3.Stopped && Timing.Now - yt3 < 0.6 && yst3.Clicks >= 3, $"a timed click step stops at once when asked ({yst3.Clicks} clicks)");
    }
}

Console.WriteLine("click settings: order, bursts, spread, keys, pictures");
{
    (int n, double dur, FakeInput fi) Burst(ClickPlan plan, FakeFinder ff = null, Action<FakeFinder> during = null)
    {
        var fi = new FakeInput();
        var pl = new Player(fi, ff, () => 1.0);
        if (during != null) new Thread(() => during(ff)) { IsBackground = true }.Start();
        double t0 = Timing.Now;
        int n = pl.Clicks(plan, default);
        return (n, Timing.Now - t0, fi);
    }
    string Spots(FakeInput fi) { lock (fi.Log) return string.Join(" ", fi.Log.Where(x => x.what == "click").Select(x => x.detail.Split('@')[1].Split(',')[0])); }
    List<int[]> P(params int[] xs) => xs.Select(x => new[] { x, x }).ToList();

    var pp = Burst(new ClickPlan { Points = P(1, 2, 3), IntervalMs = 5, Count = 7, More = new ClickMore { Order = "pingpong", Style = "fast" } });
    Check(Spots(pp.fi) == "1 2 3 2 1 2 3", $"there-and-back order ({Spots(pp.fi)})");
    var ps2 = Burst(new ClickPlan { Points = P(1, 2), IntervalMs = 5, Count = 6, More = new ClickMore { PerSpot = 2, Style = "fast" } });
    Check(Spots(ps2.fi) == "1 1 2 2 1 1", $"several clicks on a spot before moving on ({Spots(ps2.fi)})");
    var rndo = Burst(new ClickPlan { Points = P(1, 2, 3, 4), IntervalMs = 2, Count = 40, More = new ClickMore { Order = "random", Style = "fast" } });
    var seenSpots = Spots(rndo.fi).Split(' ').Distinct().ToList();
    Check(seenSpots.Count >= 3 && seenSpots.All(x => x is "1" or "2" or "3" or "4") && Spots(rndo.fi) != string.Join(" ", Enumerable.Range(0, 40).Select(k => (k % 4 + 1).ToString())),
          "random order visits the spots in no fixed order");
    var spr = Burst(new ClickPlan { Points = P(100), IntervalMs = 2, Count = 30, More = new ClickMore { Spread = 5, Style = "fast" } });
    var xs = Spots(spr.fi).Split(' ').Select(int.Parse).ToList();
    Check(xs.All(x => x is >= 95 and <= 105) && xs.Distinct().Count() > 3, $"spread lands each click near the spot, not on one pixel ({xs.Min()}..{xs.Max()})");
    var bur = Burst(new ClickPlan { IntervalMs = 10, Count = 6, More = new ClickMore { Burst = 3, Rest = 0.25 } });
    List<double> ts;
    lock (bur.fi.Log) ts = bur.fi.Log.Where(x => x.what == "click").Select(x => x.t).ToList();
    Check(bur.n == 6 && ts[3] - ts[2] > 0.23 && ts[2] - ts[0] < 0.08 && bur.dur is > 0.25 and < 0.5, $"bursts: 3 quick clicks, a rest, 3 more ({bur.dur:0.00} s)");
    var kc = Burst(new ClickPlan { IntervalMs = 5, Count = 2, More = new ClickMore { Key = "e" } });
    string kinds;
    lock (kc.fi.Log) kinds = string.Join(" ", kc.fi.Log.Select(x => x.what == "click" ? "C" : x.what == "key" ? x.detail : x.what));
    Check(kinds == "C e:down e:up C e:down e:up", $"a key can go with every click ({kinds})");
    var ko2 = Burst(new ClickPlan { IntervalMs = 5, Count = 3, Points = P(9), More = new ClickMore { Key = "space", KeyOnly = true } });
    lock (ko2.fi.Log) kinds = string.Join(" ", ko2.fi.Log.Select(x => x.what == "key" ? x.detail : x.what));
    Check(kinds == "space:down space:up space:down space:up space:down space:up" && ko2.n == 3, "key only: the key is pressed and the mouse is left alone");
    var hd = Burst(new ClickPlan { IntervalMs = 5, Count = 2, Button = "right", More = new ClickMore { Hold = 40 } });
    lock (hd.fi.Log) kinds = string.Join(" ", hd.fi.Log.Select(x => x.detail));
    Check(kinds == "right:down right:up right:down right:up" && hd.dur > 0.07, "hold keeps each press down for its time");
    var sd = Burst(new ClickPlan { IntervalMs = 10, Count = 2, More = new ClickMore { StartDelay = 0.2 } });
    Check(sd.dur is > 0.2 and < 0.35, $"start delay waits before the first click ({sd.dur:0.00} s)");
    var jit = Burst(new ClickPlan { IntervalMs = 30, Count = 12, More = new ClickMore { Jitter = 20 } });
    lock (jit.fi.Log) ts = jit.fi.Log.Select(x => x.t).ToList();
    var gaps = ts.Zip(ts.Skip(1), (a, b) => b - a).ToList();
    Check(gaps.All(g => g > 0.007 && g < 0.06) && gaps.Max() - gaps.Min() > 0.008, $"random variation changes every gap ({gaps.Min() * 1000:0}..{gaps.Max() * 1000:0} ms)");

    // put the pointer back after each click
    {
        var fi = new FakeInput();
        fi.Move(500, 500);
        var pl = new Player(fi, null, () => 1.0);
        pl.Clicks(new ClickPlan { Points = P(10), IntervalMs = 5, Count = 2, More = new ClickMore { Return = true, Style = "fast" } }, default);
        string tr;
        lock (fi.Log) tr = string.Join(" ", fi.Log.Skip(1).Select(x => x.what == "move" ? "m" + x.detail.Split(',')[0] : "C"));
        Check(tr == "m10 C m500 m10 C m500" && fi.Pointer() == (500, 500), $"the pointer goes back where it was after each click ({tr})");
    }

    // pictures: click where one is, only while one shows, until one shows
    var ffT = new FakeFinder();
    var tg = Burst(new ClickPlan { IntervalMs = 20, Seconds = 0.6, More = new ClickMore { Target = "t.png", Style = "fast" } }, ffT, f => { Thread.Sleep(300); f.Visible = true; });
    lock (tg.fi.Log) ts = tg.fi.Log.Where(x => x.what == "click").Select(x => x.t).ToList();
    Check(tg.n >= 3 && Spots(tg.fi).Split(' ').All(x => x == "1230") && tg.dur is > 0.55 and < 0.8, $"target picture: nothing until it shows, then it is clicked where it is ({tg.n} clicks)");
    var ffI = new FakeFinder { Visible = true };
    var cond = Burst(new ClickPlan { IntervalMs = 20, Seconds = 0.6, Points = P(7), More = new ClickMore { If = "t.png", Style = "fast" } }, ffI, f => { Thread.Sleep(250); f.Visible = false; });
    var ffN = new FakeFinder { Visible = true };
    var condNot = Burst(new ClickPlan { IntervalMs = 20, Seconds = 0.6, Points = P(7), More = new ClickMore { If = "t.png", IfNot = true, Style = "fast" } }, ffN, f => { Thread.Sleep(350); f.Visible = false; });
    Check(cond.n is >= 5 and <= 22 && condNot.n is >= 3 and <= 14, $"only while a picture is (or isn't) on screen ({cond.n} while shown, {condNot.n} once gone)");
    var ffU = new FakeFinder();
    var unt = Burst(new ClickPlan { IntervalMs = 20, Points = P(7), More = new ClickMore { Until = "t.png", Style = "fast" } }, ffU, f => { Thread.Sleep(300); f.Visible = true; });
    Check(unt.n >= 5 && unt.dur is > 0.28 and < 0.7, $"until a picture shows: it stops by itself ({unt.n} clicks, {unt.dur:0.00} s)");
    bool missing = false;
    try { Burst(new ClickPlan { IntervalMs = 5, Count = 2, More = new ClickMore { If = "nope.png" } }, new FakeFinder()); } catch (FileNotFoundException) { missing = true; }
    Check(missing, "a click plan that needs a missing picture says so instead of clicking blind");

    // the Auto Clicker page uses the same engine
    var cfgA = new AutoClickConfig { Mode = "fixed", Points = P(4, 5), IntervalMs = 10, Seconds = 0.2, More = new ClickMore { Burst = 2, Rest = 0.05, LimitClicks = 1 } };
    var planA = cfgA.ToPlan();
    Check(planA.Points.Count == 1 && planA.More.Style == "fast" && planA.Seconds == 0.2 && planA.More.LimitClicks == 0 && planA.More.Burst == 2,
          "the auto clicker's settings turn into a click plan (fixed spot, fast style, its own time limit)");
    var fiA = new FakeInput();
    var acA = new AutoClicker(fiA);
    var doneA = new ManualResetEventSlim();
    acA.Start(cfgA, doneA.Set);
    Check(doneA.Wait(2000) && acA.Clicks is >= 3 and <= 12 && Spots(fiA).Split(' ').All(x => x == "4"), $"the auto clicker stops by itself after its seconds ({acA.Clicks} clicks)");
    var json = System.Text.Json.JsonSerializer.Serialize(new Settings { Autoclick = cfgA });
    Check(json.Contains("\"More\"") || json.Contains("more"), "the finer click settings are saved with the auto clicker");
}

Console.WriteLine("a finer filter: any mix of moves, clicks, scrolling, keys, image steps");
{
    var evs = new List<Event> { Event.Of(0, "MOVE", 1, 1), new Event(0.1, "KEY_DOWN", new[] { "a" }), new Event(0.15, "KEY_UP", new[] { "a" }), new Event(0.2, "KEY_DOWN", new[] { "tab" }),
                                new Event(0.25, "KEY_UP", new[] { "tab" }), Event.Of(0.3, "LEFT_DOWN", 4, 4), Event.Of(0.35, "LEFT_UP", 4, 4), Event.Of(0.4, "SCROLL", 4, 4, 0, 1),
                                new Event(0.5, "IMAGE_WAIT", new[] { "x.png" }) };
    string K(string only) => string.Join(" ", Only.Of(evs, only).Select(e => e.Kind.StartsWith("KEY") ? "k" + e.Args[0] : e.Kind[..1]));
    Check(K("clicks+keys") == "ka ka ktab ktab L L" && K("keys -tab") == "ka ka I" && K("-tab") == "M ka ka L L S I" && K("moves+scroll") == "M S" && K("none") == "" && K("both") == K(""),
          $"custom mixes keep exactly what is ticked ({K("clicks+keys")} / {K("keys -tab")})");
    Check(Only.Clean("both") == "" && Only.Clean("moves+clicks+scroll+keys+images") == "" && Only.Clean("scroll+moves+clicks+images") == "mouse" && Only.Clean("keys+images") == "keys" &&
          Only.Clean(" -TAB  -esc ") == "-esc -tab" && Only.Clean("keys+clicks -f5") == "clicks+keys -f5" && Only.Clean("mouse -tab") == "mouse" && Only.Clean("rubbish") == "",
          "filter settings are tidied into one spelling");
    Check(Only.Name("") == "mouse and keyboard" && Only.Name("keys") == "keyboard only" && Only.Name("clicks+keys").Contains("clicks, keys") && Only.Name("-tab").Contains("not tab"),
          "and described in plain words");
    var fi = new FakeInput();
    new Player(fi, null, () => 1.0).Play(new Recording { Events = evs.Take(8).ToList() }, 1, 0, default, null, default, "clicks+keys -tab");
    string tr;
    lock (fi.Log) tr = string.Join(" ", fi.Log.Select(x => x.what == "move" ? "m" : x.what == "button" ? "b" : x.what == "key" ? "k" : x.what == "scroll" ? "s" : "?"));
    Check(tr == "k k m b m b", $"clicks + keys without tab: no pointer travel of its own, no scrolling, no tab ({tr})");
}

Console.WriteLine("sequences: keys, jumps, stops, chance, parts of a recording, time limits, always-watch");
{
    Recording Long() => new() { Events = { Event.Of(0, "MOVE", 1, 1), Event.Of(0.4, "MOVE", 2, 2), Event.Of(0.8, "MOVE", 3, 3) } };
    Recording Short(int x) => new() { Events = { Event.Of(0, "MOVE", x, x) } };
    Recording Fifth() => new() { Events = { Event.Of(0, "MOVE", 5, 5), Event.Of(0.2, "MOVE", 6, 6) } };
    var lib = new Dictionary<string, Recording> { ["long.txt"] = Long(), ["a.txt"] = Short(7), ["b.txt"] = Short(9), ["fifth.txt"] = Fifth() };
    string Trace(FakeInput fi) { lock (fi.Log) return string.Join(" ", fi.Log.Select(x => x.what == "move" ? "m" + x.detail.Split(',')[0] : x.what == "click" ? "C" : x.what == "key" ? "k" : x.what == "focus" ? "F" : "?")); }
    (SeqStats st, double dur, FakeInput fi, RunLog log) Go(Sequence q, FakeFinder ff = null, int loops = 1, Action<FakeFinder> during = null, string window = null, Action<SequenceRunner> setup = null)
    {
        var fi = new FakeInput { Window = window };
        var run = new SequenceRunner(new Player(fi, ff, () => 1.0), n => lib[n]);
        setup?.Invoke(run);
        var log = new RunLog("t");
        if (during != null) new Thread(() => during(ff)) { IsBackground = true }.Start();
        double t0 = Timing.Now;
        var st = run.Run(q, loops, 0, 0, log);
        return (st, Timing.Now - t0, fi, log);
    }
    SeqStep Play(string r) => new() { Kind = "play", Recording = r };

    // keys
    var k1 = Go(new Sequence { Steps = { new SeqStep { Kind = "key", Combo = "ctrl+a", KeyTimes = 2, KeyEvery = 20, KeyHold = 10 }, new SeqStep { Kind = "key", KeyMode = "type", Text = "hi", TypeDelay = 5 } } });
    string keys;
    lock (k1.fi.Log) keys = string.Join(" ", k1.fi.Log.Select(x => x.detail));
    Check(keys == "ctrl:down a:down a:up ctrl:up ctrl:down a:down a:up ctrl:up u:0068:down u:0068:up u:0069:down u:0069:up" && k1.st.Steps == 2,
          "a key step presses a combination (held in order, let go in reverse) and types text");

    // jumps and stops
    var j1 = Go(new Sequence { Steps = { Play("a.txt"), new SeqStep { Kind = "goto", GoTo = 1, MaxJumps = 2 }, Play("b.txt") } });
    Check(Trace(j1.fi) == "m7 m7 m7 m9" && j1.st.Loops == 1, $"a jump goes back at most as often as allowed ({Trace(j1.fi)})");
    var ffJ = new FakeFinder { Visible = true };
    var j2 = Go(new Sequence { Steps = { new SeqStep { Kind = "goto", GoTo = 3, IfMode = "shown", IfImage = "t.png" }, Play("a.txt"), Play("b.txt") } }, ffJ);
    var j3 = Go(new Sequence { Steps = { new SeqStep { Kind = "goto", GoTo = 3, IfMode = "hidden", IfImage = "t.png" }, Play("a.txt"), Play("b.txt") } }, new FakeFinder { Visible = true });
    Check(Trace(j2.fi) == "m9" && Trace(j3.fi) == "m7 m9", "a jump can depend on a picture being on screen, or not being on screen");
    var s1 = Go(new Sequence { Steps = { Play("a.txt"), new SeqStep { Kind = "stop" }, Play("b.txt") } }, null, 3);
    var s2 = Go(new Sequence { Steps = { Play("a.txt"), new SeqStep { Kind = "stop", StopMode = "loop" }, Play("b.txt") } }, null, 2);
    var s3 = Go(new Sequence { Steps = { Play("a.txt"), new SeqStep { Kind = "stop", IfMode = "shown", IfImage = "t.png" }, Play("b.txt") } }, new FakeFinder());
    Check(Trace(s1.fi) == "m7" && !s1.st.Stopped && s1.st.Message == "" && Trace(s2.fi) == "m7 m7" && s2.st.Loops == 2 && Trace(s3.fi) == "m7 m9",
          "a stop step ends the whole run, or just this full run, or does nothing when its picture isn't there");
    var circle = Go(new Sequence { Steps = { Play("a.txt"), new SeqStep { Kind = "goto", GoTo = 3 }, new SeqStep { Kind = "goto", GoTo = 2 } } });
    Check(circle.st.Message.Contains("circle") && circle.dur < 2, "steps that only jump between each other are caught instead of spinning forever");

    // chance, and "only on some full runs"
    var c1 = Go(new Sequence { Steps = { new SeqStep { Kind = "play", Recording = "a.txt", Chance = 0 }, Play("b.txt") } }, null, 3);
    var c2 = Go(new Sequence { Steps = { new SeqStep { Kind = "play", Recording = "a.txt", When = "first" }, Play("b.txt") } }, null, 3);
    var c3 = Go(new Sequence { Steps = { new SeqStep { Kind = "play", Recording = "a.txt", When = "notfirst" }, Play("b.txt") } }, null, 3);
    var c4 = Go(new Sequence { Steps = { new SeqStep { Kind = "play", Recording = "a.txt", When = "every", EveryN = 2 }, Play("b.txt") } }, null, 4);
    var c5 = Go(new Sequence { Steps = { new SeqStep { Kind = "play", Recording = "a.txt", Chance = 50 }, Play("b.txt") } }, null, 60);
    int sevens = Trace(c5.fi).Split(' ').Count(x => x == "m7");
    Check(Trace(c1.fi) == "m9 m9 m9" && Trace(c2.fi) == "m7 m9 m9 m9" && Trace(c3.fi) == "m9 m7 m9 m7 m9" && Trace(c4.fi) == "m9 m7 m9 m9 m7 m9" && sevens is > 12 and < 48,
          $"steps can run only sometimes: never, first run only, not the first, every 2nd, or by chance ({sevens} of 60 at 50%)");

    // part of a recording, replaying for a set time, speed variation
    var p1 = Go(new Sequence { Steps = { new SeqStep { Kind = "play", Recording = "long.txt", From = 0.3, To = 0.5 }, Play("b.txt") } });
    Check(Trace(p1.fi) == "m2 m9" && p1.dur < 0.3, $"only the chosen part of a recording plays ({Trace(p1.fi)}, {p1.dur:0.00} s)");
    var p2 = Go(new Sequence { Steps = { new SeqStep { Kind = "play", Recording = "fifth.txt", ForSecs = 0.7 }, Play("b.txt") } });
    int fives = Trace(p2.fi).Split(' ').Count(x => x == "m5");
    Check(fives is >= 3 and <= 4 && Trace(p2.fi).EndsWith("m9") && p2.dur is > 0.65 and < 0.95, $"a recording can be replayed for a set time ({fives} passes in {p2.dur:0.00} s)");
    var p3 = Go(new Sequence { Steps = { new SeqStep { Kind = "play", Recording = "long.txt", SpeedJitter = 50, Speed = 4, Times = 3 } } });
    Check(p3.st.Plays == 3 && p3.dur is > 0.35 and < 1.3, $"speed variation keeps every play within its range ({p3.dur:0.00} s for 3 plays at 4x +/- 50%)");

    // sequence-wide: start delay, random gap, time limit
    var d1 = Go(new Sequence { StartDelay = 0.2, GapRandom = 0.1, Steps = { Play("a.txt"), Play("b.txt") } });
    Check(d1.dur is > 0.2 and < 0.5 && Trace(d1.fi) == "m7 m9", $"a sequence can wait before it starts ({d1.dur:0.00} s)");
    var lim = Go(new Sequence { MaxMinutes = 0.01, Steps = { new SeqStep { Kind = "wait", Seconds = 0.1 }, Play("a.txt") } }, null, 0);
    Check(lim.st.TimedOut && !lim.st.Stopped && lim.dur is > 0.55 and < 0.9 && lim.st.Loops >= 3, $"a time limit ends a run that would go on forever ({lim.dur:0.00} s, {lim.st.Loops} full runs)");

    // when a step fails: start again from step 1, or carry on
    SeqStep Img(double timeout) => new() { Kind = "image", Image = "t.png", Timeout = timeout, OnFail = "stop", After = 0 };
    var e1 = Go(new Sequence { OnError = "restart", Steps = { Play("a.txt"), Img(0.2), Play("b.txt") } }, new FakeFinder(), 1, f => { Thread.Sleep(700); f.Visible = true; });
    Check(e1.st.Restarts >= 1 && e1.st.Loops == 1 && e1.st.Message == "" && !e1.st.Stopped && Trace(e1.fi).StartsWith("m7 m7") && Trace(e1.fi).EndsWith("C m9"),
          $"'start again' turns a failed step into a fresh run from step 1 ({e1.st.Restarts} restart(s): {Trace(e1.fi)})");
    var e2 = Go(new Sequence { OnError = "skip", Steps = { Play("a.txt"), Img(0.2), Play("b.txt") } }, new FakeFinder());
    var e3 = Go(new Sequence { Steps = { Play("a.txt"), Img(0.2), Play("b.txt") } }, new FakeFinder());
    Check(Trace(e2.fi) == "m7 m9" && e2.st.Failures == 1 && e2.st.Loops == 1 && Trace(e3.fi) == "m7" && e3.st.Message.Contains("didn't show up"),
          "'carry on' skips the failed step; with neither, the run stops as before");

    // always-watch: a picture looked for the whole run
    SeqGuard G(string action, string key = "") => new() { Image = "t.png", Action = action, Key = key, Every = 0.2, Cooldown = 5 };
    var g1 = Go(new Sequence { Guards = { G("click") }, Steps = { new SeqStep { Kind = "wait", Seconds = 0.9 }, Play("b.txt") } }, new FakeFinder(), 1, f => { Thread.Sleep(250); f.Visible = true; });
    Check(Trace(g1.fi) == "m1230 C m9" && g1.st.GuardHits == 1 && g1.log.Snapshot().Any(l => l.Text.Contains("always-watch")), $"an always-watch clicks its picture away once, then the run carries on ({Trace(g1.fi)})");
    var g2 = Go(new Sequence { Guards = { G("key", "e") }, Steps = { new SeqStep { Kind = "wait", Seconds = 0.7 } } }, new FakeFinder { Visible = true });
    Check(Trace(g2.fi) == "k k", "an always-watch can press a key instead");
    var g3 = Go(new Sequence { Guards = { G("restart") }, Steps = { Play("a.txt"), new SeqStep { Kind = "wait", Seconds = 0.6 }, Play("b.txt") } }, new FakeFinder(), 1,
                f => { Thread.Sleep(250); f.Visible = true; Thread.Sleep(350); f.Visible = false; });
    Check(g3.st.Restarts == 1 && Trace(g3.fi) == "m7 m7 m9" && g3.st.Loops == 1, $"an always-watch can start the run again from step 1 ({Trace(g3.fi)})");
    var g4 = Go(new Sequence { Guards = { G("stop") }, Steps = { new SeqStep { Kind = "wait", Seconds = 5 }, Play("b.txt") } }, new FakeFinder { Visible = true });
    Check(g4.st.Stopped && g4.st.Message.Contains("always-watch") && g4.dur < 1.5 && Trace(g4.fi) == "", $"an always-watch can stop the run ({g4.dur:0.00} s)");
    string told = null;
    var g5 = Go(new Sequence { Guards = { G("alert") }, Steps = { new SeqStep { Kind = "wait", Seconds = 0.5 }, new SeqStep { Kind = "alert", Message = "boss down" } } },
                new FakeFinder { Visible = true }, 1, null, null, r => r.Notify = (m, snd) => told = (told == null ? "" : told + " | ") + m);
    Check(told != null && told.Contains("t.png") && told.Contains("boss down"), $"alerts reach the app, from an always-watch and from an alert step ({told})");
    var g6 = Go(new Sequence { Guards = { new SeqGuard { Image = "t.png", Off = true, Every = 0.2 } }, Steps = { new SeqStep { Kind = "wait", Seconds = 0.5 }, Play("b.txt") } }, new FakeFinder { Visible = true });
    Check(Trace(g6.fi) == "m9" && g6.st.GuardHits == 0, "a switched-off always-watch does nothing");

    // wait for a picture to go away
    SeqStep GoneStep(double timeout, string fail) => new() { Kind = "image", Image = "t.png", Gone = true, Timeout = timeout, OnFail = fail, After = 0 };
    var gn1 = Go(new Sequence { Steps = { GoneStep(0, "stop"), Play("b.txt") } }, new FakeFinder { Visible = true }, 1, f => { Thread.Sleep(300); f.Visible = false; });
    var gn2 = Go(new Sequence { Steps = { GoneStep(0.3, "skip"), Play("b.txt") } }, new FakeFinder { Visible = true });
    var gn3 = Go(new Sequence { Steps = { GoneStep(0.3, "stop"), Play("b.txt") } }, new FakeFinder { Visible = true });
    Check(Trace(gn1.fi) == "m9" && gn1.dur is > 0.28 and < 0.7 && gn1.st.ImageHits == 1 && Trace(gn2.fi) == "m9" && gn2.st.ImageMisses == 1 && Trace(gn3.fi) == "" && gn3.st.Message.Contains("still on screen"),
          $"an image step can wait for its picture to go away instead, with the same time limit choices ({gn1.dur:0.00} s)");

    // windows
    var w1 = Go(new Sequence { Front = "Roblox", Steps = { new SeqStep { Kind = "focus", WindowTitle = "Rob", OnFail = "skip" }, Play("b.txt") } }, null, 1, null, "Roblox");
    var w2 = Go(new Sequence { Steps = { new SeqStep { Kind = "focus", WindowTitle = "Nope", OnFail = "skip" }, Play("b.txt") } }, null, 1, null, "Roblox");
    var w3 = Go(new Sequence { Steps = { new SeqStep { Kind = "focus", WindowTitle = "Nope", OnFail = "stop" }, Play("b.txt") } }, null, 1, null, "Roblox");
    Check(Trace(w1.fi) == "F F m9" && Trace(w2.fi) == "F m9" && Trace(w3.fi) == "F" && w3.st.Message.Contains("no window"), "windows are brought to the front; a missing one stops the run or not, as chosen");

    // clicks alongside can have their own limit; a click step can run until a picture shows
    var a1 = Go(new Sequence { Steps = { new SeqStep { Kind = "play", Recording = "long.txt", Only = "keys", Ac = true, AcEvery = 50, AcPoints = { new[] { 3, 3 } }, More = new ClickMore { LimitClicks = 4, Style = "fast" } } } });
    Check(a1.st.Clicks == 4 && a1.dur > 0.75, $"clicks alongside stop at their own limit while the recording plays on ({a1.st.Clicks} clicks, {a1.dur:0.00} s)");
    var a2 = Go(new Sequence { Steps = { new SeqStep { Kind = "click", ClickForever = true, AcEvery = 30, AcPoints = { new[] { 3, 3 } }, More = new ClickMore { Until = "t.png", Style = "fast" } }, Play("b.txt") } },
                new FakeFinder(), 1, f => { Thread.Sleep(300); f.Visible = true; });
    Check(a2.st.Clicks >= 4 && Trace(a2.fi).EndsWith("m9") && a2.dur is > 0.28 and < 0.8, $"a click step can run until a picture shows ({a2.st.Clicks} clicks)");

    // things a review turned up
    var rv1 = Go(new Sequence { Steps = { new SeqStep { Kind = "play", Recording = "a.txt", When = "first" } } }, null, 4);
    int rv1Lines; lock (rv1.fi.Log) rv1Lines = rv1.log.Snapshot().Count(l => l.Text.Contains("full run"));
    Check(rv1.st.Loops == 4 && rv1.dur > 0.5 && rv1Lines == 1, $"full runs with nothing due don't spin or fill the log ({rv1.dur:0.00} s, {rv1Lines} line)");
    var rv2 = Go(new Sequence { Steps = { new SeqStep { Kind = "play", Recording = "a.txt", When = "notfirst" }, new SeqStep { Kind = "repeat", Count = 2500, Back = 1 }, Play("b.txt") } });
    Check(rv2.st.Message == "" && Trace(rv2.fi) == "m9", $"a long Repeat around a step that isn't due is not mistaken for a circle ({rv2.st.Message})");
    var rv3 = Go(new Sequence { Steps = { new SeqStep { Kind = "play", Recording = "a.txt", Pause = 0.6 }, new SeqStep { Kind = "play", Recording = "b.txt", When = "first" } } }, null, 2);
    Check(Trace(rv3.fi) == "m7 m9 m7" && rv3.dur is > 0.58 and < 1.0, $"the wait after the last step that actually runs is dropped at the very end ({rv3.dur:0.00} s)");
    {
        var fiL = new FakeInput(); var pl = new Player(fiL, null, () => 1.0); var runL = new SequenceRunner(pl, n => lib[n]);
        new Thread(() => { Thread.Sleep(100); pl.Pause(); Thread.Sleep(500); pl.Resume(); }) { IsBackground = true }.Start();
        double t0 = Timing.Now;
        var stL = runL.Run(new Sequence { MaxMinutes = 0.3 / 60, Steps = { new SeqStep { Kind = "wait", Seconds = 5 }, Play("b.txt") } }, 1, 0, 0, new RunLog("t"));
        double durL = Timing.Now - t0;
        Check(stL.TimedOut && durL is > 0.75 and < 1.3, $"a time limit doesn't count paused time ({durL:0.00} s for a 0.3 s limit with a 0.5 s pause)");
    }
    {
        var fr = new FakeInput(); var acR = new AutoClicker(fr); var doneR = new ManualResetEventSlim();
        acR.Start(new AutoClickConfig { IntervalMs = 10, Count = 1, Button = "right", Double = true }, doneR.Set);
        doneR.Wait(3000);
        string what; lock (fr.Log) what = string.Join(" ", fr.Log.Where(x => x.what == "click").Select(x => x.detail.Split('@')[0].Trim()));
        Check(what == "right x2", $"a right double click stays a right click ({what})");
        bool threw = false;
        try { new AutoClicker(new FakeInput()).Start(new AutoClickConfig { More = new ClickMore { Key = "nonsense" } }); } catch (ArgumentException) { threw = true; }
        bool threw2 = false;
        try { new AutoClicker(new FakeInput()).Start(new AutoClickConfig { More = new ClickMore { If = "t.png" } }); } catch (ArgumentException) { threw2 = true; }
        Check(threw && threw2, "the auto clicker refuses to start with a key that doesn't exist, or with pictures it can't look for");
    }
    Check(Only.Clean("keys+") == "+keys" && !Only.Parse("+keys").Images && Only.Parse("+keys").KeysOn && !Only.Parse("+keys").Clicks && Only.Clean("+keys") == "+keys" && Only.Parse("keys").Images,
          "“keys without image steps” is kept apart from the Keyboard-only preset");
    var skp = Only.Parse("keys -Escape -ctrl");
    Check(!skp.Keeps(new Event(0, "KEY_DOWN", new[] { "ctrl_l" })) && !skp.Keeps(new Event(0, "KEY_DOWN", new[] { "esc" })) && skp.Keeps(new Event(0, "KEY_DOWN", new[] { "a" })) && Only.Clean("keys -Escape") == "keys -esc",
          "keys to leave out: “ctrl” covers both Ctrl keys and “escape” means esc");
    var rv4 = Go(new Sequence { Steps = { new SeqStep { Kind = "click", ClickForever = true, AcEvery = 3000, AcPoints = { new[] { 3, 3 } }, More = new ClickMore { Until = "t.png", Style = "fast" } }, Play("b.txt") } },
                 new FakeFinder(), 1, f => { Thread.Sleep(300); f.Visible = true; });
    Check(rv4.st.Clicks == 1 && rv4.dur is > 0.28 and < 1.0, $"“until a picture shows” is noticed between slow clicks too ({rv4.dur:0.00} s)");

    // saving, and problems listed up front
    var full = new Sequence
    {
        OnError = "restart", MaxMinutes = 30, GapRandom = 0.5, LoopRandom = 2, StartDelay = 1, Front = "Roblox", DoneSound = true, Guards = { G("click", "e") },
        Steps = { new SeqStep { Kind = "click", More = new ClickMore { Spread = 4, Order = "pingpong", Key = "e", Burst = 5, Rest = 1 } }, new SeqStep { Kind = "key", Combo = "ctrl+a" },
                  new SeqStep { Kind = "goto", GoTo = 1, IfMode = "shown", IfImage = "t.png", MaxJumps = 3 }, new SeqStep { Kind = "alert", Message = "hi" },
                  new SeqStep { Kind = "play", Recording = "a.txt", From = 1, To = 2, ForSecs = 9, Chance = 40, When = "every", EveryN = 3, Only = "clicks+keys -tab" } },
    };
    var backQ = Sequence.FromJson(full.ToJson());
    Check(backQ.OnError == "restart" && backQ.MaxMinutes == 30 && backQ.Guards.Count == 1 && backQ.Guards[0].Key == "e" && backQ.Steps[0].More.Order == "pingpong" && backQ.Steps[0].More.Burst == 5 &&
          backQ.Steps[2].IfImage == "t.png" && backQ.Steps[2].MaxJumps == 3 && backQ.Steps[4].ForSecs == 9 && backQ.Steps[4].Chance == 40 && backQ.Steps[4].EveryN == 3 && backQ.Steps[4].Only == "clicks+keys -tab" &&
          backQ.Steps.Count == 5 && backQ.Front == "Roblox" && backQ.DoneSound,
          "every new setting survives a save");
    var bad = new Sequence
    {
        Guards = { new SeqGuard { Image = "gone.png" }, new SeqGuard { Image = "t.png", Action = "key" } },
        Steps = { new SeqStep { Kind = "key" }, new SeqStep { Kind = "key", Combo = "ctrl+nonsense" }, new SeqStep { Kind = "goto", GoTo = 9 }, new SeqStep { Kind = "goto", GoTo = 4 },
                  new SeqStep { Kind = "stop", IfMode = "shown" }, new SeqStep { Kind = "focus" }, new SeqStep { Kind = "click", More = new ClickMore { If = "gone.png", Key = "zzz" } },
                  new SeqStep { Kind = "key", KeyMode = "type" }, Play("a.txt") },
    };
    var probs = bad.Problems(n => lib.ContainsKey(n), n => n == "t.png");
    Check(probs.Count == 10, $"problems with the new kinds of step are listed before anything runs ({probs.Count})");
    var oldSeq = Sequence.FromJson("{ \"name\": \"old\", \"steps\": [ { \"kind\": \"play\", \"recording\": \"b.txt\" }, { \"kind\": \"bogus\" } ] }");
    Check(oldSeq.Steps.Count == 1 && oldSeq.Steps[0].Chance == 100 && oldSeq.Steps[0].More.IsPlain && oldSeq.Guards.Count == 0 && oldSeq.OnError == "",
          "old sequences open with every new setting at its plain default");
}

Directory.Delete(tmp, true);
Console.WriteLine($"\nALL {ok} CHECKS PASSED");

sealed class FakeInput : IInput
{
    public readonly List<(double t, string what, string detail)> Log = new();
    (int x, int y) pos;
    void Add(string w, string d) { lock (Log) Log.Add((Timing.Now, w, d)); }
    public void Move(int x, int y) { pos = (x, y); Add("move", $"{x},{y}"); }
    public void Button(string name, bool down) => Add("button", $"{name}:{(down ? "down" : "up")}");
    public void Click(string name, int count) => Add("click", $"{name} x{count} @{pos.x},{pos.y}");
    public void Scroll(double dx, double dy) => Add("scroll", $"{dx},{dy}");
    public void Key(string token, bool down) => Add("key", $"{token}:{(down ? "down" : "up")}");
    public (int x, int y)? Pointer() => pos;
    public string Window;                                // the title of the one window that "exists"
    public bool Focus(string title) { Add("focus", title); return Window != null && Window.Contains(title); }
}

sealed class PointerInput : IInput
{
    public volatile int Count;
    public (int x, int y) At = (10, 10);
    public void Move(int x, int y) { }
    public void Button(string name, bool down) { }
    public void Click(string name, int count) => Count++;
    public void Scroll(double dx, double dy) { }
    public void Key(string token, bool down) { }
    public (int x, int y)? Pointer() => At;
}

sealed class FakeFinder : IImageFinder
{
    public volatile bool Visible;
    public Match Locate(string name, double conf, (int x, int y, int w, int h)? region)
    {
        if (name != "t.png") throw new FileNotFoundException($"image '{name}' not found in the image library");
        return Visible ? new Match { Found = true, X = 1230, Y = 185, Score = 0.99 } : new Match { Score = 0.2 };
    }
}

sealed class FlakyFinder : IImageFinder
{
    public int Calls;
    public Match Locate(string name, double conf, (int x, int y, int w, int h)? region)
    {
        if (++Calls < 3) throw new InvalidOperationException("the screen can't be read right now");
        return new Match { Found = true, X = 1, Y = 1, Score = 0.97 };
    }
}

sealed class TwoFinder : IImageFinder
{
    public Match Locate(string name, double conf, (int x, int y, int w, int h)? region) =>
        name == "b.png" ? new Match { Found = true, X = 5, Y = 5, Score = 0.95 } : new Match { Score = 0.1 };
}
