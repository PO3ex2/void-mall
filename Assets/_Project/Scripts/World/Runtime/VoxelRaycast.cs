using Unity.Mathematics;
using UnityEngine;

namespace VoidMall.World
{
    public struct VoxelHit
    {
        public int3 Cell;

        /// <summary>Нормаль грани, в которую попали (куда ставить новый блок). Ноль, если луч начался внутри блока.</summary>
        public int3 Normal;

        public float Distance;
        public Cell Value;
    }

    /// <summary>Луч по сетке клеток (Amanatides–Woo): точный, не зависит от коллайдеров и готовности мешей.</summary>
    public static class VoxelRaycast
    {
        /// <summary>Луч до первой твёрдой клетки: вода пропускается (сквозь неё целятся и ломают).</summary>
        public static bool Raycast(this ChunkManager world, Vector3 origin, Vector3 direction, float maxDistance, out VoxelHit hit) =>
            Cast(world, origin, direction, maxDistance, false, out hit);

        /// <summary>Луч до первой клетки воды (ведро); упёрся в твёрдое раньше — воды нет.</summary>
        public static bool RaycastLiquid(this ChunkManager world, Vector3 origin, Vector3 direction, float maxDistance, out VoxelHit hit) =>
            Cast(world, origin, direction, maxDistance, true, out hit) && world.GetInfo(hit.Value.Material).IsLiquid;

        private static bool Cast(ChunkManager world, Vector3 origin, Vector3 direction, float maxDistance, bool stopAtLiquid, out VoxelHit hit)
        {
            hit = default;
            float3 o = origin;
            float3 d = math.normalizesafe((float3)direction);
            if (math.all(d == 0f))
                return false;

            int3 cell = (int3)math.floor(o);
            int3 step = (int3)math.sign(d);
            float3 tDelta = new(
                d.x != 0f ? math.abs(1f / d.x) : float.PositiveInfinity,
                d.y != 0f ? math.abs(1f / d.y) : float.PositiveInfinity,
                d.z != 0f ? math.abs(1f / d.z) : float.PositiveInfinity);
            float3 tMax = new(
                Boundary(o.x, cell.x, d.x),
                Boundary(o.y, cell.y, d.y),
                Boundary(o.z, cell.z, d.z));

            int3 normal = int3.zero;
            float t = 0f;
            while (t <= maxDistance)
            {
                Cell value = world.GetCell(cell);
                if (!value.IsAir && !IsOpenFunctional(world, cell, value) && (stopAtLiquid || !world.GetInfo(value.Material).IsLiquid))
                {
                    hit = new VoxelHit { Cell = cell, Normal = normal, Distance = t, Value = value };
                    return true;
                }

                if (tMax.x < tMax.y && tMax.x < tMax.z)
                {
                    t = tMax.x;
                    tMax.x += tDelta.x;
                    cell.x += step.x;
                    normal = new int3(-step.x, 0, 0);
                }
                else if (tMax.y < tMax.z)
                {
                    t = tMax.y;
                    tMax.y += tDelta.y;
                    cell.y += step.y;
                    normal = new int3(0, -step.y, 0);
                }
                else
                {
                    t = tMax.z;
                    tMax.z += tDelta.z;
                    cell.z += step.z;
                    normal = new int3(0, 0, -step.z);
                }
            }
            return false;
        }

        /// <summary>Клетка открытой двери или ворот — луч проходит насквозь (в проём можно целиться).</summary>
        private static bool IsOpenFunctional(ChunkManager world, int3 cell, Cell value)
        {
            var info = world.GetInfo(value.Material);
            if (info.Kind != MaterialKind.Functional)
                return false;
            int3 anchorCell = FunctionalCells.AnchorOf(cell, value);
            // Автоматическая дверь открыта по близости игрока — у каждого клиента своё.
            if (info.Function == FunctionalKind.SlidingDoor)
                return FunctionalBlocks.Instance != null && FunctionalBlocks.Instance.IsOpenLocally(anchorCell);
            Cell anchor = world.GetCell(anchorCell);
            return anchor.Material == value.Material && FunctionalCells.IsOpen(anchor.Meta);
        }

        private static float Boundary(float origin, int cell, float direction)
        {
            if (direction > 0f)
                return (cell + 1 - origin) / direction;
            if (direction < 0f)
                return (origin - cell) / -direction;
            return float.PositiveInfinity;
        }
    }
}
