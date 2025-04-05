using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace CavesAndCaverns.Managers
{
    public class BiomeHandler
    {
        private readonly ICoreServerAPI sapi;
        private readonly ModSystem spooqBiomesMod;

        public BiomeHandler(ICoreServerAPI sapi)
        {
            this.sapi = sapi;
            spooqBiomesMod = sapi.ModLoader.GetModSystem("biomes"); // Mod ID from Spooq/Biomes; adjust if different
            if (spooqBiomesMod != null)
                sapi.Logger.Notification("[CavesAndCaverns] Spooq/Biomes mod detected.");
        }

        public string GetBiomeTag(BlockPos pos)
        {
            if (spooqBiomesMod != null)
            {
                // Use Spooq/Biomes mod's BiomeMap for biome detection
                var biomeMapProp = spooqBiomesMod.GetType().GetProperty("BiomeMap");
                if (biomeMapProp != null)
                {
                    var biomeMap = biomeMapProp.GetValue(spooqBiomesMod);
                    var getMethod = biomeMap.GetType().GetMethod("Get", new[] { typeof(BlockPos) });
                    var biome = getMethod?.Invoke(biomeMap, new object[] { pos });

                    if (biome != null)
                    {
                        var nameProp = biome.GetType().GetProperty("Name");
                        string biomeName = nameProp?.GetValue(biome)?.ToString().ToLower() ?? "unknown";
                        var rainfallProp = biome.GetType().GetProperty("Rainfall");
                        var tempProp = biome.GetType().GetProperty("Temperature");
                        float modRainfall = rainfallProp != null ? (float)rainfallProp.GetValue(biome) : 0f;
                        float modTemperature = tempProp != null ? (float)tempProp.GetValue(biome) : 0f;

                        if (biomeName.Contains("wet") || biomeName.Contains("swamp") || modRainfall > 0.8f) return "wet";
                        if (biomeName.Contains("snow") || biomeName.Contains("ice") || modTemperature < 0) return "frozen";
                        if (biomeName.Contains("desert") || biomeName.Contains("arid") || modTemperature > 20) return "hot";
                        return biomeName; // Return raw biome name if no match
                    }
                }
            }

            // Vanilla VS climate-based fallback (no biomes natively)
            var climate = sapi.World.BlockAccessor.GetClimateAt(pos);
            if (climate == null) return "temperate";

            float regionRainfall = climate.Rainfall;
            float regionTemperature = climate.Temperature;

            if (regionRainfall > 0.8f) return "wet";
            if (regionTemperature < 0) return "frozen";
            if (regionTemperature > 20) return "hot";
            return "temperate";
        }
    }
}