using CavesAndCaverns.Managers;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace CavesAndCaverns.Carvers
{
    public interface ICarver
    {
        bool[,,] Generate(int chunkSize, BlockPos origin, string biomeTag);
    }

    public class MasterCarver
    {
        private readonly Dictionary<string, ICarver> surfaceCarvers = new();
        private readonly Dictionary<string, ICarver> undergroundCarvers = new();
        private readonly ICoreServerAPI sapi;
        private readonly CaveConnector connector;

        public MasterCarver(ICoreServerAPI sapi)
        {
            this.sapi = sapi;
            this.connector = new CaveConnector(CavesAndCavernsCore.NoiseManager);
            RegisterCarvers();
        }

        private void RegisterCarvers()
        {
            surfaceCarvers["surfaceriver"] = new SurfaceRiverCarver(sapi);
            undergroundCarvers["spaghetti2d"] = new Spaghetti2DCarver(sapi);
            undergroundCarvers["canyon"] = new CanyonCarver(sapi);
            undergroundCarvers["densecave"] = new DenseCaveCarver(sapi);
            undergroundCarvers["megacave"] = new CheeseCarver(sapi);
            undergroundCarvers["thermallake"] = new ThermalLakeCarver(sapi);
            undergroundCarvers["undergroundriver"] = new UndergroundRiverCarver(sapi);
            undergroundCarvers["lavariver"] = new LavaRiverCarver(sapi);
            undergroundCarvers["veingap"] = new VeinGapCarver(sapi);
            undergroundCarvers["pillar"] = new PillarCarver(sapi);
        }

        public bool[,,] CarveSurface(int chunkSize, BlockPos origin, string biomeTag)
        {
            var config = CavesAndCavernsCore.ConfigManager.Config;
            bool[,,] map = new bool[chunkSize, chunkSize, chunkSize];
            foreach (var carver in surfaceCarvers)
            {
                if (carver.Key == "surfaceriver" && !config.EnableSurfaceRivers) continue;

                float probability = config.SurfaceRiverProbability;
                if (sapi.World.Rand.NextDouble() > probability)
                {
                    sapi.Logger.Debug("[CavesAndCaverns] Skipped carver {0} due to probability {1}", carver.Key, probability);
                    continue;
                }

                var carved = carver.Value.Generate(chunkSize, origin, biomeTag);
                sapi.Logger.Notification("[CavesAndCaverns] Carver {0} generated {1} blocks at X:{2}, Z:{3}, Y:{4}", carver.Key, CountCarvedBlocks(carved), origin.X, origin.Z, origin.Y);
                for (int x = 0; x < chunkSize; x++)
                    for (int y = 0; y < chunkSize; y++)
                        for (int z = 0; z < chunkSize; z++)
                            map[x, y, z] |= carved[x, y, z];
            }
            sapi.Logger.Notification("[CavesAndCaverns] Surface carving completed for chunk at X:{0}, Z:{1}, Y:{2} with {3} total carved blocks", origin.X, origin.Z, origin.Y, CountCarvedBlocks(map));
            return map;
        }

        public bool[,,] CarveUnderground(int chunkSize, BlockPos origin, string biomeTag)
        {
            var config = CavesAndCavernsCore.ConfigManager.Config;
            bool[,,] map = new bool[chunkSize, chunkSize, chunkSize];
            bool[,,] riverMap = null;

            foreach (var carver in undergroundCarvers)
            {
                bool shouldContinue = false;
                switch (carver.Key)
                {
                    case "spaghetti2d": if (!config.EnableSpaghetti2D) shouldContinue = true; break;
                    case "canyon": if (!config.EnableCanyons) shouldContinue = true; break;
                    case "densecave": if (!config.EnableDenseCaves) shouldContinue = true; break;
                    case "megacave": if (!config.EnableCheeseCaves) shouldContinue = true; break;
                    case "thermallake": if (!config.EnableThermalLakes) shouldContinue = true; break;
                    case "undergroundriver": if (!config.EnableUndergroundRivers) shouldContinue = true; break;
                    case "lavariver": if (!config.EnableLavaRivers) shouldContinue = true; break;
                    case "veingap": if (!config.EnableVeinGaps) shouldContinue = true; break;
                    case "pillar": if (!config.EnablePillars) shouldContinue = true; break;
                }
                if (shouldContinue)
                {
                    sapi.Logger.Debug("[CavesAndCaverns] Skipped carver {0} due to disabled flag", carver.Key);
                    continue;
                }

                float probability = carver.Key switch
                {
                    "spaghetti2d" => config.SpaghettiProbability,
                    "canyon" => config.CanyonProbability,
                    "densecave" => config.DenseCaveProbability,
                    "megacave" => config.CheeseProbability,
                    "thermallake" => config.ThermalLakeProbability,
                    "undergroundriver" => config.UndergroundRiverProbability,
                    "lavariver" => config.LavaRiverProbability,
                    "veingap" => config.VeinGapProbability,
                    "pillar" => config.PillarProbability,
                    _ => 0f
                };
                if (sapi.World.Rand.NextDouble() > probability)
                {
                    sapi.Logger.Debug("[CavesAndCaverns] Skipped carver {0} due to probability {1}", carver.Key, probability);
                    continue;
                }

                var carved = carver.Value.Generate(chunkSize, origin, biomeTag);
                sapi.Logger.Notification("[CavesAndCaverns] Carver {0} generated {1} blocks at X:{2}, Z:{3}, Y:{4} with noise sample {5}", carver.Key, CountCarvedBlocks(carved), origin.X, origin.Z, origin.Y, GetSampleNoise(carver.Value, origin));
                if (carver.Key == "undergroundriver")
                    riverMap = (bool[,,])carved.Clone();

                for (int x = 0; x < chunkSize; x++)
                    for (int y = 0; y < chunkSize; y++)
                        for (int z = 0; z < chunkSize; z++)
                            if (carver.Key != "pillar" || (riverMap != null && riverMap[x, y, z]))
                                map[x, y, z] |= carved[x, y, z];
            }

            connector.ConnectCaves(map, chunkSize, origin);
            sapi.Logger.Notification("[CavesAndCaverns] Underground carving completed for chunk at X:{0}, Z:{1}, Y:{2} with {3} total carved blocks", origin.X, origin.Z, origin.Y, CountCarvedBlocks(map));
            return map;
        }

        private int CountCarvedBlocks(bool[,,] map)
        {
            int count = 0;
            int chunkSize = map.GetLength(0);
            for (int x = 0; x < chunkSize; x++)
                for (int y = 0; y < chunkSize; y++)
                    for (int z = 0; z < chunkSize; z++)
                        if (map[x, y, z]) count++;
            return count;
        }

        private double GetSampleNoise(ICarver carver, BlockPos origin)
        {
            if (carver is Spaghetti2DCarver)
                return CavesAndCavernsCore.NoiseManager.GetSpaghetti2DNoise(origin.X, origin.Y, origin.Z);
            else if (carver is DenseCaveCarver)
                return CavesAndCavernsCore.NoiseManager.GetDenseCaveNoise(origin.X, origin.Y, origin.Z);
            else if (carver is CheeseCarver)
                return CavesAndCavernsCore.NoiseManager.GetCheeseNoise(origin.X, origin.Y, origin.Z);
            else if (carver is ThermalLakeCarver)
                return CavesAndCavernsCore.NoiseManager.GetThermalLakeNoise(origin.X, origin.Y, origin.Z);
            else if (carver is UndergroundRiverCarver)
                return CavesAndCavernsCore.NoiseManager.GetUndergroundRiverNoise(origin.X, origin.Z);
            else if (carver is LavaRiverCarver)
                return CavesAndCavernsCore.NoiseManager.GetLavaRiverNoise(origin.X, origin.Y, origin.Z);
            else if (carver is VeinGapCarver)
                return CavesAndCavernsCore.NoiseManager.GetVeinGapNoise(origin.X, origin.Y, origin.Z);
            else if (carver is PillarCarver)
                return CavesAndCavernsCore.NoiseManager.GetPillarNoise(origin.X, origin.Y, origin.Z);
            else if (carver is SurfaceRiverCarver)
                return CavesAndCavernsCore.NoiseManager.GetSurfaceRiverNoise(origin.X, origin.Z);
            return 0.0; // Default for unknown carvers
        }
    }

    public class CaveConnector
    {
        private readonly NoiseManager noise;

        public CaveConnector(NoiseManager noise)
        {
            this.noise = noise;
        }

        public void ConnectCaves(bool[,,] map, int chunkSize, BlockPos origin)
        {
            for (int x = 1; x < chunkSize - 1; x++)
                for (int y = 1; y < chunkSize - 1; y++)
                    for (int z = 1; z < chunkSize - 1; z++)
                    {
                        if (map[x, y, z])
                        {
                            for (int dx = -1; dx <= 1; dx++)
                                for (int dy = -1; dy <= 1; dy++)
                                    for (int dz = -1; dz <= 1; dz++)
                                        if (dx != 0 || dy != 0 || dz != 0)
                                            if (map[x + dx, y + dy, z + dz])
                                                map[x + dx / 2, y + dy / 2, z + dz / 2] = true;
                        }
                    }
        }
    }
}