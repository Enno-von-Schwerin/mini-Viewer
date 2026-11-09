# SimpleViewer

Minimaler Bildbetrachter für Windows (.NET 8, WPF) – nur JPG/JPEG und PNG.
Navigation liest das Verzeichnis bei jedem Schritt neu ein, Live-Modus folgt neu geschriebenen Dateien,
und die A1111/Forge-`parameters` aus PNGs lassen sich anzeigen und kopieren.
Ohne Argument gestartet zeigt es ein leeres Fenster mit Menü; Ordner öffnen mit Strg+O.
Das Menü erscheint sonst, wenn die Maus an den oberen Fensterrand fährt, oder mit Alt/F10.
Am ersten bzw. letzten Bild bleibt die Navigation stehen (kein Umlauf).

## Ansicht: Vollbild, 1:1 über alle Monitore, Mausführung

- **F11**: randloses Vollbild auf dem Monitor, auf dem das Fenster überwiegend liegt (inkl. Taskleistenbereich).
- **Linksklick**: 1:1 über alle Monitore – das Fenster überdeckt den gesamten virtuellen Desktop, 1 Bildpixel = 1 physisches Pixel.
  Erneuter Klick, F11 oder Esc stellt den vorherigen Zustand wieder her.
- **R** ist das Rechteck, das das Bild in Ausgangsgröße (min(1, Einpassen)) auf dem Bildschirm einnimmt.
  Ist das Bild größer als die Fläche, folgt der Ausschnitt der Mausposition innerhalb von R
  (außerhalb von R wird auf den Rand begrenzt): Mauszeiger am Rand von R = Bildrand sichtbar.
  R bleibt beim Zoomen und in der 1:1-Ansicht fest.
- In der 1:1-Ansicht über alle Monitore bezieht sich die Positionierung auf den **Referenzmonitor M**
  (der Monitor, der R enthält): Mauszeiger am linken/oberen Rand von R = linke/obere Bildkante auf der
  linken/oberen Kante von M, am rechten/unteren Rand = rechte/untere Bildkante auf der rechten/unteren Kante von M,
  Mitte von R = Bildmitte in der Mitte von M. Das Bild darf dabei auf Nachbarmonitore hinausragen.
  Passt das Bild in einer Richtung in M, wird es dort auf M zentriert.

## Einstellungen

Datei > Einstellungen... (Strg+,). Gespeichert in `%APPDATA%\SimpleViewer\settings.json`;
fehlende oder ungültige Werte werden still durch Standardwerte ersetzt.
„Hintergrund-Deckkraft“ der Overlays: 0 % = völlig durchsichtig, 100 % = deckend (Standard 70 %).
„Beim Öffnen eines Ordners zuerst anzeigen“: neuestes Bild (Standard) oder erstes Bild der aktuellen Sortierung –
gilt für den Dialog „Ordner öffnen“, die Schnellzugriff-Slots und das Menü.

## Schnellzugriff-Ordner

Bis zu 5 Ordner, einzurichten unter Einstellungen > „Schnellzugriff-Ordner“ (Anzeigename optional, sonst Ordnername).
Ohne geladenes Bild (Start ohne Argument oder nach dem Löschen des letzten Bildes) erscheinen sie als Kacheln am
unteren Fensterrand; Klick öffnet den Ordner, Klick auf einen leeren Slot öffnet die Einstellungen.
Außerdem im Menü unter Datei > Schnellzugriff. Existiert ein Ordner nicht mehr, erscheint „Ordner nicht gefunden“.
Pro Slot lässt sich „Datei-Dialog“ ankreuzen (Standard: aus): Dann öffnet ein Klick auf diesen Slot einen
Datei-Dialog in seinem Ordner; das gewählte Bild wird angezeigt und von dort aus weiter geblättert. Abbrechen ändert nichts.

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
| 0 | Ausgangsgröße |
| 1 | Zoom 100 % (1 Bildpixel = 1 Bildschirmpixel) im aktuellen Fenster |
| C | alles kopieren |
| I | Metadaten |
| L | Live-Modus |
| O | Sortierung Name/Datum |
| P | Prompt kopieren |
| S | Seed kopieren |
| F1 / H | diese Hilfe |
| F11 | Vollbild auf dem aktuellen Monitor (in 1:1 über alle Monitore: zurück) |
| Links / Rechts | vorheriges/nächstes Bild |
| Entf | in den Papierkorb |
| Esc | Menü/Overlay schließen, sonst 1:1 über alle Monitore bzw. Vollbild verlassen, sonst beenden |
| Alt / F10 | Menü einblenden |
| Strg+E | im Explorer anzeigen |
| Strg+O | Ordner öffnen |
| Strg+, | Einstellungen |
| Mausrad | vorheriges/nächstes Bild (kein Umlauf am Anfang/Ende) |
| Rechte Maustaste + Mausrad / Strg + Mausrad | Zoom (Punkt unter dem Mauszeiger bleibt stehen) |
| Maus bewegen | bei Bild größer als die Fläche: Ausschnitt folgt der Maus (Bildrand = Rand von R, der Bildfläche in Ausgangsgröße) |
| Linksklick | 1:1 über alle Monitore umschalten; erneuter Klick = zurück |
| Maus an den oberen Rand | Menü einblenden (nicht bei vergrößertem Bild oder 1:1 über alle Monitore) |
| Klick auf Schnellzugriff (ohne Bild) | Ordner öffnen; leerer Slot: Einstellungen öffnen |
<!-- keys:end -->
