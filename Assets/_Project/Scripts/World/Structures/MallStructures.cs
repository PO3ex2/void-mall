using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace VoidMall.World
{
    /// <summary>
    /// Стриминг готовых структур (лестничные марши, лифты, эскалаторы, пандусы) по макроячейкам в радиусе прогрузки мира.
    /// Всё выводится из плана, поэтому выгруженная ячейка при возврате появляется такой же. Пропы — PropManager.
    /// </summary>
    [DefaultExecutionOrder(-190)]
    public sealed class MallStructures : MonoBehaviour
    {
        [SerializeField] private Material _material;

        private readonly Dictionary<int2, GameObject> _cells = new();
        private readonly List<int2> _toUnload = new();
        private readonly List<(float distance, int2 cell)> _toLoad = new();
        private Transform _root;
        private ChunkManager _world;

        public int LoadedCells => _cells.Count;

        private void Awake()
        {
            _root = new GameObject("Mall Structures").transform;
            _root.SetParent(transform, false);
        }

        private void OnEnable()
        {
            _world = GetComponent<ChunkManager>();
            if (_world == null)
                _world = ChunkManager.Instance;
            if (_world != null)
                _world.WorldReset += UnloadAll;
        }

        private void OnDisable()
        {
            if (_world != null)
                _world.WorldReset -= UnloadAll;
        }

        private void Update()
        {
            if (_world == null)
                _world = ChunkManager.Instance;
            var viewer = WorldViewer.Primary;
            if (_world == null || _world.Sections == null || viewer == null)
                return;

            Vector3 position = viewer.transform.position;
            float loadRadius = ChunkManager.ViewDistance.Value * WorldConstants.ChunkSize;
            float unloadRadius = loadRadius + MallLayout.CellSize;

            _toUnload.Clear();
            foreach (var cell in _cells.Keys)
                if (Distance(cell, position) > unloadRadius)
                    _toUnload.Add(cell);
            foreach (var cell in _toUnload)
            {
                Destroy(_cells[cell]);
                _cells.Remove(cell);
            }

            _toLoad.Clear();
            int reach = (int)math.ceil(loadRadius / MallLayout.CellSize) + 1;
            int2 center = MallLayout.CellOf(position);
            for (int dz = -reach; dz <= reach; dz++)
            for (int dx = -reach; dx <= reach; dx++)
            {
                int2 cell = center + new int2(dx, dz);
                float distance = Distance(cell, position);
                if (distance <= loadRadius && !_cells.ContainsKey(cell))
                    _toLoad.Add((distance, cell));
            }
            if (_toLoad.Count == 0)
                return;
            _toLoad.Sort((a, b) => a.distance.CompareTo(b.distance));

            // Одна ячейка за кадр: структуры — это десятки объектов.
            int2 next = _toLoad[0].cell;
            var root = new GameObject($"Cell {next.x},{next.y}");
            root.transform.SetParent(_root, false);
            StructureFactory.Build(_world.Seed, _world.GetCellInfo(next), root.transform, _material);
            _cells.Add(next, root);
        }

        private void UnloadAll()
        {
            foreach (var root in _cells.Values)
                Destroy(root);
            _cells.Clear();
        }

        private static float Distance(int2 cell, Vector3 position)
        {
            float2 min = MallLayout.CellOrigin(cell);
            float2 p = new(position.x, position.z);
            return math.distance(p, math.clamp(p, min, min + MallLayout.CellSize));
        }
    }
}
