using System.Collections.Generic;
using UnityEngine;
using VoidMall.Core;
using VoidMall.World;

namespace VoidMall.Items
{
    /// <summary>
    /// Хранилища (этап 8Ж): пропы с сеткой (PropDef.ContainerGrid) — ящики и шкафы игрока, шкафы и столы мола, холодильники.
    /// Содержимое — GridInventory по ID пропа: переживает перенос и выгрузку пропа (на этапе 9 уходит в сейв). У найденных в
    /// моле содержимое появляется при первом открытии из таблицы лута (детерминированно по ID). Вес содержимого прибавляется
    /// к массе пропа: полный ящик в руки не возьмёшь. Сломали или разобрали — всё вываливается на пол.
    /// </summary>
    [DefaultExecutionOrder(-140)]
    public sealed class ContainerSystem : MonoBehaviour
    {
        /// <summary>Доля найденных хранилищ, которые уже пусты.</summary>
        private const float EmptyChance = 0.3f;

        /// <summary>В пустом биоме — почти всё вынесено.</summary>
        private const float EmptyBiomeEmptyChance = 0.9f;

        private const float SpoiledChance = 0.25f;

        public static ContainerSystem Instance { get; private set; }

        private readonly Dictionary<ulong, GridInventory> _contents = new();
        private PropManager _props;

        public int Count => _contents.Count;

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
            _props.PropRemoved += OnPropRemoved;
            if (ChunkManager.Instance != null)
                ChunkManager.Instance.WorldReset += OnWorldReset;
        }

        private void OnDestroy()
        {
            if (_props != null)
            {
                _props.PropSpawned -= OnPropSpawned;
                _props.PropRemoved -= OnPropRemoved;
            }
            if (ChunkManager.Instance != null)
                ChunkManager.Instance.WorldReset -= OnWorldReset;
            if (Instance == this)
                Instance = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Instance = null;

        public static bool IsContainer(PropBody prop) => prop != null && prop.Voxel == null && prop.Def.IsContainer;

        /// <summary>Содержимое хранилища (создаётся при первом обращении; у найденных — с лутом). Null — не хранилище.</summary>
        public GridInventory Open(PropBody prop)
        {
            if (!IsContainer(prop) || ItemSystem.Instance == null)
                return null;
            if (_contents.TryGetValue(prop.Id, out var grid))
                return grid;
            var def = prop.Def;
            grid = new GridInventory(ItemSystem.Instance.Items, def.ContainerGrid.x, def.ContainerGrid.y) { Cold = def.ContainerCold };
            Fill(prop, grid);
            Track(prop.Id, grid);
            return grid;
        }

        public bool TryGet(ulong propId, out GridInventory grid) => _contents.TryGetValue(propId, out grid);

        /// <summary>Вернуть содержимое из сейва (этап 9).</summary>
        public void Restore(ulong propId, GridInventory grid) => Track(propId, grid);

        public IEnumerable<KeyValuePair<ulong, GridInventory>> All => _contents;

        private void Track(ulong id, GridInventory grid)
        {
            _contents[id] = grid;
            grid.Changed += () => UpdateMass(id);
            UpdateMass(id);
        }

        /// <summary>Лут найденного хранилища: часть пустая, остальные — несколько попыток по таблице.</summary>
        private void Fill(PropBody prop, GridInventory grid)
        {
            var def = prop.Def;
            var system = ItemSystem.Instance;
            if (string.IsNullOrEmpty(def.ContainerLoot) || PropManager.IsPlayerMade(prop.Id) || system.Loot == null)
                return;
            var table = system.Loot.ForSection(def.ContainerLoot);
            if (table == null)
                return;
            uint seed = (uint)(prop.Id ^ (prop.Id >> 32)) ^ 0xC0A7u;
            var world = ChunkManager.Instance;
            bool emptyBiome = world != null && world.GetCellInfo(MallLayout.CellOf(prop.transform.position)).Biome == MallBiome.Empty;
            if (Hash.ToFloat01(Hash.Get(seed, 1)) < (emptyBiome ? EmptyBiomeEmptyChance : EmptyChance))
                return;
            float amount = world != null && world.Settings != null ? world.Settings.LootAmount : 1f;
            int attempts = Mathf.Max(2, grid.Width * grid.Height / 6);
            for (int i = 0; i < attempts; i++)
            {
                if (Hash.ToFloat01(Hash.Get(seed, i, 2)) >= table.Fill * amount * ShelfLoot.LootScale.Value)
                    continue;
                if (!table.Roll(Hash.Get(seed, i, 3), out var item, out int count) || !system.Items.TryGetIndex(item.Id, out ushort index))
                    continue;
                var stack = system.Items.Create(index, count);
                if ((item.Category is ItemCategory.Food or ItemCategory.Drink) && item.ShelfLifeHours > 0f && Hash.ToFloat01(Hash.Get(seed, i, 4)) < SpoiledChance)
                    stack.Spoiled = true;
                grid.TryAdd(stack);
            }
        }

        private void UpdateMass(ulong id)
        {
            if (_props == null || !_contents.TryGetValue(id, out var grid) || !_props.TryGetLive(id, out var body))
                return;
            body.ExtraMass = grid.TotalWeight();
            body.RecalculateMass();
        }

        private void OnPropSpawned(PropBody prop)
        {
            if (_contents.TryGetValue(prop.Id, out var grid))
                prop.ExtraMass = grid.TotalWeight();
        }

        /// <summary>Хранилище сломали или разобрали: содержимое вываливается рядом.</summary>
        private void OnPropRemoved(PropBody prop, PropRemoveReason reason)
        {
            if (!_contents.TryGetValue(prop.Id, out var grid))
                return;
            _contents.Remove(prop.Id);
            prop.ExtraMass = 0f;
            if (WorldItems.Instance == null)
                return;
            var bounds = prop.Bounds;
            var stacks = new List<ItemStack>();
            foreach (var placement in grid.Items)
                stacks.Add(placement.Stack);
            foreach (var stack in stacks)
            {
                grid.Remove(stack);
                var offset = new Vector3(Random.Range(-0.5f, 0.5f) * bounds.size.x, 0f, Random.Range(-0.5f, 0.5f) * bounds.size.z);
                WorldItems.Instance.Spawn(stack, bounds.center + offset, Random.insideUnitSphere * 1.5f + Vector3.up);
            }
        }

        // ───────────────────────────── Сейв (этап 9) ─────────────────────────────

        public ContainersData Capture()
        {
            var data = new ContainersData();
            var items = ItemSystem.Instance.Items;
            foreach (var (id, grid) in _contents)
            {
                var saved = new SavedContainer { Prop = id.ToString(), Width = grid.Width, Height = grid.Height, Cold = grid.Cold };
                ItemSnapshot.WriteGrid(grid, ItemSnapshot.Contents, -1, items, saved.Stacks);
                data.Containers.Add(saved);
            }
            return data;
        }

        public void Restore(ContainersData data)
        {
            _contents.Clear();
            var items = ItemSystem.Instance.Items;
            foreach (var saved in data.Containers)
            {
                if (!ulong.TryParse(saved.Prop, out ulong id) || saved.Width <= 0 || saved.Height <= 0)
                    continue;
                var grid = new GridInventory(items, saved.Width, saved.Height) { Cold = saved.Cold };
                var stacks = ItemSnapshot.ReadAll(saved.Stacks, items);
                foreach (var entry in saved.Stacks)
                    if (entry.Parent < 0 && stacks.TryGetValue(entry.Id, out var stack))
                        ItemSnapshot.PlaceIn(grid, stack, entry);
                Track(id, grid);
            }
        }

        private void OnWorldReset() => _contents.Clear();
    }

    [System.Serializable]
    public sealed class SavedContainer
    {
        public string Prop;
        public int Width, Height;
        public bool Cold;
        public List<SavedStack> Stacks = new();
    }

    [System.Serializable]
    public sealed class ContainersData
    {
        public List<SavedContainer> Containers = new();
    }
}
