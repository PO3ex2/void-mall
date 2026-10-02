using UnityEngine;

namespace VoidMall.Player
{
    /// <summary>
    /// Углы взгляда. Обновляются каждый кадр (а не в тике), чтобы мышь была отзывчивой при любом FPS.
    /// Мотор берёт текущие углы при формировании PlayerInputFrame.
    /// </summary>
    public sealed class PlayerLook : MonoBehaviour
    {
        public const float MaxPitch = 89f;

        public float Yaw { get; private set; }
        public float Pitch { get; private set; }

        private void Awake()
        {
            Yaw = transform.eulerAngles.y;
        }

        /// <param name="deltaYaw">Поворот вправо, градусы.</param>
        /// <param name="deltaPitch">Поворот вверх, градусы.</param>
        /// <param name="maxTurnRateDeg">Ограничение скорости поворота (перевес), °/с.</param>
        public void AddLook(float deltaYaw, float deltaPitch, float maxTurnRateDeg, float dt)
        {
            if (!float.IsInfinity(maxTurnRateDeg))
            {
                float limit = maxTurnRateDeg * dt;
                deltaYaw = Mathf.Clamp(deltaYaw, -limit, limit);
                deltaPitch = Mathf.Clamp(deltaPitch, -limit, limit);
            }
            Yaw = Mathf.Repeat(Yaw + deltaYaw, 360f);
            Pitch = Mathf.Clamp(Pitch - deltaPitch, -MaxPitch, MaxPitch);
        }

        public void SetAngles(float yaw, float pitch)
        {
            Yaw = Mathf.Repeat(yaw, 360f);
            Pitch = Mathf.Clamp(pitch, -MaxPitch, MaxPitch);
        }

        public Quaternion Rotation => Quaternion.Euler(Pitch, Yaw, 0f);
    }
}
