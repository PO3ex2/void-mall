using UnityEngine;
using VoidMall.Console;
using VoidMall.Core;

namespace VoidMall.World
{
    /// <summary>
    /// Игровые часы: время суток и номер дня. Авторитетный сервис (в кооперативе время шлёт хост).
    /// Длительность суток — из WorldSettings, меняется переменной time_daylength.
    /// </summary>
    [DefaultExecutionOrder(-150)]
    public sealed class WorldClock : MonoBehaviour, IWorldClock
    {
        public static readonly CVar<float> DayLength = new("time_daylength", 60f, "Минут реального времени на игровые сутки.");
        public static readonly CVar<bool> Frozen = new("time_frozen", false, "Остановить игровое время.", ConsoleFlags.Cheat);

        [SerializeField] private WorldSettings _settings;

        public static WorldClock Instance { get; private set; }

        /// <summary>Игровое время с начала мира, часы.</summary>
        public double TotalHours { get; private set; }

        /// <summary>Ускорение времени (сон в одиночной игре). 1 — обычный ход.</summary>
        public float TimeScale { get; set; } = 1f;

        public float Hour => (float)(TotalHours % 24.0);
        public int Day => (int)(TotalHours / 24.0) + 1;
        public bool IsNight => LampRules.Wave(Hour) > 0.5f;

        private void Awake()
        {
            Instance = this;
            if (_settings != null)
            {
                DayLength.Value = _settings.DayLengthMinutes;
                TotalHours = _settings.StartHour;
            }
            else
            {
                TotalHours = 9.0;
            }
        }

        private void OnEnable() => Services.Register<IWorldClock>(this);

        private void OnDisable() => Services.Unregister<IWorldClock>(this);

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        private void Update()
        {
            if (Frozen.Value)
                return;
            float minutes = Mathf.Max(0.1f, DayLength.Value);
            TotalHours += Time.deltaTime * 24.0 / (minutes * 60.0) * TimeScale;
        }

        public void SetHour(float hour)
        {
            double day = System.Math.Floor(TotalHours / 24.0);
            TotalHours = day * 24.0 + Mathf.Repeat(hour, 24f);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Instance = null;

        [ConsoleCommand("time", "Игровое время. time <часы> — установить (например, time 22.5).", Flags = ConsoleFlags.None)]
        private static string Time_(string hour = "")
        {
            if (Instance == null)
                return "В сцене нет WorldClock.";
            if (!string.IsNullOrEmpty(hour))
            {
                if (!DevConsole.Cheats.Value)
                    return "Менять время можно только с sv_cheats 1.";
                if (!float.TryParse(hour.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float value))
                    return $"Не число: {hour}";
                Instance.SetHour(value);
            }
            float h = Instance.Hour;
            return $"День {Instance.Day}, {(int)h:00}:{(int)(h % 1f * 60f):00}{(Instance.IsNight ? " (ночь)" : "")}, волна отключения {LampRules.Wave(h):0.00}";
        }
    }
}
