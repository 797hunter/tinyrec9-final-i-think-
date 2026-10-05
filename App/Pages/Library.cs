// Library.cs - every saved recording, with search, sort and per-recording actions.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using TinyRec.Core;
using TinyRec.Ui;

namespace TinyRec;

public partial class MainWindow
{
    TextBox libSearch;
    Segmented libSort;
    readonly StackPanel libList = new();
    TextBlock libCount;
    Button libFavBtn;
    readonly Dictionary<string, (DateTime mtime, Files.Summary sum)> libCache = new();
    readonly Dictionary<string, (Border row, string fp)> libRows = new();
    string libSig;
    DispatcherTimer libDebounce;

    sealed class LibItem
    {
        public string Path, File, Title;
        public DateTime MTime;
        public Files.Summary Sum;
    }

    FrameworkElement PageLibrary()
    {
        var g = K.Grid("*", "auto,*,auto");
        var bar = K.Grid("auto,auto,auto,*,auto,auto,auto");
        libSearch = K.Field("", 210);
        libSearch.Tip("Search by name or file name");
        libSearch.Padding = new Thickness(30, 0, 8, 0);            // room for the magnifier icon drawn on top
        libSearch.TextChanged += (_, _) =>
        {
            libDebounce ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(160) };
            libDebounce.Stop();
            libDebounce.Tick -= LibSearchTick;
            libDebounce.Tick += LibSearchTick;
            libDebounce.Start();
        };
        var sortNames = new Dictionary<string, string> { ["newest"] = "Newest", ["oldest"] = "Oldest", ["name"] = "Name", ["longest"] = "Longest" };
        libSort = new Segmented(sortNames.Values, sortNames.TryGetValue(cfg.LibrarySort, out var s) ? s : "Newest", v =>
        {
            cfg.LibrarySort = sortNames.First(kv => kv.Value == v).Key;
            RefreshLibrary(true);
        });
        var search = new Grid();
        search.Children.Add(libSearch);
        var mag = K.Icon("search", 15, "Muted").M(11, 0, 0, 0).Align(HorizontalAlignment.Left);
        mag.IsHitTestVisible = false;
        search.Children.Add(mag);
        var hint = K.T("Search recordings", 13, "Faint").M(33, 0, 0, 0).Align(HorizontalAlignment.Left);
        hint.VerticalAlignment = VerticalAlignment.Center;
        hint.IsHitTestVisible = false;
        libSearch.TextChanged += (_, _) => hint.Visibility = libSearch.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        search.Children.Add(hint);
        libFavBtn = K.Btn("Favorites", () =>
        {
            cfg.LibraryFavOnly = !cfg.LibraryFavOnly;
            LibFavButton();
            RefreshLibrary(true);
        }, "secondary", "star_o");
        libFavBtn.Tip("Show only the recordings you starred");
        LibFavButton();
        bar.Add(search.At(0, 0),
                libSort.M(12, 0, 0, 0).At(0, 1),
                libFavBtn.M(8, 0, 0, 0).At(0, 2),
                K.Btn("Import", OpenDialog, "secondary", "import").M(0, 0, 8, 0).At(0, 4),
                K.Btn("Folder", () => OpenFolder(RecDir), "secondary", "folder").M(0, 0, 8, 0).At(0, 5),
                K.Btn("New", NewRecording, "primary", "plus").At(0, 6));
        g.Add(bar.M(0, 0, 0, 12).At(0, 0));
        var sv = new ScrollViewer { Content = libList, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false };
        g.Add(sv.At(1, 0));
        libCount = K.T("", 12, "Muted");
        g.Add(libCount.M(0, 8, 0, 0).At(2, 0));
        return g;
    }

    bool IsFav(string file) => cfg.Favorites.Contains(file, StringComparer.OrdinalIgnoreCase);

    void LibFavButton()
    {
        K.SetBtnContent(libFavBtn, "Favorites", cfg.LibraryFavOnly ? "star" : "star_o");
        libFavBtn.SetResourceReference(Control.ForegroundProperty, cfg.LibraryFavOnly ? "Amber" : "Text");
        libFavBtn.SetResourceReference(Control.BorderBrushProperty, cfg.LibraryFavOnly ? "Amber" : "Line");
    }

    void LibToggleFav(LibItem it)
    {
        if (IsFav(it.File)) cfg.Favorites.RemoveAll(f => string.Equals(f, it.File, StringComparison.OrdinalIgnoreCase));
        else cfg.Favorites.Add(it.File);
        RefreshLibrary(true);
    }

    void LibSearchTick(object sender, EventArgs e)
    {
        libDebounce.Stop();
        RefreshLibrary(true);
    }

    List<LibItem> LibraryItems()
    {
        var items = new List<LibItem>();
        IEnumerable<string> files;
        try { files = Directory.EnumerateFiles(RecDir, "*.txt"); }
        catch (IOException) { return items; }
        foreach (var path in files)
        {
            DateTime mt;
            try { mt = File.GetLastWriteTimeUtc(path); } catch { continue; }
            if (!libCache.TryGetValue(path, out var hit) || hit.mtime != mt)
                libCache[path] = hit = (mt, Files.Scan(path));
            var name = System.IO.Path.GetFileName(path);
            items.Add(new LibItem
            {
                Path = path, File = name, MTime = mt, Sum = hit.sum,
                Title = string.IsNullOrEmpty(hit.sum.Name) ? System.IO.Path.GetFileNameWithoutExtension(name) : hit.sum.Name,
            });
        }
        return items;
    }

    void RefreshLibrary(bool force = false)
    {
        if (libSearch == null) return;
        var all = LibraryItems();
        var q = libSearch.Text.Trim().ToLowerInvariant();
        var items = all.Where(i => q.Length == 0 || i.Title.ToLowerInvariant().Contains(q) || i.File.ToLowerInvariant().Contains(q)).ToList();
        if (cfg.LibraryFavOnly) items = items.Where(i => IsFav(i.File)).ToList();
        items = cfg.LibrarySort switch
        {
            "name" => items.OrderBy(i => i.Title, StringComparer.OrdinalIgnoreCase).ToList(),
            "longest" => items.OrderByDescending(i => i.Sum.Duration).ToList(),
            "oldest" => items.OrderBy(i => i.MTime).ToList(),
            _ => items.OrderByDescending(i => i.MTime).ToList(),
        };
        items = items.OrderByDescending(i => IsFav(i.File)).ToList();          // stable: starred first, same order inside each group
        var sig = string.Join("|", items.Select(i => i.Path + i.MTime.Ticks + IsFav(i.File))) + "#" + recPath + "#" + q;
        if (sig == libSig && !force) return;
        libSig = sig;

        // reuse rows that haven't changed; build only new ones
        var live = all.ToDictionary(i => i.Path);
        foreach (var (path, (row, fp)) in libRows.ToList())
            if (!live.TryGetValue(path, out var it) || fp != LibFp(it)) libRows.Remove(path);
        libList.Children.Clear();
        if (items.Count == 0)
        {
            var empty = K.V(K.Icon("library", 40, "Faint").Align(HorizontalAlignment.Center),
                            K.T(q.Length > 0 ? "No matches" : cfg.LibraryFavOnly && all.Count > 0 ? "No favorites yet" : "No recordings yet", 16, "Text", FontWeights.SemiBold).Align(HorizontalAlignment.Center).M(0, 12, 0, 2),
                            K.T(q.Length > 0 ? "" : cfg.LibraryFavOnly && all.Count > 0 ? "Click the star on a recording to keep it at the top of the list." : $"Press {Hint("record_toggle")} anywhere to record your first one.", 13, "Muted").Align(HorizontalAlignment.Center));
            libList.Children.Add(empty.M(0, 80, 0, 0));
        }
        foreach (var it in items)
        {
            if (!libRows.TryGetValue(it.Path, out var hit)) libRows[it.Path] = hit = (LibRow(it), LibFp(it));
            libList.Children.Add(hit.row);
        }
        libCount.Text = q.Length > 0 || cfg.LibraryFavOnly ? $"{items.Count} of {all.Count} recording{(all.Count == 1 ? "" : "s")}" : $"{all.Count} recording{(all.Count == 1 ? "" : "s")}  ·  stored in {RecDir}";
    }

    string LibFp(LibItem it) => $"{it.MTime.Ticks}|{SameFile(recPath, it.Path)}|{it.Sum.Ok}|{IsFav(it.File)}";

    Border LibRow(LibItem it)
    {
        bool loaded = SameFile(recPath, it.Path);
        bool fav = IsFav(it.File);
        var g = K.Grid("auto,*,auto,auto,auto,auto", "auto,auto");
        var ic = K.Box(K.Icon(loaded ? "play" : "library", 16, loaded ? "Accent" : "Muted").Align(HorizontalAlignment.Center), loaded ? "AccentSoft" : "Card2", 10);
        ic.Width = ic.Height = 40;
        g.Add(ic.M(14, 12, 12, 12).At(0, 0, 2));
        var title = K.H(K.T(it.Title, 14, "Text", FontWeights.SemiBold));
        if (loaded) title.Children.Add(K.Chip("LOADED", "Accent", "AccentSoft", 10, false).M(8, 0, 0, 0));
        if (!it.Sum.Ok) title.Children.Add(K.Chip("HAS ERRORS", "Red", "RedSoft", 10, false).M(8, 0, 0, 0));
        g.Add(title.M(0, 12, 0, 0).At(0, 1));
        var bits = new List<string> { $"{it.Sum.Actions:N0} actions", Fmt.Dur(it.Sum.Duration), it.MTime.ToLocalTime().ToString("MMM dd, HH:mm") };
        if (it.Sum.Images > 0) bits.Add($"{it.Sum.Images} image step{(it.Sum.Images == 1 ? "" : "s")}");
        g.Add(K.T(string.Join("  ·  ", bits) + $"   ({it.File})", 12, "Muted").M(0, 2, 0, 12).At(1, 1));
        g.Add(K.IconBtn(fav ? "star" : "star_o", () => LibToggleFav(it), fav ? "Remove from favorites" : "Add to favorites (keeps it at the top)", 34, fav ? "Amber" : "Muted").M(3, 0, 3, 0).At(0, 2, 2));
        g.Add(K.IconBtn("play", () => LibPlay(it.Path), "Open and play", 34, "Accent", "AccentSoft").M(3, 0, 3, 0).At(0, 3, 2));
        g.Add(K.Btn("Open", () => LibOpen(it.Path), "secondary", null, 34).M(3, 0, 3, 0).At(0, 4, 2));
        Button more = null;
        more = K.IconBtn("more", () => K.Menu(
            ("Open", () => LibOpen(it.Path)), (fav ? "Remove from favorites" : "Add to favorites", () => LibToggleFav(it)), ("Rename…", () => LibRename(it)), ("Duplicate", () => LibDuplicate(it)), ("-", null),
            ("Export as .txt…", () => LibExport(it, "txt")), ("Export bundle .zip…", () => LibExport(it, "zip")),
            ("Show in folder", () => OpenFolder(RecDir)), ("-", null), ("Delete", () => LibDelete(it))).Popup(more), "More", 34);
        g.Add(more.M(3, 0, 12, 0).At(0, 5, 2));
        var card = K.Card(g, 0, 12);
        card.Margin = new Thickness(0, 0, 6, 8);
        if (loaded) card.SetResourceReference(Border.BorderBrushProperty, "Accent");
        card.MouseEnter += (_, _) => card.SetResourceReference(Border.BorderBrushProperty, "Accent");
        card.MouseLeave += (_, _) => card.SetResourceReference(Border.BorderBrushProperty, loaded ? "Accent" : "Line");
        card.MouseLeftButtonDown += (_, e) => { if (e.ClickCount == 2) LibOpen(it.Path); };
        return card;
    }

    void LibOpen(string path)
    {
        if (state != "idle" || !ConfirmDiscard()) return;
        if (OpenPath(path)) ShowPage("home");
    }

    void LibPlay(string path)
    {
        if (state != "idle" || !ConfirmDiscard()) return;
        if (!SameFile(recPath, path) && !OpenPath(path)) return;
        TogglePlay();
    }

    void LibRename(LibItem it)
    {
        var name = Dialog.AskString(TopWindow, "Rename recording", "New name:", it.Title, t => string.IsNullOrWhiteSpace(t) ? "Name can't be empty." : null, "Rename");
        if (name == null) return;
        name = name.Trim();
        try
        {
            var r = Files.LoadText(it.Path);
            r.SetMeta("name", name);
            var dest = Files.UniquePath(RecDir, Files.Slug(name) + ".txt", it.Path);
            Files.SaveRecording(r, dest);
            if (!SameFile(dest, it.Path))
            {
                File.Delete(it.Path);
                if (File.Exists(it.Path + ".bak")) File.Delete(it.Path + ".bak");
                var newFile = System.IO.Path.GetFileName(dest);
                if (IsFav(it.File))
                {
                    cfg.Favorites.RemoveAll(f => string.Equals(f, it.File, StringComparison.OrdinalIgnoreCase));
                    cfg.Favorites.Add(newFile);
                }
                SeqRecordingRenamed(it.File, newFile);
            }
            if (SameFile(recPath, it.Path))
            {
                recPath = dest;
                loadingMeta = true;
                rec.SetMeta("name", name);
                HomeMeta();
                loadingMeta = false;
                cfg.LastRecording = dest;
                RefreshInfo();
            }
        }
        catch (Exception e)
        {
            Dialog.Info(TopWindow, "Rename failed", e.Message, "error");
            return;
        }
        RefreshLibrary(true);
        Toast($"Renamed to “{name}”", "ok");
    }

    void LibDuplicate(LibItem it)
    {
        try
        {
            var r = Files.LoadText(it.Path);
            r.SetMeta("name", it.Title + " (copy)");
            Files.SaveRecording(r, Files.UniquePath(RecDir, System.IO.Path.GetFileNameWithoutExtension(it.File) + "_copy.txt"));
        }
        catch (Exception e)
        {
            Dialog.Info(TopWindow, "Duplicate failed", e.Message, "error");
            return;
        }
        RefreshLibrary(true);
        Toast("Duplicated", "ok");
    }

    void LibExport(LibItem it, string kind)
    {
        try { Export(Files.LoadText(it.Path), kind); }
        catch (Exception e) { Dialog.Info(TopWindow, "Can't export", e.Message, "error"); }
    }

    void LibDelete(LibItem it)
    {
        if (!Dialog.Confirm(TopWindow, "Delete recording?", $"“{it.Title}” will be moved to data\\deleted (you can get it back from there).", "Delete", true)) return;
        Directory.CreateDirectory(DelDir);
        File.Move(it.Path, Files.UniquePath(DelDir, it.File));
        try { if (File.Exists(it.Path + ".bak")) File.Move(it.Path + ".bak", Files.UniquePath(DelDir, it.File + ".bak")); } catch (IOException) { }
        cfg.Favorites.RemoveAll(f => string.Equals(f, it.File, StringComparison.OrdinalIgnoreCase));
        if (SameFile(recPath, it.Path))
        {
            recPath = null;
            dirty = true;
            RefreshInfo();
        }
        RefreshLibrary(true);
        Toast("Moved to data\\deleted", "ok");
    }
}
