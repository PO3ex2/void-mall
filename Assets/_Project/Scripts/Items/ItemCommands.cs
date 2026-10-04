using System.Linq;
using System.Text;
using VoidMall.Console;

namespace VoidMall.Items
{
    internal static class ItemCommands
    {
        [ConsoleCommand("give", "Выдать предмет: give <id> [количество] (например, give hammer, give nails 50).", Flags = ConsoleFlags.Cheat)]
        private static string Give(string id, int count = 1)
        {
            var system = ItemSystem.Instance;
            var inventory = PlayerInventory.Local;
            if (system == null || inventory == null)
                return "Нет системы предметов или игрока.";
            if (!system.TryResolve(id, out ushort index))
            {
                var similar = system.Items.All.Select(d => d.Id).Where(n => n.Contains(id)).Take(6);
                return $"Нет предмета '{id}'. Похожие: {string.Join(", ", similar)}. Список: items_list";
            }
            var def = system.Items.Get(index);
            int left = inventory.Add(index, count);
            if (left > 0)
                WorldItems.Instance?.Spawn(system.Items.Create(index, left), inventory.transform.position + UnityEngine.Vector3.up, UnityEngine.Vector3.zero);
            return left > 0 ? $"Выдано {count - left} × {def.DisplayName}, {left} не влезло — лежит на полу." : $"Выдано {count} × {def.DisplayName}";
        }

        [ConsoleCommand("items_list", "Список предметов: items_list [часть ID или категория].")]
        private static string ItemsList(string filter = "")
        {
            var system = ItemSystem.Instance;
            if (system == null)
                return "Нет системы предметов.";
            var sb = new StringBuilder("Предметы:");
            foreach (var def in system.Items.All)
            {
                if (filter.Length > 0 && !def.Id.Contains(filter) && !def.Category.ToString().ToLowerInvariant().Contains(filter.ToLowerInvariant()))
                    continue;
                sb.Append($"\n  {def.Id,-28} {def.DisplayName,-26} {def.Weight,5:0.##} кг  {def.GridSize.x}×{def.GridSize.y}");
            }
            return sb.ToString();
        }

        [ConsoleCommand("clear_inventory", "Очистить инвентарь (карманы, рюкзак, экипировку).", Flags = ConsoleFlags.Cheat)]
        private static string ClearInventory()
        {
            var inventory = PlayerInventory.Local;
            if (inventory == null)
                return "Нет игрока.";
            foreach (var placement in inventory.Pockets.Items.ToList())
                inventory.Remove(placement.Stack);
            if (inventory.Backpack != null)
                foreach (var placement in inventory.Backpack.Items.ToList())
                    inventory.Remove(placement.Stack);
            foreach (EquipSlot slot in System.Enum.GetValues(typeof(EquipSlot)))
            foreach (EquipLayer layer in System.Enum.GetValues(typeof(EquipLayer)))
            {
                var stack = inventory.Equipment.Get(slot, layer);
                if (stack != null)
                    inventory.Remove(stack);
            }
            return "Инвентарь очищен.";
        }

        [ConsoleCommand("inventory", "Что в инвентаре и сколько весит.")]
        private static string Inventory()
        {
            var inventory = PlayerInventory.Local;
            if (inventory == null)
                return "Нет игрока.";
            var sb = new StringBuilder($"Вес {inventory.TotalWeight():0.0} / {PlayerInventory.Capacity:0} кг");
            void Dump(string title, GridInventory grid)
            {
                if (grid == null)
                    return;
                sb.Append($"\n{title} ({grid.Width}×{grid.Height}):");
                foreach (var placement in grid.Items)
                    sb.Append($"\n  {inventory.Items.Get(placement.Stack.Def).DisplayName} ×{placement.Stack.Count}");
            }
            Dump("Карманы", inventory.Pockets);
            Dump("Рюкзак", inventory.Backpack);
            return sb.ToString();
        }
    }
}
