using System.IO;

namespace SimpleViewer;

/// <summary>Sorting into subfolders (keys Q/W/E/R): folder name validation, collision-free target paths, the move itself.</summary>
internal static class SortMove
{
    public const string Keys = "QWER"; // slot i is triggered by Keys[i]
    public const int MaxLength = 50;
    public static readonly string[] Defaults = ["1", "2", "3", "4"];

    /// <summary>German error for one slot name, or null if valid. "" (slot unused) is valid.</summary>
    public static string? NameError(string name) =>
        name.Length == 0 ? null
        : name is "." or ".." ? "„.“ und „..“ sind nicht erlaubt"
        : name.Length > MaxLength ? $"höchstens {MaxLength} Zeichen"
        : name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ? "enthält ungültige Zeichen (z. B. \\ / : * ? \" < > |)"
        : null;

    /// <summary>German error for the whole list (names, then case-insensitive uniqueness), or null if valid.</summary>
    public static string? ListError(IReadOnlyList<string> names)
    {
        for (int i = 0; i < names.Count; i++)
        {
            if (NameError(names[i]) is string e) return $"Taste {Keys[i]}: {e}.";
            if (Taken(names, i, names[i], earlierOnly: true)) return $"Taste {Keys[i]}: „{names[i]}“ ist schon einer anderen Taste zugeordnet.";
        }
        return null;
    }

    /// <summary>A duplicate of an earlier slot falls back to its slot default, or to "" (unused) if that is taken too.</summary>
    public static void FixDuplicates(List<string> names)
    {
        for (int i = 1; i < names.Count; i++)
            if (Taken(names, i, names[i], earlierOnly: true)) names[i] = Taken(names, i, Defaults[i], earlierOnly: false) ? "" : Defaults[i];
    }

    static bool Taken(IReadOnlyList<string> names, int i, string name, bool earlierOnly) =>
        name.Length > 0 && names.Where((n, j) => j != i && (!earlierOnly || j < i)).Any(n => n.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>folder\fileName, or "name (2).ext", "name (3).ext", ... if taken. Never an existing path.</summary>
    public static string FreePath(string folder, string fileName)
    {
        string stem = Path.GetFileNameWithoutExtension(fileName), ext = Path.GetExtension(fileName);
        string p = Path.Combine(folder, fileName);
        for (int n = 2; File.Exists(p) || Directory.Exists(p); n++) p = Path.Combine(folder, $"{stem} ({n}){ext}");
        return p;
    }

    /// <summary>Moves source into folder (created if missing) under name (default: its own name); returns the new path.</summary>
    public static string Move(string source, string folder, string? name = null)
    {
        Directory.CreateDirectory(folder);
        string dest = FreePath(folder, name ?? Path.GetFileName(source));
        File.Move(source, dest, overwrite: false); // throws if the name was taken in the meantime
        return dest;
    }
}

/// <summary>Undo stack of the last <see cref="Capacity"/> moves (session only); the oldest entry is dropped first.</summary>
internal sealed class MoveHistory
{
    public const int Capacity = 50;
    readonly LinkedList<(string From, string To)> moves = new();
    public int Count => moves.Count;

    public void Push(string from, string to)
    {
        moves.AddLast((from, to));
        if (moves.Count > Capacity) moves.RemoveFirst();
    }

    public bool TryPeek(out (string From, string To) move)
    {
        move = moves.Last?.Value ?? default;
        return moves.Count > 0;
    }

    public void Pop() { if (moves.Count > 0) moves.RemoveLast(); }
}
