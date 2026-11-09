using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Windows.Media;

namespace SimpleViewer;

internal enum OverlayPosition { TopLeft, TopRight, BottomLeft, BottomRight, Center }

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
    public int MouseMargin { get; set; } = 10;              // percent of R per side (E rectangle)
    public bool InvertWheel { get; set; }
    public SortMode DefaultSort { get; set; } = SortMode.Name;
    public bool LiveOnStart { get; set; }
    // Live mode
    public int LiveMaxWaitSeconds { get; set; } = 10;

    public static readonly string FilePath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SimpleViewer", "settings.json");

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    public Settings Clone() => (Settings)MemberwiseClone();

    public static Color? ParseColor(string? s) =>
        s != null && Regex.IsMatch(s, "^#[0-9A-Fa-f]{6}$") ? (Color)ColorConverter.ConvertFromString(s) : null;

    /// <summary>Loads settings; every missing, unreadable or invalid field silently falls back to its default.</summary>
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
            s.MouseMargin = Get(r, nameof(MouseMargin), s.MouseMargin, v => v is >= 0 and <= 50);
            s.InvertWheel = Get(r, nameof(InvertWheel), s.InvertWheel);
            s.DefaultSort = Get(r, nameof(DefaultSort), s.DefaultSort, Enum.IsDefined);
            s.LiveOnStart = Get(r, nameof(LiveOnStart), s.LiveOnStart);
            s.LiveMaxWaitSeconds = Get(r, nameof(LiveMaxWaitSeconds), s.LiveMaxWaitSeconds, v => v is >= 1 and <= 60);
        }
        catch (Exception) { } // missing or unparsable file: defaults
        return s;
    }

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
