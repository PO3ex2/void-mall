using UnityEngine;

namespace VoidMall.Player
{
    /// <summary>
    /// Формулы движения из Source SDK (gamemovement.cpp), переведённые в метры. Чистые функции.
    /// </summary>
    public static class SourceMovement
    {
        /// <summary>Трение: гасит скорость, при малых скоростях — не медленнее stopSpeed.</summary>
        public static void Friction(ref Vector3 velocity, float dt, float friction, float stopSpeed)
        {
            float speed = velocity.magnitude;
            if (speed < 0.01f)
            {
                velocity = Vector3.zero;
                return;
            }
            float control = speed < stopSpeed ? stopSpeed : speed;
            float drop = control * friction * dt;
            float newSpeed = Mathf.Max(speed - drop, 0f);
            velocity *= newSpeed / speed;
        }

        /// <summary>Разгон на земле: добавляет скорость вдоль wishDir, пока проекция не достигнет wishSpeed.</summary>
        public static void Accelerate(ref Vector3 velocity, Vector3 wishDir, float wishSpeed, float accel, float dt)
        {
            float currentSpeed = Vector3.Dot(velocity, wishDir);
            float addSpeed = wishSpeed - currentSpeed;
            if (addSpeed <= 0f)
                return;
            float accelSpeed = Mathf.Min(accel * dt * wishSpeed, addSpeed);
            velocity += accelSpeed * wishDir;
        }

        /// <summary>
        /// Разгон в воздухе: проекция ограничена airCap, а прирост считается от полного wishSpeed.
        /// Именно эта асимметрия даёт air-strafe.
        /// </summary>
        public static void AirAccelerate(ref Vector3 velocity, Vector3 wishDir, float wishSpeed, float accel, float airCap, float dt)
        {
            float cappedWish = Mathf.Min(wishSpeed, airCap);
            float currentSpeed = Vector3.Dot(velocity, wishDir);
            float addSpeed = cappedWish - currentSpeed;
            if (addSpeed <= 0f)
                return;
            float accelSpeed = Mathf.Min(accel * wishSpeed * dt, addSpeed);
            velocity += accelSpeed * wishDir;
        }

        /// <summary>Убирает из скорости составляющую, направленную в плоскость.</summary>
        public static Vector3 ClipVelocity(Vector3 velocity, Vector3 normal, float overbounce = 1f)
        {
            float backoff = Vector3.Dot(velocity, normal);
            if (backoff >= 0f)
                return velocity;
            return velocity - normal * (backoff * overbounce);
        }
    }
}
