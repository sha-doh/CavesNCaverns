using CavesAndCaverns.Carvers;
using CavesAndCaverns.Config;
using CavesAndCaverns.Managers;
using CavesAndCaverns.PostGen;
using System;
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
                    api.Logger.Notification("[CavesAndCaverns] Config loaded: DebugInverseWorld={0}, SurfaceRiverProbability={1}",
                        ConfigManager.Config.DebugInverseWorld, ConfigManager.Config.SurfaceRiverProbability);
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

                NoiseManager = sapi.ModLoader.GetModSystem<NoiseManager>();
                if (NoiseManager == null)
                {
                    sapi.Logger.Error("[CavesAndCaverns] NoiseManager is null.");
                    throw new InvalidOperationException("Failed to initialize NoiseManager.");
                }

                BiomeHandler = new BiomeHandler(sapi);
                MasterCarver = new MasterCarver(sapi);
                PostProcessor = new PostProcessor(sapi);
                FluidManager = new FluidManager(sapi);
                BedrockLayer = new BedrockNoiseLayer(sapi, NoiseManager);

                sapi.Logger.Notification("[CavesAndCaverns] All managers initialized.");

                sapi.Event.ChunkColumnGeneration(OnChunkColumnGen, EnumWorldGenPass.Terrain, "standard");
                sapi.Event.ChunkColumnGeneration(OnChunkColumnGen, EnumWorldGenPass.TerrainFeatures, "standard");
                sapi.Logger.Notification("[CavesAndCaverns] Chunk column generation events registered for Terrain and TerrainFeatures.");

                ConfigWatcher.Start();

                sapi.Logger.Notification("[CavesAndCaverns] Starting command registration...");
                sapi.RegisterCommand("cavesandcaverns", "Manage CavesAndCaverns settings and generation", "",
                    OnCavesAndCavernsCmd, Privilege.root);

                sapi.Logger.Notification("[CavesAndCaverns] Command registration complete.");
            }
            catch (Exception ex)
            {
                sapi.Logger.Error("[CavesAndCaverns] Server-side initialization failed: {0}", ex);
                throw;
            }
        }

        private void OnCavesAndCavernsCmd(IServerPlayer player, int groupId, CmdArgs args)
        {
            var subcommand = args.PopWord();
            if (subcommand == null)
            {
                player.SendMessage(groupId, "Usage: /cavesandcaverns [test|reload|config|regen] [args]", EnumChatType.Notification);
                return;
            }

            switch (subcommand.ToLower())
            {
                case "test":
                    player.SendMessage(groupId, "Test command works!", EnumChatType.Notification);
                    break;

                case "reload":
                    ConfigManager.Load();
                    ServerAPI.Logger.Notification("[CavesAndCaverns] Config reloaded: DebugInverseWorld={0}, SurfaceRiverProbability={1}",
                        ConfigManager.Config.DebugInverseWorld, ConfigManager.Config.SurfaceRiverProbability);
                    player.SendMessage(groupId, "CavesAndCaverns config reloaded.", EnumChatType.Notification);
                    break;

                case "config":
                    if (args.Length < 3)
                    {
                        player.SendMessage(groupId, "Usage: /cavesandcaverns config set [key] [value]", EnumChatType.Notification);
                        return;
                    }

                    string action = args.PopWord();
                    if (action != "set")
                    {
                        player.SendMessage(groupId, "Action must be 'set'. Usage: /cavesandcaverns config set [key] [value]", EnumChatType.Notification);
                        return;
                    }

                    string key = args.PopWord();
                    string valueStr = args.PopWord();
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
                        player.SendMessage(groupId, $"Set {key} to {valueStr}. Regen area with /cavesandcaverns regen.", EnumChatType.Notification);
                    }
                    else
                    {
                        player.SendMessage(groupId, $"Invalid key '{key}' or value '{valueStr}'.", EnumChatType.Notification);
                    }
                    break;

                case "regen":
                    if (player?.Entity == null)
                    {
                        ServerAPI.Logger.Error("[CavesAndCaverns] Player entity is null!");
                        player.SendMessage(groupId, "Error: Command must be run by a player.", EnumChatType.Notification);
                        return;
                    }

                    int radius = args.PopInt() ?? 3;
                    if (radius < 1 || radius > 10)
                    {
                        player.SendMessage(groupId, "Radius must be between 1 and 10", EnumChatType.Notification);
                        return;
                    }

                    int centerChunkX, centerChunkZ;
                    double centerBlockX, centerBlockZ;
                    if (args.Length >= 2)
                    {
                        int? customX = args.PopInt();
                        int? customZ = args.PopInt();
                        if (customX.HasValue && customZ.HasValue)
                        {
                            centerBlockX = customX.Value;
                            centerBlockZ = customZ.Value;
                            centerChunkX = (int)(centerBlockX / 32);
                            centerChunkZ = (int)(centerBlockZ / 32);
                            player.SendMessage(groupId, $"Regenerating at custom location X={centerBlockX}, Z={centerBlockZ}", EnumChatType.Notification);
                        }
                        else
                        {
                            player.SendMessage(groupId, "Invalid coordinates. Usage: /cavesandcaverns regen [radius] [x] [z]", EnumChatType.Notification);
                            return;
                        }
                    }
                    else
                    {
                        var pos = player.Entity.SidedPos;
                        int worldSizeX = ServerAPI.WorldManager.MapSizeX;
                        int worldSizeZ = ServerAPI.WorldManager.MapSizeZ;
                        double worldCenterX = worldSizeX / 2.0;
                        double worldCenterZ = worldSizeZ / 2.0;
                        centerBlockX = pos.X - worldCenterX;
                        centerBlockZ = pos.Z - worldCenterZ;
                        centerChunkX = (int)(centerBlockX / 32);
                        centerChunkZ = (int)(centerBlockZ / 32);

                        ServerAPI.Logger.Notification($"[CavesAndCaverns] Player: {player.PlayerName}, Raw SidedPos: X={pos.X:F2}, Y={pos.Y:F2}, Z={pos.Z:F2}");
                        ServerAPI.Logger.Notification($"[CavesAndCaverns] Player: {player.PlayerName}, Raw Pos: X={player.Entity.Pos.X:F2}, Y={player.Entity.Pos.Y:F2}, Z={player.Entity.Pos.Z:F2}");
                        ServerAPI.Logger.Notification($"[CavesAndCaverns] World Center: X={worldCenterX}, Z={worldCenterZ}");
                        ServerAPI.Logger.Notification($"[CavesAndCaverns] Adjusted Position: X={centerBlockX:F2}, Z={centerBlockZ:F2}");
                        ServerAPI.Logger.Notification($"[CavesAndCaverns] Calculated center chunk: X={centerChunkX}, Z={centerChunkZ}");
                    }

                    int minChunkX = centerChunkX - radius;
                    int maxChunkX = centerChunkX + radius;
                    int minChunkZ = centerChunkZ - radius;
                    int maxChunkZ = centerChunkZ + radius;
                    int minBlockX = minChunkX * 32;
                    int maxBlockX = maxChunkX * 32 + 31;
                    int minBlockZ = minChunkZ * 32;
                    int maxBlockZ = maxChunkZ * 32 + 31;
                    ServerAPI.Logger.Notification($"[CavesAndCaverns] Regenerating {radius * 2 + 1}x{radius * 2 + 1} chunks around X={centerChunkX}, Z={centerChunkZ} (block coords X={minBlockX} to {maxBlockX}, Z={minBlockZ} to {maxBlockZ})");

                    int chunkHeight = ServerAPI.WorldManager.MapSizeY / 32;
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

                    player.SendMessage(groupId, $"Regenerated {radius * 2 + 1}x{radius * 2 + 1} chunks around X={centerChunkX}, Z={centerChunkZ}", EnumChatType.Notification);
                    ServerAPI.Logger.Notification($"[CavesAndCaverns] Successfully regenerated chunks for {player.PlayerName}");
                    ServerAPI.BroadcastMessageToAllGroups("[CavesAndCaverns] Chunks regenerated. Please move away and back to see changes.", EnumChatType.Notification);
                    break;

                default:
                    player.SendMessage(groupId, "Unknown subcommand. Usage: /cavesandcaverns [test|reload|config|regen] [args]", EnumChatType.Notification);
                    break;
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
            ServerAPI.Logger.Debug("[CavesAndCaverns] OnChunkColumnGen - Config state: DebugInverseWorld={0}, SurfaceRiverProbability={1}",
                config.DebugInverseWorld, config.SurfaceRiverProbability);
            ServerAPI.Logger.Notification("[CavesAndCaverns] Generating chunk column at X:{0}, Z:{1} with DebugInverseWorld: {2}",
                request.ChunkX, request.ChunkZ, config.DebugInverseWorld);

            int chunkSize = 32;
            int worldHeight = ServerAPI.WorldManager.MapSizeY;
            BlockPos origin = new BlockPos(request.ChunkX * chunkSize, 0, request.ChunkZ * chunkSize);
            IBlockAccessor blockAccessor = ServerAPI.World.BlockAccessor;

            ServerAPI.Logger.Debug("[CavesAndCaverns] Chunk origin: X={0}, Z={1}, World height={2}", origin.X, origin.Z, worldHeight);

            int carvedCount = 0;
            try
            {
                for (int yBase = 0; yBase < worldHeight; yBase += chunkSize)
                {
                    bool[,,] surfaceMap = MasterCarver.CarveSurface(chunkSize, new BlockPos(origin.X, yBase, origin.Z), "defaultBiome");
                    bool[,,] undergroundMap = MasterCarver.CarveUnderground(chunkSize, new BlockPos(origin.X, yBase, origin.Z), "defaultBiome");
                    bool[,,] caveMap = new bool[chunkSize, chunkSize, chunkSize];

                    for (int x = 0; x < chunkSize; x++)
                        for (int y = 0; y < chunkSize && (yBase + y) < worldHeight; y++)
                            for (int z = 0; z < chunkSize; z++)
                                caveMap[x, y, z] = surfaceMap[x, y, z] || undergroundMap[x, y, z];

                    for (int x = 0; x < chunkSize; x++)
                        for (int y = 0; y < chunkSize && (yBase + y) < worldHeight; y++)
                            for (int z = 0; z < chunkSize; z++)
                                if (caveMap[x, y, z]) carvedCount++;

                    if (config.DebugInverseWorld || config.DebugGlassCaves || config.DebugGlassSurfaceRivers ||
                        config.DebugGlassUndergroundRivers || config.DebugGlassLavaRivers || config.DebugGlassBedrock)
                    {
                        if (config.DebugInverseWorld)
                        {
                            ServerAPI.Logger.Notification("[CavesAndCaverns] Applying inverse world mode for chunk at y={0} to {1}", yBase, Math.Min(yBase + chunkSize - 1, worldHeight - 1));
                            for (int x = 0; x < chunkSize; x++)
                            {
                                for (int y = 0; y < chunkSize && (yBase + y) < worldHeight; y++)
                                {
                                    for (int z = 0; z < chunkSize; z++)
                                    {
                                        BlockPos pos = new BlockPos(origin.X + x, yBase + y, origin.Z + z);
                                        if (!caveMap[x, y, z])
                                            blockAccessor.SetBlock(ServerAPI.World.GetBlock(new AssetLocation("game:stone")).BlockId, pos);
                                    }
                                }
                            }
                        }
                        else
                        {
                            ServerAPI.Logger.Notification("[CavesAndCaverns] Applying debug glass mode for chunk at y={0} to {1}", yBase, Math.Min(yBase + chunkSize - 1, worldHeight - 1));
                            for (int x = 0; x < chunkSize; x++)
                            {
                                for (int y = 0; y < chunkSize && (yBase + y) < worldHeight; y++)
                                {
                                    for (int z = 0; z < chunkSize; z++)
                                    {
                                        BlockPos pos = new BlockPos(origin.X + x, yBase + y, origin.Z + z);
                                        if (caveMap[x, y, z])
                                        {
                                            if (config.DebugGlassCaves)
                                                GlassBlockManager.PlaceDebugGlass(blockAccessor, pos, "caves");
                                            if (config.DebugGlassSurfaceRivers && surfaceMap[x, y, z])
                                                GlassBlockManager.PlaceDebugGlass(blockAccessor, pos, "surfaceriver");
                                            if (config.DebugGlassUndergroundRivers && undergroundMap[x, y, z])
                                                GlassBlockManager.PlaceDebugGlass(blockAccessor, pos, "undergroundriver");
                                            if (config.DebugGlassLavaRivers && undergroundMap[x, y, z])
                                                GlassBlockManager.PlaceDebugGlass(blockAccessor, pos, "lavariver");
                                            if (config.DebugGlassBedrock && (yBase + y) == 0 && config.EnableBedrockLayer)
                                                GlassBlockManager.PlaceDebugGlass(blockAccessor, pos, "bedrock");
                                        }
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        ServerAPI.Logger.Notification("[CavesAndCaverns] Applying normal cave mode for chunk at y={0} to {1}", yBase, Math.Min(yBase + chunkSize - 1, worldHeight - 1));
                        for (int x = 0; x < chunkSize; x++)
                            for (int y = 0; y < chunkSize && (yBase + y) < worldHeight; y++)
                                for (int z = 0; z < chunkSize; z++)
                                    if (caveMap[x, y, z])
                                        blockAccessor.SetBlock(0, new BlockPos(origin.X + x, yBase + y, origin.Z + z));
                    }
                }

                ServerAPI.Logger.Notification("[CavesAndCaverns] Total carved blocks in chunk {0},{1} (y=0 to {2}): {3}",
                    request.ChunkX, request.ChunkZ, worldHeight - 1, carvedCount);

                blockAccessor.Commit();
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