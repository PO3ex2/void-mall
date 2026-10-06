using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using VoidMall.World;

namespace VoidMall.Tests
{
    /// <summary>Этап 8Б: биомы, вход в мол, тупики в галереях.</summary>
    public sealed class BiomeTests
    {
        private const float Frequency = 1f;

        [Test]
        public void Biome_IsDeterministic_AndFormsShares()
        {
            int empty = 0, field = 0, total = 0;
            for (int z = -40; z < 40; z++)
            for (int x = -40; x < 40; x++)
            {
                var cell = new int2(x, z);
                var biome = MallPlanner.BiomeOf(7, Frequency, cell);
                Assert.AreEqual(biome, MallPlanner.BiomeOf(7, Frequency, cell));
                Assert.AreEqual(biome, MallPlanner.GetCellInfo(7, Frequency, cell).Biome);
                if (biome == MallBiome.Empty)
                    empty++;
                else if (biome == MallBiome.AnomalyField)
                    field++;
                total++;
            }
            Assert.That(empty / (float)total, Is.InRange(0.4f, 0.6f), "пустые разделы — самый частый биом");
            Assert.That(field / (float)total, Is.InRange(0.05f, 0.16f), "поле аномалий — редкое");
        }

        [Test]
        public void Spawn_IsNormal_WithEntrance()
        {
            for (uint seed = 1; seed < 20; seed++)
            {
                int2 spawn = MallPlanner.SpawnCell(seed, Frequency);
                for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    var info = MallPlanner.GetCellInfo(seed, Frequency, spawn + new int2(dx, dz));
                    Assert.AreEqual(MallBiome.Normal, info.Biome, $"seed {seed}: у старта обычный мол");
                    Assert.IsTrue(info.NearSpawn);
                }
                Assert.IsTrue(MallPlanner.GetCellInfo(seed, Frequency, spawn).HasEntrance);
                float3 point = MallPlanner.FindSpawn(seed, Frequency);
                Assert.AreEqual(spawn, MallLayout.CellOf(point));
            }
        }

        [Test]
        public void Entrance_HasGlassFacadeWithDoors()
        {
            const uint seed = 3;
            int2 spawn = MallPlanner.SpawnCell(seed, Frequency);
            var info = MallPlanner.GetCellInfo(seed, Frequency, spawn);
            var plan = new MallLevelPlan(Allocator.Temp);
            var sections = new NativeArray<SectionInfo>(1, Allocator.Temp);
            sections[0] = new SectionInfo { Layout = SectionLayout.Rows, RetailFloorMask = 0b11111, Weight = 1f };
            try
            {
                MallPlanner.BuildLevel(seed, info, MallLayout.FirstRetailLevel, sections, ref plan);
                int doors = 0;
                for (int x = 0; x < MallLayout.Band; x++)
                {
                    Tile t = plan.TileAt(x, MallPlanner.EntranceZ);
                    Assert.IsTrue(t == Tile.FacadeWall || t == Tile.FacadeGlass || t == Tile.FacadeDoor, $"x {x}: {t}");
                    if (t == Tile.FacadeDoor)
                        doors++;
                }
                Assert.AreEqual(8, doors, "две двери 4×3");
            }
            finally
            {
                plan.Dispose();
                sections.Dispose();
            }
        }

        [Test]
        public void Balconies_NotOnAtriumBands_WithBridge()
        {
            const uint seed = 9;
            var sections = new NativeArray<SectionInfo>(1, Allocator.Temp);
            sections[0] = new SectionInfo { Layout = SectionLayout.Rows, RetailFloorMask = 0b11111, Weight = 1f };
            var plan = new MallLevelPlan(Allocator.Temp);
            int balconies = 0;
            try
            {
                for (int z = -5; z < 5; z++)
                for (int x = -5; x < 5; x++)
                {
                    var info = MallPlanner.GetCellInfo(seed, Frequency, new int2(x, z));
                    Assert.IsFalse(info.BalconyX && info.AtriumX);
                    Assert.IsFalse(info.BalconyZ && info.AtriumZ);
                    if (!info.BalconyX)
                        continue;
                    balconies++;
                    MallPlanner.BuildLevel(seed, info, MallLayout.FirstRetailLevel + 2, sections, ref plan);
                    Assert.AreEqual(Tile.AtriumVoid, plan.TileAt(20, 7), "проём посередине полосы");
                    Assert.AreNotEqual(Tile.AtriumVoid, plan.TileAt(38, 7), "мостик поперёк");
                    Assert.AreEqual(Tile.Railing, plan.TileAt(20, MallPlanner.AtriumAcrossMin - 1), "ограждение по краю");
                    Assert.AreEqual(Tile.Open, plan.TileAt(20, 1), "проход-балкон у витрин");
                }
            }
            finally
            {
                plan.Dispose();
                sections.Dispose();
            }
            Assert.Greater(balconies, 0);
        }

        [Test]
        public void Fountain_OnFirstFloor_WellAbove()
        {
            const uint seed = 4;
            var sections = new NativeArray<SectionInfo>(1, Allocator.Temp);
            sections[0] = new SectionInfo { Layout = SectionLayout.Rows, RetailFloorMask = 0b11111, Weight = 1f };
            var plan = new MallLevelPlan(Allocator.Temp);
            int found = 0;
            try
            {
                for (int z = -8; z < 8; z++)
                for (int x = -8; x < 8; x++)
                {
                    var info = MallPlanner.GetCellInfo(seed, Frequency, new int2(x, z));
                    if (!MallPlanner.PlazaOf(seed, info, out bool hasFountain) || !hasFountain)
                        continue;
                    found++;
                    for (int level = MallLayout.FirstRetailLevel; level < MallLayout.FirstRetailLevel + MallLayout.RetailFloors; level++)
                    {
                        MallPlanner.RoundPlaza(seed, info, level, out bool fountain);
                        Assert.AreEqual(level == MallLayout.FirstRetailLevel, fountain, "фонтан только на первом торговом этаже");
                        Assert.AreEqual(level != MallLayout.FirstRetailLevel, MallPlanner.PlazaWell(seed, info, level));
                        MallPlanner.BuildLevel(seed, info, level, sections, ref plan);
                        Tile center = plan.TileAt(7, 7);
                        Assert.AreEqual(level == MallLayout.FirstRetailLevel ? Tile.Open : Tile.AtriumVoid, center, $"уровень {level}: центр площади");
                    }
                }
            }
            finally
            {
                plan.Dispose();
                sections.Dispose();
            }
            Assert.Greater(found, 0);
        }

        [Test]
        public void DeadEnds_NeverNearSpawn_NeverAcrossAtrium()
        {
            const uint seed = 5;
            var sections = new NativeArray<SectionInfo>(1, Allocator.Temp);
            sections[0] = new SectionInfo { Layout = SectionLayout.Rows, RetailFloorMask = 0b11111, Weight = 1f };
            var plan = new MallLevelPlan(Allocator.Temp);
            int found = 0;
            try
            {
                for (int z = -6; z < 6; z++)
                for (int x = -6; x < 6; x++)
                {
                    var info = MallPlanner.GetCellInfo(seed, Frequency, new int2(x, z));
                    for (int level = MallLayout.FirstRetailLevel; level < MallLayout.LevelCount; level++)
                    {
                        MallPlanner.BuildLevel(seed, info, level, sections, ref plan);
                        for (int axis = 0; axis < 2; axis++)
                        {
                            int walls = 0;
                            for (int along = MallLayout.Band; along < MallLayout.CellSize; along++)
                            {
                                // Поперёк середины полосы (там нет ни ограждения атриума, ни колонн).
                                Tile t = axis == 0 ? plan.TileAt(along, 1) : plan.TileAt(1, along);
                                if (t != Tile.Wall && t != Tile.Shutter)
                                    continue;
                                walls++;
                                Assert.IsFalse(info.NearSpawn, "у старта тупиков нет");
                                Assert.That(along, Is.Not.InRange(MallPlanner.AtriumAlongMin - 1, MallPlanner.AtriumAlongMax), "не поперёк атриума");
                            }
                            Assert.That(walls, Is.LessThanOrEqualTo(1), "не больше одного тупика на полосу");
                            found += walls;
                        }
                    }
                }
            }
            finally
            {
                plan.Dispose();
                sections.Dispose();
            }
            Assert.Greater(found, 0, "тупики вообще бывают");
        }
    }
}
