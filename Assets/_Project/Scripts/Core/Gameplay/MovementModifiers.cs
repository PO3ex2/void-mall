namespace VoidMall.Core
{
    /// <summary>
    /// Итоговые модификаторы движения за тик. Собираются со всех IMovementModifierSource
    /// (вес инвентаря, травмы, стамина, читы) и применяются мотором и камерой.
    /// </summary>
    public struct MovementModifiers
    {
        public float SpeedMul;
        public float AccelMul;
        public float JumpMul;
        public float StaminaCostMul;

        /// <summary>Максимальная скорость поворота взгляда, °/с. +∞ — без ограничения.</summary>
        public float MaxTurnRateDeg;

        public bool CanSprint;
        public bool CanJump;

        /// <summary>false — игрок не может встать (например, сломаны обе ноги) и двигается пригнувшись.</summary>
        public bool CanStand;

        /// <summary>Раскачка взгляда и прицела: 0 — нет, 1 — сильная.</summary>
        public float SwayAmount;

        /// <summary>Хромота: 0 — нет, 1 — сильная.</summary>
        public float LimpAmount;

        /// <summary>Дрожь от холода: мелкое быстрое дрожание взгляда, 0 — нет, 1 — сильная.</summary>
        public float ShiverAmount;

        /// <summary>Бхоп: прыжок в момент приземления не теряет скорость (навык «Атлет»).</summary>
        public bool Bhop;

        /// <summary>Потолок горизонтальной скорости при прыжке (× SprintSpeed); 0 — из настроек движения.</summary>
        public float JumpSpeedFactor;

        public static MovementModifiers Default => new()
        {
            SpeedMul = 1f,
            AccelMul = 1f,
            JumpMul = 1f,
            StaminaCostMul = 1f,
            MaxTurnRateDeg = float.PositiveInfinity,
            CanSprint = true,
            CanJump = true,
            CanStand = true,
        };
    }

    public interface IMovementModifierSource
    {
        void ApplyModifiers(ref MovementModifiers modifiers);
    }
}
