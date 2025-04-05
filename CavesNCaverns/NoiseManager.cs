using System;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace CavesAndCaverns.Managers
{
    public class NoiseManager : ModSystem
    {
        private ICoreServerAPI sapi;
        private SimplexNoise surfaceRiverNoise;
        private SimplexNoise undergroundRiverNoise;
        private SimplexNoise lavaRiverNoise;
        private SimplexNoise canyonNoise;
        private SimplexNoise denseCaveNoise;
        private SimplexNoise cheeseNoise;
        private SimplexNoise thermalLakeNoise;
        private SimplexNoise veinGapNoise;
        private SimplexNoise spaghetti2DNoise;
        private SimplexNoise pillarNoise;

        // Scaling factors based on Terralith's xz_scale and y_scale
        private readonly double cheeseFx = 1.0;   // 1 / xz_scale = 1 / 1
        private readonly double cheeseFy = 1.5;   // 1 / y_scale = 1 / 0.6666
        private readonly double cheeseFz = 1.0;

        private readonly double spaghettiFx = 0.5;  // 1 / xz_scale = 1 / 2
        private readonly double spaghettiFy = 1.0;  // 1 / y_scale = 1 / 1
        private readonly double spaghettiFz = 0.5;

        private readonly double caveLayerFx = 1.0;  // 1 / xz_scale = 1 / 1
        private readonly double caveLayerFy = 0.125; // 1 / y_scale = 1 / 8
        private readonly double caveLayerFz = 1.0;

        private readonly double noodleFx = 1.0;     // 1 / xz_scale = 1 / 1
        private readonly double noodleFy = 1.0;     // 1 / y_scale = 1 / 1
        private readonly double noodleFz = 1.0;

        // Parameterless constructor required by Vintage Story
        public NoiseManager()
        {
            // Initialize fields to null or default values
            sapi = null;
            surfaceRiverNoise = null;
            undergroundRiverNoise = null;
            lavaRiverNoise = null;
            canyonNoise = null;
            denseCaveNoise = null;
            cheeseNoise = null;
            thermalLakeNoise = null;
            veinGapNoise = null;
            spaghetti2DNoise = null;
            pillarNoise = null;
        }

        public override void StartServerSide(ICoreServerAPI sapi)
        {
            this.sapi = sapi;
            LoadAllNoises();
        }

        private void LoadAllNoises()
        {
            if (sapi == null)
            {
                throw new InvalidOperationException("ICoreServerAPI not initialized in NoiseManager.");
            }

            var config = CavesAndCavernsCore.ConfigManager.Config;
            long seed = config.Seed != 0 ? config.Seed : sapi.World.Seed;

            // Base frequency for scaling
            double baseFreq = 0.0005;

            // Cheese caves: amplitudes [1.0, 0.5, 0.75, 2.2, 0.5], firstOctave -6
            double[] cheeseAmplitudes = new double[] { 1.0, 0.5, 0.75, 2.2, 0.5 };
            double[] cheeseFrequencies = new double[5];
            double cheeseStartFreq = baseFreq * Math.Pow(2, -6);
            for (int i = 0; i < 5; i++)
                cheeseFrequencies[i] = cheeseStartFreq * Math.Pow(2, i);
            cheeseNoise = new SimplexNoise(cheeseAmplitudes, cheeseFrequencies, seed + 6);

            // Spaghetti 2D caves: amplitudes [1.1, 1.1, 0.5, 1.0, 2.0, 1.5, 1.0], firstOctave -11
            double[] spaghettiAmplitudes = new double[] { 1.1, 1.1, 0.5, 1.0, 2.0, 1.5, 1.0 };
            double[] spaghettiFrequencies = new double[7];
            double spaghettiStartFreq = baseFreq * Math.Pow(2, -11);
            for (int i = 0; i < 7; i++)
                spaghettiFrequencies[i] = spaghettiStartFreq * Math.Pow(2, i);
            spaghetti2DNoise = new SimplexNoise(spaghettiAmplitudes, spaghettiFrequencies, seed + 9);

            // Thermal lake caves: amplitudes [1.0, 0.5, 1.25, 1.75, 1.5], firstOctave -6
            double[] thermalAmplitudes = new double[] { 1.0, 0.5, 1.25, 1.75, 1.5 };
            double[] thermalFrequencies = new double[5];
            double thermalStartFreq = baseFreq * Math.Pow(2, -6);
            for (int i = 0; i < 5; i++)
                thermalFrequencies[i] = thermalStartFreq * Math.Pow(2, i);
            thermalLakeNoise = new SimplexNoise(thermalAmplitudes, thermalFrequencies, seed + 7);

            // Vein gap caves: amplitudes [1.2, 0.9, 1.1, 2.0, 0, 4.0], firstOctave -5
            double[] veinAmplitudes = new double[] { 1.2, 0.9, 1.1, 2.0, 0, 4.0 };
            double[] veinFrequencies = new double[6];
            double veinStartFreq = baseFreq * Math.Pow(2, -5);
            for (int i = 0; i < 6; i++)
                veinFrequencies[i] = veinStartFreq * Math.Pow(2, i);
            veinGapNoise = new SimplexNoise(veinAmplitudes, veinFrequencies, seed + 8);

            // Pillar caves: amplitudes [1.0, 0.5, 1.125, 1.1, 0.5], firstOctave -6
            double[] pillarAmplitudes = new double[] { 1.0, 0.5, 1.125, 1.1, 0.5 };
            double[] pillarFrequencies = new double[5];
            double pillarStartFreq = baseFreq * Math.Pow(2, -6);
            for (int i = 0; i < 5; i++)
                pillarFrequencies[i] = pillarStartFreq * Math.Pow(2, i);
            pillarNoise = new SimplexNoise(pillarAmplitudes, pillarFrequencies, seed + 10);

            // Placeholder for others (e.g., rivers, canyons) until specific Terralith data is provided
            double[] defaultAmplitudes = new double[4] { 1.0, 0.5, 0.25, 0.125 };
            double[] defaultFrequencies = new double[4];
            double defaultStartFreq = baseFreq * Math.Pow(2, -6);
            for (int i = 0; i < 4; i++)
                defaultFrequencies[i] = defaultStartFreq * Math.Pow(2, i);
            surfaceRiverNoise = new SimplexNoise(defaultAmplitudes, defaultFrequencies, seed + 1);
            undergroundRiverNoise = new SimplexNoise(defaultAmplitudes, defaultFrequencies, seed + 2);
            lavaRiverNoise = new SimplexNoise(defaultAmplitudes, defaultFrequencies, seed + 3);
            canyonNoise = new SimplexNoise(defaultAmplitudes, defaultFrequencies, seed + 4);
            denseCaveNoise = new SimplexNoise(defaultAmplitudes, defaultFrequencies, seed + 5);
        }

        // Getter methods with coordinate scaling
        public double GetSurfaceRiverNoise(int x, int z)
        {
            return surfaceRiverNoise.Noise(x * 1.0, z * 1.0); // Placeholder scaling
        }

        public double GetUndergroundRiverNoise(int x, int z)
        {
            return undergroundRiverNoise.Noise(x * 1.0, z * 1.0); // Placeholder scaling
        }

        public double GetLavaRiverNoise(int x, int y, int z)
        {
            return lavaRiverNoise.Noise(x * 1.0, y * 1.0, z * 1.0); // Placeholder scaling
        }

        public double GetCanyonNoise(int x, int y, int z)
        {
            return canyonNoise.Noise(x * caveLayerFx, y * caveLayerFy, z * caveLayerFz);
        }

        public double GetDenseCaveNoise(int x, int y, int z)
        {
            return denseCaveNoise.Noise(x * caveLayerFx, y * caveLayerFy, z * caveLayerFz);
        }

        public double GetCheeseNoise(int x, int y, int z)
        {
            return cheeseNoise.Noise(x * cheeseFx, y * cheeseFy, z * cheeseFz);
        }

        public double GetThermalLakeNoise(int x, int y, int z)
        {
            return thermalLakeNoise.Noise(x * caveLayerFx, y * caveLayerFy, z * caveLayerFz);
        }

        public double GetVeinGapNoise(int x, int y, int z)
        {
            return veinGapNoise.Noise(x * noodleFx, y * noodleFy, z * noodleFz);
        }

        public double GetSpaghetti2DNoise(int x, int y, int z)
        {
            return spaghetti2DNoise.Noise(x * spaghettiFx, y * spaghettiFy, z * spaghettiFz);
        }

        public double GetPillarNoise(int x, int y, int z)
        {
            return pillarNoise.Noise(x * caveLayerFx, y * caveLayerFy, z * caveLayerFz);
        }
    }
}