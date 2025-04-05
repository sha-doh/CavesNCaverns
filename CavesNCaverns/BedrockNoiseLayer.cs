using CavesAndCaverns.Managers;
using System;
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

        public BedrockNoiseLayer(ICoreServerAPI sapi, NoiseManager noiseManager)
        {
            this.sapi = sapi;
            this.noiseManager = noiseManager ?? throw new ArgumentNullException(nameof(noiseManager), "NoiseManager cannot be null in BedrockNoiseLayer.");

            // Initialize the mantle block
            mantleBlock = sapi.World.GetBlock(new AssetLocation("game:mantle"));
            if (mantleBlock == null)
            {
                sapi.Logger.Error("[CavesAndCaverns] Failed to load block 'game:mantle'. Bedrock layer will not be placed.");
            }

            // Initialize noise for bedrock thickness variation
            long seed = sapi.World.Seed;
            double[] amplitudes = new double[] { 1.0, 0.5, 0.25 };
            double[] frequencies = new double[] { 0.01, 0.02, 0.04 };
            bedrockNoise = new SimplexNoise(amplitudes, frequencies, seed + 11);
        }

        public void Apply(IBlockAccessor blockAccessor, BlockPos origin, bool[,,] bedrockMap = null)
        {
            var config = CavesAndCavernsCore.ConfigManager.Config;
            if (!config.EnableBedrockLayer) return;

            if (mantleBlock == null)
            {
                sapi.Logger.Warning("[CavesAndCaverns] Mantle block not found. Skipping bedrock layer generation.");
                return;
            }

            int chunkSize = 32; // Vintage Story chunk size
            int minThickness = config.BedrockMinThickness;
            int maxThickness = config.BedrockMaxThickness;

            for (int x = 0; x < chunkSize; x++)
            {
                for (int z = 0; z < chunkSize; z++)
                {
                    // Use noise to vary thickness
                    double noiseValue = bedrockNoise.Noise((origin.X + x) * 0.01, (origin.Z + z) * 0.01);
                    int thickness = minThickness + (int)(noiseValue * (maxThickness - minThickness));

                    // Place mantle blocks from y=0 up to the calculated thickness
                    for (int y = 0; y < thickness && y < chunkSize; y++)
                    {
                        int worldX = origin.X + x;
                        int worldY = y; // Start at y=0 (bottom of the world)
                        int worldZ = origin.Z + z;

                        // Only place if within world bounds
                        if (worldY >= 0 && worldY < sapi.WorldManager.MapSizeY)
                        {
                            blockAccessor.SetBlock(mantleBlock.BlockId, new BlockPos(worldX, worldY, worldZ));
                            if (bedrockMap != null)
                                bedrockMap[x, y, z] = true;
                        }
                    }
                }
            }
        }
    }
}