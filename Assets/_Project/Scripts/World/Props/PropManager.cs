using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using VoidMall.Console;
using VoidMall.Core;
using Random = UnityEngine.Random;

namespace VoidMall.World
{
    /// <summary>
    /// Пропы мира (ARCHITECTURE §7): стриминг «корзинами» (ячейка + уровень) на этаже игрока ±world_prop_levels в радиусе
    /// world_prop_radius, пул объектов, сон и пробуждение, гвозди, поломка и разбор.
    ///
    /// Изменения пропов за сессию хранятся в PropState по устойчивому ID: сдвинутые («свободные») пропы живут отдельно
    /// от своей корзины и стримятся по собственной позиции, сломанные и разобранные больше не появляются.
    /// Сохранение на диск — этап 9.
    /// </summary>
    [DefaultExecutionOrder(-180)]
    public sealed partial class PropManager : MonoBehaviour
    {
        public static readonly CVar<int> SpawnBudget = new("world_prop_budget", 200, "Сколько пропов создавать за кадр (не больше).");
        public static readonly CVar<float> SpawnBudgetMs = new("world_prop_budget_ms", 1.5f, "Сколько миллисекунд за кадр тратить на создание пропов.");
        public static readonly CVar<float> Radius = new("world_prop_radius", 80f, "Радиус появления пропов, м.");
        public static readonly CVar<int> LevelSpan = new("world_prop_levels", 1, "Пропы на скольких уровнях выше и ниже игрока.");
        public static readonly CVar<bool> Visible = new("world_props", true, "Показывать пропы.");

        [SerializeField] private PropSet _set;
        [SerializeField] private Material _material;
        [SerializeField] private Material _dustMaterial;

        public static PropManager Instance { get; private set; }

        /// <summary>Материал частиц пыли (облако при поломке, пенопласт из ящика).</summary>
        public Material DustMaterial => _dustMaterial;

        public PropRegistry Props { get; private set; }
        public int LiveCount => _live.Count;
        public int LoadedBuckets => _buckets.Count;
        public int StateCount => _states.Count;

        /// <summary>Проп появился в мире (из корзины или свободный).</summary>
        public event System.Action<PropBody> PropSpawned;

        /// <summary>Проп сейчас уйдёт в пул (выгрузка).</summary>
        public event System.Action<PropBody> PropDespawning;

        /// <summary>Проп проснулся (его взяли, толкнули, выбили опору).</summary>
        public event System.Action<PropBody> PropWoken;

        /// <summary>Проп уничтожен (сломан, разобран, упал в пустоту) — до ухода в пул.</summary>
        public event System.Action<PropBody, PropRemoveReason> PropRemoved;

        /// <summary>Изменённое за сессию состояние пропа.</summary>
        private sealed class PropState
        {
            public ulong Id;
            public ushort Def;
            public bool Removed;

            /// <summary>Сдвинут с места: стримится по своей позиции, а не корзиной.</summary>
            public bool Loose;

            /// <summary>Позиция и поворот (у ребёнка группы — локальные относительно родителя).</summary>
            public Vector3 Position;
            public Quaternion Rotation;
            public int Damage;
            public ulong Parent;
            public readonly List<int3> Anchors = new();

            /// <summary>Клетки отломанного куска (воксельное тело, Def = VoxelDefIndex). У пропов — null.</summary>
            public VoxelGrid Voxel;

            /// <summary>Опору выбили, пока проп был выгружен: при появлении — сразу разбудить.</summary>
            public bool WakeOnSpawn;
        }

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

        private sealed class Bucket
        {
            public BucketKey Key;
            public readonly List<PropSpawn> Pending = new();
            public readonly List<ulong> Ids = new();
            public int PendingIndex;
        }

        private readonly Dictionary<ulong, PropState> _states = new();
        private readonly Dictionary<ulong, List<ulong>> _children = new();
        private readonly Dictionary<int3, List<ulong>> _anchorIndex = new();
        private readonly Dictionary<ulong, PropBody> _live = new();
        private readonly Dictionary<BucketKey, Bucket> _buckets = new();
        private readonly List<BucketKey> _bucketsToUnload = new();
        private readonly List<(float distance, BucketKey key)> _bucketsToLoad = new();
        private readonly Stack<PropBody> _pool = new();
        private readonly List<Vector3> _viewers = new();
        private readonly HashSet<int2> _around = new();
        private readonly List<ulong> _scratch = new();
        private readonly Collider[] _overlap = new Collider[64];

        private ChunkManager _world;
        private PropIds _ids;
        private Transform _root;
        private Transform _poolRoot;
        private float _looseTimer;

        // ───────────────────────────── Жизненный цикл ─────────────────────────────

        private void Awake()
        {
            Instance = this;
            _root = new GameObject("Props").transform;
            _root.SetParent(transform, false);
            _poolRoot = new GameObject("Prop Pool").transform;
            _poolRoot.SetParent(transform, false);
            _poolRoot.gameObject.SetActive(false);
            if (_set == null)
            {
                Debug.LogError("[Props] PropSet is not assigned.", this);
                enabled = false;
                return;
            }
            Props = new PropRegistry(_set);
            _ids = PropIds.Resolve(Props);
            var propIds = new List<string>();
            foreach (var def in Props.All)
                propIds.Add(def.Id.Replace("prop.", ""));
            DevConsole.RegisterCompleter("prop_spawn", token => token == 0 ? propIds : null);
        }

        private void OnEnable()
        {
            _world = GetComponent<ChunkManager>();
            if (_world == null)
                _world = ChunkManager.Instance;
            if (_world != null)
            {
                _world.WorldReset += OnWorldReset;
                _world.CellChanged += OnCellChanged;
            }
            Visible.Changed += ApplyVisibility;
        }

        private void OnDisable()
        {
            if (_world != null)
            {
                _world.WorldReset -= OnWorldReset;
                _world.CellChanged -= OnCellChanged;
            }
            Visible.Changed -= ApplyVisibility;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
            Props?.Dispose();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Instance = null;

        private static readonly Perf.Probe UpdateProbe = Perf.Register("Props.Manager");
        private static readonly Perf.Probe BucketsProbe = Perf.Register("Props.Manager.Buckets");
        private static readonly Perf.Probe SpawnProbe = Perf.Register("Props.Manager.Spawn");
        private static readonly Perf.Probe PlanProbe = Perf.Register("Props.Manager.Buckets.Plan");
        private static readonly Perf.Probe PlaceProbe = Perf.Register("Props.Manager.Buckets.Place");
        private static readonly Perf.Probe UnloadProbe = Perf.Register("Props.Manager.Buckets.Unload");
        private static readonly Perf.Probe DespawnProbe = Perf.Register("Props.Manager.Despawn");
        private readonly Queue<ulong> _despawnQueue = new();
        private readonly HashSet<ulong> _despawnSet = new();

        private void Update()
        {
            using var perf = Perf.Measure(UpdateProbe);
            if (_world == null || _world.Sections == null || Props == null)
                return;
            _viewers.Clear();
            var primary = WorldViewer.Primary;
            if (primary == null)
                return;
            _viewers.Add(primary.transform.position);

            using (Perf.Measure(BucketsProbe))
                UpdateBuckets();
            using (Perf.Measure(DespawnProbe))
                DespawnQueued(System.Diagnostics.Stopwatch.GetTimestamp() + (long)(SpawnBudgetMs.Value * 0.001 * System.Diagnostics.Stopwatch.Frequency));
            using (Perf.Measure(SpawnProbe))
                SpawnPending();
            _looseTimer -= Time.deltaTime;
            if (_looseTimer <= 0f)
            {
                _looseTimer = 0.25f;
                UpdateLoose();
            }
        }

        // ───────────────────────────── Корзины ─────────────────────────────

        private void UpdateBuckets()
        {
            float radius = Radius.Value;
            int span = Mathf.Max(0, LevelSpan.Value);
            int viewerLevel = MallLayout.LevelAtFeet(_viewers[0].y);

            _bucketsToUnload.Clear();
            foreach (var key in _buckets.Keys)
                if (DistanceToViewers(key.Cell) > radius + 16f || math.abs(key.Level - viewerLevel) > span + 1)
                    _bucketsToUnload.Add(key);
            using (Perf.Measure(UnloadProbe))
                foreach (var key in _bucketsToUnload)
                    UnloadBucket(key);

            _bucketsToLoad.Clear();
            foreach (var cell in CellsAround(radius))
            {
                float distance = DistanceToViewers(cell);
                for (int level = viewerLevel - span; level <= viewerLevel + span; level++)
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
            var bucket = new Bucket { Key = key };
            var plan = new MallLevelPlan(Allocator.Temp);
            try
            {
                using (Perf.Measure(PlanProbe))
                    MallPlanner.BuildLevel(_world.Seed, info, key.Level, _world.Sections.Infos, ref plan);
                using (Perf.Measure(PlaceProbe))
                    PropPlacer.Place(_world.Seed, info, plan, _world.Sections.Infos, Props, _ids, bucket.Pending);
            }
            finally
            {
                plan.Dispose();
            }
            Vector3 viewer = _viewers[0];
            bucket.Pending.Sort((a, b) => (a.Position - viewer).sqrMagnitude.CompareTo((b.Position - viewer).sqrMagnitude));
            _buckets.Add(key, bucket);
        }

        private void SpawnPending()
        {
            int budget = Mathf.Max(1, SpawnBudget.Value);
            // Лимит и по времени: при входе в новую зону пропы появляются за несколько кадров, а не в одном долгом.
            long deadline = System.Diagnostics.Stopwatch.GetTimestamp() + (long)(SpawnBudgetMs.Value * 0.001 * System.Diagnostics.Stopwatch.Frequency);
            foreach (var bucket in _buckets.Values)
            {
                while (bucket.PendingIndex < bucket.Pending.Count && budget > 0)
                {
                    var spawn = bucket.Pending[bucket.PendingIndex++];
                    // Сдвинутые, прибитые к другим и уничтоженные пропы корзина не создаёт.
                    if (_states.TryGetValue(spawn.Id, out var state) && (state.Removed || state.Loose || state.Parent != 0))
                        continue;
                    if (_live.ContainsKey(spawn.Id))
                    {
                        if (_despawnSet.Remove(spawn.Id))
                            bucket.Ids.Add(spawn.Id);
                        continue;
                    }
                    var body = Spawn(spawn.Def, spawn.Id, spawn.Position, spawn.Rotation, _root);
                    bucket.Ids.Add(spawn.Id);
                    budget--;
                    if (System.Diagnostics.Stopwatch.GetTimestamp() > deadline)
                        budget = 0;
                    if (state != null && state.Anchors.Count > 0)
                        body.Anchors.AddRange(state.Anchors);
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

        private void UnloadBucket(BucketKey key)
        {
            var bucket = _buckets[key];
            // Пропы уходят в очередь: удаляются за несколько кадров (DespawnQueued), а не все в одном.
            foreach (var id in bucket.Ids)
                if (_live.ContainsKey(id) && _despawnSet.Add(id))
                    _despawnQueue.Enqueue(id);
            _buckets.Remove(key);
        }

        /// <summary>Удалить пропы выгруженных корзин, пока не кончится время кадра.</summary>
        private void DespawnQueued(long deadline)
        {
            while (_despawnQueue.Count > 0)
            {
                ulong id = _despawnQueue.Dequeue();
                // Корзину могли загрузить снова — тогда проп остался в ней (см. SpawnPending).
                if (!_despawnSet.Remove(id))
                    continue;
                // Свободные пропы остаются: их выгрузит стриминг по позиции.
                if (_states.TryGetValue(id, out var state) && state.Loose)
                    continue;
                if (_live.TryGetValue(id, out var body))
                    Despawn(body);
                if (System.Diagnostics.Stopwatch.GetTimestamp() > deadline)
                    return;
            }
        }

        // ───────────────────────────── Свободные пропы ─────────────────────────────

        private void UpdateLoose()
        {
            float radius = Radius.Value;
            int span = Mathf.Max(0, LevelSpan.Value);
            int viewerLevel = MallLayout.LevelAtFeet(_viewers[0].y);
            int budget = 32;

            _scratch.Clear();
            foreach (var state in _states.Values)
                if (state.Loose && !state.Removed && state.Parent == 0)
                    _scratch.Add(state.Id);

            foreach (var id in _scratch)
            {
                var state = _states[id];
                bool live = _live.TryGetValue(id, out var body);
                Vector3 position = live ? body.transform.position : state.Position;
                float distance = Vector2.Distance(new Vector2(position.x, position.z), new Vector2(_viewers[0].x, _viewers[0].z));
                int level = MallLayout.LevelAtFeet(position.y);
                bool wanted = distance <= radius && math.abs(level - viewerLevel) <= span;
                bool keep = distance <= radius + 16f && math.abs(level - viewerLevel) <= span + 1;

                if (live && !keep && !body.IsHeld)
                {
                    StorePose(body);
                    DespawnGroup(body);
                }
                else if (!live && wanted && budget-- > 0)
                {
                    SpawnGroup(state);
                }
            }
        }

        private PropBody SpawnGroup(PropState state)
        {
            var body = Spawn(state.Def, state.Id, state.Position, state.Rotation, _root, state.Voxel);
            body.Anchors.AddRange(state.Anchors);
            SpawnChildren(body);
            if (state.WakeOnSpawn)
            {
                state.WakeOnSpawn = false;
                body.Wake();
            }
            return body;
        }

        private void SpawnChildren(PropBody parent)
        {
            if (!_children.TryGetValue(parent.Id, out var children))
                return;
            foreach (var childId in children)
            {
                var state = _states[childId];
                if (state.Removed)
                    continue;
                var child = Spawn(state.Def, childId, parent.transform.TransformPoint(state.Position), parent.transform.rotation * state.Rotation, _root, state.Voxel);
                child.AttachTo(parent);
                SpawnChildren(child);
            }
        }

        private void DespawnGroup(PropBody body)
        {
            for (int i = body.Children.Count - 1; i >= 0; i--)
                DespawnGroup(body.Children[i]);
            Despawn(body);
        }

        // ───────────────────────────── Пул ─────────────────────────────

        private PropBody Spawn(ushort defIndex, ulong id, Vector3 position, Quaternion rotation, Transform parent, VoxelGrid voxel = null)
        {
            if (defIndex == VoxelDefIndex)
                return SpawnVoxelObject(id, voxel, position, rotation, parent);
            // Проп настраивается, пока лежит в выключенном пуле, и включается сразу на своём месте: перенос или
            // изменение уже включённого неподвижного коллайдера заставляет физику перестраивать дерево статики
            // (рывки Physics.Simulate по 20–60 мс при подгрузке пропов).
            PropBody body;
            if (_pool.Count > 0)
            {
                body = _pool.Pop();
            }
            else
            {
                var go = new GameObject("Prop") { layer = GameLayers.Props };
                go.transform.SetParent(_poolRoot, false);
                go.AddComponent<MeshFilter>();
                go.AddComponent<MeshRenderer>();
                go.AddComponent<BoxCollider>();
                body = go.AddComponent<PropBody>();
            }
            var def = Props.Get(defIndex);
            body.gameObject.name = def.Id;
            body.gameObject.layer = GameLayers.Props;
            body.transform.SetPositionAndRotation(position, rotation);
            body.GetComponent<MeshFilter>().sharedMesh = Props.GetMesh(defIndex);
            var renderer = body.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = def.Material != null ? def.Material : _material;
            renderer.enabled = Visible.Value;
            SetupColliders(body, def);
            // Строительная лампа светит (компонент остаётся на объекте пула, у других пропов свет выключен).
            var workLight = body.GetComponent<WorkLightProp>();
            if (def.Shape == PropShape.WorkLight && workLight == null)
                workLight = body.gameObject.AddComponent<WorkLightProp>();
            if (workLight != null)
                workLight.Configure(def.Shape == PropShape.WorkLight, id);
            // Огонь бочки-костра: горит, пока станция сообщает о тепле (HeatSources).
            var fire = body.GetComponent<FireBarrelProp>();
            if (def.Shape == PropShape.FireBarrel && fire == null)
                fire = body.gameObject.AddComponent<FireBarrelProp>();
            if (fire != null)
                fire.Configure(def.Shape == PropShape.FireBarrel, id, def.Size.y);
            body.transform.SetParent(parent, true);
            body.Initialize(this, id, defIndex, def);
            PropSpawned?.Invoke(body);
            _live[id] = body;
            return body;
        }

        private readonly List<Bounds> _parts = new();

        /// <summary>Коллайдер по форме: одна коробка или несколько частей (стеллажи), лишние компоненты из пула выключаются.</summary>
        private void SetupColliders(PropBody body, PropDef def)
        {
            PropMeshes.GetColliderParts(def.Shape, def.Size, _parts);
            if (_parts.Count == 0)
                _parts.Add(new Bounds(new Vector3(0f, def.Size.y * 0.5f, 0f), def.Size));
            body.GetComponents(body.Colliders);
            while (body.Colliders.Count < _parts.Count)
                body.Colliders.Add(body.gameObject.AddComponent<BoxCollider>());
            for (int i = 0; i < body.Colliders.Count; i++)
            {
                var box = body.Colliders[i];
                box.enabled = i < _parts.Count && Visible.Value;
                if (i >= _parts.Count)
                    continue;
                box.center = _parts[i].center;
                box.size = _parts[i].size;
            }
            body.Colliders.RemoveRange(_parts.Count, body.Colliders.Count - _parts.Count);
        }

        private void Despawn(PropBody body)
        {
            PropDespawning?.Invoke(body);
            if (body.Parent != null)
                body.Detach(_root);
            if (body.Voxel != null)
            {
                // Кусок не идёт в пул: у него свой меш и свой набор коробок.
                _live.Remove(body.Id);
                Destroy(body.gameObject);
                return;
            }
            if (body.Body != null)
                DestroyImmediate(body.Body);
            _live.Remove(body.Id);
            body.transform.SetParent(_poolRoot, false);
            _pool.Push(body);
        }

        private void OnWorldReset()
        {
            foreach (var key in new List<BucketKey>(_buckets.Keys))
                UnloadBucket(key);
            foreach (var body in new List<PropBody>(_live.Values))
                if (body != null && body.Parent == null)
                    DespawnGroup(body);
            _live.Clear();
            _states.Clear();
            _children.Clear();
            _anchorIndex.Clear();
            _despawnQueue.Clear();
            _despawnSet.Clear();
        }

        private void ApplyVisibility()
        {
            foreach (var body in _live.Values)
            {
                body.GetComponent<MeshRenderer>().enabled = Visible.Value;
                foreach (var part in body.Colliders)
                    part.enabled = Visible.Value;
            }
        }

        // ───────────────────────────── Состояния ─────────────────────────────

        private PropState GetState(PropBody body)
        {
            if (!_states.TryGetValue(body.Id, out var state))
            {
                state = new PropState { Id = body.Id, Def = body.DefIndex };
                _states.Add(body.Id, state);
            }
            return state;
        }

        /// <summary>Проп проснулся: с этого момента он «свободный» и стримится по своей позиции.</summary>
        internal void OnWoken(PropBody body)
        {
            MarkLoose(body);
            PropWoken?.Invoke(body);
        }

        /// <summary>Проп успокоился: запоминаем новое положение (вместе с детьми).</summary>
        internal void OnSettled(PropBody body) => StorePose(body);

        private void MarkLoose(PropBody body)
        {
            var state = GetState(body);
            state.Loose = true;
            StorePose(body);
        }

        private void StorePose(PropBody body)
        {
            var state = GetState(body);
            if (body.Parent != null)
            {
                state.Parent = body.Parent.Id;
                state.Position = body.Parent.transform.InverseTransformPoint(body.transform.position);
                state.Rotation = Quaternion.Inverse(body.Parent.transform.rotation) * body.transform.rotation;
            }
            else
            {
                state.Parent = 0;
                state.Position = body.transform.position;
                state.Rotation = body.transform.rotation;
            }
            foreach (var child in body.Children)
                StorePose(child);
        }

        // ───────────────────────────── Мир меняется ─────────────────────────────

        /// <summary>Клетка изменилась: оторвать прибитое к ней и разбудить пропы, стоявшие рядом (вдруг под ними была опора).</summary>
        private void OnCellChanged(int3 cell, Cell value)
        {
            bool solid = value.Material != 0 && _world.GetInfo(value.Material).IsSolid
                && (_world.GetInfo(value.Material).Kind != MaterialKind.Smooth || Density.IsSolid(value.Value));
            if (solid)
                return;

            if (_anchorIndex.TryGetValue(cell, out var roots))
            {
                _anchorIndex.Remove(cell);
                foreach (var id in roots)
                {
                    if (!_states.TryGetValue(id, out var state))
                        continue;
                    state.Anchors.Remove(cell);
                    bool live = _live.TryGetValue(id, out var body);
                    if (live)
                        body.Anchors.Remove(cell);
                    if (state.Anchors.Count == 0)
                    {
                        if (live)
                            body.Wake();
                        else
                            state.WakeOnSpawn = true;
                    }
                }
            }

            var center = new Vector3(cell.x + 0.5f, cell.y + 0.9f, cell.z + 0.5f);
            int count = Physics.OverlapBoxNonAlloc(center, new Vector3(0.7f, 0.7f, 0.7f), _overlap, Quaternion.identity, 1 << GameLayers.Props, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var body = _overlap[i].GetComponentInParent<PropBody>();
                if (body != null)
                    body.Wake();
            }
        }

        // ───────────────────────────── Гвозди ─────────────────────────────

        /// <summary>Прибить группу к клеткам мира: она замирает и держится, пока цела хоть одна клетка.</summary>
        public void NailToWorld(PropBody body, IReadOnlyList<int3> cells)
        {
            var root = body.Root;
            root.Freeze();
            var state = GetState(root);
            foreach (var cell in cells)
            {
                if (state.Anchors.Contains(cell))
                    continue;
                state.Anchors.Add(cell);
                root.Anchors.Add(cell);
                if (!_anchorIndex.TryGetValue(cell, out var list))
                    _anchorIndex[cell] = list = new List<ulong>();
                list.Add(root.Id);
            }
            state.Loose = true;
            StorePose(root);
        }

        /// <summary>Прибить проп к другому: две группы становятся одним телом (корень — группа цели).</summary>
        public void NailToProp(PropBody body, PropBody target)
        {
            var root = body.Root;
            var targetRoot = target.Root;
            if (root == targetRoot)
                return;
            // Прибиваемая группа становится частью группы цели. Её гвозди к миру переходят корню цели.
            if (root.Anchors.Count > 0)
            {
                var cells = new List<int3>(root.Anchors);
                ClearAnchors(root);
                NailToWorld(targetRoot, cells);
            }
            root.AttachTo(target);
            var state = GetState(root);
            state.Loose = true;
            AddChild(target.Id, root.Id);
            var targetState = GetState(targetRoot);
            targetState.Loose = true;
            StorePose(targetRoot);
        }

        /// <summary>Вытащить гвозди: из группы (если проп прибит к другому) или из мира. Освободившееся просыпается.</summary>
        public bool Unnail(PropBody body)
        {
            if (body.Parent != null)
            {
                var root = body.Root;
                RemoveChild(body.Parent.Id, body.Id);
                body.Detach(_root);
                GetState(body).Parent = 0;
                GetState(body).Loose = true;
                StorePose(body);
                StorePose(root);
                body.Wake();
                return true;
            }
            if (body.Anchors.Count > 0)
            {
                ClearAnchors(body);
                body.Wake();
                return true;
            }
            return false;
        }

        private void ClearAnchors(PropBody root)
        {
            var state = GetState(root);
            foreach (var cell in state.Anchors)
                if (_anchorIndex.TryGetValue(cell, out var list))
                    list.Remove(root.Id);
            state.Anchors.Clear();
            root.Anchors.Clear();
        }

        private void AddChild(ulong parent, ulong child)
        {
            if (!_children.TryGetValue(parent, out var list))
                _children[parent] = list = new List<ulong>();
            if (!list.Contains(child))
                list.Add(child);
            GetStateById(child).Parent = parent;
        }

        private void RemoveChild(ulong parent, ulong child)
        {
            if (_children.TryGetValue(parent, out var list))
                list.Remove(child);
        }

        private PropState GetStateById(ulong id) => _states[id];

        // ───────────────────────────── Поломка и разбор ─────────────────────────────

        /// <summary>Нанести урон пропу. Возвращает долю оставшейся прочности.</summary>
        public float Damage(PropBody body, int amount)
        {
            if (body.Voxel != null)
                return 1f; // кусок ломают по клеткам: DamageVoxel
            var state = GetState(body);
            state.Damage += amount;
            if (state.Damage >= body.Def.Hardness)
            {
                Remove(body, PropRemoveReason.Broken);
                return 0f;
            }
            return 1f - state.Damage / (float)body.Def.Hardness;
        }

        /// <summary>Проп сломан или разобран (например, кровать — точка возрождения).</summary>
        public bool IsRemoved(ulong id) => _states.TryGetValue(id, out var state) && state.Removed;

        /// <summary>Проп сейчас в мире (загружен).</summary>
        public bool TryGetLive(ulong id, out PropBody body) => _live.TryGetValue(id, out body) && body != null;

        public float Health(PropBody body) =>
            body.Voxel != null ? 1f : _states.TryGetValue(body.Id, out var state) ? 1f - state.Damage / (float)body.Def.Hardness : 1f;

        /// <summary>Убрать проп из мира (сломан, разобран, упал в пустоту). Дети отделяются и падают.</summary>
        public void Remove(PropBody body, PropRemoveReason reason)
        {
            for (int i = body.Children.Count - 1; i >= 0; i--)
            {
                var child = body.Children[i];
                RemoveChild(body.Id, child.Id);
                child.Detach(_root);
                var childState = GetState(child);
                childState.Parent = 0;
                childState.Loose = true;
                StorePose(child);
                child.Wake();
            }
            if (body.Parent != null)
                RemoveChild(body.Parent.Id, body.Id);
            if (body.Anchors.Count > 0)
                ClearAnchors(body);

            var bounds = body.Bounds;
            if (reason != PropRemoveReason.Void && body.Voxel == null)
            {
                EventBus<PropSalvagedEvent>.Publish(new PropSalvagedEvent { Def = body.Def, Disassembled = reason == PropRemoveReason.Disassembled, Position = bounds.center });
                if (reason == PropRemoveReason.Broken)
                    SpawnBreakFx(body.Def, bounds, body.transform.rotation);
            }
            PropRemoved?.Invoke(body, reason);
            GetState(body).Removed = true;
            Despawn(body);
        }

        private void SpawnBreakFx(PropDef def, Bounds bounds, Quaternion rotation)
        {
            Color c = def.DebrisColor;
            DustFx.Burst(_dustMaterial, bounds, (int)(def.Mass * 2f + 40f), new Color(c.r, c.g, c.b, 0.9f), new Color(c.r * 1.2f, c.g * 1.2f, c.b * 1.2f, 0.5f));
            // Несколько обломков разлетаются и исчезают через десяток секунд.
            int pieces = Mathf.Clamp((int)(bounds.size.magnitude * 3f), 3, 10);
            var mesh = PropMeshes.Build(PropShape.CardboardBox, Vector3.one);
            for (int i = 0; i < pieces; i++)
            {
                var go = new GameObject("PropShard") { layer = GameLayers.Debris };
                go.transform.position = bounds.center + Vector3.Scale(Random.insideUnitSphere * 0.4f, bounds.extents);
                go.transform.rotation = Random.rotation;
                float scale = Mathf.Clamp(bounds.size.magnitude * 0.12f, 0.08f, 0.35f) * Random.Range(0.6f, 1.2f);
                go.transform.localScale = Vector3.one * scale;
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = _material;
                go.AddComponent<BoxCollider>().center = new Vector3(0f, 0.5f, 0f);
                var rb = go.AddComponent<Rigidbody>();
                rb.mass = Mathf.Max(0.2f, def.Mass / pieces);
                rb.linearVelocity = Random.insideUnitSphere * 3f + Vector3.up * 2f;
                Destroy(go, Random.Range(8f, 12f));
            }
            Destroy(mesh, 13f);
        }

        // ───────────────────────────── Помощники ─────────────────────────────

        public static bool TryGetProp(Collider collider, out PropBody body)
        {
            body = collider != null ? collider.GetComponentInParent<PropBody>() : null;
            return body != null;
        }

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

        private const ulong PlayerMadeIdBase = 0xFFFF000000000000UL;
        private ulong _nextSpawnedId = PlayerMadeIdBase;

        /// <summary>Проп создан игроком или консолью (не генерацией): на нём нет лута полок.</summary>
        public static bool IsPlayerMade(ulong id) => id >= PlayerMadeIdBase;

        /// <summary>Создать новый проп (не из генерации) — для консоли и, позже, построек игрока.</summary>
        public PropBody SpawnNew(ushort defIndex, Vector3 position, Quaternion rotation)
        {
            ulong id = _nextSpawnedId++;
            var state = new PropState { Id = id, Def = defIndex, Loose = true, Position = position, Rotation = rotation };
            _states.Add(id, state);
            var body = SpawnGroup(state);
            body.Wake();
            return body;
        }

        [ConsoleCommand("prop_spawn", "Создать проп перед игроком: prop_spawn <id> (например, chair, cardboard_box, shelf).", Flags = ConsoleFlags.Cheat)]
        private static string PropSpawnCommand(string id)
        {
            var m = Instance;
            var camera = Camera.main;
            if (m == null || camera == null)
                return "Нет PropManager или камеры.";
            if (!m.Props.TryGetIndex(id, out ushort index) && !m.Props.TryGetIndex("prop." + id, out index))
                return $"Нет пропа '{id}'. Список: props_list";
            var def = m.Props.Get(index);
            Vector3 forward = camera.transform.forward;
            forward.y = 0f;
            forward = forward.sqrMagnitude > 0.001f ? forward.normalized : Vector3.forward;
            Vector3 position = camera.transform.position + forward * (1.5f + def.Size.magnitude * 0.5f) + Vector3.up * 0.2f;
            m.SpawnNew(index, position, Quaternion.LookRotation(-forward));
            return $"Создан: {def.DisplayName}";
        }

        [ConsoleCommand("props_list", "Список пропов: ID, масса, прочность.")]
        private static string PropsList()
        {
            var m = Instance;
            if (m == null)
                return "Нет PropManager.";
            var sb = new System.Text.StringBuilder("Пропы:");
            foreach (var def in m.Props.All)
                sb.Append($"\n  {def.Id,-20} {def.DisplayName,-20} {def.Mass,6:0} кг  прочность {def.Hardness}");
            return sb.ToString();
        }

        [ConsoleCommand("props_info", "Пропы: живые, корзины, изменённые за сессию.")]
        private static string PropsInfo()
        {
            var m = Instance;
            return m == null ? "Нет PropManager." : $"Пропов в мире {m.LiveCount}, корзин {m.LoadedBuckets}, изменённых за сессию {m.StateCount}";
        }
    }
}
