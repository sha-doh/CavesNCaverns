using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace CavesAndCaverns.Managers
{
    public static class GlassBlockManager
    {
        private static readonly Dictionary<string, int> glassIds = new();

        static GlassBlockManager()
        {
            var sapi = CavesAndCavernsCore.ServerAPI;
            glassIds["caves"] = sapi.World.GetBlock(new AssetLocation("game:glass-clear"))?.BlockId ?? 0;
            glassIds["surfaceriver"] = sapi.World.GetBlock(new AssetLocation("game:glass-blue"))?.BlockId ?? 0;
            glassIds["undergroundriver"] = sapi.World.GetBlock(new AssetLocation("game:glass-lightblue"))?.BlockId ?? 0;
            glassIds["lavariver"] = sapi.World.GetBlock(new AssetLocation("game:glass-red"))?.BlockId ?? 0;
            glassIds["bedrock"] = sapi.World.GetBlock(new AssetLocation("game:glass-dark"))?.BlockId ?? 0;
        }

        public static void PlaceDebugGlass(IBlockAccessor accessor, BlockPos pos, string type)
        {
            if (glassIds.TryGetValue(type, out int blockId) && blockId != 0)
            {
                accessor.SetBlock(blockId, pos);
            }
        }
    }
}