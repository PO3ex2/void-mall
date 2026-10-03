using System;

namespace VoidMall.Items
{
    /// <summary>
    /// Экипировка (GDD §5.1): голова, торс, руки, ноги, ступни — у каждого слой брони и слой одежды, плюс рюкзак.
    /// Рюкзак — контейнер: его сетка становится второй сеткой инвентаря.
    /// </summary>
    public sealed class Equipment
    {
        public const int BodySlots = 5;

        private readonly ItemStack[] _armor = new ItemStack[BodySlots];
        private readonly ItemStack[] _clothing = new ItemStack[BodySlots];

        public ItemStack Backpack { get; private set; }

        public event Action Changed;

        public static int SlotIndex(EquipSlot slot) => slot switch
        {
            EquipSlot.Head => 0,
            EquipSlot.Torso => 1,
            EquipSlot.Arms => 2,
            EquipSlot.Legs => 3,
            EquipSlot.Feet => 4,
            _ => -1,
        };

        public ItemStack Get(EquipSlot slot, EquipLayer layer)
        {
            if (slot == EquipSlot.Backpack)
                return Backpack;
            int index = SlotIndex(slot);
            if (index < 0)
                return null;
            return layer == EquipLayer.Armor ? _armor[index] : _clothing[index];
        }

        /// <summary>Надеть вместо текущего. Возвращает снятый предмет (или null).</summary>
        public ItemStack Set(EquipSlot slot, EquipLayer layer, ItemStack stack)
        {
            ItemStack previous;
            if (slot == EquipSlot.Backpack)
            {
                previous = Backpack;
                Backpack = stack;
            }
            else
            {
                int index = SlotIndex(slot);
                if (index < 0)
                    return stack;
                var array = layer == EquipLayer.Armor ? _armor : _clothing;
                previous = array[index];
                array[index] = stack;
            }
            Changed?.Invoke();
            return previous;
        }

        public bool Contains(ItemStack stack)
        {
            if (stack == null)
                return false;
            if (Backpack == stack)
                return true;
            for (int i = 0; i < BodySlots; i++)
                if (_armor[i] == stack || _clothing[i] == stack)
                    return true;
            return false;
        }

        /// <summary>Снять предмет из любого слота.</summary>
        public bool Remove(ItemStack stack)
        {
            if (stack == null)
                return false;
            if (Backpack == stack)
            {
                Backpack = null;
                Changed?.Invoke();
                return true;
            }
            for (int i = 0; i < BodySlots; i++)
            {
                if (_armor[i] == stack)
                    _armor[i] = null;
                else if (_clothing[i] == stack)
                    _clothing[i] = null;
                else
                    continue;
                Changed?.Invoke();
                return true;
            }
            return false;
        }

        public float TotalWeight(IItemCatalog catalog)
        {
            float weight = 0f;
            for (int i = 0; i < BodySlots; i++)
            {
                if (_armor[i] != null) weight += catalog.Weight(_armor[i].Def);
                if (_clothing[i] != null) weight += catalog.Weight(_clothing[i].Def);
            }
            if (Backpack != null)
            {
                weight += catalog.Weight(Backpack.Def);
                if (Backpack.Contents != null)
                    weight += Backpack.Contents.TotalWeight();
            }
            return weight;
        }
    }

    /// <summary>Хотбар на 9 слотов: ссылки на стаки в инвентаре (не копии).</summary>
    public sealed class Hotbar
    {
        public const int Size = 9;

        private readonly ItemStack[] _slots = new ItemStack[Size];

        public int Selected { get; private set; }
        public event Action Changed;

        public ItemStack this[int index] => _slots[index];

        public ItemStack SelectedStack => _slots[Selected];

        public void Select(int index)
        {
            index = (index % Size + Size) % Size;
            if (index == Selected)
                return;
            Selected = index;
            Changed?.Invoke();
        }

        public void Assign(int index, ItemStack stack)
        {
            // Один стак — в одном слоте.
            for (int i = 0; i < Size; i++)
                if (_slots[i] == stack)
                    _slots[i] = null;
            _slots[index] = stack;
            Changed?.Invoke();
        }

        public int IndexOf(ItemStack stack)
        {
            for (int i = 0; i < Size; i++)
                if (_slots[i] == stack)
                    return i;
            return -1;
        }

        public int FirstFree()
        {
            for (int i = 0; i < Size; i++)
                if (_slots[i] == null)
                    return i;
            return -1;
        }

        /// <summary>Убрать ссылки на стаки, которых больше нет в инвентаре.</summary>
        public void Prune(Func<ItemStack, bool> isOwned)
        {
            bool changed = false;
            for (int i = 0; i < Size; i++)
            {
                if (_slots[i] != null && (_slots[i].Count <= 0 || !isOwned(_slots[i])))
                {
                    _slots[i] = null;
                    changed = true;
                }
            }
            if (changed)
                Changed?.Invoke();
        }
    }
}
