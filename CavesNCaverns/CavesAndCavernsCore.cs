using CavesAndCaverns.Carvers;
using CavesAndCaverns.Config;
using CavesAndCaverns.Managers;
using CavesAndCaverns.PostGen;
using System;
using System.Collections.Generic;
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
        public static BedrockNoiseLayer BedrockLayer;
        public static CaveMapPrecalculator Precalculator;
        private static bool IsRegenContext = false;

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

                if (ConfigManager == null || ConfigManager.Config == null)
                {
                    sapi.Logger.Error("[CavesAndCaverns] ConfigManager or Config is null in StartServerSide.");
                    ConfigManager = new ConfigManager(sapi);
                    ConfigManager.Load();
                }

                GlassBlockManager.Initialize();

                IWorldGenHandler handler = sapi.Event.GetRegisteredWorldGenHandlers("standard");

                if (ConfigManager.Config.UseInvertedWorld)
                {
                    sapi.Logger.Notification("[CavesAndCaverns] UseInvertedWorld is true. Modifying worldgen handlers to create a voidworld.");

                    EnumWorldGenPass[] passesToClear = new[]
                    {
                        EnumWorldGenPass.Terrain,
                        EnumWorldGenPass.Vegetation,
                        EnumWorldGenPass.NeighbourSunLightFlood,
                        EnumWorldGenPass.PreDone
                    };

                    foreach (EnumWorldGenPass pass in passesToClear)
                    {
                        int passIndex = (int)pass;
                        if (passIndex >= 0 && passIndex < handler.OnChunkColumnGen.Length)
                        {
                            List<ChunkColumnGenerationDelegate> passHandlers = handler.OnChunkColumnGen[passIndex];
                            if (passHandlers != null)
                            {
                                sapi.Logger.Debug("[CavesAndCaverns] Clearing {0} handlers for pass {1}.", passHandlers.Count, pass);
                                passHandlers.Clear();
                            }
                        }
                        else
                        {
                            sapi.Logger.Warning("[CavesAndCaverns] Pass {0} index {1} is out of bounds for OnChunkColumnGen array (length {2}). Skipping.", pass, passIndex, handler.OnChunkColumnGen.Length);
                        }
                    }

                    if (handler.OnMapChunkGen != null)
                    {
                        sapi.Logger.Debug("[CavesAndCaverns] Clearing {0} MapChunkGeneration handlers.", handler.OnMapChunkGen.Count);
                        handler.OnMapChunkGen.Clear();
                    }

                    sapi.Logger.Debug("[CavesAndCaverns] Preserving MapRegionGeneration handlers to ensure climate/biome data is generated.");
                }
                else
                {
                    sapi.Logger.Notification("[CavesAndCaverns] UseInvertedWorld is false. Keeping standard worldgen pipeline.");
                }

                NoiseManager = sapi.ModLoader.GetModSystem<NoiseManager>();
                if (NoiseManager == null)
                {
                    sapi.Logger.Error("[CavesAndCaverns] NoiseManager is null.");
                    throw new InvalidOperationException("Failed to initialize NoiseManager.");
                }
                if (!NoiseManager.IsInitialized)
                {
                    sapi.Logger.Error("[CavesAndCaverns] NoiseManager is not initialized. Attempting to initialize...");
                    NoiseManager.Init();
                }

                BiomeHandler = new BiomeHandler(sapi);
                MasterCarver = new MasterCarver(sapi);
                PostProcessor = new PostProcessor(sapi);
                FluidManager = new FluidManager(sapi);
                BedrockLayer = new BedrockNoiseLayer(sapi, NoiseManager);
                Precalculator = new CaveMapPrecalculator(sapi, MasterCarver, NoiseManager);

                sapi.Logger.Notification("[CavesAndCaverns] All managers initialized.");

                sapi.Event.ChunkColumnGeneration(OnChunkColumnGen, EnumWorldGenPass.TerrainFeatures, "standard");
                sapi.Logger.Notification("[CavesAndCaverns] Chunk column generation events registered for TerrainFeatures.");

                ConfigWatcher.Start();

                sapi.Logger.Notification("[CavesAndCaverns] Starting command registration...");
                var cmd = sapi.ChatCommands.Create("cavesandcaverns")
                    .WithDescription("Manage CavesAndCaverns settings and generation")
                    .RequiresPrivilege(Privilege.root);

                cmd.BeginSubCommand("test")
                    .WithDescription("Test the command system")
                    .HandleWith(args =>
                    {
                        var player = args.Caller.Player as IServerPlayer;
                        if (player == null)
                        {
                            sapi.Logger.Error("[CavesAndCaverns] Test command failed: Caller is not a player.");
                            return TextCommandResult.Error("This command must be run by a player.");
                        }
                        player.SendMessage(0, "Test command works!", EnumChatType.Notification);
                        return TextCommandResult.Success();
                    })
                    .EndSubCommand();

                cmd.BeginSubCommand("reload")
                    .WithDescription("Reload the CavesAndCaverns configuration")
                    .HandleWith(args =>
                    {
                        var player = args.Caller.Player as IServerPlayer;
                        if (player == null)
                        {
                            sapi.Logger.Error("[CavesAndCaverns] Reload command failed: Caller is not a player.");
                            return TextCommandResult.Error("This command must be run by a player.");
                        }
                        ConfigManager.Load();
                        ServerAPI.Logger.Notification("[CavesAndCaverns] Config reloaded: DebugInverseWorld={0}, SurfaceRiverProbability={1}",
                            ConfigManager.Config.DebugInverseWorld, ConfigManager.Config.SurfaceRiverProbability);
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
                            if (player == null)
                            {
                                sapi.Logger.Error("[CavesAndCaverns] Config set command failed: Caller is not a player.");
                                return TextCommandResult.Error("This command must be run by a player.");
                            }
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
                                    case "enablecanyons": config.EnableCanyons = boolValue; changed = true; break;
                                    case "enabledensecaves": config.EnableDenseCaves = boolValue; changed = true; break;
                                    case "enablecheesecaves": config.EnableCheeseCaves = boolValue; changed = true; break;
                                    case "enablethermallakes": config.EnableThermalLakes = boolValue; changed = true; break;
                                    case "enableundergroundrivers": config.EnableUndergroundRivers = boolValue; changed = true; break;
                                    case "enablelavarivers": config.EnableLavaRivers = boolValue; changed = true; break;
                                    case "enableveingaps": config.EnableVeinGaps = boolValue; changed = true; break;
                                    case "enablepillars": config.EnablePillars = boolValue; changed = true; break;
                                    case "debugglasscaves": config.DebugGlassCaves = boolValue; changed = true; break;
                                    case "debuginverseworld": config.DebugInverseWorld = boolValue; changed = true; break;
                                    case "debugglasssurfacerivers": config.DebugGlassSurfaceRivers = boolValue; changed = true; break;
                                    case "debugglassundergroundrivers": config.DebugGlassUndergroundRivers = boolValue; changed = true; break;
                                    case "debugglasslavarivers": config.DebugGlassLavaRivers = boolValue; changed = true; break;
                                    case "debugglassbedrock": config.DebugGlassBedrock = boolValue; changed = true; break;
                                    case "enablebedrocklayer": config.EnableBedrockLayer = boolValue; changed = true; break;
                                    case "useinvertedworld": config.UseInvertedWorld = boolValue; changed = true; break;
                                }
                            }
                            else if (float.TryParse(valueStr, out float floatValue))
                            {
                                switch (key.ToLower())
                                {
                                    case "surfaceriverprobability": config.SurfaceRiverProbability = floatValue; changed = true; break;
                                    case "spaghettiprobability": config.SpaghettiProbability = floatValue; changed = true; break;
                                    case "canyonprobability": config.CanyonProbability = floatValue; changed = true; break;
                                    case "densecaveprobability": config.DenseCaveProbability = floatValue; changed = true; break;
                                    case "cheeseprobability": config.CheeseProbability = floatValue; changed = true; break;
                                    case "thermallakeprobability": config.ThermalLakeProbability = floatValue; changed = true; break;
                                    case "undergroundriverprobability": config.UndergroundRiverProbability = floatValue; changed = true; break;
                                    case "lavariverprobability": config.LavaRiverProbability = floatValue; changed = true; break;
                                    case "veingapprobability": config.VeinGapProbability = floatValue; changed = true; break;
                                    case "pillarprobability": config.PillarProbability = floatValue; changed = true; break;
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
                                    case "bedrockminthickness": config.BedrockMinThickness = intValue; changed = true; break;
                                    case "bedrockmaxthickness": config.BedrockMaxThickness = intValue; changed = true; break;
                                    case "seed": config.Seed = intValue; changed = true; break;
                                }
                            }

                            if (changed)
                            {
                                ConfigManager.Save();
                                ServerAPI.Logger.Notification("[CavesAndCaverns] Set {0} to {1}", key, valueStr);
                                player.SendMessage(0, $"Set {key} to {valueStr}. Regen area with /cavesandcaverns regen.", EnumChatType.Notification);
                                if (key.ToLower() == "useinvertedworld")
                                {
                                    player.SendMessage(0, "UseInvertedWorld changed. Please create a new world for the change to take effect.", EnumChatType.Notification);
                                }
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
                        sapi.ChatCommands.Parsers.OptionalInt("radius", 3)
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

                        var pos = player.Entity.SidedPos;
                        double centerBlockX = pos.X - 512000;
                        double centerBlockZ = pos.Z - 512000;
                        int centerChunkX = (int)(pos.X / 32);
                        int centerChunkZ = (int)(pos.Z / 32);

                        ServerAPI.Logger.Debug($"[CavesAndCaverns] Player: {player.PlayerName}, Raw SidedPos: X={pos.X:F2}, Z={pos.Z:F2}");
                        ServerAPI.Logger.Debug($"[CavesAndCaverns] Relative Pos: X={centerBlockX:F2}, Z={centerBlockZ:F2}");
                        ServerAPI.Logger.Debug($"[CavesAndCaverns] Calculated center chunk: X={centerChunkX}, Z={centerChunkZ}");

                        int minChunkX = centerChunkX - radius;
                        int maxChunkX = centerChunkX + radius;
                        int minChunkZ = centerChunkZ - radius;
                        int maxChunkZ = centerChunkZ + radius;
                        int minBlockX = minChunkX * 32;
                        int maxBlockX = maxChunkX * 32 + 31;
                        int minBlockZ = minChunkZ * 32;
                        int maxBlockZ = maxChunkZ * 32 + 31;
                        ServerAPI.Logger.Notification($"[CavesAndCaverns] Regenerating {radius * 2 + 1}x{radius * 2 + 1} chunks around X={centerChunkX}, Z={centerChunkZ} (block coords X={minBlockX} to {maxBlockX}, Z={minBlockZ} to {maxBlockZ})");
                        player.SendMessage(0, $"Regenerating {radius * 2 + 1}x{radius * 2 + 1} chunks centered at block X={centerBlockX:F2}, Z={centerBlockZ:F2} (chunk X={centerChunkX}, Z={centerChunkZ})", EnumChatType.Notification);

                        for (int dx = -radius; dx <= radius; dx++)
                        {
                            for (int dz = -radius; dz <= radius; dz++)
                            {
                                int chunkX = centerChunkX + dx;
                                int chunkZ = centerChunkZ + dz;
                                string key = $"{chunkX},{chunkZ}";
                                lock (Precalculator.PrecomputedCaveMaps)
                                {
                                    if (Precalculator.PrecomputedCaveMaps.ContainsKey(key))
                                    {
                                        Precalculator.PrecomputedCaveMaps.Remove(key);
                                        ServerAPI.Logger.Debug($"[CavesAndCaverns] Cleared precomputed cave map for chunk X={chunkX}, Z={chunkZ}");
                                    }
                                }
                            }
                        }

                        int chunkHeight = ServerAPI.WorldManager.MapSizeY / 32;
                        IsRegenContext = true;
                        for (int dx = -radius; dx <= radius; dx++)
                        {
                            for (int dz = -radius; dz <= radius; dz++)
                            {
                                int chunkX = centerChunkX + dx;
                                int chunkZ = centerChunkZ + dz;
                                ServerAPI.Logger.Debug($"[CavesAndCaverns] Deleting chunk column at X={chunkX}, Z={chunkZ}");
                                ServerAPI.WorldManager.DeleteChunkColumn(chunkX, chunkZ);
                                try
                                {
                                    OnChunkColumnGen(new CustomChunkColumnGenerateRequest(chunkX, chunkZ, false));
                                }
                                catch (Exception ex)
                                {
                                    ServerAPI.Logger.Error($"[CavesAndCaverns] Failed to regenerate chunk X={chunkX}, Z={chunkZ}: {ex}");
                                }
                            }
                        }
                        IsRegenContext = false;

                        for (int dx = -radius; dx <= radius; dx++)
                        {
                            for (int dz = -radius; dz <= radius; dz++)
                            {
                                int chunkX = centerChunkX + dx;
                                int chunkZ = centerChunkZ + dz;
                                for (int chunkY = 0; chunkY < chunkHeight; chunkY++)
                                {
                                    ServerAPI.Logger.Debug($"[CavesAndCaverns] Broadcasting chunk at X={chunkX}, Y={chunkY}, Z={chunkZ}");
                                    ServerAPI.WorldManager.BroadcastChunk(chunkX, chunkY, chunkZ, true);
                                }
                            }
                        }

                        ServerAPI.Logger.Notification($"[CavesAndCaverns] Successfully regenerated chunks for {player.PlayerName}");
                        ServerAPI.BroadcastMessageToAllGroups("[CavesAndCaverns] Chunks regenerated. Please move away and back to see changes.", EnumChatType.Notification);
                        player.SendMessage(0, $"Regenerated {radius * 2 + 1}x{radius * 2 + 1} chunks around X={centerChunkX}, Z={centerChunkZ}", EnumChatType.Notification);
                        return TextCommandResult.Success();
                    })
                    .EndSubCommand();

                cmd.WithAlias("cnc");

                sapi.Logger.Notification("[CavesAndCaverns] Command registration complete.");
            }
            catch (Exception ex)
            {
                sapi.Logger.Error("[CavesAndCaverns] Server-side initialization failed: {0}", ex);
                throw;
            }
        }

        public void OnConfigReloaded(CavesConfig newConfig)
        {
            ConfigManager.Config = newConfig;
            ServerAPI.Logger.Notification("[CavesAndCaverns] Config reloaded: DebugInverseWorld={0}, SurfaceRiverProbability={1}",
                newConfig.DebugInverseWorld, newConfig.SurfaceRiverProbability);
        }

        private void OnChunkColumnGen(IChunkColumnGenerateRequest request)
        {
            if (ConfigManager?.Config == null)
            {
                ServerAPI.Logger.Error("[CavesAndCaverns] Config is null in OnChunkColumnGen for chunk X:{0}, Z:{1}", request.ChunkX, request.ChunkZ);
                return;
            }

            if (ServerAPI == null)
            {
                ServerAPI.Logger.Error("[CavesAndCaverns] ServerAPI is null in OnChunkColumnGen for chunk X:{0}, Z:{1}", request.ChunkX, request.ChunkZ);
                return;
            }

            var config = ConfigManager.Config;
            ServerAPI.Logger.Debug("[CavesAndCaverns] OnChunkColumnGen - Config state: DebugInverseWorld={0}, SurfaceRiverProbability={1}, FromRegen={2}",
                config.DebugInverseWorld, config.SurfaceRiverProbability, IsRegenContext);
            ServerAPI.Logger.Notification("[CavesAndCaverns] Generating chunk column at X:{0}, Z:{1} with DebugInverseWorld: {2}",
                request.ChunkX, request.ChunkZ, config.DebugInverseWorld);

            int chunkSize = 32;
            int worldHeight = ServerAPI.WorldManager.MapSizeY;
            BlockPos origin = new BlockPos(request.ChunkX * chunkSize, 0, request.ChunkZ * chunkSize);
            IBlockAccessor blockAccessor = ServerAPI.World.BlockAccessor;

            int carvedCount = 0;
            const int maxBlocksPerChunk = 100000;

            try
            {
                bool[,,] bedrockMap = new bool[chunkSize, chunkSize, chunkSize];
                BedrockLayer.Apply(blockAccessor, origin, bedrockMap);

                bool[,,] caveMap = new bool[chunkSize, worldHeight, chunkSize];
                for (int yBase = 0; yBase < worldHeight; yBase += chunkSize)
                {
                    string key = $"{request.ChunkX},{request.ChunkZ}";
                    bool[,,] chunkCaveMap;
                    lock (Precalculator.PrecomputedCaveMaps)
                    {
                        if (Precalculator.PrecomputedCaveMaps.ContainsKey(key) && !IsRegenContext)
                        {
                            chunkCaveMap = Precalculator.PrecomputedCaveMaps[key];
                            ServerAPI.Logger.Debug("[CavesAndCaverns] Using precomputed cave map for chunk X={0}, Z={1}", request.ChunkX, request.ChunkZ);
                        }
                        else
                        {
                            bool[,,] surfaceMap = MasterCarver.CarveSurface(chunkSize, new BlockPos(origin.X, yBase, origin.Z), "defaultBiome");
                            bool[,,] undergroundMap = MasterCarver.CarveUnderground(chunkSize, new BlockPos(origin.X, yBase, origin.Z), "defaultBiome");
                            chunkCaveMap = new bool[chunkSize, chunkSize, chunkSize];
                            for (int x = 0; x < chunkSize; x++)
                                for (int y = 0; y < chunkSize && (yBase + y) < worldHeight; y++)
                                    for (int z = 0; z < chunkSize; z++)
                                        chunkCaveMap[x, y, z] = surfaceMap[x, y, z] || undergroundMap[x, y, z];
                            if (IsRegenContext)
                            {
                                Precalculator.PrecomputedCaveMaps[key] = chunkCaveMap;
                                ServerAPI.Logger.Debug("[CavesAndCaverns] Generated and stored new cave map for chunk X={0}, Z={1} during regen", request.ChunkX, request.ChunkZ);
                            }
                        }
                    }

                    for (int x = 0; x < chunkSize; x++)
                        for (int y = 0; y < chunkSize && (yBase + y) < worldHeight; y++)
                            for (int z = 0; z < chunkSize; z++)
                                caveMap[x, yBase + y, z] = chunkCaveMap[x, y, z];
                }

                for (int x = 0; x < chunkSize; x++)
                {
                    for (int y = 0; y < worldHeight; y++)
                    {
                        for (int z = 0; z < chunkSize; z++)
                        {
                            if (carvedCount > maxBlocksPerChunk)
                            {
                                ServerAPI.Logger.Warning("[CavesAndCaverns] Exceeded block placement limit ({0}) for chunk {1},{2}. Stopping block placement to prevent crash.", maxBlocksPerChunk, request.ChunkX, request.ChunkZ);
                                break;
                            }

                            BlockPos pos = new BlockPos(origin.X + x, y, origin.Z + z);
                            bool isBedrock = y < chunkSize && bedrockMap[x, y, z];
                            bool isCave = caveMap[x, y, z];

                            if (config.DebugInverseWorld && !isBedrock && !isCave)
                            {
                                blockAccessor.SetBlock(0, pos);
                            }

                            if (isCave)
                            {
                                carvedCount++;
                                blockAccessor.SetBlock(0, pos);
                            }

                            if (isBedrock && config.EnableBedrockLayer && config.DebugGlassBedrock)
                            {
                                GlassBlockManager.PlaceDebugGlass(blockAccessor, pos, "bedrock");
                            }
                        }
                    }
                }

                ServerAPI.Logger.Notification("[CavesAndCaverns] Total carved blocks in chunk {0},{1} (y=0 to {2}): {3}", request.ChunkX, request.ChunkZ, worldHeight - 1, carvedCount);
                GlassBlockManager.LogPlacementSummary(request.ChunkX, request.ChunkZ);

                // Ensure changes are committed
                blockAccessor.Commit();

                // Force client sync by broadcasting the chunk
                int chunkHeight = ServerAPI.WorldManager.MapSizeY / 32;
                for (int chunkY = 0; chunkY < chunkHeight; chunkY++)
                {
                    ServerAPI.WorldManager.BroadcastChunk(request.ChunkX, chunkY, request.ChunkZ, true);
                }
            }
            catch (Exception ex)
            {
                ServerAPI.Logger.Error("[CavesAndCaverns] Error during chunk generation for X:{0}, Z:{1}: {2}", request.ChunkX, request.ChunkZ, ex);
            }
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
}