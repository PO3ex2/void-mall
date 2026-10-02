using UnityEngine;

namespace VoidMall.Player
{
    public enum MoveMode
    {
        Walk,
        Fly,
        Noclip,
    }

    /// <summary>
    /// Полное состояние мотора. Position — точка между ступнями (низ капсулы).
    /// </summary>
    public struct MotorState
    {
        public Vector3 Position;
        public Vector3 Velocity;
        public float Yaw;
        public float Pitch;
        public MoveMode Mode;

        public bool IsGrounded;
        public bool IsCrouched;
        public bool IsSprinting;

        /// <summary>Под ногами слишком крутой склон: стоять нельзя, игрок съезжает (физически это полёт).</summary>
        public bool IsSliding;

        public Vector3 GroundNormal;
        public Vector3 GroundPoint;
        public Collider GroundCollider;

        /// <summary>Скорость поверхности под ногами (эскалатор, лифт, проп).</summary>
        public Vector3 GroundVelocity;

        public Vector3 HorizontalVelocity => new(Velocity.x, 0f, Velocity.z);
    }
}
