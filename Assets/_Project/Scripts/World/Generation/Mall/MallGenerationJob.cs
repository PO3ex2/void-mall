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

        private const int Size = WorldConstants.ChunkSize;

        public void Execute()
        {
            Lamps.Clear();
            int3 origin = Chunk * Size;
            int2 cell = MallLayout.CellOf(origin.x, origin.z);
            int2 cellOrigin = MallLayout.CellOrigin(cell);
            MallCellInfo info = MallPlanner.GetCellInfo(Seed, AnomalyFrequency, cell);

            // Колонки: расстояние до оси аномалии и попадание в вырез считаем один раз на (x, z).
            var radial = new NativeArray<float>(Size * Size, Allocator.Temp);
            var cut = new NativeArray<bool>(Size * Size, Allocator.Temp);
            for (int z = 0; z < Size; z++)
            for (int x = 0; x < Size; x++)
            {
                float2 world = new float2(origin.x + x + 0.5f, origin.z + z + 0.5f);
                radial[x + z * Size] = math.distance(world, info.AnomalyCenter);
                cut[x + z * Size] = info.Anomaly == AnomalyKind.Cutout
                    && world.x >= info.AnomalyCenter.x && world.x < info.AnomalyCenter.x + Size
                    && world.y >= info.AnomalyCenter.y && world.y < info.AnomalyCenter.y + Size;
            }

            var plan = new MallLevelPlan(Allocator.Temp);
            var lining = BuildLining(info, origin, cellOrigin, radial, ref plan);
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
                    Cell value = GenerateCell(p, p.xz - cellOrigin, level, hasPlan, plan, info, radial[column], cut[column], lining[column]);
                    Cells[WorldConstants.CellIndex(x, ly, z)] = value;
                    if (value.Material != 0 && (value.Material == M.Lamp || value.Material == M.Emergency || value.Material == M.Skylight))
                        Lamps.Add(p);
                }
                y = end;
            }
            plan.Dispose();
            radial.Dispose();
            cut.Dispose();
            lining.Dispose();

            Cell first = Cells[0];
            bool uniform = true;
            for (int i = 1; i < Cells.Length && uniform; i++)
                uniform = Cells[i] == first;
            IsUniform[0] = (byte)(uniform ? 1 : 0);
        }

        private Cell GenerateCell(int3 p, int2 local, int level, bool hasPlan, in MallLevelPlan plan, in MallCellInfo info, float radial, bool cut, ushort lining)
        {
            // Вырез: от дна мира до потолка уровня — пустота.
            if (cut && p.y <= MallLayout.CeilingRow(info.AnomalyLevel))
                return Cell.Air;

            bool inAbyss = info.Anomaly == AnomalyKind.Abyss && radial < MallAnomalies.AbyssZoneRadius(info.AnomalyRadius);
            bool inPool = info.Anomaly == AnomalyKind.Pool && radial < MallAnomalies.PoolZoneRadius(info.AnomalyRadius);
            if ((inAbyss || inPool) && p.y < MallLayout.CeilingRow(info.AnomalyLevel))
            {
                // Поле расстояния аномалии: над полом — воздух с плотностью (плавный край), в оболочке — гладкий материал,
                // за оболочкой — обычный мол (потолок и комнаты уровня ниже).
                float floorTop = MallLayout.FloorTop(info.AnomalyLevel);
                float s = inAbyss
                    ? MallAnomalies.ShaftSolidDistance(radial, p.y + 0.5f, info.AnomalyRadius, floorTop)
                    : MallAnomalies.PoolSolidDistance(radial, p.y + 0.5f, info.AnomalyRadius, info.AnomalyDepth, floorTop);
                if (s <= MallAnomalies.ShellThickness)
                {
                    // Облицовка — пол раздела (гладкий двойник); стенки шахты глубже кромки — бетон.
                    bool lined = inPool || p.y + 0.5f > floorTop - MallAnomalies.LipRadius - 1.5f;
                    return ShellCell(s, lined ? lining : M.ConcreteMass);
                }
            }

            if (p.y == 0)
                return new Cell(M.Bedrock);
            if (level < 0)
                return new Cell(p.y <= MallLayout.SoilTopY ? M.Soil : M.ConcreteMass, 255);
            if (!hasPlan)
                return p.y == MallLayout.RoofY ? new Cell(M.Concrete) : Cell.Air;
            Cell cell = GenerateLevelCell(p, local, level, plan, info);
            // Лампы за оболочкой аномалии торчали бы в стенках шахты или чаши — там просто потолок.
            if ((cell.Material == M.Lamp || cell.Material == M.Emergency) && p.y < MallLayout.FloorTop(info.AnomalyLevel))
            {
                bool nearAbyss = info.Anomaly == AnomalyKind.Abyss && radial < MallAnomalies.AbyssZoneRadius(info.AnomalyRadius) + 1f;
                bool nearPool = info.Anomaly == AnomalyKind.Pool && radial < MallAnomalies.PoolZoneRadius(info.AnomalyRadius) + 1f;
                if (nearAbyss || nearPool)
                    return new Cell(M.Concrete);
            }
            return cell;
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
                case Tile.Door: return rel <= doorHeight ? Cell.Air : new Cell(wallMat);
                case Tile.FacadeWall: return new Cell(facadeMat);
                case Tile.FacadeGlass: return rel < free ? new Cell(M.Glass) : new Cell(facadeMat);
                case Tile.FacadeDoor: return rel <= doorHeight ? Cell.Air : new Cell(facadeMat);
                case Tile.Shutter: return rel <= doorHeight ? new Cell(M.Shutter) : new Cell(facadeMat);
                case Tile.Pillar: return new Cell(pillarMat);
                case Tile.CoreWall: return new Cell(M.Concrete);
                case Tile.CoreDoor: return rel <= doorHeight ? Cell.Air : new Cell(M.Concrete);
                case Tile.Railing: return rel == 1 ? new Cell(M.Glass) : Cell.Air;
                case Tile.Barrier: return rel == 1 ? new Cell(M.Concrete) : Cell.Air;
                default: return Cell.Air;
            }
        }

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
