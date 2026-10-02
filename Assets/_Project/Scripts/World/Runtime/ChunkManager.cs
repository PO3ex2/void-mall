using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using VoidMall.Console;
using VoidMall.Core;

namespace VoidMall.World
{
    /// <summary>
    /// Мир из чанков: стриминг колонн вокруг наблюдателей, генерация и мешинг в Burst-джобах с бюджетом на кадр,
    /// фоновое запекание коллайдеров, правки клеток с мгновенной перестройкой.
    /// Порядок кадра: Update — забрать готовые джобы, правки, стриминг; LateUpdate — запустить новые джобы.
    /// Поэтому во время чужих Update джобы не работают и данные чанков можно безопасно менять.
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public sealed class ChunkManager : MonoBehaviour, IWorldCollisionQuery
    {
        public static readonly CVar<int> ViewDistance = new("r_viewdistance", 6, "Радиус прогрузки мира в колоннах (1 колонна = 32 м).");
        public static readonly CVar<int> GenerationBudget = new("world_gen_budget", 16, "Сколько чанков генерировать за кадр.");
        public static readonly CVar<int> MeshBudget = new("world_mesh_budget", 24, "Сколько чанков мешить за кадр.");

        [SerializeField] private MaterialCatalog _catalog;
        [SerializeField] private Material _opaqueMaterial;
        [SerializeField] private Material _transparentMaterial;
        [SerializeField] private uint _seed = 1337;

        public static ChunkManager Instance { get; private set; }

        private static readonly List<Transform> Viewers = new();

        private readonly Dictionary<int2, ChunkColumn> _columns = new();
        private readonly Dictionary<int2, ChunkColumn> _parked = new();
        private readonly Stack<ChunkRenderer> _rendererPool = new();
        private readonly Stack<MeshBuffers> _bufferPool = new();
        private readonly List<GenerationTask> _generationTasks = new();
        private readonly List<MeshTask> _meshTasks = new();
        private readonly List<BakeEntry> _bakeQueue = new();
        private readonly List<BakeEntry> _bakeInFlight = new();
        private readonly List<Candidate> _candidates = new();
        private readonly HashSet<int2> _candidateSet = new();
        private readonly List<int2> _toUnload = new();
        private readonly List<(ChunkColumn column, int index)> _immediate = new();

        private MaterialRegistry _registry;
        private Texture2DArray _textures;
        private Material _opaque;
        private Material _transparent;
        private TestWorldMaterials _generatorMaterials;
        private ChunkData _airChunk;
        private ChunkData _bedrockChunk;
        private Transform _chunkRoot;
        private NativeArray<UnityEngine.EntityId> _bakeIds;
        private JobHandle _bakeHandle;
        private int _batchDepth;

        public MaterialRegistry Materials => _registry;
        public uint Seed => _seed;
        public int LoadedColumns => _columns.Count;
        public int ParkedColumns => _parked.Count;
        public int GenerationTasksInFlight => _generationTasks.Count;
        public int MeshTasksInFlight => _meshTasks.Count;

        /// <summary>Клетка изменилась (после применения и перестройки меша).</summary>
        public event Action<int3, Cell> CellChanged;

        // ───────────────────────────── Жизненный цикл ─────────────────────────────

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogError("[World] Only one ChunkManager is allowed.", this);
                enabled = false;
                return;
            }
            Instance = this;

            _registry = new MaterialRegistry(_catalog);
            _textures = BlockTextureArrayBuilder.Build(_registry.All, out var layers);
            _registry.BuildInfos(layers);
            _generatorMaterials = TestWorldMaterials.Resolve(_registry);

            _opaque = new Material(_opaqueMaterial) { name = "Blocks (runtime)" };
            _transparent = new Material(_transparentMaterial) { name = "Blocks Transparent (runtime)" };
            _opaque.SetTexture(BlockTexturesId, _textures);
            _transparent.SetTexture(BlockTexturesId, _textures);
            _transparent.SetShaderPassEnabled("ShadowCaster", false);

            _airChunk = new ChunkData(new int3(0, WorldConstants.ChunksPerColumn, 0), Cell.Air);
            _bedrockChunk = new ChunkData(new int3(0, -1, 0), new Cell(_generatorMaterials.Bedrock));

            _chunkRoot = new GameObject("Chunks").transform;
            _chunkRoot.SetParent(transform, false);
        }

        private static readonly int BlockTexturesId = Shader.PropertyToID("_BlockTextures");

        private void OnEnable() => Services.Register<IWorldCollisionQuery>(this);

        private void OnDisable() => Services.Unregister<IWorldCollisionQuery>(this);

        private void OnDestroy()
        {
            if (Instance != this)
                return;
            CompleteAllJobs();
            foreach (var column in _columns.Values)
                column.Dispose();
            foreach (var column in _parked.Values)
                column.Dispose();
            _columns.Clear();
            _parked.Clear();
            foreach (var buffers in _bufferPool)
                buffers.Dispose();
            _bufferPool.Clear();
            _airChunk?.Dispose();
            _bedrockChunk?.Dispose();
            _registry?.Dispose();
            if (_textures != null)
                Destroy(_textures);
            Destroy(_opaque);
            Destroy(_transparent);
            Instance = null;
        }

        public static void RegisterViewer(Transform viewer)
        {
            if (!Viewers.Contains(viewer))
                Viewers.Add(viewer);
        }

        public static void UnregisterViewer(Transform viewer) => Viewers.Remove(viewer);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Viewers.Clear();
            Instance = null;
        }

        private void Update()
        {
            CompleteGeneration();
            CompleteMeshing();
            CompleteBaking();
            UpdateStreaming();
        }

        private void LateUpdate()
        {
            ScheduleGeneration();
            ScheduleMeshing();
            ScheduleBaking();
            JobHandle.ScheduleBatchedJobs();
        }

        // ───────────────────────────── Доступ к клеткам ─────────────────────────────

        public Cell GetCell(int3 cell)
        {
            if (cell.y < 0)
                return _bedrockChunk.Get(0);
            if (cell.y >= WorldConstants.WorldHeight)
                return Cell.Air;
            if (!TryGetChunk(cell, out var chunk, out _, out _))
                return Cell.Air;
            return chunk.Get(WorldConstants.CellToLocal(cell));
        }

        public bool IsLoaded(int3 cell) => TryGetChunk(cell, out _, out _, out _);

        public int GetDamage(int3 cell) =>
            TryGetChunk(cell, out var chunk, out _, out _) ? chunk.GetDamage(WorldConstants.CellIndex(WorldConstants.CellToLocal(cell))) : 0;

        public void SetDamage(int3 cell, int damage)
        {
            if (TryGetChunk(cell, out var chunk, out _, out _))
                chunk.SetDamage(WorldConstants.CellIndex(WorldConstants.CellToLocal(cell)), damage);
        }

        /// <summary>Записать клетку. Меш перестраивается сразу (или в конце пакета правок).</summary>
        public bool SetCell(int3 cell, Cell value)
        {
            if (!WorldConstants.IsInsideWorldHeight(cell.y) || !TryGetChunk(cell, out var chunk, out var column, out int index))
                return false;

            CompleteAllJobs();
            int cellIndex = WorldConstants.CellIndex(WorldConstants.CellToLocal(cell));
            if (!chunk.Set(cellIndex, value))
                return false;
            chunk.IsModified = true;
            chunk.SetDamage(cellIndex, 0);
            MarkDirtyAround(cell);

            if (_batchDepth == 0)
                RebuildDirtyNow();
            CellChanged?.Invoke(cell, value);
            return true;
        }

        /// <summary>Пакет правок (fill, взрыв): меши перестраиваются один раз в конце.</summary>
        public EditBatch BeginBatch()
        {
            _batchDepth++;
            return new EditBatch(this);
        }

        public readonly struct EditBatch : IDisposable
        {
            private readonly ChunkManager _owner;
            public EditBatch(ChunkManager owner) => _owner = owner;

            public void Dispose()
            {
                if (_owner == null)
                    return;
                _owner._batchDepth--;
                if (_owner._batchDepth == 0)
                    _owner.RebuildDirtyNow();
            }
        }

        private bool TryGetChunk(int3 cell, out ChunkData chunk, out ChunkColumn column, out int index)
        {
            chunk = null;
            index = cell.y >> WorldConstants.ChunkShift;
            if (index < 0 || index >= WorldConstants.ChunksPerColumn
                || !_columns.TryGetValue(WorldConstants.CellToColumn(cell), out column)
                || column.State != ColumnState.Generated)
            {
                column = null;
                return false;
            }
            chunk = column.Chunks[index];
            return chunk != null;
        }

        /// <summary>Помечает чанк клетки и соседние чанки, если клетка на границе (грани и AO соседей зависят от неё).</summary>
        private void MarkDirtyAround(int3 cell)
        {
            int3 local = WorldConstants.CellToLocal(cell);
            int3 chunk = WorldConstants.CellToChunk(cell);
            const int last = WorldConstants.ChunkSize - 1;
            int minX = local.x == 0 ? -1 : 0, maxX = local.x == last ? 1 : 0;
            int minY = local.y == 0 ? -1 : 0, maxY = local.y == last ? 1 : 0;
            int minZ = local.z == 0 ? -1 : 0, maxZ = local.z == last ? 1 : 0;

            for (int dy = minY; dy <= maxY; dy++)
            for (int dz = minZ; dz <= maxZ; dz++)
            for (int dx = minX; dx <= maxX; dx++)
            {
                int3 target = chunk + new int3(dx, dy, dz);
                if (target.y < 0 || target.y >= WorldConstants.ChunksPerColumn)
                    continue;
                if (!_columns.TryGetValue(new int2(target.x, target.z), out var column) || column.State != ColumnState.Generated)
                    continue;
                column.MeshDirty[target.y] = true;
                var entry = (column, target.y);
                if (!_immediate.Contains(entry))
                    _immediate.Add(entry);
            }
        }

        /// <summary>Перестроить помеченные правками чанки прямо сейчас (Burst на главном потоке + синхронное запекание).</summary>
        private void RebuildDirtyNow()
        {
            foreach (var (column, index) in _immediate)
            {
                if (!_columns.ContainsKey(column.Coord) || !column.MeshDirty[index] || !AreNeighborsGenerated(column.Coord))
                    continue;

                column.MeshDirty[index] = false;
                var buffers = RentBuffers();
                var views = BuildViews(new int3(column.Coord.x, index, column.Coord.y), Allocator.TempJob);
                CreateMeshJob(column.Coord, index, views, buffers).Run();
                views.Dispose();

                ApplyMesh(column, index, buffers, queueBake: false);
                ReturnBuffers(buffers);

                var renderer = column.Renderers[index];
                if (renderer != null && renderer.HasGeometry)
                {
                    Physics.BakeMesh(renderer.Mesh.GetEntityId(), false);
                    renderer.ApplyCollider();
                }
                column.CollisionReady[index] = true;
            }
            _immediate.Clear();
        }

        // ───────────────────────────── Стриминг ─────────────────────────────

        private void UpdateStreaming()
        {
            if (Viewers.Count == 0)
                return;

            int keepRadius = ViewDistance.Value + 3;
            _toUnload.Clear();
            foreach (var coord in _columns.Keys)
            {
                if (MinViewerDistanceSq(coord) > keepRadius * keepRadius)
                    _toUnload.Add(coord);
            }
            foreach (var coord in _toUnload)
                UnloadColumn(coord);
        }

        private void UnloadColumn(int2 coord)
        {
            var column = _columns[coord];
            _columns.Remove(coord);
            for (int i = 0; i < WorldConstants.ChunksPerColumn; i++)
            {
                ReleaseRenderer(column, i);
                column.HasMesh[i] = false;
                column.CollisionReady[i] = false;
            }
            column.MarkAllDirty();

            // До этапа 9 (сохранения) изменённые колонны держим в памяти, чтобы правки не терялись.
            if (column.IsModified)
                _parked[coord] = column;
            else
                column.Dispose();
        }

        private float MinViewerDistanceSq(int2 coord)
        {
            float best = float.MaxValue;
            foreach (var viewer in Viewers)
            {
                if (viewer == null)
                    continue;
                float2 center = ViewerColumn(viewer);
                float2 delta = new float2(coord) - center;
                best = math.min(best, math.lengthsq(delta));
            }
            return best;
        }

        private static float2 ViewerColumn(Transform viewer)
        {
            Vector3 p = viewer.position;
            return new float2(p.x, p.z) / WorldConstants.ChunkSize - 0.5f;
        }

        private void CollectCandidates(int radius, Func<int2, bool> accept)
        {
            _candidates.Clear();
            _candidateSet.Clear();
            foreach (var viewer in Viewers)
            {
                if (viewer == null)
                    continue;
                float2 center = ViewerColumn(viewer);
                int2 c = (int2)math.round(center);
                for (int dz = -radius; dz <= radius; dz++)
                for (int dx = -radius; dx <= radius; dx++)
                {
                    int2 coord = c + new int2(dx, dz);
                    float distanceSq = math.lengthsq(new float2(coord) - center);
                    if (distanceSq > (radius + 0.5f) * (radius + 0.5f) || !_candidateSet.Add(coord) || !accept(coord))
                        continue;
                    _candidates.Add(new Candidate { Coord = coord, DistanceSq = distanceSq });
                }
            }
            _candidates.Sort((a, b) => a.DistanceSq.CompareTo(b.DistanceSq));
        }

        // ───────────────────────────── Генерация ─────────────────────────────

        private void ScheduleGeneration()
        {
            if (Viewers.Count == 0)
                return;
            int generateRadius = ViewDistance.Value + 1;
            CollectCandidates(generateRadius, coord => !_columns.ContainsKey(coord));

            int budget = Mathf.Max(1, GenerationBudget.Value / WorldConstants.ChunksPerColumn);
            for (int i = 0; i < _candidates.Count && budget > 0; i++, budget--)
            {
                int2 coord = _candidates[i].Coord;
                if (_parked.Remove(coord, out var parked))
                {
                    parked.MarkAllDirty();
                    _columns.Add(coord, parked);
                    continue;
                }

                var column = new ChunkColumn(coord) { State = ColumnState.Generating };
                _columns.Add(coord, column);
                for (int y = 0; y < WorldConstants.ChunksPerColumn; y++)
                {
                    var chunk = ChunkData.CreateForGeneration(new int3(coord.x, y, coord.y));
                    column.Chunks[y] = chunk;
                    var uniform = new NativeArray<byte>(1, Allocator.TempJob);
                    var job = new TestWorldGenerationJob
                    {
                        Chunk = chunk.Coord,
                        Seed = _seed,
                        M = _generatorMaterials,
                        Cells = chunk.Cells,
                        IsUniform = uniform,
                    };
                    _generationTasks.Add(new GenerationTask { Column = column, Chunk = chunk, Uniform = uniform, Handle = job.Schedule() });
                    column.PendingGenerationJobs++;
                }
            }
        }

        private void CompleteGeneration()
        {
            foreach (var task in _generationTasks)
            {
                task.Handle.Complete();
                if (task.Uniform[0] != 0)
                    task.Chunk.SetUniform(task.Chunk.Cells[0]);
                task.Uniform.Dispose();
                if (--task.Column.PendingGenerationJobs == 0)
                    task.Column.State = ColumnState.Generated;
            }
            _generationTasks.Clear();
        }

        // ───────────────────────────── Мешинг ─────────────────────────────

        private void ScheduleMeshing()
        {
            if (Viewers.Count == 0)
                return;
            CollectCandidates(ViewDistance.Value, coord =>
                _columns.TryGetValue(coord, out var column)
                && column.State == ColumnState.Generated
                && NeedsMesh(column)
                && AreNeighborsGenerated(coord));

            int budget = MeshBudget.Value;
            for (int i = 0; i < _candidates.Count && budget > 0; i++)
            {
                var column = _columns[_candidates[i].Coord];
                for (int y = 0; y < WorldConstants.ChunksPerColumn && budget > 0; y++)
                {
                    if (!column.MeshDirty[y] || column.MeshInFlight[y])
                        continue;
                    var buffers = RentBuffers();
                    var views = BuildViews(new int3(column.Coord.x, y, column.Coord.y), Allocator.TempJob);
                    var handle = CreateMeshJob(column.Coord, y, views, buffers).Schedule();
                    _meshTasks.Add(new MeshTask { Column = column, Index = y, Views = views, Buffers = buffers, Handle = handle });
                    column.MeshInFlight[y] = true;
                    column.MeshDirty[y] = false;
                    budget--;
                }
            }
        }

        private void CompleteMeshing()
        {
            foreach (var task in _meshTasks)
            {
                task.Handle.Complete();
                task.Views.Dispose();
                task.Column.MeshInFlight[task.Index] = false;
                if (_columns.ContainsKey(task.Column.Coord))
                    ApplyMesh(task.Column, task.Index, task.Buffers, queueBake: true);
                ReturnBuffers(task.Buffers);
            }
            _meshTasks.Clear();
        }

        private ChunkMeshJob CreateMeshJob(int2 coord, int y, NativeArray<ChunkView> views, MeshBuffers buffers) => new()
        {
            Materials = _registry.Infos,
            Views = views,
            ChunkOrigin = new int3(coord.x, y, coord.y) * WorldConstants.ChunkSize,
            Vertices = buffers.Vertices,
            OpaqueIndices = buffers.Opaque,
            TransparentIndices = buffers.Transparent,
        };

        private void ApplyMesh(ChunkColumn column, int index, MeshBuffers buffers, bool queueBake)
        {
            column.HasMesh[index] = true;
            if (buffers.Vertices.Length == 0)
            {
                ReleaseRenderer(column, index);
                column.CollisionReady[index] = true;
                return;
            }

            var renderer = column.Renderers[index];
            if (renderer == null)
            {
                renderer = RentRenderer();
                renderer.Place(new int3(column.Coord.x, index, column.Coord.y));
                column.Renderers[index] = renderer;
            }
            renderer.Apply(buffers.Vertices, buffers.Opaque, buffers.Transparent);
            column.MeshVersion[index]++;
            column.CollisionReady[index] = false;
            if (queueBake)
                _bakeQueue.Add(new BakeEntry { Column = column, Index = index, Version = column.MeshVersion[index], Renderer = renderer });
        }

        private NativeArray<ChunkView> BuildViews(int3 chunk, Allocator allocator)
        {
            var views = new NativeArray<ChunkView>(27, allocator, NativeArrayOptions.UninitializedMemory);
            for (int dy = -1; dy <= 1; dy++)
            for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int3 target = chunk + new int3(dx, dy, dz);
                views[(dx + 1) + (dy + 1) * 3 + (dz + 1) * 9] = GetChunkForView(target).GetView();
            }
            return views;
        }

        private ChunkData GetChunkForView(int3 chunk)
        {
            if (chunk.y < 0)
                return _bedrockChunk;
            if (chunk.y >= WorldConstants.ChunksPerColumn)
                return _airChunk;
            return _columns[new int2(chunk.x, chunk.z)].Chunks[chunk.y];
        }

        private static bool NeedsMesh(ChunkColumn column)
        {
            for (int i = 0; i < WorldConstants.ChunksPerColumn; i++)
                if (column.MeshDirty[i] && !column.MeshInFlight[i])
                    return true;
            return false;
        }

        private bool AreNeighborsGenerated(int2 coord)
        {
            for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (!_columns.TryGetValue(coord + new int2(dx, dz), out var neighbor) || neighbor.State != ColumnState.Generated)
                    return false;
            }
            return true;
        }

        // ───────────────────────────── Коллайдеры ─────────────────────────────

        private struct BakeJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<UnityEngine.EntityId> Meshes;

            public void Execute(int index) => Physics.BakeMesh(Meshes[index], false);
        }

        private void ScheduleBaking()
        {
            if (_bakeQueue.Count == 0)
                return;
            _bakeIds = new NativeArray<UnityEngine.EntityId>(_bakeQueue.Count, Allocator.TempJob);
            for (int i = 0; i < _bakeQueue.Count; i++)
                _bakeIds[i] = _bakeQueue[i].Renderer.Mesh.GetEntityId();
            _bakeHandle = new BakeJob { Meshes = _bakeIds }.Schedule(_bakeIds.Length, 1);
            _bakeInFlight.AddRange(_bakeQueue);
            _bakeQueue.Clear();
        }

        private void CompleteBaking()
        {
            if (!_bakeIds.IsCreated)
                return;
            _bakeHandle.Complete();
            _bakeIds.Dispose();
            foreach (var entry in _bakeInFlight)
            {
                // Колонну могли выгрузить, а меш — перестроить заново: применяем только актуальное запекание.
                if (!_columns.ContainsKey(entry.Column.Coord)
                    || entry.Column.Renderers[entry.Index] != entry.Renderer
                    || entry.Column.MeshVersion[entry.Index] != entry.Version)
                    continue;
                entry.Renderer.ApplyCollider();
                entry.Column.CollisionReady[entry.Index] = true;
            }
            _bakeInFlight.Clear();
        }

        private void CompleteAllJobs()
        {
            if (_generationTasks.Count == 0 && _meshTasks.Count == 0 && !_bakeIds.IsCreated)
                return;
            CompleteGeneration();
            CompleteMeshing();
            CompleteBaking();
        }

        // ───────────────────────────── IWorldCollisionQuery ─────────────────────────────

        public bool IsCollisionReady(Vector3 position)
        {
            int3 cell = (int3)math.floor((float3)position);
            if (!WorldConstants.IsInsideWorldHeight(cell.y))
                return true;
            if (!_columns.TryGetValue(WorldConstants.CellToColumn(cell), out var column) || column.State != ColumnState.Generated)
                return false;
            int index = cell.y >> WorldConstants.ChunkShift;
            return IsChunkCollisionReady(column, index) && (index == 0 || IsChunkCollisionReady(column, index - 1));
        }

        private static bool IsChunkCollisionReady(ChunkColumn column, int index) =>
            column.HasMesh[index] && !column.MeshInFlight[index] && column.CollisionReady[index];

        // ───────────────────────────── Пулы ─────────────────────────────

        private ChunkRenderer RentRenderer()
        {
            if (_rendererPool.Count > 0)
            {
                var pooled = _rendererPool.Pop();
                pooled.gameObject.SetActive(true);
                return pooled;
            }
            return ChunkRenderer.Create(_chunkRoot, _opaque, _transparent);
        }

        private void ReleaseRenderer(ChunkColumn column, int index)
        {
            var renderer = column.Renderers[index];
            if (renderer == null)
                return;
            renderer.ResetForPool();
            _rendererPool.Push(renderer);
            column.Renderers[index] = null;
        }

        private MeshBuffers RentBuffers() => _bufferPool.Count > 0 ? _bufferPool.Pop() : new MeshBuffers();

        private void ReturnBuffers(MeshBuffers buffers) => _bufferPool.Push(buffers);

        private struct Candidate
        {
            public int2 Coord;
            public float DistanceSq;
        }

        private struct GenerationTask
        {
            public ChunkColumn Column;
            public ChunkData Chunk;
            public NativeArray<byte> Uniform;
            public JobHandle Handle;
        }

        private struct MeshTask
        {
            public ChunkColumn Column;
            public int Index;
            public NativeArray<ChunkView> Views;
            public MeshBuffers Buffers;
            public JobHandle Handle;
        }

        private struct BakeEntry
        {
            public ChunkColumn Column;
            public int Index;
            public int Version;
            public ChunkRenderer Renderer;
        }

        private sealed class MeshBuffers : IDisposable
        {
            public NativeList<ChunkVertex> Vertices = new(4096, Allocator.Persistent);
            public NativeList<int> Opaque = new(6144, Allocator.Persistent);
            public NativeList<int> Transparent = new(256, Allocator.Persistent);

            public void Dispose()
            {
                Vertices.Dispose();
                Opaque.Dispose();
                Transparent.Dispose();
            }
        }
    }
}
