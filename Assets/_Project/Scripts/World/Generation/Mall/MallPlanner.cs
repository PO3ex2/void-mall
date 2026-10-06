using Unity.Collections;
using Unity.Mathematics;
using VoidMall.Core;

namespace VoidMall.World
{
    /// <summary>
    /// Планировка мола (проходы 1–2 генерации): параметры макроячейки и план каждого уровня.
    /// Всё зависит только от сида и координат, поэтому план одинаков в любой джобе, превью и спавне структур.
    /// Код совместим с Burst (только NativeContainer и структуры).
    ///
    /// Квартал описан в «каноническом» виде (ядро у южной стороны) и поворачивается на 0/90/180/270°.
    /// Канонический квартал 50×50: фасады по краям (x или z = 0 и 49), у южного фасада — полоса магазинов
    /// глубиной 11 и ядро (лестница, лифт, щитовая), за стеной z = 12 — северная часть, нарезанная BSP.
    /// В каждой стене-разрезе BSP есть дверь, поэтому все комнаты связаны; комнаты у фасада получают витрину.
    /// </summary>
    public static class MallPlanner
    {
        public const int Block = MallLayout.BlockSize;
        public const int Band = MallLayout.Band;
        public const int CoreWidth = 11;
        public const int CoreDepth = 13;

        // Атриум в галерее: пустота поперёк [4, 10), вдоль [32, 46); две полосы эскалаторов по 3 клетки.
        public const int AtriumAcrossMin = 4;
        public const int AtriumAcrossMax = 10;
        public const int AtriumAlongMin = 32;
        public const int AtriumAlongMax = 46;
        public const int AtriumLaneWidth = 3;

        /// <summary>Линия фасада входа в мол поперёк западной галереи (z ячейки) и ряд турникетов перед ним.</summary>
        public const int EntranceZ = 17;
        public const float EntranceTurnstileZ = 20.5f;

        /// <summary>Доля ячеек со входом в мол в глубине (у старта — всегда).</summary>
        public const float EntranceChance = 0.03f;

        /// <summary>Тупики: стена поперёк отрезка галереи (на уровень и полосу) и служебный коридор между магазинами.</summary>
        public const float DeadEndChance = 0.1f, CorridorChance = 0.3f;

        /// <summary>Балконы вдоль галерей: доля полос (без атриума), пустота вдоль [16, 62), мостик поперёк [37, 41).</summary>
        public const float BalconyChance = 0.35f;
        public const int BalconyAlongMin = 16, BalconyAlongMax = 62, BalconyBridgeMin = 37, BalconyBridgeMax = 41;

        /// <summary>Радиус круглого проёма над фонтаном (пустота), ограждение — следующее кольцо.</summary>
        public const float WellRadius = 4.6f;

        public const int RampLength = 20;
        public const int RampWidth = 4;

        private const int MinRoom = 5;
        private const int SaltCell = 7000;
        private const int SaltSection = 7100;
        private const int SaltRoom = 7200;
        private const int SaltBsp = 100000;

        // ───────────────────────────── Макроячейка ─────────────────────────────

        public static MallCellInfo GetCellInfo(uint seed, float anomalyFrequency, int2 cell)
        {
            var info = new MallCellInfo { Cell = cell, Biome = BiomeOf(seed, anomalyFrequency, cell) };
            CoreOf(seed, cell, out info.Rotation, out info.CoreOffset, out info.CoreRect);
            info.AtriumX = Hash.Get(seed, cell.x, cell.y, SaltCell + 1) % 100 < 45;
            info.AtriumZ = Hash.Get(seed, cell.x, cell.y, SaltCell + 2) % 100 < 45;
            info.BalconyX = !info.AtriumX && Hash.ToFloat01(Hash.Get(seed, cell.x, cell.y, SaltCell + 3)) < BalconyChance;
            info.BalconyZ = !info.AtriumZ && Hash.ToFloat01(Hash.Get(seed, cell.x, cell.y, SaltCell + 4)) < BalconyChance;

            // Пандус P2→P1 в проезде парковки, в стороне от ядра.
            int4 core = Expand(info.CoreRect, 1);
            for (int attempt = 0; attempt < 6 && !info.HasRamp; attempt++)
            {
                uint hr = Hash.Get(seed, cell.x, cell.y, SaltCell + 10 + attempt);
                int z0 = 16 * (int)(hr % 3) + 13;
                int x0 = 2 + (int)((hr >> 4) % 41);
                var rect = new int4(x0, z0 - 1, x0 + RampLength, z0 + RampWidth + 1);
                if (Overlaps(rect, core))
                    continue;
                info.HasRamp = true;
                info.RampMin = new int2(x0, z0);
            }

            int2 spawn = SpawnCell(seed, anomalyFrequency);
            info.NearSpawn = math.all(math.abs(cell - spawn) <= 1);
            info.HasEntrance = cell.Equals(spawn) || Hash.ToFloat01(Hash.Get(seed, cell.x, cell.y, SaltCell + 50)) < EntranceChance;

            PlaceAnomaly(seed, anomalyFrequency, ref info);
            AdoptBigAbyss(seed, anomalyFrequency, ref info);
            PlaceMarkers(seed, anomalyFrequency, ref info);
            return info;
        }

        private static void PlaceAnomaly(uint seed, float frequency, ref MallCellInfo info)
        {
            int2 cell = info.Cell;
            uint h = Hash.Get(seed, cell.x, cell.y, SaltCell + 21);
            uint h2 = Hash.Get(seed, cell.x, cell.y, SaltCell + 22);
            float size = Hash.ToFloat01(h);

            float zone;
            switch (RollAnomaly(seed, frequency, cell))
            {
                case AnomalyKind.Abyss:
                    info.Anomaly = AnomalyKind.Abyss;
                    info.AnomalyLevel = 1 + (int)(h2 % 6);
                    // Размер: чаще небольшие, изредка огромные (шире квартала — только в узлах решётки, см. PlaceBigAbyss).
                    bool node = IsBigAnomalyNode(cell);
                    if (!node)
                        size *= size;
                    if (node && PlaceBigAbyss(seed, frequency, ref info, size))
                        return;
                    info.AnomalyRadius = math.lerp(AbyssRadiusMin, AbyssRadiusBlockMax, size);
                    zone = MallAnomalies.AbyssZoneRadius(info.AnomalyRadius);
                    break;
                case AnomalyKind.Pool:
                    info.Anomaly = AnomalyKind.Pool;
                    info.AnomalyLevel = MallLayout.FirstRetailLevel + (int)(h2 % MallLayout.RetailFloors);
                    size *= size;
                    info.AnomalyRadius = math.lerp(PoolRadiusMin, PoolRadiusMax, size);
                    info.AnomalyDepth = 2.5f + (h >> 12) % 100 / 100f + size;
                    zone = MallAnomalies.PoolZoneRadius(info.AnomalyRadius);
                    break;
                case AnomalyKind.Cutout:
                    info.Anomaly = AnomalyKind.Cutout;
                    int quadrant = (int)(h % 4);
                    info.AnomalyLevel = MallLayout.FirstRetailLevel + (int)(h2 % MallLayout.RetailFloors);
                    info.AnomalyCenter = MallLayout.CellOrigin(cell) + new float2(quadrant & 1, quadrant >> 1) * WorldConstants.ChunkSize;
                    info.AnomalyRadius = WorldConstants.ChunkSize;
                    return;
                default:
                    return;
            }

            // Бездна и котлован — внутри квартала, на стороне, противоположной ядру.
            float margin = zone + 1f;
            float u = math.lerp(margin, Block - margin, Hash.ToFloat01(Hash.Get(seed, cell.x, cell.y, SaltCell + 23)));
            float vMin = CoreDepth + margin, vMax = Block - margin;
            float v = vMax > vMin ? math.lerp(vMin, vMax, Hash.ToFloat01(Hash.Get(seed, cell.x, cell.y, SaltCell + 24))) : (vMin + vMax) * 0.5f;
            info.AnomalyCenter = MallLayout.CellOrigin(cell) + BlockToCell(new float2(u, v), info.Rotation);
        }

        // ───────────────────────────── Биомы ─────────────────────────────

        /// <summary>Шаг решётки центров пятен биомов, ячеек: пятна выходят по 1–6 ячеек, в среднем около четырёх.</summary>
        public const int BiomeSiteSpacing = 2;

        /// <summary>Доли биомов по центрам пятен: пустые — самые частые, поле аномалий — редкое.</summary>
        public const float BiomeEmptyShare = 0.5f, BiomeFieldShare = 0.1f;

        private const int SaltBiome = 7300;

        /// <summary>
        /// Биом ячейки: Вороной по центрам на решётке BiomeSiteSpacing со случайным сдвигом, биом центра — по хешу.
        /// Ячейка старта и её соседи — всегда обычный мол (на старте должно быть что взять).
        /// </summary>
        public static MallBiome BiomeOf(uint seed, float anomalyFrequency, int2 cell)
        {
            int2 spawn = SpawnCell(seed, anomalyFrequency);
            if (math.all(math.abs(cell - spawn) <= 1))
                return MallBiome.Normal;
            int2 block = (int2)math.floor((float2)cell / BiomeSiteSpacing);
            float2 p = (float2)cell + 0.5f;
            float best = float.MaxValue;
            uint winner = 0;
            for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int2 b = block + new int2(dx, dz);
                uint h = Hash.Get(seed, b.x, b.y, SaltBiome);
                float2 site = (float2)(b * BiomeSiteSpacing) + new float2(Hash.ToFloat01(h), Hash.ToFloat01(Hash.Get(h, 1))) * BiomeSiteSpacing;
                float d = math.distancesq(p, site);
                if (d < best)
                {
                    best = d;
                    winner = Hash.Get(h, 2);
                }
            }
            float r = Hash.ToFloat01(winner);
            return r < BiomeFieldShare ? MallBiome.AnomalyField
                : r < BiomeFieldShare + BiomeEmptyShare ? MallBiome.Empty
                : MallBiome.Normal;
        }

        /// <summary>Множитель частоты аномалий и маркеров в биоме.</summary>
        public static float BiomeAnomalyMul(MallBiome biome) => biome switch
        {
            MallBiome.Empty => 0.5f,
            MallBiome.AnomalyField => 3.5f,
            _ => 1f,
        };

        /// <summary>Ячейка старта: первая к востоку от начала координат (y = 0) без выреза. Биом там всегда обычный.</summary>
        public static int2 SpawnCell(uint seed, float anomalyFrequency)
        {
            for (int x = 0; x < 64; x++)
                if (RollAnomalyRaw(seed, anomalyFrequency, new int2(x, 0)) != AnomalyKind.Cutout)
                    return new int2(x, 0);
            return int2.zero;
        }


        // ───────────────────────────── Площади и стенды ─────────────────────────────

        /// <summary>Круглая площадь на пересечении галерей (центр — колонна (7, 7)): доля уровней и доля с фонтаном.</summary>
        public const float RoundPlazaChance = 0.3f, FountainChance = 0.6f;

        /// <summary>Радиусы (по центрам клеток): узор пола, бортик фонтана (внешний), вода, кольцо скамеек.</summary>
        public const float PlazaRadius = 6.4f, FountainRadius = 3.9f, FountainWaterRadius = 3f, PlazaBenchRadius = 5.2f;

        public static readonly float2 PlazaCenter = new(7f, 7f);

        /// <summary>Стенд с машиной в галерее: доля уровней, размер подиума (вдоль, поперёк), высота.</summary>
        public const float CarStandChance = 0.06f;
        public static readonly float2 CarStandSize = new(6f, 3.6f);
        public const float CarStandHeight = 0.3f;

        /// <summary>
        /// Круглая площадь ячейки (на всех торговых этажах). С фонтаном — фонтан на первом торговом этаже (fountain),
        /// а над ним на всех этажах выше круглый проём (<see cref="PlazaWell"/>); колонны на перекрёстке нет.
        /// </summary>
        public static bool RoundPlaza(uint seed, in MallCellInfo info, int level, out bool fountain)
        {
            fountain = false;
            if (MallLayout.Kind(level) != LevelKind.Retail || !PlazaOf(seed, info, out bool hasFountain))
                return false;
            fountain = hasFountain && level == MallLayout.FirstRetailLevel;
            return true;
        }

        /// <summary>Круглая площадь ячейки и есть ли у неё фонтан (без привязки к этажу).</summary>
        public static bool PlazaOf(uint seed, in MallCellInfo info, out bool hasFountain)
        {
            uint h = Hash.Get(seed, info.Cell.x, info.Cell.y, SaltCell + 80);
            hasFountain = Hash.ToFloat01(Hash.Get(h, 1)) < FountainChance;
            return Hash.ToFloat01(h) < RoundPlazaChance;
        }

        /// <summary>Этаж над фонтаном: круглый проём с ограждением вместо площади.</summary>
        public static bool PlazaWell(uint seed, in MallCellInfo info, int level) =>
            MallLayout.RetailFloor(level) > 0 && PlazaOf(seed, info, out bool hasFountain) && hasFountain;

        /// <summary>
        /// Стенд с машиной: подиум посреди отрезка галереи между площадью и атриумом (вдоль 36…42 — там не бывает
        /// ни тупиков, ни атриума другой полосы). Полоса с атриумом не подходит.
        /// </summary>
        public static bool CarStand(uint seed, in MallCellInfo info, int level, out float2 center, out bool alongX)
        {
            center = default;
            alongX = false;
            if (MallLayout.Kind(level) != LevelKind.Retail || info.NearSpawn)
                return false;
            uint h = Hash.Get(seed, info.Cell.x, info.Cell.y, SaltCell + 90 + level);
            if (Hash.ToFloat01(h) >= CarStandChance)
                return false;
            alongX = (h >> 20) % 2 == 0;
            if (alongX ? info.AtriumX || info.BalconyX : info.AtriumZ || info.BalconyZ)
                alongX = !alongX;
            if (alongX ? info.AtriumX || info.BalconyX : info.AtriumZ || info.BalconyZ)
                return false;
            center = alongX ? new float2(39f, 7f) : new float2(7f, 39f);
            return true;
        }
        // ───────────────────────────── Размеры бездн и котлованов ─────────────────────────────

        /// <summary>Радиус чаши котлована: зона (с кромкой) от 13 до 30 м в поперечнике, всегда внутри квартала.</summary>
        public const float PoolRadiusMin = 4f, PoolRadiusMax = 12.5f;

        /// <summary>
        /// Радиус шахты бездны: самый маленький, самый большой внутри квартала и самый большой вообще.
        /// Зона (шахта + кромка) — от ~15 до ~73 м в поперечнике.
        /// </summary>
        public const float AbyssRadiusMin = 3.5f, AbyssRadiusBlockMax = 13f, AbyssRadiusBigMax = 32.5f;

        /// <summary>Шаг решётки узлов, где может родиться большая бездна.</summary>
        public const int BigAnomalySpacing = 3;

        /// <summary>Своя аномалия ячейки — только по броску, без учёта соседей (частота с поправкой на биом ячейки).</summary>
        private static AnomalyKind RollAnomaly(uint seed, float frequency, int2 cell) =>
            RollAnomalyRaw(seed, frequency * BiomeAnomalyMul(BiomeOf(seed, frequency, cell)), cell);

        private static AnomalyKind RollAnomalyRaw(uint seed, float frequency, int2 cell)
        {
            float roll = Hash.ToFloat01(Hash.Get(seed, cell.x, cell.y, SaltCell + 20));
            // В узлах решётки бездна чаще — только там она бывает огромной.
            float pAbyss = (IsBigAnomalyNode(cell) ? 0.25f : 0.1f) * frequency, pPool = 0.08f * frequency, pCutout = 0.05f * frequency;
            return roll < pAbyss ? AnomalyKind.Abyss
                : roll < pAbyss + pPool ? AnomalyKind.Pool
                : roll < pAbyss + pPool + pCutout ? AnomalyKind.Cutout
                : AnomalyKind.None;
        }

        /// <summary>
        /// Узел решётки 3×3 ячеек. Большая бездна (шире квартала) рождается только здесь: в любом окне 3×3 ровно
        /// один узел, поэтому две большие бездны не пересекаются, а соседняя ячейка знает, где искать чужую.
        /// </summary>
        public static bool IsBigAnomalyNode(int2 cell) => Mod(cell.x, BigAnomalySpacing) == 0 && Mod(cell.y, BigAnomalySpacing) == 0;

        private static int Mod(int a, int m) => (a % m + m) % m;

        /// <summary>
        /// Большая бездна: центр в своей ячейке (квартал или галерея), провал может выходить в соседние кварталы.
        /// Радиус урезается так, чтобы зона не задела ядра (лестницы, лифты) этой и соседних ячеек и аномалии
        /// соседей; из нескольких точек-кандидатов берётся самая просторная. Ложь — бездна умещается в квартале,
        /// хватит обычной расстановки.
        /// </summary>
        private static bool PlaceBigAbyss(uint seed, float frequency, ref MallCellInfo info, float size)
        {
            float wanted = math.lerp(AbyssRadiusMin, AbyssRadiusBigMax, size);
            if (wanted <= AbyssRadiusBlockMax)
                return false;
            int2 cell = info.Cell;
            float2 cellOrigin = MallLayout.CellOrigin(cell);
            float best = 0f;
            float2 bestCenter = default;
            for (int attempt = 0; attempt < 12; attempt++)
            {
                // Кандидаты по всей ячейке, включая галерею: на перекрёстке галерей бездне просторнее.
                uint hc = Hash.Get(seed, cell.x, cell.y, SaltCell + 50 + attempt);
                float2 center = cellOrigin + new float2(Hash.ToFloat01(hc), Hash.ToFloat01(Hash.Get(hc, 1))) * MallLayout.CellSize;
                float room = FreeRadius(seed, frequency, cell, center);
                if (room > best)
                {
                    best = room;
                    bestCenter = center;
                }
            }
            // Зона = шахта + кромка, от препятствий ещё 2 м.
            float radius = math.min(wanted, best - (MallAnomalies.AbyssZoneRadius(0f) + 2f));
            if (radius <= AbyssRadiusBlockMax)
                return false;
            info.AnomalyRadius = radius;
            info.AnomalyCenter = bestCenter;
            return true;
        }

        /// <summary>Свободное место вокруг точки: до ближайшего ядра в окне 3×3 или зоны аномалии соседней ячейки.</summary>
        private static float FreeRadius(uint seed, float frequency, int2 cell, float2 center)
        {
            float free = float.MaxValue;
            for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int2 other = cell + new int2(dx, dz);
                float2 origin = MallLayout.CellOrigin(other);
                if ((dx != 0 || dz != 0) && RollAnomaly(seed, frequency, other) != AnomalyKind.None)
                    free = math.min(free, DistanceToAnomaly(seed, frequency, other, center));
                CoreOf(seed, other, out _, out _, out int4 core);
                free = math.min(free, DistanceToRect(center, new float4(origin + (float2)core.xy, origin + (float2)core.zw)));
            }
            return free;
        }

        /// <summary>Расстояние от точки до зоны своей аномалии соседней ячейки (соседи узла сами не узлы — без рекурсии).</summary>
        private static float DistanceToAnomaly(uint seed, float frequency, int2 cell, float2 p)
        {
            var other = new MallCellInfo { Cell = cell };
            CoreOf(seed, cell, out other.Rotation, out other.CoreOffset, out other.CoreRect);
            PlaceAnomaly(seed, frequency, ref other);
            return other.Anomaly switch
            {
                AnomalyKind.Abyss => math.distance(p, other.AnomalyCenter) - MallAnomalies.AbyssZoneRadius(other.AnomalyRadius),
                AnomalyKind.Pool => math.distance(p, other.AnomalyCenter) - MallAnomalies.PoolZoneRadius(other.AnomalyRadius),
                AnomalyKind.Cutout => DistanceToRect(p, new float4(other.AnomalyCenter, other.AnomalyCenter + WorldConstants.ChunkSize)),
                _ => float.MaxValue,
            };
        }

        private static float DistanceToRect(float2 p, float4 rect) => math.length(math.max(math.max(rect.xy - p, p - rect.zw), 0f));

        /// <summary>
        /// Ячейка, до которой дотягивается большая бездна из узла её окна 3×3, получает её параметры в слот
        /// BigAbyss (своя аномалия остаётся): генерация, пропы и структуры обрабатывают её как свою.
        /// </summary>
        private static void AdoptBigAbyss(uint seed, float frequency, ref MallCellInfo info)
        {
            int2 cell = info.Cell;
            int2 node = new(cell.x - Mod(cell.x + 1, BigAnomalySpacing) + 1, cell.y - Mod(cell.y + 1, BigAnomalySpacing) + 1);
            if (node.Equals(cell) || RollAnomaly(seed, frequency, node) != AnomalyKind.Abyss)
                return;
            var host = new MallCellInfo { Cell = node };
            CoreOf(seed, node, out host.Rotation, out host.CoreOffset, out host.CoreRect);
            PlaceAnomaly(seed, frequency, ref host);
            if (host.Anomaly != AnomalyKind.Abyss || host.AnomalyRadius <= AbyssRadiusBlockMax)
                return;
            float2 origin = MallLayout.CellOrigin(cell);
            // С запасом: лампы и пропы проверяют чуть шире зоны.
            float reach = MallAnomalies.AbyssZoneRadius(host.AnomalyRadius) + 4f;
            if (DistanceToRect(host.AnomalyCenter, new float4(origin, origin + MallLayout.CellSize)) >= reach)
                return;
            info.HasBigAbyss = true;
            info.BigAbyssLevel = host.AnomalyLevel;
            info.BigAbyssCenter = host.AnomalyCenter;
            info.BigAbyssRadius = host.AnomalyRadius;
        }

        /// <summary>Поворот квартала, смещение ядра и его прямоугольник (координаты ячейки).</summary>
        private static void CoreOf(uint seed, int2 cell, out int rotation, out int coreOffset, out int4 coreRect)
        {
            uint h = Hash.Get(seed, cell.x, cell.y, SaltCell);
            rotation = (int)(h & 3);
            coreOffset = 8 + (int)((h >> 2) % 24);
            coreRect = RotateRect(new int4(coreOffset, 0, coreOffset + CoreWidth, CoreDepth), rotation);
        }

        private static void PlaceMarkers(uint seed, float frequency, ref MallCellInfo info)
        {
            for (int kind = 1; kind <= 2; kind++)
            {
                int2 cell = info.Cell;
                if (Hash.ToFloat01(Hash.Get(seed, cell.x, cell.y, SaltCell + 30 + kind)) >= 0.12f * frequency * BiomeAnomalyMul(info.Biome))
                    continue;
                uint h = Hash.Get(seed, cell.x, cell.y, SaltCell + 40 + kind);
                int level = (int)(h % MallLayout.LevelCount);
                var marker = new AnomalyMarker
                {
                    Kind = (AnomalyMarkerKind)kind,
                    Center = new float3(
                        cell.x * MallLayout.CellSize + 8 + (h >> 4) % 48,
                        MallLayout.FloorTop(level) + MallLayout.FreeHeight(level) * 0.5f,
                        cell.y * MallLayout.CellSize + 8 + (h >> 12) % 48),
                    Radius = 5f + (h >> 20) % 6,
                };
                if (info.MarkerCount == 0)
                    info.Marker0 = marker;
                else
                    info.Marker1 = marker;
                info.MarkerCount++;
            }
        }

        /// <summary>
        /// Точка появления: западная галерея первого этажа (проход вдоль фасада, вне пустоты атриума)
        /// в ближайшей к началу координат ячейке без выреза.
        /// </summary>
        public static float3 FindSpawn(uint seed, float anomalyFrequency)
        {
            float2 origin = MallLayout.CellOrigin(SpawnCell(seed, anomalyFrequency));
            return new float3(origin.x + 2.5f, MallLayout.FloorTop(MallLayout.FirstRetailLevel), origin.y + 24.5f);
        }

        // ───────────────────────────── План уровня ─────────────────────────────

        public static void BuildLevel(uint seed, in MallCellInfo info, int level, NativeArray<SectionInfo> sections, ref MallLevelPlan plan)
        {
            plan.Level = level;
            plan.Rooms.Clear();
            plan.Rooms.Add(new MallRoom { Rect = new int4(0, 0, MallLayout.CellSize, MallLayout.CellSize) });
            for (int i = 0; i < MallLevelPlan.Area; i++)
            {
                plan.Tiles[i] = (byte)Tile.Open;
                plan.RoomIds[i] = 0;
            }

            var tiles = new NativeArray<byte>(Block * Block, Allocator.Temp);
            var rooms = new NativeArray<byte>(Block * Block, Allocator.Temp);
            LevelKind kind = MallLayout.Kind(level);

            if (kind == LevelKind.Parking)
            {
                BuildParking(info, level, ref plan);
                for (int i = 0; i < tiles.Length; i++)
                    tiles[i] = (byte)Tile.Open;
                StampCore(info.CoreOffset, ref plan, tiles, rooms);
                CopyBlock(info, tiles, rooms, ref plan, coreOnly: true);
            }
            else
            {
                BuildBlock(seed, info, level, kind, ref plan, tiles, rooms);
                CopyBlock(info, tiles, rooms, ref plan, coreOnly: false);
                BuildGalleries(seed, info, level, ref plan);
            }
            AssignSections(seed, info, level, sections, ref plan);

            tiles.Dispose();
            rooms.Dispose();
        }

        private static void BuildBlock(uint seed, in MallCellInfo info, int level, LevelKind kind, ref MallLevelPlan plan,
            NativeArray<byte> tiles, NativeArray<byte> rooms)
        {
            for (int i = 0; i < tiles.Length; i++)
            {
                tiles[i] = (byte)Tile.Wall;
                rooms[i] = 0;
            }
            for (int i = 0; i < Block; i++)
            {
                tiles[B(i, 0)] = (byte)Tile.FacadeWall;
                tiles[B(i, Block - 1)] = (byte)Tile.FacadeWall;
                tiles[B(0, i)] = (byte)Tile.FacadeWall;
                tiles[B(Block - 1, i)] = (byte)Tile.FacadeWall;
            }

            int c = info.CoreOffset;
            var leaves = new NativeList<int4>(32, Allocator.Temp);
            var splits = new NativeList<int4>(32, Allocator.Temp);
            int salt = SaltBsp + level * 4096;
            uint style = Hash.Get(seed, info.Cell.x, info.Cell.y, salt);

            // Полосы магазинов у южного фасада (только поперечные разрезы — каждый магазин с витриной).
            Partition(seed, info.Cell, new int4(1, 1, c, 12), stripOnly: true, maxSide: 14, salt + 1000, leaves, splits);
            Partition(seed, info.Cell, new int4(c + CoreWidth, 1, Block - 1, 12), stripOnly: true, maxSide: 14, salt + 2000, leaves, splits);
            // Северная часть: иногда — один огромный зал (якорный магазин).
            bool anchor = kind == LevelKind.Retail && style % 100 < 18;
            Partition(seed, info.Cell, new int4(1, 13, Block - 1, Block - 1), stripOnly: false, maxSide: anchor ? 999 : 20, salt + 3000, leaves, splits);

            // Служебный коридор-тупик (GDD §2.9): от самого широкого магазина южной полосы отрезаем 2 клетки и стену.
            int corridor = -1;
            if (kind == LevelKind.Retail && Hash.ToFloat01(Hash.Get(seed, info.Cell.x, info.Cell.y, salt + 6000)) < CorridorChance)
            {
                int widest = -1;
                for (int i = 0; i < leaves.Length; i++)
                {
                    int4 r = leaves[i];
                    if (r.y == 1 && r.w == 12 && r.z - r.x >= 9 && (widest < 0 || r.z - r.x > leaves[widest].z - leaves[widest].x))
                        widest = i;
                }
                if (widest >= 0)
                {
                    int4 r = leaves[widest];
                    leaves[widest] = new int4(r.x, r.y, r.z - 3, r.w);
                    leaves.Add(new int4(r.z - 2, r.y, r.z, r.w));
                    corridor = leaves.Length - 1;
                }
            }

            for (int i = 0; i < leaves.Length; i++)
            {
                int4 r = leaves[i];
                byte id = (byte)plan.Rooms.Length;
                plan.Rooms.Add(new MallRoom { Rect = r });
                for (int z = r.y; z < r.w; z++)
                for (int x = r.x; x < r.z; x++)
                {
                    tiles[B(x, z)] = (byte)Tile.Open;
                    rooms[B(x, z)] = id;
                }
            }

            StampCore(c, ref plan, tiles, rooms);

            // Двери в стенах-разрезах BSP.
            for (int i = 0; i < splits.Length; i++)
                PlaceDoor(seed, info.Cell, salt + 5000 + i, splits[i], tiles, rooms);
            // Коридор — тупик: дверь разреза, попавшая на его боковую стену, снова становится стеной.
            if (corridor >= 0)
            {
                int4 cr = leaves[corridor];
                for (int z = cr.y; z < cr.w; z++)
                {
                    if (tiles[B(cr.x - 1, z)] == (byte)Tile.Door)
                        tiles[B(cr.x - 1, z)] = (byte)Tile.Wall;
                    if (cr.z < Block && tiles[B(cr.z, z)] == (byte)Tile.Door)
                        tiles[B(cr.z, z)] = (byte)Tile.Wall;
                }
            }

            // Комнаты, к которым прилегают стены и фасады (для материалов).
            for (int z = 0; z < Block; z++)
            for (int x = 0; x < Block; x++)
            {
                int index = B(x, z);
                if (rooms[index] != 0)
                    continue;
                Tile t = (Tile)tiles[index];
                if (t == Tile.FacadeWall)
                    rooms[index] = rooms[B(math.clamp(x, 1, Block - 2), math.clamp(z, 1, Block - 2))];
                else if (t == Tile.Wall)
                    rooms[index] = (byte)NeighborRoom(x, z, tiles, rooms);
            }

            // Витрины: у каждой комнаты, выходящей на фасад. Листья — комнаты 1…N (ядро добавлено после них).
            for (int i = 0; i < leaves.Length; i++)
            {
                int roomIndex = 1 + i;
                var room = plan.Rooms[roomIndex];
                if (i == corridor)
                {
                    // Вход в коридор — проём (иногда с дверью) прямо в фасаде, витрины нет.
                    for (int x = room.Rect.x; x < room.Rect.z; x++)
                        tiles[B(x, 0)] = (byte)Tile.Door;
                    room.Flags |= RoomFlags.Service | RoomFlags.Corridor;
                    plan.Rooms[roomIndex] = room;
                    continue;
                }
                uint hr = Hash.Get(seed, info.Cell.x, info.Cell.y, SaltRoom + level * 256 + roomIndex);
                // Закрытыми (рольставни) бывают только небольшие магазины.
                int area = (room.Rect.z - room.Rect.x) * (room.Rect.w - room.Rect.y);
                bool closed = kind == LevelKind.Retail && hr % 100 < 10 && area <= 200;
                bool front = false;
                int4 r = room.Rect;
                if (r.x == 1) front |= ApplyFront(new int2(0, r.y), new int2(0, 1), r.w - r.y, kind, closed, hr, tiles);
                if (r.z == Block - 1) front |= ApplyFront(new int2(Block - 1, r.y), new int2(0, 1), r.w - r.y, kind, closed, hr >> 3, tiles);
                if (r.y == 1) front |= ApplyFront(new int2(r.x, 0), new int2(1, 0), r.z - r.x, kind, closed, hr >> 6, tiles);
                if (r.w == Block - 1) front |= ApplyFront(new int2(r.x, Block - 1), new int2(1, 0), r.z - r.x, kind, closed, hr >> 9, tiles);

                if (front && kind == LevelKind.Retail)
                    room.Flags |= RoomFlags.Storefront;
                else
                    room.Flags |= RoomFlags.Service;
                if (closed && front)
                    room.Flags |= RoomFlags.Closed;
                plan.Rooms[roomIndex] = room;
            }

            leaves.Dispose();
            splits.Dispose();
        }

        /// <summary>BSP-нарезка прямоугольника на комнаты. Стена разреза — линия клеток между половинами.</summary>
        private static void Partition(uint seed, int2 cell, int4 root, bool stripOnly, int maxSide, int salt,
            NativeList<int4> leaves, NativeList<int4> splits)
        {
            if (root.z - root.x < MinRoom || root.w - root.y < 3)
                return;
            var stack = new NativeList<int4>(16, Allocator.Temp);
            stack.Add(root);
            int counter = 0;
            while (stack.Length > 0)
            {
                int4 r = stack[stack.Length - 1];
                stack.RemoveAt(stack.Length - 1);
                uint h = Hash.Get(seed, cell.x, cell.y, salt + counter++);
                int w = r.z - r.x, d = r.w - r.y;
                bool canX = w >= MinRoom * 2 + 1;
                bool canZ = !stripOnly && d >= MinRoom * 2 + 1;
                bool must = (w > maxSide && canX) || (d > maxSide && canZ);
                bool stop = !must && (w * d <= 120 || h % 3 == 0 || maxSide >= 999);
                if ((!canX && !canZ) || stop)
                {
                    leaves.Add(r);
                    continue;
                }

                bool splitX;
                if (!canZ) splitX = true;
                else if (!canX) splitX = false;
                else if (w > d * 5 / 4) splitX = true;
                else if (d > w * 5 / 4) splitX = false;
                else splitX = (h & 0x100) != 0;

                if (splitX)
                {
                    int lo = r.x + MinRoom, hi = r.z - MinRoom - 1;
                    int s = lo + (int)((h >> 9) % (uint)(hi - lo + 1));
                    splits.Add(new int4(0, s, r.y, r.w));
                    stack.Add(new int4(r.x, r.y, s, r.w));
                    stack.Add(new int4(s + 1, r.y, r.z, r.w));
                }
                else
                {
                    int lo = r.y + MinRoom, hi = r.w - MinRoom - 1;
                    int s = lo + (int)((h >> 9) % (uint)(hi - lo + 1));
                    splits.Add(new int4(1, s, r.x, r.z));
                    stack.Add(new int4(r.x, r.y, r.z, s));
                    stack.Add(new int4(r.x, s + 1, r.z, r.w));
                }
            }
            stack.Dispose();
        }

        /// <summary>Дверь шириной 2 в стене разреза: с обеих сторон должен быть пол (не стык с другой стеной).</summary>
        private static void PlaceDoor(uint seed, int2 cell, int salt, int4 split, NativeArray<byte> tiles, NativeArray<byte> rooms)
        {
            int axis = split.x, pos = split.y, a = split.z, b = split.w;
            int count = b - a - 1;
            if (count <= 0)
                return;
            int start = (int)(Hash.Get(seed, cell.x, cell.y, salt) % (uint)count);
            for (int i = 0; i < count; i++)
            {
                int p = a + (start + i) % count;
                int2 wall0 = axis == 0 ? new int2(pos, p) : new int2(p, pos);
                int2 step = axis == 0 ? new int2(0, 1) : new int2(1, 0);
                int2 normal = axis == 0 ? new int2(1, 0) : new int2(0, 1);
                int2 wall1 = wall0 + step;
                if (!IsTile(tiles, wall0, Tile.Wall) || !IsTile(tiles, wall1, Tile.Wall)
                    || !IsTile(tiles, wall0 - normal, Tile.Open) || !IsTile(tiles, wall0 + normal, Tile.Open)
                    || !IsTile(tiles, wall1 - normal, Tile.Open) || !IsTile(tiles, wall1 + normal, Tile.Open))
                    continue;
                tiles[B(wall0.x, wall0.y)] = (byte)Tile.Door;
                tiles[B(wall1.x, wall1.y)] = (byte)Tile.Door;
                int2 side = wall0 - normal;
                rooms[B(wall0.x, wall0.y)] = rooms[B(side.x, side.y)];
                rooms[B(wall1.x, wall1.y)] = rooms[B(side.x, side.y)];
                return;
            }
        }

        /// <summary>Витрина вдоль участка фасада: пилястры по краям, вход (2 или 4 клетки), остальное — стекло.</summary>
        private static bool ApplyFront(int2 start, int2 step, int length, LevelKind kind, bool closed, uint h, NativeArray<byte> tiles)
        {
            if (length < 4)
                return false;
            int doorWidth = length >= 14 && kind == LevelKind.Retail ? 4 : 2;
            int doorStart = (length - doorWidth) / 2 + (int)(h % 3) - 1;
            doorStart = math.clamp(doorStart, 1, length - 1 - doorWidth);
            for (int i = 0; i < length; i++)
            {
                int2 p = start + step * i;
                Tile t;
                if (i == 0 || i == length - 1)
                    t = Tile.FacadeWall;
                else if (closed)
                    t = Tile.Shutter;
                else if (i >= doorStart && i < doorStart + doorWidth)
                    t = Tile.FacadeDoor;
                else
                    t = kind == LevelKind.Retail ? Tile.FacadeGlass : Tile.FacadeWall;
                tiles[B(p.x, p.y)] = (byte)t;
            }
            return true;
        }

        /// <summary>
        /// Ядро квартала (одинаково на всех уровнях): лестничная клетка с двумя маршами, лифтовая шахта, щитовая.
        /// Канонически: x ∈ [c, c+11), z ∈ [0, 13), вход с южного фасада.
        /// </summary>
        private static void StampCore(int c, ref MallLevelPlan plan, NativeArray<byte> tiles, NativeArray<byte> rooms)
        {
            byte stair = (byte)plan.Rooms.Length;
            plan.Rooms.Add(new MallRoom { Rect = new int4(c + 1, 1, c + 5, 12), Flags = RoomFlags.Service | RoomFlags.Stairwell });
            byte tech = (byte)plan.Rooms.Length;
            plan.Rooms.Add(new MallRoom { Rect = new int4(c + 6, 5, c + 10, 12), Flags = RoomFlags.Service });

            for (int z = 0; z < CoreDepth; z++)
            for (int x = c; x < c + CoreWidth; x++)
            {
                tiles[B(x, z)] = (byte)Tile.CoreWall;
                rooms[B(x, z)] = stair;
            }
            // Лестница: площадка у входа, марши A (вверх) и B (вниз), промежуточная площадка у дальней стены.
            for (int z = 1; z < 12; z++)
            for (int x = c + 1; x < c + 5; x++)
            {
                Tile t = z < 4 ? Tile.Open : z < 10 ? (x < c + 3 ? Tile.StairLaneA : Tile.StairLaneB) : Tile.StairMid;
                tiles[B(x, z)] = (byte)t;
            }
            tiles[B(c + 2, 0)] = (byte)Tile.CoreDoor;
            tiles[B(c + 3, 0)] = (byte)Tile.CoreDoor;
            // Лифт.
            for (int z = 1; z < 4; z++)
            for (int x = c + 6; x < c + 9; x++)
                tiles[B(x, z)] = (byte)Tile.ElevatorVoid;
            for (int x = c + 6; x < c + 9; x++)
                tiles[B(x, 0)] = (byte)Tile.CoreDoor;
            // Щитовая. Вход — служебная дверь из соседнего помещения за ядром (или сбоку), а не с лестницы:
            // со стороны лестницы за стеной идёт марш, и дверь выходила бы в пустоту над ступенями.
            for (int z = 5; z < 12; z++)
            for (int x = c + 6; x < c + 10; x++)
            {
                tiles[B(x, z)] = (byte)Tile.Open;
                rooms[B(x, z)] = tech;
            }
            for (int x = c + 6; x <= c + 8; x++)
            {
                if (IsTile(tiles, new int2(x, CoreDepth), Tile.Open) && IsTile(tiles, new int2(x + 1, CoreDepth), Tile.Open))
                {
                    tiles[B(x, CoreDepth - 1)] = (byte)Tile.CoreDoor;
                    tiles[B(x + 1, CoreDepth - 1)] = (byte)Tile.CoreDoor;
                    return;
                }
            }
            for (int z = 5; z <= 10; z++)
            {
                if (IsTile(tiles, new int2(c + CoreWidth, z), Tile.Open) && IsTile(tiles, new int2(c + CoreWidth, z + 1), Tile.Open))
                {
                    tiles[B(c + CoreWidth - 1, z)] = (byte)Tile.CoreDoor;
                    tiles[B(c + CoreWidth - 1, z + 1)] = (byte)Tile.CoreDoor;
                    return;
                }
            }
        }

        private static void CopyBlock(in MallCellInfo info, NativeArray<byte> tiles, NativeArray<byte> rooms, ref MallLevelPlan plan, bool coreOnly)
        {
            int c = info.CoreOffset;
            for (int z = 0; z < Block; z++)
            for (int x = 0; x < Block; x++)
            {
                if (coreOnly && (x < c || x >= c + CoreWidth || z >= CoreDepth))
                    continue;
                int2 p = RotateCell(x, z, info.Rotation) + Band;
                int index = MallLevelPlan.Index(p.x, p.y);
                plan.Tiles[index] = tiles[B(x, z)];
                plan.RoomIds[index] = rooms[B(x, z)];
            }
            for (int i = 1; i < plan.Rooms.Length; i++)
            {
                var room = plan.Rooms[i];
                room.Rect = RotateRect(room.Rect, info.Rotation);
                plan.Rooms[i] = room;
            }
        }

        private static void BuildGalleries(uint seed, in MallCellInfo info, int level, ref MallLevelPlan plan)
        {
            // Колонна на перекрёстке; у площади с фонтаном её нет на всех торговых этажах (фонтан внизу, проём над ним).
            bool fountain = MallLayout.Kind(level) == LevelKind.Retail && PlazaOf(seed, info, out bool hasFountain) && hasFountain;
            for (int z = 6; z <= 7; z++)
            for (int x = 6; x <= 7; x++)
                plan.Tiles[MallLevelPlan.Index(x, z)] = (byte)(fountain ? Tile.Open : Tile.Pillar);

            BuildDeadEnds(seed, info, level, ref plan);
            if (info.HasEntrance && level == MallLayout.FirstRetailLevel)
                BuildEntrance(ref plan);

            int floor = MallLayout.RetailFloor(level);
            if (floor < 0)
                return;
            if (info.AtriumX)
                BuildAtrium(ref plan, floor, alongX: true);
            if (info.AtriumZ)
                BuildAtrium(ref plan, floor, alongX: false);
            if (info.BalconyX)
                BuildBalcony(ref plan, floor, alongX: true);
            if (info.BalconyZ)
                BuildBalcony(ref plan, floor, alongX: false);
            if (PlazaWell(seed, info, level))
                BuildWell(ref plan);
        }

        /// <summary>
        /// Балкон-проём вдоль полосы галереи (GDD §2.9): пустота поперёк [4, 10), вдоль [BalconyAlongMin, BalconyAlongMax)
        /// с мостиком посередине, стеклянное ограждение по краю. Как атриум, но без эскалаторов и длиннее.
        /// На первом торговом этаже — только плитки пустоты (там пол, потолок открыт вверх), выше — пустота с ограждением.
        /// </summary>
        private static void BuildBalcony(ref MallLevelPlan plan, int floor, bool alongX)
        {
            for (int along = BalconyAlongMin - 1; along <= BalconyAlongMax; along++)
            for (int across = AtriumAcrossMin - 1; across <= AtriumAcrossMax; across++)
            {
                bool bridge = along >= BalconyBridgeMin && along < BalconyBridgeMax;
                bool inVoid = !bridge && along >= BalconyAlongMin && along < BalconyAlongMax
                    && across >= AtriumAcrossMin && across < AtriumAcrossMax;
                int index = alongX ? MallLevelPlan.Index(along, across) : MallLevelPlan.Index(across, along);
                if (plan.Tiles[index] != (byte)Tile.Open)
                    continue;
                if (inVoid)
                    plan.Tiles[index] = (byte)Tile.AtriumVoid;
                else if (floor > 0 && IsNextToVoid(along, across, bridge))
                    plan.Tiles[index] = (byte)Tile.Railing;
            }
        }

        /// <summary>Клетка рамки балкона, соседняя с пустотой (по краю проёма и у краёв мостика).</summary>
        private static bool IsNextToVoid(int along, int across, bool bridge)
        {
            bool alongInside = along >= BalconyAlongMin && along < BalconyAlongMax;
            bool acrossInside = across >= AtriumAcrossMin && across < AtriumAcrossMax;
            if (bridge)
                return acrossInside && (along == BalconyBridgeMin || along == BalconyBridgeMax - 1);
            return (alongInside && (across == AtriumAcrossMin - 1 || across == AtriumAcrossMax))
                || (acrossInside && (along == BalconyAlongMin - 1 || along == BalconyAlongMax));
        }

        /// <summary>Круглый проём над фонтаном на этажах выше первого: пустота и кольцо ограждения.</summary>
        private static void BuildWell(ref MallLevelPlan plan)
        {
            for (int z = 0; z < Band; z++)
            for (int x = 0; x < Band; x++)
                if (math.distance(new float2(x + 0.5f, z + 0.5f), new float2(7f, 7f)) < WellRadius)
                    plan.Tiles[MallLevelPlan.Index(x, z)] = (byte)Tile.AtriumVoid;
            // Ограждение — на каждой клетке пола, касающейся пустоты (и по диагонали): по радиусу на осях оставалась щель.
            for (int z = 0; z < Band; z++)
            for (int x = 0; x < Band; x++)
            {
                int index = MallLevelPlan.Index(x, z);
                if (plan.Tiles[index] != (byte)Tile.Open)
                    continue;
                bool touches = false;
                for (int dz = -1; dz <= 1 && !touches; dz++)
                for (int dx = -1; dx <= 1 && !touches; dx++)
                {
                    int nx = x + dx, nz = z + dz;
                    touches = nx >= 0 && nz >= 0 && nx < Band && nz < Band && plan.Tiles[MallLevelPlan.Index(nx, nz)] == (byte)Tile.AtriumVoid;
                }
                if (touches)
                    plan.Tiles[index] = (byte)Tile.Railing;
            }
        }

        /// <summary>
        /// Вход в мол: поперёк западной галереи — фасад со стеклом и двумя автодверями 4×3 (в проёме галереи двери
        /// есть всегда), перед ним ряд турникетов и касс (PropPlacer). За дверями — такой же мол.
        /// </summary>
        private static void BuildEntrance(ref MallLevelPlan plan)
        {
            for (int x = 0; x < Band; x++)
            {
                Tile t = x == 0 || x == Band - 1 ? Tile.FacadeWall
                    : (x >= 2 && x < 6) || (x >= 8 && x < 12) ? Tile.FacadeDoor
                    : Tile.FacadeGlass;
                plan.Tiles[MallLevelPlan.Index(x, EntranceZ)] = (byte)t;
            }
        }

        /// <summary>
        /// Тупик в галерее: поперёк отрезка между площадями — рольставни (за ними галерея, их можно поддеть ломом), витрина со
        /// входом в магазин, занявший кусок галереи до площади или края ячейки (сзади глухой фасад), или такой же магазин за рольставнями.
        /// Не больше одного на полосу и уровень, поэтому обе половины отрезка остаются связаны со своими площадями.
        /// Не у старта, не поперёк атриума и не у входа в мол.
        /// </summary>
        private static void BuildDeadEnds(uint seed, in MallCellInfo info, int level, ref MallLevelPlan plan)
        {
            if (info.NearSpawn)
                return;
            for (int axis = 0; axis < 2; axis++)
            {
                uint h = Hash.Get(seed, info.Cell.x, info.Cell.y, SaltCell + 60 + level * 2 + axis);
                if (Hash.ToFloat01(h) >= DeadEndChance || (axis == 0 ? info.BalconyX : info.BalconyZ))
                    continue;
                // Вне площади (0…13) и атриума (31…46): 18…28 или 50…60.
                int along = (h >> 8) % 2 == 0 ? 18 + (int)((h >> 9) % 11) : 50 + (int)((h >> 9) % 11);
                if (axis == 1 && info.HasEntrance && level == MallLayout.FirstRetailLevel && along < 30)
                    continue;
                // 0 — рольставни поперёк галереи (за ними она продолжается), 1 — витрина со входом в магазин,
                // занявший кусок галереи до площади или до края ячейки, 2 — такой же магазин за рольставнями.
                int variant = (int)((h >> 16) % 3);
                byte room = 0;
                if (variant != 0)
                {
                    // Магазин — между фронтом и глухим фасадом у площади (вдоль 14) или у края ячейки (вдоль 63).
                    bool towardPlaza = along < 30;
                    int from = towardPlaza ? 15 : along + 1, to = towardPlaza ? along : 63;
                    int back = towardPlaza ? 14 : 63;
                    room = (byte)plan.Rooms.Length;
                    int4 rect = axis == 0 ? new int4(from, 0, to, Band) : new int4(0, from, Band, to);
                    plan.Rooms.Add(new MallRoom { Rect = rect, Flags = RoomFlags.Storefront | (variant == 2 ? RoomFlags.Closed : RoomFlags.None) });
                    for (int a = from; a < to; a++)
                    for (int across = 0; across < Band; across++)
                    {
                        int index = axis == 0 ? MallLevelPlan.Index(a, across) : MallLevelPlan.Index(across, a);
                        plan.Tiles[index] = (byte)Tile.Open;
                        plan.RoomIds[index] = room;
                    }
                    for (int across = 0; across < Band; across++)
                    {
                        int index = axis == 0 ? MallLevelPlan.Index(back, across) : MallLevelPlan.Index(across, back);
                        plan.Tiles[index] = (byte)Tile.FacadeWall;
                        plan.RoomIds[index] = room;
                    }
                }
                for (int across = 0; across < Band; across++)
                {
                    Tile t = across == 0 || across == Band - 1 ? Tile.FacadeWall
                        : variant != 1 ? Tile.Shutter
                        : across >= 5 && across < 9 ? Tile.FacadeDoor
                        : Tile.FacadeGlass;
                    int index = axis == 0 ? MallLevelPlan.Index(along, across) : MallLevelPlan.Index(across, along);
                    plan.Tiles[index] = (byte)t;
                    if (room != 0)
                        plan.RoomIds[index] = room;
                }
            }
        }

        /// <summary>
        /// Атриум: пустота через этажи 2–5, эскалаторы «ножницами» в двух полосах. Эскалатор k (этаж k → k+1)
        /// идёт в полосе k % 2: чётные — от начала пустоты к концу, нечётные — обратно. Ограждение открыто только
        /// там, где на этом этаже начинается или заканчивается эскалатор.
        /// </summary>
        private static void BuildAtrium(ref MallLevelPlan plan, int floor, bool alongX)
        {
            for (int along = AtriumAlongMin - 1; along <= AtriumAlongMax; along++)
            for (int across = AtriumAcrossMin - 1; across <= AtriumAcrossMax; across++)
            {
                bool inVoid = along >= AtriumAlongMin && along < AtriumAlongMax && across >= AtriumAcrossMin && across < AtriumAcrossMax;
                Tile t;
                if (inVoid)
                    t = Tile.AtriumVoid;
                else if (floor == 0)
                    continue;
                else if (across >= AtriumAcrossMin && across < AtriumAcrossMax && IsEscalatorLanding(floor, along == AtriumAlongMax, (across - AtriumAcrossMin) / AtriumLaneWidth))
                    continue;
                else
                    t = Tile.Railing;
                int index = alongX ? MallLevelPlan.Index(along, across) : MallLevelPlan.Index(across, along);
                plan.Tiles[index] = (byte)t;
            }
        }

        /// <summary>Начинается или заканчивается на этом этаже эскалатор в полосе lane у конца (atEnd) пустоты?</summary>
        public static bool IsEscalatorLanding(int floor, bool atEnd, int lane)
        {
            // Эскалатор k: низ на этаже k, верх на этаже k+1. Чётные k: низ в начале, верх в конце.
            int up = floor;          // эскалатор, который начинается здесь
            int down = floor - 1;    // эскалатор, который заканчивается здесь
            if (up <= MallLayout.RetailFloors - 2 && up % 2 == lane && (up % 2 == 0 ? !atEnd : atEnd))
                return true;
            if (down >= 0 && down % 2 == lane && (down % 2 == 0 ? atEnd : !atEnd))
                return true;
            return false;
        }

        private static void BuildParking(in MallCellInfo info, int level, ref MallLevelPlan plan)
        {
            for (int z = 0; z < MallLayout.CellSize; z++)
            for (int x = 0; x < MallLayout.CellSize; x++)
            {
                int mx = x & 15, mz = z & 15;
                if ((mx == 6 || mx == 7) && (mz == 6 || mz == 7))
                    plan.Tiles[MallLevelPlan.Index(x, z)] = (byte)Tile.Pillar;
            }

            if (!info.HasRamp)
                return;
            int2 r = info.RampMin;
            for (int z = r.y - 1; z <= r.y + RampWidth; z++)
            for (int x = r.x + 2; x < r.x + RampLength; x++)
            {
                bool hole = x >= r.x + 3 && z >= r.y && z < r.y + RampWidth;
                if (hole)
                    plan.Tiles[MallLevelPlan.Index(x, z)] = (byte)Tile.RampHole;
                else if (level == 1)
                    plan.Tiles[MallLevelPlan.Index(x, z)] = (byte)Tile.Barrier;
            }
        }

        // ───────────────────────────── Разделы ─────────────────────────────

        private static void AssignSections(uint seed, in MallCellInfo info, int level, NativeArray<SectionInfo> sections, ref MallLevelPlan plan)
        {
            int floor = MallLayout.RetailFloor(level);
            int2 origin = MallLayout.CellOrigin(info.Cell);
            for (int i = 1; i < plan.Rooms.Length; i++)
            {
                var room = plan.Rooms[i];
                uint h = Hash.Get(seed, info.Cell.x, info.Cell.y, SaltRoom + 10000 + level * 256 + i);
                if (floor < 0 || room.Has(RoomFlags.Service))
                {
                    room.Flags |= RoomFlags.Service;
                    room.Section = (byte)PickService(sections, level, h);
                }
                else
                {
                    int2 center = origin + (room.Rect.xy + room.Rect.zw) / 2;
                    room.Section = (byte)SectionAt(seed, sections, center, floor);
                }
                if (!room.Has(RoomFlags.Stairwell))
                {
                    if ((h >> 8) % 100 < (floor < 0 ? 22u : 8u))
                        room.Flags |= RoomFlags.Dark;
                    if ((h >> 16) % 100 < 8)
                        room.Flags |= RoomFlags.Empty;
                }
                plan.Rooms[i] = room;
            }
        }

        /// <summary>
        /// Раздел в точке торгового этажа: ближайшая точка Вороного на сетке 32 м. Раздел точки выбирается по весам;
        /// с вероятностью 40% этаж повторяет раздел этажа ниже (магазины в два-три этажа).
        /// </summary>
        public static int SectionAt(uint seed, NativeArray<SectionInfo> sections, int2 position, int floor)
        {
            int2 grid = position >> 5;
            float best = float.MaxValue;
            int2 nearest = grid;
            for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int2 g = grid + new int2(dx, dz);
                uint h = Hash.Get(seed, g.x, g.y, SaltSection);
                float2 point = g * 32 + new float2(4 + h % 24, 4 + (h >> 8) % 24);
                float d = math.distancesq(point, (float2)position + 0.5f);
                if (d < best)
                {
                    best = d;
                    nearest = g;
                }
            }

            int salt = floor;
            while (salt > 0 && Hash.Get(seed, nearest.x, nearest.y, SaltSection + 1 + salt) % 10 < 4)
                salt--;
            return PickWeighted(sections, floor, Hash.Get(seed, nearest.x, nearest.y, SaltSection + 20 + salt));
        }

        private static int PickWeighted(NativeArray<SectionInfo> sections, int floor, uint h)
        {
            float total = 0f;
            for (int i = 0; i < sections.Length; i++)
                if (IsEligible(sections[i], floor))
                    total += sections[i].Weight;
            if (total <= 0f)
                return PickService(sections, MallLayout.FirstRetailLevel + floor, h);
            float roll = Hash.ToFloat01(h) * total;
            int last = 0;
            for (int i = 0; i < sections.Length; i++)
            {
                if (!IsEligible(sections[i], floor))
                    continue;
                last = i;
                roll -= sections[i].Weight;
                if (roll < 0f)
                    return i;
            }
            return last;
        }

        private static bool IsEligible(SectionInfo section, int floor) =>
            !section.Service && section.Weight > 0f && (section.RetailFloorMask & (1 << floor)) != 0;

        /// <summary>
        /// Служебный раздел комнаты по весам для типа этажа (подсобка, вентиляционная, щитовая…). Без весов —
        /// поровну среди служебных: на торговых этажах — с битом этажа в маске, на техническом и парковке — с пустой маской.
        /// </summary>
        private static int PickService(NativeArray<SectionInfo> sections, int level, uint h)
        {
            LevelKind kind = MallLayout.Kind(level);
            int floor = MallLayout.RetailFloor(level);
            float total = 0f;
            for (int i = 0; i < sections.Length; i++)
                total += ServiceWeight(sections[i], kind, floor);
            if (total > 0f)
            {
                float roll = Hash.ToFloat01(h) * total;
                int last = 0;
                for (int i = 0; i < sections.Length; i++)
                {
                    float w = ServiceWeight(sections[i], kind, floor);
                    if (w <= 0f)
                        continue;
                    last = i;
                    roll -= w;
                    if (roll < 0f)
                        return i;
                }
                return last;
            }
            int count = 0;
            for (int i = 0; i < sections.Length; i++)
                if (IsService(sections[i], floor))
                    count++;
            if (count == 0)
                return 0;
            int pick = (int)(h % (uint)count);
            for (int i = 0; i < sections.Length; i++)
                if (IsService(sections[i], floor) && pick-- == 0)
                    return i;
            return 0;
        }

        private static float ServiceWeight(in SectionInfo section, LevelKind kind, int floor)
        {
            if (!section.Service)
                return 0f;
            if (kind == LevelKind.Retail && section.RetailFloorMask != 0 && (section.RetailFloorMask & (1 << floor)) == 0)
                return 0f;
            return kind switch
            {
                LevelKind.Retail => section.ServiceRetail,
                LevelKind.Tech => section.ServiceTech,
                _ => section.ServiceParking,
            };
        }

        private static bool IsService(SectionInfo section, int floor) =>
            section.Service && (floor < 0 ? section.RetailFloorMask == 0 : (section.RetailFloorMask & (1 << floor)) != 0);

        // ───────────────────────────── Геометрия квартала ─────────────────────────────

        /// <summary>Канонические координаты клетки квартала → повёрнутые (без смещения квартала).</summary>
        public static int2 RotateCell(int x, int z, int rotation)
        {
            const int m = Block - 1;
            if (rotation == 0) return new int2(x, z);
            if (rotation == 1) return new int2(m - z, x);
            if (rotation == 2) return new int2(m - x, m - z);
            return new int2(z, m - x);
        }

        /// <summary>Канонический прямоугольник квартала → прямоугольник в координатах ячейки.</summary>
        public static int4 RotateRect(int4 rect, int rotation)
        {
            int2 a = RotateCell(rect.x, rect.y, rotation);
            int2 b = RotateCell(rect.z - 1, rect.w - 1, rotation);
            return new int4(math.min(a, b) + Band, math.max(a, b) + 1 + Band);
        }

        /// <summary>Непрерывная точка квартала (метры, углы клеток — целые) → координаты ячейки.</summary>
        public static float2 BlockToCell(float2 p, int rotation)
        {
            const float s = Block;
            float2 r = rotation == 0 ? p : rotation == 1 ? new float2(s - p.y, p.x) : rotation == 2 ? new float2(s - p.x, s - p.y) : new float2(p.y, s - p.x);
            return r + Band;
        }

        /// <summary>Поворот канонического направления +Z квартала, градусы (для префабов).</summary>
        public static float BlockYaw(int rotation) => -90f * rotation;

        private static int NeighborRoom(int x, int z, NativeArray<byte> tiles, NativeArray<byte> rooms)
        {
            if (x > 0 && IsRoomCell(tiles, rooms, x - 1, z)) return rooms[B(x - 1, z)];
            if (z > 0 && IsRoomCell(tiles, rooms, x, z - 1)) return rooms[B(x, z - 1)];
            if (x < Block - 1 && IsRoomCell(tiles, rooms, x + 1, z)) return rooms[B(x + 1, z)];
            if (z < Block - 1 && IsRoomCell(tiles, rooms, x, z + 1)) return rooms[B(x, z + 1)];
            return 0;
        }

        private static bool IsRoomCell(NativeArray<byte> tiles, NativeArray<byte> rooms, int x, int z) =>
            tiles[B(x, z)] == (byte)Tile.Open && rooms[B(x, z)] != 0;

        private static bool IsTile(NativeArray<byte> tiles, int2 p, Tile tile) =>
            p.x >= 0 && p.y >= 0 && p.x < Block && p.y < Block && tiles[B(p.x, p.y)] == (byte)tile;

        private static int B(int x, int z) => x + z * Block;

        private static int4 Expand(int4 rect, int amount) => new(rect.xy - amount, rect.zw + amount);

        private static bool Overlaps(int4 a, int4 b) => a.x < b.z && b.x < a.z && a.y < b.w && b.y < a.w;
    }
}
