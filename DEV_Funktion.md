# DEV Funktion — PhantomBite Core

## Zweck
PhantomBite Core ist der zentrale Basis-Mod für alle PhantomBite Mods. Er stellt gemeinsam genutzte Item-Definitionen, einen zentralen Logger, FileManager, Command-System und ein Mod-zu-Mod Messaging-System bereit.

---

## Dateistruktur
```
Phantombite_Core/
├── modinfo.sbmi                          (Workshop ID: 3689625814)
├── metadata.mod
├── Data/
│   ├── PhysicalItems/AdminChip.sbc
│   ├── Blueprints/AdminChip.sbc
│   └── Scripts/PhantombiteCore/
│       ├── Core/
│       │   ├── IModule.cs                (Interface für alle Module)
│       │   ├── ModDetector.cs            (Erkennt aktive Mods + Session-Typ)
│       │   ├── ModRegistry.cs            (Workshop IDs + Kanäle + Namen)
│       │   ├── ModuleManager.cs          (Verwaltet Module mit Fehler-Isolation)
│       │   └── Session.cs                (Haupt-Session-Komponente)
│       └── Modules/
│           ├── Core_Logger.cs            (M01 — Zentraler Logger)
│           ├── Core_FileManager.cs       (M02 — Log-Dateien + GlobalConfig)
│           ├── Core_Command.cs           (M03 — Command-System + Messaging)
│           └── Core_PlanetSpawner.cs     (M99 — Sulvax Planet Spawner, optional)
├── Models/Items/AdminChip_Item.mwm
└── Textures/GUI/Icons/Items/AdminChip_Item.dds
```

---

## Module

### Core_Logger (M01)
Zentraler Logger für alle PhantomBite Mods. Schreibt in einen Buffer den Core_FileManager alle 5 Sekunden in die Log-Datei schreibt.

**Log-Level pro Mod:**
- `Normal` — nur WARN + ERROR
- `Debug` — + INFO + DEBUG
- `Trace` — + alles (für Entwicklung)

**Log-Format:**
```
2026-03-27 04:01:05.335 [DEBUG] Phantombite_Artefact/Artefact_Controller : SCHOCKWELLE — Impuls #1
```

**API:**
```csharp
LoggerModule.Warn("Phantombite_Core", "Core_Command", "Nachricht");
LoggerModule.Error("Phantombite_Core", "Core_Command", "Nachricht", ex);
LoggerModule.Info("Phantombite_Core", "Core_Command", "Nachricht");   // ab Debug
LoggerModule.Debug("Phantombite_Core", "Core_Command", "Nachricht");  // ab Debug
LoggerModule.Trace("Phantombite_Core", "Core_Command", "Nachricht");  // ab Trace
LoggerModule.SetLevel("Phantombite_Artefact", LoggerModule.LogLevel.Debug);
LoggerModule.FlushBuffer(); // von Core_FileManager aufgerufen
```

---

### Core_FileManager (M02)
Drei Aufgaben:

**1. GlobalConfig** (`Phantombite_GlobalConfig.ini`) — Nur auf Server
```ini
[Debug]
Phantombite_Core=Normal
Phantombite_Artefact=Normal
Phantombite_Economy=Normal
... (alle 9 Mods)
```

**2. Log-Management** — Pro Start neue Datei `Phantombite_Core_DATUM_UHRZEIT.log`, max 20 Logs, Buffer alle 5s schreiben.

**3. Helfer-API für andere Mods:**
```csharp
FileManagerModule.ReadFile("file.ini", typeof(MyClass));
FileManagerModule.WriteFile("file.ini", content, typeof(MyClass));
FileManagerModule.ParseINI(content); // → Dictionary<string,string> "Section.Key"
FileManagerModule.GetValue/Int/Float/Bool(...)
```

---

### Core_Command (M03)
Zentrales Command-System mit Mod-zu-Mod Messaging.

**Prefix:** `!pbc` (auch `/pbc` als Fallback)

**Eingebaute Core-Commands:**
| Command | Beschreibung | Admin |
|---------|-------------|-------|
| `!pbc help [seite]` | Übersicht aller Commands (7 Zeilen/Seite) | Nein |
| `!pbc help <mod>` | Direkt Mod-Help anzeigen | Nein |
| `!pbc status` | Alle aktiven Mods + Debug-Status | Nein |
| `!pbc debug <mod\|all> normal\|debug\|trace` | 1x=temporär, 2x=permanent | Ja |
| `!pbc log copy` | Log in Zwischenablage kopieren | Ja |
| `!pbc log show` | Letzte Log-Zeilen im Chat anzeigen | Ja |

**Mod-Commands mit ID-Support:**
```
!pbc artefact on          → ID=0
!pbc artefact 1 on        → ID=1
!pbc artefact all reset   → alle
```

**READY-System (Mod-Registrierung beim Start):**
1. Core sendet `"READY"` an alle aktiven Mod-Kanäle
2. Mod antwortet mit: `"REGISTER|modname|beschreibung|kanal|cmd:adminOnly:desc|..."`
3. Core sendet zurück: `"LOGLEVEL|normal|debug|trace"`
4. Commands kommen als `"CMD|commandname|arg1|arg2|STEAM:steamId"` vom Core

**CMDRESULT-System (Feedback nach Command-Ausführung):**
1. Mod führt Command aus
2. Mod sendet auf Kanal 1995999: `"CMDRESULT|modname|cmd|args|steamId|ok|Nachricht"`
3. Core zeigt HUD-Notification: Grün bei Erfolg, Rot bei Fehler
4. Kein Result nach 2 Sekunden → WARN im Log

**Deduplizierung:**
- Key: `"modName|cmd|args|steamId"` — eindeutig pro Spieler + Command
- Duplikat innerhalb Timeout → HUD "bereits in Bearbeitung"

**Log-Kanal 1995999:**
- Mods senden Logs: `"LOG|Phantombite_Artefact|DEBUG|Artefact_Controller|Nachricht"`
- Core filtert nach gesetztem Log-Level und schreibt in Phantombite-Log

**Admin-only Mod Logik:**
- Hat ein Mod nur `adminOnly=true` Commands → für normale Spieler komplett unsichtbar
- Sobald ein `adminOnly=false` Command registriert wird → automatisch öffentlich

---

### Core_PlanetSpawner (M99)
Wird nur geladen wenn Phantombite_Sulvax aktiv ist. Stellt sicher dass der Sulvax-Planet an der korrekten Position existiert.

---

### ModDetector
Erkennt beim Start welche Mods aktiv sind.

| Property | Beschreibung |
|----------|-------------|
| `IsServer` | true im Singleplayer UND auf Dedicated Server |
| `IsSingleplayer` | true nur im Singleplayer (OFFLINE Mode) |
| `IsDevMode` | true wenn Core lokal läuft (keine Workshop-ID) |

**Dev-Mode:** Erkennung per Mod-Name statt Workshop-ID — so funktioniert alles lokal ohne Upload.

---

## AdminChip
- **TypeId:** Component / **SubtypeId:** AdminChip
- **Masse:** 0.25 kg / **Volumen:** 0.2 L
- **Mindestpreis:** 100.000 Credits / **Blueprint Bauzeit:** 99.999 Sekunden
- Nicht herstellbar, nicht kaufbar — dient als Sicherheitsmechanismus
- Blöcke mit AdminChip als CriticalComponent können nur von Server-Admins platziert werden

---

## Ladereihenfolge der Module
```
Session.BeforeStart()
  1. ModDetector.Scan()        — Mods + Session-Typ erkennen
  2. Core_Logger.Init()        — Logger bereit
  3. Core_FileManager.Init()   — GlobalConfig laden, Log-Datei erstellen
  4. Core_Command.Init()       — Commands + Messaging registrieren
  5. [Core_PlanetSpawner]      — Optional, nur wenn Sulvax aktiv
  6. CommandModule.SendReadyToActiveMods() — Alle Mods anschreiben
  7. [PlanetSpawner.CheckAndSpawn()]       — Optional
```

---

## Messaging Protokoll (vollständig)

| Richtung | Kanal | Format | Zweck |
|----------|-------|--------|-------|
| Core → Mod | 1995001-1995008 | `READY` | Start-Signal |
| Mod → Core | 1995000 | `REGISTER\|name\|desc\|kanal\|cmd:admin:desc\|...` | Registrierung |
| Core → Mod | 1995001-1995008 | `LOGLEVEL\|normal` | Log-Level setzen |
| Core → Mod | 1995001-1995008 | `CMD\|cmdname\|arg1\|STEAM:steamId` | Command ausführen |
| Mod → Core | 1995999 | `CMDRESULT\|mod\|cmd\|args\|steamId\|ok\|msg` | Command-Ergebnis |
| Mod → Core | 1995999 | `LOG\|Phantombite_X\|DEBUG\|Modul\|Nachricht` | Log-Eintrag |