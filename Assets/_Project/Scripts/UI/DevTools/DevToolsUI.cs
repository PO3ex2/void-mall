using UnityEngine;
using UnityEngine.UIElements;
using VoidMall.Input;

namespace VoidMall.UI
{
    /// <summary>Создаёт отладочные панели (консоль, HUD, модификаторы) на общем PanelSettings.</summary>
    public static class DevToolsUI
    {
        public static void Create(Transform parent, PanelSettings panelSettings, InputService input)
        {
            CreateDocument<DebugHud>("DebugHud", parent, panelSettings, 10).Initialize(input);
            CreateDocument<DebugModifiersPanel>("DebugModifiersPanel", parent, panelSettings, 20).Initialize(input);
            CreateDocument<MapPanel>("MapPanel", parent, panelSettings, 30).Initialize(input);
            CreateDocument<ConsoleOverlay>("ConsoleOverlay", parent, panelSettings, 100).Initialize(input);
        }

        private static T CreateDocument<T>(string name, Transform parent, PanelSettings panelSettings, int sortingOrder)
            where T : Component
        {
            // Документ собираем на выключенном объекте, чтобы UIDocument включился уже с PanelSettings.
            var go = new GameObject(name);
            go.SetActive(false);
            go.transform.SetParent(parent, false);
            var document = go.AddComponent<UIDocument>();
            document.panelSettings = panelSettings;
            document.sortingOrder = sortingOrder;
            var component = go.AddComponent<T>();
            go.SetActive(true);
            return component;
        }
    }
}
