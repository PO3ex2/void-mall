using System;
using System.Collections.Generic;
using UnityEngine;
using VoidMall.Core;
using VoidMall.Items;

namespace VoidMall.Crafting
{
    /// <summary>
    /// Состояние станции (по ID пропа): своя очередь, выходная сетка с готовым, топливо (плита, горелка), кто
    /// сейчас работает за станцией. Живёт в CraftingSystem и переживает выгрузку пропа (плита готовит и без игрока).
    /// </summary>
    public sealed class StationState
    {
        public const int OutputWidth = 6, OutputHeight = 3;

        /// <summary>Сколько секунд топлива можно накопить.</summary>
        public const float MaxFuel = 1200f;

        public readonly ulong PropId;
        public readonly CraftStation Kind;
        public readonly string Name;
        public readonly float Speed;
        public readonly bool Autonomous;
        public readonly CraftQueue Queue = new();
        public readonly GridInventory Output;

        /// <summary>Секунды топлива (тратится только пока идёт готовка).</summary>
        public float Fuel;

        /// <summary>Кто работает за станцией (окно открыто). Второму игроку — «Станция занята».</summary>
        public GameObject Occupant { get; internal set; }

        /// <summary>Где стоит (для выпадения вещей, если станцию сломали).</summary>
        public Vector3 Position;

        public bool NeedsFuel => Kind == CraftStation.Cooking;

        /// <summary>Горит, пока есть топливо, даже без готовки (бочка-костёр).</summary>
        public bool Heater;

        /// <summary>Тепло, пока горит: прибавка вплотную (°C) и радиус (м).</summary>
        public float Heat, HeatRadius;

        /// <summary>Горела в последнем кадре (готовка или костёр).</summary>
        public bool Burning { get; internal set; }

        /// <summary>Готовка идёт: есть очередь и (для плиты) топливо.</summary>
        public bool Working => !Queue.IsEmpty && !Queue.Blocked && (!NeedsFuel || Fuel > 0f);

        public event Action Changed;

        public StationState(ulong propId, CraftStation kind, string name, float speed, bool autonomous, IItemCatalog catalog)
        {
            PropId = propId;
            Kind = kind;
            Name = name;
            Speed = Mathf.Max(0.1f, speed);
            Autonomous = autonomous;
            Output = new GridInventory(catalog, OutputWidth, OutputHeight);
            Queue.Changed += RaiseChanged;
            Output.Changed += RaiseChanged;
        }

        public void RaiseChanged() => Changed?.Invoke();

        /// <summary>Положить результаты в выдачу целиком (или ничего).</summary>
        public bool TryDeliver(CraftJob job)
        {
            // Проверяем место на копиях, чтобы не выдать половину.
            var probe = new GridInventory(Output.Catalog, OutputWidth, OutputHeight);
            foreach (var placement in Output.Items)
                probe.TryPlace(new ItemStack(placement.Stack.Def, placement.Stack.Count), placement.X, placement.Y, placement.Rotated);
            foreach (var stack in job.Results)
                if (probe.TryAdd(new ItemStack(stack.Def, stack.Count)) > 0)
                    return false;
            foreach (var stack in job.Results)
                Output.TryAdd(stack);
            return true;
        }
    }
}
