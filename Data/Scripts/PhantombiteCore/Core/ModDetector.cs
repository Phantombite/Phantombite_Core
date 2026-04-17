using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Utils;

namespace PhantombiteCore.Core
{
    /// <summary>
    /// Detects which mods are active and logs them in 3 sections.
    ///
    /// Drei Modi:
    ///   Workshop — alle PB Mods per Workshop-ID geladen
    ///   Local    — alle PB Mods lokal geladen (ID=0 oder Name-Match)
    ///   Hybrid   — Mix aus Workshop und Local
    ///
    /// IsActive prueft IMMER beide: Workshop-ID UND lokalen Namen.
    /// </summary>
    public class ModDetector
    {
        public enum LoadMode { Workshop, Local, Hybrid }

        private readonly HashSet<ulong>  _activeIds   = new HashSet<ulong>();
        private readonly HashSet<string> _activeNames = new HashSet<string>();
        private readonly List<MyObjectBuilder_Checkpoint.ModItem> _otherMods
            = new List<MyObjectBuilder_Checkpoint.ModItem>();

        // ── Session Typ ──────────────────────────────────────────────────────

        public bool     IsServer      { get; private set; }
        public bool     IsSingleplayer { get; private set; }
        public LoadMode Mode          { get; private set; }

        /// <summary>True wenn Core lokal läuft (kein Workshop).</summary>
        public bool IsDevMode => Mode != LoadMode.Workshop;

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

            IsServer       = MyAPIGateway.Multiplayer.IsServer;
            IsSingleplayer = MyAPIGateway.Session.OnlineMode == MyOnlineModeEnum.OFFLINE;

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

            // ── Mode bestimmen ───────────────────────────────────────────────
            // Prüfe wie viele PB Mods per ID vs per Name geladen sind
            int byId   = 0;
            int byName = 0;

            foreach (var id in ALL_PB_IDS)
            {
                bool hasId   = _activeIds.Contains(id);
                var  local   = ModRegistry.GetLocalName(id);
                bool hasName = local != null && _activeNames.Contains(local);

                if (hasId)   byId++;
                if (hasName && !hasId) byName++;
            }

            // Core selbst zählt auch
            bool coreById   = _activeIds.Contains(ModRegistry.Core);
            bool coreByName = _activeNames.Contains(ModRegistry.LocalCore);

            if (coreById)   byId++;
            if (coreByName && !coreById) byName++;

            if (byId > 0 && byName == 0)
                Mode = LoadMode.Workshop;
            else if (byName > 0 && byId == 0)
                Mode = LoadMode.Local;
            else
                Mode = LoadMode.Hybrid;

            // ── Andere Mods sammeln ──────────────────────────────────────────
            foreach (var mod in mods)
            {
                if (IsActivePbMod(mod.PublishedFileId, mod.Name)) continue;
                _otherMods.Add(mod);
            }

            // ── Log ──────────────────────────────────────────────────────────
            Log("── Scan ─────────────────────────────────────────");
            Log("Mode    : " + Mode.ToString().ToUpper());
            Log("Session : " + (IsSingleplayer ? "Singleplayer" : (IsServer ? "Dedicated Server" : "Client")));
            Log("Mods    : " + mods.Count + " aktiv");
            Log("─────────────────────────────────────────────────");

            LogPhantomBiteMods();
            LogExternalDeps();
            LogOtherMods();
            LogWarnings();

            Log("─────────────────────────────────────────────────");
        }

        /// <summary>
        /// Prueft ob ein Mod aktiv ist — immer per ID UND per Name.
        /// </summary>
        public bool IsActive(ulong modId)
        {
            if (_activeIds.Contains(modId)) return true;
            var localName = ModRegistry.GetLocalName(modId);
            return localName != null && _activeNames.Contains(localName);
        }

        /// <summary>
        /// Gibt an wie ein Mod geladen wurde (Workshop / Local / nicht geladen).
        /// </summary>
        public string GetLoadSource(ulong modId)
        {
            if (_activeIds.Contains(modId)) return "Workshop";
            var localName = ModRegistry.GetLocalName(modId);
            if (localName != null && _activeNames.Contains(localName)) return "Local";
            return null;
        }

        public bool IsExternalActive(ulong modId)
        {
            return _activeIds.Contains(modId);
        }

        // ── Private ──────────────────────────────────────────────────────────

        private bool IsActivePbMod(ulong id, string name)
        {
            if (ModRegistry.IsPhantomBiteMod(id)) return true;
            if (id == ModRegistry.Core)            return true;
            if (!string.IsNullOrEmpty(name))
            {
                if (name == ModRegistry.LocalCore) return true;
                foreach (var pbId in ALL_PB_IDS)
                    if (ModRegistry.GetLocalName(pbId) == name) return true;
            }
            return false;
        }

        private void LogPhantomBiteMods()
        {
            Log("── PhantomBite Mods ──────────────────────────────");

            string coreSource = GetLoadSource(ModRegistry.Core) ?? "not loaded";
            Log("  " + PadRight("Core", 20) + "(" + ModRegistry.Core + ") : SELF [" + coreSource + "]");

            int active = 0;
            foreach (var id in ALL_PB_IDS)
            {
                string source = GetLoadSource(id);
                string state  = source != null ? "ACTIVE [" + source + "]" : "not loaded";
                Log("  " + PadRight(ModRegistry.GetName(id), 20) + "(" + id + ") : " + state);
                if (source != null) active++;
            }
            Log("  Aktiv: " + active + "/" + ALL_PB_IDS.Length);
        }

        private void LogExternalDeps()
        {
            bool mesNeeded = false;
            foreach (var id in ModRegistry.RequiresMES)
                if (IsActive(id)) { mesNeeded = true; break; }

            if (!mesNeeded && !IsExternalActive(ModRegistry.MES)) return;

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
                string src  = mod.PublishedFileId == 0 ? "Local" : "Workshop";
                Log("  " + PadRight(name, 30) + "(" + mod.PublishedFileId + ") [" + src + "]");
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