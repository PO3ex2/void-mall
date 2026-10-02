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
