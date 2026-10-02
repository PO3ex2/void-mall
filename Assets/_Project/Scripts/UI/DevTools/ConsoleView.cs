using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;
using VoidMall.Console;

namespace VoidMall.UI
{
    /// <summary>
    /// Визуальный элемент консоли: журнал, строка ввода, история (↑/↓), автодополнение
    /// (Tab / Shift+Tab перебирают варианты по очереди, как в Source).
    /// Один и тот же элемент используется в оверлее (~) и позже в приложении cmd на ПК в лобби.
    /// </summary>
    public sealed class ConsoleView : VisualElement
    {
        private const int MaxSuggestions = 8;
        private static readonly string HighlightColor = ColorUtility.ToHtmlStringRGB(DevToolsStyle.Input);

        private readonly ScrollView _log;
        private readonly TextField _input;
        private readonly Label _suggestions;
        private int _historyIndex = -1;
        private bool _subscribed;

        /// <summary>Варианты текущего перебора по Tab; null — перебор не идёт.</summary>
        private List<string> _cycleMatches;
        private int _cycleIndex;

        /// <summary>Нажат Escape в строке ввода.</summary>
        public event System.Action CloseRequested;

        public ConsoleView()
        {
            style.flexGrow = 1f;
            style.flexDirection = FlexDirection.Column;

            _log = new ScrollView(ScrollViewMode.Vertical);
            _log.style.flexGrow = 1f;
            _log.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            Add(_log);

            _suggestions = new Label();
            _suggestions.style.color = DevToolsStyle.Muted;
            _suggestions.style.fontSize = 12f;
            _suggestions.style.minHeight = 16f;
            _suggestions.enableRichText = true;
            Add(_suggestions);

            _input = new TextField();
            _input.style.marginLeft = _input.style.marginRight = 0f;
            DevToolsStyle.DarkTextInput(_input);
            _input.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            _input.RegisterCallback<NavigationMoveEvent>(OnNavigationMove, TrickleDown.TrickleDown);
            _input.RegisterValueChangedCallback(OnValueChanged);
            Add(_input);

            RegisterCallback<AttachToPanelEvent>(_ => Subscribe());
            RegisterCallback<DetachFromPanelEvent>(_ => Unsubscribe());
        }

        public void FocusInput()
        {
            _input.schedule.Execute(() =>
            {
                _input.Focus();
                _input.SelectRange(_input.value.Length, _input.value.Length);
            });
            ScrollToBottom();
        }

        private void Subscribe()
        {
            if (_subscribed)
                return;
            _subscribed = true;
            _log.Clear();
            foreach (var line in DevConsole.Lines)
                AddLine(line);
            DevConsole.LineAdded += OnLineAdded;
            DevConsole.Cleared += OnCleared;
            ScrollToBottom();
        }

        private void Unsubscribe()
        {
            if (!_subscribed)
                return;
            _subscribed = false;
            DevConsole.LineAdded -= OnLineAdded;
            DevConsole.Cleared -= OnCleared;
        }

        private void OnLineAdded(ConsoleLine line)
        {
            AddLine(line);
            ScrollToBottom();
        }

        private void OnCleared() => _log.Clear();

        private void AddLine(ConsoleLine line)
        {
            var label = new Label(line.Text) { enableRichText = false };
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.fontSize = 13f;
            label.style.marginTop = label.style.marginBottom = 0f;
            label.style.paddingTop = label.style.paddingBottom = 1f;
            if (line.Type == ConsoleLineType.Input)
                label.style.marginTop = 4f;
            label.style.color = line.Type switch
            {
                ConsoleLineType.Input => DevToolsStyle.Input,
                ConsoleLineType.Warning => DevToolsStyle.Warning,
                ConsoleLineType.Error => DevToolsStyle.Error,
                _ => DevToolsStyle.Text,
            };
            _log.Add(label);
            while (_log.contentContainer.childCount > DevConsole.MaxLines)
                _log.contentContainer.RemoveAt(0);
        }

        private void ScrollToBottom()
        {
            _log.schedule.Execute(() => _log.verticalScroller.value = _log.verticalScroller.highValue).ExecuteLater(10);
        }

        private void OnKeyDown(KeyDownEvent evt)
        {
            switch (evt.keyCode)
            {
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    Submit();
                    break;
                case KeyCode.UpArrow:
                    StopCycle();
                    BrowseHistory(-1);
                    break;
                case KeyCode.DownArrow:
                    StopCycle();
                    BrowseHistory(+1);
                    break;
                case KeyCode.Tab:
                    AutoComplete(backwards: evt.shiftKey);
                    break;
                case KeyCode.Escape:
                    CloseRequested?.Invoke();
                    break;
                default:
                    // Символьные события без keyCode: не даём вставить таб, перевод строки и символ клавиши консоли.
                    if (evt.keyCode == KeyCode.None && IsBlockedChar(evt.character))
                        break;
                    return;
            }
            evt.StopImmediatePropagation();
            _input.focusController?.IgnoreEvent(evt);
        }

        private void OnNavigationMove(NavigationMoveEvent evt)
        {
            // Tab в UI Toolkit — переход фокуса. В консоли Tab — автодополнение.
            evt.StopImmediatePropagation();
            _input.focusController?.IgnoreEvent(evt);
        }

        private void OnValueChanged(ChangeEvent<string> evt)
        {
            StopCycle();
            string value = evt.newValue ?? string.Empty;
            string filtered = Filter(value);
            if (filtered != value)
            {
                _input.SetValueWithoutNotify(filtered);
                value = filtered;
            }
            UpdateSuggestions(value);
        }

        private void Submit()
        {
            string line = _input.value;
            StopCycle();
            _input.SetValueWithoutNotify(string.Empty);
            _historyIndex = -1;
            _suggestions.text = string.Empty;
            DevConsole.Submit(line);
            FocusInput();
        }

        private void BrowseHistory(int direction)
        {
            var history = DevConsole.History;
            if (history.Count == 0)
                return;
            if (_historyIndex < 0)
                _historyIndex = direction < 0 ? history.Count - 1 : -1;
            else
                _historyIndex += direction;

            if (_historyIndex < 0 || _historyIndex >= history.Count)
            {
                _historyIndex = -1;
                SetInput(string.Empty);
                return;
            }
            SetInput(history[_historyIndex]);
        }

        /// <summary>
        /// Tab — следующий вариант, Shift+Tab — предыдущий (как в Source). Единственный вариант дописывается сразу с пробелом.
        /// Перебор сбрасывается, как только пользователь меняет текст.
        /// </summary>
        private void AutoComplete(bool backwards)
        {
            if (_cycleMatches == null)
            {
                string value = _input.value.TrimStart();
                if (value.Contains(" "))
                    return;
                List<string> matches = DevConsole.GetCompletions(value);
                if (matches.Count == 0)
                    return;
                if (matches.Count == 1)
                {
                    SetInput(matches[0] + " ");
                    return;
                }
                _cycleMatches = matches;
                _cycleIndex = backwards ? matches.Count - 1 : 0;
            }
            else
            {
                int count = _cycleMatches.Count;
                _cycleIndex = (_cycleIndex + (backwards ? -1 : 1) + count) % count;
            }

            SetInput(_cycleMatches[_cycleIndex]);
            ShowCycle();
        }

        private void ShowCycle()
        {
            int count = _cycleMatches.Count;
            int start = Mathf.Clamp(_cycleIndex - MaxSuggestions / 2, 0, Mathf.Max(0, count - MaxSuggestions));
            int end = Mathf.Min(count, start + MaxSuggestions);
            var sb = new StringBuilder();
            if (start > 0)
                sb.Append("…   ");
            for (int i = start; i < end; i++)
            {
                if (i > start)
                    sb.Append("   ");
                string name = _cycleMatches[i];
                sb.Append(i == _cycleIndex ? $"<b><color=#{HighlightColor}>{name}</color></b>" : name);
            }
            if (end < count)
                sb.Append("   …");
            sb.Append($"   ({_cycleIndex + 1}/{count})");
            _suggestions.text = sb.ToString();
        }

        private void StopCycle() => _cycleMatches = null;

        private void UpdateSuggestions(string value)
        {
            value = value.TrimStart();
            if (value.Length == 0 || value.Contains(" "))
            {
                _suggestions.text = string.Empty;
                return;
            }
            var matches = DevConsole.GetCompletions(value);
            if (matches.Count == 0)
            {
                _suggestions.text = string.Empty;
                return;
            }
            string text = string.Join("   ", matches.GetRange(0, Mathf.Min(matches.Count, MaxSuggestions)));
            if (matches.Count > MaxSuggestions)
                text += $"   … +{matches.Count - MaxSuggestions}";
            _suggestions.text = text;
        }

        private void SetInput(string value)
        {
            _input.SetValueWithoutNotify(value);
            _input.schedule.Execute(() => _input.SelectRange(value.Length, value.Length));
            UpdateSuggestions(value);
        }

        public void ClearInput()
        {
            StopCycle();
            _input.SetValueWithoutNotify(string.Empty);
            _suggestions.text = string.Empty;
            _historyIndex = -1;
        }

        private static bool IsBlockedChar(char c) => c is '\t' or '\n' or '\r' || IsToggleChar(c);

        private static bool IsToggleChar(char c) => c is '`' or '~' or 'ё' or 'Ё';

        private static string Filter(string value)
        {
            for (int i = 0; i < value.Length; i++)
            {
                if (IsToggleChar(value[i]))
                    return value.Replace("`", "").Replace("~", "").Replace("ё", "").Replace("Ё", "");
            }
            return value;
        }
    }
}
