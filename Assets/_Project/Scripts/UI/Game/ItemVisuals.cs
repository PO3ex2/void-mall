using UnityEngine;
using UnityEngine.UIElements;
using VoidMall.Items;

namespace VoidMall.UI
{
    /// <summary>Общий вид игрового UI предметов: цвета, иконка предмета, рамка слота.</summary>
    internal static class ItemVisuals
    {
        public const float Cell = 46f;

        public static readonly Color Panel = new(0.06f, 0.065f, 0.08f, 0.94f);
        public static readonly Color CellColor = new(1f, 1f, 1f, 0.05f);
        public static readonly Color CellBorder = new(1f, 1f, 1f, 0.09f);
        public static readonly Color ItemBack = new(0.2f, 0.23f, 0.28f, 0.85f);
        public static readonly Color Highlight = new(1f, 0.82f, 0.35f, 1f);
        public static readonly Color Valid = new(0.35f, 0.85f, 0.45f, 0.35f);
        public static readonly Color Invalid = new(0.95f, 0.3f, 0.3f, 0.35f);
        public static readonly Color Text = new(0.88f, 0.9f, 0.92f);
        public static readonly Color Muted = new(0.55f, 0.58f, 0.62f);

        public static void Border(VisualElement e, Color color, float width = 1f, float radius = 3f)
        {
            var s = e.style;
            s.borderBottomColor = s.borderTopColor = s.borderLeftColor = s.borderRightColor = color;
            s.borderBottomWidth = s.borderTopWidth = s.borderLeftWidth = s.borderRightWidth = width;
            s.borderBottomLeftRadius = s.borderBottomRightRadius = s.borderTopLeftRadius = s.borderTopRightRadius = radius;
        }

        /// <summary>Иконка предмета: картинка, а если её нет — плашка цвета предмета с началом названия.</summary>
        public static VisualElement Icon(ItemDef def, float width, float height, bool rotated = false)
        {
            var icon = new VisualElement { pickingMode = PickingMode.Ignore };
            icon.style.position = Position.Absolute;
            icon.style.left = 2f;
            icon.style.top = 2f;
            // Повёрнутый предмет: иконка рисуется в исходной ориентации и поворачивается на 90° вокруг центра.
            float w = rotated ? height : width, h = rotated ? width : height;
            icon.style.width = w - 4f;
            icon.style.height = h - 4f;
            if (rotated)
            {
                icon.style.left = 2f + (width - w) * 0.5f;
                icon.style.top = 2f + (height - h) * 0.5f;
                icon.style.rotate = new Rotate(90f);
            }
            if (def.Icon != null)
            {
                icon.style.backgroundImage = def.Icon;
                icon.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
            }
            else
            {
                var color = def.Tint;
                color.a = 0.75f;
                icon.style.backgroundColor = color;
                Border(icon, new Color(0f, 0f, 0f, 0.3f), 1f, 4f);
                var label = new Label(Abbreviation(def.DisplayName)) { pickingMode = PickingMode.Ignore };
                label.style.fontSize = 11f;
                label.style.color = Color.white;
                label.style.unityTextAlign = TextAnchor.MiddleCenter;
                label.style.flexGrow = 1f;
                if (rotated)
                    label.style.rotate = new Rotate(-90f);
                icon.Add(label);
            }
            return icon;
        }

        /// <summary>Пометка испорченной еды: зеленоватая плёнка и подпись.</summary>
        public static VisualElement SpoiledMark()
        {
            var mark = new Label("гниль") { pickingMode = PickingMode.Ignore };
            mark.style.position = Position.Absolute;
            mark.style.left = mark.style.right = mark.style.top = mark.style.bottom = 0f;
            mark.style.backgroundColor = new Color(0.35f, 0.5f, 0.1f, 0.35f);
            mark.style.fontSize = 9f;
            mark.style.color = new Color(0.85f, 1f, 0.5f);
            mark.style.unityTextAlign = TextAnchor.UpperLeft;
            mark.style.paddingLeft = 3f;
            return mark;
        }

        /// <summary>Полоска прочности внизу ячейки (инструмент изношен).</summary>
        public static VisualElement WearBar(ItemStack stack, ItemDef def)
        {
            if (!def.HasWear || stack.Durability >= 0.999f)
                return null;
            var back = new VisualElement { pickingMode = PickingMode.Ignore };
            back.style.position = Position.Absolute;
            back.style.left = 4f;
            back.style.right = 4f;
            back.style.bottom = 3f;
            back.style.height = 3f;
            back.style.backgroundColor = new Color(0f, 0f, 0f, 0.6f);
            var fill = new VisualElement { pickingMode = PickingMode.Ignore };
            fill.style.height = 3f;
            fill.style.width = Length.Percent(Mathf.Clamp01(stack.Durability) * 100f);
            fill.style.backgroundColor = Color.Lerp(new Color(0.95f, 0.3f, 0.25f), new Color(0.45f, 0.85f, 0.45f), stack.Durability);
            back.Add(fill);
            return back;
        }

        /// <summary>Полоска свежести скоропортящегося (сверху, голубая в холоде). Null — не портится или испорчено.</summary>
        public static VisualElement FreshnessBar(ItemStack stack)
        {
            if (stack.ShelfLife <= 0f || stack.Spoiled)
                return null;
            float fresh = Freshness.Fraction(stack);
            var back = new VisualElement { pickingMode = PickingMode.Ignore };
            back.style.position = Position.Absolute;
            back.style.left = 4f;
            back.style.right = 4f;
            back.style.top = 3f;
            back.style.height = 3f;
            back.style.backgroundColor = new Color(0f, 0f, 0f, 0.6f);
            var fill = new VisualElement { pickingMode = PickingMode.Ignore };
            fill.style.height = 3f;
            fill.style.width = Length.Percent(fresh * 100f);
            fill.style.backgroundColor = stack.Chilled ? new Color(0.5f, 0.8f, 1f) : Color.Lerp(new Color(0.75f, 0.55f, 0.2f), new Color(0.55f, 0.85f, 0.4f), fresh);
            back.Add(fill);
            return back;
        }

        public static Label CountLabel(int count)
        {
            var label = new Label(count > 1 ? count.ToString() : string.Empty) { pickingMode = PickingMode.Ignore };
            label.style.position = Position.Absolute;
            label.style.right = 3f;
            label.style.bottom = 1f;
            label.style.fontSize = 12f;
            label.style.color = Color.white;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            return label;
        }

        private static string Abbreviation(string name)
        {
            if (string.IsNullOrEmpty(name))
                return "?";
            return name.Length <= 6 ? name : name.Substring(0, 5) + "…";
        }

        public static string CategoryName(ItemCategory category) => category switch
        {
            ItemCategory.Material => "Материал",
            ItemCategory.Block => "Строительный блок",
            ItemCategory.Tool => "Инструмент",
            ItemCategory.Food => "Еда",
            ItemCategory.Drink => "Напиток",
            ItemCategory.Medicine => "Медикаменты",
            ItemCategory.Clothing => "Одежда",
            ItemCategory.Armor => "Броня",
            ItemCategory.Backpack => "Рюкзак",
            ItemCategory.Blueprint => "Чертёж",
            _ => "Разное",
        };

        public static string SlotName(EquipSlot slot) => slot switch
        {
            EquipSlot.Head => "Голова",
            EquipSlot.Torso => "Торс",
            EquipSlot.Arms => "Руки",
            EquipSlot.Legs => "Ноги",
            EquipSlot.Feet => "Ступни",
            EquipSlot.Backpack => "Рюкзак",
            _ => "",
        };
    }

    /// <summary>
    /// Сетка инвентаря: клетки и предметы поверх них. Перетаскивание обрабатывает окно персонажа —
    /// сетка лишь рисует содержимое и переводит координаты указателя в клетки.
    /// </summary>
    internal sealed class GridView : VisualElement
    {
        public GridInventory Grid { get; private set; }
        private readonly VisualElement _cells;
        private readonly VisualElement _items;
        private readonly VisualElement _preview;

        public System.Action<ItemStack, PointerDownEvent> ItemPointerDown;
        public System.Action<ItemStack> ItemHovered;

        public GridView()
        {
            style.position = Position.Relative;
            _cells = new VisualElement { pickingMode = PickingMode.Ignore };
            _cells.style.position = Position.Absolute;
            _cells.style.left = _cells.style.top = 0f;
            Add(_cells);
            _preview = new VisualElement { pickingMode = PickingMode.Ignore };
            _preview.style.position = Position.Absolute;
            _preview.style.display = DisplayStyle.None;
            _items = new VisualElement { pickingMode = PickingMode.Ignore };
            _items.style.position = Position.Absolute;
            _items.style.left = _items.style.top = 0f;
            Add(_items);
            Add(_preview);
        }

        public void Bind(GridInventory grid, ItemRegistry items, ItemStack dragging)
        {
            Grid = grid;
            _cells.Clear();
            _items.Clear();
            if (grid == null)
            {
                style.width = style.height = 0f;
                return;
            }
            style.width = grid.Width * ItemVisuals.Cell;
            style.height = grid.Height * ItemVisuals.Cell;
            for (int y = 0; y < grid.Height; y++)
            for (int x = 0; x < grid.Width; x++)
            {
                var cell = new VisualElement { pickingMode = PickingMode.Ignore };
                cell.style.position = Position.Absolute;
                cell.style.left = x * ItemVisuals.Cell;
                cell.style.top = y * ItemVisuals.Cell;
                cell.style.width = cell.style.height = ItemVisuals.Cell - 2f;
                cell.style.backgroundColor = ItemVisuals.CellColor;
                ItemVisuals.Border(cell, ItemVisuals.CellBorder, 1f, 2f);
                _cells.Add(cell);
            }
            foreach (var placement in grid.Items)
            {
                var stack = placement.Stack;
                var def = items.Get(stack.Def);
                var (w, h) = grid.SizeOf(stack, placement.Rotated);
                var element = new VisualElement();
                element.style.position = Position.Absolute;
                element.style.left = placement.X * ItemVisuals.Cell;
                element.style.top = placement.Y * ItemVisuals.Cell;
                element.style.width = w * ItemVisuals.Cell - 2f;
                element.style.height = h * ItemVisuals.Cell - 2f;
                element.style.backgroundColor = ItemVisuals.ItemBack;
                element.style.opacity = stack == dragging ? 0.3f : 1f;
                ItemVisuals.Border(element, new Color(1f, 1f, 1f, 0.15f), 1f, 3f);
                element.Add(ItemVisuals.Icon(def, w * ItemVisuals.Cell - 2f, h * ItemVisuals.Cell - 2f, placement.Rotated));
                element.Add(ItemVisuals.CountLabel(stack.Count));
                if (stack.Spoiled)
                    element.Add(ItemVisuals.SpoiledMark());
                var wear = ItemVisuals.WearBar(stack, def);
                if (wear != null)
                    element.Add(wear);
                var fresh = ItemVisuals.FreshnessBar(stack);
                if (fresh != null)
                    element.Add(fresh);
                element.RegisterCallback<PointerDownEvent>(evt => ItemPointerDown?.Invoke(stack, evt));
                element.RegisterCallback<PointerEnterEvent>(_ => ItemHovered?.Invoke(stack));
                _items.Add(element);
            }
        }

        /// <summary>Клетка под точкой (в координатах панели) или (−1, −1).</summary>
        public Vector2Int CellAt(Vector2 panelPosition, int width, int height)
        {
            Vector2 local = this.WorldToLocal(panelPosition);
            // Точка указателя — центр предмета: левый верхний угол сдвигаем на половину размера.
            int x = Mathf.RoundToInt(local.x / ItemVisuals.Cell - width * 0.5f);
            int y = Mathf.RoundToInt(local.y / ItemVisuals.Cell - height * 0.5f);
            return new Vector2Int(x, y);
        }

        public void ShowPreview(int x, int y, int width, int height, bool valid)
        {
            _preview.style.display = DisplayStyle.Flex;
            _preview.style.left = x * ItemVisuals.Cell;
            _preview.style.top = y * ItemVisuals.Cell;
            _preview.style.width = width * ItemVisuals.Cell - 2f;
            _preview.style.height = height * ItemVisuals.Cell - 2f;
            _preview.style.backgroundColor = valid ? ItemVisuals.Valid : ItemVisuals.Invalid;
        }

        public void HidePreview() => _preview.style.display = DisplayStyle.None;
    }
}
