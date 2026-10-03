using Unity.Mathematics;

namespace VoidMall.World
{
    /// <summary>
    /// Поля расстояний аномалий в координатах (r — расстояние от оси по горизонтали, y — высота).
    /// Положительное значение — «твёрдое». Аномалии вырезают из мира оболочку толщины ShellThickness
    /// вдоль поверхности: за оболочкой остаётся обычный мол (потолки и комнаты уровня ниже).
    /// </summary>
    public static class MallAnomalies
    {
        /// <summary>Радиус скругления кромки бездны, блоков.</summary>
        public const float LipRadius = 2.5f;

        /// <summary>Ширина ровного гладкого пола за кромкой (плавный стык с блочным полом).</summary>
        public const float ZoneRim = 1.5f;

        /// <summary>
        /// Толщина гладкой оболочки: ровно плита пола и потолочный ряд уровня ниже (стык с блочным потолком без ступеньки).
        /// Тоньше нельзя: куб Surface Nets, задевший и воздух, и блоки за оболочкой, прилипает к углам блоков (шипы и рваный край).
        /// </summary>
        public const float ShellThickness = 2f;

        public static float AbyssZoneRadius(float shaftRadius) => shaftRadius + LipRadius + ZoneRim;

        public static float PoolZoneRadius(float radius) => radius + 2.5f;

        /// <summary>
        /// Бездна: ровный пол → скруглённая кромка → вертикальная шахта без дна.
        /// </summary>
        public static float ShaftSolidDistance(float r, float y, float shaftRadius, float floorTop)
        {
            float lipCenterR = shaftRadius + LipRadius;
            float lipCenterY = floorTop - LipRadius;
            if (r >= lipCenterR)
                return floorTop - y;
            if (y <= lipCenterY)
                return r - shaftRadius;
            return LipRadius - math.distance(new float2(r, y), new float2(lipCenterR, lipCenterY));
        }

        /// <summary>
        /// Котлован: пол минус сферическая чаша радиуса radius у пола и глубины depth, кромка скруглена.
        /// </summary>
        public static float PoolSolidDistance(float r, float y, float radius, float depth, float floorTop)
        {
            float sphereRadius = (radius * radius + depth * depth) / (2f * depth);
            float centerY = floorTop - depth + sphereRadius;
            float bowl = math.distance(new float2(r, y), new float2(0f, centerY)) - sphereRadius; // > 0 — вне чаши
            float floor = floorTop - y;
            return SmoothMin(floor, bowl, 1.2f);
        }

        /// <summary>Оболочка вдоль поверхности: твёрдое только в слое [0, ShellThickness] от поверхности.</summary>
        public static float Shell(float solidDistance) => math.min(solidDistance, ShellThickness - solidDistance);

        private static float SmoothMin(float a, float b, float k)
        {
            float h = math.saturate(0.5f + 0.5f * (b - a) / k);
            return math.lerp(b, a, h) - k * h * (1f - h);
        }
    }
}
