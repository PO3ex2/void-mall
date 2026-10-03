using System.Collections.Generic;
using UnityEngine;
using VoidMall.Core;

namespace VoidMall.World
{
    /// <summary>Падающий обломок: разбивается при ударе (или через время), оставляя облако пыли.</summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class FallingDebris : MonoBehaviour
    {
        private const float ImpactSpeed = 2f;
        private const float MaxLifetime = 6f;
        private const float RestTime = 0.5f;

        private Rigidbody _body;
        private Material _dustMaterial;
        private Dictionary<ushort, float> _materials;
        private int _cells;
        private float _age;
        private float _restTimer;
        private bool _shattered;

        public void Initialize(Material dustMaterial, Dictionary<ushort, float> materials, int cells)
        {
            _body = GetComponent<Rigidbody>();
            _dustMaterial = dustMaterial;
            _materials = materials;
            _cells = cells;
        }

        private static readonly Perf.Probe FixedProbe = Perf.Register("World.Debris");

        private void FixedUpdate()
        {
            using var perf = Perf.Measure(FixedProbe);
            _age += Time.fixedDeltaTime;
            _restTimer = _body.linearVelocity.sqrMagnitude < 0.05f ? _restTimer + Time.fixedDeltaTime : 0f;
            // Сразу после появления обломок ещё «висит» — не считаем это покоем.
            if (_age > MaxLifetime || (_age > 1f && _restTimer > RestTime))
                Shatter(_body.linearVelocity.magnitude);
        }

        private void OnCollisionEnter(Collision collision)
        {
            float speed = collision.relativeVelocity.magnitude;
            if (speed >= ImpactSpeed)
                Shatter(speed);
        }

        private void Shatter(float speed)
        {
            if (_shattered)
                return;
            _shattered = true;

            var bounds = GetComponent<Renderer>().bounds;
            EventBus<DebrisImpactEvent>.Publish(new DebrisImpactEvent { Bounds = bounds, Mass = _body.mass, Speed = speed });
            foreach (var (material, amount) in _materials)
                EventBus<MaterialMinedEvent>.Publish(new MaterialMinedEvent { Material = material, Amount = amount, Position = bounds.center });

            SpawnDust(bounds);
            Destroy(GetComponent<MeshFilter>().sharedMesh);
            Destroy(gameObject);
        }

        private void SpawnDust(Bounds bounds) =>
            DustFx.Burst(_dustMaterial, bounds, _cells * 10, new Color(0.55f, 0.53f, 0.5f, 0.9f), new Color(0.75f, 0.73f, 0.7f, 0.6f));
    }
}
