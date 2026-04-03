# DEV Anbindung — PhantomBite Core
# Wie man einen neuen Mod an den Core anbindet

---

## Übersicht — Was ist der PhantomBite Core?

PhantomBite Core ist ein Space Engineers Mod der als zentrale Infrastruktur für alle anderen PhantomBite Mods dient. Er stellt folgendes bereit:

- **Zentralen Logger** mit Log-Level pro Mod (Normal/Debug/Trace)
- **Log-Datei Management** (automatisch, pro Server-Start)
- **Command-System** mit Prefix `!pbc` für alle Mods
- **Mod-zu-Mod Messaging** über SE-interne Kanäle
- **HUD-Feedback** für Commands (Grün=Erfolg, Rot=Fehler)
- **Admin-Erkennung** automatisch
- **Dev-Mode** für lokale Entwicklung ohne Workshop-Upload

Alle Mods sind **separate Assemblies** — sie können sich nicht direkt referenzieren. Die gesamte Kommunikation läuft über SE's `SendModMessage` / `RegisterMessageHandler`.

---

## Grundprinzip — Das READY-System

Der Core arbeitet nach dem Boss-Prinzip: **Core schreibt zuerst, Mods antworten.**

```
Spielwelt startet
       ↓
Core sendet "READY" an alle bekannten Mod-Kanäle
       ↓
Mod empfängt "READY" → sendet Registrierung zurück
       ↓
Core empfängt Registrierung → sendet LOGLEVEL zurück
       ↓
Mod ist bereit — empfängt ab jetzt Commands vom Core
```

Warum so? Weil SE Mods in unbekannter Reihenfolge starten. Der Core wartet nicht auf Mods — er schreibt sie aktiv an sobald er bereit ist.

---

## Messaging Kanäle

Alle Kanäle sind in `ModRegistry.cs` des Core definiert:

| Kanal | Richtung | Zweck |
|-------|----------|-------|
| **1995000** | Core → Mod | Boss-Kanal: READY, CMD, LOGLEVEL |
| **1995001** | Core → Artefact | Artefact-spezifische Nachrichten |
| **1995002** | Core → CableWinch | CableWinch-spezifische Nachrichten |
| **1995003** | Core → Creatures | Creatures-spezifische Nachrichten |
| **1995004** | Core → Economy | Economy-spezifische Nachrichten |
| **1995005** | Core → Encounter | Encounter-spezifische Nachrichten |
| **1995006** | Core → ServerAddon | ServerAddon-spezifische Nachrichten |
| **1995007** | Core → Sulvax | Sulvax-spezifische Nachrichten |
| **1995008** | Core → SulvaxRespawnRover | RespawnRover-spezifische Nachrichten |
| **1995999** | Mod → Core | Log-Nachrichten + CMDRESULT |

**Wichtig:** Jeder Mod hört auf SEINEM Kanal (z.B. Artefact auf 1995001). Core hört auf 1995000 (Registrierungen) und 1995999 (Logs + Results).

---

## Nachrichten-Formate (vollständig)

### Core → Mod (Kanal 1995001-1995008)

| Nachricht | Format | Bedeutung |
|-----------|--------|-----------|
| READY | `"READY"` | Core ist bereit, bitte registrieren |
| LOGLEVEL | `"LOGLEVEL\|normal"` | Log-Level setzen (normal/debug/trace) |
| CMD | `"CMD\|cmdname\|arg1\|arg2\|STEAM:steamId"` | Command ausführen |

### Mod → Core (Kanal 1995000)

| Nachricht | Format | Bedeutung |
|-----------|--------|-----------|
| REGISTER | `"REGISTER\|modname\|beschreibung\|kanal\|cmd:adminOnly:desc\|..."` | Mod registriert sich |
| CMDRESULT | `"CMDRESULT\|modname\|cmd\|args\|steamId\|ok\|Nachricht"` | Command wurde ausgeführt |

### Mod → Core (Kanal 1995999)

| Nachricht | Format | Bedeutung |
|-----------|--------|-----------|
| LOG | `"LOG\|Phantombite_X\|DEBUG\|Modul_Name\|Nachricht"` | Log-Eintrag senden |
| CMDRESULT | `"CMDRESULT\|modname\|cmd\|args\|steamId\|ok\|Nachricht"` | Command-Ergebnis |

---

## Schritt-für-Schritt: Neuen Mod anbinden

### Schritt 1 — ModRegistry prüfen

In `Core/ModRegistry.cs` des Core-Mods muss der neue Mod eingetragen sein:

```csharp
// Workshop ID
public const ulong MeinMod = 1234567890UL;

// Lokaler Name (für Dev-Mode)
public const string LocalMeinMod = "Phantombite_MeinMod";

// Messaging Kanal
public const long ChannelMeinMod = 1995009L; // nächste freie Nummer

// In GetLocalName():
if (modId == MeinMod) return LocalMeinMod;

// In GetName():
if (modId == MeinMod) return "MeinMod";

// In IsPhantomBiteMod():
|| modId == MeinMod

// In ALL_PB_IDS (ModDetector.cs):
ModRegistry.MeinMod,
```

### Schritt 2 — Core_Command.cs: SendReadyToActiveMods erweitern

In `Core_Command.cs` muss der neue Mod in der `SendReadyToActiveMods` Methode eingetragen werden:

```csharp
var modChannels = new Dictionary<ulong, long>
{
    { ModRegistry.Artefact,    1995001L },
    // ...
    { ModRegistry.MeinMod,    1995009L }, // NEU
};
```

### Schritt 3 — GlobalConfig erweitern

In `Core_FileManager.cs` muss der neue Mod in der GlobalConfig erscheinen:

```csharp
private static readonly string[] ALL_MOD_NAMES = {
    "Phantombite_Core",
    "Phantombite_Artefact",
    // ...
    "Phantombite_MeinMod", // NEU
};
```

---

## Der neue Mod — Dateien die er braucht

Ein Mod der sich beim Core registriert braucht mindestens:

```
Phantombite_MeinMod/
└── Data/Scripts/PhantombiteMeinMod/
    ├── Core/
    │   ├── IModule.cs          (Interface — identisch für alle Mods)
    │   ├── ModuleManager.cs    (Fehler-Isolation — identisch für alle Mods)
    │   └── Session.cs          (Haupt-Session)
    └── Modules/
        └── MeinMod_Command.cs  (Command-Modul — das Herzstück)
```

---

## IModule.cs — Interface (identisch für alle Mods)

```csharp
namespace PhantombiteMeinMod.Core
{
    public interface IModule
    {
        string ModuleName { get; }
        void Init();
        void Update();
        void SaveData();
        void Close();
    }
}
```

---

## Session.cs — Haupt-Session

```csharp
using PhantombiteMeinMod.Core;
using PhantombiteMeinMod.Modules;

[MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation)]
public class PhantombiteMeinModSession : MySessionComponentBase
{
    private ModuleManager       _moduleManager;
    private MeinModCommandModule _commandModule;
    private bool _isInitialized = false;

    public override void LoadData()
    {
        _moduleManager = new ModuleManager();
        _isInitialized = true;
    }

    public override void BeforeStart()
    {
        if (!_isInitialized) return;

        // Command-Modul registrieren — wartet auf Core READY
        _commandModule = new MeinModCommandModule();
        _moduleManager.RegisterModule(_commandModule);
        _moduleManager.InitAll();
    }

    public override void UpdateBeforeSimulation()
    {
        if (!_isInitialized) return;
        _moduleManager.UpdateAll();
    }

    public override void SaveData()
    {
        if (!_isInitialized) return;
        _moduleManager.SaveAll();
    }

    protected override void UnloadData()
    {
        _moduleManager?.CloseAll();
        _isInitialized = false;
    }
}
```

---

## MeinMod_Command.cs — Das Herzstück (vollständig)

```csharp
using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using PhantombiteMeinMod.Core;

namespace PhantombiteMeinMod.Modules
{
    public class MeinModCommandModule : IModule
    {
        public string ModuleName { get { return "MeinMod_Command"; } }

        // ── Kanäle ───────────────────────────────────────────────────────────
        private const long CORE_CHANNEL    = 1995000L;
        private const long MEINMOD_CHANNEL = 1995009L; // eigener Kanal
        private const long LOG_CHANNEL     = 1995999L;
        private const string MOD_NAME      = "Phantombite_MeinMod";

        private bool _initialized = false;

        // ── Log-Level (vom Core gesetzt) ─────────────────────────────────────
        // Core teilt beim Start mit welches Level er empfangen will.
        // Mod filtert selbst — so werden keine unnötigen Nachrichten gesendet.
        private enum LogLevel { Normal = 0, Debug = 1, Trace = 2 }
        private LogLevel _logLevel = LogLevel.Normal;

        // ── IModule ──────────────────────────────────────────────────────────

        public void Init()
        {
            if (_initialized) return;
            // Auf eigenem Kanal hören — Core sendet hier READY, LOGLEVEL, CMD
            MyAPIGateway.Utilities.RegisterMessageHandler(MEINMOD_CHANNEL, OnMessageReceived);
            _initialized = true;
            MyLog.Default.WriteLineAndConsole("[PhantombiteMeinMod] MeinMod_Command: Initialized — warte auf Core READY");
        }

        public void Update()   { }
        public void SaveData() { }

        public void Close()
        {
            if (!_initialized) return;
            if (MyAPIGateway.Utilities != null)
                MyAPIGateway.Utilities.UnregisterMessageHandler(MEINMOD_CHANNEL, OnMessageReceived);
            _initialized = false;
        }

        // ── Nachrichten vom Core empfangen ────────────────────────────────────

        private void OnMessageReceived(object data)
        {
            try
            {
                string msg = data as string;
                if (string.IsNullOrEmpty(msg)) return;

                // 1. READY — Core ist bereit, jetzt registrieren
                if (msg == "READY")
                {
                    MyLog.Default.WriteLineAndConsole("[PhantombiteMeinMod] MeinMod_Command: Core READY empfangen");
                    RegisterWithCore();
                    return;
                }

                // 2. LOGLEVEL — Core teilt mit welches Level er empfangen will
                if (msg.StartsWith("LOGLEVEL|"))
                {
                    string levelStr = msg.Substring(9).ToLower();
                    _logLevel = levelStr == "trace" ? LogLevel.Trace
                              : levelStr == "debug" ? LogLevel.Debug
                              : LogLevel.Normal;
                    MyLog.Default.WriteLineAndConsole("[PhantombiteMeinMod] MeinMod_Command: LogLevel gesetzt: " + _logLevel);
                    return;
                }

                // 3. CMD — Core sendet einen Command zur Ausführung
                if (msg.StartsWith("CMD|"))
                {
                    OnCommandReceived(msg);
                    return;
                }
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole("[PhantombiteMeinMod] MeinMod_Command: Fehler in OnMessageReceived: " + ex.Message);
            }
        }

        // ── Registrierung beim Core ───────────────────────────────────────────
        // Wird nach READY gesendet.
        // Format: "REGISTER|modname|beschreibung|kanal|cmd:adminOnly:desc|..."
        // adminOnly: 1 = nur Admins, 0 = alle Spieler

        private void RegisterWithCore()
        {
            try
            {
                string msg = "REGISTER"
                    + "|meinmod"                          // Mod-Name (kleinbuchstaben)
                    + "|Mein Mod Beschreibung"            // Beschreibung für !pbc help
                    + "|" + MEINMOD_CHANNEL               // Kanal auf dem Core Commands sendet
                    + "|aktion1:1:Beschreibung Aktion 1"  // cmd:adminOnly:beschreibung
                    + "|aktion2:1:Beschreibung Aktion 2"
                    + "|aktion3:0:Beschreibung Aktion 3"; // 0 = alle Spieler dürfen das

                MyAPIGateway.Utilities.SendModMessage(CORE_CHANNEL, msg);
                MyLog.Default.WriteLineAndConsole("[PhantombiteMeinMod] MeinMod_Command: Registrierung an Core gesendet");
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole("[PhantombiteMeinMod] MeinMod_Command: Fehler bei Registrierung: " + ex.Message);
            }
        }

        // ── Command empfangen und ausführen ───────────────────────────────────
        // Format: "CMD|cmdname|arg1|arg2|STEAM:steamId"
        // Das letzte Argument ist immer STEAM:steamId — muss extrahiert werden!

        private void OnCommandReceived(string msg)
        {
            try
            {
                string[] parts = msg.Split('|');
                if (parts.Length < 2) return;

                string command = parts[1].ToLower();

                // STEAM:steamId aus letztem Argument extrahieren
                ulong steamId = 0;
                int   argEnd  = parts.Length;
                if (parts[parts.Length - 1].StartsWith("STEAM:"))
                {
                    ulong.TryParse(parts[parts.Length - 1].Substring(6), out steamId);
                    argEnd = parts.Length - 1;
                }

                // Argumente ohne STEAM-Tag
                string[] args = new string[argEnd - 2];
                Array.Copy(parts, 2, args, 0, args.Length);

                // ID-Support: "1" oder "all" als erstes Argument
                int  targetId  = 0;
                bool hasId     = false;
                bool allTarget = false;

                if (args.Length > 0)
                {
                    if (args[0].ToLower() == "all")
                        allTarget = true;
                    else if (int.TryParse(args[0], out targetId))
                        hasId = true;
                }

                string argsJoined = string.Join("|", args);

                MyLog.Default.WriteLineAndConsole("[PhantombiteMeinMod] MeinMod_Command: Command empfangen: " + command);

                // Command ausführen
                bool executed = false;
                string resultMsg = "";

                switch (command)
                {
                    case "aktion1":
                        // Hier eigene Logik
                        executed   = true;
                        resultMsg  = "MeinMod " + (hasId ? "ID " + targetId : allTarget ? "alle" : "ID 0") + ": aktion1 ausgeführt";
                        break;

                    case "aktion2":
                        // Hier eigene Logik
                        executed   = true;
                        resultMsg  = "MeinMod " + (hasId ? "ID " + targetId : allTarget ? "alle" : "ID 0") + ": aktion2 ausgeführt";
                        break;

                    default:
                        resultMsg = "MeinMod: unbekannter Command: " + command;
                        break;
                }

                // CMDRESULT zurück an Core senden
                // Core zeigt dann HUD-Notification für den Spieler
                // Format: "CMDRESULT|modname|cmd|args|steamId|ok|Nachricht"
                string status = executed ? "ok" : "fail";
                string result = "CMDRESULT|meinmod|" + command + "|" + argsJoined + "|" + steamId + "|" + status + "|" + resultMsg;
                MyAPIGateway.Utilities.SendModMessage(LOG_CHANNEL, result);
                // Hinweis: CMDRESULT geht auf Kanal 1995999 (LOG_CHANNEL)!
                // Core hört dort auf CMDRESULT| Prefix und verarbeitet es.
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole("[PhantombiteMeinMod] MeinMod_Command: Fehler in OnCommandReceived: " + ex.Message);
            }
        }

        // ── Log API — Logging über Core-Logger ───────────────────────────────
        // Alle Log-Nachrichten gehen auf Kanal 1995999.
        // Core filtert anhand des gesetzten LOGLEVEL.
        // Format: "LOG|Phantombite_MeinMod|DEBUG|MeinMod_Command|Nachricht"

        public void Warn(string module, string message)
        {
            SendLog("WARN", module, message);
        }

        public void Error(string module, string message)
        {
            SendLog("ERROR", module, message);
        }

        public void Info(string module, string message)
        {
            if (_logLevel < LogLevel.Debug) return; // nicht senden wenn nicht nötig
            SendLog("INFO", module, message);
        }

        public void Debug(string module, string message)
        {
            if (_logLevel < LogLevel.Debug) return;
            SendLog("DEBUG", module, message);
        }

        public void Trace(string module, string message)
        {
            if (_logLevel < LogLevel.Trace) return;
            SendLog("TRACE", module, message);
        }

        private void SendLog(string level, string module, string message)
        {
            try
            {
                MyAPIGateway.Utilities.SendModMessage(LOG_CHANNEL,
                    "LOG|" + MOD_NAME + "|" + level + "|" + module + "|" + message);
            }
            catch { }
        }
    }
}
```

---

## Was passiert im Core wenn ein Mod sich registriert?

### Core_Command.cs — OnModRegistration

```
Nachricht: "REGISTER|meinmod|Mein Mod|1995009|aktion1:1:Beschreibung|aktion2:0:Beschreibung"

Core parst:
  modName    = "meinmod"
  modDesc    = "Mein Mod"
  channel    = 1995009
  commands   = [
    { name: "aktion1", adminOnly: true,  desc: "Beschreibung" },
    { name: "aktion2", adminOnly: false, desc: "Beschreibung" },
  ]

Core speichert:
  _modChannels["meinmod"]      = 1995009
  _modDescriptions["meinmod"]  = "Mein Mod"
  _modCommands["meinmod"]      = Liste der Commands

Core sendet zurück:
  → "LOGLEVEL|normal" (oder debug/trace je nach GlobalConfig)

Core registriert automatisch:
  → "!pbc meinmod aktion1" führt aktion1 aus
  → "!pbc meinmod aktion2" führt aktion2 aus
  → "!pbc meinmod help" zeigt Mod-Help
```

### Admin-only Logik (automatisch)

Hat ein Mod **ausschließlich** `adminOnly=true` Commands → für normale Spieler komplett unsichtbar:
- `!pbc help` zeigt den Mod nicht
- `!pbc meinmod help` → "Blockiert: Admin-Rechte erforderlich."

Sobald **mindestens ein** `adminOnly=false` Command registriert ist → Mod ist öffentlich sichtbar.

---

## Was passiert wenn ein Command eingegeben wird?

```
Admin tippt: !pbc meinmod 1 aktion1

Core parst:
  modName   = "meinmod"
  targetId  = 1      (aus "1")
  command   = "aktion1"

Core prüft:
  - Mod registriert? ✓
  - Command existiert? ✓
  - AdminOnly? → Spieler Admin? ✓
  - Duplikat? (gleicher Key + SteamId in letzten 2 Sek) → nein ✓

Core erstellt Pending-Eintrag:
  Key: "meinmod|aktion1|1|76561198xxxxxxx"
  SteamId: 76561198xxxxxxx
  SentAt: jetzt

Core sendet an Kanal 1995009:
  "CMD|aktion1|1|STEAM:76561198xxxxxxx"

Mod empfängt, führt aus, sendet zurück auf 1995999:
  "CMDRESULT|meinmod|aktion1|1|76561198xxxxxxx|ok|MeinMod ID 1: aktion1 ausgeführt"

Core empfängt CMDRESULT:
  - Findet Pending-Eintrag anhand Key
  - Entfernt Pending-Eintrag
  - Zeigt HUD für Spieler 76561198xxxxxxx:
    "[PB] MeinMod ID 1: aktion1 ausgeführt" (grün)
```

---

## Timeout-Mechanismus

Wenn ein Mod abstürzt oder CMDRESULT nicht sendet:

```
Core.Update() läuft alle 60 Ticks (1 Sekunde)
  → Prüft alle Pending-Einträge
  → Ist ein Eintrag älter als 2 Sekunden?
    → WARN im Log: "Command Timeout — keine Antwort: meinmod|aktion1|1|76561198..."
    → Pending-Eintrag entfernen
    → Kein HUD (Spieler bekommt keine Rückmeldung)
```

---

## Deduplizierungs-Mechanismus

Wenn mehrere Spieler gleichzeitig den gleichen Command eingeben:

```
Spieler A: !pbc meinmod 1 aktion1  → Key: "meinmod|aktion1|1|SteamA" → NEU → ausführen
Spieler B: !pbc meinmod 1 aktion1  → Key: "meinmod|aktion1|1|SteamB" → NEU → ausführen
Spieler A: !pbc meinmod 1 aktion1  → Key: "meinmod|aktion1|1|SteamA" → DUPLIKAT → HUD "bereits in Bearbeitung"
```

Key ist eindeutig per `modName|cmd|args|steamId` — gleicher Spieler, gleicher Command, gleiche Args = Duplikat.

---

## Debug-System

### Spieler ändert Debug-Level zur Laufzeit

```
Admin tippt: !pbc debug meinmod debug

Core ändert Log-Level für "Phantombite_MeinMod" auf Debug
Core sendet an Kanal 1995009: "LOGLEVEL|debug"
Mod empfängt, setzt _logLevel = Debug
Ab jetzt sendet Mod auch INFO + DEBUG Nachrichten auf 1995999
```

### Permanente Änderung

```
Admin tippt nochmal: !pbc debug meinmod debug

Core erkennt: gleicher Level wie zuletzt temporär gesetzt
→ Speichert in GlobalConfig permanent:
  [Debug]
  Phantombite_MeinMod=Debug
```

---

## Commands auf !pbc help anzeigen

Nach der Registrierung erscheint der Mod automatisch in `!pbc help`:

```
!pbc help

=== PhantomBite Help (1/2) ===
  !pbc artefact help     — Space Artefact Steuerung
  !pbc meinmod help      — Mein Mod Beschreibung   ← automatisch!
  ...

!pbc meinmod help

=== MeinMod Help ===
  !pbc meinmod aktion1   — Beschreibung Aktion 1
  !pbc meinmod aktion2   — Beschreibung Aktion 2
```

---

## Häufige Fehler und Lösungen

### Mod empfängt kein READY
**Problem:** Mod läuft nach Core — Core hat READY bereits gesendet bevor Mod hört.
**Lösung:** Core sendet READY erst in `SendReadyToActiveMods()` die **nach** `InitAll()` aufgerufen wird. Beide Mods müssen in `BeforeStart()` initialisiert sein.

### CMDRESULT kommt nicht an
**Problem:** CMDRESULT geht auf falschen Kanal.
**Lösung:** CMDRESULT muss auf **1995999** gesendet werden — nicht auf den eigenen Mod-Kanal!

### Log-Level wird nicht übernommen
**Problem:** Mod filtert nicht selbst.
**Lösung:** Mod muss `_logLevel` speichern und bei `Info()`/`Debug()`/`Trace()` prüfen bevor er sendet. Sonst sendet er immer alles was bei Normal-Level Spam erzeugt.

### Command erscheint nicht in !pbc help
**Problem:** Mod-Name in Registrierung falsch oder Mod hat nur adminOnly=true Commands und Spieler ist kein Admin.
**Lösung:** Mod-Name muss kleinbuchstaben sein. Admin-only Mods sind für normale Spieler unsichtbar — das ist gewollt.

### Dev-Mode funktioniert nicht
**Problem:** Core erkennt Mod nicht.
**Lösung:** In `ModRegistry.cs` muss `LocalMeinMod = "Phantombite_MeinMod"` eingetragen sein. Der lokale Mod-Ordner muss genau diesen Namen haben.

---

## Checkliste — Neuen Mod anbinden

### Im Core (einmalig):
- [ ] Workshop-ID in `ModRegistry.cs` eintragen
- [ ] Lokalen Namen in `ModRegistry.cs` eintragen
- [ ] Kanal in `ModRegistry.cs` eintragen
- [ ] Kanal in `ALL_PB_IDS` / `ModDetector.cs` eintragen
- [ ] Kanal in `SendReadyToActiveMods()` eintragen
- [ ] Mod-Name in GlobalConfig-Liste eintragen

### Im neuen Mod:
- [ ] `IModule.cs` kopieren (identisch für alle Mods)
- [ ] `ModuleManager.cs` kopieren (identisch für alle Mods)
- [ ] `Session.cs` erstellen (Vorlage oben)
- [ ] `MeinMod_Command.cs` erstellen (Vorlage oben)
- [ ] Eigenen Kanal eintragen (z.B. 1995009)
- [ ] `REGISTER` Nachricht mit Commands befüllen
- [ ] `OnCommandReceived` mit eigener Logik befüllen
- [ ] CMDRESULT nach Ausführung senden (auf 1995999!)
- [ ] Log-Level speichern und selbst filtern
- [ ] Kein `ShowNotification` für Command-Feedback — Core macht das!