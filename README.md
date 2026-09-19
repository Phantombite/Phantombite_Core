# Phantombite Core

Basis-Mod der **Phantombite Mod Suite** für Space Engineers. Er verwaltet und steuert alle Phantombite-Mods:
ein gemeinsames Command-System, zentrales Logging, globale Konfiguration und eine Performance-Überwachung.
Außerdem enthält er das Item `AdminChip`, das andere Mods als Bauteil nutzen, damit nur Admins bestimmte Blöcke bauen können.

Die Phantombite-Mods laufen auch ohne Core, aber Commands, Log-Level und Performance-Steuerung gibt es nur mit ihm.

## Funktionen
- **Ein Command-System für alle Mods:** `!pbc` (Fallback `/pbc`) mit Hilfe, Rechteprüfung (Admin) und Rückmeldung als HUD-Meldung
- **Zentrales Logging** mit Debug-Level pro Mod (0/1/2) und Log-Dateien (werden bei Größe geteilt, die letzten 20 bleiben)
- **`Phantombite_GlobalConfig.ini`:** Debug-Level und Performance-Einstellungen aller Mods an einer Stelle
- **Performance-Überwachung:** erkennt Einbrüche der Server-Geschwindigkeit, ordnet sie einem Phantombite-Mod zu und schickt ihm ein Sparlevel
- **Spielerprotokoll:** wer wann beigetreten und gegangen ist (nur Dedicated Server)
- **Sicherheit:** Admin-Commands werden auf dem Server geprüft, nicht nur im Client

## Commands
| Command | Wer | Beschreibung |
|---|---|---|
| `!pbc help [Seite]` | alle | Hilfe, 7 Zeilen pro Seite |
| `!pbc help <mod> [Seite]` | alle | Hilfe zu einem Mod |
| `!pbc status` | alle | Registrierte Mods, Versionen und Debug-Level |
| `!pbc players` | alle | Aktive Spieler mit Online-Zeit |
| `!pbc debug <mod\|all> <0\|1\|2>` | Admin | Debug-Level setzen (1× temporär, 2× gleicher Wert = dauerhaft) |
| `!pbc perf status \| log \| reset [mod]` | Admin | Performance-Levels ansehen, Log anzeigen, zurücksetzen |
| `!pbc log show \| copy` | Admin | Letzte Log-Zeilen anzeigen bzw. aktuellen Log-Teil kopieren |
| `!pbc <mod> [ID\|all] <command>` | je Command | Command eines Mods, z. B. `!pbc artefact 1 on` |

## Voraussetzungen
Keine. Core braucht keinen anderen Mod.

## Für Entwickler
Neue Mods binden sich über Nachrichten an den Core an (`READY`, `REGISTER`, `CMD`, `CMDRESULT`, `LOG`, `HEAVY_START/END`).
Beschreibung und Beispiel: `DEV_Anbindung.md`. Aufbau: `DEV_Funktion.md`. Offene Punkte: `DEV_TODO.md`.

Workshop: https://steamcommunity.com/sharedfiles/filedetails/?id=3710485076
