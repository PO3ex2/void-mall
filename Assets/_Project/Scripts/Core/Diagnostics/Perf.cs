using System;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.Profiling;
using UnityEngine.Profiling;

namespace VoidMall.Core
{
    /// <summary>
    /// Замеры систем игры: сколько времени и памяти (мусора для GC) система потратила за кадр. Память — по росту
    /// управляемой кучи (GC.GetAllocatedBytesForCurrentThread в Unity всегда 0), с точностью до блоков кучи.
    /// Метка заводится один раз (<see cref="Register"/>) и оборачивает код: <c>using var _ = Perf.Measure(Probe);</c>.
    /// Каждая метка видна и в Unity Profiler под своим именем. Сводку по кадрам собирает PerfMonitor (Game).
    /// Вложенные метки входят во время и память внешних — в отчёте это видно по иерархии имён.
    /// </summary>
    public static class Perf
    {
        public sealed class Probe
        {
            public readonly string Name;
            internal readonly ProfilerMarker Marker;

            /// <summary>Накоплено за текущий кадр.</summary>
            public long FrameTicks, FrameBytes;
            public int FrameCalls;

            internal Probe(string name)
            {
                Name = name;
                Marker = new ProfilerMarker(name);
            }
        }

        private static readonly List<Probe> _probes = new();

        public static IReadOnlyList<Probe> Probes => _probes;

        public static Probe Register(string name)
        {
            foreach (var probe in _probes)
                if (probe.Name == name)
                    return probe;
            var created = new Probe(name);
            _probes.Add(created);
            return created;
        }

        public static Scope Measure(Probe probe) => new(probe);

        public readonly struct Scope : IDisposable
        {
            private readonly Probe _probe;
            private readonly long _start, _bytes;

            public Scope(Probe probe)
            {
                _probe = probe;
                probe.Marker.Begin();
                _bytes = Profiler.GetMonoUsedSizeLong();
                _start = Stopwatch.GetTimestamp();
            }

            public void Dispose()
            {
                _probe.FrameTicks += Stopwatch.GetTimestamp() - _start;
                // Сборка мусора внутри замера уменьшает кучу — такой замер памяти не считаем.
                _probe.FrameBytes += Math.Max(0L, Profiler.GetMonoUsedSizeLong() - _bytes);
                _probe.FrameCalls++;
                _probe.Marker.End();
            }
        }

        /// <summary>Начать новый кадр: обнулить накопленное.</summary>
        public static void ResetFrame()
        {
            foreach (var probe in _probes)
            {
                probe.FrameTicks = 0;
                probe.FrameBytes = 0;
                probe.FrameCalls = 0;
            }
        }

        public static double TicksToMs(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;
    }
}
