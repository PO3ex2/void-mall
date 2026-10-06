using NUnit.Framework;
using UnityEngine;
using VoidMall.Core;
using VoidMall.Items;
using VoidMall.Survival;
using VoidMall.World;

namespace VoidMall.Tests
{
    /// <summary>Этап 8Г: ночной холод, тепло, «Согрет», бхоп «Атлета».</summary>
    public sealed class ColdTests
    {
        private static VitalsEnvironment Air(float temperature, float warmth = 0f) => new() { AirTemperature = temperature, Warmth = warmth, Light = 1f };

        [Test]
        public void NightFactor_ColdAtNight_SmoothEvening()
        {
            Assert.AreEqual(0f, SurvivalEnvironment.NightFactor(12f));
            Assert.AreEqual(1f, SurvivalEnvironment.NightFactor(0f));
            Assert.AreEqual(1f, SurvivalEnvironment.NightFactor(23.5f));
            Assert.That(SurvivalEnvironment.NightFactor(21f), Is.InRange(0.3f, 0.7f), "вечером холодает плавно");
            Assert.That(SurvivalEnvironment.NightFactor(6f), Is.InRange(0.3f, 0.7f), "утром теплеет плавно");
            Assert.AreEqual(3f, SurvivalEnvironment.LevelTemperature(LevelKind.Retail, 1f), 0.01f);
            Assert.AreEqual(21f, SurvivalEnvironment.LevelTemperature(LevelKind.Retail, 0f), 0.01f);
            Assert.AreEqual(-3f, SurvivalEnvironment.LevelTemperature(LevelKind.Parking, 1f), 0.01f);
        }

        [Test]
        public void StarterClothes_SurviveRetailNight_ButGetCold()
        {
            var m = new VitalsModel();
            var night = Air(SurvivalEnvironment.LevelTemperature(LevelKind.Retail, 1f), 12f); // толстовка, джинсы, кроссовки
            for (int i = 0; i < 1200; i++) // ночь — около 20 реальных минут
                m.Tick(1f, 0f, night);
            Assert.Less(m.BodyTemperature, VitalsModel.NormalBodyTemp, "тело остывает");
            Assert.Greater(m.BodyTemperature, 35f, "но до переохлаждения не доходит");

            var naked = new VitalsModel();
            var bare = Air(3f);
            for (int i = 0; i < 300; i++)
                naked.Tick(1f, 0f, bare);
            Assert.Less(naked.BodyTemperature, 35f, "без одежды — переохлаждение за минуты");
        }

        [Test]
        public void Clothes_WarmUpToComfort_ButDoNotOverheat()
        {
            var m = new VitalsModel();
            Assert.AreEqual(VitalsModel.ComfortMax, m.FeltTemperature(Air(21f, 30f)), 0.01f, "куртка днём не перегревает");
            Assert.AreEqual(15f, m.FeltTemperature(Air(3f, 12f)), 0.01f);
            Assert.AreEqual(35f, m.FeltTemperature(Air(35f, 0f)), 0.01f, "жара воздуха остаётся жарой");
        }

        [Test]
        public void HotFood_Warms_ForAWhile()
        {
            var m = new VitalsModel();
            var dish = ScriptableObject.CreateInstance<ItemDef>();
            dish.Category = ItemCategory.Food;
            dish.Calories = 500f;
            dish.Cooked = true;
            m.Consume(dish, false);
            Assert.IsTrue(m.Has(StatusKind.Warmed));
            Assert.AreEqual(3f + 12f + VitalsModel.WarmedBonus, m.FeltTemperature(Air(3f, 12f)), 0.01f);
            m.Tick(0f, VitalsModel.WarmedHours + 0.1f, Air(21f));
            Assert.IsFalse(m.Has(StatusKind.Warmed), "проходит за полтора часа");

            var spoiled = new VitalsModel();
            spoiled.Consume(dish, true);
            Assert.IsFalse(spoiled.Has(StatusKind.Warmed), "испорченное не греет");
            Object.DestroyImmediate(dish);
        }

        [Test]
        public void Cold_MoreCalories_SlowerStamina_Shiver()
        {
            var m = new VitalsModel();
            Assert.AreEqual(1f, m.ColdCaloriesMul(Air(21f)), 0.001f);
            Assert.AreEqual(VitalsModel.ColdCaloriesMax, m.ColdCaloriesMul(Air(-10f)), 0.001f);

            var warm = new VitalsModel();
            var cold = new VitalsModel();
            warm.SetStamina(0f);
            cold.SetStamina(0f);
            cold.SetNeeds(bodyTemperature: 35.5f);
            for (int i = 0; i < 3; i++)
            {
                warm.Tick(1f, 0f, Air(21f));
                cold.Tick(1f, 0f, Air(21f));
            }
            Assert.Less(cold.Stamina, warm.Stamina * 0.75f, "на холоде стамина восстанавливается медленнее");

            var modifiers = MovementModifiers.Default;
            cold.ApplyModifiers(ref modifiers);
            Assert.Greater(modifiers.ShiverAmount, 0f, "дрожь");
            var calm = MovementModifiers.Default;
            warm.ApplyModifiers(ref calm);
            Assert.AreEqual(0f, calm.ShiverAmount);
        }

        [Test]
        public void Bhop_ComesWithAthlete()
        {
            Assert.AreEqual(0f, VitalsModel.BhopSpeedFactor(0), "без навыка — потолок из настроек");
            Assert.AreEqual(1.5f, VitalsModel.BhopSpeedFactor(1), 0.001f);
            Assert.AreEqual(1.75f, VitalsModel.BhopSpeedFactor(2), 0.001f);
            Assert.AreEqual(2f, VitalsModel.BhopSpeedFactor(3), 0.001f);

            var m = new VitalsModel();
            var none = MovementModifiers.Default;
            m.ApplyModifiers(ref none);
            Assert.IsFalse(none.Bhop);

            var perks = VitalsPerks.None;
            perks.BhopLevel = 2;
            m.Perks = perks;
            var athlete = MovementModifiers.Default;
            m.ApplyModifiers(ref athlete);
            Assert.IsTrue(athlete.Bhop);
            Assert.AreEqual(1.75f, athlete.JumpSpeedFactor, 0.001f);
        }

        [Test]
        public void Breath_FasterWhenRunning_DenserInFrost()
        {
            Assert.Less(BreathVapor.Interval(true, 1f), BreathVapor.Interval(false, 1f));
            Assert.Less(BreathVapor.Interval(false, 0.1f), BreathVapor.Interval(false, 1f));
            Assert.Greater(BreathVapor.PuffSize(-10f), BreathVapor.PuffSize(9f));
        }
    }
}
