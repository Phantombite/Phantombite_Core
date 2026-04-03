using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Utils;

namespace PhantombiteCore.Core
{
    /// <summary>
    /// Manages all PhantombiteCore modules with error isolation
    /// </summary>
    public class ModuleManager
    {
        private readonly List<IModule> _modules = new List<IModule>();
        private readonly Dictionary<string, int> _crashCounters = new Dictionary<string, int>();
        private readonly Dictionary<string, bool> _disabledModules = new Dictionary<string, bool>();
        private const int MAX_CRASHES = 3;

        public void RegisterModule(IModule module)
        {
            if (module == null)
            {
                MyLog.Default.WriteLineAndConsole("[PhantombiteCore] ModuleManager: Cannot register null module");
                return;
            }

            _modules.Add(module);
            _crashCounters[module.ModuleName] = 0;
            _disabledModules[module.ModuleName] = false;
            MyLog.Default.WriteLineAndConsole("[PhantombiteCore] ModuleManager: Registered '" + module.ModuleName + "'");
        }

        public void InitAll()
        {
            MyLog.Default.WriteLineAndConsole("[PhantombiteCore] ModuleManager: Initializing " + _modules.Count + " modules...");

            foreach (var module in _modules)
            {
                if (_disabledModules[module.ModuleName]) continue;

                try
                {
                    var startTime = DateTime.UtcNow;
                    module.Init();
                    var elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;
                    MyLog.Default.WriteLineAndConsole("[PhantombiteCore] ModuleManager: '" + module.ModuleName + "' initialized in " + elapsed.ToString("F2") + "ms");
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

                try
                {
                    module.Update();
                }
                catch (Exception ex)
                {
                    HandleModuleError(module, "Update", ex);
                }
            }
        }

        public void SaveAll()
        {
            foreach (var module in _modules)
            {
                if (_disabledModules[module.ModuleName]) continue;

                try
                {
                    module.SaveData();
                }
                catch (Exception ex)
                {
                    HandleModuleError(module, "SaveData", ex);
                }
            }
        }

        public void CloseAll()
        {
            MyLog.Default.WriteLineAndConsole("[PhantombiteCore] ModuleManager: Closing " + _modules.Count + " modules...");

            foreach (var module in _modules)
            {
                try
                {
                    module.Close();
                    MyLog.Default.WriteLineAndConsole("[PhantombiteCore] ModuleManager: '" + module.ModuleName + "' closed");
                }
                catch (Exception ex)
                {
                    MyLog.Default.WriteLineAndConsole("[PhantombiteCore] ModuleManager: Error closing '" + module.ModuleName + "': " + ex.Message);
                }
            }
        }

        private void HandleModuleError(IModule module, string operation, Exception ex)
        {
            _crashCounters[module.ModuleName]++;

            MyLog.Default.WriteLineAndConsole(
                "[PhantombiteCore] ModuleManager ERROR: '" + module.ModuleName + "' crashed in " + operation +
                " (Count: " + _crashCounters[module.ModuleName] + "/" + MAX_CRASHES + ")\n" + ex
            );

            if (_crashCounters[module.ModuleName] >= MAX_CRASHES)
            {
                _disabledModules[module.ModuleName] = true;
                MyLog.Default.WriteLineAndConsole(
                    "[PhantombiteCore] ModuleManager: '" + module.ModuleName + "' DISABLED after " + MAX_CRASHES + " crashes!"
                );
            }
        }

        public string GetStatus()
        {
            var status = "[PhantombiteCore] ModuleManager Status:\n";
            foreach (var module in _modules)
            {
                var state = _disabledModules[module.ModuleName] ? "DISABLED" : "ACTIVE";
                var crashes = _crashCounters[module.ModuleName];
                status += "  - " + module.ModuleName + ": " + state + " (Crashes: " + crashes + ")\n";
            }
            return status;
        }
    }
}
