using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using VoidMall.Game;
using VoidMall.Items;
using VoidMall.Skills;
using VoidMall.Survival;
using VoidMall.World;

namespace VoidMall.Tests
{
    /// <summary>Этап 8Ж: резерв леса, порча и холодильник, снимок игрока для сейва.</summary>
    public sealed class PreSaveTests
    {
        private const ushort Can = 0, Milk = 1, Backpack = 2, Jacket = 3;

        /// <summary>Каталог-заглушка: банка (1×1, стопка 5), молоко (1×2, портится за 24 ч), рюкзак (3×3), куртка (2×2).</summary>
        private sealed class Items : IItemCatalog, IItemIds
        {
            private static readonly string[] Ids = { "item.can", "item.milk", "item.backpack", "item.jacket" };

            public (int width, int height) GridSize(ushort def) => def switch { Milk => (1, 2), Backpack or Jacket => (2, 2), _ => (1, 1) };
            public int MaxStack(ushort def) => def == Can ? 5 : def == Milk ? 2 : 1;
            public float Weight(ushort def) => 0.5f;
            public string IdOf(ushort def) => Ids[def];

            public bool TryGetIndex(string id, out ushort def)
            {
                def = (ushort)System.Array.IndexOf(Ids, id);
                return System.Array.IndexOf(Ids, id) >= 0;
            }

            public ItemStack Create(ushort def, int count)
            {
                var stack = new ItemStack(def, count);
                if (def == Backpack)
                    stack.Contents = new GridInventory(this, 3, 3);
                if (def == Milk)
                    Freshness.Start(stack, 24f);
                return stack;
            }
        }

        private double _now;
        private readonly Items _items = new();

        [SetUp]
        public void SetUp()
        {
            _now = 100.0;
            Freshness.Clock = () => _now;
        }

        [TearDown]
        public void TearDown() => Freshness.Clock = () => 0.0;

        // ───────────────────────────── Лес ─────────────────────────────

        [Test]
        public void Forest_IsReserved_AwayFromSpawn()
        {
            int forest = 0, total = 0;
            for (int z = -30; z < 30; z++)
            for (int x = -30; x < 30; x++)
            {
                total++;
                if (MallPlanner.BiomeOf(7, 1f, new int2(x, z)) == MallBiome.Forest)
                    forest++;
            }
            Assert.That(forest / (float)total, Is.InRange(0.03f, 0.14f), "лес — несколько процентов ячеек");
            for (uint seed = 1; seed < 30; seed++)
            {
                int2 spawn = MallPlanner.SpawnCell(seed, 1f);
                for (int dz = -MallPlanner.ForestSpawnClearance; dz <= MallPlanner.ForestSpawnClearance; dz++)
                for (int dx = -MallPlanner.ForestSpawnClearance; dx <= MallPlanner.ForestSpawnClearance; dx++)
                    Assert.AreNotEqual(MallBiome.Forest, MallPlanner.BiomeOf(seed, 1f, spawn + new int2(dx, dz)), $"seed {seed}: лес у старта");
            }
        }

        // ───────────────────────────── Порча ─────────────────────────────

        [Test]
        public void Food_SpoilsByTime_ColdSlowsIt()
        {
            var milk = _items.Create(Milk, 1);
            Assert.IsFalse(milk.Spoiled);
            Assert.AreEqual(1f, Freshness.Fraction(milk), 1e-4f);
            _now += 12;
            Assert.AreEqual(0.5f, Freshness.Fraction(milk), 1e-4f);

            // В холодильнике оставшиеся 12 ч растягиваются в 48.
            var fridge = new GridInventory(_items, 4, 4) { Cold = true };
            Assert.IsTrue(fridge.TryPlace(milk, 0, 0, false));
            Assert.IsTrue(milk.Chilled);
            _now += 40;
            Assert.IsFalse(milk.Spoiled, "в холоде через 40 ч ещё свежее");
            Assert.AreEqual(8f, Freshness.HoursLeft(milk), 1e-3f);

            // Вынули: 8 ч в холоде — это 2 ч на воздухе.
            fridge.Remove(milk);
            var pockets = new GridInventory(_items, 4, 4);
            Assert.AreEqual(0, pockets.TryAdd(milk));
            Assert.IsFalse(milk.Chilled);
            Assert.AreEqual(2f, Freshness.HoursLeft(milk), 1e-3f);
            _now += 3;
            Assert.IsTrue(milk.Spoiled);

            // Прогрев на плите снимает порчу — срок начинается заново.
            milk.Spoiled = false;
            Assert.IsFalse(milk.Spoiled);
            Assert.AreEqual(24f, Freshness.HoursLeft(milk), 1e-3f);

            var can = _items.Create(Can, 1);
            _now += 10000;
            Assert.IsFalse(can.Spoiled, "консервы не портятся");
        }

        [Test]
        public void Stacks_KeepEarliestSpoilTime_AndSplitCopiesIt()
        {
            var grid = new GridInventory(_items, 4, 4);
            var old = _items.Create(Milk, 1);
            Assert.AreEqual(0, grid.TryAdd(old));
            _now += 10;
            var fresh = _items.Create(Milk, 1);
            Assert.AreEqual(0, grid.TryAdd(fresh));
            Assert.AreEqual(1, grid.Items.Count, "свежее и почти свежее складываются");
            Assert.AreEqual(14f, Freshness.HoursLeft(grid.Items[0].Stack), 1e-3f, "у стака — ранний срок");

            var part = grid.Split(grid.Items[0].Stack, 1);
            Assert.IsNotNull(part);
            Assert.AreEqual(14f, Freshness.HoursLeft(part), 1e-3f, "отделённая часть — с тем же сроком");

            _now += 15;
            Assert.IsTrue(part.Spoiled);
            var another = _items.Create(Milk, 1);
            Assert.IsFalse(GridInventory.CanStack(part, another), "испорченное со свежим не складывается");
        }

        // ───────────────────────────── Снимок игрока ─────────────────────────────

        [Test]
        public void Inventory_RoundTrip_ThroughJson()
        {
            var pockets = new GridInventory(_items, 5, 4);
            var equipment = new Equipment();
            var hotbar = new Hotbar();
            var milk = _items.Create(Milk, 2);
            Assert.IsTrue(pockets.TryPlace(milk, 3, 1, true));
            var cans = _items.Create(Can, 4);
            pockets.TryAdd(cans);
            var backpack = _items.Create(Backpack, 1);
            var inner = _items.Create(Can, 3);
            backpack.Contents.TryPlace(inner, 2, 2, false);
            equipment.Set(EquipSlot.Backpack, EquipLayer.Armor, backpack);
            var jacket = _items.Create(Jacket, 1);
            jacket.Durability = 0.4f;
            equipment.Set(EquipSlot.Torso, EquipLayer.Clothing, jacket);
            hotbar.Assign(2, inner);
            hotbar.Assign(5, milk);
            hotbar.Select(5);
            _now += 6;

            string json = JsonUtility.ToJson(ItemSnapshot.CaptureInventory(pockets, equipment, hotbar, _items));
            var data = JsonUtility.FromJson<InventoryData>(json);

            var pockets2 = new GridInventory(_items, 5, 4);
            var equipment2 = new Equipment();
            var hotbar2 = new Hotbar();
            pockets2.TryAdd(_items.Create(Can, 1)); // старое содержимое заменяется
            var leftovers = ItemSnapshot.RestoreInventory(data, pockets2, equipment2, hotbar2, _items);
            Assert.AreEqual(0, leftovers.Count);

            Assert.AreEqual(2, pockets2.Items.Count);
            Assert.IsTrue(pockets2.TryGetPlacement(pockets2.GetAt(3, 1), out var placed) && placed.Rotated, "место и поворот сохранены");
            var milk2 = placed.Stack;
            Assert.AreEqual(Milk, milk2.Def);
            Assert.AreEqual(2, milk2.Count);
            Assert.AreEqual(18f, Freshness.HoursLeft(milk2), 1e-3f, "срок хранения сохранён");
            Assert.AreEqual(4, pockets2.CountOf(Can));

            var backpack2 = equipment2.Backpack;
            Assert.IsNotNull(backpack2);
            Assert.AreEqual(3, backpack2.Contents.CountOf(Can), "содержимое рюкзака");
            Assert.AreSame(backpack2.Contents.GetAt(2, 2), hotbar2[2], "хотбар ссылается на стак внутри рюкзака");
            Assert.AreSame(milk2, hotbar2[5]);
            Assert.AreEqual(5, hotbar2.Selected);
            Assert.AreEqual(0.4f, equipment2.Get(EquipSlot.Torso, EquipLayer.Clothing).Durability, 1e-4f);
        }

        [Test]
        public void Inventory_SkipsUnknownItems()
        {
            var data = new InventoryData();
            data.Stacks.Add(new SavedStack { Id = 0, Place = ItemSnapshot.Pockets, Item = "item.removed_in_update", Count = 1 });
            data.Stacks.Add(new SavedStack { Id = 1, Place = ItemSnapshot.Pockets, Item = "item.can", Count = 2, X = 1 });
            var pockets = new GridInventory(_items, 4, 4);
            ItemSnapshot.RestoreInventory(data, pockets, new Equipment(), new Hotbar(), _items);
            Assert.AreEqual(2, pockets.CountOf(Can));
            Assert.AreEqual(1, pockets.Items.Count);
        }

        [Test]
        public void Vitals_RoundTrip_ThroughJson()
        {
            var model = new VitalsModel();
            model.ApplyImpact(30f, "тест");
            model.Statuses.Add(new Status { Kind = StatusKind.Imprint, Remaining = 5f, Age = 2f });
            model.Statuses.Add(new Status { Kind = StatusKind.NearDeathTrauma, Stacks = 2 });
            var json = JsonUtility.ToJson(model.Capture());

            var restored = new VitalsModel();
            restored.Restore(JsonUtility.FromJson<VitalsData>(json));
            Assert.AreEqual(model.Health, restored.Health, 1e-3f);
            Assert.AreEqual(model.Calories, restored.Calories, 1e-3f);
            Assert.AreEqual(model.Statuses.Count, restored.Statuses.Count, "удар добавил свои статусы — все на месте");
            for (int i = 0; i < model.Statuses.Count; i++)
            {
                Assert.AreEqual(model.Statuses[i].Kind, restored.Statuses[i].Kind);
                Assert.AreEqual(model.Statuses[i].Remaining, restored.Statuses[i].Remaining);
                Assert.AreEqual(model.Statuses[i].Intensity, restored.Statuses[i].Intensity, 1e-4f);
            }
            Assert.IsTrue(float.IsPositiveInfinity(restored.Statuses.Find(s => s.Kind == StatusKind.NearDeathTrauma).Remaining), "бессрочный статус остаётся бессрочным");
            Assert.AreEqual(2, restored.TraumaStacks);
            Assert.AreEqual(model.MaxSanity, restored.MaxSanity, "«Отпечаток» держит рассудок");
        }

        [Test]
        public void Skills_RoundTrip_ByName()
        {
            var tree = new SkillTree();
            tree.Award(Aspect.Physical, 7.5f);
            tree.SetLevel(SkillId.Athlete, 2);
            var data = JsonUtility.FromJson<SkillsData>(JsonUtility.ToJson(tree.Capture()));
            data.Levels.Add(new SkillLevelData { Name = "RemovedSkill", Level = 3 });

            var restored = new SkillTree();
            restored.Restore(data);
            Assert.AreEqual(7.5f, restored.Points(Aspect.Physical), 1e-4f);
            Assert.AreEqual(7.5f, restored.Earned(Aspect.Physical), 1e-4f);
            Assert.AreEqual(2, restored.Level(SkillId.Athlete));
        }

        [Test]
        public void PlayerSnapshot_HasVersion_AndFillsMissingFields()
        {
            var snapshot = new PlayerSnapshot { Position = new Vector3(1f, 2f, 3f), Yaw = 90f, SpawnProp = ulong.MaxValue.ToString() };
            snapshot.Blueprints.Add("recipe.drill");
            var back = PlayerSnapshot.FromJson(snapshot.ToJson());
            Assert.AreEqual(PlayerSnapshot.CurrentVersion, back.Version);
            Assert.AreEqual(new Vector3(1f, 2f, 3f), back.Position);
            Assert.AreEqual(ulong.MaxValue.ToString(), back.SpawnProp, "ulong — строкой, без потерь");
            Assert.AreEqual("recipe.drill", back.Blueprints[0]);

            // Старый сейв без части полей (их добавили позже) — значения по умолчанию, а не ошибка.
            var old = PlayerSnapshot.FromJson("{\"Version\":1,\"Position\":{\"x\":5,\"y\":6,\"z\":7}}");
            Assert.AreEqual(new Vector3(5f, 6f, 7f), old.Position);
            Assert.IsNotNull(old.Vitals);
            Assert.AreEqual(100f, old.Vitals.Health);
            Assert.IsNotNull(old.Inventory);
            Assert.IsNotNull(old.Skills);
            Assert.AreEqual(0, old.Blueprints.Count);
        }
    }
}
