using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.VisualBasic.FileIO;

namespace SimpleViewer;

internal enum CopyPart { All, Prompt, Seed }

public sealed class MainWindow : Window
{
    readonly Canvas canvas = new() { ClipToBounds = true, Background = Brushes.Black };
    readonly Image image = new() { Stretch = Stretch.Fill };
    readonly TextBlock emptyText = new() { TextWrapping = TextWrapping.Wrap };
    readonly TextBlock toast = new();
    readonly TextBlock metaText = new() { TextWrapping = TextWrapping.Wrap };
    // All overlays/messages share one style from the settings (see ApplySettings).
    readonly Border metaOverlay, helpOverlay, emptyBox, toastBox, hintBox;
    Settings settings = Settings.Load();
    Brush overlayFg = Brushes.White, overlayBg = Brushes.Black;
    // Empty state (no image): hints at the top, quick-access slots at the bottom.
    readonly Grid emptyState = new() { Visibility = Visibility.Collapsed };
    readonly StackPanel slotPanel = new()
    {
        Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(16),
    };
    readonly Menu menu;
    readonly MenuItem liveItem, metaItem;
    readonly MenuItem quickMenu = new() { Header = "Schnellzugriff" };
    readonly DispatcherTimer toastTimer = new();
    readonly DispatcherTimer liveDebounce = new() { Interval = TimeSpan.FromMilliseconds(150) };
    FileSystemWatcher? watcher;

    string? dir;                   // null until a file or folder was opened
    bool everLoaded, menuByKeyboard;
    string? currentPath;
    DateTime currentTime;          // last write time of the current file, for neighbor lookup after it vanished
    BitmapSource? bitmap;
    string? parameters;
    int loadToken;
    SortMode sort = SortMode.Name;
    bool live, autoFit = true, rightDown, fullscreen, spanned;
    double scale = 1, offX, offY;  // scale: image px -> physical px; offsets in physical px relative to the client area
    readonly WheelAccumulator wheel = new();
    // Forward wheel notches at the last image (live off) count down to switching live mode on.
    readonly WheelToLive wheelToLive = new(() => Uptime.Elapsed);
    static readonly Stopwatch Uptime = Stopwatch.StartNew();
    // Auto mode (slideshow): countdown logic in AutoAdvance; panel (T + shrinking bar) above the toast position.
    readonly AutoAdvance auto = new(() => Uptime.Elapsed);
    readonly DispatcherTimer autoTimer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
    // Display-time controls: one step at once, then one per repeat interval while the tilt / key is held.
    readonly TiltRepeat tiltRepeat = new(() => Uptime.Elapsed), keyRepeat = new(() => Uptime.Elapsed);
    bool keyIsRepeat;              // e.IsRepeat of the key being dispatched
    readonly TextBlock autoText = new();
    readonly Border autoTrack = new() { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) };
    readonly Border autoFill = new() { HorizontalAlignment = HorizontalAlignment.Left };
    readonly Border autoPanel;
    MenuItem autoItem = null!;
    bool autoHold;                 // Auto mode paused by the middle button

    // Viewport mapping: R = image rect at initial scale (screen px); the cursor position within R drives the crop.
    Rect refRect;
    Rect spanMonitor;              // M: monitor containing R when spanning started (screen px)
    bool reloadCurrent;            // live mode: pending debounce reloads the current file (Changed) instead of the newest
    readonly DispatcherTimer tracker = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(15) };
    DateTime? animStart;           // zoom transition: from (animFromX, animFromY) to the mapped offset over AnimMs
    double animFromX, animFromY;
    const double AnimMs = 100;

    readonly Grid overlayHost = new(); // holds all overlays; restricted to one monitor while spanned
    // Window state before fullscreen/spanned: exact bounds (physical px), maximized flag, and the placement
    // (only used for maximized windows, to keep their normal bounds).
    readonly record struct WindowedState(Rect Bounds, bool Maximized, Native.WINDOWPLACEMENT Placement);
    WindowedState windowedBeforeFullscreen;
    (bool Fullscreen, WindowedState Window, double Scale, bool AutoFit) beforeSpan;
    IntPtr Hwnd => new WindowInteropHelper(this).Handle;

    public MainWindow(string? path)
    {
        Title = "SimpleViewer";
        Background = Brushes.Black;
        UseLayoutRounding = true;
        Width = SystemParameters.WorkArea.Width * 0.75;
        Height = SystemParameters.WorkArea.Height * 0.75;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        canvas.Children.Add(image);
        metaOverlay = Overlay(metaText);
        metaOverlay.Margin = new Thickness(16);
        helpOverlay = Overlay(BuildHelp());
        emptyBox = Overlay(emptyText);
        toastBox = Overlay(toast);
        toastBox.Padding = new Thickness(8, 4, 8, 4);
        toastBox.HorizontalAlignment = HorizontalAlignment.Right;
        autoTrack.Child = autoFill;
        autoPanel = Overlay(new StackPanel { Children = { autoText, autoTrack } });
        autoPanel.Padding = new Thickness(8, 4, 8, 6);
        autoPanel.HorizontalAlignment = HorizontalAlignment.Right;
        autoPanel.Margin = new Thickness(0, 6, 0, 0);
        // Short messages stack above the auto panel at the bottom right instead of overlapping it.
        var bottomRight = new StackPanel
        {
            Margin = new Thickness(10), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
            IsHitTestVisible = false, Children = { toastBox, autoPanel },
        };
        hintBox = Overlay(new TextBlock { Text = "Ordner öffnen: Strg+O\nHilfe: F1", TextAlignment = TextAlignment.Center });
        hintBox.VerticalAlignment = VerticalAlignment.Top;
        hintBox.Margin = new Thickness(16, 40, 16, 16); // below the (pinned) menu
        hintBox.Visibility = Visibility.Visible;        // shown/hidden together with emptyState
        emptyState.Children.Add(hintBox);
        emptyState.Children.Add(slotPanel);
        menu = BuildMenu(out liveItem, out metaItem);
        // Menu and overlays live in a layer above the canvas, so they never change the image area size.
        foreach (var e in new UIElement[] { emptyState, emptyBox, metaOverlay, helpOverlay, menu, bottomRight }) overlayHost.Children.Add(e);
        overlayHost.SizeChanged += (_, _) =>
        {
            metaOverlay.MaxWidth = Math.Max(200, overlayHost.ActualWidth * 0.6);
            helpOverlay.MaxWidth = Math.Max(200, overlayHost.ActualWidth - 32);
            UpdateSlotLayout();
        };
        sort = settings.DefaultSort;
        auto.Seconds = settings.AutoSeconds;
        ApplySettings(settings);
        Content = new Grid { Children = { canvas, overlayHost } };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        image.SnapsToDevicePixels = true;

        canvas.SizeChanged += (_, _) => Relayout();
        LocationChanged += (_, _) => Relayout(); // R is in screen coordinates
        tracker.Tick += (_, _) => Track();
        toastTimer.Tick += (_, _) => { toastTimer.Stop(); toastBox.Visibility = Visibility.Collapsed; };
        liveDebounce.Tick += (_, _) =>
        {
            liveDebounce.Stop();
            if (reloadCurrent && currentPath != null) Load(currentPath, fromLive: true);
            else JumpToNewest();
            reloadCurrent = false;
        };
        autoTimer.Tick += (_, _) => AutoTick();
        Closed += (_, _) => { watcher?.Dispose(); SaveAutoSeconds(); };
        Deactivated += (_, _) => { if (!AnySubmenuOpen) HideMenu(); };

        if (path == null)
        {
            Clear(null);
            menu.Visibility = Visibility.Visible; // pinned until the first image is loaded
        }
        else
        {
            path = Path.GetFullPath(path);
            dir = Path.GetDirectoryName(path) ?? Environment.CurrentDirectory;
            Load(path);
            if (settings.LiveOnStart) SetLive(true); // watch only; the requested file stays shown
        }
        ShowToast("F1 = Hilfe", 3000);
    }

    static Border Overlay(UIElement child) => new()
    {
        Child = child, Padding = new Thickness(16, 12, 16, 12), CornerRadius = new CornerRadius(6),
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        IsHitTestVisible = false, Visibility = Visibility.Collapsed,
    };

    // opacityPercent: 0 = fully transparent, 100 = fully opaque.
    internal static Brush OverlayBrush(Color c, int opacityPercent) =>
        new SolidColorBrush(Color.FromArgb((byte)Math.Round(255 * Math.Clamp(opacityPercent, 0, 100) / 100.0), c.R, c.G, c.B));

    // The one shared overlay style (font, size, colors, background opacity from the settings).
    void StyleOverlay(Border b)
    {
        b.Background = overlayBg;
        TextElement.SetForeground(b, overlayFg);
        TextElement.SetFontFamily(b, new FontFamily(settings.FontFamily));
        TextElement.SetFontSize(b, settings.FontSize);
    }

    // Applies settings live: shared overlay style, image-area background, metadata position, quick-access folders.
    void ApplySettings(Settings s)
    {
        if (s.AutoSeconds != settings.AutoSeconds) auto.Seconds = s.AutoSeconds; // changed in the dialog: adopt as T
        settings = s;
        var fg = Settings.ParseColor(s.FontColor) ?? Colors.White;
        overlayFg = new SolidColorBrush(fg);
        overlayBg = OverlayBrush(Settings.ParseColor(s.OverlayBackground) ?? Colors.Black, s.OverlayOpacity);
        foreach (var b in new[] { metaOverlay, helpOverlay, emptyBox, toastBox, hintBox, autoPanel }) StyleOverlay(b);
        // Bar: fixed width of 12 x font size; track in the dimmed font color, fill in the font color.
        autoTrack.Width = s.FontSize * 12;
        autoTrack.Height = autoFill.Height = Math.Max(4, s.FontSize * 0.4);
        autoTrack.Background = OverlayBrush(fg, 30);
        autoFill.Background = overlayFg;
        tiltRepeat.Interval = keyRepeat.Interval = TimeSpan.FromMilliseconds(s.TiltRepeatMs);
        UpdateAutoPanel();
        BuildQuickAccess();
        var area = new SolidColorBrush(Settings.ParseColor(s.ImageBackground) ?? Colors.Black);
        canvas.Background = area;
        Background = area;
        (metaOverlay.HorizontalAlignment, metaOverlay.VerticalAlignment) = s.MetadataPosition switch
        {
            OverlayPosition.TopRight => (HorizontalAlignment.Right, VerticalAlignment.Top),
            OverlayPosition.BottomLeft => (HorizontalAlignment.Left, VerticalAlignment.Bottom),
            OverlayPosition.BottomRight => (HorizontalAlignment.Right, VerticalAlignment.Bottom),
            OverlayPosition.Center => (HorizontalAlignment.Center, VerticalAlignment.Center),
            _ => (HorizontalAlignment.Left, VerticalAlignment.Top),
        };
        Relayout();
    }

    internal void OpenSettings(int? focusSlot = null)
    {
        settings.AutoSeconds = auto.Seconds; // the dialog shows the current T
        new SettingsWindow(settings.Clone(), ApplySettings, focusSlot) { Owner = this }.ShowDialog();
    }

    // ---------- Quick-access folders (empty-state slots and Datei > Schnellzugriff) ----------

    double SlotWidth => settings.FontSize * 14;

    void BuildQuickAccess()
    {
        slotPanel.Children.Clear();
        quickMenu.Items.Clear();
        for (int i = 0; i < settings.QuickFolders.Count; i++)
        {
            var q = settings.QuickFolders[i];
            int index = i;
            var slot = new Border
            {
                Width = SlotWidth, Margin = new Thickness(6), Padding = new Thickness(12, 8, 12, 8), CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(2), BorderBrush = Brushes.Transparent, Cursor = Cursors.Hand,
                Opacity = q.IsSet ? 1 : 0.5, // empty slots dimmed
                Child = new StackPanel
                {
                    Children =
                    {
                        new TextBlock { Text = q.IsSet ? q.DisplayName() : "Nicht belegt", FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis },
                        new TextBlock { Text = q.IsSet ? q.Path : " ", FontSize = settings.FontSize * 0.75, TextTrimming = TextTrimming.CharacterEllipsis },
                    },
                },
            };
            StyleOverlay(slot);
            slot.MouseEnter += (_, _) => slot.BorderBrush = overlayFg; // hover highlight
            slot.MouseLeave += (_, _) => slot.BorderBrush = Brushes.Transparent;
            slot.MouseLeftButtonDown += (_, e) => { e.Handled = true; OpenQuickFolder(index); }; // never a window double-click
            slotPanel.Children.Add(slot);

            if (!q.IsSet) continue;
            var mi = new MenuItem { Header = q.DisplayName().Replace("_", "__"), ToolTip = q.Path };
            mi.Click += (_, _) => OpenQuickFolder(index);
            quickMenu.Items.Add(mi);
        }
        quickMenu.IsEnabled = quickMenu.Items.Count > 0;
        quickMenu.Header = quickMenu.IsEnabled ? "Schnellzugriff" : "Schnellzugriff (keine Ordner eingerichtet)";
        UpdateSlotLayout();
    }

    // Horizontal row if it fits, otherwise a vertical stack.
    void UpdateSlotLayout() =>
        slotPanel.Orientation = overlayHost.ActualWidth >= settings.QuickFolders.Count * (SlotWidth + 12) + 32
            ? Orientation.Horizontal : Orientation.Vertical;

    void OpenQuickFolder(int index)
    {
        var q = settings.QuickFolders[index];
        if (!q.IsSet) { OpenSettings(index); return; }
        if (!Directory.Exists(q.Path)) { ShowToast("Ordner nicht gefunden", 3000); return; }
        if (q.FileDialog) OpenFileIn(q.Path); else OpenFolderPath(q.Path);
    }

    // Quick access with "Datei-Dialog öffnen": pick an image in the folder; cancel changes nothing.
    void OpenFileIn(string folder)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Bild öffnen", InitialDirectory = folder, Multiselect = false,
            Filter = "Bilder (*.jpg;*.jpeg;*.png)|*.jpg;*.jpeg;*.png",
        };
        if (dlg.ShowDialog(this) == true) ShowFileIn(Path.GetDirectoryName(dlg.FileName)!, dlg.FileName);
    }

    static Grid BuildHelp()
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        void Add(UIElement e, int row, int col) { Grid.SetRow(e, row); Grid.SetColumn(e, col); g.Children.Add(e); }

        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var header = new TextBlock { Text = "SimpleViewer – Hilfe", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 10) };
        Grid.SetColumnSpan(header, 2);
        Add(header, 0, 0);
        foreach (var k in KeyTable.Sorted)
        {
            int row = g.RowDefinitions.Count;
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Add(new TextBlock { Text = k.Label, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 2, 24, 2) }, row, 0);
            Add(new TextBlock { Text = k.Description, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 2) }, row, 1);
        }
        return g;
    }

    // Menu items invoke the KeyTable actions, so menu and shortcuts share one implementation.
    Menu BuildMenu(out MenuItem live, out MenuItem meta)
    {
        MenuItem Cmd(string header, KeyEntry k)
        {
            var mi = new MenuItem { Header = header, InputGestureText = k.Label };
            mi.Click += (_, _) => k.Action!(this, k.Keys[0]);
            return mi;
        }
        var exit = new MenuItem { Header = "Beenden" };
        exit.Click += (_, _) => Close();
        live = Cmd("Live-Modus", KeyTable.Live);
        meta = Cmd("Metadaten", KeyTable.Meta);
        autoItem = Cmd("Auto-Modus", KeyTable.Auto);
        autoItem.IsCheckable = true; // state is set from auto.On whenever the menu opens
        var view = new MenuItem
        {
            Header = "Ansicht",
            Items = { Cmd("Sortierung: Name/Datum", KeyTable.Sort), live, autoItem, meta, Cmd("Vollbild", KeyTable.Fullscreen) },
        };
        view.SubmenuOpened += (_, _) =>
        {
            liveItem.IsChecked = this.live;
            autoItem.IsChecked = auto.On;
            metaItem.IsChecked = metaOverlay.Visibility == Visibility.Visible;
        };
        var help = Cmd("Hilfe (F1)", KeyTable.Help); // top-level items cannot show gesture text

        var m = new Menu
        {
            IsMainMenu = false, // Alt/F10 are handled via KeyTable, not by WPF's main-menu logic
            VerticalAlignment = VerticalAlignment.Top,
            Visibility = Visibility.Collapsed,
            Items =
            {
                new MenuItem
                {
                    Header = "Datei",
                    Items =
                    {
                        Cmd("Ordner öffnen...", KeyTable.OpenFolder), quickMenu, Cmd("Im Explorer anzeigen", KeyTable.ShowInExplorer),
                        Cmd("Einstellungen...", KeyTable.OpenSettings), exit,
                    },
                },
                view,
                help,
            },
        };
        m.AddHandler(MenuItem.ClickEvent, new RoutedEventHandler((_, _) => HideMenu()));
        m.MouseEnter += (_, _) => menuByKeyboard = false;
        m.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape || AnySubmenuOpen) return;
            if (MenuPinned) Keyboard.Focus(this); else HideMenu();
            e.Handled = true;
        };
        return m;
    }

    bool MenuPinned => !everLoaded;
    bool AnySubmenuOpen => menu.Items.OfType<MenuItem>().Any(i => i.IsSubmenuOpen);
    bool MenuUnderMouse => menu.Visibility == Visibility.Visible && menu.IsMouseOver;

    void HideMenu()
    {
        if (MenuPinned || menu.Visibility != Visibility.Visible) return;
        bool hadFocus = menu.IsKeyboardFocusWithin;
        menu.Visibility = Visibility.Collapsed;
        menuByKeyboard = false;
        if (hadFocus) Keyboard.Focus(this);
    }

    // A mouse-opened menu hides as soon as the mouse is outside it; a keyboard-opened one waits for Esc/action/click.
    void HideMenuIfIdle()
    {
        if (!menuByKeyboard && !menu.IsMouseOver && !AnySubmenuOpen) HideMenu();
    }

    internal void ToggleMenuFromKeyboard()
    {
        if (spanned) return; // no menu while spanned
        if (menuByKeyboard || menu.IsKeyboardFocusWithin)
        {
            if (MenuPinned) Keyboard.Focus(this); else HideMenu();
            return;
        }
        menu.Visibility = Visibility.Visible;
        menuByKeyboard = true;
        menu.UpdateLayout();
        (menu.Items[0] as MenuItem)?.Focus();
    }

    // While the image is larger than the image area (or spanned), the top edge is used to inspect the image border.
    bool ImageExceedsView()
    {
        if (bitmap == null) return false;
        if (spanned) return true;
        var v = View;
        return bitmap.PixelWidth * scale > v.Width + 0.5 || bitmap.PixelHeight * scale > v.Height + 0.5;
    }

    // ---------- Loading ----------

    void Load(string? path, bool fromLive = false)
    {
        int token = ++loadToken;
        ResetWheelToLive(); // navigation, delete, folder open: the countdown starts over
        auto.Restart();     // same for the Auto mode countdown (also on live-mode jumps)
        string? previous = currentPath;
        currentPath = path;
        if (path == null) { Clear("Keine Bilder im Ordner"); return; }
        // Files may still be being written: retry every 200 ms (live mode: up to the configured limit).
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(fromLive ? settings.LiveMaxWaitSeconds : 1);

        Task.Run(async () =>
        {
            while (true)
            {
                if (Volatile.Read(ref loadToken) != token) return; // superseded by a newer load
                try
                {
                    var r = Decode(path);
                    _ = Dispatcher.InvokeAsync(() => Show(token, r.Bitmap, r.Time, r.Parameters));
                    return;
                }
                catch when (DateTime.UtcNow < deadline) { await Task.Delay(200); }
                catch
                {
                    _ = Dispatcher.InvokeAsync(() => LoadFailed(token, path, previous, fromLive));
                    return;
                }
            }
        });
    }

    static (BitmapSource Bitmap, DateTime Time, string? Parameters) Decode(string path)
    {
        // OnLoad decodes everything into memory, so the file handle is released when the stream is disposed.
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        string? meta = path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? PngMetadata.ReadParameters(fs) : null;
        fs.Position = 0;
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.StreamSource = fs;
        bmp.EndInit();
        bmp.Freeze();
        return (bmp, File.GetLastWriteTimeUtc(path), meta);
    }

    void Show(int token, BitmapSource bmp, DateTime time, string? meta)
    {
        if (token != loadToken) return;
        bitmap = bmp;
        currentTime = time;
        parameters = meta;
        image.Source = bmp;
        emptyBox.Visibility = Visibility.Collapsed;
        emptyState.Visibility = Visibility.Collapsed;
        metaText.Text = meta ?? "keine Metadaten";
        // Spanned mode stays active across navigation (1:1, R fixed); otherwise start at the initial scale.
        if (spanned) scale = 1; else autoFit = true;
        animStart = null;
        Relayout();
        UpdateTitle();
        if (!everLoaded)
        {
            everLoaded = true; // unpin the menu
            HideMenuIfIdle();
        }
    }

    void LoadFailed(int token, string path, string? previous, bool fromLive)
    {
        if (token != loadToken) return;
        if (fromLive) { currentPath = previous; return; } // give up silently, keep what is shown
        Clear($"{Path.GetFileName(path)} kann nicht geladen werden");
    }

    // No image: shows the empty state (hints + quick-access slots) and an optional centered message.
    void Clear(string? message)
    {
        if (spanned) ToggleSpanned(); // nothing to span without an image
        SetAuto(false);               // no images left
        bitmap = null;
        parameters = null;
        image.Source = null;
        emptyText.Text = message ?? "";
        emptyBox.Visibility = message != null ? Visibility.Visible : Visibility.Collapsed;
        emptyState.Visibility = Visibility.Visible;
        metaText.Text = "keine Metadaten";
        UpdateTitle();
    }

    void UpdateTitle()
    {
        string t = bitmap != null && currentPath != null
            ? $"{Path.GetFileName(currentPath)} - {bitmap.PixelWidth}x{bitmap.PixelHeight}"
            : "SimpleViewer";
        Title = live ? t + "  [LIVE]" : t;
    }

    void ShowToast(string text, int ms = 1500)
    {
        toast.Text = text;
        toastBox.Visibility = Visibility.Visible;
        toastTimer.Stop();
        toastTimer.Interval = TimeSpan.FromMilliseconds(ms);
        toastTimer.Start();
    }

    // ---------- Layout / zoom ----------

    double Dpi => VisualTreeHelper.GetDpi(this).DpiScaleX;
    Size View => new(canvas.ActualWidth * Dpi, canvas.ActualHeight * Dpi);

    // Client area origin in physical screen pixels.
    Point ClientOrigin => PresentationSource.FromVisual(canvas) != null ? canvas.PointToScreen(new Point(0, 0)) : default;

    double InitialScale(Size area) =>
        Math.Min(1, Math.Min(area.Width / bitmap!.PixelWidth, area.Height / bitmap.PixelHeight));

    void Relayout()
    {
        if (bitmap == null) return;
        var v = View;
        if (v.Width <= 0 || v.Height <= 0) return;
        if (!spanned)
        {
            // R: image rect at the initial scale, centered in the client area. Fixed while spanned.
            double s0 = InitialScale(v), w = bitmap.PixelWidth * s0, h = bitmap.PixelHeight * s0;
            var o = ClientOrigin;
            refRect = new Rect(o.X + (v.Width - w) / 2, o.Y + (v.Height - h) / 2, w, h);
            if (autoFit) scale = s0;
        }
        Track();
    }

    // Offset the image should have: cursor-mapped via R on axes where it exceeds the area, else centered on R.
    // Spanned: all positioning is relative to the reference monitor M instead of the window/bounding box.
    (double X, double Y) MappedOffset()
    {
        var v = View;
        var o = ClientOrigin;
        var c = Native.CursorPos();
        var r = refRect;
        double w = bitmap!.PixelWidth * scale, h = bitmap.PixelHeight * scale;
        // t: cursor clamped to R, normalized to 0..1 per axis.
        static double T(double cursor, double rStart, double rSize) =>
            Math.Clamp(rSize > 0 ? (cursor - rStart) / rSize : 0.5, 0, 1);
        if (spanned)
        {
            // Screen position M.origin + t * (M.size - imageSize), t = 0.5 (centered) where the image fits into M.
            // Not clamped to the bounding box; converted to window coordinates via the window origin.
            var M = spanMonitor;
            static double OnMonitor(double img, double mStart, double mSize, double t) => mStart + t * (mSize - img);
            return (OnMonitor(w, M.X, M.Width, w > M.Width ? T(c.X, r.X, r.Width) : 0.5) - o.X,
                    OnMonitor(h, M.Y, M.Height, h > M.Height ? T(c.Y, r.Y, r.Height) : 0.5) - o.Y);
        }
        static double Axis(double img, double area, double t, double rCenter) =>
            img > area ? -t * (img - area) : Math.Clamp(rCenter - img / 2, 0, area - img);
        return (Axis(w, v.Width, T(c.X, r.X, r.Width), r.X + r.Width / 2 - o.X),
                Axis(h, v.Height, T(c.Y, r.Y, r.Height), r.Y + r.Height / 2 - o.Y));
    }

    // Updates the offset from the cursor (polled, so it also works outside the window) and runs the zoom transition.
    void Track()
    {
        if (bitmap == null) { tracker.Stop(); return; }
        var (x, y) = MappedOffset();
        if (animStart is DateTime start)
        {
            double k = (DateTime.UtcNow - start).TotalMilliseconds / AnimMs;
            if (k >= 1) animStart = null;
            else { x = animFromX + (x - animFromX) * k; y = animFromY + (y - animFromY) * k; }
        }
        if (x != offX || y != offY || image.Width != bitmap.PixelWidth * scale / Dpi) { offX = x; offY = y; Apply(); }
        // Polling is only needed while the crop depends on the cursor or a transition is running.
        bool needed = animStart != null || (ImageExceedsView() && (IsActive || spanned));
        if (needed && !tracker.IsEnabled) tracker.Start();
        else if (!needed) tracker.Stop();
    }

    void Apply()
    {
        double d = Dpi;
        image.Width = bitmap!.PixelWidth * scale / d;
        image.Height = bitmap.PixelHeight * scale / d;
        Canvas.SetLeft(image, Math.Round(offX) / d);
        Canvas.SetTop(image, Math.Round(offY) / d);
        RenderOptions.SetBitmapScalingMode(image,
            Math.Abs(scale - 1) < 1e-9 ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality);
    }

    // Keeps the image point under the cursor stable, then hands over to the mapping with a short transition.
    void ZoomTo(double newScale, bool fit = false)
    {
        if (bitmap == null) return;
        newScale = Math.Clamp(newScale, 0.02, 50);
        var c = Native.CursorPos();
        var o = ClientOrigin;
        double px = c.X - o.X, py = c.Y - o.Y;
        offX = px - (px - offX) * newScale / scale;
        offY = py - (py - offY) * newScale / scale;
        scale = newScale;
        autoFit = fit;
        animFromX = offX;
        animFromY = offY;
        animStart = DateTime.UtcNow;
        Apply();
        Track();
    }

    internal void ResetZoom()
    {
        if (bitmap != null) ZoomTo(InitialScale(View), fit: !spanned);
    }

    internal void ZoomTo100() => ZoomTo(1);

    // ---------- Fullscreen / spanned ----------

    void SetBorderless(bool on)
    {
        if (on && WindowState != WindowState.Normal) WindowState = WindowState.Normal;
        WindowStyle = on ? WindowStyle.None : WindowStyle.SingleBorderWindow;
        ResizeMode = on ? ResizeMode.NoResize : ResizeMode.CanResize;
        Topmost = on;
    }

    // Captures the windowed state before the window is moved (fullscreen/spanned).
    WindowedState SaveWindowed() =>
        new(Native.WindowRect(Hwnd), WindowState == WindowState.Maximized, Native.GetPlacement(Hwnd));

    void RestoreWindowed(WindowedState s)
    {
        SetBorderless(false);
        // Maximized: the placement restores the maximized state on its monitor and keeps the normal bounds.
        // Normal (incl. snapped): restore the exact rect from GetWindowRect. The placement's rcNormalPosition must
        // not be used here: for a snapped window it is the pre-snap restore rect, not the visible bounds.
        if (s.Maximized) Native.SetPlacement(Hwnd, s.Placement);
        else Native.Place(Hwnd, s.Bounds);
    }

    internal void ToggleFullscreen()
    {
        if (spanned) { ToggleSpanned(); return; } // F11 while spanned returns to the previous state first
        fullscreen = !fullscreen;
        if (fullscreen)
        {
            windowedBeforeFullscreen = SaveWindowed();
            var monitor = Native.MonitorOfWindow(Hwnd); // read before changing anything
            SetBorderless(true);
            Native.Place(Hwnd, monitor);
        }
        else RestoreWindowed(windowedBeforeFullscreen);
        Relayout();
    }

    // Spanned 1:1: the window covers the whole virtual desktop; R stays where it was.
    internal void ToggleSpanned()
    {
        if (!spanned)
        {
            if (bitmap == null) return;
            Relayout(); // make sure R is current
            HideMenu();
            beforeSpan = (fullscreen, SaveWindowed(), scale, autoFit); // before the window is moved
            // Reference monitor M (contains R): fixed while spanned; used for image positioning and overlays.
            var m = spanMonitor = Native.MonitorOfRect(refRect);
            spanned = true;
            scale = 1;
            autoFit = false;
            var vs = Native.VirtualScreen();
            SetBorderless(true);
            Native.Place(Hwnd, vs);
            // Keep overlays inside M (the bounding box may have areas without a monitor).
            double d = Dpi;
            overlayHost.Margin = new Thickness((m.Left - vs.Left) / d, (m.Top - vs.Top) / d, (vs.Right - m.Right) / d, (vs.Bottom - m.Bottom) / d);
        }
        else
        {
            var b = beforeSpan;
            spanned = false;
            (scale, autoFit) = (b.Scale, b.AutoFit);
            overlayHost.Margin = new Thickness(0);
            if (b.Fullscreen) Native.Place(Hwnd, b.Window.Bounds); // fullscreen on the original monitor
            else RestoreWindowed(b.Window);
        }
        animStart = null;
        Relayout();
    }

    // ---------- Mouse ----------

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        e.Handled = true;
        if (MenuUnderMouse) return;
        if (rightDown || (Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            ZoomTo(scale * Math.Pow(1.2, e.Delta / 120.0));
            return;
        }
        // Accumulate so high-resolution wheels/touchpads step once per notch; inversion affects navigation only.
        int notches = wheel.Add(e.Delta);
        int dir1 = settings.InvertWheel ? Math.Sign(notches) : -Math.Sign(notches);
        for (int i = 0; i < Math.Abs(notches); i++) Step(dir1, fromWheel: true);
    }

    // Any key or mouse button cancels the wheel countdown to live mode.
    protected override void OnPreviewKeyDown(KeyEventArgs e) { ResetWheelToLive(); base.OnPreviewKeyDown(e); }
    protected override void OnPreviewMouseDown(MouseButtonEventArgs e) { ResetWheelToLive(); base.OnPreviewMouseDown(e); }

    void ResetWheelToLive()
    {
        if (wheelToLive.Count > 0) { toastTimer.Stop(); toastBox.Visibility = Visibility.Collapsed; } // countdown message
        wheelToLive.Reset();
    }

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        rightDown = true;
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        rightDown = false;
        ReleaseMouseCapture();
        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        rightDown = false;
        base.OnLostMouseCapture(e);
    }

    // Middle button toggles spanned 1:1 (enter and leave). Always handled, so no default middle-button behavior runs.
    // Modal dialogs block input to this window, so no extra check is needed for them.
    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle) { base.OnMouseDown(e); return; }
        e.Handled = true;
        if (MenuUnderMouse || slotPanel.IsMouseOver || OverOverlay(e)) return;
        HideMenu(); // closes a keyboard-opened menu
        if (auto.On && !spanned) ToggleAutoHold(); // Auto mode: pause on/off instead of spanning
        else ToggleSpanned();
    }

    // Overlays are not hit-testable, so check the position against every visible one.
    bool OverOverlay(MouseEventArgs e) =>
        new FrameworkElement[] { metaOverlay, helpOverlay, emptyBox, toastBox, hintBox, autoPanel }
            .Any(o => o.IsVisible && new Rect(o.RenderSize).Contains(e.GetPosition(o)));

    // Left single click does nothing; a double-click maximizes/restores the normal window (not fullscreen).
    // Clicks on quick-access slots are handled by the slots and never arrive here.
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (MenuUnderMouse) return;
        HideMenu(); // a click outside closes a keyboard-opened menu
        if (e.ClickCount == 2 && !fullscreen && !spanned) Native.ToggleMaximize(Hwnd);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (menu.Visibility == Visibility.Visible) HideMenuIfIdle();
        else if (!spanned && e.GetPosition(canvas).Y <= 8 && !ImageExceedsView()) menu.Visibility = Visibility.Visible;
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        Track(); // resume cursor tracking
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        HideMenuIfIdle();
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        Relayout();
    }

    // ---------- Keys (dispatched via KeyTable) ----------

    protected override void OnKeyDown(KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        // e.g. holding Del must not delete a series; +/- are rate-limited by TiltRepeat instead.
        if (e.IsRepeat && key is not (Key.Left or Key.Right or Key.OemPlus or Key.Add or Key.OemMinus or Key.Subtract)) return;
        keyIsRepeat = e.IsRepeat;
        var mods = Keyboard.Modifiers & ~ModifierKeys.Shift;
        if (key is Key.LeftAlt or Key.RightAlt) mods &= ~ModifierKeys.Alt; // Alt pressed alone
        var entry = KeyTable.Entries.FirstOrDefault(k => k.Modifiers == mods && k.Keys.Contains(key));
        if (entry?.Action == null) return;
        // While the menu has keyboard focus it handles keys itself; only Alt/F10 (toggle) gets through.
        if (menu.IsKeyboardFocusWithin && key is not (Key.LeftAlt or Key.RightAlt or Key.F10)) return;
        entry.Action(this, key);
        e.Handled = true;
    }

    // Keep Windows from entering system-menu mode on Alt/F10 release.
    protected override void OnKeyUp(KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftAlt or Key.RightAlt or Key.F10) e.Handled = true;
        if (key is Key.OemPlus or Key.Add or Key.OemMinus or Key.Subtract) keyRepeat.Reset(); // released: next press is fresh
        base.OnKeyUp(e);
    }

    internal void OpenFolder()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Ordner öffnen",
            InitialDirectory = bitmap != null && dir != null ? dir : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        };
        if (dlg.ShowDialog(this) == true) OpenFolderPath(dlg.FolderName);
    }

    // Shared by the dialog, quick-access slots and menu: enumerate once, show the newest or first image.
    void OpenFolderPath(string folder)
    {
        string? pick = settings.OpenFolderShows == OpenFolderMode.Newest
            ? DirectoryNavigator.Newest(folder)
            : DirectoryNavigator.Neighbor(folder, null, default, 1, sort);
        if (pick == null) { ShowToast("Keine Bilder im Ordner", 3000); return; }
        ShowFileIn(folder, pick);
    }

    // Switches the browsing folder (lazy navigation from here on) and shows the given image.
    void ShowFileIn(string folder, string file)
    {
        dir = folder;
        if (live && !StartWatcher()) SetLive(false); // re-target the watcher to the new folder
        Load(file);
    }

    internal void ShowInExplorer()
    {
        if (currentPath != null && File.Exists(currentPath))
            Process.Start("explorer.exe", $"/select,\"{currentPath}\"");
    }

    internal void Step(int delta) => Step(delta, fromWheel: false);

    void Step(int delta, bool fromWheel)
    {
        if (dir == null) return;
        string? next = DirectoryNavigator.Neighbor(dir, currentPath, currentTime, delta, sort);
        bool atEnd = next != null && bitmap != null && string.Equals(next, currentPath, StringComparison.OrdinalIgnoreCase);
        bool forwardWheelAtEnd = atEnd && fromWheel && delta > 0;
        if (!(forwardWheelAtEnd && live)) SetLive(false); // scrolling on at the last image keeps live mode on
        if (!atEnd) { Load(next); return; }
        wheel.Clear(); // drop unusable wheel deltas so scrolling back does not lag
        if (forwardWheelAtEnd)
        {
            wheelToLive.Pause = TimeSpan.FromSeconds(settings.WheelToLivePauseSeconds);
            // While Auto mode is on the wheel-to-live counter is disabled.
            if (wheelToLive.AtLastImage(next!, live, settings.WheelToLive && !auto.On) is not string msg) { ToggleLive(); return; } // final step: like "L"
            // Countdown messages replace each other and stay for the pause time (so they vanish when the counter expires).
            ShowToast(msg, wheelToLive.Count > 0 ? (int)wheelToLive.Pause.TotalMilliseconds : 1500);
            return;
        }
        ResetWheelToLive();
        ShowToast(delta > 0 ? "Letztes Bild" : "Erstes Bild");
    }

    internal void ToggleSort()
    {
        sort = sort == SortMode.Name ? SortMode.Date : SortMode.Name;
        ShowToast(sort == SortMode.Name ? "Sortierung: Name" : "Sortierung: Datum");
    }

    internal void ToggleLive()
    {
        if (dir == null) return;
        SetLive(!live);
        if (live) JumpToNewest();
    }

    void SetLive(bool on)
    {
        if (live == on) return;
        if (on && !StartWatcher()) return;
        if (!on) StopWatcher();
        ResetWheelToLive();
        live = on;
        UpdateTitle();
        ShowToast(on ? "Live-Modus an" : "Live-Modus aus");
    }

    // (Re)creates the watcher on the current directory.
    bool StartWatcher()
    {
        StopWatcher();
        try
        {
            watcher = new FileSystemWatcher(dir!) { NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite };
            watcher.Created += OnFileEvent;
            watcher.Renamed += OnFileEvent;
            watcher.Changed += OnFileChanged;
            watcher.EnableRaisingEvents = true;
            return true;
        }
        catch (Exception ex)
        {
            StopWatcher();
            ShowToast("Live-Modus nicht möglich: " + ex.Message, 3000);
            return false;
        }
    }

    void StopWatcher()
    {
        watcher?.Dispose();
        watcher = null;
        liveDebounce.Stop();
    }

    // Runs on a thread-pool thread.
    void OnFileEvent(object sender, FileSystemEventArgs e)
    {
        if (!DirectoryNavigator.IsSupported(e.FullPath)) return;
        Dispatcher.InvokeAsync(() =>
        {
            if (!live) return;
            reloadCurrent = false; // a new file wins: jump to the newest
            liveDebounce.Stop();
            liveDebounce.Start();
        });
    }

    // Runs on a thread-pool thread: the current file is still being written, so reload it once writes settle.
    void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        Dispatcher.InvokeAsync(() =>
        {
            if (!live || !string.Equals(e.FullPath, currentPath, StringComparison.OrdinalIgnoreCase)) return;
            if (!liveDebounce.IsEnabled) reloadCurrent = true;
            liveDebounce.Stop();
            liveDebounce.Start();
        });
    }

    void JumpToNewest()
    {
        if (dir == null) return;
        string? newest = DirectoryNavigator.Newest(dir);
        if (newest != null) Load(newest, fromLive: true);
    }

    // ---------- Auto mode ----------

    internal void ToggleAuto() => SetAuto(!auto.On);

    void SetAuto(bool on)
    {
        if (on == auto.On || auto.Toggle(bitmap != null) != on) return; // cannot switch on without an image
        autoPanel.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        if (on) { autoTimer.Start(); AutoTick(); }
        else { autoTimer.Stop(); SaveAutoSeconds(); autoHold = false; }
        ShowToast(on ? "Auto-Modus an" : "Auto-Modus aus");
    }

    // Manual pause (middle button in Auto mode): bar frozen, elapsed time kept until resumed.
    void ToggleAutoHold()
    {
        autoHold = !autoHold;
        AutoTick();
        ShowToast(autoHold ? "Auto-Modus pausiert" : "Auto-Modus läuft weiter");
    }

    // dir: -1 = faster (shorter T), +1 = slower. Elapsed time is kept; expired -> advances right away.
    internal void ChangeAutoSpeed(int dir)
    {
        if (auto.On && auto.ChangeSpeed(dir)) AutoTick();
    }

    // Remembered T goes to the settings only when Auto mode ends or the app closes.
    void SaveAutoSeconds()
    {
        if (settings.AutoSeconds == auto.Seconds) return;
        settings.AutoSeconds = auto.Seconds;
        try { settings.Save(); } catch (Exception) { }
    }

    // ~30 fps while Auto mode is on: pause handling, advance when due, panel update.
    void AutoTick()
    {
        auto.Paused = autoHold || spanned || !Native.IsWindowEnabled(Hwnd); // manual pause, spanned 1:1 or a modal dialog
        if (auto.Due) AutoNext();
        UpdateAutoPanel();
    }

    void UpdateAutoPanel()
    {
        autoText.Text = $"Anzeigezeit = {auto.Seconds} s" + (autoHold ? " (Pause)" : "");
        autoFill.Width = autoTrack.Width * auto.Remaining;
    }

    // Countdown expired: next image (directory read now); at the end a newer image wins, else the end-of-list setting.
    void AutoNext()
    {
        if (dir == null || currentPath == null) { auto.Restart(); return; }
        string cur = currentPath, folder = dir;
        var (show, stop) = AutoAdvance.OnExpired(cur, DirectoryNavigator.Neighbor(folder, cur, currentTime, 1, sort),
            () => DirectoryNavigator.Newest(folder) is string n && File.GetLastWriteTimeUtc(n) > currentTime ? n : null,
            () => DirectoryNavigator.Neighbor(folder, null, default, 1, sort), settings.AutoEnd);
        if (stop) { SetAuto(false); ShowToast("Auto-Modus aus (Ende der Bilder)"); }
        else if (show != null) Load(show); // restarts the countdown
        else auto.Restart();               // "Warten" (or single image): stay, full bar again
    }

    // "+"/"-": first press one step, held (auto-repeat) one step per repeat interval.
    internal void AutoSpeedKey(int dir)
    {
        if (keyRepeat.Key(dir, keyIsRepeat) is int step and not 0) ChangeAutoSpeed(step);
    }

    // Horizontal (tilt) wheel: right = slower (longer T), left = faster. Every message is one tilt event (drivers send
    // bursts and repeat while held); TiltRepeat turns them into one step at once plus one per interval.
    // InvertTilt flips the sign for mice that report right as negative.
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        HwndSource.FromHwnd(Hwnd)?.AddHook((IntPtr _, int msg, IntPtr w, IntPtr _, ref bool handled) =>
        {
            if (msg != 0x020E) return IntPtr.Zero; // WM_MOUSEHWHEEL
            int sign = Math.Sign(unchecked((short)((long)w >> 16))) * (settings.InvertTilt ? -1 : 1); // positive delta = right
            if (tiltRepeat.Tilt(sign) is int step and not 0) ChangeAutoSpeed(step);
            handled = true;
            return IntPtr.Zero;
        });
    }

    internal void ToggleMeta()
    {
        if (bitmap != null) Toggle(metaOverlay);
    }
    internal void ToggleHelp() => Toggle(helpOverlay);
    static void Toggle(UIElement e) =>
        e.Visibility = e.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;

    internal void Escape()
    {
        if (menu.Visibility == Visibility.Visible && !MenuPinned) HideMenu();
        else if (helpOverlay.Visibility == Visibility.Visible) helpOverlay.Visibility = Visibility.Collapsed;
        else if (metaOverlay.Visibility == Visibility.Visible) metaOverlay.Visibility = Visibility.Collapsed;
        else if (spanned) ToggleSpanned();
        else if (fullscreen) ToggleFullscreen();
        else Close();
    }

    internal void Copy(CopyPart part)
    {
        if (bitmap == null) return;
        string? text = parameters == null ? null : part switch
        {
            CopyPart.Prompt => PngMetadata.Prompt(parameters),
            CopyPart.Seed => PngMetadata.Seed(parameters),
            _ => parameters,
        };
        if (string.IsNullOrEmpty(text)) { ShowToast("keine Metadaten"); return; }
        try
        {
            Clipboard.SetText(text);
            ShowToast(part switch { CopyPart.Prompt => "Prompt kopiert", CopyPart.Seed => $"Seed {text} kopiert", _ => "Parameter kopiert" });
        }
        catch (Exception) { ShowToast("Zwischenablage nicht verfügbar"); }
    }

    internal void DeleteCurrent()
    {
        string? path = currentPath;
        if (dir == null || path == null || !File.Exists(path)) return;
        try
        {
            FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin, UICancelOption.DoNothing);
        }
        catch (Exception ex) { ShowToast("Löschen fehlgeschlagen: " + ex.Message, 3000); return; }
        if (File.Exists(path)) return; // cancelled or failed silently
        Load(DirectoryNavigator.AfterRemoval(dir, path, currentTime, sort));
    }
}
