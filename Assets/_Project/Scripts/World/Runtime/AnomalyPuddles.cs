using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using VoidMall.Core;

namespace VoidMall.World
{
    /// <summary>
    /// Светящиеся лужи (GDD §2.5): маркеры Puddle из плана ячеек становятся видимыми лужами, если под ними цел пол.
    /// Лужа лечит стоящего в ней (Drain списывает запас), иссякшая или оставшаяся без пола — испаряется насовсем
    /// (за сессию; сохранение — этап 9). Свет лужи — мягкий, в освещённость не входит: от темноты не спасает.
    /// </summary>
    public sealed class AnomalyPuddles : MonoBehaviour
    {
        /// <summary>Запас лечения лужи, единиц здоровья.</summary>
        public const float Capacity = 60f;

        private const float RefreshInterval = 0.25f;
        private const float VisibleRadius = 48f;

        [SerializeField] private Material _vfxMaterial;

        public static AnomalyPuddles Instance { get; private set; }

        private sealed class Puddle
        {
            public ulong Key;
            public AnomalyMarker Marker;
            public GameObject View;
            public bool Seen;
        }

        private readonly Dictionary<ulong, float> _drained = new();
        private readonly HashSet<ulong> _evaporated = new();
        private readonly Dictionary<ulong, Puddle> _active = new();
        private readonly List<ulong> _stale = new();
        private Material _material;
        private Mesh _disk;
        private float _timer;

        public int ActiveCount => _active.Count;

        private void Awake()
        {
            Instance = this;
            if (_vfxMaterial != null)
            {
                _material = new Material(_vfxMaterial) { name = "Puddle (runtime)", hideFlags = HideFlags.DontSave };
                _material.SetFloat("_Mode", 0f);
                _material.SetFloat("_Intensity", 1f);
            }
            _disk = BuildDisk(32);
        }

        private void OnEnable()
        {
            var world = ChunkManager.Instance;
            if (world != null)
                world.WorldReset += OnWorldReset;
        }

        private void OnDisable()
        {
            var world = ChunkManager.Instance;
            if (world != null)
                world.WorldReset -= OnWorldReset;
        }

        private void OnDestroy()
        {
            if (_material != null)
                Destroy(_material);
            if (_disk != null)
                Destroy(_disk);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Instance = null;

        private void OnWorldReset()
        {
            foreach (var puddle in _active.Values)
                Destroy(puddle.View);
            _active.Clear();
            _drained.Clear();
            _evaporated.Clear();
        }

        private static readonly Perf.Probe UpdateProbe = Perf.Register("World.AnomalyPuddles");

        private void Update()
        {
            using var perf = Perf.Measure(UpdateProbe);
            _timer -= Time.deltaTime;
            var world = ChunkManager.Instance;
            var viewer = WorldViewer.Primary;
            if (_timer > 0f || world == null || viewer == null)
                return;
            _timer = RefreshInterval;
            Refresh(world, viewer.transform.position);
        }

        private void Refresh(ChunkManager world, Vector3 eye)
        {
            foreach (var puddle in _active.Values)
                puddle.Seen = false;
            int2 center = MallLayout.CellOf(eye);
            for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int2 cell = center + new int2(dx, dz);
                var info = world.GetCellInfo(cell);
                for (int i = 0; i < info.MarkerCount; i++)
                {
                    var marker = info.GetMarker(i);
                    if (marker.Kind != AnomalyMarkerKind.Puddle || math.distance((float3)eye, marker.Center) > VisibleRadius)
                        continue;
                    ulong key = KeyOf(cell, i);
                    if (_evaporated.Contains(key))
                        continue;
                    var state = FloorState(world, marker.Center);
                    if (state == Floor.Unknown)
                        continue;
                    if (state == Floor.Missing)
                    {
                        // Пол под лужей выбит (или его нет вовсе — атриум): лужа исчезает навсегда.
                        Evaporate(key, marker, _active.ContainsKey(key));
                        continue;
                    }
                    if (!_active.TryGetValue(key, out var puddle))
                    {
                        puddle = new Puddle { Key = key, Marker = marker, View = CreateView(marker) };
                        _active.Add(key, puddle);
                    }
                    puddle.Seen = true;
                }
            }
            _stale.Clear();
            foreach (var pair in _active)
                if (!pair.Value.Seen)
                    _stale.Add(pair.Key);
            foreach (var key in _stale)
            {
                Destroy(_active[key].View);
                _active.Remove(key);
            }
        }

        private enum Floor
        {
            Unknown,
            Present,
            Missing,
        }

        /// <summary>Под центром лужи твёрдая клетка, а сама клетка — воздух (лужа не в стене).</summary>
        private static Floor FloorState(ChunkManager world, float3 center)
        {
            int3 surface = new((int)math.floor(center.x), (int)math.round(center.y), (int)math.floor(center.z));
            int3 below = surface - new int3(0, 1, 0);
            if (!world.IsLoaded(below) || !world.IsLoaded(surface))
                return Floor.Unknown;
            Cell floor = world.GetCell(below), air = world.GetCell(surface);
            bool solid = floor.Material != 0 && world.GetInfo(floor.Material).IsSolid;
            bool open = air.Material == 0 || (!world.GetInfo(air.Material).IsSolid && !world.GetInfo(air.Material).IsLiquid);
            return solid && open ? Floor.Present : Floor.Missing;
        }

        /// <summary>
        /// Лечение: стоящий в луже (ноги в радиусе, на её высоте) получает до wanted здоровья из её запаса.
        /// Иссякшая лужа испаряется. Возвращает, сколько списано.
        /// </summary>
        public float Drain(Vector3 feet, float wanted)
        {
            if (wanted <= 0f)
                return 0f;
            foreach (var puddle in _active.Values)
            {
                var marker = puddle.Marker;
                float2 offset = new float2(feet.x, feet.z) - marker.Center.xz;
                if (math.length(offset) > marker.Radius * 0.9f || math.abs(feet.y - marker.Center.y) > 0.6f)
                    continue;
                _drained.TryGetValue(puddle.Key, out float drained);
                float taken = math.min(wanted, Capacity - drained);
                drained += taken;
                _drained[puddle.Key] = drained;
                if (drained >= Capacity - 0.01f)
                {
                    Evaporate(puddle.Key, marker, true);
                    Destroy(puddle.View);
                    _active.Remove(puddle.Key);
                }
                return taken;
            }
            return 0f;
        }

        /// <summary>Остаток запаса лужи под ногами (−1 — лужи нет).</summary>
        public float RemainingAt(Vector3 feet)
        {
            foreach (var puddle in _active.Values)
            {
                var marker = puddle.Marker;
                if (math.length(new float2(feet.x, feet.z) - marker.Center.xz) > marker.Radius * 0.9f || math.abs(feet.y - marker.Center.y) > 0.6f)
                    continue;
                _drained.TryGetValue(puddle.Key, out float drained);
                return Capacity - drained;
            }
            return -1f;
        }

        private void Evaporate(ulong key, in AnomalyMarker marker, bool visible)
        {
            _evaporated.Add(key);
            if (_active.TryGetValue(key, out var puddle))
            {
                Destroy(puddle.View);
                _active.Remove(key);
            }
            if (!visible)
                return;
            // Пар: облачко над местом лужи.
            var dust = PropManager.Instance != null ? PropManager.Instance.DustMaterial : null;
            var bounds = new Bounds((Vector3)marker.Center + Vector3.up * 0.3f, new Vector3(marker.Radius * 1.6f, 0.4f, marker.Radius * 1.6f));
            DustFx.Burst(dust, bounds, 120, new Color(0.75f, 0.95f, 1f, 0.5f), new Color(0.9f, 1f, 0.85f, 0.35f));
        }

        private GameObject CreateView(in AnomalyMarker marker)
        {
            var go = new GameObject("Puddle");
            go.transform.SetParent(transform, false);
            go.transform.position = (Vector3)marker.Center + Vector3.up * 0.015f;
            go.transform.localScale = new Vector3(marker.Radius, 1f, marker.Radius);
            go.AddComponent<MeshFilter>().sharedMesh = _disk;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            // Мягкое свечение: светло-жёлтое, слабое, в освещённость (рассудок) не входит.
            var glow = new GameObject("Glow").AddComponent<Light>();
            glow.transform.SetParent(go.transform, false);
            glow.transform.localPosition = new Vector3(0f, 0.35f, 0f);
            glow.type = LightType.Point;
            glow.color = new Color(1f, 0.95f, 0.7f);
            glow.range = marker.Radius + 1.8f;
            glow.intensity = 0.7f;
            glow.shadows = LightShadows.None;
            return go;
        }

        private static ulong KeyOf(int2 cell, int index) => ((ulong)(uint)cell.x << 36) ^ ((ulong)(uint)cell.y << 8) ^ (uint)index;

        /// <summary>Диск радиуса 1 в плоскости XZ, uv −1…1 (форму края рисует шейдер).</summary>
        private static Mesh BuildDisk(int segments)
        {
            var vertices = new List<Vector3> { Vector3.zero };
            var uvs = new List<Vector2> { Vector2.zero };
            var normals = new List<Vector3> { Vector3.up };
            var triangles = new List<int>();
            for (int i = 0; i <= segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                vertices.Add(new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)));
                uvs.Add(new Vector2(Mathf.Cos(a), Mathf.Sin(a)));
                normals.Add(Vector3.up);
                if (i > 0)
                {
                    triangles.Add(0);
                    triangles.Add(i + 1);
                    triangles.Add(i);
                }
            }
            var mesh = new Mesh { name = "PuddleDisk" };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetNormals(normals);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
