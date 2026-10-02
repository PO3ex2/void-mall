using UnityEngine;

namespace VoidMall.Player
{
    /// <summary>
    /// Все параметры движения. Значения подобраны по пропорциям HL2 в метрах и уточняются плейтестом.
    /// </summary>
    [CreateAssetMenu(menuName = "Void Mall/Player/Movement Settings", fileName = "MovementSettings")]
    public sealed class MovementSettings : ScriptableObject
    {
        [Header("Капсула")]
        [Min(0.1f)] public float Radius = 0.35f;
        [Min(0.5f)] public float StandHeight = 1.8f;
        [Tooltip("0.95 — пролезает в лаз высотой 1 блок.")]
        [Min(0.5f)] public float CrouchHeight = 0.95f;
        public float StandEyeHeight = 1.65f;
        public float CrouchEyeHeight = 0.82f;
        [Tooltip("Зазор между капсулой и геометрией.")]
        [Min(0.001f)] public float SkinWidth = 0.015f;
        [Tooltip("Ступенька, на которую поднимаемся шагом. Полный блок (1 м) — только прыжком.")]
        public float StepHeight = 0.55f;
        [Range(1f, 89f)] public float MaxSlopeAngle = 45f;
        [Tooltip("Как далеко ищем землю под ногами, чтобы считаться стоящим.")]
        public float GroundProbeDistance = 0.06f;
        [Tooltip("Масса игрока без груза, кг (давит на пропы, толкает их).")]
        public float Mass = 80f;

        [Header("Камера")]
        [Tooltip("Скорость сглаживания взгляда при подъёме на ступеньку, 1/с. Меньше — плавнее, но больше «отставание».")]
        public float StepSmoothing = 10f;

        [Header("Скорости, м/с")]
        public float WalkSpeed = 2.0f;
        public float RunSpeed = 3.8f;
        public float SprintSpeed = 6.0f;
        public float CrouchSpeed = 1.8f;

        [Header("Source-физика")]
        public float GroundAccelerate = 10f;
        public float AirAccelerate = 10f;
        [Tooltip("Ограничение wishspeed в воздухе (30 юнитов Source). Делает возможным air-strafe.")]
        public float AirWishSpeedCap = 0.6f;
        public float Friction = 5f;
        public float StopSpeed = 1.9f;
        public float Gravity = 15.2f;
        public float MaxFallSpeed = 50f;
        [Tooltip("Если вертикальная скорость выше — мы взлетаем и не можем считаться стоящими.")]
        public float MaxGroundedUpSpeed = 2.5f;

        [Header("Прыжок")]
        [Tooltip("1.15 м — хватает, чтобы запрыгнуть на блок 1 м.")]
        public float JumpHeight = 1.15f;
        public float JumpBufferTime = 0.1f;
        public float CoyoteTime = 0.08f;
        [Tooltip("Ограничение горизонтальной скорости при прыжке (× SprintSpeed). Режет банихоп.")]
        public float MaxJumpSpeedFactor = 1.25f;

        [Header("Стамина")]
        public float SprintStaminaPerSecond = 10f;
        public float JumpStaminaCost = 6f;

        [Header("Взаимодействие с физикой")]
        [Tooltip("Максимальная сила толкания пропов, Н.")]
        public float PushForce = 900f;

        [Header("Полёт (fly / noclip)")]
        public float FlySpeed = 8f;
        public float FlySprintMultiplier = 3f;
        public float FlyAccelerate = 8f;
        public float FlyFriction = 4f;

        public float MinWalkableNormalY => Mathf.Cos(MaxSlopeAngle * Mathf.Deg2Rad);
        public float JumpSpeed => Mathf.Sqrt(2f * Gravity * JumpHeight);
    }
}
