using UnityEngine;
using UnityEngine.InputSystem;
using VoidMall.Console;
using VoidMall.Core;
using VoidMall.Input;
using VoidMall.Net;
using VoidMall.UI;

namespace VoidMall.Game
{
    /// <summary>
    /// Composition root: поднимает сервисы до загрузки первой сцены. Работает при запуске любой сцены
    /// (Boot, Sandbox, тесты), поэтому отдельная загрузочная сцена для отладки не нужна.
    /// </summary>
    public static class GameBootstrap
    {
        public static GameObject Root { get; private set; }
        public static GameConfig Config { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Boot()
        {
            Config = Resources.Load<GameConfig>(GameConfig.ResourcePath);
            if (Config == null)
            {
                Debug.LogError($"[Bootstrap] GameConfig not found at Resources/{GameConfig.ResourcePath}. Run 'Void Mall/Setup/Project Assets'.");
                return;
            }

            Time.fixedDeltaTime = 1f / Config.TickRate;

            Root = new GameObject("[VoidMall]");
            Object.DontDestroyOnLoad(Root);

            Services.Register<INetSession>(new LocalSession());

            if (InputSystem.actions == null)
            {
                Debug.LogError("[Bootstrap] Project-wide input actions are not set.");
                return;
            }
            KeyBindings.Load(InputSystem.actions);
            var input = new InputService(InputSystem.actions);
            Services.Register(input);

            DevConsole.Initialize();

            Root.AddComponent<DebugMiningLog>();
            Root.AddComponent<PlayerFallGuard>();
            Root.AddComponent<SaveSystem>();
            Root.AddComponent<GameFlow>();
            Root.AddComponent<WorldTerminals>();
            if (Config.PanelSettings != null)
            {
                var pause = new GameObject("PauseMenu");
                pause.SetActive(false);
                pause.transform.SetParent(Root.transform, false);
                var document = pause.AddComponent<UnityEngine.UIElements.UIDocument>();
                document.panelSettings = Config.PanelSettings;
                document.sortingOrder = 90;
                var menu = pause.AddComponent<PauseMenu>();
                pause.SetActive(true);
                menu.Initialize(input);
            }
            PcSettingsApp.ApplyVolume();

            if (Config.PanelSettings != null)
                DevToolsUI.Create(Root.transform, Config.PanelSettings, input);
            else
                Debug.LogError("[Bootstrap] GameConfig.PanelSettings is not assigned.");
        }
    }
}
