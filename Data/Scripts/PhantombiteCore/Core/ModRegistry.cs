namespace PhantombiteCore.Core
{
    public static class ModRegistry
    {
        // ── PhantomBite Workshop IDs ─────────────────────────────────────────
        public const ulong Core               = 3710485076UL;
        public const ulong AdminProjektor     = 3706769805UL;
        public const ulong Artefact           = 3689668016UL;
        public const ulong AutoTransfer       = 3693780953UL;
        public const ulong CableWinch         = 3689668160UL;
        public const ulong Creatures          = 3691346493UL;
        public const ulong Economy            = 3728099479UL;
        public const ulong Encounter          = 3689684015UL;
        public const ulong Mining             = 3719998525UL;
        public const ulong Pandora            = 3723475424UL;
        public const ulong PlanetSpawner      = 3723481681UL;
        public const ulong ServerAddon        = 3689667750UL;
        public const ulong StationRefill      = 3723483728UL;
        public const ulong Sulvax             = 3691347867UL;
        public const ulong SulvaxRespawnRover = 3692354958UL;
        public const ulong WaterElectrolyzer  = 3708390650UL;

        // ── Externe Abhängigkeiten ───────────────────────────────────────────
        public const ulong MES = 1521905890UL;

        // ── PhantomBite Local Names ──────────────────────────────────────────
        public const string LocalCore               = "Phantombite_Core";
        public const string LocalAdminProjektor     = "Phantombite_AdminProjektor";
        public const string LocalArtefact           = "Phantombite_Artefact";
        public const string LocalAutoTransfer       = "Phantombite_AutoTransfer";
        public const string LocalCableWinch         = "Phantombite_Cable_Winch";
        public const string LocalCreatures          = "Phantombite_Creatures";
        public const string LocalEconomy            = "Phantombite_Economy";
        public const string LocalEncounter          = "Phantombite_Encounter";
        public const string LocalMining             = "Phantombite_Mining";
        public const string LocalPandora            = "Phantombite_Pandora";
        public const string LocalPlanetSpawner      = "Phantombite_PlanetSpawner";
        public const string LocalServerAddon        = "Phantombite_Server_Addon";
        public const string LocalStationRefill      = "Phantombite_StationRefill";
        public const string LocalSulvax             = "Phantombite_Sulvax";
        public const string LocalSulvaxRespawnRover = "Phantombite_Sulvax_RespawnRover";
        public const string LocalWaterElectrolyzer  = "Phantombite_WaterElectrolyzer";

        // ── Messaging Kanäle ─────────────────────────────────────────────────
        public const long ChannelCore               = 1995000L;
        public const long ChannelArtefact           = 1995001L;
        public const long ChannelCableWinch         = 1995002L;
        public const long ChannelCreatures          = 1995003L;
        public const long ChannelEconomy            = 1995004L;
        public const long ChannelEncounter          = 1995005L;
        public const long ChannelServerAddon        = 1995006L;
        public const long ChannelSulvax             = 1995007L;
        public const long ChannelSulvaxRespawnRover = 1995008L;
        public const long ChannelAutoTransfer       = 1995009L;
        public const long ChannelPlanetSpawner      = 1995010L;
        public const long ChannelAdminProjektor     = 1995011L;
        public const long ChannelWaterElectrolyzer  = 1995012L;
        public const long ChannelMining             = 1995013L;
        public const long ChannelPandora            = 1995014L;
        public const long ChannelStationRefill      = 1995016L;
        public const long ChannelLog                = 1995999L;

        // ── MES-Abhängigkeiten ───────────────────────────────────────────────
        // Creatures nutzt kein MES mehr — eigenes Spawn-System in Entwicklung
        public static readonly ulong[] RequiresMES = { Encounter };

        // ── Alle bekannten PB-IDs (für ModDetector) ──────────────────────────
        public static readonly ulong[] AllPbIds =
        {
            AdminProjektor, Artefact, AutoTransfer, CableWinch,
            Creatures, Economy, Encounter, Mining, Pandora,
            PlanetSpawner, ServerAddon, StationRefill,
            Sulvax, SulvaxRespawnRover, WaterElectrolyzer
        };

        // ── Hilfsmethoden ────────────────────────────────────────────────────

        public static string GetLocalName(ulong modId)
        {
            if (modId == Core)               return LocalCore;
            if (modId == AdminProjektor)     return LocalAdminProjektor;
            if (modId == Artefact)           return LocalArtefact;
            if (modId == AutoTransfer)       return LocalAutoTransfer;
            if (modId == CableWinch)         return LocalCableWinch;
            if (modId == Creatures)          return LocalCreatures;
            if (modId == Economy)            return LocalEconomy;
            if (modId == Encounter)          return LocalEncounter;
            if (modId == Mining)             return LocalMining;
            if (modId == Pandora)            return LocalPandora;
            if (modId == PlanetSpawner)      return LocalPlanetSpawner;
            if (modId == ServerAddon)        return LocalServerAddon;
            if (modId == StationRefill)      return LocalStationRefill;
            if (modId == Sulvax)             return LocalSulvax;
            if (modId == SulvaxRespawnRover) return LocalSulvaxRespawnRover;
            if (modId == WaterElectrolyzer)  return LocalWaterElectrolyzer;
            return null;
        }

        public static string GetName(ulong modId)
        {
            if (modId == Core)               return "Core";
            if (modId == AdminProjektor)     return "AdminProjektor";
            if (modId == Artefact)           return "Artefact";
            if (modId == AutoTransfer)       return "AutoTransfer";
            if (modId == CableWinch)         return "CableWinch";
            if (modId == Creatures)          return "Creatures";
            if (modId == Economy)            return "Economy";
            if (modId == Encounter)          return "Encounter";
            if (modId == Mining)             return "Mining";
            if (modId == Pandora)            return "Pandora";
            if (modId == PlanetSpawner)      return "PlanetSpawner";
            if (modId == ServerAddon)        return "ServerAddon";
            if (modId == StationRefill)      return "StationRefill";
            if (modId == Sulvax)             return "Sulvax";
            if (modId == SulvaxRespawnRover) return "SulvaxRespawnRover";
            if (modId == WaterElectrolyzer)  return "WaterElectrolyzer";
            if (modId == MES)                return "MES";
            return "Unknown";
        }

        public static bool IsPhantomBiteMod(ulong modId)
        {
            foreach (var id in AllPbIds)
                if (id == modId) return true;
            return modId == Core;
        }
    }
}