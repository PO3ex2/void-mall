using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using VoidMall.Console;
using VoidMall.Core;
using VoidMall.World;
using Random = UnityEngine.Random;

namespace VoidMall.Items
{
    /// <summary>
    /// Товары на полках (GDD §2.6): у стеллажей, холодильников, витрин есть точки спавна (PropMeshes.GetItemSlots).
    /// В каждой точке сначала бросается шанс «есть ли тут что-то» (доля заполнения таблицы раздела × LootAmount мира ×
    /// заполненность этого стеллажа — одни почти пустые, другие полнее, часть разграблена подчистую), и только потом
    /// по весам таблицы выбирается, какой предмет и сколько. Всё детерминировано по ID пропа и номеру места.
    ///
    /// Товары не объекты: рисуются инстансингом, берутся прицелом (луч проверяется по габаритам предметов на полке).
    /// Взятое запоминается за сессию. Если полку толкнули, сдвинули или сломали — товары падают физическими предметами.
    /// </summary>
    [DefaultExecutionOrder(-150)]
    public sealed class ShelfLoot : MonoBehaviour
    {
        public static readonly CVar<float> LootScale = new("sv_loot_scale", 1f, "Множитель заполненности полок (поверх настроек мира).", ConsoleFlags.Cheat);
        public static readonly CVar<float> DrawDistance = new("r_itemdistance", 35f, "Дальность отрисовки товаров на полках, м.", ConsoleFlags.Archive);

        /// <summary>Доля стеллажей, разграбленных подчистую.</summary>
        private const float LootedChance = 0.15f;

        /// <summary>В пустом биоме (GDD §2.9): доля полок, где осталась одна находка, остальные пусты.</summary>
        private const float EmptyBiomeFindChance = 0.05f;

        /// <summary>Доля испорченной еды на полках: съесть можно, но вдвое меньше калорий и отравление.</summary>
        private const float SpoiledChance = 0.25f;

        public static ShelfLoot Instance { get; private set; }

        public struct ShelfItem
        {
            public int Slot;
            public ushort Def;
            public int Count;
            public bool Spoiled;
            public Vector3 LocalPosition;
            public Quaternion LocalRotation;
        }

        private sealed class Shelf
        {
            public PropBody Prop;
            public long Cell;
            public readonly List<ShelfItem> Items = new();
        }

        /// <summary>Что изменилось на полке за сессию.</summary>
        private sealed class ShelfMemory
        {
            public bool Spilled;
            public readonly HashSet<int> Taken = new();
        }

        private readonly Dictionary<ulong, Shelf> _shelves = new();
        private readonly Dictionary<ulong, ShelfMemory> _memory = new();
        private const float BatchCell = 16f;
        private readonly Dictionary<(long, ushort), Batch> _batches = new();
        private readonly Plane[] _frustum = new Plane[6];
        private readonly List<PropItemSlot> _slots = new();
        private readonly Matrix4x4[] _drawBuffer = new Matrix4x4[1023];
        private PropManager _props;
        // Товары рисуются группами «участок 16×16×16 м × вид предмета». Новая полка дописывает свои товары сразу,
        // при удалении или изменении полки пересобирается только её участок (раньше — все группы, до 20 мс).
        private readonly Dictionary<long, List<Shelf>> _cellShelves = new();
        private readonly Dictionary<long, List<Batch>> _cellBatches = new();
        private readonly HashSet<long> _dirtyCells = new();
        private bool _dirtyAll;

        // Кэш планов уровней: пропы появляются корзинами, план одной ячейки нужен десяткам полок подряд.
        private MallLevelPlan _plan;
        private int2 _planCell;
        private int _planLevel = -1;

        public int ShelfCount => _shelves.Count;

        private void Awake() => Instance = this;

        private void Start()
        {
            _props = PropManager.Instance;
            if (_props == null)
            {
                enabled = false;
                return;
            }
            _props.PropSpawned += OnPropSpawned;
            _props.PropDespawning += OnPropDespawning;
            _props.PropWoken += OnPropWoken;
            _props.PropRemoved += OnPropRemoved;
            if (ChunkManager.Instance != null)
                ChunkManager.Instance.WorldReset += OnWorldReset;
        }

        private void OnDestroy()
        {
            if (_props != null)
            {
                _props.PropSpawned -= OnPropSpawned;
                _props.PropDespawning -= OnPropDespawning;
                _props.PropWoken -= OnPropWoken;
                _props.PropRemoved -= OnPropRemoved;
            }
            if (ChunkManager.Instance != null)
                ChunkManager.Instance.WorldReset -= OnWorldReset;
            if (_plan.IsCreated)
                _plan.Dispose();
            if (Instance == this)
                Instance = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Instance = null;

        // ───────────────────────────── Наполнение ─────────────────────────────

        private static readonly Perf.Probe FillProbe = Perf.Register("Items.ShelfLoot.Fill");

        private void OnPropSpawned(PropBody prop)
        {
            using var perf = Perf.Measure(FillProbe);
            PropMeshes.GetItemSlots(prop.Def.Shape, prop.Def.Size, _slots);
            if (_slots.Count == 0 || prop.IsAwake || PropManager.IsPlayerMade(prop.Id))
                return;
            _memory.TryGetValue(prop.Id, out var memory);
            if (memory != null && memory.Spilled)
                return;

            var system = ItemSystem.Instance;
            var table = TableFor(prop);
            if (system == null || table == null)
                return;

            uint seed = (uint)(prop.Id ^ (prop.Id >> 32));
            // Заполненность этого стеллажа: часть разграблена, остальные от «пара банок» до «почти полный».
            if (Hash.ToFloat01(Hash.Get(seed, 1)) < LootedChance)
                return;
            // Пустой биом: почти всё вынесено — изредка одна находка на полку, поэтому лут там ценен.
            bool emptyBiome = ChunkManager.Instance != null
                && ChunkManager.Instance.GetCellInfo(MallLayout.CellOf(prop.transform.position)).Biome == MallBiome.Empty;
            if (emptyBiome && Hash.ToFloat01(Hash.Get(seed, 12)) >= EmptyBiomeFindChance)
                return;
            float shelfFill = math.lerp(0.25f, 1.3f, Hash.ToFloat01(Hash.Get(seed, 2)));
            float worldAmount = ChunkManager.Instance != null && ChunkManager.Instance.Settings != null ? ChunkManager.Instance.Settings.LootAmount : 1f;
            float chance = math.saturate(table.Fill * shelfFill * worldAmount * LootScale.Value);

            var shelf = new Shelf { Prop = prop };
            for (int slot = 0; slot < _slots.Count; slot++)
            {
                if (emptyBiome && shelf.Items.Count > 0)
                    break;
                if (memory != null && memory.Taken.Contains(slot))
                    continue;
                // Шаг 1: есть ли тут вообще что-то.
                if (!emptyBiome && Hash.ToFloat01(Hash.Get(seed, slot, 3)) >= chance)
                    continue;
                // Шаг 2: что именно (несколько попыток подобрать то, что влезает на это место).
                var place = _slots[slot];
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    if (!table.Roll(Hash.Get(seed, slot, 4 + attempt), out var def, out int count))
                        break;
                    Vector3 size = def.ModelSize;
                    if (size.x > place.Size.x || size.z > place.Size.z || size.y > place.Size.y)
                    {
                        // Длинный предмет можно положить поперёк.
                        if (size.z > place.Size.x || size.x > place.Size.z || size.y > place.Size.y)
                            continue;
                    }
                    bool turned = size.x > place.Size.x;
                    float slackX = math.max(0f, place.Size.x - (turned ? size.z : size.x)) * 0.5f;
                    float offset = (Hash.ToFloat01(Hash.Get(seed, slot, 9)) - 0.5f) * slackX;
                    float yaw = (turned ? 90f : 0f) + (Hash.ToFloat01(Hash.Get(seed, slot, 10)) - 0.5f) * 16f;
                    system.Items.TryGetIndex(def.Id, out ushort index);
                    shelf.Items.Add(new ShelfItem
                    {
                        Slot = slot,
                        Def = index,
                        Count = count,
                        Spoiled = def.Category == ItemCategory.Food && Hash.ToFloat01(Hash.Get(seed, slot, 11)) < SpoiledChance,
                        LocalPosition = place.Position + new Vector3(offset, 0f, 0f),
                        LocalRotation = Quaternion.Euler(0f, yaw, 0f),
                    });
                    break;
                }
            }
            // Поваленный стеллаж (заброшенный вид): товар рассыпан по полу рядом, а не лежит на полках.
            if (prop.transform.up.y < 0.5f)
            {
                ScatterOnFloor(prop, shelf, seed);
                return;
            }
            if (shelf.Items.Count > 0)
            {
                AddShelf(prop.Id, shelf);
            }
        }

        /// <summary>Таблица лута по разделу комнаты, где стоит проп.</summary>
        private LootTable TableFor(PropBody prop)
        {
            var system = ItemSystem.Instance;
            var world = ChunkManager.Instance;
            if (system?.Loot == null || world == null || world.Sections == null)
                return system?.Loot?.Default;
            if (prop.Def.Shape == PropShape.ImpulseRack)
                return system.Loot.ForSection("impulse");
            Vector3 p = prop.transform.position;
            int2 cell = MallLayout.CellOf(p);
            int level = MallLayout.LevelAtFeet(p.y + 0.1f);
            if (!_plan.IsCreated)
                _plan = new MallLevelPlan(Allocator.Persistent);
            if (_planLevel != level || !_planCell.Equals(cell))
            {
                MallPlanner.BuildLevel(world.Seed, world.GetCellInfo(cell), level, world.Sections.Infos, ref _plan);
                _planCell = cell;
                _planLevel = level;
            }
            int2 local = math.clamp((int2)math.floor(new float2(p.x, p.z)) - MallLayout.CellOrigin(cell), 0, MallLayout.CellSize - 1);
            byte roomId = _plan.RoomIds[MallLevelPlan.Index(local.x, local.y)];
            if (roomId == 0)
                return system.Loot.Default;
            var section = world.Sections.Defs[_plan.Rooms[roomId].Section];
            return system.Loot.ForSection(section.Id);
        }

        private void OnPropDespawning(PropBody prop)
        {
            RemoveShelf(prop.Id);
        }

        private void OnPropWoken(PropBody prop) => Spill(prop);

        private void OnPropRemoved(PropBody prop, PropRemoveReason reason)
        {
            Spill(prop);
            if (reason == PropRemoveReason.Broken && prop.Def.Shape == PropShape.LootCrate && !prop.Def.IsContainer) // крафтовый ящик — хранилище, не лут
                BreakCrate(prop);
        }

        private readonly HashSet<ulong> _openedRegisters = new();

        /// <summary>Кассовый аппарат на этой стойке уже вскрыт (за сессию).</summary>
        public bool IsRegisterOpened(ulong propId) => _openedRegisters.Contains(propId);

        /// <summary>Вскрыть кассовый аппарат ломом: 1–2 вещи из таблицы «register» выпадают с прилавка. false — уже вскрыт.</summary>
        public bool OpenRegister(PropBody prop)
        {
            if (!_openedRegisters.Add(prop.Id))
                return false;
            var system = ItemSystem.Instance;
            var table = system?.Loot?.ForSection("register");
            if (table == null || WorldItems.Instance == null)
                return true;
            uint seed = (uint)(prop.Id ^ (prop.Id >> 32)) ^ 0x5E61u;
            int count = 1 + (int)(Hash.Get(seed, 1) % 2);
            Vector3 top = prop.transform.position + Vector3.up * (prop.Def.Size.y + 0.2f);
            for (int i = 0; i < count; i++)
            {
                if (!table.Roll(Hash.Get(seed, i, 2), out var def, out int amount) || !system.Items.TryGetIndex(def.Id, out ushort index))
                    continue;
                Vector3 velocity = new Vector3(Hash.ToFloat01(Hash.Get(seed, i, 3)) - 0.5f, 0.6f, Hash.ToFloat01(Hash.Get(seed, i, 4)) - 0.5f) * 1.5f;
                WorldItems.Instance.Spawn(system.Items.Create(index, amount), top, velocity);
            }
            return true;
        }

        /// <summary>Товары поваленного стеллажа — физическими предметами на полу вокруг него (один раз за сессию).</summary>
        private void ScatterOnFloor(PropBody prop, Shelf shelf, uint seed)
        {
            Memory(prop.Id).Spilled = true;
            var items = WorldItems.Instance;
            if (items == null)
                return;
            Bounds bounds = prop.Bounds;
            for (int i = 0; i < shelf.Items.Count; i++)
            {
                float u = Hash.ToFloat01(Hash.Get(seed, i, 21)) * 2f - 1f, v = Hash.ToFloat01(Hash.Get(seed, i, 22)) * 2f - 1f;
                var position = new Vector3(bounds.center.x + u * (bounds.extents.x + 0.6f), bounds.min.y + 0.15f, bounds.center.z + v * (bounds.extents.z + 0.6f));
                var rotation = Quaternion.Euler(0f, Hash.ToFloat01(Hash.Get(seed, i, 23)) * 360f, 0f);
                items.Spawn(CreateStack(shelf.Items[i]), position + Vector3.up * 0.4f, Vector3.zero, rotation, tumble: true);
            }
        }

        /// <summary>Ящик с лутом разбит: высыпается 2–4 вещи из таблицы «crate» и облако пенопластового наполнителя.</summary>
        private void BreakCrate(PropBody prop)
        {
            var system = ItemSystem.Instance;
            Bounds bounds = prop.Bounds;
            if (PropManager.Instance != null)
                DustFx.Burst(PropManager.Instance.DustMaterial, bounds, 260, new Color(0.97f, 0.97f, 0.95f, 1f), new Color(0.9f, 0.9f, 0.88f, 0.8f));
            var table = system?.Loot?.ForSection("crate");
            if (table == null || WorldItems.Instance == null)
                return;
            uint seed = (uint)(prop.Id ^ (prop.Id >> 32)) ^ 0xC4A7E5u;
            int count = 2 + (int)(Hash.Get(seed, 1) % 3);
            for (int i = 0; i < count; i++)
            {
                if (!table.Roll(Hash.Get(seed, i, 2), out var def, out int amount) || !system.Items.TryGetIndex(def.Id, out ushort index))
                    continue;
                var stack = system.Items.Create(index, amount);
                Vector3 velocity = new Vector3(Hash.ToFloat01(Hash.Get(seed, i, 3)) - 0.5f, 1.2f, Hash.ToFloat01(Hash.Get(seed, i, 4)) - 0.5f) * 2.5f;
                WorldItems.Instance.Spawn(stack, bounds.center + Vector3.up * 0.2f, velocity);
            }
        }

        /// <summary>
        /// Полку сдвинули или сломали: каждый товар становится физическим предметом ровно там, где стоял, и дальше
        /// им управляет физика — стоит на полке, пока её не наклонят или не дёрнут, а у сломанной падает.
        /// </summary>
        private void Spill(PropBody prop)
        {
            if (!_shelves.TryGetValue(prop.Id, out var shelf))
                return;
            var system = ItemSystem.Instance;
            var memory = Memory(prop.Id);
            memory.Spilled = true;
            var body = prop.Root.Body;
            foreach (var item in shelf.Items)
            {
                // Чуть выше доски, чтобы коробка предмета не начинала внутри неё.
                Vector3 position = prop.transform.TransformPoint(item.LocalPosition + Vector3.up * 0.005f);
                Vector3 velocity = body != null && !body.isKinematic ? body.GetPointVelocity(position) : Vector3.zero;
                WorldItems.Instance?.Spawn(CreateStack(item), position, velocity, prop.transform.rotation * item.LocalRotation, tumble: false);
            }
            RemoveShelf(prop.Id);
        }

        private ShelfMemory Memory(ulong id)
        {
            if (!_memory.TryGetValue(id, out var memory))
                _memory[id] = memory = new ShelfMemory();
            return memory;
        }

        private void OnWorldReset()
        {
            _shelves.Clear();
            _cellShelves.Clear();
            _memory.Clear();
            _openedRegisters.Clear();
            _planLevel = -1;
            _dirtyAll = true;
        }

        // ───────────────────────────── Сейв (этап 9) ─────────────────────────────

        public ShelvesData Capture()
        {
            var data = new ShelvesData();
            foreach (var (id, memory) in _memory)
                data.Shelves.Add(new SavedShelf { Prop = id.ToString(), Spilled = memory.Spilled, Taken = new List<int>(memory.Taken) });
            foreach (var id in _openedRegisters)
                data.OpenedRegisters.Add(id.ToString());
            return data;
        }

        /// <summary>Вернуть взятое с полок и вскрытые кассы (до появления пропов).</summary>
        public void Restore(ShelvesData data)
        {
            OnWorldReset();
            foreach (var saved in data.Shelves)
            {
                if (!ulong.TryParse(saved.Prop, out ulong id))
                    continue;
                var memory = new ShelfMemory { Spilled = saved.Spilled };
                memory.Taken.UnionWith(saved.Taken);
                _memory[id] = memory;
            }
            foreach (var register in data.OpenedRegisters)
                if (ulong.TryParse(register, out ulong id))
                    _openedRegisters.Add(id);
        }

        // ───────────────────────────── Подбор ─────────────────────────────

        /// <summary>Луч по товарам на полке: ближайший предмет, в который он попадает.</summary>
        public bool TryPick(PropBody prop, Ray ray, float maxDistance, out int itemIndex, out float distance)
        {
            itemIndex = -1;
            distance = float.MaxValue;
            if (prop == null || !_shelves.TryGetValue(prop.Id, out var shelf))
                return false;
            var registry = ItemSystem.Instance.Items;
            for (int i = 0; i < shelf.Items.Count; i++)
            {
                var item = shelf.Items[i];
                // Луч в систему координат предмета: проверка по его габариту (центр низа — опорная точка).
                var toWorld = prop.transform.localToWorldMatrix * Matrix4x4.TRS(item.LocalPosition, item.LocalRotation, Vector3.one);
                var toLocal = toWorld.inverse;
                Vector3 origin = toLocal.MultiplyPoint3x4(ray.origin);
                Vector3 direction = toLocal.MultiplyVector(ray.direction);
                Vector3 size = Vector3.Max(registry.Get(item.Def).ModelSize, Vector3.one * 0.08f);
                var bounds = new Bounds(new Vector3(0f, size.y * 0.5f, 0f), size + Vector3.one * 0.03f);
                if (bounds.IntersectRay(new Ray(origin, direction), out float hit) && hit < distance && hit <= maxDistance)
                {
                    distance = hit;
                    itemIndex = i;
                }
            }
            return itemIndex >= 0;
        }

        public bool TryGetItem(PropBody prop, int itemIndex, out ShelfItem item)
        {
            item = default;
            if (prop == null || !_shelves.TryGetValue(prop.Id, out var shelf) || itemIndex < 0 || itemIndex >= shelf.Items.Count)
                return false;
            item = shelf.Items[itemIndex];
            return true;
        }

        /// <summary>Снять предмет с полки (взятое запоминается).</summary>
        public ItemStack Take(PropBody prop, int itemIndex)
        {
            if (!TryGetItem(prop, itemIndex, out var item))
                return null;
            var shelf = _shelves[prop.Id];
            shelf.Items.RemoveAt(itemIndex);
            Memory(prop.Id).Taken.Add(item.Slot);
            if (shelf.Items.Count == 0)
                RemoveShelf(prop.Id);
            else
                _dirtyCells.Add(shelf.Cell);
            return CreateStack(item);
        }

        private static ItemStack CreateStack(in ShelfItem item)
        {
            var stack = ItemSystem.Instance.Items.Create(item.Def, item.Count);
            stack.Spoiled = item.Spoiled;
            return stack;
        }

        // ───────────────────────────── Отрисовка ─────────────────────────────

        private static readonly Perf.Probe RenderProbe = Perf.Register("Items.ShelfLoot.Render");

        private void LateUpdate()
        {
            using var perf = Perf.Measure(RenderProbe);
            var system = ItemSystem.Instance;
            if (system == null)
                return;
            if (_dirtyAll)
                RebuildBatches();
            else if (_dirtyCells.Count > 0)
            {
                foreach (long cell in _dirtyCells)
                    RebuildCell(cell);
                _dirtyCells.Clear();
            }
            var camera = Camera.main;
            if (!PropManager.Visible.Value || camera == null)
                return;

            // Товары мелкие: рисуются только участки в пределах r_itemdistance и в кадре, без теней.
            GeometryUtility.CalculateFrustumPlanes(camera, _frustum);
            Vector3 eye = camera.transform.position;
            float maxDistance = DrawDistance.Value;
            var renderParams = new RenderParams(system.Material)
            {
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = true,
                layer = GameLayers.Default,
            };
            foreach (var batch in _batches.Values)
            {
                if (batch.Matrices.Count == 0 || batch.Bounds.SqrDistance(eye) > maxDistance * maxDistance || !GeometryUtility.TestPlanesAABB(_frustum, batch.Bounds))
                    continue;
                var mesh = system.Items.GetMesh(batch.Def);
                renderParams.worldBounds = batch.Bounds;
                for (int start = 0; start < batch.Matrices.Count; start += _drawBuffer.Length)
                {
                    int count = Mathf.Min(_drawBuffer.Length, batch.Matrices.Count - start);
                    batch.Matrices.CopyTo(start, _drawBuffer, 0, count);
                    Graphics.RenderMeshInstanced(renderParams, mesh, 0, _drawBuffer, count);
                }
            }
        }

        /// <summary>Группы отрисовки: предметы одного вида на участке BatchCell×BatchCell м.</summary>
        private void RebuildBatches()
        {
            _dirtyAll = false;
            _dirtyCells.Clear();
            foreach (var batch in _batches.Values)
            {
                batch.Matrices.Clear();
                batch.HasBounds = false;
            }
            foreach (var shelf in _shelves.Values)
                AppendBatches(shelf);
        }

        private void RebuildCell(long cell)
        {
            if (_cellBatches.TryGetValue(cell, out var batches))
                foreach (var batch in batches)
                {
                    batch.Matrices.Clear();
                    batch.HasBounds = false;
                }
            if (_cellShelves.TryGetValue(cell, out var shelves))
                foreach (var shelf in shelves)
                    AppendBatches(shelf);
        }

        private void AddShelf(ulong id, Shelf shelf)
        {
            Vector3 p = shelf.Prop.transform.position;
            shelf.Cell = ((long)Mathf.FloorToInt(p.x / BatchCell) << 42) ^ ((long)Mathf.FloorToInt(p.y / BatchCell) << 21) ^ Mathf.FloorToInt(p.z / BatchCell);
            _shelves[id] = shelf;
            if (!_cellShelves.TryGetValue(shelf.Cell, out var list))
                _cellShelves[shelf.Cell] = list = new List<Shelf>();
            list.Add(shelf);
            AppendBatches(shelf);
        }

        private void RemoveShelf(ulong id)
        {
            if (!_shelves.Remove(id, out var shelf))
                return;
            if (_cellShelves.TryGetValue(shelf.Cell, out var list))
                list.Remove(shelf);
            _dirtyCells.Add(shelf.Cell);
        }

        private void AppendBatches(Shelf shelf)
        {
            var toWorld = shelf.Prop.transform.localToWorldMatrix;
            long cell = shelf.Cell;
            foreach (var item in shelf.Items)
            {
                var key = (cell, item.Def);
                if (!_batches.TryGetValue(key, out var batch))
                {
                    _batches[key] = batch = new Batch { Def = item.Def };
                    if (!_cellBatches.TryGetValue(cell, out var cellBatches))
                        _cellBatches[cell] = cellBatches = new List<Batch>();
                    cellBatches.Add(batch);
                }
                var matrix = toWorld * Matrix4x4.TRS(item.LocalPosition, item.LocalRotation, Vector3.one);
                batch.Matrices.Add(matrix);
                var bounds = new Bounds(matrix.GetPosition(), Vector3.one);
                if (batch.HasBounds)
                    batch.Bounds.Encapsulate(bounds);
                else
                    batch.Bounds = bounds;
                batch.HasBounds = true;
            }
        }

        private sealed class Batch
        {
            public ushort Def;
            public Bounds Bounds;
            public bool HasBounds;
            public readonly List<Matrix4x4> Matrices = new();
        }

        [ConsoleCommand("loot_info", "Полки с товарами рядом и сколько на них предметов.")]
        private static string LootInfo()
        {
            var loot = Instance;
            if (loot == null)
                return "Нет ShelfLoot.";
            int items = 0;
            foreach (var shelf in loot._shelves.Values)
                items += shelf.Items.Count;
            return $"Полок с товарами: {loot._shelves.Count}, предметов на них: {items}, взятых или рассыпанных полок за сессию: {loot._memory.Count}";
        }
    }

    [System.Serializable]
    public sealed class SavedShelf
    {
        public string Prop;
        public bool Spilled;
        public List<int> Taken = new();
    }

    [System.Serializable]
    public sealed class ShelvesData
    {
        public List<SavedShelf> Shelves = new();
        public List<string> OpenedRegisters = new();
    }
}
