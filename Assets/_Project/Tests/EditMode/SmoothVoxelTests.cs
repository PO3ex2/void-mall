using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using VoidMall.World;

namespace VoidMall.Tests
{
    public sealed class SurfaceNetsTests
    {
        private const ushort Stone = 1;
        private const ushort Mass = 2;

        private NativeArray<MaterialInfo> _materials;
        private ChunkData _air;

        [SetUp]
        public void SetUp()
        {
            _materials = new NativeArray<MaterialInfo>(3, Allocator.Persistent);
            _materials[0] = new MaterialInfo { Kind = MaterialKind.Air };
            _materials[Stone] = new MaterialInfo { Kind = MaterialKind.Block, Flags = MaterialFlags.Solid | MaterialFlags.Opaque };
            _materials[Mass] = new MaterialInfo { Kind = MaterialKind.Smooth, Flags = MaterialFlags.Solid | MaterialFlags.Opaque, TextureLayer = 3 };
            _air = new ChunkData(int3.zero, Cell.Air);
        }

        [TearDown]
        public void TearDown()
        {
            _materials.Dispose();
            _air.Dispose();
        }

        [Test]
        public void SingleDenseCell_GivesSixQuads()
        {
            using var center = new ChunkData(int3.zero, Cell.Air);
            center.Set(WorldConstants.CellIndex(10, 10, 10), new Cell(Mass, 255));
            var mesh = Mesh(center, null);
            Assert.That(mesh.Triangles.Count, Is.EqualTo(12));
            Assert.That(mesh.AllSmooth, Is.True, "Вершины гладкой поверхности помечаются отрицательным слоем.");
        }

        [Test]
        public void SmoothCellNextToBlock_HidesSharedSide()
        {
            using var center = new ChunkData(int3.zero, Cell.Air);
            center.Set(WorldConstants.CellIndex(10, 10, 10), new Cell(Mass, 255));
            center.Set(WorldConstants.CellIndex(11, 10, 10), new Cell(Stone));
            var mesh = Mesh(center, null);
            // Гладкая клетка: 5 четырёхугольников (сторона к блоку — нет). Блок: 5 граней (грань к плотной гладкой скрыта).
            Assert.That(mesh.Triangles.Count, Is.EqualTo(5 * 2 + 5 * 2));
        }

        [Test]
        public void Sphere_IsClosedSurface()
        {
            using var center = new ChunkData(int3.zero, Cell.Air);
            FillSphere(center, int3.zero, new float3(16f, 16f, 16f), 5f);
            var mesh = Mesh(center, null);
            Assert.That(mesh.Triangles.Count, Is.GreaterThan(100));
            Assert.That(CountOpenEdges(mesh.Triangles), Is.EqualTo(0), "Поверхность сферы должна быть замкнутой.");
        }

        [Test]
        public void SphereAcrossChunkBorder_IsSeamless()
        {
            using var west = new ChunkData(int3.zero, Cell.Air);
            using var east = new ChunkData(new int3(1, 0, 0), Cell.Air);
            var sphereCenter = new float3(32f, 16f, 16f);
            FillSphere(west, int3.zero, sphereCenter, 5f);
            FillSphere(east, new int3(1, 0, 0), sphereCenter, 5f);

            var westMesh = Mesh(west, east, eastOfCenter: true);
            var eastMesh = Mesh(east, west, eastOfCenter: false, origin: new float3(32f, 0f, 0f));
            var all = new List<(float3, float3, float3)>(westMesh.Triangles);
            all.AddRange(eastMesh.Triangles);
            Assert.That(westMesh.Triangles.Count, Is.GreaterThan(0));
            Assert.That(eastMesh.Triangles.Count, Is.GreaterThan(0));
            Assert.That(CountOpenEdges(all), Is.EqualTo(0), "Половинки сферы из двух чанков должны сшиваться без щелей.");
        }

        [Test]
        public void SmoothSphereGrownIntoBlocks_IsWatertight()
        {
            using var center = new ChunkData(int3.zero, Cell.Air);
            for (int y = 10; y < 14; y++)
            for (int z = 10; z < 14; z++)
            for (int x = 12; x < 16; x++)
                center.Set(WorldConstants.CellIndex(x, y, z), new Cell(Stone));
            FillSphere(center, int3.zero, new float3(11f, 12f, 12f), 3.2f, onlyAir: true);

            var mesh = Mesh(center, null);
            Assert.That(CountOpenEdges(mesh.Triangles), Is.EqualTo(0), "Стык гладкой поверхности и граней блоков должен быть без щелей.");
        }

        [Test]
        public void DensityRoundTrip()
        {
            Assert.That(Density.IsSolid(Density.FromField(0.05f)), Is.True);
            Assert.That(Density.IsSolid(Density.FromField(-0.05f)), Is.False);
            Assert.That(Density.FromField(1f), Is.EqualTo(255));
            Assert.That(Density.FromField(-1f), Is.EqualTo(0));
        }

        // ── Утилиты ──

        private struct MeshResult
        {
            public List<(float3, float3, float3)> Triangles;
            public bool AllSmooth;
        }

        private static void FillSphere(ChunkData chunk, int3 chunkCoord, float3 center, float radius, bool onlyAir = false)
        {
            for (int y = 0; y < 32; y++)
            for (int z = 0; z < 32; z++)
            for (int x = 0; x < 32; x++)
            {
                float3 p = new float3(chunkCoord * 32 + new int3(x, y, z)) + 0.5f;
                float field = Density.SphereField(p, center, radius);
                if (field <= -1f)
                    continue;
                if (onlyAir && !chunk.Get(WorldConstants.CellIndex(x, y, z)).IsAir)
                    continue;
                byte density = Density.FromField(field);
                chunk.Set(WorldConstants.CellIndex(x, y, z), new Cell(Density.IsSolid(density) ? Mass : (ushort)0, density));
            }
        }

        /// <param name="neighbor">Чанк по оси X (с востока или с запада).</param>
        private MeshResult Mesh(ChunkData center, ChunkData neighbor, bool eastOfCenter = true, float3 origin = default)
        {
            var views = new NativeArray<ChunkView>(27, Allocator.TempJob);
            for (int i = 0; i < 27; i++)
                views[i] = _air.GetView();
            views[13] = center.GetView();
            if (neighbor != null)
                views[eastOfCenter ? 14 : 12] = neighbor.GetView();

            var vertices = new NativeList<ChunkVertex>(Allocator.TempJob);
            var opaque = new NativeList<int>(Allocator.TempJob);
            var transparent = new NativeList<int>(Allocator.TempJob);
            var waterVertices = new NativeList<ChunkVertex>(Allocator.TempJob);
            var waterIndices = new NativeList<int>(Allocator.TempJob);
            BlockShapes.BuildNative(out var shapeVertices, out var shapeRanges, Allocator.TempJob);
            try
            {
                new ChunkMeshJob
                {
                    Materials = _materials,
                    Views = views,
                    ChunkOrigin = (int3)origin,
                    Vertices = vertices,
                    OpaqueIndices = opaque,
                    TransparentIndices = transparent,
                    WaterVertices = waterVertices,
                    WaterIndices = waterIndices,
                    ShapeVertices = shapeVertices,
                    ShapeRanges = shapeRanges,
                }.Run();

                var result = new MeshResult { Triangles = new List<(float3, float3, float3)>(), AllSmooth = true };
                for (int i = 0; i < opaque.Length; i += 3)
                {
                    result.Triangles.Add((
                        vertices[opaque[i]].Position + origin,
                        vertices[opaque[i + 1]].Position + origin,
                        vertices[opaque[i + 2]].Position + origin));
                }
                for (int i = 0; i < vertices.Length; i++)
                    if (vertices[i].Uv.z >= 0f)
                        result.AllSmooth = false;
                return result;
            }
            finally
            {
                views.Dispose();
                vertices.Dispose();
                opaque.Dispose();
                transparent.Dispose();
                waterVertices.Dispose();
                waterIndices.Dispose();
                shapeVertices.Dispose();
                shapeRanges.Dispose();
            }
        }

        /// <summary>Рёбра, которые принадлежат не ровно двум треугольникам (дыры и швы).</summary>
        private static int CountOpenEdges(List<(float3 a, float3 b, float3 c)> triangles)
        {
            var edges = new Dictionary<(long, long), int>();
            foreach (var (a, b, c) in triangles)
            {
                Add(edges, a, b);
                Add(edges, b, c);
                Add(edges, c, a);
            }
            int open = 0;
            foreach (var count in edges.Values)
                if (count != 2)
                    open++;
            return open;
        }

        private static void Add(Dictionary<(long, long), int> edges, float3 a, float3 b)
        {
            long ka = Key(a), kb = Key(b);
            var key = ka < kb ? (ka, kb) : (kb, ka);
            edges.TryGetValue(key, out int count);
            edges[key] = count + 1;
        }

        private static long Key(float3 p)
        {
            int3 q = (int3)math.round(p * 1000f);
            return ((long)(q.x & 0x1FFFFF) << 42) | ((long)(q.y & 0x1FFFFF) << 21) | (long)(q.z & 0x1FFFFF);
        }
    }

    public sealed class StructuralIntegrityTests
    {
        private sealed class FakeWorld : IVoxelAccess
        {
            public readonly Dictionary<int3, Cell> Cells = new();
            public readonly HashSet<int3> Unloaded = new();

            public bool IsLoaded(int3 cell) => !Unloaded.Contains(cell);
            public Cell GetCell(int3 cell) => Cells.TryGetValue(cell, out var value) ? value : Cell.Air;

            public MaterialInfo GetInfo(ushort material) => material == 9
                ? new MaterialInfo { Kind = MaterialKind.Block, Flags = MaterialFlags.Solid | MaterialFlags.Opaque | MaterialFlags.Unbreakable }
                : new MaterialInfo { Kind = MaterialKind.Block, Flags = MaterialFlags.Solid | MaterialFlags.Opaque };

            public void Column(int x, int z, int fromY, int toY)
            {
                for (int y = fromY; y <= toY; y++)
                    Cells[new int3(x, y, z)] = new Cell(1);
            }
        }

        [Test]
        public void ColumnStandingOnGround_IsSupported()
        {
            var world = new FakeWorld();
            world.Column(0, 0, 0, 10);
            var integrity = new StructuralIntegrity(world);
            Assert.That(integrity.TryCollectIsland(new int3(0, 10, 0), new List<int3>()), Is.False);
        }

        [Test]
        public void CutColumn_TopPartCollapses()
        {
            var world = new FakeWorld();
            world.Column(0, 0, 0, 10);
            world.Cells.Remove(new int3(0, 5, 0));
            var islands = new List<List<int3>>();
            new StructuralIntegrity(world).FindUnsupportedIslands(new[] { new int3(0, 5, 0) }, islands);
            Assert.That(islands.Count, Is.EqualTo(1));
            Assert.That(islands[0].Count, Is.EqualTo(5), "Висят клетки 6…10.");
        }

        [Test]
        public void Bridge_HeldByOneEnd_StaysUp()
        {
            var world = new FakeWorld();
            world.Column(0, 0, 0, 5);
            for (int x = 1; x <= 20; x++)
                world.Cells[new int3(x, 5, 0)] = new Cell(1);
            var integrity = new StructuralIntegrity(world);
            Assert.That(integrity.TryCollectIsland(new int3(20, 5, 0), new List<int3>()), Is.False, "Нагрузку не считаем: консоль держится.");
        }

        [Test]
        public void UnbreakableMaterial_IsAnchor()
        {
            var world = new FakeWorld();
            world.Cells[new int3(0, 10, 0)] = new Cell(9);
            world.Cells[new int3(0, 9, 0)] = new Cell(1);
            Assert.That(new StructuralIntegrity(world).TryCollectIsland(new int3(0, 9, 0), new List<int3>()), Is.False);
        }

        [Test]
        public void UnloadedNeighbour_CountsAsSupport()
        {
            var world = new FakeWorld();
            world.Cells[new int3(0, 20, 0)] = new Cell(1);
            world.Unloaded.Add(new int3(1, 20, 0));
            Assert.That(new StructuralIntegrity(world).TryCollectIsland(new int3(0, 20, 0), new List<int3>()), Is.False);
        }

        [Test]
        public void HugeIsland_IsAssumedSupported()
        {
            var world = new FakeWorld();
            for (int x = 0; x < 20; x++)
            for (int z = 0; z < 20; z++)
                world.Cells[new int3(x, 30, z)] = new Cell(1);
            var integrity = new StructuralIntegrity(world, maxIslandCells: 100);
            Assert.That(integrity.TryCollectIsland(new int3(0, 30, 0), new List<int3>()), Is.False);
        }
    }
}
