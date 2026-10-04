using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;
using VoidMall.World;

namespace VoidMall.Tests
{
    public sealed class FunctionalBlockTests
    {
        [Test]
        public void Offset_PackRoundTrip()
        {
            for (int x = 0; x < 8; x++)
            for (int y = 0; y < 8; y++)
            for (int z = 0; z < 4; z++)
                Assert.AreEqual(new int3(x, y, z), FunctionalCells.UnpackOffset(FunctionalCells.PackOffset(new int3(x, y, z))));
        }

        [Test]
        public void Meta_FlagsIndependent()
        {
            byte meta = FunctionalCells.Meta(3, open: true, locked: false, side: true);
            Assert.AreEqual(3, FunctionalCells.Yaw(meta));
            Assert.IsTrue(FunctionalCells.IsOpen(meta));
            Assert.IsFalse(FunctionalCells.IsLocked(meta));
            Assert.IsTrue(FunctionalCells.OpenSide(meta));
        }

        [Test]
        public void AnchorOf_EveryPartEveryYaw()
        {
            var anchor = new int3(10, 20, 30);
            var size = new int3(2, 3, 1);
            for (int yaw = 0; yaw < 4; yaw++)
            {
                byte meta = FunctionalCells.Meta(yaw, locked: true);
                for (int y = 0; y < size.y; y++)
                for (int x = 0; x < size.x; x++)
                {
                    var local = new int3(x, y, 0);
                    var cell = FunctionalCells.Part(7, local, meta);
                    int3 world = FunctionalCells.CellOf(anchor, local, yaw);
                    Assert.AreEqual(anchor, FunctionalCells.AnchorOf(world, cell), $"yaw {yaw}, part {local}");
                    // Состояние — только в якоре, поворот — во всех клетках.
                    Assert.AreEqual(math.all(local == 0), FunctionalCells.IsLocked(cell.Meta));
                    Assert.AreEqual(yaw, FunctionalCells.Yaw(cell.Meta));
                }
            }
        }

        [Test]
        public void Footprint_RotatesWidth()
        {
            var cells = new List<int3>();
            FunctionalCells.Footprint(int3.zero, new int3(2, 1, 1), 1, cells);
            CollectionAssert.AreEquivalent(new[] { new int3(0, 0, 0), new int3(0, 0, -1) }, cells);
            FunctionalCells.Footprint(int3.zero, new int3(1, 3, 1), 2, cells);
            CollectionAssert.AreEquivalent(new[] { new int3(0, 0, 0), new int3(0, 1, 0), new int3(0, 2, 0) }, cells);
        }

        [Test]
        public void Shapes_AllOrientationsValid()
        {
            for (int s = 1; s < (int)BlockShape.Count; s++)
            for (int o = 0; o < BlockShapes.Orientations; o++)
            {
                var tris = BlockShapes.Triangles((BlockShape)s, o);
                Assert.Greater(tris.Count, 0, $"{(BlockShape)s} o{o}");
                Assert.AreEqual(0, tris.Count % 3);
                for (int i = 0; i < tris.Count; i += 3)
                {
                    float3 a = tris[i].Position, b = tris[i + 1].Position, c = tris[i + 2].Position;
                    Assert.IsTrue(math.all(a >= -1e-4f) && math.all(a <= 1f + 1e-4f), $"{(BlockShape)s} o{o}: вершина вне клетки {a}");
                    // Лицевая сторона — по нормали (как у граней в мешере).
                    float facing = math.dot(math.cross(b - a, c - a), tris[i].Normal);
                    Assert.Greater(facing, 0f, $"{(BlockShape)s} o{o}: треугольник {i / 3} смотрит внутрь");
                }
            }
        }

        [Test]
        public void Shapes_CullFaces()
        {
            // Полублок: низ закрывает клетку целиком (−Y), верх — нет. Перевёрнутый — наоборот.
            Assert.IsTrue(HasCull(BlockShape.Slab, 0, 3));
            Assert.IsFalse(HasCull(BlockShape.Slab, 0, 2));
            Assert.IsTrue(HasCull(BlockShape.Slab, BlockShapes.Meta(1, 0), 2));
            // Скос: высокая сторона +Z; повёрнутый на четверть — +X.
            Assert.IsTrue(HasCull(BlockShape.Slope, 0, 4));
            Assert.IsTrue(HasCull(BlockShape.Slope, BlockShapes.Meta(0, 1), 0));
            // Наклон: полублок «верхом» на +X лежит на стенке −X.
            Assert.IsTrue(HasCull(BlockShape.Slab, BlockShapes.Meta(2, 0), 1));
            // Столб нигде не закрывает грань целиком, кроме низа и верха.
            Assert.IsFalse(HasCull(BlockShape.Post, 0, 3));
        }

        private static bool HasCull(BlockShape shape, int orientation, int direction)
        {
            foreach (var v in BlockShapes.Triangles(shape, orientation))
                if (v.Cull == direction)
                    return true;
            return false;
        }
    }
}
