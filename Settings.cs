using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Windows.Media;

namespace SimpleViewer;

internal enum OverlayPosition { TopLeft, TopRight, BottomLeft, BottomRight, Center }
internal enum OpenFolderMode { Newest, First }

/// <summary>Quick-access folder slot; empty Path = not configured.</summary>
internal sealed record QuickFolder(string Name = "", string Path = "")
{
    [JsonIgnore] public bool IsSet => Path.Length > 0;
    // Display name: configured name, else the folder's name (or the path itself for a drive root).
    public string DisplayName()
    {
        if (Name.Length > 0) return Name;
        string n = System.IO.Path.GetFileName(Path.TrimEnd('\\', '/'));
        return n.Length > 0 ? n : Path;
    }
}

/// <summary>User settings, stored as JSON in %APPDATA%\SimpleViewer\settings.json.</summary>
internal sealed class Settings
{
    // Overlays (metadata, help and all messages)
    public string FontFamily { get; set; } = "Consolas";
    public double FontSize { get; set; } = 14;
    public string FontColor { get; set; } = "#FFFFFF";
    public string OverlayBackground { get; set; } = "#000000";
    public int OverlayOpacity { get; set; } = 70;           // percent; 0 = fully transparent, 100 = fully opaque
    public OverlayPosition MetadataPosition { get; set; } = OverlayPosition.TopLeft;
    // View
    public string ImageBackground { get; set; } = "#000000";
    public bool InvertWheel { get; set; }
    public SortMode DefaultSort { get; set; } = SortMode.Name;
    public bool LiveOnStart { get; set; }
    public OpenFolderMode OpenFolderShows { get; set; } = OpenFolderMode.Newest;
    // Quick-access folders: always exactly QuickFolderCount slots
    public const int QuickFolderCount = 5;
    public List<QuickFolder> QuickFolders { get; set; } = EmptySlots();
    // Live mode
    public int LiveMaxWaitSeconds { get; set; } = 10;

    public static readonly string FilePath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SimpleViewer", "settings.json");

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    public Settings Clone()
    {
        var s = (Settings)MemberwiseClone();
        s.QuickFolders = [.. QuickFolders]; // records are immutable, a new list is enough
        return s;
    }

    static List<QuickFolder> EmptySlots() => Enumerable.Range(0, QuickFolderCount).Select(_ => new QuickFolder()).ToList();

    public static Color? ParseColor(string? s) =>
        s != null && Regex.IsMatch(s, "^#[0-9A-Fa-f]{6}$") ? (Color)ColorConverter.ConvertFromString(s) : null;

    /// <summary>
    /// Loads settings; every missing, unreadable or invalid field silently falls back to its default.
    /// Unknown fields (e.g. the removed "MouseMargin") are ignored.
    /// </summary>
    public static Settings Load(string? path = null)
    {
        var s = new Settings();
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path ?? FilePath));
            var r = doc.RootElement;
            if (r.ValueKind != JsonValueKind.Object) return s;
            s.FontFamily = Get(r, nameof(FontFamily), s.FontFamily, v => !string.IsNullOrWhiteSpace(v));
            s.FontSize = Get(r, nameof(FontSize), s.FontSize, v => v is >= 8 and <= 48);
            s.FontColor = Get(r, nameof(FontColor), s.FontColor, v => ParseColor(v) != null);
            s.OverlayBackground = Get(r, nameof(OverlayBackground), s.OverlayBackground, v => ParseColor(v) != null);
            s.OverlayOpacity = Get(r, nameof(OverlayOpacity), s.OverlayOpacity, v => v is >= 0 and <= 100);
            s.MetadataPosition = Get(r, nameof(MetadataPosition), s.MetadataPosition, Enum.IsDefined);
            s.ImageBackground = Get(r, nameof(ImageBackground), s.ImageBackground, v => ParseColor(v) != null);
            s.InvertWheel = Get(r, nameof(InvertWheel), s.InvertWheel);
            s.DefaultSort = Get(r, nameof(DefaultSort), s.DefaultSort, Enum.IsDefined);
            s.LiveOnStart = Get(r, nameof(LiveOnStart), s.LiveOnStart);
            s.LiveMaxWaitSeconds = Get(r, nameof(LiveMaxWaitSeconds), s.LiveMaxWaitSeconds, v => v is >= 1 and <= 60);
            s.OpenFolderShows = Get(r, nameof(OpenFolderShows), s.OpenFolderShows, Enum.IsDefined);
            // Quick folders per slot: a broken entry becomes an empty slot; paths are not checked here (only on click).
            if (r.TryGetProperty(nameof(QuickFolders), out var q) && q.ValueKind == JsonValueKind.Array)
                for (int i = 0; i < Math.Min(QuickFolderCount, q.GetArrayLength()); i++)
                    s.QuickFolders[i] = new QuickFolder(Str(q[i], nameof(QuickFolder.Name)), Str(q[i], nameof(QuickFolder.Path)));
        }
        catch (Exception) { } // missing or unparsable file: defaults
        return s;
    }

    static string Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()!.Trim() : "";

    static T Get<T>(JsonElement root, string name, T fallback, Func<T, bool>? valid = null)
    {
        try
        {
            if (root.TryGetProperty(name, out var p) && p.Deserialize<T>(Json) is T v && (valid?.Invoke(v) ?? true)) return v;
        }
        catch (Exception) { }
        return fallback;
    }

    /// <summary>Atomic save: write a temp file, then replace the target.</summary>
    public void Save(string? path = null)
    {
        path ??= FilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
        File.Move(tmp, path, overwrite: true);
    }
}
