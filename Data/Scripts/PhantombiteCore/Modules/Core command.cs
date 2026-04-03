using System;
using System.Collections.Generic;
using System.Text;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Utils;
using PhantombiteCore.Core;

namespace PhantombiteCore.Modules
{
    /// <summary>
    /// Core_Command - Command System
    ///
    /// Prefix: !pbc
    ///
    /// Admin-only Mod Logik (automatisch):
    ///   - Hat ein Mod ausschliesslich adminOnly=true Commands gilt er als Admin-only
    ///   - Normale Spieler sehen ihn nicht in !pbc help
    ///   - !pbc artefact help und !pbc artefact command → "Blockiert: Admin-Rechte erforderlich"
    ///   - Sobald ein adminOnly=false Command registriert wird ist der Mod automatisch öffentlich
    ///   - Admins sehen immer alles
    /// </summary>
    public class CommandModule : IModule
    {
        public string ModuleName { get { return "Core_Command"; } }

        private const string PREFIX    = "!pbc";
        private const string MOD       = "Phantombite_Core";
        private const string MODULE    = "Core_Command";
        private const int    PAGE_SIZE = 7;

        private ModDetector       _modDetector;
        private FileManagerModule _fileManager;
        private bool _initialized = false;

        // ── Mod-zu-Mod Messaging ──────────────────────────────────────────────
        // Kanal-ID → Mod-Name (für Command-Weiterleitung)
        private readonly Dictionary<long, string> _modChannels = new Dictionary<long, string>();

        private readonly Dictionary<string, string>            _modDescriptions = new Dictionary<string, string>();
        private readonly Dictionary<string, List<CommandInfo>> _modCommands     = new Dictionary<string, List<CommandInfo>>();
        private readonly Dictionary<string, LoggerModule.LogLevel> _tempLevels  = new Dictionary<string, LoggerModule.LogLevel>();

        // ── Pending Commands (Deduplizierung + Result-Tracking) ───────────────
        // Key = "modName|cmdName|argsJoined|steamId"
        private class PendingCommand
        {
            public ulong    SteamId;
            public DateTime SentAt;
        }
        private readonly Dictionary<string, PendingCommand> _pendingCommands = new Dictionary<string, PendingCommand>();
        private const double COMMAND_TIMEOUT_SEC = 2.0;
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

        // ── Setup ─────────────────────────────────────────────────────────────

        public void SetModDetector(ModDetector modDetector) { _modDetector = modDetector; }
        public void SetFileManager(FileManagerModule fileManager) { _fileManager = fileManager; }

        // ── IModule ───────────────────────────────────────────────────────────

        public void Init()
        {
            if (_initialized) return;
            MyAPIGateway.Utilities.MessageEntered += OnMessageEntered;
            MyAPIGateway.Utilities.RegisterMessageHandler(1995000L, OnModRegistration);
            MyAPIGateway.Utilities.RegisterMessageHandler(1995999L, OnLogReceived);
            _initialized = true;
            LoggerModule.Info(MOD, MODULE, "Initialized — Prefix: " + PREFIX);
        }

        public void Update()
        {
            // Pending Commands auf Timeout prüfen (alle 60 Ticks = 1 Sek)
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
                LoggerModule.Warn(MOD, MODULE, "Command Timeout — keine Antwort: " + key);
                LoggerModule.Trace(MOD, MODULE, $"Timeout entfernt: '{key}', Pending verbleibend: {_pendingCommands.Count - 1}");
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
            }
            _initialized = false;
        }

        // ── Public API für andere Mods ────────────────────────────────────────

        public void RegisterMod(string modName, string description)
        {
            modName = modName.ToLower();
            _modDescriptions[modName] = description;
            if (!_modCommands.ContainsKey(modName))
                _modCommands[modName] = new List<CommandInfo>();
            LoggerModule.Info(MOD, MODULE, "Mod registriert: " + modName);
        }

        public void RegisterCommand(string modName, string commandName, string description, bool adminOnly, Action<IMyPlayer, string[]> handler)
        {
            modName     = modName.ToLower();
            commandName = commandName.ToLower();
            if (!_modCommands.ContainsKey(modName))
                _modCommands[modName] = new List<CommandInfo>();
            _modCommands[modName].Add(new CommandInfo(commandName, description, adminOnly, handler));
            LoggerModule.Info(MOD, MODULE, "Command registriert: " + modName + " " + commandName + " (Admin: " + adminOnly + ")");
        }

        /// <summary>
        /// Prueft ob ein Mod ausschliesslich Admin-Commands hat.
        /// Sobald ein adminOnly=false Command existiert → false.
        /// </summary>
        private bool IsAdminOnlyMod(string modName)
        {
            if (!_modCommands.ContainsKey(modName)) return true;
            foreach (var cmd in _modCommands[modName])
                if (!cmd.AdminOnly) return false;
            return true;
        }

        /// <summary>
        /// Empfängt Registrierungsnachrichten von anderen Mods.
        /// Format: "REGISTER|modname|description|channel|cmd1:adminOnly:desc|cmd2:adminOnly:desc|..."
        /// </summary>
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

                string modName    = parts[1].ToLower();
                string modDesc    = parts[2];
                long   channel    = 0;
                if (!long.TryParse(parts[3], out channel)) return;

                // Mod registrieren
                RegisterMod(modName, modDesc);
                _modChannels[channel] = modName;

                // Commands registrieren
                for (int i = 4; i < parts.Length; i++)
                {
                    string[] cmdParts = parts[i].Split(':');
                    if (cmdParts.Length < 3) continue;

                    string cmdName   = cmdParts[0];
                    bool   adminOnly = cmdParts[1] == "1";
                    string cmdDesc   = cmdParts[2];
                    long   ch        = channel; // Closure-safe

                    // Handler schickt Command über Messaging an den Mod
                    RegisterCommand(modName, cmdName, cmdDesc, adminOnly,
                        (player, args) => SendCommandToMod(player, ch, cmdName, args));
                }

                LoggerModule.Info(MOD, MODULE, "Mod registriert via Messaging: " + modName + " (Kanal: " + channel + ")");

                // Dem Mod sofort seinen Debug-Level mitteilen
                SendLogLevel(channel, modName);
            }
            catch (Exception ex)
            {
                LoggerModule.Error(MOD, MODULE, "Fehler in OnModRegistration", ex);
            }
        }

        /// <summary>
        /// Sendet den aktuellen Log-Level an einen Mod.
        /// Format: "LOGLEVEL|normal|debug|trace"
        /// </summary>
        private void SendLogLevel(long channel, string modName)
        {
            try
            {
                // Mod-Namen zu globalem Namen konvertieren z.B. "artefact" → "Phantombite_Artefact"
                string fullName = "Phantombite_" + char.ToUpper(modName[0]) + modName.Substring(1);
                LoggerModule.LogLevel level = LoggerModule.GetLevel(fullName);
                string levelStr = level == LoggerModule.LogLevel.Trace ? "trace"
                                : level == LoggerModule.LogLevel.Debug  ? "debug"
                                : "normal";
                MyAPIGateway.Utilities.SendModMessage(channel, "LOGLEVEL|" + levelStr);
                LoggerModule.Info(MOD, MODULE, "LOGLEVEL gesendet an " + modName + ": " + levelStr);
            }
            catch (Exception ex)
            {
                LoggerModule.Error(MOD, MODULE, "Fehler in SendLogLevel", ex);
            }
        }

        /// <summary>
        /// Empfängt Log-Nachrichten von anderen Mods über Kanal 1995999.
        /// Format: "LOG|Phantombite_Artefact|DEBUG|Artefact_Controller|Nachricht"
        /// </summary>
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

                switch (levelStr)
                {
                    case "WARN":  LoggerModule.Warn(modName, module, message);  break;
                    case "ERROR": LoggerModule.Error(modName, module, message); break;
                    case "INFO":  LoggerModule.Info(modName, module, message);  break;
                    case "DEBUG": LoggerModule.Debug(modName, module, message); break;
                    case "TRACE": LoggerModule.Trace(modName, module, message); break;
                }
            }
            catch (Exception ex)
            {
                LoggerModule.Error(MOD, MODULE, "Fehler in OnLogReceived", ex);
            }
        }

        /// <summary>
        /// Empfängt CMDRESULT von einem Mod.
        /// Format: "CMDRESULT|key|ok|message" — key = "modName|cmdName|args|steamId"
        /// </summary>
        private void OnCmdResult(string msg)
        {
            try
            {
                // Format: CMDRESULT|modName|cmdName|args|steamId|status|message
                string[] parts = msg.Split(new[] { '|' }, 7);
                if (parts.Length < 7) return;

                string key     = parts[1] + "|" + parts[2] + "|" + parts[3] + "|" + parts[4];
                string status  = parts[5];
                string message = parts[6];

                LoggerModule.Trace(MOD, MODULE, $"CMDRESULT empfangen: Key='{key}', Status='{status}', Nachricht='{message}'");

                PendingCommand pending;
                if (!_pendingCommands.TryGetValue(key, out pending))
                {
                    LoggerModule.Warn(MOD, MODULE, "CMDRESULT ohne pending Command: " + key);
                    return;
                }

                double durationMs = (DateTime.UtcNow - pending.SentAt).TotalMilliseconds;
                _pendingCommands.Remove(key);

                bool ok = status == "ok";
                LoggerModule.Debug(MOD, MODULE, $"CMDRESULT: '{key}' — {status} — '{message}' ({durationMs:F0}ms)");
                LoggerModule.Trace(MOD, MODULE, $"Pending-Eintrag entfernt: Key='{key}', Dauer={durationMs:F0}ms, Pending verbleibend: {_pendingCommands.Count}");
                ShowHud(message, ok);
            }
            catch (Exception ex)
            {
                LoggerModule.Error(MOD, MODULE, "Fehler in OnCmdResult", ex);
            }
        }

        private void ShowHud(string message, bool success)
        {
            try
            {
                var font = success ? MyFontEnum.Green : MyFontEnum.Red;
                MyAPIGateway.Utilities.ShowNotification("[PB] " + message, 3000, font);
            }
            catch { }
        }

        /// <summary>
        /// Schickt einen Command-Aufruf an einen Mod über dessen Kanal.
        /// Format: "CMD|cmdname|arg1|arg2|STEAM:steamId"
        /// </summary>
        private void SendCommandToMod(IMyPlayer player, long channel, string commandName, string[] args)
        {
            try
            {
                string argsJoined = args != null && args.Length > 0 ? string.Join("|", args) : "";
                string modName    = _modChannels.ContainsKey(channel) ? _modChannels[channel] : channel.ToString();
                string key        = modName + "|" + commandName + "|" + argsJoined + "|" + player.SteamUserId;

                LoggerModule.Trace(MOD, MODULE, $"Deduplizierungs-Check: Key='{key}' — {(_pendingCommands.ContainsKey(key) ? "DUPLIKAT" : "neu")}");

                // Duplikat-Check
                if (_pendingCommands.ContainsKey(key))
                {
                    ShowHud(modName + " " + commandName + ": bereits in Bearbeitung", false);
                    LoggerModule.Debug(MOD, MODULE, $"Command Duplikat ignoriert: '{player.DisplayName}' (SteamId: {player.SteamUserId}) — {key}");
                    return;
                }

                string msg = "CMD|" + commandName;
                if (!string.IsNullOrEmpty(argsJoined)) msg += "|" + argsJoined;
                msg += "|STEAM:" + player.SteamUserId;

                _pendingCommands[key] = new PendingCommand { SteamId = player.SteamUserId, SentAt = DateTime.UtcNow };

                MyAPIGateway.Utilities.SendModMessage(channel, msg);
                LoggerModule.Debug(MOD, MODULE, $"Command weitergeleitet — Mod: '{modName}', Kanal: {channel}, Nachricht: '{msg}'");
                LoggerModule.Trace(MOD, MODULE, $"Pending-Eintrag erstellt: Key='{key}', SentAt={DateTime.UtcNow:HH:mm:ss.fff}, Pending gesamt: {_pendingCommands.Count}");
            }
            catch (Exception ex)
            {
                LoggerModule.Error(MOD, MODULE, "Fehler in SendCommandToMod", ex);
            }
        }

        /// <summary>
        /// Wird von Session nach InitAll() aufgerufen.
        /// Schickt READY an alle aktiven Mod-Kanäle damit sie sich registrieren können.
        /// </summary>
        public void SendReadyToActiveMods(ModDetector modDetector)
        {
            var modChannels = new Dictionary<ulong, long>
            {
                { ModRegistry.Artefact,           1995001L },
                { ModRegistry.CableWinch,         1995002L },
                { ModRegistry.Creatures,          1995003L },
                { ModRegistry.Economy,            1995004L },
                { ModRegistry.Encounter,          1995005L },
                { ModRegistry.ServerAddon,        1995006L },
                { ModRegistry.Sulvax,             1995007L },
                { ModRegistry.SulvaxRespawnRover, 1995008L },
                { ModRegistry.AutoTransfer,       1995009L }
            };

            foreach (var kvp in modChannels)
            {
                if (!modDetector.IsActive(kvp.Key)) continue;
                try
                {
                    MyAPIGateway.Utilities.SendModMessage(kvp.Value, "READY");
                    LoggerModule.Info(MOD, MODULE, "READY gesendet an Kanal: " + kvp.Value);
                }
                catch (Exception ex)
                {
                    LoggerModule.Error(MOD, MODULE, "Fehler beim Senden von READY an " + kvp.Value, ex);
                }
            }
        }

        // ── Message Handler ───────────────────────────────────────────────────

        private void OnMessageEntered(string messageText, ref bool sendToOthers)
        {
            try
            {
                string trimmed = messageText.Trim();

                // Hinweis bei generischen Help-Nachrichten — NICHT abfangen
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

                // Beide Prefixe entfernen
                string commandText = trimmed.StartsWith("/pbc", StringComparison.OrdinalIgnoreCase)
                    ? trimmed.Substring(4).Trim()
                    : trimmed.Substring(PREFIX.Length).Trim();

                LoggerModule.Trace(MOD, MODULE, $"Eingabe von '{p.DisplayName}' (SteamId: {p.SteamUserId}): '{trimmed}'");

                if (string.IsNullOrWhiteSpace(commandText)) { CmdHelp(p, new string[0]); return; }

                string[] tokens = commandText.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                ParseAndExecute(p, tokens);
            }
            catch (Exception ex) { LoggerModule.Error(MOD, MODULE, "Fehler in OnMessageEntered", ex); }
        }

        private void ParseAndExecute(IMyPlayer player, string[] tokens)
        {
            try
            {
                string first = tokens[0].ToLower();
                LoggerModule.Trace(MOD, MODULE, $"ParseAndExecute: '{player.DisplayName}' (SteamId: {player.SteamUserId}) — Tokens: [{string.Join(", ", tokens)}]");

                if (first == "help")
                {
                    // !pbc help economy <seite> direkt weiterleiten
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

                if (first == "log")
                {
                    if (!IsAdmin(player)) { Send(player, "Blockiert: Admin-Rechte erforderlich."); return; }
                    CmdLog(player, SubArgs(tokens, 1));
                    return;
                }

                if (_modCommands.ContainsKey(first))
                {
                    // Admin-only Mod: normale Spieler werden blockiert
                    if (IsAdminOnlyMod(first) && !IsAdmin(player))
                    {
                        LoggerModule.Debug(MOD, MODULE, $"Admin-Blockierung: '{player.DisplayName}' (SteamId: {player.SteamUserId}) versuchte Admin-Mod '{first}'");
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

                    // Prüfen ob zweites Token eine ID oder "all" ist
                    // !pbc artefact 1 reset  oder  !pbc artefact all reset
                    int  parsedId = 0;
                    bool isId     = int.TryParse(second, out parsedId);
                    bool isAll    = second == "all";

                    if ((isId || isAll) && tokens.Length > 2)
                    {
                        string cmdName  = tokens[2].ToLower();
                        // ID/all als erstes Arg übergeben, Rest dahinter
                        int    extraLen = tokens.Length - 3;
                        string[] cmdArgs = new string[1 + extraLen];
                        cmdArgs[0] = second; // "1" oder "all"
                        Array.Copy(tokens, 3, cmdArgs, 1, extraLen);
                        ExecuteModCommand(player, first, cmdName, cmdArgs);
                        return;
                    }

                    // Normal: !pbc artefact reset
                    ExecuteModCommand(player, first, second, SubArgs(tokens, 2));
                    return;
                }

                LoggerModule.Debug(MOD, MODULE, $"Unbekannter Command: '{player.DisplayName}' tippte '{first}'");
                Send(player, "Unbekannter Command: " + first + " — tippe " + PREFIX + " help");
            }
            catch (Exception ex) { LoggerModule.Error(MOD, MODULE, "Fehler in ParseAndExecute", ex); }
        }

        // ── Core Command Handler ──────────────────────────────────────────────

        private void CmdHelp(IMyPlayer player, string[] args)
        {
            bool isAdmin = IsAdmin(player);

            // !pbc help economy <seite> direkt weiterleiten
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

            // Help Commands zuerst
            lines.Add("!pbc help <Seitenzahl>");
            lines.Add("  Für eine andere Seite");

            foreach (var kvp in _modDescriptions)
            {
                if (IsAdminOnlyMod(kvp.Key) && !isAdmin) continue;
                lines.Add("!pbc help " + kvp.Key + " <Seitenzahl>");
                lines.Add("  Für " + CapFirst(kvp.Key) + " Help");
            }

            // Dann Status
            lines.Add("!pbc status");
            lines.Add("  Aktive Mods und Debug-Status");

            // Admin Commands nur für Admins
            if (isAdmin)
            {
                lines.Add("!pbc debug <mod> normal|debug|trace");
                lines.Add("  Debug-Level setzen (1x=temporär, 2x=permanent)");
                lines.Add("!pbc debug all normal|debug|trace");
                lines.Add("  Debug-Level setzen (1x=temporär, 2x=permanent)");
                lines.Add("!pbc log copy");
                lines.Add("  Log in Zwischenablage");
                lines.Add("!pbc log show");
                lines.Add("  Letzte Zeilen im Chat");
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
            lines.Add("[Debug-Status]");

            string[] allMods = {
                "Phantombite_Core", "Phantombite_Artefact", "Phantombite_CableWinch",
                "Phantombite_Creatures", "Phantombite_Economy", "Phantombite_Encounter",
                "Phantombite_Server_Addon", "Phantombite_Sulvax", "Phantombite_SulvaxRespawnRover",
                "Phantombite_AutoTransfer"
            };

            // Mod-Name zu ID Mapping für ModDetector
            var modIds = new Dictionary<string, ulong>
            {
                { "Phantombite_Core",               ModRegistry.Core },
                { "Phantombite_Artefact",           ModRegistry.Artefact },
                { "Phantombite_CableWinch",         ModRegistry.CableWinch },
                { "Phantombite_Creatures",           ModRegistry.Creatures },
                { "Phantombite_Economy",            ModRegistry.Economy },
                { "Phantombite_Encounter",          ModRegistry.Encounter },
                { "Phantombite_Server_Addon",       ModRegistry.ServerAddon },
                { "Phantombite_Sulvax",             ModRegistry.Sulvax },
                { "Phantombite_SulvaxRespawnRover", ModRegistry.SulvaxRespawnRover },
                { "Phantombite_AutoTransfer",       ModRegistry.AutoTransfer }
            };

            foreach (var mod in allMods)
            {
                // Core immer anzeigen, andere nur wenn aktiv
                if (mod != "Phantombite_Core" && _modDetector != null)
                {
                    ulong modId;
                    if (modIds.TryGetValue(mod, out modId) && !_modDetector.IsActive(modId))
                        continue;
                }

                string shortName = mod.Replace("Phantombite_", "");
                LoggerModule.LogLevel current = LoggerModule.GetLevel(mod);
                bool isTemp = _tempLevels.ContainsKey(mod);

                string levelStr = current.ToString();
                if (isTemp) levelStr += " (Temporär)";

                lines.Add(PadRight(shortName, 20) + levelStr);
            }

            if (_modDescriptions.Count > 0)
            {
                lines.Add("[Registrierte Mods]");
                foreach (var kvp in _modDescriptions)
                    lines.Add(kvp.Key + " — " + kvp.Value);
            }

            Send(player, "=== Phantombite Status ===");
            foreach (var line in lines)
                Send(player, line);
        }

        private void CmdDebug(IMyPlayer player, string[] args)
        {
            if (args.Length < 2)
            {
                Send(player, "Verwendung: " + PREFIX + " debug <mod|all> <normal|debug|trace>");
                return;
            }

            string target   = args[0].ToLower();
            string levelStr = args[1].ToLower();

            LoggerModule.LogLevel level;
            if      (levelStr == "normal") level = LoggerModule.LogLevel.Normal;
            else if (levelStr == "debug")  level = LoggerModule.LogLevel.Debug;
            else if (levelStr == "trace")  level = LoggerModule.LogLevel.Trace;
            else { Send(player, "Unbekannter Level: " + levelStr + " — erlaubt: normal, debug, trace"); return; }

            if (target == "all")
            {
                var allModIds = new Dictionary<string, ulong>
                {
                    { "Phantombite_Core",               ModRegistry.Core },
                    { "Phantombite_Artefact",           ModRegistry.Artefact },
                    { "Phantombite_CableWinch",         ModRegistry.CableWinch },
                    { "Phantombite_Creatures",           ModRegistry.Creatures },
                    { "Phantombite_Economy",            ModRegistry.Economy },
                    { "Phantombite_Encounter",          ModRegistry.Encounter },
                    { "Phantombite_Server_Addon",       ModRegistry.ServerAddon },
                    { "Phantombite_Sulvax",             ModRegistry.Sulvax },
                    { "Phantombite_SulvaxRespawnRover", ModRegistry.SulvaxRespawnRover },
                    { "Phantombite_AutoTransfer",       ModRegistry.AutoTransfer }
                };
                foreach (var kvp in allModIds)
                {
                    if (_modDetector != null && !_modDetector.IsActive(kvp.Value)) continue;
                    SetDebugLevel(player, kvp.Key, level, true);
                }
                Send(player, "Alle aktiven Mods auf " + level + " gesetzt.");
                return;
            }

            string fullName = "Phantombite_" + CapFirst(target);

            // Prüfen ob Mod aktiv ist
            if (_modDetector != null)
            {
                var modIds = new Dictionary<string, ulong>
                {
                    { "Phantombite_Core",               ModRegistry.Core },
                    { "Phantombite_Artefact",           ModRegistry.Artefact },
                    { "Phantombite_CableWinch",         ModRegistry.CableWinch },
                    { "Phantombite_Creatures",           ModRegistry.Creatures },
                    { "Phantombite_Economy",            ModRegistry.Economy },
                    { "Phantombite_Encounter",          ModRegistry.Encounter },
                    { "Phantombite_Server_Addon",       ModRegistry.ServerAddon },
                    { "Phantombite_Sulvax",             ModRegistry.Sulvax },
                    { "Phantombite_SulvaxRespawnRover", ModRegistry.SulvaxRespawnRover },
                    { "Phantombite_AutoTransfer",       ModRegistry.AutoTransfer }
                };
                ulong modId;
                if (modIds.TryGetValue(fullName, out modId) && !_modDetector.IsActive(modId))
                {
                    Send(player, "Mod nicht aktiv: " + CapFirst(target));
                    return;
                }
            }

            SetDebugLevel(player, fullName, level, false);
        }

        private void SetDebugLevel(IMyPlayer player, string modName, LoggerModule.LogLevel level, bool silent)
        {
            bool alreadyTemp = _tempLevels.ContainsKey(modName) && _tempLevels[modName] == level;
            bool isPermanent = !_tempLevels.ContainsKey(modName) && LoggerModule.GetLevel(modName) == level;
            string shortName = modName.Replace("Phantombite_", "");

            if (alreadyTemp || isPermanent)
            {
                _tempLevels.Remove(modName);
                LoggerModule.SetLevel(modName, level);
                SaveDebugToConfig(modName, level);
                if (!silent) Send(player, shortName + ": " + level + " — permanent gespeichert.");
                LoggerModule.Info(MOD, MODULE, "Debug permanent: " + modName + " = " + level);
            }
            else
            {
                _tempLevels[modName] = LoggerModule.GetLevel(modName);
                LoggerModule.SetLevel(modName, level);
                if (!silent) Send(player, shortName + ": " + level + " — temporär. Nochmal eingeben = permanent.");
                LoggerModule.Info(MOD, MODULE, "Debug temporär: " + modName + " = " + level);
            }

            // Mod über neuen Level informieren falls registriert
            foreach (var kvp in _modChannels)
            {
                if (_modDescriptions.ContainsKey(kvp.Value))
                {
                    string fullName = "Phantombite_" + char.ToUpper(kvp.Value[0]) + kvp.Value.Substring(1);
                    if (fullName == modName)
                    {
                        SendLogLevel(kvp.Key, kvp.Value);
                        break;
                    }
                }
            }
        }

        private void SaveDebugToConfig(string modName, LoggerModule.LogLevel level)
        {
            try
            {
                string content = FileManagerModule.ReadFile("Phantombite_GlobalConfig.ini", typeof(FileManagerModule));
                if (content == null) return;
                string oldKey = modName + "=";
                string newLine = modName + "=" + level;
                var rawLines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.None);
                var sb = new StringBuilder();
                foreach (var line in rawLines)
                    sb.AppendLine(line.TrimStart().StartsWith(oldKey) ? newLine : line);
                FileManagerModule.WriteFile("Phantombite_GlobalConfig.ini", sb.ToString(), typeof(FileManagerModule));
            }
            catch (Exception ex) { LoggerModule.Error(MOD, MODULE, "Fehler beim Speichern der Config", ex); }
        }

        private void CmdLog(IMyPlayer player, string[] args)
        {
            if (args.Length == 0)
            {
                Send(player, "Verwendung: " + PREFIX + " log copy | " + PREFIX + " log show");
                return;
            }

            string index = FileManagerModule.ReadFile("Phantombite_LogIndex.txt", typeof(FileManagerModule));
            if (index == null) { Send(player, "Kein Log-Index gefunden."); return; }

            string[] indexLines = index.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (indexLines.Length == 0) { Send(player, "Log-Index ist leer."); return; }

            string latestLog = indexLines[indexLines.Length - 1].Trim();
            string content   = FileManagerModule.ReadFile(latestLog, typeof(FileManagerModule));
            if (content == null) { Send(player, "Log-Datei nicht gefunden: " + latestLog); return; }

            string sub = args[0].ToLower();

            if (sub == "copy")
            {
                VRage.Utils.MyClipboardHelper.SetClipboard(content);
                Send(player, "Log in Zwischenablage: " + latestLog);
                LoggerModule.Info(MOD, MODULE, "Log kopiert von: " + player.DisplayName);
                return;
            }

            if (sub == "show")
            {
                string[] logLines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                int start = Math.Max(0, logLines.Length - PAGE_SIZE);
                Send(player, "=== Log (letzte " + (logLines.Length - start) + " Zeilen) ===");
                for (int i = start; i < logLines.Length; i++)
                    Send(player, logLines[i]);
                return;
            }

            Send(player, "Unbekannt: " + sub + " — erlaubt: copy, show");
        }

        private void ExecuteModCommand(IMyPlayer player, string modName, string commandName, string[] args)
        {
            string argsStr = args != null && args.Length > 0 ? string.Join(" ", args) : "";
            string fullCmd = modName + " " + commandName + (argsStr.Length > 0 ? " " + argsStr : "");

            LoggerModule.Trace(MOD, MODULE, $"ExecuteModCommand: '{player.DisplayName}' (SteamId: {player.SteamUserId}) — {fullCmd}");

            CommandInfo cmd = null;
            foreach (var c in _modCommands[modName])
                if (c.Name == commandName) { cmd = c; break; }

            if (cmd == null)
            {
                LoggerModule.Debug(MOD, MODULE, $"Command nicht gefunden: {modName} {commandName} — von '{player.DisplayName}'");
                Send(player, "Command nicht gefunden: " + commandName + " — tippe " + PREFIX + " " + modName + " help");
                return;
            }

            if (cmd.AdminOnly && !IsAdmin(player))
            {
                Send(player, "Blockiert: Admin-Rechte erforderlich.");
                LoggerModule.Warn(MOD, MODULE, $"Admin-Blockierung: '{player.DisplayName}' (SteamId: {player.SteamUserId}) versuchte: {fullCmd}");
                return;
            }

            LoggerModule.Debug(MOD, MODULE, $"'{player.DisplayName}' (SteamId: {player.SteamUserId}) führt aus: {fullCmd}");
            LoggerModule.Trace(MOD, MODULE, $"Admin-Check: PromoteLevel={player.PromoteLevel}, AdminOnly={cmd.AdminOnly} → erlaubt");
            cmd.Handler(player, args);
        }

        // ── Seiten-System ─────────────────────────────────────────────────────

        private void ShowPage(IMyPlayer player, List<string> lines, string baseCommand, string[] args, string title = "")
        {
            int page = 1;
            if (args != null && args.Length > 0) int.TryParse(args[0], out page);
            if (page < 1) page = 1;

            int totalPages = (int)Math.Ceiling(lines.Count / (double)PAGE_SIZE);
            if (totalPages < 1) totalPages = 1;
            if (page > totalPages) page = totalPages;

            // Header mit Seitenzahl
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

        // ── Helpers ───────────────────────────────────────────────────────────

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
            catch (Exception ex) { LoggerModule.Error(MOD, MODULE, "Fehler beim Senden", ex); }
        }

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