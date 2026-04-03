namespace PhantombiteCore.Core
{
    public static class ModRegistry
    {
        // ── PhantomBite Workshop IDs ─────────────────────────────────────────
        public const ulong Core               = 3689625814UL;
        public const ulong Artefact           = 3689668016UL;
        public const ulong CableWinch         = 3689668160UL;
        public const ulong Creatures          = 3691346493UL;
        public const ulong Economy            = 3689686739UL;
        public const ulong Encounter          = 3689684015UL;
        public const ulong ServerAddon        = 3689667750UL;
        public const ulong Sulvax             = 3691347867UL;
        public const ulong SulvaxRespawnRover = 3692354958UL;
        public const ulong AutoTransfer       = 3693780953UL;

        // ── Externe Abhängigkeiten ───────────────────────────────────────────
        public const ulong MES = 1521905890UL;

        // ── PhantomBite Local Names ──────────────────────────────────────────
        public const string LocalCore               = "Phantombite_Core";
        public const string LocalArtefact           = "Phantombite_Artefact";
        public const string LocalCableWinch         = "Phantombite_CableWinch";
        public const string LocalCreatures          = "Phantombite_Creatures";
        public const string LocalEconomy            = "Phantombite_Economy";
        public const string LocalEncounter          = "Phantombite_Encounter";
        public const string LocalServerAddon        = "Phantombite_Server_Addon";
        public const string LocalSulvax             = "Phantombite_Sulvax";
        public const string LocalSulvaxRespawnRover = "Phantombite_Sulvax_RespawnRover";
        public const string LocalAutoTransfer       = "Phantombite_AutoTransfer";

        // ── Messaging Kanäle ─────────────────────────────────────────────────
        // Mods schicken ihre Registrierung an Core (1995000)
        // Core schickt Commands an den jeweiligen Mod-Kanal
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
        public const long ChannelLog                = 1995999L; // Alle Mods → Core Log

        // ── Welche PB Mods brauchen MES ─────────────────────────────────────
        public static readonly ulong[] RequiresMES = { Encounter, Creatures };

        public static string GetLocalName(ulong modId)
        {
            if (modId == Core)               return LocalCore;
            if (modId == Artefact)           return LocalArtefact;
            if (modId == CableWinch)         return LocalCableWinch;
            if (modId == Creatures)          return LocalCreatures;
            if (modId == Economy)            return LocalEconomy;
            if (modId == Encounter)          return LocalEncounter;
            if (modId == ServerAddon)        return LocalServerAddon;
            if (modId == Sulvax)             return LocalSulvax;
            if (modId == SulvaxRespawnRover) return LocalSulvaxRespawnRover;
            if (modId == AutoTransfer)       return LocalAutoTransfer;
            return null;
        }

        public static string GetName(ulong modId)
        {
            if (modId == Core)               return "Core";
            if (modId == Artefact)           return "Artefact";
            if (modId == CableWinch)         return "CableWinch";
            if (modId == Creatures)          return "Creatures";
            if (modId == Economy)            return "Economy";
            if (modId == Encounter)          return "Encounter";
            if (modId == ServerAddon)        return "ServerAddon";
            if (modId == Sulvax)             return "Sulvax";
            if (modId == SulvaxRespawnRover) return "SulvaxRespawnRover";
            if (modId == AutoTransfer)       return "AutoTransfer";
            if (modId == MES)                return "MES";
            return "Unknown";
        }

        public static bool IsPhantomBiteMod(ulong modId)
        {
            return modId == Core
                || modId == Artefact
                || modId == CableWinch
                || modId == Creatures
                || modId == Economy
                || modId == Encounter
                || modId == ServerAddon
                || modId == Sulvax
                || modId == SulvaxRespawnRover
                || modId == AutoTransfer;
        }
    }
}