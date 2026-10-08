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

        /// <summary>Отладка: стамина не тратится.</summary>
        bool Infinite { get; set; }

        /// <summary>Восстановить до максимума.</summary>
        void Refill();
    }

    /// <summary>Поверхность, которая переносит стоящих на ней: эскалатор, лифт, платформа.</summary>
    public interface IMovingSurface
    {
        Vector3 GetVelocityAt(Vector3 worldPoint);
    }

    /// <summary>
    /// Готова ли коллизия мира в точке. Пока чанк под игроком не прогружен, мотор стоит на месте,
    /// а не проваливается сквозь мир (как в Minecraft). Регистрирует мир, если он есть в сцене.
    /// </summary>
    public interface IWorldCollisionQuery
    {
        bool IsCollisionReady(Vector3 position);

        /// <summary>
        /// Капсула замурована в твёрдом мире (центр внутри массива — физика её уже не вытолкнет)?
        /// Тогда возвращает ближайшую свободную позицию ступней: сначала вверх, потом по сторонам.
        /// </summary>
        bool TryGetEmbeddedEscape(Vector3 feet, float height, float radius, out Vector3 freeFeet);
    }

    /// <summary>Игровые часы мира (авторитетные). Сутки — 24 игровых часа.</summary>
    public interface IWorldClock
    {
        /// <summary>Время суток, часы 0…24.</summary>
        float Hour { get; }

        /// <summary>Номер суток с начала мира (с 1).</summary>
        int Day { get; }

        bool IsNight { get; }
    }

    /// <summary>
    /// Освещённость в точке (0 — тьма, 1 — ярко) для рассудка игрока и видимости для Манекена.
    /// Считается на CPU по лампам рядом, без чтения рендера.
    /// </summary>
    public interface ILightProbe
    {
        float SampleLight(Vector3 position);
    }

    /// <summary>Упавший обломок разбился. Survival (этап 7) по этому событию нанесёт урон тем, кто под ним.</summary>
    public struct DebrisImpactEvent
    {
        public Bounds Bounds;
        public float Mass;
        public float Speed;
    }

    /// <summary>Крафтовая станция (None — крафт в руках). Пропы-станции и рецепты ссылаются на неё.</summary>
    public enum CraftStation : byte
    {
        None,
        Workbench,
        Sewing,
        Chemistry,
        Cooking,
        Crusher,
    }

    public enum DeathCause
    {
        Void,
        Fall,
        Damage,
        Glitch,
    }

    /// <summary>Игрок погиб. На этапе 7 по нему выпадет рюкзак и начнётся возрождение (как в 7DTD).</summary>
    public struct PlayerDiedEvent
    {
        public GameObject Player;
        public DeathCause Cause;
        public Vector3 Position;
    }

    /// <summary>Игрока ударило что-то тяжёлое (летящий проп). Survival наносит урон.</summary>
    public struct PlayerImpactEvent
    {
        public GameObject Player;
        public float Damage;
        public string Reason;
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

        /// <summary>Проёмы порталов: камера портала их не видит (иначе проём рисовал бы сам себя).</summary>
        public const int Portal = 12;

        /// <summary>С чем сталкивается капсула игрока.</summary>
        public const int PlayerCollisionMask = (1 << Default) | (1 << World) | (1 << Props) | (1 << Enemy);
    }
}
