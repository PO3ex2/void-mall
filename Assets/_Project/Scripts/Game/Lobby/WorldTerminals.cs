using System.Collections.Generic;
using UnityEngine;
using VoidMall.World;

namespace VoidMall.Game
{
    /// <summary>
    /// ПК в мире (этап 10): на каждый проп «Компьютер» вешает экран и терминал (та же ОС, что в лобби, плюс «В лобби»),
    /// при выгрузке пропа — снимает (тела пропов идут в пул и потом становятся другой мебелью). Часть ПК сломана —
    /// это решает ID пропа, так что после загрузки сломаны те же самые.
    /// </summary>
    public sealed class WorldTerminals : MonoBehaviour
    {
        public const string DefId = "prop.pc_desk";

        /// <summary>Доля сломанных ПК в мире.</summary>
        public const float BrokenShare = 0.3f;

        /// <summary>Экран ЭЛТ-монитора в осях пропа (PropShape.PcDesk): квад смотрит в +Z, к сидящему за столом.</summary>
        private static readonly Vector3 ScreenPosition = new(0f, 0.955f, 0.162f);
        private static readonly Vector3 ScreenScale = new(0.34f, 0.255f, 1f);

        private readonly Dictionary<PropBody, (PcTerminal Terminal, GameObject Screen)> _attached = new();
        private PropManager _props;
        private Texture2D _logo;

        private void Awake() => ChunkManager.Starting += OnWorldStarting;

        private void OnDestroy()
        {
            ChunkManager.Starting -= OnWorldStarting;
            Unsubscribe();
        }

        private void OnWorldStarting(ChunkManager world)
        {
            Unsubscribe();
            _attached.Clear();
            _props = PropManager.Instance;
            if (_props == null)
                return;
            var lobby = FindAnyObjectByType<LobbyRoom>(FindObjectsInactive.Include);
            _logo = lobby != null && lobby.Terminal != null ? lobby.Terminal.Logo : null;
            _props.PropSpawned += OnPropSpawned;
            _props.PropDespawning += OnPropDespawning;
        }

        private void Unsubscribe()
        {
            if (_props == null)
                return;
            _props.PropSpawned -= OnPropSpawned;
            _props.PropDespawning -= OnPropDespawning;
            _props = null;
        }

        public static bool IsBroken(ulong propId) => (propId * 0x9E3779B97F4A7C15UL >> 40) % 1000 < BrokenShare * 1000f;

        private void OnPropSpawned(PropBody prop)
        {
            if (prop.Def == null || prop.Def.Id != DefId || _attached.ContainsKey(prop))
                return;
            var screen = GameObject.CreatePrimitive(PrimitiveType.Quad);
            screen.name = "PC Screen";
            // Коллайдер экрана не нужен (точку на экране считает PcTerminal по плоскости), а невыпуклый меш-коллайдер
            // на динамическом теле пропа Unity не принимает.
            DestroyImmediate(screen.GetComponent<Collider>());
            screen.transform.SetParent(prop.transform, false);
            screen.transform.localPosition = ScreenPosition;
            screen.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            screen.transform.localScale = ScreenScale;
            var terminal = prop.gameObject.AddComponent<PcTerminal>();
            terminal.Configure(screen.GetComponent<Renderer>(), null, null, _logo, inWorld: true, broken: IsBroken(prop.Id));
            _attached[prop] = (terminal, screen);
        }

        private void OnPropDespawning(PropBody prop)
        {
            if (!_attached.Remove(prop, out var attached))
                return;
            // Сразу, а не в конце кадра: тело уходит в пул и может тут же стать другим пропом.
            if (attached.Terminal != null)
                DestroyImmediate(attached.Terminal);
            if (attached.Screen != null)
                DestroyImmediate(attached.Screen);
        }
    }
}
