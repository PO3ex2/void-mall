using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using VoidMall.Core;
using VoidMall.World;

namespace VoidMall.Tests
{
    public sealed class MallGenerationTests
    {
        private static readonly MallMaterials Materials = new()
        {
            Bedrock = 1, Concrete = 2, ConcreteMass = 3, Soil = 4, SmoothTile = 5, Terrazzo = 6, CeilingTile = 7,
            PaintWhite = 8, Glass = 9, Shutter = 10, FacadeDark = 11, Asphalt = 12, LineX = 13, LineZ = 14,
            Lamp = 15, Emergency = 16, Skylight = 17, SkyScreen = 18, Grass = 19,
        };

        private NativeArray<SectionInfo> _sections;
        private NativeArray<MaterialInfo> _materials;

        [SetUp]
        public void SetUp()
        {
            _sections = new NativeArray<SectionInfo>(4, Allocator.Persistent);
            _materials = new NativeArray<MaterialInfo>(32, Allocator.Persistent);
            _sections[0] = new SectionInfo { Floor = 20, Wall = 21, Facade = 22, Ceiling = 23, Layout = SectionLayout.Rows, RetailFloorMask = 0b11111, Weight = 2f };
            _sections[1] = new SectionInfo { Floor = 24, Wall = 25, Facade = 22, Ceiling = 23, Layout = SectionLayout.Hall, RetailFloorMask = 0b11000, Weight = 1f };
            _sections[2] = new SectionInfo { Floor = 2, Wall = 26, Facade = 22, Ceiling = 2, Layout = SectionLayout.Warehouse, RetailFloorMask = 0b11111, Service = true, Weight = 1f };
            _sections[3] = new SectionInfo { Floor = 2, Wall = 2, Facade = 2, Ceiling = 2, Layout = SectionLayout.Tech, RetailFloorMask = 0, Service = true, Weight = 1f };
        }

        [TearDown]
        public void TearDown()
        {
            _sections.Dispose();
            _materials.Dispose();
        }

        // ───────────── Детерминизм ─────────────

        [Test]
        public void SameSeed_SameChunkHashes_InAnyOrder()
        {
            var chunks = new[] { new int3(0, 0, 0), new int3(3, 1, -2), new int3(-5, 0, 7), new int3(1, 1, 1) };
            var forward = new List<uint>();
            foreach (var chunk in chunks)
                forward.Add(ChunkHash(chunk, 42));
            var backward = new List<uint>();
            for (int i = chunks.Length - 1; i >= 0; i--)
                backward.Insert(0, ChunkHash(chunks[i], 42));
            Assert.That(backward, Is.EqualTo(forward));
        }

        [Test]
        public void DifferentSeed_DifferentMall()
        {
            Assert.That(ChunkHash(new int3(0, 0, 0), 1), Is.Not.EqualTo(ChunkHash(new int3(0, 0, 0), 2)));
        }

        [Test]
        public void AboveRoof_IsUniformAir()
        {
            var cells = Generate(new int3(0, 2, 0), 7, out bool uniform, out _);
            try
            {
                Assert.That(uniform, Is.True);
                Assert.That(cells[0].IsAir, Is.True);
            }
            finally
            {
                cells.Dispose();
            }
        }

        [Test]
        public void VerticalSection_FoundationSlabsAndRoof()
        {
            // Ищем ячейку без аномалии и смотрим колонку внутри галереи (у фасада, вне атриума).
            uint seed = 5;
            int2 cell = FindPlainCell(seed);
            int3 chunk = new(cell.x * 2, 0, cell.y * 2);
            var low = Generate(chunk, seed, out _, out _);
            var high = Generate(chunk + new int3(0, 1, 0), seed, out _, out _);
            try
            {
                Cell At(int y) => y < 32 ? low[WorldConstants.CellIndex(1, y, 20)] : high[WorldConstants.CellIndex(1, y - 32, 20)];
                Assert.That(At(0).Material, Is.EqualTo(Materials.Bedrock));
                Assert.That(At(3).Material, Is.EqualTo(Materials.Soil));
                Assert.That(At(MallLayout.LevelBase(0)).Material, Is.Not.EqualTo(0), "Плита P2");
                Assert.That(At(MallLayout.FloorTop(0)).IsAir, Is.True, "Над плитой P2 — воздух");
                Assert.That(At(MallLayout.LevelBase(2)).Material, Is.EqualTo(Materials.Terrazzo), "Пол галереи этажа 1");
                Assert.That(At(MallLayout.FloorTop(2) + 2).IsAir, Is.True);
                Assert.That(At(MallLayout.RoofY).Material, Is.EqualTo(Materials.Concrete), "Кровля");
                Assert.That(At(MallLayout.RoofY + 1).IsAir, Is.True);
            }
            finally
            {
                low.Dispose();
                high.Dispose();
            }
        }

        [Test]
        public void Lamps_AreReported()
        {
            int2 cell = FindPlainCell(11);
            var cells = Generate(new int3(cell.x * 2, 0, cell.y * 2), 11, out _, out var lamps);
            try
            {
                Assert.That(lamps.Count, Is.GreaterThan(0));
                foreach (var lamp in lamps)
                {
                    int3 local = lamp - new int3(cell.x * 64, 0, cell.y * 64);
                    ushort material = cells[WorldConstants.CellIndex(local)].Material;
                    Assert.That(material == Materials.Lamp || material == Materials.Emergency || material == Materials.Skylight, Is.True);
                }
            }
            finally
            {
                cells.Dispose();
            }
        }

        [Test]
        public void ServiceRooms_ElectricalOnParking_RareInBackrooms_AndAFewOnTechLevel()
        {
            // Подсобка (только торговые), вентиляционная (только техэтаж), щитовая (парковка, изредка остальные).
            var sections = new NativeArray<SectionInfo>(4, Allocator.Persistent);
            sections[0] = new SectionInfo { Layout = SectionLayout.Rows, RetailFloorMask = 0b11111, Weight = 1f };
            sections[1] = new SectionInfo { Layout = SectionLayout.Warehouse, RetailFloorMask = 0b11111, Service = true, ServiceRetail = 1f };
            sections[2] = new SectionInfo { Layout = SectionLayout.Tech, Service = true, ServiceTech = 1f };
            sections[3] = new SectionInfo { Layout = SectionLayout.Electrical, Service = true, ServiceRetail = 0.12f, ServiceTech = 0.07f, ServiceParking = 1f };
            var plan = new MallLevelPlan(Allocator.Persistent);
            try
            {
                var counts = new int[3, 4]; // [парковка, торговые, техэтаж] × раздел
                for (int x = 0; x < 12; x++)
                for (int level = 0; level < MallLayout.LevelCount; level++)
                {
                    var info = MallPlanner.GetCellInfo(21, 0f, new int2(x, 2));
                    MallPlanner.BuildLevel(21, info, level, sections, ref plan);
                    int kind = (int)MallLayout.Kind(level);
                    for (int i = 1; i < plan.Rooms.Length; i++)
                    {
                        var room = plan.Rooms[i];
                        if (room.Has(RoomFlags.Service) && !room.Has(RoomFlags.Stairwell))
                            counts[kind, room.Section]++;
                    }
                }
                int parking = (int)LevelKind.Parking, retail = (int)LevelKind.Retail, tech = (int)LevelKind.Tech;
                Assert.That(counts[parking, 3], Is.GreaterThan(0));
                Assert.That(counts[parking, 1] + counts[parking, 2], Is.Zero, "На парковке служебная комната ядра — щитовая");
                Assert.That(counts[retail, 2], Is.Zero, "Вентиляционные — только на техэтаже");
                Assert.That(counts[retail, 3], Is.GreaterThan(0), "В закулисье магазинов есть щитовые");
                Assert.That(counts[retail, 3], Is.LessThan(counts[retail, 1] / 3), "…но подсобок намного больше");
                Assert.That(counts[tech, 3], Is.GreaterThan(0), "На техэтаже есть пара щитовых");
                Assert.That(counts[tech, 3], Is.LessThan(counts[tech, 2] / 5), "…а в основном — вентиляционные");
            }
            finally
            {
                plan.Dispose();
                sections.Dispose();
            }
        }

        [Test]
        public void Anomalies_VaryInSize_AndBigAbyssesSpareCoresAndReachNeighbours()
        {
            float abyssMin = float.MaxValue, abyssMax = 0f, poolMin = float.MaxValue, poolMax = 0f;
            int foreign = 0;
            for (int z = -30; z < 30; z++)
            for (int x = -30; x < 30; x++)
            {
                var info = MallPlanner.GetCellInfo(77, 1f, new int2(x, z));
                if (info.HasBigAbyss)
                {
                    foreign++;
                    // Своя аномалия ячейки и пришедшая бездна соседа не пересекаются.
                    float bigZone = MallAnomalies.AbyssZoneRadius(info.BigAbyssRadius);
                    if (info.Anomaly == AnomalyKind.Abyss || info.Anomaly == AnomalyKind.Pool)
                    {
                        float own = info.Anomaly == AnomalyKind.Abyss ? MallAnomalies.AbyssZoneRadius(info.AnomalyRadius) : MallAnomalies.PoolZoneRadius(info.AnomalyRadius);
                        Assert.That(math.distance(info.AnomalyCenter, info.BigAbyssCenter), Is.GreaterThan(own + bigZone), $"Аномалии ячейки {x},{z} пересекаются");
                    }
                }
                float zone;
                if (info.Anomaly == AnomalyKind.Pool)
                {
                    zone = MallAnomalies.PoolZoneRadius(info.AnomalyRadius);
                    poolMin = math.min(poolMin, zone * 2f);
                    poolMax = math.max(poolMax, zone * 2f);
                }
                else if (info.Anomaly == AnomalyKind.Abyss)
                {
                    zone = MallAnomalies.AbyssZoneRadius(info.AnomalyRadius);
                    abyssMin = math.min(abyssMin, zone * 2f);
                    abyssMax = math.max(abyssMax, zone * 2f);
                }
                else
                    continue;
                // Ни одно ядро (лестница, лифт) в окне 3×3 не попадает в зону аномалии.
                for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int2 cell = new(x + dx, z + dz);
                    var other = MallPlanner.GetCellInfo(77, 1f, cell);
                    float2 origin = MallLayout.CellOrigin(cell);
                    float2 nearest = math.clamp(info.AnomalyCenter, origin + (float2)other.CoreRect.xy, origin + (float2)other.CoreRect.zw);
                    Assert.That(math.distance(nearest, info.AnomalyCenter), Is.GreaterThanOrEqualTo(zone), $"{info.Anomaly} ячейки {x},{z} задевает ядро {cell}");
                }
            }
            Assert.That(poolMin, Is.GreaterThanOrEqualTo(13f - 0.01f), "Котлован от 13 м");
            Assert.That(poolMax, Is.LessThanOrEqualTo(30f + 0.01f), "Котлован до 30 м");
            Assert.That(abyssMin, Is.LessThan(16f), "Есть бездны меньше прежних (~16 м)");
            Assert.That(abyssMax, Is.GreaterThan(45f), "Есть огромные бездны");
            Assert.That(foreign, Is.GreaterThan(0), "Большие бездны дотягиваются до соседних ячеек");
        }

        [Test]
        public void PoolLining_UsesSmoothVariantOfSectionFloor()
        {
            // Гладкие двойники полов разделов: 20 → 30, 24 → 31, 2 → 32.
            _materials[20] = new MaterialInfo { SmoothVariant = 30 };
            _materials[24] = new MaterialInfo { SmoothVariant = 31 };
            _materials[2] = new MaterialInfo { SmoothVariant = 32 };
            for (int x = 0; x < 400; x++)
            {
                var info = MallPlanner.GetCellInfo(13, 1f, new int2(x, 3));
                if (info.Anomaly != AnomalyKind.Pool)
                    continue;
                int3 center = new((int)info.AnomalyCenter.x, MallLayout.FloorTop(info.AnomalyLevel) - 1, (int)info.AnomalyCenter.y);
                int3 chunk = center >> WorldConstants.ChunkShift;
                var cells = Generate(chunk, 13, out _, out _);
                try
                {
                    int lined = 0;
                    for (int i = 0; i < cells.Length; i++)
                    {
                        ushort m = cells[i].Material;
                        if (m == 30 || m == 31 || m == 32)
                            lined++;
                    }
                    Assert.That(lined, Is.GreaterThan(0), "Чаша облицована полом раздела");
                    return;
                }
                finally
                {
                    cells.Dispose();
                }
            }
            Assert.Fail("No pool anomaly found.");
        }

        // ───────────── Планировка ─────────────

        [Test]
        public void EveryOpenRoom_IsReachableFromGallery()
        {
            var plan = new MallLevelPlan(Allocator.Persistent);
            try
            {
                for (uint seed = 1; seed <= 4; seed++)
                for (int cx = -2; cx <= 2; cx++)
                for (int level = MallLayout.FirstRetailLevel; level < MallLayout.LevelCount; level++)
                {
                    var info = MallPlanner.GetCellInfo(seed, 1f, new int2(cx, cx * 3));
                    MallPlanner.BuildLevel(seed, info, level, _sections, ref plan);
                    var reached = Flood(plan);
                    for (int room = 1; room < plan.Rooms.Length; room++)
                    {
                        if (plan.Rooms[room].Has(RoomFlags.Closed))
                            continue;
                        Assert.That(reached.Contains(room), Is.True,
                            $"seed {seed}, cell {cx}, level {level}: room {room} {plan.Rooms[room].Rect} [{plan.Rooms[room].Flags}] is unreachable");
                    }
                }
            }
            finally
            {
                plan.Dispose();
            }
        }

        [Test]
        public void CorePosition_IsSameOnAllLevels()
        {
            var plan = new MallLevelPlan(Allocator.Persistent);
            try
            {
                var info = MallPlanner.GetCellInfo(9, 1f, new int2(3, -4));
                int2? elevator = null;
                for (int level = 0; level < MallLayout.LevelCount; level++)
                {
                    MallPlanner.BuildLevel(9, info, level, _sections, ref plan);
                    int2 found = new(-1, -1);
                    for (int z = 0; z < 64 && found.x < 0; z++)
                    for (int x = 0; x < 64; x++)
                        if (plan.TileAt(x, z) == Tile.ElevatorVoid)
                        {
                            found = new int2(x, z);
                            break;
                        }
                    Assert.That(found.x, Is.GreaterThanOrEqualTo(0), $"level {level}: no elevator");
                    elevator ??= found;
                    Assert.That(found, Is.EqualTo(elevator.Value), $"level {level}: elevator moved");
                }
            }
            finally
            {
                plan.Dispose();
            }
        }

        [Test]
        public void EscalatorLandings_MatchEscalatorEnds()
        {
            for (int k = 0; k < MallLayout.RetailFloors - 1; k++)
            {
                int lane = k % 2;
                bool bottomAtEnd = k % 2 == 1;
                Assert.That(MallPlanner.IsEscalatorLanding(k, bottomAtEnd, lane), Is.True, $"escalator {k} bottom");
                Assert.That(MallPlanner.IsEscalatorLanding(k + 1, !bottomAtEnd, lane), Is.True, $"escalator {k} top");
            }
            // На верхнем этаже начинающихся эскалаторов нет: в начале пустоты открыт только приход снизу.
            Assert.That(MallPlanner.IsEscalatorLanding(MallLayout.RetailFloors - 1, false, 0), Is.False);
        }

        [Test]
        public void Sections_RespectFloorMask()
        {
            for (int x = 0; x < 512; x += 7)
            {
                int section = MallPlanner.SectionAt(3, _sections, new int2(x, x * 2), 0);
                Assert.That(section, Is.EqualTo(0), "Фудкорт (маска этажей 4–5) не должен попадать на этаж 1");
            }
        }

        [Test]
        public void ShaftProfile_FloorLipAndVerticalWall()
        {
            const float floor = 18f;
            const float lip = MallAnomalies.LipRadius;
            const float shaft = 8f;
            Assert.That(MallAnomalies.ShaftSolidDistance(30f, floor - 2f, shaft, floor), Is.EqualTo(2f).Within(1e-4f));
            Assert.That(MallAnomalies.ShaftSolidDistance(shaft + 0.5f, 3f, shaft, floor), Is.EqualTo(0.5f).Within(1e-4f));
            Assert.That(MallAnomalies.ShaftSolidDistance(shaft - 0.5f, -50f, shaft, floor), Is.LessThan(0f));
            Assert.That(MallAnomalies.ShaftSolidDistance(shaft + lip - 1e-3f, floor - 0.3f, shaft, floor), Is.EqualTo(0.3f).Within(0.01f));
            Assert.That(MallAnomalies.ShaftSolidDistance(shaft + 0.3f, floor - 0.3f, shaft, floor), Is.LessThan(0f));
        }

        [Test]
        public void LampWave_DayOnNightOff()
        {
            Assert.That(LampRules.Wave(12f), Is.EqualTo(0f));
            Assert.That(LampRules.Wave(2f), Is.EqualTo(1f));
            Assert.That(LampRules.Wave(22.5f), Is.EqualTo(0.5f).Within(1e-4f));
            Assert.That(LampRules.Wave(6.5f), Is.EqualTo(0.5f).Within(1e-4f));
            // Аварийные горят только с 20:00 до 23:30, к полуночи гаснут все и не горят до вечера.
            Assert.That(LampRules.Intensity(1, new int3(10, 24, 10), LampKind.Emergency, 1f, 0f, LampRules.EmergencyWave(21f), 0f), Is.EqualTo(1f));
            Assert.That(LampRules.EmergencyWave(21f), Is.EqualTo(0f));
            Assert.That(LampRules.EmergencyWave(3f), Is.EqualTo(1f));
            Assert.That(LampRules.EmergencyWave(12f), Is.EqualTo(1f));
            for (int x = 0; x < 50; x++)
            {
                Assert.That(LampRules.Intensity(1, new int3(x, 24, 7), LampKind.Emergency, 1f, 0f, LampRules.EmergencyWave(3f), 0f), Is.EqualTo(0f));
                Assert.That(LampRules.Intensity(1, new int3(x, 24, 7), LampKind.Emergency, 0f, 1f, LampRules.EmergencyWave(12f), 0f), Is.EqualTo(0f));
            }
        }

        // ───────────── Вспомогательное ─────────────

        /// <summary>Комнаты, достижимые пешком от галереи (проходимые плитки: пол, двери, входы).</summary>
        private static HashSet<int> Flood(in MallLevelPlan plan)
        {
            var reached = new HashSet<int>();
            var visited = new bool[MallLevelPlan.Area];
            var queue = new Queue<int2>();
            queue.Enqueue(new int2(1, 1));
            visited[MallLevelPlan.Index(1, 1)] = true;
            // Северный и восточный фасады квартала выходят на галереи соседних ячеек: проходы на краю — тоже вход.
            for (int i = 0; i < 64; i++)
            {
                foreach (var edge in new[] { new int2(63, i), new int2(i, 63) })
                {
                    int index = MallLevelPlan.Index(edge.x, edge.y);
                    if (!visited[index] && IsWalkable(plan.TileAt(edge.x, edge.y)))
                    {
                        visited[index] = true;
                        queue.Enqueue(edge);
                    }
                }
            }
            while (queue.Count > 0)
            {
                int2 p = queue.Dequeue();
                reached.Add(plan.RoomIds[MallLevelPlan.Index(p.x, p.y)]);
                foreach (var d in new[] { new int2(1, 0), new int2(-1, 0), new int2(0, 1), new int2(0, -1) })
                {
                    int2 n = p + d;
                    if (n.x < 0 || n.y < 0 || n.x >= 64 || n.y >= 64)
                        continue;
                    int index = MallLevelPlan.Index(n.x, n.y);
                    if (visited[index] || !IsWalkable(plan.TileAt(n.x, n.y)))
                        continue;
                    visited[index] = true;
                    queue.Enqueue(n);
                }
            }
            return reached;
        }

        private static bool IsWalkable(Tile tile) =>
            tile == Tile.Open || tile == Tile.Door || tile == Tile.FacadeDoor || tile == Tile.CoreDoor || tile == Tile.AtriumVoid
            || tile == Tile.Shutter; // рольставню можно сломать

        // ───────────── Аномалии 2 (этап 8В) ─────────────

        private static MallCellInfo FindAnomaly(AnomalyKind kind, out uint seed)
        {
            for (seed = 1; seed < 40; seed++)
            for (int z = 0; z < 20; z++)
            for (int x = 0; x < 20; x++)
            {
                var info = MallPlanner.GetCellInfo(seed, 1f, new int2(x, z));
                if (info.Anomaly == kind)
                    return info;
            }
            Assert.Fail($"No {kind} found.");
            return default;
        }

        [Test]
        public void Inverted_LampsInFloorSlab_NotInCeiling()
        {
            var info = FindAnomaly(AnomalyKind.Inverted, out uint seed);
            int3 chunk = new((int)info.AnomalyCenter.x / 32, 0, (int)info.AnomalyCenter.y / 32);
            var low = Generate(chunk, seed, out _, out var lamps);
            var high = Generate(chunk + new int3(0, 1, 0), seed, out _, out var highLamps);
            try
            {
                lamps.AddRange(highLamps);
                Assert.That(lamps.Count, Is.GreaterThan(0), "лампы в перевёрнутом чанке есть");
                foreach (var lamp in lamps)
                {
                    int level = MallLayout.LevelAt(lamp.y);
                    Assert.That(lamp.y, Is.EqualTo(MallLayout.LevelBase(level)), "лампа — в плите пола, а не в потолке");
                }
            }
            finally
            {
                low.Dispose();
                high.Dispose();
            }
        }

        [Test]
        public void Oasis_HoleInCeiling_GrassBelow_ScreenAbove()
        {
            var info = FindAnomaly(AnomalyKind.Oasis, out uint seed);
            int level = info.AnomalyLevel;
            Assert.That(MallLayout.Kind(level), Is.EqualTo(LevelKind.Retail));
            int3 column = new((int)math.floor(info.AnomalyCenter.x), 0, (int)math.floor(info.AnomalyCenter.y));
            int3 chunk = new(column.x >> 5, 0, column.z >> 5);
            var low = Generate(chunk, seed, out _, out _);
            var high = Generate(chunk + new int3(0, 1, 0), seed, out _, out _);
            try
            {
                Cell At(int y)
                {
                    int3 local = new(column.x & 31, y & 31, column.z & 31);
                    return (y < 32 ? low : high)[WorldConstants.CellIndex(local)];
                }
                Assert.That(At(MallLayout.LevelBase(level)).Material, Is.EqualTo(Materials.Grass), "под дырой — трава");
                Assert.That(At(MallLayout.CeilingRow(level)).IsAir, Is.True, "дыра в потолке");
                Assert.That(At(MallLayout.LevelBase(level + 1)).IsAir, Is.True, "пол этажа выше прорезан");
                Assert.That(At(MallLayout.CeilingRow(level + 1)).Material, Is.EqualTo(Materials.SkyScreen), "наверху экран неба");
            }
            finally
            {
                low.Dispose();
                high.Dispose();
            }
        }

        private static int2 FindPlainCell(uint seed)
        {
            for (int x = 0; x < 100; x++)
            {
                var info = MallPlanner.GetCellInfo(seed, 1f, new int2(x, 0));
                if (info.Anomaly == AnomalyKind.None && !info.AtriumZ)
                    return info.Cell;
            }
            Assert.Fail("No plain cell found.");
            return default;
        }

        private uint ChunkHash(int3 chunk, uint seed)
        {
            var cells = Generate(chunk, seed, out _, out _);
            try
            {
                uint hash = 0;
                for (int i = 0; i < cells.Length; i++)
                    hash = Hash.Get(hash, cells[i].GetHashCode());
                return hash;
            }
            finally
            {
                cells.Dispose();
            }
        }

        private NativeArray<Cell> Generate(int3 chunk, uint seed, out bool uniform, out List<int3> lamps)
        {
            var cells = new NativeArray<Cell>(WorldConstants.ChunkVolume, Allocator.TempJob);
            var flag = new NativeArray<byte>(1, Allocator.TempJob);
            var lampList = new NativeList<int3>(64, Allocator.TempJob);
            var anchorList = new NativeList<int3>(16, Allocator.TempJob);
            new MallGenerationJob
            {
                Chunk = chunk,
                Seed = seed,
                AnomalyFrequency = 1f,
                M = Materials,
                Sections = _sections,
                Materials = _materials,
                Cells = cells,
                IsUniform = flag,
                Lamps = lampList,
                Anchors = anchorList,
            }.Run();
            uniform = flag[0] != 0;
            lamps = new List<int3>(lampList.AsArray().ToArray());
            flag.Dispose();
            lampList.Dispose();
            anchorList.Dispose();
            return cells;
        }
    }
}
