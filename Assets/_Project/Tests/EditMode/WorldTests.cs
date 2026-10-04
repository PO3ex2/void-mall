using NUnit.Framework;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using VoidMall.World;

namespace VoidMall.Tests
{
    public sealed class ChunkDataTests
    {
        [Test]
        public void UniformChunk_ExpandsOnWrite_AndCompressesBack()
        {
            using var chunk = new ChunkData(int3.zero, Cell.Air);
            Assert.That(chunk.IsUniform, Is.True);

            int index = WorldConstants.CellIndex(3, 4, 5);
            Assert.That(chunk.Set(index, new Cell(2)), Is.True);
            Assert.That(chunk.IsUniform, Is.False);
            Assert.That(chunk.Get(index).Material, Is.EqualTo(2));
            Assert.That(chunk.Get(0).IsAir, Is.True);

            chunk.Set(index, Cell.Air);
            chunk.Compress();
            Assert.That(chunk.IsUniform, Is.True);
        }

        [Test]
        public void WritingSameValue_ReportsNoChange()
        {
            using var chunk = new ChunkData(int3.zero, new Cell(1));
            Assert.That(chunk.Set(10, new Cell(1)), Is.False);
            Assert.That(chunk.IsUniform, Is.True);
        }

        [Test]
        public void Damage_IsSparse()
        {
            using var chunk = new ChunkData(int3.zero, new Cell(1));
            chunk.SetDamage(7, 50);
            Assert.That(chunk.GetDamage(7), Is.EqualTo(50));
            Assert.That(chunk.GetDamage(8), Is.EqualTo(0));
            chunk.SetDamage(7, 0);
            Assert.That(chunk.GetDamage(7), Is.EqualTo(0));
        }

        [Test]
        public void CoordinateHelpers_HandleNegativeCells()
        {
            Assert.That(WorldConstants.CellToChunk(new int3(-1, 0, 31)), Is.EqualTo(new int3(-1, 0, 0)));
            Assert.That(WorldConstants.CellToLocal(new int3(-1, 0, 32)), Is.EqualTo(new int3(31, 0, 0)));
            Assert.That(WorldConstants.CellToColumn(new int3(-33, 5, 64)), Is.EqualTo(new int2(-2, 2)));
        }
    }

    public sealed class ChunkMeshJobTests
    {
        private const ushort Stone = 1;
        private const ushort Glass = 2;

        private NativeArray<MaterialInfo> _materials;
        private ChunkData _air;

        [SetUp]
        public void SetUp()
        {
            _materials = new NativeArray<MaterialInfo>(3, Allocator.Persistent);
            _materials[0] = new MaterialInfo { Kind = MaterialKind.Air };
            _materials[Stone] = new MaterialInfo { Kind = MaterialKind.Block, Flags = MaterialFlags.Solid | MaterialFlags.Opaque, TextureLayer = 0 };
            _materials[Glass] = new MaterialInfo { Kind = MaterialKind.Block, Flags = MaterialFlags.Solid | MaterialFlags.Transparent, TextureLayer = 1 };
            _air = new ChunkData(int3.zero, Cell.Air);
        }

        [TearDown]
        public void TearDown()
        {
            _materials.Dispose();
            _air.Dispose();
        }

        [Test]
        public void SingleBlock_HasSixFaces()
        {
            using var center = new ChunkData(int3.zero, Cell.Air);
            center.Set(WorldConstants.CellIndex(5, 5, 5), new Cell(Stone));
            var (faces, transparentFaces) = Mesh(center);
            Assert.That(faces, Is.EqualTo(6));
            Assert.That(transparentFaces, Is.EqualTo(0));
        }

        [Test]
        public void AdjacentBlocks_HideSharedFaces()
        {
            using var center = new ChunkData(int3.zero, Cell.Air);
            center.Set(WorldConstants.CellIndex(5, 5, 5), new Cell(Stone));
            center.Set(WorldConstants.CellIndex(6, 5, 5), new Cell(Stone));
            Assert.That(Mesh(center).faces, Is.EqualTo(10));
        }

        [Test]
        public void Glass_DoesNotHideNeighbour_ButHidesItsOwnInnerFaces()
        {
            using var center = new ChunkData(int3.zero, Cell.Air);
            center.Set(WorldConstants.CellIndex(5, 5, 5), new Cell(Stone));
            center.Set(WorldConstants.CellIndex(6, 5, 5), new Cell(Glass));
            center.Set(WorldConstants.CellIndex(7, 5, 5), new Cell(Glass));
            var (faces, transparentFaces) = Mesh(center);
            // Камень — 6 граней (стекло его не закрывает). Стекло 1: скрыты грани к камню и к стеклу 2 → 4; стекло 2: скрыта грань к стеклу 1 → 5.
            Assert.That(faces, Is.EqualTo(6 + 4 + 5));
            Assert.That(transparentFaces, Is.EqualTo(9));
        }

        [Test]
        public void ChunkBorder_IsSeamless()
        {
            using var center = new ChunkData(int3.zero, Cell.Air);
            using var east = new ChunkData(new int3(1, 0, 0), Cell.Air);
            center.Set(WorldConstants.CellIndex(31, 5, 5), new Cell(Stone));
            east.Set(WorldConstants.CellIndex(0, 5, 5), new Cell(Stone));
            Assert.That(Mesh(center, east).faces, Is.EqualTo(5), "Грань к соседнему чанку с блоком не должна рисоваться.");
        }

        [Test]
        public void SolidUniformChunk_SurroundedBySolid_IsEmpty()
        {
            using var solid = new ChunkData(int3.zero, new Cell(Stone));
            var views = new NativeArray<ChunkView>(27, Allocator.TempJob);
            for (int i = 0; i < 27; i++)
                views[i] = solid.GetView();
            Assert.That(Run(views).faces, Is.EqualTo(0));
        }

        private (int faces, int transparentFaces) Mesh(ChunkData center, ChunkData east = null)
        {
            var views = new NativeArray<ChunkView>(27, Allocator.TempJob);
            for (int i = 0; i < 27; i++)
                views[i] = _air.GetView();
            views[13] = center.GetView();
            if (east != null)
                views[14] = east.GetView();
            return Run(views);
        }

        private (int faces, int transparentFaces) Run(NativeArray<ChunkView> views)
        {
            var vertices = new NativeList<ChunkVertex>(Allocator.TempJob);
            var opaque = new NativeList<int>(Allocator.TempJob);
            var transparent = new NativeList<int>(Allocator.TempJob);
            BlockShapes.BuildNative(out var shapeVertices, out var shapeRanges, Allocator.TempJob);
            try
            {
                new ChunkMeshJob
                {
                    Materials = _materials,
                    Views = views,
                    ChunkOrigin = int3.zero,
                    Vertices = vertices,
                    OpaqueIndices = opaque,
                    TransparentIndices = transparent,
                    ShapeVertices = shapeVertices,
                    ShapeRanges = shapeRanges,
                }.Run();
                Assert.That(vertices.Length, Is.EqualTo((opaque.Length + transparent.Length) / 6 * 4));
                return ((opaque.Length + transparent.Length) / 6, transparent.Length / 6);
            }
            finally
            {
                views.Dispose();
                vertices.Dispose();
                opaque.Dispose();
                transparent.Dispose();
                shapeVertices.Dispose();
                shapeRanges.Dispose();
            }
        }
    }
}
