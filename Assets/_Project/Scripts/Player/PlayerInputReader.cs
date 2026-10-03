using UnityEngine;
using UnityEngine.InputSystem;
using VoidMall.Console;
using VoidMall.Core;
using VoidMall.Input;

namespace VoidMall.Player
{
    /// <summary>
    /// Локальный ввод: каждый кадр крутит взгляд и копит нажатия, мотор раз в тик забирает PlayerInputFrame.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public sealed class PlayerInputReader : MonoBehaviour, IPlayerInputSource
    {
        public static readonly CVar<float> MouseSensitivity = new("sensitivity", 0.08f, "Чувствительность мыши, °/пиксель.", ConsoleFlags.Archive);
        public static readonly CVar<float> StickSensitivity = new("joy_sensitivity", 220f, "Скорость поворота стиком, °/с.", ConsoleFlags.Archive);
        public static readonly CVar<bool> InvertY = new("m_invert", false, "Инвертировать ось Y взгляда.", ConsoleFlags.Archive);
        public static readonly CVar<bool> CrouchToggle = new("crouch_toggle", false, "Присед переключением, а не удержанием.", ConsoleFlags.Archive);

        [SerializeField] private PlayerLook _look;
        [SerializeField] private PlayerMotor _motor;

        private InputAction _move;
        private InputAction _lookMouse;
        private InputAction _lookStick;
        private InputAction _jump;
        private InputAction _crouch;
        private InputAction _sprint;
        private InputAction _walk;
        private InputAction _interact;
        private InputAction _primary;
        private InputAction _secondary;

        private PlayerButtons _pressed;
        private bool _crouchToggled;
        private bool _bound;

        private void Awake()
        {
            if (_look == null)
                _look = GetComponent<PlayerLook>();
            if (_motor == null)
                _motor = GetComponent<PlayerMotor>();
        }

        private bool TryBind()
        {
            if (_bound)
                return true;
            if (!Services.TryGet(out InputService input))
                return false;

            const string map = InputService.Maps.Player;
            _move = input.Action(map, "Move");
            _lookMouse = input.Action(map, "LookMouse");
            _lookStick = input.Action(map, "LookStick");
            _jump = input.Action(map, "Jump");
            _crouch = input.Action(map, "Crouch");
            _sprint = input.Action(map, "Sprint");
            _walk = input.Action(map, "Walk");
            _interact = input.Action(map, "Interact");
            _primary = input.Action(map, "Primary");
            _secondary = input.Action(map, "Secondary");
            _bound = true;
            return true;
        }

        private static readonly Perf.Probe UpdateProbe = Perf.Register("Player.Input");

        private void Update()
        {
            using var perf = Perf.Measure(UpdateProbe);
            if (!TryBind())
                return;

            float dt = Time.deltaTime;
            Vector2 mouse = _lookMouse.ReadValue<Vector2>() * MouseSensitivity.Value;
            Vector2 stick = _lookStick.ReadValue<Vector2>();
            stick = new Vector2(StickCurve(stick.x), StickCurve(stick.y)) * (StickSensitivity.Value * dt);
            Vector2 look = mouse + stick;
            if (InvertY.Value)
                look.y = -look.y;

            float maxTurn = _motor != null ? _motor.Modifiers.MaxTurnRateDeg : float.PositiveInfinity;
            _look.AddLook(look.x, look.y, maxTurn, dt);

            if (_jump.WasPressedThisFrame()) _pressed |= PlayerButtons.Jump;
            if (_crouch.WasPressedThisFrame())
            {
                _pressed |= PlayerButtons.Crouch;
                if (CrouchToggle.Value)
                    _crouchToggled = !_crouchToggled;
            }
            if (_sprint.WasPressedThisFrame()) _pressed |= PlayerButtons.Sprint;
            if (_interact.WasPressedThisFrame()) _pressed |= PlayerButtons.Interact;
            if (_primary.WasPressedThisFrame()) _pressed |= PlayerButtons.Primary;
            if (_secondary.WasPressedThisFrame()) _pressed |= PlayerButtons.Secondary;
        }

        public PlayerInputFrame ConsumeFrame()
        {
            var frame = new PlayerInputFrame
            {
                Yaw = _look != null ? _look.Yaw : 0f,
                Pitch = _look != null ? _look.Pitch : 0f,
            };
            if (!_bound)
                return frame;

            frame.Move = _move.ReadValue<Vector2>();
            frame.Pressed = _pressed;
            _pressed = PlayerButtons.None;

            bool crouchHeld = CrouchToggle.Value ? _crouchToggled : _crouch.IsPressed();
            if (_jump.IsPressed()) frame.Held |= PlayerButtons.Jump;
            if (crouchHeld) frame.Held |= PlayerButtons.Crouch;
            if (_sprint.IsPressed()) frame.Held |= PlayerButtons.Sprint;
            if (_walk.IsPressed()) frame.Held |= PlayerButtons.Walk;
            if (_interact.IsPressed()) frame.Held |= PlayerButtons.Interact;
            if (_primary.IsPressed()) frame.Held |= PlayerButtons.Primary;
            if (_secondary.IsPressed()) frame.Held |= PlayerButtons.Secondary;
            return frame;
        }

        /// <summary>Квадратичная кривая стика: точное прицеливание на малых отклонениях.</summary>
        private static float StickCurve(float value) => Mathf.Sign(value) * value * value;
    }
}
