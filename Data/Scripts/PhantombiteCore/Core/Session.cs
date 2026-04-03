using System;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Components;
using VRage.Utils;
using PhantombiteCore.Core;
using PhantombiteCore.Modules;

namespace PhantombiteCore
{
    /// <summary>
    /// Main session component for PhantomBite Core.
    ///
    /// Reihenfolge:
    /// - Core_Logger        (immer, zuerst)
    /// - Core_FileManager   (immer, nach Logger)
    /// - Core_Command       (immer, nach FileManager)
    /// - Core_StationRefill (nur wenn Phantombite_Economy aktiv)
    /// - Core_PlanetSpawner (nur wenn Phantombite_Sulvax aktiv)
    /// </summary>
    [MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation)]
    public class PhantombiteCoreSession : MySessionComponentBase
    {
        private ModuleManager       _moduleManager;
        private ModDetector         _modDetector;
        private LoggerModule        _logger;
        private FileManagerModule   _fileManager;
        private CommandModule       _commandModule;
        private StationRefillModule _stationRefill;
        private PlanetSpawnerModule _planetSpawner;

        private bool _isInitialized = false;
        private const string MOD_NAME = "PhantombiteCore";

        public override void LoadData()
        {
            try
            {
                MyLog.Default.WriteLineAndConsole("[" + MOD_NAME + "] Session LoadData started...");
                _moduleManager = new ModuleManager();
                _modDetector   = new ModDetector();
                _isInitialized = true;
                MyLog.Default.WriteLineAndConsole("[" + MOD_NAME + "] Session LoadData completed.");
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole("[" + MOD_NAME + "] CRITICAL ERROR in LoadData:\n" + ex);
            }
        }

        public override void BeforeStart()
        {
            if (!_isInitialized) return;

            try
            {
                MyLog.Default.WriteLineAndConsole("[" + MOD_NAME + "] BeforeStart - scanning active mods...");

                _modDetector.Scan();

                // Core_Logger
                _logger = new LoggerModule();
                _moduleManager.RegisterModule(_logger);

                // Core_FileManager
                _fileManager = new FileManagerModule();
                _fileManager.SetModDetector(_modDetector);
                _moduleManager.RegisterModule(_fileManager);

                // Core_Command
                _commandModule = new CommandModule();
                _commandModule.SetModDetector(_modDetector);
                _commandModule.SetFileManager(_fileManager);
                _moduleManager.RegisterModule(_commandModule);

                // Core_StationRefill: nur wenn Economy aktiv
                if (_modDetector.IsActive(ModRegistry.Economy))
                {
                    _stationRefill = new StationRefillModule();
                    _moduleManager.RegisterModule(_stationRefill);
                }
                else
                {
                    LoggerModule.Info("Core", "Session", "Economy nicht aktiv - Core_StationRefill übersprungen");
                }

                // M99 - Planet Spawner: nur wenn Sulvax aktiv
                if (_modDetector.IsActive(ModRegistry.Sulvax))
                {
                    _planetSpawner = new PlanetSpawnerModule();
                    _moduleManager.RegisterModule(_planetSpawner);
                }
                else
                {
                    LoggerModule.Info("Core", "Session", "Sulvax nicht aktiv - Core_PlanetSpawner übersprungen");
                }

                _moduleManager.InitAll();

                // Core ist bereit — aktive Mods anschreiben damit sie sich registrieren
                _commandModule.SendReadyToActiveMods(_modDetector);

                if (_stationRefill != null)
                    _stationRefill.BeforeStart();

                if (_planetSpawner != null)
                    _planetSpawner.CheckAndSpawn();

                LoggerModule.Info("Core", "Session", "BeforeStart abgeschlossen");
                MyLog.Default.WriteLineAndConsole(_moduleManager.GetStatus());
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole("[" + MOD_NAME + "] CRITICAL ERROR in BeforeStart:\n" + ex);
            }
        }

        public override void UpdateBeforeSimulation()
        {
            if (!_isInitialized) return;
            try { _moduleManager.UpdateAll(); }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole("[" + MOD_NAME + "] ERROR in UpdateBeforeSimulation:\n" + ex);
            }
        }

        public override void SaveData()
        {
            if (!_isInitialized) return;
            try { _moduleManager.SaveAll(); }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole("[" + MOD_NAME + "] ERROR in SaveData:\n" + ex);
            }
        }

        protected override void UnloadData()
        {
            try
            {
                LoggerModule.Info("Core", "Session", "UnloadData gestartet");
                _moduleManager.CloseAll();
                _isInitialized = false;
                MyLog.Default.WriteLineAndConsole("[" + MOD_NAME + "] UnloadData completed.");
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole("[" + MOD_NAME + "] ERROR in UnloadData:\n" + ex);
            }
        }
    }
}