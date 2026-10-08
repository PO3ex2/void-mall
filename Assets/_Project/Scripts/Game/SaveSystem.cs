using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using VoidMall.Console;
using VoidMall.Crafting;
using VoidMall.Items;
using VoidMall.Survival;
using VoidMall.World;

namespace VoidMall.Game
{
    /// <summary>Описание мира в сейве (world.json).</summary>
    [Serializable]
    public sealed class WorldMeta
    {
        public int SaveVersion = SaveSystem.SaveVersion;
        public int GeneratorVersion = WorldConstants.GeneratorVersion;
        public string Name;

        /// <summary>Сид строкой (uint).</summary>
        public string Seed;
        public float AnomalyFrequency = 1f;

        /// <summary>Настройки мира на момент создания — пока справочно (до генератора миров этапа 10 действует ассет WorldSettings).</summary>
        public float LootAmount = 1f, LootRarity = 1f, EnemyDensity = 1f, DayLengthMinutes = 60f;
        public double TotalHours;
        public float PlaySeconds;
        public string CreatedUtc, SavedUtc;
    }

    /// <summary>Состояние мира кроме клеток (state.json.gz).</summary>
    [Serializable]
    public sealed class WorldState
    {
        public PropsData Props = new();
        public ContainersData Containers = new();
        public WorldItemsData Items = new();
        public ShelvesData Shelves = new();
        public StationsData Stations = new();
        public PuddlesData Puddles = new();
    }

    /// <summary>
    /// Сохранения (этап 9). Мир — папка Saves/&lt;имя&gt;: world.json (сид, версии, время), regions/ (изменённые колонны,
    /// RegionStore), state.json.gz (пропы, хранилища, предметы на полу, полки, станции, лужи), players/local.json
    /// (PlayerSnapshot). При старте мира сцены (ChunkManager.Starting) продолжается последний мир (или создаётся первый). Автосейв по таймеру,
    /// при выходе; изменённые колонны уходят в сейв и при выгрузке. Запись — в фоне, каждый файл атомарно (через .tmp).
    /// Смена мира — сохранить текущий и перезагрузить сцену: всё поднимается заново, без ручного сброса систем.
    /// </summary>
    public sealed class SaveSystem : MonoBehaviour
    {
        public const int SaveVersion = 1;
        public const string LastWorldKey = "vm_last_world";
        private const string DefaultWorldName = "Мир 1";

        public static readonly CVar<float> AutosaveMinutes = new("sv_autosave", 5f, "Автосейв каждые N минут (0 — выключен).", ConsoleFlags.Archive);

        public static SaveSystem Instance { get; private set; }

        public static string SavesRoot => Path.Combine(Application.persistentDataPath, "Saves");

        public bool IsActive => _meta != null;
        public string WorldName => _meta?.Name;
        public string WorldFolder { get; private set; }

        private WorldMeta _meta;
        private RegionStore _store;
        private Task _writing;
        private float _autosaveTimer;
        private float _sessionStart;
        private string _pendingWorld;
        private uint? _pendingSeed;
        private string _pendingDelete;
        private float _pendingDeleteTime;
        private bool _returnToLobby;

        private void Awake() => Instance = this;

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Instance = null;

        // ───────────────────────────── Открытие мира ─────────────────────────────

        /// <summary>Мир выбран командой (world_load, world_new) до перезагрузки сцены — открыть его сразу.</summary>
        public bool HasPending => _pendingWorld != null;

        /// <summary>Игрок в мире (а не в лобби): только тогда его снимок пишется в сейв.</summary>
        public bool PlayerInWorld { get; set; }

        /// <summary>Сцену перезагрузили командой «в лобби» — мир не открывать.</summary>
        public bool ConsumeReturnToLobby()
        {
            bool value = _returnToLobby;
            _returnToLobby = false;
            return value;
        }

        /// <summary>Новая сцена мира (ChunkManager.Starting): ничего не открыто, игрок не в мире.</summary>
        public void BeginScene()
        {
            _meta = null;
            _store = null;
            WorldFolder = null;
            PlayerInWorld = false;
        }

        /// <summary>Открыть мир, выбранный командой до перезагрузки, вместе с игроком.</summary>
        public bool OpenPending()
        {
            string name = _pendingWorld;
            uint? seed = _pendingSeed;
            _pendingWorld = null;
            _pendingSeed = null;
            return name != null && OpenWorld(name, seed, null, placePlayer: true, out _);
        }

        /// <summary>Продолжить последний мир (без лобби: dev_skip_lobby, сцены без лобби) — или создать первый.</summary>
        public bool OpenLast()
        {
            string name = PlayerPrefs.GetString(LastWorldKey, null);
            if (string.IsNullOrEmpty(name) || !File.Exists(Path.Combine(FolderOf(name), "world.json")))
                name = FirstWorld() ?? DefaultWorldName;
            return OpenWorld(name, null, null, placePlayer: true, out _);
        }

        public static bool Exists(string name) => File.Exists(Path.Combine(FolderOf(name), "world.json"));

        /// <summary>
        /// Открыть мир (существующий или новый с настройками template). placePlayer — сразу вернуть игроку его снимок (или
        /// поставить у входа); иначе снимок отдаётся в player (null — нового мира или игрок не сохранён), игроком займётся
        /// портал. false — сейв не открыть (повреждён, новее игры).
        /// </summary>
        public bool OpenWorld(string name, uint? newSeed, WorldMeta template, bool placePlayer, out PlayerSnapshot player)
        {
            player = null;
            var world = ChunkManager.Instance;
            if (world == null)
                return false;
            string folder = FolderOf(name);
            string metaPath = Path.Combine(folder, "world.json");
            bool exists = File.Exists(metaPath);
            WorldMeta meta;
            if (exists)
            {
                try
                {
                    meta = JsonUtility.FromJson<WorldMeta>(File.ReadAllText(metaPath));
                }
                catch (Exception e)
                {
                    DevConsole.Error($"Сейв «{name}» не прочитан ({e.Message}) — сохранение выключено, чтобы его не затереть.");
                    return false;
                }
                if (meta.SaveVersion > SaveVersion)
                {
                    DevConsole.Error($"Сейв «{name}» сделан более новой версией игры (формат {meta.SaveVersion}) — не открываю.");
                    return false;
                }
                if (meta.GeneratorVersion != WorldConstants.GeneratorVersion)
                    DevConsole.Warn($"Мир «{name}» создан генератором версии {meta.GeneratorVersion}, сейчас {WorldConstants.GeneratorVersion}: новые места сгенерируются по-новому, на границе с сохранёнными возможны швы.");
            }
            else
            {
                meta = template ?? DefaultMeta(world);
                meta.Seed = (newSeed ?? (uint.TryParse(meta.Seed, out uint s) ? s : world.Seed)).ToString();
                meta.TotalHours = world.Settings != null ? world.Settings.StartHour : 9.0;
                meta.CreatedUtc = DateTime.UtcNow.ToString("o");
            }
            meta.Name = name;
            if (!uint.TryParse(meta.Seed, out uint seed))
                seed = world.Seed;

            world.ColumnStore = null;
            world.LoadWorld(seed, meta.AnomalyFrequency);
            world.ApplyWorldSettings(meta.LootAmount, meta.LootRarity, meta.EnemyDensity);
            WorldClock.DayLength.Value = meta.DayLengthMinutes;
            _store = new RegionStore(folder, world.Materials);
            world.ColumnStore = _store;
            WorldClock.Instance?.Restore(meta.TotalHours);

            if (exists)
                RestoreState(folder);
            _meta = meta;
            WorldFolder = folder;
            _autosaveTimer = 0f;
            _sessionStart = Time.unscaledTime;
            PlayerPrefs.SetString(LastWorldKey, name);
            PlayerPrefs.Save();
            string playerPath = Path.Combine(folder, "players", "local.json");
            if (placePlayer)
            {
                PlayerInWorld = true;
                StartCoroutine(PreparePlayer(playerPath, seed, meta.AnomalyFrequency));
            }
            else
            {
                player = ReadPlayer(playerPath);
            }
            DevConsole.Log(exists ? $"Мир «{name}» загружен (сид {seed}, день {Mathf.FloorToInt((float)(meta.TotalHours / 24.0)) + 1})." : $"Новый мир «{name}» (сид {seed}).");
            return true;
        }

        /// <summary>Настройки нового мира по умолчанию — из ассета WorldSettings.</summary>
        public static WorldMeta DefaultMeta(ChunkManager world)
        {
            var settings = world != null ? world.Settings : null;
            return new WorldMeta
            {
                Seed = (world != null ? world.Seed : 1337u).ToString(),
                AnomalyFrequency = settings != null ? settings.AnomalyFrequency : 1f,
                LootAmount = settings != null ? settings.LootAmount : 1f,
                LootRarity = settings != null ? settings.LootRarity : 1f,
                EnemyDensity = settings != null ? settings.EnemyDensity : 1f,
                DayLengthMinutes = settings != null ? settings.DayLengthMinutes : 60f,
            };
        }

        private static PlayerSnapshot ReadPlayer(string path)
        {
            if (!File.Exists(path))
                return null;
            try
            {
                return PlayerSnapshot.FromJson(File.ReadAllText(path));
            }
            catch (Exception e)
            {
                DevConsole.Error($"Игрок не прочитан ({e.Message}) — начинаете у входа.");
                return null;
            }
        }

        private void RestoreState(string folder)
        {
            string path = Path.Combine(folder, "state.json.gz");
            if (!File.Exists(path))
                return;
            WorldState state;
            try
            {
                state = JsonUtility.FromJson<WorldState>(ReadGzip(path));
            }
            catch (Exception e)
            {
                DevConsole.Error($"Состояние мира не прочитано ({e.Message}) — пропы, хранилища и предметы будут как новые.");
                return;
            }
            int skipped = PropManager.Instance != null && state.Props != null ? PropManager.Instance.Restore(state.Props) : 0;
            if (state.Containers != null)
                ContainerSystem.Instance?.Restore(state.Containers);
            if (state.Items != null)
                WorldItems.Instance?.Restore(state.Items);
            if (state.Shelves != null)
                ShelfLoot.Instance?.Restore(state.Shelves);
            if (state.Stations != null)
                CraftingSystem.Instance?.Restore(state.Stations);
            if (state.Puddles != null)
                AnomalyPuddles.Instance?.Restore(state.Puddles);
            if (skipped > 0)
                DevConsole.Warn($"Пропущено пропов неизвестных видов: {skipped} (убраны из игры).");
        }

        /// <summary>Игрок появляется после Start своих компонентов: тогда вернуть его снимок (или поставить у входа нового мира).</summary>
        private IEnumerator PreparePlayer(string path, uint seed, float frequency)
        {
            while (PlayerInventory.Local == null || PlayerInventory.Local.Items == null)
                yield return null;
            var player = PlayerInventory.Local.gameObject;
            if (File.Exists(path))
            {
                try
                {
                    PlayerSnapshot.FromJson(File.ReadAllText(path)).Restore(player);
                }
                catch (Exception e)
                {
                    DevConsole.Error($"Игрок не прочитан ({e.Message}) — начинаете у входа.");
                }
            }
            else if (player.TryGetComponent(out VoidMall.Player.PlayerMotor motor))
            {
                motor.Teleport(MallPlanner.FindSpawn(seed, frequency));
            }
        }

        // ───────────────────────────── Сохранение ─────────────────────────────

        private void Update()
        {
            if (!IsActive)
                return;
            float minutes = AutosaveMinutes.Value;
            if (minutes <= 0f)
                return;
            _autosaveTimer += Time.unscaledDeltaTime;
            if (_autosaveTimer >= minutes * 60f)
            {
                _autosaveTimer = 0f;
                Save(wait: false, quiet: true);
            }
        }

        private void OnApplicationQuit() => Save(wait: true, quiet: true);

        /// <summary>Сохранить мир. wait — дождаться записи (выход, смена мира). Возвращает текст для консоли.</summary>
        public string Save(bool wait, bool quiet = false)
        {
            if (!IsActive || ChunkManager.Instance == null)
                return "Сохранять нечего: мир не открыт.";
            // Мёртвого не сохраняем: иначе сумка с вещами и пустой инвентарь разошлись бы с прошлым сейвом.
            if (PlayerVitals.Local != null && PlayerVitals.Local.IsDead)
                return "Сейчас не сохранить: вы погибли.";

            _writing?.Wait();
            var started = System.Diagnostics.Stopwatch.StartNew();
            int columns = ChunkManager.Instance.StoreModifiedColumns();
            var files = _store.TakeDirty();

            _meta.TotalHours = WorldClock.Instance != null ? WorldClock.Instance.TotalHours : _meta.TotalHours;
            _meta.PlaySeconds += Time.unscaledTime - _sessionStart;
            _sessionStart = Time.unscaledTime;
            _meta.SavedUtc = DateTime.UtcNow.ToString("o");
            _meta.GeneratorVersion = WorldConstants.GeneratorVersion;
            _meta.SaveVersion = SaveVersion;
            files.Add((Path.Combine(WorldFolder, "world.json"), Encoding.UTF8.GetBytes(JsonUtility.ToJson(_meta, true))));

            var state = new WorldState
            {
                Props = PropManager.Instance != null ? PropManager.Instance.Capture() : new PropsData(),
                Containers = ContainerSystem.Instance != null ? ContainerSystem.Instance.Capture() : new ContainersData(),
                Items = WorldItems.Instance != null ? WorldItems.Instance.Capture() : new WorldItemsData(),
                Shelves = ShelfLoot.Instance != null ? ShelfLoot.Instance.Capture() : new ShelvesData(),
                Stations = CraftingSystem.Instance != null ? CraftingSystem.Instance.Capture() : new StationsData(),
                Puddles = AnomalyPuddles.Instance != null ? AnomalyPuddles.Instance.Capture() : new PuddlesData(),
            };
            files.Add((Path.Combine(WorldFolder, "state.json.gz"), Gzip(JsonUtility.ToJson(state))));
            if (PlayerInWorld && PlayerInventory.Local != null && PlayerInventory.Local.Items != null)
                files.Add((Path.Combine(WorldFolder, "players", "local.json"), Encoding.UTF8.GetBytes(PlayerSnapshot.Capture(PlayerInventory.Local.gameObject).ToJson(true))));

            long ms = started.ElapsedMilliseconds;
            _writing = Task.Run(() => WriteAll(files));
            if (wait)
                _writing.Wait();
            string message = $"Сохранено «{_meta.Name}»: колонн {columns}, файлов {files.Count}, {ms} мс на сборку.";
            if (!quiet)
                return message;
            Debug.Log("[Save] " + message);
            return null;
        }

        private static void WriteAll(List<(string path, byte[] data)> files)
        {
            foreach (var (path, data) in files)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    string temp = path + ".tmp";
                    File.WriteAllBytes(temp, data);
                    if (File.Exists(path))
                        File.Replace(temp, path, null);
                    else
                        File.Move(temp, path);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Save] Не записан {path}: {e.Message}");
                }
            }
        }

        private static byte[] Gzip(string text)
        {
            using var output = new MemoryStream();
            using (var gzip = new GZipStream(output, System.IO.Compression.CompressionLevel.Fastest))
            {
                var bytes = Encoding.UTF8.GetBytes(text);
                gzip.Write(bytes, 0, bytes.Length);
            }
            return output.ToArray();
        }

        private static string ReadGzip(string path)
        {
            using var input = File.OpenRead(path);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var reader = new StreamReader(gzip, Encoding.UTF8);
            return reader.ReadToEnd();
        }

        // ───────────────────────────── Миры ─────────────────────────────

        public static string FolderOf(string name)
        {
            var clean = new StringBuilder();
            foreach (char c in name.Trim())
                clean.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c);
            return Path.Combine(SavesRoot, clean.Length > 0 ? clean.ToString() : "_");
        }

        /// <summary>Миры в папке сейвов: (имя папки, описание) по времени последнего сохранения.</summary>
        public static List<(string name, WorldMeta meta)> ListWorlds()
        {
            var worlds = new List<(string, WorldMeta)>();
            if (!Directory.Exists(SavesRoot))
                return worlds;
            foreach (var folder in Directory.GetDirectories(SavesRoot))
            {
                string path = Path.Combine(folder, "world.json");
                if (!File.Exists(path))
                    continue;
                try
                {
                    var meta = JsonUtility.FromJson<WorldMeta>(File.ReadAllText(path));
                    worlds.Add((meta.Name ?? Path.GetFileName(folder), meta));
                }
                catch
                {
                    worlds.Add((Path.GetFileName(folder), null));
                }
            }
            worlds.Sort((a, b) => string.CompareOrdinal(b.Item2?.SavedUtc ?? "", a.Item2?.SavedUtc ?? ""));
            return worlds;
        }

        private static string FirstWorld()
        {
            var worlds = ListWorlds();
            return worlds.Count > 0 ? worlds[0].name : null;
        }

        /// <summary>Сохранить мир и вернуться в лобби (сцена перезагружается: мир выгружается начисто).</summary>
        public void ReturnToLobby()
        {
            if (IsActive)
                Save(wait: true, quiet: true);
            _returnToLobby = true;
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        /// <summary>Переименовать мир (папку и имя в world.json). Null — получилось, иначе причина.</summary>
        public static string Rename(string name, string newName)
        {
            newName = newName?.Trim();
            if (string.IsNullOrEmpty(newName))
                return "Пустое имя";
            if (!Exists(name))
                return $"Мира «{name}» нет";
            if (Instance != null && Instance.WorldName == name)
                return "Текущий мир не переименовать";
            string from = FolderOf(name), to = FolderOf(newName);
            if (from != to && Directory.Exists(to))
                return $"Мир «{newName}» уже есть";
            string metaPath = Path.Combine(from, "world.json");
            var meta = JsonUtility.FromJson<WorldMeta>(File.ReadAllText(metaPath));
            meta.Name = newName;
            File.WriteAllText(metaPath, JsonUtility.ToJson(meta, true));
            if (from != to)
                Directory.Move(from, to);
            if (PlayerPrefs.GetString(LastWorldKey, null) == name)
                PlayerPrefs.SetString(LastWorldKey, newName);
            return null;
        }

        /// <summary>Удалить мир. Null — удалён, иначе причина.</summary>
        public static string Delete(string name)
        {
            string folder = FolderOf(name);
            if (!Directory.Exists(folder))
                return $"Мира «{name}» нет";
            if (Instance != null && Instance.WorldName == name)
                return "Текущий мир не удалить";
            Directory.Delete(folder, true);
            return null;
        }

        /// <summary>Сохранить текущий мир и перезагрузить сцену с другим (новым или существующим).</summary>
        private void SwitchTo(string name, uint? seed)
        {
            if (IsActive)
                Save(wait: true, quiet: true);
            _pendingWorld = name;
            _pendingSeed = seed;
            PlayerPrefs.SetString(LastWorldKey, name);
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        // ───────────────────────────── Консоль ─────────────────────────────

        [ConsoleCommand("save", "Сохранить мир сейчас.")]
        private static string SaveCommand() => Instance != null ? Instance.Save(wait: true) : "Нет системы сохранений.";

        [ConsoleCommand("worlds", "Список миров (сейвов).")]
        private static string WorldsCommand()
        {
            var worlds = ListWorlds();
            if (worlds.Count == 0)
                return "Миров нет. world_new <имя> [сид] — создать.";
            var sb = new StringBuilder($"Миры ({SavesRoot}):");
            foreach (var (name, meta) in worlds)
            {
                string current = Instance != null && Instance.WorldName == name ? " ← текущий" : "";
                sb.Append(meta == null
                    ? $"\n  {name} — повреждён"
                    : $"\n  {name}: сид {meta.Seed}, день {Mathf.FloorToInt((float)(meta.TotalHours / 24.0)) + 1}, в игре {meta.PlaySeconds / 3600f:0.0} ч{current}");
            }
            return sb.ToString();
        }

        [ConsoleCommand("world_new", "Создать мир и перейти в него: world_new <имя> [сид].")]
        private static string WorldNewCommand(string name, string seed = "")
        {
            if (Instance == null || ChunkManager.Instance == null)
                return "Нет мира или системы сохранений.";
            if (string.IsNullOrWhiteSpace(name))
                return "Укажите имя.";
            if (Directory.Exists(FolderOf(name)))
                return $"Мир «{name}» уже есть — world_load {name}.";
            uint value = string.IsNullOrEmpty(seed) ? (uint)UnityEngine.Random.Range(1, int.MaxValue)
                : uint.TryParse(seed, out uint parsed) ? parsed : (uint)seed.GetHashCode();
            Instance.SwitchTo(name, value);
            return null;
        }

        [ConsoleCommand("world_load", "Сохранить текущий мир и загрузить другой: world_load <имя>.")]
        private static string WorldLoadCommand(string name)
        {
            if (Instance == null || ChunkManager.Instance == null)
                return "Нет мира или системы сохранений.";
            if (!File.Exists(Path.Combine(FolderOf(name), "world.json")))
                return $"Мира «{name}» нет. Список: worlds";
            Instance.SwitchTo(name, null);
            return null;
        }

        [ConsoleCommand("world_delete", "Удалить мир: world_delete <имя> (повторить команду в течение 10 с для подтверждения).")]
        private static string WorldDeleteCommand(string name)
        {
            if (Instance == null)
                return "Нет системы сохранений.";
            string folder = FolderOf(name);
            if (!Directory.Exists(folder))
                return $"Мира «{name}» нет.";
            if (Instance.WorldName == name)
                return "Нельзя удалить текущий мир — сначала загрузите другой.";
            if (Instance._pendingDelete != name || Time.unscaledTime - Instance._pendingDeleteTime > 10f)
            {
                Instance._pendingDelete = name;
                Instance._pendingDeleteTime = Time.unscaledTime;
                return $"Удалить мир «{name}» безвозвратно? Повторите команду в течение 10 с.";
            }
            Instance._pendingDelete = null;
            Directory.Delete(folder, true);
            return $"Мир «{name}» удалён.";
        }
    }
}
