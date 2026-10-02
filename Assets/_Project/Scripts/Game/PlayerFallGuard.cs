using UnityEngine;
using VoidMall.Console;
using VoidMall.Core;
using VoidMall.Player;

namespace VoidMall.Game
{
    /// <summary>
    /// Смерть в пустоте: упавший ниже мира (в шахту-бездну или из-за бага коллизии) игрок погибает.
    /// Пока нет модуля выживания (этап 7), «смерть» = событие PlayerDiedEvent + возрождение на последнем месте,
    /// где игрок уверенно стоял. На этапе 7 здесь появятся рюкзак и возрождение на кровати.
    /// </summary>
    public sealed class PlayerFallGuard : MonoBehaviour
    {
        /// <summary>Ниже этой высоты — смерть (дно мира на 0, пустота под ним).</summary>
        public const float VoidDeathY = -40f;

        private const float SafeSampleInterval = 0.5f;
        private const float RescueRayHeight = 200f;
        private const int WorldMask = (1 << GameLayers.World) | (1 << GameLayers.Default);

        private Vector3 _lastSafePosition;
        private bool _hasSafePosition;
        private float _sampleTimer;

        private void LateUpdate()
        {
            var motor = PlayerMotor.Local;
            if (motor == null)
            {
                _hasSafePosition = false;
                return;
            }

            ref readonly var state = ref motor.State;
            if (state.Mode == MoveMode.Noclip)
                return;

            if (state.Position.y > VoidDeathY)
            {
                RememberSafePosition(in state);
                return;
            }

            EventBus<PlayerDiedEvent>.Publish(new PlayerDiedEvent { Player = motor.gameObject, Cause = DeathCause.Void, Position = state.Position });
            Vector3 respawn = _hasSafePosition ? _lastSafePosition : FindSurface(state.Position);
            motor.Teleport(respawn);
            DevConsole.Error($"Вы упали в пустоту и погибли. Возрождение в ({respawn.x:0.#}, {respawn.y:0.#}, {respawn.z:0.#}).");
        }

        private void RememberSafePosition(in MotorState state)
        {
            _sampleTimer -= Time.unscaledDeltaTime;
            if (!state.IsGrounded || state.Position.y < 0f || _sampleTimer > 0f)
                return;
            _sampleTimer = SafeSampleInterval;
            _lastSafePosition = state.Position + Vector3.up * 0.05f;
            _hasSafePosition = true;
        }

        private static Vector3 FindSurface(Vector3 position)
        {
            Vector3 origin = new(position.x, RescueRayHeight, position.z);
            return Physics.Raycast(origin, Vector3.down, out var hit, RescueRayHeight * 2f, WorldMask, QueryTriggerInteraction.Ignore)
                ? hit.point + Vector3.up * 0.05f
                : new Vector3(position.x, 40f, position.z);
        }
    }
}
