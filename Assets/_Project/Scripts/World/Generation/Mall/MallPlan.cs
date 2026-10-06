using System;
using Unity.Collections;
using Unity.Mathematics;

namespace VoidMall.World
{
    /// <summary>Что стоит в столбике (x, z) на уровне. Высоту материалов решает генератор по ряду внутри уровня.</summary>
    public enum Tile : byte
    {
        /// <summary>Пол, пусто, потолок.</summary>
        Open,

        /// <summary>Перегородка на всю свободную высоту.</summary>
        Wall,

        /// <summary>Проём в перегородке (над ним — стена).</summary>
        Door,

        FacadeWall,
        FacadeGlass,
        FacadeDoor,

        /// <summary>Закрытый магазин: рольставня вместо витрины.</summary>
        Shutter,

        /// <summary>Колонна — сквозь все ряды уровня.</summary>
        Pillar,

        /// <summary>Стена ядра (лестница, лифт, щитовая) — бетон сквозь все ряды, кроме плиты.</summary>
        CoreWall,
        CoreDoor,

        /// <summary>Пустота атриума: нет плиты (кроме этажа 1) и потолка (кроме этажа 5 — там световой фонарь).</summary>
        AtriumVoid,

        /// <summary>Стеклянное ограждение высотой 1 м у края атриума или проёма.</summary>
        Railing,

        /// <summary>Лестничная клетка: полоса марша вверх (A), полоса марша вниз (B), промежуточная площадка.</summary>
        StairLaneA,
        StairLaneB,
        StairMid,

        ElevatorVoid,

        /// <summary>Парковка: проём над пандусом (на P2 нет потолка, на P1 нет плиты).</summary>
        RampHole,

        /// <summary>Парковка: бетонный отбойник высотой 1 м.</summary>
        Barrier,
    }

    [Flags]
    public enum RoomFlags : byte
    {
        None = 0,
        Storefront = 1 << 0,
        Dark = 1 << 1,
        Closed = 1 << 2,
        Empty = 1 << 3,
        Service = 1 << 4,
        Stairwell = 1 << 5,

        /// <summary>Служебный коридор-тупик между магазинами (GDD §2.9): узкий, кончается стеной.</summary>
        Corridor = 1 << 6,
    }

    /// <summary>Комната плана. Индекс 0 — общественная зона (галерея, коридор, парковка).</summary>
    public struct MallRoom
    {
        /// <summary>Прямоугольник внутренней части в координатах макроячейки: x0, z0 (включительно), x1, z1 (исключительно).</summary>
        public int4 Rect;
        public byte Section;
        public RoomFlags Flags;

        public bool Has(RoomFlags flag) => (Flags & flag) != 0;
    }

    public enum AnomalyKind : byte
    {
        None,

        /// <summary>Бездна: шахта без дна со скруглённой кромкой, прорезает все уровни ниже.</summary>
        Abyss,

        /// <summary>Котлован (pool room): скруглённая чаша в полу, облицованная плиткой.</summary>
        Pool,

        /// <summary>Вырез: колонна 32×32 вырезана от потолка уровня до пустоты под миром.</summary>
        Cutout,

        /// <summary>
        /// Перевёрнутый чанк: колонна 32×32 через все этажи, на каждом этаже содержимое отражено вверх ногами (пол и
        /// потолок меняются местами). Пропы и предметы внутри падают вверх, игрок — как обычно.
        /// </summary>
        Inverted,

        /// <summary>
        /// Оазис: круглая дыра в потолке торгового этажа, над ней шахта на этаж выше с экраном «неба» наверху;
        /// внизу трава, кусты, лианы с краёв, лучи света, иногда дождь. Уютное место: свет и рассудок.
        /// </summary>
        Oasis,
    }

    /// <summary>
    /// Биом макроячейки (GDD §2.9): чем заполнена ячейка на всех уровнях. Соседние ячейки сливаются в пятна.
    /// Мутный лес (этап 12Б) встанет сюда же отдельным значением.
    /// </summary>
    public enum MallBiome : byte
    {
        /// <summary>Обычный мол: мебель, лут, аномалии с базовой частотой.</summary>
        Normal,

        /// <summary>Пустые разделы — самый частый биом: мебели мало, полки пусты, аномалий вдвое меньше.</summary>
        Empty,

        /// <summary>Поле аномалий: аномалии и маркеры в несколько раз чаще, туман и мерцание ламп.</summary>
        AnomalyField,
    }

    public static class MallBiomeNames
    {
        public static string DisplayName(this MallBiome biome) => biome switch
        {
            MallBiome.Empty => "пустые разделы",
            MallBiome.AnomalyField => "поле аномалий",
            _ => "обычный мол",
        };
    }

    /// <summary>Объём-маркер аномалии без геометрии (эффекты в шейдере и на объектах рядом).</summary>
    public enum AnomalyMarkerKind : byte
    {
        Distortion = 1,
        Glitch = 2,

        /// <summary>Пульсирующее искажение: тянет к центру и отталкивает (цикл PulsePeriod), стены «дышат».</summary>
        Pulse = 3,

        /// <summary>«Смотри на меня»: фиолетовая вспышка в воздухе, тянет взгляд к себе и ест рассудок.</summary>
        LookAtMe = 4,

        /// <summary>Светящаяся лужа на полу (центр — на поверхности пола): лечит, пока не иссякнет.</summary>
        Puddle = 5,
    }

    /// <summary>
    /// Класс аномалии (GDD §2.5): геометрические меняют сам мир — их можно только замерить; визуальные — объёмы-эффекты,
    /// их можно засосать пылесосом (§2.11).
    /// </summary>
    public enum AnomalyClass : byte
    {
        Geometric,
        Visual,
    }

    public static class AnomalyNames
    {
        public static AnomalyClass Class(this AnomalyKind kind) => AnomalyClass.Geometric;

        public static AnomalyClass Class(this AnomalyMarkerKind kind) => AnomalyClass.Visual;

        /// <summary>Идентификатор для консоли (goto_anomaly).</summary>
        public static string Id(this AnomalyKind kind) => kind switch
        {
            AnomalyKind.Abyss => "abyss",
            AnomalyKind.Pool => "pool",
            AnomalyKind.Cutout => "cutout",
            AnomalyKind.Inverted => "inverted",
            AnomalyKind.Oasis => "oasis",
            _ => "none",
        };

        public static string Id(this AnomalyMarkerKind kind) => kind switch
        {
            AnomalyMarkerKind.Distortion => "distortion",
            AnomalyMarkerKind.Glitch => "glitch",
            AnomalyMarkerKind.Pulse => "pulse",
            AnomalyMarkerKind.LookAtMe => "lookme",
            AnomalyMarkerKind.Puddle => "puddle",
            _ => "none",
        };

        public static string DisplayName(this AnomalyKind kind) => kind switch
        {
            AnomalyKind.Abyss => "бездна",
            AnomalyKind.Pool => "котлован",
            AnomalyKind.Cutout => "вырез",
            AnomalyKind.Inverted => "перевёрнутый чанк",
            AnomalyKind.Oasis => "оазис",
            _ => "нет",
        };

        public static string DisplayName(this AnomalyMarkerKind kind) => kind switch
        {
            AnomalyMarkerKind.Distortion => "искажение",
            AnomalyMarkerKind.Glitch => "глитч",
            AnomalyMarkerKind.Pulse => "пульсирующее искажение",
            AnomalyMarkerKind.LookAtMe => "«Смотри на меня»",
            AnomalyMarkerKind.Puddle => "светящаяся лужа",
            _ => "нет",
        };

        public static string ClassName(this AnomalyClass value) => value == AnomalyClass.Geometric ? "геометрическая" : "визуальная";
    }

    public struct AnomalyMarker
    {
        public AnomalyMarkerKind Kind;
        public float3 Center;
        public float Radius;
    }

    /// <summary>Неизменные по уровням параметры макроячейки: поворот квартала, ядро, атриумы, пандус, аномалия.</summary>
    public struct MallCellInfo
    {
        public int2 Cell;

        public MallBiome Biome;

        /// <summary>Поворот квартала (0…3, по 90°): ядро прижато к одной из четырёх сторон.</summary>
        public int Rotation;

        /// <summary>Смещение ядра вдоль стороны квартала (канонические координаты).</summary>
        public int CoreOffset;

        /// <summary>Прямоугольник ядра в координатах ячейки.</summary>
        public int4 CoreRect;

        /// <summary>Атриум в галерее вдоль X (южная полоса) и вдоль Z (западная полоса).</summary>
        public bool AtriumX, AtriumZ;

        /// <summary>Балкон-проём вдоль полосы (только где нет атриума): этажи видно друг с друга.</summary>
        public bool BalconyX, BalconyZ;

        /// <summary>Вход в мол на первом этаже (GDD §2.9): стеклянный фасад с автодверями поперёк западной галереи, турникеты, кассы.</summary>
        public bool HasEntrance;

        /// <summary>Ячейка старта или её сосед: всегда обычный биом, без тупиков.</summary>
        public bool NearSpawn;

        public bool HasRamp;

        /// <summary>Угол пандуса P2→P1 в координатах ячейки; пандус 20×4, подъём к +X.</summary>
        public int2 RampMin;

        public AnomalyKind Anomaly;
        public int AnomalyLevel;

        /// <summary>Центр аномалии в мировых XZ (для выреза — угол чанка).</summary>
        public float2 AnomalyCenter;

        /// <summary>Бездна — радиус шахты; котлован — радиус чаши у пола.</summary>
        public float AnomalyRadius;

        /// <summary>Котлован — глубина чаши.</summary>
        public float AnomalyDepth;

        /// <summary>
        /// Большая бездна соседней ячейки, дотянувшаяся сюда (у ячейки может быть и своя аномалия — их зоны не
        /// пересекаются). Генерация, пропы и структуры учитывают её наравне со своей, в списки аномалий она не входит.
        /// </summary>
        public bool HasBigAbyss;
        public float2 BigAbyssCenter;
        public float BigAbyssRadius;
        public int BigAbyssLevel;

        /// <summary>Эта же ячейка, но с большой бездной соседа на месте своей аномалии (для общих проверок).</summary>
        public MallCellInfo WithBigAbyss()
        {
            var copy = this;
            copy.Anomaly = AnomalyKind.Abyss;
            copy.AnomalyCenter = BigAbyssCenter;
            copy.AnomalyRadius = BigAbyssRadius;
            copy.AnomalyLevel = BigAbyssLevel;
            copy.AnomalyDepth = 0f;
            copy.HasBigAbyss = false;
            return copy;
        }

        /// <summary>Сколько маркеров помещается в ячейку (в поле аномалий их много).</summary>
        public const int MaxMarkers = 6;

        public int MarkerCount;
        public AnomalyMarker Marker0, Marker1, Marker2, Marker3, Marker4, Marker5;

        public AnomalyMarker GetMarker(int index) => index switch
        {
            0 => Marker0,
            1 => Marker1,
            2 => Marker2,
            3 => Marker3,
            4 => Marker4,
            _ => Marker5,
        };

        /// <summary>Добавить маркер; ложь — места нет.</summary>
        public bool AddMarker(in AnomalyMarker marker)
        {
            switch (MarkerCount)
            {
                case 0: Marker0 = marker; break;
                case 1: Marker1 = marker; break;
                case 2: Marker2 = marker; break;
                case 3: Marker3 = marker; break;
                case 4: Marker4 = marker; break;
                case 5: Marker5 = marker; break;
                default: return false;
            }
            MarkerCount++;
            return true;
        }
    }

    /// <summary>План одного уровня макроячейки: плитки 64×64, номер комнаты на плитку и список комнат.</summary>
    public struct MallLevelPlan : IDisposable
    {
        public const int Size = MallLayout.CellSize;
        public const int Area = Size * Size;

        public NativeArray<byte> Tiles;
        public NativeArray<byte> RoomIds;
        public NativeList<MallRoom> Rooms;
        public int Level;

        public MallLevelPlan(Allocator allocator)
        {
            Tiles = new NativeArray<byte>(Area, allocator);
            RoomIds = new NativeArray<byte>(Area, allocator);
            Rooms = new NativeList<MallRoom>(64, allocator);
            Level = -1;
        }

        public bool IsCreated => Tiles.IsCreated;

        public static int Index(int x, int z) => x + z * Size;

        public Tile TileAt(int x, int z) => (Tile)Tiles[Index(x, z)];

        public MallRoom RoomAt(int x, int z) => Rooms[RoomIds[Index(x, z)]];

        public void Dispose()
        {
            if (Tiles.IsCreated) Tiles.Dispose();
            if (RoomIds.IsCreated) RoomIds.Dispose();
            if (Rooms.IsCreated) Rooms.Dispose();
        }
    }
}
