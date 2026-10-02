using System;
using System.Collections.Generic;
using UnityEngine;

namespace VoidMall.Core
{
    /// <summary>
    /// Типизированная шина событий без аллокаций (события — struct, передаются по in).
    /// </summary>
    public static class EventBus<T> where T : struct
    {
        public delegate void Handler(in T evt);

        private static Handler _handlers;
        private static bool _registeredForReset;

        public static void Subscribe(Handler handler)
        {
            if (!_registeredForReset)
            {
                _registeredForReset = true;
                EventBusRegistry.AddReset(Reset);
            }
            _handlers += handler;
        }

        public static void Unsubscribe(Handler handler) => _handlers -= handler;

        public static void Publish(in T evt) => _handlers?.Invoke(in evt);

        private static void Reset()
        {
            _handlers = null;
            _registeredForReset = false;
        }
    }

    internal static class EventBusRegistry
    {
        private static readonly List<Action> _resets = new();

        internal static void AddReset(Action reset) => _resets.Add(reset);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetAll()
        {
            foreach (var reset in _resets)
                reset();
            _resets.Clear();
        }
    }
}
