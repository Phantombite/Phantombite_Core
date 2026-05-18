using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Game;
using PhantombiteCore.Modules;

namespace PhantombiteCore.Core
{
    /// <summary>
    /// ModDetector — Erkennt welche Mods aktiv sind und loggt sie beim Start.
    ///
    /// Drei Modi:
    ///   Workshop — alle PB Mods per Workshop-ID geladen
    ///   Local    — alle PB Mods lokal geladen (ID=0 oder Name-Match)
    ///   Hybrid   — Mix aus Workshop und Local
    ///
    /// IsActive prüft IMMER beide: Workshop-ID UND lokalen Namen.
    /// </summary>
    public class ModDetector
    {
        public enum LoadMode { Workshop, Local, Hybrid }

        private const string MOD = "Phantombite_Core";
        private const string MDL = "ModDetector";

        private readonly HashSet<ulong>  _activeIds   = new HashSet<ulong>();
        private readonly HashSet<string> _activeNames = new HashSet<string>();
        private readonly List<MyObjectBuilder_Checkpoint.ModItem> _otherMods
            = new List<MyObjectBuilder_Checkpoint.ModItem>();

        public bool     IsServer       { get; private set; }
        public bool     IsSingleplayer { get; private set; }
        public LoadMode Mode           { get; private set; }

        public bool IsDevMode => Mode != LoadMode.Workshop;

        public void Scan()
        {
            _activeIds.Clear();
            _activeNames.Clear();
            _otherMods.Clear();

            IsServer       = MyAPIGateway.Multiplayer.IsServer;
            IsSingleplayer = MyAPIGateway.Session.OnlineMode == MyOnlineModeEnum.OFFLINE;

            var mods = MyAPIGateway.Session?.Mods;
            if (mods == null)
            {
                PBLog.Warn(MOD, MDL, "Session.Mods nicht verfügbar!");
                return;
            }

            foreach (var mod in mods)
            {
                _activeIds.Add(mod.PublishedFileId);
                if (!string.IsNullOrEmpty(mod.Name))
                    _activeNames.Add(mod.Name);
            }

            // ── Mode bestimmen ───────────────────────────────────────────────
            int byId = 0, byName = 0;
            foreach (var id in ModRegistry.AllPbIds)
            {
                bool hasId   = _activeIds.Contains(id);
                var  local   = ModRegistry.GetLocalName(id);
                bool hasName = local != null && _activeNames.Contains(local);
                if (hasId)             byId++;
                if (hasName && !hasId) byName++;
            }

            bool coreById   = _activeIds.Contains(ModRegistry.Core);
            bool coreByName = _activeNames.Contains(ModRegistry.LocalCore);
            if (coreById)               byId++;
            if (coreByName && !coreById) byName++;

            if      (byId > 0 && byName == 0) Mode = LoadMode.Workshop;
            else if (byName > 0 && byId == 0) Mode = LoadMode.Local;
            else                              Mode = LoadMode.Hybrid;

            // ── Andere Mods sammeln ──────────────────────────────────────────
            foreach (var mod in mods)
            {
                if (IsActivePbMod(mod.PublishedFileId, mod.Name)) continue;
                _otherMods.Add(mod);
            }

            // ── Log ausgeben ─────────────────────────────────────────────────
            PBLog.Log(MOD, MDL, "==========================================");
            PBLog.Log(MOD, MDL, "Mode    : " + Mode.ToString().ToUpper());
            PBLog.Log(MOD, MDL, "Session : " + (IsSingleplayer ? "Singleplayer" : (IsServer ? "Dedicated Server" : "Client")));
            PBLog.Log(MOD, MDL, "==========================================");
            LogActivePbMods();
            LogExternalDeps();
            LogOtherMods();
            LogWarnings();
            PBLog.Log(MOD, MDL, "==========================================");
        }

        // ── Public API ───────────────────────────────────────────────────────

        /// <summary>Prüft ob ein Mod aktiv ist — per ID UND per lokalem Namen.</summary>
        public bool IsActive(ulong modId)
        {
            if (_activeIds.Contains(modId)) return true;
            var localName = ModRegistry.GetLocalName(modId);
            return localName != null && _activeNames.Contains(localName);
        }

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
                foreach (var pbId in ModRegistry.AllPbIds)
                    if (ModRegistry.GetLocalName(pbId) == name) return true;
            }
            return false;
        }

        private void LogActivePbMods()
        {
            var activeList = new List<string>();

            // Core selbst
            string coreSource = GetLoadSource(ModRegistry.Core);
            if (coreSource != null)
                activeList.Add(PadRight("Core", 20) + "[" + coreSource + "]");

            // Alle anderen PB Mods — nur aktive
            foreach (var id in ModRegistry.AllPbIds)
            {
                string source = GetLoadSource(id);
                if (source == null) continue;
                activeList.Add(PadRight(ModRegistry.GetName(id), 20) + "[" + source + "]");
            }

            PBLog.Log(MOD, MDL, "Aktive PB Mods (" + activeList.Count + "):");
            foreach (var entry in activeList)
                PBLog.Log(MOD, MDL, "  " + entry);
        }

        private void LogExternalDeps()
        {
            // MES nur anzeigen wenn ein Mod es braucht ODER es aktiv ist
            bool mesNeeded = false;
            foreach (var id in ModRegistry.RequiresMES)
                if (IsActive(id)) { mesNeeded = true; break; }

            bool mesActive = IsExternalActive(ModRegistry.MES);
            if (!mesNeeded && !mesActive) return;

            PBLog.Log(MOD, MDL, "Externe Abhängigkeiten:");
            string mesState = mesActive ? "AKTIV" : "FEHLT";
            PBLog.Log(MOD, MDL, "  MES [" + ModRegistry.MES + "] " + mesState);
        }

        private void LogOtherMods()
        {
            if (_otherMods.Count == 0) return;

            PBLog.Log(MOD, MDL, "Andere Mods (" + _otherMods.Count + "):");
            foreach (var mod in _otherMods)
            {
                if (mod.PublishedFileId == ModRegistry.MES) continue;
                string name = !string.IsNullOrEmpty(mod.Name) ? mod.Name : "(kein Name)";
                PBLog.Log(MOD, MDL, "  " + name + " [" + mod.PublishedFileId + "]");
            }
        }

        private void LogWarnings()
        {
            foreach (var id in ModRegistry.RequiresMES)
            {
                if (IsActive(id) && !IsExternalActive(ModRegistry.MES))
                    PBLog.Warn(MOD, MDL, ModRegistry.GetName(id) + " ist aktiv aber MES fehlt! [" + ModRegistry.MES + "]");
            }
        }

        private static string PadRight(string s, int width)
        {
            if (s == null) s = "";
            return s.Length >= width ? s + " " : s + new string(' ', width - s.Length);
        }
    }
}