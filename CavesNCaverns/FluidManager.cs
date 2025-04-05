using CavesAndCaverns.Managers;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace CavesAndCaverns
{
    public class FluidManager
    {
        private readonly ICoreServerAPI sapi;

        public FluidManager(ICoreServerAPI sapi)
        {
            this.sapi = sapi;
        }

        public void ApplyFlow(IBlockAccessor blockAccessor, BlockPos origin, bool[,,] map, bool isSurface)
        {
            var config = CavesAndCavernsCore.ConfigManager.Config;
            int chunkSize = sapi.WorldManager.ChunkSize;
            for (int x = 0; x < chunkSize; x++)
                for (int y = 0; y < chunkSize; y++)
                    for (int z = 0; z < chunkSize; z++)
                    {
                        if (map[x, y, z])
                        {
                            string fluid = FluidRegistry.GetFluid(origin.X + x, origin.Y + y, origin.Z + z);
                            if (fluid != null)
                            {
                                blockAccessor.SetBlock(sapi.World.GetBlock(new AssetLocation(fluid)).BlockId, new BlockPos(origin.X + x, origin.Y + y, origin.Z + z));
                            }
                        }
                    }
        }
    }
}