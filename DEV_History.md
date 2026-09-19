# DEV History — Phantombite Core

## 2026-09-19 — Bereinigung (noch nicht veröffentlicht, Code-Version weiter 2.0.0)

Durchgang durch den gesamten Core-Code. Kompiliert fehlerfrei gegen die SE-DLLs (LangVersion 6).

### Behobene Fehler
- **Debug-Level wirkte bei vielen Mods nicht:** Core baute Namen mit `"Phantombite_" + Großbuchstabe`
  (`Phantombite_Autotransfer`), die Config nutzt aber `Phantombite_AutoTransfer`, `Phantombite_Cable_Winch`,
  `Phantombite_Server_Addon` usw. Betroffen waren `!pbc debug <mod>`, `!pbc status` und das an die Mods
  gesendete `LOGLEVEL` (AutoTransfer, CableWinch, PlanetSpawner, StationRefill, AdminProjektor,
  WaterElectrolyzer, ServerAddon). Neu: `ModRegistry.ResolveLocalName`, eine Auflösung für alle Stellen.
- **GlobalConfig hatte falsche Schlüssel** für CableWinch (`Phantombite_CableWinch`) und
  SulvaxRespawnRover (`Phantombite_SulvaxRespawnRover`), Pandora fehlte. Die Liste wird jetzt aus
  `ModRegistry.AllLocalNames` erzeugt.
- **Performance-Zeiten falsch:** `_tick` zählt Ticks, wurde aber zusätzlich mit `SampleInterval` malgenommen bzw.
  geteilt. Ein Drop zählte schon nach 9 statt 90 Ticks als Strike, das Zuordnungsfenster nach HEAVY_START
  war statt 12 s nur etwa 1,2 s lang, „SimSpeed erholt nach ~Ns“ zeigte 10× zu viel. Jetzt echte
  Ticks/Sekunden. **Das Verhalten ändert sich spürbar:** Drops zählen später (weniger Fehlalarme),
  werden aber länger einem Mod zugeordnet.
- „Externe Drops“ in `!pbc perf status` blieb immer 0, jetzt wird gezählt.
- `!pbc debug`: fehlte der Schlüssel in einer älteren GlobalConfig, wurde die Einstellung nicht gespeichert. Jetzt wird sie unter `[Debug]` ergänzt.
- Performance-History: ein einziger fehlerhafter Zahlenwert brach das komplette Laden ab. Jetzt tolerant.
- Pandora bekam kein `READY`, obwohl ID und Kanal im Registry standen.
- **Log-Nachrichten mit abweichendem Mod-Namen wurden verworfen:** Meldete ein Mod sich im `LOG|`-Paket mit
  einem anderen Namen als dem lokalen (Cable_Winch sendete `Phantombite_CableWinch` statt `Phantombite_Cable_Winch`),
  fand `PBLog` sein Debug-Level nicht und unterdrückte Level-1/2-Meldungen. Core normalisiert den Namen jetzt
  über `ResolveLocalName` (`OnLogReceived`).

### Sicherheit und Log-Dateien
- **Sicherheitslücke geschlossen:** Bisher prüfte nur der Client das Admin-Recht, der Server gab jedes Paket 5997
  ungeprüft an den Mod weiter (`STEAM:` kam vom Client). Jetzt `RegisterSecureMessageHandler`: echter Absender,
  Kanal-/Typ-/Command-Prüfung, Admin-Recht serverseitig, `STEAM:` wird überschrieben, Paketgröße begrenzt.
- **Log-Datei:** Kein Lesen der ganzen Datei mehr bei jedem Schreiben. Inhalt liegt im Speicher, Datei wird in Teilen
  zu ca. 0,8 MB geschrieben (`_Teil2` ...), `MAX_LOGS` = 20 Dateien. `!pbc perf log` liest aus dem Speicher.
- **Player-Log:** wird einmal geladen, nicht mehr bei jedem Ereignis gelesen, auf 5000 Zeilen begrenzt.

### Aufgeräumt
- Toter Code entfernt: `EscalateAllMods`, `IsInDrop`, unbenutzte Konstante `FLUSH_INTERVAL`, doppelte Startup-Prüfung, ungenutzte `using`s.
- Mod-Listen (Debug, Performance) nicht mehr mehrfach von Hand gepflegt, sondern aus `ModRegistry`.
- Kommentare korrigiert (Session-Modulliste, GlobalConfig-Beschreibung, `!pbc perf log`).
- Init-Log nennt die tatsächlich registrierten Module.
- Doku neu geschrieben (`DEV_Funktion`, `DEV_Anbindung`, `DEV_Dependencies`, `DEV_TODO`), `README.md`, `.gitignore`, `.gitattributes` ergänzt, veraltete `Phantombite IDs.txt` entfernt.

### Bisher nirgends dokumentierte Erweiterungen (im Code bereits vorhanden)
- `Core_Performance`: SimSpeed-Überwachung, HEAVY-Zuordnung, Eskalation, History-Datei
- `Core_PlayerTracker`: Join/Leave-Protokoll, `!pbc players`
- Adaptives Schreiben der Log-Datei
- REGISTER mit Versionsfeld, automatischer Reset bei Mod-Update
- `Core_PlanetSpawner` und `Core_StationRefill` sind aus dem Core ausgezogen (eigene Mods)

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