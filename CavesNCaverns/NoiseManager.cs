using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace CavesAndCaverns.Managers
{
    public class FastNoisePool
    {
        private readonly ConcurrentQueue<FastNoise> pool = new ConcurrentQueue<FastNoise>();
        private readonly List<FastNoise> allInstances;
        private readonly int octaves;
        private readonly float lacunarity;
        private readonly float gain;
        private readonly string nodeType;
        private readonly bool useSourceNode;
        private readonly ICoreServerAPI sapi;
        private readonly int maxPoolSize;
        private int totalInstancesCreated = 0;

        public FastNoisePool(int poolSize, int octaves, float lacunarity, float gain, ICoreServerAPI sapi, int maxPoolSize, string nodeType = "FractalFBm", bool useSourceNode = true)
        {
            this.octaves = octaves;
            this.lacunarity = lacunarity;
            this.gain = gain;
            this.nodeType = nodeType;
            this.useSourceNode = useSourceNode;
            this.sapi = sapi;
            this.maxPoolSize = maxPoolSize;
            allInstances = new List<FastNoise>(poolSize);

            for (int i = 0; i < poolSize; i++)
            {
                var noise = CreateNewNoiseInstance();
                pool.Enqueue(noise);
                allInstances.Add(noise);
                Interlocked.Increment(ref totalInstancesCreated);
            }

            if (CavesAndCavernsCore.ConfigManager.Config.VerboseLogging == true)
            {
                sapi.Logger.Debug("[CavesAndCaverns] Initialized FastNoise pool for {0} with initial size {1}, max size {2}, total instances created {3}",
                    nodeType, poolSize, maxPoolSize, totalInstancesCreated);
            }
        }

        private FastNoise CreateNewNoiseInstance()
        {
            var noise = new FastNoise(nodeType);
            if (useSourceNode && nodeType == "FractalFBm")
            {
                noise.Set("Source", new FastNoise("Perlin"));
            }
            noise.Set("Octaves", octaves);
            noise.Set("Lacunarity", lacunarity);
            noise.Set("Gain", gain);
            return noise;
        }

        public FastNoise Rent()
        {
            if (pool.TryDequeue(out FastNoise noise))
            {
                return noise;
            }

            if (totalInstancesCreated >= maxPoolSize)
            {
                sapi.Logger.Warning("[CavesAndCaverns] FastNoise pool for {0} reached max size {1}. Waiting for available instance.", nodeType, maxPoolSize);
                while (!pool.TryDequeue(out noise))
                {
                    Thread.Sleep(1);
                }
                return noise;
            }

            lock (allInstances)
            {
                if (totalInstancesCreated >= maxPoolSize)
                {
                    sapi.Logger.Warning("[CavesAndCaverns] FastNoise pool for {0} reached max size {1}. Waiting for available instance.", nodeType, maxPoolSize);
                    while (!pool.TryDequeue(out noise))
                    {
                        Thread.Sleep(1);
                    }
                    return noise;
                }

                sapi.Logger.Warning("[CavesAndCaverns] FastNoise pool exhausted for {0}. Creating new instance (total created: {1}).", nodeType, totalInstancesCreated + 1);
                noise = CreateNewNoiseInstance();
                allInstances.Add(noise);
                Interlocked.Increment(ref totalInstancesCreated);
                return noise;
            }
        }

        public void Return(FastNoise noise)
        {
            pool.Enqueue(noise);
        }
    }

    public static class FastNoiseSafeInit
    {
        private static readonly object simdLock = new object();
        private static bool initialized = false;
        private static int simdLevel = -1;

        public static void EnsureSIMDInitialized(FastNoise noiseInstance = null)
        {
            if (initialized) return;

            lock (simdLock)
            {
                if (!initialized)
                {
                    if (noiseInstance != null)
                    {
                        simdLevel = (int)noiseInstance.GetSIMDLevel();
                    }
                    else
                    {
                        var tempNoise = new FastNoise("FractalFBm");
                        tempNoise.Set("Source", new FastNoise("Perlin"));
                        simdLevel = (int)tempNoise.GetSIMDLevel();
                    }
                    initialized = true;
                }
            }
        }

        public static int SIMDLevel => simdLevel;
    }

    public class NoiseManager : ModSystem
    {
        private ICoreServerAPI sapi;
        private readonly ConcurrentDictionary<string, FastNoisePool> noisePools = new ConcurrentDictionary<string, FastNoisePool>();
        private bool isInitialized = false;
        private readonly object initLock = new object();

        private readonly ConcurrentDictionary<(int x, int y, int z, string noiseType, int seed), float[]> noiseCache = new();
        private readonly LinkedList<(int x, int y, int z, string noiseType, int seed)> lruList = new LinkedList<(int x, int y, int z, string noiseType, int seed)>();
        private readonly object lruLock = new object();
        private const int CacheSizeLimit = 1000;

        // Thread-local cache for sparse noise results
        [ThreadStatic]
        private static Dictionary<(int xStart, int yStart, int zStart, float frequency, int seed), float[]> sparseNoiseCache;
        private const int SparseCacheSizeLimit = 100; // Max entries per thread

        private readonly int surfaceRiverOctaves = 4;
        private readonly float surfaceRiverLacunarity = 2.0f;
        private readonly float surfaceRiverGain = 0.5f;
        private readonly float surfaceRiverFrequency = 0.015f;

        private readonly int undergroundRiverOctaves = 4;
        private readonly float undergroundRiverLacunarity = 2.0f;
        private readonly float undergroundRiverGain = 0.5f;
        private readonly float undergroundRiverFrequency = 0.015f;

        private readonly int lavaRiverOctaves = 4;
        private readonly float lavaRiverLacunarity = 2.0f;
        private readonly float lavaRiverGain = 0.5f;
        private readonly float lavaRiverFrequency = 0.015f;

        private readonly int canyonOctaves = 4;
        private readonly float canyonLacunarity = 2.0f;
        private readonly float canyonGain = 0.5f;
        private readonly float canyonFrequency = 0.015f;

        private readonly int canyonRotationOctaves = 2;
        private readonly float canyonRotationLacunarity = 2.0f;
        private readonly float canyonRotationGain = 0.5f;
        private readonly float canyonRotationFrequency = 0.015f;

        private readonly int denseCaveRadiusHorizontalOctaves = 2;
        private readonly float denseCaveRadiusHorizontalLacunarity = 2.0f;
        private readonly float denseCaveRadiusHorizontalGain = 0.5f;
        private readonly float denseCaveRadiusHorizontalFrequency = 0.015f;

        private readonly int denseCaveRadiusVerticalOctaves = 2;
        private readonly float denseCaveRadiusVerticalLacunarity = 2.0f;
        private readonly float denseCaveRadiusVerticalGain = 0.5f;
        private readonly float denseCaveRadiusVerticalFrequency = 0.015f;

        private readonly int cheeseRadiusHorizontalOctaves = 2;
        private readonly float cheeseRadiusHorizontalLacunarity = 2.0f;
        private readonly float cheeseRadiusHorizontalGain = 0.5f;
        private readonly float cheeseRadiusHorizontalFrequency = 0.015f;

        private readonly int cheeseRadiusVerticalOctaves = 2;
        private readonly float cheeseRadiusVerticalLacunarity = 2.0f;
        private readonly float cheeseRadiusVerticalGain = 0.5f;
        private readonly float cheeseRadiusVerticalFrequency = 0.015f;

        private readonly int spaghetti2DRoughnessOctaves = 2;
        private readonly float spaghetti2DRoughnessLacunarity = 2.0f;
        private readonly float spaghetti2DRoughnessGain = 0.5f;
        private readonly float spaghetti2DRoughnessFrequency = 0.015f;

        private readonly int pillarOctaves = 3;
        private readonly float pillarLacunarity = 2.0f;
        private readonly float pillarGain = 0.5f;
        private readonly float pillarFrequency = 0.015f;

        private readonly int cheeseOctaves = 8;
        private readonly float cheeseLacunarity = 2.0f;
        private readonly float cheeseGain = 1.0f;
        private readonly float cheeseFrequency = 0.015f;

        private readonly int denseCaveOctaves = 5;
        private readonly float denseCaveLacunarity = 2.0f;
        private readonly float denseCaveGain = 0.5f;
        private readonly float denseCaveFrequency = 0.015f;

        private readonly int thermalLakeOctaves = 5;
        private readonly float thermalLakeLacunarity = 2.0f;
        private readonly float thermalLakeGain = 0.5f;
        private readonly float thermalLakeFrequency = 0.015f;

        private readonly int veinGapOctaves = 3;
        private readonly float veinGapLacunarity = 2.0f;
        private readonly float veinGapGain = 0.5f;
        private readonly float veinGapFrequency = 0.015f;

        private readonly int spaghetti2DOctaves = 4;
        private readonly float spaghetti2DLacunarity = 2.0f;
        private readonly float spaghetti2DGain = 0.5f;
        private readonly float spaghetti2DFrequency = 0.015f;

        private readonly int spaghetti3DOctaves = 4;
        private readonly float spaghetti3DLacunarity = 2.0f;
        private readonly float spaghetti3DGain = 0.5f;
        private readonly float spaghetti3DFrequency = 0.015f;

        private readonly int caveEntranceOctaves = 3;
        private readonly float caveEntranceLacunarity = 2.0f;
        private readonly float caveEntranceGain = 0.5f;
        private readonly float caveEntranceFrequency = 0.015f;

        private readonly double cheeseFx = 1.0;
        private readonly double cheeseFy = 1.0;
        private readonly double cheeseFz = 1.0;

        private readonly double spaghettiFx = 15.0;
        private readonly double spaghettiFy = 15.0;
        private readonly double spaghettiFz = 15.0;

        private readonly double caveLayerFx = 1.0;
        private readonly double caveLayerFy = 1.0;
        private readonly double caveLayerFz = 1.0;

        private readonly double noodleFx = 60.0;
        private readonly double noodleFy = 60.0;
        private readonly double noodleFz = 60.0;

        private readonly double lavaRiverFx = 1.0;
        private readonly double lavaRiverFy = 0.5;
        private readonly double lavaRiverFz = 1.0;

        public bool IsInitialized => isInitialized;

        public NoiseManager()
        {
        }

        public override bool ShouldLoad(EnumAppSide side)
        {
            return side == EnumAppSide.Server;
        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            this.sapi = api ?? throw new ArgumentNullException(nameof(api), "ICoreServerAPI cannot be null.");

            FastNoiseSafeInit.EnsureSIMDInitialized();
            sapi.Logger.Notification("[CavesAndCaverns] FastNoise2 SIMD Level initialized: {0}", FastNoiseSafeInit.SIMDLevel);
            sapi.Logger.Debug("[CavesAndCaverns] FastNoise static init confirmed, SIMD Level: {0}", FastNoiseSafeInit.SIMDLevel);

            lock (initLock)
            {
                Init();
            }
            sapi.Logger.Notification("[CavesAndCaverns] NoiseManager started.");
        }

        public void Init()
        {
            lock (initLock)
            {
                if (isInitialized || sapi == null) return;
                LoadAllNoises();
                isInitialized = true;
                sapi.Logger.Debug("[CavesAndCaverns] NoiseManager initialized.");
            }
        }

        private void LoadAllNoises()
        {
            var config = CavesAndCavernsCore.ConfigManager.Config;
            long seed = config.Seed != 0 ? config.Seed : sapi.World.Seed;

            try
            {
                // Calculate pool size based on threads, carvers, and layers
                int numCarvers = 18; // Number of unique noise types
                int threadCount = Math.Max(1, Environment.ProcessorCount - 2);
                int layersPerChunk = 8; // Number of chunk layers (256 height / 32 chunk size)
                int bufferFactor = 2; // Extra buffer for peak demand
                int poolSize = threadCount * numCarvers * layersPerChunk * bufferFactor;
                int maxPoolSize = poolSize * 2; // Soft limit to prevent runaway growth

                void InitializeNoisePool(string name, int octaves, float lacunarity, float gain)
                {
                    var pool = new FastNoisePool(poolSize, octaves, lacunarity, gain, sapi, maxPoolSize);
                    noisePools[name] = pool;
                    sapi.Logger.Debug($"[CavesAndCaverns] Initialized noise pool: {name} with {octaves} octaves, lacunarity {lacunarity}, gain {gain}, size {poolSize}, max {maxPoolSize}.");
                }

                InitializeNoisePool("SurfaceRiverNoise", surfaceRiverOctaves, surfaceRiverLacunarity, surfaceRiverGain);
                InitializeNoisePool("UndergroundRiverNoise", undergroundRiverOctaves, undergroundRiverLacunarity, undergroundRiverGain);
                InitializeNoisePool("LavaRiverNoise", lavaRiverOctaves, lavaRiverLacunarity, lavaRiverGain);
                InitializeNoisePool("CanyonNoise", canyonOctaves, canyonLacunarity, canyonGain);
                InitializeNoisePool("CanyonRotationNoise", canyonRotationOctaves, canyonRotationLacunarity, canyonRotationGain);
                InitializeNoisePool("DenseCaveRadiusHorizontalNoise", denseCaveRadiusHorizontalOctaves, denseCaveRadiusHorizontalLacunarity, denseCaveRadiusHorizontalGain);
                InitializeNoisePool("DenseCaveRadiusVerticalNoise", denseCaveRadiusVerticalOctaves, denseCaveRadiusVerticalLacunarity, denseCaveRadiusVerticalGain);
                InitializeNoisePool("CheeseRadiusHorizontalNoise", cheeseRadiusHorizontalOctaves, cheeseRadiusHorizontalLacunarity, cheeseRadiusHorizontalGain);
                InitializeNoisePool("CheeseRadiusVerticalNoise", cheeseRadiusVerticalOctaves, cheeseRadiusVerticalLacunarity, cheeseRadiusVerticalGain);
                InitializeNoisePool("Spaghetti2DRoughnessNoise", spaghetti2DRoughnessOctaves, spaghetti2DRoughnessLacunarity, spaghetti2DRoughnessGain);
                InitializeNoisePool("PillarNoise", pillarOctaves, pillarLacunarity, pillarGain);
                InitializeNoisePool("CheeseNoise", cheeseOctaves, cheeseLacunarity, cheeseGain);
                InitializeNoisePool("DenseCaveNoise", denseCaveOctaves, denseCaveLacunarity, denseCaveGain);
                InitializeNoisePool("ThermalLakeNoise", thermalLakeOctaves, thermalLakeLacunarity, thermalLakeGain);
                InitializeNoisePool("VeinGapNoise", veinGapOctaves, veinGapLacunarity, veinGapGain);
                InitializeNoisePool("Spaghetti2DNoise", spaghetti2DOctaves, spaghetti2DLacunarity, spaghetti2DGain);
                InitializeNoisePool("Spaghetti3DNoise", spaghetti3DOctaves, spaghetti3DLacunarity, spaghetti3DGain);
                InitializeNoisePool("CaveEntranceNoise", caveEntranceOctaves, caveEntranceLacunarity, caveEntranceGain);
            }
            catch (Exception ex)
            {
                sapi.Logger.Error($"[CavesAndCaverns] Failed to initialize noise pools: {ex.Message}");
                sapi.Logger.Debug("[CavesAndCaverns] LoadAllNoises exception details: {0}", ex.StackTrace);
                throw;
            }
        }

        private FastNoise GetThreadLocalNoise(string noiseType)
        {
            if (!noisePools.TryGetValue(noiseType, out FastNoisePool pool))
            {
                sapi.Logger.Error("[CavesAndCaverns] Noise pool for {0} not initialized.", noiseType);
                throw new InvalidOperationException($"Noise pool for {noiseType} not initialized.");
            }
            return pool.Rent();
        }

        private void EnsureSparseNoiseCache()
        {
            if (sparseNoiseCache == null)
            {
                sparseNoiseCache = new Dictionary<(int xStart, int yStart, int zStart, float frequency, int seed), float[]>();
            }
        }

        public float[] Generate3DNoise(FastNoise noiseTemplate, int chunkSize, BlockPos origin, double xzScale, double yScale, int seed, string noiseType)
        {
            if (!isInitialized)
            {
                sapi.Logger.Error("[CavesAndCaverns] NoiseManager not initialized in Generate3DNoise for X={0}, Y={1}, Z={2}", origin.X, origin.Y, origin.Z);
                throw new InvalidOperationException("NoiseManager not initialized.");
            }

            if (noiseTemplate == null)
            {
                sapi.Logger.Error("[CavesAndCaverns] Noise generator is null in Generate3DNoise for X={0}, Y={1}, Z={2}", origin.X, origin.Y, origin.Z);
                throw new ArgumentNullException(nameof(noiseTemplate), "Noise generator is null.");
            }

            if (chunkSize < 1 || chunkSize > 512)
            {
                sapi.Logger.Error("[CavesAndCaverns] Invalid chunkSize: {0} for X={1}, Y={2}, Z={3}", chunkSize, origin.X, origin.Y, origin.Z);
                throw new ArgumentException("chunkSize must be between 1 and 512.");
            }

            float frequency = GetSafeFrequencyFor(noiseType);
            if (xzScale <= 0 || yScale <= 0 || float.IsNaN(frequency) || frequency <= 0 || frequency > 1)
            {
                sapi.Logger.Error("[CavesAndCaverns] Invalid noise parameters: xzScale={0}, yScale={1}, frequency={2} for X={3}, Y={4}, Z={5}", xzScale, yScale, frequency, origin.X, origin.Y, origin.Z);
                throw new ArgumentOutOfRangeException("Unsafe noise scale or frequency detected.");
            }

            int noiseSeedOffset = GetNoiseSeedOffset(noiseType);
            int consistentSeed = (int)sapi.World.Seed + noiseSeedOffset + seed;

            var cacheKey = (origin.X, origin.Y, origin.Z, noiseType, consistentSeed);
            if (noiseCache.TryGetValue(cacheKey, out float[] cachedNoise))
            {
                if (CavesAndCavernsCore.ConfigManager.Config.VerboseLogging == true)
                    sapi.Logger.Debug("[CavesAndCaverns] Cache hit for X={0}, Y={1}, Z={2}, Type={3}, Seed={4}, Thread={5}", origin.X, origin.Y, origin.Z, noiseType, consistentSeed, Thread.CurrentThread.ManagedThreadId);
                return cachedNoise;
            }

            int totalSize = chunkSize * chunkSize * chunkSize;
            float[] noiseOut = new float[totalSize];
            frequency = Math.Max(0.00001f, Math.Min(frequency, 1.0f));

            double xStartDouble = origin.X * xzScale;
            double yStartDouble = origin.Y * yScale;
            double zStartDouble = origin.Z * xzScale;
            int xStart = (int)Math.Max(int.MinValue, Math.Min(int.MaxValue, xStartDouble));
            int yStart = (int)Math.Max(int.MinValue, Math.Min(int.MaxValue, yStartDouble));
            int zStart = (int)Math.Max(int.MinValue, Math.Min(int.MaxValue, zStartDouble));

            if (CavesAndCavernsCore.ConfigManager.Config.VerboseLogging == true)
            {
                sapi.Logger.Debug("[CavesAndCaverns] GenUniformGrid3D debug → noiseOut.Length={0}, expected={1}, xStart={2}, yStart={3}, zStart={4}, xSize={5}, ySize={6}, zSize={7}, frequency={8}, seed={9}, Thread={10}",
                    noiseOut.Length, totalSize, xStart, yStart, zStart, chunkSize, chunkSize, chunkSize, frequency, consistentSeed, Thread.CurrentThread.ManagedThreadId);
            }

            try
            {
                int sparseSize = chunkSize / 8; // Increased sparsity: 4x4x4 grid (sample every 8 blocks)
                int sparseTotalSize = sparseSize * sparseSize * sparseSize;
                float[] sparseNoise;

                // Check thread-local sparse noise cache
                EnsureSparseNoiseCache();
                var sparseCacheKey = (xStart: xStart / 8, yStart: yStart / 8, zStart: zStart / 8, frequency: frequency * 8, seed: consistentSeed);
                if (sparseNoiseCache.TryGetValue(sparseCacheKey, out sparseNoise))
                {
                    if (CavesAndCavernsCore.ConfigManager.Config.VerboseLogging == true)
                        sapi.Logger.Debug("[CavesAndCaverns] Sparse noise cache hit for xStart={0}, yStart={1}, zStart={2}, freq={3}, seed={4}, Thread={5}",
                            sparseCacheKey.xStart, sparseCacheKey.yStart, sparseCacheKey.zStart, sparseCacheKey.frequency, sparseCacheKey.seed, Thread.CurrentThread.ManagedThreadId);
                }
                else
                {
                    sparseNoise = new float[sparseTotalSize];
                    noiseTemplate.GenUniformGrid3D(sparseNoise, xStart / 8, yStart / 8, zStart / 8, sparseSize, sparseSize, sparseSize, frequency * 8, consistentSeed);
                    if (sparseNoiseCache.Count >= SparseCacheSizeLimit)
                    {
                        sparseNoiseCache.Clear();
                        if (CavesAndCavernsCore.ConfigManager.Config.VerboseLogging == true)
                            sapi.Logger.Debug("[CavesAndCaverns] Cleared sparse noise cache to manage memory, Thread={0}", Thread.CurrentThread.ManagedThreadId);
                    }
                    sparseNoiseCache[sparseCacheKey] = sparseNoise;
                    if (CavesAndCavernsCore.ConfigManager.Config.VerboseLogging == true)
                        sapi.Logger.Debug("[CavesAndCaverns] Cached sparse noise for xStart={0}, yStart={1}, zStart={2}, freq={3}, seed={4}, Thread={5}",
                            sparseCacheKey.xStart, sparseCacheKey.yStart, sparseCacheKey.zStart, sparseCacheKey.frequency, sparseCacheKey.seed, Thread.CurrentThread.ManagedThreadId);
                }

                for (int x = 0; x < chunkSize; x++)
                    for (int y = 0; y < chunkSize; y++)
                        for (int z = 0; z < chunkSize; z++)
                        {
                            float fx = x / 8.0f, fy = y / 8.0f, fz = z / 8.0f;
                            int ix0 = (int)fx, iy0 = (int)fy, iz0 = (int)fz;
                            int ix1 = Math.Min(ix0 + 1, sparseSize - 1);
                            int iy1 = Math.Min(iy0 + 1, sparseSize - 1);
                            int iz1 = Math.Min(iz0 + 1, sparseSize - 1);
                            float dx = fx - ix0, dy = fy - iy0, dz = fz - iz0;

                            int idx000 = (iy0 * sparseSize + iz0) * sparseSize + ix0;
                            int idx100 = (iy0 * sparseSize + iz0) * sparseSize + ix1;
                            int idx010 = (iy1 * sparseSize + iz0) * sparseSize + ix0;
                            int idx110 = (iy1 * sparseSize + iz0) * sparseSize + ix1;
                            int idx001 = (iy0 * sparseSize + iz1) * sparseSize + ix0;
                            int idx101 = (iy0 * sparseSize + iz1) * sparseSize + ix1;
                            int idx011 = (iy1 * sparseSize + iz1) * sparseSize + ix0;
                            int idx111 = (iy1 * sparseSize + iz1) * sparseSize + ix1;

                            float n000 = sparseNoise[idx000];
                            float n100 = sparseNoise[idx100];
                            float n010 = sparseNoise[idx010];
                            float n110 = sparseNoise[idx110];
                            float n001 = sparseNoise[idx001];
                            float n101 = sparseNoise[idx101];
                            float n011 = sparseNoise[idx011];
                            float n111 = sparseNoise[idx111];

                            float nx00 = GameMath.Lerp(n000, n100, dx);
                            float nx01 = GameMath.Lerp(n001, n101, dx);
                            float nx10 = GameMath.Lerp(n010, n110, dx);
                            float nx11 = GameMath.Lerp(n011, n111, dx);

                            float nxy0 = GameMath.Lerp(nx00, nx10, dy);
                            float nxy1 = GameMath.Lerp(nx01, nx11, dy);

                            float nxyz = GameMath.Lerp(nxy0, nxy1, dz);

                            int index = (y * chunkSize + z) * chunkSize + x;
                            noiseOut[index] = nxyz;
                        }

                if (noiseType == "Cheese" || noiseType == "Spaghetti2D" || noiseType == "Spaghetti3D")
                {
                    for (int x = 0; x < chunkSize; x++)
                        for (int y = 0; y < chunkSize; y++)
                            for (int z = 0; z < chunkSize; z++)
                            {
                                int index = (y * chunkSize + z) * chunkSize + x;
                                float baseScale = noiseType == "Cheese" ? 1.0f : 1.0f;
                                float deepScale = noiseType == "Cheese" ? 1.5f : 1.1f;
                                noiseOut[index] = ApplyYLevelScaling(noiseOut[index], origin.Y + y, baseScale, deepScale, sapi.World.SeaLevel);
                            }
                }

                if (noiseType == "Cheese" || noiseType == "VeinGap" || noiseType == "Spaghetti3D")
                {
                    lock (lruLock)
                    {
                        if (noiseCache.Count >= CacheSizeLimit)
                        {
                            var oldestKey = lruList.Last.Value;
                            lruList.RemoveLast();
                            noiseCache.TryRemove(oldestKey, out _);
                            if (CavesAndCavernsCore.ConfigManager.Config.VerboseLogging == true)
                                sapi.Logger.Debug("[CavesAndCaverns] Evicted cache entry for X={0}, Y={1}, Z={2}, Type={3}, Seed={4}", oldestKey.x, oldestKey.y, oldestKey.z, oldestKey.noiseType, oldestKey.seed);
                        }
                        noiseCache.TryAdd(cacheKey, noiseOut);
                        lruList.AddFirst(cacheKey);
                        if (CavesAndCavernsCore.ConfigManager.Config.VerboseLogging == true)
                            sapi.Logger.Debug("[CavesAndCaverns] Cached noise for X={0}, Y={1}, Z={2}, Type={3}, Seed={4}, Thread={5}", origin.X, origin.Y, origin.Z, noiseType, consistentSeed, Thread.CurrentThread.ManagedThreadId);
                    }
                }

                if (CavesAndCavernsCore.ConfigManager.Config.VerboseLogging == true)
                {
                    sapi.Logger.Debug("[CavesAndCaverns] Noise sample at X={0}, Y={1}, Z={2}: {3}, Thread={4}", origin.X, origin.Y, origin.Z, noiseOut[0], Thread.CurrentThread.ManagedThreadId);
                }
            }
            catch (Exception ex)
            {
                sapi.Logger.Error($"[CavesAndCaverns] Exception in GenUniformGrid3D at X={0}, Y={1}, Z={2}: {ex.Message}, Thread={3}", origin.X, origin.Y, origin.Z, Thread.CurrentThread.ManagedThreadId);
                sapi.Logger.Debug("[CavesAndCaverns] GenUniformGrid3D exception details: {0}", ex.StackTrace);
                throw;
            }

            return noiseOut;
        }

        public float[] GenerateCheeseNoise(int chunkSize, BlockPos origin, int seed)
        {
            var noiseGen = GetThreadLocalNoise("CheeseNoise");
            try
            {
                return Generate3DNoise(noiseGen, chunkSize, origin, cheeseFx, cheeseFy, seed, "Cheese");
            }
            finally
            {
                noisePools["CheeseNoise"].Return(noiseGen);
            }
        }

        public float[] GenerateSpaghetti2DNoise(int chunkSize, BlockPos origin, int seed)
        {
            var noiseGen = GetThreadLocalNoise("Spaghetti2DNoise");
            try
            {
                return Generate3DNoise(noiseGen, chunkSize, origin, spaghettiFx, spaghettiFy, seed, "Spaghetti2D");
            }
            finally
            {
                noisePools["Spaghetti2DNoise"].Return(noiseGen);
            }
        }

        public float[] GenerateSpaghetti3DNoise(int chunkSize, BlockPos origin, int seed)
        {
            var noiseGen = GetThreadLocalNoise("Spaghetti3DNoise");
            try
            {
                return Generate3DNoise(noiseGen, chunkSize, origin, spaghettiFx, spaghettiFy, seed, "Spaghetti3D");
            }
            finally
            {
                noisePools["Spaghetti3DNoise"].Return(noiseGen);
            }
        }

        public float[] GenerateVeinGapNoise(int chunkSize, BlockPos origin, int seed)
        {
            var noiseGen = GetThreadLocalNoise("VeinGapNoise");
            try
            {
                return Generate3DNoise(noiseGen, chunkSize, origin, noodleFx, noodleFy, seed, "VeinGap");
            }
            finally
            {
                noisePools["VeinGapNoise"].Return(noiseGen);
            }
        }

        public float[] GenerateCaveEntrancesFloat(int chunkSize, BlockPos origin, Random rand, float caveEntranceProbability)
        {
            int totalSize = chunkSize * chunkSize * chunkSize;
            float[] noiseOut = new float[totalSize];
            Array.Fill(noiseOut, 1.0f);
            if (rand.NextDouble() <= caveEntranceProbability)
            {
                var noiseGen = GetThreadLocalNoise("CaveEntranceNoise");
                try
                {
                    noiseOut = Generate3DNoise(noiseGen, chunkSize, origin, caveLayerFx, caveLayerFy, (int)(sapi.World.SeaLevel + 18), "CaveEntrance");
                    int seaLevel = sapi.World.SeaLevel;
                    for (int x = 0; x < chunkSize; x++)
                        for (int y = 0; y < chunkSize; y++)
                            for (int z = 0; z < chunkSize; z++)
                            {
                                int index = (y * chunkSize + z) * chunkSize + x;
                                float weight = Math.Max(0, (float)(origin.Y + y - seaLevel) / (sapi.WorldManager.MapSizeY - seaLevel));
                                noiseOut[index] *= weight;
                            }
                }
                finally
                {
                    noisePools["CaveEntranceNoise"].Return(noiseGen);
                }
            }
            return noiseOut;
        }

        public float GetSurfaceRiverNoise(int x, int z)
        {
            var noiseGen = GetThreadLocalNoise("SurfaceRiverNoise");
            try
            {
                return noiseGen.GenSingle2D(x, z, (int)(sapi.World.Seed + 1));
            }
            finally
            {
                noisePools["SurfaceRiverNoise"].Return(noiseGen);
            }
        }

        public float GetUndergroundRiverNoise(int x, int z)
        {
            var noiseGen = GetThreadLocalNoise("UndergroundRiverNoise");
            try
            {
                return noiseGen.GenSingle2D(x, z, (int)(sapi.World.Seed + 2));
            }
            finally
            {
                noisePools["UndergroundRiverNoise"].Return(noiseGen);
            }
        }

        public float GetLavaRiverNoise(int x, int y, int z)
        {
            var noiseGen = GetThreadLocalNoise("LavaRiverNoise");
            try
            {
                float noise = noiseGen.GenSingle3D(x, y, z, (int)(sapi.World.Seed + 3));
                return ApplyYLevelScaling(noise, y, 1.0f, 1.2f, sapi.World.SeaLevel);
            }
            finally
            {
                noisePools["LavaRiverNoise"].Return(noiseGen);
            }
        }

        public float GetCanyonNoise(int x, int y, int z)
        {
            var noiseGen = GetThreadLocalNoise("CanyonNoise");
            try
            {
                float noise = noiseGen.GenSingle3D(x, y, z, (int)(sapi.World.Seed + 4));
                return ApplyYLevelScaling(noise, y, 1.0f, 1.2f, sapi.World.SeaLevel);
            }
            finally
            {
                noisePools["CanyonNoise"].Return(noiseGen);
            }
        }

        public float GetCanyonRotationNoise(int x, int y, int z)
        {
            var noiseGen = GetThreadLocalNoise("CanyonRotationNoise");
            try
            {
                return noiseGen.GenSingle3D(x, y, z, (int)(sapi.World.Seed + 15));
            }
            finally
            {
                noisePools["CanyonRotationNoise"].Return(noiseGen);
            }
        }

        public float GetDenseCaveNoise(int x, int y, int z)
        {
            var noiseGen = GetThreadLocalNoise("DenseCaveNoise");
            try
            {
                float noise = noiseGen.GenSingle3D(x, y, z, (int)(sapi.World.Seed + 5));
                float thermalNoise = GetThermalLakeNoise(x, y, z);
                return BlendNoise(noise, thermalNoise, 0.3f);
            }
            finally
            {
                noisePools["DenseCaveNoise"].Return(noiseGen);
            }
        }

        public float GetDenseCaveRadiusHorizontalNoise(int x, int y, int z)
        {
            var noiseGen = GetThreadLocalNoise("DenseCaveRadiusHorizontalNoise");
            try
            {
                return noiseGen.GenSingle3D(x, y, z, (int)(sapi.World.Seed + 16));
            }
            finally
            {
                noisePools["DenseCaveRadiusHorizontalNoise"].Return(noiseGen);
            }
        }

        public float GetDenseCaveRadiusVerticalNoise(int x, int y, int z)
        {
            var noiseGen = GetThreadLocalNoise("DenseCaveRadiusVerticalNoise");
            try
            {
                return noiseGen.GenSingle3D(x, y, z, (int)(sapi.World.Seed + 17));
            }
            finally
            {
                noisePools["DenseCaveRadiusVerticalNoise"].Return(noiseGen);
            }
        }

        public float GetCheeseNoise(int x, int y, int z)
        {
            var noiseGen = GetThreadLocalNoise("CheeseNoise");
            try
            {
                float noise = noiseGen.GenSingle3D(x, y, z, (int)(sapi.World.Seed + 6));
                return ApplyYLevelScaling(noise, y, 1.0f, 1.5f, sapi.World.SeaLevel);
            }
            finally
            {
                noisePools["CheeseNoise"].Return(noiseGen);
            }
        }

        public float GetCheeseRadiusHorizontalNoise(int x, int y, int z)
        {
            var noiseGen = GetThreadLocalNoise("CheeseRadiusHorizontalNoise");
            try
            {
                return noiseGen.GenSingle3D(x, y, z, (int)(sapi.World.Seed + 11));
            }
            finally
            {
                noisePools["CheeseRadiusHorizontalNoise"].Return(noiseGen);
            }
        }

        public float GetCheeseRadiusVerticalNoise(int x, int y, int z)
        {
            var noiseGen = GetThreadLocalNoise("CheeseRadiusVerticalNoise");
            try
            {
                return noiseGen.GenSingle3D(x, y, z, (int)(sapi.World.Seed + 12));
            }
            finally
            {
                noisePools["CheeseRadiusVerticalNoise"].Return(noiseGen);
            }
        }

        public float GetThermalLakeNoise(int x, int y, int z)
        {
            var noiseGen = GetThreadLocalNoise("ThermalLakeNoise");
            try
            {
                float noise = noiseGen.GenSingle3D(x, y, z, (int)(sapi.World.Seed + 7));
                return ApplyYLevelScaling(noise, y, 1.0f, 1.2f, sapi.World.SeaLevel);
            }
            finally
            {
                noisePools["ThermalLakeNoise"].Return(noiseGen);
            }
        }

        public float GetVeinGapNoise(int x, int y, int z)
        {
            var noiseGen = GetThreadLocalNoise("VeinGapNoise");
            try
            {
                float noise = noiseGen.GenSingle3D(x, y, z, (int)(sapi.World.Seed + 8));
                return noise;
            }
            finally
            {
                noisePools["VeinGapNoise"].Return(noiseGen);
            }
        }

        public float GetSpaghetti2DNoise(int x, int y, int z)
        {
            var noiseGen = GetThreadLocalNoise("Spaghetti2DNoise");
            try
            {
                float noise = noiseGen.GenSingle3D(x, y, z, (int)(sapi.World.Seed + 9));
                return ApplyYLevelScaling(noise, y, 1.0f, 1.1f, sapi.World.SeaLevel);
            }
            finally
            {
                noisePools["Spaghetti2DNoise"].Return(noiseGen);
            }
        }

        public float GetSpaghetti2DRoughnessNoise(int x, int y, int z)
        {
            var noiseGen = GetThreadLocalNoise("Spaghetti2DRoughnessNoise");
            try
            {
                return noiseGen.GenSingle3D(x, y, z, (int)(sapi.World.Seed + 13));
            }
            finally
            {
                noisePools["Spaghetti2DRoughnessNoise"].Return(noiseGen);
            }
        }

        public float GetSpaghetti3DNoise(int x, int y, int z)
        {
            var noiseGen = GetThreadLocalNoise("Spaghetti3DNoise");
            try
            {
                float noise = noiseGen.GenSingle3D(x, y, z, (int)(sapi.World.Seed + 14));
                return ApplyYLevelScaling(noise, y, 1.0f, 1.1f, sapi.World.SeaLevel);
            }
            finally
            {
                noisePools["Spaghetti3DNoise"].Return(noiseGen);
            }
        }

        public float GetPillarNoise(int x, int y, int z)
        {
            var noiseGen = GetThreadLocalNoise("PillarNoise");
            try
            {
                return noiseGen.GenSingle3D(x, y, z, (int)(sapi.World.Seed + 10));
            }
            finally
            {
                noisePools["PillarNoise"].Return(noiseGen);
            }
        }

        public float GetCaveEntranceNoise(int x, int y, int z)
        {
            var noiseGen = GetThreadLocalNoise("CaveEntranceNoise");
            try
            {
                float noise = noiseGen.GenSingle3D(x, y, z, (int)(sapi.World.Seed + 18));
                int seaLevel = sapi.World.SeaLevel;
                float weight = Math.Max(0, (float)(y - seaLevel) / (sapi.WorldManager.MapSizeY - seaLevel));
                return noise * weight;
            }
            finally
            {
                noisePools["CaveEntranceNoise"].Return(noiseGen);
            }
        }

        public FastNoise GetSurfaceRiverNoiseGenerator() => GetThreadLocalNoise("SurfaceRiverNoise");
        public FastNoise GetUndergroundRiverNoiseGenerator() => GetThreadLocalNoise("UndergroundRiverNoise");
        public FastNoise GetLavaRiverNoiseGenerator() => GetThreadLocalNoise("LavaRiverNoise");
        public FastNoise GetCanyonNoiseGenerator() => GetThreadLocalNoise("CanyonNoise");
        public FastNoise GetCanyonRotationNoiseGenerator() => GetThreadLocalNoise("CanyonRotationNoise");
        public FastNoise GetDenseCaveNoiseGenerator() => GetThreadLocalNoise("DenseCaveNoise");
        public FastNoise GetDenseCaveRadiusHorizontalNoiseGenerator() => GetThreadLocalNoise("DenseCaveRadiusHorizontalNoise");
        public FastNoise GetDenseCaveRadiusVerticalNoiseGenerator() => GetThreadLocalNoise("DenseCaveRadiusVerticalNoise");
        public FastNoise GetCheeseNoiseGenerator() => GetThreadLocalNoise("CheeseNoise");
        public FastNoise GetCheeseRadiusHorizontalNoiseGenerator() => GetThreadLocalNoise("CheeseRadiusHorizontalNoise");
        public FastNoise GetCheeseRadiusVerticalNoiseGenerator() => GetThreadLocalNoise("CheeseRadiusVerticalNoise");
        public FastNoise GetThermalLakeNoiseGenerator() => GetThreadLocalNoise("ThermalLakeNoise");
        public FastNoise GetVeinGapNoiseGenerator() => GetThreadLocalNoise("VeinGapNoise");
        public FastNoise GetSpaghetti2DNoiseGenerator() => GetThreadLocalNoise("Spaghetti2DNoise");
        public FastNoise GetSpaghetti2DRoughnessNoiseGenerator() => GetThreadLocalNoise("Spaghetti2DRoughnessNoise");
        public FastNoise GetSpaghetti3DNoiseGenerator() => throw new InvalidOperationException("Use pool for Spaghetti3DNoise");
        public FastNoise GetPillarNoiseGenerator() => GetThreadLocalNoise("PillarNoise");
        public FastNoise GetCaveEntranceNoiseGenerator() => GetThreadLocalNoise("CaveEntranceNoise");

        private int GetNoiseSeedOffset(string noiseType)
        {
            return noiseType switch
            {
                "Spaghetti2DNoise" => 9,
                "VeinGapNoise" => 8,
                "CheeseNoise" => 6,
                "CaveEntranceNoise" => 18,
                "SurfaceRiverNoise" => 1,
                "UndergroundRiverNoise" => 2,
                "LavaRiverNoise" => 3,
                "CanyonNoise" => 4,
                "CanyonRotationNoise" => 15,
                "DenseCaveNoise" => 5,
                "DenseCaveRadiusHorizontalNoise" => 16,
                "DenseCaveRadiusVerticalNoise" => 17,
                "CheeseRadiusHorizontalNoise" => 11,
                "CheeseRadiusVerticalNoise" => 12,
                "ThermalLakeNoise" => 7,
                "PillarNoise" => 10,
                "Spaghetti2DRoughnessNoise" => 13,
                "Spaghetti3DNoise" => 14,
                _ => 0
            };
        }

        private float GetSafeFrequencyFor(string noiseType)
        {
            return noiseType switch
            {
                "Spaghetti2DNoise" => spaghetti2DFrequency,
                "VeinGapNoise" => veinGapFrequency,
                "CheeseNoise" => cheeseFrequency,
                "CaveEntranceNoise" => caveEntranceFrequency,
                "SurfaceRiverNoise" => surfaceRiverFrequency,
                "UndergroundRiverNoise" => undergroundRiverFrequency,
                "LavaRiverNoise" => lavaRiverFrequency,
                "CanyonNoise" => canyonFrequency,
                "CanyonRotationNoise" => canyonRotationFrequency,
                "DenseCaveNoise" => denseCaveFrequency,
                "DenseCaveRadiusHorizontalNoise" => denseCaveRadiusHorizontalFrequency,
                "DenseCaveRadiusVerticalNoise" => denseCaveRadiusVerticalFrequency,
                "CheeseRadiusHorizontalNoise" => cheeseRadiusHorizontalFrequency,
                "CheeseRadiusVerticalNoise" => cheeseRadiusVerticalFrequency,
                "ThermalLakeNoise" => thermalLakeFrequency,
                "PillarNoise" => pillarFrequency,
                "Spaghetti2DRoughnessNoise" => spaghetti2DRoughnessFrequency,
                "Spaghetti3DNoise" => spaghetti3DFrequency,
                _ => 0.015f
            };
        }

        private float ApplyYLevelScaling(float noiseValue, int y, float baseScale, float deepScale, int thresholdY)
        {
            float scale = y < thresholdY ? deepScale : baseScale;
            return noiseValue * scale;
        }

        private float BlendNoise(float primaryNoise, float secondaryNoise, float weight)
        {
            return primaryNoise * (1f - weight) + secondaryNoise * weight;
        }
    }
}