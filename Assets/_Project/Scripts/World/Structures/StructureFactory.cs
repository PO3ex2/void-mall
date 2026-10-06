using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using VoidMall.Core;

namespace VoidMall.World
{
    /// <summary>
    /// Готовые структуры макроячейки (проход 4 из ARCHITECTURE §4.6): лестничные марши в ядре, кабина лифта
    /// с дверями шахты, эскалаторы атриумов, пандус парковки. Меши процедурные, коллайдеры — простые коробки
    /// (марши — наклонные плоскости, по ним контроллер идёт плавно).
    /// </summary>
    public static class StructureFactory
    {
        private static readonly Color Concrete = new(0.62f, 0.62f, 0.6f, 0.15f);
        private static readonly Color StepEdge = new(0.85f, 0.75f, 0.2f, 0.2f);
        private static readonly Color Rail = new(0.2f, 0.21f, 0.23f, 0.5f);
        private static readonly Color EscalatorBody = new(0.3f, 0.31f, 0.33f, 0.6f);
        private static readonly Color EscalatorStep = new(0.62f, 0.63f, 0.65f, 0.7f);
        private static readonly Color Balustrade = new(0.7f, 0.82f, 0.86f, 0.9f);
        private static readonly Color Rubber = new(0.06f, 0.06f, 0.06f, 0.3f);
        private static readonly Color CarFloor = new(0.45f, 0.45f, 0.47f, 0.6f);
        private static readonly Color DoorColor = new(0.66f, 0.68f, 0.7f, 0.75f);

        public const float EscalatorSpeed = 0.6f;

        // Одинаковые меши (марши, двери и кабина лифта, эскалатор, пандус) строятся один раз и переиспользуются:
        // раньше каждая ячейка собирала свои копии — сотни КБ мусора и меши, которые не удалялись при выгрузке.
        private static readonly Dictionary<int, Mesh> StairsMeshes = new();
        private static readonly Dictionary<int, Mesh> DoorMeshes = new();
        private static readonly Dictionary<long, Mesh> EscalatorMeshes = new();
        private static Mesh _carMesh, _rampMesh;
        private static readonly Dictionary<int2, Mesh> VineMeshes = new();
        private static readonly MeshBuilder Builder = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            StairsMeshes.Clear();
            DoorMeshes.Clear();
            VineMeshes.Clear();
            EscalatorMeshes.Clear();
            _carMesh = null;
            _rampMesh = null;
            _plazaMesh = _podiumMesh = null;
        }

        public static void Build(uint seed, in MallCellInfo info, Transform parent, Material material)
        {
            var steps = BuildSteps(seed, info, parent, material);
            while (steps.MoveNext())
            {
            }
        }

        /// <summary>
        /// Постройка по частям: ядро (марши), лифт, каждый эскалатор, пандус — по одной части за шаг, чтобы
        /// вход в новую ячейку не стоил одного длинного кадра.
        /// </summary>
        public static IEnumerator<bool> BuildSteps(uint seed, MallCellInfo info, Transform parent, Material material)
        {
            var core = BuildCore(info, parent, material);
            yield return true;
            float4 coreRect = new(info.CoreRect.x, info.CoreRect.y, info.CoreRect.z, info.CoreRect.w);
            if (!Blocked(info, CellRect(info, coreRect), 0, MallLayout.LevelCount - 1))
            {
                BuildElevator(seed, info, core, info.CoreOffset, material);
                yield return true;
            }
            for (int axis = 0; axis < 2; axis++)
            {
                bool alongX = axis == 0;
                if (!(alongX ? info.AtriumX : info.AtriumZ))
                    continue;
                for (int k = 0; k < MallLayout.RetailFloors - 1; k++)
                {
                    if (BuildEscalator(info, parent, material, alongX, k))
                        yield return true;
                }
            }
            if (info.HasRamp && !Blocked(info, CellRect(info, new float4(info.RampMin.x, info.RampMin.y, info.RampMin.x + MallPlanner.RampLength, info.RampMin.y + MallPlanner.RampWidth)), 0, 1))
                BuildRamp(info, parent, material);
            if (BuildPlazasAndStands(seed, info, parent, material))
                yield return true;
            if (BuildOasis(seed, info, parent, material))
                yield return true;
        }

        // ───────────────────────────── Площади и стенды (этап 8Б) ─────────────────────────────

        private static readonly Color PlazaPattern = new(0.32f, 0.3f, 0.28f, 0.6f);
        private static readonly Color PlazaInlay = new(0.72f, 0.62f, 0.42f, 0.7f);
        private static readonly Color PodiumTop = new(0.12f, 0.12f, 0.13f, 0.85f);
        private static readonly Color PodiumEdge = new(0.8f, 0.82f, 0.85f, 0.9f);

        private static Mesh _plazaMesh, _podiumMesh;

        /// <summary>
        /// Круглые площади (узор на полу) и подиумы стендов с машиной на торговых этажах. Фонтан — блоки мира
        /// (MallGenerationJob.FountainCell), скамейки вокруг площади и машина со столбиками — пропы (PropPlacer).
        /// </summary>
        private static bool BuildPlazasAndStands(uint seed, in MallCellInfo info, Transform parent, Material material)
        {
            bool built = false;
            float2 origin = MallLayout.CellOrigin(info.Cell);
            for (int level = MallLayout.FirstRetailLevel; level < MallLayout.FirstRetailLevel + MallLayout.RetailFloors; level++)
            {
                float y = MallLayout.FloorTop(level);
                if (MallPlanner.RoundPlaza(seed, info, level, out _) && !MallPlanner.PlazaWell(seed, info, level))
                {
                    var plaza = NewChild(parent, $"Plaza {MallLayout.LevelName(level)}");
                    plaza.transform.localPosition = new Vector3(origin.x + MallPlanner.PlazaCenter.x, y, origin.y + MallPlanner.PlazaCenter.y);
                    AddRenderer(plaza, PlazaMesh(), material);
                    built = true;
                }
                if (MallPlanner.CarStand(seed, info, level, out float2 center, out bool alongX))
                {
                    var podium = NewChild(parent, $"Car Stand {MallLayout.LevelName(level)}");
                    podium.transform.SetLocalPositionAndRotation(new Vector3(origin.x + center.x, y, origin.y + center.y), Quaternion.Euler(0f, alongX ? 0f : 90f, 0f));
                    AddRenderer(podium, PodiumMesh(), material);
                    var box = podium.AddComponent<BoxCollider>();
                    box.center = new Vector3(0f, MallPlanner.CarStandHeight * 0.5f, 0f);
                    box.size = new Vector3(MallPlanner.CarStandSize.x, MallPlanner.CarStandHeight, MallPlanner.CarStandSize.y);
                    built = true;
                }
            }
            return built;
        }

        // ───────────────────────────── Оазис (этап 8В) ─────────────────────────────

        /// <summary>
        /// Лианы оазиса: свисают с края дыры в потолке — начинаются в шахте этажом выше и спускаются в зал на 1–3.5 м.
        /// Декор без коллизий. Трава, кусты и экран «неба» — блоки и пропы, свет и дождь — AnomalyVisuals.
        /// </summary>
        private static bool BuildOasis(uint seed, in MallCellInfo info, Transform parent, Material material)
        {
            if (info.Anomaly != AnomalyKind.Oasis)
                return false;
            if (!VineMeshes.TryGetValue(info.Cell, out var mesh))
            {
                // Кэш на ячейку: меш не пересобирается при повторном входе, а старые изредка чистятся целиком.
                if (VineMeshes.Count > 48)
                {
                    foreach (var old in VineMeshes.Values)
                        Object.Destroy(old);
                    VineMeshes.Clear();
                }
                mesh = VineMesh(seed, info);
                VineMeshes.Add(info.Cell, mesh);
            }
            var vines = NewChild(parent, "Oasis Vines");
            vines.transform.localPosition = new Vector3(info.AnomalyCenter.x, MallLayout.CeilingRow(info.AnomalyLevel), info.AnomalyCenter.y);
            AddRenderer(vines, mesh, material);
            return true;
        }

        private static Mesh VineMesh(uint seed, in MallCellInfo info)
        {
            Builder.Clear();
            float radius = info.AnomalyRadius - 0.12f;
            int strands = (int)(radius * 7f);
            for (int s = 0; s < strands; s++)
            {
                uint h = Hash.Get(seed, info.Cell.x, info.Cell.y, 7900 + s);
                float angle = (s + Hash.ToFloat01(h) * 0.6f) / strands * Mathf.PI * 2f;
                var dir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                // Верх — в шахте над дырой, низ — в зале под потолком (y = 0 — низ потолочного ряда).
                float top = 1f + Hash.ToFloat01(Hash.Get(h, 1)) * 4f;
                float bottom = -1f - Hash.ToFloat01(Hash.Get(h, 2)) * 2.5f;
                var stem = new Color(0.2f, 0.33f + Hash.ToFloat01(Hash.Get(h, 3)) * 0.1f, 0.13f, 0.15f);
                Vector3 previous = dir * radius + Vector3.up * top;
                int segments = Mathf.CeilToInt((top - bottom) / 0.45f);
                for (int k = 1; k <= segments; k++)
                {
                    float y = Mathf.Lerp(top, bottom, (float)k / segments);
                    // Лиана чуть отходит от стенки и покачивается из стороны в сторону.
                    float sway = Mathf.Sin(k * 1.3f + s) * 0.08f;
                    var tangent = new Vector3(-dir.z, 0f, dir.x);
                    Vector3 next = dir * (radius - Mathf.Max(0f, -y) * 0.12f) + tangent * sway + Vector3.up * y;
                    Builder.Slab(previous, next, 0.035f, 0.035f, stem);
                    // Листья — по очереди с двух сторон.
                    var leaf = new Color(0.16f + (k % 3) * 0.04f, 0.42f + (k % 2) * 0.1f, 0.15f, 0.2f);
                    Vector3 side = tangent * ((k & 1) == 0 ? 0.1f : -0.1f) - dir * 0.04f;
                    Builder.Box(next + side, new Vector3(0.16f, 0.02f, 0.1f), leaf, Quaternion.LookRotation(side.normalized, Vector3.up) * Quaternion.Euler(-35f, 0f, 0f));
                    previous = next;
                }
            }
            return Builder.ToMesh("OasisVines");
        }

        private static Mesh PlazaMesh()
        {
            if (_plazaMesh != null)
                return _plazaMesh;
            Builder.Clear();
            // Узор в терраццо: тёмная кайма по краю площади, светлая вставка и второе кольцо ближе к центру.
            Builder.Ring(Vector3.zero, MallPlanner.PlazaRadius - 0.45f, MallPlanner.PlazaRadius, 0f, 0.006f, 48, PlazaPattern, walls: false);
            Builder.Ring(Vector3.zero, MallPlanner.PlazaRadius - 0.7f, MallPlanner.PlazaRadius - 0.55f, 0f, 0.006f, 48, PlazaInlay, walls: false);
            Builder.Ring(Vector3.zero, MallPlanner.FountainRadius + 0.5f, MallPlanner.FountainRadius + 0.75f, 0f, 0.006f, 40, PlazaPattern, walls: false);
            _plazaMesh = Builder.ToMesh("Plaza");
            return _plazaMesh;
        }

        private static Mesh PodiumMesh()
        {
            if (_podiumMesh != null)
                return _podiumMesh;
            Builder.Clear();
            float hx = MallPlanner.CarStandSize.x * 0.5f, hz = MallPlanner.CarStandSize.y * 0.5f, h = MallPlanner.CarStandHeight;
            Builder.BoxMinMax(new Vector3(-hx, 0f, -hz), new Vector3(hx, h - 0.02f, hz), PodiumEdge);
            Builder.BoxMinMax(new Vector3(-hx + 0.05f, h - 0.02f, -hz + 0.05f), new Vector3(hx - 0.05f, h, hz - 0.05f), PodiumTop);
            _podiumMesh = Builder.ToMesh("Car Stand");
            return _podiumMesh;
        }

        // ───────────────────────────── Ядро: лестница и лифт ─────────────────────────────

        private static Transform BuildCore(in MallCellInfo info, Transform parent, Material material)
        {
            // Канонический кадр квартала: локальные x = u, z = v (метры), поворот — как у квартала.
            float2 origin = MallLayout.CellOrigin(info.Cell) + MallPlanner.BlockToCell(float2.zero, info.Rotation);
            var core = new GameObject("Core").transform;
            core.SetParent(parent, false);
            core.SetPositionAndRotation(new Vector3(origin.x, 0f, origin.y), Quaternion.Euler(0f, MallPlanner.BlockYaw(info.Rotation), 0f));

            float4 coreRect = new(info.CoreRect.x, info.CoreRect.y, info.CoreRect.z, info.CoreRect.w);
            int c = info.CoreOffset;

            var colliders = NewChild(core, "Stair Colliders");
            int mask = 0;
            for (int level = 0; level < MallLayout.LevelCount - 1; level++)
            {
                if (Blocked(info, CellRect(info, coreRect), level, level + 1))
                    continue;
                mask |= 1 << level;
                BuildStairs(null, colliders.transform, c, level, level == MallLayout.LevelCount - 2);
            }
            if (mask != 0)
            {
                if (!StairsMeshes.TryGetValue(mask, out var stairs) || stairs == null)
                {
                    // Меш маршей — в кадре ядра со смещением 0, объект сдвигается на смещение ядра.
                    Builder.Clear();
                    for (int level = 0; level < MallLayout.LevelCount - 1; level++)
                        if ((mask & (1 << level)) != 0)
                            BuildStairs(Builder, null, 0, level, level == MallLayout.LevelCount - 2);
                    StairsMeshes[mask] = stairs = Builder.ToMesh("Stairs");
                }
                var stairsObject = NewChild(core, "Stairs");
                stairsObject.transform.localPosition = new Vector3(c, 0f, 0f);
                AddRenderer(stairsObject, stairs, material);
            }
            return core;
        }

        /// <summary>
        /// Марши между уровнями level и level+1 в лестничной клетке: полоса A (u ∈ [c+1, c+3)) от площадки
        /// у входа (v = 4) вверх к промежуточной площадке (v ∈ [10, 12)), полоса B — от неё обратно к v = 4 на уровень выше.
        /// </summary>
        private static void BuildStairs(MeshBuilder mesh, Transform colliders, int c, int level, bool top)
        {
            // mesh == null — только коллайдеры, colliders == null — только меш (для кэша).
            float h0 = MallLayout.FloorTop(level);
            float rise = MallLayout.FloorTop(level + 1) - h0;
            float half = rise * 0.5f;
            const float v0 = 4f, v1 = 10f, width = 1.9f;
            float uA = c + 2f, uB = c + 4f;
            int steps = Mathf.Max(2, Mathf.RoundToInt(half / 0.175f));
            float tread = (v1 - v0) / steps, stepRise = half / steps;

            for (int i = 0; i < steps; i++)
            {
                // Марш A: вверх по +v.
                float topA = h0 + (i + 1) * stepRise;
                float va = v0 + i * tread;
                mesh?.BoxMinMax(new Vector3(uA - width / 2, topA - stepRise - 0.18f, va), new Vector3(uA + width / 2, topA, va + tread), Concrete);
                mesh?.BoxMinMax(new Vector3(uA - width / 2, topA - 0.01f, va), new Vector3(uA + width / 2, topA + 0.005f, va + 0.06f), StepEdge);
                // Марш B: вверх по −v.
                float topB = h0 + half + (i + 1) * stepRise;
                float vb = v1 - (i + 1) * tread;
                mesh?.BoxMinMax(new Vector3(uB - width / 2, topB - stepRise - 0.18f, vb), new Vector3(uB + width / 2, topB, vb + tread), Concrete);
                mesh?.BoxMinMax(new Vector3(uB - width / 2, topB - 0.01f, vb + tread - 0.06f), new Vector3(uB + width / 2, topB + 0.005f, vb + tread), StepEdge);
            }
            // Промежуточная площадка.
            mesh?.BoxMinMax(new Vector3(c + 1f, h0 + half - 0.3f, v1), new Vector3(c + 5f, h0 + half, 12f), Concrete);
            if (colliders != null) AddBox(colliders, new Vector3(c + 3f, h0 + half - 0.15f, 11f), new Vector3(4f, 0.3f, 2f));

            // Наклонные коллайдеры маршей — по линии внутренних углов ступеней.
            if (colliders != null) AddRamp(colliders, new Vector3(uA, h0, v0), new Vector3(uA, h0 + half, v1), width, 0.3f);
            if (colliders != null) AddRamp(colliders, new Vector3(uB, h0 + half, v1), new Vector3(uB, h0 + rise, v0), width, 0.3f);

            // Перегородка между полосами: от марша A до перил над маршем B.
            for (int k = 0; k < 6; k++)
            {
                float v = v0 + k;
                float bottom = h0 + half * (k / 6f) - 0.25f;
                float topRail = h0 + half + half * ((6 - k) / 6f) + 1.0f;
                mesh?.BoxMinMax(new Vector3(c + 2.95f, bottom, v), new Vector3(c + 3.05f, topRail, v + 1f), Concrete);
                if (colliders != null) AddBox(colliders, new Vector3(c + 3f, (bottom + topRail) * 0.5f, v + 0.5f), new Vector3(0.1f, topRail - bottom, 1f));
            }
            mesh?.BoxMinMax(new Vector3(c + 2.9f, h0 + rise + 0.95f, v0), new Vector3(c + 3.1f, h0 + rise + 1.05f, v1), Rail);

            if (top)
            {
                // На верхнем уровне над маршем B — проём в плите: ограждение по его дальнему краю.
                mesh?.BoxMinMax(new Vector3(c + 3f, h0 + rise, v1 - 0.05f), new Vector3(c + 5f, h0 + rise + 1.05f, v1 + 0.05f), Rail);
                if (colliders != null) AddBox(colliders, new Vector3(c + 4f, h0 + rise + 0.5f, v1), new Vector3(2f, 1f, 0.1f));
            }
        }

        private static void BuildElevator(uint seed, in MallCellInfo info, Transform core, int c, Material material)
        {
            int levels = MallLayout.LevelCount;
            var stops = new float[levels];
            var doors = new Transform[levels];
            var heights = new float[levels];

            var shaft = NewChild(core, "Elevator").transform;
            for (int level = 0; level < levels; level++)
            {
                stops[level] = MallLayout.FloorTop(level);
                heights[level] = MallLayout.DoorHeight(level);
                // Створка в проёме шахты (u ∈ [c+6, c+9), стена v ∈ [0, 1)); опора — низ створки.
                var door = NewChild(shaft, $"Door {MallLayout.LevelName(level)}");
                door.transform.localPosition = new Vector3(c + 7.5f, stops[level], 0.5f);
                int doorKey = Mathf.RoundToInt(heights[level] * 100f);
                if (!DoorMeshes.TryGetValue(doorKey, out var doorMesh) || doorMesh == null)
                {
                    Builder.Clear();
                    Builder.BoxMinMax(new Vector3(-1.5f, 0f, -0.06f), new Vector3(1.5f, heights[level], 0.06f), DoorColor);
                    Builder.BoxMinMax(new Vector3(-0.02f, 0f, -0.07f), new Vector3(0.02f, heights[level], 0.07f), Rail);
                    DoorMeshes[doorKey] = doorMesh = Builder.ToMesh("ElevatorDoor");
                }
                AddRenderer(door, doorMesh, material);
                var doorCollider = door.AddComponent<BoxCollider>();
                doorCollider.center = new Vector3(0f, heights[level] * 0.5f, 0f);
                doorCollider.size = new Vector3(3f, heights[level], 0.12f);
                doors[level] = door.transform;
            }

            var car = NewChild(shaft, "Car");
            car.transform.localPosition = new Vector3(c + 7.5f, stops[0], 2.5f);
            car.transform.localRotation = Quaternion.identity;
            if (_carMesh == null)
            {
                Builder.Clear();
                Builder.BoxMinMax(new Vector3(-1.45f, -0.25f, -1.45f), new Vector3(1.45f, 0f, 1.45f), CarFloor);
                Builder.BoxMinMax(new Vector3(-1.45f, 0f, 1.35f), new Vector3(1.45f, 1.05f, 1.45f), Rail);
                _carMesh = Builder.ToMesh("ElevatorCar");
            }
            AddRenderer(car, _carMesh, material);
            var carCollider = car.AddComponent<BoxCollider>();
            carCollider.center = new Vector3(0f, -0.125f, 0f);
            carCollider.size = new Vector3(2.9f, 0.25f, 2.9f);
            car.AddComponent<Rigidbody>();
            float phase = Hash.ToFloat01(Hash.Get(seed, info.Cell.x, info.Cell.y, 7777)) * 120f;
            car.AddComponent<ElevatorCar>().Setup(stops, doors, heights, phase);
        }

        // ───────────────────────────── Эскалаторы ─────────────────────────────

        /// <summary>Эскалатор k (этаж k → k+1) в полосе k % 2; чётные идут к концу пустоты атриума, нечётные — обратно.</summary>
        private static bool BuildEscalator(in MallCellInfo info, Transform parent, Material material, bool alongX, int k)
        {
            float2 cellOrigin = MallLayout.CellOrigin(info.Cell);
            int lane = k % 2;
            float across = MallPlanner.AtriumAcrossMin + MallPlanner.AtriumLaneWidth * (lane + 0.5f);
            float bottomAlong = k % 2 == 0 ? MallPlanner.AtriumAlongMin : MallPlanner.AtriumAlongMax;
            float topAlong = k % 2 == 0 ? MallPlanner.AtriumAlongMax : MallPlanner.AtriumAlongMin;
            int floorLevel = MallLayout.FirstRetailLevel + k;
            float y0 = MallLayout.FloorTop(floorLevel), y1 = MallLayout.FloorTop(floorLevel + 1);

            float2 bottom = alongX ? new float2(bottomAlong, across) : new float2(across, bottomAlong);
            float2 top = alongX ? new float2(topAlong, across) : new float2(across, topAlong);
            float4 rect = new(math.min(bottom, top) - 1f, math.max(bottom, top) + 1f);
            if (Blocked(info, CellRect(info, rect), floorLevel, floorLevel + 1))
                return false;

            Vector3 start = new(cellOrigin.x + bottom.x, y0, cellOrigin.y + bottom.y);
            Vector3 end = new(cellOrigin.x + top.x, y1, cellOrigin.y + top.y);
            BuildEscalator(parent, material, start, end, k);
            return true;
        }

        private static void BuildEscalator(Transform parent, Material material, Vector3 start, Vector3 end, int index)
        {
            Vector3 flat = end - start;
            flat.y = 0f;
            float run = flat.magnitude;
            float rise = end.y - start.y;
            var root = NewChild(parent, $"Escalator {index}").transform;
            root.SetPositionAndRotation(start, Quaternion.LookRotation(flat / run, Vector3.up));

            Vector3 a = Vector3.zero, b = new(0f, rise, run);
            const float width = 1.6f;
            long escalatorKey = ((long)Mathf.RoundToInt(rise * 100f) << 32) | (uint)Mathf.RoundToInt(run * 100f);
            if (!EscalatorMeshes.TryGetValue(escalatorKey, out var escalatorMesh) || escalatorMesh == null)
            {
                var mesh = Builder;
                mesh.Clear();
                mesh.Slab(a + Vector3.down * 0.02f, b + Vector3.down * 0.02f, width + 0.3f, 0.9f, EscalatorBody);
                int steps = Mathf.RoundToInt(run / 0.4f);
                for (int i = 0; i < steps; i++)
                {
                    float t = (i + 0.5f) / steps;
                    Vector3 p = Vector3.Lerp(a, b, t);
                    mesh.Box(p + new Vector3(0f, 0.02f, 0f), new Vector3(width, 0.05f, 0.12f), EscalatorStep);
                }
                // Гребёнки на площадках у концов.
                mesh.BoxMinMax(new Vector3(-width / 2, -0.01f, -1f), new Vector3(width / 2, 0.03f, 0f), EscalatorStep);
                mesh.BoxMinMax(new Vector3(-width / 2, rise - 0.01f, run), new Vector3(width / 2, rise + 0.03f, run + 1f), EscalatorStep);
                // Балюстрады с поручнями.
                for (int side = -1; side <= 1; side += 2)
                {
                    float x = side * (width / 2 + 0.08f);
                    mesh.Slab(new Vector3(x, 1f, -0.6f), new Vector3(x, rise + 1f, run + 0.6f), 0.06f, 0.95f, Balustrade);
                    mesh.Slab(new Vector3(x, 1.05f, -0.8f), new Vector3(x, rise + 1.05f, run + 0.8f), 0.12f, 0.1f, Rubber);
                }
                EscalatorMeshes[escalatorKey] = escalatorMesh = mesh.ToMesh("Escalator");
            }
            AddRenderer(root.gameObject, escalatorMesh, material);

            var belt = AddRamp(root, a, b, width, 0.4f);
            belt.AddComponent<Escalator>().Configure(EscalatorSpeed);
            for (int side = -1; side <= 1; side += 2)
            {
                float x = side * (width / 2 + 0.08f);
                AddRamp(root, new Vector3(x, 1.1f, -0.5f), new Vector3(x, rise + 1.1f, run + 0.5f), 0.1f, 1.2f);
            }
        }

        // ───────────────────────────── Пандус парковки ─────────────────────────────

        private static void BuildRamp(in MallCellInfo info, Transform parent, Material material)
        {
            float2 origin = MallLayout.CellOrigin(info.Cell);
            float z = origin.y + info.RampMin.y + MallPlanner.RampWidth * 0.5f;
            // Пандус строится в своём кадре (от начала подъёма) — меш один на все ячейки.
            var root = NewChild(parent, "Ramp").transform;
            root.position = new Vector3(origin.x + info.RampMin.x, MallLayout.FloorTop(0), z);
            Vector3 start = Vector3.zero;
            Vector3 end = new(MallPlanner.RampLength, MallLayout.FloorTop(1) - MallLayout.FloorTop(0), 0f);

            if (_rampMesh == null)
            {
                Builder.Clear();
                Builder.Slab(start, end, MallPlanner.RampWidth, 0.35f, Concrete);
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector3 offset = new(0f, 0.45f, side * (MallPlanner.RampWidth * 0.5f - 0.1f));
                    Builder.Slab(start + offset, end + offset, 0.2f, 0.9f, Concrete);
                }
                _rampMesh = Builder.ToMesh("Ramp");
            }
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 offset = new(0f, 0.45f, side * (MallPlanner.RampWidth * 0.5f - 0.1f));
                AddRamp(root, start + offset, end + offset, 0.2f, 0.9f);
            }
            AddRenderer(root.gameObject, _rampMesh, material);
            AddRamp(root, start, end, MallPlanner.RampWidth, 0.35f);
        }

        // ───────────────────────────── Вспомогательное ─────────────────────────────

        /// <summary>
        /// Структура пересекает «вырез», а её нижний уровень не выше уровня выреза (там пустота до дна мира)?
        /// </summary>
        private static bool Blocked(in MallCellInfo info, float4 worldRect, int lowestLevel, int highestLevel) =>
            BlockedBy(info, worldRect, lowestLevel, highestLevel)
            || (info.HasBigAbyss && BlockedBy(info.WithBigAbyss(), worldRect, lowestLevel, highestLevel));

        private static bool BlockedBy(in MallCellInfo info, float4 worldRect, int lowestLevel, int highestLevel)
        {
            if (info.Anomaly == AnomalyKind.Pool)
            {
                // Котлован: чаша на своём уровне и выпуклость в потолке уровня ниже — там структуре не на чем стоять.
                if (highestLevel < info.AnomalyLevel - 1 || lowestLevel > info.AnomalyLevel)
                    return false;
                float2 nearest = math.clamp(info.AnomalyCenter, worldRect.xy, worldRect.zw);
                return math.distance(nearest, info.AnomalyCenter) < MallAnomalies.PoolZoneRadius(info.AnomalyRadius) + 1f;
            }
            if (info.Anomaly == AnomalyKind.Abyss)
            {
                // Бездна: от своего уровня вниз до пустоты.
                if (lowestLevel > info.AnomalyLevel)
                    return false;
                float2 nearest = math.clamp(info.AnomalyCenter, worldRect.xy, worldRect.zw);
                return math.distance(nearest, info.AnomalyCenter) < MallAnomalies.AbyssZoneRadius(info.AnomalyRadius) + 1f;
            }
            if (info.Anomaly == AnomalyKind.Oasis)
            {
                // Оазис: дыра в потолке своего уровня и шахта этажом выше.
                if (highestLevel < info.AnomalyLevel || lowestLevel > info.AnomalyLevel + 1)
                    return false;
                float2 nearest = math.clamp(info.AnomalyCenter, worldRect.xy, worldRect.zw);
                return math.distance(nearest, info.AnomalyCenter) < MallPlanner.OasisZoneRadius(info.AnomalyRadius) + 1f;
            }
            if (info.Anomaly != AnomalyKind.Cutout && info.Anomaly != AnomalyKind.Inverted)
                return false;
            float2 c = info.AnomalyCenter;
            bool overlap = worldRect.x < c.x + WorldConstants.ChunkSize && c.x < worldRect.z
                && worldRect.y < c.y + WorldConstants.ChunkSize && c.y < worldRect.w;
            // Перевёрнутая колонна — на всех уровнях (структуры не переворачиваются).
            return overlap && lowestLevel <= info.AnomalyLevel;
        }

        /// <summary>Прямоугольник в координатах ячейки → мировые XZ.</summary>
        private static float4 CellRect(in MallCellInfo info, float4 rect)
        {
            float2 origin = MallLayout.CellOrigin(info.Cell);
            return new float4(rect.xy + origin, rect.zw + origin);
        }

        private static GameObject NewChild(Transform parent, string name)
        {
            var go = new GameObject(name) { layer = GameLayers.World };
            go.transform.SetParent(parent, false);
            return go;
        }

        private static void AddRenderer(GameObject go, Mesh mesh, Material material)
        {
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
        }

        private static void AddBox(Transform parent, Vector3 center, Vector3 size)
        {
            var box = parent.gameObject.AddComponent<BoxCollider>();
            box.center = center;
            box.size = size;
        }

        /// <summary>Наклонный коллайдер: верхняя грань проходит через from → to (в локальных координатах parent).</summary>
        private static GameObject AddRamp(Transform parent, Vector3 from, Vector3 to, float width, float thickness)
        {
            var go = NewChild(parent, "Ramp Collider");
            Vector3 axis = to - from;
            var rotation = Quaternion.LookRotation(axis.normalized, Vector3.up);
            go.transform.localPosition = (from + to) * 0.5f;
            go.transform.localRotation = rotation;
            var box = go.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, -thickness * 0.5f, 0f);
            box.size = new Vector3(width, thickness, axis.magnitude);
            return go;
        }
    }
}
