using System;
using System.Collections.Generic;
using System.Text;
using Sandbox.ModAPI;
using VRage.Utils;
using PhantombiteCore.Core;

namespace PhantombiteCore.Modules
{
    /// <summary>
    /// Core_FileManager
    ///
    /// Drei Aufgaben:
    ///
    /// 1. GLOBAL CONFIG
    ///    - Phantombite_GlobalConfig.ini — Debug-Level pro Mod
    ///    - Self-Healing: Datei wird erstellt wenn nicht vorhanden
    ///    - Debug-Level wird nach dem Laden an Core_Logger weitergegeben
    ///    - Nur auf Server (Singleplayer + Dedicated Server)
    ///
    /// 2. CORE LOG MANAGEMENT
    ///    - Pro Spielstart eine neue Log-Datei: Phantombite_Core_DATUM_UHRZEIT.log
    ///    - Maximal 20 Log-Dateien, älteste wird gelöscht
    ///    - Schreibt Core_Logger-Buffer alle 5 Sekunden in die Log-Datei
    ///    - Nur auf Server
    ///
    /// 3. HELFER-API FÜR ANDERE MODS
    ///    - Andere Mods übergeben ihren eigenen Typ als Namespace
    ///    - Dateien landen im Ordner des jeweiligen Mods
    ///    - ReadFile, WriteFile, FileExists, DeleteFile
    ///    - ParseINI, GetValue für Konfigurationsdateien
    /// </summary>
    public class FileManagerModule : IModule
    {
        public string ModuleName { get { return "Core_FileManager"; } }

        // ── Dateinamen ───────────────────────────────────────────────────────
        private const string GLOBAL_CONFIG_FILE = "Phantombite_GlobalConfig.ini";
        private const string LOG_INDEX_FILE     = "Phantombite_LogIndex.txt";
        private const string LOG_PREFIX         = "Phantombite_Core_";
        private const string LOG_EXTENSION      = ".log";
        private const int    MAX_LOGS           = 20;

        // Update10 = alle 10 Ticks, 60 Ticks = 1s → alle 30 Aufrufe = 5s
        private const int WRITE_INTERVAL = 30;
        private int _writeCounter = 0;

        private string       _currentLogFile = null;
        private List<string> _logIndex       = new List<string>();
        private bool         _isServer       = false;

        private ModDetector _modDetector;

        // ── ModDetector setzen ───────────────────────────────────────────────

        public void SetModDetector(ModDetector modDetector)
        {
            _modDetector = modDetector;
        }

        // ── IModule ──────────────────────────────────────────────────────────

        public void Init()
        {
            _isServer = _modDetector != null ? _modDetector.IsServer : MyAPIGateway.Multiplayer.IsServer;

            if (!_isServer) return;

            // Alles nur auf Server (Singleplayer + Dedicated Server)
            LoadGlobalConfig();
            LoadLogIndex();
            CreateNewLogFile();
            CleanOldLogs();

            LoggerModule.Info("Core", "Core_FileManager", "Initialized — Log: " + _currentLogFile);
        }

        public void Update()
        {
            if (!_isServer || _currentLogFile == null) return;

            _writeCounter++;
            if (_writeCounter < WRITE_INTERVAL) return;
            _writeCounter = 0;

            WriteBufferToFile();
        }

        public void SaveData()
        {
            if (!_isServer || _currentLogFile == null) return;
            WriteBufferToFile();
        }

        public void Close()
        {
            if (!_isServer || _currentLogFile == null) return;
            WriteBufferToFile();
            AppendToLog("--- Session End ---");
        }

        // ── Global Config ────────────────────────────────────────────────────

        private void LoadGlobalConfig()
        {
            try
            {
                if (!CoreFileExists(GLOBAL_CONFIG_FILE))
                {
                    DeployGlobalConfig();
                    MyLog.Default.WriteLineAndConsole("[PhantombiteCore] Core_FileManager: GlobalConfig erstellt");
                }

                string content = CoreReadFile(GLOBAL_CONFIG_FILE);
                if (content == null) return;

                var config = ParseINI(content);

                ApplyDebugLevel(config, "Phantombite_Core");
                ApplyDebugLevel(config, "Phantombite_Artefact");
                ApplyDebugLevel(config, "Phantombite_CableWinch");
                ApplyDebugLevel(config, "Phantombite_Creatures");
                ApplyDebugLevel(config, "Phantombite_Economy");
                ApplyDebugLevel(config, "Phantombite_Encounter");
                ApplyDebugLevel(config, "Phantombite_Server_Addon");
                ApplyDebugLevel(config, "Phantombite_Sulvax");
                ApplyDebugLevel(config, "Phantombite_SulvaxRespawnRover");

                MyLog.Default.WriteLineAndConsole("[PhantombiteCore] Core_FileManager: GlobalConfig geladen");
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole("[PhantombiteCore] Core_FileManager: Fehler beim Laden der GlobalConfig: " + ex.Message);
            }
        }

        private void ApplyDebugLevel(Dictionary<string, string> config, string modName)
        {
            string value = GetValue(config, "Debug", modName, "Normal");
            LoggerModule.LogLevel level = LoggerModule.LogLevel.Normal;
            if (value == "Debug") level = LoggerModule.LogLevel.Debug;
            else if (value == "Trace") level = LoggerModule.LogLevel.Trace;
            LoggerModule.SetLevel(modName, level);
        }

        private void DeployGlobalConfig()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# ==============================================================================");
            sb.AppendLine("# GLOBAL CONFIG - PhantomBite Core");
            sb.AppendLine("# ==============================================================================");
            sb.AppendLine("# Debug-Level pro Mod: Normal, Debug, Trace");
            sb.AppendLine("#   Normal — nur WARN + ERROR");
            sb.AppendLine("#   Debug  — + INFO + DEBUG");
            sb.AppendLine("#   Trace  — + jeden einzelnen Schritt (sehr viel Output!)");
            sb.AppendLine("# ==============================================================================");
            sb.AppendLine();
            sb.AppendLine("[Debug]");
            sb.AppendLine("Phantombite_Core=Normal");
            sb.AppendLine("Phantombite_Artefact=Normal");
            sb.AppendLine("Phantombite_CableWinch=Normal");
            sb.AppendLine("Phantombite_Creatures=Normal");
            sb.AppendLine("Phantombite_Economy=Normal");
            sb.AppendLine("Phantombite_Encounter=Normal");
            sb.AppendLine("Phantombite_Server_Addon=Normal");
            sb.AppendLine("Phantombite_Sulvax=Normal");
            sb.AppendLine("Phantombite_SulvaxRespawnRover=Normal");

            CoreWriteFile(GLOBAL_CONFIG_FILE, sb.ToString());
        }

        // ── Log Verwaltung intern ────────────────────────────────────────────

        private void LoadLogIndex()
        {
            try
            {
                _logIndex.Clear();
                string content = CoreReadFile(LOG_INDEX_FILE);
                if (content == null) return;

                string[] lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                    if (!string.IsNullOrEmpty(line.Trim()))
                        _logIndex.Add(line.Trim());
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole("[PhantombiteCore] Core_FileManager: Fehler beim Laden des Log-Index: " + ex.Message);
            }
        }

        private void SaveLogIndex()
        {
            try
            {
                var sb = new StringBuilder();
                foreach (var entry in _logIndex)
                    sb.AppendLine(entry);
                CoreWriteFile(LOG_INDEX_FILE, sb.ToString());
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole("[PhantombiteCore] Core_FileManager: Fehler beim Speichern des Log-Index: " + ex.Message);
            }
        }

        private void CreateNewLogFile()
        {
            try
            {
                string timestamp    = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                string sessionType  = _modDetector != null
                    ? (_modDetector.IsSingleplayer ? "Singleplayer" : "Server")
                    : "Unknown";

                _currentLogFile = LOG_PREFIX + timestamp + LOG_EXTENSION;

                AppendToLog("# ==============================================================================");
                AppendToLog("# PhantomBite Core Log");
                AppendToLog("# ==============================================================================");
                AppendToLog("# Start   : " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                AppendToLog("# Session : " + sessionType);
                AppendToLog("# Mode    : " + (_modDetector != null && _modDetector.IsDevMode ? "DEV" : "WORKSHOP"));
                AppendToLog("# ==============================================================================");

                _logIndex.Add(_currentLogFile);
                SaveLogIndex();
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole("[PhantombiteCore] Core_FileManager: Fehler beim Erstellen der Log-Datei: " + ex.Message);
            }
        }

        private void CleanOldLogs()
        {
            try
            {
                while (_logIndex.Count > MAX_LOGS)
                {
                    string oldest = _logIndex[0];
                    _logIndex.RemoveAt(0);
                    if (CoreFileExists(oldest)) CoreDeleteFile(oldest);
                    MyLog.Default.WriteLineAndConsole("[PhantombiteCore] Core_FileManager: Alter Log gelöscht: " + oldest);
                }
                SaveLogIndex();
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole("[PhantombiteCore] Core_FileManager: Fehler beim Bereinigen alter Logs: " + ex.Message);
            }
        }

        private void WriteBufferToFile()
        {
            try
            {
                var entries = LoggerModule.FlushBuffer();
                if (entries == null || entries.Count == 0) return;

                var sb = new StringBuilder();
                foreach (var entry in entries)
                    sb.AppendLine(entry);

                AppendToLog(sb.ToString().TrimEnd());
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole("[PhantombiteCore] Core_FileManager: Fehler beim Schreiben des Buffers: " + ex.Message);
            }
        }

        private void AppendToLog(string content)
        {
            try
            {
                string existing = CoreReadFile(_currentLogFile) ?? "";
                CoreWriteFile(_currentLogFile, existing + content + "\n");
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole("[PhantombiteCore] Core_FileManager: Fehler beim Anhängen an Log: " + ex.Message);
            }
        }

        // ── Core interne Datei-Operationen ───────────────────────────────────

        private string CoreReadFile(string filename)
        {
            try
            {
                if (!MyAPIGateway.Utilities.FileExistsInWorldStorage(filename, typeof(FileManagerModule)))
                    return null;
                using (var reader = MyAPIGateway.Utilities.ReadFileInWorldStorage(filename, typeof(FileManagerModule)))
                    return reader.ReadToEnd();
            }
            catch { return null; }
        }

        private void CoreWriteFile(string filename, string content)
        {
            try
            {
                using (var writer = MyAPIGateway.Utilities.WriteFileInWorldStorage(filename, typeof(FileManagerModule)))
                    writer.Write(content);
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole("[PhantombiteCore] Core_FileManager: Fehler beim Schreiben von '" + filename + "': " + ex.Message);
            }
        }

        private bool CoreFileExists(string filename)
        {
            try { return MyAPIGateway.Utilities.FileExistsInWorldStorage(filename, typeof(FileManagerModule)); }
            catch { return false; }
        }

        private void CoreDeleteFile(string filename)
        {
            try { MyAPIGateway.Utilities.DeleteFileInWorldStorage(filename, typeof(FileManagerModule)); }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole("[PhantombiteCore] Core_FileManager: Fehler beim Löschen von '" + filename + "': " + ex.Message);
            }
        }

        // ── Public Helfer-API für andere Mods ────────────────────────────────

        /// <summary>Liest eine Datei aus dem Storage des angegebenen Mods.</summary>
        public static string ReadFile(string filename, Type modType)
        {
            try
            {
                if (!MyAPIGateway.Multiplayer.IsServer) return null;
                if (!MyAPIGateway.Utilities.FileExistsInWorldStorage(filename, modType)) return null;
                using (var reader = MyAPIGateway.Utilities.ReadFileInWorldStorage(filename, modType))
                    return reader.ReadToEnd();
            }
            catch (Exception ex)
            {
                LoggerModule.Error("Core", "Core_FileManager", "Fehler beim Lesen von '" + filename + "'", ex);
                return null;
            }
        }

        /// <summary>Schreibt eine Datei in den Storage des angegebenen Mods.</summary>
        public static bool WriteFile(string filename, string content, Type modType)
        {
            try
            {
                if (!MyAPIGateway.Multiplayer.IsServer) return false;
                using (var writer = MyAPIGateway.Utilities.WriteFileInWorldStorage(filename, modType))
                    writer.Write(content);
                return true;
            }
            catch (Exception ex)
            {
                LoggerModule.Error("Core", "Core_FileManager", "Fehler beim Schreiben von '" + filename + "'", ex);
                return false;
            }
        }

        /// <summary>Prüft ob eine Datei im Storage des angegebenen Mods existiert.</summary>
        public static bool FileExists(string filename, Type modType)
        {
            try
            {
                if (!MyAPIGateway.Multiplayer.IsServer) return false;
                return MyAPIGateway.Utilities.FileExistsInWorldStorage(filename, modType);
            }
            catch { return false; }
        }

        /// <summary>Löscht eine Datei aus dem Storage des angegebenen Mods.</summary>
        public static bool DeleteFile(string filename, Type modType)
        {
            try
            {
                if (!MyAPIGateway.Multiplayer.IsServer) return false;
                MyAPIGateway.Utilities.DeleteFileInWorldStorage(filename, modType);
                return true;
            }
            catch (Exception ex)
            {
                LoggerModule.Error("Core", "Core_FileManager", "Fehler beim Löschen von '" + filename + "'", ex);
                return false;
            }
        }

        /// <summary>Parst eine INI-Datei in ein Dictionary. Schlüssel: "Section.Key"</summary>
        public static Dictionary<string, string> ParseINI(string content)
        {
            var result = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(content)) return result;

            string currentSection = "";
            try
            {
                string[] lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    string trimmed = line.Trim();
                    if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#")) continue;

                    if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
                    {
                        currentSection = trimmed.Substring(1, trimmed.Length - 2);
                        continue;
                    }

                    int eq = trimmed.IndexOf('=');
                    if (eq <= 0) continue;

                    string key     = trimmed.Substring(0, eq).Trim();
                    string value   = trimmed.Substring(eq + 1).Trim();
                    string fullKey = string.IsNullOrEmpty(currentSection) ? key : currentSection + "." + key;
                    result[fullKey] = value;
                }
            }
            catch (Exception ex)
            {
                LoggerModule.Error("Core", "Core_FileManager", "Fehler beim Parsen der INI", ex);
            }
            return result;
        }

        /// <summary>Liest einen String-Wert aus einem geparsten INI-Dictionary.</summary>
        public static string GetValue(Dictionary<string, string> config, string section, string key, string defaultValue = "")
        {
            if (config == null) return defaultValue;
            string fullKey = section + "." + key;
            string value;
            return config.TryGetValue(fullKey, out value) ? value : defaultValue;
        }

        /// <summary>Liest einen int-Wert.</summary>
        public static int GetValueInt(Dictionary<string, string> config, string section, string key, int defaultValue = 0)
        {
            string value = GetValue(config, section, key, defaultValue.ToString());
            int result;
            return int.TryParse(value, out result) ? result : defaultValue;
        }

        /// <summary>Liest einen float-Wert.</summary>
        public static float GetValueFloat(Dictionary<string, string> config, string section, string key, float defaultValue = 0f)
        {
            string value = GetValue(config, section, key, defaultValue.ToString());
            float result;
            return float.TryParse(value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out result) ? result : defaultValue;
        }

        /// <summary>Liest einen bool-Wert.</summary>
        public static bool GetValueBool(Dictionary<string, string> config, string section, string key, bool defaultValue = false)
        {
            string value = GetValue(config, section, key, defaultValue.ToString());
            bool result;
            return bool.TryParse(value, out result) ? result : defaultValue;
        }
    }
}