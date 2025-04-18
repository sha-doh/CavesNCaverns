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

        public void Generate(int chunkSize, BlockPos origin, string biomeTag, IBlockAccessor blockAccessor)
        {
            int minWidth = config.SurfaceRiverWidthMin;
            int maxWidth = config.SurfaceRiverWidthMax;
            float wetMultiplier = biomeTag == "wet" ? config.WetBiomeRiverChanceMultiplier : 1.0f;

            int surfaceYMin = 100;
            int surfaceYMax = 150;
            if (origin.Y < surfaceYMin || origin.Y > surfaceYMax)
            {
                sapi.Logger.Debug("[CavesAndCaverns] Skipping surfaceriver carver at Y={0} (outside range {1}-{2})", origin.Y, surfaceYMin, surfaceYMax);
                return;
            }

            double spawnNoise = noiseManager.GetSurfaceRiverNoise(origin.X, origin.Z);
            if (spawnNoise * wetMultiplier > config.SurfaceRiverProbability)
            {
                int riverWidth = GameMath.Clamp((int)(minWidth + spawnNoise * (maxWidth - minWidth)), minWidth, maxWidth);
                int riverDepth = 2;
                int riverY = chunkSize - 1;

                for (int x = 0; x < chunkSize; x++)
                {
                    double carveNoise = noiseManager.GetSurfaceRiverNoise(origin.X + x, origin.Z);
                    int centerZ = (int)(chunkSize / 2 + carveNoise * (chunkSize / 4));
                    centerZ = GameMath.Clamp(centerZ, riverWidth / 2, chunkSize - riverWidth / 2 - 1);

                    if (carveNoise > 0.6)
                    {
                        for (int z = centerZ - riverWidth / 2; z <= centerZ + riverWidth / 2; z++)
                        {
                            for (int y = riverY; y >= riverY - riverDepth && y >= 0; y--)
                            {
                                BlockPos pos = new BlockPos(origin.X + x, origin.Y + y, origin.Z + z);
                                if (config.DebugGlassSurfaceRivers)
                                    GlassBlockManager.PlaceDebugGlass(blockAccessor, pos, "surfaceriver");
                                else
                                    blockAccessor.SetBlock(0, pos);
                            }
                        }
                    }
                }

                sapi.Logger.Notification("[CavesAndCaverns] Carver surfaceriver generated river at X:{0}, Z:{1}, Y:{2} with width {3}", origin.X, origin.Z, origin.Y, riverWidth);
            }
        }
    }
}