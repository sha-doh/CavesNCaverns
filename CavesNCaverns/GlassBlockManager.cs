using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace CavesAndCaverns.Managers
{
    public static class GlassBlockManager
    {
        private static readonly Dictionary<string, int> glassIds = new();
        private static readonly Dictionary<string, int> placementCounts = new();
        private static bool isInitialized = false;

        public static void Initialize()
        {
            if (isInitialized) return;

            var sapi = CavesAndCavernsCore.ServerAPI;
            if (sapi == null)
            {
                throw new System.Exception("[CavesAndCaverns] GlassBlockManager initialization failed: ServerAPI is null.");
            }

            // Load glass variants
            glassIds["caves"] = sapi.World.GetBlock(new AssetLocation("game:glass-plain"))?.BlockId ?? 0; // Fallback to base glass
            glassIds["surfaceriver"] = sapi.World.GetBlock(new AssetLocation("game:glass-blue"))?.BlockId ?? 0;
            glassIds["undergroundriver"] = sapi.World.GetBlock(new AssetLocation("game:glass-yellow"))?.BlockId ?? 0;
            glassIds["lavariver"] = sapi.World.GetBlock(new AssetLocation("game:glass-red"))?.BlockId ?? 0;
            glassIds["bedrock"] = sapi.World.GetBlock(new AssetLocation("game:glass-smoky"))?.BlockId ?? 0;

            // Log the results of block loading and apply fallback if needed
            foreach (var pair in glassIds)
            {
                if (pair.Value == 0)
                {
                    sapi.Logger.Error("[CavesAndCaverns] Failed to load glass block for type '{0}'. Debug visualization will be skipped.", pair.Key);
                }
                else
                {
                    sapi.Logger.Notification("[CavesAndCaverns] Successfully loaded glass block for type '{0}' with ID {1}", pair.Key, pair.Value);
                }
                placementCounts[pair.Key] = 0;
            }

            isInitialized = true;
            sapi.Logger.Notification("[CavesAndCaverns] GlassBlockManager initialized successfully.");
        }

        public static void PlaceDebugGlass(IBlockAccessor accessor, BlockPos pos, string type)
        {
            if (!isInitialized)
            {
                Initialize();
            }

            if (glassIds.TryGetValue(type, out int blockId) && blockId != 0)
            {
                accessor.SetBlock(blockId, pos);
                placementCounts[type]++;
            }
            else
            {
                // Log only once per type to avoid flooding
                if (placementCounts[type] == 0)
                {
                    CavesAndCavernsCore.ServerAPI.Logger.Error("[CavesAndCaverns] Skipped placing debug glass for type '{0}' at {1} (block ID not found).", type, pos);
                }
                placementCounts[type]++;
            }
        }

        // Call this at the end of chunk generation to log a summary
        public static void LogPlacementSummary(int chunkX, int chunkZ)
        {
            foreach (var pair in placementCounts)
            {
                if (pair.Value > 0)
                {
                    CavesAndCavernsCore.ServerAPI.Logger.Notification("[CavesAndCaverns] Placed {0} debug glass blocks of type '{1}' in chunk {2},{3}", pair.Value, pair.Key, chunkX, chunkZ);
                }
            }
            // Reset counts for the next chunk
            foreach (var key in placementCounts.Keys)
            {
                placementCounts[key] = 0;
            }
        }
    }
}