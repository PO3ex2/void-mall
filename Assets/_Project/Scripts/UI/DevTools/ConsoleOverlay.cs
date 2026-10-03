using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using VoidMall.Input;
using VoidMall.Core;

namespace VoidMall.UI
{
    /// <summary>Консоль поверх игры по клавише ` (Ё).</summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class ConsoleOverlay : MonoBehaviour
    {
        private InputService _input;
        private InputAction _toggle;
        private VisualElement _panel;
        private ConsoleView _view;
        private bool _open;

        public bool IsOpen => _open;

        public void Initialize(InputService input)
        {
            _input = input;
            _toggle = input.Action(InputService.Maps.Global, "ToggleConsole");

            var root = GetComponent<UIDocument>().rootVisualElement;
            DevToolsStyle.FullScreenRoot(root);

            _panel = new VisualElement { name = "console-overlay" };
            DevToolsStyle.Panel(_panel);
            _panel.style.position = Position.Absolute;
            _panel.style.left = _panel.style.right = _panel.style.top = 0f;
            _panel.style.height = Length.Percent(45f);
            root.Add(_panel);

            _view = new ConsoleView();
            _view.CloseRequested += () => SetOpen(false);
            _panel.Add(_view);

            _panel.style.display = DisplayStyle.None;
        }

        private static readonly Perf.Probe UpdateProbe = Perf.Register("UI.Console");

        private void Update()
        {
            using var perf = Perf.Measure(UpdateProbe);
            if (_toggle != null && _toggle.WasPressedThisFrame())
                SetOpen(!_open);
        }

        public void SetOpen(bool open)
        {
            if (_open == open)
                return;
            _open = open;
            _panel.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            if (open)
            {
                _input.Push(InputContext.Console);
                _view.ClearInput();
                _view.FocusInput();
            }
            else
            {
                _input.Pop(InputContext.Console);
                _panel.focusController?.focusedElement?.Blur();
            }
        }
    }
}
