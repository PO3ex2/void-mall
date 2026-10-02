using UnityEngine;
using VoidMall.Core;

namespace VoidMall.Player
{
    /// <summary>
    /// Временный пул стамины до появления модуля Survival (этап 7).
    /// </summary>
    public sealed class SimpleStaminaPool : MonoBehaviour, IStaminaPool
    {
        [SerializeField] private float _max = 100f;
        [SerializeField] private float _regenPerSecond = 15f;
        [Tooltip("Пауза перед восстановлением после траты, с.")]
        [SerializeField] private float _regenDelay = 1f;
        [Tooltip("После истощения спринт возвращается при этой доле стамины.")]
        [Range(0f, 1f)] [SerializeField] private float _recoverFraction = 0.25f;

        private float _current;
        private float _regenTimer;
        private bool _exhausted;

        public float Current => _current;
        public float Max => _max;
        public bool IsExhausted => _exhausted;

        /// <summary>Debug: стамина не тратится.</summary>
        public bool Infinite { get; set; }

        private void Awake() => _current = _max;

        private void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            if (_regenTimer > 0f)
                _regenTimer -= dt;
            else
                _current = Mathf.Min(_max, _current + _regenPerSecond * dt);

            if (_exhausted && _current >= _max * _recoverFraction)
                _exhausted = false;
        }

        public bool TryConsume(float amount)
        {
            if (Infinite)
                return true;
            if (_current < amount)
                return false;
            Spend(amount);
            return true;
        }

        public void Drain(float amount)
        {
            if (Infinite)
                return;
            Spend(Mathf.Min(amount, _current));
        }

        public void Refill()
        {
            _current = _max;
            _exhausted = false;
        }

        private void Spend(float amount)
        {
            _current -= amount;
            _regenTimer = _regenDelay;
            if (_current <= 0.001f)
            {
                _current = 0f;
                _exhausted = true;
            }
        }
    }
}
