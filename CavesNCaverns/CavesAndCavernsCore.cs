using CavesAndCaverns.Carvers;
using CavesAndCaverns.Config;
using CavesAndCaverns.Managers;
using CavesAndCaverns.PostGen;
using System;
using System.Threading;
using System.Threading.Tasks;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace CavesAndCaverns
{
    public class CavesAndCavernsCore : ModSystem
    {
        public static ICoreServerAPI ServerAPI;
        public static ConfigManager ConfigManager;
        public static ConfigWatcher ConfigWatcher;
        public static NoiseManager NoiseManager;
        public static BiomeHandler BiomeHandler;
        public static MasterCarver MasterCarver;
        public static PostProcessor PostProcessor;
        public static FluidManager FluidManager;
        public static CaveMapPrecalculator Precalculator;
        private static bool IsRegenContext = false;
        private int maxThreads;

        public override double ExecuteOrder() => 10;

        public override void Start(ICoreAPI api)
        {
            base.Start(api);
            api.Logger.Notification("[CavesAndCaverns] Starting mod initialization...");
            ConfigManager = new ConfigManager(api);
            ConfigWatcher = new ConfigWatcher(api, ConfigManager, OnConfigReloaded);
            if (api.Side == EnumAppSide.Server)
            {
                api.Logger.Notification("[CavesAndCaverns] Attempting to load config...");
                try
                {
                    ConfigManager.Load();
                    api.Logger.Notification("[CavesAndCaverns] Config loaded: DebugInverseWorld={0}, SurfaceRiverProbability={1}, UseInvertedWorld={2}",
                        ConfigManager.Config.DebugInverseWorld, ConfigManager.Config.SurfaceRiverProbability, ConfigManager.Config.UseInvertedWorld);
                    if (ConfigManager.Config.VerboseLogging == true)
                        api.Logger.Debug("[CavesAndCaverns] Verbose logging enabled in Start.");
                }
                catch (Exception ex)
                {
                    api.Logger.Error("[CavesAndCaverns] Failed to load config: {0}. Using defaults.", ex.Message);
                    ConfigManager.Config = ConfigManager.GetDefaultConfig();
                    ConfigManager.Save();
                }
            }
            api.Logger.Notification("[CavesAndCaverns] Config initialized.");
        }

        public override void StartServerSide(ICoreServerAPI sapi)
        {
            try
            {
                ServerAPI = sapi;
                sapi.Logger.Notification("[CavesAndCaverns] Starting server-side initialization...");

                FastNoiseSafeInit.EnsureSIMDInitialized();
                sapi.Logger.Notification("[CavesAndCaverns] FastNoise2 SIMD Level initialized: {0}", FastNoiseSafeInit.SIMDLevel);

                if (ConfigManager == null || ConfigManager.Config == null)
                {
                    sapi.Logger.Error("[CavesAndCaverns] ConfigManager or Config is null in StartServerSide.");
                    ConfigManager = new ConfigManager(sapi);
                    ConfigManager.Load();
                }

                GlassBlockManager.Initialize();

                sapi.Logger.Notification("[CavesAndCaverns] Using standard worldgen pipeline with added mantle layer.");

                NoiseManager = new NoiseManager();
                NoiseManager.StartServerSide(sapi);
                if (!NoiseManager.IsInitialized)
                {
                    sapi.Logger.Error("[CavesAndCaverns] NoiseManager failed to initialize.");
                    throw new InvalidOperationException("NoiseManager initialization failed.");
                }
                else if (ConfigManager.Config.VerboseLogging == true)
                {
                    sapi.Logger.Debug("[CavesAndCaverns] NoiseManager initialized successfully.");
                }

                BiomeHandler = new BiomeHandler(sapi);
                MasterCarver = new MasterCarver(sapi);
                PostProcessor = new PostProcessor(sapi);
                FluidManager = new FluidManager(sapi);

                maxThreads = Math.Max(1, Environment.ProcessorCount - 4);
                Precalculator = new CaveMapPrecalculator(sapi, MasterCarver, NoiseManager, maxThreads);

                sapi.Logger.Notification("[CavesAndCaverns] All managers initialized.");

                sapi.Event.ChunkColumnGeneration(OnTerrainGen, EnumWorldGenPass.Terrain, "standard");
                sapi.Event.ChunkColumnGeneration(OnChunkColumnGen, EnumWorldGenPass.TerrainFeatures, "standard");
                sapi.Logger.Notification("[CavesAndCaverns] Chunk column generation events registered for Terrain and TerrainFeatures.");

                ConfigWatcher.Start();
                RegisterCommands(sapi);

                Precalculator.StartPrecomputation();

                sapi.Logger.Notification("[CavesAndCaverns] Server-side initialization completed.");
                if (ConfigManager.Config.VerboseLogging == true)
                    sapi.Logger.Debug("[CavesAndCaverns] Server-side initialization completed with maxThreads={0}", maxThreads);
            }
            catch (Exception ex)
            {
                sapi.Logger.Error("[CavesAndCaverns] Server-side initialization failed: {0}", ex);
                throw;
            }
        }

        private void RegisterCommands(ICoreServerAPI sapi)
        {
            sapi.Logger.Notification("[CavesAndCaverns] Starting command registration...");
            var cmd = sapi.ChatCommands.Create("cavesandcaverns")
                .WithDescription("Manage CavesAndCaverns settings and generation")
                .RequiresPrivilege(Privilege.root);

            cmd.BeginSubCommand("test")
                .WithDescription("Test the command system")
                .HandleWith(args =>
                {
                    var player = args.Caller.Player as IServerPlayer;
                    if (player == null) return TextCommandResult.Error("This command must be run by a player.");
                    player.SendMessage(0, "Test command works!", EnumChatType.Notification);
                    if (ConfigManager.Config.VerboseLogging == true)
                        sapi.Logger.Debug("[CavesAndCaverns] Test command executed by {0}", player.PlayerName);
                    return TextCommandResult.Success();
                })
                .EndSubCommand();

            cmd.BeginSubCommand("reload")
                .WithDescription("Reload the CavesAndCaverns configuration")
                .HandleWith(args =>
                {
                    var player = args.Caller.Player as IServerPlayer;
                    if (player == null) return TextCommandResult.Error("This command must be run by a player.");
                    ConfigManager.Load();
                    ServerAPI.Logger.Notification("[CavesAndCaverns] Config reloaded: DebugInverseWorld={0}, SurfaceRiverProbability={1}",
                        ConfigManager.Config.DebugInverseWorld, ConfigManager.Config.SurfaceRiverProbability);
                    if (ConfigManager.Config.VerboseLogging == true)
                        ServerAPI.Logger.Debug("[CavesAndCaverns] Config reloaded by {0}", player.PlayerName);
                    player.SendMessage(0, "CavesAndCaverns config reloaded.", EnumChatType.Notification);
                    return TextCommandResult.Success();
                })
                .EndSubCommand();

            cmd.BeginSubCommand("config")
                .WithDescription("Modify CavesAndCaverns configuration settings")
                .BeginSubCommand("set")
                    .WithDescription("Set a configuration value")
                    .WithArgs(
                        sapi.ChatCommands.Parsers.Word("key", null),
                        sapi.ChatCommands.Parsers.Word("value", null)
                    )
                    .HandleWith(args =>
                    {
                        var player = args.Caller.Player as IServerPlayer;
                        if (player == null) return TextCommandResult.Error("This command must be run by a player.");
                        string key = args[0].ToString();
                        string valueStr = args[1].ToString();
                        var config = ConfigManager.Config;
                        bool changed = false;

                        if (bool.TryParse(valueStr, out bool boolValue))
                        {
                            switch (key.ToLower())
                            {
                                case "enablesurfacerivers": config.EnableSurfaceRivers = boolValue; changed = true; break;
                                case "enablespaghetti2d": config.EnableSpaghetti2D = boolValue; changed = true; break;
                                case "enablespaghetti3d": config.EnableSpaghetti3D = boolValue; changed = true; break;
                                case "enablecanyons": config.EnableCanyons = boolValue; changed = true; break;
                                case "enabledensecaves": config.EnableDenseCaves = boolValue; changed = true; break;
                                case "enablecheesecaves": config.EnableCheeseCaves = boolValue; changed = true; break;
                                case "enablethermallakes": config.EnableThermalLakes = boolValue; changed = true; break;
                                case "enableundergroundrivers": config.EnableUndergroundRivers = boolValue; changed = true; break;
                                case "enablelavarivers": config.EnableLavaRivers = boolValue; changed = true; break;
                                case "enableveingaps": config.EnableVeinGaps = boolValue; changed = true; break;
                                case "enablepillars": config.EnablePillars = boolValue; changed = true; break;
                                case "enablecaveentrances": config.EnableCaveEntrances = boolValue; changed = true; break;
                                case "debugglasscaves": config.DebugGlassCaves = boolValue; changed = true; break;
                                case "debuginverseworld": config.DebugInverseWorld = boolValue; changed = true; break;
                                case "debugglasssurfacerivers": config.DebugGlassSurfaceRivers = boolValue; changed = true; break;
                                case "debugglassundergroundrivers": config.DebugGlassUndergroundRivers = boolValue; changed = true; break;
                                case "debugglasslavarivers": config.DebugGlassLavaRivers = boolValue; changed = true; break;
                                case "debugglassmantle": config.DebugGlassMantle = boolValue; changed = true; break;
                                case "enablemantlelayer": config.EnableMantleLayer = boolValue; changed = true; break;
                                case "useinvertedworld": config.UseInvertedWorld = boolValue; changed = true; break;
                            }
                        }
                        else if (float.TryParse(valueStr, out float floatValue))
                        {
                            switch (key.ToLower())
                            {
                                case "surfaceriverprobability": config.SurfaceRiverProbability = floatValue; changed = true; break;
                                case "spaghettiprobability": config.SpaghettiProbability = floatValue; changed = true; break;
                                case "spaghetti3dprobability": config.Spaghetti3DProbability = floatValue; changed = true; break;
                                case "canyonprobability": config.CanyonProbability = floatValue; changed = true; break;
                                case "densecaveprobability": config.DenseCaveProbability = floatValue; changed = true; break;
                                case "cheeseprobability": config.CheeseProbability = floatValue; changed = true; break;
                                case "thermallakeprobability": config.ThermalLakeProbability = floatValue; changed = true; break;
                                case "undergroundriverprobability": config.UndergroundRiverProbability = floatValue; changed = true; break;
                                case "lavariverprobability": config.LavaRiverProbability = floatValue; changed = true; break;
                                case "veingapprobability": config.VeinGapProbability = floatValue; changed = true; break;
                                case "pillarprobability": config.PillarProbability = floatValue; changed = true; break;
                                case "caveentranceprobability": config.CaveEntranceProbability = floatValue; changed = true; break;
                                case "wetbiomeriverchancemultiplier": config.WetBiomeRiverChanceMultiplier = floatValue; changed = true; break;
                            }
                        }
                        else if (int.TryParse(valueStr, out int intValue))
                        {
                            switch (key.ToLower())
                            {
                                case "riverwidthmin": config.RiverWidthMin = intValue; changed = true; break;
                                case "riverwidthmax": config.RiverWidthMax = intValue; changed = true; break;
                                case "surfaceriverwidthmin": config.SurfaceRiverWidthMin = intValue; changed = true; break;
                                case "surfaceriverwidthmax": config.SurfaceRiverWidthMax = intValue; changed = true; break;
                                case "glowwormcountmin": config.GlowWormCountMin = intValue; changed = true; break;
                                case "glowwormcountmax": config.GlowWormCountMax = intValue; changed = true; break;
                                case "glowwormlightlevel": config.GlowWormLightLevel = intValue; changed = true; break;
                                case "mantleminthickness": config.MantleMinThickness = intValue; changed = true; break;
                                case "mantlemaxthickness": config.MantleMaxThickness = intValue; changed = true; break;
                                case "seed": config.Seed = intValue; changed = true; break;
                            }
                        }

                        if (changed)
                        {
                            ConfigManager.Save();
                            ServerAPI.Logger.Notification("[CavesAndCaverns] Set {0} to {1}", key, valueStr);
                            if (ConfigManager.Config.VerboseLogging == true)
                                ServerAPI.Logger.Debug("[CavesAndCaverns] Config set {0}={1} by {2}", key, valueStr, player.PlayerName);
                            player.SendMessage(0, $"Set {key} to {valueStr}. Regen area with /cnc regen.", EnumChatType.Notification);
                        }
                        else
                        {
                            player.SendMessage(0, $"Invalid key '{key}' or value '{valueStr}'.", EnumChatType.Notification);
                        }
                        return TextCommandResult.Success();
                    })
                .EndSubCommand()
                .EndSubCommand();

            cmd.BeginSubCommand("regen")
                .WithDescription("Regenerate chunks around the player")
                .WithArgs(
                    sapi.ChatCommands.Parsers.OptionalInt("radius", 1)
                )
                .HandleWith(args =>
                {
                    var player = args.Caller.Player as IServerPlayer;
                    if (player?.Entity == null)
                    {
                        ServerAPI.Logger.Error("[CavesAndCaverns] Regen command failed: Player entity is null!");
                        return TextCommandResult.Error("This command must be run by a player.");
                    }

                    int radius = (int)args[0];
                    if (radius < 1 || radius > 10)
                    {
                        player.SendMessage(0, "Radius must be between 1 and 10", EnumChatType.Notification);
                        return TextCommandResult.Success();
                    }

                    Vec3d pos = player.Entity.Pos.XYZ;
                    int chunkX = (int)(pos.X / 32);
                    int chunkZ = (int)(pos.Z / 32);

                    ServerAPI.Logger.Notification($"[CavesAndCaverns] Regenerating {radius * 2 + 1}x{radius * 2 + 1} chunks around {chunkX},{chunkZ} for {player.PlayerName}");
                    if (ConfigManager.Config.VerboseLogging == true)
                        ServerAPI.Logger.Debug("[CavesAndCaverns] Regen initiated by {0} at X={1}, Z={2} with radius={3}", player.PlayerName, chunkX, chunkZ, radius);

                    int chunkHeight = ServerAPI.WorldManager.MapSizeY / 32;

                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        for (int dz = -radius; dz <= radius; dz++)
                        {
                            int cx = chunkX + dx;
                            int cz = chunkZ + dz;

                            ServerAPI.WorldManager.DeleteChunkColumn(cx, cz);
                            ServerAPI.WorldManager.LoadChunkColumnFast(cx, cz);
                            var chunks = new IServerChunk[chunkHeight];
                            for (int cy = 0; cy < chunkHeight; cy++)
                            {
                                chunks[cy] = ServerAPI.WorldManager.GetChunk(cx, cy, cz);
                                if (chunks[cy] == null)
                                {
                                    ServerAPI.Logger.Error($"[CavesAndCaverns] Failed to load chunk X={cx}, Y={cy}, Z={cz} during regen.");
                                    return TextCommandResult.Error($"Chunk load failed at X={cx}, Z={cz}");
                                }
                            }

                            string key = $"{cx},{cz}";
                            lock (Precalculator.PrecomputedCaveMaps)
                            {
                                if (Precalculator.PrecomputedCaveMaps.ContainsKey(key))
                                {
                                    Precalculator.PrecomputedCaveMaps.TryRemove(key, out _);
                                    ServerAPI.Logger.Debug($"[CavesAndCaverns] Cleared precomputed cave map for chunk X={cx}, Z={cz}");
                                    if (ConfigManager.Config.VerboseLogging == true)
                                        ServerAPI.Logger.Debug("[CavesAndCaverns] Precomputed cave map cleared for X={0}, Z={1}", cx, cz);
                                }
                            }

                            IsRegenContext = true;
                            var regenRequest = new RegenChunkColumnGenerateRequest(cx, cz, chunks);
                            OnTerrainGen(regenRequest);
                            GenerateChunkColumn(regenRequest);
                            for (int cy = 0; cy < chunkHeight; cy++)
                            {
                                chunks[cy].MarkModified();
                                ServerAPI.WorldManager.BroadcastChunk(cx, cy, cz, true);
                                ServerAPI.Logger.Debug($"[CavesAndCaverns] Marked and broadcast chunk X={cx}, Y={cy}, Z={cz} as modified in regen.");
                                if (ConfigManager.Config.VerboseLogging == true)
                                    ServerAPI.Logger.Debug("[CavesAndCaverns] Chunk X={0}, Y={1}, Z={2} marked modified and broadcast", cx, cy, cz);
                            }

                            // Relight the entire chunk column during regeneration
                            BlockPos minPos = new BlockPos(cx * 32, 0, cz * 32);
                            BlockPos maxPos = new BlockPos(cx * 32 + 31, ServerAPI.WorldManager.MapSizeY - 1, cz * 32 + 31);
                            ServerAPI.WorldManager.FullRelight(minPos, maxPos, true);
                            ServerAPI.Logger.Debug($"[CavesAndCaverns] Performed full relight for chunk X={cx}, Z={cz} during regen.");

                            IsRegenContext = false;
                        }
                    }

                    ServerAPI.Logger.Notification($"[CavesAndCaverns] Successfully regenerated chunks for {player.PlayerName}");
                    if (ConfigManager.Config.VerboseLogging == true)
                        ServerAPI.Logger.Debug("[CavesAndCaverns] Regen completed for {0} around X={1}, Z={2}", player.PlayerName, chunkX, chunkZ);
                    player.SendMessage(0, $"[CavesAndCaverns] Chunks regenerated. Please move away and back to see changes.", EnumChatType.Notification);
                    return TextCommandResult.Success();
                })
                .EndSubCommand();

            cmd.WithAlias("cnc");
            sapi.Logger.Notification("[CavesAndCaverns] Command registration complete.");
        }

        public void OnConfigReloaded(CavesConfig newConfig)
        {
            ConfigManager.Config = newConfig;
            ServerAPI.Logger.Notification("[CavesAndCaverns] Config reloaded: DebugInverseWorld={0}, SurfaceRiverProbability={1}",
                newConfig.DebugInverseWorld, newConfig.SurfaceRiverProbability);
            if (ConfigManager.Config.VerboseLogging == true)
                ServerAPI.Logger.Debug("[CavesAndCaverns] Config reloaded with VerboseLogging={0}", newConfig.VerboseLogging);
        }

        private void OnTerrainGen(IChunkColumnGenerateRequest request)
        {
            var blockAccessor = ServerAPI.World.BlockAccessor;
            if (blockAccessor == null)
            {
                ServerAPI.Logger.Error("[CavesAndCaverns] BlockAccessor is null in OnTerrainGen for X={0}, Z={1}", request.ChunkX, request.ChunkZ);
                return;
            }

            int chunkX = request.ChunkX;
            int chunkZ = request.ChunkZ;
            int chunkSize = 32;
            int mantleBlockId = ServerAPI.World.GetBlock(new AssetLocation("game:mantle")).Id;
            int mantleCount = 0;

            var noise = new NormalizedSimplexNoise(new double[] { 0.8 }, new double[] { 0.05 }, ServerAPI.WorldManager.Seed);
            int minThickness = ConfigManager.Config.MantleMinThickness;
            int maxThickness = ConfigManager.Config.MantleMaxThickness;

            if (ConfigManager.Config.VerboseLogging == true)
                ServerAPI.Logger.Debug("[CavesAndCaverns] Starting mantle generation for chunk X={0}, Z={1}", chunkX, chunkZ);

            for (int x = 0; x < chunkSize; x++)
            {
                for (int z = 0; z < chunkSize; z++)
                {
                    int worldX = chunkX * chunkSize + x;
                    int worldZ = chunkZ * chunkSize + z;

                    double noiseValue = (noise.Noise(worldX, worldZ) + 1) / 2;
                    int thickness = minThickness + (int)(noiseValue * (maxThickness - minThickness + 1));
                    thickness = GameMath.Clamp(thickness, minThickness, maxThickness);

                    for (int y = 0; y < thickness; y++)
                    {
                        BlockPos pos = new BlockPos(worldX, y, worldZ);
                        blockAccessor.SetBlock(mantleBlockId, pos);
                        blockAccessor.MarkBlockDirty(pos);
                        // Mark neighboring blocks to improve initial lighting propagation
                        for (int dx = -1; dx <= 1; dx++)
                            for (int dy = -1; dy <= 1; dy++)
                                for (int dz = -1; dz <= 1; dz++)
                                {
                                    if (dx == 0 && dy == 0 && dz == 0) continue;
                                    BlockPos neighborPos = new BlockPos(worldX + dx, y + dy, worldZ + dz);
                                    if (neighborPos.Y >= 0 && neighborPos.Y < ServerAPI.WorldManager.MapSizeY)
                                        blockAccessor.MarkBlockDirty(neighborPos);
                                }
                        Interlocked.Increment(ref mantleCount);
                    }
                }
            }

            // Perform a full relight of the chunk column after mantle generation
            BlockPos minPos = new BlockPos(chunkX * chunkSize, 0, chunkZ * chunkSize);
            BlockPos maxPos = new BlockPos(chunkX * chunkSize + chunkSize - 1, ServerAPI.WorldManager.MapSizeY - 1, chunkZ * chunkSize + chunkSize - 1);
            ServerAPI.WorldManager.FullRelight(minPos, maxPos, true);
            ServerAPI.Logger.Debug($"[CavesAndCaverns] Performed full relight for chunk X={chunkX}, Z={chunkZ} after mantle generation.");

            ServerAPI.Logger.Notification("[CavesAndCaverns] Applied {0} mantle blocks in mantle layer for chunk X={1}, Z={2}", mantleCount, chunkX, chunkZ);
            if (ConfigManager.Config.VerboseLogging == true)
                ServerAPI.Logger.Debug("[CavesAndCaverns] Mantle generation completed for chunk X={0}, Z={1} with {2} blocks", chunkX, chunkZ, mantleCount);
        }

        private void OnChunkColumnGen(IChunkColumnGenerateRequest request)
        {
            var chunks = request.Chunks;
            int chunkHeight = ServerAPI.WorldManager.MapSizeY / 32;
            if (chunks.Length != chunkHeight)
            {
                ServerAPI.Logger.Error("[CavesAndCaverns] Chunk array length mismatch: expected {0}, got {1} for X={2}, Z={3}", chunkHeight, chunks.Length, request.ChunkX, request.ChunkZ);
                return;
            }
            if (ConfigManager.Config.VerboseLogging == true)
                ServerAPI.Logger.Debug("[CavesAndCaverns] Starting OnChunkColumnGen for X={0}, Z={1}", request.ChunkX, request.ChunkZ);

            GenerateChunkColumn(request);

            for (int cy = 0; cy < chunkHeight; cy++)
            {
                if (chunks[cy] != null)
                {
                    chunks[cy].MarkModified();
                    ServerAPI.WorldManager.BroadcastChunk(request.ChunkX, cy, request.ChunkZ, true);
                    ServerAPI.Logger.Debug($"[CavesAndCaverns] Marked and broadcast chunk X={request.ChunkX}, Y={cy}, Z={request.ChunkZ} as modified.");
                    if (ConfigManager.Config.VerboseLogging == true)
                        ServerAPI.Logger.Debug("[CavesAndCaverns] Chunk X={0}, Y={1}, Z={2} marked and broadcast", request.ChunkX, cy, request.ChunkZ);
                }
                else
                {
                    ServerAPI.Logger.Warning($"[CavesAndCaverns] Chunk at X={request.ChunkX}, Y={cy}, Z={request.ChunkZ} is null after generation!");
                }
            }

            // Perform a full relight of the chunk column after generation
            BlockPos minPos = new BlockPos(request.ChunkX * 32, 0, request.ChunkZ * 32);
            BlockPos maxPos = new BlockPos(request.ChunkX * 32 + 31, ServerAPI.WorldManager.MapSizeY - 1, request.ChunkZ * 32 + 31);
            ServerAPI.WorldManager.FullRelight(minPos, maxPos, true);
            ServerAPI.Logger.Debug($"[CavesAndCaverns] Performed full relight for chunk X={request.ChunkX}, Z={request.ChunkZ} after generation.");

            if (ConfigManager.Config.VerboseLogging == true)
                ServerAPI.Logger.Debug("[CavesAndCaverns] OnChunkColumnGen completed for X={0}, Z={1}", request.ChunkX, request.ChunkZ);
        }

        private void GenerateChunkColumn(IChunkColumnGenerateRequest request)
        {
            if (ConfigManager?.Config == null)
            {
                ServerAPI.Logger.Error("[CavesAndCaverns] Config is null in GenerateChunkColumn for chunk X:{0}, Z:{1}", request.ChunkX, request.ChunkZ);
                return;
            }

            if (ServerAPI == null)
            {
                ServerAPI.Logger.Error("[CavesAndCaverns] ServerAPI is null in GenerateChunkColumn for chunk X:{0}, Z:{1}", request.ChunkX, request.ChunkZ);
                return;
            }

            var config = ConfigManager.Config;
            ServerAPI.Logger.Debug("[CavesAndCaverns] GenerateChunkColumn - Config state: DebugInverseWorld={0}, SurfaceRiverProbability={1}, FromRegen={2}",
                config.DebugInverseWorld, config.SurfaceRiverProbability, IsRegenContext);
            if (config.VerboseLogging == true)
                ServerAPI.Logger.Debug("[CavesAndCaverns] Starting GenerateChunkColumn for X={0}, Z={1}", request.ChunkX, request.ChunkZ);
            ServerAPI.Logger.Notification("[CavesAndCaverns] Generating chunk column at X:{0}, Z:{1} in TerrainFeatures pass", request.ChunkX, request.ChunkZ);

            int chunkSize = 32;
            int worldHeight = ServerAPI.WorldManager.MapSizeY;
            int chunkX = request.ChunkX;
            int chunkZ = request.ChunkZ;
            var chunks = request.Chunks;
            int totalCarvedCount = 0;

            try
            {
                string key = $"{chunkX},{chunkZ}";

                lock (Precalculator.PrecomputedCaveMaps)
                {
                    if (!Precalculator.PrecomputedCaveMaps.ContainsKey(key) || IsRegenContext)
                    {
                        if (config.VerboseLogging == true)
                            ServerAPI.Logger.Debug("[CavesAndCaverns] Generating new cave map for X={0}, Z={1}", chunkX, chunkZ);
                        Precalculator.PrecomputeChunkColumn(chunkX, chunkZ);
                    }
                    else
                    {
                        ServerAPI.Logger.Debug("[CavesAndCaverns] Using precomputed cave map for chunk X={0}, Z={1}", chunkX, chunkZ);
                        if (config.VerboseLogging == true)
                            ServerAPI.Logger.Debug("[CavesAndCaverns] Loaded precomputed cave map for X={0}, Z={1}", chunkX, chunkZ);
                    }
                }

                for (int yBase = 0; yBase < worldHeight; yBase += chunkSize)
                {
                    BlockPos origin = new BlockPos(chunkX * chunkSize, yBase, chunkZ * chunkSize);
                    int chunkY = yBase / chunkSize;
                    IWorldChunk worldChunk = chunkY < chunks.Length ? chunks[chunkY] : null;
                    MasterCarver.CarveSurface(chunkSize, origin, "defaultBiome", ServerAPI.World.BlockAccessor);
                    totalCarvedCount += MasterCarver.GetCarvedCount();
                    MasterCarver.CarveUnderground(chunkSize, origin, "defaultBiome", worldChunk, ServerAPI.World.BlockAccessor);
                    totalCarvedCount += MasterCarver.GetCarvedCount();
                }

                ServerAPI.Logger.Notification("[CavesAndCaverns] Total carved blocks in chunk {0},{1} (y=0 to {2}): {3}", chunkX, chunkZ, worldHeight - 1, totalCarvedCount);
                if (config.VerboseLogging == true)
                    ServerAPI.Logger.Debug("[CavesAndCaverns] GenerateChunkColumn completed for X={0}, Z={1} with {2} carved blocks", chunkX, chunkZ, totalCarvedCount);
                GlassBlockManager.LogPlacementSummary(chunkX, chunkZ);
            }
            catch (Exception ex)
            {
                ServerAPI.Logger.Error("[CavesAndCaverns] Error during chunk generation for X:{0}, Z:{1}: {2}", chunkX, chunkZ, ex);
                if (config.VerboseLogging == true)
                    ServerAPI.Logger.Debug("[CavesAndCaverns] Exception details: {0}", ex.StackTrace);
            }
        }

        public class CustomChunkColumnGenerateRequest : IChunkColumnGenerateRequest
        {
            public int ChunkX { get; set; }
            public int ChunkZ { get; set; }
            public bool Pregenerate { get; set; }
            public IServerChunk[] Chunks { get; } = new IServerChunk[0];
            public ITreeAttribute ChunkGenParams { get; } = null;
            public ushort[][] NeighbourTerrainHeight { get; } = new ushort[0][];
            public bool RequiresChunkBorderSmoothing { get; } = false;

            public CustomChunkColumnGenerateRequest(int chunkX, int chunkZ, bool pregenerate)
            {
                ChunkX = chunkX;
                ChunkZ = chunkZ;
                Pregenerate = pregenerate;
            }
        }

        public class RegenChunkColumnGenerateRequest : IChunkColumnGenerateRequest
        {
            public int ChunkX { get; set; }
            public int ChunkZ { get; set; }
            public bool Pregenerate { get; set; }
            public IServerChunk[] Chunks { get; }
            public ITreeAttribute ChunkGenParams { get; } = null;
            public ushort[][] NeighbourTerrainHeight { get; } = new ushort[0][];
            public bool RequiresChunkBorderSmoothing { get; } = false;

            public RegenChunkColumnGenerateRequest(int chunkX, int chunkZ, IServerChunk[] chunks)
            {
                ChunkX = chunkX;
                ChunkZ = chunkZ;
                Pregenerate = false;
                Chunks = chunks;
            }
        }
    }
}