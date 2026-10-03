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
        private readonly List<string> _salvaged = new();
        private float _timer;

        private void OnEnable()
        {
            EventBus<MaterialMinedEvent>.Subscribe(OnMined);
            EventBus<DebrisImpactEvent>.Subscribe(OnDebris);
            EventBus<PropSalvagedEvent>.Subscribe(OnSalvaged);
        }

        private void OnDisable()
        {
            EventBus<MaterialMinedEvent>.Unsubscribe(OnMined);
            EventBus<DebrisImpactEvent>.Unsubscribe(OnDebris);
            EventBus<PropSalvagedEvent>.Unsubscribe(OnSalvaged);
        }

        private void OnMined(in MaterialMinedEvent evt)
        {
            _mined.TryGetValue(evt.Material, out float amount);
            _mined[evt.Material] = amount + evt.Amount;
        }

        /// <summary>Названия материалов из пропов — до появления предметов (этап 6).</summary>
        private static readonly Dictionary<string, string> MaterialNames = new()
        {
            ["material.scrap_metal"] = "металлолом",
            ["material.screws"] = "шурупы",
            ["material.plank"] = "доски",
            ["material.glass_shard"] = "стекло",
            ["material.plastic"] = "пластик",
            ["material.cardboard"] = "картон",
            ["material.fabric"] = "ткань",
            ["material.wire"] = "провод",
            ["material.rubber"] = "резина",
            ["material.foam"] = "поролон",
            ["material.pipe"] = "трубы",
        };

        private void OnSalvaged(in PropSalvagedEvent evt)
        {
            var parts = new List<string>();
            foreach (var yield in evt.Def.Yields)
            {
                int amount = evt.Disassembled ? yield.Disassembled : yield.Broken;
                if (amount <= 0)
                    continue;
                string name = MaterialNames.TryGetValue(yield.Material, out var n) ? n : yield.Material;
                parts.Add($"{name} ×{amount}");
            }
            string verb = evt.Disassembled ? "Разобрано" : "Сломано";
            _salvaged.Add($"{verb}: {evt.Def.DisplayName} → {(parts.Count > 0 ? string.Join(", ", parts) : "ничего полезного")}");
        }

        private void OnDebris(in DebrisImpactEvent evt) =>
            _impacts.Add($"Обрушение: обломок {evt.Mass:0} кг разбился на скорости {evt.Speed:0.0} м/с");

        private void Update()
        {
            _timer += Time.unscaledDeltaTime;
            if (_timer < FlushInterval)
                return;
            _timer = 0f;
            if (!Enabled.Value || (_mined.Count == 0 && _impacts.Count == 0 && _salvaged.Count == 0))
            {
                _mined.Clear();
                _impacts.Clear();
                _salvaged.Clear();
                return;
            }

            foreach (var line in _salvaged)
                DevConsole.Log(line);
            _salvaged.Clear();
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
