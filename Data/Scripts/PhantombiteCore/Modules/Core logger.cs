using System;
using System.Collections.Generic;
using VRage.Utils;

namespace PhantombiteCore.Modules
{
    /// <summary>
    /// PBLog — Zentraler Logger für alle PhantomBite Mods.
    ///
    /// Debug-Level pro Mod (gesetzt via GlobalConfig oder !pbc debug):
    ///   0 — Nur wichtige Infos, immer sichtbar (Standard)
    ///   1 — Wichtigste Debug-Infos
    ///   2 — Detaillierte Debug-Infos (nicht für jeden Mod nötig)
    ///
    /// Log-Format:
    ///   [PB.Economy] FileManager: Preisliste geladen
    ///   [PB.Economy][1] FileManager: Item Iron Ore aufgelöst
    ///   [PB.Economy][2] FileManager: Slot 12 geprüft
    /// </summary>
    public static class PBLog
    {
        private const string MOD_PREFIX = "Phantombite_";
        private static readonly Dictionary<string, int> _levels = new Dictionary<string, int>();
        private static readonly List<string> _fileBuffer = new List<string>();

        // ── Level Management ─────────────────────────────────────────────────

        public static void SetLevel(string mod, int level)
        {
            if (level < 0) level = 0;
            if (level > 2) level = 2;
            _levels[mod] = level;
            Write("Core", "PBLog", "Debug-Level: " + Short(mod) + " = " + level, 0);
        }

        public static int GetLevel(string mod)
        {
            int level;
            return _levels.TryGetValue(mod, out level) ? level : 0;
        }

        // ── Public API ───────────────────────────────────────────────────────

        /// <summary>
        /// Schreibt eine Log-Zeile.
        /// level 0 = immer sichtbar (Standard)
        /// level 1 = nur wenn Debug-Level >= 1
        /// level 2 = nur wenn Debug-Level >= 2
        /// </summary>
        public static void Log(string mod, string module, string message, int level = 0)
        {
            if (level > 0 && GetLevel(mod) < level) return;
            Write(mod, module, message, level);
        }

        /// <summary>Warnung — immer sichtbar, mit [WARN] Tag.</summary>
        public static void Warn(string mod, string module, string message)
        {
            Write(mod, module, "[WARN] " + message, 0);
        }

        /// <summary>Fehler — immer sichtbar, mit [ERROR] Tag. Exception optional.</summary>
        public static void Error(string mod, string module, string message, Exception ex = null)
        {
            string full = ex != null ? message + "\n" + ex : message;
            Write(mod, module, "[ERROR] " + full, 0);
        }

        // ── Intern ───────────────────────────────────────────────────────────

        private static void Write(string mod, string module, string message, int level)
        {
            try
            {
                string tag  = level > 0 ? "[" + level + "]" : "";
                string line = "[PB." + Short(mod) + "]" + tag + " " + module + ": " + message;
                MyLog.Default.WriteLineAndConsole(line);

                // Level 0 + WARN + ERROR → Log-Datei Buffer
                // Level 1/2 → nur SE-Log, kein Datei-Spam
                if (level == 0)
                {
                    string ts = DateTime.Now.ToString("HH:mm:ss");
                    _fileBuffer.Add(ts + "  " + line);
                }
            }
            catch { }
        }

        /// <summary>
        /// Gibt den aktuellen Buffer zurück und leert ihn.
        /// Wird von FileManager periodisch aufgerufen.
        /// </summary>
        public static List<string> TakeLogBuffer()
        {
            if (_fileBuffer.Count == 0) return null;
            var copy = new List<string>(_fileBuffer);
            _fileBuffer.Clear();
            return copy;
        }

        /// <summary>Schätzt die aktuelle Buffer-Größe in Bytes (UTF-16).</summary>
        public static long GetBufferSizeEstimate()
        {
            long size = 0;
            foreach (var line in _fileBuffer)
                size += line.Length * 2;
            return size;
        }

        internal static string Short(string mod)
        {
            if (string.IsNullOrEmpty(mod)) return "?";
            return mod.StartsWith(MOD_PREFIX) ? mod.Substring(MOD_PREFIX.Length) : mod;
        }
    }

    // ── IModule-Wrapper ──────────────────────────────────────────────────────

    /// <summary>
    /// Dünner IModule-Wrapper für PBLog — meldet Init/Close im ModuleManager.
    /// PBLog selbst ist statisch und braucht keine echte Initialisierung.
    /// </summary>
    public class LoggerModule : Core.IModule
    {
        public string ModuleName { get { return "Core_Logger"; } }

        public void Init()
        {
            PBLog.Log("Core", "Logger", "Initialisiert");
        }

        public void Update()   { }
        public void SaveData() { }

        public void Close()
        {
            PBLog.Log("Core", "Logger", "Beendet");
        }
    }
}