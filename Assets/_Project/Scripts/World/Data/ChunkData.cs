using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;

namespace VoidMall.World
{
    /// <summary>
    /// Клетки одного чанка 32³. Однородный чанк (весь воздух, весь бетон) хранит одну клетку вместо 32768 —
    /// так пустые чанки над крышей мола почти ничего не стоят.
    /// </summary>
    public sealed unsafe class ChunkData : IDisposable
    {
        private NativeArray<Cell> _cells;
        private Dictionary<int, int> _damage;

        public int3 Coord { get; }
        public bool IsUniform { get; private set; }

        /// <summary>Чанк менялся после генерации (на этапе 9 такие чанки пишутся в сейв).</summary>
        public bool IsModified { get; set; }

        public ChunkData(int3 coord, Cell uniformValue)
        {
            Coord = coord;
            SetUniform(uniformValue);
        }

        /// <summary>Чанк под генерацию: полный массив, заполняется джобом.</summary>
        public static ChunkData CreateForGeneration(int3 coord)
        {
            var chunk = new ChunkData(coord);
            chunk._cells = new NativeArray<Cell>(WorldConstants.ChunkVolume, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            chunk.IsUniform = false;
            return chunk;
        }

        private ChunkData(int3 coord) => Coord = coord;

        /// <summary>Массив для записи джобом генерации (только пока чанк не однородный).</summary>
        public NativeArray<Cell> Cells => _cells;

        public Cell Get(int index) => IsUniform ? _cells[0] : _cells[index];

        public Cell Get(int3 local) => Get(WorldConstants.CellIndex(local));

        /// <returns>true, если значение изменилось.</returns>
        public bool Set(int index, Cell value)
        {
            if (IsUniform)
            {
                if (_cells[0] == value)
                    return false;
                Expand();
            }
            if (_cells[index] == value)
                return false;
            _cells[index] = value;
            return true;
        }

        public void SetUniform(Cell value)
        {
            if (_cells.IsCreated)
                _cells.Dispose();
            _cells = new NativeArray<Cell>(1, Allocator.Persistent);
            _cells[0] = value;
            IsUniform = true;
        }

        /// <summary>Если все клетки одинаковые — сжимаем до одной.</summary>
        public void Compress()
        {
            if (IsUniform)
                return;
            Cell first = _cells[0];
            for (int i = 1; i < _cells.Length; i++)
            {
                if (_cells[i] != first)
                    return;
            }
            SetUniform(first);
        }

        private void Expand()
        {
            Cell value = _cells[0];
            _cells.Dispose();
            _cells = new NativeArray<Cell>(WorldConstants.ChunkVolume, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            UnsafeUtility.MemCpyReplicate(_cells.GetUnsafePtr(), &value, sizeof(Cell), WorldConstants.ChunkVolume);
            IsUniform = false;
        }

        public ChunkView GetView() => new()
        {
            Cells = (Cell*)_cells.GetUnsafeReadOnlyPtr(),
            Uniform = IsUniform ? 1 : 0,
        };

        // ── Повреждения (разреженно) ──

        public int GetDamage(int index) => _damage != null && _damage.TryGetValue(index, out int damage) ? damage : 0;

        /// <summary>Все повреждённые клетки (индекс → урон) — для сейва.</summary>
        public IEnumerable<KeyValuePair<int, int>> Damage => _damage ?? (IEnumerable<KeyValuePair<int, int>>)Array.Empty<KeyValuePair<int, int>>();

        public void SetDamage(int index, int damage)
        {
            if (damage <= 0)
            {
                _damage?.Remove(index);
                return;
            }
            _damage ??= new Dictionary<int, int>();
            _damage[index] = damage;
        }

        public void Dispose()
        {
            if (_cells.IsCreated)
                _cells.Dispose();
            _damage = null;
        }
    }

    /// <summary>Указатель на клетки чанка для Burst-джобов. Однородный чанк — один элемент.</summary>
    public unsafe struct ChunkView
    {
        [NativeDisableUnsafePtrRestriction] public Cell* Cells;
        public int Uniform;

        public Cell Get(int index) => Uniform != 0 ? Cells[0] : Cells[index];
    }
}
