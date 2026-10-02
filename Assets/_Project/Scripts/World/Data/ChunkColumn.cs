using System;
using Unity.Mathematics;

namespace VoidMall.World
{
    public enum ColumnState
    {
        /// <summary>Генерация в процессе (джобы запущены).</summary>
        Generating,

        /// <summary>Данные готовы, можно мешить (если соседи тоже готовы).</summary>
        Generated,
    }

    /// <summary>Вертикальная колонна из 4 чанков — единица стриминга и (позже) сохранения.</summary>
    public sealed class ChunkColumn : IDisposable
    {
        public readonly int2 Coord;
        public readonly ChunkData[] Chunks = new ChunkData[WorldConstants.ChunksPerColumn];
        public readonly ChunkRenderer[] Renderers = new ChunkRenderer[WorldConstants.ChunksPerColumn];

        /// <summary>Нужно перестроить меш чанка.</summary>
        public readonly bool[] MeshDirty = new bool[WorldConstants.ChunksPerColumn];

        /// <summary>Меш чанка в работе (джоб запущен).</summary>
        public readonly bool[] MeshInFlight = new bool[WorldConstants.ChunksPerColumn];

        /// <summary>Меш построен хотя бы раз.</summary>
        public readonly bool[] HasMesh = new bool[WorldConstants.ChunksPerColumn];

        /// <summary>Коллайдер соответствует текущему мешу (или чанк пустой и коллайдер не нужен).</summary>
        public readonly bool[] CollisionReady = new bool[WorldConstants.ChunksPerColumn];

        /// <summary>Растёт при каждой заливке меша — отбрасываем устаревшие результаты запекания коллайдера.</summary>
        public readonly int[] MeshVersion = new int[WorldConstants.ChunksPerColumn];

        public ColumnState State;
        public int PendingGenerationJobs;

        public ChunkColumn(int2 coord)
        {
            Coord = coord;
            for (int i = 0; i < WorldConstants.ChunksPerColumn; i++)
                MeshDirty[i] = true;
        }

        public bool IsModified
        {
            get
            {
                foreach (var chunk in Chunks)
                    if (chunk != null && chunk.IsModified)
                        return true;
                return false;
            }
        }

        public void MarkAllDirty()
        {
            for (int i = 0; i < WorldConstants.ChunksPerColumn; i++)
                MeshDirty[i] = true;
        }

        public void Dispose()
        {
            for (int i = 0; i < Chunks.Length; i++)
            {
                Chunks[i]?.Dispose();
                Chunks[i] = null;
            }
        }
    }
}
