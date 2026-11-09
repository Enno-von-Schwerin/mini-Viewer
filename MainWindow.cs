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
    readonly Border metaOverlay, helpOverlay, emptyBox, toastBox;
    Settings settings = Settings.Load();
    readonly Menu menu;
    readonly MenuItem liveItem, metaItem;
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
    int wheelAccum;

    // Viewport mapping: R = image rect at initial scale (screen px), E = R grown by settings.MouseMargin % per side.
    Rect refRect;
    bool reloadCurrent;            // live mode: pending debounce reloads the current file (Changed) instead of the newest
    readonly DispatcherTimer tracker = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(15) };
    DateTime? animStart;           // zoom transition: from (animFromX, animFromY) to the mapped offset over AnimMs
    double animFromX, animFromY;
    const double AnimMs = 100;

    readonly Grid overlayHost = new(); // holds all overlays; restricted to one monitor while spanned
    Native.WINDOWPLACEMENT placementBeforeFullscreen;
    (bool Fullscreen, Native.WINDOWPLACEMENT Placement, Rect Bounds, double Scale, bool AutoFit) beforeSpan;
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
        toastBox.Margin = new Thickness(10);
        toastBox.HorizontalAlignment = HorizontalAlignment.Right;
        toastBox.VerticalAlignment = VerticalAlignment.Bottom;
        menu = BuildMenu(out liveItem, out metaItem);
        // Menu and overlays live in a layer above the canvas, so they never change the image area size.
        foreach (var e in new UIElement[] { emptyBox, metaOverlay, helpOverlay, menu, toastBox }) overlayHost.Children.Add(e);
        overlayHost.SizeChanged += (_, _) =>
        {
            metaOverlay.MaxWidth = Math.Max(200, overlayHost.ActualWidth * 0.6);
            helpOverlay.MaxWidth = Math.Max(200, overlayHost.ActualWidth - 32);
        };
        sort = settings.DefaultSort;
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
        Closed += (_, _) => watcher?.Dispose();
        Deactivated += (_, _) => { if (!AnySubmenuOpen) HideMenu(); };

        if (path == null)
        {
            Clear("Ordner öffnen mit Strg+O");
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

    internal static Brush OverlayBrush(Color c, int transparencyPercent) =>
        new SolidColorBrush(Color.FromArgb((byte)Math.Round(255 * (100 - transparencyPercent) / 100.0), c.R, c.G, c.B));

    // Applies settings live: shared overlay style, image-area background, metadata position, mouse margin.
    void ApplySettings(Settings s)
    {
        settings = s;
        var fg = new SolidColorBrush(Settings.ParseColor(s.FontColor) ?? Colors.White);
        var bg = OverlayBrush(Settings.ParseColor(s.OverlayBackground) ?? Colors.Black, s.OverlayTransparency);
        var font = new FontFamily(s.FontFamily);
        foreach (var b in new[] { metaOverlay, helpOverlay, emptyBox, toastBox })
        {
            b.Background = bg;
            TextElement.SetForeground(b, fg);
            TextElement.SetFontFamily(b, font);
            TextElement.SetFontSize(b, s.FontSize);
        }
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

    internal void OpenSettings() => new SettingsWindow(settings.Clone(), ApplySettings) { Owner = this }.ShowDialog();

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
        foreach (var k in KeyTable.Entries)
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
        var view = new MenuItem
        {
            Header = "Ansicht",
            Items = { Cmd("Sortierung: Name/Datum", KeyTable.Sort), live, meta, Cmd("Vollbild", KeyTable.Fullscreen) },
        };
        view.SubmenuOpened += (_, _) =>
        {
            liveItem.IsChecked = this.live;
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
                        Cmd("Ordner öffnen...", KeyTable.OpenFolder), Cmd("Im Explorer anzeigen", KeyTable.ShowInExplorer),
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

    void Clear(string message)
    {
        if (spanned) ToggleSpanned(); // nothing to span without an image
        bitmap = null;
        parameters = null;
        image.Source = null;
        emptyText.Text = message;
        emptyBox.Visibility = Visibility.Visible;
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

    // Offset the image should have: cursor-mapped via E on axes where it exceeds the area, else centered on R.
    (double X, double Y) MappedOffset()
    {
        var v = View;
        var o = ClientOrigin;
        var c = Native.CursorPos();
        var r = refRect;
        double m = settings.MouseMargin / 100.0;
        var e = Rect.Inflate(r, r.Width * m, r.Height * m);
        static double Axis(double img, double area, double cursor, double eStart, double eSize, double rCenter) =>
            img > area
                ? -Math.Clamp(eSize > 0 ? (cursor - eStart) / eSize : 0.5, 0, 1) * (img - area)
                : Math.Clamp(rCenter - img / 2, 0, area - img);
        return (Axis(bitmap!.PixelWidth * scale, v.Width, c.X, e.X, e.Width, r.X + r.Width / 2 - o.X),
                Axis(bitmap.PixelHeight * scale, v.Height, c.Y, e.Y, e.Height, r.Y + r.Height / 2 - o.Y));
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

    internal void ToggleFullscreen()
    {
        if (spanned) { ToggleSpanned(); return; } // F11 while spanned returns to the previous state first
        fullscreen = !fullscreen;
        if (fullscreen)
        {
            placementBeforeFullscreen = Native.GetPlacement(Hwnd);
            var monitor = Native.MonitorOfWindow(Hwnd); // read before changing anything
            SetBorderless(true);
            Native.Place(Hwnd, monitor);
        }
        else
        {
            SetBorderless(false);
            Native.SetPlacement(Hwnd, placementBeforeFullscreen);
        }
        Relayout();
    }

    // Spanned 1:1: the window covers the whole virtual desktop; R/E stay where they were.
    internal void ToggleSpanned()
    {
        if (!spanned)
        {
            if (bitmap == null) return;
            Relayout(); // make sure R is current
            HideMenu();
            beforeSpan = (fullscreen, Native.GetPlacement(Hwnd), Native.WindowRect(Hwnd), scale, autoFit);
            spanned = true;
            scale = 1;
            autoFit = false;
            var vs = Native.VirtualScreen();
            SetBorderless(true);
            Native.Place(Hwnd, vs);
            // Keep overlays inside the monitor that contains R (the bounding box may have areas without a monitor).
            var m = Native.MonitorOfRect(refRect);
            double d = Dpi;
            overlayHost.Margin = new Thickness((m.Left - vs.Left) / d, (m.Top - vs.Top) / d, (vs.Right - m.Right) / d, (vs.Bottom - m.Bottom) / d);
        }
        else
        {
            var b = beforeSpan;
            spanned = false;
            (scale, autoFit) = (b.Scale, b.AutoFit);
            overlayHost.Margin = new Thickness(0);
            if (b.Fullscreen) Native.Place(Hwnd, b.Bounds);
            else
            {
                SetBorderless(false);
                Native.SetPlacement(Hwnd, b.Placement);
            }
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
        // Accumulate so high-resolution wheels/touchpads step once per notch.
        wheelAccum += e.Delta;
        int notches = wheelAccum / 120;
        wheelAccum -= notches * 120;
        if (notches != 0) Step(settings.InvertWheel ? notches : -notches); // inversion affects navigation only
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

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (MenuUnderMouse) return;
        HideMenu(); // a click outside closes a keyboard-opened menu
        ToggleSpanned();
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
        if (e.IsRepeat && key is not (Key.Left or Key.Right)) return; // e.g. holding Del must not delete a series
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
        base.OnKeyUp(e);
    }

    internal void OpenFolder()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Ordner öffnen",
            InitialDirectory = bitmap != null && dir != null ? dir : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        };
        if (dlg.ShowDialog(this) != true) return;
        string? first = DirectoryNavigator.Neighbor(dlg.FolderName, null, default, 1, sort);
        if (first == null) { ShowToast("Keine Bilder im Ordner", 3000); return; }
        dir = dlg.FolderName;
        if (live && !StartWatcher()) SetLive(false); // re-target the watcher to the new folder
        Load(first);
    }

    internal void ShowInExplorer()
    {
        if (currentPath != null && File.Exists(currentPath))
            Process.Start("explorer.exe", $"/select,\"{currentPath}\"");
    }

    internal void Step(int delta)
    {
        if (dir == null) return;
        SetLive(false);
        string? next = DirectoryNavigator.Neighbor(dir, currentPath, currentTime, delta, sort);
        if (next != null && bitmap != null && string.Equals(next, currentPath, StringComparison.OrdinalIgnoreCase))
        {
            wheelAccum = 0; // drop unusable wheel deltas so scrolling back does not lag
            ShowToast(delta > 0 ? "Letztes Bild" : "Erstes Bild");
            return;
        }
        Load(next);
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
