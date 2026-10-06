using UnityEngine;

namespace VoidMall.World
{
    /// <summary>
    /// Огонь бочки-костра: мерцающий оранжевый свет и искры над бочкой, пока станция горит (сообщает о тепле в
    /// <see cref="HeatSources"/>). Свет входит в освещённость — у огня не темно. Пропы живут в пуле, поэтому
    /// огонь включается и выключается через <see cref="Configure"/>.
    /// </summary>
    public sealed class FireBarrelProp : MonoBehaviour
    {
        private const float Intensity = 3.2f;

        private Light _light;
        private ParticleSystem _sparks;
        private ulong _id;
        private bool _isBarrel, _burning;
        private float _seed;

        public void Configure(bool isBarrel, ulong id, float height)
        {
            _isBarrel = isBarrel;
            _id = id;
            _seed = (id % 1000) * 0.37f;
            if (_light == null && isBarrel)
                Build(height);
            SetBurning(false);
            enabled = isBarrel;
        }

        private void Build(float height)
        {
            var go = new GameObject("Fire Light");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, height + 0.35f, 0f);
            _light = go.AddComponent<Light>();
            _light.type = LightType.Point;
            _light.range = 7f;
            _light.color = new Color(1f, 0.55f, 0.2f);
            _light.shadows = LightShadows.None;

            var material = PropManager.Instance != null ? PropManager.Instance.DustMaterial : null;
            if (material == null)
                return;
            var sparks = new GameObject("Fire");
            sparks.transform.SetParent(transform, false);
            sparks.transform.localPosition = new Vector3(0f, height - 0.05f, 0f);
            _sparks = sparks.AddComponent<ParticleSystem>();
            _sparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = _sparks.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 1.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.22f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.75f, 0.25f, 0.9f), new Color(1f, 0.35f, 0.08f, 0.8f));
            main.gravityModifier = -0.15f;
            main.maxParticles = 120;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var emission = _sparks.emission;
            emission.rateOverTime = 45f;
            var shape = _sparks.shape;
            // Конус вверх: ось +Z повёрнута к +Y.
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 15f;
            shape.radius = 0.2f;
            shape.rotation = new Vector3(-90f, 0f, 0f);
            var size = _sparks.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));
            sparks.GetComponent<ParticleSystemRenderer>().sharedMaterial = material;
        }

        private void Update()
        {
            if (!_isBarrel)
                return;
            bool burning = HeatSources.IsBurning(_id);
            if (burning != _burning)
                SetBurning(burning);
            if (_burning && _light != null)
            {
                // Неровное мерцание огня: два синуса и шум.
                float t = Time.time * 9f + _seed;
                _light.intensity = Intensity * (0.8f + 0.12f * Mathf.Sin(t) + 0.08f * Mathf.Sin(t * 2.3f) + 0.1f * Mathf.PerlinNoise(t * 0.3f, _seed));
            }
        }

        private void SetBurning(bool burning)
        {
            _burning = burning;
            if (_light != null)
            {
                _light.enabled = burning;
                LightingDirector.DynamicLights.Remove(_light);
                if (burning && isActiveAndEnabled)
                    LightingDirector.DynamicLights.Add(_light);
            }
            if (_sparks != null)
            {
                if (burning)
                    _sparks.Play();
                else
                    _sparks.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
        }

        private void OnDisable()
        {
            if (_light != null)
                LightingDirector.DynamicLights.Remove(_light);
            _burning = false;
        }
    }
}
