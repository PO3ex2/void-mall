using NUnit.Framework;
using UnityEngine;
using VoidMall.Core;
using VoidMall.Items;
using VoidMall.Survival;

namespace VoidMall.Tests
{
    public sealed class VitalsModelTests
    {
        private static VitalsEnvironment Comfort => new() { AirTemperature = 22f, Light = 1f };

        private static ItemDef Food(float calories, float fat, float protein, float carbs, float water = 0f)
        {
            var def = ScriptableObject.CreateInstance<ItemDef>();
            def.Category = ItemCategory.Food;
            def.Calories = calories;
            def.Fat = fat;
            def.Protein = protein;
            def.Carbs = carbs;
            def.Water = water;
            return def;
        }

        private static ItemDef Medicine(MedicalTreatment treats, float heal = 0f)
        {
            var def = ScriptableObject.CreateInstance<ItemDef>();
            def.Category = ItemCategory.Medicine;
            def.Treats = treats;
            def.Heal = heal;
            return def;
        }

        [Test]
        public void Needs_DrainPerGameHour()
        {
            var m = new VitalsModel();
            m.Tick(0f, 10f, Comfort);
            Assert.AreEqual(2000f - VitalsModel.CaloriesPerHour * 10f, m.Calories, 0.01f);
            Assert.AreEqual(2000f - VitalsModel.WaterPerHour * 10f, m.Water, 0.01f);
            Assert.AreEqual(100f - VitalsModel.RestPerHour * 10f, m.Rest, 0.01f);
        }

        [Test]
        public void Sleep_RestoresRestAndSanity()
        {
            var m = new VitalsModel();
            m.SetNeeds(rest: 20f, sanity: 30f);
            var env = Comfort;
            env.Sleeping = true;
            m.Tick(0f, 2f, env);
            Assert.AreEqual(20f + VitalsModel.SleepRestPerHour * 2f, m.Rest, 0.01f);
            Assert.AreEqual(54f, m.Sanity, 0.01f);
        }

        [Test]
        public void FloorSleep_SlowAndCapped()
        {
            var m = new VitalsModel();
            m.SetNeeds(rest: 20f);
            var env = Comfort;
            env.Sleeping = true;
            env.SleepQuality = VitalsModel.FloorSleepQuality;
            env.SleepRestCap = VitalsModel.FloorRestCap;
            m.Tick(0f, 2f, env);
            Assert.AreEqual(20f + VitalsModel.SleepRestPerHour * 0.5f * 2f, m.Rest, 0.01f);
            m.Tick(0f, 10f, env);
            Assert.AreEqual(VitalsModel.FloorRestCap, m.Rest, 0.01f);
        }

        [Test]
        public void Unrested_LowersStaminaAndDrainsFaster()
        {
            var m = new VitalsModel();
            m.Add(StatusKind.Unrested, hours: VitalsModel.UnrestedHours);
            Assert.AreEqual(85f, m.MaxStamina, 0.01f);
            m.Tick(0f, 2f, Comfort);
            Assert.AreEqual(100f - VitalsModel.RestPerHour * 1.3f * 2f, m.Rest, 0.01f);
            var dark = new VitalsEnvironment { AirTemperature = 22f, Light = 0f };
            m.SetNeeds(sanity: 100f);
            m.Tick(10f, 0f, dark);
            Assert.AreEqual(100f - 0.12f * 1.3f * 10f, m.Sanity, 0.01f);
        }

        [Test]
        public void Diet_BalancedAndMonotonous()
        {
            var m = new VitalsModel();
            Assert.IsNull(m.DietBalanced, "ничего не ели — оценки нет");
            // Сбалансированно: жиры 20 г (180 ккал), белки 30 г (120), углеводы 100 г (400) → 26/17/57%.
            m.Consume(Food(700f, 20f, 30f, 100f), false);
            Assert.AreEqual(true, m.DietBalanced);

            var sugar = new VitalsModel();
            sugar.Consume(Food(600f, 0f, 0f, 150f), false);
            Assert.AreEqual(false, sugar.DietBalanced);
        }

        [Test]
        public void Diet_ForgetsAfterDay()
        {
            var m = new VitalsModel();
            m.Consume(Food(700f, 20f, 30f, 100f), false);
            m.Tick(0f, 25f, Comfort);
            Assert.IsNull(m.DietBalanced);
        }

        [Test]
        public void SpoiledFood_HalfCaloriesAndPoisoning()
        {
            var m = new VitalsModel();
            m.SetNeeds(calories: 1000f);
            m.Consume(Food(400f, 10f, 10f, 60f), true);
            Assert.AreEqual(1200f, m.Calories, 0.01f);
            Assert.IsTrue(m.Has(StatusKind.Poisoning));
        }

        [Test]
        public void Water_FlushesPoisoning()
        {
            var m = new VitalsModel();
            m.Consume(Food(400f, 10f, 10f, 60f), true);
            var water = ScriptableObject.CreateInstance<ItemDef>();
            water.Category = ItemCategory.Drink;
            water.Water = 500f;
            m.Consume(water, false);
            Assert.AreEqual(0.5f, m.Find(StatusKind.Poisoning).Remaining, 0.01f);
            m.Consume(water, false);
            Assert.IsFalse(m.Has(StatusKind.Poisoning));
        }

        [Test]
        public void Bandage_HeavyToLightToNone()
        {
            var m = new VitalsModel();
            m.Add(StatusKind.BleedingHeavy);
            var bandage = Medicine(MedicalTreatment.Bleeding);
            Assert.IsNull(m.WhyUseless(bandage));
            m.Consume(bandage, false);
            Assert.IsFalse(m.Has(StatusKind.BleedingHeavy));
            Assert.IsTrue(m.Has(StatusKind.BleedingLight));
            m.Consume(bandage, false);
            Assert.IsFalse(m.Has(StatusKind.BleedingLight));
            Assert.IsNotNull(m.WhyUseless(bandage), "без кровотечения бинт не тратится");
        }

        [Test]
        public void Bleeding_DrainsHealthAndBlocksRegen()
        {
            var m = new VitalsModel();
            m.Add(StatusKind.BleedingHeavy);
            m.Tick(10f, 0f, Comfort);
            Assert.AreEqual(95f, m.Health, 0.01f);
        }

        [Test]
        public void Splint_FixesLegFracture()
        {
            var m = new VitalsModel();
            m.Add(StatusKind.FractureLeg);
            var free = MovementModifiers.Default;
            m.ApplyModifiers(ref free);
            Assert.IsFalse(free.CanSprint);

            m.Consume(Medicine(MedicalTreatment.Fracture), false);
            var leg = m.Find(StatusKind.FractureLeg);
            Assert.IsTrue(leg.Splinted);
            Assert.AreEqual(48f, leg.Remaining, 0.01f);
            var splinted = MovementModifiers.Default;
            m.ApplyModifiers(ref splinted);
            Assert.Greater(splinted.SpeedMul, free.SpeedMul);
        }

        [Test]
        public void Fall_DamagesAndKillsFromHeight()
        {
            var soft = new VitalsModel();
            soft.ApplyFall(MovementRules.FallDamageSpeed - 1f);
            Assert.AreEqual(100f, soft.Health);

            var hard = new VitalsModel();
            hard.ApplyFall(MovementRules.FallDamageSpeed + 2f);
            Assert.Less(hard.Health, 100f);
            Assert.IsTrue(hard.Has(StatusKind.Pain));

            var deadly = new VitalsModel();
            deadly.ApplyFall(40f);
            Assert.IsTrue(deadly.IsDead);
            Assert.IsNotEmpty(deadly.DeathReason);
        }

        [Test]
        public void God_KeepsAlive()
        {
            var m = new VitalsModel { God = true };
            m.Hurt(500f, "тест");
            Assert.IsFalse(m.IsDead);
            m.SetHealth(0f, "глитч");
            Assert.IsFalse(m.IsDead);
        }

        [Test]
        public void Respawn_AddsStackingTrauma()
        {
            var m = new VitalsModel();
            m.Add(StatusKind.BleedingHeavy);
            m.Kill("тест");
            m.Respawn();
            Assert.IsFalse(m.IsDead);
            Assert.IsFalse(m.Has(StatusKind.BleedingHeavy));
            Assert.AreEqual(1, m.TraumaStacks);
            Assert.AreEqual(80f, m.MaxHealth, 0.01f);
            Assert.AreEqual(m.MaxHealth, m.Health, 0.01f);

            m.Kill("тест");
            m.Respawn();
            Assert.AreEqual(2, m.TraumaStacks);
            Assert.AreEqual(60f, m.MaxHealth, 0.01f);

            m.Tick(0f, VitalsModel.TraumaHours + 0.1f, Comfort);
            Assert.AreEqual(0, m.TraumaStacks, "травма проходит за 8 игровых часов");
        }

        [Test]
        public void Temperature_ColdWithoutClothes_WarmWithClothes()
        {
            var cold = new VitalsModel();
            var env = new VitalsEnvironment { AirTemperature = 4f, Light = 1f };
            for (int i = 0; i < 600; i++)
                cold.Tick(1f, 0f, env);
            Assert.Less(cold.BodyTemperature, 36f);

            var dressed = new VitalsModel();
            env.Warmth = 14f;
            for (int i = 0; i < 600; i++)
                dressed.Tick(1f, 0f, env);
            Assert.AreEqual(VitalsModel.NormalBodyTemp, dressed.BodyTemperature, 0.01f);
        }

        [Test]
        public void Sanity_DropsInDarkAndAnomaly()
        {
            var m = new VitalsModel();
            var dark = new VitalsEnvironment { AirTemperature = 22f, Light = 0f };
            m.Tick(10f, 0f, dark);
            Assert.AreEqual(98.8f, m.Sanity, 0.01f);
            var anomaly = new VitalsEnvironment { AirTemperature = 22f, Light = 1f, InAnomaly = true };
            m.Tick(10f, 0f, anomaly);
            Assert.AreEqual(95.8f, m.Sanity, 0.01f);
        }

        [Test]
        public void Stamina_SpendAndExhaust()
        {
            var m = new VitalsModel();
            Assert.IsTrue(m.TryConsumeStamina(60f));
            Assert.IsFalse(m.TryConsumeStamina(60f));
            m.DrainStamina(100f);
            Assert.IsTrue(m.StaminaExhausted);
            m.Tick(1.1f, 0f, Comfort);
            m.Tick(2f, 0f, Comfort);
            Assert.IsTrue(m.StaminaExhausted || m.Stamina >= VitalsModel.BaseMaxStamina * VitalsModel.StaminaRecover);
        }
    }
}
