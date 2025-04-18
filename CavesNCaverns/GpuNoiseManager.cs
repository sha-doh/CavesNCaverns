//// Full hybrid NoiseManager.cs using ILGPU for GPU-accelerated noise
//// Supports hybrid FastNoise2 + ILGPU FBM fallback system

//using System;
//using Vintagestory.API.Common;
//using Vintagestory.API.MathTools;
//using Vintagestory.API.Server;
//using ILGPU;
//using ILGPU.Runtime;

//namespace CavesAndCaverns.Managers
//{
//    public class NoiseManager : ModSystem
//    {
//        private ICoreServerAPI sapi;
//        private Context context;
//        private Accelerator accelerator;
//        private Action<Index1D, ArrayView<float>, int, int, int, int, float, int, int, float, float> fbmKernel;

//        private bool useGpuNoise = true;
//        private readonly float baseFreq = 0.0005f;

//        private readonly float cheeseBaseFrequency = 0.001f;
//        private readonly float spaghettiBaseFrequency = 0.015f;
//        private readonly float noodleBaseFrequency = 0.06f;
//        private readonly float biomeSpecificBaseFrequency = 0.003f;
//        private readonly float entranceBaseFrequency = 0.001f;

//        private readonly double cheeseFx = 1.0, cheeseFy = 1.0;
//        private readonly double spaghettiFx = 1.0, spaghettiFy = 0.5;
//        private readonly double caveLayerFx = 1.0, caveLayerFy = 0.5;
//        private readonly double noodleFx = 1.0, noodleFy = 1.0;

//        public override bool ShouldLoad(EnumAppSide side) => side == EnumAppSide.Server;

//        public override void StartServerSide(ICoreServerAPI api)
//        {
//            sapi = api ?? throw new ArgumentNullException(nameof(api));

//            if (useGpuNoise)
//            {
//                context = Context.CreateDefault();
//                accelerator = context.CreateDefaultAccelerator();
//                fbmKernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, int, int, int, int, float, int, int, float, float>(FBMKernel);
//                sapi.Logger.Notification("[CavesAndCaverns] ILGPU initialized: " + accelerator.Name);
//            }

//            sapi.Logger.Notification("[CavesAndCaverns] NoiseManager initialized.");
//        }

//        private float ApplyYLevelScaling(float noiseValue, int y, float baseScale, float deepScale, int thresholdY)
//        {
//            float scale = y < thresholdY ? deepScale : baseScale;
//            return noiseValue * scale;
//        }

//        private float BlendNoise(float primary, float secondary, float weight)
//        {
//            return primary * (1f - weight) + secondary * weight;
//        }

//        public float[] GenerateHybridFBM(int chunkSize, BlockPos origin, float fx, float fy, int seed, float baseFrequency, int octaves, float lacunarity, float gain)
//        {
//            int total = chunkSize * chunkSize * chunkSize;

//            if (useGpuNoise && accelerator != null)
//            {
//                using var buffer = accelerator.Allocate1D<float>(total);
//                fbmKernel(total, buffer.View, chunkSize, origin.X, origin.Y, origin.Z, baseFrequency, seed, octaves, lacunarity, gain);
//                accelerator.Synchronize();
//                return buffer.GetAsArray1D();
//            }
//            else
//            {
//                float[] buffer = new float[total];
//                FastNoise cpuNode = new FastNoise("FractalFBm");
//                cpuNode.Set("Octaves", octaves);
//                cpuNode.Set("Lacunarity", lacunarity);
//                cpuNode.Set("Gain", gain);
//                cpuNode.Set("Frequency", baseFrequency);
//                cpuNode.GenUniformGrid3D(buffer, origin.X, origin.Y, origin.Z, chunkSize, chunkSize, chunkSize, baseFrequency * fx * fy, seed);
//                return buffer;
//            }
//        }

//        static void FBMKernel(
//            Index1D index,
//            ArrayView<float> output,
//            int size,
//            int originX, int originY, int originZ,
//            float baseFreq,
//            int seed,
//            int octaves,
//            float lacunarity,
//            float gain)
//        {
//            int x = index % size;
//            int y = (index / size) % size;
//            int z = index / (size * size);

//            float fx = (originX + x) * baseFreq;
//            float fy = (originY + y) * baseFreq;
//            float fz = (originZ + z) * baseFreq;

//            float sum = 0f;
//            float amp = 1f;

//            for (int i = 0; i < octaves; i++)
//            {
//                float noise = XMath.Sin(fx + seed) * XMath.Cos(fy + seed) * XMath.Sin(fz + seed); // placeholder noise
//                sum += noise * amp;
//                fx *= lacunarity;
//                fy *= lacunarity;
//                fz *= lacunarity;
//                amp *= gain;
//            }

//            output[index] = sum;
//        }

//        public bool[] GenerateCombinedNoiseCaves(int chunkSize, BlockPos origin, string biomeTag, Random rand,
//            float cheeseProb, float spaghettiProb, float spaghetti3DProb, float veinGapProb, float denseCaveProb, float thermalLakeProb)
//        {
//            int total = chunkSize * chunkSize * chunkSize;
//            bool[] map = new bool[total];

//            float[] cheese = null, spaghetti2D = null, spaghetti3D = null;
//            float[] vein = null, dense = null, thermal = null;

//            int seed = (int)sapi.World.Seed;

//            if (rand.NextDouble() <= cheeseProb)
//            {
//                cheese = GenerateHybridFBM(chunkSize, origin, (float)cheeseFx, (float)cheeseFy, seed + 6, cheeseBaseFrequency, 8, 2f, 1f);
//                for (int i = 0; i < total; i++)
//                    cheese[i] = ApplyYLevelScaling(cheese[i], origin.Y + (i / (chunkSize * chunkSize)) % chunkSize, 1f, 1.5f, 0);
//            }

//            if (rand.NextDouble() <= spaghettiProb)
//            {
//                spaghetti2D = GenerateHybridFBM(chunkSize, origin, (float)spaghettiFx, (float)spaghettiFy, seed + 9, spaghettiBaseFrequency, 4, 2f, 0.5f);
//                for (int i = 0; i < total; i++)
//                    spaghetti2D[i] = ApplyYLevelScaling(spaghetti2D[i], origin.Y + (i / (chunkSize * chunkSize)) % chunkSize, 1f, 1.1f, 0);
//            }

//            if (rand.NextDouble() <= spaghetti3DProb)
//            {
//                spaghetti3D = GenerateHybridFBM(chunkSize, origin, (float)spaghettiFx, (float)spaghettiFy, seed + 14, spaghettiBaseFrequency, 4, 2f, 0.5f);
//                for (int i = 0; i < total; i++)
//                    spaghetti3D[i] = ApplyYLevelScaling(spaghetti3D[i], origin.Y + (i / (chunkSize * chunkSize)) % chunkSize, 1f, 1.1f, 0);
//            }

//            if (rand.NextDouble() <= veinGapProb)
//            {
//                vein = GenerateHybridFBM(chunkSize, origin, (float)noodleFx, (float)noodleFy, seed + 8, noodleBaseFrequency, 3, 2f, 0.5f);
//            }

//            if (rand.NextDouble() <= denseCaveProb)
//            {
//                dense = GenerateHybridFBM(chunkSize, origin, (float)caveLayerFx, (float)caveLayerFy, seed + 5, biomeSpecificBaseFrequency, 5, 2f, 0.5f);
//                thermal = GenerateHybridFBM(chunkSize, origin, (float)caveLayerFx, (float)caveLayerFy, seed + 7, biomeSpecificBaseFrequency, 5, 2f, 0.5f);
//                for (int i = 0; i < total; i++)
//                {
//                    float y = origin.Y + (i / (chunkSize * chunkSize)) % chunkSize;
//                    thermal[i] = ApplyYLevelScaling(thermal[i], (int)y, 1f, 1.2f, 0);
//                    dense[i] = BlendNoise(dense[i], thermal[i], 0.3f);
//                }
//            }
//            else if (rand.NextDouble() <= thermalLakeProb)
//            {
//                thermal = GenerateHybridFBM(chunkSize, origin, (float)caveLayerFx, (float)caveLayerFy, seed + 7, biomeSpecificBaseFrequency, 5, 2f, 0.5f);
//                for (int i = 0; i < total; i++)
//                {
//                    float y = origin.Y + (i / (chunkSize * chunkSize)) % chunkSize;
//                    thermal[i] = ApplyYLevelScaling(thermal[i], (int)y, 1f, 1.2f, 0);
//                }
//            }

//            for (int i = 0; i < total; i++)
//            {
//                if ((cheese != null && cheese[i] > 0.5f) ||
//                    (spaghetti2D != null && spaghetti2D[i] > 0.5f) ||
//                    (spaghetti3D != null && spaghetti3D[i] > 0.5f) ||
//                    (vein != null && vein[i] > 0.5f) ||
//                    (dense != null && dense[i] > 0.5f) ||
//                    (thermal != null && thermal[i] > 0.5f))
//                {
//                    map[i] = true;
//                }
//            }

//            return map;
//        }
//    }
//}
