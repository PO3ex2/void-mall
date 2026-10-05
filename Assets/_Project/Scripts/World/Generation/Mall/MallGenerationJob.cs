using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using VoidMall.Core;

namespace VoidMall.World
{
    /// <summary>Материалы, которые генератор ставит сам (не из разделов). Резолвятся по ID при старте.</summary>
    public struct MallMaterials
    {
        public ushort Bedrock, Concrete, ConcreteMass, Soil, SmoothTile;
        public ushort Terrazzo, CeilingTile, PaintWhite, Glass, Shutter, FacadeDark;
        public ushort Asphalt, LineX, LineZ;
        public ushort Lamp, Emergency, Skylight;
        public ushort Duct, CableTray;
        public ushort Door, Gate, Sliding1, Sliding2, Sliding4, Water;

        public static MallMaterials Resolve(MaterialRegistry registry) => new()
        {
            Bedrock = registry.IndexOf("block.bedrock"),
            Concrete = registry.IndexOf("block.concrete"),
            ConcreteMass = registry.IndexOf("smooth.concrete"),
            Soil = registry.IndexOf("smooth.soil"),
            SmoothTile = registry.IndexOf("smooth.tile"),
            Terrazzo = registry.IndexOf("block.terrazzo"),
            CeilingTile = registry.IndexOf("block.ceiling_tile"),
            PaintWhite = registry.IndexOf("block.paint_white"),
            Glass = registry.IndexOf("block.glass"),
            Shutter = registry.IndexOf("block.shutter"),
            FacadeDark = registry.IndexOf("block.facade_dark"),
            Asphalt = registry.IndexOf("block.asphalt"),
            LineX = registry.IndexOf("block.parking_line_x"),
            LineZ = registry.IndexOf("block.parking_line_z"),
            Lamp = registry.IndexOf("block.lamp_panel"),
            Emergency = registry.IndexOf("block.emergency_lamp"),
            Skylight = registry.IndexOf("block.skylight"),
            Duct = registry.IndexOf("block.duct"),
            CableTray = registry.IndexOf("block.cable_tray"),
            Door = registry.TryGetIndex("fblock.door", out ushort door) ? door : (ushort)0,
            Gate = registry.TryGetIndex("fblock.gate", out ushort gate) ? gate : (ushort)0,
            Sliding1 = registry.TryGetIndex("fblock.sliding_door_1", out ushort sliding1) ? sliding1 : (ushort)0,
            Sliding2 = registry.TryGetIndex("fblock.sliding_door_2", out ushort sliding2) ? sliding2 : (ushort)0,
            Sliding4 = registry.TryGetIndex("fblock.sliding_door_4", out ushort sliding4) ? sliding4 : (ushort)0,
            Water = registry.TryGetIndex("block.water", out ushort water) ? water : (ushort)0,
        };
    }

    /// <summary>
    /// Генерация чанка мола (проходы 1–5 из ARCHITECTURE §4.6): фундамент, уровни по плану из MallPlanner,
    /// отделка по разделам, лампы, аномалии (бездна, котлован, вырез). Детерминирована по сиду и координатам.
    /// Чанк 32×32 всегда лежит в одной макроячейке 64×64, поэтому план строится для одной ячейки.
    /// </summary>
    [BurstCompile]
    public struct MallGenerationJob : IJob
    {
        public int3 Chunk;
        public uint Seed;
        public float AnomalyFrequency;
        public MallMaterials M;

        [ReadOnly] public NativeArray<SectionInfo> Sections;

        /// <summary>Описания материалов — для гладких двойников полов (облицовка аномалий).</summary>
        [ReadOnly] public NativeArray<MaterialInfo> Materials;

        public NativeArray<Cell> Cells;

        /// <summary>[0] = 1, если все клетки чанка одинаковые.</summary>
        public NativeArray<byte> IsUniform;

        /// <summary>Мировые координаты всех светящихся блоков чанка (для LightingDirector).</summary>
        public NativeList<int3> Lamps;

        /// <summary>Мировые координаты якорей функциональных блоков (двери, ворота) — для их моделей.</summary>
        public NativeList<int3> Anchors;

        private const int Size = WorldConstants.ChunkSize;

        public void Execute()
        {
            Lamps.Clear();
            Anchors.Clear();
            int3 origin = Chunk * Size;
            int2 cell = MallLayout.CellOf(origin.x, origin.z);
            int2 cellOrigin = MallLayout.CellOrigin(cell);
            MallCellInfo info = MallPlanner.GetCellInfo(Seed, AnomalyFrequency, cell);

            // Колонки: расстояние до оси аномалии и попадание в вырез считаем один раз на (x, z).
            // Большая бездна соседней ячейки — вторая аномалия этой ячейки (зоны со своей не пересекаются).
            MallCellInfo big = info.HasBigAbyss ? info.WithBigAbyss() : default;
            var radial = new NativeArray<float>(Size * Size, Allocator.Temp);
            var radialBig = new NativeArray<float>(Size * Size, Allocator.Temp);
            var cut = new NativeArray<bool>(Size * Size, Allocator.Temp);
            for (int z = 0; z < Size; z++)
            for (int x = 0; x < Size; x++)
            {
                float2 world = new float2(origin.x + x + 0.5f, origin.z + z + 0.5f);
                radial[x + z * Size] = math.distance(world, info.AnomalyCenter);
                radialBig[x + z * Size] = info.HasBigAbyss ? math.distance(world, big.AnomalyCenter) : float.MaxValue;
                cut[x + z * Size] = info.Anomaly == AnomalyKind.Cutout
                    && world.x >= info.AnomalyCenter.x && world.x < info.AnomalyCenter.x + Size
                    && world.y >= info.AnomalyCenter.y && world.y < info.AnomalyCenter.y + Size;
            }

            var plan = new MallLevelPlan(Allocator.Temp);
            var lining = BuildLining(info, origin, cellOrigin, radial, ref plan);
            var liningBig = BuildLining(big, origin, cellOrigin, radialBig, ref plan);
            int y = 0;
            while (y < Size)
            {
                int worldY = origin.y + y;
                int level = MallLayout.LevelAt(worldY);
                // Диапазон строк чанка, принадлежащих этому уровню.
                int end = y + 1;
                while (end < Size && MallLayout.LevelAt(origin.y + end) == level)
                    end++;

                bool hasPlan = level >= 0 && level < MallLayout.LevelCount;
                if (hasPlan)
                    MallPlanner.BuildLevel(Seed, info, level, Sections, ref plan);

                for (int ly = y; ly < end; ly++)
                for (int z = 0; z < Size; z++)
                for (int x = 0; x < Size; x++)
                {
                    int3 p = origin + new int3(x, ly, z);
                    int column = x + z * Size;
                    Cell value = GenerateCell(p, p.xz - cellOrigin, level, hasPlan, plan, info, radial[column], cut[column], lining[column],
                        big, radialBig[column], liningBig[column]);
                    Cells[WorldConstants.CellIndex(x, ly, z)] = value;
                    if (value.Material != 0 && (value.Material == M.Lamp || value.Material == M.Emergency || value.Material == M.Skylight))
                        Lamps.Add(p);
                    else if (value.Material != 0 && value.Value == 0 && (value.Material == M.Door || value.Material == M.Gate
                             || value.Material == M.Sliding1 || value.Material == M.Sliding2 || value.Material == M.Sliding4))
                        Anchors.Add(p);
                }
                y = end;
            }
            plan.Dispose();
            radial.Dispose();
            radialBig.Dispose();
            liningBig.Dispose();
            cut.Dispose();
            lining.Dispose();

            Cell first = Cells[0];
            bool uniform = true;
            for (int i = 1; i < Cells.Length && uniform; i++)
                uniform = Cells[i] == first;
            IsUniform[0] = (byte)(uniform ? 1 : 0);
        }

        private Cell GenerateCell(int3 p, int2 local, int level, bool hasPlan, in MallLevelPlan plan, in MallCellInfo info, float radial, bool cut, ushort lining,
            in MallCellInfo big, float radialBig, ushort liningBig)
        {
            // Вырез: от дна мира до потолка уровня — пустота.
            if (cut && p.y <= MallLayout.CeilingRow(info.AnomalyLevel))
                return Cell.Air;

            if (TryShell(p, info, radial, lining, out Cell shell) || TryShell(p, big, radialBig, liningBig, out shell))
                return shell;

            if (p.y == 0)
                return new Cell(M.Bedrock);
            if (level < 0)
                return new Cell(p.y <= MallLayout.SoilTopY ? M.Soil : M.ConcreteMass, 255);
            if (!hasPlan)
                return p.y == MallLayout.RoofY ? new Cell(M.Concrete) : Cell.Air;
            Cell cell = GenerateLevelCell(p, local, level, plan, info);
            // Лампы за оболочкой аномалии торчали бы в стенках шахты или чаши — там просто потолок.
            if ((cell.Material == M.Lamp || cell.Material == M.Emergency) && (NearShell(p, info, radial) || NearShell(p, big, radialBig)))
                return new Cell(M.Concrete);
            return cell;
        }

        /// <summary>
        /// Клетка в оболочке бездны или котлована: над полом — воздух с плотностью (плавный край), в оболочке — гладкий
        /// материал. За оболочкой (ложь) — обычный мол: потолок и комнаты уровня ниже.
        /// </summary>
        private bool TryShell(int3 p, in MallCellInfo anomaly, float radial, ushort lining, out Cell cell)
        {
            cell = default;
            bool inAbyss = anomaly.Anomaly == AnomalyKind.Abyss && radial < MallAnomalies.AbyssZoneRadius(anomaly.AnomalyRadius);
            bool inPool = anomaly.Anomaly == AnomalyKind.Pool && radial < MallAnomalies.PoolZoneRadius(anomaly.AnomalyRadius);
            if ((!inAbyss && !inPool) || p.y >= MallLayout.CeilingRow(anomaly.AnomalyLevel))
                return false;
            float floorTop = MallLayout.FloorTop(anomaly.AnomalyLevel);
            float s = inAbyss
                ? MallAnomalies.ShaftSolidDistance(radial, p.y + 0.5f, anomaly.AnomalyRadius, floorTop)
                : MallAnomalies.PoolSolidDistance(radial, p.y + 0.5f, anomaly.AnomalyRadius, anomaly.AnomalyDepth, floorTop);
            if (s > MallAnomalies.ShellThickness)
                return false;
            // Облицовка — пол раздела (гладкий двойник); стенки шахты глубже кромки — бетон.
            bool lined = inPool || p.y + 0.5f > floorTop - MallAnomalies.LipRadius - 1.5f;
            cell = ShellCell(s, lined ? lining : M.ConcreteMass);
            return true;
        }

        private static bool NearShell(int3 p, in MallCellInfo anomaly, float radial)
        {
            if (p.y >= MallLayout.FloorTop(anomaly.AnomalyLevel))
                return false;
            return (anomaly.Anomaly == AnomalyKind.Abyss && radial < MallAnomalies.AbyssZoneRadius(anomaly.AnomalyRadius) + 1f)
                || (anomaly.Anomaly == AnomalyKind.Pool && radial < MallAnomalies.PoolZoneRadius(anomaly.AnomalyRadius) + 1f);
        }

        /// <summary>
        /// Материал облицовки бездны или котлована по колонкам: гладкий двойник пола комнаты на уровне аномалии
        /// (ковёр, плитка, терраццо...). Без двойника — плитка бассейна или бетон.
        /// </summary>
        private NativeArray<ushort> BuildLining(in MallCellInfo info, int3 origin, int2 cellOrigin, NativeArray<float> radial, ref MallLevelPlan plan)
        {
            var lining = new NativeArray<ushort>(Size * Size, Allocator.Temp);
            bool pool = info.Anomaly == AnomalyKind.Pool;
            if (!pool && info.Anomaly != AnomalyKind.Abyss)
                return lining;
            ushort fallback = pool ? M.SmoothTile : M.ConcreteMass;
            float zone = pool ? MallAnomalies.PoolZoneRadius(info.AnomalyRadius) : MallAnomalies.AbyssZoneRadius(info.AnomalyRadius);
            int level = info.AnomalyLevel;
            bool built = false;
            for (int z = 0; z < Size; z++)
            for (int x = 0; x < Size; x++)
            {
                int column = x + z * Size;
                lining[column] = fallback;
                if (radial[column] >= zone + 1f)
                    continue;
                if (!built)
                {
                    MallPlanner.BuildLevel(Seed, info, level, Sections, ref plan);
                    built = true;
                }
                int3 slab = new(origin.x + x, MallLayout.LevelBase(level), origin.z + z);
                ushort floor = GenerateLevelCell(slab, slab.xz - cellOrigin, level, plan, info).Material;
                ushort smooth = floor != 0 && floor < Materials.Length ? Materials[floor].SmoothVariant : (ushort)0;
                if (smooth != 0)
                    lining[column] = smooth;
            }
            return lining;
        }

        /// <summary>Клетка оболочки аномалии: твёрдая — гладкий материал, снаружи — воздух с плотностью (плавная поверхность).</summary>
        private static Cell ShellCell(float solidDistance, ushort material)
        {
            byte density = Density.FromField(MallAnomalies.Shell(solidDistance));
            return Density.IsSolid(density) ? new Cell(material, density) : new Cell(0, density);
        }

        private Cell GenerateLevelCell(int3 p, int2 local, int level, in MallLevelPlan plan, in MallCellInfo info)
        {
            int index = MallLevelPlan.Index(local.x, local.y);
            Tile tile = (Tile)plan.Tiles[index];
            MallRoom room = plan.Rooms[plan.RoomIds[index]];
            LevelKind kind = MallLayout.Kind(level);
            int rel = p.y - MallLayout.LevelBase(level);
            int free = MallLayout.FreeHeight(level);
            int doorHeight = MallLayout.DoorHeight(level);
            int retailFloor = MallLayout.RetailFloor(level);
            bool isPublic = plan.RoomIds[index] == 0;
            SectionInfo section = Sections[room.Section];

            // Фонтан круглой площади — из блоков (только в галерее у перекрёстка, нижние ряды уровня).
            if (isPublic && rel <= 2 && local.x < MallLayout.Band && local.y < MallLayout.Band && kind == LevelKind.Retail
                && MallPlanner.RoundPlaza(Seed, info, level, out bool fountain) && fountain)
            {
                Cell f = FountainCell(local, rel, out bool handled);
                if (handled)
                    return f;
            }

            ushort floorMat, wallMat, ceilingMat, facadeMat, pillarMat;
            if (kind == LevelKind.Parking)
            {
                floorMat = isPublic ? ParkingFloor(local) : section.Floor;
                wallMat = M.Concrete;
                ceilingMat = M.Concrete;
                facadeMat = M.Concrete;
                pillarMat = M.Concrete;
            }
            else if (isPublic)
            {
                floorMat = kind == LevelKind.Tech ? M.Concrete : M.Terrazzo;
                wallMat = kind == LevelKind.Tech ? M.Concrete : M.PaintWhite;
                ceilingMat = kind == LevelKind.Tech ? M.Concrete : M.CeilingTile;
                facadeMat = M.FacadeDark;
                pillarMat = wallMat;
            }
            else
            {
                floorMat = section.Floor;
                wallMat = section.Wall;
                ceilingMat = section.Ceiling;
                facadeMat = kind == LevelKind.Tech ? M.Concrete : section.Facade;
                pillarMat = kind == LevelKind.Tech ? M.Concrete : M.PaintWhite;
            }
            if (room.Has(RoomFlags.Stairwell))
            {
                floorMat = M.Concrete;
                ceilingMat = M.Concrete;
            }
            ushort galleryFloor = kind == LevelKind.Retail ? M.Terrazzo : kind == LevelKind.Tech ? M.Concrete : M.Asphalt;
            ushort galleryCeiling = kind == LevelKind.Retail ? M.CeilingTile : M.Concrete;

            bool top = level == MallLayout.TechLevel;

            // ── Плита ──
            if (rel == 0)
            {
                switch (tile)
                {
                    case Tile.AtriumVoid: return retailFloor == 0 ? new Cell(galleryFloor) : Cell.Air;
                    case Tile.StairLaneA:
                    case Tile.StairMid: return level == 0 || top ? new Cell(M.Concrete) : Cell.Air;
                    case Tile.StairLaneB:
                    case Tile.ElevatorVoid: return level == 0 ? new Cell(M.Concrete) : Cell.Air;
                    case Tile.RampHole: return level == 0 ? new Cell(floorMat) : Cell.Air;
                    case Tile.Pillar: return new Cell(pillarMat);
                    case Tile.CoreWall:
                    case Tile.CoreDoor: return new Cell(M.Concrete);
                    case Tile.FacadeWall:
                    case Tile.FacadeGlass:
                    case Tile.FacadeDoor:
                    case Tile.Shutter:
                    case Tile.Railing:
                    case Tile.Barrier: return new Cell(galleryFloor);
                    default: return new Cell(floorMat);
                }
            }

            // ── Потолочный ряд ──
            if (rel == free + 1)
            {
                switch (tile)
                {
                    case Tile.AtriumVoid: return retailFloor == MallLayout.RetailFloors - 1 ? new Cell(M.Skylight) : Cell.Air;
                    case Tile.StairLaneA:
                    case Tile.StairLaneB:
                    case Tile.StairMid:
                    case Tile.ElevatorVoid: return top ? new Cell(M.Concrete) : Cell.Air;
                    case Tile.RampHole: return level == 0 ? Cell.Air : new Cell(ceilingMat);
                    case Tile.Pillar: return new Cell(pillarMat);
                    case Tile.CoreWall:
                    case Tile.CoreDoor: return new Cell(M.Concrete);
                    case Tile.FacadeWall:
                    case Tile.FacadeGlass:
                    case Tile.FacadeDoor:
                    case Tile.Shutter:
                    case Tile.Railing: return new Cell(galleryCeiling);
                    case Tile.Wall:
                    case Tile.Door: return new Cell(ceilingMat);
                    default:
                    {
                        ushort lamp = LampAt(local, kind, room, isPublic, info);
                        return new Cell(lamp != 0 ? lamp : ceilingMat);
                    }
                }
            }

            // ── Свободная высота ──
            switch (tile)
            {
                case Tile.Wall: return new Cell(wallMat);
                case Tile.Door: return rel <= doorHeight ? DoorCell(local, rel, level, plan, info, Tile.Door, 0.55f, wallMat) : new Cell(wallMat);
                case Tile.FacadeWall: return new Cell(facadeMat);
                case Tile.FacadeGlass: return rel < free ? new Cell(M.Glass) : new Cell(facadeMat);
                case Tile.FacadeDoor: return rel <= doorHeight ? SlidingDoorCell(local, rel, level, plan, info, doorHeight, isPublic) : new Cell(facadeMat);
                case Tile.Shutter: return rel <= doorHeight ? GateCell(local, rel, level, plan, info, doorHeight) : new Cell(facadeMat);
                case Tile.Pillar: return new Cell(pillarMat);
                case Tile.CoreWall: return new Cell(M.Concrete);
                case Tile.CoreDoor: return rel <= doorHeight ? DoorCell(local, rel, level, plan, info, Tile.CoreDoor, 0.8f, M.Concrete) : new Cell(M.Concrete);
                case Tile.Railing: return rel == 1 ? new Cell(M.Glass) : Cell.Air;
                case Tile.Barrier: return rel == 1 ? new Cell(M.Concrete) : Cell.Air;
                default:
                {
                    ushort overhead = rel == free ? OverheadAt(local, kind, room, isPublic, section) : (ushort)0;
                    return overhead != 0 ? new Cell(overhead) : Cell.Air;
                }
            }
        }

        /// <summary>
        /// Дверь в проходе (функциональный блок 1×2): проход из двух клеток — двойная дверь (петли по краям), из
        /// одной — одиночная. Есть дверь или нет — решает хеш прохода; над дверью (торговые этажи) — перемычка.
        /// </summary>
        private Cell DoorCell(int2 local, int rel, int level, in MallLevelPlan plan, in MallCellInfo info, Tile doorTile, float chance, ushort aboveMat)
        {
            if (M.Door == 0)
                return Cell.Air;
            bool alongX = IsWallLike(plan, local + new int2(1, 0)) || IsWallLike(plan, local + new int2(-1, 0));
            int2 axis = alongX ? new int2(1, 0) : new int2(0, 1);
            bool hasLower = TileAt(plan, local - axis) == doorTile;
            bool hasUpper = TileAt(plan, local + axis) == doorTile;
            int2 key = hasLower ? local - axis : local;
            uint h = Hash.Get(Seed, info.Cell.x * 131 + key.x, info.Cell.y * 131 + key.y, level * 7 + 5111);
            if (Hash.ToFloat01(h) >= chance)
                return Cell.Air;
            // Над дверью высотой 2 в проёме высотой 3 — перемычка из стены, без щели.
            if (rel > 2)
                return new Cell(aboveMat);
            // Петля — на внешнем крае створки (у двойной — по краям прохода).
            bool second = hasLower && !hasUpper;
            int yaw = alongX ? (second ? 2 : 0) : (second ? 1 : 3);
            return FunctionalCells.Part(M.Door, new int3(0, rel - 1, 0), FunctionalCells.Meta(yaw));
        }

        /// <summary>
        /// Большие ворота на месте рольставни (сегменты 1×3 по линии фасада). Половина магазинов заперта:
        /// решает хеш начала проёма, поэтому весь проём заперт одинаково.
        /// </summary>
        private Cell GateCell(int2 local, int rel, int level, in MallLevelPlan plan, in MallCellInfo info, int doorHeight)
        {
            if (M.Gate == 0 || doorHeight != 3)
                return new Cell(M.Shutter);
            bool alongX = TileAt(plan, local + new int2(1, 0)) is Tile.Shutter or Tile.FacadeWall or Tile.FacadeGlass or Tile.FacadeDoor
                          || TileAt(plan, local + new int2(-1, 0)) is Tile.Shutter or Tile.FacadeWall or Tile.FacadeGlass or Tile.FacadeDoor;
            int2 axis = alongX ? new int2(1, 0) : new int2(0, 1);
            int2 start = local;
            for (int i = 0; i < 16 && TileAt(plan, start - axis) == Tile.Shutter; i++)
                start -= axis;
            uint h = Hash.Get(Seed, info.Cell.x * 131 + start.x, info.Cell.y * 131 + start.y, level * 7 + 6007);
            bool locked = Hash.ToFloat01(h) < 0.5f;
            return FunctionalCells.Part(M.Gate, new int3(0, rel - 1, 0), FunctionalCells.Meta(alongX ? 0 : 1, locked: locked));
        }

        /// <summary>
        /// Автоматическая стеклянная дверь во входе магазина — цельный блок на кусок проёма (4×3, 2×3 или 1×3),
        /// чтобы створки открывались вместе: жадно 4, 2, 1 (проёмы мола — 2 или 4). Две двери 2×3 рядом не ставим —
        /// их внутренние створки съехались бы в середину проёма и перекрыли его.
        /// Есть ли в проёме двери — по хешу начала проёма (иначе открытый проход).
        /// </summary>
        private Cell SlidingDoorCell(int2 local, int rel, int level, in MallLevelPlan plan, in MallCellInfo info, int doorHeight, bool entrance)
        {
            if (M.Sliding1 == 0 || doorHeight != 3)
                return Cell.Air;
            bool alongX = TileAt(plan, local + new int2(1, 0)) is Tile.FacadeDoor or Tile.FacadeWall or Tile.FacadeGlass or Tile.Shutter
                          || TileAt(plan, local + new int2(-1, 0)) is Tile.FacadeDoor or Tile.FacadeWall or Tile.FacadeGlass or Tile.Shutter;
            int2 axis = alongX ? new int2(1, 0) : new int2(0, 1);
            int2 start = local, end = local;
            for (int i = 0; i < 16 && TileAt(plan, start - axis) == Tile.FacadeDoor; i++)
                start -= axis;
            for (int i = 0; i < 16 && TileAt(plan, end + axis) == Tile.FacadeDoor; i++)
                end += axis;
            uint h = Hash.Get(Seed, info.Cell.x * 131 + start.x, info.Cell.y * 131 + start.y, level * 7 + 7019);
            // Вход в мол (фасад в галерее) — двери всегда, витрины магазинов — через раз.
            if (!entrance && Hash.ToFloat01(h) >= 0.6f)
                return Cell.Air;
            int along = alongX ? local.x - start.x : local.y - start.y;
            int length = (alongX ? end.x - start.x : end.y - start.y) + 1;
            // Куски проёма от начала: локальная +X блока смотрит вдоль проёма (поворот 0 по X, 3 по Z).
            int pieceStart = 0, width = 1;
            for (int s = 0; s < length; s += width)
            {
                int rest = length - s;
                width = M.Sliding4 != 0 && rest >= 4 ? 4 : M.Sliding2 != 0 && rest >= 2 ? 2 : 1;
                if (along < s + width)
                {
                    pieceStart = s;
                    break;
                }
            }
            ushort material = width == 4 ? M.Sliding4 : width == 2 ? M.Sliding2 : M.Sliding1;
            return FunctionalCells.Part(material, new int3(along - pieceStart, rel - 1, 0), FunctionalCells.Meta(alongX ? 0 : 3));
        }


        /// <summary>
        /// Фонтан круглой площади из блоков (GDD §2.9): вода на месте плиты пола, бортик из полублоков,
        /// в центре (вместо колонны) тумба и закруглённая «шапка» из четырёх круглых скосов «вертушкой».
        /// Air — клетка не фонтанная, генерация идёт обычным путём.
        /// </summary>
        private Cell FountainCell(int2 local, int rel, out bool handled)
        {
            handled = false;
            if (local.x >= MallLayout.Band || local.y >= MallLayout.Band || rel > 2)
                return Cell.Air;
            bool center = local.x >= 6 && local.x <= 7 && local.y >= 6 && local.y <= 7;
            float d = math.distance(new float2(local.x + 0.5f, local.y + 0.5f), new float2(7f, 7f)); // MallPlanner.PlazaCenter (static readonly — не для Burst)
            if (center)
            {
                if (rel == 0)
                    return Cell.Air;
                handled = true;
                if (rel == 1)
                    return new Cell(M.Terrazzo);
                // Вертушка: каждая четверть поднимается к соседней по кругу — шапка симметрична.
                int yaw = local.x == 6 ? (local.y == 6 ? 1 : 2) : (local.y == 6 ? 0 : 3);
                return new Cell(M.PaintWhite, (byte)BlockShape.RoundOuter, BlockShapes.Meta(0, yaw));
            }
            if (d < MallPlanner.FountainWaterRadius && rel == 0 && M.Water != 0)
            {
                handled = true;
                return new Cell(M.Water);
            }
            if (d >= MallPlanner.FountainWaterRadius && d < MallPlanner.FountainRadius && rel == 1)
            {
                handled = true;
                return new Cell(M.Terrazzo, (byte)BlockShape.Slab);
            }
            return Cell.Air;
        }

        private static Tile TileAt(in MallLevelPlan plan, int2 local)
        {
            if (local.x < 0 || local.y < 0 || local.x >= MallLayout.CellSize || local.y >= MallLayout.CellSize)
                return Tile.Open;
            return (Tile)plan.Tiles[MallLevelPlan.Index(local.x, local.y)];
        }

        private static bool IsWallLike(in MallLevelPlan plan, int2 local) =>
            TileAt(plan, local) is Tile.Wall or Tile.Door or Tile.CoreWall or Tile.CoreDoor or Tile.FacadeWall or Tile.Pillar;

        /// <summary>
        /// Под потолком (верхний ряд свободной высоты): воздуховоды и кабельные лотки в служебных коридорах
        /// технического этажа, воздуховод в вентиляционных, лоток в щитовых. Линии идут мимо ламп и колонн.
        /// </summary>
        private ushort OverheadAt(int2 local, LevelKind kind, in MallRoom room, bool isPublic, in SectionInfo section)
        {
            int x = local.x, z = local.y;
            if (isPublic)
            {
                if (kind != LevelKind.Tech)
                    return 0;
                // Обе галереи ячейки: лампы — сетка 8 м с остатком 2, аварийные — на 12, колонны — 6–7.
                bool alongZ = x < MallLayout.Band, alongX = z < MallLayout.Band;
                if ((alongZ && x == 4) || (alongX && z == 4))
                    return M.Duct;
                if ((alongZ && x == 9) || (alongX && z == 9))
                    return M.CableTray;
                return 0;
            }
            if (room.Has(RoomFlags.Stairwell))
                return 0;
            int4 r = room.Rect;
            bool longX = r.z - r.x >= r.w - r.y;
            if (section.Layout == SectionLayout.Tech)
            {
                int line = AwayFromLamps(longX ? r.y + 1 : r.x + 1);
                return (longX ? z == line : x == line) ? M.Duct : (ushort)0;
            }
            if (section.Layout == SectionLayout.Electrical)
            {
                int line = AwayFromLamps(longX ? r.y : r.x);
                return (longX ? z == line : x == line) ? M.CableTray : (ushort)0;
            }
            return 0;
        }

        /// <summary>Лампы в комнатах — через 4 клетки с остатком 2: линию сдвигаем, чтобы не закрыть их снизу.</summary>
        private static int AwayFromLamps(int line) => (line & 3) == 2 ? line + 1 : line;

        /// <summary>Лампа в потолочном ряду (или 0). Сетка 4 м в магазинах и галереях, аварийные — вдоль фасадов и на лестницах.</summary>
        private ushort LampAt(int2 local, LevelKind kind, MallRoom room, bool isPublic, in MallCellInfo info)
        {
            int x = local.x, z = local.y;
            if (room.Has(RoomFlags.Stairwell))
            {
                int2 lamp = MallPlanner.RotateCell(info.CoreOffset + 2, 2, info.Rotation) + MallLayout.Band;
                return x == lamp.x && z == lamp.y ? M.Emergency : (ushort)0;
            }
            if (kind == LevelKind.Parking)
            {
                if (!isPublic)
                    return (x & 3) == 2 && (z & 3) == 2 && !room.Has(RoomFlags.Dark) ? M.Lamp : (ushort)0;
                int mz = z & 15;
                if ((x & 15) == 5 && mz == 6)
                    return M.Emergency;
                if (mz == 15 && x % 6 == 3)
                    return M.Lamp;
                if ((mz == 4 || mz == 9) && x % 6 == 0)
                    return M.Lamp;
                return 0;
            }
            if (isPublic)
            {
                if ((x == 12 && z >= MallLayout.Band && (z & 7) == 4) || (z == 12 && x >= MallLayout.Band && (x & 7) == 4))
                    return M.Emergency;
                int spacing = kind == LevelKind.Tech ? 8 : 4;
                return x % spacing == 2 && z % spacing == 2 ? M.Lamp : (ushort)0;
            }
            if (room.Has(RoomFlags.Dark))
                return 0;
            return (x & 3) == 2 && (z & 3) == 2 ? M.Lamp : (ushort)0;
        }

        /// <summary>Разметка парковки: места 3×4 по обе стороны ряда колонн, пунктир по оси проезда.</summary>
        private ushort ParkingFloor(int2 local)
        {
            int mz = local.y & 15;
            bool bay = (mz >= 2 && mz < 6) || (mz >= 8 && mz < 12);
            if (bay && local.x % 3 == 0)
                return M.LineZ;
            if (mz == 15 && (local.x >> 1) % 2 == 0)
                return M.LineX;
            return M.Asphalt;
        }
    }
}
