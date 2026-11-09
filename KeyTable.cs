using System.Windows.Input;

namespace SimpleViewer;

/// <summary>
/// One row of the help table. Entries with keys are also dispatched from here (and the menu invokes the same
/// actions), so help, shortcuts and menu cannot drift. Modifiers must match exactly (Shift is ignored).
/// </summary>
internal sealed record KeyEntry(string Label, string Description, Key[] Keys, Action<MainWindow, Key>? Action,
    ModifierKeys Modifiers = ModifierKeys.None);

internal static class KeyTable
{
    // Entries referenced by the menu (declared before Entries so they are initialized first).
    public static readonly KeyEntry OpenFolder = new("Strg+O", "Ordner öffnen", [Key.O], (w, _) => w.OpenFolder(), ModifierKeys.Control);
    public static readonly KeyEntry ShowInExplorer = new("Strg+E", "im Explorer anzeigen", [Key.E], (w, _) => w.ShowInExplorer(), ModifierKeys.Control);
    public static readonly KeyEntry OpenSettings = new("Strg+,", "Einstellungen", [Key.OemComma], (w, _) => w.OpenSettings(), ModifierKeys.Control);
    public static readonly KeyEntry Sort = new("O", "Sortierung Name/Datum", [Key.O], (w, _) => w.ToggleSort());
    public static readonly KeyEntry Live = new("L", "Live-Modus", [Key.L], (w, _) => w.ToggleLive());
    public static readonly KeyEntry Meta = new("I", "Metadaten", [Key.I], (w, _) => w.ToggleMeta());
    public static readonly KeyEntry Fullscreen = new("F11", "Vollbild auf dem aktuellen Monitor (in 1:1 über alle Monitore: zurück)", [Key.F11], (w, _) => w.ToggleFullscreen());
    public static readonly KeyEntry Help = new("F1 / H", "diese Hilfe", [Key.F1, Key.H], (w, _) => w.ToggleHelp());

    public static readonly KeyEntry[] Entries =
    [
        new("Mausrad", "vorheriges/nächstes Bild (kein Umlauf am Anfang/Ende)", [], null),
        new("Mausrad am letzten Bild weiter", "nach 5 Schritten Live-Modus ein (Pause zurücksetzen)", [], null),
        new("Rechte Maustaste + Mausrad / Strg + Mausrad", "Zoom (Punkt unter dem Mauszeiger bleibt stehen)", [], null),
        new("Maus bewegen", "bei Bild größer als die Fläche: Ausschnitt folgt der Maus (Bildrand = Rand von R, der Bildfläche in Ausgangsgröße)", [], null),
        new("Mittlere Maustaste", "1:1-Ansicht über alle Monitore umschalten (Maus bewegen = Ausschnitt verschieben)", [], null),
        new("Doppelklick", "Fenster maximieren / wiederherstellen", [], null),
        new("Maus an den oberen Rand", "Menü einblenden (nicht bei vergrößertem Bild oder 1:1 über alle Monitore)", [], null),
        new("Klick auf Schnellzugriff (ohne Bild)", "Ordner öffnen; leerer Slot: Einstellungen öffnen", [], null),
        new("0", "Ausgangsgröße", [Key.D0, Key.NumPad0], (w, _) => w.ResetZoom()),
        new("1", "Zoom 100 % (1 Bildpixel = 1 Bildschirmpixel) im aktuellen Fenster", [Key.D1, Key.NumPad1], (w, _) => w.ZoomTo100()),
        new("Links / Rechts", "vorheriges/nächstes Bild", [Key.Left, Key.Right], (w, k) => w.Step(k == Key.Left ? -1 : 1)),
        Sort,
        Live,
        Meta,
        new("C", "alles kopieren", [Key.C], (w, _) => w.Copy(CopyPart.All)),
        new("P", "Prompt kopieren", [Key.P], (w, _) => w.Copy(CopyPart.Prompt)),
        new("S", "Seed kopieren", [Key.S], (w, _) => w.Copy(CopyPart.Seed)),
        new("Entf", "in den Papierkorb", [Key.Delete], (w, _) => w.DeleteCurrent()),
        OpenFolder,
        ShowInExplorer,
        OpenSettings,
        new("Alt / F10", "Menü einblenden", [Key.LeftAlt, Key.RightAlt, Key.F10], (w, _) => w.ToggleMenuFromKeyboard()),
        Fullscreen,
        new("Esc", "Menü/Overlay schließen, sonst 1:1 über alle Monitore bzw. Vollbild verlassen, sonst beenden", [Key.Escape], (w, _) => w.Escape()),
        Help,
    ];

    /// <summary>Entries in display order (help overlay, README), independent of the table order.</summary>
    public static IEnumerable<KeyEntry> Sorted => Entries.Select((e, i) => (e, i)).OrderBy(x => SortRank(x.e, x.i)).Select(x => x.e);

    // Order: no modifier (digits, letters, F-keys, other keys), then Ctrl, then Alt combinations, then mouse rows
    // (text-only) in table order. Combined entries ("Links / Rechts") sort by their first key.
    static (int Modifier, int Group, int Sub, string Name, int Index) SortRank(KeyEntry e, int index)
    {
        if (e.Keys.Length == 0) return (9, 0, 0, "", index);
        int modifier = e.Modifiers switch { ModifierKeys.None => 0, ModifierKeys.Control => 1, ModifierKeys.Alt => 2, _ => 3 };
        var k = e.Keys[0];
        return k switch
        {
            >= Key.D0 and <= Key.D9 => (modifier, 0, k - Key.D0, "", index),
            >= Key.NumPad0 and <= Key.NumPad9 => (modifier, 0, k - Key.NumPad0, "", index),
            >= Key.A and <= Key.Z => (modifier, 1, k - Key.A, "", index),
            >= Key.F1 and <= Key.F24 => (modifier, 2, k - Key.F1, "", index),
            Key.Left => (modifier, 3, 0, "", index),
            Key.Right => (modifier, 3, 1, "", index),
            Key.Up => (modifier, 3, 2, "", index),
            Key.Down => (modifier, 3, 3, "", index),
            Key.Delete => (modifier, 3, 4, "", index),
            Key.Escape => (modifier, 3, 5, "", index),
            _ => (modifier, 3, 6, e.Label, index), // anything else: alphabetically by display name
        };
    }

    public static string ToMarkdown() =>
        "| Taste / Maus | Aktion |\n|---|---|\n" +
        string.Join("\n", Sorted.Select(e => $"| {e.Label} | {e.Description} |"));
}
