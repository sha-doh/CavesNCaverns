using CavesAndCaverns.Carvers;
using CavesAndCaverns.Managers;
using System;
using System.Collections.Generic;
using System.Threading;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace CavesAndCaverns
{
    public class CaveMapPrecalculator
    {
        private ICoreServerAPI sapi;
        private MasterCarver masterCarver;
        private NoiseManager noiseManager;
        public Dictionary<string, bool[,,]> PrecomputedCaveMaps { get; private set; } = new Dictionary<string, bool[,,]>();

        public CaveMapPrecalculator(ICoreServerAPI sapi, MasterCarver masterCarver, NoiseManager noiseManager)
        {
            this.sapi = sapi;
            this.masterCarver = masterCarver;
            this.noiseManager = noiseManager;
            Thread precalcThread = new Thread(PrecomputeCaveMaps);
            precalcThread.IsBackground = true;
            precalcThread.Start();
        }

        private void PrecomputeCaveMaps()
        {
            try
            {
                // Wait for NoiseManager to be ready
                while (!noiseManager.IsInitialized)
                {
                    Thread.Sleep(100);
                    sapi.Logger.Debug("[CavesAndCaverns] Waiting for NoiseManager to initialize...");
                }

                int spawnChunkX = (int)(sapi.WorldManager.MapSizeX / 2.0 / 32);
                int spawnChunkZ = (int)(sapi.WorldManager.MapSizeZ / 2.0 / 32);
                int chunkSize = 32;

                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        int chunkX = spawnChunkX + dx;
                        int chunkZ = spawnChunkZ + dz;
                        string key = $"{chunkX},{chunkZ}";
                        BlockPos origin = new BlockPos(chunkX * chunkSize, 0, chunkZ * chunkSize);
                        bool[,,] surfaceMap = masterCarver.CarveSurface(chunkSize, origin, "defaultBiome");
                        bool[,,] undergroundMap = masterCarver.CarveUnderground(chunkSize, origin, "defaultBiome");
                        bool[,,] caveMap = new bool[chunkSize, chunkSize, chunkSize];

                        for (int x = 0; x < chunkSize; x++)
                            for (int y = 0; y < chunkSize; y++)
                                for (int z = 0; z < chunkSize; z++)
                                    caveMap[x, y, z] = surfaceMap[x, y, z] || undergroundMap[x, y, z];

                        lock (PrecomputedCaveMaps)
                        {
                            PrecomputedCaveMaps[key] = caveMap;
                        }
                        sapi.Logger.Notification("[CavesAndCaverns] Precomputed cave map for chunk X={0}, Z={1}", chunkX, chunkZ);
                    }
                }
            }
            catch (Exception ex)
            {
                sapi.Logger.Error("[CavesAndCaverns] Precomputation failed: {0}", ex);
            }
        }
    }
}