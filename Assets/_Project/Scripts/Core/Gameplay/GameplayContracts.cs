using UnityEngine;

namespace VoidMall.Core
{
    /// <summary>Пул стамины. Мотор только потребляет, реализация живёт в Survival (пока — заглушка в Player).</summary>
    public interface IStaminaPool
    {
        float Current { get; }
        float Max { get; }

        /// <summary>Истощение: стамина упала до нуля и ещё не восстановилась до порога.</summary>
        bool IsExhausted { get; }

        /// <summary>Списать, только если хватает. Для разовых действий (прыжок, удар).</summary>
        bool TryConsume(float amount);

        /// <summary>Списать сколько есть. Для непрерывных действий (спринт).</summary>
        void Drain(float amount);
    }

    /// <summary>Поверхность, которая переносит стоящих на ней: эскалатор, лифт, платформа.</summary>
    public interface IMovingSurface
    {
        Vector3 GetVelocityAt(Vector3 worldPoint);
    }

    /// <summary>Игрок приземлился. Survival по этому событию считает урон ногам.</summary>
    public struct PlayerLandedEvent
    {
        public GameObject Player;
        public float ImpactSpeed;
        public Vector3 Position;
    }

    public static class GameLayers
    {
        public const int Default = 0;
        public const int World = 6;
        public const int Props = 7;
        public const int Player = 8;
        public const int Enemy = 9;
        public const int Trigger = 10;
        public const int Debris = 11;

        /// <summary>С чем сталкивается капсула игрока.</summary>
        public const int PlayerCollisionMask = (1 << Default) | (1 << World) | (1 << Props) | (1 << Enemy);
    }
}
