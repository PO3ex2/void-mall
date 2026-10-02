using UnityEngine;
using UnityEngine.UIElements;

namespace VoidMall.UI
{
    /// <summary>Общие стили отладочных панелей (задаются из кода — это dev-инструменты, не игровой UI).</summary>
    internal static class DevToolsStyle
    {
        public static readonly Color Background = new(0.04f, 0.045f, 0.06f, 0.9f);
        public static readonly Color Border = new(1f, 1f, 1f, 0.12f);
        public static readonly Color Text = new(0.86f, 0.88f, 0.9f);
        public static readonly Color Muted = new(0.55f, 0.58f, 0.62f);
        public static readonly Color Input = new(0.55f, 0.8f, 1f);
        public static readonly Color Warning = new(1f, 0.8f, 0.35f);
        public static readonly Color Error = new(1f, 0.45f, 0.42f);

        public static void Panel(VisualElement element)
        {
            var s = element.style;
            s.backgroundColor = Background;
            s.borderBottomColor = s.borderTopColor = s.borderLeftColor = s.borderRightColor = Border;
            s.borderBottomWidth = s.borderTopWidth = s.borderLeftWidth = s.borderRightWidth = 1f;
            s.paddingLeft = s.paddingRight = 10f;
            s.paddingTop = s.paddingBottom = 8f;
            s.color = Text;
        }

        public static void DarkTextInput(TextField field)
        {
            var input = field.Q(className: TextField.inputUssClassName);
            if (input == null)
                return;
            var s = input.style;
            s.backgroundColor = new Color(0.1f, 0.11f, 0.14f, 1f);
            s.color = Text;
            s.borderBottomColor = s.borderTopColor = s.borderLeftColor = s.borderRightColor = new Color(1f, 1f, 1f, 0.2f);
            s.paddingLeft = 6f;
            s.minHeight = 26f;
            s.unityTextAlign = TextAnchor.MiddleLeft;
        }

        /// <summary>Затемняет стандартные контролы темы (кнопки, поля ввода) внутри панели.</summary>
        public static void DarkControls(VisualElement container)
        {
            container.Query<TextField>().ForEach(DarkTextInput);
            container.Query<Button>().ForEach(button =>
            {
                var s = button.style;
                s.backgroundColor = new Color(0.18f, 0.2f, 0.25f, 1f);
                s.color = Text;
                s.borderBottomColor = s.borderTopColor = s.borderLeftColor = s.borderRightColor = new Color(1f, 1f, 1f, 0.18f);
            });
        }

        public static void FullScreenRoot(VisualElement root)
        {
            root.pickingMode = PickingMode.Ignore;
            root.style.position = Position.Absolute;
            root.style.left = root.style.right = root.style.top = root.style.bottom = 0f;
        }
    }
}
