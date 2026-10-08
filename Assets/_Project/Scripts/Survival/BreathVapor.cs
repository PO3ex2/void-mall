using UnityEngine;
using VoidMall.Player;
using VoidMall.World;

namespace VoidMall.Survival
{
    /// <summary>
    /// Пар изо рта на холоде (GDD §4.1): когда воздух холоднее <see cref="Threshold"/>, на каждом выдохе перед лицом
    /// появляется облачко — раз в 3–4 с, при беге и усталости чаще, на морозе гуще. Облачко рождается у головы
    /// персонажа (по его взгляду), поэтому видно и самому игроку, и другим (кооператив — этап 13).
    /// </summary>
    public sealed class BreathVapor : MonoBehaviour
    {
        /// <summary>Ниже этой температуры воздуха (°C) идёт пар.</summary>
        public const float Threshold = 10f;

        private const float CalmInterval = 3.5f, HardInterval = 1.6f;

        private PlayerVitals _vitals;
        private PlayerMotor _motor;
        private ParticleSystem _particles;
        private float _timer;

        private void Awake()
        {
            _vitals = GetComponent<PlayerVitals>();
            _motor = GetComponent<PlayerMotor>();
        }

        private void OnDestroy()
        {
            if (_particles != null)
                Destroy(_particles.gameObject);
        }

        /// <summary>Интервал между выдохами: при беге и низкой стамине дышишь чаще.</summary>
        public static float Interval(bool sprinting, float staminaFraction) =>
            sprinting || staminaFraction < 0.3f ? HardInterval : CalmInterval;

        /// <summary>Гуще на морозе: число частиц облачка.</summary>
        public static int PuffSize(float air) => Mathf.RoundToInt(Mathf.Lerp(9f, 22f, Mathf.Clamp01((Threshold - air) / 15f)));

        private void Update()
        {
            // В лобби (этап 10) выживание на паузе — и пара изо рта нет (лобби далеко над миром, «на морозе»).
            if (_vitals == null || _motor == null || _vitals.IsDead || _vitals.Sleeping || _vitals.Suspended)
                return;
            // Под водой вместо пара — пузыри.
            bool underwater = _motor.State.HeadUnderwater;
            float air = _vitals.AirTemperature;
            if (!underwater && air >= Threshold)
                return;
            _timer -= Time.deltaTime;
            if (_timer > 0f)
                return;
            var model = _vitals.Model;
            _timer = Interval(_motor.State.IsSprinting, model.Stamina / Mathf.Max(1f, model.MaxStamina)) + Random.Range(-0.3f, 0.4f);
            Puff(underwater ? 7 : PuffSize(air), underwater);
        }

        private void Puff(int count, bool bubbles)
        {
            if (_particles == null && !Build())
                return;
            var look = _motor.Look;
            Quaternion view = look != null ? look.Rotation : transform.rotation;
            float eye = _motor.TargetEyeHeight;
            Vector3 mouth = _motor.InterpolatedPosition + Vector3.up * (eye - 0.12f) + view * new Vector3(0f, 0f, 0.3f);
            Vector3 forward = view * Vector3.forward;
            var emit = new ParticleSystem.EmitParams();
            for (int i = 0; i < count; i++)
            {
                emit.position = mouth + Random.insideUnitSphere * 0.05f;
                if (bubbles)
                {
                    // Пузыри всплывают, мельче и ярче пара.
                    emit.velocity = Vector3.up * Random.Range(0.6f, 1.1f) + Random.insideUnitSphere * 0.12f;
                    emit.startColor = new Color(0.8f, 0.95f, 1f, 0.55f);
                    emit.startSize = Random.Range(0.02f, 0.045f);
                }
                else
                {
                    emit.velocity = forward * Random.Range(0.35f, 0.7f) + Random.insideUnitSphere * 0.08f + Vector3.up * 0.05f;
                    emit.ResetStartColor();
                    emit.ResetStartSize();
                }
                _particles.Emit(emit, 1);
            }
        }

        private bool Build()
        {
            var material = PropManager.Instance != null ? PropManager.Instance.DustMaterial : null;
            if (material == null)
                return false;
            var go = new GameObject("Breath Vapor");
            _particles = go.AddComponent<ParticleSystem>();
            _particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = _particles.main;
            main.loop = false;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.1f, 1.7f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.06f);
            main.startColor = new Color(0.92f, 0.94f, 0.97f, 0.14f);
            main.gravityModifier = -0.01f;
            main.maxParticles = 200;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var emission = _particles.emission;
            emission.rateOverTime = 0f;
            var size = _particles.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 6f));
            var color = _particles.colorOverLifetime;
            color.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.6f, 0.4f), new GradientAlphaKey(0f, 1f) });
            color.color = fade;
            var velocity = _particles.limitVelocityOverLifetime;
            velocity.enabled = true;
            velocity.limit = 0.15f;
            velocity.dampen = 0.08f;
            go.GetComponent<ParticleSystemRenderer>().sharedMaterial = material;
            return true;
        }
    }
}
