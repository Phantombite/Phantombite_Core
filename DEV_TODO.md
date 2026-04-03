# DEV TODO — PhantomBite Core

## Offen

### Mods anbinden
- [ ] Economy an Core anbinden (READY-System, Commands, Logger)
- [ ] CableWinch an Core anbinden
- [ ] Creatures an Core anbinden
- [ ] Encounter an Core anbinden
- [ ] ServerAddon an Core anbinden
- [ ] Sulvax an Core anbinden
- [ ] SulvaxRespawnRover an Core anbinden

### Verbesserungen
- [ ] Workshop IDs der Mods in DEV_Dependencies verifizieren (alle mit TBD markieren falls noch unbekannt)

---

## Erledigt

### Core System
- [x] Core_Logger mit Log-Level pro Mod (Normal/Debug/Trace)
- [x] Core_FileManager mit GlobalConfig + Log-Datei Management
- [x] Core_Command mit `!pbc` Prefix + Help + Status + Debug + Log Commands
- [x] READY-System: Core schreibt Mods an, Mods registrieren sich
- [x] LOGLEVEL-System: Core teilt Mods ihr Log-Level mit, Mods filtern selbst
- [x] Log-Kanal 1995999: Mods loggen über Messaging in Phantombite-Log
- [x] CMDRESULT-System: Mods bestätigen Commands, Core zeigt HUD-Feedback
- [x] Deduplizierung: gleicher Command + Spieler innerhalb Timeout → einmal
- [x] Timeout-Erkennung: WARN im Log wenn kein CMDRESULT nach 2 Sekunden
- [x] ID-Support: `!pbc artefact 1 on` / `!pbc artefact all reset`
- [x] Admin-only Mod Erkennung: automatisch
- [x] Dev-Mode: Erkennung per Mod-Name statt Workshop-ID
- [x] ModDetector: Session-Typ + Mod-Erkennung + Logging
- [x] ModRegistry: alle Workshop-IDs + Kanäle + Namen zentral

### Artefact angebunden
- [x] Artefact registriert sich beim Core über READY-System
- [x] Commands kommen vom Core, Artefact führt aus
- [x] CMDRESULT mit korrektem Target (ID 0 / ID 1 / alle)
- [x] Artefact loggt über Core-Logger (Kanal 1995999)
- [x] Debug/Trace Logging im Controller vollständig
- [x] ShowNotification Command-Feedback aus Controller entfernt — Core macht das

### Allgemein
- [x] AdminChip aus PhantomBite Economy extrahiert
- [x] Mod auf Steam Workshop veröffentlicht
- [x] GitHub Repository angelegt
- [x] Alle Module von M0x auf Core_Name / Artefact_Name umbenannt