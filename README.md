# SimpleViewer

Minimaler Bildbetrachter für Windows (.NET 8, WPF) – nur JPG/JPEG und PNG.
Navigation liest das Verzeichnis bei jedem Schritt neu ein, Live-Modus folgt neu geschriebenen Dateien,
und die A1111/Forge-`parameters` aus PNGs lassen sich anzeigen und kopieren.
Ohne Argument gestartet zeigt es ein leeres Fenster mit Menü; Ordner öffnen mit Strg+O.
Das Menü erscheint sonst, wenn die Maus an den oberen Fensterrand fährt, oder mit Alt/F10.

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
| Mausrad | vorheriges/nächstes Bild |
| Rechte Maustaste + Mausrad / Strg + Mausrad | Zoom (zum Mauszeiger) |
| Linksklick | 1:1-Ansicht umschalten (Maus bewegen = Ausschnitt verschieben) |
| Maus an den oberen Rand | Menü einblenden (nicht bei 1:1 oder vergrößertem Bild) |
| 0 | Ausgangsgröße |
| 1 | 100 % |
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
| Alt / F10 | Menü einblenden |
| F11 | Vollbild |
| Esc | schließen (offenes Menü/Overlay zuerst) |
| F1 / H | diese Hilfe |
<!-- keys:end -->
