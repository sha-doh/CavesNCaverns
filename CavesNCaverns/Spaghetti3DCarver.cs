using CavesAndCaverns;
using CavesAndCaverns.Config;
using CavesAndCaverns.Managers;
using System;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace CavesAndCaverns.Carvers
{
    public class Spaghetti3DCarver : ICarver
    {
        private readonly ICoreServerAPI sapi;
        private readonly CavesConfig config;
        private readonly NoiseManager noiseManager;

        public Spaghetti3DCarver(ICoreServerAPI sapi)
        {
            this.sapi = sapi;
            this.config = CavesAndCavernsCore.ConfigManager.Config;
            this.noiseManager = CavesAndCavernsCore.NoiseManager;
        }

        public void Generate(int chunkSize, BlockPos origin, string biomeTag, IBlockAccessor blockAccessor)
        {
            double xzScale1 = 1.0, yScale1 = 1.0;
            double xzScale2 = 1.0, yScale2 = 1.0;
            double xzScaleThickness = 2.0, yScaleThickness = 1.0;
            double xzScaleRoughness = 1.0, yScaleRoughness = 1.0;
            int seed = (int)(sapi.World.Seed + 14);

            float[] noiseMap1 = noiseManager.GenerateSpaghetti3DNoise(chunkSize, origin, seed);
            float[] noiseMap2 = noiseManager.GenerateSpaghetti3DNoise(chunkSize, origin, seed + 1);
            float[] noiseMapThickness = noiseManager.GenerateSpaghetti3DNoise(chunkSize, origin, seed + 2);
            float[] noiseMapRoughness = noiseManager.Generate3DNoise(noiseManager.GetSpaghetti2DRoughnessNoiseGenerator(), chunkSize, origin, xzScaleRoughness, yScaleRoughness, seed + 3, "Spaghetti2DRoughness");

            double[] yModifiers = PrecomputeYModifiers(chunkSize, origin);

            for (int x = 0; x < chunkSize; x++)
            {
                for (int y = 0; y < chunkSize; y++)
                {
                    if (yModifiers[y] < 0.1) continue;

                    for (int z = 0; z < chunkSize; z++)
                    {
                        int index = (y * chunkSize + z) * chunkSize + x;
                        float noiseValue1 = noiseMap1[index] + 0.47f;
                        float noiseValue2 = noiseMap2[index] + 0.27f;
                        float thicknessVariation = noiseMapThickness[index] * 0.1f;
                        float roughness = noiseMapRoughness[index];
                        float roughnessModulator = -0.05f + (-0.05f * roughness);
                        float roughnessBase = -0.4f + Math.Abs(roughness);
                        float roughnessVariation = roughnessModulator * roughnessBase;

                        float density = Math.Min(noiseValue1 * 2.0f, noiseValue2);
                        density = GameMath.Clamp(density, -1.0f, 1.0f);

                        float threshold = -0.7f - (float)(yModifiers[y] * 0.7) + thicknessVariation + roughnessVariation;
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