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
            var info = new MallCellInfo { Cell = cell };
            uint h = Hash.Get(seed, cell.x, cell.y, SaltCell);
            info.Rotation = (int)(h & 3);
            info.CoreOffset = 8 + (int)((h >> 2) % 24);
            info.CoreRect = RotateRect(new int4(info.CoreOffset, 0, info.CoreOffset + CoreWidth, CoreDepth), info.Rotation);
            info.AtriumX = Hash.Get(seed, cell.x, cell.y, SaltCell + 1) % 100 < 45;
            info.AtriumZ = Hash.Get(seed, cell.x, cell.y, SaltCell + 2) % 100 < 45;

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

            PlaceAnomaly(seed, anomalyFrequency, ref info);
            PlaceMarkers(seed, anomalyFrequency, ref info);
            return info;
        }

        private static void PlaceAnomaly(uint seed, float frequency, ref MallCellInfo info)
        {
            int2 cell = info.Cell;
            float roll = Hash.ToFloat01(Hash.Get(seed, cell.x, cell.y, SaltCell + 20));
            uint h = Hash.Get(seed, cell.x, cell.y, SaltCell + 21);
            uint h2 = Hash.Get(seed, cell.x, cell.y, SaltCell + 22);
            float pAbyss = 0.1f * frequency, pPool = 0.08f * frequency, pCutout = 0.05f * frequency;

            float zone;
            if (roll < pAbyss)
            {
                info.Anomaly = AnomalyKind.Abyss;
                info.AnomalyRadius = 4f + h % 500 / 100f;
                info.AnomalyLevel = 1 + (int)(h2 % 6);
                zone = MallAnomalies.AbyssZoneRadius(info.AnomalyRadius);
            }
            else if (roll < pAbyss + pPool)
            {
                info.Anomaly = AnomalyKind.Pool;
                info.AnomalyRadius = 5f + h % 300 / 100f;
                info.AnomalyDepth = 2.5f + (h >> 12) % 150 / 100f;
                info.AnomalyLevel = MallLayout.FirstRetailLevel + (int)(h2 % MallLayout.RetailFloors);
                zone = MallAnomalies.PoolZoneRadius(info.AnomalyRadius);
            }
            else if (roll < pAbyss + pPool + pCutout)
            {
                info.Anomaly = AnomalyKind.Cutout;
                int quadrant = (int)(h % 4);
                info.AnomalyLevel = MallLayout.FirstRetailLevel + (int)(h2 % MallLayout.RetailFloors);
                info.AnomalyCenter = MallLayout.CellOrigin(cell) + new float2(quadrant & 1, quadrant >> 1) * WorldConstants.ChunkSize;
                info.AnomalyRadius = WorldConstants.ChunkSize;
                return;
            }
            else
            {
                return;
            }

            // Бездна и котлован — внутри квартала, на стороне, противоположной ядру.
            float margin = zone + 1f;
            float u = math.lerp(margin, Block - margin, Hash.ToFloat01(Hash.Get(seed, cell.x, cell.y, SaltCell + 23)));
            float vMin = CoreDepth + margin, vMax = Block - margin;
            float v = vMax > vMin ? math.lerp(vMin, vMax, Hash.ToFloat01(Hash.Get(seed, cell.x, cell.y, SaltCell + 24))) : (vMin + vMax) * 0.5f;
            info.AnomalyCenter = MallLayout.CellOrigin(cell) + BlockToCell(new float2(u, v), info.Rotation);
        }

        private static void PlaceMarkers(uint seed, float frequency, ref MallCellInfo info)
        {
            for (int kind = 1; kind <= 2; kind++)
            {
                int2 cell = info.Cell;
                if (Hash.ToFloat01(Hash.Get(seed, cell.x, cell.y, SaltCell + 30 + kind)) >= 0.12f * frequency)
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
            for (int x = 0; x < 64; x++)
            {
                var info = GetCellInfo(seed, anomalyFrequency, new int2(x, 0));
                if (info.Anomaly == AnomalyKind.Cutout)
                    continue;
                float2 origin = MallLayout.CellOrigin(info.Cell);
                return new float3(origin.x + 2.5f, MallLayout.FloorTop(MallLayout.FirstRetailLevel), origin.y + 24.5f);
            }
            return new float3(2.5f, MallLayout.FloorTop(MallLayout.FirstRetailLevel), 24.5f);
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
                BuildGalleries(info, level, ref plan);
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

        private static void BuildGalleries(in MallCellInfo info, int level, ref MallLevelPlan plan)
        {
            for (int z = 6; z <= 7; z++)
            for (int x = 6; x <= 7; x++)
                plan.Tiles[MallLevelPlan.Index(x, z)] = (byte)Tile.Pillar;

            int floor = MallLayout.RetailFloor(level);
            if (floor < 0)
                return;
            if (info.AtriumX)
                BuildAtrium(ref plan, floor, alongX: true);
            if (info.AtriumZ)
                BuildAtrium(ref plan, floor, alongX: false);
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
                    room.Section = (byte)PickService(sections, floor, h);
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
                return PickService(sections, floor, h);
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

        /// <summary>Служебный раздел: на торговых этажах — с битом этажа в маске, на техническом и парковке — с пустой маской.</summary>
        private static int PickService(NativeArray<SectionInfo> sections, int floor, uint h)
        {
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
