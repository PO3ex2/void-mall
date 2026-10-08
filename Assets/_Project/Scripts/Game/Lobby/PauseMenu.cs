using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using VoidMall.Core;
using VoidMall.Input;

namespace VoidMall.Game
{
    /// <summary>
    /// Меню по Esc в мире (этап 10): «Продолжить / Настройки / Сохранить и в лобби / Сохранить и выйти». Игра не
    /// ставится на паузу (как в кооперативе). Настройки — те же, что в приложении ПК.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class PauseMenu : MonoBehaviour
    {
        private InputService _input;
        private InputAction _pause;
        private VisualElement _root, _panel, _main, _settings;
        private bool _open;

        /// <summary>В конце прошлого кадра была обычная игра: Esc, закрывший инвентарь или консоль, меню не откроет.</summary>
        private bool _gameplayLastFrame;

        public static PauseMenu Instance { get; private set; }
        public bool IsOpen => _open;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Instance = null;

        public void Initialize(InputService input)
        {
            Instance = this;
            _input = input;
            _pause = input.Action(InputService.Maps.Global, "Pause");
            _root = GetComponent<UIDocument>().rootVisualElement;
            _root.style.position = Position.Absolute;
            _root.style.left = _root.style.right = _root.style.top = _root.style.bottom = 0f;
            _root.style.backgroundColor = new Color(0f, 0f, 0f, 0.55f);
            _root.style.alignItems = Align.Center;
            _root.style.justifyContent = Justify.Center;
            _root.style.unityFontDefinition = FontDefinition.FromFont(PcOs.MonoFont);
            _root.style.color = PcOs.TextColor;

            _panel = new VisualElement();
            _panel.style.backgroundColor = PcOs.PanelColor;
            _panel.style.paddingLeft = _panel.style.paddingRight = 18f;
            _panel.style.paddingTop = _panel.style.paddingBottom = 14f;
            PcOs.Border(_panel, PcOs.Accent * 0.55f, 1f, 6f);
            _root.Add(_panel);

            _main = new VisualElement();
            _main.style.width = 320f;
            var title = new Label("Пауза");
            title.style.fontSize = 20f;
            title.style.color = PcOs.Accent;
            title.style.marginBottom = 10f;
            _main.Add(title);
            AddButton("Продолжить", Close, accent: true);
            AddButton("Настройки", ShowSettings);
            AddButton("Сохранить и в лобби", () =>
            {
                Close();
                SaveSystem.Instance?.ReturnToLobby();
            });
            AddButton("Сохранить и выйти", PcApps.Quit);
            var hint = PcApps.Muted("Игра не на паузе: мир живёт, пока открыто меню.");
            hint.style.marginTop = 8f;
            hint.style.whiteSpace = WhiteSpace.Normal;
            _main.Add(hint);
            _panel.Add(_main);

            _root.style.display = DisplayStyle.None;
        }

        private void AddButton(string text, System.Action action, bool accent = false)
        {
            var button = PcApps.Button(text, action, accent);
            button.style.height = 34f;
            button.style.marginBottom = 6f;
            _main.Add(button);
        }

        private void ShowSettings()
        {
            _main.style.display = DisplayStyle.None;
            _settings = new VisualElement();
            _settings.style.width = 640f;
            _settings.style.height = 520f;
            var back = PcApps.Button("← Назад", HideSettings);
            back.style.alignSelf = Align.FlexStart;
            back.style.marginBottom = 8f;
            _settings.Add(back);
            _settings.Add(PcSettingsApp.Build());
            _panel.Add(_settings);
        }

        private void HideSettings()
        {
            _settings?.RemoveFromHierarchy();
            _settings = null;
            _main.style.display = DisplayStyle.Flex;
        }

        private void Update()
        {
            if (_pause == null || !_pause.WasPressedThisFrame())
                return;
            if (_open)
            {
                if (_settings != null)
                    HideSettings();
                else
                    Close();
                return;
            }
            // Только в мире и только из обычной игры (инвентарь, консоль, ПК закрываются своим Esc).
            if (!_gameplayLastFrame || _input.Current != InputContext.Gameplay || GameFlow.Instance == null || GameFlow.Instance.State != FlowState.InWorld)
                return;
            Open();
        }

        private void LateUpdate() => _gameplayLastFrame = _input != null && _input.Current == InputContext.Gameplay;

        private void Open()
        {
            _open = true;
            _root.style.display = DisplayStyle.Flex;
            _input.Push(InputContext.Menu);
        }

        private void Close()
        {
            if (!_open)
                return;
            HideSettings();
            _open = false;
            _root.style.display = DisplayStyle.None;
            _input.Pop(InputContext.Menu);
        }
    }
}
