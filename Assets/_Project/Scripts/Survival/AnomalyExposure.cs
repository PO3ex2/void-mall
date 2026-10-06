using Unity.Mathematics;
using UnityEngine;
using VoidMall.Core;
using VoidMall.Player;
using VoidMall.World;

namespace VoidMall.Survival
{
    /// <summary>
    /// Что аномалии делают с игроком напрямую (GDD §2.5), каждый кадр из PlayerVitals:
    /// «Смотри на меня» тянет взгляд к себе (мягко, мышью можно сопротивляться) и, пока на неё смотришь, ест рассудок;
    /// пульсирующее искажение сносит; светящаяся лужа лечит из своего запаса; оазис — уютное место.
    /// </summary>
    public sealed class AnomalyExposure
    {
        /// <summary>С какого расстояния вспышка тянет взгляд, в каком конусе от взгляда её замечаешь, когда «смотришь».</summary>
        public const float LookRange = 24f, NoticeAngle = 70f, StareAngle = 12f;

        /// <summary>Скорость поворота взгляда к вспышке, °/с: вплотную и на краю дальности.</summary>
        public const float PullNear = 60f, PullFar = 15f;

        /// <summary>Лечение светящейся лужей, здоровья в секунду.</summary>
        public const float PuddleHealRate = 2f;

        private const float SearchInterval = 0.1f;

        public bool Staring { get; private set; }
        public bool InOasis { get; private set; }
        public float PuddleHeal { get; private set; }

        /// <summary>Видимая вспышка, которая тянет взгляд (для отладки и эффектов).</summary>
        public bool HasTarget => _hasTarget;
        public Vector3 Target => _target;

        private bool _hasTarget;
        private Vector3 _target;
        private float _searchTimer;

        public void Tick(ChunkManager world, PlayerMotor motor, SurvivalEnvironment probe, VitalsModel model, bool active, float dt)
        {
            Staring = false;
            PuddleHeal = 0f;
            if (motor != null)
                motor.ExternalDrift = Vector3.zero;
            if (world == null || motor == null || !active)
            {
                _hasTarget = false;
                return;
            }
            Vector3 feet = motor.transform.position;
            var camera = Camera.main;

            // Пульсация сносит (вне режима полёта).
            if (motor.State.Mode == MoveMode.Walk)
                motor.ExternalDrift = AnomalyPhysics.PulseVelocity(feet + Vector3.up, Time.time, AnomalyPhysics.PulsePlayerSpeed);

            // Светящаяся лужа лечит, пока есть что лечить.
            if (AnomalyPuddles.Instance != null && model.Health < model.MaxHealth - 0.01f && dt > 0f)
                PuddleHeal = AnomalyPuddles.Instance.Drain(feet, PuddleHealRate * dt) / dt;

            InOasis = probe.InOasis(world, feet);

            if (camera == null || motor.Look == null)
                return;
            Vector3 eye = camera.transform.position;
            _searchTimer -= dt;
            if (_searchTimer <= 0f)
            {
                _searchTimer = SearchInterval;
                _hasTarget = FindVisible(world, eye, camera.transform.forward, out _target);
            }
            if (!_hasTarget)
                return;

            Vector3 toTarget = _target - eye;
            float distance = toTarget.magnitude;
            if (distance < 0.05f)
                return;
            Vector3 direction = toTarget / distance;
            float angle = Vector3.Angle(camera.transform.forward, direction);
            Staring = angle < StareAngle;

            // Мягкая тяга: поворот к вспышке с ограниченной скоростью — мышь её перебивает.
            var look = motor.Look;
            float wantYaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
            float wantPitch = -Mathf.Asin(Mathf.Clamp(direction.y, -1f, 1f)) * Mathf.Rad2Deg;
            float rate = Mathf.Lerp(PullNear, PullFar, distance / LookRange) * dt;
            float yawStep = Mathf.Clamp(Mathf.DeltaAngle(look.Yaw, wantYaw), -rate, rate);
            float pitchStep = Mathf.Clamp(Mathf.DeltaAngle(look.Pitch, wantPitch), -rate, rate);
            look.AddLook(yawStep, -pitchStep, float.PositiveInfinity, dt);
        }

        /// <summary>Ближайшая вспышка «Смотри на меня» в поле зрения и без стены между ней и глазами.</summary>
        private static bool FindVisible(ChunkManager world, Vector3 eye, Vector3 forward, out Vector3 target)
        {
            target = default;
            float best = float.MaxValue;
            int2 center = MallLayout.CellOf(eye);
            int mask = 1 << GameLayers.World;
            for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                var info = world.GetCellInfo(center + new int2(dx, dz));
                for (int i = 0; i < info.MarkerCount; i++)
                {
                    var marker = info.GetMarker(i);
                    if (marker.Kind != AnomalyMarkerKind.LookAtMe)
                        continue;
                    Vector3 point = marker.Center;
                    float distance = Vector3.Distance(eye, point);
                    if (distance > LookRange || distance >= best || Vector3.Angle(forward, point - eye) > NoticeAngle)
                        continue;
                    if (Physics.Linecast(eye, point, mask, QueryTriggerInteraction.Ignore))
                        continue;
                    best = distance;
                    target = point;
                }
            }
            return best < float.MaxValue;
        }
    }
}
