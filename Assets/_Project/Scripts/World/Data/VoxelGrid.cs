using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace VoidMall.World
{
    /// <summary>
    /// Клетки отломанного куска — воксельного тела (этап 8Е) — в его локальных координатах: клетка (x, y, z) занимает
    /// куб [x, x+1) в пространстве тела. Формат клеток как в мире (материал, форма или плотность, ориентация), урон —
    /// разреженно. Все клетки твёрдые. Пределы: MaxCells клеток и MaxSpan по каждой оси (меш строит мешер чанка 32³).
    /// </summary>
    public sealed class VoxelGrid
    {
        public const int MaxCells = 512;
        public const int MaxSpan = WorldConstants.ChunkSize;

        /// <summary>Доля веса клетки с формой (полублок, скос, ступень) от полной.</summary>
        public const float ShapeMassFactor = 0.5f;

        private static readonly int3[] Neighbors =
        {
            new(1, 0, 0), new(-1, 0, 0), new(0, 1, 0), new(0, -1, 0), new(0, 0, 1), new(0, 0, -1),
        };

        private readonly Dictionary<int3, Cell> _cells = new();
        private readonly Dictionary<int3, int> _damage = new();

        /// <summary>Мировая клетка, с которой совпадала локальная (0, 0, 0) при отрыве: текстуры лежат как в стене.</summary>
        public int3 UvOrigin;

        public int Count => _cells.Count;
        public IReadOnlyDictionary<int3, Cell> Cells => _cells;

        /// <summary>Габарит клеток (включительно). У пустой сетки — нули.</summary>
        public int3 Min { get; private set; }
        public int3 Max { get; private set; }
        public int3 Span => Count == 0 ? int3.zero : Max - Min + 1;

        public bool Contains(int3 cell) => _cells.ContainsKey(cell);
        public bool TryGet(int3 cell, out Cell value) => _cells.TryGetValue(cell, out value);
        public Cell Get(int3 cell) => _cells.TryGetValue(cell, out var value) ? value : Cell.Air;

        public void Set(int3 cell, Cell value)
        {
            if (value.IsAir)
            {
                Remove(cell);
                return;
            }
            bool added = !_cells.ContainsKey(cell);
            _cells[cell] = value;
            if (!added)
                return;
            if (_cells.Count == 1)
            {
                Min = Max = cell;
                return;
            }
            Min = math.min(Min, cell);
            Max = math.max(Max, cell);
        }

        public bool Remove(int3 cell)
        {
            if (!_cells.Remove(cell))
                return false;
            _damage.Remove(cell);
            RecalculateBounds();
            return true;
        }

        /// <summary>Можно ли добавить клетку: она свободна и сетка не выйдет за пределы.</summary>
        public bool CanAdd(int3 cell)
        {
            if (_cells.ContainsKey(cell) || _cells.Count >= MaxCells)
                return false;
            if (_cells.Count == 0)
                return true;
            int3 span = math.max(Max, cell) - math.min(Min, cell) + 1;
            return math.all(span <= MaxSpan);
        }

        /// <summary>Поместится ли набор клеток в одно тело.</summary>
        public static bool Fits(IReadOnlyList<int3> cells)
        {
            if (cells.Count == 0 || cells.Count > MaxCells)
                return false;
            int3 min = cells[0], max = cells[0];
            foreach (var cell in cells)
            {
                min = math.min(min, cell);
                max = math.max(max, cell);
            }
            return math.all(max - min + 1 <= MaxSpan);
        }

        public int GetDamage(int3 cell) => _damage.TryGetValue(cell, out int damage) ? damage : 0;

        public void SetDamage(int3 cell, int damage)
        {
            if (damage <= 0)
                _damage.Remove(cell);
            else if (_cells.ContainsKey(cell))
                _damage[cell] = damage;
        }

        /// <summary>Связные части (6-связность). Одна часть — тело цело.</summary>
        public void Components(List<List<int3>> result)
        {
            result.Clear();
            var visited = new HashSet<int3>();
            var queue = new Queue<int3>();
            foreach (var start in _cells.Keys)
            {
                if (!visited.Add(start))
                    continue;
                var part = new List<int3>();
                queue.Enqueue(start);
                while (queue.Count > 0)
                {
                    int3 cell = queue.Dequeue();
                    part.Add(cell);
                    foreach (var offset in Neighbors)
                    {
                        int3 next = cell + offset;
                        if (_cells.ContainsKey(next) && visited.Add(next))
                            queue.Enqueue(next);
                    }
                }
                result.Add(part);
            }
        }

        /// <summary>Перенести клетки (с уроном) в новую сетку с тем же началом текстур — отколовшаяся часть.</summary>
        public VoxelGrid Extract(IReadOnlyList<int3> cells)
        {
            var part = new VoxelGrid { UvOrigin = UvOrigin };
            foreach (var cell in cells)
            {
                if (!_cells.TryGetValue(cell, out var value))
                    continue;
                part.Set(cell, value);
                part.SetDamage(cell, GetDamage(cell));
                _cells.Remove(cell);
                _damage.Remove(cell);
            }
            RecalculateBounds();
            return part;
        }

        /// <summary>Вес клетки: гладкая — по плотности, с формой — половина полной.</summary>
        public static float CellMass(Cell cell, MaterialKind kind, float fullMass) => kind switch
        {
            MaterialKind.Smooth => fullMass * math.max(0.25f, cell.Value / 255f),
            _ => cell.Value != Cell.FullCube ? fullMass * ShapeMassFactor : fullMass,
        };

        /// <summary>Масса и центр масс (в локальных координатах тела) по весу клеток.</summary>
        public float Mass(Func<Cell, float> massOf, out float3 centerOfMass)
        {
            float mass = 0f;
            float3 sum = float3.zero;
            foreach (var (cell, value) in _cells)
            {
                float m = massOf(value);
                mass += m;
                sum += ((float3)cell + 0.5f) * m;
            }
            centerOfMass = mass > 0f ? sum / mass : float3.zero;
            return mass;
        }

        /// <summary>Самый частый материал (по нему тело называется).</summary>
        public ushort DominantMaterial()
        {
            var counts = new Dictionary<ushort, int>();
            ushort best = 0;
            int bestCount = 0;
            foreach (var value in _cells.Values)
            {
                counts.TryGetValue(value.Material, out int count);
                counts[value.Material] = ++count;
                if (count > bestCount)
                {
                    bestCount = count;
                    best = value.Material;
                }
            }
            return best;
        }

        /// <summary>
        /// Коллизия: клетки, склеенные жадно в коробки (min — угол, size — размер в клетках). Сначала тянем по X, потом
        /// по Z, потом по Y. Клетки с формой — целыми коробками.
        /// </summary>
        public void BuildBoxes(List<(int3 min, int3 size)> boxes)
        {
            boxes.Clear();
            if (_cells.Count == 0)
                return;
            int3 span = Span;
            var used = new bool[span.x * span.y * span.z];
            int Index(int3 p) => p.x + p.z * span.x + p.y * span.x * span.z;
            bool Free(int3 local) => !used[Index(local)] && _cells.ContainsKey(local + Min);

            for (int y = 0; y < span.y; y++)
            for (int z = 0; z < span.z; z++)
            for (int x = 0; x < span.x; x++)
            {
                var start = new int3(x, y, z);
                if (!Free(start))
                    continue;
                int sx = 1;
                while (x + sx < span.x && Free(new int3(x + sx, y, z)))
                    sx++;
                int sz = 1;
                while (z + sz < span.z && RowFree(x, sx, y, z + sz))
                    sz++;
                int sy = 1;
                while (y + sy < span.y && SlabFree(x, sx, y + sy, z, sz))
                    sy++;
                for (int dy = 0; dy < sy; dy++)
                for (int dz = 0; dz < sz; dz++)
                for (int dx = 0; dx < sx; dx++)
                    used[Index(new int3(x + dx, y + dy, z + dz))] = true;
                boxes.Add((start + Min, new int3(sx, sy, sz)));
            }

            bool RowFree(int x0, int sx, int y0, int z0)
            {
                for (int dx = 0; dx < sx; dx++)
                    if (!Free(new int3(x0 + dx, y0, z0)))
                        return false;
                return true;
            }

            bool SlabFree(int x0, int sx, int y0, int z0, int sz)
            {
                for (int dz = 0; dz < sz; dz++)
                    if (!RowFree(x0, sx, y0, z0 + dz))
                        return false;
                return true;
            }
        }

        private void RecalculateBounds()
        {
            bool first = true;
            foreach (var cell in _cells.Keys)
            {
                if (first)
                {
                    Min = Max = cell;
                    first = false;
                    continue;
                }
                Min = math.min(Min, cell);
                Max = math.max(Max, cell);
            }
            if (first)
                Min = Max = int3.zero;
        }
    }
}
