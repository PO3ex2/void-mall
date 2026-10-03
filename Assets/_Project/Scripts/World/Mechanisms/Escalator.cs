using UnityEngine;
using VoidMall.Core;

namespace VoidMall.World
{
    /// <summary>
    /// Эскалатор: неподвижный наклонный коллайдер, поверхность которого «едет» вдоль локальной оси Z
    /// (вверх по уклону при положительной скорости).
    /// </summary>
    public sealed class Escalator : MonoBehaviour, IMovingSurface
    {
        [Tooltip("Скорость ленты, м/с. Отрицательная — едет вниз.")]
        [SerializeField] private float _speed = 0.75f;

        public Vector3 GetVelocityAt(Vector3 worldPoint) => transform.forward * _speed;

        /// <summary>Для эскалаторов, созданных генератором.</summary>
        public void Configure(float speed) => _speed = speed;

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            Gizmos.color = _speed >= 0f ? Color.green : Color.red;
            Vector3 center = transform.position;
            Gizmos.DrawLine(center, center + transform.forward * Mathf.Sign(_speed) * 2f);
        }
#endif
    }
}
