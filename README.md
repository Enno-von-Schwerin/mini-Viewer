# SimpleViewer

Minimaler Bildbetrachter für Windows (.NET 8, WPF) – nur JPG/JPEG und PNG.
Navigation liest das Verzeichnis bei jedem Schritt neu ein, Live-Modus folgt neu geschriebenen Dateien,
und die A1111/Forge-`parameters` aus PNGs lassen sich anzeigen und kopieren.
Ohne Argument gestartet zeigt es ein leeres Fenster mit Menü; Ordner öffnen mit Strg+O.
Das Menü erscheint sonst, wenn die Maus an den oberen Fensterrand fährt, oder mit Alt/F10.
Am ersten bzw. letzten Bild bleibt die Navigation stehen (kein Umlauf).

## Ansicht: Vollbild, 1:1 über alle Monitore, Mausbereich

- **F11**: randloses Vollbild auf dem Monitor, auf dem das Fenster überwiegend liegt (inkl. Taskleistenbereich).
- **Linksklick**: 1:1 über alle Monitore – das Fenster überdeckt den gesamten virtuellen Desktop, 1 Bildpixel = 1 physisches Pixel.
  Erneuter Klick, F11 oder Esc stellt den vorherigen Zustand wieder her.
- **R** ist das Rechteck, das das Bild in Ausgangsgröße (min(1, Einpassen)) auf dem Bildschirm einnimmt;
  **E** ist R, auf jeder Seite um den „Mausbereich über Bildrand hinaus“ (Standard 10 %) vergrößert.
  Ist das Bild größer als die Fläche, folgt der Ausschnitt der Mausposition innerhalb von E:
  Mauszeiger am Rand von E = Bildrand sichtbar. R und E bleiben beim Zoomen und in der 1:1-Ansicht fest.

## Einstellungen

Datei > Einstellungen... (Strg+,). Gespeichert in `%APPDATA%\SimpleViewer\settings.json`;
fehlende oder ungültige Werte werden still durch Standardwerte ersetzt.
„Hintergrund-Deckkraft“ der Overlays: 0 % = völlig durchsichtig, 100 % = deckend (Standard 70 %).

## Build / Publish

```
dotnet publish SimpleViewer.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true
```

Ergebnis: `bin\Release\net8.0-windows\win-x64\publish\SimpleViewer.exe`

## Installation (nur HKCU, keine Adminrechte)

```
SimpleViewer.exe --install                 # erscheint unter "Öffnen mit"
SimpleViewer.exe --install --contextmenu   # zusätzlich Kontextmenü-Eintrag für Bilder
SimpleViewer.exe --uninstall
```

Die exe vorher an ihren endgültigen Ort kopieren – der Pfad wird in die Registry geschrieben.
Als Standard-App muss SimpleViewer einmalig über „Öffnen mit > Immer“ oder die Windows-Einstellungen gewählt werden.

## Tasten und Maus

Die Tabelle wird aus `KeyTable.cs` erzeugt (`SimpleViewer.exe --update-readme README.md`), nicht von Hand bearbeiten.

<!-- keys:start -->
| Taste / Maus | Aktion |
|---|---|
| Mausrad | vorheriges/nächstes Bild (kein Umlauf am Anfang/Ende) |
| Rechte Maustaste + Mausrad / Strg + Mausrad | Zoom (Punkt unter dem Mauszeiger bleibt stehen) |
| Maus bewegen | bei Bild größer als die Fläche: Ausschnitt folgt der Maus (Bildrand = Rand des Mausbereichs) |
| Linksklick | 1:1 über alle Monitore umschalten; erneuter Klick = zurück |
| Maus an den oberen Rand | Menü einblenden (nicht bei vergrößertem Bild oder 1:1 über alle Monitore) |
| 0 | Ausgangsgröße |
| 1 | Zoom 100 % (1 Bildpixel = 1 Bildschirmpixel) im aktuellen Fenster |
| Links / Rechts | vorheriges/nächstes Bild |
| O | Sortierung Name/Datum |
| L | Live-Modus |
| I | Metadaten |
| C | alles kopieren |
| P | Prompt kopieren |
| S | Seed kopieren |
| Entf | in den Papierkorb |
| Strg+O | Ordner öffnen |
| Strg+E | im Explorer anzeigen |
| Strg+, | Einstellungen |
| Alt / F10 | Menü einblenden |
| F11 | Vollbild auf dem aktuellen Monitor (in 1:1 über alle Monitore: zurück) |
| Esc | Menü/Overlay schließen, sonst 1:1 über alle Monitore bzw. Vollbild verlassen, sonst beenden |
| F1 / H | diese Hilfe |
<!-- keys:end -->
