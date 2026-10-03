using UnityEngine;
using VoidMall.Core;

namespace VoidMall.World
{
    /// <summary>
    /// Платформа или кабина лифта, курсирующая между двумя точками. Переносит игрока через IMovingSurface.
    /// Выполняется раньше мотора игрока, чтобы скорость за тик была уже известна.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class MovingPlatform : MonoBehaviour, IMovingSurface
    {
        [Tooltip("Смещение второй точки относительно стартовой позиции.")]
        [SerializeField] private Vector3 _offset = new(0f, 6f, 0f);
        [SerializeField] private float _speed = 2f;
        [Tooltip("Пауза в крайних точках, с.")]
        [SerializeField] private float _wait = 2f;

        private Rigidbody _body;
        private Vector3 _from;
        private Vector3 _to;
        private float _progress;
        private int _direction = 1;
        private float _waitTimer;
        private Vector3 _velocity;

        private void Awake()
        {
            _body = GetComponent<Rigidbody>();
            _body.isKinematic = true;
            _body.interpolation = RigidbodyInterpolation.Interpolate;
            _from = transform.position;
            _to = _from + _offset;
            _waitTimer = _wait;
        }

        private static readonly Perf.Probe FixedProbe = Perf.Register("World.MovingPlatform");

        private void FixedUpdate()
        {
            using var perf = Perf.Measure(FixedProbe);
            float dt = Time.fixedDeltaTime;
            if (_waitTimer > 0f)
            {
                _waitTimer -= dt;
                _velocity = Vector3.zero;
                return;
            }

            float length = Mathf.Max(_offset.magnitude, 0.001f);
            float previous = _progress;
            _progress = Mathf.Clamp01(_progress + _direction * _speed * dt / length);
            Vector3 current = Vector3.Lerp(_from, _to, previous);
            Vector3 next = Vector3.Lerp(_from, _to, _progress);
            _velocity = (next - current) / dt;
            _body.MovePosition(next);

            if (_progress <= 0f || _progress >= 1f)
            {
                _direction = -_direction;
                _waitTimer = _wait;
            }
        }

        public Vector3 GetVelocityAt(Vector3 worldPoint) => _velocity;

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            Vector3 start = Application.isPlaying ? _from : transform.position;
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(start, start + _offset);
            Gizmos.DrawWireCube(start + _offset, transform.lossyScale);
        }
#endif
    }
}
