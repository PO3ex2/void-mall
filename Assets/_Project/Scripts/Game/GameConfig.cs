using UnityEngine;
using UnityEngine.UIElements;

namespace VoidMall.Game
{
    /// <summary>
    /// Глобальные ссылки на ассеты, нужные при старте. Лежит в Resources/VoidMall/GameConfig.
    /// </summary>
    [CreateAssetMenu(menuName = "Void Mall/Game Config", fileName = "GameConfig")]
    public sealed class GameConfig : ScriptableObject
    {
        public const string ResourcePath = "VoidMall/GameConfig";

        [Tooltip("Общие PanelSettings для экранного UI Toolkit.")]
        public PanelSettings PanelSettings;

        [Tooltip("Частота симуляции, Гц.")]
        [Min(10)] public int TickRate = 60;
    }
}
