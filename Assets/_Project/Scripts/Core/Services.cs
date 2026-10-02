using System;
using System.Collections.Generic;
using UnityEngine;

namespace VoidMall.Core
{
    /// <summary>
    /// Минимальный сервис-локатор. Сервисы регистрирует composition root (GameBootstrap).
    /// Сбрасывается при каждом входе в Play, т.к. перезагрузка домена в проекте отключена.
    /// </summary>
    public static class Services
    {
        private static readonly Dictionary<Type, object> _services = new();

        public static void Register<T>(T service) where T : class
        {
            _services[typeof(T)] = service ?? throw new ArgumentNullException(nameof(service));
        }

        public static void Unregister<T>(T service) where T : class
        {
            if (_services.TryGetValue(typeof(T), out var current) && ReferenceEquals(current, service))
                _services.Remove(typeof(T));
        }

        public static T Get<T>() where T : class
        {
            if (_services.TryGetValue(typeof(T), out var service))
                return (T)service;
            throw new InvalidOperationException($"Service {typeof(T).Name} is not registered.");
        }

        public static bool TryGet<T>(out T service) where T : class
        {
            if (_services.TryGetValue(typeof(T), out var s))
            {
                service = (T)s;
                return true;
            }
            service = null;
            return false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            foreach (var service in _services.Values)
                (service as IDisposable)?.Dispose();
            _services.Clear();
        }
    }
}
