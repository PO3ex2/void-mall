using Unity.Mathematics;

namespace VoidMall.World
{
    /// <summary>
    /// «Выталкивание из вокселей» по данным сетки, а не физики. Коллизия мира — тонкая оболочка треугольников:
    /// если капсула целиком внутри массива, физика не находит касаний и игрок проваливается. Здесь мы смотрим
    /// на плотность вокселей (с той же интерполяцией, что у Surface Nets) и ищем ближайшее свободное место.
    /// </summary>
    public static class EmbeddedEscape
    {
        /// <summary>Центр полусферы капсулы внутри твёрдого — значит, погружение глубже радиуса.</summary>
        private const float EmbeddedDensity = Density.IsoValue;

        /// <summary>Для свободного места требуем запас от поверхности.</summary>
        private const float FreeDensity = 100f;

        private const float Step = 0.25f;
        private const float MaxDistance = 8f;

        public static bool IsEmbedded(IVoxelAccess world, float3 feet, float height, float radius)
        {
            float3 bottom = feet + new float3(0f, radius, 0f);
            float3 top = feet + new float3(0f, math.max(height - radius, radius), 0f);
            return SampleDensity(world, bottom) >= EmbeddedDensity || SampleDensity(world, top) >= EmbeddedDensity;
        }

        public static bool TryFindEscape(IVoxelAccess world, float3 feet, float height, float radius, out float3 free)
        {
            free = feet;
            if (!IsEmbedded(world, feet, height, radius))
                return false;

            // Ближайшее свободное место: расширяем радиус поиска шагами; на каждом радиусе сначала вверх
            // (выбраться из «сугроба»), потом по сторонам, вниз — в последнюю очередь.
            // Так игрока не выкидывает на крышу сквозь потолок, если рядом в комнате есть место.
            for (float distance = Step; distance <= MaxDistance; distance += Step)
            {
                if (TryCandidate(world, feet + new float3(0f, distance, 0f), height, radius, out free))
                    return true;

                for (int direction = 0; direction < 8; direction++)
                {
                    float angle = direction * math.PI * 0.25f;
                    float3 offset = new float3(math.cos(angle), 0f, math.sin(angle)) * distance;
                    if (TryCandidate(world, feet + offset, height, radius, out free)
                        || TryCandidate(world, feet + offset + new float3(0f, 0.5f, 0f), height, radius, out free))
                        return true;
                }

                if (TryCandidate(world, feet - new float3(0f, distance, 0f), height, radius, out free))
                    return true;
            }
            free = feet;
            return false;
        }

        private static bool TryCandidate(IVoxelAccess world, float3 candidate, float height, float radius, out float3 free)
        {
            free = candidate;
            return candidate.y >= 0f && IsFree(world, candidate, height, radius);
        }

        /// <summary>Капсула целиком в воздухе: проверяем ось и «обод» на трёх высотах.</summary>
        public static bool IsFree(IVoxelAccess world, float3 feet, float height, float radius)
        {
            float r = radius * 0.9f;
            float3 bottom = feet + new float3(0f, radius, 0f);
            float3 top = feet + new float3(0f, math.max(height - radius, radius), 0f);
            float3 middle = (bottom + top) * 0.5f;
            for (int level = 0; level < 3; level++)
            {
                float3 center = level == 0 ? bottom : level == 1 ? middle : top;
                if (SampleDensity(world, center) > FreeDensity
                    || SampleDensity(world, center + new float3(r, 0f, 0f)) > FreeDensity
                    || SampleDensity(world, center - new float3(r, 0f, 0f)) > FreeDensity
                    || SampleDensity(world, center + new float3(0f, 0f, r)) > FreeDensity
                    || SampleDensity(world, center - new float3(0f, 0f, r)) > FreeDensity)
                    return false;
            }
            return true;
        }

        /// <summary>Плотность в точке: трилинейная интерполяция по центрам клеток (как поле Surface Nets).</summary>
        public static float SampleDensity(IVoxelAccess world, float3 point)
        {
            float3 q = point - 0.5f;
            int3 i0 = (int3)math.floor(q);
            float3 f = q - i0;
            float c000 = CellDensity(world, i0);
            float c100 = CellDensity(world, i0 + new int3(1, 0, 0));
            float c010 = CellDensity(world, i0 + new int3(0, 1, 0));
            float c110 = CellDensity(world, i0 + new int3(1, 1, 0));
            float c001 = CellDensity(world, i0 + new int3(0, 0, 1));
            float c101 = CellDensity(world, i0 + new int3(1, 0, 1));
            float c011 = CellDensity(world, i0 + new int3(0, 1, 1));
            float c111 = CellDensity(world, i0 + new int3(1, 1, 1));
            float x00 = math.lerp(c000, c100, f.x);
            float x10 = math.lerp(c010, c110, f.x);
            float x01 = math.lerp(c001, c101, f.x);
            float x11 = math.lerp(c011, c111, f.x);
            return math.lerp(math.lerp(x00, x10, f.y), math.lerp(x01, x11, f.y), f.z);
        }

        /// <summary>Плотность клетки: блок — 255, гладкий материал — своя, воздух — своя (&lt; 128). Над миром — воздух.</summary>
        private static float CellDensity(IVoxelAccess world, int3 cell)
        {
            if (cell.y >= WorldConstants.WorldHeight || cell.y < 0)
                return 0f;
            if (!world.IsLoaded(cell))
                return 255f; // в незагруженное не выталкиваем
            Cell value = world.GetCell(cell);
            if (value.IsAir)
                return math.min((int)value.Value, Density.Iso - 1);
            return world.GetInfo(value.Material).Kind == MaterialKind.Smooth ? value.Value : 255f;
        }
    }
}
