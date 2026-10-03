using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using VoidMall.Core;
using VoidMall.Input;
using VoidMall.Player;
using VoidMall.World;

namespace VoidMall.UI
{
    /// <summary>Отладочный HUD движения (F3): скорость, состояние, стамина, модификаторы, последнее приземление.</summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class DebugHud : MonoBehaviour
    {
        private const float RefreshInterval = 0.1f;

        private readonly StringBuilder _sb = new(512);
        private InputAction _toggle;
        private Label _label;
        private VisualElement _panel;
        private bool _visible = true;
        private float _refreshTimer;
        private float _fpsAccumulator;
        private int _fpsFrames;
        private float _fps;

        public void Initialize(InputService input)
        {
            _toggle = input.Action(InputService.Maps.Global, "ToggleDebugHud");

            var root = GetComponent<UIDocument>().rootVisualElement;
            DevToolsStyle.FullScreenRoot(root);

            _panel = new VisualElement { pickingMode = PickingMode.Ignore };
            DevToolsStyle.Panel(_panel);
            _panel.style.position = Position.Absolute;
            _panel.style.left = 8f;
            _panel.style.bottom = 8f;
            _panel.style.backgroundColor = new Color(0f, 0f, 0f, 0.55f);
            root.Add(_panel);

            _label = new Label { enableRichText = false, pickingMode = PickingMode.Ignore };
            _label.style.fontSize = 13f;
            _panel.Add(_label);
        }

        private void Update()
        {
            if (_toggle != null && _toggle.WasPressedThisFrame())
            {
                _visible = !_visible;
                _panel.style.display = _visible ? DisplayStyle.Flex : DisplayStyle.None;
            }

            _fpsAccumulator += Time.unscaledDeltaTime;
            _fpsFrames++;
            _refreshTimer -= Time.unscaledDeltaTime;
            if (!_visible || _refreshTimer > 0f)
                return;
            _refreshTimer = RefreshInterval;
            _fps = _fpsFrames / Mathf.Max(_fpsAccumulator, 1e-4f);
            _fpsAccumulator = 0f;
            _fpsFrames = 0;
            _label.text = BuildText();
        }

        private string BuildText()
        {
            _sb.Clear();
            _sb.Append($"FPS {_fps:0}   [F3] HUD   [F4] Травмы/вес   [F6] Карта   [`] Консоль\n");

            var motor = PlayerMotor.Local;
            if (motor == null)
            {
                _sb.Append("Нет локального игрока");
                return _sb.ToString();
            }

            var s = motor.State;
            var m = motor.Modifiers;
            float horizontal = s.HorizontalVelocity.magnitude;
            float slope = Vector3.Angle(s.GroundNormal, Vector3.up);

            _sb.Append($"Позиция  {s.Position.x:0.00} {s.Position.y:0.00} {s.Position.z:0.00}\n");
            _sb.Append($"Скорость {horizontal:0.00} м/с  (верт. {s.Velocity.y:0.00})   режим {s.Mode}\n");
            _sb.Append(
                s.IsGrounded ? $"На земле, уклон {slope:0}°" :
                s.IsSliding ? $"Скольжение: уклон {slope:0}° круче {motor.Settings.MaxSlopeAngle:0}°" :
                "В воздухе");
            if (s.GroundVelocity.sqrMagnitude > 0.0001f)
                _sb.Append($", поверхность едет {s.GroundVelocity.magnitude:0.00} м/с");
            _sb.Append('\n');
            _sb.Append($"{(s.IsCrouched ? "Присед" : "Стоя")}{(s.IsSprinting ? ", спринт" : "")}\n");

            if (motor.Stamina != null)
                _sb.Append($"Стамина {motor.Stamina.Current:0}/{motor.Stamina.Max:0}{(motor.Stamina.IsExhausted ? "  ИСТОЩЕНИЕ" : "")}\n");

            _sb.Append($"Модификаторы: скорость ×{m.SpeedMul:0.00}, разгон ×{m.AccelMul:0.00}");
            if (!float.IsInfinity(m.MaxTurnRateDeg))
                _sb.Append($", поворот ≤{m.MaxTurnRateDeg:0}°/с");
            if (!m.CanSprint) _sb.Append(", без спринта");
            if (!m.CanJump) _sb.Append(", без прыжка");
            if (!m.CanStand) _sb.Append(", не встать");
            _sb.Append('\n');

            float landing = motor.LastLandingSpeed;
            var severity = MovementRules.ClassifyFall(landing);
            string severityText = severity switch
            {
                MovementRules.FallSeverity.Fracture => " — ПЕРЕЛОМ",
                MovementRules.FallSeverity.Damage => " — урон",
                _ => string.Empty,
            };
            _sb.Append($"Последнее приземление {landing:0.0} м/с{severityText}");

            var world = ChunkManager.Instance;
            if (world != null)
            {
                _sb.Append('\n').Append(MallDebug.Describe(world, s.Position));
                if (Services.TryGet(out IWorldClock clock))
                {
                    float hour = clock.Hour;
                    _sb.Append($"\nДень {clock.Day}, {(int)hour:00}:{(int)(hour % 1f * 60f):00}{(clock.IsNight ? " — ночь" : "")}");
                }
                if (Services.TryGet(out ILightProbe probe))
                    _sb.Append($", освещённость {probe.SampleLight(s.Position + Vector3.up):0.00}");
                var lighting = LightingDirector.Instance;
                if (lighting != null)
                    _sb.Append($", ламп со светом {lighting.ActiveLights}");
            }
            return _sb.ToString();
        }
    }
}
