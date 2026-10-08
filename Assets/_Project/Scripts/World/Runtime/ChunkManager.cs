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
    public sealed class ChunkManager : MonoBehaviour, IWorldCollisionQuery, IVoxelAccess
    {
        public static readonly CVar<int> ViewDistance = new("r_viewdistance", 6, "Радиус прогрузки мира в колоннах (1 колонна = 32 м).", ConsoleFlags.Archive);
        public static readonly CVar<int> GenerationBudget = new("world_gen_budget", 16, "Сколько чанков генерировать за кадр.");
        public static readonly CVar<int> MeshBudget = new("world_mesh_budget", 24, "Сколько чанков мешить за кадр.");
        public static readonly CVar<float> ApplyBudgetMs = new("world_apply_budget_ms", 2f, "Сколько миллисекунд за кадр подключать готовые меши и коллайдеры чанков (остальное — в следующих кадрах).");

        [SerializeField] private MaterialCatalog _catalog;
        [SerializeField] private Material _opaqueMaterial;
        [SerializeField] private Material _transparentMaterial;
        [SerializeField] private Material _waterMaterial;
        [SerializeField] private WorldSettings _settings;

        [Tooltip("Сид, если настройки мира не заданы.")]
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

        // Готовые меши и запечённые коллайдеры ждут подключения: за кадр — сколько влезет в ApplyBudgetMs,
        // иначе несколько чанков, доделанных одновременно, давали рывок в десятки миллисекунд.
        private readonly List<MeshTask> _meshReady = new();
        private readonly List<BakeEntry> _bakeReady = new();
        private readonly List<Candidate> _candidates = new();
        private readonly HashSet<int2> _candidateSet = new();
        private readonly List<int2> _toUnload = new();
        private readonly List<(ChunkColumn column, int index)> _immediate = new();

        private MaterialRegistry _registry;
        private Texture2DArray _textures;
        private Material _opaque;
        private Material _transparent;
        private MallMaterials _generatorMaterials;
        private NativeArray<ShapeVertex> _shapeVertices;
        private NativeArray<int2> _shapeRanges;
        private SectionTable _sections;
        private float _anomalyFrequency = 1f;
        private readonly Dictionary<int2, MallCellInfo> _cellInfos = new();
        private ChunkData _airChunk;
        private ChunkData _bedrockChunk;
        private Transform _chunkRoot;
        private NativeArray<UnityEngine.EntityId> _bakeIds;
        private JobHandle _bakeHandle;
        private int _bakeFrame;
        private bool _unloadPending;
        private int _batchDepth;

        public MaterialRegistry Materials => _registry;
        public uint Seed => _seed;
        public float AnomalyFrequency => _anomalyFrequency;
        public WorldSettings Settings => _settings;
        public SectionTable Sections => _sections;

        /// <summary>Мир пересоздан с новым сидом (все колонны выгружены).</summary>
        public event Action WorldReset;
        public int LoadedColumns => _columns.Count;
        public int ParkedColumns => _parked.Count;
        public int GenerationTasksInFlight => _generationTasks.Count;
        public int MeshTasksInFlight => _meshTasks.Count;

        /// <summary>Клетка изменилась (после применения и перестройки меша).</summary>
        public event Action<int3, Cell> CellChanged;

        /// <summary>Сейв (этап 9): сохранённые колонны грузятся отсюда вместо генерации, изменённые при выгрузке уходят сюда. Null — без сейва (правки держатся в памяти).</summary>
        public IColumnStore ColumnStore { get; set; }

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
            _generatorMaterials = MallMaterials.Resolve(_registry);
            BlockShapes.BuildNative(out _shapeVertices, out _shapeRanges, Allocator.Persistent);
            if (_settings != null)
            {
                _seed = _settings.Seed;
                _anomalyFrequency = _settings.AnomalyFrequency;
                _sections = new SectionTable(_settings.Sections, _settings.GetWeight, _registry);
            }
            else
            {
                Debug.LogError("[World] WorldSettings is not assigned — the mall has no sections.", this);
                _sections = new SectionTable(new List<SectionDef> { ScriptableObject.CreateInstance<SectionDef>() }, _ => 1f, _registry);
            }
            Shader.SetGlobalVectorArray(LayerEmissionId, BlockTextureArrayBuilder.BuildLayerEmission(_registry.All, layers));
            Shader.SetGlobalFloat(LayerCountId, _textures.depth);
            RegisterConsoleCompleters();

            _opaque = new Material(_opaqueMaterial) { name = "Blocks (runtime)" };
            _transparent = new Material(_transparentMaterial) { name = "Blocks Transparent (runtime)" };
            _opaque.SetTexture(BlockTexturesId, _textures);
            _transparent.SetTexture(BlockTexturesId, _textures);
            _transparent.SetShaderPassEnabled("ShadowCaster", false);

            _airChunk = new ChunkData(new int3(0, WorldConstants.ChunksPerColumn, 0), Cell.Air);
            _bedrockChunk = new ChunkData(new int3(0, -1, 0), new Cell(_generatorMaterials.Bedrock));

            LiquidProbe.IsLiquidAt = IsLiquidAt;
            _chunkRoot = new GameObject("Chunks").transform;
            _chunkRoot.SetParent(transform, false);
        }

        private static readonly int BlockTexturesId = Shader.PropertyToID("_BlockTextures");
        private static readonly int LayerEmissionId = Shader.PropertyToID("_VM_LayerEmission");
        private static readonly int LayerCountId = Shader.PropertyToID("_VM_LayerCount");

        /// <summary>Автодополнение аргументов консольных команд мира (материалы, виды аномалий).</summary>
        private void RegisterConsoleCompleters()
        {
            var materials = new List<string>();
            foreach (var def in _registry.All)
                materials.Add(def.Id.StartsWith("block.") ? def.Id.Substring(6) : def.Id);
            DevConsole.RegisterCompleter("setblock", token => token == 3 ? materials : null);
            DevConsole.RegisterCompleter("fill", token => token == 6 ? materials : null);
            DevConsole.RegisterCompleter("goto_anomaly", token => token == 0 ? new[] { "abyss", "pool", "cutout", "inverted", "oasis", "flooded", "distortion", "glitch", "pulse", "lookme", "puddle" } : null);
            DevConsole.RegisterCompleter("time", token => token == 0 ? new[] { "6", "9", "12", "18", "22", "22.5", "23", "0" } : null);
        }

        /// <summary>Параметры макроячейки мола (кэш; одинаковы на всех уровнях).</summary>
        public MallCellInfo GetCellInfo(int2 cell)
        {
            if (!_cellInfos.TryGetValue(cell, out var info))
            {
                if (_cellInfos.Count > 4096)
                    _cellInfos.Clear();
                info = MallPlanner.GetCellInfo(_seed, _anomalyFrequency, cell);
                _cellInfos.Add(cell, info);
            }
            return info;
        }

        /// <summary>Открыть мир сейва: сид и частота аномалий, все колонны и состояния сбрасываются (WorldReset).</summary>
        public void LoadWorld(uint seed, float anomalyFrequency)
        {
            _anomalyFrequency = anomalyFrequency;
            ResetWorld(seed);
        }

        private WorldSettings _settingsAsset;

        /// <summary>Настройки открытого мира (этап 10): копия ассета с подменёнными значениями, сам ассет не трогаем.</summary>
        public void ApplyWorldSettings(float lootAmount, float lootRarity, float enemyDensity)
        {
            if (_settingsAsset == null)
                _settingsAsset = _settings;
            if (_settingsAsset == null)
                return;
            if (_settings == _settingsAsset)
                _settings = Instantiate(_settingsAsset);
            _settings.LootAmount = lootAmount;
            _settings.LootRarity = lootRarity;
            _settings.EnemyDensity = enemyDensity;
        }

        /// <summary>
        /// Доля готовых колонн вокруг точки (данные, меш и коллайдер) в радиусе radius колонн — прогресс загрузки мира
        /// у портала. 1 — можно входить.
        /// </summary>
        public float ReadyFraction(Vector3 point, int radius)
        {
            int2 center = WorldConstants.CellToColumn((int3)math.floor((float3)point));
            int total = 0, ready = 0;
            for (int dz = -radius; dz <= radius; dz++)
            for (int dx = -radius; dx <= radius; dx++)
            {
                total++;
                if (StageOf(center + new int2(dx, dz)) == ColumnStage.Ready)
                    ready++;
            }
            return total > 0 ? ready / (float)total : 1f;
        }

        /// <summary>Как далеко продвинулась колонна (карта загрузки на ПК в лобби).</summary>
        public ColumnStage StageOf(int2 column)
        {
            if (!_columns.TryGetValue(column, out var data))
                return ColumnStage.Queued;
            if (data.State != ColumnState.Generated)
                return ColumnStage.Generating;
            for (int y = 0; y < WorldConstants.ChunksPerColumn; y++)
            {
                if (data.MeshDirty[y] || !data.CollisionReady[y])
                    return ColumnStage.Meshing;
            }
            return ColumnStage.Ready;
        }

        /// <summary>Отдать в сейв все загруженные изменённые колонны (сохранение без выгрузки). Возвращает их число.</summary>
        public int StoreModifiedColumns()
        {
            if (ColumnStore == null)
                return 0;
            CompleteAllJobs();
            int count = 0;
            foreach (var column in _columns.Values)
            {
                if (column.State != ColumnState.Generated || !column.IsModified)
                    continue;
                ColumnStore.Store(column);
                count++;
            }
            return count;
        }

        /// <summary>Лампы и якоря функциональных блоков загруженной из сейва колонны (у сгенерированных их собирает джоб).</summary>
        private void RebuildMarkers(ChunkColumn column)
        {
            for (int y = 0; y < WorldConstants.ChunksPerColumn; y++)
            {
                column.Lamps[y] = null;
                column.Anchors[y] = null;
                var chunk = column.Chunks[y];
                if (chunk == null)
                    continue;
                int3 origin = new int3(column.Coord.x, y, column.Coord.y) * WorldConstants.ChunkSize;
                int count = chunk.IsUniform ? 1 : WorldConstants.ChunkVolume;
                for (int i = 0; i < count; i++)
                {
                    Cell cell = chunk.Get(i);
                    if (cell.Material == 0)
                        continue;
                    var info = _registry.Infos[cell.Material];
                    bool lamp = info.Lamp != LampKind.None;
                    bool anchor = info.Kind == MaterialKind.Functional && cell.Value == 0;
                    if (!lamp && !anchor)
                        continue;
                    if (chunk.IsUniform)
                        break; // однородный чанк из ламп или дверей не бывает
                    int3 world = origin + new int3(i & WorldConstants.ChunkMask, i >> (WorldConstants.ChunkShift * 2), (i >> WorldConstants.ChunkShift) & WorldConstants.ChunkMask);
                    if (lamp)
                        (column.Lamps[y] ??= new List<int3>()).Add(world);
                    if (anchor)
                        (column.Anchors[y] ??= new List<int3>()).Add(world);
                }
            }
        }

        /// <summary>Пересоздать мир с другим сидом: все колонны (и несохранённые правки) выбрасываются.</summary>
        public void ResetWorld(uint seed)
        {
            CompleteAllJobs();
            foreach (var coord in new List<int2>(_columns.Keys))
                UnloadColumn(coord);
            foreach (var column in _parked.Values)
                column.Dispose();
            _parked.Clear();
            _immediate.Clear();
            _cellInfos.Clear();
            _seed = seed;
            WorldReset?.Invoke();
        }

        /// <summary>Перегенерировать одну колонну (правки в ней теряются).</summary>
        public bool RegenerateColumn(int2 coord)
        {
            if (!_columns.ContainsKey(coord))
                return false;
            CompleteAllJobs();
            var column = _columns[coord];
            _columns.Remove(coord);
            for (int i = 0; i < WorldConstants.ChunksPerColumn; i++)
                ReleaseRenderer(column, i);
            column.Dispose();
            _parked.Remove(coord);
            ColumnStore?.Remove(coord);
            // Соседям нужно перестроить меши: грани на границе зависят от этой колонны.
            for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
                if (_columns.TryGetValue(coord + new int2(dx, dz), out var neighbor))
                    neighbor.MarkAllDirty();
            return true;
        }

        /// <summary>Светящиеся блоки загруженных колонн в радиусе (по горизонтали) от точки.</summary>
        public void CollectLamps(Vector3 center, float radius, List<int3> result)
        {
            int2 min = WorldConstants.CellToColumn((int3)math.floor((float3)center - radius));
            int2 max = WorldConstants.CellToColumn((int3)math.floor((float3)center + radius));
            float radiusSq = radius * radius;
            for (int cz = min.y; cz <= max.y; cz++)
            for (int cx = min.x; cx <= max.x; cx++)
            {
                if (!_columns.TryGetValue(new int2(cx, cz), out var column) || column.State != ColumnState.Generated)
                    continue;
                foreach (var list in column.Lamps)
                {
                    if (list == null)
                        continue;
                    foreach (var lamp in list)
                    {
                        float3 delta = (float3)lamp + 0.5f - (float3)center;
                        if (math.lengthsq(delta) <= radiusSq)
                            result.Add(lamp);
                    }
                }
            }
        }

        /// <summary>Якоря функциональных блоков (двери, ворота) загруженных колонн в радиусе (по горизонтали) от точки.</summary>
        public void CollectAnchors(Vector3 center, float radius, List<int3> result)
        {
            int2 min = WorldConstants.CellToColumn((int3)math.floor((float3)center - radius));
            int2 max = WorldConstants.CellToColumn((int3)math.floor((float3)center + radius));
            float radiusSq = radius * radius;
            for (int cz = min.y; cz <= max.y; cz++)
            for (int cx = min.x; cx <= max.x; cx++)
            {
                if (!_columns.TryGetValue(new int2(cx, cz), out var column) || column.State != ColumnState.Generated)
                    continue;
                foreach (var list in column.Anchors)
                {
                    if (list == null)
                        continue;
                    foreach (var anchor in list)
                    {
                        float3 delta = (float3)anchor + 0.5f - (float3)center;
                        if (math.lengthsq(delta) <= radiusSq)
                            result.Add(anchor);
                    }
                }
            }
        }

        private void UpdateAnchorList(int3 cell, Cell value)
        {
            if (!_columns.TryGetValue(WorldConstants.CellToColumn(cell), out var column))
                return;
            int index = cell.y >> WorldConstants.ChunkShift;
            var list = column.Anchors[index];
            bool isAnchor = value.Material != 0 && _registry.Infos[value.Material].Kind == MaterialKind.Functional && value.Value == 0;
            if (isAnchor)
            {
                list ??= column.Anchors[index] = new List<int3>();
                if (!list.Contains(cell))
                    list.Add(cell);
            }
            else
            {
                list?.Remove(cell);
            }
        }

        private void UpdateLampList(int3 cell, Cell value)
        {
            if (!_columns.TryGetValue(WorldConstants.CellToColumn(cell), out var column))
                return;
            int index = cell.y >> WorldConstants.ChunkShift;
            var list = column.Lamps[index];
            bool isLamp = value.Material != 0 && _registry.Infos[value.Material].Lamp != LampKind.None;
            if (isLamp)
            {
                list ??= column.Lamps[index] = new List<int3>();
                if (!list.Contains(cell))
                    list.Add(cell);
            }
            else
            {
                list?.Remove(cell);
            }
        }

        /// <summary>
        /// Мир сцены запускается (Start: все системы сцены уже проснулись, генерации ещё не было) — момент открыть сейв
        /// (SaveSystem). Работает и без перезагрузки сцены при входе в режим игры.
        /// </summary>
        public static event Action<ChunkManager> Starting;

        private void Start() => Starting?.Invoke(this);

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
            _sections?.Dispose();
            _registry?.Dispose();
            if (_shapeVertices.IsCreated)
                _shapeVertices.Dispose();
            if (_shapeRanges.IsCreated)
                _shapeRanges.Dispose();
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
            Starting = null;
            Instance = null;
        }

        private static readonly Perf.Probe GenerationProbe = Perf.Register("World.Chunks.CompleteGeneration");
        private static readonly Perf.Probe MeshingProbe = Perf.Register("World.Chunks.CompleteMeshing");
        private static readonly Perf.Probe BakingProbe = Perf.Register("World.Chunks.CompleteBaking");
        private static readonly Perf.Probe StreamingProbe = Perf.Register("World.Chunks.Streaming");
        private static readonly Perf.Probe ScheduleProbe = Perf.Register("World.Chunks.Schedule");

        private void Update()
        {
            using (Perf.Measure(GenerationProbe))
                CompleteGeneration();
            using (Perf.Measure(MeshingProbe))
                CompleteMeshing();
            using (Perf.Measure(BakingProbe))
                CompleteBaking();
            using (Perf.Measure(StreamingProbe))
                UpdateStreaming();
        }

        private void LateUpdate()
        {
            using var perf = Perf.Measure(ScheduleProbe);
            if (_unloadPending)
                return;
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

        public MaterialInfo GetInfo(ushort material) => _registry.Infos[material];

        /// <summary>Вода в точке (клетка — жидкость).</summary>
        public bool IsLiquidAt(Vector3 position)
        {
            Cell cell = GetCell((int3)math.floor((float3)position));
            return cell.Material != 0 && _registry.Infos[cell.Material].IsLiquid;
        }

        /// <summary>
        /// Чем становится клетка, из которой убрали блок: в водном чанке — вода (её там не убрать), иначе воздух.
        /// </summary>
        public Cell EmptyCellAt(int3 cell)
        {
            var info = GetCellInfo(MallLayout.CellOf(cell.x, cell.z));
            int level = MallLayout.LevelAt(cell.y);
            if (_generatorMaterials.WaterDeep != 0 && level >= 0 && level < MallLayout.LevelCount && MallPlanner.InFlooded(info, new float2(cell.x + 0.5f, cell.z + 0.5f)))
                return new Cell(_generatorMaterials.WaterDeep);
            return Cell.Air;
        }

        /// <summary>Материал блоков (runtime-экземпляр с массивом текстур) — для обломков и превью.</summary>
        public Material BlockMaterial => _opaque;

        /// <summary>Прозрачный материал блоков (стекло).</summary>
        public Material TransparentBlockMaterial => _transparent;

        /// <summary>
        /// Меш воксельного тела тем же мешером, что и чанки (формы, гладкие воксели, AO, текстуры): клетки сетки
        /// копируются в чанк 32³, соседи — воздух. Два подмеша: непрозрачный и стекло. Вершины — в координатах тела.
        /// </summary>
        public unsafe void BuildDetachedMesh(VoxelGrid grid, Mesh mesh)
        {
            mesh.Clear();
            if (grid.Count == 0)
                return;
            int3 min = grid.Min;
            var cells = new NativeArray<Cell>(WorldConstants.ChunkVolume, Allocator.TempJob);
            var views = new NativeArray<ChunkView>(27, Allocator.TempJob);
            var vertices = new NativeList<ChunkVertex>(1024, Allocator.TempJob);
            var opaque = new NativeList<int>(2048, Allocator.TempJob);
            var transparent = new NativeList<int>(64, Allocator.TempJob);
            // Воды в куске нет, но джоб требует созданные контейнеры.
            var waterVertices = new NativeList<ChunkVertex>(0, Allocator.TempJob);
            var waterIndices = new NativeList<int>(0, Allocator.TempJob);
            try
            {
                foreach (var (cell, value) in grid.Cells)
                    cells[WorldConstants.CellIndex(cell - min)] = value;
                var air = _airChunk.GetView();
                for (int i = 0; i < views.Length; i++)
                    views[i] = air;
                views[13] = new ChunkView { Cells = (Cell*)Unity.Collections.LowLevel.Unsafe.NativeArrayUnsafeUtility.GetUnsafePtr(cells), Uniform = 0 };
                // Мешер не рисует грани ниже дна мира (y < 0): начало текстур сдвигаем вверх на период узоров.
                int3 uvOrigin = grid.UvOrigin + min;
                while (uvOrigin.y < 0)
                    uvOrigin.y += 256;
                new ChunkMeshJob
                {
                    Materials = _registry.Infos,
                    Views = views,
                    ShapeVertices = _shapeVertices,
                    ShapeRanges = _shapeRanges,
                    ChunkOrigin = uvOrigin,
                    Vertices = vertices,
                    OpaqueIndices = opaque,
                    TransparentIndices = transparent,
                    WaterVertices = waterVertices,
                    WaterIndices = waterIndices,
                }.Run();

                var array = vertices.AsArray();
                float3 offset = min;
                for (int i = 0; i < array.Length; i++)
                {
                    var vertex = array[i];
                    vertex.Position += offset;
                    array[i] = vertex;
                }
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                mesh.SetVertexBufferParams(array.Length, ChunkVertex.Layout);
                mesh.SetVertexBufferData(array, 0, 0, array.Length);
                mesh.SetIndexBufferParams(opaque.Length + transparent.Length, UnityEngine.Rendering.IndexFormat.UInt32);
                mesh.SetIndexBufferData(opaque.AsArray(), 0, 0, opaque.Length);
                mesh.SetIndexBufferData(transparent.AsArray(), 0, opaque.Length, transparent.Length);
                mesh.subMeshCount = 2;
                mesh.SetSubMesh(0, new UnityEngine.Rendering.SubMeshDescriptor(0, opaque.Length));
                mesh.SetSubMesh(1, new UnityEngine.Rendering.SubMeshDescriptor(opaque.Length, transparent.Length));
                mesh.RecalculateBounds();
            }
            finally
            {
                cells.Dispose();
                views.Dispose();
                vertices.Dispose();
                opaque.Dispose();
                transparent.Dispose();
                waterVertices.Dispose();
                waterIndices.Dispose();
            }
        }

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
            UpdateLampList(cell, value);
            UpdateAnchorList(cell, value);

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
            if (_toUnload.Count == 0)
            {
                _unloadPending = false;
                return;
            }
            // Фоновые задачи читают память соседних чанков: выгружаем, только когда в работе ничего нет.
            // Пока выгрузка ждёт, новые задачи не ставятся — текущие доделываются за пару кадров.
            if (_generationTasks.Count > 0 || _meshTasks.Count > 0 || _bakeIds.IsCreated)
            {
                _unloadPending = true;
                return;
            }
            _unloadPending = false;
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

            // Изменённая колонна уходит в сейв (сжатой) — или, без сейва, остаётся в памяти, чтобы правки не терялись.
            if (column.IsModified && ColumnStore != null)
            {
                ColumnStore.Store(column);
                column.Dispose();
            }
            else if (column.IsModified)
            {
                _parked[coord] = column;
            }
            else
            {
                column.Dispose();
            }
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
                if (ColumnStore != null && ColumnStore.TryLoad(coord, out var loaded))
                {
                    RebuildMarkers(loaded);
                    _columns.Add(coord, loaded);
                    continue;
                }

                var column = new ChunkColumn(coord) { State = ColumnState.Generating };
                _columns.Add(coord, column);
                for (int y = 0; y < WorldConstants.ChunksPerColumn; y++)
                {
                    var chunk = ChunkData.CreateForGeneration(new int3(coord.x, y, coord.y));
                    column.Chunks[y] = chunk;
                    var uniform = new NativeArray<byte>(1, Allocator.TempJob);
                    var lamps = new NativeList<int3>(64, Allocator.TempJob);
                    var anchors = new NativeList<int3>(16, Allocator.TempJob);
                    var job = new MallGenerationJob
                    {
                        Chunk = chunk.Coord,
                        Seed = _seed,
                        AnomalyFrequency = _anomalyFrequency,
                        M = _generatorMaterials,
                        Sections = _sections.Infos,
                        Materials = _registry.Infos,
                        Cells = chunk.Cells,
                        IsUniform = uniform,
                        Lamps = lamps,
                        Anchors = anchors,
                    };
                    _generationTasks.Add(new GenerationTask { Column = column, Chunk = chunk, Uniform = uniform, Lamps = lamps, Anchors = anchors, Handle = job.Schedule(), Frame = Time.frameCount });
                    column.PendingGenerationJobs++;
                }
            }
        }

        private void CompleteGeneration(bool force = false)
        {
            int kept = 0;
            for (int i = 0; i < _generationTasks.Count; i++)
            {
                var task = _generationTasks[i];
                if (!force && !IsDone(task.Handle, task.Frame))
                {
                    _generationTasks[kept++] = task;
                    continue;
                }
                task.Handle.Complete();
                if (task.Uniform[0] != 0)
                    task.Chunk.SetUniform(task.Chunk.Cells[0]);
                task.Uniform.Dispose();
                int chunkIndex = task.Chunk.Coord.y;
                task.Column.Lamps[chunkIndex] = task.Lamps.Length > 0 ? new List<int3>(task.Lamps.AsArray().ToArray()) : null;
                task.Lamps.Dispose();
                task.Column.Anchors[chunkIndex] = task.Anchors.Length > 0 ? new List<int3>(task.Anchors.AsArray().ToArray()) : null;
                task.Anchors.Dispose();
                if (--task.Column.PendingGenerationJobs == 0)
                    task.Column.State = ColumnState.Generated;
            }
            _generationTasks.RemoveRange(kept, _generationTasks.Count - kept);
        }

        /// <summary>
        /// Задача готова или ждёт слишком долго. Основной поток не ждёт фоновые задачи: недоделанные забираются в
        /// следующих кадрах (раньше ожидание в Complete давало кадры по 30–60 мс). TempJob-память живёт не больше
        /// 4 кадров — старше MaxJobFrames дожидаемся принудительно.
        /// </summary>
        private static bool IsDone(JobHandle handle, int frame) => handle.IsCompleted || Time.frameCount - frame >= MaxJobFrames;

        private const int MaxJobFrames = 3;

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
                    _meshTasks.Add(new MeshTask { Column = column, Index = y, Views = views, Buffers = buffers, Handle = handle, Frame = Time.frameCount });
                    column.MeshInFlight[y] = true;
                    column.MeshDirty[y] = false;
                    budget--;
                }
            }
        }

        private void CompleteMeshing(bool all = false)
        {
            int kept = 0;
            for (int i = 0; i < _meshTasks.Count; i++)
            {
                var task = _meshTasks[i];
                if (!all && !IsDone(task.Handle, task.Frame))
                {
                    _meshTasks[kept++] = task;
                    continue;
                }
                task.Handle.Complete();
                task.Views.Dispose();
                _meshReady.Add(task);
            }
            _meshTasks.RemoveRange(kept, _meshTasks.Count - kept);

            long deadline = Deadline(ApplyBudgetMs.Value * 0.5f);
            int applied = 0;
            for (; applied < _meshReady.Count; applied++)
            {
                if (!all && applied > 0 && System.Diagnostics.Stopwatch.GetTimestamp() > deadline)
                    break;
                var task = _meshReady[applied];
                // Чанк остаётся «в работе», пока меш не подключён: новая задача на него не встанет раньше.
                task.Column.MeshInFlight[task.Index] = false;
                if (_columns.ContainsKey(task.Column.Coord))
                    ApplyMesh(task.Column, task.Index, task.Buffers, queueBake: true);
                ReturnBuffers(task.Buffers);
            }
            _meshReady.RemoveRange(0, applied);
        }

        private static long Deadline(float milliseconds) =>
            System.Diagnostics.Stopwatch.GetTimestamp() + (long)(milliseconds * 0.001 * System.Diagnostics.Stopwatch.Frequency);

        private ChunkMeshJob CreateMeshJob(int2 coord, int y, NativeArray<ChunkView> views, MeshBuffers buffers) => new()
        {
            Materials = _registry.Infos,
            Views = views,
            ShapeVertices = _shapeVertices,
            ShapeRanges = _shapeRanges,
            ChunkOrigin = new int3(coord.x, y, coord.y) * WorldConstants.ChunkSize,
            Vertices = buffers.Vertices,
            OpaqueIndices = buffers.Opaque,
            TransparentIndices = buffers.Transparent,
            WaterVertices = buffers.WaterVertices,
            WaterIndices = buffers.WaterIndices,
        };

        private void ApplyMesh(ChunkColumn column, int index, MeshBuffers buffers, bool queueBake)
        {
            column.HasMesh[index] = true;
            if (buffers.Vertices.Length == 0 && buffers.WaterVertices.Length == 0)
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
            renderer.ApplyWater(buffers.WaterVertices, buffers.WaterIndices);
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

        /// <summary>
        /// Соседи для мешинга. Под миром — пустота: стенки шахт-бездн доходят до дна мира гладко
        /// (нижние грани блоков на дне мира мешер не рисует сам). Для логики (GetCell) под миром по-прежнему основание.
        /// </summary>
        private ChunkData GetChunkForView(int3 chunk)
        {
            if (chunk.y < 0)
                return _airChunk;
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
            if (_bakeQueue.Count == 0 || _bakeIds.IsCreated)
                return;
            _bakeIds = new NativeArray<UnityEngine.EntityId>(_bakeQueue.Count, Allocator.TempJob);
            for (int i = 0; i < _bakeQueue.Count; i++)
                _bakeIds[i] = _bakeQueue[i].Renderer.Mesh.GetEntityId();
            _bakeHandle = new BakeJob { Meshes = _bakeIds }.Schedule(_bakeIds.Length, 1);
            _bakeFrame = Time.frameCount;
            _bakeInFlight.AddRange(_bakeQueue);
            _bakeQueue.Clear();
        }

        private void CompleteBaking(bool all = false)
        {
            if (_bakeIds.IsCreated && (all || IsDone(_bakeHandle, _bakeFrame)))
            {
                _bakeHandle.Complete();
                _bakeIds.Dispose();
                _bakeReady.AddRange(_bakeInFlight);
                _bakeInFlight.Clear();
            }

            long deadline = Deadline(ApplyBudgetMs.Value * 0.5f);
            int applied = 0;
            for (; applied < _bakeReady.Count; applied++)
            {
                if (!all && applied > 0 && System.Diagnostics.Stopwatch.GetTimestamp() > deadline)
                    break;
                var entry = _bakeReady[applied];
                // Колонну могли выгрузить, а меш — перестроить заново: применяем только актуальное запекание.
                if (!_columns.ContainsKey(entry.Column.Coord)
                    || entry.Column.Renderers[entry.Index] != entry.Renderer
                    || entry.Column.MeshVersion[entry.Index] != entry.Version)
                    continue;
                entry.Renderer.ApplyCollider();
                entry.Column.CollisionReady[entry.Index] = true;
            }
            _bakeReady.RemoveRange(0, applied);
        }

        private void CompleteAllJobs()
        {
            if (_generationTasks.Count == 0 && _meshTasks.Count == 0 && !_bakeIds.IsCreated && _meshReady.Count == 0 && _bakeReady.Count == 0)
                return;
            CompleteGeneration(force: true);
            CompleteMeshing(all: true);
            CompleteBaking(all: true);
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

        public bool TryGetEmbeddedEscape(Vector3 feet, float height, float radius, out Vector3 freeFeet)
        {
            bool found = EmbeddedEscape.TryFindEscape(this, feet, height, radius, out float3 free);
            freeFeet = free;
            return found;
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
            return ChunkRenderer.Create(_chunkRoot, _opaque, _transparent, _waterMaterial);
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
            public NativeList<int3> Lamps;
            public NativeList<int3> Anchors;
            public JobHandle Handle;
            public int Frame;
        }

        private struct MeshTask
        {
            public ChunkColumn Column;
            public int Index;
            public NativeArray<ChunkView> Views;
            public MeshBuffers Buffers;
            public JobHandle Handle;
            public int Frame;
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
            public NativeList<ChunkVertex> WaterVertices = new(64, Allocator.Persistent);
            public NativeList<int> WaterIndices = new(96, Allocator.Persistent);

            public void Dispose()
            {
                Vertices.Dispose();
                Opaque.Dispose();
                Transparent.Dispose();
                WaterVertices.Dispose();
                WaterIndices.Dispose();
            }
        }
    }
}
