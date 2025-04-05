using CavesAndCaverns.Config;
using CavesAndCaverns.Managers;
using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace CavesAndCaverns.Carvers
{
    public class UndergroundRiverCarver : ICarver
    {
        private readonly ICoreServerAPI sapi;
        private readonly CavesConfig config;
        private readonly NoiseManager noiseManager;

        public UndergroundRiverCarver(ICoreServerAPI sapi)
        {
            this.sapi = sapi;
            this.config = CavesAndCavernsCore.ConfigManager.Config;
            this.noiseManager = CavesAndCavernsCore.NoiseManager;
        }

        public bool[,,] Generate(int chunkSize, BlockPos origin, string biomeTag)
        {
            bool[,,] map = new bool[chunkSize, chunkSize, chunkSize];
            float wetMultiplier = biomeTag == "wet" ? config.WetBiomeRiverChanceMultiplier : 1.0f;

            List<RiverNode> sources = FindRiverSources(chunkSize, origin, wetMultiplier);
            foreach (var source in sources)
                CarveTunnelPath(map, chunkSize, origin, source);

            return map;
        }

        private List<RiverNode> FindRiverSources(int chunkSize, BlockPos origin, float wetMultiplier)
        {
            List<RiverNode> sources = new List<RiverNode>();
            for (int x = 0; x < chunkSize; x++)
                for (int z = 0; z < chunkSize; z++)
                {
                    double noiseValue = noiseManager.GetUndergroundRiverNoise(origin.X + x, origin.Z + z);
                    if (noiseValue * wetMultiplier > 0.1) // Lowered from 0.6 to 0.1
                    {
                        int surfaceY = sapi.World.BlockAccessor.GetTerrainMapheightAt(new BlockPos(origin.X + x, 0, origin.Z + z));
                        int tunnelY = Math.Min(surfaceY - 10, sapi.World.SeaLevel + 20); // Y=48 equivalent
                        if (surfaceY > sapi.World.SeaLevel + 30) // Under mountains
                        {
                            // Adjust tunnelY to be within the chunk's y-range
                            int localY = tunnelY - origin.Y;
                            if (localY >= 0 && localY < chunkSize)
                            {
                                sources.Add(new RiverNode { X = x, Y = localY, Z = z, Flow = (float)noiseValue });
                            }
                        }
                    }
                }
            return sources;
        }

        private void CarveTunnelPath(bool[,,] map, int chunkSize, BlockPos origin, RiverNode start)
        {
            RiverNode current = start;
            HashSet<(int, int, int)> carved = new HashSet<(int, int, int)>();
            int maxSteps = 100;

            while (maxSteps-- > 0)
            {
                int surfaceY = sapi.World.BlockAccessor.GetTerrainMapheightAt(new BlockPos(origin.X + current.X, 0, origin.Z + current.Z));
                current.Y = GameMath.Clamp(current.Y, 0, chunkSize - 1); // Ensure within chunk bounds

                int width = (int)(config.RiverWidthMin + (config.RiverWidthMax - config.RiverWidthMin) * current.Flow);
                int height = 3; // Tectonic-like tunnel height
                CarveTunnelSection(map, chunkSize, origin, current, width, height, carved);

                Block block = sapi.World.BlockAccessor.GetBlock(origin.X + current.X, origin.Y + current.Y, origin.Z + current.Z);
                if (block.BlockMaterial == EnumBlockMaterial.Liquid) break;

                RiverNode next = GetNextNode(current, chunkSize, origin);
                if (next == null) break;
                current = next;
            }
        }

        private void CarveTunnelSection(bool[,,] map, int chunkSize, BlockPos origin, RiverNode node, int width, int height, HashSet<(int, int, int)> carved)
        {
            for (int dx = -width; dx <= width; dx++)
                for (int dy = 0; dy < height; dy++)
                    for (int dz = -width; dz <= width; dz++)
                        if (dx * dx + dz * dz <= width * width)
                        {
                            int newX = node.X + dx;
                            int newY = node.Y + dy;
                            int newZ = node.Z + dz;
                            if (newX >= 0 && newX < chunkSize && newY >= 0 && newY < chunkSize && newZ >= 0 && newZ < chunkSize)
                                if (!carved.Contains((newX, newY, newZ)))
                                {
                                    map[newX, newY, newZ] = true;
                                    carved.Add((newX, newY, newZ));
                                    if (dy == 0) FluidRegistry.MarkFluid(origin.X + newX, origin.Y + newY, origin.Z + newZ, "water");
                                }
                        }
        }

        private RiverNode GetNextNode(RiverNode current, int chunkSize, BlockPos origin)
        {
            RiverNode next = null;
            double lowestNoise = double.MaxValue;
            for (int dx = -1; dx <= 1; dx++)
                for (int dz = -1; dz <= 1; dz++)
                {
                    if (dx == 0 && dz == 0) continue;
                    int newX = current.X + dx;
                    int newZ = current.Z + dz;
                    if (newX >= 0 && newX < chunkSize && newZ >= 0 && newZ < chunkSize)
                    {
                        double noise = noiseManager.GetUndergroundRiverNoise(origin.X + newX, origin.Z + newZ);
                        int surfaceY = sapi.World.BlockAccessor.GetTerrainMapheightAt(new BlockPos(origin.X + newX, 0, origin.Z + newZ));
                        int newY = GameMath.Clamp(current.Y - 1, 0, chunkSize - 1);
                        if (noise < lowestNoise && surfaceY > sapi.World.SeaLevel + 30) // Under mountains
                        {
                            lowestNoise = noise;
                            next = new RiverNode { X = newX, Y = newY, Z = newZ, Flow = current.Flow };
                        }
                    }
                }
            return next;
        }
    }

    public class RiverNode
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Z { get; set; }
        public float Flow { get; set; }
    }
}