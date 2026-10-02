using Unity.Mathematics;

namespace VoidMall.World
{
    /// <summary>
    /// Плотность гладких вокселей. В клетке хранится байт 0…255 (Cell.Value), поверхность — между 127 и 128.
    /// Удобнее считать в «поле» f ∈ [−1, 1]: f > 0 — твёрдое, f ≤ 0 — пусто.
    /// Блоки для Surface Nets всегда плотность 255; воздух хранит свою плотность (обычно 0), чтобы края были плавными.
    /// </summary>
    public static class Density
    {
        public const byte Iso = 128;
        public const float IsoValue = 127.5f;

        public static bool IsSolid(byte density) => density >= Iso;

        public static float ToField(byte density) => density / 127.5f - 1f;

        public static byte FromField(float field) => (byte)math.clamp((int)math.round((math.clamp(field, -1f, 1f) + 1f) * 127.5f), 0, 255);

        /// <summary>Поле сферы: +1 глубоко внутри, −1 снаружи, переход шириной 2·falloff метров вокруг поверхности.</summary>
        public static float SphereField(float3 point, float3 center, float radius, float falloff = 1f) =>
            math.clamp((radius - math.distance(point, center)) / falloff, -1f, 1f);
    }
}
