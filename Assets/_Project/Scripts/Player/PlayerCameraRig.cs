using UnityEngine;
using VoidMall.Console;

namespace VoidMall.Player
{
    /// <summary>
    /// Камера от первого лица: интерполяция между тиками, плавная высота глаз (присед, ступеньки),
    /// покачивание при ходьбе, просадка при приземлении, FOV при спринте, хромота и раскачка от травм.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class PlayerCameraRig : MonoBehaviour
    {
        public static readonly CVar<float> Fov = new("fov", 80f, "Вертикальный угол обзора, градусы.");
        public static readonly CVar<bool> ViewBob = new("cl_bob", true, "Покачивание камеры при ходьбе.");

        [SerializeField] private PlayerMotor _motor;
        [SerializeField] private PlayerLook _look;
        [SerializeField] private Camera _camera;

        [Header("Сглаживание")]
        [SerializeField] private float _eyeHeightSharpness = 12f;

        [Header("Покачивание")]
        [SerializeField] private float _bobAmplitude = 0.035f;
        [SerializeField] private float _bobSideAmplitude = 0.02f;
        [Tooltip("Длина шага, м: одна просадка камеры на шаг.")]
        [SerializeField] private float _bobStride = 1.2f;
        [SerializeField] private float _limpDepth = 0.08f;

        [Header("Приземление")]
        [SerializeField] private float _landingKick = 0.15f;
        [SerializeField] private float _landingMaxKick = 2.5f;
        [SerializeField] private float _landingSpring = 120f;
        [SerializeField] private float _landingDamping = 14f;

        [Header("FOV")]
        [SerializeField] private float _sprintFovBonus = 6f;

        [Header("Раскачка (боль, травмы рук)")]
        [SerializeField] private float _swayDegrees = 2.5f;
        [SerializeField] private float _swayFrequency = 0.6f;

        private float _eyeHeight;
        private float _landOffset;
        private float _landVelocity;
        private float _bobPhase;
        private float _bobWeight;
        private float _fov;

        public Camera Camera => _camera;

        private void Awake()
        {
            if (_camera == null)
                _camera = GetComponent<Camera>();
        }

        private void OnEnable()
        {
            if (_motor == null)
                return;
            _motor.Landed += OnLanded;
        }

        private void OnDisable()
        {
            if (_motor == null)
                return;
            _motor.Landed -= OnLanded;
        }

        private void Start()
        {
            _eyeHeight = _motor.TargetEyeHeight;
            _fov = Fov.Value;
        }

        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            ref readonly var state = ref _motor.State;
            var modifiers = _motor.Modifiers;

            _eyeHeight = Damp(_eyeHeight, _motor.TargetEyeHeight, _eyeHeightSharpness, dt);

            // Пружина приземления.
            float acceleration = -_landingSpring * _landOffset - _landingDamping * _landVelocity;
            _landVelocity += acceleration * dt;
            _landOffset += _landVelocity * dt;

            // Покачивание при ходьбе.
            float speed = state.HorizontalVelocity.magnitude;
            bool bobbing = ViewBob.Value && state.Mode == MoveMode.Walk && state.IsGrounded && speed > 0.3f;
            float bobTarget = bobbing ? Mathf.Clamp01(speed / _motor.Settings.RunSpeed) : 0f;
            _bobWeight = Damp(_bobWeight, bobTarget, 8f, dt);
            if (bobbing)
                _bobPhase = Mathf.Repeat(_bobPhase + speed / _bobStride * Mathf.PI * dt, Mathf.PI * 2f);

            float limp = modifiers.LimpAmount;
            float bobY = (Mathf.Sin(_bobPhase * 2f) * _bobAmplitude - limp * _limpDepth * Mathf.Max(0f, Mathf.Sin(_bobPhase))) * _bobWeight;
            float bobX = Mathf.Sin(_bobPhase) * _bobSideAmplitude * _bobWeight;

            // Раскачка.
            float sway = modifiers.SwayAmount;
            float t = Time.time * _swayFrequency;
            float swayPitch = (Mathf.PerlinNoise(t, 0.17f) - 0.5f) * 2f * sway * _swayDegrees;
            float swayYaw = (Mathf.PerlinNoise(0.53f, t) - 0.5f) * 2f * sway * _swayDegrees;
            float roll = -bobX * 30f - limp * Mathf.Sin(_bobPhase) * 1.5f * _bobWeight;

            Vector3 feet = _motor.InterpolatedPosition;
            Vector3 right = Quaternion.Euler(0f, _look.Yaw, 0f) * Vector3.right;
            Vector3 position = feet
                + Vector3.up * (_eyeHeight + _motor.InterpolatedStepOffset + _landOffset + bobY)
                + right * bobX;
            Quaternion rotation = Quaternion.Euler(_look.Pitch + swayPitch, _look.Yaw + swayYaw, roll);
            transform.SetPositionAndRotation(position, rotation);

            float targetFov = Fov.Value + (state.IsSprinting && speed > _motor.Settings.RunSpeed ? _sprintFovBonus : 0f);
            _fov = Damp(_fov, targetFov, 6f, dt);
            if (_camera != null)
                _camera.fieldOfView = _fov;
        }

        private void OnLanded(float impactSpeed)
        {
            if (impactSpeed < 1.5f)
                return;
            _landVelocity -= Mathf.Min(impactSpeed * _landingKick, _landingMaxKick);
        }


        private static float Damp(float current, float target, float sharpness, float dt) =>
            Mathf.Lerp(current, target, 1f - Mathf.Exp(-sharpness * dt));
    }
}
