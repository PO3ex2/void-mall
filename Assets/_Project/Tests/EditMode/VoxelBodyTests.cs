using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;
using VoidMall.World;

namespace VoidMall.Tests
{
    /// <summary>Этап 8Е: отломанные куски — сетка клеток, пределы, разделение, коробки коллизии, масса, откол.</summary>
    public sealed class VoxelBodyTests
    {
        private static readonly Cell Concrete = new(2);
        private static readonly Cell Wood = new(10);

        private static VoxelGrid Bar(int length, Cell cell)
        {
            var grid = new VoxelGrid();
            for (int x = 0; x < length; x++)
                grid.Set(new int3(x, 0, 0), cell);
            return grid;
        }

        [Test]
        public void Grid_TracksBounds_OnAddAndRemove()
        {
            var grid = Bar(3, Concrete);
            grid.Set(new int3(-1, 2, 0), Concrete);
            Assert.AreEqual(new int3(-1, 0, 0), grid.Min);
            Assert.AreEqual(new int3(2, 2, 0), grid.Max);
            Assert.AreEqual(new int3(4, 3, 1), grid.Span);
            grid.Remove(new int3(-1, 2, 0));
            Assert.AreEqual(new int3(0, 0, 0), grid.Min);
            Assert.AreEqual(new int3(3, 1, 1), grid.Span);
            grid.Set(new int3(1, 0, 0), Cell.Air);
            Assert.AreEqual(2, grid.Count, "воздух убирает клетку");
        }

        [Test]
        public void Grid_RespectsLimits()
        {
            var bar = Bar(VoxelGrid.MaxSpan, Concrete);
            Assert.IsFalse(bar.CanAdd(new int3(VoxelGrid.MaxSpan, 0, 0)), "шире 32 клеток нельзя");
            Assert.IsFalse(bar.CanAdd(new int3(-1, 0, 0)));
            Assert.IsTrue(bar.CanAdd(new int3(0, 1, 0)));
            Assert.IsFalse(bar.CanAdd(new int3(0, 0, 0)), "занятая клетка");

            var cube = new VoxelGrid();
            for (int y = 0; y < 8; y++)
            for (int z = 0; z < 8; z++)
            for (int x = 0; x < 8; x++)
                cube.Set(new int3(x, y, z), Concrete);
            Assert.AreEqual(VoxelGrid.MaxCells, cube.Count);
            Assert.IsFalse(cube.CanAdd(new int3(8, 0, 0)), "больше 512 клеток нельзя");
        }

        [Test]
        public void Fits_DecidesBodyOrDebris()
        {
            var small = new List<int3> { new(0, 0, 0), new(1, 0, 0) };
            Assert.IsTrue(VoxelGrid.Fits(small));
            var wide = new List<int3> { new(0, 0, 0), new(VoxelGrid.MaxSpan, 0, 0) };
            Assert.IsFalse(VoxelGrid.Fits(wide), "слишком длинный — обломки");
            var many = new List<int3>();
            for (int i = 0; i < VoxelGrid.MaxCells + 1; i++)
                many.Add(new int3(i % 20, i / 400, (i / 20) % 20));
            Assert.IsFalse(VoxelGrid.Fits(many), "больше 512 клеток — обломки");
            Assert.IsFalse(VoxelGrid.Fits(new List<int3>()));
        }

        [Test]
        public void Components_SplitWhenMiddleRemoved()
        {
            var grid = Bar(5, Concrete);
            var parts = new List<List<int3>>();
            grid.Components(parts);
            Assert.AreEqual(1, parts.Count);

            grid.Remove(new int3(2, 0, 0));
            grid.Components(parts);
            Assert.AreEqual(2, parts.Count);
            Assert.AreEqual(2, parts[0].Count);
            Assert.AreEqual(2, parts[1].Count);

            // Диагональ — не связь (6-связность).
            var diagonal = new VoxelGrid();
            diagonal.Set(new int3(0, 0, 0), Concrete);
            diagonal.Set(new int3(1, 1, 0), Concrete);
            diagonal.Components(parts);
            Assert.AreEqual(2, parts.Count);
        }

        [Test]
        public void Extract_MovesCellsWithDamage()
        {
            var grid = Bar(4, Concrete);
            grid.UvOrigin = new int3(100, 20, 5);
            grid.SetDamage(new int3(3, 0, 0), 77);
            var part = grid.Extract(new List<int3> { new(2, 0, 0), new(3, 0, 0) });
            Assert.AreEqual(2, grid.Count);
            Assert.AreEqual(2, part.Count);
            Assert.AreEqual(77, part.GetDamage(new int3(3, 0, 0)));
            Assert.AreEqual(grid.UvOrigin, part.UvOrigin, "текстуры части лежат как в стене");
            Assert.AreEqual(new int3(1, 0, 0), grid.Max);
            Assert.IsFalse(grid.Contains(new int3(3, 0, 0)));
        }

        [Test]
        public void Boxes_MergeGreedily_AndCoverEveryCell()
        {
            var boxes = new List<(int3 min, int3 size)>();
            var cube = new VoxelGrid();
            for (int y = 0; y < 3; y++)
            for (int z = 0; z < 2; z++)
            for (int x = 0; x < 4; x++)
                cube.Set(new int3(x, y, z) + new int3(-5, 7, 1), Concrete);
            cube.BuildBoxes(boxes);
            Assert.AreEqual(1, boxes.Count, "сплошной брус — одна коробка");
            Assert.AreEqual(new int3(-5, 7, 1), boxes[0].min);
            Assert.AreEqual(new int3(4, 3, 2), boxes[0].size);

            // L-форма и дырка: коробки не пересекаются и покрывают все клетки ровно один раз.
            var shape = new VoxelGrid();
            for (int x = 0; x < 5; x++)
                shape.Set(new int3(x, 0, 0), Concrete);
            for (int y = 1; y < 4; y++)
                shape.Set(new int3(0, y, 0), Concrete);
            for (int z = 1; z < 3; z++)
            for (int x = 2; x < 5; x++)
                shape.Set(new int3(x, 0, z), Concrete);
            shape.Remove(new int3(3, 0, 1));
            shape.BuildBoxes(boxes);
            var covered = new HashSet<int3>();
            foreach (var (min, size) in boxes)
                for (int y = 0; y < size.y; y++)
                for (int z = 0; z < size.z; z++)
                for (int x = 0; x < size.x; x++)
                {
                    var cell = min + new int3(x, y, z);
                    Assert.IsTrue(shape.Contains(cell), $"коробка вылезла за клетки: {cell}");
                    Assert.IsTrue(covered.Add(cell), $"клетка в двух коробках: {cell}");
                }
            Assert.AreEqual(shape.Count, covered.Count);
            Assert.Less(boxes.Count, shape.Count, "склеивает, а не коробка на клетку");
        }

        [Test]
        public void Mass_ByMaterial_ShapeAndDensity()
        {
            Assert.AreEqual(45f, VoxelGrid.CellMass(Concrete, MaterialKind.Block, 45f));
            Assert.AreEqual(45f * VoxelGrid.ShapeMassFactor, VoxelGrid.CellMass(new Cell(2, 1, 0), MaterialKind.Block, 45f), 1e-4f, "полублок легче");
            Assert.AreEqual(45f, VoxelGrid.CellMass(new Cell(20, 255), MaterialKind.Smooth, 45f), 1e-4f);
            Assert.Less(VoxelGrid.CellMass(new Cell(20, 140), MaterialKind.Smooth, 45f), 45f, "гладкий — по плотности");

            // Бетон и дерево: центр масс смещён к бетону.
            var grid = new VoxelGrid();
            grid.Set(new int3(0, 0, 0), Concrete);
            grid.Set(new int3(1, 0, 0), Wood);
            float mass = grid.Mass(c => c.Material == Concrete.Material ? 45f : 8f, out float3 center);
            Assert.AreEqual(53f, mass, 1e-4f);
            Assert.Less(center.x, 1f, "центр масс ближе к бетону");
            Assert.AreEqual(Concrete.Material, Bar(1, Concrete).DominantMaterial());
        }

        [Test]
        public void Chip_OnlyOnHardImpacts_AndCapped()
        {
            Assert.AreEqual(0, VoxelBodyRules.ChipCount(450f, VoxelBodyRules.ChipSpeed - 0.1f, 1200), "медленный удар ничего не ломает");
            Assert.AreEqual(0, VoxelBodyRules.ChipCount(45f, 7.7f, 1200), "один бетонный блок с потолка не крошится");
            int concrete = VoxelBodyRules.ChipCount(450f, 7.7f, 1200);
            int wood = VoxelBodyRules.ChipCount(450f, 7.7f, 400);
            Assert.Greater(concrete, 0, "кусок стены с потолка откалывается");
            Assert.Greater(wood, concrete, "мягкий материал крошится сильнее");
            Assert.Greater(VoxelBodyRules.ChipCount(450f, 12f, 1200), concrete, "чем быстрее, тем больше");
            Assert.AreEqual(VoxelBodyRules.MaxChipCells, VoxelBodyRules.ChipCount(20000f, 15f, 300));
        }

        [Test]
        public void ImpactDamage_LikeProps_HeavyCrushes()
        {
            Assert.AreEqual(0f, VoxelBodyRules.ImpactDamage(100f, 3f, crush: true), "медленно — без урона");
            Assert.AreEqual(VoxelBodyRules.CrushDamage, VoxelBodyRules.ImpactDamage(900f, 5f, crush: true), "тяжёлый кусок давит насмерть");
            Assert.AreEqual(80f, VoxelBodyRules.ImpactDamage(900f, 5f, crush: false), "проп — не больше 80");
            Assert.AreEqual(20f * 6f * 0.08f, VoxelBodyRules.ImpactDamage(20f, 6f, crush: true), 1e-4f);
        }

        [Test]
        public void Floats_WoodYes_ConcreteNo()
        {
            Assert.IsTrue(VoxelBodyRules.Floats(8f * 10, 10));
            Assert.IsFalse(VoxelBodyRules.Floats(45f * 10, 10));
            Assert.IsFalse(VoxelBodyRules.Floats(0f, 0));
        }
    }
}
