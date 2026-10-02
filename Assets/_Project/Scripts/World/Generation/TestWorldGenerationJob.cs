using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using VoidMall.Core;

namespace VoidMall.World
{
    /// <summary>Индексы материалов, нужных генератору (резолвятся по ID при старте).</summary>
    public struct TestWorldMaterials
    {
        public ushort Bedrock, Concrete, Carpet, FloorTile, Wallpaper, Drywall, CeilingTile, Glass, Wood, Metal;

        public static TestWorldMaterials Resolve(MaterialRegistry registry) => new()
        {
            Bedrock = registry.IndexOf("block.bedrock"),
            Concrete = registry.IndexOf("block.concrete"),
            Carpet = registry.IndexOf("block.carpet"),
            FloorTile = registry.IndexOf("block.floor_tile"),
            Wallpaper = registry.IndexOf("block.wallpaper"),
            Drywall = registry.IndexOf("block.drywall"),
            CeilingTile = registry.IndexOf("block.ceiling_tile"),
            Glass = registry.IndexOf("block.glass"),
            Wood = registry.IndexOf("block.wood"),
            Metal = registry.IndexOf("block.metal"),
        };
    }

    /// <summary>
    /// Временный генератор этапа 2 (заменится генерацией мола на этапе 4): бетонное основание,
    /// пол на высоте 18, лабиринт комнат 8×8 со стенами из обоев, дверями, стеклом и частичным потолком.
    /// Детерминирован по сиду и координатам.
    /// </summary>
    [BurstCompile]
    public struct TestWorldGenerationJob : IJob
    {
        public const int FloorBlockY = 17;
        public const int WallTopY = 22;
        public const int CeilingY = 23;
        private const int RoomSize = 8;

        public int3 Chunk;
        public uint Seed;
        public TestWorldMaterials M;

        [WriteOnly] public NativeArray<Cell> Cells;

        /// <summary>[0] = 1, если все клетки чанка одинаковые.</summary>
        public NativeArray<byte> IsUniform;

        public void Execute()
        {
            int3 origin = Chunk * WorldConstants.ChunkSize;
            Cell first = default;
            bool uniform = true;

            for (int y = 0; y < WorldConstants.ChunkSize; y++)
            for (int z = 0; z < WorldConstants.ChunkSize; z++)
            for (int x = 0; x < WorldConstants.ChunkSize; x++)
            {
                Cell cell = Generate(origin + new int3(x, y, z));
                int index = WorldConstants.CellIndex(x, y, z);
                Cells[index] = cell;
                if (index == 0)
                    first = cell;
                else if (uniform && cell != first)
                    uniform = false;
            }

            IsUniform[0] = (byte)(uniform ? 1 : 0);
        }

        private Cell Generate(int3 p)
        {
            if (p.y == 0)
                return new Cell(M.Bedrock);
            if (p.y < FloorBlockY)
                return new Cell(M.Concrete);

            int roomX = FloorDiv(p.x, RoomSize);
            int roomZ = FloorDiv(p.z, RoomSize);
            int rx = p.x - roomX * RoomSize;
            int rz = p.z - roomZ * RoomSize;

            if (p.y == FloorBlockY)
                return new Cell(Hash.Get(Seed, roomX, roomZ, 4) % 4 == 0 ? M.FloorTile : M.Carpet);

            if (p.y <= WallTopY)
                return GenerateRoomContent(p.y, roomX, roomZ, rx, rz);

            if (p.y == CeilingY)
                return Hash.Get(Seed, roomX, roomZ, 5) % 3 != 0 ? new Cell(M.CeilingTile) : Cell.Air;

            return Cell.Air;
        }

        private Cell GenerateRoomContent(int y, int roomX, int roomZ, int rx, int rz)
        {
            // Колонны на пересечениях линий сетки.
            if (rx == 0 && rz == 0)
                return new Cell(M.Drywall);

            // Стена вдоль Z по линии rx == 0 (между комнатами roomX-1 и roomX).
            if (rx == 0 && IsWall(roomX, roomZ, axis: 1, rz))
                return new Cell(WallMaterial(roomX, roomZ, 1));

            // Стена вдоль X по линии rz == 0.
            if (rz == 0 && IsWall(roomX, roomZ, axis: 2, rx))
                return new Cell(WallMaterial(roomX, roomZ, 2));

            // Немного «мебели»: деревянные ящики и металлический короб.
            uint props = Hash.Get(Seed, roomX, roomZ, 6);
            if (y == FloorBlockY + 1)
            {
                if (props % 5 == 0 && rz == 5 && (rx == 5 || rx == 6))
                    return new Cell(M.Wood);
                if (props % 7 == 0 && rx == 2 && rz == 6)
                    return new Cell(M.Metal);
            }
            if (y == FloorBlockY + 2 && props % 10 == 0 && rz == 5 && rx == 5)
                return new Cell(M.Wood);

            return Cell.Air;
        }

        private bool IsWall(int roomX, int roomZ, int axis, int along)
        {
            uint h = Hash.Get(Seed, roomX, roomZ, axis);
            if (h % 100 >= 55)
                return false;
            // Дверной проём шириной 2 в середине стены.
            bool hasDoor = (h >> 8) % 3 != 0;
            return !(hasDoor && (along == 3 || along == 4));
        }

        private ushort WallMaterial(int roomX, int roomZ, int axis)
        {
            uint h = Hash.Get(Seed, roomX, roomZ, axis + 10);
            return h % 12 == 0 ? M.Glass : M.Wallpaper;
        }

        private static int FloorDiv(int value, int divisor) => value >= 0 ? value / divisor : (value - divisor + 1) / divisor;
    }
}
