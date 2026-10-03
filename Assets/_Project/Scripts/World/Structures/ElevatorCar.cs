using UnityEngine;
using VoidMall.Core;

namespace VoidMall.World
{
    /// <summary>
    /// Кабина лифта: ходит по расписанию P2 → тех. этаж → P2 с остановками на каждом уровне.
    /// Положение — функция времени (в кооперативе у всех одинаково без синхронизации состояния).
    /// Двери шахты на уровне открываются, только пока кабина стоит на нём.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class ElevatorCar : MonoBehaviour, IMovingSurface
    {
        public const float Speed = 2.5f;
        public const float Wait = 4f;

        private float[] _stops;
        private Transform[] _doors;
        private float[] _doorHeights;
        private float[] _legStart;
        private int[] _legFrom;
        private int[] _legTo;
        private float _cycle;
        private float _phase;
        private Rigidbody _body;
        private Vector3 _velocity;
        private float _x, _z;

        /// <param name="stops">Высоты пола кабины на каждом уровне (снизу вверх).</param>
        /// <param name="doors">Створки шахты на каждом уровне (поднимаются, когда кабина стоит).</param>
        public void Setup(float[] stops, Transform[] doors, float[] doorHeights, float phase)
        {
            _stops = stops;
            _doors = doors;
            _doorHeights = doorHeights;
            _phase = phase;
            _body = GetComponent<Rigidbody>();
            _body.isKinematic = true;
            _body.interpolation = RigidbodyInterpolation.Interpolate;
            _x = transform.position.x;
            _z = transform.position.z;

            // Маршрут: 0 → 1 → … → n−1 → n−2 → … → 1 → (0), на каждой остановке ожидание.
            int n = stops.Length;
            int legs = (n - 1) * 2;
            _legStart = new float[legs + 1];
            _legFrom = new int[legs];
            _legTo = new int[legs];
            float time = 0f;
            for (int i = 0; i < legs; i++)
            {
                int from = i < n - 1 ? i : 2 * (n - 1) - i;
                int to = i < n - 1 ? i + 1 : 2 * (n - 1) - i - 1;
                _legFrom[i] = from;
                _legTo[i] = to;
                _legStart[i] = time;
                time += Wait + Mathf.Abs(stops[to] - stops[from]) / Speed;
            }
            _legStart[legs] = time;
            _cycle = time;
            Apply(Evaluate(Time.fixedTime, out _), teleport: true);
        }

        private float Evaluate(float time, out int stoppedAt)
        {
            float t = Mathf.Repeat(time + _phase, _cycle);
            int leg = 0;
            while (leg < _legFrom.Length - 1 && t >= _legStart[leg + 1])
                leg++;
            float local = t - _legStart[leg];
            float from = _stops[_legFrom[leg]], to = _stops[_legTo[leg]];
            if (local < Wait)
            {
                stoppedAt = _legFrom[leg];
                return from;
            }
            stoppedAt = -1;
            float travel = Mathf.Abs(to - from) / Speed;
            return Mathf.Lerp(from, to, Mathf.Clamp01((local - Wait) / Mathf.Max(travel, 0.001f)));
        }

        private void FixedUpdate()
        {
            if (_stops == null)
                return;
            float dt = Time.fixedDeltaTime;
            float y = Evaluate(Time.fixedTime, out int stopped);
            float previous = _body.position.y;
            _velocity = new Vector3(0f, (y - previous) / dt, 0f);
            Apply(y, teleport: false);

            for (int i = 0; i < _doors.Length; i++)
            {
                if (_doors[i] == null)
                    continue;
                float target = i == stopped ? _doorHeights[i] : 0f;
                Vector3 p = _doors[i].localPosition;
                p.y = Mathf.MoveTowards(p.y, _stops[i] + target, dt * 2.5f);
                _doors[i].localPosition = p;
            }
        }

        private void Apply(float y, bool teleport)
        {
            var position = new Vector3(_x, y, _z);
            if (teleport)
            {
                _body.position = position;
                transform.position = position;
            }
            else
            {
                _body.MovePosition(position);
            }
        }

        public Vector3 GetVelocityAt(Vector3 worldPoint) => _velocity;
    }
}
