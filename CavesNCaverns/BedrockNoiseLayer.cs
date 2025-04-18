//using CavesAndCaverns.Managers;
//using System;
//using System.Linq;
//using Vintagestory.API.Common;
//using Vintagestory.API.MathTools;
//using Vintagestory.API.Server;

//namespace CavesAndCaverns
//{
//    public class BedrockNoiseLayer
//    {
//        private readonly ICoreServerAPI sapi;
//        private readonly NoiseManager noiseManager;
//        private int mantleBlockId;
//        private int glassBedrockBlockId;
//        private int fallbackBlockId = 0;
//        private SimplexNoise bedrockNoise;
//        private SimplexNoise variationNoise; // Renamed detailNoise for clarity

//        public BedrockNoiseLayer(ICoreServerAPI sapi, NoiseManager noiseManager)
//        {
//            this.sapi = sapi;
//            this.noiseManager = noiseManager ?? throw new ArgumentNullException(nameof(noiseManager), "NoiseManager cannot be null in BedrockNoiseLayer.");

//            var allBlocks = sapi.World.Blocks;
//            sapi.Logger.Notification("[CavesAndCaverns] Total blocks in registry: {0}", allBlocks.Count);
//            var blockCodes = allBlocks.Select(b => b.Code?.ToString()).Where(code => code != null).OrderBy(code => code).ToList();
//            sapi.Logger.Notification("[CavesAndCaverns] Available block codes: {0}", string.Join(", ", blockCodes));

//            var mantleBlock = sapi.World.GetBlock(new AssetLocation("mantle"));
//            mantleBlockId = mantleBlock?.BlockId ?? 0;
//            if (mantleBlockId == 0)
//            {
//                sapi.Logger.Error("[CavesAndCaverns] Failed to load block 'mantle'. Falling back to rock-granite...");
//                var graniteBlock = sapi.World.GetBlock(new AssetLocation("rock-granite"));
//                if (graniteBlock == null)
//                {
//                    sapi.Logger.Error("[CavesAndCaverns] Failed to load block 'rock-granite'. Bedrock layer will not be placed.");
//                }
//                else
//                {
//                    sapi.Logger.Notification("[CavesAndCaverns] Fallback to rock-granite block for bedrock with ID {0}", graniteBlock.BlockId);
//                    fallbackBlockId = graniteBlock.BlockId;
//                }
//            }
//            else
//            {
//                sapi.Logger.Notification("[CavesAndCaverns] Successfully loaded mantle block with ID {0}", mantleBlockId);
//            }

//            GlassBlockManager.Initialize();
//            glassBedrockBlockId = GetGlassBlockId("bedrock");
//            if (glassBedrockBlockId == 0)
//            {
//                sapi.Logger.Error("[CavesAndCaverns] Failed to retrieve glass block ID for 'bedrock' from GlassBlockManager.");
//            }
//            else
//            {
//                sapi.Logger.Notification("[CavesAndCaverns] Successfully retrieved glass-bedrock block ID {0} from GlassBlockManager", glassBedrockBlockId);
//            }

//            long seed = sapi.World.Seed;
//            // Main noise for thickness variation (more octaves for varied scales)
//            double[] amplitudes = new double[] { 1.0, 0.5, 0.25, 0.125 }; // 4 octaves for more variation
//            double[] frequencies = new double[] { 0.02, 0.04, 0.08, 0.16 }; // Higher frequencies for more rapid changes
//            bedrockNoise = new SimplexNoise(amplitudes, frequencies, seed + 11);

//            // Secondary noise for small-scale variations
//            double[] variationAmplitudes = new double[] { 1.0, 0.5 };
//            double[] variationFrequencies = new double[] { 0.1, 0.2 }; // Even higher frequencies for fine details
//            variationNoise = new SimplexNoise(variationAmplitudes, variationFrequencies, seed + 12);
//        }

//        private int GetGlassBlockId(string type)
//        {
//            string glassVariant = type == "bedrock" ? "smoky" : "plain";
//            var block = sapi.World.GetBlock(new AssetLocation($"game:glass-{glassVariant}"));
//            return block?.BlockId ?? 0;
//        }

//        public void Apply(IBlockAccessor blockAccessor, BlockPos origin, bool[,,] bedrockMap = null, bool[,,] caveMap = null)
//        {
//            var config = CavesAndCavernsCore.ConfigManager.Config;
//            if (!config.EnableBedrockLayer)
//            {
//                sapi.Logger.Notification("[CavesAndCaverns] Bedrock layer generation skipped because EnableBedrockLayer is false.");
//                return;
//            }

//            if (mantleBlockId == 0 && fallbackBlockId == 0 && glassBedrockBlockId == 0)
//            {
//                sapi.Logger.Warning("[CavesAndCaverns] No valid block IDs available for bedrock layer. Skipping generation.");
//                return;
//            }

//            int chunkSize = 32;
//            int chunkX = origin.X / chunkSize;
//            int chunkZ = origin.Z / chunkSize;
//            int blockIdToPlace = config.DebugGlassBedrock && glassBedrockBlockId != 0
//                ? glassBedrockBlockId
//                : (mantleBlockId != 0 ? mantleBlockId : fallbackBlockId);
//            int placedBlocks = 0;

//            // Determine the base Y-level based on world height and UseInvertedWorld
//            int worldHeight = sapi.WorldManager.MapSizeY;
//            int baseY = config.UseInvertedWorld ? (worldHeight - 1) : 0;
//            int yDirection = config.UseInvertedWorld ? -1 : 1;

//            sapi.Logger.Notification("[CavesAndCaverns] Starting bedrock layer generation for chunk at X={0}, Z={1}, baseY={2}, inverted={3}", chunkX, chunkZ, baseY, config.UseInvertedWorld);

//            IServerChunk chunk = blockAccessor.GetChunk(chunkX, 0, chunkZ) as IServerChunk;
//            if (chunk == null)
//            {
//                sapi.Logger.Warning("[CavesAndCaverns] Chunk at X={0}, Z={1} not loaded before bedrock generation. Attempting to preload.", chunkX, chunkZ);
//                blockAccessor.GetChunk(chunkX, 0, chunkZ);
//                chunk = blockAccessor.GetChunk(chunkX, 0, chunkZ) as IServerChunk;
//                if (chunk == null)
//                {
//                    sapi.Logger.Error("[CavesAndCaverns] Failed to load chunk at X={0}, Z={1}. Skipping bedrock generation.", chunkX, chunkZ);
//                    return;
//                }
//            }

//            int minX = chunkX * chunkSize;
//            int maxX = minX + chunkSize - 1;
//            int minZ = chunkZ * chunkSize;
//            int maxZ = minZ + chunkSize - 1;

//            // Sample noise at a lower resolution (every 4 blocks) and interpolate for smooth transitions
//            int resolution = 4; // Sample every 4 blocks
//            int downsampledSize = (chunkSize + resolution - 1) / resolution; // Ceiling division
//            float[,,] thicknessMap = new float[downsampledSize, downsampledSize, 1];

//            for (int dx = 0; dx < downsampledSize; dx++)
//            {
//                for (int dz = 0; dz < downsampledSize; dz++)
//                {
//                    int worldX = origin.X + (dx * resolution);
//                    int worldZ = origin.Z + (dz * resolution);

//                    // Main noise for base thickness
//                    float noise = (float)bedrockNoise.Noise(worldX * 0.02, worldZ * 0.02);
//                    noise = (noise + 1) / 2; // Normalize to [0, 1]

//                    // Secondary noise for additional variation
//                    float variation = (float)variationNoise.Noise(worldX * 0.1, worldZ * 0.1);
//                    variation = (variation + 1) / 2; // Normalize to [0, 1]
//                    float combinedNoise = noise + (variation * 0.3f); // Add small variation (0.3 weight)

//                    // Map noise to thickness range [1, 3]
//                    int thickness = 1 + (int)(combinedNoise * 3); // Range: [1, 4)
//                    thickness = Math.Clamp(thickness, 1, 3); // Clamp to [1, 3]

//                    thicknessMap[dx, dz, 0] = thickness;
//                }
//            }

//            for (int x = 0; x < chunkSize; x++)
//            {
//                for (int z = 0; z < chunkSize; z++)
//                {
//                    int worldX = origin.X + x;
//                    int worldZ = origin.Z + z;

//                    // Interpolate thickness from downsampled map
//                    float fx = (float)x / resolution;
//                    float fz = (float)z / resolution;
//                    int x0 = (int)fx;
//                    int z0 = (int)fz;
//                    int x1 = Math.Min(x0 + 1, downsampledSize - 1);
//                    int z1 = Math.Min(z0 + 1, downsampledSize - 1);
//                    float dx = fx - x0;
//                    float dz = fz - z0;

//                    float t00 = thicknessMap[x0, z0, 0];
//                    float t10 = thicknessMap[x1, z0, 0];
//                    float t01 = thicknessMap[x0, z1, 0];
//                    float t11 = thicknessMap[x1, z1, 0];

//                    float ix0 = GameMath.Lerp(t00, t10, dx);
//                    float ix1 = GameMath.Lerp(t01, t11, dx);
//                    float thickness = GameMath.Lerp(ix0, ix1, dz);
//                    int finalThickness = Math.Clamp((int)thickness, 1, 3); // Ensure final thickness is [1, 3]

//                    for (int yOffset = 0; yOffset < finalThickness && yOffset < chunkSize; yOffset++)
//                    {
//                        int y = baseY + (yOffset * yDirection);
//                        int worldY = y;

//                        if (worldX >= minX && worldX <= maxX && worldY >= 0 && worldY < worldHeight && worldZ >= minZ && worldZ <= maxZ)
//                        {
//                            BlockPos pos = new BlockPos(worldX, worldY, worldZ);
//                            if (blockAccessor.IsValidPos(pos))
//                            {
//                                // Skip if this position is carved by a cave (from caveMap)
//                                if (caveMap != null && x < chunkSize && yOffset < chunkSize && z < chunkSize && caveMap[x, yOffset, z])
//                                {
//                                    continue;
//                                }

//                                // Skip if the existing block is a liquid (e.g., water, lava)
//                                int existingBlockId = blockAccessor.GetBlock(pos).Id;
//                                var existingBlock = sapi.World.GetBlock(existingBlockId);
//                                if (existingBlock != null && (existingBlock.BlockMaterial == EnumBlockMaterial.Liquid || existingBlock.BlockMaterial == EnumBlockMaterial.Lava))
//                                {
//                                    continue;
//                                }

//                                // Add small-scale variation using variationNoise
//                                float detail = (float)variationNoise.Noise(worldX * 0.1, worldY * 0.1, worldZ * 0.1);
//                                if (detail > 0.7f) // Example threshold for creating small gaps
//                                {
//                                    continue;
//                                }

//                                blockAccessor.SetBlock(blockIdToPlace, pos);
//                                placedBlocks++;
//                                if (bedrockMap != null && x < chunkSize && yOffset < chunkSize && z < chunkSize)
//                                    bedrockMap[x, yOffset, z] = true;
//                            }
//                        }
//                    }
//                }
//            }

//            chunk.MarkModified();
//            sapi.Logger.Debug("[CavesAndCaverns] Chunk at X={0}, Z={1} marked as modified with {2} blocks placed", chunkX, chunkZ, placedBlocks);

//            int verifyId = blockAccessor.GetBlock(new BlockPos(origin.X, config.UseInvertedWorld ? worldHeight - 1 : 0, origin.Z)).Id;
//            sapi.Logger.Notification("[CavesAndCaverns] Bedrock layer generated for chunk at X={0}, Z={1} with {2} blocks placed, verified block at {3},{4},{5} is ID {6}",
//                chunkX, chunkZ, placedBlocks, chunkX * chunkSize, config.UseInvertedWorld ? worldHeight - 1 : 0, chunkZ * chunkSize, verifyId);
//        }

//        public int GetBedrockBlockId()
//        {
//            var config = CavesAndCavernsCore.ConfigManager.Config;
//            return config.DebugGlassBedrock && glassBedrockBlockId != 0
//                ? glassBedrockBlockId
//                : (mantleBlockId != 0 ? mantleBlockId : fallbackBlockId);
//        }
//    }
//}