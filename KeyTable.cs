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
        new("Rechte Maustaste + Mausrad / Strg + Mausrad", "Zoom (Punkt unter dem Mauszeiger bleibt stehen)", [], null),
        new("Maus bewegen", "bei Bild größer als die Fläche: Ausschnitt folgt der Maus (Bildrand = Rand des Mausbereichs)", [], null),
        new("Linksklick", "1:1 über alle Monitore umschalten; erneuter Klick = zurück", [], null),
        new("Maus an den oberen Rand", "Menü einblenden (nicht bei vergrößertem Bild oder 1:1 über alle Monitore)", [], null),
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

    public static string ToMarkdown() =>
        "| Taste / Maus | Aktion |\n|---|---|\n" +
        string.Join("\n", Entries.Select(e => $"| {e.Label} | {e.Description} |"));
}
