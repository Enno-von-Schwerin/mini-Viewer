# SimpleViewer

Minimaler Bildbetrachter für Windows (.NET 8, WPF) – nur JPG/JPEG und PNG.
Navigation liest das Verzeichnis bei jedem Schritt neu ein, Live-Modus folgt neu geschriebenen Dateien,
und die A1111/Forge-`parameters` aus PNGs lassen sich anzeigen und kopieren.
Ohne Argument gestartet zeigt es ein leeres Fenster mit Menü; Ordner öffnen mit Strg+O.
Das Menü erscheint sonst, wenn die Maus an den oberen Fensterrand fährt, oder mit Alt/F10.
Am ersten bzw. letzten Bild bleibt die Navigation stehen (kein Umlauf).

## Live-Modus per Mausrad

Am letzten Bild (Live-Modus aus) zählt jede weitere Mausrad-Raste vorwärts als Schritt: „Letztes Bild“,
„Live Update Ein (3)“, „(2)“, „(1)“, beim 5. Schritt wird der Live-Modus eingeschaltet wie mit L.
Der Zähler beginnt neu bei zu langer Pause, Zurückscrollen, jeder Taste oder Maustaste, Löschen, Ordnerwechsel
oder manuellem Umschalten des Live-Modus. Die Pfeiltaste Rechts zählt nicht mit.
Einstellungen > Live-Modus: „Live-Modus durch Weiterscrollen am letzten Bild einschalten“ (Standard: an) und
„Pause zwischen Scrollschritten (s)“ (0,5–10, Standard 2,0).

## Auto-Modus (Diashow)

- **A** (oder Ansicht > Auto-Modus) schaltet den Auto-Modus ein/aus; ohne geladenes Bild passiert nichts.
  Jedes Bild wird für die Anzeigezeit T gezeigt, danach folgt das nächste Bild der aktuellen Sortierung
  (Verzeichnis wird in diesem Moment neu gelesen). Wechselt das Bild aus anderem Grund (Blättern, Löschen,
  Live-Modus, Ordner öffnen), beginnt der Countdown von vorn. Sind keine Bilder mehr da, endet der Auto-Modus.
- Blättern mit Mausrad und Pfeiltasten beendet den Auto-Modus nicht, sondern startet den Countdown neu.
  „Mausrad am letzten Bild weiter → Live-Modus“ ist im Auto-Modus abgeschaltet. Esc beendet ihn nicht.
- **Anzeigezeit**: Mausrad rechts (Kippen) oder **+** = langsamer (längere Anzeigezeit), Mausrad links oder **-** = schneller (kürzere Anzeigezeit), in den Stufen
  1–10, 15, 20, 30, 45, 60, 90, 120 s. Die bereits verstrichene Zeit bleibt erhalten; ist sie länger als das neue T,
  wird sofort weitergeschaltet. Das zuletzt benutzte T wird beim Beenden des Auto-Modus bzw. der App gespeichert.
- **Kippen/Halten**: Ein kurzes Kippen des seitlichen Mausrads bzw. ein Tastendruck auf +/- ändert T um genau eine Stufe;
  gehalten folgt alle 0,5 s eine weitere Stufe (Einstellungen > Auto-Modus > „Seitliches Mausrad: Wiederholintervall (ms)“,
  100–1000, Standard 500; gilt auch für +/-). Die Gegenrichtung wirkt sofort; nach 250 ms ohne Kippen gilt das Halten als beendet.
- **Richtung**: „Seitliches Mausrad: Richtung umkehren“ (Standard: an) kehrt das Vorzeichen um, das die Maus meldet – für Mäuse/Treiber,
  bei denen rechts sonst die Anzeigezeit verkürzt. Gilt nur für das seitliche Mausrad, nicht für +/-.
- **Anzeige** unten rechts: „Anzeigezeit = T s“ und ein Balken, der von voll (Countdown-Start) nach links bis leer
  (nächstes Bild) schrumpft. Kurze Meldungen erscheinen darüber.
- **Pause per Maus**: Die mittlere Maustaste schaltet im Auto-Modus die Pause ein/aus (Balken steht, Anzeige „(Pause)“)
  statt der 1:1-Ansicht; Blättern bleibt möglich. Ist die 1:1-Ansicht bereits aktiv, verlässt die mittlere Maustaste sie wie gewohnt.
- **Pause**: Countdown steht außerdem in der 1:1-Ansicht über alle Monitore und solange ein Dialog offen ist; Hilfe/Metadaten pausieren nicht.
- **Ende der Bilder**: Gibt es beim Ablauf am letzten Bild ein neueres Bild, wird es gezeigt. Sonst gilt
  Einstellungen > Auto-Modus > „Am Ende der Bilder“: „Warten“ (Standard; bleibt stehen und nimmt später
  erscheinende Bilder mit), „Von vorne beginnen“ oder „Auto-Modus beenden“.
  Dort auch „Standard-Anzeigezeit (s)“ (Standard 5, Startwert von T).

## Ansicht: Vollbild, 1:1 über alle Monitore, Mausführung

- **F11**: randloses Vollbild auf dem Monitor, auf dem das Fenster überwiegend liegt (inkl. Taskleistenbereich).
- **Mittlere Maustaste**: 1:1 über alle Monitore – das Fenster überdeckt den gesamten virtuellen Desktop, 1 Bildpixel = 1 physisches Pixel.
  Erneuter Klick mit der mittleren Maustaste, F11 oder Esc stellt den vorherigen Zustand wieder her.
  Im Auto-Modus schaltet die mittlere Maustaste stattdessen die Pause ein/aus.
- **Doppelklick** (linke Maustaste, nur im normalen Fenster): Fenster maximieren / wiederherstellen. Ein einfacher Linksklick bewirkt nichts.
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
| A | Auto-Modus (Diashow) ein/aus |
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
| + | Auto-Modus: langsamer (längere Anzeigezeit); gehalten = alle 0,5 s ein Schritt |
| - | Auto-Modus: schneller (kürzere Anzeigezeit); gehalten = alle 0,5 s ein Schritt |
| Alt / F10 | Menü einblenden |
| Strg+E | im Explorer anzeigen |
| Strg+O | Ordner öffnen |
| Strg+, | Einstellungen |
| Mausrad | vorheriges/nächstes Bild (kein Umlauf am Anfang/Ende) |
| Mausrad am letzten Bild weiter | nach 5 Schritten Live-Modus ein (Pause zurücksetzen) |
| Rechte Maustaste + Mausrad / Strg + Mausrad | Zoom (Punkt unter dem Mauszeiger bleibt stehen) |
| Maus bewegen | bei Bild größer als die Fläche: Ausschnitt folgt der Maus (Bildrand = Rand von R, der Bildfläche in Ausgangsgröße) |
| Mittlere Maustaste | 1:1-Ansicht über alle Monitore umschalten (Maus bewegen = Ausschnitt verschieben); im Auto-Modus: Pause ein/aus |
| Doppelklick | Fenster maximieren / wiederherstellen |
| Mausrad links/rechts | Anzeigezeit ändern (Auto-Modus; rechts = länger, links = kürzer); gehalten = alle 0,5 s ein Schritt |
| Maus an den oberen Rand | Menü einblenden (nicht bei vergrößertem Bild oder 1:1 über alle Monitore) |
| Klick auf Schnellzugriff (ohne Bild) | Ordner öffnen; leerer Slot: Einstellungen öffnen |
<!-- keys:end -->
