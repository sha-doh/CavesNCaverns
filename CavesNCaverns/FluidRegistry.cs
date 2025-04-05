using System.Collections.Generic;
using Vintagestory.API.MathTools;

namespace CavesAndCaverns.Managers
{
    public static class FluidRegistry
    {
        private static readonly Dictionary<Vec3f, string> fluids = new();

        public static void MarkFluid(float x, float y, float z, string fluidType)
        {
            fluids[new Vec3f(x, y, z)] = fluidType;
        }

        public static string GetFluid(float x, float y, float z)
        {
            return fluids.TryGetValue(new Vec3f(x, y, z), out string fluid) ? fluid : null;
        }

        public static void Clear()
        {
            fluids.Clear();
        }
    }
}