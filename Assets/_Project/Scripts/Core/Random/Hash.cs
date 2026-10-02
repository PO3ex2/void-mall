namespace VoidMall.Core
{
    /// <summary>
    /// Детерминированные хеши координат (на основе xxHash32). Одинаковы на всех платформах.
    /// </summary>
    public static class Hash
    {
        private const uint Prime2 = 2246822519U;
        private const uint Prime3 = 3266489917U;
        private const uint Prime4 = 668265263U;
        private const uint Prime5 = 374761393U;

        public static uint Get(uint seed, int x) =>
            Avalanche(Round(seed + Prime5 + 4, (uint)x));

        public static uint Get(uint seed, int x, int y) =>
            Avalanche(Round(Round(seed + Prime5 + 8, (uint)x), (uint)y));

        public static uint Get(uint seed, int x, int y, int z) =>
            Avalanche(Round(Round(Round(seed + Prime5 + 12, (uint)x), (uint)y), (uint)z));

        public static uint Get(uint seed, int x, int y, int z, int w) =>
            Avalanche(Round(Round(Round(Round(seed + Prime5 + 16, (uint)x), (uint)y), (uint)z), (uint)w));

        /// <summary>FNV-1a — для превращения строковых «солей» в число.</summary>
        public static uint String(string value)
        {
            uint hash = 2166136261U;
            foreach (char c in value)
            {
                hash ^= c;
                hash *= 16777619U;
            }
            return hash;
        }

        public static float ToFloat01(uint hash) => (hash >> 8) * (1f / 16777216f);

        private static uint Round(uint hash, uint value)
        {
            hash += value * Prime3;
            return RotateLeft(hash, 17) * Prime4;
        }

        private static uint Avalanche(uint hash)
        {
            hash ^= hash >> 15;
            hash *= Prime2;
            hash ^= hash >> 13;
            hash *= Prime3;
            hash ^= hash >> 16;
            return hash;
        }

        private static uint RotateLeft(uint value, int count) => (value << count) | (value >> (32 - count));
    }
}
