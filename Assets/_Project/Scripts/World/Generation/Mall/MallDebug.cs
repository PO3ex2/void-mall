using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace VoidMall.World
{
    /// <summary>Отладочные описания мола для HUD и консоли. План последнего запрошенного уровня кэшируется.</summary>
    public static class MallDebug
    {
        private static MallLevelPlan _plan;
        private static int2 _planCell;
        private static int _planLevel = -1;
        private static uint _planSeed;

        /// <summary>«Этаж 2 · Электроника (комната 5) · ячейка (0, 1)».</summary>
        public static string Describe(ChunkManager world, Vector3 position)
        {
            if (world == null || world.Sections == null)
                return string.Empty;
            int2 cell = MallLayout.CellOf(position);
            int level = MallLayout.LevelAtFeet(position.y);
            EnsurePlan(world, cell, level);
            int2 local = (int2)math.floor(new float2(position.x, position.z)) - MallLayout.CellOrigin(cell);
            byte roomId = _plan.RoomIds[MallLevelPlan.Index(local.x, local.y)];
            var room = _plan.Rooms[roomId];
            string place = roomId == 0
                ? MallLayout.Kind(level) switch { LevelKind.Parking => "парковка", LevelKind.Tech => "служебный коридор", _ => "галерея" }
                : room.Has(RoomFlags.Stairwell) ? "лестничная клетка" : world.Sections.Defs[room.Section].DisplayName;
            string flags = room.Has(RoomFlags.Dark) ? ", тёмная" : string.Empty;
            return $"Этаж {MallLayout.LevelName(level)} · {place}{flags} (комната {roomId}, {_plan.TileAt(local.x, local.y)}) · ячейка ({cell.x}, {cell.y}), {world.GetCellInfo(cell).Biome.DisplayName()}";
        }

        private static void EnsurePlan(ChunkManager world, int2 cell, int level)
        {
            if (!_plan.IsCreated)
            {
                Application.quitting -= Release;
                Application.quitting += Release;
                _plan = new MallLevelPlan(Allocator.Persistent);
            }
            if (_planLevel == level && _planCell.Equals(cell) && _planSeed == world.Seed)
                return;
            MallPlanner.BuildLevel(world.Seed, world.GetCellInfo(cell), level, world.Sections.Infos, ref _plan);
            _planCell = cell;
            _planLevel = level;
            _planSeed = world.Seed;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Release();

        private static void Release()
        {
            if (_plan.IsCreated)
                _plan.Dispose();
            _plan = default;
            _planLevel = -1;
        }
    }
}
