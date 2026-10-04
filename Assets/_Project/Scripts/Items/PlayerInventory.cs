using System;
using System.Collections.Generic;
using UnityEngine;
using VoidMall.Console;
using VoidMall.Core;
using VoidMall.World;

namespace VoidMall.Items
{
    /// <summary>Игрок съел, выпил или применил предмет. Эффекты — модуль выживания (этап 7).</summary>
    public struct ItemConsumedEvent
    {
        public GameObject Player;
        public ItemDef Item;
        public bool Spoiled;
    }

    /// <summary>
    /// Инвентарь игрока (GDD §5): карманы (маленькая сетка), рюкзак (сетка из надетого рюкзака), экипировка, хотбар.
    /// Получает добычу (блоки, гладкий материал, разбор пропов), считает вес и замедляет перегруженного игрока.
    /// </summary>
    public sealed class PlayerInventory : MonoBehaviour, IMovementModifierSource
    {
        public static readonly CVar<float> CarryCapacity = new("sv_carry_capacity", 30f, "Сколько килограммов игрок несёт без штрафов.", ConsoleFlags.Cheat);

        public const int PocketsWidth = 7;
        public const int PocketsHeight = 2;

        /// <summary>Добыча дальше этого расстояния (обломки обрушений) падает в мир, а не в карманы.</summary>
        private const float DirectPickupDistance = 6f;

        public static PlayerInventory Local { get; private set; }

        public GridInventory Pockets { get; private set; }
        public Equipment Equipment { get; } = new();
        public Hotbar Hotbar { get; } = new();
        public ItemRegistry Items { get; private set; }

        /// <summary>Сетка надетого рюкзака (или null).</summary>
        public GridInventory Backpack => Equipment.Backpack?.Contents;

        /// <summary>Что-то в инвентаре изменилось (сетки, экипировка, хотбар).</summary>
        public event Action Changed;

        private readonly Dictionary<ushort, float> _fractions = new();
        private GridInventory _subscribedBackpack;

        public ItemStack ActiveStack
        {
            get
            {
                var stack = Hotbar.SelectedStack;
                return stack != null && stack.Count > 0 && Owns(stack) ? stack : null;
            }
        }

        public ItemDef ActiveItem => ActiveStack != null ? Items.Get(ActiveStack.Def) : null;

        private void Start()
        {
            var system = ItemSystem.Instance;
            if (system == null || system.Items == null)
            {
                enabled = false;
                return;
            }
            Items = system.Items;
            Pockets = new GridInventory(Items, PocketsWidth, PocketsHeight);
            Pockets.Changed += OnContentsChanged;
            Equipment.Changed += OnEquipmentChanged;
            Hotbar.Changed += RaiseChanged;
            Local = this;

            EventBus<MaterialMinedEvent>.Subscribe(OnMined);
            EventBus<PropSalvagedEvent>.Subscribe(OnSalvaged);
            RaiseChanged();
        }

        private void OnDestroy()
        {
            EventBus<MaterialMinedEvent>.Unsubscribe(OnMined);
            EventBus<PropSalvagedEvent>.Unsubscribe(OnSalvaged);
            if (Local == this)
                Local = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Local = null;

        // ───────────────────────────── Состав ─────────────────────────────

        /// <summary>Стак лежит в карманах, рюкзаке или надет.</summary>
        public bool Owns(ItemStack stack) =>
            stack != null && (Pockets.Contains(stack) || (Backpack != null && Backpack.Contains(stack)) || Equipment.Contains(stack));

        /// <summary>Сетка, в которой лежит стак (или null).</summary>
        public GridInventory GridOf(ItemStack stack)
        {
            if (Pockets.Contains(stack))
                return Pockets;
            if (Backpack != null && Backpack.Contains(stack))
                return Backpack;
            return null;
        }

        public int CountOf(ushort def) => Pockets.CountOf(def) + (Backpack?.CountOf(def) ?? 0);

        public float TotalWeight() => Pockets.TotalWeight() + Equipment.TotalWeight(Items);

        public float LoadFraction => TotalWeight() / Mathf.Max(1f, CarryCapacity.Value);

        // ───────────────────────────── Изменения ─────────────────────────────

        /// <summary>
        /// Подобрать: сначала рюкзак, потом карманы. Инструменты и блоки сами занимают свободные слоты хотбара.
        /// Одежда, броня и рюкзак сразу надеваются, если их слот пуст (autoEquip). Возвращает, сколько не поместилось.
        /// </summary>
        public int Add(ItemStack stack, bool autoEquip = true)
        {
            if (stack.Contents != null && Items.Get(stack.Def).UnpackOnPickup)
                return Unpack(stack);
            if (autoEquip && TryAutoEquip(stack))
                return 0;
            int before = stack.Count;
            int left = Backpack != null ? Backpack.TryAdd(stack) : stack.Count;
            if (left > 0)
                left = Pockets.TryAdd(stack);
            if (left < before)
                AutoAssignHotbar(stack.Def);
            RaiseChanged();
            return left;
        }

        public int Add(ushort def, int count) => Add(Items.Create(def, count));

        private readonly List<ItemStack> _unpack = new();

        /// <summary>Сумка с вещами: содержимое — в инвентарь (одежда и рюкзак надеваются), что не влезло — остаётся в сумке.</summary>
        private int Unpack(ItemStack bag)
        {
            _unpack.Clear();
            foreach (var placement in bag.Contents.Items)
                _unpack.Add(placement.Stack);
            foreach (var inner in _unpack)
            {
                bag.Contents.Remove(inner);
                if (Add(inner) > 0)
                    bag.Contents.TryAdd(inner);
            }
            RaiseChanged();
            return bag.Contents.Items.Count > 0 ? 1 : 0;
        }

        private bool TryAutoEquip(ItemStack stack)
        {
            var def = Items.Get(stack.Def);
            if (!def.IsEquippable || stack.Count != 1 || Equipment.Get(def.Slot, def.Layer) != null)
                return false;
            Equipment.Set(def.Slot, def.Layer, stack);
            RaiseChanged();
            return true;
        }

        /// <summary>Списать предметы (сначала из карманов). В творческом режиме — бесплатно.</summary>
        public bool TryConsume(ushort def, int count, bool respectCreative = true)
        {
            if (respectCreative && ItemSystem.Creative.Value)
                return true;
            if (CountOf(def) < count)
                return false;
            int fromPockets = Mathf.Min(Pockets.CountOf(def), count);
            if (fromPockets > 0)
                Pockets.TryConsume(def, fromPockets);
            if (count - fromPockets > 0)
                Backpack.TryConsume(def, count - fromPockets);
            Hotbar.Prune(Owns);
            RaiseChanged();
            return true;
        }

        /// <summary>Убрать стак откуда бы то ни было (сетка, экипировка). Хотбар чистится сам.</summary>
        public bool Remove(ItemStack stack)
        {
            bool removed = Pockets.Remove(stack) || (Backpack != null && Backpack.Remove(stack)) || Equipment.Remove(stack);
            if (removed)
            {
                Hotbar.Prune(Owns);
                RaiseChanged();
            }
            return removed;
        }

        /// <summary>Выбросить стак перед игроком — он падает физическим предметом.</summary>
        public void Drop(ItemStack stack)
        {
            if (!Remove(stack))
                return;
            var camera = Camera.main;
            Vector3 origin = camera != null ? camera.transform.position + camera.transform.forward * 0.6f : transform.position + Vector3.up;
            Vector3 velocity = camera != null ? camera.transform.forward * 2.5f : Vector3.zero;
            WorldItems.Instance?.Spawn(stack, origin, velocity);
        }

        /// <summary>Надеть (из сетки). Снятое возвращается в сетку, а если не влезло — падает в мир.</summary>
        public bool Equip(ItemStack stack)
        {
            var def = Items.Get(stack.Def);
            if (!def.IsEquippable)
                return false;
            var grid = GridOf(stack);
            if (grid == null)
                return false;
            // Рюкзак нельзя надеть «из самого себя».
            if (def.Slot == EquipSlot.Backpack && grid == Backpack)
                return false;
            grid.Remove(stack);
            var previous = Equipment.Set(def.Slot, def.Layer, stack);
            if (previous != null && Add(previous, autoEquip: false) > 0)
                Drop(previous, fromEquipment: false);
            RaiseChanged();
            return true;
        }

        /// <summary>Снять в карманы или рюкзак.</summary>
        public bool Unequip(ItemStack stack)
        {
            if (!Equipment.Contains(stack))
                return false;
            Equipment.Remove(stack);
            // Рюкзак снимается в карманы (в нём самом лежать он не может).
            int left = Equipment.Backpack == null && stack.Contents != null ? Pockets.TryAdd(stack) : Add(stack, autoEquip: false);
            if (left > 0)
                Drop(stack, fromEquipment: false);
            RaiseChanged();
            return true;
        }

        private void Drop(ItemStack stack, bool fromEquipment)
        {
            var camera = Camera.main;
            Vector3 origin = camera != null ? camera.transform.position + camera.transform.forward * 0.6f : transform.position + Vector3.up;
            WorldItems.Instance?.Spawn(stack, origin, camera != null ? camera.transform.forward * 1.5f : Vector3.zero);
        }

        /// <summary>Съесть, выпить, применить (1 штуку).</summary>
        /// <summary>
        /// Проверка перед использованием (выживание: бинт без кровотечения не тратится). Вернуть текст отказа или null.
        /// </summary>
        public static System.Func<PlayerInventory, ItemDef, ItemStack, string> UseBlocker;

        /// <summary>Почему последнее использование не удалось (для подсказки), null — удалось или не проверялось.</summary>
        public string LastRefusal { get; private set; }

        public bool Use(ItemStack stack)
        {
            var def = Items.Get(stack.Def);
            if (def.IsEquippable)
                return Equipment.Contains(stack) ? Unequip(stack) : Equip(stack);
            if (!def.IsConsumable)
                return false;
            LastRefusal = UseBlocker?.Invoke(this, def, stack);
            if (LastRefusal != null)
                return false;
            var grid = GridOf(stack);
            if (grid == null)
                return false;
            stack.Count--;
            if (stack.Count <= 0)
                grid.Remove(stack);
            else
                grid.NotifyChanged();
            Hotbar.Prune(Owns);
            EventBus<ItemConsumedEvent>.Publish(new ItemConsumedEvent { Player = gameObject, Item = def, Spoiled = stack.Spoiled });
            RaiseChanged();
            return true;
        }

        private void AutoAssignHotbar(ushort def)
        {
            var item = Items.Get(def);
            if (!item.IsTool && item.PlacesBlock == null && !item.IsConsumable)
                return;
            // Уже есть в хотбаре предмет того же вида — не дублируем.
            for (int i = 0; i < Hotbar.Size; i++)
                if (Hotbar[i] != null && Hotbar[i].Def == def && Owns(Hotbar[i]))
                    return;
            int free = Hotbar.FirstFree();
            if (free < 0)
                return;
            var stack = FindStack(def);
            if (stack != null)
                Hotbar.Assign(free, stack);
        }

        /// <summary>Любой стак этого вида в инвентаре.</summary>
        public ItemStack FindStack(ushort def)
        {
            foreach (var placement in Pockets.Items)
                if (placement.Stack.Def == def)
                    return placement.Stack;
            if (Backpack != null)
                foreach (var placement in Backpack.Items)
                    if (placement.Stack.Def == def)
                        return placement.Stack;
            return null;
        }

        /// <summary>Если стак в хотбаре кончился, но такие предметы ещё есть — подставить следующий стак.</summary>
        public void RefillHotbar(int slot, ushort def)
        {
            if (Hotbar[slot] != null && Owns(Hotbar[slot]))
                return;
            var next = FindStack(def);
            if (next != null)
                Hotbar.Assign(slot, next);
        }

        private void OnContentsChanged() => RaiseChanged();

        private void OnEquipmentChanged()
        {
            if (_subscribedBackpack != null)
                _subscribedBackpack.Changed -= OnContentsChanged;
            _subscribedBackpack = Backpack;
            if (_subscribedBackpack != null)
                _subscribedBackpack.Changed += OnContentsChanged;
            Hotbar.Prune(Owns);
            RaiseChanged();
        }

        private void RaiseChanged() => Changed?.Invoke();

        // ───────────────────────────── Добыча ─────────────────────────────

        private void OnMined(in MaterialMinedEvent evt)
        {
            var world = ChunkManager.Instance;
            if (world == null || Items == null)
                return;
            var material = world.Materials.Get(evt.Material);
            ushort item;
            bool found = material.Kind == MaterialKind.Smooth
                ? Items.TryGetSmoothItem(material.Id, out item) || Items.TryGetIndex("material.concrete_rubble", out item)
                : Items.TryGetBlockItem(material.Id, out item);
            if (!found)
                return;

            // Гладкий материал копится по кубометрам, блоки — штуками.
            _fractions.TryGetValue(item, out float amount);
            amount += evt.Amount;
            int whole = (int)amount;
            _fractions[item] = amount - whole;
            if (whole <= 0)
                return;

            var stack = Items.Create(item, whole);
            if (Vector3.Distance(evt.Position, transform.position) > DirectPickupDistance)
            {
                WorldItems.Instance?.Spawn(stack, evt.Position + Vector3.up * 0.3f, Vector3.up);
                return;
            }
            if (Add(stack) > 0)
                WorldItems.Instance?.Spawn(stack, evt.Position + Vector3.up * 0.3f, Vector3.up);
        }

        private void OnSalvaged(in PropSalvagedEvent evt)
        {
            foreach (var yield in evt.Def.Yields)
            {
                int amount = evt.Disassembled ? yield.Disassembled : yield.Broken;
                if (amount <= 0 || !Items.TryGetIndex(yield.Material, out ushort item))
                    continue;
                var stack = Items.Create(item, amount);
                if (Add(stack) > 0)
                    WorldItems.Instance?.Spawn(stack, evt.Position, Vector3.up);
            }
        }

        // ───────────────────────────── Вес ─────────────────────────────

        public void ApplyModifiers(ref MovementModifiers modifiers)
        {
            if (Pockets != null)
                MovementRules.ApplyEncumbrance(ref modifiers, LoadFraction);
        }
    }
}
