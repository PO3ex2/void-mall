using UnityEngine;
using VoidMall.Console;

namespace VoidMall.Player
{
    internal static class PlayerCommands
    {
        [ConsoleCommand("noclip", "Полёт сквозь стены.", Flags = ConsoleFlags.Cheat)]
        private static string Noclip() => ToggleMode(MoveMode.Noclip);

        [ConsoleCommand("fly", "Полёт с коллизиями.", Flags = ConsoleFlags.Cheat)]
        private static string Fly() => ToggleMode(MoveMode.Fly);

        [ConsoleCommand("tp", "Телепорт ступней игрока в точку.", Flags = ConsoleFlags.Cheat)]
        private static string Teleport(Vector3 position)
        {
            if (!TryGetPlayer(out var motor, out var error))
                return error;
            motor.Teleport(position);
            return $"Teleported to {ConsoleParser.Format(position)}";
        }

        [ConsoleCommand("getpos", "Позиция и углы взгляда игрока.")]
        private static string GetPos()
        {
            if (!TryGetPlayer(out var motor, out var error))
                return error;
            var state = motor.State;
            return $"pos {ConsoleParser.Format(state.Position)}  yaw {state.Yaw:0.#}  pitch {state.Pitch:0.#}";
        }

        [ConsoleCommand("setspeed", "Задать скорость игрока, м/с (по взгляду).", Flags = ConsoleFlags.Cheat)]
        private static string SetSpeed(float speed)
        {
            if (!TryGetPlayer(out var motor, out var error))
                return error;
            motor.SetVelocity(motor.Look.Rotation * Vector3.forward * speed);
            return null;
        }

        [ConsoleCommand("stamina_infinite", "Бесконечная стамина (0/1).", Flags = ConsoleFlags.Cheat)]
        private static string InfiniteStamina(bool enabled)
        {
            if (!TryGetPlayer(out var motor, out var error))
                return error;
            if (motor.Stamina is not SimpleStaminaPool pool)
                return "Player has no SimpleStaminaPool.";
            pool.Infinite = enabled;
            pool.Refill();
            return $"stamina_infinite {(enabled ? 1 : 0)}";
        }

        private static string ToggleMode(MoveMode mode)
        {
            if (!TryGetPlayer(out var motor, out var error))
                return error;
            var next = motor.State.Mode == mode ? MoveMode.Walk : mode;
            motor.SetMode(next);
            return $"{mode.ToString().ToLowerInvariant()} {(next == mode ? "ON" : "OFF")}";
        }

        private static bool TryGetPlayer(out PlayerMotor motor, out string error)
        {
            motor = PlayerMotor.Local;
            error = motor == null ? "No local player." : null;
            return motor != null;
        }
    }
}
