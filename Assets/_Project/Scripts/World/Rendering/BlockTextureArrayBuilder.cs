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

        /// <summary>Размер массива эмиссии слоёв в шейдере (_VM_LayerEmission).</summary>
        public const int MaxLayers = 64;

        /// <summary>
        /// Эмиссия по слоям текстур: rgb — цвет × яркость, a — LampKind. Слои светящихся материалов уникальны
        /// (ключ слоя включает LampKind), поэтому эмиссию можно хранить по слою.
        /// </summary>
        public static Vector4[] BuildLayerEmission(IReadOnlyList<MaterialDef> materials, IReadOnlyList<ushort> layers)
        {
            var emission = new Vector4[MaxLayers];
            for (int i = 0; i < materials.Count; i++)
            {
                var def = materials[i];
                if (def.Kind == MaterialKind.Air || def.Lamp == LampKind.None || layers[i] >= MaxLayers)
                    continue;
                emission[layers[i]] = new Vector4(def.Tint.r * def.Emission, def.Tint.g * def.Emission, def.Tint.b * def.Emission, (float)def.Lamp);
            }
            return emission;
        }

        /// <param name="layers">Для каждого материала каталога — индекс слоя в массиве.</param>
        public static Texture2DArray Build(IReadOnlyList<MaterialDef> materials, out ushort[] layers)
        {
            layers = new ushort[materials.Count];
            var unique = new Dictionary<(BlockPattern, Color, LampKind), ushort>();
            var pixelsPerLayer = new List<Color32[]>();

            for (int i = 0; i < materials.Count; i++)
            {
                var def = materials[i];
                if (def.Kind == MaterialKind.Air)
                    continue;
                var key = (def.Pattern, def.Lamp != LampKind.None ? Color.white : def.Tint, def.Lamp);
                if (!unique.TryGetValue(key, out ushort layer))
                {
                    layer = (ushort)pixelsPerLayer.Count;
                    unique.Add(key, layer);
                    // У светящихся материалов Tint — цвет свечения, текстура остаётся белой.
                    pixelsPerLayer.Add(Generate(def.Pattern, key.Item2, (uint)layer + 1));
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
                case BlockPattern.Soil:
                {
                    // Грунт: крупные комья, мелкие камешки.
                    float clumps = Fbm(u, v, 4, 4, seed, 4);
                    float pebble = Noise(x / 2, y / 2, seed + 9) > 0.9f ? 0.12f : 0f;
                    float g = 0.75f + clumps * 0.35f + pebble;
                    return new Color(0.42f * g, 0.32f * g, 0.22f * g, 0.08f);
                }
                case BlockPattern.LampPanel:
                {
                    // Светильник 600×600: рамка и рассеиватель с решёткой.
                    float frame = Mathf.Min(Mathf.Min(u, 1f - u), Mathf.Min(v, 1f - v));
                    if (frame < 0.06f)
                        return new Color(0.55f, 0.55f, 0.55f, 0.4f);
                    bool louver = Mathf.Repeat(u * 6f, 1f) < 0.06f || Mathf.Repeat(v * 6f, 1f) < 0.06f;
                    float g = louver ? 0.78f : 1f;
                    return new Color(g, g, g * 0.98f, 0.5f);
                }
                case BlockPattern.EmergencyLamp:
                {
                    // Корпус тёмный, светится только табличка в центре.
                    bool sign = Mathf.Abs(u - 0.5f) < 0.3f && Mathf.Abs(v - 0.5f) < 0.16f;
                    return sign ? new Color(1f, 0.32f, 0.22f, 0.5f) : new Color(0.06f, 0.06f, 0.06f, 0.3f);
                }
                case BlockPattern.Skylight:
                {
                    // Матовое стекло фонаря: 2×2 стекла, переплёт.
                    float mu = Mathf.Repeat(u * 2f, 1f), mv = Mathf.Repeat(v * 2f, 1f);
                    float mullion = Mathf.Min(Mathf.Min(mu, 1f - mu), Mathf.Min(mv, 1f - mv));
                    if (mullion < 0.04f)
                        return new Color(0.3f, 0.32f, 0.34f, 0.6f);
                    float g = 0.92f + Fbm(u, v, 4, 4, seed, 3) * 0.08f;
                    return new Color(g * 0.92f, g * 0.97f, g, 0.8f);
                }
                case BlockPattern.Terrazzo:
                {
                    // Терраццо: светлая основа с разноцветной крошкой.
                    float baseTone = 0.8f + Fbm(u, v, 6, 6, seed, 3) * 0.08f;
                    Color c = new(baseTone, baseTone * 0.97f, baseTone * 0.92f, 0.65f);
                    uint chip = Hash.Get(seed, x / 4, y / 4);
                    if (chip % 100 < 14)
                    {
                        float t = Hash.ToFloat01(chip);
                        c = t < 0.4f ? new Color(0.45f, 0.42f, 0.4f, 0.7f) : t < 0.7f ? new Color(0.72f, 0.55f, 0.45f, 0.7f) : new Color(0.3f, 0.32f, 0.33f, 0.7f);
                    }
                    return c;
                }
                case BlockPattern.CheckerTile:
                {
                    float tu = Mathf.Repeat(u * 2f, 1f), tv = Mathf.Repeat(v * 2f, 1f);
                    float grout = Mathf.Min(Mathf.Min(tu, 1f - tu), Mathf.Min(tv, 1f - tv));
                    if (grout < 0.02f)
                        return new Color(0.5f, 0.5f, 0.5f, 0.2f);
                    bool dark = ((int)(u * 2f) + (int)(v * 2f)) % 2 == 1;
                    float g = (dark ? 0.16f : 0.88f) + Fbm(u, v, 6, 6, seed, 2) * 0.04f;
                    return new Color(g, g, g, 0.75f);
                }
                case BlockPattern.Linoleum:
                {
                    float mottle = Fbm(u, v, 5, 5, seed, 4);
                    float g = 0.8f + mottle * 0.12f + (Noise(x, y, seed) - 0.5f) * 0.03f;
                    return new Color(g, g, g, 0.45f);
                }
                case BlockPattern.Shutter:
                {
                    // Рольставня: горизонтальные ламели.
                    float rib = Mathf.Repeat(v * 10f, 1f);
                    float shade = rib < 0.15f ? 0.62f : rib > 0.85f ? 0.8f : 0.72f;
                    float g = shade + Noise(x / 8, y, seed) * 0.04f;
                    return new Color(g, g * 1.01f, g * 1.03f, 0.55f);
                }
                case BlockPattern.Asphalt:
                case BlockPattern.ParkingLineU:
                case BlockPattern.ParkingLineV:
                {
                    float n = Fbm(u, v, 8, 8, seed, 4);
                    float speck = Noise(x, y, seed + 5) > 0.9f ? 0.06f : 0f;
                    float g = 0.24f + n * 0.08f + speck;
                    // Линия разметки идёт вдоль u (по X мира на полу) или вдоль v (по Z).
                    float distance = pattern == BlockPattern.ParkingLineU ? Mathf.Abs(v - 0.5f) : pattern == BlockPattern.ParkingLineV ? Mathf.Abs(u - 0.5f) : 1f;
                    if (distance < 0.06f)
                    {
                        float wear = Fbm(u, v, 8, 8, seed + 11, 3);
                        if (wear > 0.35f)
                            return new Color(0.86f, 0.82f, 0.62f, 0.25f);
                    }
                    return new Color(g, g, g * 1.02f, 0.2f);
                }
                case BlockPattern.Grass:
                {
                    // Трава: тёмная основа, светлые травинки (вытянуты по v), редкие сухие.
                    float blade = Noise(x, y / 4, seed) * 0.6f + Noise(x / 2, y / 8, seed + 1) * 0.4f;
                    float patch = Fbm(u, v, 4, 4, seed + 3, 3);
                    float g = 0.55f + blade * 0.45f + patch * 0.2f;
                    bool dry = Noise(x / 3, y / 6, seed + 5) > 0.93f;
                    return dry ? new Color(0.55f * g, 0.5f * g, 0.25f * g, 0.1f) : new Color(0.22f * g, 0.42f * g, 0.16f * g, 0.12f);
                }
                case BlockPattern.SkyScreen:
                {
                    // Экран: 16×16 пикселей на блок, в каждом три полоски R, G, B и тёмная сетка. Издали — голубое небо.
                    int px = x & 7, py = y & 7;
                    if (py == 7 || px >= 6)
                        return new Color(0.03f, 0.03f, 0.04f, 0.6f);
                    int sub = px / 2;
                    float cloud = 0.92f + Noise(x / 8, y / 8, seed) * 0.08f;
                    return sub == 0 ? new Color(0.55f * cloud, 0.03f, 0.03f, 0.6f)
                        : sub == 1 ? new Color(0.03f, 0.75f * cloud, 0.05f, 0.6f)
                        : new Color(0.04f, 0.05f, 1f * cloud, 0.6f);
                }
                case BlockPattern.FacadePanel:
                {
                    float frame = Mathf.Min(Mathf.Min(u, 1f - u), Mathf.Min(v, 1f - v));
                    float g = frame < 0.015f ? 0.6f : 0.85f + Fbm(u, v, 3, 3, seed, 2) * 0.06f;
                    return new Color(g, g, g, 0.6f);
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
