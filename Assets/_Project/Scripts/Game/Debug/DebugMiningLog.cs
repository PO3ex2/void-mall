using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using VoidMall.Console;
using VoidMall.Core;
using VoidMall.World;

namespace VoidMall.Game
{
    /// <summary>
    /// Заглушка до инвентаря (этап 6): копит добытые материалы и обрушения и раз в пару секунд пишет итог в консоль.
    /// </summary>
    public sealed class DebugMiningLog : MonoBehaviour
    {
        public static readonly CVar<bool> Enabled = new("debug_mining_log", true, "Писать в консоль добытые материалы и обрушения.");

        private const float FlushInterval = 2f;

        private readonly Dictionary<ushort, float> _mined = new();
        private readonly List<string> _impacts = new();
        private float _timer;

        private void OnEnable()
        {
            EventBus<MaterialMinedEvent>.Subscribe(OnMined);
            EventBus<DebrisImpactEvent>.Subscribe(OnDebris);
        }

        private void OnDisable()
        {
            EventBus<MaterialMinedEvent>.Unsubscribe(OnMined);
            EventBus<DebrisImpactEvent>.Unsubscribe(OnDebris);
        }

        private void OnMined(in MaterialMinedEvent evt)
        {
            _mined.TryGetValue(evt.Material, out float amount);
            _mined[evt.Material] = amount + evt.Amount;
        }

        private void OnDebris(in DebrisImpactEvent evt) =>
            _impacts.Add($"Обрушение: обломок {evt.Mass:0} кг разбился на скорости {evt.Speed:0.0} м/с");

        private void Update()
        {
            _timer += Time.unscaledDeltaTime;
            if (_timer < FlushInterval)
                return;
            _timer = 0f;
            if (!Enabled.Value || (_mined.Count == 0 && _impacts.Count == 0))
            {
                _mined.Clear();
                _impacts.Clear();
                return;
            }

            foreach (var impact in _impacts)
                DevConsole.Warn(impact);
            _impacts.Clear();

            var world = ChunkManager.Instance;
            if (_mined.Count > 0 && world != null)
            {
                var sb = new StringBuilder("Добыто: ");
                sb.Append(string.Join(", ", _mined.Select(m =>
                {
                    var def = world.Materials.Get(m.Key);
                    return def.Kind == MaterialKind.Smooth ? $"{def.DisplayName} {m.Value:0.0} м³" : $"{def.DisplayName} ×{m.Value:0}";
                })));
                DevConsole.Log(sb.ToString());
            }
            _mined.Clear();
        }
    }
}
