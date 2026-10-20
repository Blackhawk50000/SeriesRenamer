# SeriesRenamer

Ein kleines Windows-Tool (WPF, .NET 10), das Serien-Videodateien anhand von
[The Movie Database (TMDb)](https://www.themoviedb.org/) sauber umbenennt.

Aus `breaking.bad.s01e05.1080p.web.mkv` wird zum Beispiel
`Breaking Bad - S01E05 - Gray Matter.mkv` (mit Sprache en-US).

## Funktionen

- **Drag & Drop** von Videodateien oder ganzen Ordnern
- **Automatische Erkennung** von Serie, Staffel und Episode aus dem Dateinamen
  (`S01E05`, `s1e5`, `S01.E05`, `1x05`, Doppelfolgen wie `S01E05E06`) –
  bei Bedarf auch über den Ordnernamen (`Breaking Bad\Season 1\05.mkv`)
- **Abgleich mit TMDb** für Seriennamen, Jahr und Episodentitel in der gewünschten Sprache
  (de-DE, en-US, fr-FR, es-ES, ja-JP)
- **Serie manuell festlegen**, falls die automatische Erkennung danebenliegt
- **Alternative Ausstrahlungsreihenfolgen** (TMDb „Episode Groups“, z. B. DVD-, Produktions-
  oder absolute Reihenfolge), wenn die Dateien nicht nach der normalen TMDb-Staffelzählung nummeriert sind
- **Vorschau** der neuen Namen vor dem Umbenennen
- **Rückgängig** machen der letzten Umbenennung
- Ungültige Zeichen für Windows-Dateinamen werden automatisch ersetzt

## Voraussetzungen

- Windows
- [.NET 10 SDK](https://dotnet.microsoft.com/download) (zum Bauen) bzw. .NET 10 Desktop Runtime (zum Ausführen)
- Ein kostenloser **TMDb API-Key** – erhältlich unter
  [themoviedb.org/settings/api](https://www.themoviedb.org/settings/api).
  Es funktionieren sowohl der v3-API-Key als auch der v4 „API Read Access Token“.

## Bauen und starten

```
git clone https://github.com/Blackhawk50000/SeriesRenamer.git
cd SeriesRenamer
dotnet run --project SeriesRenamer
```

Alternativ `SeriesRenamer.slnx` in Visual Studio öffnen.

## Benutzung

1. TMDb API-Key und Sprache unter **Einstellungen** eintragen.
2. Videodateien oder Ordner in das Fenster ziehen.
3. **Mit TMDb abgleichen** klicken und die Vorschau unter „Neue Namen“ prüfen.
4. Falls eine Serie falsch erkannt wurde: Dateien markieren, unter
   **Serie manuell festlegen** suchen und **Für Auswahl übernehmen**.
5. Falls die Dateien nach einer anderen Reihenfolge nummeriert sind (z. B. DVD-Reihenfolge):
   Dateien der Serie markieren, unter **Ausstrahlungsreihenfolge** die passende Reihenfolge
   wählen und **Für Auswahl übernehmen**. Staffel und Episode aus dem Dateinamen werden dann
   in dieser Reihenfolge nachgeschlagen (Staffel = Gruppen-Nummer bei TMDb, wie bei Kodi/Jellyfin).
6. **Dateien umbenennen** – bei Bedarf mit **Rückgängig** zurücksetzen.

## Benennungsmuster

Standard: `{Serienname} - S{Staffel:00}E{Episode:00} - {EpisodenTitel}`

| Platzhalter       | Bedeutung                                   |
|-------------------|---------------------------------------------|
| `{Serienname}`    | Name der Serie                              |
| `{Staffel}`       | Staffelnummer                               |
| `{Episode}`       | Episodennummer (Doppelfolgen: `05-E06`)     |
| `{SxxExx}`        | Kurzform, z. B. `S01E05`                    |
| `{EpisodenTitel}` | Titel der Episode(n)                        |
| `{Jahr}`          | Erscheinungsjahr der Serie                  |
| `{Original}`      | Ursprünglicher Dateiname (ohne Endung)      |

Zahlen lassen sich formatieren: `{Staffel:00}` → `01`, `{Episode:0}` → `5`.
Groß-/Kleinschreibung der Platzhalter spielt keine Rolle.

## Einstellungen

API-Key, Sprache und Muster werden lokal unter
`%AppData%\SeriesRenamer\settings.json` gespeichert und nicht ins Repository übernommen.

## Hinweis

Dieses Produkt nutzt die TMDb-API, ist aber nicht von TMDb unterstützt oder zertifiziert.

## Lizenz

[MIT](LICENSE)
