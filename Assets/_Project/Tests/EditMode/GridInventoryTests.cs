using NUnit.Framework;
using VoidMall.Items;

namespace VoidMall.Tests
{
    public sealed class GridInventoryTests
    {
        private const ushort Can = 0;      // 1×1, стак 5, 0.4 кг
        private const ushort Plank = 1;    // 3×1, стак 10, 1.5 кг
        private const ushort Drill = 2;    // 2×2, не стакается, 3 кг
        private const ushort Nails = 3;    // 1×1, стак 200, 0.01 кг
        private const ushort Pack = 4;     // 2×2, рюкзак, 0.5 кг

        private sealed class Catalog : IItemCatalog
        {
            public (int width, int height) GridSize(ushort def) => def switch
            {
                Plank => (3, 1),
                Drill or Pack => (2, 2),
                _ => (1, 1),
            };

            public int MaxStack(ushort def) => def switch
            {
                Can => 5,
                Plank => 10,
                Nails => 200,
                _ => 1,
            };

            public float Weight(ushort def) => def switch
            {
                Can => 0.4f,
                Plank => 1.5f,
                Drill => 3f,
                Nails => 0.01f,
                _ => 0.5f,
            };
        }

        private static readonly Catalog Items = new();

        [Test]
        public void Place_RespectsBoundsAndOverlap()
        {
            var grid = new GridInventory(Items, 4, 3);
            var drill = new ItemStack(Drill);
            Assert.That(grid.TryPlace(drill, 0, 0, false), Is.True);
            Assert.That(grid.TryPlace(new ItemStack(Drill), 1, 1, false), Is.False, "Пересечение");
            Assert.That(grid.TryPlace(new ItemStack(Drill), 3, 0, false), Is.False, "Выход за край");
            Assert.That(grid.TryPlace(new ItemStack(Drill), 2, 1, false), Is.True);
            Assert.That(grid.GetAt(1, 1), Is.SameAs(drill));
        }

        [Test]
        public void Rotation_SwapsSize()
        {
            var grid = new GridInventory(Items, 2, 3);
            var plank = new ItemStack(Plank);
            Assert.That(grid.CanPlace(plank, 0, 0, false), Is.False, "3×1 не лезет в ширину 2");
            Assert.That(grid.TryPlace(plank, 0, 0, true), Is.True, "Повёрнутая 1×3 лезет");
            Assert.That(grid.GetAt(0, 2), Is.SameAs(plank));
        }

        [Test]
        public void FindSpace_TriesRotation()
        {
            var grid = new GridInventory(Items, 1, 3);
            var plank = new ItemStack(Plank);
            Assert.That(grid.FindSpace(plank, out _, out _, out bool rotated), Is.True);
            Assert.That(rotated, Is.True);
        }

        [Test]
        public void TryAdd_MergesStacksThenFillsFreeSpace()
        {
            var grid = new GridInventory(Items, 2, 1);
            Assert.That(grid.TryAdd(new ItemStack(Can, 3)), Is.EqualTo(0));
            Assert.That(grid.TryAdd(new ItemStack(Can, 4)), Is.EqualTo(0), "2 доложились в стак, 2 — новый стак");
            Assert.That(grid.CountOf(Can), Is.EqualTo(7));
            Assert.That(grid.Items.Count, Is.EqualTo(2));
            var extra = new ItemStack(Can, 5);
            Assert.That(grid.TryAdd(extra), Is.EqualTo(2), "Место кончилось: 3 доложились, 2 остались");
            Assert.That(extra.Count, Is.EqualTo(2));
        }

        [Test]
        public void TryAdd_SplitsOversizedStack()
        {
            var grid = new GridInventory(Items, 3, 1);
            Assert.That(grid.TryAdd(new ItemStack(Can, 12)), Is.EqualTo(0));
            Assert.That(grid.Items.Count, Is.EqualTo(3), "5 + 5 + 2");
        }

        [Test]
        public void TryConsume_TakesFromSeveralStacks()
        {
            var grid = new GridInventory(Items, 3, 1);
            grid.TryAdd(new ItemStack(Can, 12));
            Assert.That(grid.TryConsume(Can, 20), Is.False, "Не хватает — ничего не списывается");
            Assert.That(grid.CountOf(Can), Is.EqualTo(12));
            Assert.That(grid.TryConsume(Can, 8), Is.True);
            Assert.That(grid.CountOf(Can), Is.EqualTo(4));
        }

        [Test]
        public void Split_And_Merge()
        {
            var grid = new GridInventory(Items, 3, 1);
            var cans = new ItemStack(Can, 5);
            grid.TryPlace(cans, 0, 0, false);
            var part = grid.Split(cans, 2);
            Assert.That(part, Is.Not.Null);
            Assert.That(cans.Count, Is.EqualTo(3));
            Assert.That(grid.Merge(part, cans), Is.EqualTo(2));
            Assert.That(cans.Count, Is.EqualTo(5));
            Assert.That(grid.Contains(part), Is.False, "Опустевший стак убран");
        }

        [Test]
        public void Move_KeepsItemWhenTargetBlocked()
        {
            var grid = new GridInventory(Items, 4, 2);
            var drill = new ItemStack(Drill);
            var can = new ItemStack(Can);
            grid.TryPlace(drill, 0, 0, false);
            grid.TryPlace(can, 3, 0, false);
            Assert.That(grid.TryMove(drill, 2, 0, false), Is.False);
            Assert.That(grid.GetAt(0, 0), Is.SameAs(drill));
            Assert.That(grid.TryMove(drill, 1, 0, false), Is.True, "Сдвиг на себя самого допустим");
            Assert.That(grid.GetAt(0, 0), Is.Null);
        }

        [Test]
        public void Weight_IncludesNestedContainers()
        {
            var grid = new GridInventory(Items, 4, 4);
            var pack = new ItemStack(Pack) { Contents = new GridInventory(Items, 3, 3) };
            pack.Contents.TryAdd(new ItemStack(Drill));
            grid.TryAdd(pack);
            grid.TryAdd(new ItemStack(Nails, 100));
            Assert.That(grid.TotalWeight(), Is.EqualTo(0.5f + 3f + 1f).Within(1e-4f));
        }
    }
}
