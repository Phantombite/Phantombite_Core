# DEV Anbindung — Neuen Mod an Phantombite Core anbinden

Stand: 2026-09-19. Ein Mod ist eine eigene Assembly und kann Core nicht direkt aufrufen. Die
gesamte Kommunikation läuft über `MyAPIGateway.Utilities.SendModMessage` /
`RegisterMessageHandler`. Als Vorbild dienen `Phantombite_Artefact` (`ArtefactCommand.cs`) und
`Phantombite_Autotransfer` (`AutoTransfer_Command.cs`).

## Ablauf

```
Spielwelt startet → Core wartet nicht, er schreibt zuerst
Core sendet "READY" an den Kanal jedes aktiven Mods
Mod empfängt READY → sendet REGISTER an Kanal 1995000
Core antwortet mit "LOGLEVEL|0..2" (und ggf. "PERFLEVEL|n")
Ab jetzt kommen Commands als "CMD|..." auf dem Mod-Kanal
```

Grund: SE startet Mods in unbekannter Reihenfolge. Der Mod muss deshalb schon in `BeforeStart`
(Init) auf seinem Kanal lauschen und darf erst nach READY registrieren.

## Nachrichten

**Core → Mod** (auf dem Kanal des Mods)

| Nachricht | Bedeutung |
|---|---|
| `READY` | Core ist bereit, jetzt `REGISTER` senden |
| `LOGLEVEL\|n` | Debug-Level 0, 1 oder 2. Der Mod filtert selbst und sendet nur, was das Level erlaubt |
| `CMD\|cmd\|arg1\|arg2\|STEAM:steamId` | Command ausführen. Das letzte Feld ist immer `STEAM:` und muss abgetrennt werden |
| `PERFLEVEL\|n` | Performance-Level. 0 = normal, höher = mehr sparen. Mit `PERFACK\|mod\|n` bestätigen |

**Mod → Core**

| Kanal | Nachricht |
|---|---|
| 1995000 | `REGISTER\|name\|beschreibung\|version\|kanal\|cmd:admin:beschreibung\|...` |
| 1995000 | `HEAVY_START\|name\|operation` und `HEAVY_END\|name\|operation` um rechenintensive Vorgänge zu markieren |
| 1995000 | `PERFACK\|name\|level` |
| 1995999 | `LOG\|Phantombite_Mod\|0..2\|Modul\|Text` (auch `WARN`/`ERROR` möglich) |
| 1995999 | `CMDRESULT\|name\|cmd\|args\|steamId\|ok\|Text` (Status `ok` oder `fail`) — **auf 1995999, nicht auf dem Mod-Kanal** |

Regeln für `REGISTER`:
- `name` immer kleingeschrieben und ohne Unterstrich (`cablewinch`, `autotransfer`). Core ordnet
  ihn dem lokalen Namen zu (`Phantombite_Cable_Winch`).
- `version` z. B. `1.0.0`. Fehlt das Feld (altes Format), erkennt Core das automatisch, dann gibt
  es aber keinen Reset der Performance-History bei Updates.
- `admin`: `1` = nur Admin, `0` = alle Spieler. Hat ein Mod nur Admin-Commands, ist er für normale
  Spieler unsichtbar.

`CMDRESULT` muss exakt mit den Argumenten des Commands zurückkommen (`args` = die Argumente ohne
`STEAM:`, mit `|` verbunden), sonst findet Core den offenen Command nicht. Der Mod zeigt kein
eigenes `ShowNotification` für Command-Feedback, das macht Core (grün/rot).
Bleibt `CMDRESULT` länger als 10 s aus, schreibt Core eine WARN.

Der Spieler kann `!pbc <mod> [id|all] <cmd> [args]` eingeben. Gibt er eine ID oder `all` an, steht
sie als **erstes Argument** (`args[0]`), sonst fehlt sie und der Mod nimmt seinen Standard (ID 0).

## Schritte

### 1. Im Core (`ModRegistry.cs`)

- [ ] Workshop-ID (`public const ulong MeinMod`) und lokalen Namen (`LocalMeinMod`, muss genau dem Ordnernamen entsprechen)
- [ ] Kanal (`ChannelMeinMod`, nächste freie Nummer ab 1995017; 1995015 ist ungenutzt)
- [ ] In `AllPbIds`, in `AllLocalNames`, in `GetLocalName()` und `GetName()`
- [ ] Nutzt der Mod `HEAVY_START`: Kurzname in `PerformanceMods`
- [ ] In `Core command.cs` → `SendReadyToActiveMods()` das Paar ID/Kanal ergänzen
- [ ] Braucht der Mod MES: ID in `RequiresMES`

Die GlobalConfig (`[Debug]`, `[Performance.<Mod>]`), `!pbc debug` und die Statusausgabe
verwenden diese Listen automatisch, dort ist nichts mehr zu ergänzen.

### 2. Im neuen Mod

Struktur nach `0_Phantombite_MOD_TEMPLATE.md`: `Core/` mit `Session`, `IModule`, `ModuleManager`
(Kopien aus Core, im Namespace des Mods) und `Modules/<Mod>_Command.cs`.

- [ ] Kanal-Handler in `Init()` registrieren, in `Close()` wieder entfernen
- [ ] Auf `READY` mit `REGISTER` antworten (Name, Beschreibung, Version, Kanal, Commands)
- [ ] `LOGLEVEL` speichern und beim Loggen selbst filtern
- [ ] `CMD` parsen (`STEAM:` abtrennen, ID/`all` als `args[0]` behandeln), ausführen, `CMDRESULT` auf 1995999 senden
- [ ] Rechenintensive Abschnitte mit `HEAVY_START`/`HEAVY_END` markieren und auf `PERFLEVEL` reagieren
- [ ] Nur Server-seitig arbeiten wo nötig (`IsServer`-Check) und Singleplayer testen

## Minimales Gerüst (Command-Modul)

```csharp
private const long CORE_CHANNEL = 1995000L;
private const long MY_CHANNEL   = 1995017L;   // Beispiel
private const long LOG_CHANNEL  = 1995999L;
private int _logLevel = 0;

public void Init()
{
    MyAPIGateway.Utilities.RegisterMessageHandler(MY_CHANNEL, OnMessage);
}

private void OnMessage(object data)
{
    string msg = data as string;
    if (string.IsNullOrEmpty(msg)) return;

    if (msg == "READY")
    {
        MyAPIGateway.Utilities.SendModMessage(CORE_CHANNEL,
            "REGISTER|meinmod|Beschreibung für !pbc help|1.0.0|" + MY_CHANNEL +
            "|aktion:1:Beschreibung|info:0:Für alle Spieler");
    }
    else if (msg.StartsWith("LOGLEVEL|"))
    {
        int.TryParse(msg.Substring(9), out _logLevel);
    }
    else if (msg.StartsWith("CMD|"))
    {
        string[] parts = msg.Split('|');                     // CMD|cmd|args...|STEAM:id
        string cmd = parts[1];
        string steam = parts[parts.Length - 1].Replace("STEAM:", "");
        string[] args = new string[Math.Max(0, parts.Length - 3)];
        Array.Copy(parts, 2, args, 0, args.Length);
        string argStr = string.Join("|", args);

        // ... Logik ausführen ...

        MyAPIGateway.Utilities.SendModMessage(LOG_CHANNEL,
            "CMDRESULT|meinmod|" + cmd + "|" + argStr + "|" + steam + "|ok|Fertig");
    }
}
```

## Häufige Fehler

| Problem | Ursache / Lösung |
|---|---|
| Mod bekommt kein READY | Handler wird zu spät registriert. Er muss in `BeforeStart` bereit sein, bevor Core `SendReadyToActiveMods()` aufruft |
| Kein HUD-Feedback nach Command | `CMDRESULT` an falschen Kanal oder mit anderen Argumenten gesendet |
| Mod erscheint nicht in `!pbc help` | Nur Admin-Commands (gewollt) oder Name in `REGISTER` nicht klein/ohne Unterstrich |
| Debug-Level wirkt nicht | Lokaler Name in `ModRegistry` passt nicht zum Ordnernamen, oder Mod filtert nicht selbst |
| Im Dev-Betrieb nicht erkannt | `LocalMeinMod` muss exakt dem Namen des Mod-Ordners entsprechen |

## Sicherheit

Core prüft Commands auf dem Server selbst: Kanal, Command und Admin-Recht des echten Absenders,
und ersetzt `STEAM:` durch die echte SteamId. Ein Mod kann sich also auf `STEAM:` und auf das
`adminOnly`-Flag verlassen. Commands, die ein Mod zusätzlich selbst per Chat oder anderem Weg
annimmt, muss er selbst absichern.

Damit die Prüfung greift, muss ein Command **immer** über `REGISTER` angemeldet werden. Nicht
angemeldete Commands lehnt der Server ab.
