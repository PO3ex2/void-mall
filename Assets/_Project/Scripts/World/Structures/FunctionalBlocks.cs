using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using VoidMall.Console;
using VoidMall.Core;

namespace VoidMall.World
{
    /// <summary>Модель функционального блока у игрока: по коллайдеру находится якорь (взаимодействие E).</summary>
    public sealed class FunctionalBlockView : MonoBehaviour
    {
        public int3 Anchor { get; internal set; }
        public ushort Material { get; internal set; }
        public FunctionalKind Kind { get; internal set; }

        internal Transform Pivot;
        internal GameObject LockMark;
        internal float Current;  // 0 — закрыто, 1 — открыто
        internal float Target;
        internal float Sign = 1f;
        internal int Width = 1;
        internal Transform[] Leaves;     // створки автоматической двери
        internal Vector3[] LeafHome;
        internal float[] LeafShift;
    }

    /// <summary>
    /// Модели и коллизия функциональных блоков рядом с игроком (ARCHITECTURE §4.9). Сами блоки — клетки мира
    /// (материал + якорь, см. FunctionalCells), сетка их не рисует. Здесь — пул GameObject по якорям в радиусе:
    /// дверь поворачивается на петле, ворота уезжают вверх (состояние — из якоря), автоматическая стеклянная
    /// дверь (цельный блок 1–4 клетки) разводит створки, когда рядом игрок: одна проверка на весь блок, 10 раз в секунду
    /// (состояние не хранится — у каждого клиента своё по близости игроков).
    /// </summary>
    public sealed class FunctionalBlocks : MonoBehaviour
    {
        public static readonly CVar<float> Radius = new("world_fblock_radius", 48f, "Радиус моделей дверей и ворот, м.");

        private const float RefreshInterval = 0.25f;
        private const float DoorSpeed = 3f;
        private const float GateSpeed = 1.2f;
        private const float SlideSpeed = 2.5f;
        private const float SlideRange = 2.8f;
        private const float SenseInterval = 0.1f;

        [SerializeField] private Material _material;
        [SerializeField] private Material _glassMaterial;

        public static FunctionalBlocks Instance { get; private set; }

        private readonly Dictionary<int3, FunctionalBlockView> _views = new();
        private readonly Dictionary<int, Stack<FunctionalBlockView>> _pools = new();
        private readonly List<int3> _anchors = new();
        private readonly HashSet<int3> _wanted = new();
        private readonly List<int3> _stale = new();
        private readonly Dictionary<FunctionalKind, Mesh> _meshes = new();
        private readonly Dictionary<int, Mesh> _ghostMeshes = new();
        private Mesh _lockMesh;
        private ChunkManager _world;
        private float _nextRefresh;
        private float _nextSense;

        private static readonly Perf.Probe UpdateProbe = Perf.Register("World.FunctionalBlocks");

        private void Awake() => Instance = this;

        private void Start()
        {
            _world = ChunkManager.Instance;
            if (_world != null)
            {
                _world.CellChanged += OnCellChanged;
                _world.WorldReset += Clear;
            }
        }

        private void OnDestroy()
        {
            if (_world != null)
            {
                _world.CellChanged -= OnCellChanged;
                _world.WorldReset -= Clear;
            }
            foreach (var mesh in _meshes.Values)
                Destroy(mesh);
            foreach (var mesh in _ghostMeshes.Values)
                Destroy(mesh);
            if (_lockMesh != null)
                Destroy(_lockMesh);
            if (_glassMesh != null)
                Destroy(_glassMesh);
            if (Instance == this)
                Instance = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Instance = null;

        /// <summary>Модель по коллайдеру (луч игрока попал в дверь).</summary>
        public static bool TryGetView(Collider collider, out FunctionalBlockView view)
        {
            view = collider != null ? collider.GetComponentInParent<FunctionalBlockView>() : null;
            return view != null;
        }

        private void Update()
        {
            using var perf = Perf.Measure(UpdateProbe);
            if (_world == null)
                return;
            if (Time.unscaledTime >= _nextRefresh)
            {
                _nextRefresh = Time.unscaledTime + RefreshInterval;
                Refresh();
            }
            bool sense = Time.unscaledTime >= _nextSense;
            if (sense)
                _nextSense = Time.unscaledTime + SenseInterval;
            float dt = Time.deltaTime;
            foreach (var view in _views.Values)
            {
                if (sense && view.Kind == FunctionalKind.SlidingDoor)
                    view.Target = SomeoneNear(view) ? 1f : 0f;
                if (Mathf.Approximately(view.Current, view.Target))
                    continue;
                float speed = view.Kind switch { FunctionalKind.Gate => GateSpeed, FunctionalKind.SlidingDoor => SlideSpeed, _ => DoorSpeed };
                view.Current = Mathf.MoveTowards(view.Current, view.Target, speed * dt);
                Pose(view);
            }
        }

        /// <summary>Автоматическая дверь открыта у этого клиента (луч проходит в проём).</summary>
        public bool IsOpenLocally(int3 anchor) => _views.TryGetValue(anchor, out var view) && view.Current > 0.5f;

        /// <summary>Кто-то из игроков у проёма: расстояние до отрезка двери в её локальных осях (одна проверка на блок).</summary>
        private static bool SomeoneNear(FunctionalBlockView view)
        {
            var viewers = WorldViewer.All;
            float center = (view.Width - 1) * 0.5f;
            for (int i = 0; i < viewers.Count; i++)
            {
                Vector3 local = view.transform.InverseTransformPoint(viewers[i].transform.position);
                if (local.y < -1.5f || local.y > 2.5f)
                    continue;
                float dx = Mathf.Max(0f, Mathf.Abs(local.x - center) - view.Width * 0.5f);
                if (dx * dx + local.z * local.z < SlideRange * SlideRange)
                    return true;
            }
            return false;
        }

        private void Refresh()
        {
            Vector3 center = ViewerPosition();
            _anchors.Clear();
            _world.CollectAnchors(center, Radius.Value, _anchors);
            _wanted.Clear();
            foreach (var anchor in _anchors)
                _wanted.Add(anchor);
            _stale.Clear();
            foreach (var anchor in _views.Keys)
                if (!_wanted.Contains(anchor))
                    _stale.Add(anchor);
            foreach (var anchor in _stale)
                Release(anchor);
            foreach (var anchor in _anchors)
                if (!_views.ContainsKey(anchor))
                    Spawn(anchor, snap: true);
        }

        private void OnCellChanged(int3 cell, Cell value)
        {
            bool isAnchor = value.Material != 0 && value.Value == 0 && _world.GetInfo(value.Material).Kind == MaterialKind.Functional;
            if (_views.TryGetValue(cell, out var view))
            {
                if (!isAnchor || value.Material != view.Material)
                {
                    Release(cell);
                    if (isAnchor)
                        Spawn(cell, snap: true);
                    return;
                }
                Apply(view, value, snap: false);
                return;
            }
            if (isAnchor && math.distance((float3)cell, (float3)ViewerPosition()) <= Radius.Value)
                Spawn(cell, snap: true);
        }

        private void Spawn(int3 anchor, bool snap)
        {
            Cell value = _world.GetCell(anchor);
            if (value.IsAir || value.Value != 0)
                return;
            var info = _world.GetInfo(value.Material);
            if (info.Kind != MaterialKind.Functional)
                return;
            int width = info.Function == FunctionalKind.SlidingDoor ? info.FunctionalSize.x : 1;
            var pool = Pool(info.Function, width);
            var view = pool.Count > 0 ? pool.Pop() : Create(info.Function, width);
            view.gameObject.SetActive(true);
            view.Anchor = anchor;
            view.Material = value.Material;
            view.transform.SetPositionAndRotation(new Vector3(anchor.x + 0.5f, anchor.y, anchor.z + 0.5f),
                Quaternion.Euler(0f, 90f * FunctionalCells.Yaw(value.Meta), 0f));
            _views[anchor] = view;
            Apply(view, value, snap);
        }

        private void Release(int3 anchor)
        {
            if (!_views.Remove(anchor, out var view))
                return;
            view.gameObject.SetActive(false);
            Pool(view.Kind, view.Width).Push(view);
        }

        private Stack<FunctionalBlockView> Pool(FunctionalKind kind, int width)
        {
            int key = (int)kind * 16 + width;
            if (!_pools.TryGetValue(key, out var pool))
                _pools[key] = pool = new Stack<FunctionalBlockView>();
            return pool;
        }

        private void Clear()
        {
            foreach (var anchor in new List<int3>(_views.Keys))
                Release(anchor);
        }

        private void Apply(FunctionalBlockView view, Cell value, bool snap)
        {
            if (view.Kind == FunctionalKind.SlidingDoor)
            {
                view.Target = SomeoneNear(view) ? 1f : 0f;
                if (snap)
                    view.Current = view.Target;
                Pose(view);
                return;
            }
            view.Target = FunctionalCells.IsOpen(value.Meta) ? 1f : 0f;
            view.Sign = FunctionalCells.OpenSide(value.Meta) ? -1f : 1f;
            if (view.LockMark != null)
                view.LockMark.SetActive(FunctionalCells.IsLocked(value.Meta));
            if (snap)
                view.Current = view.Target;
            Pose(view);
        }

        private static void Pose(FunctionalBlockView view)
        {
            float t = Mathf.SmoothStep(0f, 1f, view.Current);
            if (view.Kind == FunctionalKind.Gate)
                view.Pivot.localPosition = new Vector3(0f, 2.6f * t, 0f);
            else if (view.Kind == FunctionalKind.SlidingDoor)
            {
                for (int i = 0; i < view.Leaves.Length; i++)
                    view.Leaves[i].localPosition = view.LeafHome[i] + new Vector3(view.LeafShift[i] * t, 0f, 0f);
            }
            else
                view.Pivot.localRotation = Quaternion.Euler(0f, -95f * t * view.Sign, 0f);
        }

        // ───────────────────────────── Модели ─────────────────────────────

        private FunctionalBlockView Create(FunctionalKind kind, int width)
        {
            var go = new GameObject(kind == FunctionalKind.SlidingDoor ? $"SlidingDoor{width}" : kind.ToString());
            go.transform.SetParent(transform, false);
            var view = go.AddComponent<FunctionalBlockView>();
            view.Kind = kind;
            view.Width = width;
            if (kind == FunctionalKind.SlidingDoor)
            {
                CreateLeaves(view, width);
                return view;
            }

            var pivot = new GameObject("Pivot");
            pivot.layer = GameLayers.World;
            pivot.transform.SetParent(go.transform, false);
            // Дверь вращается на петле у левого края клетки, ворота — целиком (едут вверх).
            pivot.transform.localPosition = kind == FunctionalKind.Door ? new Vector3(-0.5f, 0f, 0f) : Vector3.zero;
            pivot.AddComponent<MeshFilter>().sharedMesh = MeshFor(kind);
            var renderer = pivot.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _material;
            var collider = pivot.AddComponent<BoxCollider>();
            if (kind == FunctionalKind.Gate)
            {
                collider.center = new Vector3(0f, 1.5f, 0f);
                collider.size = new Vector3(1f, 3f, 0.14f);
            }
            else
            {
                collider.center = new Vector3(0.5f, 1f, 0f);
                collider.size = new Vector3(0.98f, 2f, 0.1f);
            }
            view.Pivot = pivot.transform;

            if (kind == FunctionalKind.Gate)
            {
                // Замок с обеих сторон рольставни: заперто видно и из галереи, и из магазина.
                var mark = new GameObject("Lock");
                mark.transform.SetParent(pivot.transform, false);
                for (int side = 0; side < 2; side++)
                {
                    var face = new GameObject(side == 0 ? "Front" : "Back");
                    face.transform.SetParent(mark.transform, false);
                    face.transform.SetLocalPositionAndRotation(new Vector3(0f, 0.25f, side == 0 ? -0.09f : 0.09f), Quaternion.Euler(0f, side * 180f, 0f));
                    face.AddComponent<MeshFilter>().sharedMesh = LockMesh();
                    face.AddComponent<MeshRenderer>().sharedMaterial = _material;
                }
                view.LockMark = mark;
            }
            return view;
        }

        /// <summary>
        /// Створки автоматической двери: по одной на клетку. Половина у начала уезжает к −X, половина у конца — к +X;
        /// у двери 4×3 внутренние створки чуть впереди и уезжают вдвое дальше (телескопом), проём открывается целиком.
        /// </summary>
        private void CreateLeaves(FunctionalBlockView view, int width)
        {
            view.Leaves = new Transform[width];
            view.LeafHome = new Vector3[width];
            view.LeafShift = new float[width];
            for (int i = 0; i < width; i++)
            {
                LeafLayout(width, i, out var home, out float shift);
                var leaf = new GameObject("Leaf");
                leaf.layer = GameLayers.World;
                leaf.transform.SetParent(view.transform, false);
                leaf.transform.localPosition = home;
                leaf.AddComponent<MeshFilter>().sharedMesh = MeshFor(FunctionalKind.SlidingDoor);
                leaf.AddComponent<MeshRenderer>().sharedMaterial = _material;
                var collider = leaf.AddComponent<BoxCollider>();
                collider.center = new Vector3(0f, 1.5f, 0f);
                collider.size = new Vector3(0.98f, 3f, 0.08f);
                if (_glassMaterial != null)
                {
                    var glass = new GameObject("Glass");
                    glass.transform.SetParent(leaf.transform, false);
                    glass.AddComponent<MeshFilter>().sharedMesh = GlassMesh();
                    var glassRenderer = glass.AddComponent<MeshRenderer>();
                    glassRenderer.sharedMaterial = _glassMaterial;
                    glassRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
                view.Leaves[i] = leaf.transform;
                view.LeafHome[i] = home;
                view.LeafShift[i] = shift;
            }
            view.Pivot = view.Leaves[0];
        }

        private static void LeafLayout(int width, int index, out Vector3 home, out float shift)
        {
            bool towardStart = index * 2 < width || width == 1;
            int fromEdge = towardStart ? index : width - 1 - index;
            home = new Vector3(index, 0f, fromEdge * 0.09f);
            shift = (towardStart ? -0.92f : 0.92f) * (fromEdge + 1);
        }

        /// <summary>Модель для призрака при постановке: у автоматической двери — все створки и стекло одним мешем.</summary>
        public Mesh GhostMesh(FunctionalKind kind, int width)
        {
            if (kind != FunctionalKind.SlidingDoor)
                return MeshFor(kind);
            int key = width;
            if (_ghostMeshes.TryGetValue(key, out var mesh))
                return mesh;
            var b = new MeshBuilder();
            for (int i = 0; i < width; i++)
            {
                LeafLayout(width, i, out var home, out _);
                AddLeafFrame(b, home);
                b.BoxMinMax(home + new Vector3(-0.42f, 0.1f, -0.012f), home + new Vector3(0.42f, 2.9f, 0.012f), new Color(0.7f, 0.85f, 0.9f, 0.9f));
            }
            mesh = b.ToMesh($"FBlock_SlidingGhost{width}");
            _ghostMeshes[key] = mesh;
            return mesh;
        }

        private static void AddLeafFrame(MeshBuilder b, Vector3 o)
        {
            // Алюминиевая рама и ручка-полоса; стекло — отдельным мешем со своим материалом.
            var frame = new Color(0.72f, 0.74f, 0.76f, 0.7f);
            b.BoxMinMax(o + new Vector3(-0.49f, 0f, -0.04f), o + new Vector3(-0.42f, 3f, 0.04f), frame);
            b.BoxMinMax(o + new Vector3(0.42f, 0f, -0.04f), o + new Vector3(0.49f, 3f, 0.04f), frame);
            b.BoxMinMax(o + new Vector3(-0.49f, 0f, -0.04f), o + new Vector3(0.49f, 0.1f, 0.04f), frame);
            b.BoxMinMax(o + new Vector3(-0.49f, 2.9f, -0.04f), o + new Vector3(0.49f, 3f, 0.04f), frame);
            b.BoxMinMax(o + new Vector3(-0.42f, 1.0f, -0.05f), o + new Vector3(0.42f, 1.06f, 0.05f), frame);
        }

        public Mesh MeshFor(FunctionalKind kind)
        {
            if (_meshes.TryGetValue(kind, out var mesh))
                return mesh;
            var b = new MeshBuilder();
            if (kind == FunctionalKind.SlidingDoor)
                AddLeafFrame(b, Vector3.zero);
            else if (kind == FunctionalKind.Gate)
            {
                // Рольставня: гофрированное полотно, нижняя планка, ручка.
                var steel = new Color(0.62f, 0.64f, 0.66f, 0.55f);
                var dark = new Color(0.3f, 0.31f, 0.33f, 0.5f);
                b.BoxMinMax(new Vector3(-0.5f, 0f, -0.05f), new Vector3(0.5f, 3f, 0.05f), steel);
                for (int i = 0; i < 20; i++)
                {
                    float y = 0.12f + i * 0.145f;
                    b.BoxMinMax(new Vector3(-0.5f, y, -0.065f), new Vector3(0.5f, y + 0.03f, 0.065f), dark);
                }
                b.BoxMinMax(new Vector3(-0.5f, 0f, -0.08f), new Vector3(0.5f, 0.08f, 0.08f), dark);
            }
            else
            {
                // Дверь: полотно с филёнкой, ручки с обеих сторон.
                var wood = new Color(0.55f, 0.42f, 0.3f, 0.2f);
                var panel = new Color(0.48f, 0.36f, 0.25f, 0.2f);
                var metal = new Color(0.75f, 0.76f, 0.78f, 0.8f);
                b.BoxMinMax(new Vector3(0.02f, 0f, -0.04f), new Vector3(0.98f, 1.98f, 0.04f), wood);
                b.BoxMinMax(new Vector3(0.15f, 1.1f, -0.05f), new Vector3(0.85f, 1.8f, 0.05f), panel);
                b.BoxMinMax(new Vector3(0.15f, 0.2f, -0.05f), new Vector3(0.85f, 0.9f, 0.05f), panel);
                b.BoxMinMax(new Vector3(0.82f, 0.95f, -0.12f), new Vector3(0.92f, 1.0f, 0.12f), metal);
            }
            mesh = b.ToMesh("FBlock_" + kind);
            _meshes[kind] = mesh;
            return mesh;
        }

        private Mesh _glassMesh;

        private Mesh GlassMesh()
        {
            if (_glassMesh != null)
                return _glassMesh;
            var b = new MeshBuilder();
            b.BoxMinMax(new Vector3(-0.42f, 0.1f, -0.012f), new Vector3(0.42f, 2.9f, 0.012f), Color.white);
            _glassMesh = b.ToMesh("FBlock_Glass");
            return _glassMesh;
        }

        private Mesh LockMesh()
        {
            if (_lockMesh != null)
                return _lockMesh;
            var b = new MeshBuilder();
            var brass = new Color(0.85f, 0.65f, 0.2f, 0.8f);
            b.BoxMinMax(new Vector3(-0.08f, -0.08f, -0.03f), new Vector3(0.08f, 0.06f, 0.03f), brass);
            b.BoxMinMax(new Vector3(-0.05f, 0.06f, -0.015f), new Vector3(0.05f, 0.14f, 0.015f), new Color(0.6f, 0.6f, 0.62f, 0.7f));
            _lockMesh = b.ToMesh("FBlock_Lock");
            return _lockMesh;
        }

        private static Vector3 ViewerPosition()
        {
            var camera = Camera.main;
            if (camera != null)
                return camera.transform.position;
            var viewer = WorldViewer.Primary;
            return viewer != null ? viewer.transform.position : Vector3.zero;
        }
    }
}
