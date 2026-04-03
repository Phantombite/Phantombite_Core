using System;
using System.Collections.Generic;
using VRage.Utils;
using PhantombiteCore.Core;

namespace PhantombiteCore.Modules
{
    /// <summary>
    /// Core_Logger - Zentraler Logger für alle PhantomBite Mods.
    ///
    /// Log-Stufen pro Mod:
    ///   Normal — nur WARN + ERROR
    ///   Debug  — + INFO + DEBUG
    ///   Trace  — + jeden einzelnen Schritt
    ///
    /// Alle Mods schreiben in diesen Logger.
    /// Core_FileManager liest den Buffer und schreibt ihn in die Log-Datei.
    ///
    /// Format: 2026-03-26 18:05:12.123 [INFO ] Economy/FileManager : Nachricht
    /// </summary>
    public class LoggerModule : IModule
    {
        public string ModuleName { get { return "Core_Logger"; } }

        public enum LogLevel
        {
            Normal = 0,
            Debug  = 1,
            Trace  = 2
        }

        private static readonly Dictionary<string, LogLevel> _levels     = new Dictionary<string, LogLevel>();
        private static readonly List<string>                 _buffer     = new List<string>();
        private static bool                                  _initialized = false;

        private const int MAX_BUFFER = 2000;

        // ── IModule ──────────────────────────────────────────────────────────

        public void Init()
        {
            _initialized = true;
            Info("Core", "Core_Logger", "Logger initialized");
        }

        public void Update()   { }
        public void SaveData() { }

        public void Close()
        {
            Info("Core", "Core_Logger", "Logger closing");
            _initialized = false;
        }

        // ── Stufe setzen ─────────────────────────────────────────────────────

        public static void SetLevel(string mod, LogLevel level)
        {
            _levels[mod] = level;
            Write("INFO ", mod, "Core_Logger", "Log-Stufe gesetzt: " + level);
        }

        public static LogLevel GetLevel(string mod)
        {
            LogLevel level;
            return _levels.TryGetValue(mod, out level) ? level : LogLevel.Normal;
        }

        // ── Public API ───────────────────────────────────────────────────────

        /// <summary>WARN - Immer sichtbar.</summary>
        public static void Warn(string mod, string module, string message)
        {
            Write("WARN ", mod, module, message);
        }

        /// <summary>ERROR - Immer sichtbar. Exception optional.</summary>
        public static void Error(string mod, string module, string message, Exception ex = null)
        {
            Write("ERROR", mod, module, ex != null ? message + "\n" + ex : message);
        }

        /// <summary>INFO - Ab Debug-Stufe sichtbar.</summary>
        public static void Info(string mod, string module, string message)
        {
            if (GetLevel(mod) < LogLevel.Debug) return;
            Write("INFO ", mod, module, message);
        }

        /// <summary>DEBUG - Ab Debug-Stufe sichtbar.</summary>
        public static void Debug(string mod, string module, string message)
        {
            if (GetLevel(mod) < LogLevel.Debug) return;
            Write("DEBUG", mod, module, message);
        }

        /// <summary>TRACE - Nur bei Trace-Stufe sichtbar.</summary>
        public static void Trace(string mod, string module, string message)
        {
            if (GetLevel(mod) < LogLevel.Trace) return;
            Write("TRACE", mod, module, message);
        }

        // ── Buffer API für Core_FileManager ───────────────────────────────────────────────

        /// <summary>Gibt den Buffer zurück und leert ihn. Wird von Core_FileManager aufgerufen.</summary>
        public static List<string> FlushBuffer()
        {
            if (_buffer.Count == 0) return null;
            var copy = new List<string>(_buffer);
            _buffer.Clear();
            return copy;
        }

        // ── Interner Writer ──────────────────────────────────────────────────

        private static void Write(string level, string mod, string module, string message)
        {
            try
            {
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                string line = timestamp + " [" + level + "] " + mod + "/" + module + " : " + message;

                // Immer in SE Log
                MyLog.Default.WriteLineAndConsole(line);

                // In Buffer für Core_FileManager
                if (_buffer.Count < MAX_BUFFER)
                    _buffer.Add(line);
                else if (_buffer.Count == MAX_BUFFER)
                    _buffer.Add(timestamp + " [WARN ] Core/Core_Logger : Buffer voll! (" + MAX_BUFFER + " Eintraege)");
            }
            catch { }
        }
    }
}