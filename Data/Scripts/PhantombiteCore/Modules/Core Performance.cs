using System;
using System.Collections.Generic;
using System.Text;
using Sandbox.ModAPI;
using PhantombiteCore.Modules;

namespace PhantombiteCore.Core
{
    /// <summary>
    /// Core_Performance — Dreischichtiges Performance-Monitoring System
    ///
    /// Schicht 1 — State (ServerSimulationRatio):
    ///   Gate: ServerSimRatio kleiner Threshold → System reagiert
    ///
    /// Schicht 2 — Timeline (Tick-Zähler):
    ///   Strike gilt erst nach _strikeDurationTicks (kein Rauschen)
    ///   HEAVY-Timeout: auto-schließen nach _heavyTimeoutTicks
    ///
    /// Schicht 3 — Causality (HEAVY Events):
    ///   HEAVY_START / HEAVY_END von Mods → Korrelation mit Drops
    ///
    /// Protokoll Mods an Core (1995000):
    ///   HEAVY_START|modName|opName
    ///   HEAVY_END|modName|opName
    ///   PERFACK|modName|confirmedLevel
    ///
    /// Protokoll Core an Mods:
    ///   PERFLEVEL|level
    /// </summary>
    public class PerformanceModule : IModule
    {
        public string ModuleName { get { return "Core_Performance"; } }

        private const string MOD          = "Phantombite_Core";
        private const string MDL          = "Core_Performance";
        private const string HISTORY_FILE = "Phantombite_PerfHistory.txt";

        // ── Referenzen ────────────────────────────────────────────────────────
        private CommandModule _commandModule;
        public void SetCommandModule(CommandModule cmd) { _commandModule = cmd; }

        // ── Config ────────────────────────────────────────────────────────────
        private float _dropThreshold             = 0.85f;
        private float _recoveryThreshold         = 0.95f;
        private int   _strikeDurationTicks        = 90;
        private int   _defaultCorrelations        = 3;
        private int   _heavyTimeoutTicks          = 600;
        private int   _heavyGraceWindowSec         = 12;   // Sekunden nach HEAVY_START für Attribution
        private int   _startupDelayTicks          = 3600;
        private int   _sampleInterval             = 10;

        // Per-Mod Config
        private readonly Dictionary<string, int>   _modCorrelationThreshold = new Dictionary<string, int>();
        private readonly Dictionary<string, int[]>  _modEscalationPath       = new Dictionary<string, int[]>();
        private static readonly int[] DEFAULT_PATH = new int[] { 0, 1, 2, 3 };

        // ── Performance Levels ────────────────────────────────────────────────
        private readonly Dictionary<string, int>  _modPerfLevels      = new Dictionary<string, int>();
        private readonly Dictionary<string, bool> _modPerfPermanent   = new Dictionary<string, bool>();
        private readonly Dictionary<string, int>  _modConfirmedLevels = new Dictionary<string, int>();
        private readonly Dictionary<string, int>   _correlationCounts  = new Dictionary<string, int>();   // rohe Treffer-Anzahl
        private readonly Dictionary<string, float> _confidenceSum      = new Dictionary<string, float>(); // Summe der Vertrauenswerte (0.3 oder 1.0 pro Treffer)

        // ── SimSpeed ──────────────────────────────────────────────────────────
        public static float CurrentSimSpeed { get; private set; } = 1f;
        private float _lastLoggedSimSpeed = -1f;
        private float _lastSimSpeed       = 1f;
        private const float SIM_LOG_THRESHOLD = 0.05f;

        // ── Drop Tracking ─────────────────────────────────────────────────────
        private bool _inDrop               = false;
        private int  _dropStartTick        = 0;
        private float _dropMinSimSpeed     = 1f;   // tiefster SimSpeed im aktuellen Drop
        private bool _strikeCountedThisDrop = false;

        // Unbekannte Drops — nur für Info/Statistik, keine Eskalation
        private int _unknownDropCount    = 0;

        // Join-Schutz: nach Spieler-Beitritt kein Strike für X Sekunden
        private int _joinGraceUntilTick  = -1;
        private const int JOIN_GRACE_TICKS = 1800; // 30 Sek bei 60 TPS

        // ── HEAVY Operationen ─────────────────────────────────────────────────
        private readonly Dictionary<string, HeavyOp> _activeHeavyOps = new Dictionary<string, HeavyOp>();
        private readonly Queue<MiniEvent>             _recentHeavy    = new Queue<MiniEvent>();
        private const int MINI_QUEUE_MAX = 20;

        // ── Timing ────────────────────────────────────────────────────────────
        private int  _tick       = 0;
        private int  _sampleTick = 0;
        private bool _isServer    = false;
        private bool _initialized = false;

        // ── Performance History ───────────────────────────────────────────────
        private readonly List<PerfHistoryEntry> _history = new List<PerfHistoryEntry>();

        // ── Interne Klassen ───────────────────────────────────────────────────

        private class HeavyOp
        {
            public string ModName;
            public string OpName;
            public int    StartTick;
            public float  SimSpeedAtStart; // SimSpeed als die Operation startete
        }

        private class MiniEvent
        {
            public string ModName;
            public string OpName;
            public int    Tick;
        }

        private class PerfHistoryEntry
        {
            public string   ModName;
            public string   ModVersion;
            public DateTime Timestamp;
            public string   Cause;
            public int      LevelBefore;
            public int      LevelAfter;
            public bool     Permanent;
            public float    MinSimSpeed;   // tiefster SimSpeed beim auslösenden Drop
        }

        // ── IModule ───────────────────────────────────────────────────────────

        public void Init()
        {
            _isServer = MyAPIGateway.Multiplayer.IsServer ||
                        MyAPIGateway.Session.OnlineMode == VRage.Game.MyOnlineModeEnum.OFFLINE;
            if (!_isServer) return;

            LoadConfig();
            LoadHistory();

            MyAPIGateway.Utilities.RegisterMessageHandler(1995000L, OnModMessage);
            _initialized = true;

            PBLog.Log(MOD, MDL, "Initialisiert" +
                " | Drop: " + _dropThreshold +
                " | Strike: " + _strikeDurationTicks + " Ticks" +
                " | Korrelationen: " + _defaultCorrelations);
        }

        public void Update()
        {
            if (!_initialized) return;
            _tick++;
            _sampleTick++;

            if (_tick < _startupDelayTicks) return;
            if (_tick == _startupDelayTicks) return;

            CheckHeavyTimeouts();

            if (_sampleTick < _sampleInterval) return;
            _sampleTick = 0;

            EvaluateSimSpeed();
        }

        public void SaveData() { }

        public void Close()
        {
            if (!_initialized) return;
            SaveHistory();
            if (MyAPIGateway.Utilities != null)
                MyAPIGateway.Utilities.UnregisterMessageHandler(1995000L, OnModMessage);
            _initialized = false;
        }

        // ── Config laden ─────────────────────────────────────────────────────

        private void LoadConfig()
        {
            var config = FileManagerModule.GetCachedConfig();
            if (config == null) return;

            _dropThreshold             = FileManagerModule.GetValueFloat(config, "Performance", "DropThreshold",              _dropThreshold);
            _recoveryThreshold         = FileManagerModule.GetValueFloat(config, "Performance", "RecoveryThreshold",          _recoveryThreshold);
            _strikeDurationTicks        = FileManagerModule.GetValueInt  (config, "Performance", "StrikeDurationTicks",        _strikeDurationTicks);
            _defaultCorrelations        = FileManagerModule.GetValueInt  (config, "Performance", "CorrelationsBeforeEscalate", _defaultCorrelations);
            _heavyTimeoutTicks          = FileManagerModule.GetValueInt  (config, "Performance", "HeavyTimeoutTicks",          _heavyTimeoutTicks);
            _heavyGraceWindowSec         = FileManagerModule.GetValueInt  (config, "Performance", "HeavyGraceWindowSec",         _heavyGraceWindowSec);
            _startupDelayTicks          = FileManagerModule.GetValueInt  (config, "Performance", "StartupDelayTicks",          _startupDelayTicks);
            _sampleInterval             = FileManagerModule.GetValueInt  (config, "Performance", "SampleInterval",             _sampleInterval);

            string[] knownMods = {
                "Mining", "Economy", "AutoTransfer", "CableWinch", "Creatures",
                "Encounter", "Artefact", "PlanetSpawner", "WaterElectrolyzer",
                "AdminProjektor", "StationRefill"
            };

            foreach (var mod in knownMods)
            {
                string section = "Performance." + mod;
                string modKey  = mod.ToLower();

                int corr = FileManagerModule.GetValueInt(config, section, "CorrelationsBeforeEscalate", _defaultCorrelations);
                _modCorrelationThreshold[modKey] = corr;

                string pathStr = FileManagerModule.GetValue(config, section, "EscalationPath", "0,1,2,3");
                _modEscalationPath[modKey] = ParsePath(pathStr);

                int savedLevel = FileManagerModule.GetValueInt(config, section, "CurrentLevel", 0);
                if (savedLevel > 0)
                {
                    _modPerfLevels[modKey]    = savedLevel;
                    _modPerfPermanent[modKey] = true;
                    PBLog.Log(MOD, MDL, mod + " startet auf Performance Level " + savedLevel);
                }
            }
        }

        // ── SimSpeed Evaluierung ─────────────────────────────────────────────

        private void EvaluateSimSpeed()
        {
            float sim = MyAPIGateway.Physics.ServerSimulationRatio;
            CurrentSimSpeed = sim;

            LogSimSpeedChange(sim);

            if (!_inDrop)
            {
                if (sim < _dropThreshold)
                {
                    _inDrop               = true;
                    _dropStartTick        = _tick;
                    _dropMinSimSpeed      = sim;
                    _strikeCountedThisDrop = false;
                }
            }
            else
            {
                if (sim >= _recoveryThreshold)
                {
                    int duration = (_tick - _dropStartTick) * _sampleInterval;
                    PBLog.Log(MOD, MDL, "SimSpeed erholt nach ~" + (duration / 60) + "s", 1);
                    _inDrop               = false;
                    _strikeCountedThisDrop = false;
                }
                else
                {
                    if (sim < _dropMinSimSpeed) _dropMinSimSpeed = sim;
                    int dropAge = _tick - _dropStartTick;
                    if (dropAge >= _strikeDurationTicks / _sampleInterval && !_strikeCountedThisDrop)
                    {
                        _strikeCountedThisDrop = true;
                        HandleDropStrike(sim);
                    }
                }
            }

            _lastSimSpeed = sim;
        }

        private void LogSimSpeedChange(float sim)
        {
            bool crossedDown = _lastSimSpeed >= _dropThreshold && sim < _dropThreshold;
            bool crossedUp   = _lastSimSpeed <  _dropThreshold && sim >= _dropThreshold;

            if (crossedDown)
            {
                PBLog.Log(MOD, MDL, "SimSpeed unter Schwellwert (" + _dropThreshold + "): " + sim.ToString("F2"));
                _lastLoggedSimSpeed = sim;
            }
            else if (crossedUp)
            {
                PBLog.Log(MOD, MDL, "SimSpeed über Schwellwert (" + _dropThreshold + "): " + sim.ToString("F2"));
                _lastLoggedSimSpeed = sim;
            }
            else if (_lastLoggedSimSpeed < 0f || Math.Abs(sim - _lastLoggedSimSpeed) >= SIM_LOG_THRESHOLD)
            {
                if (_lastLoggedSimSpeed >= 0f)
                {
                    string dir = sim < _lastLoggedSimSpeed ? "v" : "^";
                    PBLog.Log(MOD, MDL, "SimSpeed " + _lastLoggedSimSpeed.ToString("F2") +
                              " -> " + sim.ToString("F2") + " " + dir);
                }
                _lastLoggedSimSpeed = sim;
            }
        }

        /// <summary>Wird von PlayerTracker aufgerufen wenn ein Spieler beitritt.</summary>
        public void OnPlayerJoined(string playerName)
        {
            _joinGraceUntilTick = _tick + JOIN_GRACE_TICKS;
            PBLog.Log(MOD, MDL, playerName + " beigetreten — SimSpeed-Schutz aktiv (30s)");
        }

        // ── Strike Logik ─────────────────────────────────────────────────────

        private void HandleDropStrike(float sim)
        {
            // Kein Strike direkt nach einem Spieler-Join — Join selbst kann SimSpeed kurz senken
            if (_tick <= _joinGraceUntilTick)
            {
                PBLog.Log(MOD, MDL, "SimSpeed Drop ignoriert — Spieler gerade beigetreten (Schutz aktiv)", 1);
                return;
            }
            // Aktive HEAVY_OPs prüfen
            if (_activeHeavyOps.Count > 0)
            {
                foreach (var kvp in _activeHeavyOps)
                    RecordKnownCorrelation(kvp.Value.ModName, kvp.Value.OpName, sim);
                return;
            }

            // Mini-Queue Rückwärtsanalyse
            MiniEvent recent = null;
            foreach (var e in _recentHeavy)
                if (recent == null || e.Tick > recent.Tick) recent = e;

            if (recent != null)
            {
                int ageSec = (_tick - recent.Tick) * _sampleInterval / 60;
                if (ageSec < _heavyGraceWindowSec)
                {
                    PBLog.Log(MOD, MDL, "Schwache Korrelation: " + recent.ModName +
                        " (" + recent.OpName + ") vor " + ageSec + "s", 1);
                    RecordKnownCorrelation(recent.ModName, recent.OpName, sim);
                    return;
                }
            }

            RecordUnknownStrike();
        }

        private void RecordKnownCorrelation(string modName, string opName, float sim)
        {
            // War der Server stabil als die HEAVY-Operation startete?
            // Server war stabil (SimSpeed normal)  → volles Vertrauen (1.0)
            //   Der PB-Mod war wahrscheinlich der Auslöser des Drops
            // Server war bereits überlastet        → geringes Vertrauen (0.3)
            //   Der PB-Mod hat den Drop möglicherweise nur verstärkt, nicht verursacht
            float confidence = 1.0f;
            HeavyOp op;
            if (_activeHeavyOps.TryGetValue(modName, out op) && op.SimSpeedAtStart < _dropThreshold)
                confidence = 0.3f;

            // Treffer und Vertrauen getrennt tracken — nicht zusammenrechnen
            if (!_correlationCounts.ContainsKey(modName)) _correlationCounts[modName] = 0;
            if (!_confidenceSum.ContainsKey(modName))     _confidenceSum[modName]     = 0f;
            _correlationCounts[modName]++;
            _confidenceSum[modName] += confidence;

            int   hits          = _correlationCounts[modName];
            float effectiveScore = _confidenceSum[modName];
            int   threshold     = GetCorrelationThreshold(modName);
            int   avgConfPct    = (int)(effectiveScore / hits * 100f);

            PBLog.Warn(MOD, MDL, "SimSpeed Drop durch " + modName + " (" + opName + ")" +
                " — " + hits + " Treffer | Vertrauen " + avgConfPct + "%" +
                " | Effektiv " + effectiveScore.ToString("F1") + "/" + threshold +
                " | " + GetSeverityStr(_dropMinSimSpeed));

            if (effectiveScore >= threshold)
            {
                _correlationCounts[modName] = 0;
                _confidenceSum[modName]     = 0f;
                PBLog.Warn(MOD, MDL, "Performance Level erhöht — " + modName + ":");
                EscalateMod(modName, false, modName + " " + hits + " Treffer, ø" + avgConfPct + "% Vertrauen");
            }
        }

        private string GetSeverityStr(float minSimSpeed)
        {
            int depth = (int)((1f - minSimSpeed) * 100f);
            return "SimSpeed " + minSimSpeed.ToString("F2") + " | Tiefe " + depth + "%";
        }

        private void RecordUnknownStrike()
        {
            // Kein PB-Mod beteiligt → nur loggen, keine Eskalation
            // Vanilla SE Drops (Planeten, NPC-Spawns etc.) sollen PB-Mods nicht drosseln
            PBLog.Log(MOD, MDL, "Kein PB-Mod beteiligt | " + GetSeverityStr(_dropMinSimSpeed));
        }

        // ── Eskalation ────────────────────────────────────────────────────────

        private void EscalateAllMods(bool permanent, string cause)
        {
            if (_commandModule == null) return;
            var mods = _commandModule.GetRegisteredMods();
            if (mods == null || mods.Count == 0)
            {
                PBLog.Log(MOD, MDL, "Keine Mods registriert — kein Performance-Eingriff", 1);
                return;
            }
            PBLog.Warn(MOD, MDL, "Performance Level erhöht (" + cause + "):");
            foreach (var mod in mods)
                EscalateMod(mod, permanent, cause);
        }

        private void EscalateMod(string modName, bool permanent, string cause)
        {
            int current  = GetPerfLevel(modName);
            int newLevel = GetNextLevel(modName, current);
            if (newLevel == current)
            {
                PBLog.Log(MOD, MDL, "  " + PadRight(modName, 18) + ": Level " + current + " (max)");
                return;
            }
            SetPerfLevel(modName, newLevel, permanent, cause);
        }

        private void SetPerfLevel(string modName, int level, bool permanent, string cause)
        {
            int old = GetPerfLevel(modName);
            _modPerfLevels[modName]    = level;
            _modPerfPermanent[modName] = permanent;

            string permTag = permanent ? "PERMANENT" : "TEMPORÄR";
            PBLog.Log(MOD, MDL, "  " + PadRight(modName, 18) +
                ": Level " + old + " -> " + level + " [" + permTag + "]");

            if (_commandModule != null)
                _commandModule.SendToMod(modName, "PERFLEVEL|" + level);

            if (permanent)
                SavePerfLevelToConfig(modName, level);

            AddHistoryEntry(modName, old, level, cause, permanent);
        }

        // ── Reset ─────────────────────────────────────────────────────────────

        public void ResetMod(string modName)
        {
            modName = modName.ToLower();
            int current = GetPerfLevel(modName);
            if (current == 0) { PBLog.Log(MOD, MDL, modName + " bereits auf Level 0"); return; }

            _modPerfLevels.Remove(modName);
            _modPerfPermanent.Remove(modName);
            _correlationCounts.Remove(modName);
            _confidenceSum.Remove(modName);
            PBLog.Log(MOD, MDL, modName + " zurückgesetzt (war Level " + current + ")");
            if (_commandModule != null) _commandModule.SendToMod(modName, "PERFLEVEL|0");
            SavePerfLevelToConfig(modName, 0);
        }

        public void ResetAll()
        {
            var mods = new List<string>(_modPerfLevels.Keys);
            foreach (var mod in mods) ResetMod(mod);
            _correlationCounts.Clear();
            _confidenceSum.Clear();
            _unknownDropCount = 0;
            _history.Clear();
            SaveHistory();
            PBLog.Log(MOD, MDL, "Alle Performance Level und History zurückgesetzt");
        }

        public void OnModVersionChanged(string modName, string oldVersion, string newVersion)
        {
            modName = modName.ToLower();
            int removed = _history.RemoveAll(e =>
                e.ModName.ToLower() == modName && e.ModVersion == oldVersion);
            if (removed > 0)
            {
                SaveHistory();
                PBLog.Log(MOD, MDL, modName + " Update " + oldVersion + " -> " + newVersion +
                    " — " + removed + " History-Einträge gelöscht");
            }
            int current = GetPerfLevel(modName);
            if (current > 0)
            {
                PBLog.Log(MOD, MDL, modName + " Update — Level " + current + " -> 0");
                ResetMod(modName);
            }
        }

        public void OnModRegistered(string modName)
        {
            modName = modName.ToLower();
            int level = GetPerfLevel(modName);
            if (level == 0 || _commandModule == null) return;
            _commandModule.SendToMod(modName, "PERFLEVEL|" + level);
            bool perm = _modPerfPermanent.ContainsKey(modName) && _modPerfPermanent[modName];
            PBLog.Log(MOD, MDL, modName + " registriert — PERFLEVEL " + level +
                (perm ? " [PERMANENT]" : " [TEMPORÄR]") + " gesendet");
        }

        // ── HEAVY Operationen ─────────────────────────────────────────────────

        private void CheckHeavyTimeouts()
        {
            if (_activeHeavyOps.Count == 0) return;
            var expired = new List<string>();
            foreach (var kvp in _activeHeavyOps)
                if (_tick - kvp.Value.StartTick > _heavyTimeoutTicks)
                    expired.Add(kvp.Key);
            foreach (var mod in expired)
            {
                PBLog.Log(MOD, MDL, "HEAVY Timeout: " + mod +
                    " (" + _activeHeavyOps[mod].OpName + ") — auto-geschlossen", 1);
                _activeHeavyOps.Remove(mod);
            }
        }

        private void OnModMessage(object data)
        {
            try
            {
                string msg = data as string;
                if (string.IsNullOrEmpty(msg)) return;

                if (msg.StartsWith("HEAVY_START|"))
                {
                    string[] parts = msg.Split('|');
                    if (parts.Length < 3) return;
                    string modName = parts[1].ToLower();
                    string opName  = parts[2];
                    _activeHeavyOps[modName] = new HeavyOp
                        { ModName = modName, OpName = opName, StartTick = _tick, SimSpeedAtStart = CurrentSimSpeed };
                    if (_recentHeavy.Count >= MINI_QUEUE_MAX) _recentHeavy.Dequeue();
                    _recentHeavy.Enqueue(new MiniEvent { ModName = modName, OpName = opName, Tick = _tick });
                    PBLog.Log(MOD, MDL, "HEAVY " + modName + " — " + opName, 2);
                    return;
                }

                if (msg.StartsWith("HEAVY_END|"))
                {
                    string[] parts = msg.Split('|');
                    if (parts.Length < 2) return;
                    string modName = parts[1].ToLower();
                    string opName  = parts.Length > 2 ? parts[2] : "";
                    _activeHeavyOps.Remove(modName);
                    PBLog.Log(MOD, MDL, "HEAVY_END: " + modName + " — " + opName, 1);
                    return;
                }

                if (msg.StartsWith("PERFACK|"))
                {
                    string[] parts = msg.Split('|');
                    if (parts.Length < 3) return;
                    string modName = parts[1].ToLower();
                    int confirmed;
                    if (!int.TryParse(parts[2], out confirmed)) return;
                    _modConfirmedLevels[modName] = confirmed;
                    PBLog.Log(MOD, MDL, modName + " bestätigt Level " + confirmed, 1);
                    return;
                }
            }
            catch (Exception ex)
            {
                PBLog.Error(MOD, MDL, "Fehler in OnModMessage", ex);
            }
        }

        // ── Performance History ───────────────────────────────────────────────

        private void AddHistoryEntry(string modName, int oldLevel, int newLevel, string cause, bool permanent)
        {
            string version = _commandModule != null
                ? _commandModule.GetModVersion(modName) : "?.?.?";

            // Wiederholungs-Check: war dieser Mod+Version schon temporär eskaliert?
            if (!permanent)
            {
                foreach (var prev in _history)
                {
                    if (prev.ModName.ToLower() == modName &&
                        prev.ModVersion == version && !prev.Permanent)
                    {
                        PBLog.Warn(MOD, MDL, modName + " Wiederholungstäter (letzter Eintrag: " +
                            prev.Timestamp.ToString("HH:mm") + ") — PERMANENT");
                        permanent = true;
                        _modPerfPermanent[modName] = true;
                        SavePerfLevelToConfig(modName, newLevel);
                        break;
                    }
                }
            }

            _history.Add(new PerfHistoryEntry
            {
                ModName    = modName,
                ModVersion = version,
                Timestamp  = DateTime.UtcNow,
                Cause      = cause,
                LevelBefore = oldLevel,
                LevelAfter  = newLevel,
                Permanent  = permanent,
                MinSimSpeed = _dropMinSimSpeed
            });
            SaveHistory();
        }

        private void LoadHistory()
        {
            try
            {
                string content = FileManagerModule.ReadFile(HISTORY_FILE, typeof(FileManagerModule));
                if (string.IsNullOrEmpty(content)) return;
                foreach (var line in content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (line.StartsWith("#")) continue;
                    string[] parts = line.Split('|');
                    if (parts.Length < 7) continue;
                    DateTime ts;
                    if (!DateTime.TryParse(parts[0].Trim(), out ts)) continue;
                    int lvlBefore, lvlAfter;
                    if (!int.TryParse(parts[4].Trim(), out lvlBefore)) continue;
                    if (!int.TryParse(parts[5].Trim(), out lvlAfter)) continue;
                    _history.Add(new PerfHistoryEntry
                    {
                        Timestamp   = ts,
                        ModName     = parts[1].Trim(),
                        ModVersion  = parts[2].Trim(),
                        Cause       = parts[3].Trim(),
                        LevelBefore = lvlBefore,
                        LevelAfter  = lvlAfter,
                        Permanent   = parts[6].Trim() == "permanent",
                        MinSimSpeed = parts.Length > 7
                            ? float.Parse(parts[7].Trim(),
                                System.Globalization.CultureInfo.InvariantCulture) : 0f
                    });
                }
                if (_history.Count > 0)
                    PBLog.Log(MOD, MDL, "Performance History geladen: " + _history.Count + " Einträge");
            }
            catch (Exception ex)
            {
                PBLog.Error(MOD, MDL, "Fehler beim Laden der History", ex);
            }
        }

        private void SaveHistory()
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("# Phantombite Performance History");
                sb.AppendLine("# Timestamp|ModName|Version|Cause|LevelBefore|LevelAfter|permanent/temporary|MinSimSpeed");
                foreach (var e in _history)
                    sb.AppendLine(e.Timestamp.ToString("yyyy-MM-dd HH:mm:ss") + "|" +
                        e.ModName + "|" + e.ModVersion + "|" + e.Cause + "|" +
                        e.LevelBefore + "|" + e.LevelAfter + "|" +
                        (e.Permanent ? "permanent" : "temporary") + "|" +
                        e.MinSimSpeed.ToString("F2"));
                FileManagerModule.WriteFile(HISTORY_FILE, sb.ToString(), typeof(FileManagerModule));
            }
            catch (Exception ex)
            {
                PBLog.Error(MOD, MDL, "Fehler beim Speichern der History", ex);
            }
        }

        // ── Status ────────────────────────────────────────────────────────────

        public string GetStatusText()
        {
            var sb = new StringBuilder();
            sb.AppendLine("ServerSimSpeed: " + CurrentSimSpeed.ToString("F2") +
                (_inDrop ? " [DROP]" : ""));
            sb.AppendLine("Externe Drops (kein PB-Mod): " + _unknownDropCount + " | Kein Eingriff");

            bool anyElevated = false;
            foreach (var kvp in _modPerfLevels)
            {
                if (kvp.Value == 0) continue;
                if (!anyElevated) { sb.AppendLine("[Erhöhte Levels]"); anyElevated = true; }
                bool perm = _modPerfPermanent.ContainsKey(kvp.Key) && _modPerfPermanent[kvp.Key];
                int conf = _modConfirmedLevels.ContainsKey(kvp.Key) ? _modConfirmedLevels[kvp.Key] : -1;
                string confStr = conf >= 0 ? " [ACK:" + conf + "]" : " [unbestätigt]";
                float score = _confidenceSum.ContainsKey(kvp.Key) ? _confidenceSum[kvp.Key] : 0f;
                int   hits  = _correlationCounts.ContainsKey(kvp.Key) ? _correlationCounts[kvp.Key] : 0;
                int thr = GetCorrelationThreshold(kvp.Key);
                sb.AppendLine("  " + PadRight(kvp.Key, 18) +
                    "Level " + kvp.Value + (perm ? " [PERM]" : " [TEMP]") +
                    confStr + "  " + hits + " Treffer, Effektiv " + score.ToString("F1") + "/" + thr);
            }
            if (!anyElevated) sb.AppendLine("Alle Mods auf Level 0");

            if (_history.Count > 0)
            {
                sb.AppendLine("[History]");
                int start = Math.Max(0, _history.Count - 5);
                for (int i = start; i < _history.Count; i++)
                {
                    var e = _history[i];
                    sb.AppendLine("  " + e.Timestamp.ToString("HH:mm") + " " +
                        e.ModName + " " + e.LevelBefore + "->" + e.LevelAfter +
                        " [" + (e.Permanent ? "PERM" : "TEMP") + "] " +
                        e.Cause + " | " + GetSeverityStr(e.MinSimSpeed));
                }
            }
            return sb.ToString();
        }

        public List<string> GetRecentLog(int count = 15)
        {
            var result = new List<string>();
            try
            {
                string fn = FileManagerModule.GetCurrentLogFileName();
                if (fn == null) return result;
                string content = FileManagerModule.ReadFile(fn, typeof(FileManagerModule));
                if (content == null) return result;
                var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                int start = Math.Max(0, lines.Length - count);
                for (int i = start; i < lines.Length; i++) result.Add(lines[i]);
            }
            catch { }
            return result;
        }

        public int  GetPerfLevel(string modName) {
            int l; return _modPerfLevels.TryGetValue(modName.ToLower(), out l) ? l : 0; }
        public bool IsInDrop() { return _inDrop; }

        // ── Config schreiben ──────────────────────────────────────────────────

        private void SavePerfLevelToConfig(string modName, int level)
        {
            try
            {
                string content = FileManagerModule.ReadFile(
                    "Phantombite_GlobalConfig.ini", typeof(FileManagerModule));
                if (content == null) return;

                string section  = "[Performance." + CapFirst(modName) + "]";
                string keyStart = "CurrentLevel=";
                string newLine  = "CurrentLevel=" + level;

                var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.None);
                var sb = new StringBuilder();
                bool inSection = false, found = false;

                foreach (var line in lines)
                {
                    string t = line.Trim();
                    if (t.StartsWith("[")) inSection = t.Equals(section, StringComparison.OrdinalIgnoreCase);
                    if (inSection && t.StartsWith(keyStart)) { sb.AppendLine(newLine); found = true; }
                    else sb.AppendLine(line);
                }

                if (!found)
                {
                    sb.AppendLine(); sb.AppendLine(section);
                    sb.AppendLine("EscalationPath=0,1,2,3");
                    sb.AppendLine("CorrelationsBeforeEscalate=" + _defaultCorrelations);
                    sb.AppendLine(newLine);
                }
                FileManagerModule.WriteFile("Phantombite_GlobalConfig.ini",
                    sb.ToString(), typeof(FileManagerModule));
            }
            catch (Exception ex)
            {
                PBLog.Error(MOD, MDL, "Fehler beim Speichern des Performance Levels", ex);
            }
        }

        // ── Hilfsmethoden ─────────────────────────────────────────────────────

        private int GetNextLevel(string modName, int currentLevel)
        {
            int[] path;
            if (!_modEscalationPath.TryGetValue(modName.ToLower(), out path)) path = DEFAULT_PATH;
            int idx = Array.IndexOf(path, currentLevel);
            if (idx < 0 || idx >= path.Length - 1) return currentLevel;
            return path[idx + 1];
        }

        private int GetCorrelationThreshold(string modName)
        {
            int t;
            return _modCorrelationThreshold.TryGetValue(modName.ToLower(), out t) ? t : _defaultCorrelations;
        }

        private static int[] ParsePath(string pathStr)
        {
            var parts = pathStr.Split(',');
            var result = new List<int>();
            foreach (var p in parts) { int v; if (int.TryParse(p.Trim(), out v)) result.Add(v); }
            return result.Count > 0 ? result.ToArray() : DEFAULT_PATH;
        }

        private static string PadRight(string s, int width)
        {
            if (s == null) s = "";
            return s.Length >= width ? s + " " : s + new string(' ', width - s.Length);
        }

        private static string CapFirst(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return char.ToUpper(s[0]) + s.Substring(1);
        }
    }
}