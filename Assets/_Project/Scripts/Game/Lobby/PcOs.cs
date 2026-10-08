using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using VoidMall.World;

namespace VoidMall.Game
{
    /// <summary>
    /// ОС ПК HEXARYTE (этап 10) на UI Toolkit, нарисованная в текстуру экрана ПК. Первое включение — щелчок, ASCII-логотип
    /// построчно (из HEXALOGO), аккорд, строки самопроверки; затем рабочий стол: иконки приложений, окна (перетаскиваются
    /// за заголовок), панель с «Пуском» и часами. Шрифт — моноширинный системный (Consolas).
    /// </summary>
    public sealed class PcOs : MonoBehaviour
    {
        public static readonly Color Back = new(0.06f, 0.08f, 0.1f);
        public static readonly Color PanelColor = new(0.11f, 0.14f, 0.17f);
        public static readonly Color TitleColor = new(0.17f, 0.24f, 0.31f);
        public static readonly Color Accent = new(0.55f, 0.78f, 0.95f);
        public static readonly Color TextColor = new(0.84f, 0.9f, 0.93f);
        public static readonly Color Muted = new(0.52f, 0.6f, 0.66f);
        public static readonly Color Danger = new(0.95f, 0.45f, 0.4f);

        private const float LineInterval = 0.035f;

        private PcTerminal _terminal;
        private UIDocument _document;
        private PanelSettings _panel;
        private VisualElement _root, _boot, _desktop, _windows, _taskButtons, _startMenu;
        private Label _bootText, _clock;
        private readonly List<PcWindow> _open = new();
        private readonly List<string> _bootLines = new();
        private int _shownLines;
        private float _bootTimer;
        private bool _built, _wakeRequested, _booting, _booted, _chimed;

        public PcTerminal Terminal => _terminal;

        private static Font _font;

        public static Font MonoFont => _font ??= Font.CreateDynamicFontFromOSFont(new[] { "Consolas", "Lucida Console", "Courier New" }, 14);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _font = null;

        public void Setup(PcTerminal terminal)
        {
            _terminal = terminal;
            var source = GameBootstrap.Config != null ? GameBootstrap.Config.PanelSettings : null;
            _panel = source != null ? Instantiate(source) : ScriptableObject.CreateInstance<PanelSettings>();
            _panel.name = "PC Panel";
            _panel.targetTexture = terminal.Texture;
            _panel.scaleMode = PanelScaleMode.ConstantPixelSize;
            _panel.scale = 1f;
            _panel.clearColor = true;
            _panel.colorClearValue = Back;
            _panel.SetScreenToPanelSpaceFunction(terminal.ScreenToPanel);
            _document = gameObject.AddComponent<UIDocument>();
            _document.panelSettings = _panel;
        }

        private void OnDestroy()
        {
            if (_panel != null)
                Destroy(_panel);
        }

        /// <summary>ПК включили (E): первый раз — загрузка, потом сразу рабочий стол.</summary>
        public void Wake() => _wakeRequested = true;

        private void Start() => Build();

        private void Build()
        {
            if (_built)
                return;
            _built = true;
            _root = _document.rootVisualElement;
            _root.style.flexGrow = 1f;
            _root.style.backgroundColor = Back;
            _root.style.color = TextColor;
            _root.style.unityFontDefinition = FontDefinition.FromFont(MonoFont);
            _root.style.fontSize = 14f;

            _boot = new VisualElement();
            _boot.style.flexGrow = 1f;
            _boot.style.paddingLeft = 24f;
            _boot.style.paddingTop = 18f;
            _bootText = new Label();
            _bootText.style.color = new Color(0.75f, 0.85f, 0.8f);
            _bootText.style.fontSize = 11f;
            _bootText.style.whiteSpace = WhiteSpace.Pre;
            _boot.Add(_bootText);
            _root.Add(_boot);

            BuildDesktop();
            _desktop.style.display = DisplayStyle.None;
        }

        private void Update()
        {
            if (!_built)
                return;
            if (_wakeRequested)
            {
                _wakeRequested = false;
                if (!_booted && !_booting)
                    BeginBoot();
            }
            if (_booting)
                TickBoot();
            if (_clock != null && _booted)
                _clock.text = ClockText();
            foreach (var window in _open)
                window.Tick();
        }

        // ───────────────────────────── Загрузка ─────────────────────────────

        private void BeginBoot()
        {
            _booting = true;
            _bootTimer = 0f;
            _shownLines = 0;
            _bootLines.Clear();
            _bootLines.AddRange(AsciiArt.FromTexture(_terminal.Logo, 60));
            _bootLines.Add("");
            _bootLines.Add("               H E X A R Y T E   O S   3.1");
            _bootLines.Add("            (c) Hexaryte Systems, 1997. Все права защищены.");
            _bootLines.Add("");
            _bootLines.Add("Проверка памяти ........ 65536K OK");
            _bootLines.Add("Терминал ............... " + (_terminal.InWorld ? "СЕТЬ МОЛА" : "ЛОББИ, ПОСТ ОХРАНЫ"));
            _bootLines.Add("Загрузка рабочего стола ...");
            _terminal.Play(PcSounds.Click);
            _chimed = false;
        }

        private void TickBoot()
        {
            _bootTimer += Time.deltaTime;
            if (!_chimed && _bootTimer > 0.35f)
            {
                _chimed = true;
                _terminal.Play(PcSounds.Chime, 0.8f);
            }
            int target = Mathf.Min(_bootLines.Count, Mathf.FloorToInt(Mathf.Max(0f, _bootTimer - 0.3f) / LineInterval));
            if (target != _shownLines)
            {
                _shownLines = target;
                _bootText.text = string.Join("\n", _bootLines.GetRange(0, _shownLines));
            }
            if (_bootTimer > 0.3f + _bootLines.Count * LineInterval + 0.9f)
            {
                _booting = false;
                _booted = true;
                _boot.style.display = DisplayStyle.None;
                _desktop.style.display = DisplayStyle.Flex;
            }
        }

        // ───────────────────────────── Рабочий стол ─────────────────────────────

        private void BuildDesktop()
        {
            _desktop = new VisualElement();
            _desktop.style.flexGrow = 1f;
            _desktop.style.backgroundColor = new Color(0.09f, 0.13f, 0.17f);
            _root.Add(_desktop);

            if (_terminal.Logo != null)
            {
                var watermark = new VisualElement { pickingMode = PickingMode.Ignore };
                watermark.style.position = Position.Absolute;
                watermark.style.width = 300f;
                watermark.style.height = 260f;
                watermark.style.left = (PcTerminal.ScreenWidth - 300f) * 0.5f;
                watermark.style.top = (PcTerminal.ScreenHeight - 34f - 260f) * 0.5f;
                watermark.style.backgroundImage = Background.FromTexture2D(_terminal.Logo);
                watermark.style.opacity = 0.07f;
                _desktop.Add(watermark);
            }

            var icons = new VisualElement();
            icons.style.position = Position.Absolute;
            icons.style.left = 16f;
            icons.style.top = 16f;
            foreach (var app in PcApps.AppsFor(_terminal.InWorld))
                icons.Add(Icon(app.glyph, app.title, () => OpenApp(app.id)));
            _desktop.Add(icons);

            _windows = new VisualElement { pickingMode = PickingMode.Ignore };
            _windows.style.position = Position.Absolute;
            _windows.style.left = _windows.style.top = _windows.style.right = 0f;
            _windows.style.bottom = 34f;
            _desktop.Add(_windows);

            BuildTaskbar();
        }

        private VisualElement Icon(string glyph, string caption, Action open)
        {
            var icon = new VisualElement();
            icon.style.width = 96f;
            icon.style.height = 84f;
            icon.style.alignItems = Align.Center;
            icon.style.marginBottom = 8f;
            var box = new Label(glyph);
            box.style.width = 46f;
            box.style.height = 40f;
            box.style.unityTextAlign = TextAnchor.MiddleCenter;
            box.style.backgroundColor = PanelColor;
            box.style.color = Accent;
            box.style.fontSize = 16f;
            Border(box, Accent * 0.6f, 1f, 3f);
            var label = new Label(caption);
            label.style.fontSize = 12f;
            label.style.marginTop = 4f;
            label.style.unityTextAlign = TextAnchor.UpperCenter;
            label.style.whiteSpace = WhiteSpace.Normal;
            icon.Add(box);
            icon.Add(label);
            icon.RegisterCallback<PointerEnterEvent>(_ => box.style.backgroundColor = TitleColor);
            icon.RegisterCallback<PointerLeaveEvent>(_ => box.style.backgroundColor = PanelColor);
            icon.RegisterCallback<ClickEvent>(_ => open());
            return icon;
        }

        private void BuildTaskbar()
        {
            var bar = new VisualElement();
            bar.style.position = Position.Absolute;
            bar.style.left = bar.style.right = bar.style.bottom = 0f;
            bar.style.height = 34f;
            bar.style.flexDirection = FlexDirection.Row;
            bar.style.alignItems = Align.Center;
            bar.style.backgroundColor = PanelColor;
            bar.style.borderTopWidth = 1f;
            bar.style.borderTopColor = Accent * 0.4f;

            var start = new VisualElement();
            start.style.flexDirection = FlexDirection.Row;
            start.style.alignItems = Align.Center;
            start.style.height = 28f;
            start.style.paddingLeft = start.style.paddingRight = 8f;
            start.style.marginLeft = 4f;
            start.style.backgroundColor = TitleColor;
            Border(start, Accent * 0.5f, 1f, 3f);
            if (_terminal.Logo != null)
            {
                var logo = new VisualElement();
                logo.style.width = 20f;
                logo.style.height = 17f;
                logo.style.backgroundImage = Background.FromTexture2D(_terminal.Logo);
                logo.style.marginRight = 6f;
                start.Add(logo);
            }
            start.Add(new Label("Пуск"));
            start.RegisterCallback<ClickEvent>(_ => ToggleStartMenu());
            bar.Add(start);

            _taskButtons = new VisualElement();
            _taskButtons.style.flexDirection = FlexDirection.Row;
            _taskButtons.style.flexGrow = 1f;
            _taskButtons.style.marginLeft = 8f;
            bar.Add(_taskButtons);

            _clock = new Label();
            _clock.style.marginRight = 12f;
            _clock.style.color = Muted;
            bar.Add(_clock);
            _desktop.Add(bar);

            _startMenu = new VisualElement();
            _startMenu.style.position = Position.Absolute;
            _startMenu.style.left = 4f;
            _startMenu.style.bottom = 38f;
            _startMenu.style.width = 260f;
            _startMenu.style.backgroundColor = PanelColor;
            _startMenu.style.paddingTop = _startMenu.style.paddingBottom = 6f;
            Border(_startMenu, Accent * 0.5f, 1f, 3f);
            _startMenu.style.display = DisplayStyle.None;
            var header = new Label("HEXARYTE OS 3.1");
            header.style.color = Muted;
            header.style.fontSize = 12f;
            header.style.marginLeft = 10f;
            header.style.marginBottom = 4f;
            _startMenu.Add(header);
            foreach (var (title, action) in PcApps.StartMenu(this))
                _startMenu.Add(MenuItem(title, action));
            _desktop.Add(_startMenu);
        }

        private VisualElement MenuItem(string title, Action action)
        {
            var item = new Label(title);
            item.style.paddingLeft = 12f;
            item.style.paddingTop = item.style.paddingBottom = 6f;
            item.RegisterCallback<PointerEnterEvent>(_ => item.style.backgroundColor = TitleColor);
            item.RegisterCallback<PointerLeaveEvent>(_ => item.style.backgroundColor = Color.clear);
            item.RegisterCallback<ClickEvent>(_ =>
            {
                _startMenu.style.display = DisplayStyle.None;
                action();
            });
            return item;
        }

        private void ToggleStartMenu()
        {
            bool show = _startMenu.style.display == DisplayStyle.None;
            _startMenu.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (show)
                _startMenu.BringToFront();
        }

        private static string ClockText()
        {
            var clock = WorldClock.Instance;
            if (clock != null && GameFlow.Instance != null && GameFlow.Instance.State != FlowState.Lobby)
            {
                float hour = clock.Hour;
                return $"День {clock.Day}  {(int)hour:00}:{(int)(hour % 1f * 60f):00}";
            }
            return DateTime.Now.ToString("HH:mm");
        }

        /// <summary>Esc на ПК: сначала закрывает «Пуск», иначе — выход из ПК (false).</summary>
        public bool HandleEscape()
        {
            if (_startMenu != null && _startMenu.style.display == DisplayStyle.Flex)
            {
                _startMenu.style.display = DisplayStyle.None;
                return true;
            }
            return false;
        }

        /// <summary>Снять захват мыши, если он остался (вышли из ПК посреди перетаскивания окна).</summary>
        public void ReleasePointer()
        {
            var panel = _document != null ? _document.rootVisualElement?.panel : null;
            if (panel != null && PointerCaptureHelper.GetCapturingElement(panel, PointerId.mousePointerId) is VisualElement element)
                element.ReleasePointer(PointerId.mousePointerId);
        }

        // ───────────────────────────── Окна ─────────────────────────────

        /// <summary>Открыть приложение (или вывести его окно наверх).</summary>
        public void OpenApp(string id)
        {
            if (!_booted)
                return;
            foreach (var window in _open)
            {
                if (window.AppId != id)
                    continue;
                window.BringToFront();
                return;
            }
            var app = PcApps.Create(id, this);
            if (app == null)
                return;
            var created = new PcWindow(this, id, app);
            created.style.left = 140f + _open.Count * 26f;
            created.style.top = 30f + _open.Count * 22f;
            _windows.Add(created);
            _open.Add(created);
            RebuildTaskButtons();
        }

        public void Close(PcWindow window)
        {
            window.RemoveFromHierarchy();
            _open.Remove(window);
            window.App.Closed?.Invoke();
            RebuildTaskButtons();
        }

        public void CloseApp(string id)
        {
            foreach (var window in _open.ToArray())
                if (window.AppId == id)
                    Close(window);
        }

        private void RebuildTaskButtons()
        {
            _taskButtons.Clear();
            foreach (var window in _open)
            {
                var button = new Label(window.App.Title);
                button.style.paddingLeft = button.style.paddingRight = 10f;
                button.style.paddingTop = button.style.paddingBottom = 4f;
                button.style.marginRight = 4f;
                button.style.backgroundColor = TitleColor;
                button.style.fontSize = 12f;
                button.RegisterCallback<ClickEvent>(_ => window.BringToFront());
                _taskButtons.Add(button);
            }
        }

        public static void Border(VisualElement element, Color color, float width, float radius)
        {
            element.style.borderTopColor = element.style.borderBottomColor = element.style.borderLeftColor = element.style.borderRightColor = color;
            element.style.borderTopWidth = element.style.borderBottomWidth = element.style.borderLeftWidth = element.style.borderRightWidth = width;
            element.style.borderTopLeftRadius = element.style.borderTopRightRadius = element.style.borderBottomLeftRadius = element.style.borderBottomRightRadius = radius;
        }
    }

    /// <summary>Приложение ОС: заголовок, размер окна, содержимое; Tick — каждый кадр, пока окно открыто.</summary>
    public sealed class PcApp
    {
        public string Title;
        public float Width = 520f, Height = 420f;
        public VisualElement Content;
        public Action Tick;
        public Action Closed;
    }

    /// <summary>Окно ОС: заголовок (перетаскивание), крестик, содержимое приложения.</summary>
    public sealed class PcWindow : VisualElement
    {
        public readonly string AppId;
        public readonly PcApp App;
        private readonly PcOs _os;
        private VisualElement _title;
        private bool _dragging;
        private Vector2 _grab;

        public PcWindow(PcOs os, string appId, PcApp app)
        {
            _os = os;
            AppId = appId;
            App = app;
            style.position = Position.Absolute;
            style.width = app.Width;
            style.height = app.Height;
            style.backgroundColor = PcOs.PanelColor;
            PcOs.Border(this, PcOs.Accent * 0.55f, 1f, 4f);
            RegisterCallback<PointerDownEvent>(_ => BringToFront(), TrickleDown.TrickleDown);

            var title = new VisualElement();
            title.style.flexDirection = FlexDirection.Row;
            title.style.alignItems = Align.Center;
            title.style.height = 28f;
            title.style.backgroundColor = PcOs.TitleColor;
            title.style.paddingLeft = 10f;
            var caption = new Label(app.Title);
            caption.style.flexGrow = 1f;
            caption.style.color = PcOs.Accent;
            title.Add(caption);
            // Крестик — кнопка: она сама захватывает указатель и сама ловит отпускание. Нажатие на неё не начинает
            // перетаскивание окна (иначе захват уходил крестику, и клик терялся).
            var close = new Button(() => _os.Close(this)) { text = "×" };
            close.style.width = 30f;
            close.style.height = 28f;
            close.style.marginLeft = close.style.marginRight = close.style.marginTop = close.style.marginBottom = 0f;
            close.style.paddingLeft = close.style.paddingRight = close.style.paddingTop = close.style.paddingBottom = 0f;
            close.style.unityTextAlign = TextAnchor.MiddleCenter;
            close.style.fontSize = 18f;
            close.style.color = PcOs.TextColor;
            close.style.backgroundColor = Color.clear;
            PcOs.Border(close, Color.clear, 0f, 0f);
            close.RegisterCallback<PointerEnterEvent>(_ => close.style.backgroundColor = PcOs.Danger * 0.7f);
            close.RegisterCallback<PointerLeaveEvent>(_ => close.style.backgroundColor = Color.clear);
            close.RegisterCallback<PointerDownEvent>(evt => evt.StopPropagation());
            title.Add(close);
            Add(title);

            // Перетаскивание: захват — на самой полоске заголовка, и все её обработчики — на ней же. Захваченные
            // события приходят только захватившему элементу: захват дочерней подписи не отпускался никогда, и ПК
            // переставал реагировать на мышь.
            _title = title;
            title.RegisterCallback<PointerDownEvent>(OnDragStart);
            title.RegisterCallback<PointerMoveEvent>(OnDragMove);
            title.RegisterCallback<PointerUpEvent>(OnDragEnd);
            title.RegisterCallback<PointerCaptureOutEvent>(_ => _dragging = false);

            var body = app.Content ?? new VisualElement();
            body.style.flexGrow = 1f;
            body.style.paddingLeft = body.style.paddingRight = 12f;
            body.style.paddingTop = body.style.paddingBottom = 10f;
            Add(body);
        }

        public void Tick() => App.Tick?.Invoke();

        private void OnDragStart(PointerDownEvent evt)
        {
            if (evt.button != 0 || float.IsNaN(evt.position.x))
                return;
            _dragging = true;
            _grab = (Vector2)evt.position - new Vector2(resolvedStyle.left, resolvedStyle.top);
            _title.CapturePointer(evt.pointerId);
            evt.StopPropagation();
        }

        private void OnDragMove(PointerMoveEvent evt)
        {
            // Курсор ушёл за край экрана ПК — позиции нет (NaN): окно стоит, где было.
            if (!_dragging || float.IsNaN(evt.position.x) || float.IsNaN(evt.position.y))
                return;
            Vector2 position = (Vector2)evt.position - _grab;
            style.left = Mathf.Clamp(position.x, -App.Width + 80f, PcTerminal.ScreenWidth - 80f);
            style.top = Mathf.Clamp(position.y, 0f, PcTerminal.ScreenHeight - 70f);
        }

        private void OnDragEnd(PointerUpEvent evt)
        {
            _dragging = false;
            if (_title.HasPointerCapture(evt.pointerId))
                _title.ReleasePointer(evt.pointerId);
        }
    }
}
