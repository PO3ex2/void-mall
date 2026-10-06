using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using VoidMall.Console;
using VoidMall.Core;

namespace VoidMall.World
{
    /// <summary>
    /// Передаёт шейдерам блоков и пропов ближайшие объёмы-маркеры аномалий (искажение, глитч, пульсация,
    /// «Смотри на меня»), обновляет физику аномалий рядом (AnomalyPhysics) и будит пропы в пульсации.
    /// Маркеры выводятся из плана макроячеек, поэтому ничего не хранится.
    /// </summary>
    public sealed class AnomalyFxDirector : MonoBehaviour
    {
        public static readonly CVar<float> Strength = new("fx_anomaly_strength", 1f, "Сила эффектов аномалий (0 — выключить).", ConsoleFlags.Archive);

        private const int MaxVolumes = 8;

        private static readonly int AnomaliesId = Shader.PropertyToID("_VM_Anomalies");
        private static readonly int ParamsId = Shader.PropertyToID("_VM_AnomalyParams");
        private static readonly int CountId = Shader.PropertyToID("_VM_AnomalyCount");

        private readonly Vector4[] _volumes = new Vector4[MaxVolumes];
        private readonly Vector4[] _params = new Vector4[MaxVolumes];
        private readonly List<(float distance, AnomalyMarker marker)> _found = new();
        private readonly Collider[] _overlap = new Collider[64];
        private float _wakeTimer;

        /// <summary>Сила глитча в точке камеры (для эффектов камеры и звука позже).</summary>
        public static float GlitchAtCamera { get; private set; }

        private void OnDisable() => Shader.SetGlobalFloat(CountId, 0f);

        private static readonly Perf.Probe UpdateProbe = Perf.Register("World.AnomalyFx");

        private void LateUpdate()
        {
            using var perf = Perf.Measure(UpdateProbe);
            var world = ChunkManager.Instance;
            var camera = Camera.main;
            if (world == null || camera == null)
                return;

            Vector3 eye = camera.transform.position;
            AnomalyPhysics.Refresh(world, eye);
            WakePulsedProps();
            int2 center = MallLayout.CellOf(eye);
            _found.Clear();
            for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                var info = world.GetCellInfo(center + new int2(dx, dz));
                for (int i = 0; i < info.MarkerCount; i++)
                {
                    var marker = info.GetMarker(i);
                    if (marker.Kind == AnomalyMarkerKind.Puddle)
                        continue;
                    float distance = math.distance((float3)eye, marker.Center) - marker.Radius;
                    _found.Add((distance, marker));
                }
            }
            _found.Sort((a, b) => a.distance.CompareTo(b.distance));

            int count = Mathf.Min(_found.Count, MaxVolumes);
            GlitchAtCamera = 0f;
            for (int i = 0; i < count; i++)
            {
                var marker = _found[i].marker;
                _volumes[i] = new Vector4(marker.Center.x, marker.Center.y, marker.Center.z, marker.Radius);
                _params[i] = new Vector4((float)marker.Kind, Strength.Value, 0f, 0f);
                if (marker.Kind == AnomalyMarkerKind.Glitch)
                {
                    float f = Mathf.Clamp01(1f - Vector3.Distance(eye, marker.Center) / marker.Radius);
                    GlitchAtCamera = Mathf.Max(GlitchAtCamera, f * Strength.Value);
                }
            }
            Shader.SetGlobalVectorArray(AnomaliesId, _volumes);
            Shader.SetGlobalVectorArray(ParamsId, _params);
            Shader.SetGlobalFloat(CountId, Strength.Value > 0f ? count : 0);
        }

        /// <summary>Спящие пропы в пульсации просыпаются (дальше их таскает AnomalyPhysics, пока они в объёме).</summary>
        private void WakePulsedProps()
        {
            _wakeTimer -= Time.deltaTime;
            if (_wakeTimer > 0f)
                return;
            _wakeTimer = 0.3f;
            foreach (var pulse in AnomalyPhysics.NearbyPulses)
            {
                int count = Physics.OverlapSphereNonAlloc(pulse.Center, pulse.Radius, _overlap, 1 << GameLayers.Props, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < count; i++)
                    if (PropManager.TryGetProp(_overlap[i], out var prop) && !prop.Root.IsAwake && !prop.IsNailed && !prop.Root.IsHeld
                        && AnomalyPhysics.PulseVelocity(pulse, prop.Root.transform.position + Vector3.up * 0.3f, 1f, 1f) != Vector3.zero)
                        prop.Wake();
            }
        }
    }
}
