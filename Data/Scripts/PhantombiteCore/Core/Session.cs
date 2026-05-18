using System;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Components;
using PhantombiteCore.Core;
using PhantombiteCore.Modules;

namespace PhantombiteCore
{
    /// <summary>
    /// PhantombiteCore Session — Einstiegspunkt.
    ///
    /// Modul-Reihenfolge (fest):
    ///   1. Core_Logger       — PBLog initialisieren
    ///   2. Core_FileManager  — GlobalConfig laden, Debug-Level setzen
    ///   3. Core_Command      — Commands + Mod-Registrierung
    ///
    /// Ausgebaut:
    ///   Core_PlanetSpawner → Phantombite_PlanetSpawner (eigener Mod)
    ///   Core_StationRefill → Phantombite_StationRefill (eigener Mod)
    /// </summary>
    [MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation)]
    public class PhantombiteCoreSession : MySessionComponentBase
    {
        private const string VERSION  = "2.0.0";
        private const string MOD      = "Phantombite_Core";
        private const string MOD_NAME = "PhantombiteCore";

        private ModuleManager       _moduleManager;
        private ModDetector         _modDetector;
        private LoggerModule        _logger;
        private FileManagerModule   _fileManager;
        private CommandModule       _commandModule;
        private PerformanceModule   _performanceModule;
        private PlayerTrackerModule _playerTracker;

        private bool _isInitialized = false;

        public override void LoadData()
        {
            try
            {
                // Noch kein PBLog (Logger läuft erst in BeforeStart) — MyLog direkt nutzen
                VRage.Utils.MyLog.Default.WriteLineAndConsole(
                    "[PB.Core] Phantombite Core v" + VERSION + " — LoadData");
                _moduleManager = new ModuleManager();
                _modDetector   = new ModDetector();
                _isInitialized = true;
            }
            catch (Exception ex)
            {
                VRage.Utils.MyLog.Default.WriteLineAndConsole(
                    "[PB.Core] KRITISCHER FEHLER in LoadData:\n" + ex);
            }
        }

        public override void BeforeStart()
        {
            if (!_isInitialized) return;

            try
            {
                _modDetector.Scan();

                // ── Module registrieren und initialisieren ───────────────────
                _logger = new LoggerModule();
                _moduleManager.RegisterModule(_logger);

                _fileManager = new FileManagerModule();
                _fileManager.SetModDetector(_modDetector);
                _moduleManager.RegisterModule(_fileManager);

                _commandModule = new CommandModule();
                _commandModule.SetModDetector(_modDetector);
                _commandModule.SetFileManager(_fileManager);
                _moduleManager.RegisterModule(_commandModule);

                // Core_Performance (nach FileManager — liest GlobalConfig)
                _performanceModule = new PerformanceModule();
                _performanceModule.SetCommandModule(_commandModule);
                _commandModule.SetPerformanceModule(_performanceModule);
                _moduleManager.RegisterModule(_performanceModule);

                // Core_PlayerTracker (nach Performance — benötigt PerformanceModule)
                _playerTracker = new PlayerTrackerModule();
                _playerTracker.SetPerformanceModule(_performanceModule);
                _commandModule.SetPlayerTracker(_playerTracker);
                _moduleManager.RegisterModule(_playerTracker);

                _moduleManager.InitAll();

                // ── Core ist bereit — aktive Mods anschreiben ────────────────
                _commandModule.SendReadyToActiveMods(_modDetector);

                PBLog.Log(MOD, "Session", "BeforeStart abgeschlossen — Core v" + VERSION);
            }
            catch (Exception ex)
            {
                PBLog.Error(MOD, "Session", "KRITISCHER FEHLER in BeforeStart", ex);
            }
        }

        public override void UpdateBeforeSimulation()
        {
            if (!_isInitialized) return;
            try { _moduleManager.UpdateAll(); }
            catch (Exception ex)
            {
                PBLog.Error(MOD, "Session", "Fehler in UpdateBeforeSimulation", ex);
            }
        }

        public override void SaveData()
        {
            if (!_isInitialized) return;
            try { _moduleManager.SaveAll(); }
            catch (Exception ex)
            {
                PBLog.Error(MOD, "Session", "Fehler in SaveData", ex);
            }
        }

        protected override void UnloadData()
        {
            try
            {
                PBLog.Log(MOD, "Session", "UnloadData");
                _moduleManager?.CloseAll();
                _isInitialized = false;
            }
            catch (Exception ex)
            {
                VRage.Utils.MyLog.Default.WriteLineAndConsole(
                    "[PB.Core] Fehler in UnloadData:\n" + ex);
            }
        }
    }
}