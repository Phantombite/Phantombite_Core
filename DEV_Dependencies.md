# DEV Dependencies — Phantombite Core

Stand: 2026-09-19. Quelle der Wahrheit ist `Data/Scripts/PhantombiteCore/Core/ModRegistry.cs`.
Diese Tabelle nur bei Änderungen dort mitpflegen.

## Dieser Mod benötigt

Nichts. Core hat keine Abhängigkeiten.

- Workshop-ID von Core: **3710485076** (`modinfo.sbmi`, `ModRegistry.Core`)

## Mods, die Core nutzen

| Mod | Workshop-ID | Lokaler Name (Ordner) | Kanal |
|---|---|---|---|
| AdminProjektor | 3706769805 | Phantombite_AdminProjektor | 1995011 |
| Artefact | 3689668016 | Phantombite_Artefact | 1995001 |
| AutoTransfer | 3693780953 | Phantombite_AutoTransfer | 1995009 |
| Cable Winch | 3689668160 | Phantombite_Cable_Winch | 1995002 |
| Creatures | 3728225683 | Phantombite_Creatures | 1995003 |
| Economy | 3728099479 | Phantombite_Economy | 1995004 |
| Encounter | 3689684015 | Phantombite_Encounter | 1995005 |
| Mining | 3719998525 | Phantombite_Mining | 1995013 |
| Pandora | 3723475424 | Phantombite_Pandora | 1995014 |
| PlanetSpawner | 3723481681 | Phantombite_PlanetSpawner | 1995010 |
| Server Addon | 3689667750 | Phantombite_Server_Addon | 1995006 |
| StationRefill | 3723483728 | Phantombite_StationRefill | 1995016 |
| Sulvax | 3691347867 | Phantombite_Sulvax | 1995007 |
| Sulvax RespawnRover | 3692354958 | Phantombite_Sulvax_RespawnRover | 1995008 |
| WaterElectrolyzer | 3708390650 | Phantombite_WaterElectrolyzer | 1995012 |

Der Mod muss nicht alle Kanäle nutzen: Core schickt `READY` nur an Mods, die aktiv sind.
Ob jeder dieser Mods tatsächlich am Core hängt, steht in `DEV_TODO.md`.

## Externe Abhängigkeiten (optional)

| Mod | Workshop-ID | Zweck |
|---|---|---|
| Modular Encounters Systems (MES) | 1521905890 | Benötigt von Encounter (`RequiresMES`). Core warnt im Log, wenn Encounter aktiv ist und MES fehlt |

## Feste Kanäle und Pakete

| Wert | Verwendung |
|---|---|
| 1995000 | Mod → Core: `REGISTER`, `HEAVY_START/END`, `PERFACK` |
| 1995999 | Mod → Core: `LOG`, `CMDRESULT` |
| Paket 5997 | Client → Server: Command-Weitergabe |
| Paket 5998 | Server → Client: Command-Ergebnis für die HUD-Anzeige |

## Links

- Steam Workshop: https://steamcommunity.com/sharedfiles/filedetails/?id=3710485076
- GitHub: https://github.com/Phantombite/Phantombite_Core
