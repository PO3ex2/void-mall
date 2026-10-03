using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using VoidMall.Core;
using VoidMall.Input;
using VoidMall.Player;

namespace VoidMall.UI
{
    /// <summary>
    /// Панель F4: ползунки веса и травм для проверки влияния модификаторов на движение,
    /// пока нет инвентаря и модуля выживания.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class DebugModifiersPanel : MonoBehaviour
    {
        private InputService _input;
        private InputAction _toggle;
        private VisualElement _panel;
        private Slider _load;
        private Toggle _leftLeg;
        private Toggle _rightLeg;
        private SliderInt _arms;
        private Slider _pain;
        private Toggle _infiniteStamina;
        private Label _status;
        private bool _open;

        public void Initialize(InputService input)
        {
            _input = input;
            _toggle = input.Action(InputService.Maps.Global, "ToggleDebugPanel");

            var root = GetComponent<UIDocument>().rootVisualElement;
            DevToolsStyle.FullScreenRoot(root);

            _panel = new VisualElement();
            DevToolsStyle.Panel(_panel);
            _panel.style.position = Position.Absolute;
            _panel.style.right = 12f;
            _panel.style.top = 12f;
            _panel.style.width = 380f;
            root.Add(_panel);

            var title = new Label("Отладка: вес и травмы");
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginBottom = 6f;
            _panel.Add(title);

            _load = new Slider($"Вес / лимит", 0f, MovementRules.OverloadMax) { showInputField = true };
            _leftLeg = new Toggle("Перелом левой ноги");
            _rightLeg = new Toggle("Перелом правой ноги");
            _arms = new SliderInt("Травмы рук", 0, 2) { showInputField = true };
            _pain = new Slider("Боль", 0f, 1f) { showInputField = true };
            _infiniteStamina = new Toggle("Бесконечная стамина");
            _panel.Add(_load);
            _panel.Add(_leftLeg);
            _panel.Add(_rightLeg);
            _panel.Add(_arms);
            _panel.Add(_pain);
            _panel.Add(_infiniteStamina);

            _load.RegisterValueChangedCallback(e => Apply(s => s.LoadFraction = e.newValue));
            _leftLeg.RegisterValueChangedCallback(e => Apply(s => s.LeftLegBroken = e.newValue));
            _rightLeg.RegisterValueChangedCallback(e => Apply(s => s.RightLegBroken = e.newValue));
            _arms.RegisterValueChangedCallback(e => Apply(s => s.InjuredArms = e.newValue));
            _pain.RegisterValueChangedCallback(e => Apply(s => s.Pain = e.newValue));
            _infiniteStamina.RegisterValueChangedCallback(e =>
            {
                if (TryGetStamina(out var pool))
                    pool.Infinite = e.newValue;
            });

            var buttons = new VisualElement();
            buttons.style.flexDirection = FlexDirection.Row;
            buttons.style.marginTop = 6f;
            buttons.Add(new Button(ResetAll) { text = "Сбросить всё" });
            buttons.Add(new Button(() => { if (TryGetStamina(out var pool)) pool.Refill(); }) { text = "Пополнить стамину" });
            _panel.Add(buttons);

            _status = new Label();
            _status.style.color = DevToolsStyle.Muted;
            _status.style.marginTop = 6f;
            _status.style.whiteSpace = WhiteSpace.Normal;
            _panel.Add(_status);

            DevToolsStyle.DarkControls(_panel);
            _panel.style.display = DisplayStyle.None;
        }

        private static readonly Perf.Probe UpdateProbe = Perf.Register("UI.Modifiers");

        private void Update()
        {
            using var perf = Perf.Measure(UpdateProbe);
            if (_toggle != null && _toggle.WasPressedThisFrame())
                SetOpen(!_open);
            if (_open)
                RefreshStatus();
        }

        public void SetOpen(bool open)
        {
            if (_open == open)
                return;
            _open = open;
            _panel.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            if (open)
            {
                _input.Push(InputContext.Menu);
                PullFromSource();
            }
            else
            {
                _input.Pop(InputContext.Menu);
                _panel.focusController?.focusedElement?.Blur();
            }
        }

        private void PullFromSource()
        {
            if (!TryGetSource(out var source))
                return;
            _load.SetValueWithoutNotify(source.LoadFraction);
            _leftLeg.SetValueWithoutNotify(source.LeftLegBroken);
            _rightLeg.SetValueWithoutNotify(source.RightLegBroken);
            _arms.SetValueWithoutNotify(source.InjuredArms);
            _pain.SetValueWithoutNotify(source.Pain);
            _infiniteStamina.SetValueWithoutNotify(TryGetStamina(out var pool) && pool.Infinite);
        }

        private void ResetAll()
        {
            if (TryGetSource(out var source))
                source.ResetAll();
            if (TryGetStamina(out var pool))
            {
                pool.Infinite = false;
                pool.Refill();
            }
            PullFromSource();
        }

        private void RefreshStatus()
        {
            var motor = PlayerMotor.Local;
            if (motor == null)
            {
                _status.text = "Нет локального игрока.";
                return;
            }
            var m = motor.Modifiers;
            string turn = float.IsInfinity(m.MaxTurnRateDeg) ? "∞" : $"{m.MaxTurnRateDeg:0}°/с";
            _status.text = $"Скорость ×{m.SpeedMul:0.00}, разгон ×{m.AccelMul:0.00}, поворот {turn}\n" +
                           $"Спринт {(m.CanSprint ? "да" : "нет")}, прыжок {(m.CanJump ? "да" : "нет")}, встать {(m.CanStand ? "да" : "нет")}\n" +
                           $"Хромота {m.LimpAmount:0.0}, раскачка {m.SwayAmount:0.0}";
        }

        private static void Apply(System.Action<DebugStatusModifierSource> change)
        {
            if (TryGetSource(out var source))
                change(source);
        }

        private static bool TryGetSource(out DebugStatusModifierSource source)
        {
            source = PlayerMotor.Local != null ? PlayerMotor.Local.GetComponent<DebugStatusModifierSource>() : null;
            return source != null;
        }

        private static bool TryGetStamina(out SimpleStaminaPool pool)
        {
            pool = PlayerMotor.Local != null ? PlayerMotor.Local.Stamina as SimpleStaminaPool : null;
            return pool != null;
        }
    }
}
