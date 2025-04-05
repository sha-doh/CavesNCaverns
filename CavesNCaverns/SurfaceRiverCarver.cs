using CavesAndCaverns.Config;
using CavesAndCaverns.Managers;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace CavesAndCaverns.Carvers
{
    public class SurfaceRiverCarver : ICarver
    {
        private readonly ICoreServerAPI sapi;
        private readonly CavesConfig config;
        private readonly NoiseManager noiseManager;

        public SurfaceRiverCarver(ICoreServerAPI sapi)
        {
            this.sapi = sapi;
            this.config = CavesAndCavernsCore.ConfigManager.Config;
            this.noiseManager = CavesAndCavernsCore.NoiseManager;
        }

        public bool[,,] Generate(int chunkSize, BlockPos origin, string biomeTag)
        {
            bool[,,] map = new bool[chunkSize, chunkSize, chunkSize];
            int minWidth = config.SurfaceRiverWidthMin;
            int maxWidth = config.SurfaceRiverWidthMax;
            float wetMultiplier = biomeTag == "wet" ? config.WetBiomeRiverChanceMultiplier : 1.0f;

            for (int x = 0; x < chunkSize; x++)
            {
                for (int z = 0; z < chunkSize; z++)
                {
                    double noiseValue = noiseManager.GetSurfaceRiverNoise(origin.X + x, origin.Z + z);
                    if (noiseValue * wetMultiplier > 0.1) // Lowered from 0.5 to 0.1
                    {
                        int width = sapi.World.Rand.Next(minWidth, maxWidth + 1);
                        for (int dx = -width; dx <= width; dx++)
                        {
                            for (int dz = -width; dz <= width; dz++)
                            {
                                if (dx * dx + dz * dz <= width * width)
                                {
                                    int newX = x + dx;
                                    int newZ = z + dz;
                                    if (newX >= 0 && newX < chunkSize && newZ >= 0 && newZ < chunkSize)
                                    {
                                        for (int y = 0; y < chunkSize; y++)
                                            map[newX, y, newZ] = true;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            return map;
        }
    }
}