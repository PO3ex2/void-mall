using System.Collections.Generic;
using Unity.Mathematics;

namespace VoidMall.World
{
    /// <summary>Доступ к клеткам для проверки целостности (мир или тестовая заглушка).</summary>
    public interface IVoxelAccess
    {
        bool IsLoaded(int3 cell);
        Cell GetCell(int3 cell);
        MaterialInfo GetInfo(ushort material);
    }

    /// <summary>
    /// Упрощённая структурная целостность: кусок твёрдых клеток, не связанный с опорой, обрушивается.
    /// Опора — нижний слой мира (y = 0), неразрушимые материалы и незагруженные клетки (не знаем — считаем, что держится).
    /// Поиск идёт «сначала вниз» (очередь по высоте): от стены до фундамента — десятки шагов, а не тысячи.
    /// Если кусок больше бюджета, считаем, что он держится (защита от дорогих проверок огромных конструкций).
    /// </summary>
    public sealed class StructuralIntegrity
    {
        public const int DefaultMaxIslandCells = 4096;

        private static readonly int3[] Neighbors =
        {
            new(0, -1, 0), new(1, 0, 0), new(-1, 0, 0), new(0, 0, 1), new(0, 0, -1), new(0, 1, 0),
        };

        private readonly IVoxelAccess _world;
        private readonly int _maxIslandCells;
        private readonly HashSet<int3> _visited = new();
        private readonly HashSet<int3> _supported = new();
        private readonly HashSet<int3> _collapsed = new();
        private readonly List<int3>[] _buckets = new List<int3>[WorldConstants.WorldHeight];
        private int _queued;
        private int _lowestBucket;

        public StructuralIntegrity(IVoxelAccess world, int maxIslandCells = DefaultMaxIslandCells)
        {
            _world = world;
            _maxIslandCells = maxIslandCells;
            for (int i = 0; i < _buckets.Length; i++)
                _buckets[i] = new List<int3>();
        }

        /// <summary>
        /// Проверить соседей удалённых клеток. Каждый найденный остров без опоры добавляется в islands.
        /// </summary>
        public void FindUnsupportedIslands(IReadOnlyList<int3> removedCells, List<List<int3>> islands)
        {
            _supported.Clear();
            _collapsed.Clear();
            foreach (var removed in removedCells)
            {
                foreach (var offset in Neighbors)
                {
                    int3 seed = removed + offset;
                    if (_supported.Contains(seed) || _collapsed.Contains(seed) || !IsSolid(seed))
                        continue;
                    var island = new List<int3>();
                    if (TryCollectIsland(seed, island))
                    {
                        islands.Add(island);
                        foreach (var cell in island)
                            _collapsed.Add(cell);
                    }
                }
            }
        }

        /// <returns>true — кусок без опоры (island заполнен), false — держится.</returns>
        public bool TryCollectIsland(int3 seed, List<int3> island)
        {
            island.Clear();
            _visited.Clear();
            ResetQueue();

            _visited.Add(seed);
            Push(seed);
            while (TryPop(out int3 cell))
            {
                island.Add(cell);
                if (island.Count > _maxIslandCells || cell.y <= 0 || IsAnchor(cell))
                    return MarkSupported();

                foreach (var offset in Neighbors)
                {
                    int3 next = cell + offset;
                    if (!WorldConstants.IsInsideWorldHeight(next.y) || _visited.Contains(next))
                        continue;
                    if (!_world.IsLoaded(next) || _supported.Contains(next))
                        return MarkSupported();
                    if (!IsSolid(next))
                        continue;
                    _visited.Add(next);
                    Push(next);
                }
            }
            return true;
        }

        private bool MarkSupported()
        {
            // Всё, что мы обошли, связано с опорой — повторно не проверяем.
            foreach (var cell in _visited)
                _supported.Add(cell);
            return false;
        }

        private bool IsSolid(int3 cell) => WorldConstants.IsInsideWorldHeight(cell.y) && !_world.GetCell(cell).IsAir;

        private bool IsAnchor(int3 cell)
        {
            var value = _world.GetCell(cell);
            return !value.IsAir && (_world.GetInfo(value.Material).Flags & MaterialFlags.Unbreakable) != 0;
        }

        private void ResetQueue()
        {
            if (_queued > 0)
                foreach (var bucket in _buckets)
                    bucket.Clear();
            _queued = 0;
            _lowestBucket = int.MaxValue;
        }

        private void Push(int3 cell)
        {
            _buckets[cell.y].Add(cell);
            _queued++;
            if (cell.y < _lowestBucket)
                _lowestBucket = cell.y;
        }

        private bool TryPop(out int3 cell)
        {
            while (_queued > 0)
            {
                var bucket = _buckets[_lowestBucket];
                if (bucket.Count == 0)
                {
                    _lowestBucket++;
                    continue;
                }
                cell = bucket[^1];
                bucket.RemoveAt(bucket.Count - 1);
                _queued--;
                return true;
            }
            cell = default;
            return false;
        }
    }
}
