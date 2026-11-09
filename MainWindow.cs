using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
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
    readonly TextBlock emptyText = new()
    {
        Foreground = Brushes.Gray, FontSize = 16, IsHitTestVisible = false,
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
    };
    readonly TextBlock toast = new()
    {
        Foreground = Brushes.White, Background = new SolidColorBrush(Color.FromArgb(0xB0, 0, 0, 0)),
        Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(10), IsHitTestVisible = false,
        HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
        Visibility = Visibility.Collapsed,
    };
    readonly TextBlock metaText = new() { FontFamily = new FontFamily("Consolas"), FontSize = 13, TextWrapping = TextWrapping.Wrap };
    readonly Border metaOverlay, helpOverlay;
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
    bool live, autoFit = true, oneToOne, rightDown, fullscreen;
    double scale = 1, offX, offY;  // scale: image px -> device px; offsets in device px
    (double Scale, double X, double Y, bool AutoFit) beforeOneToOne;
    Point mouse;                   // last mouse position in device px
    int wheelAccum;
    WindowState stateBeforeFullscreen;

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
        metaOverlay.VerticalAlignment = VerticalAlignment.Bottom;
        metaOverlay.Margin = new Thickness(16, 16, 16, 52);
        helpOverlay = Overlay(BuildHelp());
        helpOverlay.HorizontalAlignment = HorizontalAlignment.Center;
        helpOverlay.VerticalAlignment = VerticalAlignment.Center;
        helpOverlay.MaxWidth = 760;
        menu = BuildMenu(out liveItem, out metaItem);
        // The menu is an overlay in the same grid cell, so it never changes the image area size.
        var root = new Grid();
        foreach (var e in new UIElement[] { canvas, emptyText, metaOverlay, helpOverlay, menu, toast }) root.Children.Add(e);
        Content = root;

        canvas.SizeChanged += (_, _) => Relayout();
        toastTimer.Tick += (_, _) => { toastTimer.Stop(); toast.Visibility = Visibility.Collapsed; };
        liveDebounce.Tick += (_, _) => { liveDebounce.Stop(); JumpToNewest(); };
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
        }
        ShowToast("F1 = Hilfe", 3000);
    }

    static Border Overlay(UIElement child)
    {
        var b = new Border
        {
            Child = child, Padding = new Thickness(16, 12, 16, 12), CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(Color.FromArgb(0xC8, 0x10, 0x10, 0x10)),
            IsHitTestVisible = false, Visibility = Visibility.Collapsed,
        };
        TextElement.SetForeground(b, Brushes.White);
        return b;
    }

    static Grid BuildHelp()
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        void Add(UIElement e, int row, int col) { Grid.SetRow(e, row); Grid.SetColumn(e, col); g.Children.Add(e); }

        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var header = new TextBlock { Text = "SimpleViewer – Hilfe", FontSize = 16, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 10) };
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
                new MenuItem { Header = "Datei", Items = { Cmd("Ordner öffnen...", KeyTable.OpenFolder), Cmd("Im Explorer anzeigen", KeyTable.ShowInExplorer), exit } },
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

    // While the image is zoomed beyond the window (or in 1:1), the top edge is used to inspect the image border.
    bool ImageExceedsView()
    {
        if (bitmap == null) return false;
        if (oneToOne) return true;
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

        Task.Run(async () =>
        {
            for (int attempt = 0; ; attempt++)
            {
                if (Volatile.Read(ref loadToken) != token) return; // superseded by a newer load
                try
                {
                    var r = Decode(path);
                    _ = Dispatcher.InvokeAsync(() => Show(token, r.Bitmap, r.Time, r.Parameters));
                    return;
                }
                catch when (attempt < 5) { await Task.Delay(200); } // file may still be being written
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
        emptyText.Text = "";
        metaText.Text = meta ?? "keine Metadaten";
        oneToOne = false;
        autoFit = true;
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
        bitmap = null;
        parameters = null;
        image.Source = null;
        emptyText.Text = message;
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
        toast.Visibility = Visibility.Visible;
        toastTimer.Stop();
        toastTimer.Interval = TimeSpan.FromMilliseconds(ms);
        toastTimer.Start();
    }

    // ---------- Layout / zoom ----------

    double Dpi => VisualTreeHelper.GetDpi(this).DpiScaleX;
    Size View => new(canvas.ActualWidth * Dpi, canvas.ActualHeight * Dpi);

    void Relayout()
    {
        if (bitmap == null) return;
        var v = View;
        if (v.Width <= 0 || v.Height <= 0) return;
        if (oneToOne) PanOneToOne();
        else
        {
            if (autoFit) scale = Math.Min(1, Math.Min(v.Width / bitmap.PixelWidth, v.Height / bitmap.PixelHeight));
            Clamp();
        }
        Apply();
    }

    // Center along an axis if the image is smaller than the view, otherwise keep the view covered.
    void Clamp()
    {
        var v = View;
        double w = bitmap!.PixelWidth * scale, h = bitmap.PixelHeight * scale;
        offX = w <= v.Width ? (v.Width - w) / 2 : Math.Clamp(offX, v.Width - w, 0);
        offY = h <= v.Height ? (v.Height - h) / 2 : Math.Clamp(offY, v.Height - h, 0);
    }

    // 1:1 crop follows the mouse proportionally: view edge = image edge.
    void PanOneToOne()
    {
        var v = View;
        double w = bitmap!.PixelWidth, h = bitmap.PixelHeight;
        scale = 1;
        offX = w <= v.Width ? (v.Width - w) / 2 : -Math.Clamp(mouse.X / v.Width, 0, 1) * (w - v.Width);
        offY = h <= v.Height ? (v.Height - h) / 2 : -Math.Clamp(mouse.Y / v.Height, 0, 1) * (h - v.Height);
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

    void ZoomAt(Point p, double newScale)
    {
        if (bitmap == null) return;
        newScale = Math.Clamp(newScale, 0.02, 50);
        oneToOne = false;
        autoFit = false;
        offX = p.X - (p.X - offX) * newScale / scale;
        offY = p.Y - (p.Y - offY) * newScale / scale;
        scale = newScale;
        Clamp();
        Apply();
    }

    internal void ResetZoom()
    {
        oneToOne = false;
        autoFit = true;
        Relayout();
    }

    internal void ZoomTo100()
    {
        var v = View;
        ZoomAt(new Point(v.Width / 2, v.Height / 2), 1);
    }

    // ---------- Mouse ----------

    Point DevicePos(MouseEventArgs e)
    {
        var p = e.GetPosition(canvas);
        double d = Dpi;
        return new Point(p.X * d, p.Y * d);
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        e.Handled = true;
        if (MenuUnderMouse) return;
        if (rightDown || (Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            ZoomAt(DevicePos(e), scale * Math.Pow(1.2, e.Delta / 120.0));
            return;
        }
        // Accumulate so high-resolution wheels/touchpads step once per notch.
        wheelAccum += e.Delta;
        int notches = wheelAccum / 120;
        wheelAccum -= notches * 120;
        if (notches != 0) Step(-notches);
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
        if (bitmap == null) return;
        mouse = DevicePos(e);
        if (!oneToOne)
        {
            beforeOneToOne = (scale, offX, offY, autoFit);
            oneToOne = true;
        }
        else
        {
            oneToOne = false;
            (scale, offX, offY, autoFit) = beforeOneToOne;
        }
        Relayout();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        mouse = DevicePos(e);
        if (oneToOne && bitmap != null) { PanOneToOne(); Apply(); }
        if (menu.Visibility == Visibility.Visible) HideMenuIfIdle();
        else if (e.GetPosition(canvas).Y <= 8 && !ImageExceedsView()) menu.Visibility = Visibility.Visible;
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
        if (next != null && bitmap != null && string.Equals(next, currentPath, StringComparison.OrdinalIgnoreCase)) return;
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

    internal void ToggleFullscreen()
    {
        fullscreen = !fullscreen;
        if (fullscreen)
        {
            stateBeforeFullscreen = WindowState;
            WindowStyle = WindowStyle.None;
            WindowState = WindowState.Normal; // re-maximize so the borderless window covers the taskbar
            WindowState = WindowState.Maximized;
        }
        else
        {
            WindowStyle = WindowStyle.SingleBorderWindow;
            WindowState = stateBeforeFullscreen;
        }
    }
}
