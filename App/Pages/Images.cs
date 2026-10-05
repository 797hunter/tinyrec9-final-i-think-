// Images.cs - the image library for image recognition: snip, import, test on screen, use in a recording.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TinyRec.Core;
using TinyRec.Overlays;
using TinyRec.Ui;

namespace TinyRec;

public partial class MainWindow
{
    readonly WrapPanel imgGrid = new();
    readonly Dictionary<string, Button> imgButtons = new();
    readonly Dictionary<string, (DateTime mtime, BitmapSource src, int w, int h)> thumbCache = new();
    string imgSel, imgShown, imgSig;
    TextBlock imgCount, imgCap, imgResult, imgUsed, imgNone, confLabel;
    Border imgPrev;
    StackPanel imgBody;
    Slider confSlider;
    Dictionary<string, int> usage;
    string usageSig;

    FrameworkElement PageImages()
    {
        var g = K.Grid("*,350", "auto,*");
        imgCount = K.T("", 12, "Muted");
        var bar = K.Grid("auto,auto,auto,*,auto");
        bar.Add(K.Btn("Snip from screen", ImgSnip, "primary", "snip").At(0, 0), K.Btn("Import file", ImgImport, "secondary", "import").M(8, 0, 8, 0).At(0, 1),
                K.Btn("Open folder", () => OpenFolder(ImgDir), "secondary", "folder").At(0, 2), imgCount.At(0, 4));
        g.Add(bar.M(0, 0, 0, 12).At(0, 0, 1, 2));
        g.Add(new ScrollViewer { Content = imgGrid, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false }.At(1, 0));

        var det = new StackPanel();
        det.Children.Add(K.CardTitle("Details", "image"));
        imgNone = K.T("Select an image.", 13, "Muted").M(20, 0, 20, 16);
        det.Children.Add(imgNone);
        imgBody = new StackPanel { Margin = new Thickness(18, 0, 18, 18) };
        imgPrev = new Border { Height = 130, CornerRadius = new CornerRadius(8) };
        imgPrev.SetResourceReference(Border.BackgroundProperty, "Card2");
        imgCap = K.T("", 11, "Muted");
        confSlider = K.Slide(0.5, 1.0, 0.9, v => confLabel.Text = v.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), 230);
        confLabel = K.T("0.90", 13, "Text", FontWeights.SemiBold);
        imgResult = K.T("", 12, "Muted", null, true);
        imgUsed = K.T("", 12, "Faint");
        var rd = K.Grid("*,*");
        rd.Add(K.Btn("Rename", ImgRename, "secondary", "rename").M(0, 0, 4, 0).At(0, 0), K.Btn("Delete", ImgDelete, "secondary", "trash").M(4, 0, 0, 0).At(0, 1));
        foreach (var el in new FrameworkElement[]
                 {
                     imgPrev, imgCap.M(0, 4, 0, 0), K.T("Test it on your screen", 13, "Text", FontWeights.SemiBold).M(0, 16, 0, 2),
                     K.T("Strictness - higher only accepts a closer match. 0.85–0.95 is usual.", 12, "Muted", null, true),
                     K.H(confSlider, confLabel.M(10, 0, 0, 0)).M(0, 8, 0, 6),
                     K.Btn("Find it on screen now", ImgTest, "soft", "search").Align(HorizontalAlignment.Left),
                     imgResult.M(0, 6, 0, 0), K.Divider().M(0, 14, 0, 14),
                     K.Btn("Add to recording", () => { if (imgSel != null) { ShowPage("editor"); EdImage(imgSel); } }, "primary", "plus"),
                     rd.M(0, 8, 0, 8), imgUsed,
                 })
            imgBody.Children.Add(el);
        det.Children.Add(imgBody);
        g.Add(K.Card(new ScrollViewer { Content = det, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false }).M(12, 0, 0, 0).At(1, 1));
        return g;
    }

    List<string> ImageNames()
    {
        try
        {
            return Directory.EnumerateFiles(ImgDir).Select(Path.GetFileName).Where(n => Format.ImageName.IsMatch(n))
                            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (IOException) { return new List<string>(); }
    }

    /// A ready-to-show picture of a library image (small snips are enlarged crisply), plus a caption.
    (Image img, string cap) ThumbImage(string name, double maxW, double maxH)
    {
        var path = Path.Combine(ImgDir, name);
        if (!File.Exists(path)) return (null, $"⚠ '{name}' isn't in the image library");
        var mt = File.GetLastWriteTimeUtc(path);
        if (!thumbCache.TryGetValue(name, out var hit) || hit.mtime != mt)
        {
            try
            {
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;        // don't keep the file locked
                bi.UriSource = new Uri(path);
                bi.EndInit();
                bi.Freeze();
                thumbCache[name] = hit = (mt, bi, bi.PixelWidth, bi.PixelHeight);
            }
            catch { return (null, $"⚠ couldn't read '{name}'"); }
        }
        double f = Math.Min(maxW / hit.w, maxH / hit.h);
        bool enlarge = f > 1;
        f = Math.Min(f, 4);
        var img = new Image { Source = hit.src, Width = hit.w * f, Height = hit.h * f, Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(img, enlarge ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality);
        return (img, $"{name} · {hit.w}×{hit.h}px");
    }

    void RefreshImages(bool force = false)
    {
        if (imgCount == null) return;
        var names = ImageNames();
        var sig = string.Join("|", names.Select(n => n + File.GetLastWriteTimeUtc(Path.Combine(ImgDir, n)).Ticks));
        if (imgSel == null || !names.Contains(imgSel)) imgSel = names.FirstOrDefault();
        if (sig != imgSig || force)
        {
            imgSig = sig;
            imgGrid.Children.Clear();
            imgButtons.Clear();
            imgShown = null;
            imgCount.Text = $"{names.Count} image{(names.Count == 1 ? "" : "s")}";
            if (names.Count == 0)
            {
                imgGrid.Children.Add(K.V(K.Icon("image", 40, "Faint").Align(HorizontalAlignment.Center),
                                         K.T("No images yet", 16, "Text", FontWeights.SemiBold).Align(HorizontalAlignment.Center).M(0, 12, 0, 2),
                                         K.T("Snip a button or icon from your screen to click it by picture.", 13, "Muted").Align(HorizontalAlignment.Center))
                                      .M(40, 80, 0, 0));
            }
            foreach (var n in names)
            {
                var (img, _) = ThumbImage(n, 170, 80);
                var content = K.V(new Border { Height = 84, Child = img }, K.T(n, 12).Align(HorizontalAlignment.Center).M(0, 6, 0, 0));
                var b = new Button { Width = 196, Height = 140, Content = content, Padding = new Thickness(8) };
                b.SetResourceReference(StyleProperty, "Btn");
                b.SetResourceReference(BackgroundProperty, "Card");
                Att.SetRadius(b, new CornerRadius(12));
                var name = n;
                b.Click += (_, _) => SelectImage(name);
                imgButtons[n] = b;
                imgGrid.Children.Add(b.M(0, 0, 10, 10));
            }
        }
        SelectImage(imgSel);
    }

    void SelectImage(string name)
    {
        foreach (var n in new[] { imgShown, name }.Where(x => x != null).Distinct())
        {
            if (!imgButtons.TryGetValue(n, out var b)) continue;
            bool on = n == name;
            b.SetResourceReference(BackgroundProperty, on ? "AccentSoft" : "Card");
            b.SetResourceReference(BorderBrushProperty, on ? "Accent" : "Line");
            b.BorderThickness = new Thickness(on ? 2 : 1);
        }
        imgShown = imgSel = name;
        imgNone.Visibility = name == null ? Visibility.Visible : Visibility.Collapsed;
        imgBody.Visibility = name == null ? Visibility.Collapsed : Visibility.Visible;
        if (name == null) return;
        var (img, cap) = ThumbImage(name, 310, 130);
        imgPrev.Child = img;
        imgCap.Text = cap;
        imgResult.Text = "";
        var used = ImageUsage().TryGetValue(name, out var u) ? u : 0;
        imgUsed.Text = $"Used in {used} saved recording{(used == 1 ? "" : "s")}";
    }

    Dictionary<string, int> ImageUsage()
    {
        var items = LibraryItems();
        var sig = string.Join("|", items.Select(i => i.Path + i.MTime.Ticks));
        if (usage != null && sig == usageSig) return usage;
        usage = new Dictionary<string, int>();
        foreach (var it in items.Where(i => i.Sum.Images > 0))
            foreach (var n in Files.ImagesUsed(it.Path))
                usage[n] = usage.TryGetValue(n, out var c) ? c + 1 : 1;
        usageSig = sig;
        return usage;
    }

    string AskImageName(string def)
    {
        var name = Dialog.AskString(TopWindow, "Name this image", "Letters, numbers, _ and -", def, t =>
        {
            var n = Regex.Replace(t.Trim(), @"[^A-Za-z0-9_\-]", "_");
            return n.Length == 0 ? "Enter a name." : null;
        }, "Save");
        if (name == null) return null;
        name = Regex.Replace(name.Trim(), @"[^A-Za-z0-9_\-]", "_");
        if (!name.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) name += ".png";
        if (File.Exists(Path.Combine(ImgDir, name)) && !Dialog.Confirm(TopWindow, "Replace image?", $"“{name}” already exists.", "Replace", true)) return null;
        return name;
    }

    void ImgSnip()
    {
        var bmp = Hidden(() => Snipper.Snip());
        if (bmp == null) return;
        using (bmp)
        {
            int n = 1;
            while (File.Exists(Path.Combine(ImgDir, $"image_{n}.png"))) n++;
            var name = AskImageName($"image_{n}");
            if (name == null) return;
            bmp.Save(Path.Combine(ImgDir, name), System.Drawing.Imaging.ImageFormat.Png);
            finder?.Forget(name);
            imgSel = name;
            RefreshImages();
            Toast($"Saved {name}", "ok");
        }
    }

    void ImgImport()
    {
        var d = new Microsoft.Win32.OpenFileDialog { Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp" };
        if (d.ShowDialog(this) != true) return;
        System.Drawing.Bitmap bmp;
        try { bmp = new System.Drawing.Bitmap(d.FileName); }
        catch (Exception e)
        {
            Dialog.Info(TopWindow, "Couldn't read that image", e.Message, "error");
            return;
        }
        using (bmp)
        {
            var name = AskImageName(Regex.Replace(Path.GetFileNameWithoutExtension(d.FileName), @"[^A-Za-z0-9_\-]", "_"));
            if (name == null) return;
            bmp.Save(Path.Combine(ImgDir, name), System.Drawing.Imaging.ImageFormat.Png);
            finder?.Forget(name);
            imgSel = name;
            RefreshImages();
        }
    }

    void ImgRename()
    {
        var old = imgSel;
        if (old == null) return;
        var nw = AskImageName(Path.GetFileNameWithoutExtension(old));
        if (nw == null || nw == old) return;
        File.Move(Path.Combine(ImgDir, old), Path.Combine(ImgDir, nw), true);
        finder?.Forget(old);
        finder?.Forget(nw);
        int changed = 0;
        foreach (var e in rec.Events.Where(e => e.IsImage && e.Args[0] == old)) { e.Args[0] = nw; changed++; }
        if (changed > 0) Edited();
        imgSel = nw;
        RefreshImages();
        Toast($"Renamed to {nw}" + (changed > 0 ? $" and updated {changed} step(s) in the current recording" : ""), "ok");
    }

    void ImgDelete()
    {
        var n = imgSel;
        if (n == null || !Dialog.Confirm(TopWindow, "Delete image?", $"“{n}” will be moved to data\\deleted. Recordings that use it will skip or stop at that step.", "Delete", true)) return;
        Directory.CreateDirectory(DelDir);
        File.Move(Path.Combine(ImgDir, n), Files.UniquePath(DelDir, n));
        finder?.Forget(n);
        imgSel = null;
        RefreshImages();
    }

    void ImgTest()
    {
        var n = imgSel;
        if (n == null) return;
        if (finder == null)
        {
            Dialog.Info(TopWindow, "Image recognition is off", "OpenCV couldn't start on this PC.", "error");
            return;
        }
        double conf = Math.Round(confSlider.Value, 2);
        TinyRec.Core.Match m;
        try { m = Hidden(() => finder.Locate(n, conf, null)); }
        catch (Exception e)
        {
            Dialog.Info(TopWindow, "Test failed", e.Message, "error");
            return;
        }
        if (m.Found)
        {
            draw.Flash(m.X, m.Y, $"{n}  {m.Score:0.00}");
            imgResult.Text = $"✓ Found at ({m.X}, {m.Y}) - score {m.Score:0.00}";
            imgResult.SetResourceReference(TextBlock.ForegroundProperty, "Green");
        }
        else
        {
            imgResult.Text = $"✗ Not found - best score {m.Score:0.00}, needs {conf:0.00}. Lower the strictness or re-snip it the way it looks right now.";
            imgResult.SetResourceReference(TextBlock.ForegroundProperty, "Red");
        }
    }
}
