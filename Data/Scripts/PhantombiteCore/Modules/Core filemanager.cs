using System;
using System.Collections.Generic;
using System.Text;
using Sandbox.ModAPI;
using PhantombiteCore.Core;
using PhantombiteCore.Modules;

namespace PhantombiteCore.Modules
{
    /// <summary>
    /// Core_FileManager
    ///
    /// Zwei Aufgaben:
    ///
    /// 1. GLOBAL CONFIG
    ///    - Phantombite_GlobalConfig.ini — Debug-Level (0/1/2) pro Mod
    ///    - Self-Healing: Datei wird erstellt wenn nicht vorhanden
    ///    - Debug-Level wird nach dem Laden an PBLog weitergegeben
    ///    - Nur auf Server (Singleplayer + Dedicated Server)
    ///
    /// 2. HELFER-API FÜR ANDERE MODS
    ///    - Andere Mods übergeben ihren eigenen Typ als Namespace
    ///    - Dateien landen im Ordner des jeweiligen Mods
    ///    - ReadFile, WriteFile, FileExists, DeleteFile
    ///    - ParseINI, GetValue für Konfigurationsdateien
    /// </summary>
    public class FileManagerModule : IModule
    {
        public string ModuleName { get { return "Core_FileManager"; } }

        private const string MOD            = "Core";
        private const string MODULE         = "Core_FileManager";
        private const string GLOBAL_CONFIG  = "Phantombite_GlobalConfig.ini";
        private const string LOG_INDEX      = "Phantombite_LogIndex.txt";
        private const string LOG_PREFIX     = "Phantombite_";
        private const string LOG_EXT        = ".log";
        private const int    MAX_LOGS       = 20;      // Dateien (Teile zählen einzeln)
        private const int    MAX_SEGMENT_CHARS = 400000; // ~0,8 MB pro Log-Teil

        private bool   _isServer        = false;
        private string _currentLogFile  = null;
        private string _sessionStamp    = null;
        private int    _segmentNo       = 1;
        private readonly StringBuilder _segment = new StringBuilder();
        private ModDetector _modDetector;
        private Dictionary<string, string> _parsedConfig = null;

        // ── Adaptives Flush-System ────────────────────────────────────────────
        private DateTime _lastFlush          = DateTime.MinValue;
        private int      _flushIntervalSec   = 60;   // Start: 1 Minute
        private const int FLUSH_MIN_SEC      = 60;   // Minimum: 1 Min
        private const int FLUSH_MAX_SEC      = 300;  // Maximum: 5 Min (Fallback)
        private const float SIM_FLUSH_THRESH = 0.90f; // SimSpeed unter dem wir nicht flushen
        private readonly System.Diagnostics.Stopwatch _flushWatch = new System.Diagnostics.Stopwatch();

        public Dictionary<string, string> GetParsedConfig() { return _parsedConfig; }

        // Statischer Zugriff für PerformanceModule (läuft nach FileManager.Init())
        private static FileManagerModule _instance;
        public static Dictionary<string, string> GetCachedConfig()
        {
            return _instance != null ? _instance._parsedConfig : null;
        }

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

            _instance = this;
            LoadGlobalConfig();
            CreateLogFile();
            PBLog.Log(MOD, MODULE, "Initialisiert");
        }

        public void Update()
        {
            if (!_isServer || _currentLogFile == null) return;

            // RAM-Limit: 50MB hardcoded — Notfall-Flush unabhängig von SimSpeed
            const long MAX_BUFFER_BYTES = 50L * 1024L * 1024L;
            if (PBLog.GetBufferSizeEstimate() >= MAX_BUFFER_BYTES)
            {
                PBLog.Log(MOD, MODULE, "Log-Buffer 50MB Limit erreicht — Notfall-Flush");
                FlushBuffer();
                _lastFlush = DateTime.UtcNow;
                return;
            }

            double elapsed = (DateTime.UtcNow - _lastFlush).TotalSeconds;

            // Fallback: nach FLUSH_MAX_SEC immer flushen egal was
            bool fallback = elapsed >= FLUSH_MAX_SEC;

            // Normal: SimSpeed gut genug + Intervall abgelaufen
            bool simOk  = PerformanceModule.CurrentSimSpeed >= SIM_FLUSH_THRESH;
            bool timeOk = elapsed >= _flushIntervalSec;

            if (!fallback && (!simOk || !timeOk)) return;

            // Flush mit Zeitmessung
            _flushWatch.Restart();
            FlushBuffer();
            _flushWatch.Stop();

            long writeMs = _flushWatch.ElapsedMilliseconds;
            _lastFlush   = DateTime.UtcNow;

            // Intervall dynamisch anpassen
            AdjustFlushInterval(writeMs, simOk);
        }

        private void AdjustFlushInterval(long writeMs, bool simWasOk)
        {
            int newInterval;

            if (writeMs < 5)
            {
                // Sehr schnell → Minimum
                newInterval = FLUSH_MIN_SEC;
            }
            else if (writeMs < 15)
            {
                // Normal → 2 Minuten
                newInterval = 120;
            }
            else if (writeMs < 40)
            {
                // Langsam → 3 Minuten
                newInterval = 180;
                PBLog.Log(MOD, MODULE, "Log flush langsam: " + writeMs + "ms — Intervall auf 3min", 1);
            }
            else
            {
                // Sehr langsam → Maximum + Warnung
                newInterval = FLUSH_MAX_SEC;
                PBLog.Warn(MOD, MODULE, "Log flush sehr langsam: " + writeMs + "ms — Intervall auf 5min");
            }

            // Falls wir wegen Fallback geflusht haben obwohl SimSpeed schlecht war
            if (!simWasOk)
            {
                newInterval = FLUSH_MAX_SEC;
                PBLog.Log(MOD, MODULE, "Log flush unter Last (Fallback) — " + writeMs + "ms", 1);
            }

            if (newInterval != _flushIntervalSec)
            {
                PBLog.Log(MOD, MODULE, "Flush-Intervall: " + _flushIntervalSec + "s → " + newInterval + "s", 1);
                _flushIntervalSec = newInterval;
            }
        }

        public void SaveData() { FlushBuffer(); }

        public void Close()
        {
            PBLog.Log(MOD, MODULE, "Session beendet");
            FlushBuffer();
        }

        // ── Global Config ────────────────────────────────────────────────────

        private void LoadGlobalConfig()
        {
            try
            {
                if (!CoreFileExists(GLOBAL_CONFIG))
                {
                    DeployGlobalConfig();
                    PBLog.Log(MOD, MODULE, "GlobalConfig neu erstellt");
                }

                string content = CoreReadFile(GLOBAL_CONFIG);
                if (content == null) return;

                var config = ParseINI(content);
                _parsedConfig = config;

                // Core immer, alle anderen nur wenn aktiv
                ApplyDebugLevel(config, ModRegistry.LocalCore, true);
                foreach (var id in ModRegistry.AllPbIds)
                    ApplyDebugLevel(config, ModRegistry.GetLocalName(id), _modDetector.IsActive(id));

                PBLog.Log(MOD, MODULE, "GlobalConfig geladen");
            }
            catch (Exception ex)
            {
                PBLog.Error(MOD, MODULE, "Fehler beim Laden der GlobalConfig", ex);
            }
        }

        private void ApplyDebugLevel(Dictionary<string, string> config, string modName, bool active)
        {
            if (!active) return;
            string value = GetValue(config, "Debug", modName, "0");
            int level;
            if (!int.TryParse(value, out level)) level = 0;
            if (level < 0) level = 0;
            if (level > 2) level = 2;
            PBLog.SetLevel(modName, level);
        }

        private void DeployGlobalConfig()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# ==============================================================================");
            sb.AppendLine("# GLOBAL CONFIG - PhantomBite Core");
            sb.AppendLine("# ==============================================================================");
            sb.AppendLine("# Debug-Level pro Mod:");
            sb.AppendLine("#   0 — Nur wichtige Infos (Standard, immer sichtbar)");
            sb.AppendLine("#   1 — Wichtigste Debug-Infos");
            sb.AppendLine("#   2 — Detaillierte Debug-Infos (nicht für jeden Mod nötig)");
            sb.AppendLine("# ==============================================================================");
            sb.AppendLine();
            sb.AppendLine("[Debug]");
            foreach (var localName in ModRegistry.AllLocalNames)
                sb.AppendLine(localName + "=0");
            sb.AppendLine();
            sb.AppendLine("# ==============================================================================");
            sb.AppendLine("# PERFORMANCE SYSTEM");
            sb.AppendLine("# ==============================================================================");
            sb.AppendLine("# SampleInterval             — SimSpeed Prüfung alle N Ticks");
            sb.AppendLine("# DropThreshold              — Unter diesem Wert = Drop erkannt (0.0-1.0)");
            sb.AppendLine("# RecoveryThreshold          — Über diesem Wert = Drop beendet");
            sb.AppendLine("# StrikeDurationTicks        — Drop muss so viele Ticks anhalten bevor er zählt");
            sb.AppendLine("# CorrelationsBeforeEscalate — Effektive Treffer bis ein Mod eskaliert wird");
            sb.AppendLine("# HeavyTimeoutTicks          — HEAVY-Operation ohne HEAVY_END wird nach N Ticks geschlossen");
            sb.AppendLine("# HeavyGraceWindowSec        — Sekunden nach HEAVY_START in denen ein Drop noch zugeordnet wird");
            sb.AppendLine("# StartupDelayTicks          — Ticks nach Start bis Messung beginnt (3600=1Min, 18000=5Min)");
            sb.AppendLine("# ==============================================================================");
            sb.AppendLine();
            sb.AppendLine("[Performance]");
            sb.AppendLine("SampleInterval=10");
            sb.AppendLine("DropThreshold=0.85");
            sb.AppendLine("RecoveryThreshold=0.95");
            sb.AppendLine("StrikeDurationTicks=90");
            sb.AppendLine("CorrelationsBeforeEscalate=3");
            sb.AppendLine("HeavyTimeoutTicks=600");
            sb.AppendLine("HeavyGraceWindowSec=12");
            sb.AppendLine("StartupDelayTicks=3600");
            sb.AppendLine();
            sb.AppendLine("# CurrentLevel wird von Core automatisch verwaltet.");
            sb.AppendLine("# Wird zurückgesetzt durch: !pbc perf reset ODER Mod-Update.");
            sb.AppendLine("# Bekannte Muster (permanent) bleiben über Neustart erhalten.");
            sb.AppendLine();

            foreach (var mod in ModRegistry.PerformanceMods)
            {
                sb.AppendLine("[Performance." + mod + "]");
                sb.AppendLine("EscalationPath=0,1,2,3");
                sb.AppendLine("CorrelationsBeforeEscalate=3");
                sb.AppendLine("CurrentLevel=0");
                sb.AppendLine();
            }

            CoreWriteFile(GLOBAL_CONFIG, sb.ToString());
        }

        // ── Log-Datei ────────────────────────────────────────────────────────

        private void CreateLogFile()
        {
            try
            {
                _sessionStamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                _segmentNo    = 1;
                StartLogSegment("# Phantombite Core Log — " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                PBLog.Log(MOD, MODULE, "Log-Datei: " + _currentLogFile);
            }
            catch (Exception ex)
            {
                PBLog.Error(MOD, MODULE, "Fehler beim Erstellen der Log-Datei", ex);
            }
        }

        /// <summary>
        /// Beginnt eine neue Log-Datei (Teil 1 = Sitzungsstart, danach "_Teil2" usw.),
        /// trägt sie im Index ein und löscht die ältesten Dateien über MAX_LOGS.
        /// </summary>
        private void StartLogSegment(string headerLine)
        {
            _currentLogFile = LOG_PREFIX + _sessionStamp +
                (_segmentNo > 1 ? "_Teil" + _segmentNo : "") + LOG_EXT;
            _segment.Clear();
            _segment.Append(headerLine).Append('\n');
            _segment.Append("# ==========================================").Append('\n');

            var index = LoadLogIndex();
            index.Add(_currentLogFile);
            while (index.Count > MAX_LOGS)
            {
                string old = index[0];
                index.RemoveAt(0);
                try { MyAPIGateway.Utilities.DeleteFileInWorldStorage(old, typeof(FileManagerModule)); }
                catch { }
            }
            SaveLogIndex(index);
            WriteSegment();
        }

        private void FlushBuffer()
        {
            try
            {
                var lines = PBLog.TakeLogBuffer();
                if (lines == null || lines.Count == 0) return;
                var sb = new StringBuilder();
                foreach (var line in lines)
                    sb.AppendLine(line);
                CoreAppendToLog(sb.ToString().TrimEnd());
            }
            catch { }
        }

        /// <summary>
        /// Hängt Text an das aktuelle Log-Segment an. Der Inhalt liegt im Speicher (_segment),
        /// die Datei wird nur geschrieben, nie gelesen. Wird ein Segment größer als
        /// MAX_SEGMENT_CHARS, beginnt eine neue Datei — so bleibt jeder Schreibvorgang klein.
        /// </summary>
        private void CoreAppendToLog(string content)
        {
            if (_currentLogFile == null) return;
            try
            {
                if (_segment.Length + content.Length > MAX_SEGMENT_CHARS)
                {
                    _segmentNo++;
                    StartLogSegment("# Fortsetzung Teil " + _segmentNo + " — " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                }
                _segment.Append(content).Append('\n');
                WriteSegment();
            }
            catch { }
        }

        private void WriteSegment()
        {
            try
            {
                using (var w = MyAPIGateway.Utilities.WriteFileInWorldStorage(_currentLogFile, typeof(FileManagerModule)))
                    w.Write(_segment.ToString());
            }
            catch { }
        }

        private List<string> LoadLogIndex()
        {
            var result = new List<string>();
            try
            {
                if (!MyAPIGateway.Utilities.FileExistsInWorldStorage(LOG_INDEX, typeof(FileManagerModule)))
                    return result;
                using (var r = MyAPIGateway.Utilities.ReadFileInWorldStorage(LOG_INDEX, typeof(FileManagerModule)))
                {
                    string content = r.ReadToEnd();
                    foreach (var line in content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                        result.Add(line.Trim());
                }
            }
            catch { }
            return result;
        }

        private void SaveLogIndex(List<string> index)
        {
            try
            {
                var sb = new StringBuilder();
                foreach (var entry in index)
                    sb.AppendLine(entry);
                using (var w = MyAPIGateway.Utilities.WriteFileInWorldStorage(LOG_INDEX, typeof(FileManagerModule)))
                    w.Write(sb.ToString());
            }
            catch { }
        }

        // ── Public Log API für Command (!pbc log) ────────────────────────────

        public string GetCurrentLogFile()         { return _currentLogFile; }

        /// <summary>Inhalt des aktuellen Log-Teils (aus dem Speicher, ohne Dateizugriff).</summary>
        public string ReadCurrentLog()
        {
            if (_currentLogFile == null) return null;
            FlushBuffer(); // Erst flushen damit alles aktuell ist
            return _segment.ToString();
        }

        /// <summary>Die letzten Zeilen des aktuellen Log-Teils inkl. noch nicht geschriebener Zeilen.</summary>
        public static List<string> GetRecentLogLines(int count)
        {
            var result = new List<string>();
            if (_instance == null || _instance._currentLogFile == null) return result;
            _instance.FlushBuffer();
            var lines = _instance._segment.ToString().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            int start = Math.Max(0, lines.Length - count);
            for (int i = start; i < lines.Length; i++) result.Add(lines[i]);
            return result;
        }

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
                PBLog.Error(MOD, MODULE, "Fehler beim Schreiben von '" + filename + "'", ex);
            }
        }

        private bool CoreFileExists(string filename)
        {
            try { return MyAPIGateway.Utilities.FileExistsInWorldStorage(filename, typeof(FileManagerModule)); }
            catch { return false; }
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
                PBLog.Error("Core", "Core_FileManager", "Fehler beim Lesen von '" + filename + "'", ex);
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
                PBLog.Error("Core", "Core_FileManager", "Fehler beim Schreiben von '" + filename + "'", ex);
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
                PBLog.Error("Core", "Core_FileManager", "Fehler beim Löschen von '" + filename + "'", ex);
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
                PBLog.Error("Core", "Core_FileManager", "Fehler beim Parsen der INI", ex);
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