using System;
using System.Collections.Generic;

namespace VoidMall.Net
{
    public readonly struct PlayerId : IEquatable<PlayerId>
    {
        public readonly int Value;

        public PlayerId(int value) => Value = value;

        public static readonly PlayerId Host = new(0);

        public bool Equals(PlayerId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is PlayerId other && Equals(other);
        public override int GetHashCode() => Value;
        public override string ToString() => $"Player#{Value}";
    }

    /// <summary>Запрос к авторитету (хосту): «сломать блок», «переложить предмет»...</summary>
    public interface INetCommand { }

    /// <summary>Событие от авторитета всем клиентам: «блок сломан», «предмет переложен»...</summary>
    public interface INetEvent { }

    public delegate void CommandHandler<T>(PlayerId sender, in T command) where T : struct, INetCommand;
    public delegate void NetEventHandler<T>(in T evt) where T : struct, INetEvent;

    /// <summary>
    /// Сетевая сессия. Главное правило проекта: игровая логика не меняет общее состояние
    /// напрямую из ввода — только через SendToAuthority. Тогда кооп добавляется заменой реализации.
    /// </summary>
    public interface INetSession
    {
        bool IsAuthority { get; }
        PlayerId LocalPlayer { get; }
        int MaxPlayers { get; }

        void SendToAuthority<T>(in T command) where T : struct, INetCommand;
        void Broadcast<T>(in T evt) where T : struct, INetEvent;

        void RegisterCommandHandler<T>(CommandHandler<T> handler) where T : struct, INetCommand;
        void UnregisterCommandHandler<T>(CommandHandler<T> handler) where T : struct, INetCommand;
        void RegisterEventHandler<T>(NetEventHandler<T> handler) where T : struct, INetEvent;
        void UnregisterEventHandler<T>(NetEventHandler<T> handler) where T : struct, INetEvent;
    }

    /// <summary>Одиночная игра: мы сами себе хост, команды и события — прямые вызовы.</summary>
    public sealed class LocalSession : INetSession
    {
        private readonly Dictionary<Type, Delegate> _commandHandlers = new();
        private readonly Dictionary<Type, Delegate> _eventHandlers = new();

        public bool IsAuthority => true;
        public PlayerId LocalPlayer => PlayerId.Host;
        public int MaxPlayers => 1;

        public void SendToAuthority<T>(in T command) where T : struct, INetCommand
        {
            if (_commandHandlers.TryGetValue(typeof(T), out var handler))
                ((CommandHandler<T>)handler)(LocalPlayer, in command);
        }

        public void Broadcast<T>(in T evt) where T : struct, INetEvent
        {
            if (_eventHandlers.TryGetValue(typeof(T), out var handler))
                ((NetEventHandler<T>)handler)(in evt);
        }

        public void RegisterCommandHandler<T>(CommandHandler<T> handler) where T : struct, INetCommand =>
            _commandHandlers[typeof(T)] = Delegate.Combine(_commandHandlers.GetValueOrDefault(typeof(T)), handler);

        public void UnregisterCommandHandler<T>(CommandHandler<T> handler) where T : struct, INetCommand =>
            Remove(_commandHandlers, typeof(T), handler);

        public void RegisterEventHandler<T>(NetEventHandler<T> handler) where T : struct, INetEvent =>
            _eventHandlers[typeof(T)] = Delegate.Combine(_eventHandlers.GetValueOrDefault(typeof(T)), handler);

        public void UnregisterEventHandler<T>(NetEventHandler<T> handler) where T : struct, INetEvent =>
            Remove(_eventHandlers, typeof(T), handler);

        private static void Remove(Dictionary<Type, Delegate> map, Type type, Delegate handler)
        {
            if (!map.TryGetValue(type, out var current))
                return;
            var result = Delegate.Remove(current, handler);
            if (result == null)
                map.Remove(type);
            else
                map[type] = result;
        }
    }
}
