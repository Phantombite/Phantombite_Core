using System;
using System.Collections.Generic;
using System.Text;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;
using PhantombiteCore.Core;

namespace PhantombiteCore.Modules
{
    /// <summary>
    /// Core_Command — Command System
    ///
    /// Prefix: !pbc
    ///
    /// REGISTER-Paket Format (Mods → Core):
    ///   REGISTER|modName|modDesc|version|channel|cmd1:adminOnly:desc|...
    ///   Backward-Compat: Fehlt version (parts[3] ist eine Zahl) → version = "?.?.?"
    ///
    /// Admin-only Mod Logik (automatisch):
    ///   - Hat ein Mod ausschliesslich adminOnly=true Commands gilt er als Admin-only
    ///   - Normale Spieler sehen ihn nicht in !pbc help
    ///   - Sobald ein adminOnly=false Command registriert wird ist der Mod öffentlich
    ///   - Admins sehen immer alles
    /// </summary>
    public class CommandModule : IModule
    {
        public string ModuleName { get { return "Core_Command"; } }

        private const string PREFIX    = "!pbc";
        private const string MOD       = "Phantombite_Core";
        private const string MODULE    = "Core_Command";
        private const int    PAGE_SIZE = 7;

        private const ushort CMD_TO_SERVER_PACKET       = 5997;
        private const ushort CMDRESULT_TO_CLIENT_PACKET = 5998;

        private ModDetector       _modDetector;
        private FileManagerModule _fileManager;
        private bool _initialized = false;

        private readonly Dictionary<long, string>             _modChannels     = new Dictionary<long, string>();
        private readonly Dictionary<string, string>           _modDescriptions = new Dictionary<string, string>();
        private readonly Dictionary<string, string>           _modVersions     = new Dictionary<string, string>();
        private readonly Dictionary<string, List<CommandInfo>> _modCommands    = new Dictionary<string, List<CommandInfo>>();
        private readonly Dictionary<string, int>              _tempLevels      = new Dictionary<string, int>();
        private PerformanceModule _performanceModule;
        private PlayerTrackerModule _playerTracker;

        private class PendingCommand
        {
            public ulong    SteamId;
            public DateTime SentAt;
        }
        private readonly Dictionary<string, PendingCommand> _pendingCommands = new Dictionary<string, PendingCommand>();
        private const double COMMAND_TIMEOUT_SEC = 10.0;
        private int _timeoutCheckTick = 0;

        private class CommandInfo
        {
            public string Name;
            public string Description;
            public bool   AdminOnly;
            public Action<IMyPlayer, string[]> Handler;

            public CommandInfo(string name, string description, bool adminOnly, Action<IMyPlayer, string[]> handler)
            {
                Name = name; Description = description; AdminOnly = adminOnly; Handler = handler;
            }
        }

        public void SetModDetector(ModDetector modDetector)         { _modDetector = modDetector; }
        public void SetFileManager(FileManagerModule fileManager)   { _fileManager = fileManager; }
        public void SetPerformanceModule(PerformanceModule perf)    { _performanceModule = perf; }
        public void SetPlayerTracker(PlayerTrackerModule tracker)    { _playerTracker = tracker; }

        // ── IModule ──────────────────────────────────────────────────────────

        public void Init()
        {
            if (_initialized) return;
            MyAPIGateway.Utilities.MessageEntered += OnMessageEntered;
            MyAPIGateway.Utilities.RegisterMessageHandler(1995000L, OnModRegistration);
            MyAPIGateway.Utilities.RegisterMessageHandler(1995999L, OnLogReceived);
            MyAPIGateway.Multiplayer.RegisterMessageHandler(CMD_TO_SERVER_PACKET,       OnClientCmdReceived);
            MyAPIGateway.Multiplayer.RegisterMessageHandler(CMDRESULT_TO_CLIENT_PACKET, OnServerCmdResultReceived);
            _initialized = true;
            PBLog.Log(MOD, MODULE, "Initialisiert — Prefix: " + PREFIX);
        }

        public void Update()
        {
            _timeoutCheckTick++;
            if (_timeoutCheckTick < 60) return;
            _timeoutCheckTick = 0;

            var now     = DateTime.UtcNow;
            var expired = new List<string>();
            foreach (var kvp in _pendingCommands)
                if ((now - kvp.Value.SentAt).TotalSeconds > COMMAND_TIMEOUT_SEC)
                    expired.Add(kvp.Key);

            foreach (var key in expired)
            {
                PBLog.Warn(MOD, MODULE, "Command Timeout — keine Antwort: " + key);
                _pendingCommands.Remove(key);
            }
        }

        public void SaveData() { }

        public void Close()
        {
            if (!_initialized) return;
            if (MyAPIGateway.Utilities != null)
            {
                MyAPIGateway.Utilities.MessageEntered -= OnMessageEntered;
                MyAPIGateway.Utilities.UnregisterMessageHandler(1995000L, OnModRegistration);
                MyAPIGateway.Utilities.UnregisterMessageHandler(1995999L, OnLogReceived);
                MyAPIGateway.Multiplayer.UnregisterMessageHandler(CMD_TO_SERVER_PACKET,       OnClientCmdReceived);
                MyAPIGateway.Multiplayer.UnregisterMessageHandler(CMDRESULT_TO_CLIENT_PACKET, OnServerCmdResultReceived);
            }
            _initialized = false;
        }

        // ── Mod Registrierung ────────────────────────────────────────────────

        private void RegisterMod(string modName, string description)
        {
            modName = modName.ToLower();
            _modDescriptions[modName] = description;
            if (!_modCommands.ContainsKey(modName))
                _modCommands[modName] = new List<CommandInfo>();
        }

        private void RegisterCommand(string modName, string commandName, string description, bool adminOnly, Action<IMyPlayer, string[]> handler)
        {
            modName     = modName.ToLower();
            commandName = commandName.ToLower();
            if (!_modCommands.ContainsKey(modName))
                _modCommands[modName] = new List<CommandInfo>();
            _modCommands[modName].Add(new CommandInfo(commandName, description, adminOnly, handler));
            PBLog.Log(MOD, MODULE, "Command: " + modName + " — " + commandName + (adminOnly ? " [A]" : ""), 1);
        }

        private bool IsAdminOnlyMod(string modName)
        {
            if (!_modCommands.ContainsKey(modName)) return true;
            foreach (var cmd in _modCommands[modName])
                if (!cmd.AdminOnly) return false;
            return true;
        }

        // ── Nachrichten-Handler ──────────────────────────────────────────────

        private void OnModRegistration(object data)
        {
            try
            {
                string msg = data as string;
                if (string.IsNullOrEmpty(msg)) return;

                if (msg.StartsWith("CMDRESULT|")) { OnCmdResult(msg); return; }
                if (!msg.StartsWith("REGISTER|")) return;

                string[] parts = msg.Split('|');
                if (parts.Length < 4) return;

                string modName = parts[1].ToLower();
                string modDesc = parts[2];

                // Backward-Compat: parts[3] kann version ODER channel sein
                string version;
                long   channel;
                int cmdOffset;

                if (long.TryParse(parts[3], out channel))
                {
                    // Altes Format: kein version-Feld
                    version   = "?.?.?";
                    cmdOffset = 4;
                }
                else
                {
                    // Neues Format: parts[3] = version, parts[4] = channel
                    version = parts[3];
                    if (parts.Length < 5 || !long.TryParse(parts[4], out channel)) return;
                    cmdOffset = 5;
                }

                RegisterMod(modName, modDesc);
                _modChannels[channel]  = modName;

                // Versions-Änderung prüfen → automatischer Performance-Reset
                string oldVersion;
                if (_modVersions.TryGetValue(modName, out oldVersion) && oldVersion != version && oldVersion != "?.?.?")
                    _performanceModule?.OnModVersionChanged(modName, oldVersion, version);

                _modVersions[modName] = version;

                for (int i = cmdOffset; i < parts.Length; i++)
                {
                    string[] cmdParts = parts[i].Split(':');
                    if (cmdParts.Length < 3) continue;

                    string cmdName   = cmdParts[0];
                    bool   adminOnly = cmdParts[1] == "1";
                    string cmdDesc   = cmdParts[2];
                    long   ch        = channel;

                    RegisterCommand(modName, cmdName, cmdDesc, adminOnly,
                        (player, args) => SendCommandToMod(player, ch, cmdName, args));
                }

                // Zusammenfassung im Log (Level 0 — immer sichtbar)
                int cmdCount = _modCommands.ContainsKey(modName) ? _modCommands[modName].Count : 0;
                PBLog.Log(MOD, MODULE,
                    PadRight(CapFirst(modName), 18) + " v" + PadRight(version, 8) +
                    ": " + cmdCount + " Commands registriert");

                SendLogLevel(channel, modName);
                _performanceModule?.OnModRegistered(modName);
            }
            catch (Exception ex)
            {
                PBLog.Error(MOD, MODULE, "Fehler in OnModRegistration", ex);
            }
        }

        private void SendLogLevel(long channel, string modName)
        {
            try
            {
                string fullName = "Phantombite_" + char.ToUpper(modName[0]) + modName.Substring(1);
                int    level    = PBLog.GetLevel(fullName);
                MyAPIGateway.Utilities.SendModMessage(channel, "LOGLEVEL|" + level);
                PBLog.Log(MOD, MODULE, "LOGLEVEL " + level + " → " + modName, 1);
            }
            catch (Exception ex)
            {
                PBLog.Error(MOD, MODULE, "Fehler in SendLogLevel", ex);
            }
        }

        private void OnLogReceived(object data)
        {
            try
            {
                string msg = data as string;
                if (string.IsNullOrEmpty(msg) || !msg.StartsWith("LOG|")) return;

                string[] parts = msg.Split(new[] { '|' }, 5);
                if (parts.Length < 5) return;

                string modName  = parts[1];
                string levelStr = parts[2];
                string module   = parts[3];
                string message  = parts[4];

                // Unterstützt altes Format (WARN/ERROR/INFO/DEBUG/TRACE)
                // und neues Format (0/1/2)
                switch (levelStr)
                {
                    case "WARN":  case "warn":  PBLog.Warn(modName, module, message);          break;
                    case "ERROR": case "error": PBLog.Error(modName, module, message);         break;
                    case "INFO":  case "0":     PBLog.Log(modName, module, message, 0);        break;
                    case "DEBUG": case "1":     PBLog.Log(modName, module, message, 1);        break;
                    case "TRACE": case "2":     PBLog.Log(modName, module, message, 2);        break;
                    default:                    PBLog.Log(modName, module, message, 0);        break;
                }
            }
            catch (Exception ex)
            {
                PBLog.Error(MOD, MODULE, "Fehler in OnLogReceived", ex);
            }
        }

        private void OnCmdResult(string msg)
        {
            try
            {
                string[] parts = msg.Split(new[] { '|' }, 7);
                if (parts.Length < 7) return;

                string key    = parts[1] + "|" + parts[2] + "|" + parts[3] + "|" + parts[4];
                string status = parts[5];
                string result = parts[6];

                ulong targetSteamId = 0;
                ulong.TryParse(parts[4], out targetSteamId);

                PendingCommand pending;
                if (_pendingCommands.TryGetValue(key, out pending))
                {
                    double ms = (DateTime.UtcNow - pending.SentAt).TotalMilliseconds;
                    _pendingCommands.Remove(key);
                    PBLog.Log(MOD, MODULE, "CMDRESULT: " + key + " — " + status + " (" + ms.ToString("F0") + "ms)", 1);
                }
                else
                {
                    PBLog.Log(MOD, MODULE, "CMDRESULT (server): " + status + " — " + result, 1);
                }

                bool ok = status == "ok";

                if (_modDetector != null && !_modDetector.IsSingleplayer && MyAPIGateway.Multiplayer.IsServer)
                {
                    byte[] resultData = Encoding.UTF8.GetBytes(msg);
                    MyAPIGateway.Multiplayer.SendMessageTo(CMDRESULT_TO_CLIENT_PACKET, resultData, targetSteamId);
                }
                else
                {
                    ShowHud(result, ok);
                }
            }
            catch (Exception ex)
            {
                PBLog.Error(MOD, MODULE, "Fehler in OnCmdResult", ex);
            }
        }

        private void OnClientCmdReceived(byte[] data)
        {
            try
            {
                if (!MyAPIGateway.Multiplayer.IsServer) return;
                string packet = Encoding.UTF8.GetString(data);
                int sep = packet.IndexOf('|');
                if (sep <= 0) return;
                long channel;
                if (!long.TryParse(packet.Substring(0, sep), out channel)) return;
                string msg = packet.Substring(sep + 1);
                MyAPIGateway.Utilities.SendModMessage(channel, msg);
            }
            catch (Exception ex)
            {
                PBLog.Error(MOD, MODULE, "Fehler in OnClientCmdReceived", ex);
            }
        }

        private void OnServerCmdResultReceived(byte[] data)
        {
            try
            {
                if (MyAPIGateway.Multiplayer.IsServer) return;
                string msg = Encoding.UTF8.GetString(data);
                string[] parts = msg.Split(new[] { '|' }, 7);
                if (parts.Length < 7) return;
                bool ok = parts[5] == "ok";
                ShowHud(parts[6], ok);
            }
            catch (Exception ex)
            {
                PBLog.Error(MOD, MODULE, "Fehler in OnServerCmdResultReceived", ex);
            }
        }

        // ── READY senden ─────────────────────────────────────────────────────

        public void SendReadyToActiveMods(ModDetector modDetector)
        {
            var modChannels = new Dictionary<ulong, long>
            {
                { ModRegistry.Artefact,           ModRegistry.ChannelArtefact           },
                { ModRegistry.CableWinch,         ModRegistry.ChannelCableWinch         },
                { ModRegistry.Creatures,          ModRegistry.ChannelCreatures          },
                { ModRegistry.Economy,            ModRegistry.ChannelEconomy            },
                { ModRegistry.Encounter,          ModRegistry.ChannelEncounter          },
                { ModRegistry.ServerAddon,        ModRegistry.ChannelServerAddon        },
                { ModRegistry.Sulvax,             ModRegistry.ChannelSulvax             },
                { ModRegistry.SulvaxRespawnRover, ModRegistry.ChannelSulvaxRespawnRover },
                { ModRegistry.AutoTransfer,       ModRegistry.ChannelAutoTransfer       },
                { ModRegistry.PlanetSpawner,      ModRegistry.ChannelPlanetSpawner      },
                { ModRegistry.AdminProjektor,     ModRegistry.ChannelAdminProjektor     },
                { ModRegistry.WaterElectrolyzer,  ModRegistry.ChannelWaterElectrolyzer  },
                { ModRegistry.Mining,             ModRegistry.ChannelMining             },
                { ModRegistry.StationRefill,      ModRegistry.ChannelStationRefill      },
            };

            int sent = 0;
            foreach (var kvp in modChannels)
            {
                string source = modDetector.GetLoadSource(kvp.Key);
                if (source == null)
                {
                    PBLog.Log(MOD, MODULE, "READY übersprungen — nicht aktiv: " + ModRegistry.GetName(kvp.Key), 1);
                    continue;
                }
                try
                {
                    MyAPIGateway.Utilities.SendModMessage(kvp.Value, "READY");
                    sent++;
                }
                catch (Exception ex)
                {
                    PBLog.Error(MOD, MODULE, "READY fehlgeschlagen — " + ModRegistry.GetName(kvp.Key), ex);
                }
            }
            PBLog.Log(MOD, MODULE, "READY gesendet: " + sent + "/" + modChannels.Count + " Mods");
        }

        // ── Chat-Command Parsing ─────────────────────────────────────────────

        private void OnMessageEntered(string messageText, ref bool sendToOthers)
        {
            try
            {
                string trimmed = messageText.Trim();

                if (trimmed.Equals("help",  StringComparison.OrdinalIgnoreCase) ||
                    trimmed.Equals("/help", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.Equals("!help", StringComparison.OrdinalIgnoreCase))
                {
                    IMyPlayer player = MyAPIGateway.Session?.Player;
                    if (player != null)
                        Send(player, PREFIX + " help — Für Hilfe bei PhantomBite Mods");
                    return;
                }

                if (!trimmed.StartsWith(PREFIX, StringComparison.OrdinalIgnoreCase) &&
                    !trimmed.StartsWith("/pbc", StringComparison.OrdinalIgnoreCase)) return;
                sendToOthers = false;

                IMyPlayer p = MyAPIGateway.Session?.Player;
                if (p == null) return;

                string commandText = trimmed.StartsWith("/pbc", StringComparison.OrdinalIgnoreCase)
                    ? trimmed.Substring(4).Trim()
                    : trimmed.Substring(PREFIX.Length).Trim();

                if (string.IsNullOrWhiteSpace(commandText)) { CmdHelp(p, new string[0]); return; }

                string[] tokens = commandText.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                ParseAndExecute(p, tokens);
            }
            catch (Exception ex) { PBLog.Error(MOD, MODULE, "Fehler in OnMessageEntered", ex); }
        }

        private void ParseAndExecute(IMyPlayer player, string[] tokens)
        {
            try
            {
                // Jeden Command mit Spielernamen loggen
                // Command + Spieler in SE-Log und Phantombite-Log
                PBLog.Log(MOD, MODULE, player.DisplayName + " (" + player.SteamUserId + ") -> " + PREFIX + " " + string.Join(" ", tokens));

                string first = tokens[0].ToLower();

                if (first == "help")
                {
                    if (tokens.Length > 1 && _modCommands.ContainsKey(tokens[1].ToLower()))
                    {
                        int page = 1;
                        if (tokens.Length > 2) int.TryParse(tokens[2], out page);
                        CmdModHelp(player, tokens[1].ToLower(), page);
                        return;
                    }
                    CmdHelp(player, SubArgs(tokens, 1));
                    return;
                }
                if (first == "status") { CmdStatus(player); return; }

                if (first == "debug")
                {
                    if (!IsAdmin(player)) { Send(player, "Blockiert: Admin-Rechte erforderlich."); return; }
                    CmdDebug(player, SubArgs(tokens, 1));
                    return;
                }

                if (first == "players")
                {
                    string list = _playerTracker != null
                        ? _playerTracker.GetPlayerListText()
                        : "PlayerTracker nicht verfügbar";
                    Send(player, list);
                    return;
                }

                if (first == "perf")
                {
                    if (!IsAdmin(player)) { Send(player, "Blockiert: Admin-Rechte erforderlich."); return; }
                    CmdPerf(player, SubArgs(tokens, 1));
                    return;
                }

                if (first == "log")
                {
                    if (!IsAdmin(player)) { Send(player, "Blockiert: Admin-Rechte erforderlich."); return; }
                    CmdLog(player, SubArgs(tokens, 1));
                    return;
                }

                if (_modCommands.ContainsKey(first))
                {
                    if (IsAdminOnlyMod(first) && !IsAdmin(player))
                    {
                        Send(player, "Blockiert: Admin-Rechte erforderlich.");
                        return;
                    }

                    if (tokens.Length < 2) { CmdModHelp(player, first, 1); return; }
                    string second = tokens[1].ToLower();

                    if (second == "help")
                    {
                        int page = 1;
                        if (tokens.Length > 2) int.TryParse(tokens[2], out page);
                        CmdModHelp(player, first, page);
                        return;
                    }

                    int  parsedId = 0;
                    bool isId     = int.TryParse(second, out parsedId);
                    bool isAll    = second == "all";

                    if ((isId || isAll) && tokens.Length > 2)
                    {
                        string cmdName    = tokens[2].ToLower();
                        int    extraLen   = tokens.Length - 3;
                        string[] cmdArgs  = new string[1 + extraLen];
                        cmdArgs[0] = second;
                        Array.Copy(tokens, 3, cmdArgs, 1, extraLen);
                        ExecuteModCommand(player, first, cmdName, cmdArgs);
                        return;
                    }

                    ExecuteModCommand(player, first, second, SubArgs(tokens, 2));
                    return;
                }

                Send(player, "Unbekannter Command: " + first + " — tippe " + PREFIX + " help");
            }
            catch (Exception ex) { PBLog.Error(MOD, MODULE, "Fehler in ParseAndExecute", ex); }
        }

        // ── Commands ─────────────────────────────────────────────────────────

        private void CmdHelp(IMyPlayer player, string[] args)
        {
            bool isAdmin = IsAdmin(player);

            if (args != null && args.Length > 0)
            {
                string first = args[0].ToLower();
                if (_modCommands.ContainsKey(first))
                {
                    int page = 1;
                    if (args.Length > 1) int.TryParse(args[1], out page);
                    CmdModHelp(player, first, page);
                    return;
                }
            }

            var lines = new List<string>();
            lines.Add("!pbc help <Seitenzahl>");
            lines.Add("  Für eine andere Seite");

            foreach (var kvp in _modDescriptions)
            {
                if (IsAdminOnlyMod(kvp.Key) && !isAdmin) continue;
                lines.Add("!pbc help " + kvp.Key + " <Seitenzahl>");
                lines.Add("  Für " + CapFirst(kvp.Key) + " Help");
            }

            lines.Add("!pbc players");
            lines.Add("  Aktive Spieler mit Join-Zeit anzeigen");
            lines.Add("!pbc status");
            lines.Add("  Aktive Mods, Versionen und Debug-Level");

            if (isAdmin)
            {
                lines.Add("!pbc log show");
                lines.Add("  Letzte Log-Zeilen im Chat anzeigen");
                lines.Add("!pbc log copy");
                lines.Add("  Kompletten Log in Zwischenablage");
                lines.Add("!pbc debug <mod|all> <0|1|2>");
                lines.Add("  Debug-Level setzen (1x=temporär, 2x=permanent)");
                lines.Add("!pbc perf status");
                lines.Add("  Performance Level aller Mods + Top Verursacher");
                lines.Add("!pbc perf log");
                lines.Add("  Letzte SimSpeed-Ereignisse (10min RAM-Log)");
                lines.Add("!pbc perf reset [mod]");
                lines.Add("  Performance Level zurücksetzen");
            }

            ShowPage(player, lines, PREFIX + " help", args, "Phantombite Help");
        }

        private void CmdModHelp(IMyPlayer player, string modName, int page)
        {
            if (!_modCommands.ContainsKey(modName))
            {
                Send(player, "Mod nicht gefunden: " + modName);
                return;
            }

            bool isAdmin = IsAdmin(player);
            string desc  = _modDescriptions.ContainsKey(modName) ? _modDescriptions[modName] : "";

            var lines = new List<string>();
            if (!string.IsNullOrEmpty(desc)) lines.Add(desc);
            lines.Add("");

            foreach (var cmd in _modCommands[modName])
            {
                if (cmd.AdminOnly && !isAdmin) continue;
                string adminTag = cmd.AdminOnly ? "[Admin] " : "";
                lines.Add("  " + PREFIX + " " + modName + " " + cmd.Name + " — " + adminTag + cmd.Description);
            }

            ShowPage(player, lines, PREFIX + " " + modName + " help", new[] { page.ToString() }, CapFirst(modName) + " Help");
        }

        private void CmdStatus(IMyPlayer player)
        {
            string sessionStr = _modDetector != null
                ? (_modDetector.IsSingleplayer ? "Singleplayer" : "Dedicated Server") : "Unbekannt";
            string modeStr = _modDetector != null && _modDetector.IsDevMode ? "DEV" : "WORKSHOP";

            var lines = new List<string>();
            lines.Add("Session: " + sessionStr + " | Mode: " + modeStr);
            lines.Add("[Registrierte Mods]");

            foreach (var kvp in _modDescriptions)
            {
                string version = _modVersions.ContainsKey(kvp.Key) ? "v" + _modVersions[kvp.Key] : "v?.?.?";
                string fullName = "Phantombite_" + CapFirst(kvp.Key);
                int    level    = PBLog.GetLevel(fullName);
                bool   isTemp   = _tempLevels.ContainsKey(fullName);
                string levelStr = "Debug: " + level + (isTemp ? " (Temporär)" : "");
                lines.Add("  " + PadRight(CapFirst(kvp.Key), 18) + " " + PadRight(version, 10) + levelStr);
            }

            Send(player, "=== Phantombite Status ===");
            foreach (var line in lines)
                Send(player, line);
        }

        private void CmdDebug(IMyPlayer player, string[] args)
        {
            if (args.Length < 2)
            {
                Send(player, "Verwendung: " + PREFIX + " debug <mod|all> <0|1|2>");
                return;
            }

            string target   = args[0].ToLower();
            string levelStr = args[1].ToLower();
            int    level;

            if (!int.TryParse(levelStr, out level) || level < 0 || level > 2)
            {
                Send(player, "Unbekannter Level: " + levelStr + " — erlaubt: 0, 1, 2");
                return;
            }

            var allModNames = new List<string>
            {
                "Phantombite_Core",           "Phantombite_AdminProjektor",
                "Phantombite_Artefact",       "Phantombite_AutoTransfer",
                "Phantombite_CableWinch",     "Phantombite_Creatures",
                "Phantombite_Economy",        "Phantombite_Encounter",
                "Phantombite_Mining",         "Phantombite_PlanetSpawner",
                "Phantombite_Server_Addon",   "Phantombite_StationRefill",
                "Phantombite_Sulvax",         "Phantombite_SulvaxRespawnRover",
                "Phantombite_WaterElectrolyzer"
            };

            if (target == "all")
            {
                foreach (var modName in allModNames)
                    SetDebugLevel(player, modName, level, true);
                Send(player, "Alle Mods auf Debug-Level " + level + " gesetzt.");
                return;
            }

            string fullName = "Phantombite_" + CapFirst(target);
            if (!allModNames.Contains(fullName))
            {
                Send(player, "Unbekannter Mod: " + target);
                return;
            }

            SetDebugLevel(player, fullName, level, false);
        }

        private void SetDebugLevel(IMyPlayer player, string modName, int level, bool silent)
        {
            bool alreadyTemp = _tempLevels.ContainsKey(modName) && _tempLevels[modName] == level;
            bool isPermanent = !_tempLevels.ContainsKey(modName) && PBLog.GetLevel(modName) == level;
            string shortName = modName.Replace("Phantombite_", "");

            if (alreadyTemp || isPermanent)
            {
                _tempLevels.Remove(modName);
                PBLog.SetLevel(modName, level);
                SaveDebugToConfig(modName, level);
                if (!silent) Send(player, shortName + ": Level " + level + " — permanent gespeichert.");
            }
            else
            {
                if (!_tempLevels.ContainsKey(modName))
                    _tempLevels[modName] = PBLog.GetLevel(modName);
                PBLog.SetLevel(modName, level);
                if (!silent) Send(player, shortName + ": Level " + level + " — temporär. Nochmal = permanent.");
            }

            // LOGLEVEL an den Mod senden falls er registriert ist
            foreach (var kvp in _modChannels)
            {
                if (_modDescriptions.ContainsKey(kvp.Value))
                {
                    string fullName = "Phantombite_" + CapFirst(kvp.Value);
                    if (fullName == modName)
                    {
                        SendLogLevel(kvp.Key, kvp.Value);
                        break;
                    }
                }
            }
        }

        private void SaveDebugToConfig(string modName, int level)
        {
            try
            {
                string content = FileManagerModule.ReadFile("Phantombite_GlobalConfig.ini", typeof(FileManagerModule));
                if (content == null) return;

                string oldKey  = modName + "=";
                string newLine = modName + "=" + level;

                var rawLines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.None);
                var sb = new StringBuilder();
                foreach (var line in rawLines)
                    sb.AppendLine(line.TrimStart().StartsWith(oldKey) ? newLine : line);

                FileManagerModule.WriteFile("Phantombite_GlobalConfig.ini", sb.ToString(), typeof(FileManagerModule));
            }
            catch (Exception ex)
            {
                PBLog.Error(MOD, MODULE, "Fehler beim Speichern der GlobalConfig", ex);
            }
        }

        private void CmdPerf(IMyPlayer player, string[] args)
        {
            if (_performanceModule == null)
            {
                Send(player, "Performance Modul nicht verfügbar.");
                return;
            }

            string sub = args.Length > 0 ? args[0].ToLower() : "status";

            if (sub == "status")
            {
                Send(player, "=== Performance Status ===");
                foreach (var line in _performanceModule.GetStatusText().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    Send(player, line);
                return;
            }

            if (sub == "log")
            {
                Send(player, "=== Performance Log (letzte Ereignisse) ===");
                foreach (var line in _performanceModule.GetRecentLog())
                    Send(player, line);
                return;
            }

            if (sub == "reset")
            {
                if (args.Length > 1)
                {
                    string mod = args[1].ToLower();
                    _performanceModule.ResetMod(mod);
                    Send(player, mod + " Performance Level zurückgesetzt.");
                }
                else
                {
                    _performanceModule.ResetAll();
                    Send(player, "Alle Performance Level zurückgesetzt.");
                }
                return;
            }

            Send(player, "Verwendung: !pbc perf status | log | reset [mod]");
        }

        // ── Public Helfer für PerformanceModule ──────────────────────────────

        /// <summary>Sendet eine Nachricht an einen registrierten Mod über seinen Kanal.</summary>
        public void SendToMod(string modName, string message)
        {
            try
            {
                modName = modName.ToLower();
                foreach (var kvp in _modChannels)
                {
                    if (kvp.Value == modName)
                    {
                        MyAPIGateway.Utilities.SendModMessage(kvp.Key, message);
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                PBLog.Error(MOD, MODULE, "Fehler in SendToMod (" + modName + ")", ex);
            }
        }

        /// <summary>Gibt die registrierte Version eines Mods zurück.</summary>
        public string GetModVersion(string modName)
        {
            string version;
            return _modVersions.TryGetValue(modName.ToLower(), out version) ? version : "?.?.?";
        }

        /// <summary>Gibt alle aktuell registrierten Mod-Namen zurück.</summary>
        public List<string> GetRegisteredMods()
        {
            return new List<string>(_modDescriptions.Keys);
        }

        private void CmdLog(IMyPlayer player, string[] args)
        {
            if (_fileManager == null) { Send(player, "FileManager nicht verfügbar."); return; }

            string sub = args.Length > 0 ? args[0].ToLower() : "show";

            if (sub == "show")
            {
                string content = _fileManager.ReadCurrentLog();
                if (content == null) { Send(player, "Keine Log-Datei gefunden."); return; }
                var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                int start = Math.Max(0, lines.Length - PAGE_SIZE);
                Send(player, "=== Log (" + _fileManager.GetCurrentLogFile() + ") ===");
                for (int i = start; i < lines.Length; i++)
                    Send(player, lines[i]);
                return;
            }

            if (sub == "copy")
            {
                string content = _fileManager.ReadCurrentLog();
                if (content == null) { Send(player, "Keine Log-Datei gefunden."); return; }
                VRage.Utils.MyClipboardHelper.SetClipboard(content);
                Send(player, "Log in Zwischenablage: " + _fileManager.GetCurrentLogFile());
                return;
            }

            Send(player, "Verwendung: !pbc log show | !pbc log copy");
        }

        private void ExecuteModCommand(IMyPlayer player, string modName, string commandName, string[] args)
        {
            CommandInfo cmd = null;
            foreach (var c in _modCommands[modName])
                if (c.Name == commandName) { cmd = c; break; }

            if (cmd == null)
            {
                Send(player, "Command nicht gefunden: " + commandName + " — tippe " + PREFIX + " " + modName + " help");
                return;
            }

            if (cmd.AdminOnly && !IsAdmin(player))
            {
                Send(player, "Blockiert: Admin-Rechte erforderlich.");
                return;
            }

            cmd.Handler(player, args);
        }

        private void SendCommandToMod(IMyPlayer player, long channel, string commandName, string[] args)
        {
            try
            {
                string argsJoined = args != null && args.Length > 0 ? string.Join("|", args) : "";
                string modName    = _modChannels.ContainsKey(channel) ? _modChannels[channel] : channel.ToString();
                string key        = modName + "|" + commandName + "|" + argsJoined + "|" + player.SteamUserId;

                if (_pendingCommands.ContainsKey(key))
                {
                    ShowHud(modName + " " + commandName + ": bereits in Bearbeitung", false);
                    return;
                }

                string msg = "CMD|" + commandName;
                if (!string.IsNullOrEmpty(argsJoined)) msg += "|" + argsJoined;
                msg += "|STEAM:" + player.SteamUserId;

                _pendingCommands[key] = new PendingCommand { SteamId = player.SteamUserId, SentAt = DateTime.UtcNow };

                if (_modDetector != null && !_modDetector.IsSingleplayer)
                {
                    string packet    = channel + "|" + msg;
                    byte[] packetData = Encoding.UTF8.GetBytes(packet);
                    MyAPIGateway.Multiplayer.SendMessageToServer(CMD_TO_SERVER_PACKET, packetData);
                }
                else
                {
                    MyAPIGateway.Utilities.SendModMessage(channel, msg);
                }
            }
            catch (Exception ex)
            {
                PBLog.Error(MOD, MODULE, "Fehler in SendCommandToMod", ex);
            }
        }

        // ── UI Helfer ────────────────────────────────────────────────────────

        private void ShowHud(string message, bool success)
        {
            try
            {
                var font = success ? MyFontEnum.Green : MyFontEnum.Red;
                MyAPIGateway.Utilities.ShowNotification("[PB] " + message, 3000, font);
            }
            catch { }
        }

        private void ShowPage(IMyPlayer player, List<string> lines, string baseCommand, string[] args, string title = "")
        {
            int page = 1;
            if (args != null && args.Length > 0) int.TryParse(args[0], out page);
            if (page < 1) page = 1;

            int totalPages = (int)Math.Ceiling(lines.Count / (double)PAGE_SIZE);
            if (totalPages < 1) totalPages = 1;
            if (page > totalPages) page = totalPages;

            if (!string.IsNullOrEmpty(title))
            {
                string pageStr = totalPages > 1 ? " (" + page + "/" + totalPages + ")" : "";
                Send(player, "=== " + title + pageStr + " ===");
            }

            int start = (page - 1) * PAGE_SIZE;
            int end   = Math.Min(start + PAGE_SIZE, lines.Count);

            for (int i = start; i < end; i++)
                Send(player, lines[i]);
        }

        public bool IsAdmin(IMyPlayer player)
        {
            if (player == null) return false;
            if (_modDetector != null && _modDetector.IsSingleplayer) return true;
            if (MyAPIGateway.Session.OnlineMode == MyOnlineModeEnum.OFFLINE) return true;
            return player.PromoteLevel >= MyPromoteLevel.Admin;
        }

        public void Send(IMyPlayer player, string message)
        {
            if (player == null || MyAPIGateway.Utilities == null) return;
            try { MyAPIGateway.Utilities.ShowMessage("[PB-Core]", message); }
            catch (Exception ex) { PBLog.Error(MOD, MODULE, "Fehler beim Senden", ex); }
        }

        // ── Interne Helfer ───────────────────────────────────────────────────

        private string[] SubArgs(string[] tokens, int from)
        {
            if (from >= tokens.Length) return new string[0];
            string[] result = new string[tokens.Length - from];
            Array.Copy(tokens, from, result, 0, result.Length);
            return result;
        }

        private static string PadRight(string s, int width)
        {
            if (s == null) s = "";
            return s.Length >= width ? s + " " : s + new string(' ', width - s.Length);
        }

        private static string CapFirst(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return char.ToUpper(s[0]) + s.Substring(1);
        }
    }
}