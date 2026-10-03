using Unity.Mathematics;

namespace VoidMall.World
{
    public enum LevelKind : byte
    {
        Parking,
        Retail,
        Tech,
    }

    /// <summary>
    /// Геометрия мола (GDD §2.1). Вертикальный разрез, снизу вверх:
    /// <code>
    ///  y 0        основание (неразрушимое)
    ///  y 1…7      фундамент: грунт (1…4), бетонный массив (5…7) — гладкие воксели
    ///  уровень 0  P2  плита 8,  свободно 9…11,  потолок 12
    ///  уровень 1  P1  плита 13, свободно 14…16, потолок 17
    ///  уровень 2  этаж 1  плита 18, свободно 19…23, потолочный ряд 24
    ///  …          каждый торговый этаж — 7 блоков
    ///  уровень 6  этаж 5  плита 46, свободно 47…51, потолок 52
    ///  уровень 7  технический этаж  плита 53, свободно 54…58, потолок 59
    ///  y 60       кровля
    /// </code>
    /// По горизонтали мир разбит на макроячейки 64×64: вдоль западного и южного края ячейки идут галереи
    /// шириной 14 (на пересечении — площадь с колонной), остальное — квартал 50×50 с магазинами.
    /// Чанк (32) целиком лежит в одной макроячейке.
    /// </summary>
    public static class MallLayout
    {
        public const int CellSize = 64;
        public const int CellShift = 6;
        public const int Band = 14;
        public const int BlockSize = CellSize - Band;

        public const int LevelCount = 8;
        public const int ParkingLevels = 2;
        public const int FirstRetailLevel = 2;
        public const int RetailFloors = 5;
        public const int TechLevel = 7;
        public const int RoofY = 60;
        public const int SoilTopY = 4;
        public const int FoundationTopY = 7;

        public static int LevelBase(int level) => level < ParkingLevels ? 8 + level * 5 : 18 + (level - ParkingLevels) * 7;

        /// <summary>Свободная высота уровня (между плитой и потолочным рядом).</summary>
        public static int FreeHeight(int level) => level < ParkingLevels ? 3 : 5;

        public static int LevelHeight(int level) => FreeHeight(level) + 2;

        /// <summary>Высота поверхности пола (верх плиты) — здесь стоят ноги.</summary>
        public static int FloorTop(int level) => LevelBase(level) + 1;

        public static int CeilingRow(int level) => LevelBase(level) + FreeHeight(level) + 1;

        /// <summary>Высота проёмов дверей.</summary>
        public static int DoorHeight(int level) => level < ParkingLevels ? 2 : 3;

        public static LevelKind Kind(int level) =>
            level < ParkingLevels ? LevelKind.Parking : level == TechLevel ? LevelKind.Tech : LevelKind.Retail;

        /// <summary>Уровень по высоте клетки: −1 — фундамент, LevelCount — кровля и выше.</summary>
        public static int LevelAt(int y)
        {
            if (y < 8)
                return -1;
            if (y < 18)
                return (y - 8) / 5;
            if (y < RoofY)
                return ParkingLevels + (y - 18) / 7;
            return LevelCount;
        }

        /// <summary>Номер торгового этажа (0…4) или −1.</summary>
        public static int RetailFloor(int level) => level >= FirstRetailLevel && level < TechLevel ? level - FirstRetailLevel : -1;

        public static string LevelName(int level) => level switch
        {
            0 => "P2",
            1 => "P1",
            TechLevel => "T",
            _ when level > 1 && level < TechLevel => (level - 1).ToString(),
            _ => "?",
        };

        public static int2 CellOf(int x, int z) => new(x >> CellShift, z >> CellShift);

        public static int2 CellOf(float3 position) => CellOf((int)math.floor(position.x), (int)math.floor(position.z));

        public static int2 CellOrigin(int2 cell) => cell * CellSize;

        /// <summary>Уровень, на полу которого стоит точка (по ногам).</summary>
        public static int LevelAtFeet(float y)
        {
            int level = LevelAt((int)math.floor(y - 0.5f));
            return math.clamp(level, 0, LevelCount - 1);
        }
    }
}
