# DEV Funktion — Phantombite Core

Stand: 2026-09-19 · Code-Stand: Session `VERSION = "2.0.0"` (siehe `DEV_History.md`)

## Zweck

Core ist die gemeinsame Infrastruktur aller Phantombite-Mods. Jeder Mod ist eine eigene
Assembly und kann andere Mods nicht direkt aufrufen. Core übernimmt deshalb alles, was
mehrere Mods brauchen:

- ein zentrales Command-System (`!pbc ...`) für alle Mods
- einen zentralen Logger mit Debug-Level pro Mod und eine Log-Datei
- die globale Konfiguration (`Phantombite_GlobalConfig.ini`)
- eine Performance-Überwachung, die Mods bei SimSpeed-Einbrüchen drosseln kann
- eine Spieler-Protokollierung (Join/Leave)
- den `AdminChip` (Item, das nur Admins platzieren können)

Core hat keine Abhängigkeiten. Alle anderen Phantombite-Mods hängen (optional) an Core.

**Idee hinter dem Core:** Er entstand, als der `AdminChip` aus Phantombite Economy herausgelöst wurde
(Version 1.0.0). Daraus wurde die Grundidee: Core verwaltet und steuert alle Phantombite-Mods und hat die
Kontrolle über Commands, Logging, Konfiguration und Performance. Neue Mods binden sich deshalb über `READY` und
`REGISTER` an den Core an, statt eigene Command- oder Log-Systeme zu bauen (siehe `DEV_Anbindung.md`).
Mods, die den `AdminChip` als Bauteil benötigen, brauchen Core: AdminProjektor, Artefact sowie Blöcke in Economy und AutoTransfer.

## Dateien

```
Phantombite_Core/
├── modinfo.sbmi, metadata.mod
├── Data/
│   ├── PhysicalItems/AdminChip.sbc, Blueprints/AdminChip.sbc
│   └── Scripts/PhantombiteCore/
│       ├── Core/
│       │   ├── Session.cs         Einstieg, legt Module an, sendet READY
│       │   ├── ModuleManager.cs   Init/Update/Save/Close, Modul wird nach 3 Abstürzen abgeschaltet
│       │   ├── IModule.cs         Interface: ModuleName, Init, Update, SaveData, Close
│       │   ├── ModDetector.cs     Welche Mods laufen (Workshop / Lokal / Hybrid)
│       │   └── ModRegistry.cs     ZENTRALE LISTE: Workshop-IDs, lokale Namen, Kanäle
│       └── Modules/
│           ├── Core logger.cs         PBLog (statischer Logger) + LoggerModule
│           ├── Core filemanager.cs    GlobalConfig, Log-Datei, Datei-API für andere Mods
│           ├── Core command.cs        !pbc Commands, Mod-Registrierung, Messaging
│           ├── Core Performance.cs    SimSpeed-Überwachung und Eskalation
│           └── Core playertracker.cs  Join/Leave-Protokoll
├── Models/Items, Textures/GUI/Icons/Items   (AdminChip)
└── DEV_*.md
```

`ModRegistry.cs` ist die einzige Quelle für IDs, Namen und Kanäle. Es gibt keine zweite Liste
mehr (die frühere `Phantombite IDs.txt` wurde entfernt).

## Ladereihenfolge (`Session.BeforeStart`)

1. `ModDetector.Scan()` — aktive Mods und Session-Typ erkennen
2. `Core_Logger` → 3. `Core_FileManager` (GlobalConfig, Log-Datei) → 4. `Core_Command`
   → 5. `Core_Performance` → 6. `Core_PlayerTracker` (in genau dieser Reihenfolge registriert)
3. `ModuleManager.InitAll()`
4. `Core_Command.SendReadyToActiveMods()` — schreibt alle aktiven Mods an (READY)

## Logger (`PBLog`)

Debug-Level pro Mod: `0` = immer sichtbar, `1` = wichtige Debug-Infos, `2` = Details.
Level 0, WARN und ERROR landen in der Log-Datei, Level 1 und 2 nur im SE-Log.

```csharp
PBLog.Log("Phantombite_Core", "Core_Command", "Text");        // Level 0
PBLog.Log("Phantombite_Core", "Core_Command", "Text", 1);     // nur ab Debug-Level 1
PBLog.Warn(mod, modul, "Text");   PBLog.Error(mod, modul, "Text", exception);
```

Format im SE-Log: `[PB.Economy][1] FileManager: Text` (ohne Präfix `Phantombite_`).

## FileManager und GlobalConfig

- `Phantombite_GlobalConfig.ini` (nur auf dem Server, wird bei Fehlen mit Standardwerten angelegt):
  - `[Debug]` ein Eintrag pro Mod, Schlüssel = lokaler Name aus `ModRegistry.AllLocalNames`
  - `[Performance]` globale Schwellwerte, `[Performance.<Mod>]` je Mod (`EscalationPath`, `CorrelationsBeforeEscalate`, `CurrentLevel`)
- Log-Datei pro Start: `Phantombite_<Datum>_<Uhrzeit>.log`. Ab ca. 0,8 MB (`MAX_SEGMENT_CHARS`) beginnt eine neue Datei `..._Teil2.log` usw.
  Es bleiben die letzten 20 Dateien (`MAX_LOGS`), Index in `Phantombite_LogIndex.txt`. Der aktuelle Teil liegt im Speicher, die Datei wird nur geschrieben, nie gelesen.
- Der Log-Buffer wird adaptiv geschrieben (60–300 s, nur bei SimSpeed ≥ 0.90), bei 50 MB sofort.
- Player-Log (`Phantombite_PlayerLog.txt`): einmal beim Start geladen, auf 5000 Zeilen begrenzt.
- Datei-API für andere Code-Stellen im Core: `ReadFile`, `WriteFile`, `FileExists`, `DeleteFile`,
  `ParseINI` (Schlüssel `Section.Key`), `GetValue/GetValueInt/GetValueFloat/GetValueBool`.

## Commands (`!pbc`, Fallback `/pbc`)

| Command | Wer | Wirkung |
|---|---|---|
| `!pbc help [Seite]`, `!pbc help <mod> [Seite]` | alle | Hilfe, 7 Zeilen pro Seite |
| `!pbc status` | alle | Registrierte Mods, Versionen, Debug-Level |
| `!pbc players` | alle | Aktive Spieler mit Online-Zeit |
| `!pbc debug <mod\|all> <0\|1\|2>` | Admin | 1× = temporär, 2× gleicher Wert = dauerhaft in GlobalConfig |
| `!pbc perf status \| log \| reset [mod]` | Admin | Performance-Levels, letzte Log-Zeilen, Zurücksetzen |
| `!pbc log show \| copy` | Admin | Letzte Zeilen anzeigen / aktuellen Log-Teil in die Zwischenablage |
| `!pbc <mod> [id\|all] <command> [args]` | je Command | Command eines registrierten Mods |

Admin-only-Mods: Hat ein Mod nur Admin-Commands, sehen normale Spieler ihn nicht in der Hilfe.
Debug-Namen werden tolerant aufgelöst (`cablewinch` → `Phantombite_Cable_Winch`),
siehe `ModRegistry.ResolveLocalName`.

Im Multiplayer schickt der Client den Command als Paket (5997) an den Server. Der Server nimmt es
über `RegisterSecureMessageHandler` an und **glaubt dem Paket nichts**: Er kennt den Absender von SE,
akzeptiert nur Kanäle registrierter Mods, nur `CMD`-Nachrichten und nur bekannte Commands, prüft
Admin-Commands gegen das echte Admin-Recht des Absenders und ersetzt `STEAM:` durch dessen echte
SteamId. Erst dann geht die Nachricht an den Mod. Das Ergebnis kommt als Paket (5998) zurück und
wird als HUD-Meldung angezeigt (grün = ok, rot = Fehler). Gleicher Command + gleiche Argumente + gleicher Spieler
gilt als Duplikat, solange die Antwort aussteht. Ohne Antwort nach 10 s gibt es eine WARN im Log.

## Nachrichten-Protokoll (Mod ↔ Core)

Details und Beispiele: `DEV_Anbindung.md`.

| Richtung | Kanal | Nachricht |
|---|---|---|
| Core → Mod | Mod-Kanal | `READY` · `LOGLEVEL\|0..2` · `CMD\|cmd\|args...\|STEAM:id` · `PERFLEVEL\|n` |
| Mod → Core | 1995000 | `REGISTER\|name\|beschreibung\|version\|kanal\|cmd:admin:beschr...` (Version optional, altes Format wird erkannt) |
| Mod → Core | 1995000 | `HEAVY_START\|mod\|op` · `HEAVY_END\|mod\|op` · `PERFACK\|mod\|level` |
| Mod → Core | 1995999 | `LOG\|Phantombite_X\|0..2\|Modul\|Text` · `CMDRESULT\|mod\|cmd\|args\|steamId\|ok\|Text` |

Kanäle: siehe `ModRegistry.Channel*` (1995000 Core, 1995001–1995014 je Mod, 1995999 Log/Ergebnis).
Kanal 1995015 gehört dem Encounter System, 1995016 StationRefill.

## Performance-System

Ziel: Bricht die SimSpeed ein, soll Core erkennen, welcher Phantombite-Mod schuld ist, und ihn
in einen Sparmodus schicken (`PERFLEVEL|n`).

1. Alle `SampleInterval` Ticks wird `ServerSimulationRatio` gemessen (erst nach `StartupDelayTicks`).
2. Fällt sie unter `DropThreshold` und bleibt `StrikeDurationTicks` Ticks dort, zählt ein Strike.
   Nach einem Spieler-Join gibt es 30 s Schutz.
3. Läuft gerade eine HEAVY-Operation eines Mods (oder lag sie höchstens `HeavyGraceWindowSec`
   Sekunden zurück), zählt der Treffer für diesen Mod: Vertrauen 1.0 wenn der Server vorher stabil
   war, sonst 0.3.
4. Erreicht die Summe `CorrelationsBeforeEscalate`, steigt das Level des Mods entlang seines
   `EscalationPath`. Wiederholungstäter (gleiche Mod-Version) werden dauerhaft eingestuft.
5. Drops ohne Phantombite-Beteiligung werden nur gezählt und geloggt, nie eskaliert.
6. Neue Mod-Version → History und Level des Mods werden zurückgesetzt.

Historie: `Phantombite_PerfHistory.txt`. Dauerhafte Levels stehen in der GlobalConfig (`CurrentLevel`).

## ModDetector

Erkennt aktive Mods per Workshop-ID **und** per lokalem Namen. `Mode`: `Workshop`, `Local`
oder `Hybrid`; `IsDevMode` ist wahr, sobald nicht alles über Workshop läuft.
Loggt beim Start aktive Phantombite-Mods, MES-Abhängigkeit (`RequiresMES`) und fremde Mods.

## AdminChip

`Component/AdminChip`, 0.25 kg, 0.2 L, nicht herstellbar und nicht kaufbar. Blöcke mit AdminChip
als kritischer Komponente können nur Server-Admins platzieren.

## Bekannte Grenzen

Siehe `DEV_TODO.md` (Abschnitt „Bekannte Schwächen“).
