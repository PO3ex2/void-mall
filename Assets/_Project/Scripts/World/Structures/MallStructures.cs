using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using VoidMall.Console;
using VoidMall.Core;

namespace VoidMall.World
{
    /// <summary>
    /// Стриминг структур (лестницы, лифты, эскалаторы, пандусы) по макроячейкам в радиусе прогрузки мира и мебели-заглушек
    /// «корзинами» (ячейка + уровень) рядом с игроком: на его этаже и соседних, в радиусе world_prop_radius.
    /// Всё выводится из плана, поэтому выгруженное при возврате появляется таким же. Мебель пока статична
    /// (физика и лут — этап 5); объекты мебели переиспользуются через пул.
    /// </summary>
    [DefaultExecutionOrder(-190)]
    public sealed class MallStructures : MonoBehaviour
    {
        public static readonly CVar<int> PropBudget = new("world_prop_budget", 200, "Сколько предметов мебели создавать за кадр.");
        public static readonly CVar<float> PropRadius = new("world_prop_radius", 80f, "Радиус появления мебели, м.");
        public static readonly CVar<int> PropLevels = new("world_prop_levels", 1, "Мебель на скольких уровнях выше и ниже игрока.");
        public static readonly CVar<bool> ShowProps = new("world_props", true, "Показывать мебель-заглушки.");

        [SerializeField] private Material _material;

        private readonly struct BucketKey : System.IEquatable<BucketKey>
        {
            public readonly int2 Cell;
            public readonly int Level;

            public BucketKey(int2 cell, int level)
            {
                Cell = cell;
                Level = level;
            }

            public bool Equals(BucketKey other) => Cell.Equals(other.Cell) && Level == other.Level;
            public override bool Equals(object obj) => obj is BucketKey other && Equals(other);
            public override int GetHashCode() => Cell.GetHashCode() * 31 + Level;
        }

        private sealed class PropBucket
        {
            public BucketKey Key;
            public Transform Root;
            public readonly List<PropSpawn> Pending = new();
            public readonly List<GameObject> Props = new();
            public int PendingIndex;
        }

        private readonly Dictionary<int2, GameObject> _cells = new();
        private readonly Dictionary<BucketKey, PropBucket> _buckets = new();
        private readonly List<int2> _cellsToUnload = new();
        private readonly List<BucketKey> _bucketsToUnload = new();
        private readonly List<(float distance, int2 cell)> _cellsToLoad = new();
        private readonly List<(float distance, BucketKey key)> _bucketsToLoad = new();
        private readonly Stack<GameObject> _propPool = new();
        private readonly List<Vector3> _viewers = new();
        private Transform _root;
        private Transform _poolRoot;
        private ChunkManager _world;

        public int LoadedCells => _cells.Count;
        public int LoadedBuckets => _buckets.Count;
        public int PropCount { get; private set; }

        private void Awake()
        {
            _root = new GameObject("Mall Structures").transform;
            _root.SetParent(transform, false);
            _poolRoot = new GameObject("Prop Pool").transform;
            _poolRoot.SetParent(transform, false);
            _poolRoot.gameObject.SetActive(false);
        }

        private void OnEnable()
        {
            _world = GetComponent<ChunkManager>();
            if (_world == null)
                _world = ChunkManager.Instance;
            if (_world != null)
                _world.WorldReset += UnloadAll;
            ShowProps.Changed += ApplyPropVisibility;
        }

        private void OnDisable()
        {
            if (_world != null)
                _world.WorldReset -= UnloadAll;
            ShowProps.Changed -= ApplyPropVisibility;
        }

        private void Update()
        {
            if (_world == null)
                _world = ChunkManager.Instance;
            if (_world == null || _world.Sections == null)
                return;

            RefreshViewers();
            if (_viewers.Count == 0)
                return;

            UpdateStructures();
            UpdateProps();
            SpawnPendingProps();
        }

        // ───────────────────────────── Структуры ─────────────────────────────

        private void UpdateStructures()
        {
            float loadRadius = ChunkManager.ViewDistance.Value * WorldConstants.ChunkSize;
            float unloadRadius = loadRadius + MallLayout.CellSize;

            _cellsToUnload.Clear();
            foreach (var cell in _cells.Keys)
                if (DistanceToViewers(cell) > unloadRadius)
                    _cellsToUnload.Add(cell);
            foreach (var cell in _cellsToUnload)
            {
                Destroy(_cells[cell]);
                _cells.Remove(cell);
            }

            _cellsToLoad.Clear();
            foreach (var cell in CellsAround(loadRadius))
                if (!_cells.ContainsKey(cell))
                    _cellsToLoad.Add((DistanceToViewers(cell), cell));
            if (_cellsToLoad.Count == 0)
                return;
            _cellsToLoad.Sort((a, b) => a.distance.CompareTo(b.distance));

            // Одна ячейка за кадр: структуры — это десятки объектов.
            int2 next = _cellsToLoad[0].cell;
            var root = new GameObject($"Cell {next.x},{next.y}");
            root.transform.SetParent(_root, false);
            StructureFactory.Build(_world.Seed, _world.GetCellInfo(next), root.transform, _material);
            _cells.Add(next, root);
        }

        // ───────────────────────────── Мебель ─────────────────────────────

        private void UpdateProps()
        {
            float radius = PropRadius.Value;
            int levelSpan = Mathf.Max(0, PropLevels.Value);
            int viewerLevel = MallLayout.LevelAtFeet(_viewers[0].y);

            _bucketsToUnload.Clear();
            foreach (var key in _buckets.Keys)
                if (DistanceToViewers(key.Cell) > radius + 16f || math.abs(key.Level - viewerLevel) > levelSpan + 1)
                    _bucketsToUnload.Add(key);
            foreach (var key in _bucketsToUnload)
                UnloadBucket(key);

            _bucketsToLoad.Clear();
            foreach (var cell in CellsAround(radius))
            {
                float distance = DistanceToViewers(cell);
                for (int level = viewerLevel - levelSpan; level <= viewerLevel + levelSpan; level++)
                {
                    if (level < 0 || level >= MallLayout.LevelCount)
                        continue;
                    var key = new BucketKey(cell, level);
                    if (!_buckets.ContainsKey(key))
                        _bucketsToLoad.Add((distance + math.abs(level - viewerLevel) * 40f, key));
                }
            }
            if (_bucketsToLoad.Count == 0)
                return;
            _bucketsToLoad.Sort((a, b) => a.distance.CompareTo(b.distance));
            LoadBucket(_bucketsToLoad[0].key);
        }

        private void LoadBucket(BucketKey key)
        {
            var info = _world.GetCellInfo(key.Cell);
            var bucket = new PropBucket { Key = key };
            bucket.Root = new GameObject($"Props {key.Cell.x},{key.Cell.y} {MallLayout.LevelName(key.Level)}").transform;
            bucket.Root.SetParent(_root, false);

            var plan = new MallLevelPlan(Allocator.Temp);
            try
            {
                MallPlanner.BuildLevel(_world.Seed, info, key.Level, _world.Sections.Infos, ref plan);
                PropPlacer.Place(_world.Seed, info, plan, _world.Sections.Infos, bucket.Pending);
            }
            finally
            {
                plan.Dispose();
            }
            Vector3 viewer = _viewers[0];
            bucket.Pending.Sort((a, b) => (a.Position - viewer).sqrMagnitude.CompareTo((b.Position - viewer).sqrMagnitude));
            _buckets.Add(key, bucket);
        }

        private void SpawnPendingProps()
        {
            int budget = Mathf.Max(1, PropBudget.Value);
            foreach (var bucket in _buckets.Values)
            {
                while (bucket.PendingIndex < bucket.Pending.Count && budget > 0)
                {
                    var spawn = bucket.Pending[bucket.PendingIndex++];
                    bucket.Props.Add(SpawnProp(spawn, bucket.Root));
                    budget--;
                }
                if (bucket.PendingIndex >= bucket.Pending.Count && bucket.Pending.Count > 0)
                {
                    bucket.Pending.Clear();
                    bucket.PendingIndex = 0;
                }
                if (budget <= 0)
                    break;
            }
        }

        private GameObject SpawnProp(in PropSpawn spawn, Transform parent)
        {
            GameObject go;
            if (_propPool.Count > 0)
            {
                go = _propPool.Pop();
                go.transform.SetParent(parent, false);
            }
            else
            {
                go = new GameObject("Prop") { layer = GameLayers.Props };
                go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>();
                go.AddComponent<MeshRenderer>().sharedMaterial = _material;
                go.AddComponent<BoxCollider>();
            }
            go.name = spawn.Kind.ToString();
            go.transform.SetPositionAndRotation(spawn.Position, Quaternion.Euler(0f, spawn.Yaw, 0f));
            go.GetComponent<MeshFilter>().sharedMesh = PropCatalog.GetMesh(spawn.Kind);
            go.GetComponent<MeshRenderer>().enabled = ShowProps.Value;
            var box = go.GetComponent<BoxCollider>();
            Vector3 size = PropCatalog.Size(spawn.Kind);
            box.size = size;
            box.center = new Vector3(0f, size.y * 0.5f, 0f);
            box.enabled = ShowProps.Value;
            PropCount++;
            return go;
        }

        private void UnloadBucket(BucketKey key)
        {
            var bucket = _buckets[key];
            foreach (var prop in bucket.Props)
            {
                prop.transform.SetParent(_poolRoot, false);
                _propPool.Push(prop);
                PropCount--;
            }
            Destroy(bucket.Root.gameObject);
            _buckets.Remove(key);
        }

        private void UnloadAll()
        {
            foreach (var key in new List<BucketKey>(_buckets.Keys))
                UnloadBucket(key);
            foreach (var root in _cells.Values)
                Destroy(root);
            _cells.Clear();
        }

        private void ApplyPropVisibility()
        {
            foreach (var bucket in _buckets.Values)
            foreach (var prop in bucket.Props)
            {
                prop.GetComponent<MeshRenderer>().enabled = ShowProps.Value;
                prop.GetComponent<BoxCollider>().enabled = ShowProps.Value;
            }
        }

        // ───────────────────────────── Наблюдатели ─────────────────────────────

        private readonly HashSet<int2> _around = new();

        private HashSet<int2> CellsAround(float radius)
        {
            _around.Clear();
            int reach = (int)math.ceil(radius / MallLayout.CellSize) + 1;
            foreach (var viewer in _viewers)
            {
                int2 center = MallLayout.CellOf(viewer);
                for (int dz = -reach; dz <= reach; dz++)
                for (int dx = -reach; dx <= reach; dx++)
                {
                    int2 cell = center + new int2(dx, dz);
                    if (DistanceToViewers(cell) <= radius)
                        _around.Add(cell);
                }
            }
            return _around;
        }

        private float DistanceToViewers(int2 cell)
        {
            float2 min = MallLayout.CellOrigin(cell);
            float2 max = min + MallLayout.CellSize;
            float best = float.MaxValue;
            foreach (var viewer in _viewers)
            {
                float2 p = new(viewer.x, viewer.z);
                best = math.min(best, math.distance(p, math.clamp(p, min, max)));
            }
            return best;
        }

        private void RefreshViewers()
        {
            _viewers.Clear();
            var primary = WorldViewer.Primary;
            if (primary != null)
                _viewers.Add(primary.transform.position);
        }
    }
}
