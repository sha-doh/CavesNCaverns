using CavesAndCaverns.Managers;
using System;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace CavesAndCaverns
{
    public class BedrockNoiseLayer
    {
        private readonly ICoreServerAPI sapi;
        private readonly NoiseManager noiseManager;
        private Block mantleBlock;
        private SimplexNoise bedrockNoise;
        private int fallbackBlockId = 0;

        public BedrockNoiseLayer(ICoreServerAPI sapi, NoiseManager noiseManager)
        {
            this.sapi = sapi;
            this.noiseManager = noiseManager ?? throw new ArgumentNullException(nameof(noiseManager), "NoiseManager cannot be null in BedrockNoiseLayer.");

            // Debug: Log all block codes to verify availability
            var allBlocks = sapi.World.Blocks;
            sapi.Logger.Notification("[CavesAndCaverns] Total blocks in registry: {0}", allBlocks.Count);
            var blockCodes = allBlocks.Select(b => b.Code?.ToString()).Where(code => code != null).OrderBy(code => code).ToList();
            sapi.Logger.Notification("[CavesAndCaverns] Available block codes: {0}", string.Join(", ", blockCodes));

            // Try to load game:glass-plain
            mantleBlock = sapi.World.GetBlock(new AssetLocation("game:glass-plain"));
            if (mantleBlock == null)
            {
                sapi.Logger.Error("[CavesAndCaverns] Failed to load block 'game:glass-plain'. Trying 'game:mantle'...");
                mantleBlock = sapi.World.GetBlock(new AssetLocation("game:mantle"));
                if (mantleBlock == null)
                {
                    sapi.Logger.Error("[CavesAndCaverns] Failed to load block 'game:mantle'. Using fallback block ID 1491 (glass-caves)...");
                    // Fallback to block ID 1491 (glass-caves) directly
                    mantleBlock = sapi.World.Blocks.FirstOrDefault(b => b.Id == 1491);
                    if (mantleBlock == null)
                    {
                        sapi.Logger.Error("[CavesAndCaverns] Failed to load block with ID 1491. Falling back to stone (ID 1)...");
                        mantleBlock = sapi.World.GetBlock(new AssetLocation("game:stone"));
                        if (mantleBlock == null)
                        {
                            sapi.Logger.Error("[CavesAndCaverns] Failed to load block 'game:stone'. Bedrock layer will not be placed.");
                        }
                        else
                        {
                            sapi.Logger.Notification("[CavesAndCaverns] Fallback to stone block for bedrock with ID {0}", mantleBlock.BlockId);
                            fallbackBlockId = mantleBlock.BlockId;
                        }
                    }
                    else
                    {
                        sapi.Logger.Notification("[CavesAndCaverns] Successfully loaded block with ID 1491 (glass-caves) for bedrock");
                        fallbackBlockId = 1491;
                    }
                }
                else
                {
                    sapi.Logger.Notification("[CavesAndCaverns] Successfully loaded game:mantle block for bedrock with ID {0}", mantleBlock.BlockId);
                    fallbackBlockId = mantleBlock.BlockId;
                }
            }
            else
            {
                sapi.Logger.Notification("[CavesAndCaverns] Successfully loaded game:glass-plain block for bedrock with ID {0}", mantleBlock.BlockId);
                fallbackBlockId = mantleBlock.BlockId;
            }

            // Initialize noise for bedrock thickness variation (not used for now)
            long seed = sapi.World.Seed;
            double[] amplitudes = new double[] { 1.0, 0.5, 0.25 };
            double[] frequencies = new double[] { 0.01, 0.02, 0.04 };
            bedrockNoise = new SimplexNoise(amplitudes, frequencies, seed + 11);
        }

        public void Apply(IBlockAccessor blockAccessor, BlockPos origin, bool[,,] bedrockMap = null)
        {
            var config = CavesAndCavernsCore.ConfigManager.Config;
            if (!config.EnableBedrockLayer)
            {
                sapi.Logger.Notification("[CavesAndCaverns] Bedrock layer generation skipped because EnableBedrockLayer is false.");
                return;
            }

            if (mantleBlock == null && fallbackBlockId == 0)
            {
                sapi.Logger.Warning("[CavesAndCaverns] Mantle block not found and no fallback ID available. Skipping bedrock layer generation.");
                return;
            }

            int chunkSize = 32;
            int fixedThickness = config.BedrockMinThickness;
            int placedBlocks = 0;

            sapi.Logger.Notification("[CavesAndCaverns] Starting bedrock layer generation for chunk at X={0}, Z={1} with thickness {2}", origin.X / chunkSize, origin.Z / chunkSize, fixedThickness);

            for (int x = 0; x < chunkSize; x++)
            {
                for (int z = 0; z < chunkSize; z++)
                {
                    for (int y = 0; y < fixedThickness && y < chunkSize; y++)
                    {
                        int worldX = origin.X + x;
                        int worldY = y;
                        int worldZ = origin.Z + z;

                        if (worldY >= 0 && worldY < sapi.WorldManager.MapSizeY)
                        {
                            int blockIdToPlace = mantleBlock != null ? mantleBlock.BlockId : fallbackBlockId;
                            blockAccessor.SetBlock(blockIdToPlace, new BlockPos(worldX, worldY, worldZ));
                            placedBlocks++;
                            if (bedrockMap != null)
                                bedrockMap[x, y, z] = true;
                        }
                    }
                }
            }

            sapi.Logger.Notification("[CavesAndCaverns] Bedrock layer generated for chunk at X={0}, Z={1} with {2} blocks placed", origin.X / chunkSize, origin.Z / chunkSize, placedBlocks);
        }
    }
}