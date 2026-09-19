# DEV TODO / Roadmap — Phantombite Core

Stand: 2026-09-19

## Roadmap

| Ziel | Status |
|---|---|
| PlanetSpawn-Script aus Core entfernen, eigener Mod `Phantombite_PlanetSpawner` mit eigenem Kanal | erledigt |
| ModDetector überarbeiten (Workshop/Lokal/Hybrid) | erledigt |
| Performance-Überwachung und Mod-Steuerung (`Core_Performance`) | erledigt, im Feldtest (siehe unten) |
| Spieler-Protokoll (`Core_PlayerTracker`) | erledigt |
| Mod-zu-Mod-System (Items anderer Mods nur anbieten, wenn der Mod geladen ist, z. B. Mining-Bomben in Economy) | offen — Core kennt über `ModDetector` alle aktiven Mods, es fehlt die Nachricht an Economy |
| Übersetzung zur Laufzeit für Phantombite-Mods | offen — Idee, noch keine Umsetzung |
| Angleichen an `0_Phantombite_MOD_TEMPLATE.md` (README, patch_notes, thumb.jpg, Ordnerstruktur) | teilweise: README, `.gitignore` und `.gitattributes` sind da; `patch_notes.md` und `thumb.jpg` fehlen noch |

## Offen

- [ ] Nach den Bugfixes vom 2026-09-19 im Spiel testen (Checkliste unten)
- [ ] `Session.VERSION` und `metadata.mod` (steht auf `1.0`) an einen echten Versionsstand angleichen, bevor die nächste Workshop-Version erscheint
- [ ] Prüfen, welche Mods sich wirklich beim Core registrieren (`REGISTER`): laut Stichprobe im Mods-Ordner Artefact, Autotransfer, CableWinch, Economy, Mining, PlanetSpawner, StationRefill, WaterElectrolyzer, AdminProjektor, Creatures. Ohne Anbindung: Encounter, Pandora, ServerAddon, Sulvax, Sulvax_RespawnRover (die beiden letzten und ServerAddon sind reine Daten-Mods)
- [ ] Encounter: braucht MES (`RequiresMES`) — Anbindung an den Core klären
- [ ] Pandora: Kanal (1995014) und Eintrag sind vorhanden, im Mod fehlt aber die Anbindung
- [ ] Workshop-IDs in `ModRegistry.cs` mit den echten Workshop-Seiten abgleichen

## Bekannte Schwächen (bewusst noch nicht geändert)

- **Mods prüfen `STEAM:` nicht selbst:** Core setzt jetzt die echte Absender-ID (siehe „Erledigt“). Die einzelnen Mods sollten sensible Aktionen trotzdem nicht allein auf Client-Angaben stützen.
- `!pbc log copy/show` zeigen nur den aktuellen Log-Teil (bei sehr langen Sitzungen gibt es mehrere Dateien `_Teil2`, `_Teil3` ...).
- **Vier Kopien** von `PadRight`/`CapFirst` in `command`, `Performance`, `playertracker`, `ModDetector` (leicht unterschiedlich). Auslagern wäre sauberer.
- `PlayerTrackerModule` liegt im Namespace `PhantombiteCore.Core`, alle anderen Module in `PhantombiteCore.Modules`.
- Dateinamen der Module haben Leerzeichen und weichen vom Template ab (`Core command.cs` statt `Core_Command.cs`).
- Bestehende GlobalConfigs behalten alte Schlüssel: `Phantombite_CableWinch` und `Phantombite_SulvaxRespawnRover` wurden nie gelesen. Richtig sind `Phantombite_Cable_Winch` und `Phantombite_Sulvax_RespawnRover`. Auf dem Server einmal von Hand anpassen oder `!pbc debug <mod> <n>` zweimal ausführen (schreibt den Schlüssel jetzt nach `[Debug]`).

## Testliste nach dem Update (im Spiel)

1. Server/Welt startet, Core-Log zeigt „Init — 5 Module: Core_Logger, Core_FileManager, Core_Command, Core_Performance, Core_PlayerTracker“.
2. `!pbc status` listet alle aktiven Mods mit Version und Debug-Level.
3. `!pbc debug cablewinch 1` und `!pbc debug autotransfer 1`: Meldung „Level 1“ und der Mod loggt danach mit Level 1 (früher wirkte das bei Mods mit Großbuchstaben im Namen nicht).
4. `!pbc debug <mod> 1` ein zweites Mal: „permanent gespeichert“, in `Phantombite_GlobalConfig.ini` steht der Eintrag unter `[Debug]`.
5. Ein Admin-Command eines Mods ausführen, HUD-Meldung erscheint. Auf dem Server (Multiplayer) als Admin **und** als normaler Spieler testen: Der Spieler muss abgelehnt werden (Log-Warnung „kein Admin“), auch wenn er den Command im Chat tippt. Achtung Host eines Listen-Servers: Auch dort im Log prüfen, dass der eigene Command ankommt.
5a. Sitzung länger laufen lassen: Log-Dateien `Phantombite_<Datum>.log` (und ab ca. 0,8 MB `_Teil2`) erscheinen, `!pbc log show` und `!pbc perf log` zeigen aktuelle Zeilen.
6. `!pbc perf status`: „Externe Drops“ zählt jetzt hoch, wenn die SimSpeed ohne Phantombite-Ursache einbricht.
7. Performance-Verhalten beobachten: Ein Drop zählt jetzt erst nach `StrikeDurationTicks` (90 Ticks ≈ 1,5 s) statt nach 9 Ticks, und das Zuordnungsfenster `HeavyGraceWindowSec` gilt in echten Sekunden (12 s statt etwa 1,2 s). Bei Bedarf beide Werte in der GlobalConfig anpassen.

## Erledigt

### Core-System
- [x] Logger mit Debug-Level pro Mod (0/1/2), Log-Datei mit Rotation
- [x] FileManager mit GlobalConfig (Self-Healing) und adaptivem Schreiben
- [x] Command-System `!pbc` mit Hilfe, Status, Debug, Log, Players, Perf
- [x] READY/REGISTER/LOGLEVEL/CMD/CMDRESULT-Protokoll, Duplikat-Schutz, Timeout-Warnung
- [x] ID-Support (`!pbc artefact 1 on`, `all`), automatische Admin-only-Erkennung
- [x] Dev-Modus (Erkennung per Ordnername), ModDetector mit Workshop/Lokal/Hybrid
- [x] ModRegistry zentral (IDs, Namen, Kanäle)
- [x] Performance-System (SimSpeed, HEAVY-Zuordnung, Eskalation, History)
- [x] PlayerTracker (Join/Leave-Protokoll)
- [x] AdminChip aus Economy herausgelöst; Core auf Workshop veröffentlicht; GitHub-Repo angelegt

### Sicherheit und Log-Dateien 2026-09-19
- [x] `!pbc`-Commands: Server nimmt Pakete nur noch über den sicheren Handler an, kennt den echten Absender, prüft Kanal, Nachrichtentyp, Command und Admin-Recht selbst und überschreibt `STEAM:` mit der echten ID
- [x] Log-Datei wird nicht mehr bei jedem Schreiben gelesen: Inhalt liegt im Speicher, Dateien werden in Teile zu je ca. 0,8 MB geteilt (`_Teil2` ...), es bleiben 20 Dateien
- [x] Player-Log wird einmal geladen, nicht mehr bei jedem Ereignis gelesen, und auf 5000 Zeilen begrenzt

### Bereinigung 2026-09-19
- [x] Debug-Namen der Mods vereinheitlicht (`ResolveLocalName`), Bugs bei CableWinch, SulvaxRespawnRover, AutoTransfer, PlanetSpawner, ServerAddon, StationRefill, AdminProjektor, WaterElectrolyzer behoben (Details in `DEV_History.md`)
- [x] Performance-Zeitberechnung korrigiert, „Externe Drops“-Zähler funktioniert
- [x] Pandora bekommt READY
- [x] Toter Code und veraltete Kommentare entfernt, Doku überarbeitet
- [x] Kompilierung gegen die SE-DLLs geprüft (keine Fehler)
