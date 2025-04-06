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

            // Surface rivers should only carve around surface level (Y=100-150 globally)
            int surfaceYMin = 100;
            int surfaceYMax = 150;
            if (origin.Y < surfaceYMin || origin.Y > surfaceYMax)
            {
                sapi.Logger.Debug("[CavesAndCaverns] Skipping surfaceriver carver at Y={0} (outside range {1}-{2})", origin.Y, surfaceYMin, surfaceYMax);
                return map;
            }

            // Check if a river should spawn in this chunk
            double noiseValue = noiseManager.GetSurfaceRiverNoise(origin.X, origin.Z);
            if (noiseValue * wetMultiplier > config.SurfaceRiverProbability)
            {
                // River parameters
                int riverWidth = GameMath.Clamp((int)(minWidth + noiseValue * (maxWidth - minWidth)), minWidth, maxWidth); // 3-6 blocks wide
                int riverDepth = 2; // 2 blocks deep
                int riverY = chunkSize - 1; // Start at the top of the chunk section

                // Create a winding river path across the chunk
                for (int x = 0; x < chunkSize; x++)
                {
                    // Use noise to determine the Z position of the river's center
                    double zNoise = noiseManager.GetSurfaceRiverNoise(origin.X + x, origin.Z);
                    int centerZ = (int)(chunkSize / 2 + zNoise * (chunkSize / 4)); // Center of the river, with some variation
                    centerZ = GameMath.Clamp(centerZ, riverWidth / 2, chunkSize - riverWidth / 2 - 1);

                    // Carve a river of specified width and depth
                    for (int z = centerZ - riverWidth / 2; z <= centerZ + riverWidth / 2; z++)
                    {
                        for (int y = riverY; y >= riverY - riverDepth && y >= 0; y--)
                        {
                            map[x, y, z] = true;
                        }
                    }
                }

                sapi.Logger.Notification("[CavesAndCaverns] Carver surfaceriver generated river at X:{0}, Z:{1}, Y:{2} with width {3}", origin.X, origin.Z, origin.Y, riverWidth);
            }

            return map;
        }
    }
}