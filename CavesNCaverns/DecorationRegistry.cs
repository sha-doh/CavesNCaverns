using System.Collections.Generic;

namespace CavesAndCaverns.Managers
{
    public static class DecorationRegistry
    {
        private static readonly Dictionary<(int, int, int), string> decorations = new();

        public static void MarkDecoration(int x, int y, int z, string decorType)
        {
            decorations[(x, y, z)] = decorType;
        }

        public static string GetDecor(int x, int y, int z)
        {
            return decorations.TryGetValue((x, y, z), out string decor) ? decor : null;
        }

        public static void Clear()
        {
            decorations.Clear();
        }
    }
}