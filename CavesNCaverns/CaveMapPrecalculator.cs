using CavesAndCaverns.Carvers;
using CavesAndCaverns.Managers;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace CavesAndCaverns
{
    public class CaveMapPrecalculator
    {
        private readonly ICoreServerAPI sapi;
        private readonly MasterCarver masterCarver;
        private readonly NoiseManager noiseManager;
        private readonly Barrier initBarrier;
        private readonly int maxThreads;
        public ConcurrentDictionary<string, Dictionary<string, float[]>> PrecomputedCaveMaps { get; private set; } = new ConcurrentDictionary<string, Dictionary<string, float[]>>();
        private readonly int chunkSize = 32;
        private readonly int precomputeRadius = 5;
        private readonly int maxCacheSize = 100;
        private HashSet<string> chunksInProgress = new HashSet<string>();
        private Vec3d lastPlayerPos = new Vec3d();
        private bool isPrecomputingStarted = false;

        public CaveMapPrecalculator(ICoreServerAPI sapi, MasterCarver masterCarver, NoiseManager noiseManager, int maxThreads)
        {
            this.sapi = sapi;
            this.masterCarver = masterCarver;
            this.noiseManager = noiseManager;
            this.maxThreads = maxThreads;

            int workerThreadCount = (2 * precomputeRadius + 1);
            this.initBarrier = new Barrier(workerThreadCount + 1);

            sapi.Event.PlayerNowPlaying += OnPlayerNowPlaying;
        }

        public void StartPrecomputation()
        {
            if (isPrecomputingStarted) return;

            Thread precalcThread = new Thread(PrecomputeCaveMaps)
            {
                IsBackground = true,
                Name = "CaveMapPrecomputationThread"
            };
            precalcThread.Start();
            isPrecomputingStarted = true;

            sapi.Logger.Notification("[CavesAndCaverns] CaveMapPrecalculator precomputation thread started.");
        }

        private void OnPlayerNowPlaying(IServerPlayer player)
        {
            Task.Run(() =>
            {
                while (true)
                {
                    if (player.Entity == null)
                    {
                        Thread.Sleep(1000);
                        continue;
                    }

                    Vec3d playerPos = player.Entity.Pos.XYZ;
                    if (playerPos.DistanceTo(lastPlayerPos) > chunkSize * 2)
                    {
                        lastPlayerPos = playerPos;
                        PrecomputeNearbyChunks(playerPos);
                    }

                    Thread.Sleep(5000);
                }
            });
        }

        private void PrecomputeNearbyChunks(Vec3d playerPos)
        {
            int playerChunkX = (int)(playerPos.X / chunkSize);
            int playerChunkZ = (int)(playerPos.Z / chunkSize);

            for (int dx = -precomputeRadius; dx <= precomputeRadius; dx++)
            {
                for (int dz = -precomputeRadius; dz <= precomputeRadius; dz++)
                {
                    int chunkX = playerChunkX + dx;
                    int chunkZ = playerChunkZ + dz;
                    string key = $"{chunkX},{chunkZ}";

                    lock (PrecomputedCaveMaps)
                    {
                        if (!PrecomputedCaveMaps.ContainsKey(key) && !chunksInProgress.Contains(key))
                        {
                            chunksInProgress.Add(key);
                            Task.Run(() => PrecomputeChunkColumn(chunkX, chunkZ));
                        }
                    }
                }
            }

            lock (PrecomputedCaveMaps)
            {
                while (PrecomputedCaveMaps.Count > maxCacheSize)
                {
                    string farthestKey = null;
                    double maxDistance = 0;
                    foreach (var entry in PrecomputedCaveMaps)
                    {
                        var parts = entry.Key.Split(',');
                        int cx = int.Parse(parts[0]);
                        int cz = int.Parse(parts[1]);
                        double distance = Math.Sqrt(Math.Pow(cx - playerChunkX, 2) + Math.Pow(cz - playerChunkZ, 2));
                        if (distance > maxDistance)
                        {
                            maxDistance = distance;
                            farthestKey = entry.Key;
                        }
                    }

                    if (farthestKey != null)
                    {
                        PrecomputedCaveMaps.TryRemove(farthestKey, out _);
                        sapi.Logger.Debug("[CavesAndCaverns] Evicted precomputed cave map for chunk {0} to manage cache size", farthestKey);
                    }
                }
            }
        }

        private void PrecomputeCaveMaps()
        {
            try
            {
                int spawnChunkX = (int)(sapi.WorldManager.MapSizeX / 2.0 / chunkSize);
                int spawnChunkZ = (int)(sapi.WorldManager.MapSizeZ / 2.0 / chunkSize);

                Parallel.For(-precomputeRadius, precomputeRadius + 1, new ParallelOptions { MaxDegreeOfParallelism = maxThreads }, dx =>
                {
                    sapi.Logger.Debug("[CavesAndCaverns] Thread {0} waiting for initialization barrier in PrecomputeCaveMaps...", Thread.CurrentThread.ManagedThreadId);
                    initBarrier.SignalAndWait();
                    sapi.Logger.Debug("[CavesAndCaverns] Thread {0} passed initialization barrier in PrecomputeCaveMaps.", Thread.CurrentThread.ManagedThreadId);

                    for (int dz = -precomputeRadius; dz <= precomputeRadius; dz++)
                    {
                        int chunkX = spawnChunkX + dx;
                        int chunkZ = spawnChunkZ + dz;
                        PrecomputeChunkColumn(chunkX, chunkZ);
                    }
                });

                sapi.Logger.Debug("[CavesAndCaverns] Main thread waiting for initialization barrier in PrecomputeCaveMaps...");
                initBarrier.SignalAndWait();
                sapi.Logger.Debug("[CavesAndCaverns] Main thread passed initialization barrier in PrecomputeCaveMaps.");

                sapi.Logger.Notification("[CavesAndCaverns] Initial cave map precomputation around spawn completed.");
            }
            catch (Exception ex)
            {
                sapi.Logger.Error("[CavesAndCaverns] Precomputation failed: {0}", ex);
            }
        }

        public void PrecomputeChunkColumn(int chunkX, int chunkZ)
        {
            try
            {
                string key = $"{chunkX},{chunkZ}";
                int worldHeight = sapi.WorldManager.MapSizeY;
                var config = CavesAndCavernsCore.ConfigManager.Config;
                Dictionary<string, float[]> noiseMaps = new Dictionary<string, float[]>();
                Random rand = new Random((int)(sapi.World.Seed + chunkX + chunkZ));
                int baseSeed = (int)sapi.World.Seed;

                Parallel.For(0, worldHeight / chunkSize, new ParallelOptions { MaxDegreeOfParallelism = maxThreads }, ySegment =>
                {
                    int yBase = ySegment * chunkSize;
                    BlockPos origin = new BlockPos(chunkX * chunkSize, yBase, chunkZ * chunkSize);
                    int totalSize = chunkSize * chunkSize * chunkSize;
                    var localNoiseMaps = new Dictionary<string, float[]>();

                    if (config.EnableSurfaceRivers && rand.NextDouble() <= config.SurfaceRiverProbability)
                    {
                        float[] surfaceRiverNoise = noiseManager.Generate3DNoise(noiseManager.GetSurfaceRiverNoiseGenerator(), chunkSize, origin, 1.0, 1.0, baseSeed + 1, "SurfaceRiver");
                        localNoiseMaps[$"SurfaceRiver_{yBase}"] = surfaceRiverNoise;
                    }

                    if (config.EnableCheeseCaves && rand.NextDouble() <= config.CheeseProbability)
                    {
                        float[] cheeseNoise = noiseManager.GenerateCheeseNoise(chunkSize, origin, baseSeed + 6);
                        localNoiseMaps[$"Cheese_{yBase}"] = cheeseNoise;
                    }

                    if (config.EnableSpaghetti2D && rand.NextDouble() <= config.SpaghettiProbability)
                    {
                        float[] spaghetti2DNoise = noiseManager.GenerateSpaghetti2DNoise(chunkSize, origin, baseSeed + 9);
                        localNoiseMaps[$"Spaghetti2D_{yBase}"] = spaghetti2DNoise;
                    }

                    if (config.EnableSpaghetti3D && rand.NextDouble() <= config.Spaghetti3DProbability)
                    {
                        float[] spaghetti3DNoise = noiseManager.GenerateSpaghetti3DNoise(chunkSize, origin, baseSeed + 14);
                        localNoiseMaps[$"Spaghetti3D_{yBase}"] = spaghetti3DNoise;
                    }

                    if (config.EnableVeinGaps && rand.NextDouble() <= config.VeinGapProbability)
                    {
                        float[] veinGapNoise = noiseManager.GenerateVeinGapNoise(chunkSize, origin, baseSeed + 8);
                        localNoiseMaps[$"VeinGap_{yBase}"] = veinGapNoise;
                    }

                    if (config.EnableCaveEntrances)
                    {
                        float[] caveEntranceNoise = noiseManager.GenerateCaveEntrancesFloat(chunkSize, origin, rand, 0.5f);
                        localNoiseMaps[$"CaveEntrance_{yBase}"] = caveEntranceNoise;
                    }

                    if (config.EnableCanyons && rand.NextDouble() <= config.CanyonProbability)
                    {
                        float[] canyonNoise = new float[totalSize];
                        for (int x = 0; x < chunkSize; x++)
                            for (int y = 0; y < chunkSize; y++)
                                for (int z = 0; z < chunkSize; z++)
                                {
                                    int index = (y * chunkSize + z) * chunkSize + x;
                                    canyonNoise[index] = noiseManager.GetCanyonNoise(origin.X + x, origin.Y + y, origin.Z + z);
                                }
                        localNoiseMaps[$"Canyon_{yBase}"] = canyonNoise;
                    }

                    if (config.EnableUndergroundRivers && rand.NextDouble() <= config.UndergroundRiverProbability)
                    {
                        float[] undergroundRiverNoise = new float[totalSize];
                        for (int x = 0; x < chunkSize; x++)
                            for (int y = 0; y < chunkSize; y++)
                                for (int z = 0; z < chunkSize; z++)
                                {
                                    int index = (y * chunkSize + z) * chunkSize + x;
                                    undergroundRiverNoise[index] = noiseManager.GetUndergroundRiverNoise(origin.X + x, origin.Z + z);
                                }
                        localNoiseMaps[$"UndergroundRiver_{yBase}"] = undergroundRiverNoise;
                    }

                    if (config.EnableLavaRivers && rand.NextDouble() <= config.LavaRiverProbability)
                    {
                        float[] lavaRiverNoise = new float[totalSize];
                        for (int x = 0; x < chunkSize; x++)
                            for (int y = 0; y < chunkSize; y++)
                                for (int z = 0; z < chunkSize; z++)
                                {
                                    int index = (y * chunkSize + z) * chunkSize + x;
                                    lavaRiverNoise[index] = noiseManager.GetLavaRiverNoise(origin.X + x, origin.Y + y, origin.Z + z);
                                }
                        localNoiseMaps[$"LavaRiver_{yBase}"] = lavaRiverNoise;
                    }

                    if (config.EnablePillars && rand.NextDouble() <= config.PillarProbability)
                    {
                        float[] pillarNoise = new float[totalSize];
                        for (int x = 0; x < chunkSize; x++)
                            for (int y = 0; y < chunkSize; y++)
                                for (int z = 0; z < chunkSize; z++)
                                {
                                    int index = (y * chunkSize + z) * chunkSize + x;
                                    pillarNoise[index] = noiseManager.GetPillarNoise(origin.X + x, origin.Y + y, origin.Z + z);
                                }
                        localNoiseMaps[$"Pillar_{yBase}"] = pillarNoise;
                    }

                    if (config.EnableThermalLakes && rand.NextDouble() <= config.ThermalLakeProbability)
                    {
                        float[] thermalLakeNoise = new float[totalSize];
                        for (int x = 0; x < chunkSize; x++)
                            for (int y = 0; y < chunkSize; y++)
                                for (int z = 0; z < chunkSize; z++)
                                {
                                    int index = (y * chunkSize + z) * chunkSize + x;
                                    thermalLakeNoise[index] = noiseManager.GetThermalLakeNoise(origin.X + x, origin.Y + y, origin.Z + z);
                                }
                        localNoiseMaps[$"ThermalLake_{yBase}"] = thermalLakeNoise;
                    }

                    lock (noiseMaps)
                    {
                        foreach (var kvp in localNoiseMaps)
                        {
                            noiseMaps[kvp.Key] = kvp.Value;
                        }
                    }
                });

                PrecomputedCaveMaps.TryAdd(key, noiseMaps);
                lock (chunksInProgress)
                {
                    chunksInProgress.Remove(key);
                }
                sapi.Logger.Notification("[CavesAndCaverns] Precomputed noise data for chunk X={0}, Z={1}", chunkX, chunkZ);
            }
            catch (Exception ex)
            {
                sapi.Logger.Error("[CavesAndCaverns] Failed to precompute noise data for chunk X={0}, Z={1}: {2}", chunkX, chunkZ, ex);
                lock (chunksInProgress)
                {
                    chunksInProgress.Remove($"{chunkX},{chunkZ}");
                }
            }
        }
    }
}