using CavesAndCaverns;
using CavesAndCaverns.Config;
using CavesAndCaverns.Managers;
using System;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace CavesAndCaverns.Carvers
{
    public class CanyonCarver : ICarver
    {
        private readonly ICoreServerAPI sapi;
        private readonly CavesConfig config;
        private readonly NoiseManager noiseManager;

        public CanyonCarver(ICoreServerAPI sapi)
        {
            this.sapi = sapi;
            this.config = CavesAndCavernsCore.ConfigManager.Config;
            this.noiseManager = CavesAndCavernsCore.NoiseManager;
        }

        public void Generate(int chunkSize, BlockPos origin, string biomeTag, IBlockAccessor blockAccessor)
        {
            double xzScalePath = 0.5;
            double yScalePath = 0.5;
            double xzScaleVariation = 0.05;
            double yScaleVariation = 0.05;
            int seed = (int)(sapi.World.Seed + 3);

            float[] noiseMapPath = noiseManager.Generate3DNoise(noiseManager.GetCanyonNoiseGenerator(), chunkSize, origin, xzScalePath, yScalePath, seed, "Canyon");
            float[] noiseMapVariation = noiseManager.Generate3DNoise(noiseManager.GetCanyonRotationNoiseGenerator(), chunkSize, origin, xzScaleVariation, yScaleVariation, seed + 2, "CanyonRotation");

            Random rand = new Random((int)(sapi.World.Seed + origin.X + origin.Z));
            int startY = rand.Next(20, 60);
            int minY = 0;

            bool[] yInRange = PrecomputeYRange(chunkSize, origin, minY, startY);

            for (int x = 0; x < chunkSize; x++)
            {
                for (int y = 0; y < chunkSize; y++)
                {
                    if (!yInRange[y]) continue;

                    for (int z = 0; z < chunkSize; z++)
                    {
                        int index = (y * chunkSize + z) * chunkSize + x;
                        float pathNoise = noiseMapPath[index] + 0.5f;
                        float variation = noiseMapVariation[index] * 0.5f;

                        double threshold = 0.8;
                        if (pathNoise > threshold)
                        {
                            double ravineWidth = 5.0 + variation;
                            double dx = (x - chunkSize / 2.0) / (double)chunkSize;
                            double dz = (z - chunkSize / 2.0) / (double)chunkSize;
                            double distanceFromPath = Math.Sqrt(dx * dx + dz * dz) * chunkSize;

                            if (distanceFromPath < ravineWidth / 2.0)
                            {
                                BlockPos pos = new BlockPos(origin.X + x, origin.Y + y, origin.Z + z);
                                if (config.DebugGlassCaves)
                                    GlassBlockManager.PlaceDebugGlass(blockAccessor, pos, "caves");
                                else
                                    blockAccessor.SetBlock(0, pos);
                            }
                        }
                    }
                }
            }
        }

        private bool[] PrecomputeYRange(int chunkSize, BlockPos origin, int minY, int startY)
        {
            bool[] yInRange = new bool[chunkSize];
            for (int y = 0; y < chunkSize; y++)
            {
                double worldY = origin.Y + y;
                yInRange[y] = worldY >= minY && worldY <= startY;
            }
            return yInRange;
        }
    }
}