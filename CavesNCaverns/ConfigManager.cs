using System;
using System.IO;
using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace CavesAndCaverns.Config
{
    public class ConfigManager
    {
        private readonly ICoreAPI api;
        private static CavesConfig config; // Static to ensure single instance

        public ConfigManager(ICoreAPI api)
        {
            this.api = api;
            if (api.Side != EnumAppSide.Server)
            {
                if (config == null) config = new CavesConfig(); // Default for client
            }
            else if (config == null) // Only load once on server
            {
                Load();
            }
        }

        public CavesConfig Config
        {
            get => config;
            set
            {
                if (api.Side == EnumAppSide.Server)
                {
                    config = value;
                    Save();
                    api.Logger.Notification("[CavesAndCaverns] Config updated: DebugInverseWorld={0}", config.DebugInverseWorld);
                }
                else
                {
                    throw new InvalidOperationException("Cannot modify config on client.");
                }
            }
        }

        public void Load()
        {
            if (api.Side != EnumAppSide.Server) return;

            try
            {
                string configPath = Path.Combine(api.DataBasePath, "ModConfig", "CavesAndCaverns.json");
                api.Logger.Notification("[CavesAndCaverns] Attempting to load config from: {0}", configPath);

                string configDir = Path.GetDirectoryName(configPath);
                if (!Directory.Exists(configDir))
                {
                    api.Logger.Warning("[CavesAndCaverns] Config directory missing: {0}. Creating it.", configDir);
                    Directory.CreateDirectory(configDir);
                }

                if (File.Exists(configPath))
                {
                    string jsonContent = File.ReadAllText(configPath);
                    api.Logger.Notification("[CavesAndCaverns] Config file found. Contents: {0}", jsonContent);

                    config = JsonConvert.DeserializeObject<CavesConfig>(jsonContent);
                    if (config == null)
                    {
                        api.Logger.Error("[CavesAndCaverns] Failed to deserialize config file. Using defaults.");
                        config = GetDefaultConfig();
                        Save();
                    }
                    else
                    {
                        api.Logger.Notification("[CavesAndCaverns] Config loaded successfully: DebugInverseWorld={0}, SurfaceRiverProbability={1}",
                            config.DebugInverseWorld, config.SurfaceRiverProbability);
                    }
                }
                else
                {
                    api.Logger.Warning("[CavesAndCaverns] Config file not found at {0}. Creating default config.", configPath);
                    config = GetDefaultConfig();
                    Save();
                }
            }
            catch (Exception ex)
            {
                api.Logger.Error("[CavesAndCaverns] Failed to load config: {0}. Using defaults.", ex.Message);
                config = GetDefaultConfig();
                Save();
            }
        }

        public void Save()
        {
            if (api.Side != EnumAppSide.Server) return;

            try
            {
                string configPath = Path.Combine(api.DataBasePath, "ModConfig", "CavesAndCaverns.json");
                api.Logger.Debug("[CavesAndCaverns] Saving config to: {0}", configPath);

                string jsonContent = JsonConvert.SerializeObject(config, Formatting.Indented);
                File.WriteAllText(configPath, jsonContent);
                api.Logger.Notification("[CavesAndCaverns] Config saved successfully: DebugInverseWorld={0}, DebugGlassCaves={1}",
                    config.DebugInverseWorld, config.DebugGlassCaves);
            }
            catch (Exception ex)
            {
                api.Logger.Error("[CavesAndCaverns] Failed to save config: {0}", ex.Message);
            }
        }

        public CavesConfig GetDefaultConfig()
        {
            return new CavesConfig
            {
                DebugInverseWorld = true,
                DebugGlassCaves = true,
                EnableSurfaceRivers = true,
                EnableSpaghetti2D = true,
                EnableCanyons = true,
                EnableDenseCaves = true,
                EnableCheeseCaves = true,
                EnableThermalLakes = true,
                EnableUndergroundRivers = true,
                EnableLavaRivers = true,
                EnableVeinGaps = true,
                EnablePillars = true,
                SurfaceRiverProbability = 0.1f,
                SpaghettiProbability = 0.05f,
                CanyonProbability = 0.03f,
                DenseCaveProbability = 0.07f,
                CheeseProbability = 0.02f,
                ThermalLakeProbability = 0.04f,
                UndergroundRiverProbability = 0.06f,
                LavaRiverProbability = 0.01f,
                VeinGapProbability = 0.05f,
                PillarProbability = 0.03f,
                DebugGlassSurfaceRivers = true,
                DebugGlassUndergroundRivers = true,
                DebugGlassLavaRivers = true,
                DebugGlassBedrock = true,
                EnableBedrockLayer = true,
                WetBiomeRiverChanceMultiplier = 1.5f,
                RiverWidthMin = 2,
                RiverWidthMax = 5,
                SurfaceRiverWidthMin = 3,
                SurfaceRiverWidthMax = 6,
                GlowWormCountMin = 1,
                GlowWormCountMax = 5,
                GlowWormLightLevel = 7,
                BedrockMinThickness = 1,
                BedrockMaxThickness = 3,
                Seed = 0,
                UseInvertedWorld = false // Added default value
            };
        }
    }

    public class CavesConfig
    {
        public bool EnableSurfaceRivers { get; set; } = true;
        public bool EnableSpaghetti2D { get; set; } = true;
        public bool EnableCanyons { get; set; } = true;
        public bool EnableDenseCaves { get; set; } = true;
        public bool EnableCheeseCaves { get; set; } = true;
        public bool EnableThermalLakes { get; set; } = true;
        public bool EnableUndergroundRivers { get; set; } = true;
        public bool EnableLavaRivers { get; set; } = true;
        public bool EnableVeinGaps { get; set; } = true;
        public bool EnablePillars { get; set; } = true;

        public float SurfaceRiverProbability { get; set; } = 0.1f;
        public float SpaghettiProbability { get; set; } = 0.05f;
        public float CanyonProbability { get; set; } = 0.03f;
        public float DenseCaveProbability { get; set; } = 0.07f;
        public float CheeseProbability { get; set; } = 0.02f;
        public float ThermalLakeProbability { get; set; } = 0.04f;
        public float UndergroundRiverProbability { get; set; } = 0.06f;
        public float LavaRiverProbability { get; set; } = 0.01f;
        public float VeinGapProbability { get; set; } = 0.05f;
        public float PillarProbability { get; set; } = 0.03f;

        public bool DebugGlassCaves { get; set; } = false;
        public bool DebugInverseWorld { get; set; } = false;
        public bool DebugGlassSurfaceRivers { get; set; } = false;
        public bool DebugGlassUndergroundRivers { get; set; } = false;
        public bool DebugGlassLavaRivers { get; set; } = false;
        public bool DebugGlassBedrock { get; set; } = false;
        public bool EnableBedrockLayer { get; set; } = true;
        public float WetBiomeRiverChanceMultiplier { get; set; } = 1.5f;
        public int RiverWidthMin { get; set; } = 2;
        public int RiverWidthMax { get; set; } = 5;
        public int SurfaceRiverWidthMin { get; set; } = 3;
        public int SurfaceRiverWidthMax { get; set; } = 6;
        public int GlowWormCountMin { get; set; } = 1;
        public int GlowWormCountMax { get; set; } = 5;
        public int GlowWormLightLevel { get; set; } = 7;
        public int BedrockMinThickness { get; set; } = 1;
        public int BedrockMaxThickness { get; set; } = 3;
        public int Seed { get; set; } = 0;

        // Added UseInvertedWorld property
        public bool UseInvertedWorld { get; set; } = false;
    }
}