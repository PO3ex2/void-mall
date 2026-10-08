using UnityEngine;

namespace VoidMall.Game
{
    /// <summary>Мигающая лампа (лобби): ровный свет с редкими короткими провалами, иногда — серия морганий.</summary>
    [RequireComponent(typeof(Light))]
    public sealed class FlickerLight : MonoBehaviour
    {
        [SerializeField] private Renderer _fixture;
        [SerializeField] private float _intensity = 2f;

        private Light _light;
        private float _next;
        private int _blinks;
        private float _off;

        private void Awake()
        {
            _light = GetComponent<Light>();
            _next = Time.time + Random.Range(3f, 8f);
        }

        private void Update()
        {
            if (Time.time >= _next)
            {
                if (_blinks <= 0)
                    _blinks = Random.value < 0.3f ? Random.Range(2, 5) : 1;
                _off = Random.Range(0.04f, 0.12f);
                _blinks--;
                _next = Time.time + (_blinks > 0 ? Random.Range(0.06f, 0.2f) : Random.Range(4f, 12f));
            }
            _off -= Time.deltaTime;
            bool dark = _off > 0f;
            _light.intensity = dark ? _intensity * 0.08f : _intensity * (0.97f + 0.03f * Mathf.PerlinNoise(Time.time * 7f, 0.3f));
            if (_fixture != null)
                _fixture.enabled = !dark;
        }
    }
}
