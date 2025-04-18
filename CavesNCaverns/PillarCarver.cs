﻿using CavesAndCaverns;
using CavesAndCaverns.Config;
using CavesAndCaverns.Managers;
using System;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace CavesAndCaverns.Carvers
{
    public class PillarCarver : ICarver
    {
        private readonly ICoreServerAPI sapi;
        private readonly CavesConfig config;
        private readonly NoiseManager noiseManager;
        private string rockType;

        public PillarCarver(ICoreServerAPI sapi)
        {
            this.sapi = sapi;
            this.config = CavesAndCavernsCore.ConfigManager.Config;
            this.noiseManager = CavesAndCavernsCore.NoiseManager;
        }

        public void SetRockType(string rockType)
        {
            this.rockType = rockType.ToLower();
        }

        public void Generate(int chunkSize, BlockPos origin, string biomeTag, IBlockAccessor blockAccessor)
        {
            int seed = (int)sapi.World.Seed;
            float[] noiseMap1 = noiseManager.Generate3DNoise(noiseManager.GetPillarNoiseGenerator(), chunkSize, origin, 15.0, 0.3, seed + 3, "Pillar");
            float[] noiseMap2 = noiseManager.Generate3DNoise(noiseManager.GetPillarNoiseGenerator(), chunkSize, origin, 15.0, 0.3, seed + 4, "Pillar");
            float[] noiseMapVariation = noiseManager.Generate3DNoise(noiseManager.GetPillarNoiseGenerator(), chunkSize, origin, 0.05, 0.05, seed + 5, "Pillar");

            double[] yModifiers = PrecomputeYModifiers(chunkSize, origin);

            for (int x = 0; x < chunkSize; x++)
            {
                for (int y = 0; y < chunkSize; y++)
                {
                    if (origin.Y + y >= 16 && (origin.Y + y < 32 || origin.Y + y >= 64) || yModifiers[y] < 0.1) continue;

                    for (int z = 0; z < chunkSize; z++)
                    {
                        int index = (y * chunkSize + z) * chunkSize + x;
                        float noiseValue1 = noiseMap1[index] + 0.47f;
                        float noiseValue2 = noiseMap2[index] + 0.27f;
                        float variation = noiseMapVariation[index] * 0.03f;

                        float density = Math.Min(noiseValue1 * 2.0f, noiseValue2);
                        density = GameMath.Clamp(density, -1.0f, 1.0f);

                        float threshold = -0.6f - (float)(yModifiers[y] * 0.7) + variation;
                        if (density < threshold)
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

        private double[] PrecomputeYModifiers(int chunkSize, BlockPos origin)
        {
            double[] yModifiers = new double[chunkSize];
            for (int y = 0; y < chunkSize; y++)
            {
                double worldY = origin.Y + y;
                yModifiers[y] = 1.0 - (worldY / sapi.WorldManager.MapSizeY);
            }
            return yModifiers;
        }
    }
}