using CavesAndCaverns.Config;
using CavesAndCaverns.Managers;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace CavesAndCaverns.Carvers
{
    public class CheeseCarver : ICarver
    {
        private readonly ICoreServerAPI sapi;
        private readonly CavesConfig config;
        private readonly NoiseManager noiseManager;

        public CheeseCarver(ICoreServerAPI sapi)
        {
            this.sapi = sapi;
            this.config = CavesAndCavernsCore.ConfigManager.Config;
            this.noiseManager = CavesAndCavernsCore.NoiseManager;
        }

        public bool[,,] Generate(int chunkSize, BlockPos origin, string biomeTag)
        {
            bool[,,] map = new bool[chunkSize, chunkSize, chunkSize];
            for (int x = 0; x < chunkSize; x++)
                for (int y = 0; y < chunkSize; y++)
                    for (int z = 0; z < chunkSize; z++)
                    {
                        double noiseValue = noiseManager.GetCheeseNoise(origin.X + x, origin.Y + y, origin.Z + z);
                        if (noiseValue > 0.1) // Lowered from 0.5 to 0.1
                            map[x, y, z] = true;
                    }
            return map;
        }
    }
}