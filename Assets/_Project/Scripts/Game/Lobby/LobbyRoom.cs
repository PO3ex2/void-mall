using UnityEngine;

namespace VoidMall.Game
{
    /// <summary>
    /// Комната лобби (этап 10) — корень префаба Lobby.prefab: точка появления, ПК и портал. Префаб создаёт генератор
    /// один раз и больше не трогает — его можно обставлять по-своему (главное — не терять эти ссылки).
    /// </summary>
    public sealed class LobbyRoom : MonoBehaviour
    {
        [SerializeField] private Transform _spawn;
        [SerializeField] private PcTerminal _terminal;
        [SerializeField] private PortalLink _portal;

        [Tooltip("Рамка портала с плоскостью «Surface»: её копия встаёт в мире выходом портала.")]
        [SerializeField] private GameObject _portalFrame;

        public Transform Spawn => _spawn;
        public PcTerminal Terminal => _terminal;
        public PortalLink Portal => _portal;
        public GameObject PortalFrame => _portalFrame;
    }
}
