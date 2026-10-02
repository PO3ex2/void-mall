using System;
using UnityEngine;

namespace VoidMall.Player
{
    [Flags]
    public enum PlayerButtons : ushort
    {
        None = 0,
        Jump = 1 << 0,
        Crouch = 1 << 1,
        Sprint = 1 << 2,
        Walk = 1 << 3,
        Interact = 1 << 4,
        Primary = 1 << 5,
        Secondary = 1 << 6,
    }

    /// <summary>
    /// Ввод игрока за один тик симуляции. Компактная структура: её же позже будем слать по сети
    /// и хранить в буфере для предсказания/реконсиляции.
    /// </summary>
    public struct PlayerInputFrame
    {
        /// <summary>x — вправо, y — вперёд. Длина ≤ 1 (стик даёт аналоговую скорость).</summary>
        public Vector2 Move;

        public float Yaw;
        public float Pitch;

        /// <summary>Кнопки, зажатые в момент тика.</summary>
        public PlayerButtons Held;

        /// <summary>Кнопки, нажатые с прошлого тика (фронт нажатия).</summary>
        public PlayerButtons Pressed;

        public bool IsHeld(PlayerButtons button) => (Held & button) != 0;
        public bool WasPressed(PlayerButtons button) => (Pressed & button) != 0;
    }

    /// <summary>Источник ввода для мотора: локальный игрок, тест, бот или сеть.</summary>
    public interface IPlayerInputSource
    {
        /// <summary>Вызывается мотором ровно один раз за тик.</summary>
        PlayerInputFrame ConsumeFrame();
    }
}
