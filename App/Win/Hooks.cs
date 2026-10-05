// Hooks.cs - watching the keyboard and mouse (hotkeys, recording, "Record spots").
//
// The low-level hooks live on their own thread with their own message loop, so a busy window can never
// make your keyboard or mouse lag. Everything here runs on that hook thread: keep it quick, and hand
// work to the UI with Dispatcher.BeginInvoke.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using TinyRec.Core;
using static TinyRec.Win.Native;

namespace TinyRec.Win;

public readonly record struct KeyInfo(int Vk, bool Down, bool Injected, int Scan = 0);
public readonly record struct MouseInfo(int Msg, int X, int Y, int Delta, bool Injected);

public sealed class HookThread : IDisposable
{
    public Func<KeyInfo, bool> OnKey;       // return true to swallow the key
    public Action<MouseInfo> OnMouse;

    readonly HookProc kbProc, msProc;       // kept in fields so the GC never frees them
    readonly ManualResetEventSlim ready = new();
    Thread thread;
    uint threadId;
    IntPtr kbHook, msHook;
    const uint MouseOn = WM_APP + 1, MouseOff = WM_APP + 2;

    public HookThread()
    {
        kbProc = KbProc;
        msProc = MsProc;
    }

    public bool KeyboardActive => kbHook != IntPtr.Zero;

    public void Start()
    {
        thread = new Thread(Run) { IsBackground = true, Name = "TinyRec hooks", Priority = ThreadPriority.Highest };
        thread.Start();
        ready.Wait(3000);
    }

    void Run()
    {
        threadId = GetCurrentThreadId();
        var mod = GetModuleHandle(null);
        kbHook = SetWindowsHookEx(WH_KEYBOARD_LL, kbProc, mod, 0);
        ready.Set();
        while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            if (msg.message == MouseOn && msHook == IntPtr.Zero) msHook = SetWindowsHookEx(WH_MOUSE_LL, msProc, mod, 0);
            else if (msg.message == MouseOff && msHook != IntPtr.Zero) { UnhookWindowsHookEx(msHook); msHook = IntPtr.Zero; }
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }
        if (msHook != IntPtr.Zero) UnhookWindowsHookEx(msHook);
        if (kbHook != IntPtr.Zero) UnhookWindowsHookEx(kbHook);
    }

    /// The mouse hook is only installed while something needs it (recording, Record spots).
    public void SetMouse(bool on)
    {
        if (threadId != 0) PostThreadMessage(threadId, on ? MouseOn : MouseOff, IntPtr.Zero, IntPtr.Zero);
    }

    IntPtr KbProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && OnKey != null)
        {
            var k = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            int m = (int)wParam;
            bool down = m is WM_KEYDOWN or WM_SYSKEYDOWN;
            try
            {
                if (OnKey(new KeyInfo((int)k.vkCode, down, (k.flags & LLKHF_INJECTED) != 0 && k.dwExtraInfo == OwnTag, (int)k.scanCode))) return new IntPtr(1);
            }
            catch { /* never let an error break the keyboard */ }
        }
        return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    IntPtr MsProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && OnMouse != null)
        {
            var s = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            int m = (int)wParam;
            int delta = m is WM_MOUSEWHEEL or WM_MOUSEHWHEEL ? (short)((s.mouseData >> 16) & 0xFFFF) : 0;
            try { OnMouse(new MouseInfo(m, s.pt.X, s.pt.Y, delta, (s.flags & LLMHF_INJECTED) != 0 && s.dwExtraInfo == OwnTag)); }
            catch { }
        }
        return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (threadId != 0) PostThreadMessage(threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
    }
}

/// Turns raw key presses into hotkeys. Hotkey presses can be swallowed so they don't reach the app you're in.
public sealed class HotkeyRouter
{
    readonly HotkeyTable table;
    readonly HashSet<string> pressed = new();
    readonly HashSet<int> swallowed = new();
    public volatile bool Block = true;             // swallow hotkeys
    public volatile bool Capture;                  // next non-modifier press is reported via Captured (rebinding)
    public Action<string> Fired;                   // action name (hook thread)
    public Action<string> Captured;                // "ctrl+alt+x" (hook thread)
    public Action PanicNow;                        // runs immediately on the hook thread
    public Recorder Recorder;
    public volatile bool GrabEscape;               // while drawing: Esc finishes it, wherever the focus is
    public Action EscapePressed;                   // hook thread

    public HotkeyRouter(HotkeyTable table) => this.table = table;

    /// Would pressing `token` right now (with the modifiers held) fire a hotkey?
    public bool IsHotkey(string token)
    {
        lock (pressed)
            return Keys.ModOf(token) == null && table.Find(Keys.Make(pressed.Select(Keys.ModOf).Where(m => m != null), token)) != null;
    }

    public bool OnKey(KeyInfo k)
    {
        if (k.Injected) return false;                        // TinyRec's own playback: never hotkeys, never recorded
        var tok = k.Vk == 0xE7 ? Keys.CharToken(k.Scan) : Keys.Name(k.Vk);   // 0xE7 = VK_PACKET (a typed character)
        if (tok == "esc" && GrabEscape && !Capture)
        {
            if (k.Down) EscapePressed?.Invoke();
            return true;
        }
        if (!k.Down)
        {
            lock (pressed) pressed.Remove(tok);
            if (swallowed.Remove(k.Vk)) return true;          // swallow the release of a swallowed press
            Recorder?.Key(tok, false);
            return false;
        }
        bool repeat;
        List<string> mods;
        lock (pressed)
        {
            repeat = !pressed.Add(tok);
            mods = pressed.Select(Keys.ModOf).Where(m => m != null).Distinct().ToList();
        }
        if (Keys.ModOf(tok) == null)
        {
            var combo = Keys.Make(mods, tok);
            if (Capture)
            {
                Capture = false;
                swallowed.Add(k.Vk);
                Captured?.Invoke(combo.ToString());
                return true;
            }
            var action = table.Find(combo);
            if (action != null)
            {
                if (!repeat)
                {
                    if (action == "panic") PanicNow?.Invoke();
                    Fired?.Invoke(action);
                }
                if (Block)
                {
                    swallowed.Add(k.Vk);
                    return true;
                }
                return false;                                 // not swallowed: still keep it out of recordings
            }
        }
        if (!repeat) Recorder?.Key(tok, true);
        return false;
    }
}

/// Records mouse and keyboard into TinyRec actions.
public sealed class Recorder
{
    readonly object gate = new();
    List<Event> events = new();
    readonly HashSet<string> btnDown = new(), keyDown = new();
    double t0;
    public volatile bool Active;
    public volatile Only.Spec What = Only.Parse("");   // which kinds of action are captured (everything by default)
    public volatile (int l, int t, int r, int b)[] Exclude = Array.Empty<(int, int, int, int)>();   // TinyRec's own windows

    public int Count
    {
        get { lock (gate) return events.Count; }
    }

    public void Start()
    {
        lock (gate)
        {
            events = new List<Event>();
            btnDown.Clear();
            keyDown.Clear();
            t0 = Timing.Now;
            paused = false;
        }
        Active = true;
    }

    // ---- pause: nothing is recorded while paused, and the timeline carries on from the same moment afterwards
    volatile bool paused;
    double pausedAt;
    int lastX, lastY;
    public bool Paused => paused;

    /// Seconds recorded so far (time spent paused doesn't count).
    public double Elapsed
    {
        get { lock (gate) return (paused ? pausedAt : Timing.Now) - t0; }
    }

    public void Pause()
    {
        lock (gate)
        {
            if (!Active || paused) return;
            pausedAt = Timing.Now;
            double t = pausedAt - t0;
            foreach (var k in keyDown) events.Add(new Event(t, "KEY_UP", new[] { k }));          // nothing stays "held" across a pause
            foreach (var b in btnDown) events.Add(Event.Of(t, $"{b.ToUpperInvariant()}_UP", lastX, lastY));
            keyDown.Clear();
            btnDown.Clear();
            paused = true;
        }
    }

    public void Resume()
    {
        lock (gate)
        {
            if (!paused) return;
            t0 += Timing.Now - pausedAt;
            paused = false;
        }
    }

    bool Excluded(int x, int y) => Exclude.Any(r => x >= r.l && x < r.r && y >= r.t && y < r.b);

    void Add(string kind, params object[] args)
    {
        lock (gate) events.Add(Event.Of(Timing.Now - t0, kind, args));
    }

    public void Key(string tok, bool down)
    {
        var what = What;
        if (!Active || paused || !what.KeysOn || what.Skips(tok)) return;
        lock (gate)
        {
            if (down)
            {
                if (!keyDown.Add(tok)) return;               // auto-repeat collapsed into one press
            }
            else if (!keyDown.Remove(tok)) return;          // release without a press: ignore
            events.Add(new Event(Timing.Now - t0, down ? "KEY_DOWN" : "KEY_UP", new[] { tok }));
        }
    }

    public void Mouse(MouseInfo m)
    {
        if (!Active || m.Injected || paused) return;
        lastX = m.X;
        lastY = m.Y;
        var what = What;
        bool wheel = m.Msg == WM_MOUSEWHEEL || m.Msg == WM_MOUSEHWHEEL;
        if (m.Msg == WM_MOUSEMOVE ? !what.Moves : wheel ? !what.Scroll : !what.Clicks) return;
        if (Excluded(m.X, m.Y) && m.Msg != WM_LBUTTONUP && m.Msg != WM_RBUTTONUP && m.Msg != WM_MBUTTONUP) return;
        switch (m.Msg)
        {
            case WM_MOUSEMOVE: Add("MOVE", m.X, m.Y); break;
            case WM_LBUTTONDOWN: Btn("left", true, m); break;
            case WM_LBUTTONUP: Btn("left", false, m); break;
            case WM_RBUTTONDOWN: Btn("right", true, m); break;
            case WM_RBUTTONUP: Btn("right", false, m); break;
            case WM_MBUTTONDOWN: Btn("middle", true, m); break;
            case WM_MBUTTONUP: Btn("middle", false, m); break;
            case WM_MOUSEWHEEL: Add("SCROLL", m.X, m.Y, 0, m.Delta / 120.0); break;
            case WM_MOUSEHWHEEL: Add("SCROLL", m.X, m.Y, m.Delta / 120.0, 0); break;
        }
    }

    void Btn(string name, bool down, MouseInfo m)
    {
        lock (gate)
        {
            if (down) btnDown.Add(name);
            else if (!btnDown.Remove(name)) return;          // release of a press we didn't record (e.g. on TinyRec)
            events.Add(Event.Of(Timing.Now - t0, $"{name.ToUpperInvariant()}_{(down ? "DOWN" : "UP")}", m.X, m.Y));
        }
    }

    /// Finish: strip the stop-hotkey's modifiers from the end, release anything still held, optionally start at 0.
    public List<Event> Stop(bool trimStart, ISet<string> stripTokens)
    {
        Active = false;
        List<Event> ev;
        lock (gate) ev = events.ToList();
        while (ev.Count > 0 && ev[^1].Kind == "KEY_DOWN" && stripTokens.Contains(ev[^1].Args[0])) ev.RemoveAt(ev.Count - 1);
        var keys = new List<string>();
        var btns = new Dictionary<string, List<string>>();
        foreach (var e in ev)
        {
            if (e.Kind == "KEY_DOWN") keys.Add(e.Args[0]);
            else if (e.Kind == "KEY_UP") keys.Remove(e.Args[0]);
            else if (e.Kind.EndsWith("_DOWN")) btns[e.Kind.Split('_')[0]] = e.Args;
            else if (e.Kind.EndsWith("_UP")) btns.Remove(e.Kind.Split('_')[0]);
        }
        double end = ev.Count > 0 ? ev[^1].T + 0.001 : 0;
        foreach (var k in keys) ev.Add(new Event(end, "KEY_UP", new[] { k }));
        foreach (var (b, pos) in btns) ev.Add(new Event(end, b + "_UP", pos));
        if (trimStart && ev.Count > 0)
        {
            double first = ev[0].T;
            foreach (var e in ev) e.T = Math.Max(0, e.T - first);
        }
        return ev;
    }
}
