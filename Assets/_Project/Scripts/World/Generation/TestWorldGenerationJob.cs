using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using VoidMall.Core;

namespace VoidMall.World
{
    /// <summary>Индексы материалов, нужных генератору (резолвятся по ID при старте).</summary>
    public struct TestWorldMaterials
    {
        public ushort Bedrock, Concrete, Carpet, FloorTile, Wallpaper, Drywall, CeilingTile, Glass, Wood, Metal, ConcreteMass, Soil, SmoothCarpet;

        public static TestWorldMaterials Resolve(MaterialRegistry registry) => new()
        {
            Bedrock = registry.IndexOf("block.bedrock"),
            Concrete = registry.IndexOf("block.concrete"),
            Carpet = registry.IndexOf("block.carpet"),
            FloorTile = registry.IndexOf("block.floor_tile"),
            Wallpaper = registry.IndexOf("block.wallpaper"),
            Drywall = registry.IndexOf("block.drywall"),
            CeilingTile = registry.IndexOf("block.ceiling_tile"),
            Glass = registry.IndexOf("block.glass"),
            Wood = registry.IndexOf("block.wood"),
            Metal = registry.IndexOf("block.metal"),
            ConcreteMass = registry.IndexOf("smooth.concrete"),
            Soil = registry.IndexOf("smooth.soil"),
            SmoothCarpet = registry.IndexOf("smooth.carpet"),
        };
    }

    /// <summary>
    /// Временный генератор этапов 2–3 (заменится генерацией мола на этапе 4): гладкий массив (бетон, грунт) под полом,
    /// пол на высоте 18, лабиринт комнат 8×8 со стенами из обоев, дверями, стеклом и частичным потолком,
    /// аномалии-«бездны» (шахта без дна со скруглённой кромкой). Детерминирован по сиду и координатам.
    /// </summary>
    [BurstCompile]
    public struct TestWorldGenerationJob : IJob
    {
        public const int FloorBlockY = 17;
        public const int WallTopY = 22;
        public const int CeilingY = 23;
        private const int RoomSize = 8;

        public int3 Chunk;
        public uint Seed;
        public TestWorldMaterials M;

        public NativeArray<Cell> Cells;

        /// <summary>[0] = 1, если все клетки чанка одинаковые.</summary>
        public NativeArray<byte> IsUniform;

        public void Execute()
        {
            int3 origin = Chunk * WorldConstants.ChunkSize;

            // Данные вертикальной колонки (комната, воронка) считаем один раз на (x, z), а не на каждую клетку.
            for (int z = 0; z < WorldConstants.ChunkSize; z++)
            for (int x = 0; x < WorldConstants.ChunkSize; x++)
            {
                var column = GetColumn(origin.x + x, origin.z + z);
                for (int y = 0; y < WorldConstants.ChunkSize; y++)
                    Cells[WorldConstants.CellIndex(x, y, z)] = Generate(origin + new int3(x, y, z), column);
            }

            Cell first = Cells[0];
            bool uniform = true;
            for (int i = 1; i < Cells.Length && uniform; i++)
                uniform = Cells[i] == first;
            IsUniform[0] = (byte)(uniform ? 1 : 0);
        }

        private struct ColumnInfo
        {
            public int RoomX, RoomZ, Rx, Rz;

            /// <summary>Колонка в зоне аномалии: стены убраны, пол гладкий, потолок сплошной.</summary>
            public bool InShaftZone;

            /// <summary>Расстояние по горизонтали до оси шахты и её радиус.</summary>
            public float Distance, ShaftRadius;
        }

        private ColumnInfo GetColumn(int x, int z)
        {
            var info = new ColumnInfo
            {
                RoomX = FloorDiv(x, RoomSize),
                RoomZ = FloorDiv(z, RoomSize),
            };
            info.Rx = x - info.RoomX * RoomSize;
            info.Rz = z - info.RoomZ * RoomSize;

            if (TryFindShaft(Seed, new float2(x + 0.5f, z + 0.5f), out float distance, out float radius))
            {
                info.InShaftZone = true;
                info.Distance = distance;
                info.ShaftRadius = radius;
            }
            return info;
        }

        private Cell Generate(int3 p, in ColumnInfo column)
        {
            if (column.InShaftZone)
                return GenerateShaft(p, column);

            if (p.y == 0)
                return new Cell(M.Bedrock);

            // Гладкий массив под полом: бетон, ниже — грунт.
            if (p.y < FloorBlockY)
                return new Cell(p.y <= SoilTopY ? M.Soil : M.ConcreteMass, 255);

            if (p.y == FloorBlockY)
                return new Cell(Hash.Get(Seed, column.RoomX, column.RoomZ, 4) % 4 == 0 ? M.FloorTile : M.Carpet);

            if (p.y <= WallTopY)
                return GenerateRoomContent(p.y, column.RoomX, column.RoomZ, column.Rx, column.Rz);

            if (p.y == CeilingY)
                return Hash.Get(Seed, column.RoomX, column.RoomZ, 5) % 3 != 0 ? new Cell(M.CeilingTile) : Cell.Air;

            return Cell.Air;
        }

        /// <summary>
        /// Аномалия «Бездна»: ровный пол → скруглённая кромка (≈2.5 блока) → вертикальная шахта без дна,
        /// прорезающая даже основание мира. Форма — поле расстояния в координатах (радиус, высота).
        /// На краю зоны поверхность ровно на высоте пола, поэтому стык с блочным полом без шва.
        /// </summary>
        private Cell GenerateShaft(int3 p, in ColumnInfo column)
        {
            if (p.y == CeilingY)
                return new Cell(M.CeilingTile);
            if (p.y > FloorBlockY + 1)
                return Cell.Air;

            float solid = ShaftSolidDistance(column.Distance, p.y + 0.5f, column.ShaftRadius);
            byte density = Density.FromField(solid);
            if (!Density.IsSolid(density))
                return new Cell(0, density);
            if (p.y == 0 && solid >= 1f)
                return new Cell(M.Bedrock);

            // Ковёр — на полу и кромке, ниже стены шахты голые: бетон, затем грунт.
            bool lining = solid < LiningThickness && p.y + 0.5f > FloorTopY - LipRadius - 2f;
            ushort material = lining ? M.SmoothCarpet : p.y <= SoilTopY ? M.Soil : M.ConcreteMass;
            return new Cell(material, density);
        }

        /// <summary>
        /// Расстояние до поверхности внутрь твёрдого (&gt; 0 — твёрдое): плита пола минус шахта радиуса shaftRadius
        /// со скруглением-галтелью радиуса LipRadius между полом и стеной шахты.
        /// </summary>
        public static float ShaftSolidDistance(float r, float y, float shaftRadius)
        {
            float lipCenterR = shaftRadius + LipRadius;
            float lipCenterY = FloorTopY - LipRadius;
            if (r >= lipCenterR)
                return FloorTopY - y;          // ровный пол
            if (y <= lipCenterY)
                return r - shaftRadius;        // вертикальная стена шахты
            return LipRadius - math.distance(new float2(r, y), new float2(lipCenterR, lipCenterY)); // кромка
        }

        private Cell GenerateRoomContent(int y, int roomX, int roomZ, int rx, int rz)
        {
            // Колонны на пересечениях линий сетки.
            if (rx == 0 && rz == 0)
                return new Cell(M.Drywall);

            // Стена вдоль Z по линии rx == 0 (между комнатами roomX-1 и roomX).
            if (rx == 0 && IsWall(roomX, roomZ, axis: 1, rz))
                return new Cell(WallMaterial(roomX, roomZ, 1));

            // Стена вдоль X по линии rz == 0.
            if (rz == 0 && IsWall(roomX, roomZ, axis: 2, rx))
                return new Cell(WallMaterial(roomX, roomZ, 2));

            // Немного «мебели»: деревянные ящики и металлический короб.
            uint props = Hash.Get(Seed, roomX, roomZ, 6);
            if (y == FloorBlockY + 1)
            {
                if (props % 5 == 0 && rz == 5 && (rx == 5 || rx == 6))
                    return new Cell(M.Wood);
                if (props % 7 == 0 && rx == 2 && rz == 6)
                    return new Cell(M.Metal);
            }
            if (y == FloorBlockY + 2 && props % 10 == 0 && rz == 5 && rx == 5)
                return new Cell(M.Wood);

            return Cell.Air;
        }

        private bool IsWall(int roomX, int roomZ, int axis, int along)
        {
            uint h = Hash.Get(Seed, roomX, roomZ, axis);
            if (h % 100 >= 55)
                return false;
            // Дверной проём шириной 2 в середине стены.
            bool hasDoor = (h >> 8) % 3 != 0;
            return !(hasDoor && (along == 3 || along == 4));
        }

        private ushort WallMaterial(int roomX, int roomZ, int axis)
        {
            uint h = Hash.Get(Seed, roomX, roomZ, axis + 10);
            return h % 12 == 0 ? M.Glass : M.Wallpaper;
        }

        // ───────────── Аномалия «Бездна» ─────────────

        public const int AnomalyCellSize = 96;

        /// <summary>Радиус скругления кромки, блоков.</summary>
        public const float LipRadius = 2.5f;

        /// <summary>Ширина зоны за кромкой, где пол уже гладкий (ровный стык с блочным полом).</summary>
        private const float ZoneRim = 1.5f;

        private const float LiningThickness = 1.5f;
        private const int SoilTopY = 8;
        private const float FloorTopY = FloorBlockY + 1;

        /// <summary>Полный радиус зоны аномалии (шахта + кромка + гладкий пол вокруг).</summary>
        public static float ZoneRadius(float shaftRadius) => shaftRadius + LipRadius + ZoneRim;

        /// <summary>
        /// Шахта-бездна в ячейке макросетки 96×96 м (примерно в каждой третьей): центр и радиус шахты 6–12 м.
        /// </summary>
        public static bool TryGetShaft(uint seed, int cellX, int cellZ, out float2 center, out float radius)
        {
            center = default;
            radius = 0f;
            if (Hash.Get(seed, cellX, cellZ, 100) % 3 != 0)
                return false;
            radius = 6f + Hash.Get(seed, cellX, cellZ, 101) % 600 / 100f;
            float margin = ZoneRadius(radius) + 2f;
            float span = AnomalyCellSize - 2f * margin;
            center = new float2(
                cellX * AnomalyCellSize + margin + Hash.ToFloat01(Hash.Get(seed, cellX, cellZ, 102)) * span,
                cellZ * AnomalyCellSize + margin + Hash.ToFloat01(Hash.Get(seed, cellX, cellZ, 103)) * span);
            return true;
        }

        /// <summary>Шахта, в зону которой попадает точка (проверяются соседние ячейки макросетки).</summary>
        public static bool TryFindShaft(uint seed, float2 point, out float distance, out float radius)
        {
            distance = float.MaxValue;
            radius = 0f;
            int cellX = FloorDiv((int)math.floor(point.x), AnomalyCellSize);
            int cellZ = FloorDiv((int)math.floor(point.y), AnomalyCellSize);
            bool found = false;
            for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (!TryGetShaft(seed, cellX + dx, cellZ + dz, out float2 center, out float r))
                    continue;
                float dist = math.distance(point, center);
                if (dist >= ZoneRadius(r) || dist >= distance)
                    continue;
                distance = dist;
                radius = r;
                found = true;
            }
            return found;
        }

        private static int FloorDiv(int value, int divisor) => value >= 0 ? value / divisor : (value - divisor + 1) / divisor;
    }
}
