using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.TestTools;
using VoidMall.Crafting;
using VoidMall.Game;
using VoidMall.Items;
using VoidMall.World;

namespace VoidMall.Tests
{
    /// <summary>Этап 9: «сохранить → загрузить → сравнить» для колонн, регионов, очередей крафта и описания мира.</summary>
    public sealed class SaveTests
    {
        private sealed class Materials : IMaterialIds
        {
            private readonly string[] _ids;
            public Materials(params string[] ids) => _ids = ids;
            public string IdOf(ushort material) => _ids[material];

            public bool TryGetIndex(string id, out ushort material)
            {
                int index = System.Array.IndexOf(_ids, id);
                material = (ushort)math.max(0, index);
                return index >= 0;
            }
        }

        private sealed class Items : IItemCatalog, IItemIds
        {
            public (int width, int height) GridSize(ushort def) => (1, 1);
            public int MaxStack(ushort def) => 10;
            public float Weight(ushort def) => 1f;
            public string IdOf(ushort def) => "item." + def;

            public bool TryGetIndex(string id, out ushort def)
            {
                def = 0;
                return id.StartsWith("item.") && ushort.TryParse(id.Substring(5), out def);
            }

            public ItemStack Create(ushort def, int count) => new(def, count);
        }

        private static readonly Materials Original = new("block.air", "block.concrete", "block.glass", "smooth.soil", "block.wood");
        private readonly List<ChunkColumn> _columns = new();
        private string _folder;

        [SetUp]
        public void SetUp() => _folder = Path.Combine(Path.GetTempPath(), "VoidMallSaveTests_" + System.Guid.NewGuid().ToString("N"));

        [TearDown]
        public void TearDown()
        {
            foreach (var column in _columns)
                column.Dispose();
            _columns.Clear();
            if (Directory.Exists(_folder))
                Directory.Delete(_folder, true);
        }

        /// <summary>Колонна: чанк 0 — полный (бетон, стекло с формами, гладкий грунт, урон), 1 — однородный бетон, 2 — воздух, 3 — нет.</summary>
        private ChunkColumn MakeColumn(int2 coord)
        {
            var column = new ChunkColumn(coord) { State = ColumnState.Generated };
            var full = ChunkData.CreateForGeneration(new int3(coord.x, 0, coord.y));
            var cells = full.Cells;
            for (int i = 0; i < cells.Length; i++)
            {
                int y = i >> 10;
                cells[i] = y < 8 ? new Cell(1)
                    : y == 8 && (i & 7) == 0 ? new Cell(2, 3, (byte)(i % 24))
                    : y < 12 ? new Cell(3, (byte)(i * 37 % 256))
                    : (i % 97) == 0 ? new Cell(4)
                    : Cell.Air;
            }
            full.SetDamage(5, 120);
            full.SetDamage(30000, 7);
            full.IsModified = true;
            column.Chunks[0] = full;
            column.Chunks[1] = new ChunkData(new int3(coord.x, 1, coord.y), new Cell(1));
            column.Chunks[2] = new ChunkData(new int3(coord.x, 2, coord.y), Cell.Air);
            _columns.Add(column);
            return column;
        }

        private static void AssertSame(ChunkColumn expected, ChunkColumn actual, System.Func<Cell, Cell> map = null)
        {
            for (int y = 0; y < WorldConstants.ChunksPerColumn; y++)
            {
                var a = expected.Chunks[y];
                var b = actual.Chunks[y];
                Assert.AreEqual(a == null, b == null, $"чанк {y}");
                if (a == null)
                    continue;
                for (int i = 0; i < WorldConstants.ChunkVolume; i++)
                {
                    Cell want = map != null ? map(a.Get(i)) : a.Get(i);
                    if (want != b.Get(i))
                        Assert.Fail($"чанк {y}, клетка {i}: ждали {want}, получили {b.Get(i)}");
                }
                CollectionAssert.AreEquivalent(new List<KeyValuePair<int, int>>(a.Damage), new List<KeyValuePair<int, int>>(b.Damage), "урон");
                Assert.IsTrue(b.IsModified, "загруженная колонна снова пишется в сейв");
            }
            Assert.AreEqual(ColumnState.Generated, actual.State);
        }

        [Test]
        public void Column_RoundTrip_KeepsCellsShapesAndDamage()
        {
            var column = MakeColumn(new int2(-3, 7));
            byte[] data = ColumnCodec.Encode(column, Original);
            Assert.Less(data.Length, 64 * 1024, "сжатие: колонна — десятки килобайт, а не мегабайт");
            var loaded = ColumnCodec.Decode(column.Coord, data, Original);
            _columns.Add(loaded);
            AssertSame(column, loaded);
            Assert.IsTrue(loaded.Chunks[1].IsUniform, "однородный чанк остаётся однородным");
        }

        [Test]
        public void Column_RemapsMaterialsById_RemovedBecomesAir()
        {
            var column = MakeColumn(new int2(1, 1));
            byte[] data = ColumnCodec.Encode(column, Original);
            // После «обновления»: материалы переставлены, стекло убрано из игры.
            var updated = new Materials("block.air", "block.wood", "smooth.soil", "block.concrete");
            var loaded = ColumnCodec.Decode(column.Coord, data, updated);
            _columns.Add(loaded);
            AssertSame(column, loaded, cell => cell.Material switch
            {
                1 => new Cell(3, cell.Value, cell.Meta),
                2 => Cell.Air,
                3 => new Cell(2, cell.Value, cell.Meta),
                4 => new Cell(1, cell.Value, cell.Meta),
                _ => cell,
            });
        }

        [Test]
        public void Region_RoundTrip_ThroughFiles()
        {
            var store = new RegionStore(_folder, Original);
            var a = MakeColumn(new int2(0, 0));
            var b = MakeColumn(new int2(-1, 40)); // другой регион (отрицательные координаты)
            store.Store(a);
            store.Store(b);
            Assert.AreEqual(new int2(-1, 1), RegionStore.RegionOf(new int2(-1, 40)));
            var files = store.TakeDirty();
            Assert.AreEqual(2, files.Count, "два региона");
            Assert.IsFalse(store.HasDirty);
            foreach (var (path, bytes) in files)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, bytes);
            }

            var reopened = new RegionStore(_folder, Original);
            Assert.IsTrue(reopened.TryLoad(new int2(-1, 40), out var loadedB));
            _columns.Add(loadedB);
            AssertSame(b, loadedB);
            Assert.IsTrue(reopened.TryLoad(new int2(0, 0), out var loadedA));
            _columns.Add(loadedA);
            AssertSame(a, loadedA);
            Assert.IsFalse(reopened.TryLoad(new int2(5, 5), out _), "нетронутая колонна — генерировать");

            reopened.Remove(new int2(0, 0));
            Assert.IsTrue(reopened.HasDirty);
            Assert.IsFalse(reopened.Contains(new int2(0, 0)), "перегенерированная колонна забыта");
        }

        [Test]
        public void Region_Corrupted_FallsBackToGeneration()
        {
            Directory.CreateDirectory(Path.Combine(_folder, "regions"));
            File.WriteAllBytes(Path.Combine(_folder, "regions", "r.0.0.bin"), new byte[] { 1, 2, 3, 4, 5 });
            LogAssert.Expect(LogType.Error, new Regex("не прочитан"));
            var store = new RegionStore(_folder, Original);
            Assert.IsFalse(store.TryLoad(new int2(1, 1), out _));

            var column = MakeColumn(new int2(2, 2));
            var whole = ColumnCodec.Encode(column, Original);
            var data = new byte[whole.Length / 2]; // оборванная запись
            System.Array.Copy(whole, data, data.Length);
            var regionData = new Dictionary<int2, byte[]> { [new int2(2, 2)] = data };
            File.WriteAllBytes(Path.Combine(_folder, "regions", "r.0.0.bin"), RegionStore.Serialize(regionData));
            var reopened = new RegionStore(_folder, Original);
            LogAssert.Expect(LogType.Error, new Regex("повреждена"));
            Assert.IsFalse(reopened.TryLoad(new int2(2, 2), out _), "битая колонна генерируется заново, а не роняет игру");
        }

        [Test]
        public void CraftQueue_RoundTrip_KeepsProgressAndRepair()
        {
            var items = new Items();
            var queue = new CraftQueue();
            var tool = items.Create(3, 1);
            tool.Durability = 0.2f;
            var repair = new CraftJob { Kind = CraftJobKind.Repair, Title = "Починить", Duration = 20f, Elapsed = 5f, Points = 0.5f };
            repair.Taken.Add(tool);
            repair.Results.Add(tool);
            queue.Enqueue(repair);
            var reheat = new CraftJob { Kind = CraftJobKind.Reheat, Title = "Прогреть", Duration = 15f };
            reheat.Taken.Add(items.Create(7, 1));
            reheat.Results.Add(items.Create(7, 1));
            queue.Enqueue(reheat);

            var json = JsonUtility.ToJson(CraftSave.Write(queue, items));
            var restored = new CraftQueue();
            CraftSave.Read(JsonUtility.FromJson<QueueData>(json), restored, items, _ => null);
            Assert.AreEqual(2, restored.Count);
            var job = restored.Jobs[0];
            Assert.AreEqual(CraftJobKind.Repair, job.Kind);
            Assert.AreEqual(5f, job.Elapsed);
            Assert.AreEqual(0.2f, job.Taken[0].Durability, 1e-4f, "отмена вернёт инструмент как был");
            job.Completed();
            Assert.AreEqual(1f, job.Results[0].Durability, "завершение чинит");
            Assert.AreEqual(7, restored.Jobs[1].Results[0].Def);
        }

        [Test]
        public void WorldMeta_And_Props_SurviveJson()
        {
            var meta = new WorldMeta { Name = "Тест", Seed = uint.MaxValue.ToString(), TotalHours = 123.5 };
            var back = JsonUtility.FromJson<WorldMeta>(JsonUtility.ToJson(meta));
            Assert.AreEqual(uint.MaxValue.ToString(), back.Seed);
            Assert.AreEqual(SaveSystem.SaveVersion, back.SaveVersion);
            Assert.AreEqual(WorldConstants.GeneratorVersion, back.GeneratorVersion);
            Assert.AreEqual(123.5, back.TotalHours, 1e-9);

            var props = new PropsData { NextId = ulong.MaxValue.ToString() };
            var prop = new SavedProp { Id = "18446462598732840960", Def = PropManager.VoxelDefId, Parent = "42", HasVoxel = true };
            prop.Voxel.Cells.Add(new SavedVoxelCell { P = new Vector3Int(1, -2, 3), Material = "block.wood", Value = 1, Meta = 5, Damage = 40 });
            prop.Anchors.Add(new Vector3Int(10, 20, -30));
            props.Props.Add(prop);
            var state = JsonUtility.FromJson<WorldState>(JsonUtility.ToJson(new WorldState { Props = props }));
            var loaded = state.Props.Props[0];
            Assert.AreEqual(prop.Id, loaded.Id, "ID пропа (ulong) без потерь");
            Assert.AreEqual("42", loaded.Parent);
            Assert.AreEqual(new Vector3Int(1, -2, 3), loaded.Voxel.Cells[0].P);
            Assert.AreEqual(40, loaded.Voxel.Cells[0].Damage);
            Assert.AreEqual(new Vector3Int(10, 20, -30), loaded.Anchors[0]);
            Assert.AreEqual(ulong.MaxValue.ToString(), state.Props.NextId);
        }

        [Test]
        public void WorldFolders_AreSafeNames()
        {
            StringAssert.EndsWith("Мир_1", SaveSystem.FolderOf("Мир/1"));
            StringAssert.StartsWith(SaveSystem.SavesRoot, SaveSystem.FolderOf("a:b"));
        }
    }
}
