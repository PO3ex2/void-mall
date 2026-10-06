using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using VoidMall.Core;

namespace VoidMall.Survival
{
    /// <summary>
    /// Состояние игрока на картинке (постобработка URP): низкий рассудок — виньетка, зерно, расслоение цвета, бледность,
    /// дыхание линзы; ранение — красная вспышка; мало здоровья — красные края; глитч — резкий сбой картинки;
    /// переохлаждение — синева. Глобальный Volume создаётся в коде, камере включается постобработка.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class VitalsEffects : MonoBehaviour
    {
        private Volume _volume;
        private VolumeProfile _profile;
        private Vignette _vignette;
        private FilmGrain _grain;
        private ChromaticAberration _chromatic;
        private ColorAdjustments _color;
        private LensDistortion _lens;
        private Camera _camera;

        private void OnEnable()
        {
            _profile = ScriptableObject.CreateInstance<VolumeProfile>();
            _vignette = _profile.Add<Vignette>(true);
            _grain = _profile.Add<FilmGrain>(true);
            _chromatic = _profile.Add<ChromaticAberration>(true);
            _color = _profile.Add<ColorAdjustments>(true);
            _lens = _profile.Add<LensDistortion>(true);
            _grain.type.Override(FilmGrainLookup.Medium3);
            _vignette.smoothness.Override(0.8f);

            var go = new GameObject("Vitals Volume");
            go.transform.SetParent(transform, false);
            _volume = go.AddComponent<Volume>();
            _volume.isGlobal = true;
            _volume.priority = 10f;
            _volume.sharedProfile = _profile;
        }

        private void OnDisable()
        {
            if (_volume != null)
                Destroy(_volume.gameObject);
            if (_profile != null)
                Destroy(_profile);
        }

        private void LateUpdate()
        {
            var vitals = PlayerVitals.Local;
            EnsureCameraPostProcessing();
            if (vitals == null)
            {
                _volume.weight = 0f;
                return;
            }
            _volume.weight = 1f;
            var m = vitals.Model;
            float time = Time.unscaledTime;

            // Рассудок: эффекты нарастают ниже 60%.
            float madness = Mathf.Clamp01(1f - m.Sanity / 60f);
            // Здоровье: красные края ниже 35%, вспышка при уроне.
            float hurt = Mathf.Clamp01(1f - m.Health / Mathf.Max(1f, m.MaxHealth * 0.35f));
            float flash = Mathf.Clamp01(1f - (time - vitals.LastDamageTime) / 0.4f) * Mathf.Clamp01(vitals.LastDamage / 15f + 0.3f);
            float glitch = Mathf.Clamp01(1f - (time - vitals.LastGlitchTime) / 0.35f);
            float cold = Mathf.Clamp01((36f - m.BodyTemperature) / 1.5f);
            float pulse = 0.5f + 0.5f * Mathf.Sin(time * Mathf.Lerp(1.5f, 4f, madness));

            float red = Mathf.Max(hurt * (0.6f + 0.4f * pulse), flash);
            _vignette.intensity.Override(Mathf.Clamp01(0.18f * madness + 0.25f * madness * pulse + 0.45f * red + (vitals.IsDead ? 0.6f : 0f)));
            _vignette.color.Override(Color.Lerp(Color.black, new Color(0.55f, 0f, 0f), red));
            _grain.intensity.Override(Mathf.Clamp01(0.6f * madness + glitch));
            _chromatic.intensity.Override(Mathf.Clamp01(0.8f * madness * madness + glitch));
            _lens.intensity.Override(Mathf.Clamp(0.25f * madness * Mathf.Sin(time * 0.7f) - 0.5f * glitch, -1f, 1f));
            _color.saturation.Override(-60f * madness - 30f * cold - (vitals.IsDead ? 80f : 0f));
            Color filter = Color.Lerp(Color.white, new Color(0.8f, 0.9f, 1f), cold);
            // Под водой — зелёно-синий оттенок.
            if (_camera != null && LiquidProbe.At(_camera.transform.position))
                filter *= new Color(0.6f, 0.9f, 0.85f);
            _color.colorFilter.Override(filter);
            _color.postExposure.Override(glitch > 0.5f ? Random.Range(-1.5f, 1f) : 0f);
        }

        private void EnsureCameraPostProcessing()
        {
            var camera = Camera.main;
            if (camera == null || camera == _camera)
                return;
            _camera = camera;
            var data = camera.GetUniversalAdditionalCameraData();
            if (data != null)
                data.renderPostProcessing = true;
        }
    }
}
