using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using VoidMall.Core;
using VoidMall.Crafting;
using VoidMall.Input;
using VoidMall.Items;

namespace VoidMall.UI
{
    /// <summary>
    /// Окно крафтовой станции (E по станции): панель крафта с рецептами станции, выдача готового, у плиты —
    /// топливо и прогрев испорченного, у верстака — починка инструментов. Один игрок за станцией, второму —
    /// «Станция занята». Закрывается E, Esc, Tab или если отойти дальше 4 м.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class StationWindow : MonoBehaviour
    {
        private const float MaxDistance = 4f;

        public static StationWindow Instance { get; private set; }

        public bool IsOpen => _station != null;

        private InputService _input;
        private VisualElement _root;
        private Label _title;
        private CraftPanel _craft;
        private VisualElement _output;
        private VisualElement _fuelBox;
        private VisualElement _fuelBar;
        private Label _fuelLabel;
        private VisualElement _fuelButtons;
        private VisualElement _service;
        private Label _serviceTitle;
        private Label _notice;
        private StationState _station;
        private bool _dirty;
        private int _openedFrame;

        public void Initialize(InputService input)
        {
            Instance = this;
            _input = input;
            _root = GetComponent<UIDocument>().rootVisualElement;
            DevToolsStyle.FullScreenRoot(_root);
            Build();
            _root.style.display = DisplayStyle.None;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Instance = null;

        private void Build()
        {
            var backdrop = new VisualElement();
            backdrop.style.position = Position.Absolute;
            backdrop.style.left = backdrop.style.right = backdrop.style.top = backdrop.style.bottom = 0f;
            backdrop.style.backgroundColor = new Color(0f, 0f, 0f, 0.45f);
            backdrop.style.alignItems = Align.Center;
            backdrop.style.justifyContent = Justify.Center;
            _root.Add(backdrop);

            var window = new VisualElement();
            window.style.backgroundColor = ItemVisuals.Panel;
            ItemVisuals.Border(window, new Color(1f, 1f, 1f, 0.12f), 1f, 6f);
            window.style.paddingLeft = window.style.paddingRight = 16f;
            window.style.paddingTop = window.style.paddingBottom = 12f;
            window.style.color = ItemVisuals.Text;
            backdrop.Add(window);

            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.marginBottom = 8f;
            _title = new Label();
            _title.style.unityFontStyleAndWeight = FontStyle.Bold;
            _title.style.fontSize = 16f;
            _title.style.flexGrow = 1f;
            header.Add(_title);
            var hint = new Label("E / Esc — закрыть");
            hint.style.color = ItemVisuals.Muted;
            hint.style.fontSize = 11f;
            header.Add(hint);
            window.Add(header);

            var body = new VisualElement();
            body.style.flexDirection = FlexDirection.Row;
            window.Add(body);
            _craft = new CraftPanel();
            body.Add(_craft);

            var side = new VisualElement();
            side.style.width = 250f;
            side.style.marginLeft = 16f;
            body.Add(side);

            side.Add(CraftPanel.Title("Готово (клик — забрать)"));
            _output = new VisualElement();
            _output.style.flexDirection = FlexDirection.Row;
            _output.style.flexWrap = Wrap.Wrap;
            _output.style.minHeight = 50f;
            side.Add(_output);
            var takeAll = CraftPanel.SmallButton("Забрать всё", () => PlayerCrafting.Local?.TakeAllOutput(_station));
            takeAll.style.marginLeft = 0f;
            side.Add(takeAll);

            _fuelBox = new VisualElement();
            _fuelBox.Add(CraftPanel.Title("Топливо"));
            var back = new VisualElement();
            back.style.height = 8f;
            back.style.backgroundColor = new Color(1f, 1f, 1f, 0.08f);
            _fuelBar = new VisualElement();
            _fuelBar.style.height = 8f;
            _fuelBar.style.backgroundColor = new Color(0.95f, 0.55f, 0.2f);
            back.Add(_fuelBar);
            _fuelBox.Add(back);
            _fuelLabel = new Label();
            _fuelLabel.style.fontSize = 11f;
            _fuelLabel.style.color = ItemVisuals.Muted;
            _fuelBox.Add(_fuelLabel);
            _fuelButtons = new VisualElement();
            _fuelBox.Add(_fuelButtons);
            side.Add(_fuelBox);

            _serviceTitle = CraftPanel.Title(string.Empty);
            side.Add(_serviceTitle);
            _service = new ScrollView();
            _service.style.maxHeight = 200f;
            side.Add(_service);

            _notice = new Label();
            _notice.style.color = new Color(1f, 0.6f, 0.5f);
            _notice.style.whiteSpace = WhiteSpace.Normal;
            side.Add(_notice);
        }

        /// <summary>Открыть станцию. Null — открыто, иначе — почему нет («Станция занята»).</summary>
        public string Open(StationState station)
        {
            var player = PlayerInventory.Local != null ? PlayerInventory.Local.gameObject : null;
            if (station == null || player == null || CraftingSystem.Instance == null)
                return "Станция недоступна";
            if (IsOpen)
                Close();
            string refusal = CraftingSystem.Instance.TryOccupy(station, player);
            if (refusal != null)
                return refusal;
            _station = station;
            _station.Changed += MarkDirty;
            if (PlayerInventory.Local != null)
                PlayerInventory.Local.Changed += MarkDirty;
            _title.text = station.Name + (station.Autonomous ? " — готовит сама, пока есть топливо" : " — работает, пока окно открыто");
            _craft.Bind(station);
            _notice.text = string.Empty;
            _root.style.display = DisplayStyle.Flex;
            _input.Push(InputContext.Menu);
            _openedFrame = Time.frameCount;
            _dirty = true;
            return null;
        }

        public void Close()
        {
            if (!IsOpen)
                return;
            _station.Changed -= MarkDirty;
            if (PlayerInventory.Local != null)
            {
                PlayerInventory.Local.Changed -= MarkDirty;
                CraftingSystem.Instance?.Release(_station, PlayerInventory.Local.gameObject);
            }
            _station = null;
            _root.style.display = DisplayStyle.None;
            _input.Pop(InputContext.Menu);
        }

        private void MarkDirty() => _dirty = true;

        private static readonly Perf.Probe UpdateProbe = Perf.Register("UI.StationWindow");

        private void Update()
        {
            using var perf = Perf.Measure(UpdateProbe);
            if (!IsOpen)
                return;
            var keyboard = Keyboard.current;
            var inventory = PlayerInventory.Local;
            bool tooFar = inventory == null || Vector3.Distance(inventory.transform.position, _station.Position) > MaxDistance + 1.5f;
            bool keys = keyboard != null && Time.frameCount > _openedFrame
                        && (keyboard.escapeKey.wasPressedThisFrame || keyboard.eKey.wasPressedThisFrame || keyboard.tabKey.wasPressedThisFrame);
            if (tooFar || keys || _input.Current == InputContext.Console)
            {
                Close();
                return;
            }
            _craft.Tick();
            if (_station.NeedsFuel)
            {
                _fuelBar.style.width = Length.Percent(_station.Fuel / StationState.MaxFuel * 100f);
                string text = _station.Fuel > 0f ? $"Хватит на {_station.Fuel:0} с готовки" : "Топлива нет — подбросьте доски, картон или газовый баллончик";
                if (_fuelLabel.text != text)
                    _fuelLabel.text = text;
            }
            if (_dirty)
            {
                _dirty = false;
                Rebuild();
            }
        }

        private void Rebuild()
        {
            var inventory = PlayerInventory.Local;
            var crafting = PlayerCrafting.Local;
            if (inventory == null || crafting == null)
                return;
            var items = inventory.Items;

            // Выдача.
            _output.Clear();
            foreach (var placement in _station.Output.Items)
            {
                var stack = placement.Stack;
                var cell = new VisualElement();
                cell.style.width = cell.style.height = 46f;
                cell.style.marginRight = cell.style.marginBottom = 3f;
                cell.style.backgroundColor = ItemVisuals.ItemBack;
                ItemVisuals.Border(cell, new Color(1f, 1f, 1f, 0.15f), 1f, 3f);
                cell.Add(ItemVisuals.Icon(items.Get(stack.Def), 46f, 46f));
                cell.Add(ItemVisuals.CountLabel(stack.Count));
                cell.tooltip = items.Get(stack.Def).DisplayName;
                cell.RegisterCallback<PointerDownEvent>(_ => crafting.TakeOutput(_station, stack));
                _output.Add(cell);
            }
            if (_station.Output.Items.Count == 0)
            {
                var empty = new Label("пусто");
                empty.style.color = ItemVisuals.Muted;
                _output.Add(empty);
            }

            // Топливо.
            _fuelBox.style.display = _station.NeedsFuel ? DisplayStyle.Flex : DisplayStyle.None;
            if (_station.NeedsFuel)
            {
                _fuelButtons.Clear();
                for (int i = 0; i < items.Count; i++)
                {
                    var def = items.Get(i);
                    ushort index = (ushort)i;
                    int count = inventory.CountOf(index);
                    if (def.FuelSeconds <= 0f || count <= 0)
                        continue;
                    var button = CraftPanel.SmallButton($"+ {def.DisplayName} ×{count} ({def.FuelSeconds:0} с)", () =>
                    {
                        _notice.text = CraftingSystem.Instance.AddFuel(_station, inventory, index) ?? string.Empty;
                    });
                    button.style.marginLeft = 0f;
                    _fuelButtons.Add(button);
                }
                if (_fuelButtons.childCount == 0)
                {
                    var none = new Label("Нечем топить: доски, картон, газовый баллончик");
                    none.style.fontSize = 11f;
                    none.style.color = ItemVisuals.Muted;
                    _fuelButtons.Add(none);
                }
            }

            // Починка (верстак) или прогрев испорченного (плита).
            _service.Clear();
            _serviceTitle.style.display = DisplayStyle.None;
            if (_station.Kind == CraftStation.Workbench)
            {
                _serviceTitle.text = "Починка инструментов";
                _serviceTitle.style.display = DisplayStyle.Flex;
                foreach (var stack in Stacks(inventory))
                {
                    var def = items.Get(stack.Def);
                    if (!def.IsRepairable || stack.Durability >= 0.999f)
                        continue;
                    string cost = string.Join(", ", def.RepairCost.ConvertAll(c => c.Item != null ? $"{c.Item.DisplayName} ×{c.Count}" : "?"));
                    string why = crafting.WhyCannotRepair(stack, _station);
                    _service.Add(ServiceRow($"{def.DisplayName} {stack.Durability * 100f:0}% · {cost}", "Починить", why,
                        () => _notice.text = crafting.Repair(stack, _station) ?? string.Empty));
                }
                if (_service.childCount == 0)
                    _service.Add(Muted("Изношенных инструментов нет"));
            }
            else if (_station.Kind == CraftStation.Cooking)
            {
                _serviceTitle.text = "Прогреть испорченное";
                _serviceTitle.style.display = DisplayStyle.Flex;
                foreach (var stack in Stacks(inventory))
                {
                    if (!stack.Spoiled)
                        continue;
                    var def = items.Get(stack.Def);
                    string why = crafting.WhyCannotReheat(stack, _station);
                    _service.Add(ServiceRow($"{def.DisplayName} ×{stack.Count} — перестанет отравлять", "Прогреть", why,
                        () => _notice.text = crafting.Reheat(stack, _station) ?? string.Empty));
                }
                if (_service.childCount == 0)
                    _service.Add(Muted("Испорченной еды нет"));
            }
            _craft.MarkDirty();
        }

        private static IEnumerable<ItemStack> Stacks(PlayerInventory inventory)
        {
            var list = new List<ItemStack>();
            foreach (var placement in inventory.Pockets.Items)
                list.Add(placement.Stack);
            if (inventory.Backpack != null)
                foreach (var placement in inventory.Backpack.Items)
                    list.Add(placement.Stack);
            return list;
        }

        private static VisualElement ServiceRow(string text, string action, string why, System.Action onClick)
        {
            var row = new VisualElement();
            row.style.marginBottom = 4f;
            var label = new Label(text);
            label.style.fontSize = 12f;
            label.style.whiteSpace = WhiteSpace.Normal;
            row.Add(label);
            var line = new VisualElement();
            line.style.flexDirection = FlexDirection.Row;
            line.style.alignItems = Align.Center;
            var button = CraftPanel.SmallButton(action, onClick);
            button.style.marginLeft = 0f;
            button.SetEnabled(why == null);
            line.Add(button);
            if (why != null)
            {
                var reason = new Label(why);
                reason.style.fontSize = 11f;
                reason.style.color = new Color(1f, 0.6f, 0.5f);
                reason.style.whiteSpace = WhiteSpace.Normal;
                reason.style.flexShrink = 1f;
                line.Add(reason);
            }
            row.Add(line);
            return row;
        }

        private static Label Muted(string text)
        {
            var label = new Label(text);
            label.style.color = ItemVisuals.Muted;
            label.style.fontSize = 11f;
            return label;
        }
    }
}
