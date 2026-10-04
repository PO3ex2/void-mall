using System.Collections.Generic;
using UnityEngine;
using VoidMall.Core;
using VoidMall.World;

namespace VoidMall.Items
{
    /// <summary>Предмет, лежащий в мире (выброшенный, сбитый с полки, выпавший из обломка).</summary>
    public sealed class WorldItem : MonoBehaviour
    {
        public ItemStack Stack { get; internal set; }
        internal WorldItems.Entry Entry;
        internal Rigidbody Body;
    }

    /// <summary>
    /// Предметы в мире: обычная физика (засыпают средствами PhysX и просыпаются от любого касания — сдвинутая полка
    /// роняет лежащее на ней), стриминг по расстоянию до игрока и запоминание за сессию.
    /// Слой Debris: игрок через них проходит, но их можно подобрать прицелом (E).
    /// </summary>
    [DefaultExecutionOrder(-160)]
    public sealed class WorldItems : MonoBehaviour
    {
        private const float SpawnRadius = 40f;
        private const float DespawnRadius = 48f;

        public static WorldItems Instance { get; private set; }

        internal sealed class Entry
        {
            public ItemStack Stack;
            public Vector3 Position;
            public Quaternion Rotation;
            public WorldItem Instance;
        }

        private readonly List<Entry> _entries = new();
        private readonly Stack<WorldItem> _pool = new();
        private Transform _root;
        private float _streamTimer;

        public int Count => _entries.Count;

        private void Awake()
        {
            Instance = this;
            _root = new GameObject("World Items").transform;
            _root.SetParent(transform, false);
        }

        private void OnEnable()
        {
            var world = ChunkManager.Instance;
            if (world != null)
            {
                world.CellChanged += OnCellChanged;
                world.WorldReset += OnWorldReset;
            }
        }

        private void OnDisable()
        {
            var world = ChunkManager.Instance;
            if (world != null)
            {
                world.CellChanged -= OnCellChanged;
                world.WorldReset -= OnWorldReset;
            }
        }

        private void OnCellChanged(Unity.Mathematics.int3 cell, Cell value) =>
            WakeAround(new Vector3(cell.x + 0.5f, cell.y + 1f, cell.z + 0.5f), 1.6f);

        private void OnWorldReset()
        {
            foreach (var entry in _entries)
                if (entry.Instance != null)
                    Release(entry.Instance);
            _entries.Clear();
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Instance = null;

        /// <summary>Положить предмет в мир (со скоростью — например, бросок или выпадение с полки).</summary>
        public WorldItem Spawn(ItemStack stack, Vector3 position, Vector3 velocity, Quaternion? rotation = null, bool tumble = true)
        {
            if (stack == null || stack.Count <= 0 || ItemSystem.Instance == null)
                return null;
            var entry = new Entry { Stack = stack, Position = position, Rotation = rotation ?? Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) };
            _entries.Add(entry);
            var item = Instantiate(entry);
            item.Body.WakeUp();
            item.Body.linearVelocity = velocity;
            item.Body.angularVelocity = tumble ? Random.insideUnitSphere * 2f : Vector3.zero;
            return item;
        }

        /// <summary>Подобрать: убрать из мира и вернуть стак.</summary>
        public ItemStack Take(WorldItem item)
        {
            var entry = item.Entry;
            _entries.Remove(entry);
            var stack = entry.Stack;
            Release(item);
            return stack;
        }

        private WorldItem Instantiate(Entry entry)
        {
            var system = ItemSystem.Instance;
            var def = system.Items.Get(entry.Stack.Def);
            WorldItem item;
            if (_pool.Count > 0)
            {
                item = _pool.Pop();
                item.gameObject.SetActive(true);
            }
            else
            {
                var go = new GameObject("WorldItem") { layer = GameLayers.Debris };
                go.transform.SetParent(_root, false);
                go.AddComponent<MeshFilter>();
                go.AddComponent<MeshRenderer>();
                go.AddComponent<BoxCollider>();
                item = go.AddComponent<WorldItem>();
                item.Body = go.AddComponent<Rigidbody>();
                item.Body.interpolation = RigidbodyInterpolation.Interpolate;
                item.Body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            }
            item.gameObject.name = def.Id;
            item.transform.SetPositionAndRotation(entry.Position, entry.Rotation);
            // Тело тоже: иначе на следующем шаге физики оно вернёт объект туда, где он был в пуле (или в начало
            // координат под миром) — предмет проваливался и «пропадал».
            item.Body.position = entry.Position;
            item.Body.rotation = entry.Rotation;
            item.GetComponent<MeshFilter>().sharedMesh = system.Items.GetMesh(entry.Stack.Def);
            item.GetComponent<MeshRenderer>().sharedMaterial = def.Material != null ? def.Material : system.Material;
            var box = item.GetComponent<BoxCollider>();
            Vector3 size = Vector3.Max(def.ModelSize, Vector3.one * 0.06f);
            box.size = size;
            box.center = new Vector3(0f, size.y * 0.5f, 0f);
            item.Body.mass = Mathf.Max(0.05f, def.Weight * entry.Stack.Count);
            item.Body.isKinematic = false;
            item.Body.linearVelocity = Vector3.zero;
            item.Body.angularVelocity = Vector3.zero;
            // Подгруженный обратно предмет лежит, пока его не тронут.
            item.Body.Sleep();
            item.Stack = entry.Stack;
            item.Entry = entry;
            entry.Instance = item;
            return item;
        }

        private void Release(WorldItem item)
        {
            if (item.Entry != null)
                item.Entry.Instance = null;
            item.Entry = null;
            item.Stack = null;
            item.Body.isKinematic = true;
            item.gameObject.SetActive(false);
            _pool.Push(item);
        }

        private static readonly Perf.Probe FixedProbe = Perf.Register("Items.WorldItems.Fixed");

        private void FixedUpdate()
        {
            using var perf = Perf.Measure(FixedProbe);
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                var item = _entries[i].Instance;
                if (item == null)
                    continue;
                if (item.transform.position.y < -40f)
                {
                    _entries.RemoveAt(i);
                    Release(item);
                    continue;
                }
                if (item.Body.IsSleeping())
                {
                    _entries[i].Position = item.transform.position;
                    _entries[i].Rotation = item.transform.rotation;
                }
            }
        }

        private static readonly Perf.Probe UpdateProbe = Perf.Register("Items.WorldItems.Stream");

        private void Update()
        {
            using var perf = Perf.Measure(UpdateProbe);
            _streamTimer -= Time.deltaTime;
            if (_streamTimer > 0f || WorldViewer.Primary == null || ItemSystem.Instance == null)
                return;
            _streamTimer = 0.3f;
            Vector3 viewer = WorldViewer.Primary.transform.position;
            foreach (var entry in _entries)
            {
                Vector3 position = entry.Instance != null ? entry.Instance.transform.position : entry.Position;
                float distance = Vector3.Distance(position, viewer);
                if (entry.Instance != null && distance > DespawnRadius)
                {
                    entry.Position = entry.Instance.transform.position;
                    entry.Rotation = entry.Instance.transform.rotation;
                    Release(entry.Instance);
                }
                else if (entry.Instance == null && distance < SpawnRadius)
                {
                    Instantiate(entry);
                }
            }
        }

        /// <summary>Мир меняется (выбили пол под предметами) — разбудить лежащие рядом.</summary>
        public void WakeAround(Vector3 center, float radius)
        {
            foreach (var entry in _entries)
                if (entry.Instance != null && Vector3.Distance(entry.Instance.transform.position, center) < radius)
                    entry.Instance.Body.WakeUp();
        }

        /// <summary>Где лежит стак (если он ещё в мире): для маркера сумки с вещами после смерти.</summary>
        public bool TryGetPosition(ItemStack stack, out Vector3 position)
        {
            foreach (var entry in _entries)
            {
                if (entry.Stack != stack)
                    continue;
                position = entry.Instance != null ? entry.Instance.transform.position : entry.Position;
                return true;
            }
            position = default;
            return false;
        }

        public static bool TryGet(Collider collider, out WorldItem item)
        {
            item = collider != null ? collider.GetComponent<WorldItem>() : null;
            return item != null && item.Stack != null;
        }
    }
}
