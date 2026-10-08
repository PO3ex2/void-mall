using Unity.Mathematics;

namespace VoidMall.World
{
    /// <summary>Геометрия мира. 1 клетка = 1 м. Чанк 32³, колонна — 4 чанка по вертикали.</summary>
    public static class WorldConstants
    {
        public const int ChunkSize = 32;
        public const int ChunkShift = 5;
        public const int ChunkMask = ChunkSize - 1;
        public const int ChunkVolume = ChunkSize * ChunkSize * ChunkSize;

        public const int ChunksPerColumn = 4;

        /// <summary>
        /// Версия генератора мола. Поднимать, когда меняется то, что генерируется (сохранённые колонны останутся старыми,
        /// новые — по-новому: на границе возможны швы, SaveSystem предупредит).
        /// </summary>
        public const int GeneratorVersion = 1;
        public const int WorldHeight = ChunkSize * ChunksPerColumn;

        /// <summary>Индекс клетки в чанке: X быстрее всего, потом Z, потом Y.</summary>
        public static int CellIndex(int x, int y, int z) => x + (z << ChunkShift) + (y << (ChunkShift * 2));

        public static int CellIndex(int3 local) => CellIndex(local.x, local.y, local.z);

        public static int3 CellToChunk(int3 cell) => cell >> ChunkShift;

        public static int3 CellToLocal(int3 cell) => cell & ChunkMask;

        public static int2 CellToColumn(int3 cell) => new(cell.x >> ChunkShift, cell.z >> ChunkShift);

        public static int2 ChunkToColumn(int3 chunk) => new(chunk.x, chunk.z);

        public static bool IsInsideWorldHeight(int y) => y >= 0 && y < WorldHeight;
    }
}
