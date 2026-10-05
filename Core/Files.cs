// Files.cs - saving that can't corrupt, plus .zip bundles (recording + the images it uses).
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace TinyRec.Core;

public static class Files
{
    static readonly UTF8Encoding Utf8NoBom = new(false);

    /// Write to a temp file, flush it to disk, then swap it in. A crash mid-save can never leave a
    /// half-written file. The previous version is kept as <path>.bak.
    public static void AtomicWrite(string path, byte[] data, bool keepBackup = true)
    {
        var full = Path.GetFullPath(path);
        var folder = Path.GetDirectoryName(full)!;
        Directory.CreateDirectory(folder);
        var tmp = Path.Combine(folder, $".tmp_{Guid.NewGuid():N}.part");
        try
        {
            using (var fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                fs.Write(data, 0, data.Length);
                fs.Flush(true);
            }
            if (keepBackup && File.Exists(full)) File.Copy(full, full + ".bak", true);
            File.Move(tmp, full, true);
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    public static void SaveRecording(Recording rec, string path)
    {
        AtomicWrite(path, Utf8NoBom.GetBytes(rec.ToText()));
        var again = LoadText(path);                      // read it back and make sure it's identical
        if (again.Events.Count != rec.Events.Count || again.Events.Where((e, i) => !e.SameAs(rec.Events[i])).Any())
            throw new IOException("Saved file did not read back identically - the previous version is in .bak");
    }

    public static Recording LoadText(string path) => Recording.FromText(File.ReadAllText(path, Encoding.UTF8));

    public const long MaxZipTotal = 100L * 1024 * 1024;

    /// recording.txt + images/<each image the recording uses>. Returns the images that weren't in the library.
    public static List<string> ExportBundle(Recording rec, string imagesDir, string zipPath)
    {
        var missing = new List<string>();
        using var ms = new MemoryStream();
        using (var z = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            using (var w = new StreamWriter(z.CreateEntry("recording.txt", CompressionLevel.Optimal).Open(), Utf8NoBom))
                w.Write(rec.ToText());
            foreach (var name in rec.ImageNames())
            {
                var p = Path.Combine(imagesDir, name);
                if (File.Exists(p)) z.CreateEntryFromFile(p, "images/" + name, CompressionLevel.Optimal);
                else missing.Add(name);
            }
        }
        AtomicWrite(zipPath, ms.ToArray(), keepBackup: false);
        using (var check = ZipFile.OpenRead(zipPath))          // verify it opens and every entry reads
            foreach (var e in check.Entries)
                using (var s = e.Open()) s.CopyTo(Stream.Null);
        return missing;
    }

    /// Read a bundle. Only recording.txt and images/<safe name> are accepted and nothing else is ever
    /// written. An image that already exists with different content is imported under a new name.
    public static (Recording rec, List<string> notes) ImportBundle(string zipPath, string imagesDir)
    {
        var notes = new List<string>();
        using var z = ZipFile.OpenRead(zipPath);
        if (z.Entries.Sum(e => e.Length) > MaxZipTotal) throw new FormatError("bundle is unreasonably large");
        var recEntry = z.GetEntry("recording.txt") ?? throw new FormatError("bundle has no recording.txt");
        Recording rec;
        using (var r = new StreamReader(recEntry.Open(), Encoding.UTF8)) rec = Recording.FromText(r.ReadToEnd());
        var rename = new Dictionary<string, string>();
        Directory.CreateDirectory(imagesDir);
        foreach (var e in z.Entries)
        {
            var fn = e.FullName;
            if (!fn.StartsWith("images/") || fn.EndsWith("/")) continue;
            var name = fn["images/".Length..];
            if (name.Contains('/') || name.Contains('\\') || !Format.ImageName.IsMatch(name))
            {
                notes.Add($"skipped unsafe file name: {fn}");
                continue;
            }
            byte[] data;
            using (var s = e.Open())
            using (var m = new MemoryStream()) { s.CopyTo(m); data = m.ToArray(); }
            var dest = Path.Combine(imagesDir, name);
            if (File.Exists(dest))
            {
                if (File.ReadAllBytes(dest).AsSpan().SequenceEqual(data)) continue;   // identical, already have it
                var stem = Path.GetFileNameWithoutExtension(name);
                var ext = Path.GetExtension(name);
                var hash = Convert.ToHexString(SHA1.HashData(data))[..6].ToLowerInvariant();
                var newName = $"{stem}_{hash}{ext}";
                rename[name] = newName;
                notes.Add($"image '{name}' already exists with different content - imported as '{newName}'");
                dest = Path.Combine(imagesDir, newName);
            }
            AtomicWrite(dest, data, keepBackup: false);
        }
        foreach (var ev in rec.Events)
        {
            if (!ev.IsImage) continue;
            if (rename.TryGetValue(ev.Args[0], out var nn)) ev.Args[0] = nn;
            for (int i = 0; i < ev.Opts.Count; i++)                      // the "or" pictures follow the same renames
                if (ev.Opts[i].Key == "or")
                    ev.Opts[i] = new("or", string.Join(",", ev.Opts[i].Value.Split(',').Select(n => rename.TryGetValue(n, out var r2) ? r2 : n)));
        }
        return (rec, notes);
    }

    public static (Recording rec, List<string> notes) LoadAny(string path, string imagesDir) =>
        path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? ImportBundle(path, imagesDir) : (LoadText(path), new List<string>());

    public sealed class Summary
    {
        public string Name = "", Created = "";
        public int Actions, Images;
        public double Duration;
        public bool Ok = true;
    }

    /// Quick look at a saved recording for the Library (no full validation).
    public static Summary Scan(string path)
    {
        var s = new Summary();
        try
        {
            foreach (var raw in File.ReadLines(path, Encoding.UTF8))
            {
                var line = raw.Trim().TrimStart('﻿');
                if (line.Length == 0) continue;
                if (line.StartsWith('#'))
                {
                    if (Format.TryMeta(line, out var k, out var v))
                    {
                        if (k == "name") s.Name = v;
                        else if (k == "created") s.Created = v;
                    }
                    continue;
                }
                var parts = line.Split((char[])null, 3, StringSplitOptions.RemoveEmptyEntries);
                s.Actions++;
                if (double.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var t))
                    s.Duration = Math.Max(s.Duration, t);
                else s.Ok = false;
                if (parts.Length > 1 && parts[1].StartsWith("IMAGE_", StringComparison.OrdinalIgnoreCase)) s.Images++;
            }
        }
        catch (IOException) { s.Ok = false; }
        return s;
    }

    /// Names of images a saved recording uses (for "used in N recordings").
    public static HashSet<string> ImagesUsed(string path)
    {
        var set = new HashSet<string>();
        try
        {
            foreach (var line in File.ReadLines(path))
            {
                if (!line.Contains("IMAGE_")) continue;
                var p = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                if (p.Length > 2) set.Add(p[2]);
                foreach (var o in p.Skip(3).Where(x => x.StartsWith("or=")))
                    foreach (var n in o[3..].Split(',', StringSplitOptions.RemoveEmptyEntries)) set.Add(n);
            }
        }
        catch (IOException) { }
        return set;
    }

    public static string Slug(string text)
    {
        var sb = new StringBuilder();
        foreach (var ch in (text ?? "").Trim())
            if (char.IsAsciiLetterOrDigit(ch) || ch is '_' or '-') sb.Append(ch);
            else if (ch == ' ') sb.Append('_');
        return sb.Length > 0 ? sb.ToString() : "recording";
    }

    public static string UniquePath(string folder, string name, string allow = null)
    {
        var stem = Path.GetFileNameWithoutExtension(name);
        var ext = Path.GetExtension(name);
        var p = Path.Combine(folder, name);
        int n = 2;
        while (File.Exists(p) && (allow == null || !string.Equals(Path.GetFullPath(p), Path.GetFullPath(allow), StringComparison.OrdinalIgnoreCase)))
            p = Path.Combine(folder, $"{stem}_{n++}{ext}");
        return p;
    }
}
