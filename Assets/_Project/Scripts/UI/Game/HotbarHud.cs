using UnityEngine;
using UnityEngine.UIElements;
using VoidMall.Items;
using VoidMall.Core;

namespace VoidMall.UI
{
    /// <summary>Хотбар внизу экрана: 9 слотов (1–9, колесо), иконки, количество, выбранный подсвечен.</summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class HotbarHud : MonoBehaviour
    {
        private VisualElement _bar;
        private PlayerInventory _inventory;
        private bool _dirty = true;

        public void Initialize()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;
            DevToolsStyle.FullScreenRoot(root);
            _bar = new VisualElement { pickingMode = PickingMode.Ignore };
            _bar.style.position = Position.Absolute;
            _bar.style.bottom = 12f;
            _bar.style.left = 0f;
            _bar.style.right = 0f;
            _bar.style.flexDirection = FlexDirection.Row;
            _bar.style.justifyContent = Justify.Center;
            root.Add(_bar);
        }

        private static readonly Perf.Probe UpdateProbe = Perf.Register("UI.Hotbar");

        private void Update()
        {
            using var perf = Perf.Measure(UpdateProbe);
            if (_inventory == null && PlayerInventory.Local != null)
            {
                _inventory = PlayerInventory.Local;
                _inventory.Changed += () => _dirty = true;
            }
            if (_inventory == null || !_dirty)
                return;
            _dirty = false;
            Rebuild();
        }

        private void Rebuild()
        {
            _bar.Clear();
            for (int i = 0; i < Hotbar.Size; i++)
            {
                bool selected = i == _inventory.Hotbar.Selected;
                var slot = new VisualElement { pickingMode = PickingMode.Ignore };
                slot.style.width = slot.style.height = 50f;
                slot.style.marginLeft = slot.style.marginRight = 2f;
                slot.style.backgroundColor = new Color(0f, 0f, 0f, selected ? 0.6f : 0.4f);
                ItemVisuals.Border(slot, selected ? ItemVisuals.Highlight : new Color(1f, 1f, 1f, 0.15f), selected ? 2f : 1f, 4f);
                var stack = _inventory.Hotbar[i];
                if (stack != null && _inventory.Owns(stack))
                {
                    slot.Add(ItemVisuals.Icon(_inventory.Items.Get(stack.Def), 50f, 50f));
                    slot.Add(ItemVisuals.CountLabel(_inventory.CountOf(stack.Def)));
                    var wear = ItemVisuals.WearBar(stack, _inventory.Items.Get(stack.Def));
                    if (wear != null)
                        slot.Add(wear);
                }
                var number = new Label((i + 1).ToString()) { pickingMode = PickingMode.Ignore };
                number.style.position = Position.Absolute;
                number.style.left = 3f;
                number.style.top = 0f;
                number.style.fontSize = 10f;
                number.style.color = new Color(1f, 1f, 1f, 0.6f);
                slot.Add(number);
                _bar.Add(slot);
            }
        }
    }
}
