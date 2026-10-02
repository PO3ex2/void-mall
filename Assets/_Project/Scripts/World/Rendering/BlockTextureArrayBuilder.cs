using System.Collections.Generic;
using UnityEngine;
using VoidMall.Core;

namespace VoidMall.World
{
    /// <summary>
    /// Собирает Texture2DArray блоков из процедурных узоров (заглушки до настоящих PBR-текстур).
    /// RGB — альбедо, A — гладкость (для прозрачных материалов — непрозрачность).
    /// </summary>
    public static class BlockTextureArrayBuilder
    {
        public const int Resolution = 128;

        /// <param name="layers">Для каждого материала каталога — индекс слоя в массиве.</param>
        public static Texture2DArray Build(IReadOnlyList<MaterialDef> materials, out ushort[] layers)
        {
            layers = new ushort[materials.Count];
            var unique = new Dictionary<(BlockPattern, Color), ushort>();
            var pixelsPerLayer = new List<Color32[]>();

            for (int i = 0; i < materials.Count; i++)
            {
                var def = materials[i];
                if (def.Kind == MaterialKind.Air)
                    continue;
                var key = (def.Pattern, def.Tint);
                if (!unique.TryGetValue(key, out ushort layer))
                {
                    layer = (ushort)pixelsPerLayer.Count;
                    unique.Add(key, layer);
                    pixelsPerLayer.Add(Generate(def.Pattern, def.Tint, (uint)layer + 1));
                }
                layers[i] = layer;
            }
            if (pixelsPerLayer.Count == 0)
                pixelsPerLayer.Add(Generate(BlockPattern.Flat, Color.magenta, 1));

            var array = new Texture2DArray(Resolution, Resolution, pixelsPerLayer.Count, TextureFormat.RGBA32, true, false)
            {
                name = "BlockTextures",
                filterMode = FilterMode.Trilinear,
                wrapMode = TextureWrapMode.Repeat,
                anisoLevel = 8,
            };
            for (int i = 0; i < pixelsPerLayer.Count; i++)
                array.SetPixels32(pixelsPerLayer[i], i, 0);
            array.Apply(updateMipmaps: true, makeNoLongerReadable: true);
            return array;
        }

        private static Color32[] Generate(BlockPattern pattern, Color tint, uint seed)
        {
            var pixels = new Color32[Resolution * Resolution];
            for (int y = 0; y < Resolution; y++)
            for (int x = 0; x < Resolution; x++)
            {
                float u = (x + 0.5f) / Resolution;
                float v = (y + 0.5f) / Resolution;
                Color c = Sample(pattern, u, v, x, y, seed);
                c.r *= tint.r;
                c.g *= tint.g;
                c.b *= tint.b;
                pixels[x + y * Resolution] = c;
            }
            return pixels;
        }

        private static Color Sample(BlockPattern pattern, float u, float v, int x, int y, uint seed)
        {
            switch (pattern)
            {
                case BlockPattern.Bedrock:
                {
                    float n = Fbm(u, v, 6, 6, seed, 4);
                    float g = Mathf.Lerp(0.12f, 0.3f, n);
                    return new Color(g, g, g * 1.05f, 0.1f);
                }
                case BlockPattern.Concrete:
                {
                    float n = Fbm(u, v, 8, 8, seed, 4);
                    float speck = Noise(x, y, seed + 7) > 0.93f ? -0.08f : 0f;
                    float g = Mathf.Lerp(0.48f, 0.62f, n) + speck;
                    return new Color(g, g, g * 0.98f, 0.18f);
                }
                case BlockPattern.Carpet:
                {
                    // Ворсистый ковёр «как в Backrooms»: мелкий шум + лёгкие пятна.
                    float fine = Noise(x, y, seed);
                    float blotch = Fbm(u, v, 3, 3, seed + 3, 3);
                    float g = 0.75f + fine * 0.18f - blotch * 0.12f;
                    return new Color(0.62f * g, 0.55f * g, 0.38f * g, 0.05f);
                }
                case BlockPattern.Wallpaper:
                {
                    // Жёлтые обои: вертикальные полосы и слабый ромбовидный узор.
                    float stripe = Mathf.Abs(Mathf.Sin(u * Mathf.PI * 8f));
                    float diamond = Mathf.Abs(Mathf.Repeat(u * 4f + v * 4f, 1f) - 0.5f) + Mathf.Abs(Mathf.Repeat(u * 4f - v * 4f, 1f) - 0.5f);
                    float age = Fbm(u, v, 4, 4, seed, 3) * 0.12f;
                    float g = 0.88f + stripe * 0.05f - (diamond < 0.12f ? 0.05f : 0f) - age;
                    return new Color(0.86f * g, 0.79f * g, 0.5f * g, 0.12f);
                }
                case BlockPattern.Drywall:
                {
                    float n = Fbm(u, v, 10, 10, seed, 3);
                    float g = 0.86f + n * 0.06f;
                    return new Color(g, g * 0.99f, g * 0.96f, 0.15f);
                }
                case BlockPattern.CeilingTile:
                {
                    // Армстронг-панель: рамка + перфорация точками.
                    float frame = Mathf.Min(Mathf.Min(u, 1f - u), Mathf.Min(v, 1f - v));
                    if (frame < 0.025f)
                        return new Color(0.7f, 0.7f, 0.68f, 0.35f);
                    bool hole = Noise(x / 3, y / 3, seed) > 0.82f;
                    float g = hole ? 0.68f : 0.9f - Noise(x, y, seed + 2) * 0.04f;
                    return new Color(g, g, g * 0.97f, 0.08f);
                }
                case BlockPattern.FloorTile:
                {
                    // Плитка 50×50 см: 2×2 плитки на блок, затирка, лёгкий разнобой тона.
                    float tu = Mathf.Repeat(u * 2f, 1f);
                    float tv = Mathf.Repeat(v * 2f, 1f);
                    float grout = Mathf.Min(Mathf.Min(tu, 1f - tu), Mathf.Min(tv, 1f - tv));
                    if (grout < 0.02f)
                        return new Color(0.55f, 0.55f, 0.53f, 0.2f);
                    int tile = (int)(u * 2f) + (int)(v * 2f) * 2;
                    float tone = Hash.ToFloat01(Hash.Get(seed, tile)) * 0.05f;
                    float g = 0.88f - tone + Fbm(u, v, 6, 6, seed, 2) * 0.04f;
                    return new Color(g, g, g * 0.98f, 0.7f);
                }
                case BlockPattern.Metal:
                {
                    // Шлифованный металл: вытянутый по одной оси шум.
                    float brushed = Noise(x / 16, y, seed) * 0.6f + Noise(x / 4, y, seed + 1) * 0.4f;
                    float g = 0.55f + brushed * 0.15f;
                    return new Color(g, g * 1.01f, g * 1.04f, 0.75f);
                }
                case BlockPattern.Glass:
                {
                    float frame = Mathf.Min(Mathf.Min(u, 1f - u), Mathf.Min(v, 1f - v));
                    if (frame < 0.03f)
                        return new Color(0.55f, 0.6f, 0.62f, 0.9f);
                    float streak = Mathf.Repeat(u + v, 0.5f) < 0.04f ? 0.06f : 0f;
                    return new Color(0.78f + streak, 0.88f + streak, 0.92f + streak, 0.22f + streak);
                }
                case BlockPattern.Wood:
                {
                    // Доски 4 шт. на блок, волокна вдоль u.
                    float plank = Mathf.Repeat(v * 4f, 1f);
                    int plankIndex = (int)(v * 4f);
                    float grain = Mathf.Sin((u * Mathf.PI * 12f + Fbm(u, v, 4, 16, seed + (uint)plankIndex, 3) * 6f)) * 0.5f + 0.5f;
                    float gap = plank < 0.04f ? 0.55f : 1f;
                    float tone = 0.85f + Hash.ToFloat01(Hash.Get(seed, plankIndex)) * 0.15f;
                    float g = (0.75f + grain * 0.1f) * tone * gap;
                    return new Color(0.62f * g, 0.45f * g, 0.3f * g, 0.25f);
                }
                default:
                    return new Color(0.8f, 0.8f, 0.8f, 0.2f);
            }
        }

        /// <summary>Шум одной точки пикселя (детерминированный).</summary>
        private static float Noise(int x, int y, uint seed) => Hash.ToFloat01(Hash.Get(seed, x & (Resolution - 1), y & (Resolution - 1)));

        /// <summary>
        /// Тайлящийся fbm на value-noise: на каждой октаве решётка имеет целое число ячеек на текстуру,
        /// поэтому левый край совпадает с правым и швов между блоками нет.
        /// </summary>
        private static float Fbm(float u, float v, int scaleU, int scaleV, uint seed, int octaves)
        {
            float sum = 0f, amplitude = 0.5f, norm = 0f;
            int periodU = scaleU, periodV = scaleV;
            for (int o = 0; o < octaves; o++)
            {
                sum += ValueNoise(u * periodU, v * periodV, periodU, periodV, seed + (uint)o * 31u) * amplitude;
                norm += amplitude;
                amplitude *= 0.5f;
                periodU *= 2;
                periodV *= 2;
            }
            return sum / norm;
        }


        private static float ValueNoise(float x, float y, int periodX, int periodY, uint seed)
        {
            int x0 = Mathf.FloorToInt(x);
            int y0 = Mathf.FloorToInt(y);
            float fx = x - x0;
            float fy = y - y0;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            float a = Lattice(x0, y0, periodX, periodY, seed);
            float b = Lattice(x0 + 1, y0, periodX, periodY, seed);
            float c = Lattice(x0, y0 + 1, periodX, periodY, seed);
            float d = Lattice(x0 + 1, y0 + 1, periodX, periodY, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        private static float Lattice(int x, int y, int periodX, int periodY, uint seed)
        {
            x = ((x % periodX) + periodX) % periodX;
            y = ((y % periodY) + periodY) % periodY;
            return Hash.ToFloat01(Hash.Get(seed, x, y));
        }
    }
}
