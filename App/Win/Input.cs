// Input.cs - sends mouse and keyboard input with Windows SendInput (what playback and the auto clicker use).
using System;
using System.Threading;
using TinyRec.Core;
using static TinyRec.Win.Native;

namespace TinyRec.Win;

public sealed class WinInput : IInput
{
    static readonly int Size = System.Runtime.InteropServices.Marshal.SizeOf<INPUT>();

    static void Send(INPUT i) => SendInput(1, new[] { i }, Size);

    /// Run every native call once at start-up, so the first real playback step isn't slowed by one-time set-up work.
    public static void Warm()
    {
        try
        {
            SendInput(0, Array.Empty<INPUT>(), Size);
            CursorPos();
            VirtualScreen();
            MapVirtualKey(0x41, 0);
        }
        catch { }
    }

    static void Mouse(uint flags, int data = 0, int dx = 0, int dy = 0) =>
        Send(new INPUT { type = 0, U = new InputUnion { mi = new MOUSEINPUT { dx = dx, dy = dy, mouseData = unchecked((uint)data), dwFlags = flags, dwExtraInfo = Native.OwnTag } } });

    public static (int x, int y) CursorPos()
    {
        GetCursorPos(out var p);
        return (p.X, p.Y);
    }

    public (int x, int y)? Pointer() => CursorPos();

    public bool Focus(string title) => ScreenGrab.FocusWindow(title);

    public void Move(int x, int y)
    {
        var (vx, vy, vw, vh) = VirtualScreen();
        x = Math.Clamp(x, vx, vx + vw - 1);
        y = Math.Clamp(y, vy, vy + vh - 1);
        // normalised 0..65535 over all monitors; rounded up so Windows maps back to exactly this pixel
        long nx = ((long)(x - vx) * 65536 + vw - 1) / vw;
        long ny = ((long)(y - vy) * 65536 + vh - 1) / vh;
        Mouse(MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK, 0, (int)Math.Min(nx, 65535), (int)Math.Min(ny, 65535));
        if (CursorPos() != (x, y)) SetCursorPos(x, y);    // belt and braces: force the exact pixel
    }

    public void Button(string name, bool down)
    {
        uint f = (name, down) switch
        {
            ("left", true) => MOUSEEVENTF_LEFTDOWN, ("left", false) => MOUSEEVENTF_LEFTUP,
            ("right", true) => MOUSEEVENTF_RIGHTDOWN, ("right", false) => MOUSEEVENTF_RIGHTUP,
            ("middle", true) => MOUSEEVENTF_MIDDLEDOWN, ("middle", false) => MOUSEEVENTF_MIDDLEUP,
            _ => 0,
        };
        if (f != 0) Mouse(f);
    }

    public void Click(string name, int count)
    {
        for (int i = 0; i < count; i++)
        {
            Button(name, true);
            Thread.Sleep(12);
            Button(name, false);
            if (i + 1 < count) Thread.Sleep(40);
        }
    }

    /// Used for image clicks. Games often miss a click that arrives in the same instant as the pointer, so:
    /// go there, let it settle, nudge the pointer (so the game sees it hovering), then press and hold for a few frames.
    public void ClickAt(int x, int y, string name, int count)
    {
        Move(x, y);
        Thread.Sleep(40);
        Mouse(MOUSEEVENTF_MOVE, 0, 1, 1);
        Thread.Sleep(15);
        Move(x, y);
        Thread.Sleep(45);
        for (int i = 0; i < count; i++)
        {
            Button(name, true);
            Thread.Sleep(60);
            Button(name, false);
            if (i + 1 < count) Thread.Sleep(70);
        }
    }

    public void Scroll(double dx, double dy)
    {
        if (dy != 0) Mouse(MOUSEEVENTF_WHEEL, (int)Math.Round(dy * 120));
        if (dx != 0) Mouse(MOUSEEVENTF_HWHEEL, (int)Math.Round(dx * 120));
    }

    static bool Extended(int vk) =>
        vk is >= 0x21 and <= 0x28 or 0x2C or 0x2D or 0x2E or 0x5B or 0x5C or 0x5D or 0x6F or 0x90 or 0xA3 or 0xA5
            or >= 0xA6 and <= 0xB7;

    public void Key(string token, bool down)
    {
        if (Keys.IsChar(token))
        {
            Send(new INPUT
            {
                type = 1,
                U = new InputUnion { ki = new KEYBDINPUT { wVk = 0, wScan = (ushort)Keys.CharCode(token),
                                                           dwFlags = KEYEVENTF_UNICODE | (down ? 0u : KEYEVENTF_KEYUP), dwExtraInfo = Native.OwnTag } }
            });
            return;
        }
        int vk = Keys.Vk(token);
        uint flags = down ? 0u : KEYEVENTF_KEYUP;
        if (Extended(vk)) flags |= KEYEVENTF_EXTENDEDKEY;
        Send(new INPUT
        {
            type = 1,
            U = new InputUnion { ki = new KEYBDINPUT { wVk = (ushort)vk, wScan = (ushort)MapVirtualKey((uint)vk, 0), dwFlags = flags, dwExtraInfo = Native.OwnTag } }
        });
    }
}
