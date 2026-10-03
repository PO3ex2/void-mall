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

        public static ShelfLoot Instance { get; private set; }

        public struct ShelfItem
        {
            public int Slot;
            public ushort Def;
            public int Count;
            public Vector3 LocalPosition;
            public Quaternion LocalRotation;
        }

        private sealed class Shelf
        {
            public PropBody Prop;
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
        private bool _dirty;

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

        private void OnPropSpawned(PropBody prop)
        {
            PropMeshes.GetItemSlots(prop.Def.Shape, prop.Def.Size, _slots);
            if (_slots.Count == 0 || prop.IsAwake)
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
            float shelfFill = math.lerp(0.25f, 1.3f, Hash.ToFloat01(Hash.Get(seed, 2)));
            float worldAmount = ChunkManager.Instance != null && ChunkManager.Instance.Settings != null ? ChunkManager.Instance.Settings.LootAmount : 1f;
            float chance = math.saturate(table.Fill * shelfFill * worldAmount * LootScale.Value);

            var shelf = new Shelf { Prop = prop };
            for (int slot = 0; slot < _slots.Count; slot++)
            {
                if (memory != null && memory.Taken.Contains(slot))
                    continue;
                // Шаг 1: есть ли тут вообще что-то.
                if (Hash.ToFloat01(Hash.Get(seed, slot, 3)) >= chance)
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
                        LocalPosition = place.Position + new Vector3(offset, 0f, 0f),
                        LocalRotation = Quaternion.Euler(0f, yaw, 0f),
                    });
                    break;
                }
            }
            if (shelf.Items.Count > 0)
            {
                _shelves[prop.Id] = shelf;
                _dirty = true;
            }
        }

        /// <summary>Таблица лута по разделу комнаты, где стоит проп.</summary>
        private LootTable TableFor(PropBody prop)
        {
            var system = ItemSystem.Instance;
            var world = ChunkManager.Instance;
            if (system?.Loot == null || world == null || world.Sections == null)
                return system?.Loot?.Default;
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
            if (_shelves.Remove(prop.Id))
                _dirty = true;
        }

        private void OnPropWoken(PropBody prop) => Spill(prop);

        private void OnPropRemoved(PropBody prop, PropRemoveReason reason) => Spill(prop);

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
                WorldItems.Instance?.Spawn(system.Items.Create(item.Def, item.Count), position, velocity, prop.transform.rotation * item.LocalRotation, tumble: false);
            }
            _shelves.Remove(prop.Id);
            _dirty = true;
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
            _memory.Clear();
            _planLevel = -1;
            _dirty = true;
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
                _shelves.Remove(prop.Id);
            _dirty = true;
            return ItemSystem.Instance.Items.Create(item.Def, item.Count);
        }

        // ───────────────────────────── Отрисовка ─────────────────────────────

        private void LateUpdate()
        {
            var system = ItemSystem.Instance;
            if (system == null)
                return;
            if (_dirty)
                RebuildBatches();
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
            _dirty = false;
            foreach (var batch in _batches.Values)
            {
                batch.Matrices.Clear();
                batch.HasBounds = false;
            }
            foreach (var shelf in _shelves.Values)
            {
                var toWorld = shelf.Prop.transform.localToWorldMatrix;
                Vector3 p = shelf.Prop.transform.position;
                long cell = ((long)Mathf.FloorToInt(p.x / BatchCell) << 42) ^ ((long)Mathf.FloorToInt(p.y / BatchCell) << 21) ^ Mathf.FloorToInt(p.z / BatchCell);
                foreach (var item in shelf.Items)
                {
                    var key = (cell, item.Def);
                    if (!_batches.TryGetValue(key, out var batch))
                        _batches[key] = batch = new Batch { Def = item.Def };
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
}
