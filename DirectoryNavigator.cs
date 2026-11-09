using System.IO;
using System.Runtime.InteropServices;

namespace SimpleViewer;

internal enum SortMode { Name, Date }

/// <summary>Stateless directory navigation: every call enumerates the directory fresh.</summary>
internal static class DirectoryNavigator
{
    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    static extern int StrCmpLogicalW(string a, string b);

    readonly record struct Entry(string Path, string Name, DateTime Time);

    public static bool IsSupported(string path)
    {
        string ext = System.IO.Path.GetExtension(path);
        return ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".png", StringComparison.OrdinalIgnoreCase);
    }

    static List<Entry> List(string dir, SortMode mode)
    {
        var list = new List<Entry>();
        try
        {
            foreach (var f in new DirectoryInfo(dir).EnumerateFiles())
                if (IsSupported(f.Name)) list.Add(new(f.FullName, f.Name, f.LastWriteTimeUtc));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        list.Sort((a, b) => Compare(a, b, mode));
        return list;
    }

    static int Compare(Entry a, Entry b, SortMode mode)
    {
        if (mode == SortMode.Date)
        {
            int c = a.Time.CompareTo(b.Time);
            if (c != 0) return c;
        }
        return StrCmpLogicalW(a.Name, b.Name);
    }

    // Index of the file in the list, or ~insertionIndex if it no longer exists (time = its last known write time).
    static int Locate(List<Entry> list, string path, DateTime time, SortMode mode)
    {
        int i = list.FindIndex(e => string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase));
        if (i >= 0) return i;
        var key = new Entry(path, System.IO.Path.GetFileName(path), time);
        i = list.FindIndex(e => Compare(e, key, mode) > 0);
        return ~(i < 0 ? list.Count : i);
    }

    /// <summary>
    /// Neighbor at offset delta, clamped to the first/last image (no wrap-around), so at either end the current
    /// file itself is returned. Null if the directory has no images.
    /// </summary>
    public static string? Neighbor(string dir, string? current, DateTime time, int delta, SortMode mode)
    {
        var list = List(dir, mode);
        int n = list.Count;
        if (n == 0) return null;
        if (current == null) return delta > 0 ? list[0].Path : list[^1].Path;
        int i = Locate(list, current, time, mode);
        // Missing file: the insertion point sits between two neighbors, so "next" is ~i and "previous" is ~i - 1.
        int target = i >= 0 ? i + delta : (delta > 0 ? ~i + delta - 1 : ~i + delta);
        return list[Math.Clamp(target, 0, n - 1)].Path;
    }

    /// <summary>File to show after 'removed' was deleted: the next one, or the previous one if it was the last.</summary>
    public static string? AfterRemoval(string dir, string removed, DateTime time, SortMode mode)
    {
        var list = List(dir, mode);
        if (list.Count == 0) return null;
        int i = Locate(list, removed, time, mode);
        if (i < 0) i = ~i;
        return list[Math.Min(i, list.Count - 1)].Path;
    }

    public static string? Newest(string dir)
    {
        var list = List(dir, SortMode.Date);
        return list.Count > 0 ? list[^1].Path : null;
    }
}
