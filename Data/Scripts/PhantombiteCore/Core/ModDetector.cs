using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Utils;

namespace PhantombiteCore.Core
{
    /// <summary>
    /// Detects which mods are active and logs them in 3 sections:
    ///   1. PhantomBite Mods
    ///   2. Externe Abhängigkeiten (MES etc.)
    ///   3. Andere Mods
    ///
    /// Workshop Mode: Core hat Steam ID  → Erkennung per PublishedFileId
    /// Dev Mode:      Core läuft lokal   → Erkennung per Mod-Name
    ///
    /// Stellt ausserdem Session-Typ bereit:
    ///   IsServer      → true im Singleplayer UND auf Dedicated Server
    ///   IsSingleplayer → true nur im Singleplayer (OFFLINE mode)
    ///
    /// Call Scan() once in Session.BeforeStart().
    /// </summary>
    public class ModDetector
    {
        private readonly HashSet<ulong>  _activeIds   = new HashSet<ulong>();
        private readonly HashSet<string> _activeNames = new HashSet<string>();
        private readonly List<MyObjectBuilder_Checkpoint.ModItem> _otherMods
            = new List<MyObjectBuilder_Checkpoint.ModItem>();

        // ── Session Typ ──────────────────────────────────────────────────────

        /// <summary>
        /// True im Singleplayer UND auf dem Dedicated Server.
        /// Clients auf einem Dedicated Server: false.
        /// Nutzen: Dateien schreiben, Log schreiben, Config laden.
        /// </summary>
        public bool IsServer { get; private set; }

        /// <summary>
        /// True nur im Singleplayer (OFFLINE Mode).
        /// Nutzen: Admin-Check überspringen, voller Debug-Zugriff.
        /// </summary>
        public bool IsSingleplayer { get; private set; }

        /// <summary>True wenn Core lokal läuft (kein Workshop).</summary>
        public bool IsDevMode { get; private set; }

        private static readonly ulong[] ALL_PB_IDS = {
            ModRegistry.Artefact,
            ModRegistry.AutoTransfer,
            ModRegistry.CableWinch,
            ModRegistry.Creatures,
            ModRegistry.Economy,
            ModRegistry.Encounter,
            ModRegistry.ServerAddon,
            ModRegistry.Sulvax,
            ModRegistry.SulvaxRespawnRover
        };

        public void Scan()
        {
            _activeIds.Clear();
            _activeNames.Clear();
            _otherMods.Clear();

            // ── Session Typ bestimmen ────────────────────────────────────────
            IsServer      = MyAPIGateway.Multiplayer.IsServer;
            IsSingleplayer = MyAPIGateway.Session.OnlineMode == MyOnlineModeEnum.OFFLINE;

            // ── Mods einlesen ────────────────────────────────────────────────
            var mods = MyAPIGateway.Session != null ? MyAPIGateway.Session.Mods : null;
            if (mods == null)
            {
                MyLog.Default.WriteLineAndConsole("[PhantombiteCore] ModDetector: Session.Mods not available!");
                return;
            }

            foreach (var mod in mods)
            {
                _activeIds.Add(mod.PublishedFileId);
                if (!string.IsNullOrEmpty(mod.Name))
                    _activeNames.Add(mod.Name);
            }

            IsDevMode = !_activeIds.Contains(ModRegistry.Core);

            // Andere Mods sammeln — PB Mods herausfiltern
            foreach (var mod in mods)
            {
                if (ModRegistry.IsPhantomBiteMod(mod.PublishedFileId)) continue;
                if (IsDevMode && IsPhantomBiteModByName(mod.Name)) continue;
                _otherMods.Add(mod);
            }

            // ── Log ──────────────────────────────────────────────────────────
            Log("── Scan ─────────────────────────────────────────");
            Log("Mode    : " + (IsDevMode ? "DEV" : "WORKSHOP"));
            Log("Session : " + (IsSingleplayer ? "Singleplayer" : (IsServer ? "Dedicated Server" : "Client")));
            Log("Mods    : " + mods.Count + " aktiv");
            Log("─────────────────────────────────────────────────");

            LogPhantomBiteMods();
            LogExternalDeps();
            LogOtherMods();
            LogWarnings();

            Log("─────────────────────────────────────────────────");
        }

        public bool IsActive(ulong modId)
        {
            if (IsDevMode)
                return _activeNames.Contains(ModRegistry.GetLocalName(modId));
            else
                return _activeIds.Contains(modId);
        }

        public bool IsExternalActive(ulong modId)
        {
            return _activeIds.Contains(modId);
        }

        // ── Private ──────────────────────────────────────────────────────────

        private bool IsPhantomBiteModByName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            foreach (var id in ALL_PB_IDS)
                if (ModRegistry.GetLocalName(id) == name) return true;
            return name == ModRegistry.LocalCore;
        }

        private void LogPhantomBiteMods()
        {
            Log("── PhantomBite Mods ──────────────────────────────");
            Log("  Core (" + ModRegistry.Core + ") : SELF");

            int active = 0;
            foreach (var id in ALL_PB_IDS)
            {
                var state = IsActive(id) ? "ACTIVE" : "not loaded";
                Log("  " + PadRight(ModRegistry.GetName(id), 20) + "(" + id + ") : " + state);
                if (IsActive(id)) active++;
            }
            Log("  Aktiv: " + active + "/" + ALL_PB_IDS.Length);
        }

        private void LogExternalDeps()
        {
            // MES nur anzeigen wenn mindestens ein Mod aktiv ist der es braucht, oder wenn MES bereits aktiv ist
            bool mesNeeded = false;
            foreach (var id in ModRegistry.RequiresMES)
                if (IsActive(id)) { mesNeeded = true; break; }

            if (!mesNeeded && !IsExternalActive(ModRegistry.MES))
                return; // Kein Mod der MES braucht → Block weglassen

            Log("── Externe Abhängigkeiten ────────────────────────");
            var mesState = IsExternalActive(ModRegistry.MES) ? "ACTIVE" : "FEHLT";
            Log("  " + PadRight("MES", 20) + "(" + ModRegistry.MES + ") : " + mesState);
        }

        private void LogOtherMods()
        {
            Log("── Andere Mods ───────────────────────────────────");
            if (_otherMods.Count == 0) { Log("  (keine)"); return; }
            foreach (var mod in _otherMods)
            {
                if (mod.PublishedFileId == ModRegistry.MES) continue;
                string name = !string.IsNullOrEmpty(mod.Name) ? mod.Name : "(kein Name)";
                Log("  " + PadRight(name, 30) + "(" + mod.PublishedFileId + ")");
            }
        }

        private void LogWarnings()
        {
            bool anyWarning = false;
            foreach (var id in ModRegistry.RequiresMES)
            {
                if (IsActive(id) && !IsExternalActive(ModRegistry.MES))
                {
                    if (!anyWarning) { Log("── WARNUNGEN ─────────────────────────────────────"); anyWarning = true; }
                    Log("  WARNUNG: " + ModRegistry.GetName(id) + " ist aktiv aber MES fehlt! (ID: " + ModRegistry.MES + ")");
                }
            }
        }

        private void Log(string message)
        {
            MyLog.Default.WriteLineAndConsole("[PhantombiteCore] ModDetector: " + message);
        }

        private static string PadRight(string s, int width)
        {
            if (s == null) s = "";
            return s.Length >= width ? s + " " : s + new string(' ', width - s.Length);
        }
    }
}