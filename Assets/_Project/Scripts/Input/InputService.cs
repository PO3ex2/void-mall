using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace VoidMall.Input
{
    /// <summary>
    /// Кто сейчас забирает ввод. Пустой стек = Gameplay.
    /// </summary>
    public enum InputContext
    {
        Gameplay,
        Menu,
        Console,
        Terminal,
    }

    /// <summary>
    /// Владеет ассетом действий (project-wide actions) и стеком контекстов ввода.
    /// Карты: Player (только в Gameplay), Terminal (только в режиме ПК), Global и UI — всегда.
    /// </summary>
    public sealed class InputService : IDisposable
    {
        public static class Maps
        {
            public const string Player = "Player";
            public const string UI = "UI";
            public const string Global = "Global";
            public const string Terminal = "Terminal";
        }

        private readonly List<InputContext> _stack = new();

        public InputActionAsset Asset { get; }
        public InputActionMap PlayerMap { get; }
        public InputActionMap GlobalMap { get; }
        public InputActionMap TerminalMap { get; }

        public InputContext Current => _stack.Count > 0 ? _stack[^1] : InputContext.Gameplay;

        public event Action<InputContext> ContextChanged;

        public InputService(InputActionAsset asset)
        {
            Asset = asset ? asset : throw new ArgumentNullException(nameof(asset));
            PlayerMap = asset.FindActionMap(Maps.Player, throwIfNotFound: true);
            GlobalMap = asset.FindActionMap(Maps.Global, throwIfNotFound: true);
            TerminalMap = asset.FindActionMap(Maps.Terminal, throwIfNotFound: true);

            GlobalMap.Enable();
            asset.FindActionMap(Maps.UI, throwIfNotFound: true).Enable();
            Apply();
        }

        public InputAction Action(string map, string action) => Asset.FindActionMap(map, true).FindAction(action, true);

        public void Push(InputContext context)
        {
            _stack.Add(context);
            Apply();
        }

        public void Pop(InputContext context)
        {
            int index = _stack.LastIndexOf(context);
            if (index < 0)
                return;
            _stack.RemoveAt(index);
            Apply();
        }

        public bool IsActive(InputContext context) => _stack.Contains(context);

        private void Apply()
        {
            var current = Current;
            SetEnabled(PlayerMap, current == InputContext.Gameplay);
            SetEnabled(TerminalMap, current == InputContext.Terminal);

            bool gameplay = current == InputContext.Gameplay;
            Cursor.lockState = gameplay ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !gameplay;

            ContextChanged?.Invoke(current);
        }

        private static void SetEnabled(InputActionMap map, bool enabled)
        {
            if (enabled)
                map.Enable();
            else
                map.Disable();
        }

        public void Dispose()
        {
            PlayerMap.Disable();
            TerminalMap.Disable();
            GlobalMap.Disable();
            _stack.Clear();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }
}
