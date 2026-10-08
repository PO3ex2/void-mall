using UnityEngine;

namespace VoidMall.Game
{
    /// <summary>
    /// Звуки ПК, синтезированные в коде (этап 10): мягкий аккорд включения в духе старого Macintosh (свой — фа-мажор с
    /// октавой, медленное затухание), щелчок кнопки питания и тихий гул ЭЛТ. Можно заменить своими клипами.
    /// </summary>
    public static class PcSounds
    {
        private const int Rate = 44100;

        private static AudioClip _chime, _click, _hum;

        public static AudioClip Chime => _chime ??= Create("PC Chime", 2.8f, Chord);
        public static AudioClip Click => _click ??= Create("PC Click", 0.06f, PowerClick);
        public static AudioClip Hum => _hum ??= Create("PC Hum", 2f, CrtHum);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _chime = _click = _hum = null;

        private delegate float Sample(float t, float duration);

        private static AudioClip Create(string name, float seconds, Sample sample)
        {
            int count = Mathf.CeilToInt(seconds * Rate);
            var data = new float[count];
            for (int i = 0; i < count; i++)
                data[i] = sample(i / (float)Rate, seconds);
            var clip = AudioClip.Create(name, count, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>Аккорд: F3, A3, C4, F4 с лёгкой расстройкой, мягкая атака, экспоненциальный спад.</summary>
        private static float Chord(float t, float duration)
        {
            float[] notes = { 174.61f, 220.00f, 261.63f, 349.23f };
            float value = 0f;
            for (int n = 0; n < notes.Length; n++)
            {
                float f = notes[n];
                float phase = 2f * Mathf.PI * f * t;
                // Чуть расстроенная пара голосов и вторая гармоника — тёплый, «электрический» тембр.
                value += Mathf.Sin(phase) + 0.6f * Mathf.Sin(phase * 1.003f) + 0.18f * Mathf.Sin(phase * 2f);
            }
            float attack = Mathf.Clamp01(t / 0.025f);
            float decay = Mathf.Exp(-t * 1.6f);
            float tail = Mathf.Clamp01((duration - t) / 0.2f);
            return value / (notes.Length * 1.8f) * attack * decay * tail * 0.6f;
        }

        private static float PowerClick(float t, float duration)
        {
            float noise = Mathf.PerlinNoise(t * 9000f, 0.37f) * 2f - 1f;
            return noise * Mathf.Exp(-t * 120f) * 0.5f;
        }

        /// <summary>Гул ЭЛТ: 50 Гц с гармониками, очень тихо (зациклен — длина кратна периоду).</summary>
        private static float CrtHum(float t, float duration)
        {
            float phase = 2f * Mathf.PI * 50f * t;
            return (Mathf.Sin(phase) * 0.5f + Mathf.Sin(phase * 2f) * 0.3f + Mathf.Sin(phase * 3f) * 0.12f) * 0.05f;
        }
    }
}
