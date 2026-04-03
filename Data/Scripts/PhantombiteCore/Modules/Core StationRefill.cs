using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage;
using VRage.Game;
using VRage.ObjectBuilders;
using VRage.Utils;
using PhantombiteCore.Core;

namespace PhantombiteCore.Modules
{
    /// <summary>
    /// Core_StationRefill
    ///
    /// Hält alle Stationen konfigurierter Fraktionen vollständig versorgt.
    /// Wird nur geladen wenn Phantombite_Economy aktiv ist.
    ///
    /// - Reaktoren: Uran bis voll auffüllen
    /// - Geschütze: Munition bis voll auffüllen
    /// - Einmalig beim Serverstart + alle INTERVAL_HOURS Stunden
    /// </summary>
    public class StationRefillModule : IModule
    {
        public string ModuleName { get { return "Core_StationRefill"; } }

        // ── Konfiguration ─────────────────────────────────────────────────────
        // Fraktions-Tags deren statische Grids aufgefüllt werden.
        // Mehrere Fraktionen möglich: { "SPT", "STG" }
        private static readonly string[] FACTION_TAGS = { "SPT" };

        // Intervall zwischen automatischen Auffüllungen
        private const int INTERVAL_HOURS = 5;

        // Munitionstyp für alle Geschütze
        private const string AMMO_SUBTYPE = "RapidFireAutomaticRifleGun_Mag_50rd";
        // ─────────────────────────────────────────────────────────────────────

        private const string MOD    = "Phantombite_Core";
        private const string MODULE = "Core_StationRefill";

        // Gefundene Grids + gecachte Blöcke
        private List<VRage.Game.ModAPI.IMyCubeGrid> _stationGrids = new List<VRage.Game.ModAPI.IMyCubeGrid>();
        private List<IMyReactor>                     _reactors     = new List<IMyReactor>();
        private List<IMyUserControllableGun>         _turrets      = new List<IMyUserControllableGun>();

        // Timer
        private int  _tickCounter   = 0;
        private int  _intervalTicks = 0; // 60 Ticks = 1s, 216000 = 1h

        private List<VRage.Game.ModAPI.Ingame.MyInventoryItem> _reuseItems
            = new List<VRage.Game.ModAPI.Ingame.MyInventoryItem>();

        // ── IModule ───────────────────────────────────────────────────────────

        public void Init()
        {
            _intervalTicks = INTERVAL_HOURS * 216000;
            LoggerModule.Info(MOD, MODULE, "Initialized"
                + " — Fraktionen: " + string.Join(", ", FACTION_TAGS)
                + ", Intervall: " + INTERVAL_HOURS + "h"
                + ", Ammo: " + AMMO_SUBTYPE);
        }

        /// <summary>
        /// Wird von Session.BeforeStart() aufgerufen nachdem ModDetector Economy bestätigt hat.
        /// Fraktionen und Entities sind dann vollständig geladen.
        /// </summary>
        public void BeforeStart()
        {
            if (!MyAPIGateway.Multiplayer.IsServer)
            {
                LoggerModule.Debug(MOD, MODULE, "Client — kein Auffüllen");
                return;
            }

            FindAllStations();

            if (_stationGrids.Count > 0)
                Refill();
        }

        public void Update()
        {
            if (!MyAPIGateway.Multiplayer.IsServer) return;
            if (_stationGrids.Count == 0) return;

            _tickCounter++;
            if (_tickCounter < _intervalTicks) return;
            _tickCounter = 0;

            LoggerModule.Info(MOD, MODULE, "Intervall erreicht (" + INTERVAL_HOURS + "h) — starte Auffüllung");
            Refill();
        }

        public void SaveData() { }

        public void Close()
        {
            _stationGrids.Clear();
            _reactors.Clear();
            _turrets.Clear();
            LoggerModule.Debug(MOD, MODULE, "Closed");
        }

        // ── Stationen suchen ──────────────────────────────────────────────────

        private void FindAllStations()
        {
            try
            {
                _stationGrids.Clear();
                _reactors.Clear();
                _turrets.Clear();

                // Alle Mitglieder aller konfigurierten Fraktionen sammeln
                var factionMembers = new HashSet<long>();
                foreach (var tag in FACTION_TAGS)
                {
                    var faction = MyAPIGateway.Session.Factions.TryGetFactionByTag(tag);
                    if (faction == null)
                    {
                        LoggerModule.Warn(MOD, MODULE, "Fraktion '" + tag + "' nicht gefunden — wird übersprungen");
                        continue;
                    }
                    foreach (var member in faction.Members)
                        factionMembers.Add(member.Key);

                    LoggerModule.Info(MOD, MODULE, "Fraktion '" + tag + "' gefunden — " + faction.Members.Count + " Mitglieder");
                }

                if (factionMembers.Count == 0)
                {
                    LoggerModule.Warn(MOD, MODULE, "Keine Fraktionsmitglieder gefunden — keine Station wird aufgefüllt");
                    return;
                }

                // Alle Entities durchsuchen — alle passenden Grids sammeln
                var entities = new HashSet<VRage.ModAPI.IMyEntity>();
                MyAPIGateway.Entities.GetEntities(entities);

                foreach (var entity in entities)
                {
                    var grid = entity as VRage.Game.ModAPI.IMyCubeGrid;
                    if (grid == null || !grid.IsStatic) continue;

                    bool owned = false;
                    foreach (var ownerId in grid.BigOwners)
                    {
                        if (factionMembers.Contains(ownerId)) { owned = true; break; }
                    }
                    if (!owned) continue;

                    _stationGrids.Add(grid);
                    LoggerModule.Info(MOD, MODULE, "Station gefunden: '" + grid.DisplayName + "'");
                }

                if (_stationGrids.Count > 0)
                    CacheAllBlocks();

                string searchResult = "Suche abgeschlossen — "
                    + _stationGrids.Count + " Station(en) gefunden, "
                    + _reactors.Count + " Reaktoren, "
                    + _turrets.Count + " Geschütze";
                LoggerModule.Info(MOD, MODULE, searchResult);
                MyLog.Default.WriteLineAndConsole("[PhantombiteCore|Core_StationRefill] " + searchResult);
            }
            catch (Exception ex)
            {
                LoggerModule.Error(MOD, MODULE, "Fehler in FindAllStations", ex);
            }
        }

        private void CacheAllBlocks()
        {
            _reactors.Clear();
            _turrets.Clear();

            var blocks = new List<VRage.Game.ModAPI.IMySlimBlock>();
            foreach (var grid in _stationGrids)
            {
                blocks.Clear();
                grid.GetBlocks(blocks);
                foreach (var slim in blocks)
                {
                    var reactor = slim.FatBlock as IMyReactor;
                    if (reactor != null) { _reactors.Add(reactor); continue; }

                    var turret = slim.FatBlock as IMyUserControllableGun;
                    if (turret != null) { _turrets.Add(turret); }
                }
            }
        }

        // ── Auffüllen ─────────────────────────────────────────────────────────

        private void Refill()
        {
            try
            {
                // Grids prüfen ob noch vorhanden
                bool anyRemoved = false;
                for (int i = _stationGrids.Count - 1; i >= 0; i--)
                {
                    if (_stationGrids[i] == null || _stationGrids[i].MarkedForClose)
                    {
                        LoggerModule.Warn(MOD, MODULE, "Station verschwunden — wird entfernt");
                        _stationGrids.RemoveAt(i);
                        anyRemoved = true;
                    }
                }

                if (anyRemoved)
                {
                    if (_stationGrids.Count == 0)
                    {
                        LoggerModule.Warn(MOD, MODULE, "Alle Stationen verschwunden — suche neu");
                        FindAllStations();
                        return;
                    }
                    CacheAllBlocks();
                }

                int reactorsFilled = 0;
                int turretsFilled  = 0;

                var uraniumId = new MyDefinitionId(typeof(MyObjectBuilder_Ingot), "Uranium");
                foreach (var reactor in _reactors)
                {
                    if (reactor.MarkedForClose) continue;
                    var inv = reactor.GetInventory(0);
                    if (inv == null) continue;

                    int current = GetItemAmount(inv, uraniumId);
                    int max     = GetMaxAmount(inv, uraniumId);
                    int diff    = max - current;
                    LoggerModule.Trace(MOD, MODULE, "Reaktor '" + reactor.DisplayName + "': Uran " + current + "/" + max + ", Diff: " + diff);
                    if (diff <= 0) continue;

                    AddItems(inv, uraniumId, diff);
                    reactorsFilled++;
                }

                var ammoId = new MyDefinitionId(typeof(MyObjectBuilder_AmmoMagazine), AMMO_SUBTYPE);
                foreach (var turret in _turrets)
                {
                    if (turret.MarkedForClose) continue;
                    var inv = turret.GetInventory(0);
                    if (inv == null) continue;

                    int current = GetItemAmount(inv, ammoId);
                    int max     = GetMaxAmount(inv, ammoId);
                    int diff    = max - current;
                    LoggerModule.Trace(MOD, MODULE, "Geschütz '" + turret.DisplayName + "': " + AMMO_SUBTYPE + " " + current + "/" + max + ", Diff: " + diff);
                    if (diff <= 0) continue;

                    AddItems(inv, ammoId, diff);
                    turretsFilled++;
                }

                string refillResult = "Auffüllung abgeschlossen — "
                    + reactorsFilled + "/" + _reactors.Count + " Reaktoren, "
                    + turretsFilled  + "/" + _turrets.Count  + " Geschütze"
                    + " (" + _stationGrids.Count + " Station(en))";
                LoggerModule.Info(MOD, MODULE, refillResult);
                MyLog.Default.WriteLineAndConsole("[PhantombiteCore|Core_StationRefill] " + refillResult);
            }
            catch (Exception ex)
            {
                LoggerModule.Error(MOD, MODULE, "Fehler in Refill", ex);
            }
        }

        // ── Inventory Helpers ─────────────────────────────────────────────────

        private void AddItems(VRage.Game.ModAPI.IMyInventory inv, MyDefinitionId itemDefId, int amount)
        {
            try
            {
                var builder = MyObjectBuilderSerializer.CreateNewObject(itemDefId);
                var physObj = builder as MyObjectBuilder_PhysicalObject;
                if (physObj != null)
                    inv.AddItems((MyFixedPoint)amount, physObj);
            }
            catch (Exception ex)
            {
                LoggerModule.Error(MOD, MODULE, "Fehler in AddItems", ex);
            }
        }

        private int GetItemAmount(VRage.Game.ModAPI.IMyInventory inv, MyDefinitionId itemDefId)
        {
            _reuseItems.Clear();
            inv.GetItems(_reuseItems);
            int total = 0;
            foreach (var item in _reuseItems)
            {
                if (item.Type.TypeId    == itemDefId.TypeId.ToString() &&
                    item.Type.SubtypeId == itemDefId.SubtypeName)
                    total += (int)item.Amount;
            }
            return total;
        }

        private int GetMaxAmount(VRage.Game.ModAPI.IMyInventory inv, MyDefinitionId itemDefId)
        {
            try
            {
                float maxVolL = (float)inv.MaxVolume * 1000f;
                var   defId   = MyDefinitionId.Parse(itemDefId.TypeId + "/" + itemDefId.SubtypeName);
                var   itemDef = Sandbox.Definitions.MyDefinitionManager.Static.GetPhysicalItemDefinition(defId);
                if (itemDef == null || itemDef.Volume <= 0f) return 10000;
                return (int)(maxVolL / (itemDef.Volume * 1000f));
            }
            catch
            {
                return 10000;
            }
        }
    }
}