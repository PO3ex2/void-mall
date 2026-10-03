using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using VoidMall.Core;

namespace VoidMall.World
{
    /// <summary>
    /// Расстановка мебели-заглушек по плану уровня (проход 6 из ARCHITECTURE §4.6): шаблон раздела
    /// (ряды, острова, периметр, зал, склад...) + скамейки в галереях и машины на парковке.
    /// Детерминирована: зависит от сида, ячейки, уровня и комнаты.
    /// </summary>
    public static class PropPlacer
    {
        private const int Salt = 8000;

        private struct Context
        {
            public uint Seed;
            public MallCellInfo Info;
            public int Level;
            public float FloorY;
            public float2 Origin;

            /// <summary>Занятые прямоугольники (min.xy, max.xy) — мебель не пересекается.</summary>
            public List<float4> Occupied;
        }

        public static void Place(uint seed, in MallCellInfo info, in MallLevelPlan plan, NativeArray<SectionInfo> sections, List<PropSpawn> output)
        {
            var ctx = new Context
            {
                Seed = seed,
                Info = info,
                Level = plan.Level,
                FloorY = MallLayout.FloorTop(plan.Level),
                Origin = MallLayout.CellOrigin(info.Cell),
                Occupied = new List<float4>(),
            };
            LevelKind kind = MallLayout.Kind(plan.Level);

            if (kind == LevelKind.Parking)
            {
                PlaceCars(ctx, plan, output);
            }
            else if (kind == LevelKind.Retail)
            {
                PlaceGalleryFurniture(ctx, plan, output);
            }

            for (int i = 1; i < plan.Rooms.Length; i++)
            {
                var room = plan.Rooms[i];
                if (room.Has(RoomFlags.Stairwell))
                    continue;
                uint h = Hash.Get(seed, info.Cell.x, info.Cell.y, Salt + plan.Level * 256 + i);
                if (room.Has(RoomFlags.Empty))
                {
                    if (h % 3 == 0)
                        TryAdd(ctx, plan, i, PropKind.BoxStack, Center(room.Rect) + new float2(0.5f, 0.5f), 0f, output);
                    continue;
                }
                SectionLayout layout = kind == LevelKind.Tech ? SectionLayout.Tech : sections[room.Section].Layout;
                switch (layout)
                {
                    case SectionLayout.Rows: Rows(ctx, plan, i, PropKind.Shelf, 3f, 2f, h, output); break;
                    case SectionLayout.Warehouse:
                        Rows(ctx, plan, i, PropKind.PalletRack, 3.9f, 2.7f, h, output);
                        Scatter(ctx, plan, i, PropKind.BoxStack, 3, h, output);
                        break;
                    case SectionLayout.Islands: Grid(ctx, plan, i, PropKind.DisplayTable, 3.4f, 3f, 2.2f, h, output); break;
                    case SectionLayout.Perimeter:
                        Perimeter(ctx, plan, i, PropKind.Shelf, output);
                        Grid(ctx, plan, i, PropKind.DisplayTable, 4.5f, 4f, 3.4f, h, output);
                        break;
                    case SectionLayout.Clothing:
                        Perimeter(ctx, plan, i, PropKind.Shelf, output);
                        Grid(ctx, plan, i, PropKind.ClothesRack, 2.6f, 2.4f, 2.4f, h, output);
                        break;
                    case SectionLayout.Hall: Dining(ctx, plan, i, output); break;
                    case SectionLayout.Showroom:
                        Grid(ctx, plan, i, PropKind.Sofa, 4.6f, 4.2f, 2.5f, h, output);
                        break;
                    case SectionLayout.Tech:
                        Perimeter(ctx, plan, i, PropKind.Cabinet, output);
                        if (math.min(room.Rect.z - room.Rect.x, room.Rect.w - room.Rect.y) >= 6)
                            TryAdd(ctx, plan, i, PropKind.Boiler, Center(room.Rect), 0f, output);
                        break;
                }
            }
        }

        // ───────────────────────────── Шаблоны ─────────────────────────────

        /// <summary>Двойные ряды (спина к спине) вдоль длинной стороны комнаты, поперечный проход каждые 5 секций.</summary>
        private static void Rows(in Context ctx, in MallLevelPlan plan, int room, PropKind kind, float pitch, float unit, uint h, List<PropSpawn> output)
        {
            int4 r = plan.Rooms[room].Rect;
            bool alongX = r.z - r.x >= r.w - r.y;
            float depth = PropCatalog.Size(kind).z;
            float a0 = alongX ? r.x : r.y, a1 = alongX ? r.z : r.w;
            float b0 = alongX ? r.y : r.x, b1 = alongX ? r.w : r.z;
            float margin = 1.8f;
            int units = (int)((a1 - a0 - margin * 2f) / unit);
            if (units <= 0)
                return;
            float start = (a0 + a1) * 0.5f - units * unit * 0.5f;
            for (float b = b0 + margin + depth; b + depth <= b1 - margin + 0.01f; b += pitch)
            {
                for (int u = 0; u < units; u++)
                {
                    if (units > 6 && u % 5 == 4)
                        continue; // поперечный проход
                    float a = start + (u + 0.5f) * unit;
                    float2 front = alongX ? new float2(a, b - depth * 0.5f) : new float2(b - depth * 0.5f, a);
                    float2 back = alongX ? new float2(a, b + depth * 0.5f) : new float2(b + depth * 0.5f, a);
                    float yaw = alongX ? 0f : 90f;
                    TryAdd(ctx, plan, room, kind, front, yaw, output);
                    TryAdd(ctx, plan, room, kind, back, yaw + 180f, output);
                }
            }
        }

        private static void Grid(in Context ctx, in MallLevelPlan plan, int room, PropKind kind, float pitchX, float pitchZ, float margin, uint h, List<PropSpawn> output)
        {
            int4 r = plan.Rooms[room].Rect;
            int nx = (int)((r.z - r.x - margin * 2f) / pitchX) + 1;
            int nz = (int)((r.w - r.y - margin * 2f) / pitchZ) + 1;
            if (r.z - r.x < margin * 2f || r.w - r.y < margin * 2f)
                return;
            float sx = (r.x + r.z) * 0.5f - (nx - 1) * pitchX * 0.5f;
            float sz = (r.y + r.w) * 0.5f - (nz - 1) * pitchZ * 0.5f;
            float yaw = (h & 1) == 0 ? 0f : 90f;
            for (int iz = 0; iz < nz; iz++)
            for (int ix = 0; ix < nx; ix++)
                TryAdd(ctx, plan, room, kind, new float2(sx + ix * pitchX, sz + iz * pitchZ), yaw, output);
        }

        /// <summary>Мебель вдоль стен: секции с отступом от стены, не ближе 1.5 м к дверям.</summary>
        private static void Perimeter(in Context ctx, in MallLevelPlan plan, int room, PropKind kind, List<PropSpawn> output)
        {
            int4 r = plan.Rooms[room].Rect;
            Vector3 size = PropCatalog.Size(kind);
            float half = size.z * 0.5f + 0.05f;
            // Стены: −Z (y), +Z (w), −X (x), +X (z). Мебель смотрит внутрь комнаты.
            for (int side = 0; side < 4; side++)
            {
                bool alongX = side < 2;
                float a0 = alongX ? r.x : r.y, a1 = alongX ? r.z : r.w;
                int units = (int)((a1 - a0 - 1f) / size.x);
                float start = (a0 + a1) * 0.5f - units * size.x * 0.5f;
                for (int u = 0; u < units; u++)
                {
                    float a = start + (u + 0.5f) * size.x;
                    float2 position;
                    float yaw;
                    int2 wall;
                    switch (side)
                    {
                        case 0: position = new float2(a, r.y + half); yaw = 180f; wall = new int2((int)a, r.y - 1); break;
                        case 1: position = new float2(a, r.w - half); yaw = 0f; wall = new int2((int)a, r.w); break;
                        case 2: position = new float2(r.x + half, a); yaw = 270f; wall = new int2(r.x - 1, (int)a); break;
                        default: position = new float2(r.z - half, a); yaw = 90f; wall = new int2(r.z, (int)a); break;
                    }
                    if (NearDoor(plan, wall, alongX, size.x * 0.5f + 1.5f))
                        continue;
                    TryAdd(ctx, plan, room, kind, position, yaw, output);
                }
            }
        }

        private static void Dining(in Context ctx, in MallLevelPlan plan, int room, List<PropSpawn> output)
        {
            int4 r = plan.Rooms[room].Rect;
            const float pitch = 3.4f, margin = 2f;
            int nx = (int)((r.z - r.x - margin * 2f) / pitch) + 1;
            int nz = (int)((r.w - r.y - margin * 2f) / pitch) + 1;
            if (r.z - r.x < margin * 2f || r.w - r.y < margin * 2f)
                return;
            float sx = (r.x + r.z) * 0.5f - (nx - 1) * pitch * 0.5f;
            float sz = (r.y + r.w) * 0.5f - (nz - 1) * pitch * 0.5f;
            for (int iz = 0; iz < nz; iz++)
            for (int ix = 0; ix < nx; ix++)
            {
                float2 c = new(sx + ix * pitch, sz + iz * pitch);
                if (!TryAdd(ctx, plan, room, PropKind.Table, c, 0f, output))
                    continue;
                TryAdd(ctx, plan, room, PropKind.Chair, c + new float2(0f, -0.95f), 180f, output);
                TryAdd(ctx, plan, room, PropKind.Chair, c + new float2(0f, 0.95f), 0f, output);
                TryAdd(ctx, plan, room, PropKind.Chair, c + new float2(-0.95f, 0f), 270f, output);
                TryAdd(ctx, plan, room, PropKind.Chair, c + new float2(0.95f, 0f), 90f, output);
            }
        }

        private static void Scatter(in Context ctx, in MallLevelPlan plan, int room, PropKind kind, int count, uint h, List<PropSpawn> output)
        {
            int4 r = plan.Rooms[room].Rect;
            for (int i = 0; i < count; i++)
            {
                uint hi = Hash.Get(h, i);
                float2 p = new(math.lerp(r.x + 1f, r.z - 1f, Hash.ToFloat01(hi)), math.lerp(r.y + 1f, r.w - 1f, Hash.ToFloat01(hi * 2654435761u)));
                TryAdd(ctx, plan, room, kind, p, (hi >> 3) % 4 * 90f, output);
            }
        }

        private static void PlaceGalleryFurniture(in Context ctx, in MallLevelPlan plan, List<PropSpawn> output)
        {
            for (int axis = 0; axis < 2; axis++)
            {
                bool atrium = axis == 0 ? ctx.Info.AtriumX : ctx.Info.AtriumZ;
                if (atrium)
                    continue;
                for (int along = 20; along < MallLayout.CellSize; along += 12)
                {
                    float2 bench = axis == 0 ? new float2(along, 7f) : new float2(7f, along);
                    float2 planter = axis == 0 ? new float2(along + 4.5f, 7f) : new float2(7f, along + 4.5f);
                    TryAdd(ctx, plan, 0, PropKind.Bench, bench, axis == 0 ? 0f : 90f, output);
                    TryAdd(ctx, plan, 0, PropKind.Planter, planter, 0f, output);
                }
            }
        }

        private static void PlaceCars(in Context ctx, in MallLevelPlan plan, List<PropSpawn> output)
        {
            for (int row = 0; row < MallLayout.CellSize / 16; row++)
            for (int side = 0; side < 2; side++)
            for (int bay = 0; bay < 21; bay++)
            {
                uint h = Hash.Get(ctx.Seed, ctx.Info.Cell.x, ctx.Info.Cell.y, Salt + 5000 + ctx.Level * 1000 + row * 64 + side * 32 + bay);
                if (h % 100 >= 30)
                    continue;
                float2 p = new(bay * 3 + 1.5f, row * 16 + (side == 0 ? 4f : 10f));
                TryAdd(ctx, plan, 0, PropKind.Car, p, (h >> 8) % 2 == 0 ? 0f : 180f, output);
            }
        }

        // ───────────────────────────── Проверки ─────────────────────────────

        /// <summary>Добавить, если весь габарит лежит на свободном полу этой комнаты и вне аномалий.</summary>
        private static bool TryAdd(in Context ctx, in MallLevelPlan plan, int room, PropKind kind, float2 local, float yaw, List<PropSpawn> output)
        {
            Vector3 size = PropCatalog.Size(kind);
            bool swap = Mathf.Abs(Mathf.Repeat(yaw, 180f) - 90f) < 1f;
            float2 half = (swap ? new float2(size.z, size.x) : new float2(size.x, size.z)) * 0.5f;
            int2 min = (int2)math.floor(local - half + 0.02f);
            int2 max = (int2)math.floor(local + half - 0.02f);
            if (min.x < 0 || min.y < 0 || max.x >= MallLayout.CellSize || max.y >= MallLayout.CellSize)
                return false;
            for (int z = min.y; z <= max.y; z++)
            for (int x = min.x; x <= max.x; x++)
            {
                int index = MallLevelPlan.Index(x, z);
                if (plan.Tiles[index] != (byte)Tile.Open || plan.RoomIds[index] != room)
                    return false;
            }
            float2 world = ctx.Origin + local;
            if (InAnomaly(ctx, world, math.length(half)))
                return false;
            float4 box = new(local - half, local + half);
            foreach (var other in ctx.Occupied)
                if (box.x < other.z && other.x < box.z && box.y < other.w && other.y < box.w)
                    return false;
            ctx.Occupied.Add(box);
            output.Add(new PropSpawn { Kind = kind, Position = new Vector3(world.x, ctx.FloorY, world.y), Yaw = yaw });
            return true;
        }

        public static bool InAnomaly(in MallCellInfo info, int level, float2 world, float radius)
        {
            float distance = math.distance(world, info.AnomalyCenter);
            switch (info.Anomaly)
            {
                case AnomalyKind.Abyss:
                    if (level == info.AnomalyLevel)
                        return distance < MallAnomalies.AbyssZoneRadius(info.AnomalyRadius) + radius;
                    return level < info.AnomalyLevel && distance < info.AnomalyRadius + MallAnomalies.ShellThickness + radius + 0.5f;
                case AnomalyKind.Pool:
                    if (level == info.AnomalyLevel)
                        return distance < MallAnomalies.PoolZoneRadius(info.AnomalyRadius) + radius;
                    return level == info.AnomalyLevel - 1 && distance < info.AnomalyRadius + radius;
                case AnomalyKind.Cutout:
                    float2 c = info.AnomalyCenter;
                    return level <= info.AnomalyLevel
                        && world.x + radius > c.x && world.x - radius < c.x + WorldConstants.ChunkSize
                        && world.y + radius > c.y && world.y - radius < c.y + WorldConstants.ChunkSize;
                default:
                    return false;
            }
        }

        private static bool InAnomaly(in Context ctx, float2 world, float radius) => InAnomaly(ctx.Info, ctx.Level, world, radius);

        private static bool NearDoor(in MallLevelPlan plan, int2 wall, bool alongX, float reach)
        {
            int span = (int)math.ceil(reach);
            for (int d = -span; d <= span; d++)
            {
                int2 p = wall + (alongX ? new int2(d, 0) : new int2(0, d));
                if (p.x < 0 || p.y < 0 || p.x >= MallLayout.CellSize || p.y >= MallLayout.CellSize)
                    continue;
                Tile t = plan.TileAt(p.x, p.y);
                if (t == Tile.Door || t == Tile.FacadeDoor || t == Tile.CoreDoor)
                    return true;
            }
            return false;
        }

        private static float2 Center(int4 rect) => new((rect.x + rect.z) * 0.5f, (rect.y + rect.w) * 0.5f);
    }
}
