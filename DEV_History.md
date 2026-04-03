# DEV History — PhantomBite Core

## 2026-03-27 — v2.0.0 — Core System komplett neu gebaut

### Core_Logger (M01)
- Zentraler Logger mit Log-Level pro Mod (Normal/Debug/Trace)
- Buffer-System: alle 5 Sekunden in Log-Datei schreiben
- Log-Kanal 1995999: externe Mods können über Messaging loggen

### Core_FileManager (M02)
- GlobalConfig (`Phantombite_GlobalConfig.ini`) mit Debug-Level pro Mod
- Log-Datei pro Start (`Phantombite_Core_DATUM_UHRZEIT.log`), max 20 Logs
- Helfer-API für andere Mods: ReadFile, WriteFile, ParseINI, GetValue

### Core_Command (M03)
- Command-System mit Prefix `!pbc`
- Mod-Registrierung über READY-System beim Start
- ID-Support: `!pbc artefact 1 on` / `!pbc artefact all reset`
- CMDRESULT-System: Mods bestätigen Ausführung, Core zeigt HUD-Feedback
- Deduplizierung: gleicher Command + gleicher Spieler → nicht doppelt ausgeführt
- Timeout-Erkennung: kein CMDRESULT nach 2 Sekunden → WARN im Log
- Log-Kanal empfang: schreibt Mod-Logs in Phantombite-Log
- Debug-Toggle: 1x = temporär, 2x = permanent in GlobalConfig
- Admin-only Mod Erkennung: automatisch, kein manuelles Flag nötig

### ModDetector
- Erkennt Session-Typ: Singleplayer, Dedicated Server, Client
- Dev-Mode: Erkennung per Mod-Name statt Workshop-ID
- Loggt alle aktiven Mods in 3 Sektionen + MES-Warnung

### ModRegistry
- Alle Workshop-IDs und lokalen Namen zentral
- Alle Messaging-Kanäle zentral

### Core_PlanetSpawner (M99)
- Sulvax Planet Spawner — optional, nur wenn Sulvax aktiv

### Umbenennung der Module
- Alle Module von `M0x_Name` auf `Core_Name` / `Artefact_Name` umbenannt
- Dateinamen angepasst für bessere Lesbarkeit im Log

---

## 2026-03-22 — v1.0.0 — Initialer Release

- AdminChip aus PhantomBite Economy extrahiert und als eigenständiger Core Mod veröffentlicht
- Steam Workshop ID: 3689625814
- GitHub Repository: https://github.com/Phantombite/PhantomBiteCore
- MIT License