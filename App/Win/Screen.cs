// Screen.cs - screenshots, finding windows by title, and image recognition (OpenCV template matching).
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using OpenCvSharp;
using TinyRec.Core;
using static TinyRec.Win.Native;
using Drawing = System.Drawing;
using Imaging = System.Drawing.Imaging;

namespace TinyRec.Win;

public static class ScreenGrab
{
    /// The whole virtual screen (all monitors) as a GDI bitmap. Caller disposes.
    public static (Drawing.Bitmap bmp, int left, int top) GrabBitmap()
    {
        var (x, y, w, h) = VirtualScreen();
        var bmp = new Drawing.Bitmap(w, h, Imaging.PixelFormat.Format32bppArgb);
        using (var g = Drawing.Graphics.FromImage(bmp))
            g.CopyFromScreen(x, y, 0, 0, new Drawing.Size(w, h), Drawing.CopyPixelOperation.SourceCopy);
        return (bmp, x, y);
    }

    /// Just one rectangle of the screen (real pixels) as an OpenCV BGR image - much cheaper than the whole desktop.
    public static Mat GrabMat(int x, int y, int w, int h)
    {
        using var bmp = new Drawing.Bitmap(w, h, Imaging.PixelFormat.Format32bppArgb);
        using (var g = Drawing.Graphics.FromImage(bmp))
            g.CopyFromScreen(x, y, 0, 0, new Drawing.Size(w, h), Drawing.CopyPixelOperation.SourceCopy);
        var data = bmp.LockBits(new Drawing.Rectangle(0, 0, w, h), Imaging.ImageLockMode.ReadOnly, Imaging.PixelFormat.Format32bppArgb);
        try
        {
            using var bgra = OpenCvSharp.Mat.FromPixelData(h, w, MatType.CV_8UC4, data.Scan0, data.Stride);
            var bgr = new OpenCvSharp.Mat();
            Cv2.CvtColor(bgra, bgr, ColorConversionCodes.BGRA2BGR);
            return bgr;
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }

    /// Screenshot as an OpenCV BGR image.
    public static (Mat bgr, int left, int top) GrabMat()
    {
        var (bmp, x, y) = GrabBitmap();
        using (bmp)
        {
            var rect = new Drawing.Rectangle(0, 0, bmp.Width, bmp.Height);
            var data = bmp.LockBits(rect, Imaging.ImageLockMode.ReadOnly, Imaging.PixelFormat.Format32bppArgb);
            try
            {
                using var bgra = OpenCvSharp.Mat.FromPixelData(bmp.Height, bmp.Width, MatType.CV_8UC4, data.Scan0, data.Stride);
                var bgr = new OpenCvSharp.Mat();
                Cv2.CvtColor(bgra, bgr, ColorConversionCodes.BGRA2BGR);
                return (bgr, x, y);
            }
            finally
            {
                bmp.UnlockBits(data);
            }
        }
    }

    public static (string title, int left, int top)? Foreground()
    {
        var h = GetForegroundWindow();
        if (h == IntPtr.Zero) return null;
        GetWindowRect(h, out var r);
        return (WindowTitle(h), r.Left, r.Top);
    }

    /// First visible window whose title contains `text` (not case sensitive).
    public static (string title, int left, int top)? FindWindow(string text)
    {
        (string, int, int)? found = null;
        var want = text.ToLowerInvariant();
        EnumWindows((h, _) =>
        {
            if (!IsWindowVisible(h)) return true;
            var t = WindowTitle(h);
            if (t.Length == 0 || !t.ToLowerInvariant().Contains(want)) return true;
            GetWindowRect(h, out var r);
            found = (t, r.Left, r.Top);
            return false;
        }, IntPtr.Zero);
        return found;
    }

    /// Bring the first window whose title contains `text` to the front (restoring it if it is minimised).
    /// TinyRec's own windows never count. False if there is no such window.
    public static bool FocusWindow(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        IntPtr target = IntPtr.Zero;
        var want = text.Trim().ToLowerInvariant();
        EnumWindows((h, _) =>
        {
            if (!IsWindowVisible(h) || IsCloaked(h)) return true;
            var t = WindowTitle(h);
            if (t.Length == 0 || !t.ToLowerInvariant().Contains(want) || t.Contains("TinyRec", StringComparison.OrdinalIgnoreCase)) return true;
            target = h;
            return false;
        }, IntPtr.Zero);
        if (target == IntPtr.Zero) return false;
        if (IsIconic(target)) ShowWindow(target, 9);                 // SW_RESTORE
        if (GetForegroundWindow() == target) return true;
        SetForegroundWindow(target);
        if (GetForegroundWindow() != target)
        {
            // Windows only lets the app the user last touched change the front window. A tap of Alt counts as a touch.
            var input = new WinInput();
            input.Key("alt_l", true);
            input.Key("alt_l", false);
            SetForegroundWindow(target);
        }
        BringWindowToTop(target);
        return true;
    }

    /// Titles of the visible, un-minimised windows (for the "pick a window" menu).
    public static List<string> WindowTitles()
    {
        var list = new List<string>();
        EnumWindows((h, _) =>
        {
            if (!IsWindowVisible(h) || IsIconic(h) || IsCloaked(h)) return true;
            var t = WindowTitle(h);
            GetWindowRect(h, out var r);
            if (t.Length > 0 && r.Right - r.Left >= 120 && r.Bottom - r.Top >= 80 && t != "Program Manager") list.Add(t);
            return true;
        }, IntPtr.Zero);
        return list;
    }

    /// Screen rectangle (x, y, w, h) of the first visible, un-minimised window whose title contains `text` - or null.
    public static (int x, int y, int w, int h)? FindWindowRect(string text, string notContaining = "TinyRec")
    {
        (int, int, int, int)? found = null;
        var want = text.ToLowerInvariant();
        EnumWindows((h, _) =>
        {
            if (!IsWindowVisible(h) || IsIconic(h) || IsCloaked(h)) return true;
            var t = WindowTitle(h);
            if (t.Length == 0 || !t.ToLowerInvariant().Contains(want)) return true;
            if (notContaining != null && t.Contains(notContaining, StringComparison.OrdinalIgnoreCase)) return true;
            GetWindowRect(h, out var r);
            if (r.Right - r.Left < 40 || r.Bottom - r.Top < 40) return true;
            found = (r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
            return false;
        }, IntPtr.Zero);
        return found;
    }
}

public sealed class ImageFinder : IImageFinder
{
    readonly string dir;
    readonly ConcurrentDictionary<string, (DateTime mtime, OpenCvSharp.Mat img)> cache = new();

    public ImageFinder(string imagesDir) => dir = imagesDir;

    OpenCvSharp.Mat Template(string name)
    {
        var p = Path.Combine(dir, name);
        if (!File.Exists(p)) throw new FileNotFoundException($"image '{name}' isn't in the image library");
        var mt = File.GetLastWriteTimeUtc(p);
        if (cache.TryGetValue(name, out var hit) && hit.mtime == mt) return hit.img;
        var img = Cv2.ImDecode(File.ReadAllBytes(p), ImreadModes.Color);
        if (img == null || img.Empty()) throw new InvalidDataException($"couldn't read image '{name}'");
        cache[name] = (mt, img);
        return img;
    }

    /// Where to look when a step doesn't say (null = the whole desktop). Set by the app from Settings.
    public Func<(int x, int y, int w, int h)?> Area;

    public TinyRec.Core.Match Locate(string name, double conf, (int x, int y, int w, int h)? region)
    {
        var tpl = Template(name);
        try { region ??= Area?.Invoke(); } catch { }
        var (vx, vy, vw, vh) = VirtualScreen();
        int x0 = vx, y0 = vy, w = vw, h = vh;
        if (region is { } r)                                  // clip the search area to what's actually on a screen
        {
            x0 = Math.Clamp(r.x, vx, vx + vw);
            y0 = Math.Clamp(r.y, vy, vy + vh);
            w = Math.Clamp(r.x + r.w, vx, vx + vw) - x0;
            h = Math.Clamp(r.y + r.h, vy, vy + vh) - y0;
        }
        if (w < tpl.Cols || h < tpl.Rows) return new TinyRec.Core.Match();
        using var area = ScreenGrab.GrabMat(x0, y0, w, h);
        using var res = new OpenCvSharp.Mat();
        Cv2.MatchTemplate(area, tpl, res, TemplateMatchModes.CCoeffNormed);
        Cv2.MinMaxLoc(res, out _, out double best, out _, out OpenCvSharp.Point loc);
        if (double.IsNaN(best) || double.IsInfinity(best)) return new TinyRec.Core.Match();
        return new TinyRec.Core.Match
        {
            Found = best >= conf,
            X = x0 + loc.X + tpl.Cols / 2,
            Y = y0 + loc.Y + tpl.Rows / 2,
            Score = best,
        };
    }

    public void Forget(string name) => cache.TryRemove(name, out _);
}
