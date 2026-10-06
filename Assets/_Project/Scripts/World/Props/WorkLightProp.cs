using UnityEngine;
using VoidMall.Core;

namespace VoidMall.World
{
    /// <summary>
    /// Свет строительной лампы (проп на треноге с автомобильным аккумулятором): прожектор вниз-вперёд.
    /// Технический этаж без ламп — освещён только ими. Заряд пока не тратится; часть ламп с севшим аккумулятором
    /// (решает ID пропа). Пропы живут в пуле, поэтому свет включается и выключается через <see cref="Configure"/>.
    /// </summary>
    public sealed class WorkLightProp : MonoBehaviour
    {
        /// <summary>Доля ламп с живым аккумулятором.</summary>
        public const float ChargedChance = 0.8f;

        private Light _light;
        private bool _on;

        public void Configure(bool isWorkLight, ulong id)
        {
            if (_light == null)
            {
                var go = new GameObject("Work Light");
                go.transform.SetParent(transform, false);
                go.transform.SetLocalPositionAndRotation(new Vector3(0f, 1.75f, 0.12f), Quaternion.Euler(32f, 0f, 0f));
                _light = go.AddComponent<Light>();
                _light.type = LightType.Spot;
                _light.spotAngle = 115f;
                _light.innerSpotAngle = 60f;
                _light.range = 15f;
                _light.intensity = 4f;
                _light.color = new Color(1f, 0.93f, 0.8f);
                _light.shadows = LightShadows.None;
            }
            bool charged = Hash.ToFloat01(Hash.Get((uint)(id ^ (id >> 32)), 0x11A7)) < ChargedChance;
            SetOn(isWorkLight && charged);
        }

        private void OnEnable() => Register(_on);

        private void OnDisable() => LightingDirector.DynamicLights.Remove(_light);

        private void SetOn(bool on)
        {
            _on = on;
            _light.enabled = on;
            if (isActiveAndEnabled)
                Register(on);
        }

        private void Register(bool on)
        {
            if (_light == null)
                return;
            LightingDirector.DynamicLights.Remove(_light);
            if (on)
                LightingDirector.DynamicLights.Add(_light);
        }
    }
}
