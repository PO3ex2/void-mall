using System.Collections.Generic;
using NUnit.Framework;
using VoidMall.Core;
using VoidMall.Crafting;
using VoidMall.Items;
using VoidMall.Skills;

namespace VoidMall.Tests
{
    public sealed class CraftingTests
    {
        private static CraftJob Job(float duration, string title = "job") => new() { Title = title, Duration = duration };

        private sealed class Catalog : IItemCatalog
        {
            public (int width, int height) GridSize(ushort def) => (1, 1);
            public int MaxStack(ushort def) => 10;
            public float Weight(ushort def) => 0.1f;
        }

        [Test]
        public void Queue_RunsFirstJobAndCarriesOverTime()
        {
            var queue = new CraftQueue();
            queue.Enqueue(Job(2f, "a"));
            queue.Enqueue(Job(3f, "b"));
            var done = new List<CraftJob>();
            queue.Tick(1f, done);
            Assert.AreEqual(0, done.Count);
            Assert.AreEqual(0.5f, queue.Current.Progress, 1e-4f);
            queue.Tick(2f, done); // 1 с до конца первой, 1 с переходит ко второй
            Assert.AreEqual(1, done.Count);
            Assert.AreEqual("a", done[0].Title);
            Assert.AreEqual("b", queue.Current.Title);
            Assert.AreEqual(1f, queue.Current.Elapsed, 1e-4f);
        }

        [Test]
        public void Queue_CapacityAndCancel()
        {
            var queue = new CraftQueue();
            for (int i = 0; i < CraftQueue.Capacity; i++)
                Assert.IsTrue(queue.Enqueue(Job(1f)));
            Assert.IsTrue(queue.IsFull);
            Assert.IsFalse(queue.Enqueue(Job(1f)));
            var cancelled = queue.Cancel(3);
            Assert.IsNotNull(cancelled);
            Assert.AreEqual(CraftQueue.Capacity - 1, queue.Count);
            Assert.IsNull(queue.Cancel(42));
        }

        [Test]
        public void Queue_BlockedUntilDeliveryPossible()
        {
            var queue = new CraftQueue();
            queue.Enqueue(Job(1f));
            var done = new List<CraftJob>();
            bool room = false;
            queue.Tick(5f, done, _ => room);
            Assert.AreEqual(0, done.Count);
            Assert.IsTrue(queue.Blocked);
            Assert.AreEqual(1f, queue.Current.Progress);
            room = true;
            queue.Tick(0.1f, done, _ => room);
            Assert.AreEqual(1, done.Count);
            Assert.IsTrue(queue.IsEmpty);
        }

        [Test]
        public void Station_DeliverAllOrNothing()
        {
            var station = new StationState(1, CraftStation.Workbench, "Верстак", 1f, false, new Catalog());
            var job = new CraftJob();
            for (int i = 0; i < StationState.OutputWidth * StationState.OutputHeight; i++)
                job.Results.Add(new ItemStack((ushort)i, 10));
            Assert.IsTrue(station.TryDeliver(job));
            var extra = new CraftJob();
            extra.Results.Add(new ItemStack(999, 1));
            Assert.IsFalse(station.TryDeliver(extra), "выдача заполнена — ничего не кладём");
            Assert.AreEqual(StationState.OutputWidth * StationState.OutputHeight, station.Output.Items.Count);
        }

        [Test]
        public void Skills_CostRequirementsAndReset()
        {
            var tree = new SkillTree();
            Assert.IsNotNull(tree.WhyCannotLearn(SkillId.Craftsman), "нет очков");
            tree.Award(Aspect.Mental, 3.5f);
            Assert.IsTrue(tree.TryLearn(SkillId.Craftsman));
            Assert.AreEqual(1, tree.Level(SkillId.Craftsman));
            Assert.AreEqual(0.5f, tree.Points(Aspect.Mental), 1e-4f);

            // Техник 1 — без требований, Техник 2 — нужен Мастеровой 1 (есть).
            tree.Award(Aspect.Mental, 9f);
            Assert.IsTrue(tree.TryLearn(SkillId.Technician));
            Assert.IsTrue(tree.TryLearn(SkillId.Technician));
            Assert.AreEqual(2, tree.Level(SkillId.Technician));

            // Прораб требует Плотника 1.
            tree.Award(Aspect.Construction, 10f);
            StringAssert.Contains("Плотник", tree.WhyCannotLearn(SkillId.Foreman));

            tree.Reset();
            Assert.AreEqual(0, tree.Level(SkillId.Technician));
            Assert.AreEqual(12.5f, tree.Points(Aspect.Mental), 1e-4f, "очки за уровни вернулись");
        }

        [Test]
        public void Skills_MaxLevel()
        {
            var tree = new SkillTree();
            tree.Award(Aspect.Physical, 100f);
            for (int i = 0; i < 3; i++)
                Assert.IsTrue(tree.TryLearn(SkillId.Loader));
            Assert.IsFalse(tree.TryLearn(SkillId.Loader));
            Assert.AreEqual(100f - 3f - 6f - 10f, tree.Points(Aspect.Physical), 1e-4f);
        }
    }
}
