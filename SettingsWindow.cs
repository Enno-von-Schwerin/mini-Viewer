using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace SimpleViewer;

/// <summary>Modal "Einstellungen" dialog. Edits a copy; "Übernehmen"/"OK" validate, apply via callback and save.</summary>
internal sealed class SettingsWindow : Window
{
    readonly Action<Settings> apply;
    readonly ComboBox font = new() { IsEditable = false };
    readonly Slider fontSize = Num(8, 48), opacity = Num(0, 100), maxWait = Num(1, 60);
    readonly TextBox fontColor = new(), overlayBg = new(), imageBg = new();
    readonly ComboBox position = new(), defaultSort = new(), openShows = new();
    readonly TextBox[] slotNames = new TextBox[Settings.QuickFolderCount], slotPaths = new TextBox[Settings.QuickFolderCount];
    readonly CheckBox invertWheel = new() { Content = "Mausrad-Richtung beim Blättern umkehren" };
    readonly CheckBox liveOnStart = new() { Content = "Live-Modus beim Start aktivieren" };
    readonly Border preview = new() { Padding = new Thickness(12, 8, 12, 8), CornerRadius = new CornerRadius(6) };
    readonly TextBlock previewText = new() { Text = "Seed: 1234567890, Steps: 30\nÄÖÜ äöü ß – Beispieltext" };
    readonly Border previewBack = new() { Padding = new Thickness(12), Background = new LinearGradientBrush(Colors.White, Colors.SteelBlue, 0) };

    static readonly string[] Positions = ["links oben", "rechts oben", "links unten", "rechts unten", "Mitte"];

    public SettingsWindow(Settings current, Action<Settings> apply, int? focusSlot = null)
    {
        this.apply = apply;
        Title = "Einstellungen";
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        foreach (var f in Fonts.SystemFontFamilies.Select(f => f.Source).Distinct().Order()) font.Items.Add(f);
        foreach (var p in Positions) position.Items.Add(p);
        defaultSort.Items.Add("Name");
        defaultSort.Items.Add("Änderungsdatum");
        openShows.Items.Add("Neuestes Bild");
        openShows.Items.Add("Erstes Bild");
        preview.Child = previewText;
        previewBack.Child = preview;

        var overlays = Group("Overlays",
            Row("Schriftart", font),
            Row("Schriftgröße", WithValue(fontSize, "")),
            Row("Schriftfarbe", ColorBox(fontColor)),
            Row("Hintergrundfarbe", ColorBox(overlayBg)),
            Row("Hintergrund-Deckkraft", WithValue(opacity, " %")),
            Row("Overlay-Position (Metadaten)", position),
            Row("Vorschau", previewBack));
        var view = Group("Ansicht",
            Row("Hintergrundfarbe der Bildfläche", ColorBox(imageBg)),
            Row("", invertWheel),
            Row("Standard-Sortierung", defaultSort),
            Row("Beim Öffnen eines Ordners zuerst anzeigen", openShows),
            Row("", liveOnStart));
        var quickRows = new List<UIElement>
        {
            Row("", new DockPanel { Children = { new TextBlock { Text = "Anzeigename", Width = 136 }, new TextBlock { Text = "Pfad" } } }, 60),
        };
        for (int i = 0; i < Settings.QuickFolderCount; i++) quickRows.Add(SlotRow(i));
        var quickGroup = Group("Schnellzugriff-Ordner", [.. quickRows]);
        var liveGroup = Group("Live-Modus", Row("Maximale Wartezeit für unfertige Dateien", WithValue(maxWait, " s")));

        var defaults = Button("Standardwerte", () => Show(new Settings()));
        var ok = Button("OK", () => { if (TryApply()) Close(); });
        ok.IsDefault = true;
        var applyBtn = Button("Übernehmen", () => TryApply());
        var cancel = Button("Abbrechen", Close);
        cancel.IsCancel = true;
        var buttons = new DockPanel { Margin = new Thickness(0, 12, 0, 0), LastChildFill = false };
        DockPanel.SetDock(defaults, Dock.Left);
        buttons.Children.Add(defaults);
        foreach (var b in new[] { cancel, applyBtn, ok }) { DockPanel.SetDock(b, Dock.Right); buttons.Children.Add(b); }

        Content = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = SystemParameters.WorkArea.Height * 0.9,
            Content = new StackPanel { Margin = new Thickness(12), Width = 640, Children = { overlays, view, quickGroup, liveGroup, buttons } },
        };
        // Opened from an empty quick-access slot: scroll to the group and focus that slot's path field.
        if (focusSlot is int slot && slot >= 0 && slot < Settings.QuickFolderCount)
            Loaded += (_, _) => { quickGroup.BringIntoView(); slotPaths[slot].Focus(); };

        // Any change refreshes the preview and the color validation.
        foreach (var t in new[] { fontColor, overlayBg, imageBg }) t.TextChanged += (_, _) => Refresh();
        foreach (var s in new[] { fontSize, opacity }) s.ValueChanged += (_, _) => Refresh();
        font.SelectionChanged += (_, _) => Refresh();
        Show(current);
    }

    static Slider Num(double min, double max) => new()
    {
        Minimum = min, Maximum = max, IsSnapToTickEnabled = true, TickFrequency = 1, SmallChange = 1, LargeChange = 5,
        VerticalAlignment = VerticalAlignment.Center,
    };

    static UIElement WithValue(Slider s, string unit)
    {
        var label = new TextBlock { Width = 48, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right, Text = s.Value + unit };
        s.ValueChanged += (_, _) => label.Text = s.Value + unit;
        var d = new DockPanel();
        DockPanel.SetDock(label, Dock.Right);
        d.Children.Add(label);
        d.Children.Add(s);
        return d;
    }

    static UIElement ColorBox(TextBox t)
    {
        var swatch = new Border { Width = 22, Height = 22, Margin = new Thickness(6, 0, 0, 0), BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1) };
        t.Width = 90;
        t.MaxLength = 7;
        t.TextChanged += (_, _) =>
        {
            var c = Settings.ParseColor(t.Text);
            swatch.Background = c is Color col ? new SolidColorBrush(col) : null;
            t.Background = c == null ? new SolidColorBrush(Color.FromRgb(0xFF, 0xD0, 0xD0)) : Brushes.White; // invalid input highlighted
        };
        return new StackPanel { Orientation = Orientation.Horizontal, Children = { t, swatch } };
    }

    // One quick-access slot: display name, path, "..." (folder dialog) and "Leeren".
    UIElement SlotRow(int i)
    {
        var name = slotNames[i] = new TextBox { Width = 130, Margin = new Thickness(0, 0, 6, 0), ToolTip = "Anzeigename (optional, leer = Ordnername)" };
        var path = slotPaths[i] = new TextBox { ToolTip = "Ordnerpfad" };
        var browse = Button("...", () =>
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Ordner wählen" };
            if (System.IO.Directory.Exists(path.Text)) dlg.InitialDirectory = path.Text;
            if (dlg.ShowDialog(this) == true) path.Text = dlg.FolderName;
        });
        browse.MinWidth = 32;
        var clear = Button("Leeren", () => { name.Text = ""; path.Text = ""; });
        clear.MinWidth = 64;
        var d = new DockPanel();
        DockPanel.SetDock(name, Dock.Left);
        DockPanel.SetDock(clear, Dock.Right);
        DockPanel.SetDock(browse, Dock.Right);
        foreach (var c in new UIElement[] { name, clear, browse, path }) d.Children.Add(c); // path fills the rest
        return Row($"Slot {i + 1}", d, 60);
    }

    static UIElement Row(string label, UIElement control, double labelWidth = 240)
    {
        var g = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(labelWidth) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        var l = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        Grid.SetColumn((FrameworkElement)control, 1);
        g.Children.Add(l);
        g.Children.Add(control);
        return g;
    }

    static GroupBox Group(string header, params UIElement[] rows)
    {
        var p = new StackPanel { Margin = new Thickness(6) };
        foreach (var r in rows) p.Children.Add(r);
        return new GroupBox { Header = header, Content = p, Margin = new Thickness(0, 0, 0, 8) };
    }

    static Button Button(string text, Action click)
    {
        var b = new Button { Content = text, MinWidth = 90, Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(8, 3, 8, 3) };
        b.Click += (_, _) => click();
        return b;
    }

    void Show(Settings s)
    {
        font.SelectedItem = font.Items.Contains(s.FontFamily) ? s.FontFamily : null;
        if (font.SelectedItem == null) { font.Items.Add(s.FontFamily); font.SelectedItem = s.FontFamily; }
        fontSize.Value = s.FontSize;
        fontColor.Text = s.FontColor;
        overlayBg.Text = s.OverlayBackground;
        opacity.Value = s.OverlayOpacity;
        position.SelectedIndex = (int)s.MetadataPosition;
        imageBg.Text = s.ImageBackground;
        invertWheel.IsChecked = s.InvertWheel;
        defaultSort.SelectedIndex = (int)s.DefaultSort;
        liveOnStart.IsChecked = s.LiveOnStart;
        maxWait.Value = s.LiveMaxWaitSeconds;
        openShows.SelectedIndex = (int)s.OpenFolderShows;
        for (int i = 0; i < Settings.QuickFolderCount; i++)
        {
            slotNames[i].Text = s.QuickFolders[i].Name;
            slotPaths[i].Text = s.QuickFolders[i].Path;
        }
        Refresh();
    }

    void Refresh()
    {
        if (font.SelectedItem is string f) previewText.FontFamily = new FontFamily(f);
        previewText.FontSize = fontSize.Value;
        if (Settings.ParseColor(fontColor.Text) is Color fc) previewText.Foreground = new SolidColorBrush(fc);
        if (Settings.ParseColor(overlayBg.Text) is Color bc) preview.Background = MainWindow.OverlayBrush(bc, (int)opacity.Value);
    }

    // Returns false (and applies nothing) while any color field is invalid.
    bool TryApply()
    {
        if (new[] { fontColor, overlayBg, imageBg }.Any(t => Settings.ParseColor(t.Text) == null))
        {
            MessageBox.Show(this, "Bitte gültige Farben im Format #RRGGBB eingeben.", "Einstellungen", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        var s = new Settings
        {
            FontFamily = font.SelectedItem as string ?? "Consolas",
            FontSize = fontSize.Value,
            FontColor = fontColor.Text.ToUpperInvariant(),
            OverlayBackground = overlayBg.Text.ToUpperInvariant(),
            OverlayOpacity = (int)opacity.Value,
            MetadataPosition = (OverlayPosition)Math.Max(0, position.SelectedIndex),
            ImageBackground = imageBg.Text.ToUpperInvariant(),
            InvertWheel = invertWheel.IsChecked == true,
            DefaultSort = (SortMode)Math.Max(0, defaultSort.SelectedIndex),
            LiveOnStart = liveOnStart.IsChecked == true,
            LiveMaxWaitSeconds = (int)maxWait.Value,
            OpenFolderShows = (OpenFolderMode)Math.Max(0, openShows.SelectedIndex),
            QuickFolders = Enumerable.Range(0, Settings.QuickFolderCount)
                .Select(i => new QuickFolder(slotNames[i].Text.Trim(), slotPaths[i].Text.Trim())).ToList(),
        };
        apply(s);
        try { s.Save(); }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Einstellungen konnten nicht gespeichert werden:\n" + ex.Message, "Einstellungen", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        return true;
    }
}
