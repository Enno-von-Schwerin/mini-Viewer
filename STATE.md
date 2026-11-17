# SimpleViewer – current state

Snapshot of 2026-11-17, based on the code in the working tree. HEAD is `bee97b3`; the uncommitted changes
(program icon, permanent zoom display, sorting into subfolders) are included. The program is a minimal image viewer for Windows (C#, .NET 8, WPF).
The UI text is German, so German UI strings are quoted verbatim below.

## 1. Features

**General**
- Shows JPG/JPEG and PNG files only.
- Started with an image path (`SimpleViewer.exe <bild>`), it opens that image without listing the folder.
  Started without an argument, it shows an empty window with the menu pinned, hints ("Ordner öffnen: Strg+O", "Hilfe: F1") and the quick-access tiles.
- Window title: `<file> - <width>x<height>`, plus `  [LIVE]` while live mode is on. The toast "F1 = Hilfe" shows for 3 s at start.
- Files that are still being written are retried every 200 ms: for up to 1 s normally, and up to `LiveMaxWaitSeconds` in live mode.

**Navigation**
- The mouse wheel and the Left/Right keys go to the previous/next image in the same folder (no subfolders).
- The folder is listed again on every step; there is no cached file list, so new files appear immediately.
- Sort order is name (`StrCmpLogicalW`, Explorer order) or last-write time (ties by name). `O` toggles it; the default comes from the settings.
- There is no wrap-around. At the ends the image stays and a toast says "Erstes Bild" / "Letztes Bild".
- If the current file has vanished, navigation continues from its old sort position.
- High-resolution wheels are accumulated to full notches (120 units), so each notch is one step.
- `Entf` moves the image to the recycle bin and then shows the next image, or the previous one if it was the last.

**Sorting into subfolders**
- `Q`/`W`/`E`/`R` (slots 1–4) move the current image into a subfolder of its own folder, then continue like `Entf`.
  - The subfolder is created if missing; its name per slot comes from the settings (default `1`–`4`).
  - Toast: "-> {Ordner}: {Datei}".
- `Strg+Z` moves the most recently sorted file back and shows it. The undo stack holds the last 50 moves of the session.
- Also available under "Datei > In Ordner verschieben": the configured targets with their keys, then "Verschieben rückgängig".

**Zoom, 1:1, multi-monitor**
- Initial scale is `min(1, fit)`: images are never enlarged automatically.
- Zoom with right button + wheel or Ctrl + wheel: factor 1.2 per notch, limited to 2 %–5000 %. The point under the cursor stays fixed, with a 100 ms transition.
- `0` returns to the initial scale; `1` sets 100 % (1 image pixel = 1 physical pixel).
- **R** is the image rectangle at the initial scale. When the image is larger than the area, the visible crop follows the cursor position inside R (clamped to R). R stays fixed while zooming and in 1:1 mode.
- **Zoom display** "Zoom n %" at the bottom right is permanent while the rounded zoom is not 100 % and hidden at 100 % or without an image. It also shows at the initial scale of large images (e.g. "Zoom 21 %").
- `F11`: borderless, topmost fullscreen on the monitor that holds most of the window, including the taskbar area.
- Middle button: spanned 1:1 mode. The window covers the whole virtual desktop at 1:1.
  - Positioning refers to reference monitor **M**, the monitor containing R.
  - Overlays are kept inside M.
  - Navigation keeps spanned mode.
  - Middle button, F11 or Esc restores the previous state (window, maximized or fullscreen).
- Double-click (normal window only) maximizes or restores. A single left click does nothing.
- The process is PerMonitorV2 DPI aware (`app.manifest`); all positions are in physical pixels.

**Live mode**
- `L` toggles live mode. It watches the folder (`FileSystemWatcher`) and jumps to the newest image when switched on.
- Created or renamed images make it jump to the newest file. A change to the current file reloads it. Both go through a 150 ms debounce.
- **Wheel-to-live:** scrolling forward at the last image (live off) counts steps.
  - Messages: "Letztes Bild", then "Live Update Ein (3)", "(2)", "(1)".
  - The 5th step switches live on, like `L`.

**Auto mode (slideshow)**
- `A` or "Ansicht > Auto-Modus" toggles it. It needs a loaded image.
- After the display time T it shows the next image in the current sort order.
- T steps are 1–10, 15, 20, 30, 45, 60, 90 and 120 s.
  - Change T with the tilt wheel or `+`/`-`.
  - One step per tilt or key press; while held, one step per repeat interval (default 500 ms).
- The panel at the bottom right shows "Anzeigezeit = T s" and a bar that shrinks from full to empty. It adds " (Pause)" while paused by the middle button.
- The middle button pauses or resumes Auto mode instead of entering 1:1.
- The end-of-list behavior is configurable; see section 4.

**Overlays and metadata**
- `I` shows the metadata overlay with the A1111/Forge `parameters` text from PNG `tEXt`/`zTXt`/`iTXt` chunks. JPG files have no metadata support.
- `C`/`P`/`S` copy all parameters, the prompt (text before "Negative prompt:") or the seed.
- `F1`/`H` shows the help overlay. It is generated from `KeyTable.cs`.
- All overlays and messages share one style from the settings: font, size, color, background and opacity.
- Messages (toasts) stack at the bottom right, above the zoom display and the Auto panel.

**Menu**
- The menu is hidden. It appears when the mouse reaches the top edge (≤ 8 px), or with `Alt`/`F10`.
- It is not offered at the top edge while the image is larger than the area, and not in spanned mode.
- It is pinned (always visible) until the first image has loaded.
- Entries:
  - "Datei": Ordner öffnen..., Schnellzugriff, Im Explorer anzeigen, In Ordner verschieben, Einstellungen..., Beenden.
  - "Ansicht": Sortierung, Live-Modus ✓, Auto-Modus ✓, Metadaten ✓, Vollbild.
  - "Hilfe (F1)".
- Menu items call the same actions as the keys (`KeyTable`).

**Quick-access folders**
- There are 5 slots, each with a display name, a path and a "Datei-Dialog" option.
- Without an image they appear as tiles at the bottom: in a row, or stacked if the window is too narrow. Configured slots are also listed under "Datei > Schnellzugriff".
- Clicking a slot:
  - shows the newest or first image (setting `OpenFolderShows`);
  - with "Datei-Dialog", opens a file dialog in that folder instead;
  - on an empty slot, opens the settings with that slot focused;
  - on a missing folder, shows the toast "Ordner nicht gefunden".

**Settings**
- The modal dialog "Einstellungen" (`Strg+,`) has the groups Overlays (with a live preview), Ansicht, Schnellzugriff-Ordner, Live-Modus, Auto-Modus and Sortieren.
- Buttons: Standardwerte, Abbrechen, Übernehmen, OK. Changes are applied live, without a restart.

**Installation and command line**
- `--install [--contextmenu]` and `--uninstall` register the viewer under HKCU only (no admin rights) for "Öffnen mit". They never set the default app.
- `--update-readme [README.md]` regenerates the key table between `<!-- keys:start -->` and `<!-- keys:end -->`.
- The program icon `app.ico` (16–256 px) is embedded in the exe. It is used by Explorer, the taskbar, Alt+Tab, the title bar and all `DefaultIcon` entries.

## 2. Key and mouse bindings

Source: `KeyTable.cs`, listed in the order the help overlay and README use.
- Keys are matched with exact modifiers; Shift is ignored. An `Alt` key pressed alone counts as having no modifier.
- Auto-repeat is accepted only for Left/Right and `+`/`-` (OemPlus, Add, OemMinus, Subtract). Holding any other key acts once.
- While the menu has keyboard focus, only Alt/F10 reach the table.
- Mouse rows are text-only; their behavior is coded in `MainWindow.cs`.

| Label | Keys (WPF `Key`) | Modifier | Description (UI text) |
|---|---|---|---|
| 0 | D0, NumPad0 | – | Ausgangsgröße |
| 1 | D1, NumPad1 | – | Zoom 100 % (1 Bildpixel = 1 Bildschirmpixel) im aktuellen Fenster |
| A | A | – | Auto-Modus (Diashow) ein/aus |
| C | C | – | alles kopieren |
| I | I | – | Metadaten |
| L | L | – | Live-Modus |
| O | O | – | Sortierung Name/Datum |
| P | P | – | Prompt kopieren |
| Q / W / E / R | Q, W, E, R | – | Bild in Unterordner verschieben (Ziel je Taste in den Einstellungen, Standard 1–4; Name schon vorhanden: „ (2)“ usw.; danach nächstes Bild; gehalten = nur ein Bild) |
| S | S | – | Seed kopieren |
| F1 / H | F1, H | – | diese Hilfe |
| F11 | F11 | – | Vollbild auf dem aktuellen Monitor (in 1:1 über alle Monitore: zurück) |
| Links / Rechts | Left, Right | – | vorheriges/nächstes Bild |
| Entf | Delete | – | in den Papierkorb |
| Esc | Escape | – | Menü/Overlay schließen, sonst 1:1 über alle Monitore bzw. Vollbild verlassen, sonst beenden |
| + | OemPlus, Add | – | Auto-Modus: langsamer (längere Anzeigezeit); gehalten = alle 0,5 s ein Schritt |
| - | OemMinus, Subtract | – | Auto-Modus: schneller (kürzere Anzeigezeit); gehalten = alle 0,5 s ein Schritt |
| Alt / F10 | LeftAlt, RightAlt, F10 | – | Menü einblenden |
| Strg+E | E | Ctrl | im Explorer anzeigen |
| Strg+O | O | Ctrl | Ordner öffnen |
| Strg+Z | Z | Ctrl | Verschieben rückgängig (bis zu 50 Schritte in dieser Sitzung) |
| Strg+, | OemComma | Ctrl | Einstellungen |
| Mausrad | (mouse) | | vorheriges/nächstes Bild (kein Umlauf am Anfang/Ende) |
| Mausrad am letzten Bild weiter | (mouse) | | nach 5 Schritten Live-Modus ein (Pause zurücksetzen) |
| Rechte Maustaste + Mausrad / Strg + Mausrad | (mouse) | | Zoom (Punkt unter dem Mauszeiger bleibt stehen; Zoomfaktor steht unten rechts, solange er nicht 100 % ist) |
| Maus bewegen | (mouse) | | bei Bild größer als die Fläche: Ausschnitt folgt der Maus (Bildrand = Rand von R, der Bildfläche in Ausgangsgröße) |
| Mittlere Maustaste | (mouse) | | 1:1-Ansicht über alle Monitore umschalten (Maus bewegen = Ausschnitt verschieben); im Auto-Modus: Pause ein/aus |
| Doppelklick | (mouse) | | Fenster maximieren / wiederherstellen |
| Mausrad links/rechts | (mouse) | | Anzeigezeit ändern (Auto-Modus; rechts = länger, links = kürzer); gehalten = alle 0,5 s ein Schritt |
| Maus an den oberen Rand | (mouse) | | Menü einblenden (nicht bei vergrößertem Bild oder 1:1 über alle Monitore) |
| Klick auf Schnellzugriff (ohne Bild) | (mouse) | | Ordner öffnen; leerer Slot: Einstellungen öffnen |

Menu entries reuse these entries: Datei uses `Strg+O`, `Strg+E`, `Q`/`W`/`E`/`R`, `Strg+Z` and `Strg+,`; Ansicht uses `O`, `L`, `A`, `I` and `F11`; Hilfe uses `F1`.
`E` without a modifier sorts into slot 3; `Strg+E` stays "im Explorer anzeigen".

## 3. Settings

They are stored in **`%APPDATA%\SimpleViewer\settings.json`** as indented JSON, with enums as strings.
The environment variable `SIMPLEVIEWER_SETTINGS` overrides this path; the UI tests use it so the real file is never touched.
- Saving is atomic: the app writes a `.tmp` file, then replaces the target.
- On load, every missing, unreadable or out-of-range field falls back to its default on its own. Unknown fields are ignored.
- The legacy field `QuickFolderFileDialog` is used as the per-slot default for `FileDialog`.

| Name | Default | Range / values | UI (group: label) | Effect |
|---|---|---|---|---|
| FontFamily | Consolas | any non-empty name (dialog: installed fonts) | Overlays: Schriftart | Font of all overlays/messages |
| FontSize | 14 | 8–48 | Overlays: Schriftgröße | Overlay font size; also tile width (14×) and Auto bar size (12× wide) |
| FontColor | #FFFFFF | `#RRGGBB` | Overlays: Schriftfarbe | Overlay text, Auto bar fill (track = 30 % of it) |
| OverlayBackground | #000000 | `#RRGGBB` | Overlays: Hintergrundfarbe | Overlay background color |
| OverlayOpacity | 70 | 0–100 (%) | Overlays: Hintergrund-Deckkraft | 0 = transparent, 100 = opaque |
| MetadataPosition | TopLeft | TopLeft, TopRight, BottomLeft, BottomRight, Center | Overlays: Overlay-Position (Metadaten) | Position of the metadata overlay |
| ImageBackground | #000000 | `#RRGGBB` | Ansicht: Hintergrundfarbe der Bildfläche | Image area / window background |
| InvertWheel | false | bool | Ansicht: Mausrad-Richtung beim Blättern umkehren | Inverts wheel navigation (not zoom) |
| DefaultSort | Name | Name, Date | Ansicht: Standard-Sortierung | Sort order at start |
| OpenFolderShows | Newest | Newest, First | Ansicht: Beim Öffnen eines Ordners zuerst anzeigen | First image after opening a folder (dialog, quick access) |
| LiveOnStart | false | bool | Ansicht: Live-Modus beim Start aktivieren | Live mode on at start (watch only, the requested file stays) |
| QuickFolders | 5 empty slots | exactly 5 × {Name, Path, FileDialog} | Schnellzugriff-Ordner: Slot 1–5 | See section 1; paths are checked only on click |
| LiveMaxWaitSeconds | 10 | 1–60 | Live-Modus: Maximale Wartezeit für unfertige Dateien | Retry time for files still being written (live mode) |
| WheelToLive | true | bool | Live-Modus: Live-Modus durch Weiterscrollen am letzten Bild einschalten | Enables wheel-to-live |
| WheelToLivePauseSeconds | 2.0 | 0.5–10 (accepts `,` or `.`) | Live-Modus: Pause zwischen Scrollschritten (s) | Max gap between counted steps |
| AutoSeconds | 5 | 1–10, 15, 20, 30, 45, 60, 90, 120 | Auto-Modus: Standard-Anzeigezeit (s) | Start value of T; also stores the last used T |
| AutoEnd | Wait | Wait, Restart, Stop ("Warten", "Von vorne beginnen", "Auto-Modus beenden") | Auto-Modus: Am Ende der Bilder | End-of-list behavior |
| InvertTilt | true | bool | Auto-Modus: Seitliches Mausrad: Richtung umkehren | Flips the tilt-wheel sign (not `+`/`-`) |
| TiltRepeatMs | 500 | 100–1000 | Auto-Modus: Seitliches Mausrad: Wiederholintervall (ms) | Hold-repeat interval for the tilt wheel and `+`/`-` |
| SortFolders | ["1", "2", "3", "4"] | exactly 4 strings; "" = slot unused; otherwise no `\ / : * ? " < > |` or control characters, not `.`/`..`, ≤ 50 characters, unique (case-insensitive) | Sortieren: Taste Q/W/E/R -> Unterordner | Subfolder name per sort key |

Dialog behavior:
- Invalid colors block "Übernehmen" and "OK" with a message box. So do invalid or duplicate sort folder names; the message names the key.
- SortFolders in the JSON:
  - a non-string or invalid entry keeps that slot's default;
  - extra entries are ignored;
  - a duplicate of an earlier slot falls back to its default, or to "" if that default is taken too.
- An invalid pause or repeat value is replaced by its default, and the field shows that replacement.
- "Standardwerte" only fills the fields; nothing is applied until "Übernehmen" or "OK".

## 4. Behavior rules that are not obvious

**Navigation and live mode**
- Navigation ends live mode: arrows, wheel back and any step that changes the image. The one exception is scrolling forward at the last image while live is on, which keeps it on.
- Live mode is also switched off when the watcher cannot follow a newly opened folder. Failed live loads are silent and keep the current image.
- Wheel-to-live counter:
  - It only counts while live mode is off, the setting is on and **Auto mode is off**.
  - It restarts on:
    - a gap longer than the pause setting;
    - a different "last image";
    - any key or mouse button;
    - a load (navigation, delete, folder open);
    - toggling live mode by hand.
  - The Right arrow does not count.
  - Countdown toasts stay for the pause time, so they disappear when the counter expires.
- `Clear` (no images left) leaves spanned mode and switches Auto mode off.

**Auto mode**
- It is not ended by browsing, live jumps or Esc. Any image change restarts the countdown with a full bar.
- **Pause:** the countdown stops (elapsed time is kept) in three cases:
  - the middle-button pause;
  - spanned 1:1 mode;
  - while a modal dialog has disabled the window (`IsWindowEnabled`).
  The help and metadata overlays do not pause it.
- **Middle button:** with Auto mode on and not spanned, it toggles the pause. When already spanned, it leaves 1:1 as usual. Switching Auto mode off clears the pause.
- **Changing T** keeps the elapsed time. If that is already longer than the new T, the next image follows at once. T stops at both ends of the list.
- **Hold detection:**
  - Tilt: a gap of 250 ms ends the hold. The opposite direction acts at once.
  - Keys: releasing `+`/`-` ends the hold.
  - Repeats stay on a fixed interval grid (no drift).
- **When the countdown ends:**
  1. The next image in sort order is shown (the folder is read at that moment).
  2. At the end, a file newer than the current one wins.
  3. Otherwise `AutoEnd` applies:
     - Wait: stay, the bar refills, and later images are picked up.
     - Restart: go to the first image; with a single image it stays.
     - Stop: Auto mode ends with "Auto-Modus aus (Ende der Bilder)".
- **Saving T:** T is written to `settings.json` only when Auto mode is switched off or the app closes, and only if T changed. The settings dialog shows the current T, and changing it there takes effect at once.
- The tilt wheel and `+`/`-` are consumed even when Auto mode is off (no effect). `WM_MOUSEHWHEEL` is always marked handled.

**Sorting into subfolders**
- **When the keys work:** the sort keys act only while an image is loaded. Auto-repeat is ignored, so a held key moves exactly one file. They work in fullscreen and spanned mode; the menu-focus rule applies as for all keys.
- **Move:** `File.Move` into `<folder of the image>\<name>`, never overwriting. A taken name (file or folder) gets " (2)", " (3)", ... before the extension.
- **Failures:** a locked file, missing permission, or a file in place of the target folder shows "Verschieben fehlgeschlagen: …" and keeps the image. The recycle bin is not used.
- **Unused slot:** only the toast "Slot nicht belegt".
- **After a move:**
  - It continues exactly like `Entf`: next image, else the previous one, else the empty state.
  - Auto mode restarts its countdown, or ends when no image remains.
  - Live mode stays on and does not jump: the watcher ignores directories, even one named like `x.png`.
- **Undo (`Strg+Z`):**
  - It moves the file back to its original folder and name, with the same collision rule.
  - If that folder is still the browsing folder, the file is shown (also from the empty state); a live-mode watcher event for it is ignored once.
  - Otherwise only the toast "Verschieben rückgängig: {full path}" appears.
  - On failure the entry is kept for a retry, unless the file no longer exists.
  - With an empty stack: "Nichts rückgängig zu machen".
- The viewer holds no handle on the shown file (decoded completely, stream disposed), and opens files with `FileShare.Delete`, so a move during loading also works.

**Zoom and view**
- The zoom display uses `Math.Round(scale*100)`, so 99.5–100.4 % counts as 100 % and is hidden. Spanned mode is always 100 % unless zoomed there.
- After a load, a non-spanned view returns to the initial scale (auto-fit). Spanned mode keeps 1:1 and R across navigation.
- Window resize keeps auto-fit until the user zooms. `0` restores auto-fit (not while spanned).
- Fullscreen and spanned save the exact window state. Snapped windows are restored by their visible rectangle, maximized windows by their placement.
- F11 while spanned leaves spanned first.
- The cursor is polled (15 ms) only while the crop depends on it (image larger than the area, window active or spanned) or during a zoom transition.

**Mouse, keys and overlays**
- The middle button is ignored over the menu, the quick-access tiles and any visible overlay, including the zoom display and the Auto panel.
- `Esc` order: hide the menu, then help, then metadata, then leave spanned, then leave fullscreen, then close the app.
- `Entf` uses the Windows recycle bin; if the user cancels, nothing changes. Holding `Entf` does not delete a series, because auto-repeat is ignored.
- Opening a folder (dialog, quick access) sets the browsing folder; from then on navigation is lazy in that folder. "Keine Bilder im Ordner" appears as a toast and the current image stays.

## 5. Changes made by hand after the last prompt-driven extension

**None found.**

How this was checked:
- Git cannot separate hand edits from generated code: all 9 commits are by Enno and bundle whole prompt rounds.
- Instead, the history was compared with the Claude Code session log, which covers the project from the first prompt.
- Every "file changed on disk" notice for a project file traces back to the assistant's own edits or to `--update-readme`.
- When the files were next read after `bee97b3` (2026-11-16), their sizes matched the committed versions.
- The working tree after the last extension (permanent zoom display) contains only prompt-driven changes.

Commit → content, for orientation:

| Commit | Date | Content |
|---|---|---|
| 465b575 | 2026-11-09 | Initial commit (README stub) |
| 60e18c1 | 2026-11-09 | Initial viewer: navigation, zoom, live mode, metadata, installer, KeyTable |
| 30efe3f | 2026-11-09 | Settings (JSON + dialog), shared overlay style, fullscreen/spanned 1:1, menu |
| 3e56ff0 | 2026-11-09 | OverlayTransparency → OverlayOpacity ("Hintergrund-Deckkraft") |
| 542617b | 2026-11-09 | Quick-access folders, empty state, open-folder setting |
| cce8ffa | 2026-11-09 | Per-slot "Datei-Dialog" option |
| 05077c4 | 2026-11-09 | Mouse controls (double-click maximize, middle-button rules), key table update |
| 730526e | 2026-11-09 | Wheel-to-live (`WheelToLive.cs`, settings) |
| bee97b3 | 2026-11-11 | Auto mode, reversed T direction, middle-button pause, tilt debounce/inversion (`AutoAdvance.cs`, `TiltRepeat.cs`) |
| (uncommitted) | 2026-11-16 | Program icon (`app.ico`, `<ApplicationIcon>`), permanent zoom display |
| (uncommitted) | 2026-11-17 | Sorting into subfolders: Q/W/E/R, Strg+Z, "Sortieren" settings, `SortMove.cs`, `SIMPLEVIEWER_SETTINGS` |

## 6. Known limitations, untested and open items

**Limitations**
- Only JPG/JPEG/PNG are supported; there are no subfolders and no wrap-around. Metadata is read from PNG `parameters` only, with no EXIF.
- Some help texts are hard-coded and do not follow the code or settings:
  - "gehalten = alle 0,5 s" stays the same when `TiltRepeatMs` changes;
  - "nach 5 Schritten" is not taken from `WheelToLive.RequiredSteps`.
- Several instances share `settings.json`. Saving writes the instance's whole settings object, so the last writer wins.
- The tests are not in the repository:
  - the unit tests (console harness covering DirectoryNavigator, PngMetadata, Settings, WheelToLive, AutoAdvance and TiltRepeat);
  - the UI Automation scripts (PowerShell).
  Both lived only in the temporary session scratchpad.
- The working copy uses LF line endings with `core.autocrlf=true`, so Git warns on every diff.
- Nothing since `bee97b3` is committed: `app.ico`, `SortMove.cs`, `KeyTable.cs`, `MainWindow.cs`, `Settings.cs`, `SettingsWindow.cs`, `README.md`, `SimpleViewer.csproj` and this file.
- Sort folder names are not checked against reserved device names (`CON`, `NUL`, …) or trailing dots, which Windows strips.
- The undo stack is not persisted. Undo does not check whether the browsing folder was re-opened in between; only the path matters.

**Untested or open**
- `--install` / `--uninstall` were not run against the real registry, so the `DefaultIcon` and context-menu icon have not been seen in Explorer.
- The tilt wheel and middle-button pause were tested only with synthetic input (SendInput). Behavior on the real mouse and driver (direction, bursts) depends on the user's hardware.
- Zoom display:
  - together with a visible Auto panel;
  - in F11 fullscreen;
  - when zooming while spanned (the display should sit inside M).
- The 99.5–100.4 % rounding edge of the zoom display (hidden by design).
- The Auto-mode pause during non-WPF modal dialogs other than those used in tests (e.g. the recycle-bin error dialog).
- Behavior with very large folders: every step lists the folder again, and no performance tests were done.
- Sorting:
  - a move failing for lack of permission (only the locked-file case was tested);
  - network drives and other volumes;
  - the settings dialog validation in the real UI (the validation code is unit-tested; the dialog itself was not driven).

## 7. Source files

| File | Purpose |
|---|---|
| `Program.cs` | Entry point; command-line commands (`--install`, `--uninstall`, `--update-readme`), console/message-box output |
| `MainWindow.cs` | The window: loading, layout/zoom/R mapping, fullscreen/spanned, mouse/keys, menu, overlays, live and Auto mode, quick access |
| `KeyTable.cs` | Key/mouse table: dispatch, help overlay, menu actions, README Markdown, display order |
| `Settings.cs` | Settings model, per-field validated load, atomic save, `QuickFolder` record |
| `SettingsWindow.cs` | "Einstellungen" dialog (code-only WPF) with preview and validation |
| `DirectoryNavigator.cs` | Stateless folder listing, sort (name/date), neighbor/after-removal/newest lookup |
| `PngMetadata.cs` | Reads the `parameters` text chunk (tEXt/zTXt/iTXt) from PNGs; prompt/seed extraction |
| `WheelToLive.cs` | `WheelAccumulator` (notches) and the wheel-to-live counter (injectable clock) |
| `AutoAdvance.cs` | Auto mode timing: T list, countdown, pause, speed change, end-of-list decision (injectable clock) |
| `SortMove.cs` | Sort-folder name validation, duplicate fix-up, collision-free target path, move; `MoveHistory` undo stack (50) |
| `TiltRepeat.cs` | Rate limit for tilt wheel and `+`/`-`: immediate first step, one per interval while held |
| `Native.cs` | Win32 interop: cursor, monitors, virtual screen, window placement, maximize, `IsWindowEnabled` |
| `Installer.cs` | HKCU registration for "Öffnen mit", ProgId, capabilities, optional context menu, uninstall |
| `SimpleViewer.csproj` | .NET 8 WPF WinExe; manifest and icon; no NuGet packages |
| `app.manifest` | PerMonitorV2 DPI awareness |
| `app.ico` | Program icon (16, 20, 24, 32, 40, 48, 64, 256 px) |
| `nuget.config` | nuget.org as the only package source |
| `README.md` | User documentation (German); key table generated from `KeyTable.cs` |
| `.gitignore` | `bin/`, `obj/`, `.vs/` |
