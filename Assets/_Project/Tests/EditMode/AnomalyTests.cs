using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using VoidMall.Survival;
using VoidMall.World;

namespace VoidMall.Tests
{
    /// <summary>Этап 8В: новые аномалии — план, «Отпечаток», лужа, пульсация.</summary>
    public sealed class AnomalyTests
    {
        private const float Frequency = 1f;

        private static VitalsEnvironment Comfort => new() { AirTemperature = 22f, Light = 1f };

        [Test]
        public void NewAnomalies_AppearAndAreDeterministic()
        {
            int oasis = 0, inverted = 0, pulse = 0, puddle = 0, look = 0;
            for (int z = -20; z < 20; z++)
            for (int x = -20; x < 20; x++)
            {
                var cell = new int2(x, z);
                var info = MallPlanner.GetCellInfo(3, Frequency, cell);
                var again = MallPlanner.GetCellInfo(3, Frequency, cell);
                Assert.AreEqual(info.Anomaly, again.Anomaly);
                Assert.AreEqual(info.MarkerCount, again.MarkerCount);
                Assert.LessOrEqual(info.MarkerCount, MallCellInfo.MaxMarkers);
                if (info.Anomaly == AnomalyKind.Oasis)
                {
                    oasis++;
                    Assert.AreEqual(LevelKind.Retail, MallLayout.Kind(info.AnomalyLevel), "оазис — в потолке торгового этажа");
                    Assert.That(info.AnomalyRadius, Is.InRange(MallPlanner.OasisRadiusMin, MallPlanner.OasisRadiusMax));
                }
                if (info.Anomaly == AnomalyKind.Inverted)
                    inverted++;
                for (int i = 0; i < info.MarkerCount; i++)
                {
                    var marker = info.GetMarker(i);
                    Assert.AreEqual(marker.Center, again.GetMarker(i).Center);
                    switch (marker.Kind)
                    {
                        case AnomalyMarkerKind.Pulse: pulse++; break;
                        case AnomalyMarkerKind.LookAtMe: look++; break;
                        case AnomalyMarkerKind.Puddle:
                            puddle++;
                            int level = MallLayout.LevelAtFeet(marker.Center.y);
                            Assert.AreEqual(MallLayout.FloorTop(level), marker.Center.y, 1e-4f, "лужа лежит на полу");
                            break;
                    }
                }
            }
            Assert.Greater(oasis, inverted, "оазис — частая аномалия, перевёрнутый чанк — редкая");
            Assert.Greater(inverted, 0);
            Assert.Greater(pulse, 0);
            Assert.Greater(puddle, 0);
            Assert.Greater(look, 0);
        }

        [Test]
        public void LookAtMe_ManyInAnomalyField_RareInEmpty()
        {
            int fieldCells = 0, fieldLooks = 0, emptyCells = 0, emptyLooks = 0;
            for (int z = -40; z < 40; z++)
            for (int x = -40; x < 40; x++)
            {
                var info = MallPlanner.GetCellInfo(7, Frequency, new int2(x, z));
                int looks = 0;
                for (int i = 0; i < info.MarkerCount; i++)
                    if (info.GetMarker(i).Kind == AnomalyMarkerKind.LookAtMe)
                        looks++;
                if (info.Biome == MallBiome.AnomalyField)
                {
                    fieldCells++;
                    fieldLooks += looks;
                    Assert.That(looks, Is.InRange(MallPlanner.LookAtMeFieldMin, MallPlanner.LookAtMeFieldMax), "в поле аномалий — по 2–4 вспышки");
                }
                else if (info.Biome == MallBiome.Empty)
                {
                    emptyCells++;
                    emptyLooks += looks;
                }
            }
            Assert.Greater(fieldCells, 0);
            Assert.Less(emptyLooks / (float)emptyCells, 0.05f, "в пустых разделах почти нет");
        }

        [Test]
        public void Spawn_IsNeverInvertedOrCutout()
        {
            for (uint seed = 1; seed < 40; seed++)
            {
                var info = MallPlanner.GetCellInfo(seed, Frequency, MallPlanner.SpawnCell(seed, Frequency));
                Assert.AreNotEqual(AnomalyKind.Inverted, info.Anomaly);
                Assert.AreNotEqual(AnomalyKind.Cutout, info.Anomaly);
            }
        }

        [Test]
        public void MirrorY_SwapsFloorAndCeiling()
        {
            for (int level = 0; level < MallLayout.LevelCount; level++)
            {
                // Верх плиты пола ↔ низ потолочного ряда.
                Assert.AreEqual(MallLayout.CeilingRow(level), MallPlanner.MirrorY(level, MallLayout.FloorTop(level)), 1e-4f);
                Assert.AreEqual(MallLayout.FloorTop(level), MallPlanner.MirrorY(level, MallLayout.CeilingRow(level)), 1e-4f);
            }
        }

        [Test]
        public void Classes_GeometricAndVisual()
        {
            Assert.AreEqual(AnomalyClass.Geometric, AnomalyKind.Oasis.Class());
            Assert.AreEqual(AnomalyClass.Geometric, AnomalyKind.Inverted.Class());
            Assert.AreEqual(AnomalyClass.Visual, AnomalyMarkerKind.LookAtMe.Class());
            Assert.AreEqual(AnomalyClass.Visual, AnomalyMarkerKind.Puddle.Class());
            Assert.AreEqual("lookme", AnomalyMarkerKind.LookAtMe.Id());
        }

        [Test]
        public void Pulse_PullsThenPushes()
        {
            var pulse = new AnomalyMarker { Kind = AnomalyMarkerKind.Pulse, Center = new float3(0f, 1f, 0f), Radius = 6f };
            var at = new Vector3(3f, 1f, 0f);
            Vector3 pull = AnomalyPhysics.PulseVelocity(pulse, at, 1f, 2f);
            Vector3 push = AnomalyPhysics.PulseVelocity(pulse, at, -1f, 2f);
            Assert.Less(pull.x, 0f, "фаза > 0 — к центру");
            Assert.Greater(push.x, 0f, "фаза < 0 — от центра");
            Assert.AreEqual(0f, pull.y, "вертикально не подбрасывает");
            Assert.AreEqual(Vector3.zero, AnomalyPhysics.PulseVelocity(pulse, new Vector3(7f, 1f, 0f), 1f, 2f), "за радиусом не действует");
            Assert.Greater(AnomalyPhysics.PulsePhase(AnomalyPhysics.PulsePeriod * 0.25f), 0.99f);
        }

        // ───────────── «Отпечаток», лужа, оазис ─────────────

        [Test]
        public void Imprint_AfterLongStare_CapsSanity_AndSleepRemovesIt()
        {
            var m = new VitalsModel();
            var env = Comfort;
            env.LookingAtAnomaly = true;
            for (int i = 0; i < 25; i++)
                m.Tick(1f, 0f, env);
            Assert.IsTrue(m.Has(StatusKind.Imprint), "20 с взгляда — «Отпечаток»");
            Assert.AreEqual(VitalsModel.ImprintSanityCap, m.MaxSanity);
            Assert.LessOrEqual(m.Sanity, VitalsModel.ImprintSanityCap);

            m.SetNeeds(sanity: 100f);
            Assert.AreEqual(VitalsModel.ImprintSanityCap, m.Sanity, 0.01f, "выше половины рассудок не поднять");

            var sleep = Comfort;
            sleep.Sleeping = true;
            for (int i = 0; i < 3; i++)
                m.Tick(1f, 1f, sleep);
            Assert.IsFalse(m.Has(StatusKind.Imprint), "сон снимает «Отпечаток»");
            Assert.AreEqual(100f, m.MaxSanity);
        }

        [Test]
        public void Imprint_ShortGlances_AreForgotten()
        {
            var m = new VitalsModel();
            var look = Comfort;
            look.LookingAtAnomaly = true;
            var away = Comfort;
            for (int round = 0; round < 3; round++)
            {
                for (int i = 0; i < 15; i++)
                    m.Tick(1f, 0f, look);
                for (int i = 0; i < 31; i++)
                    m.Tick(1f, 0f, away);
            }
            Assert.IsFalse(m.Has(StatusKind.Imprint), "по 15 с с перерывами по 30 с — не копится");
        }

        [Test]
        public void Staring_DrainsSanity_OasisRestores()
        {
            var m = new VitalsModel();
            var look = Comfort;
            look.LookingAtAnomaly = true;
            m.Tick(10f, 0f, look);
            Assert.AreEqual(100f - VitalsModel.LookAtMeSanityDrain * 10f, m.Sanity, 0.01f);

            var oasis = Comfort;
            oasis.InOasis = true;
            float before = m.Sanity;
            m.Tick(10f, 0f, oasis);
            Assert.AreEqual(before + VitalsModel.OasisSanityRegen * 10f, m.Sanity, 0.01f);
        }

        [Test]
        public void Puddle_Heals()
        {
            var m = new VitalsModel();
            m.SetHealth(40f, "тест");
            var env = Comfort;
            env.PuddleHeal = 2f;
            m.Tick(5f, 0f, env);
            Assert.AreEqual(50f, m.Health, 0.01f);
        }
    }
}
