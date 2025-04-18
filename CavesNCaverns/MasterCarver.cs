using CavesAndCaverns.Config;
using CavesAndCaverns.Managers;
using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace CavesAndCaverns.Carvers
{
    public interface ICarver
    {
        void Generate(int chunkSize, BlockPos origin, string biomeTag, IBlockAccessor blockAccessor, BlockChangeBuffer buffer = null);
        void SetRockType(string rockType) { }
    }

    public class BlockChangeBuffer
    {
        private readonly Dictionary<BlockPos, int> blockChanges = new Dictionary<BlockPos, int>();
        private readonly IBlockAccessor blockAccessor;
        private int carvedCount = 0;

        public BlockChangeBuffer(IBlockAccessor blockAccessor)
        {
            this.blockAccessor = blockAccessor;
        }

        public void SetBlock(int blockId, BlockPos pos)
        {
            blockChanges[pos.Copy()] = blockId;
            if (blockId == 0) // Assume 0 is air for carving
                carvedCount++;
        }

        public void Apply()
        {
            foreach (var change in blockChanges)
            {
                blockAccessor.SetBlock(change.Value, change.Key);
                blockAccessor.MarkBlockDirty(change.Key);
            }
            blockChanges.Clear();
        }

        public int GetCarvedCount()
        {
            return carvedCount;
        }
    }

    public class MasterCarver
    {
        private readonly Dictionary<string, ICarver> surfaceCarvers = new();
        private readonly Dictionary<string, ICarver> undergroundCarvers = new();
        private readonly ICoreServerAPI sapi;
        private readonly CaveConnector connector;
        private int carvedCount;

        public MasterCarver(ICoreServerAPI sapi)
        {
            this.sapi = sapi;
            this.connector = new CaveConnector(CavesAndCavernsCore.NoiseManager);
            RegisterCarvers();
            if (CavesAndCavernsCore.ConfigManager.Config.VerboseLogging == true)
                sapi.Logger.Debug("[CavesAndCaverns] MasterCarver initialized with {0} surface and {1} underground carvers", surfaceCarvers.Count, undergroundCarvers.Count);
        }

        private void RegisterCarvers()
        {
            surfaceCarvers["surfaceriver"] = new SurfaceRiverCarver(sapi);
            undergroundCarvers["canyon"] = new CanyonCarver(sapi);
            undergroundCarvers["undergroundriver"] = new UndergroundRiverCarver(sapi);
            undergroundCarvers["lavariver"] = new LavaRiverCarver(sapi);
            undergroundCarvers["pillar"] = new PillarCarver(sapi);
            if (CavesAndCavernsCore.ConfigManager.Config.VerboseLogging == true)
                sapi.Logger.Debug("[CavesAndCaverns] Registered carvers: Surface={0}, Underground={1}", string.Join(",", surfaceCarvers.Keys), string.Join(",", undergroundCarvers.Keys));
        }

        public void CarveSurface(int chunkSize, BlockPos origin, string biomeTag, IBlockAccessor blockAccessor)
        {
            var config = CavesAndCavernsCore.ConfigManager.Config;
            if (!AnySurfaceCarversEnabled(config, biomeTag, origin.Y))
            {
                if (config.VerboseLogging == true)
                    sapi.Logger.Debug("[CavesAndCaverns] Skipping surface carving at X={0}, Z={1}, Y={2} due to no enabled carvers.", origin.X, origin.Z, origin.Y);
                return;
            }

            carvedCount = 0;
            var buffer = new BlockChangeBuffer(blockAccessor);
            if (config.VerboseLogging == true)
                sapi.Logger.Debug("[CavesAndCaverns] Starting CarveSurface at X={0}, Y={1}, Z={2}", origin.X, origin.Y, origin.Z);

            foreach (var carver in surfaceCarvers)
            {
                if (carver.Key == "surfaceriver" && !config.EnableSurfaceRivers)
                {
                    sapi.Logger.Debug("[CavesAndCaverns] Skipped carver {0} due to disabled flag", carver.Key);
                    continue;
                }

                float probability = config.SurfaceRiverProbability;
                if (sapi.World.Rand.NextDouble() > probability)
                {
                    sapi.Logger.Debug("[CavesAndCaverns] Skipped carver {0} due to probability {1}", carver.Key, probability);
                    continue;
                }

                if (config.VerboseLogging == true)
                    sapi.Logger.Debug("[CavesAndCaverns] Executing carver {0} at X={1}, Y={2}, Z={3}", carver.Key, origin.X, origin.Y, origin.Z);
                carver.Value.Generate(chunkSize, origin, biomeTag, blockAccessor, buffer);
            }

            buffer.Apply();
            carvedCount = buffer.GetCarvedCount();
            sapi.Logger.Notification("[CavesAndCaverns] Surface carving completed for chunk at X:{0}, Z:{1}, Y:{2} with {3} total carved blocks", origin.X, origin.Z, origin.Y, carvedCount);
            if (config.VerboseLogging == true)
                sapi.Logger.Debug("[CavesAndCaverns] CarveSurface completed at X={0}, Y={1}, Z={2} with {3} carved blocks", origin.X, origin.Y, origin.Z, carvedCount);
        }

        public void CarveUnderground(int chunkSize, BlockPos origin, string biomeTag, IWorldChunk worldChunk, IBlockAccessor blockAccessor)
        {
            var config = CavesAndCavernsCore.ConfigManager.Config;
            if (!AnyUndergroundCarversEnabled(config, biomeTag, origin.Y))
            {
                if (config.VerboseLogging == true)
                    sapi.Logger.Debug("[CavesAndCaverns] Skipping underground carving at X={0}, Z={1}, Y={2} due to no enabled carvers.", origin.X, origin.Z, origin.Y);
                return;
            }

            carvedCount = 0;
            var buffer = new BlockChangeBuffer(blockAccessor);
            if (config.VerboseLogging == true)
                sapi.Logger.Debug("[CavesAndCaverns] Starting CarveUnderground at X={0}, Y={1}, Z={2}", origin.X, origin.Y, origin.Z);
            int totalSize = chunkSize * chunkSize * chunkSize;
            float[] density = new float[totalSize];
            Array.Fill(density, 1.0f);
            bool[] riverMap = null;
            Random rand = new Random((int)(sapi.World.Seed + origin.X + origin.Z));

            int seaLevel = sapi.World.SeaLevel;
            int worldHeight = sapi.WorldManager.MapSizeY;
            if (config.VerboseLogging == true)
                sapi.Logger.Debug("[CavesAndCaverns] Using seaLevel={0}, worldHeight={1}", seaLevel, worldHeight);

            string rockType = "granite";
            if (worldChunk != null)
            {
                int relativeY = 20;
                if (relativeY >= chunkSize) relativeY = chunkSize - 1;
                int x = 0, z = 0;
                int index = (relativeY * chunkSize + z) * chunkSize + x;
                int blockId = worldChunk.Data.GetBlockId(index, 0);
                var block = sapi.World.BlockAccessor.GetBlock(blockId);
                if (block != null)
                {
                    string blockCode = block.Code.ToString().ToLower();
                    if (blockCode.Contains("granite")) rockType = "granite";
                    else if (blockCode.Contains("andesite")) rockType = "andesite";
                    else if (blockCode.Contains("diorite")) rockType = "diorite";
                    else rockType = "granite";
                    if (config.VerboseLogging == true)
                        sapi.Logger.Debug("[CavesAndCaverns] Determined rockType={0} from blockCode={1}", rockType, blockCode);
                }
            }

            float[] cheeseMap = null, spaghetti2DMap = null, spaghetti3DMap = null, noodleMap = null, entranceMap = null;
            int baseSeed = (int)sapi.World.Seed;
            if (config.EnableCheeseCaves && rand.NextDouble() <= config.CheeseProbability)
            {
                cheeseMap = CavesAndCavernsCore.NoiseManager.GenerateCheeseNoise(chunkSize, origin, baseSeed + 6);
                if (config.VerboseLogging == true)
                    sapi.Logger.Debug("[CavesAndCaverns] Generated cheeseMap for X={0}, Y={1}, Z={2}", origin.X, origin.Y, origin.Z);
            }
            if (config.EnableSpaghetti2D && rand.NextDouble() <= config.SpaghettiProbability)
            {
                spaghetti2DMap = CavesAndCavernsCore.NoiseManager.GenerateSpaghetti2DNoise(chunkSize, origin, baseSeed + 9);
                if (config.VerboseLogging == true)
                    sapi.Logger.Debug("[CavesAndCaverns] Generated spaghetti2DMap for X={0}, Y={1}, Z={2}", origin.X, origin.Y, origin.Z);
            }
            if (config.EnableSpaghetti3D && rand.NextDouble() <= config.Spaghetti3DProbability)
            {
                spaghetti3DMap = CavesAndCavernsCore.NoiseManager.GenerateSpaghetti3DNoise(chunkSize, origin, baseSeed + 14);
                if (config.VerboseLogging == true)
                    sapi.Logger.Debug("[CavesAndCaverns] Generated spaghetti3DMap for X={0}, Y={1}, Z={2}", origin.X, origin.Y, origin.Z);
            }
            if (config.EnableVeinGaps && rand.NextDouble() <= config.VeinGapProbability)
            {
                noodleMap = CavesAndCavernsCore.NoiseManager.GenerateVeinGapNoise(chunkSize, origin, baseSeed + 8);
                if (config.VerboseLogging == true)
                    sapi.Logger.Debug("[CavesAndCaverns] Generated noodleMap for X={0}, Y={1}, Z={2}", origin.X, origin.Y, origin.Z);
            }
            if (config.EnableCaveEntrances)
            {
                entranceMap = CavesAndCavernsCore.NoiseManager.GenerateCaveEntrancesFloat(chunkSize, origin, rand, 0.5f);
                if (config.VerboseLogging == true)
                    sapi.Logger.Debug("[CavesAndCaverns] Generated entranceMap for X={0}, Y={1}, Z={2}", origin.X, origin.Y, origin.Z);
            }

            float[] spagBlend = new float[totalSize];
            Array.Fill(spagBlend, 1.0f);
            if (spaghetti2DMap != null && spaghetti3DMap != null)
            {
                for (int i = 0; i < totalSize; i++)
                    spagBlend[i] = Math.Min(spaghetti2DMap[i] * config.SpaghettiProbability, spaghetti3DMap[i] * config.Spaghetti3DProbability);
                if (config.VerboseLogging == true)
                    sapi.Logger.Debug("[CavesAndCaverns] Blended spaghetti2D and spaghetti3D at X={0}, Y={1}, Z={2}", origin.X, origin.Y, origin.Z);
            }
            else if (spaghetti2DMap != null)
            {
                for (int i = 0; i < totalSize; i++)
                    spagBlend[i] = spaghetti2DMap[i] * config.SpaghettiProbability;
                if (config.VerboseLogging == true)
                    sapi.Logger.Debug("[CavesAndCaverns] Used spaghetti2D only for blend at X={0}, Y={1}, Z={2}", origin.X, origin.Y, origin.Z);
            }
            else if (spaghetti3DMap != null)
            {
                for (int i = 0; i < totalSize; i++)
                    spagBlend[i] = spaghetti3DMap[i] * config.Spaghetti3DProbability;
                if (config.VerboseLogging == true)
                    sapi.Logger.Debug("[CavesAndCaverns] Used spaghetti3D only for blend at X={0}, Y={1}, Z={2}", origin.X, origin.Y, origin.Z);
            }

            float[] cheeseScaled = new float[totalSize];
            Array.Fill(cheeseScaled, 1.0f);
            if (cheeseMap != null)
            {
                for (int x = 0; x < chunkSize; x++)
                    for (int y = 0; y < chunkSize; y++)
                        for (int z = 0; z < chunkSize; z++)
                        {
                            int index = (y * chunkSize + z) * chunkSize + x;
                            float scale = GameMath.Lerp(1.5f, 1.0f, (float)(origin.Y + y + seaLevel) / worldHeight);
                            cheeseScaled[index] = cheeseMap[index] * scale * config.CheeseProbability;
                        }
                if (config.VerboseLogging == true)
                    sapi.Logger.Debug("[CavesAndCaverns] Scaled cheeseMap at X={0}, Y={1}, Z={2}", origin.X, origin.Y, origin.Z);
            }

            float[] noodleCheeseBlend = new float[totalSize];
            Array.Fill(noodleCheeseBlend, 1.0f);
            if (noodleMap != null)
            {
                for (int i = 0; i < totalSize; i++)
                    noodleCheeseBlend[i] = Math.Min(noodleMap[i] * config.VeinGapProbability, cheeseScaled[i]);
                if (config.VerboseLogging == true)
                    sapi.Logger.Debug("[CavesAndCaverns] Blended noodleMap and cheeseScaled at X={0}, Y={1}, Z={2}", origin.X, origin.Y, origin.Z);
            }
            else
            {
                Array.Copy(cheeseScaled, noodleCheeseBlend, totalSize);
            }

            float[] caveBlend = new float[totalSize];
            for (int i = 0; i < totalSize; i++)
                caveBlend[i] = Math.Min(spagBlend[i], noodleCheeseBlend[i]);
            if (config.VerboseLogging == true)
                sapi.Logger.Debug("[CavesAndCaverns] Blended spagBlend and noodleCheeseBlend at X={0}, Y={1}, Z={2}", origin.X, origin.Y, origin.Z);

            if (entranceMap != null)
            {
                for (int x = 0; x < chunkSize; x++)
                    for (int y = 0; y < chunkSize; y++)
                        for (int z = 0; z < chunkSize; z++)
                        {
                            int index = (y * chunkSize + z) * chunkSize + x;
                            float yWeight = Math.Max(0, (float)(origin.Y + y - seaLevel) / (worldHeight - seaLevel));
                            density[index] = caveBlend[index] + Math.Max(0, entranceMap[index] * yWeight);
                        }
                if (config.VerboseLogging == true)
                    sapi.Logger.Debug("[CavesAndCaverns] Blended with entranceMap at X={0}, Y={1}, Z={2}", origin.X, origin.Y, origin.Z);
            }
            else
            {
                Array.Copy(caveBlend, density, totalSize);
            }

            foreach (var carver in undergroundCarvers)
            {
                float probability;
                bool shouldSkip;

                switch (carver.Key)
                {
                    case "canyon":
                        shouldSkip = !config.EnableCanyons;
                        probability = config.CanyonProbability;
                        break;
                    case "undergroundriver":
                        shouldSkip = !config.EnableUndergroundRivers;
                        probability = config.UndergroundRiverProbability;
                        break;
                    case "lavariver":
                        shouldSkip = !config.EnableLavaRivers;
                        probability = config.LavaRiverProbability;
                        break;
                    case "pillar":
                        shouldSkip = !config.EnablePillars;
                        probability = config.PillarProbability;
                        break;
                    default:
                        shouldSkip = true;
                        probability = 0f;
                        break;
                }

                if (shouldSkip || rand.NextDouble() > probability)
                {
                    sapi.Logger.Debug("[CavesAndCaverns] Skipped carver {0} due to {1}", carver.Key, shouldSkip ? "disabled flag" : $"probability {probability}");
                    continue;
                }

                if (carver.Key == "pillar")
                    (carver.Value as PillarCarver)?.SetRockType(rockType);

                if (config.VerboseLogging == true)
                    sapi.Logger.Debug("[CavesAndCaverns] Executing carver {0} at X={1}, Y={2}, Z={3}", carver.Key, origin.X, origin.Y, origin.Z);
                carver.Value.Generate(chunkSize, origin, biomeTag, blockAccessor, buffer);
            }

            if (config.VerboseLogging == true)
                sapi.Logger.Debug("[CavesAndCaverns] Applying density threshold at X={0}, Y={1}, Z={2}", origin.X, origin.Y, origin.Z);
            bool[,,] map = new bool[chunkSize, chunkSize, chunkSize];
            for (int x = 0; x < chunkSize; x++)
                for (int y = 0; y < chunkSize; y++)
                    for (int z = 0; z < chunkSize; z++)
                    {
                        int index = (y * chunkSize + z) * chunkSize + x;
                        map[x, y, z] = density[index] < 0.0f;
                        if (map[x, y, z])
                        {
                            BlockPos pos = new BlockPos(origin.X + x, origin.Y + y, origin.Z + z);
                            if (config.DebugGlassCaves)
                                buffer.SetBlock(GlassBlockManager.GetDebugGlassBlockId(blockAccessor, "caves"), pos);
                            else
                                buffer.SetBlock(0, pos);
                        }
                    }

            if (config.VerboseLogging == true)
                sapi.Logger.Debug("[CavesAndCaverns] Connecting caves at X={0}, Y={1}, Z={2}", origin.X, origin.Y, origin.Z);
            connector.ConnectCaves(map, chunkSize, origin, blockAccessor, buffer);

            buffer.Apply();
            carvedCount = buffer.GetCarvedCount();
            sapi.Logger.Notification("[CavesAndCaverns] Underground carving completed for chunk at X:{0}, Z:{1}, Y:{2} with {3} total carved blocks", origin.X, origin.Z, origin.Y, carvedCount);
            if (config.VerboseLogging == true)
                sapi.Logger.Debug("[CavesAndCaverns] CarveUnderground completed at X={0}, Y={1}, Z={2} with {3} carved blocks", origin.X, origin.Y, origin.Z, carvedCount);
        }

        private bool AnySurfaceCarversEnabled(CavesConfig config, string biomeTag, int y)
        {
            Random rand = new Random();
            if (!config.EnableSurfaceRivers || rand.NextDouble() > config.SurfaceRiverProbability)
                return false;
            return y >= sapi.World.SeaLevel - 20 && y <= sapi.World.SeaLevel + 20;
        }

        private bool AnyUndergroundCarversEnabled(CavesConfig config, string biomeTag, int y)
        {
            Random rand = new Random();
            return (config.EnableCheeseCaves && rand.NextDouble() <= config.CheeseProbability) ||
                   (config.EnableSpaghetti2D && rand.NextDouble() <= config.SpaghettiProbability) ||
                   (config.EnableSpaghetti3D && rand.NextDouble() <= config.Spaghetti3DProbability) ||
                   (config.EnableVeinGaps && rand.NextDouble() <= config.VeinGapProbability) ||
                   (config.EnableCaveEntrances && rand.NextDouble() <= 0.5f && y >= sapi.World.SeaLevel - 50 && y <= sapi.World.SeaLevel + 50) ||
                   (config.EnableCanyons && rand.NextDouble() <= config.CanyonProbability) ||
                   (config.EnableUndergroundRivers && rand.NextDouble() <= config.UndergroundRiverProbability) ||
                   (config.EnableLavaRivers && rand.NextDouble() <= config.LavaRiverProbability && y <= sapi.World.SeaLevel - 50) ||
                   (config.EnablePillars && rand.NextDouble() <= config.PillarProbability);
        }

        private double GetNoiseValue(string carverKey, BlockPos origin)
        {
            switch (carverKey)
            {
                case "spaghetti2d": return CavesAndCavernsCore.NoiseManager.GetSpaghetti2DNoise(origin.X, origin.Y, origin.Z);
                case "spaghetti3d": return CavesAndCavernsCore.NoiseManager.GetSpaghetti3DNoise(origin.X, origin.Y, origin.Z);
                case "canyon": return CavesAndCavernsCore.NoiseManager.GetCanyonNoise(origin.X, origin.Y, origin.Z);
                case "densecave": return CavesAndCavernsCore.NoiseManager.GetDenseCaveNoise(origin.X, origin.Y, origin.Z);
                case "megacave": return CavesAndCavernsCore.NoiseManager.GetCheeseNoise(origin.X, origin.Y, origin.Z);
                case "thermallake": return CavesAndCavernsCore.NoiseManager.GetThermalLakeNoise(origin.X, origin.Y, origin.Z);
                case "undergroundriver": return CavesAndCavernsCore.NoiseManager.GetUndergroundRiverNoise(origin.X, origin.Z);
                case "lavariver": return CavesAndCavernsCore.NoiseManager.GetLavaRiverNoise(origin.X, origin.Y, origin.Z);
                case "veingap": return CavesAndCavernsCore.NoiseManager.GetVeinGapNoise(origin.X, origin.Y, origin.Z);
                case "pillar": return CavesAndCavernsCore.NoiseManager.GetPillarNoise(origin.X, origin.Y, origin.Z);
                default: return 0;
            }
        }

        public int GetCarvedCount()
        {
            return carvedCount;
        }
    }

    public class CaveConnector
    {
        private readonly NoiseManager noise;

        public CaveConnector(NoiseManager noise)
        {
            this.noise = noise;
        }

        public void ConnectCaves(bool[,,] map, int chunkSize, BlockPos origin, IBlockAccessor blockAccessor, BlockChangeBuffer buffer)
        {
            var config = CavesAndCavernsCore.ConfigManager.Config;
            if (config.VerboseLogging == true)
                CavesAndCavernsCore.ServerAPI.Logger.Debug("[CavesAndCaverns] Starting cave connection at X={0}, Y={1}, Z={2}", origin.X, origin.Y, origin.Z);

            double[,,] noiseMap = new double[chunkSize, chunkSize, chunkSize];
            for (int x = 0; x < chunkSize; x++)
                for (int y = 0; y < chunkSize; y++)
                    for (int z = 0; z < chunkSize; z++)
                        noiseMap[x, y, z] = noise.GetCheeseNoise(
                            (int)((origin.X + x) * 0.1),
                            (int)((origin.Y + y) * 0.1),
                            (int)((origin.Z + z) * 0.1)
                        );

            for (int x = 1; x < chunkSize - 1; x++)
                for (int y = 1; y < chunkSize - 1; y++)
                    for (int z = 1; z < chunkSize - 1; z++)
                        if (map[x, y, z])
                            for (int dx = -1; dx <= 1; dx++)
                                for (int dy = -1; dy <= 1; dy++)
                                    for (int dz = -1; dz <= 1; dz++)
                                    {
                                        if (dx == 0 && dy == 0 && dz == 0) continue;
                                        int nx = x + dx, ny = y + dy, nz = z + dz;
                                        if (nx < 0 || nx >= chunkSize || ny < 0 || ny >= chunkSize || nz < 0 || nz >= chunkSize) continue;

                                        if (map[nx, ny, nz])
                                        {
                                            int steps = Math.Max(Math.Max(Math.Abs(dx), Math.Abs(dy)), Math.Abs(dz));
                                            int stepCount = Math.Min(steps, 2);
                                            for (int step = 1; step < stepCount; step++)
                                            {
                                                int cx = x + (dx * step) / steps;
                                                int cy = y + (dy * step) / steps;
                                                int cz = z + (dz * step) / steps;
                                                double noiseValue = noiseMap[cx, cy, cz];
                                                if (noiseValue > 0.8)
                                                {
                                                    map[cx, cy, cz] = true;
                                                    BlockPos pos = new BlockPos(origin.X + cx, origin.Y + cy, origin.Z + cz);
                                                    if (config.DebugGlassCaves)
                                                        buffer.SetBlock(GlassBlockManager.GetDebugGlassBlockId(blockAccessor, "caves"), pos);
                                                    else
                                                        buffer.SetBlock(0, pos);
                                                    if (config.VerboseLogging == true)
                                                        CavesAndCavernsCore.ServerAPI.Logger.Debug("[CavesAndCaverns] Connected cave at X={0}, Y={1}, Z={2}", origin.X + cx, origin.Y + cy, origin.Z + cz);
                                                }
                                            }
                                        }
                                    }
            if (config.VerboseLogging == true)
                CavesAndCavernsCore.ServerAPI.Logger.Debug("[CavesAndCaverns] Cave connection completed at X={0}, Y={1}, Z={2}", origin.X, origin.Y, origin.Z);
        }
    }

    public static class GlassBlockManager
    {
        public static void PlaceDebugGlass(IBlockAccessor blockAccessor, BlockPos pos, string type)
        {
            // Placeholder: Assume a method to get debug glass block ID based on type
            int glassBlockId = blockAccessor.GetBlock(new AssetLocation("game:glass")).BlockId;
            blockAccessor.SetBlock(glassBlockId, pos);
        }

        public static int GetDebugGlassBlockId(IBlockAccessor blockAccessor, string type)
        {
            // Placeholder: Return the block ID for debug glass
            return blockAccessor.GetBlock(new AssetLocation("game:glass")).BlockId;
        }
    }
}