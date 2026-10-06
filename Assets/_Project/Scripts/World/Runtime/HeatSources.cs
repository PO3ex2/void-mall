using System.Collections.Generic;
using UnityEngine;

namespace VoidMall.World
{
    /// <summary>
    /// Горящие источники тепла (бочка-костёр, плита и горелка во время готовки): кто горит, каждый кадр сообщает о
    /// себе (<see cref="Report"/>), выживание спрашивает тепло в точке (<see cref="HeatAt"/>). Источник, не
    /// сообщавший о себе дольше Timeout, считается погасшим — выгруженная станция не греет.
    /// </summary>
    public static class HeatSources
    {
        private const float Timeout = 1f;

        private struct Source
        {
            public Vector3 Position;
            public float Heat, Radius, Time;
        }

        private static readonly Dictionary<ulong, Source> Sources = new();
        private static readonly List<ulong> Stale = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Sources.Clear();

        /// <param name="key">ID пропа-источника.</param>
        /// <param name="heat">Прибавка к температуре вплотную, °C.</param>
        /// <param name="radius">Докуда тепло доходит (линейно до нуля), м.</param>
        public static void Report(ulong key, Vector3 position, float heat, float radius) =>
            Sources[key] = new Source { Position = position, Heat = heat, Radius = Mathf.Max(0.1f, radius), Time = Time.time };

        public static bool IsBurning(ulong key) => Sources.TryGetValue(key, out var source) && Time.time - source.Time <= Timeout;

        /// <summary>Сумма тепла горящих источников в точке, °C.</summary>
        public static float HeatAt(Vector3 position)
        {
            float now = Time.time, sum = 0f;
            Stale.Clear();
            foreach (var pair in Sources)
            {
                var source = pair.Value;
                if (now - source.Time > Timeout)
                {
                    Stale.Add(pair.Key);
                    continue;
                }
                float closeness = 1f - Mathf.Clamp01(Vector3.Distance(source.Position, position) / source.Radius);
                sum += source.Heat * closeness;
            }
            foreach (var key in Stale)
                Sources.Remove(key);
            return sum;
        }
    }
}
