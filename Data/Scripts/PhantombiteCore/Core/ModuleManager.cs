using System;
using System.Collections.Generic;
using PhantombiteCore.Modules;

namespace PhantombiteCore.Core
{
    /// <summary>
    /// ModuleManager — Verwaltet alle PhantombiteCore Module mit Fehler-Isolation.
    ///
    /// Ein Modul wird nach MAX_CRASHES Abstürzen deaktiviert und nie wieder aufgerufen.
    /// </summary>
    public class ModuleManager
    {
        private const string MOD = "Phantombite_Core";
        private const string MDL = "ModuleManager";
        private const int MAX_CRASHES = 3;

        private readonly List<IModule>               _modules         = new List<IModule>();
        private readonly Dictionary<string, int>     _crashCounters   = new Dictionary<string, int>();
        private readonly Dictionary<string, bool>    _disabledModules = new Dictionary<string, bool>();

        public void RegisterModule(IModule module)
        {
            if (module == null)
            {
                PBLog.Warn(MOD, MDL, "RegisterModule: null übergeben — übersprungen");
                return;
            }
            _modules.Add(module);
            _crashCounters[module.ModuleName]   = 0;
            _disabledModules[module.ModuleName] = false;
            PBLog.Log(MOD, MDL, "Modul registriert: " + module.ModuleName, 1);
        }

        public void InitAll()
        {
            PBLog.Log(MOD, MDL, "Init — " + _modules.Count + " Module: Logger, FileManager, Command, Performance, PlayerTracker");

            foreach (var module in _modules)
            {
                if (_disabledModules[module.ModuleName]) continue;
                try
                {
                    var start   = DateTime.UtcNow;
                    module.Init();
                    double ms   = (DateTime.UtcNow - start).TotalMilliseconds;
                    PBLog.Log(MOD, MDL, module.ModuleName + " — OK (" + ms.ToString("F0") + "ms)");
                }
                catch (Exception ex)
                {
                    HandleModuleError(module, "Init", ex);
                }
            }
        }

        public void UpdateAll()
        {
            foreach (var module in _modules)
            {
                if (_disabledModules[module.ModuleName]) continue;
                try   { module.Update(); }
                catch (Exception ex) { HandleModuleError(module, "Update", ex); }
            }
        }

        public void SaveAll()
        {
            foreach (var module in _modules)
            {
                if (_disabledModules[module.ModuleName]) continue;
                try   { module.SaveData(); }
                catch (Exception ex) { HandleModuleError(module, "SaveData", ex); }
            }
        }

        public void CloseAll()
        {
            PBLog.Log(MOD, MDL, "Close — " + _modules.Count + " Module");
            foreach (var module in _modules)
            {
                try   { module.Close(); }
                catch (Exception ex)
                {
                    PBLog.Error(MOD, MDL, "Fehler beim Schließen von '" + module.ModuleName + "'", ex);
                }
            }
        }

        private void HandleModuleError(IModule module, string operation, Exception ex)
        {
            _crashCounters[module.ModuleName]++;
            int count = _crashCounters[module.ModuleName];

            PBLog.Error(MOD, MDL,
                module.ModuleName + " Fehler in " + operation +
                " (" + count + "/" + MAX_CRASHES + ")", ex);

            if (count >= MAX_CRASHES)
            {
                _disabledModules[module.ModuleName] = true;
                PBLog.Warn(MOD, MDL, module.ModuleName + " DEAKTIVIERT nach " + MAX_CRASHES + " Abstürzen!");
            }
        }
    }
}