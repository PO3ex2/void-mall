using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace VoidMall.World
{
    /// <summary>
    /// Физика аномалий для тел мира (GDD §2.5): перевёрнутая гравитация в колонне перевёрнутого чанка и пульсирующее
    /// искажение (тянет к центру и отталкивает). Пропы и предметы спрашивают ускорение каждый физический шаг, поэтому
    /// активные объёмы рядом с игроком собираются заранее (Refresh раз в четверть секунды) — без аномалий рядом
    /// проверка стоит один if.
    /// </summary>
    public static class AnomalyPhysics
    {
        /// <summary>Период пульсации: половину тянет, половину отталкивает (как VM_PULSE_PERIOD в шейдере).</summary>
        public const float PulsePeriod = 3f;

        /// <summary>Скорость, которую пульсация придаёт пропам и предметам в центре (без урона: не разгоняет сильнее).</summary>
        public const float PulseBodySpeed = 2.5f;

        /// <summary>Скорость сноса игрока в центре — слабее, чем у вещей: устоять можно.</summary>
        public const float PulsePlayerSpeed = 0.9f;

        /// <summary>Сколько ускорения пульсация может приложить к телу (м/с²): чтобы догонять целевую скорость.</summary>
        public const float PulseMaxAcceleration = 18f;

        /// <summary>Тяжелее этого (кг) тела пульсация тянет слабее — стеллаж только покачнётся, коробка поедет.</summary>
        public const float PulseHeavyMass = 15f;

        /// <summary>Пульсация действует на своём этаже: от центра вниз и вверх не дальше этого (центр — на 1.2 м над полом).</summary>
        public const float PulseBelow = 1.4f, PulseAbove = 3.6f;

        private const float RefreshInterval = 0.25f;

        private static readonly List<AnomalyMarker> Pulses = new();
        private static readonly List<float4> InvertedColumns = new();
        private static float _nextRefresh;

        /// <summary>Есть ли рядом хоть один объём (иначе тела ничего не считают).</summary>
        public static bool Any => Pulses.Count > 0 || InvertedColumns.Count > 0;

        public static IReadOnlyList<AnomalyMarker> NearbyPulses => Pulses;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Pulses.Clear();
            InvertedColumns.Clear();
            _nextRefresh = 0f;
        }

        /// <summary>Собрать пульсации и перевёрнутые колонны в окне 3×3 ячеек вокруг точки.</summary>
        public static void Refresh(ChunkManager world, Vector3 viewer, bool force = false)
        {
            if (!force && Time.time < _nextRefresh)
                return;
            _nextRefresh = Time.time + RefreshInterval;
            Pulses.Clear();
            InvertedColumns.Clear();
            if (world == null)
                return;
            int2 center = MallLayout.CellOf(viewer);
            for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                var info = world.GetCellInfo(center + new int2(dx, dz));
                if (info.Anomaly == AnomalyKind.Inverted)
                    InvertedColumns.Add(new float4(info.AnomalyCenter, info.AnomalyCenter + WorldConstants.ChunkSize));
                for (int i = 0; i < info.MarkerCount; i++)
                {
                    var marker = info.GetMarker(i);
                    if (marker.Kind == AnomalyMarkerKind.Pulse)
                        Pulses.Add(marker);
                }
            }
        }

        /// <summary>Точка в колонне перевёрнутого чанка (между дном мира и кровлей).</summary>
        public static bool InInverted(Vector3 position)
        {
            if (position.y >= MallLayout.RoofY || position.y < MallLayout.LevelBase(0))
                return false;
            for (int i = 0; i < InvertedColumns.Count; i++)
            {
                float4 r = InvertedColumns[i];
                if (position.x >= r.x && position.x < r.z && position.z >= r.y && position.z < r.w)
                    return true;
            }
            return false;
        }

        /// <summary>Фаза пульсации: &gt; 0 — тянет к центру, &lt; 0 — отталкивает.</summary>
        public static float PulsePhase(float time) => math.sin(time * 2f * math.PI / PulsePeriod);

        /// <summary>
        /// Скорость, которую пульсация хочет придать в точке: к центру или от центра, сильнее у центра, по горизонтали
        /// (вертикальная тяга подбрасывала бы вещи).
        /// </summary>
        public static Vector3 PulseVelocity(Vector3 position, float time, float speed)
        {
            Vector3 sum = Vector3.zero;
            float phase = PulsePhase(time);
            for (int i = 0; i < Pulses.Count; i++)
                sum += PulseVelocity(Pulses[i], position, phase, speed);
            return sum;
        }

        public static Vector3 PulseVelocity(in AnomalyMarker pulse, Vector3 position, float phase, float speed)
        {
            Vector3 toCenter = (Vector3)pulse.Center - position;
            if (toCenter.y > PulseBelow || toCenter.y < -PulseAbove)
                return Vector3.zero;
            toCenter.y = 0f;
            float distance = toCenter.magnitude;
            if (distance >= pulse.Radius || distance < 0.05f)
                return Vector3.zero;
            // Плавно к краю и у самого центра (иначе вещи дрожат на месте).
            float falloff = math.smoothstep(pulse.Radius, pulse.Radius * 0.4f, distance) * math.saturate(distance / 0.6f);
            return toCenter / distance * (speed * falloff * phase);
        }

        /// <summary>
        /// Применить аномалии к телу: в перевёрнутой колонне гравитация вверх, в пульсации — тяга к целевой скорости.
        /// Возвращает true, если пульсация действует (тело надо держать проснувшимся).
        /// baseY — низ тела: тягу пульсации прикладываем у основания, иначе трение о пол опрокидывает высокую мебель.
        /// </summary>
        public static bool Apply(Rigidbody body, float time, float baseY = float.NaN)
        {
            if (body == null || body.isKinematic || !Any)
                return false;
            Vector3 position = body.worldCenterOfMass;
            if (body.useGravity && InInverted(position))
                body.AddForce(-2f * Physics.gravity, ForceMode.Acceleration);
            if (Pulses.Count == 0)
                return false;
            Vector3 target = PulseVelocity(position, time, PulseBodySpeed);
            if (target.sqrMagnitude < 1e-4f)
                return false;
            Vector3 velocity = body.linearVelocity;
            Vector3 change = target - new Vector3(velocity.x, 0f, velocity.z);
            float cap = PulseMaxAcceleration * Mathf.Clamp(PulseHeavyMass / Mathf.Max(body.mass, 0.01f), 0.15f, 1f);
            // Резкий разгон до целевой скорости: иначе трение покоя держит вещь на месте при слабой фазе.
            Vector3 acceleration = Vector3.ClampMagnitude(change * 20f, cap);
            if (float.IsNaN(baseY))
                body.AddForce(acceleration, ForceMode.Acceleration);
            else
                body.AddForceAtPosition(acceleration * body.mass, new Vector3(position.x, Mathf.Min(baseY + 0.05f, position.y), position.z), ForceMode.Force);
            return true;
        }
    }
}
