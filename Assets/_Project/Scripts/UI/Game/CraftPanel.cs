using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;
using VoidMall.Core;
using VoidMall.Crafting;
using VoidMall.Items;
using VoidMall.Skills;

namespace VoidMall.UI
{
    /// <summary>
    /// Панель крафта (GDD §5.3): список рецептов с поиском и фильтрами, карточка рецепта, производство
    /// (ингредиенты есть/нет, количество, «Крафт», очередь с прогрессом и отменой). Одна и та же панель —
    /// во вкладке «Крафт» (руки) и в окне станции (рецепты станции, её очередь).
    /// </summary>
    internal sealed class CraftPanel : VisualElement
    {
        private static readonly Color Ok = new(0.55f, 0.9f, 0.6f);
        private static readonly Color Bad = new(1f, 0.55f, 0.5f);

        private readonly TextField _search;
        private readonly Toggle _onlyCraftable;
        private readonly DropdownField _stationFilter;
        private readonly ScrollView _list;
        private readonly VisualElement _cardIcon;
        private readonly Label _cardTitle;
        private readonly Label _cardBody;
        private readonly VisualElement _ingredients;
        private readonly Label _count;
        private readonly Button _craft;
        private readonly Label _notice;
        private readonly VisualElement _queue;
        private readonly List<(VisualElement bar, Label time, CraftJob job)> _progress = new();

        private StationState _station;
        private RecipeDef _selected;
        private int _amount = 1;
        private bool _dirty = true;
        private float _refresh;
        private PlayerCrafting _crafting;
        private PlayerInventory _inventory;
        private CraftQueue _boundQueue;

        private static readonly string[] StationChoices = { "Руки", "Все", "Верстак", "Швейный стол", "Химстол", "Плита или горелка" };

        public CraftPanel()
        {
            style.flexDirection = FlexDirection.Row;
            style.height = 440f;

            // Список рецептов.
            var left = Column(270f);
            _search = new TextField { value = string.Empty };
            _search.style.marginLeft = 0f;
            _search.RegisterValueChangedCallback(_ => _dirty = true);
            left.Add(_search);
            _stationFilter = new DropdownField(new List<string>(StationChoices), 0);
            _stationFilter.style.marginLeft = 0f;
            _stationFilter.RegisterValueChangedCallback(_ => _dirty = true);
            left.Add(_stationFilter);
            _onlyCraftable = new Toggle("Только можно сейчас");
            _onlyCraftable.RegisterValueChangedCallback(_ => _dirty = true);
            left.Add(_onlyCraftable);
            _list = new ScrollView();
            _list.style.flexGrow = 1f;
            left.Add(_list);
            Add(left);

            // Карточка.
            var middle = Column(280f);
            middle.style.marginLeft = middle.style.marginRight = 14f;
            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            _cardIcon = new VisualElement();
            _cardIcon.style.width = _cardIcon.style.height = 72f;
            _cardIcon.style.backgroundColor = ItemVisuals.ItemBack;
            ItemVisuals.Border(_cardIcon, new Color(1f, 1f, 1f, 0.15f), 1f, 4f);
            header.Add(_cardIcon);
            _cardTitle = new Label("Выберите рецепт");
            _cardTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            _cardTitle.style.fontSize = 15f;
            _cardTitle.style.whiteSpace = WhiteSpace.Normal;
            _cardTitle.style.marginLeft = 10f;
            _cardTitle.style.flexShrink = 1f;
            header.Add(_cardTitle);
            middle.Add(header);
            _cardBody = new Label();
            _cardBody.style.whiteSpace = WhiteSpace.Normal;
            _cardBody.style.color = ItemVisuals.Muted;
            _cardBody.style.marginTop = 8f;
            middle.Add(_cardBody);
            Add(middle);

            // Производство.
            var right = Column(300f);
            right.Add(Title("Ингредиенты"));
            _ingredients = new VisualElement();
            right.Add(_ingredients);
            var countRow = new VisualElement();
            countRow.style.flexDirection = FlexDirection.Row;
            countRow.style.marginTop = 8f;
            countRow.style.alignItems = Align.Center;
            countRow.Add(SmallButton("−", () => ChangeAmount(-1)));
            _count = new Label("1");
            _count.style.width = 34f;
            _count.style.unityTextAlign = TextAnchor.MiddleCenter;
            countRow.Add(_count);
            countRow.Add(SmallButton("+", () => ChangeAmount(1)));
            countRow.Add(SmallButton("Макс", () => { _amount = 99; ChangeAmount(0); }));
            _craft = SmallButton("Крафт", Craft);
            _craft.style.flexGrow = 1f;
            _craft.style.height = 28f;
            countRow.Add(_craft);
            right.Add(countRow);
            _notice = new Label();
            _notice.style.color = Bad;
            _notice.style.whiteSpace = WhiteSpace.Normal;
            _notice.style.minHeight = 18f;
            right.Add(_notice);
            right.Add(Title("Очередь"));
            _queue = new ScrollView();
            _queue.style.flexGrow = 1f;
            right.Add(_queue);
            Add(right);
            DevToolsStyle.DarkControls(this);
        }

        /// <summary>Привязать к станции (null — крафт в руках).</summary>
        public void Bind(StationState station)
        {
            _station = station;
            _stationFilter.style.display = station == null ? DisplayStyle.Flex : DisplayStyle.None;
            _selected = null;
            _amount = 1;
            _notice.text = string.Empty;
            _dirty = true;
        }

        public void MarkDirty() => _dirty = true;

        /// <summary>Каждый кадр, пока панель видна.</summary>
        public void Tick()
        {
            if (_crafting == null && PlayerCrafting.Local != null)
            {
                _crafting = PlayerCrafting.Local;
                _crafting.Changed += MarkDirty;
            }
            if (_inventory == null && PlayerInventory.Local != null)
            {
                _inventory = PlayerInventory.Local;
                _inventory.Changed += MarkDirty;
            }
            if (_crafting == null || _inventory == null || CraftingSystem.Instance == null)
                return;
            var queue = Queue;
            if (queue != _boundQueue)
            {
                if (_boundQueue != null)
                    _boundQueue.Changed -= MarkDirty;
                _boundQueue = queue;
                _boundQueue.Changed += MarkDirty;
                _dirty = true;
            }
            // Навыки и чертежи меняют доступность — перепроверяем раз в секунду.
            _refresh -= Time.unscaledDeltaTime;
            if (_refresh <= 0f)
            {
                _refresh = 1f;
                _dirty = true;
            }
            if (_dirty)
            {
                _dirty = false;
                Rebuild();
            }
            foreach (var (bar, time, job) in _progress)
            {
                bar.style.width = Length.Percent(job.Progress * 100f);
                string text = job == queue.Current && _station != null && _station.NeedsFuel && _station.Fuel <= 0f ? "нет топлива"
                    : job == queue.Current && queue.Blocked ? "нет места в выдаче"
                    : $"{job.Remaining:0} с";
                if (time.text != text)
                    time.text = text;
            }
        }

        private CraftQueue Queue => _station?.Queue ?? _crafting.Queue;

        // ───────────────────────────── Список ─────────────────────────────

        private void Rebuild()
        {
            var recipes = CraftingSystem.Instance.Recipes;
            string search = (_search.value ?? string.Empty).Trim().ToLowerInvariant();
            _list.Clear();
            var shown = new List<(RecipeDef recipe, string why)>();
            foreach (var recipe in recipes)
            {
                if (!MatchesStation(recipe))
                    continue;
                if (search.Length > 0 && !recipe.DisplayName.ToLowerInvariant().Contains(search))
                    continue;
                string why = WhyNot(recipe);
                if (_onlyCraftable.value && why != null)
                    continue;
                shown.Add((recipe, why));
            }
            // Сначала то, что можно сделать сейчас; дальше по категориям.
            shown.Sort((a, b) => (a.why == null ? 0 : 1).CompareTo(b.why == null ? 0 : 1) != 0
                ? (a.why == null ? 0 : 1).CompareTo(b.why == null ? 0 : 1)
                : a.recipe.Category != b.recipe.Category ? a.recipe.Category.CompareTo(b.recipe.Category)
                : string.CompareOrdinal(a.recipe.DisplayName, b.recipe.DisplayName));
            if (_selected != null && !shown.Exists(s => s.recipe == _selected))
                _selected = null;
            if (_selected == null && shown.Count > 0)
                _selected = shown[0].recipe;
            foreach (var (recipe, why) in shown)
                _list.Add(RecipeRow(recipe, why));
            if (shown.Count == 0)
            {
                var empty = new Label(_station != null ? "Нет рецептов для этой станции." : "Ничего не найдено.");
                empty.style.color = ItemVisuals.Muted;
                _list.Add(empty);
            }
            ShowCard();
            ShowQueue();
        }

        private bool MatchesStation(RecipeDef recipe)
        {
            if (_station != null)
                return recipe.Station == _station.Kind;
            return _stationFilter.index switch
            {
                0 => recipe.Station == CraftStation.None,
                1 => true,
                2 => recipe.Station == CraftStation.Workbench,
                3 => recipe.Station == CraftStation.Sewing,
                4 => recipe.Station == CraftStation.Chemistry,
                _ => recipe.Station == CraftStation.Cooking,
            };
        }

        private string WhyNot(RecipeDef recipe) => _crafting.WhyCannotCraft(recipe, _station, 1);

        private VisualElement RecipeRow(RecipeDef recipe, string why)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.height = 38f;
            row.style.marginBottom = 2f;
            row.style.paddingLeft = 4f;
            row.style.backgroundColor = recipe == _selected ? new Color(0.3f, 0.34f, 0.42f, 0.9f) : new Color(1f, 1f, 1f, 0.04f);
            ItemVisuals.Border(row, new Color(1f, 1f, 1f, 0.06f), 1f, 3f);
            var icon = new VisualElement { pickingMode = PickingMode.Ignore };
            icon.style.width = icon.style.height = 34f;
            icon.style.position = Position.Relative;
            if (recipe.Result != null)
                icon.Add(ItemVisuals.Icon(recipe.Result, 34f, 34f));
            row.Add(icon);
            bool locked = !_crafting.IsLearned(recipe);
            var name = new Label(locked ? $"{recipe.DisplayName} (нужен чертёж)" : recipe.DisplayName + (recipe.ResultCount > 1 ? $" ×{recipe.ResultCount}" : "")) { pickingMode = PickingMode.Ignore };
            name.style.marginLeft = 6f;
            name.style.flexShrink = 1f;
            name.style.color = why == null ? ItemVisuals.Text : ItemVisuals.Muted;
            row.Add(name);
            row.RegisterCallback<PointerDownEvent>(_ =>
            {
                _selected = recipe;
                _amount = 1;
                _notice.text = string.Empty;
                _dirty = true;
            });
            return row;
        }

        // ───────────────────────────── Карточка и ингредиенты ─────────────────────────────

        private void ShowCard()
        {
            _cardIcon.Clear();
            _ingredients.Clear();
            if (_selected == null)
            {
                _cardTitle.text = "Выберите рецепт";
                _cardBody.text = string.Empty;
                _craft.SetEnabled(false);
                return;
            }
            var recipe = _selected;
            var result = recipe.Result;
            _cardIcon.Add(ItemVisuals.Icon(result, 72f, 72f));
            int count = PlayerCrafting.ResultCount(recipe);
            _cardTitle.text = result.DisplayName + (count > 1 ? $" ×{count}" : "");
            var sb = new StringBuilder();
            sb.Append($"{CraftNames.Category(recipe.Category)} · {CraftNames.Station(recipe.Station)} · {PlayerCrafting.CraftTime(recipe):0.#} с");
            if (recipe.Blueprint)
                sb.Append(_crafting.IsLearned(recipe) ? "\nЧертёж изучен" : "\nНужен чертёж: ищите в магазинах и на складах");
            if (recipe.SkillLevel > 0)
            {
                int have = PlayerSkills.LevelOf(recipe.Skill);
                sb.Append($"\nНавык: {SkillCatalog.Get(recipe.Skill).Name} {recipe.SkillLevel} (у вас {have})");
            }
            if (!string.IsNullOrEmpty(result.Description))
                sb.Append("\n\n").Append(result.Description);
            sb.Append(Stats(result));
            _cardBody.text = sb.ToString();

            foreach (var ingredient in recipe.Ingredients)
            {
                if (ingredient.Item == null)
                    continue;
                int have = _crafting.Have(ingredient.Item);
                int need = ingredient.Count * Mathf.Max(1, _amount);
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                row.style.height = 30f;
                var icon = new VisualElement();
                icon.style.width = icon.style.height = 28f;
                icon.style.position = Position.Relative;
                icon.Add(ItemVisuals.Icon(ingredient.Item, 28f, 28f));
                row.Add(icon);
                var label = new Label($"{ingredient.Item.DisplayName}");
                label.style.flexGrow = 1f;
                label.style.marginLeft = 6f;
                row.Add(label);
                var amount = new Label($"{have} / {need}");
                amount.style.color = have >= need || ItemSystem.Creative.Value ? Ok : Bad;
                row.Add(amount);
                _ingredients.Add(row);
            }
            _count.text = _amount.ToString();
            _craft.SetEnabled(_crafting.WhyCannotCraft(recipe, _station, Mathf.Max(1, _amount)) == null);
        }

        private static string Stats(ItemDef item)
        {
            var sb = new StringBuilder();
            if (item.IsTool && item.BlockDamage > 0) sb.Append($"\nУрон по блокам: {item.BlockDamage}");
            if (item.IsTool && item.PropDamage > 0) sb.Append($"\nУрон по мебели: {item.PropDamage}");
            if (item.HasWear) sb.Append($"\nПрочность: {item.Uses} применений");
            if (item.Calories > 0 || item.Water > 0) sb.Append($"\nКалории: {item.Calories:0} · вода: {item.Water:0} мл");
            if (item.Cooked) sb.Append("\nГорячее: успокаивает");
            if (item.Armor > 0) sb.Append($"\nЗащита: {item.Armor:0}");
            if (item.Warmth > 0) sb.Append($"\nТепло: +{item.Warmth:0} °C");
            if (item.IsContainer) sb.Append($"\nВместимость: {item.ContainerGrid.x}×{item.ContainerGrid.y}");
            if (item.FuelSeconds > 0) sb.Append($"\nТопливо: {item.FuelSeconds:0} с");
            sb.Append($"\nВес: {item.Weight:0.##} кг · размер {item.GridSize.x}×{item.GridSize.y}");
            return sb.Length > 0 ? "\n" + sb : string.Empty;
        }

        private void ChangeAmount(int delta)
        {
            if (_selected == null)
                return;
            int max = Mathf.Max(1, _crafting.MaxCraftable(_selected, _station));
            _amount = Mathf.Clamp(_amount + delta, 1, max);
            _dirty = true;
        }

        private void Craft()
        {
            if (_selected == null)
                return;
            string refusal = _crafting.Craft(_selected, _station, Mathf.Max(1, _amount));
            _notice.text = refusal ?? string.Empty;
            _amount = 1;
            _dirty = true;
        }

        // ───────────────────────────── Очередь ─────────────────────────────

        private void ShowQueue()
        {
            _queue.Clear();
            _progress.Clear();
            var queue = Queue;
            if (queue.IsEmpty)
            {
                var empty = new Label(_station != null && !_station.Autonomous
                    ? "Пусто. Станция работает, пока открыто это окно."
                    : _station != null ? "Пусто. Плита готовит сама, пока есть топливо." : "Пусто. Крафт в руках идёт всегда.");
                empty.style.color = ItemVisuals.Muted;
                empty.style.whiteSpace = WhiteSpace.Normal;
                _queue.Add(empty);
                return;
            }
            for (int i = 0; i < queue.Jobs.Count; i++)
            {
                var job = queue.Jobs[i];
                int index = i;
                var row = new VisualElement();
                row.style.marginBottom = 4f;
                var top = new VisualElement();
                top.style.flexDirection = FlexDirection.Row;
                var title = new Label(job.Title);
                title.style.flexGrow = 1f;
                title.style.fontSize = 12f;
                top.Add(title);
                var time = new Label();
                time.style.fontSize = 11f;
                time.style.color = ItemVisuals.Muted;
                top.Add(time);
                var cancel = SmallButton("×", () => _crafting.Cancel(queue, index));
                cancel.style.height = 18f;
                top.Add(cancel);
                row.Add(top);
                var back = new VisualElement();
                back.style.height = 4f;
                back.style.backgroundColor = new Color(1f, 1f, 1f, 0.08f);
                var bar = new VisualElement();
                bar.style.height = 4f;
                bar.style.backgroundColor = i == 0 ? ItemVisuals.Highlight : new Color(1f, 1f, 1f, 0.2f);
                back.Add(bar);
                row.Add(back);
                _queue.Add(row);
                _progress.Add((bar, time, job));
            }
        }

        // ───────────────────────────── Разметка ─────────────────────────────

        private static VisualElement Column(float width)
        {
            var column = new VisualElement();
            column.style.flexDirection = FlexDirection.Column;
            column.style.width = width;
            return column;
        }

        internal static Label Title(string text)
        {
            var label = new Label(text);
            label.style.color = ItemVisuals.Muted;
            label.style.fontSize = 12f;
            label.style.marginBottom = 4f;
            label.style.marginTop = 6f;
            return label;
        }

        internal static Button SmallButton(string text, System.Action action)
        {
            var button = new Button(action) { text = text };
            button.style.backgroundColor = new Color(0.16f, 0.18f, 0.22f, 1f);
            button.style.color = ItemVisuals.Text;
            ItemVisuals.Border(button, new Color(1f, 1f, 1f, 0.15f), 1f, 4f);
            return button;
        }
    }
}
