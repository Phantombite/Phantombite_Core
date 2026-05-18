using System;
using System.Collections.Generic;
using System.Text;
using Sandbox.Game;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using PhantombiteCore.Modules;

namespace PhantombiteCore.Core
{
    /// <summary>
    /// Core_PlayerTracker — Spieler-Erkennung und Logging
    ///
    /// Aufgaben:
    ///   - Spieler-Join/Leave Events erkennen und loggen
    ///   - Eigene Log-Datei: Phantombite_PlayerLog.txt
    ///   - Performance-System bei Join benachrichtigen (SimSpeed-Schutz)
    ///
    /// Erweiterbar:
    ///   - Whitelist / Blacklist
    ///   - Automatische Aktionen bei bestimmten Spielern
    ///
    /// Protokoll:
    ///   PlayerConnected(identityId)    → MyVisualScriptLogicProvider Event
    ///   PlayerDisconnected(identityId) → MyVisualScriptLogicProvider Event
    /// </summary>
    public class PlayerTrackerModule : IModule
    {
        public string ModuleName { get { return "Core_PlayerTracker"; } }

        private const string MOD             = "Phantombite_Core";
        private const string MDL             = "Core_PlayerTracker";
        private const string PLAYER_LOG_FILE = "Phantombite_PlayerLog.txt";

        // ── Referenzen ────────────────────────────────────────────────────────
        private PerformanceModule _performanceModule;
        public void SetPerformanceModule(PerformanceModule perf) { _performanceModule = perf; }

        // ── State ─────────────────────────────────────────────────────────────
        private bool _initialized = false;
        private readonly Dictionary<long, PlayerRecord> _activePlayers = new Dictionary<long, PlayerRecord>();

        private class PlayerRecord
        {
            public string   Name;
            public ulong    SteamId;
            public long     IdentityId;
            public DateTime JoinTime;
        }

        // ── IModule ───────────────────────────────────────────────────────────

        public void Init()
        {
            // PlayerLog nur auf echtem Dedicated Server — nicht in Singleplayer
            bool isDedicated = MyAPIGateway.Multiplayer.IsServer &&
                               MyAPIGateway.Session.OnlineMode != VRage.Game.MyOnlineModeEnum.OFFLINE;
            if (!isDedicated) return;

            EnsurePlayerLogExists();

            MyVisualScriptLogicProvider.PlayerConnected    += OnPlayerConnected;
            MyVisualScriptLogicProvider.PlayerDisconnected += OnPlayerDisconnected;

            _initialized = true;
            PBLog.Log(MOD, MDL, "Initialisiert — Spieler Events aktiv");
        }

        public void Update()   { }
        public void SaveData() { }

        public void Close()
        {
            if (!_initialized) return;
            if (MyVisualScriptLogicProvider.PlayerConnected != null)
                MyVisualScriptLogicProvider.PlayerConnected    -= OnPlayerConnected;
            if (MyVisualScriptLogicProvider.PlayerDisconnected != null)
                MyVisualScriptLogicProvider.PlayerDisconnected -= OnPlayerDisconnected;
            _initialized = false;
        }

        // ── Player Events ─────────────────────────────────────────────────────

        private void OnPlayerConnected(long identityId)
        {
            try
            {
                string name    = ResolvePlayerName(identityId);
                ulong  steamId = MyAPIGateway.Players.TryGetSteamId(identityId);

                _activePlayers[identityId] = new PlayerRecord
                {
                    Name = name, SteamId = steamId,
                    IdentityId = identityId, JoinTime = DateTime.UtcNow
                };

                PBLog.Log(MOD, MDL, name + " (" + steamId + ") Joined");
                AppendToPlayerLog(DateTime.UtcNow.ToString("HH:mm:ss") +
                    "  [PB.Core.PlayerTracker] " + name + " (" + steamId + ") Joined");

                // Performance-System: SimSpeed-Schutz für 30 Sek aktivieren
                _performanceModule?.OnPlayerJoined(name);
            }
            catch (Exception ex)
            {
                PBLog.Error(MOD, MDL, "Fehler in OnPlayerConnected", ex);
            }
        }

        private void OnPlayerDisconnected(long identityId)
        {
            try
            {
                PlayerRecord record;
                _activePlayers.TryGetValue(identityId, out record);

                string name    = record != null ? record.Name    : "ID:" + identityId;
                ulong  steamId = record != null ? record.SteamId : 0;

                if (record != null) _activePlayers.Remove(identityId);

                PBLog.Log(MOD, MDL, name + " (" + steamId + ") Left");
                AppendToPlayerLog(DateTime.UtcNow.ToString("HH:mm:ss") +
                    "  [PB.Core.PlayerTracker] " + name + " (" + steamId + ") Left");
            }
            catch (Exception ex)
            {
                PBLog.Error(MOD, MDL, "Fehler in OnPlayerDisconnected", ex);
            }
        }

        // ── Spielerliste für !pbc players ─────────────────────────────────────

        public string GetPlayerListText()
        {
            if (_activePlayers.Count == 0)
                return "Keine Spieler online";

            var sb = new StringBuilder();
            sb.AppendLine("Aktive Spieler (" + _activePlayers.Count + "):");
            foreach (var kvp in _activePlayers)
            {
                var r = kvp.Value;
                string duration = FormatDuration(DateTime.UtcNow - r.JoinTime);
                sb.AppendLine("  " + PadRight(r.Name, 20) +
                    "(" + r.SteamId + ")  online seit " + duration);
            }
            return sb.ToString();
        }

        public int GetPlayerCount() { return _activePlayers.Count; }

        // ── Player Log Datei ──────────────────────────────────────────────────

        private void EnsurePlayerLogExists()
        {
            if (!FileManagerModule.FileExists(PLAYER_LOG_FILE, typeof(FileManagerModule)))
                FileManagerModule.WriteFile(PLAYER_LOG_FILE, "", typeof(FileManagerModule));

            PBLog.Log(MOD, MDL, "Player-Log bereit: " + PLAYER_LOG_FILE);
        }

        private void AppendToPlayerLog(string line)
        {
            try
            {
                string existing = FileManagerModule.ReadFile(PLAYER_LOG_FILE,
                    typeof(FileManagerModule)) ?? "";
                FileManagerModule.WriteFile(PLAYER_LOG_FILE,
                    existing + line + "\n", typeof(FileManagerModule));
            }
            catch (Exception ex)
            {
                PBLog.Error(MOD, MDL, "Fehler beim Schreiben des Player-Logs", ex);
            }
        }

        // ── Hilfsmethoden ─────────────────────────────────────────────────────

        private string ResolvePlayerName(long identityId)
        {
            var list = new List<IMyPlayer>();
            MyAPIGateway.Players.GetPlayers(list, p => p.IdentityId == identityId);
            return list.Count > 0 ? list[0].DisplayName : "ID:" + identityId;
        }

        private string FormatDuration(TimeSpan span)
        {
            if (span.TotalHours >= 1)
                return (int)span.TotalHours + "h " + span.Minutes + "min";
            if (span.TotalMinutes >= 1)
                return (int)span.TotalMinutes + "min";
            return (int)span.TotalSeconds + "s";
        }

        private static string PadRight(string s, int width)
        {
            if (s == null) s = "";
            return s.Length >= width ? s : s + new string(' ', width - s.Length);
        }
    }
}