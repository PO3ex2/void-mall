using UnityEngine;
using VoidMall.Console;

namespace VoidMall.Game
{
    internal static class GameCommands
    {
        [ConsoleCommand("quit", "Выйти из игры.")]
        private static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        [ConsoleCommand("time_scale", "Скорость времени (1 — норма).", Flags = ConsoleFlags.Cheat)]
        private static string TimeScale(float scale)
        {
            Time.timeScale = Mathf.Clamp(scale, 0f, 10f);
            return $"time_scale {Time.timeScale:0.##}";
        }

        [ConsoleCommand("fps_max", "Ограничение FPS (0 — без ограничения).")]
        private static string FpsMax(int fps)
        {
            Application.targetFrameRate = fps <= 0 ? -1 : fps;
            return $"fps_max {fps}";
        }

        [ConsoleCommand("vsync", "Вертикальная синхронизация (0/1).")]
        private static string VSync(bool enabled)
        {
            QualitySettings.vSyncCount = enabled ? 1 : 0;
            return $"vsync {(enabled ? 1 : 0)}";
        }

        [ConsoleCommand("version", "Версия игры и движка.")]
        private static string Version() => $"{Application.productName} {Application.version} (Unity {Application.unityVersion})";
    }
}
