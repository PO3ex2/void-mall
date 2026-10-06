using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using VoidMall.Items;
using VoidMall.Survival;
using VoidMall.World;

namespace VoidMall.Tests
{
    /// <summary>Этап 8Д: водные чанки, воздух под водой, «Мокрый», сырая вода.</summary>
    public sealed class WaterTests
    {
        private static VitalsEnvironment Air(float temperature, float warmth = 0f) => new() { AirTemperature = temperature, Warmth = warmth, Light = 1f };

        [Test]
        public void Flooded_IsRare_AndNeverAtSpawn()
        {
            int flooded = 0, inverted = 0, total = 0;
            for (int z = -30; z < 30; z++)
            for (int x = -30; x < 30; x++)
            {
                var info = MallPlanner.GetCellInfo(5, 1f, new int2(x, z));
                total++;
                if (info.Anomaly == AnomalyKind.Flooded)
                {
                    flooded++;
                    Assert.IsTrue(MallPlanner.InFlooded(info, info.AnomalyCenter + 1f));
                }
                if (info.Anomaly == AnomalyKind.Inverted)
                    inverted++;
            }
            Assert.Greater(flooded, 0);
            Assert.Less(flooded / (float)total, 0.06f, "водный чанк — редкость");
            for (uint seed = 1; seed < 40; seed++)
                Assert.AreNotEqual(AnomalyKind.Flooded, MallPlanner.GetCellInfo(seed, 1f, MallPlanner.SpawnCell(seed, 1f)).Anomaly);
        }

        [Test]
        public void Underwater_AirRunsOut_ThenDrowning()
        {
            var m = new VitalsModel();
            var env = Air(21f);
            env.HeadUnderwater = true;
            env.InWater = true;
            for (int i = 0; i < 30; i++)
                m.Tick(1f, 0f, env);
            Assert.AreEqual(VitalsModel.MaxAir - 30f, m.Air, 0.01f);
            Assert.AreEqual(VitalsModel.BaseMaxHealth, m.Health, 0.01f, "пока есть воздух — не тонешь");
            for (int i = 0; i < 15; i++)
                m.Tick(1f, 0f, env);
            Assert.AreEqual(0f, m.Air);
            Assert.Less(m.Health, VitalsModel.BaseMaxHealth - 40f, "без воздуха — урон");

            var surface = Air(21f);
            m.Tick(1f, 0f, surface);
            Assert.Greater(m.Air, 10f, "над водой воздух быстро возвращается");
        }

        [Test]
        public void Wet_ClothesStopWarming_DryFasterAtFire()
        {
            var m = new VitalsModel();
            var water = Air(3f, 12f);
            water.InWater = true;
            m.Tick(1f, 0f, water);
            Assert.IsTrue(m.Has(StatusKind.Wet));
            Assert.AreEqual(3f + 12f * VitalsModel.WetWarmthMul, m.FeltTemperature(Air(3f, 12f)), 0.01f, "мокрая одежда почти не греет");

            var away = new VitalsModel();
            away.Tick(1f, 0f, water);
            var fire = Air(20f);
            fire.HeatNearby = 20f;
            m.Tick(0f, 0.3f, fire);
            away.Tick(0f, 0.3f, Air(20f));
            Assert.IsFalse(m.Has(StatusKind.Wet), "у огня высох за 0.3 ч (в 4 раза быстрее)");
            Assert.IsTrue(away.Has(StatusKind.Wet), "без огня ещё мокрый");
        }

        [Test]
        public void RawWater_SometimesPoisons()
        {
            var raw = ScriptableObject.CreateInstance<ItemDef>();
            raw.Category = ItemCategory.Drink;
            raw.Water = 1000f;
            raw.PoisonChance = 0.35f;
            int poisoned = 0;
            for (int i = 0; i < 200; i++)
            {
                var m = new VitalsModel(i);
                m.Consume(raw, false);
                if (m.Has(StatusKind.Poisoning))
                    poisoned++;
            }
            Assert.That(poisoned, Is.InRange(40, 100), "примерно треть");
            Object.DestroyImmediate(raw);
        }
    }
}
