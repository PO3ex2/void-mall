using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace VoidMall.Game
{
    /// <summary>
    /// Переназначение клавиш (этап 10): переопределения привязок project-wide actions хранятся JSON-ом в PlayerPrefs и
    /// применяются при запуске. Клавиатура с мышью и геймпад — отдельно (группы привязок).
    /// </summary>
    public static class KeyBindings
    {
        public const string PrefsKey = "vm_bindings";
        public const string KeyboardGroup = "KeyboardMouse", GamepadGroup = "Gamepad";

        /// <summary>Карты, чьи действия можно переназначать (UI и Terminal — служебные).</summary>
        public static readonly string[] Maps = { "Player", "Global" };

        public static void Load(InputActionAsset asset)
        {
            if (asset == null)
                return;
            string json = PlayerPrefs.GetString(PrefsKey, "");
            if (!string.IsNullOrEmpty(json))
                asset.LoadBindingOverridesFromJson(json);
        }

        public static void Save(InputActionAsset asset)
        {
            if (asset == null)
                return;
            PlayerPrefs.SetString(PrefsKey, asset.SaveBindingOverridesAsJson());
            PlayerPrefs.Save();
        }

        public static void ResetAll(InputActionAsset asset)
        {
            if (asset == null)
                return;
            asset.RemoveAllBindingOverrides();
            PlayerPrefs.DeleteKey(PrefsKey);
        }

        /// <summary>Действия, которых нет в списке: обзор (ось, не клавиша) и запасная копия инвентаря (работает Global).</summary>
        private static readonly HashSet<string> Hidden = new() { "LookMouse", "LookStick", "Player/Inventory" };

        private static readonly Dictionary<string, string> Names = new()
        {
            ["Move"] = "Движение", ["Up"] = "вперёд", ["Down"] = "назад", ["Left"] = "влево", ["Right"] = "вправо",
            ["Jump"] = "Прыжок", ["Crouch"] = "Присесть", ["Sprint"] = "Бег", ["Walk"] = "Шагом",
            ["Interact"] = "Взаимодействие", ["Disassemble"] = "Разобрать", ["Primary"] = "Удар / действие",
            ["Secondary"] = "Второе действие", ["Pick"] = "Взять блок под прицелом", ["Reload"] = "Повернуть / перезарядить",
            ["Scroll"] = "Прокрутка хотбара", ["Negative"] = "назад", ["Positive"] = "вперёд",
            ["Pause"] = "Меню", ["ToggleInventory"] = "Инвентарь", ["ToggleMap"] = "Карта", ["ToggleConsole"] = "Консоль",
            ["ToggleDebugHud"] = "Отладка: HUD", ["ToggleDebugPanel"] = "Отладка: панель",
        };

        /// <summary>Подпись действия по-русски (Slot3 — «Слот 3»); неизвестное — как есть.</summary>
        public static string Caption(string name) =>
            Names.TryGetValue(name, out var caption) ? caption
            : name.StartsWith("Slot") && name.Length > 4 ? $"Слот {name.Substring(4)}"
            : name;

        /// <summary>Строки списка: действие, индекс привязки, подпись (части WASD — «Движение: вперёд», вторая клавиша — «Присесть (2)»).</summary>
        public static List<(InputAction action, int index, string caption)> Rows(InputActionAsset asset, string group)
        {
            var rows = new List<(InputAction, int, string)>();
            if (asset == null)
                return rows;
            foreach (var mapName in Maps)
            {
                var map = asset.FindActionMap(mapName);
                if (map == null)
                    continue;
                foreach (var action in map.actions)
                {
                    if (Hidden.Contains(action.name) || Hidden.Contains($"{mapName}/{action.name}"))
                        continue;
                    int plain = 0;
                    for (int i = 0; i < action.bindings.Count; i++)
                    {
                        var binding = action.bindings[i];
                        if (binding.isComposite || !InGroup(binding, group))
                            continue;
                        string caption = binding.isPartOfComposite
                            ? $"{Caption(action.name)}: {Caption(binding.name)}"
                            : ++plain > 1 ? $"{Caption(action.name)} ({plain})" : Caption(action.name);
                        rows.Add((action, i, caption));
                    }
                }
            }
            return rows;
        }

        private static bool InGroup(InputBinding binding, string group) =>
            !string.IsNullOrEmpty(binding.groups) && binding.groups.Split(';').Contains(group);

        private static bool Contains(this string[] values, string value) => System.Array.IndexOf(values, value) >= 0;

        /// <summary>
        /// Название клавиши по её месту на клавиатуре (W, а не «Ц» в русской раскладке): игровые клавиши физические.
        /// </summary>
        public static string Display(InputAction action, int index)
        {
            string path = action.bindings[index].effectivePath;
            if (string.IsNullOrEmpty(path))
                return "—";
            return InputControlPath.ToHumanReadableString(path, InputControlPath.HumanReadableStringOptions.OmitDevice);
        }
    }
}
