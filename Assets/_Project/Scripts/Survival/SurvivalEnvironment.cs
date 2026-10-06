using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using VoidMall.Core;
using VoidMall.World;

namespace VoidMall.Survival
{
    /// <summary>
    /// Что вокруг игрока для выживания: температура воздуха (этаж, раздел, время суток, холодильники и бойлеры рядом,
    /// аномалии), нахождение в аномалии и в глитче. Планы уровней кэшируются по ячейке.
    /// </summary>
    public sealed class SurvivalEnvironment
    {
        private const float NearPropRadius = 3f;

        private readonly Collider[] _nearby = new Collider[32];
        private MallLevelPlan _plan;
        private int2 _planCell;
        private int _planLevel = -1;

        public void Dispose()
        {
            if (_plan.IsCreated)
                _plan.Dispose();
        }

        /// <summary>
        /// Ночной холод (GDD §4.1): 0 — день, 1 — ночь. Холодает с 19 до 23, теплеет с 4 до 8 (плавно).
        /// </summary>
        public static float NightFactor(float hour)
        {
            hour = Mathf.Repeat(hour, 24f);
            if (hour >= 23f || hour < 4f)
                return 1f;
            if (hour >= 19f)
                return Mathf.SmoothStep(0f, 1f, (hour - 19f) / 4f);
            if (hour < 8f)
                return Mathf.SmoothStep(1f, 0f, (hour - 4f) / 4f);
            return 0f;
        }

        /// <summary>Воздух уровня днём и ночью, °C: торговые 21 → 3, парковка 12 → −3, техэтаж 24 → 12.</summary>
        public static float LevelTemperature(LevelKind kind, float night) => kind switch
        {
            LevelKind.Parking => Mathf.Lerp(12f, -3f, night),
            LevelKind.Tech => Mathf.Lerp(24f, 12f, night),
            _ => Mathf.Lerp(21f, 3f, night),
        };

        /// <summary>Температура воздуха, °C. night — ночной холод 0…1 (<see cref="NightFactor"/>).</summary>
        public float AirTemperature(ChunkManager world, Vector3 position, float night)
        {
            if (world == null || world.Sections == null)
                return 20f;
            int level = MallLayout.LevelAtFeet(position.y + 0.1f);
            if (level < 0 || level >= MallLayout.LevelCount)
                return 8f;
            int2 cell = MallLayout.CellOf(position);
            var info = world.GetCellInfo(cell);

            // Аномалии: из бездны тянет холодом пустоты, в котловане сыро.
            float2 xz = new(position.x, position.z);
            if (InZone(info, level, xz, AnomalyKind.Abyss))
                return 4f;
            if (InZone(info, level, xz, AnomalyKind.Pool))
                return 13f;

            float temperature = LevelTemperature(MallLayout.Kind(level), night);

            // Раздел комнаты: в вентиляционных тепло от установок, в щитовых — от щитов.
            var layout = RoomLayout(world, info, level, position);
            if (layout == SectionLayout.Tech)
                temperature += 4f;
            else if (layout == SectionLayout.Electrical)
                temperature += 2f;

            // Пропы рядом: у открытых холодильников холодно, у бойлера жарко.
            int count = Physics.OverlapSphereNonAlloc(position + Vector3.up, NearPropRadius, _nearby, 1 << GameLayers.Props, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                if (!PropManager.TryGetProp(_nearby[i], out var prop))
                    continue;
                float closeness = 1f - Mathf.Clamp01(Vector3.Distance(prop.Bounds.ClosestPoint(position + Vector3.up), position + Vector3.up) / NearPropRadius);
                if (prop.Def.Shape == PropShape.Fridge)
                    temperature -= 15f * closeness;
                else if (prop.Def.Shape == PropShape.Boiler)
                    temperature += 18f * closeness;
            }
            // Горящие источники: бочка-костёр, плита и горелка во время готовки.
            temperature += HeatSources.HeatAt(position + Vector3.up);
            return temperature;
        }

        /// <summary>
        /// Игрок в зоне аномалии, которая давит на рассудок: бездна, котлован, вырез, перевёрнутый чанк, объёмы
        /// искажения, глитча и пульсации. Оазис — наоборот, уютное место (InOasis).
        /// </summary>
        public bool InAnomaly(ChunkManager world, Vector3 position)
        {
            if (world == null)
                return false;
            int level = MallLayout.LevelAtFeet(position.y + 0.1f);
            int2 cell = MallLayout.CellOf(position);
            var info = world.GetCellInfo(cell);
            float2 xz = new(position.x, position.z);
            if (MallPlanner.InInverted(info, xz))
                return true;
            if (info.Anomaly != AnomalyKind.Oasis && level >= 0 && level < MallLayout.LevelCount && PropPlacer.InAnomaly(info, level, xz, 0f))
                return true;
            return InMarker(world, position, AnomalyMarkerKind.Distortion) || InMarker(world, position, AnomalyMarkerKind.Glitch)
                || InMarker(world, position, AnomalyMarkerKind.Pulse);
        }

        /// <summary>Игрок под дырой оазиса (на его этаже, в пределах зоны).</summary>
        public bool InOasis(ChunkManager world, Vector3 position)
        {
            if (world == null)
                return false;
            var info = world.GetCellInfo(MallLayout.CellOf(position));
            if (info.Anomaly != AnomalyKind.Oasis || MallLayout.LevelAtFeet(position.y + 0.1f) != info.AnomalyLevel)
                return false;
            return math.distance(new float2(position.x, position.z), info.AnomalyCenter) < MallPlanner.OasisZoneRadius(info.AnomalyRadius);
        }

        /// <summary>Внутри объёма аномалии-маркера (сферы искажения или глитча) в окне 3×3 ячеек.</summary>
        public bool InMarker(ChunkManager world, Vector3 position, AnomalyMarkerKind kind)
        {
            if (world == null)
                return false;
            int2 center = MallLayout.CellOf(position);
            for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                var info = world.GetCellInfo(center + new int2(dx, dz));
                for (int i = 0; i < info.MarkerCount; i++)
                {
                    var marker = info.GetMarker(i);
                    if (marker.Kind == kind && math.distance((float3)position + new float3(0f, 1f, 0f), marker.Center) < marker.Radius)
                        return true;
                }
            }
            return false;
        }

        private static bool InZone(in MallCellInfo info, int level, float2 xz, AnomalyKind kind)
        {
            bool Inside(in MallCellInfo a) =>
                a.Anomaly == kind && PropPlacer.InAnomaly(a, level, xz, 0f);
            return Inside(info) || (info.HasBigAbyss && kind == AnomalyKind.Abyss && Inside(info.WithBigAbyss()));
        }

        private SectionLayout? RoomLayout(ChunkManager world, in MallCellInfo info, int level, Vector3 position)
        {
            if (!_plan.IsCreated)
                _plan = new MallLevelPlan(Allocator.Persistent);
            if (_planLevel != level || !_planCell.Equals(info.Cell))
            {
                MallPlanner.BuildLevel(world.Seed, info, level, world.Sections.Infos, ref _plan);
                _planCell = info.Cell;
                _planLevel = level;
            }
            int2 local = math.clamp((int2)math.floor(new float2(position.x, position.z)) - MallLayout.CellOrigin(info.Cell), 0, MallLayout.CellSize - 1);
            byte room = _plan.RoomIds[MallLevelPlan.Index(local.x, local.y)];
            if (room == 0)
                return null;
            return world.Sections.Infos[_plan.Rooms[room].Section].Layout;
        }
    }
}
