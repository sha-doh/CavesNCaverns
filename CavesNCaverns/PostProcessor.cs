using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace CavesAndCaverns.PostGen
{
    public class PostProcessor
    {
        private readonly ICoreServerAPI sapi;

        public PostProcessor(ICoreServerAPI sapi)
        {
            this.sapi = sapi;
        }

        public void ApplyDecorations(IBlockAccessor blockAccessor, BlockPos pos, string biomeTag)
        {
            var config = CavesAndCavernsCore.ConfigManager.Config;
            if (biomeTag == "wet" && sapi.World.Rand.NextDouble() < 0.1)
            {
                int count = sapi.World.Rand.Next(config.GlowWormCountMin, config.GlowWormCountMax + 1);
                for (int i = 0; i < count; i++)
                {
                    blockAccessor.SetBlock(sapi.World.GetBlock(new AssetLocation("glowworm")).BlockId, pos);
                }
            }
        }
    }
}