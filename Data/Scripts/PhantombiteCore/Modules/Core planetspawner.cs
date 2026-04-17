using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;
using PhantombiteCore.Core;

namespace PhantombiteCore.Modules
{
    /// <summary>
    /// Core_PlanetSpawner - Sulvax Planet Spawner
    /// Only registered when Phantombite_Sulvax mod is active.
    /// Ensures the Sulvax planet exists at the correct position on the server.
    /// </summary>
    public class PlanetSpawnerModule : IModule
    {
        public string ModuleName { get { return "Core_PlanetSpawner"; } }

        private const string STORAGE_NAME = "Sulvax-12345d120000";
        private const string PLANET_NAME  = "Sulvax";
        private const string SUBTYPE_ID   = "Sulvax";
        private const int    SEED         = 12345;
        private const float  DIAMETER     = 120000f;
        private static readonly Vector3D POSITION_CENTER = new Vector3D(578292.44, 87234.60, 3629005.91); 

        public void Init()    { }
        public void Update()  { }
        public void SaveData(){ }
        public void Close()   { }

        /// <summary>
        /// Called by Session.BeforeStart() after ModDetector confirms Sulvax mod is active.
        /// Server-only.
        /// </summary>
        public void CheckAndSpawn()
        {
            if (!MyAPIGateway.Session.IsServer) return;

            Log("=======================================");
            Log("  Sulvax Planet Spawner - Check");
            Log("=======================================");
            Log("  Suche nach StorageName : " + STORAGE_NAME);
            Log("  Suche nach Name        : " + PLANET_NAME);

            var voxelMaps = new List<IMyVoxelBase>();
            MyAPIGateway.Session.VoxelMaps.GetInstances(voxelMaps);

            foreach (var voxel in voxelMaps)
            {
                bool matchStorage = voxel.StorageName == STORAGE_NAME;
                bool matchName    = voxel.StorageName.StartsWith(PLANET_NAME);

                if (matchStorage || matchName)
                {
                    Log("  GEFUNDEN: "  + voxel.StorageName);
                    Log("  EntityId : " + voxel.EntityId);
                    Log("  Position : " + voxel.GetPosition());
                    Log("  Kein Eingriff noetig.");
                    Log("=======================================");
                    return;
                }
            }

            Log("  Planet NICHT gefunden - starte Spawn...");
            SpawnSulvax();
        }

        private void SpawnSulvax()
        {
            Log("  Spawne " + SUBTYPE_ID + "...");
            Log("  Seed     : " + SEED);
            Log("  Diameter : " + DIAMETER + "m");
            Log("  Position : " + POSITION_CENTER);

            Vector3D spawnPos = POSITION_CENTER - new Vector3D(DIAMETER / 2.0);

            IMyVoxelBase planet = MyAPIGateway.Session.VoxelMaps.SpawnPlanet(
                SUBTYPE_ID,
                DIAMETER,
                SEED,
                spawnPos
            );

            if (planet != null)
            {
                Log("  Planet erfolgreich gespawnt!");
                Log("  EntityId    : " + planet.EntityId);
                Log("  StorageName : " + planet.StorageName);
                Log("  Position    : " + planet.GetPosition());
            }
            else
            {
                Log("  FEHLER: SpawnPlanet hat null zurueckgegeben!");
                Log("  Ist Sulvax.sbc korrekt im Mod vorhanden?");
            }

            Log("=======================================");
        }

        private void Log(string message)
        {
            MyLog.Default.WriteLineAndConsole("[PhantombiteCore|Core_PlanetSpawner] " + message);
        }
    }
}