using System;
using System.Collections.Generic;

namespace VoidMall.Items
{
    /// <summary>Что снимку нужно о предметах: строковые ID (не индексы — порядок реестра может меняться) и создание стака.</summary>
    public interface IItemIds
    {
        string IdOf(ushort def);
        bool TryGetIndex(string id, out ushort def);
        ItemStack Create(ushort def, int count);
    }

    /// <summary>
    /// Стак в сейве (этап 8Ж). Вложенность (содержимое рюкзака) — плоским списком: у вложенного Parent = Id родителя,
    /// поэтому JSON не рекурсивный. Place — где лежит: "pockets", "equip:Torso:Armor", "contents" (в родителе) и т. п.
    /// </summary>
    [Serializable]
    public sealed class SavedStack
    {
        public int Id;
        public int Parent = -1;
        public string Place;
        public int X, Y;
        public bool Rotated;
        public string Item;
        public int Count = 1;
        public float Durability = 1f;
        public bool Spoiled;
        public double SpoilAt;
        public bool Chilled;
    }

    /// <summary>Инвентарь игрока в сейве: карманы, экипировка (с рюкзаком и его содержимым), хотбар.</summary>
    [Serializable]
    public sealed class InventoryData
    {
        public List<SavedStack> Stacks = new();

        /// <summary>Id стака в каждом слоте хотбара (−1 — пусто).</summary>
        public List<int> Hotbar = new();
        public int Selected;
    }

    /// <summary>Запись и чтение предметов для снимков игрока и (на этапе 9) хранилищ мира.</summary>
    public static class ItemSnapshot
    {
        public const string Pockets = "pockets";
        public const string Contents = "contents";
        public const string EquipPrefix = "equip:";

        /// <summary>Записать сетку: каждый стак — строка с местом и координатами, содержимое контейнеров — следом.</summary>
        public static void WriteGrid(GridInventory grid, string place, int parent, IItemIds items, List<SavedStack> output, Dictionary<ItemStack, int> ids = null)
        {
            foreach (var placement in grid.Items)
            {
                var saved = WriteStack(placement.Stack, place, parent, items, output, ids);
                saved.X = placement.X;
                saved.Y = placement.Y;
                saved.Rotated = placement.Rotated;
            }
        }

        public static SavedStack WriteStack(ItemStack stack, string place, int parent, IItemIds items, List<SavedStack> output, Dictionary<ItemStack, int> ids = null)
        {
            var saved = new SavedStack
            {
                Id = output.Count,
                Parent = parent,
                Place = place,
                Item = items.IdOf(stack.Def),
                Count = stack.Count,
                Durability = stack.Durability,
                Spoiled = stack.SpoiledFlag,
                SpoilAt = stack.SpoilAt,
                Chilled = stack.Chilled,
            };
            output.Add(saved);
            if (ids != null)
                ids[stack] = saved.Id;
            if (stack.Contents != null)
                WriteGrid(stack.Contents, Contents, saved.Id, items, output, ids);
            return saved;
        }

        /// <summary>
        /// Создать все стаки и разложить вложенные по родителям. Неизвестные предметы (убраны из игры) пропускаются и
        /// попадают в unknown. Возвращает Id → стак.
        /// </summary>
        public static Dictionary<int, ItemStack> ReadAll(IReadOnlyList<SavedStack> saved, IItemIds items, List<string> unknown = null)
        {
            var map = new Dictionary<int, ItemStack>();
            foreach (var entry in saved)
            {
                if (string.IsNullOrEmpty(entry.Item) || !items.TryGetIndex(entry.Item, out ushort def) || entry.Count <= 0)
                {
                    unknown?.Add(entry.Item);
                    continue;
                }
                var stack = items.Create(def, entry.Count);
                stack.Durability = entry.Durability;
                stack.RestoreFreshness(entry.Spoiled, entry.SpoilAt, entry.Chilled);
                map[entry.Id] = stack;
            }
            foreach (var entry in saved)
            {
                if (entry.Parent < 0 || entry.Place != Contents || !map.TryGetValue(entry.Id, out var stack))
                    continue;
                if (map.TryGetValue(entry.Parent, out var parent) && parent.Contents != null)
                    PlaceIn(parent.Contents, stack, entry);
            }
            return map;
        }

        /// <summary>Положить на сохранённое место; не вышло (сетка поменялась) — на любое свободное.</summary>
        public static int PlaceIn(GridInventory grid, ItemStack stack, SavedStack entry) =>
            grid.TryPlace(stack, entry.X, entry.Y, entry.Rotated) ? 0 : grid.TryAdd(stack);

        // ───────────────────────────── Инвентарь игрока ─────────────────────────────

        public static InventoryData CaptureInventory(GridInventory pockets, Equipment equipment, Hotbar hotbar, IItemIds items)
        {
            var data = new InventoryData();
            var ids = new Dictionary<ItemStack, int>();
            WriteGrid(pockets, Pockets, -1, items, data.Stacks, ids);
            foreach (EquipSlot slot in Enum.GetValues(typeof(EquipSlot)))
            {
                if (slot == EquipSlot.None)
                    continue;
                foreach (EquipLayer layer in Enum.GetValues(typeof(EquipLayer)))
                {
                    var stack = equipment.Get(slot, layer);
                    if (stack == null || ids.ContainsKey(stack))
                        continue;
                    WriteStack(stack, $"{EquipPrefix}{slot}:{layer}", -1, items, data.Stacks, ids);
                }
            }
            // Хотбар ссылается на стаки — по их Id в записи.
            for (int i = 0; i < Hotbar.Size; i++)
                data.Hotbar.Add(hotbar[i] != null && ids.TryGetValue(hotbar[i], out int id) ? id : -1);
            data.Selected = hotbar.Selected;
            return data;
        }

        /// <summary>Заменить инвентарь снимком: карманы, экипировка, хотбар. Что не влезло — возвращается списком.</summary>
        public static List<ItemStack> RestoreInventory(InventoryData data, GridInventory pockets, Equipment equipment, Hotbar hotbar, IItemIds items)
        {
            var leftovers = new List<ItemStack>();
            foreach (var placement in new List<GridPlacement>(pockets.Items))
                pockets.Remove(placement.Stack);
            foreach (EquipSlot slot in Enum.GetValues(typeof(EquipSlot)))
                foreach (EquipLayer layer in Enum.GetValues(typeof(EquipLayer)))
                    if (slot != EquipSlot.None && equipment.Get(slot, layer) != null)
                        equipment.Set(slot, layer, null);
            for (int i = 0; i < Hotbar.Size; i++)
                hotbar.Assign(i, null);

            var map = ReadAll(data.Stacks, items);
            foreach (var entry in data.Stacks)
            {
                if (entry.Parent >= 0 || !map.TryGetValue(entry.Id, out var stack))
                    continue;
                if (entry.Place == Pockets)
                {
                    if (PlaceIn(pockets, stack, entry) > 0)
                        leftovers.Add(stack);
                }
                else if (entry.Place != null && entry.Place.StartsWith(EquipPrefix))
                {
                    var parts = entry.Place.Substring(EquipPrefix.Length).Split(':');
                    if (parts.Length == 2 && Enum.TryParse(parts[0], out EquipSlot slot) && Enum.TryParse(parts[1], out EquipLayer layer))
                        equipment.Set(slot, layer, stack);
                    else
                        leftovers.Add(stack);
                }
            }
            for (int i = 0; i < data.Hotbar.Count && i < Hotbar.Size; i++)
                if (data.Hotbar[i] >= 0 && map.TryGetValue(data.Hotbar[i], out var stack))
                    hotbar.Assign(i, stack);
            hotbar.Select(Math.Clamp(data.Selected, 0, Hotbar.Size - 1));
            return leftovers;
        }
    }
}
