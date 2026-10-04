using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using VoidMall.Input;
using VoidMall.Items;
using VoidMall.Core;
using VoidMall.Crafting;
using VoidMall.Survival;

namespace VoidMall.UI
{
    /// <summary>
    /// Окно персонажа (Tab, GDD §5): вкладки «Инвентарь | Навыки | Крафт | Состояние».
    /// Инвентарь: экипировка (слои «Броня / Одежда»), карманы, сетка рюкзака, карточка предмета, вес и хотбар.
    /// Перетаскивание мышью: R — повернуть, на слот экипировки — надеть, на слот хотбара — назначить,
    /// за пределы окна — выбросить. ПКМ — использовать/надеть/снять, Shift+клик — переложить между карманами и рюкзаком.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class CharacterWindow : MonoBehaviour
    {
        private enum Source { None, Grid, Equipment }

        private InputService _input;
        private InputAction _toggle;
        private VisualElement _root;
        private VisualElement _window;
        private VisualElement _inventoryTab;
        private VisualElement _stateTab;
        private Label _stateVitals;
        private VisualElement _stateList;
        private float _stateTimer;
        private readonly List<StatusKind> _conditions = new();
        private Label _notice;
        private Label _floorRefusal;
        private CraftPanel _craftPanel;
        private SkillsPanel _skillsPanel;
        private readonly List<Button> _tabButtons = new();
        private int _tab;

        private GridView _pockets;
        private GridView _backpack;
        private Label _backpackTitle;
        private VisualElement _equipment;
        private EquipLayer _layer = EquipLayer.Armor;
        private Button _layerButton;
        private VisualElement _hotbar;
        private Label _infoTitle;
        private Label _infoBody;
        private Label _weight;
        private VisualElement _weightBar;

        private PlayerInventory _inventory;
        private bool _open;
        private bool _dirty = true;

        private ItemStack _dragging;
        private Source _dragSource;
        private GridInventory _dragGrid;
        private bool _dragRotated;
        private VisualElement _ghost;
        private Vector2 _ghostSize;
        private int _pointerId = -1;
        private Vector2 _pointer;

        public bool IsOpen => _open;

        public void Initialize(InputService input)
        {
            _input = input;
            _toggle = input.Action(InputService.Maps.Global, "ToggleInventory");
            _root = GetComponent<UIDocument>().rootVisualElement;
            DevToolsStyle.FullScreenRoot(_root);
            Build();
            _root.style.display = DisplayStyle.None;
        }

        private static readonly Perf.Probe UpdateProbe = Perf.Register("UI.CharacterWindow");

        private void Update()
        {
            using var perf = Perf.Measure(UpdateProbe);
            if (_inventory == null && PlayerInventory.Local != null)
            {
                _inventory = PlayerInventory.Local;
                _inventory.Changed += () => _dirty = true;
            }
            if (_toggle != null && _toggle.WasPressedThisFrame())
                SetOpen(!_open);
            if (_open && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                SetOpen(false);
            if (!_open || _inventory == null)
                return;

            // Поворот имеет смысл только у неквадратных предметов (1×1, 2×2 повернуть — то же самое).
            if (_dragging != null && Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame && IsRotatable(_dragging))
            {
                _dragRotated = !_dragRotated;
                UpdateGhost();
            }
            if (_dirty)
                Refresh();
            UpdatePreview();
            if (_tab == 3)
                UpdateStateTab();
            else if (_tab == 2)
                _craftPanel.Tick();
            else if (_tab == 1)
                _skillsPanel.Tick();
        }

        private void SetOpen(bool open)
        {
            if (open == _open)
                return;
            if (open && _input.Current == InputContext.Console)
                return;
            _open = open;
            _root.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            if (open)
            {
                _input.Push(InputContext.Menu);
                _dirty = true;
            }
            else
            {
                CancelDrag();
                _input.Pop(InputContext.Menu);
            }
        }

        // ───────────────────────────── Разметка ─────────────────────────────

        private void Build()
        {
            // Фон-затемнение: клик по нему с предметом — выбросить.
            var backdrop = new VisualElement { name = "backdrop" };
            backdrop.style.position = Position.Absolute;
            backdrop.style.left = backdrop.style.right = backdrop.style.top = backdrop.style.bottom = 0f;
            backdrop.style.backgroundColor = new Color(0f, 0f, 0f, 0.45f);
            backdrop.style.alignItems = Align.Center;
            backdrop.style.justifyContent = Justify.Center;
            _root.Add(backdrop);

            _window = new VisualElement { name = "window" };
            _window.style.backgroundColor = ItemVisuals.Panel;
            ItemVisuals.Border(_window, new Color(1f, 1f, 1f, 0.12f), 1f, 6f);
            _window.style.paddingLeft = _window.style.paddingRight = 16f;
            _window.style.paddingTop = _window.style.paddingBottom = 12f;
            _window.style.color = ItemVisuals.Text;
            backdrop.Add(_window);

            // Вкладки.
            var tabs = new VisualElement();
            tabs.style.flexDirection = FlexDirection.Row;
            tabs.style.marginBottom = 10f;
            string[] names = { "Инвентарь", "Навыки", "Крафт", "Состояние" };
            for (int i = 0; i < names.Length; i++)
            {
                int index = i;
                var button = new Button(() => SelectTab(index)) { text = names[i] };
                StyleButton(button);
                button.style.marginRight = 4f;
                _tabButtons.Add(button);
                tabs.Add(button);
            }
            var hint = new Label("Tab — закрыть · R — повернуть · ПКМ — использовать · Shift+клик — переложить · за окно — выбросить");
            hint.style.color = ItemVisuals.Muted;
            hint.style.fontSize = 11f;
            hint.style.marginLeft = 12f;
            hint.style.unityTextAlign = TextAnchor.MiddleLeft;
            tabs.Add(hint);
            _window.Add(tabs);

            _skillsPanel = new SkillsPanel();
            _window.Add(_skillsPanel);
            _craftPanel = new CraftPanel();
            _craftPanel.Bind(null);
            _window.Add(_craftPanel);

            BuildStateTab();

            _inventoryTab = new VisualElement();
            _inventoryTab.style.flexDirection = FlexDirection.Row;
            _window.Add(_inventoryTab);

            // Экипировка.
            var left = Column(270f);
            left.Add(Title("Экипировка"));
            _layerButton = new Button(ToggleLayer);
            StyleButton(_layerButton);
            _layerButton.style.fontSize = 12f;
            _layerButton.style.marginLeft = 0f;
            _layerButton.style.marginBottom = 6f;
            left.Add(_layerButton);
            _equipment = new VisualElement();
            _equipment.style.width = 262f;
            _equipment.style.height = 250f;
            _equipment.style.position = Position.Relative;
            left.Add(_equipment);
            _inventoryTab.Add(left);

            // Сетки.
            var middle = Column(0f);
            middle.style.marginLeft = 16f;
            middle.style.marginRight = 16f;
            middle.Add(Title("Карманы"));
            _pockets = new GridView();
            _pockets.ItemPointerDown = (stack, evt) => OnItemPointerDown(stack, _pockets.Grid, evt);
            _pockets.ItemHovered = ShowInfo;
            middle.Add(_pockets);
            _backpackTitle = Title("Рюкзак");
            _backpackTitle.style.marginTop = 12f;
            middle.Add(_backpackTitle);
            _backpack = new GridView();
            _backpack.ItemPointerDown = (stack, evt) => OnItemPointerDown(stack, _backpack.Grid, evt);
            _backpack.ItemHovered = ShowInfo;
            middle.Add(_backpack);
            _inventoryTab.Add(middle);

            // Карточка предмета и вес.
            var right = Column(240f);
            right.Add(Title("Предмет"));
            _infoTitle = new Label("—");
            _infoTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            _infoTitle.style.fontSize = 15f;
            _infoTitle.style.whiteSpace = WhiteSpace.Normal;
            right.Add(_infoTitle);
            _infoBody = new Label("Наведите на предмет.");
            _infoBody.style.whiteSpace = WhiteSpace.Normal;
            _infoBody.style.color = ItemVisuals.Muted;
            _infoBody.style.marginTop = 4f;
            _infoBody.style.flexGrow = 1f;
            right.Add(_infoBody);
            _weight = new Label();
            _weight.style.marginTop = 10f;
            right.Add(_weight);
            var barBack = new VisualElement();
            barBack.style.height = 6f;
            barBack.style.backgroundColor = new Color(1f, 1f, 1f, 0.08f);
            _weightBar = new VisualElement();
            _weightBar.style.height = 6f;
            barBack.Add(_weightBar);
            right.Add(barBack);
            _notice = new Label();
            _notice.style.color = new Color(1f, 0.6f, 0.5f);
            _notice.style.whiteSpace = WhiteSpace.Normal;
            _notice.style.marginTop = 6f;
            right.Add(_notice);
            _inventoryTab.Add(right);

            // Хотбар.
            var hotbarTitle = Title("Хотбар (перетащите предмет на слот)");
            hotbarTitle.style.marginTop = 12f;
            _window.Add(hotbarTitle);
            _hotbar = new VisualElement();
            _hotbar.style.flexDirection = FlexDirection.Row;
            _window.Add(_hotbar);

            _root.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            _root.RegisterCallback<PointerUpEvent>(OnPointerUp);
            SelectTab(0);
        }

        private static VisualElement Column(float width)
        {
            var column = new VisualElement();
            column.style.flexDirection = FlexDirection.Column;
            if (width > 0f)
                column.style.width = width;
            return column;
        }

        private static Label Title(string text)
        {
            var label = new Label(text);
            label.style.color = ItemVisuals.Muted;
            label.style.fontSize = 12f;
            label.style.marginBottom = 4f;
            return label;
        }

        private static void StyleButton(Button button)
        {
            button.style.backgroundColor = new Color(0.16f, 0.18f, 0.22f, 1f);
            button.style.color = ItemVisuals.Text;
            ItemVisuals.Border(button, new Color(1f, 1f, 1f, 0.15f), 1f, 4f);
        }

        private void SelectTab(int index)
        {
            _tab = index;
            for (int i = 0; i < _tabButtons.Count; i++)
                _tabButtons[i].style.backgroundColor = i == index ? new Color(0.3f, 0.34f, 0.42f, 1f) : new Color(0.16f, 0.18f, 0.22f, 1f);
            _inventoryTab.style.display = index == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            _stateTab.style.display = index == 3 ? DisplayStyle.Flex : DisplayStyle.None;
            _skillsPanel.style.display = index == 1 ? DisplayStyle.Flex : DisplayStyle.None;
            _craftPanel.style.display = index == 2 ? DisplayStyle.Flex : DisplayStyle.None;
            _craftPanel.MarkDirty();
            _stateTimer = 0f;
        }

        // ───────────────────────────── Состояние ─────────────────────────────

        private void BuildStateTab()
        {
            _stateTab = new VisualElement();
            _stateTab.style.flexDirection = FlexDirection.Row;
            _stateTab.style.minHeight = 380f;
            _stateTab.style.width = 820f;

            var left = Column(330f);
            left.Add(Title("Показатели"));
            _stateVitals = new Label();
            _stateVitals.style.whiteSpace = WhiteSpace.Normal;
            _stateVitals.style.fontSize = 13f;
            left.Add(_stateVitals);

            // Сон на полу: когда кровати рядом нет.
            var floorSleep = new Button(SleepOnFloor) { text = "Поспать на полу" };
            StyleButton(floorSleep);
            floorSleep.style.marginTop = 12f;
            floorSleep.style.marginLeft = 0f;
            floorSleep.style.alignSelf = Align.FlexStart;
            left.Add(floorSleep);
            var floorHint = new Label($"Бодрость — вдвое медленнее и только до {VitalsModel.FloorRestCap:0}%, точка возрождения не сохраняется. После — «Не выспался» на {VitalsModel.UnrestedHours:0} ч.");
            floorHint.style.whiteSpace = WhiteSpace.Normal;
            floorHint.style.color = ItemVisuals.Muted;
            floorHint.style.fontSize = 11f;
            left.Add(floorHint);
            _floorRefusal = new Label();
            _floorRefusal.style.whiteSpace = WhiteSpace.Normal;
            _floorRefusal.style.color = new Color(1f, 0.6f, 0.5f);
            left.Add(_floorRefusal);
            _stateTab.Add(left);

            var right = Column(0f);
            right.style.flexGrow = 1f;
            right.style.marginLeft = 20f;
            right.Add(Title("Состояния — что это и как лечить"));
            _stateList = new ScrollView();
            _stateList.style.maxHeight = 420f;
            right.Add(_stateList);
            _stateTab.Add(right);
            _window.Add(_stateTab);
        }

        private void SleepOnFloor()
        {
            var vitals = PlayerVitals.Local;
            if (vitals == null)
                return;
            string refusal = vitals.TrySleepOnFloor();
            _floorRefusal.text = refusal ?? string.Empty;
            if (refusal == null)
                SetOpen(false);
        }

        private void UpdateStateTab()
        {
            _stateTimer -= Time.unscaledDeltaTime;
            if (_stateTimer > 0f)
                return;
            _stateTimer = 0.25f;
            var vitals = PlayerVitals.Local;
            if (vitals == null)
            {
                _stateVitals.text = "Нет игрока с выживанием (полигон мира).";
                _stateList.Clear();
                return;
            }
            var m = vitals.Model;
            var env = vitals.Environment;
            var sb = new StringBuilder();
            sb.Append($"Здоровье: {m.Health:0} / {m.MaxHealth:0}");
            if (m.TraumaStacks > 0)
                sb.Append($"  (травма: −{VitalsModel.TraumaPerStack * m.TraumaStacks * 100f:0}%)");
            sb.Append($"\nСтамина: {m.Stamina:0} / {m.MaxStamina:0}");
            sb.Append($"\nСытость: {m.Calories:0} / {VitalsModel.MaxCalories:0} ккал");
            sb.Append($"\nВода: {m.Water:0} / {VitalsModel.MaxWater:0} мл");
            sb.Append($"\nБодрость: {m.Rest:0}%");
            sb.Append($"\nРассудок: {m.Sanity:0}%");
            sb.Append($"\n\nТемпература тела: {m.BodyTemperature:0.0} °C");
            sb.Append($"\nВоздух: {env.AirTemperature:0} °C, одежда греет на {env.Warmth:0} °C");
            sb.Append($"\nКомфорт: {VitalsModel.ComfortMin:0}…{VitalsModel.ComfortMax:0} °C с учётом одежды");
            var (fat, protein, carbs) = m.DietShares;
            sb.Append("\n\nРацион за сутки:");
            if (fat + protein + carbs <= 0f)
                sb.Append(" почти ничего не ели");
            else
                sb.Append($"\n  жиры {fat * 100f:0}% (норма 15–40)\n  белки {protein * 100f:0}% (норма 12–30)\n  углеводы {carbs * 100f:0}% (норма 35–70)\n  {(m.DietBalanced == true ? "сбалансирован" : "однообразно — ешьте разное")}");
            if (vitals.HasSpawnPoint)
                sb.Append("\n\nТочка возрождения: у кровати");
            _stateVitals.text = sb.ToString();

            m.GetConditions(_conditions);
            _stateList.Clear();
            if (_conditions.Count == 0)
            {
                var none = new Label("Ничего не беспокоит.");
                none.style.color = ItemVisuals.Muted;
                _stateList.Add(none);
            }
            foreach (var kind in _conditions)
            {
                var info = StatusCatalog.Get(kind);
                var status = StatusCatalog.IsStored(kind) ? m.Find(kind) : null;
                var title = new Label(info.Name + StatusSuffix(status));
                title.style.unityFontStyleAndWeight = FontStyle.Bold;
                title.style.color = info.Positive ? new Color(0.55f, 0.9f, 0.6f) : new Color(1f, 0.6f, 0.55f);
                title.style.marginTop = 6f;
                _stateList.Add(title);
                var text = new Label(info.Description + (string.IsNullOrEmpty(info.Treatment) ? "" : "\nЛечение: " + info.Treatment));
                text.style.whiteSpace = WhiteSpace.Normal;
                text.style.color = ItemVisuals.Muted;
                text.style.fontSize = 12f;
                _stateList.Add(text);
            }
        }

        private static string StatusSuffix(Status status)
        {
            if (status == null)
                return string.Empty;
            var sb = new StringBuilder();
            if (status.Splinted)
                sb.Append(" (в шине)");
            if (status.Stacks > 1)
                sb.Append($" ×{status.Stacks}");
            if (!float.IsInfinity(status.Remaining))
                sb.Append($" · ещё {status.Remaining:0.#} ч");
            return sb.ToString();
        }

        private static string TreatsText(MedicalTreatment treats)
        {
            var parts = new List<string>();
            if ((treats & MedicalTreatment.Bleeding) != 0) parts.Add("кровотечение");
            if ((treats & MedicalTreatment.Fracture) != 0) parts.Add("перелом (шина)");
            if ((treats & MedicalTreatment.Pain) != 0) parts.Add("боль");
            if ((treats & MedicalTreatment.Infection) != 0) parts.Add("инфекцию");
            if ((treats & MedicalTreatment.Poisoning) != 0) parts.Add("отравление");
            return string.Join(", ", parts);
        }

        private void ToggleLayer()
        {
            _layer = _layer == EquipLayer.Armor ? EquipLayer.Clothing : EquipLayer.Armor;
            _dirty = true;
        }

        // ───────────────────────────── Обновление ─────────────────────────────

        private void Refresh()
        {
            _dirty = false;
            var items = _inventory.Items;
            _pockets.Bind(_inventory.Pockets, items, _dragging);
            var backpack = _inventory.Equipment.Backpack;
            _backpack.Bind(backpack?.Contents, items, _dragging);
            _backpackTitle.text = backpack != null ? $"Рюкзак: {items.Get(backpack.Def).DisplayName}" : "Рюкзака нет — наденьте рюкзак, чтобы носить больше";
            _layerButton.text = _layer == EquipLayer.Armor ? "Слой: броня · переключить на одежду" : "Слой: одежда · переключить на броню";
            BuildEquipment();
            BuildHotbar();

            float weight = _inventory.TotalWeight();
            float capacity = PlayerInventory.Capacity;
            _weight.text = $"Вес: {weight:0.0} / {capacity:0} кг";
            float fraction = weight / Mathf.Max(1f, capacity);
            _weightBar.style.width = Length.Percent(Mathf.Clamp01(fraction) * 100f);
            _weightBar.style.backgroundColor = fraction > 1f ? new Color(0.95f, 0.3f, 0.3f) : fraction > 0.7f ? new Color(0.95f, 0.75f, 0.3f) : new Color(0.4f, 0.75f, 0.5f);
        }

        private void BuildEquipment()
        {
            _equipment.Clear();
            // Силуэт-заглушка (модель игрока — позже).
            // Слоты — две колонки по бокам силуэта, чтобы не перекрывать его.
            AddShape(_equipment, 113f, 6f, 36f, 36f, 18f);
            AddShape(_equipment, 101f, 46f, 60f, 92f, 6f);
            AddShape(_equipment, 77f, 50f, 20f, 86f, 6f);
            AddShape(_equipment, 165f, 50f, 20f, 86f, 6f);
            AddShape(_equipment, 103f, 142f, 24f, 104f, 6f);
            AddShape(_equipment, 135f, 142f, 24f, 104f, 6f);

            AddSlot(EquipSlot.Head, _layer, 0f, 16f);
            AddSlot(EquipSlot.Torso, _layer, 0f, 98f);
            AddSlot(EquipSlot.Legs, _layer, 0f, 180f);
            AddSlot(EquipSlot.Backpack, EquipLayer.Armor, 206f, 16f);
            AddSlot(EquipSlot.Arms, _layer, 206f, 98f);
            AddSlot(EquipSlot.Feet, _layer, 206f, 180f);
        }

        private static void AddShape(VisualElement parent, float x, float y, float w, float h, float radius)
        {
            var shape = new VisualElement { pickingMode = PickingMode.Ignore };
            shape.style.position = Position.Absolute;
            shape.style.left = x;
            shape.style.top = y;
            shape.style.width = w;
            shape.style.height = h;
            shape.style.backgroundColor = new Color(1f, 1f, 1f, 0.06f);
            ItemVisuals.Border(shape, new Color(1f, 1f, 1f, 0.05f), 1f, radius);
            parent.Add(shape);
        }

        private void AddSlot(EquipSlot slot, EquipLayer layer, float x, float y)
        {
            var element = new VisualElement { userData = (slot, layer) };
            element.style.position = Position.Absolute;
            element.style.left = x;
            element.style.top = y;
            element.style.width = element.style.height = 56f;
            element.style.backgroundColor = new Color(0f, 0f, 0f, 0.35f);
            ItemVisuals.Border(element, new Color(1f, 1f, 1f, 0.18f), 1f, 4f);
            var caption = new Label(ItemVisuals.SlotName(slot)) { pickingMode = PickingMode.Ignore };
            caption.style.position = Position.Absolute;
            caption.style.top = -15f;
            caption.style.fontSize = 10f;
            caption.style.color = ItemVisuals.Muted;
            element.Add(caption);

            var stack = _inventory.Equipment.Get(slot, layer);
            if (stack != null)
            {
                var def = _inventory.Items.Get(stack.Def);
                element.Add(ItemVisuals.Icon(def, 56f, 56f));
                element.style.opacity = stack == _dragging ? 0.3f : 1f;
                element.RegisterCallback<PointerDownEvent>(evt => OnEquippedPointerDown(stack, evt));
                element.RegisterCallback<PointerEnterEvent>(_ => ShowInfo(stack));
            }
            _equipment.Add(element);
        }

        private void BuildHotbar()
        {
            _hotbar.Clear();
            for (int i = 0; i < Hotbar.Size; i++)
            {
                int index = i;
                var element = new VisualElement { userData = index };
                element.style.width = element.style.height = 52f;
                element.style.marginRight = 4f;
                element.style.backgroundColor = new Color(0f, 0f, 0f, 0.35f);
                ItemVisuals.Border(element, i == _inventory.Hotbar.Selected ? ItemVisuals.Highlight : new Color(1f, 1f, 1f, 0.18f), i == _inventory.Hotbar.Selected ? 2f : 1f, 4f);
                var number = new Label((i + 1).ToString()) { pickingMode = PickingMode.Ignore };
                number.style.position = Position.Absolute;
                number.style.left = 3f;
                number.style.top = 0f;
                number.style.fontSize = 10f;
                number.style.color = ItemVisuals.Muted;
                var stack = _inventory.Hotbar[i];
                if (stack != null && _inventory.Owns(stack))
                {
                    element.Add(ItemVisuals.Icon(_inventory.Items.Get(stack.Def), 52f, 52f));
                    element.Add(ItemVisuals.CountLabel(_inventory.CountOf(stack.Def)));
                    // ПКМ по слоту — убрать предмет из хотбара.
                    element.RegisterCallback<PointerDownEvent>(evt =>
                    {
                        if (evt.button == 1)
                            _inventory.Hotbar.Assign(index, null);
                        else
                            _inventory.Hotbar.Select(index);
                    });
                    element.RegisterCallback<PointerEnterEvent>(_ => ShowInfo(stack));
                }
                element.Add(number);
                _hotbar.Add(element);
            }
        }

        private void ShowInfo(ItemStack stack)
        {
            if (stack == null || _inventory == null)
                return;
            var def = _inventory.Items.Get(stack.Def);
            _infoTitle.text = def.DisplayName + (stack.Count > 1 ? $" ×{stack.Count}" : "") + (stack.Spoiled ? " (испорчено)" : "");
            var sb = new StringBuilder();
            sb.Append(ItemVisuals.CategoryName(def.Category));
            sb.Append($"\nВес: {def.Weight:0.##} кг{(stack.Count > 1 ? $" (всего {def.Weight * stack.Count:0.##})" : "")}");
            sb.Append($"\nРазмер: {def.GridSize.x}×{def.GridSize.y}");
            if (def.IsTool)
            {
                if (def.BlockDamage > 0) sb.Append($"\nУрон по блокам: {def.BlockDamage}");
                if (def.PropDamage > 0) sb.Append($"\nУрон по мебели: {def.PropDamage}");
                if (def.DrillStrength > 0) sb.Append($"\nСила бура: {def.DrillStrength:0.##}");
            }
            if (def.Calories > 0 || def.Water > 0)
                sb.Append($"\nКалории: {def.Calories:0} · вода: {def.Water:0} мл");
            if (def.Fat + def.Protein + def.Carbs > 0)
                sb.Append($"\nЖиры {def.Fat:0} г · белки {def.Protein:0} г · углеводы {def.Carbs:0} г");
            if (stack.Spoiled)
                sb.Append("\nИспорчено: вдвое меньше калорий и отравление");
            if (def.Treats != MedicalTreatment.None)
                sb.Append($"\nЛечит: {TreatsText(def.Treats)}");
            if (def.Heal > 0)
                sb.Append($"\nЗдоровье: +{def.Heal:0}");
            if (def.Warmth != 0)
                sb.Append($"\nТепло: +{def.Warmth:0} °C");
            if (def.IsEquippable)
                sb.Append($"\nСлот: {ItemVisuals.SlotName(def.Slot)}{(def.Slot != EquipSlot.Backpack ? (def.Layer == EquipLayer.Armor ? " (броня)" : " (одежда)") : "")}");
            if (def.Armor > 0)
                sb.Append($"\nЗащита: {def.Armor:0}");
            if (def.HasWear)
                sb.Append($"\nПрочность: {stack.Durability * 100f:0}%{(def.IsRepairable ? " · чинится на верстаке" + (def.Electric ? " (нужен «Техник»)" : "") : "")}");
            if (def.FuelSeconds > 0)
                sb.Append($"\nТопливо для плиты: {def.FuelSeconds:0} с");
            if (def.IsReadable)
                sb.Append(PlayerCrafting.Local != null && CraftingSystem.Instance != null && CraftingSystem.Instance.TryGetRecipe(def.TeachesRecipe, out var taught)
                    ? $"\nОткрывает рецепт: {taught.DisplayName}{(PlayerCrafting.Local.IsLearned(taught) ? " (уже изучен)" : " · ПКМ — изучить")}"
                    : "\nПКМ — изучить");
            if (def.IsContainer)
                sb.Append($"\nВместимость: {def.ContainerGrid.x}×{def.ContainerGrid.y}");
            if (!string.IsNullOrEmpty(def.Description))
                sb.Append("\n\n").Append(def.Description);
            _infoBody.text = sb.ToString();
        }

        // ───────────────────────────── Перетаскивание ─────────────────────────────

        private void OnItemPointerDown(ItemStack stack, GridInventory grid, PointerDownEvent evt)
        {
            if (_dragging != null || grid == null)
                return;
            evt.StopPropagation();
            if (evt.button == 1)
            {
                int slot = _inventory.Hotbar.IndexOf(stack);
                bool used = _inventory.Use(stack);
                _notice.text = used ? string.Empty : _inventory.LastRefusal ?? string.Empty;
                if (slot >= 0)
                    _inventory.RefillHotbar(slot, stack.Def);
                return;
            }
            if (evt.button != 0)
                return;
            if (evt.shiftKey)
            {
                // Быстрое перекладывание между карманами и рюкзаком.
                var other = grid == _inventory.Pockets ? _inventory.Backpack : _inventory.Pockets;
                if (other != null && other != stack.Contents)
                {
                    grid.Remove(stack);
                    if (other.TryAdd(stack) > 0)
                        grid.TryAdd(stack);
                }
                return;
            }
            grid.TryGetPlacement(stack, out var placement);
            BeginDrag(stack, Source.Grid, grid, placement.Rotated, evt);
        }

        private void OnEquippedPointerDown(ItemStack stack, PointerDownEvent evt)
        {
            if (_dragging != null)
                return;
            evt.StopPropagation();
            if (evt.button == 1)
            {
                _inventory.Unequip(stack);
                return;
            }
            if (evt.button == 0)
                BeginDrag(stack, Source.Equipment, null, false, evt);
        }

        private void BeginDrag(ItemStack stack, Source source, GridInventory grid, bool rotated, PointerDownEvent evt)
        {
            _dragging = stack;
            _dragSource = source;
            _dragGrid = grid;
            _dragRotated = rotated;
            _pointerId = evt.pointerId;
            _pointer = evt.position;
            _root.CapturePointer(_pointerId);
            _ghost = new VisualElement { pickingMode = PickingMode.Ignore };
            _ghost.style.position = Position.Absolute;
            _ghost.style.opacity = 0.85f;
            _root.Add(_ghost);
            UpdateGhost();
            ShowInfo(stack);
            _dirty = true;
        }

        private bool IsRotatable(ItemStack stack)
        {
            var (w, h) = _inventory.Items.GridSize(stack.Def);
            return w != h;
        }

        private void UpdateGhost()
        {
            if (_ghost == null)
                return;
            _ghost.Clear();
            var (w, h) = _inventory.Items.GridSize(_dragging.Def);
            if (_dragRotated)
                (w, h) = (h, w);
            float width = w * ItemVisuals.Cell - 2f, height = h * ItemVisuals.Cell - 2f;
            _ghost.style.width = width;
            _ghost.style.height = height;
            _ghostSize = new Vector2(width, height);
            _ghost.style.backgroundColor = ItemVisuals.ItemBack;
            ItemVisuals.Border(_ghost, ItemVisuals.Highlight, 1f, 3f);
            _ghost.Add(ItemVisuals.Icon(_inventory.Items.Get(_dragging.Def), width, height, _dragRotated));
            _ghost.Add(ItemVisuals.CountLabel(_dragging.Count));
            MoveGhost();
        }

        private void MoveGhost()
        {
            if (_ghost == null)
                return;
            Vector2 local = _root.WorldToLocal(_pointer);
            // Размер известен сразу: resolvedStyle до первой раскладки — NaN, и призрак прилипал к углу окна.
            _ghost.style.left = local.x - _ghostSize.x * 0.5f;
            _ghost.style.top = local.y - _ghostSize.y * 0.5f;
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            if (_dragging == null || evt.pointerId != _pointerId)
                return;
            _pointer = evt.position;
            MoveGhost();
        }

        private void UpdatePreview()
        {
            _pockets.HidePreview();
            _backpack.HidePreview();
            if (_dragging == null)
                return;
            var grid = GridUnderPointer(out var view);
            if (grid == null)
                return;
            var (w, h) = grid.SizeOf(_dragging, _dragRotated);
            var cell = view.CellAt(_pointer, w, h);
            view.ShowPreview(cell.x, cell.y, w, h, grid.CanPlace(_dragging, cell.x, cell.y, _dragRotated) && grid != _dragging.Contents);
        }

        private GridInventory GridUnderPointer(out GridView view)
        {
            view = null;
            var picked = _root.panel.Pick(_pointer);
            for (var e = picked; e != null; e = e.parent)
            {
                if (e == _pockets || e == _backpack)
                {
                    view = (GridView)e;
                    return view.Grid;
                }
            }
            return null;
        }

        private void OnPointerUp(PointerUpEvent evt)
        {
            if (_dragging == null || evt.pointerId != _pointerId)
                return;
            _pointer = evt.position;
            Drop();
            CancelDrag();
        }

        private void Drop()
        {
            var stack = _dragging;
            var items = _inventory.Items;
            var def = items.Get(stack.Def);
            var picked = _root.panel.Pick(_pointer);

            // Слоты экипировки и хотбара помечены userData.
            for (var e = picked; e != null; e = e.parent)
            {
                if (e.userData is (EquipSlot slot, EquipLayer layer))
                {
                    if (_dragSource == Source.Grid && def.Slot == slot && (slot == EquipSlot.Backpack || def.Layer == layer))
                        _inventory.Equip(stack);
                    return;
                }
                if (e.userData is int hotbarIndex)
                {
                    if (_dragSource == Source.Grid)
                        _inventory.Hotbar.Assign(hotbarIndex, stack);
                    return;
                }
            }

            var grid = GridUnderPointer(out var view);
            if (grid != null)
            {
                if (grid == stack.Contents)
                    return; // рюкзак не кладётся в самого себя
                var (w, h) = grid.SizeOf(stack, _dragRotated);
                var cell = view.CellAt(_pointer, w, h);
                // Отпустили на такой же предмет — слить стаки.
                var under = grid.GetAt(cell.x + w / 2, cell.y + h / 2);
                if (under != null && under != stack && under.Def == stack.Def && _dragSource == Source.Grid && _dragGrid == grid)
                {
                    grid.Merge(stack, under);
                    return;
                }
                if (_dragSource == Source.Grid && _dragGrid == grid)
                {
                    grid.TryMove(stack, cell.x, cell.y, _dragRotated);
                    return;
                }
                if (!grid.CanPlace(stack, cell.x, cell.y, _dragRotated))
                    return;
                if (_dragSource == Source.Grid)
                    _dragGrid.Remove(stack);
                else
                    _inventory.Equipment.Remove(stack);
                grid.TryPlace(stack, cell.x, cell.y, _dragRotated);
                return;
            }

            // Отпустили вне окна — выбросить.
            bool insideWindow = false;
            for (var e = picked; e != null; e = e.parent)
                if (e == _window)
                    insideWindow = true;
            if (!insideWindow)
                _inventory.Drop(stack);
        }

        private void CancelDrag()
        {
            if (_pointerId >= 0 && _root.HasPointerCapture(_pointerId))
                _root.ReleasePointer(_pointerId);
            _pointerId = -1;
            _ghost?.RemoveFromHierarchy();
            _ghost = null;
            _dragging = null;
            _dragSource = Source.None;
            _dragGrid = null;
            _dirty = true;
        }
    }
}
