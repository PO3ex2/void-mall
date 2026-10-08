using System.Collections;
using UnityEngine;
using VoidMall.Console;
using VoidMall.Player;
using VoidMall.Survival;
using VoidMall.UI;
using VoidMall.World;

namespace VoidMall.Game
{
    public enum FlowState
    {
        /// <summary>Игрок в лобби, мир не выбран.</summary>
        Lobby,

        /// <summary>Мир выбран и строится вокруг выхода портала.</summary>
        Loading,

        /// <summary>Мир готов, портал открыт; игрок ещё в лобби.</summary>
        Ready,

        /// <summary>Игрок в мире.</summary>
        InWorld,
    }

    /// <summary>
    /// Поток игры (этап 10): лобби → мир выбран на ПК → мир строится вокруг выхода портала (у игрока в лобби своей точки
    /// прогрузки нет, мир грузится вокруг «якоря») → портал открывается → игрок проходит → через PortalClose секунд портал
    /// схлопывается, лобби выключается. Выход портала — у входа в мол (новый мир) или там, где игрок был (сейв).
    /// dev_skip_lobby — сразу последний мир, как до этапа 10. «В лобби» из мира — сохранить и перезагрузить сцену.
    /// </summary>
    public sealed class GameFlow : MonoBehaviour
    {
        public static readonly CVar<bool> SkipLobby = new("dev_skip_lobby", false, "Разработка: сразу в последний мир, без лобби.", ConsoleFlags.Archive);
        public static readonly CVar<float> PortalClose = new("sv_portal_close", 60f, "Через сколько секунд после выхода в мир схлопывается портал.");

        /// <summary>Радиус готовых колонн вокруг выхода портала, чтобы он открылся.</summary>
        /// <summary>Радиус зоны портала в колоннах: она должна прогрузиться, чтобы портал открылся (карта загрузки — та же зона).</summary>
        public const int ReadyRadius = 2;

        public static GameFlow Instance { get; private set; }

        public FlowState State { get; private set; } = FlowState.InWorld;
        public float Progress { get; private set; }
        public string WorldName { get; private set; }

        /// <summary>Точка выхода портала в мире, пока он грузится и открыт (null — в лобби без мира или уже в мире).</summary>
        public Vector3? LoadCenter => _anchor != null ? _anchor.position : null;

        private LobbyRoom _lobby;
        private PlayerSnapshot _pending;
        private Transform _anchor;
        private GameObject _worldPortal;
        private float _portalTimer;
        private bool _portalClosing;

        private void Awake()
        {
            Instance = this;
            ChunkManager.Starting += OnWorldStarting;
        }

        private void OnDestroy()
        {
            ChunkManager.Starting -= OnWorldStarting;
            if (Instance == this)
                Instance = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Instance = null;

        // ───────────────────────────── Старт сцены ─────────────────────────────

        private void OnWorldStarting(ChunkManager world)
        {
            if (world != ChunkManager.Instance)
                return;
            StopAllCoroutines();
            var save = SaveSystem.Instance;
            save?.BeginScene();
            _lobby = FindAnyObjectByType<LobbyRoom>(FindObjectsInactive.Include);
            _pending = null;
            _anchor = null;
            _worldPortal = null;
            _portalClosing = false;
            Progress = 0f;
            bool toLobby = save != null && save.ConsumeReturnToLobby();

            if (save != null && save.HasPending && save.OpenPending())
                EnterWorldDirect();
            else if (save != null && (_lobby == null || (!toLobby && SkipLobby.Value)))
            {
                save.OpenLast();
                EnterWorldDirect();
            }
            else if (_lobby != null)
                StartCoroutine(EnterLobby());
            else
                EnterWorldDirect();
        }

        private void EnterWorldDirect()
        {
            State = FlowState.InWorld;
            WorldName = SaveSystem.Instance?.WorldName;
            Progress = 1f;
            GameHud.Visible = true;
            if (_lobby != null)
                _lobby.gameObject.SetActive(false);
        }

        private IEnumerator EnterLobby()
        {
            State = FlowState.Lobby;
            GameHud.Visible = false;
            _lobby.gameObject.SetActive(true);
            while (PlayerMotor.Local == null || PlayerVitals.Local == null || PlayerCameraRig.Local == null)
                yield return null;
            var motor = PlayerMotor.Local;
            var spawn = _lobby.Spawn;
            motor.Teleport(spawn.position);
            motor.Look.SetAngles(spawn.eulerAngles.y, 0f);
            SetPlayerInLobby(true);
            _lobby.Portal.SetPlayer(motor, motor.Look, PlayerCameraRig.Local.Camera, inWorld: false);
            _lobby.Portal.SetOpen(false);
            _lobby.Portal.Crossed -= OnCrossed;
            _lobby.Portal.Crossed += OnCrossed;
        }

        /// <summary>Игрок в лобби: выживание на паузе, HUD скрыт, мир вокруг него не грузится (своя точка прогрузки выключена).</summary>
        private static void SetPlayerInLobby(bool inLobby)
        {
            var motor = PlayerMotor.Local;
            if (motor != null && motor.TryGetComponent(out WorldViewer viewer))
                viewer.enabled = !inLobby;
            if (PlayerVitals.Local != null)
                PlayerVitals.Local.Suspended = inLobby;
            GameHud.Visible = !inLobby;
            if (SaveSystem.Instance != null)
                SaveSystem.Instance.PlayerInWorld = !inLobby;
        }

        // ───────────────────────────── Выбор мира на ПК ─────────────────────────────

        /// <summary>Создать мир (World Generator). Null — пошла загрузка, иначе — почему нет.</summary>
        public string CreateWorld(string name, uint seed, WorldMeta settings)
        {
            if (State != FlowState.Lobby)
                return "Мир уже выбран.";
            if (string.IsNullOrWhiteSpace(name))
                return "Введите имя мира.";
            if (SaveSystem.Exists(name))
                return $"Мир «{name}» уже есть — загрузите его в Saves.";
            if (SaveSystem.Instance == null || !SaveSystem.Instance.OpenWorld(name, seed, settings, placePlayer: false, out _pending))
                return "Мир не создан (смотрите консоль).";
            BeginLoading(name);
            return null;
        }

        /// <summary>Загрузить сейв (Saves). Null — пошла загрузка.</summary>
        public string LoadWorld(string name)
        {
            if (State != FlowState.Lobby)
                return "Мир уже выбран.";
            if (!SaveSystem.Exists(name))
                return $"Мира «{name}» нет.";
            if (SaveSystem.Instance == null || !SaveSystem.Instance.OpenWorld(name, null, null, placePlayer: false, out _pending))
                return "Сейв не открыт (смотрите консоль).";
            BeginLoading(name);
            return null;
        }

        private void BeginLoading(string name)
        {
            WorldName = name;
            State = FlowState.Loading;
            Progress = 0f;
            var world = ChunkManager.Instance;
            // Выход портала: где игрок был (сейв) или у входа в мол (новый мир).
            Vector3 position;
            float yaw;
            if (_pending != null)
            {
                position = _pending.Position;
                yaw = _pending.Yaw;
            }
            else
            {
                position = MallPlanner.FindSpawn(world.Seed, world.AnomalyFrequency);
                yaw = 0f;
            }
            var exit = new GameObject("World Portal");
            exit.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            _worldPortal = exit;
            if (_lobby.PortalFrame != null)
            {
                var frame = Instantiate(_lobby.PortalFrame, exit.transform);
                frame.transform.localPosition = Vector3.zero;
                frame.transform.localRotation = Quaternion.identity;
                frame.SetActive(true);
                var surface = frame.transform.Find("Surface");
                if (surface != null)
                    _lobby.Portal.PlaceWorldEnd(exit.transform, surface.GetComponent<Renderer>());
            }
            var anchor = new GameObject("World Anchor");
            anchor.transform.position = position;
            anchor.AddComponent<WorldViewer>();
            _anchor = anchor.transform;
        }

        // ───────────────────────────── Портал ─────────────────────────────

        private void Update()
        {
            var world = ChunkManager.Instance;
            if (State == FlowState.Loading && world != null && _anchor != null)
            {
                Progress = world.ReadyFraction(_anchor.position, ReadyRadius);
                if (Progress >= 1f && world.IsCollisionReady(_anchor.position))
                {
                    State = FlowState.Ready;
                    _lobby.Portal.SetOpen(true);
                    DevConsole.Log($"Мир «{WorldName}» готов — портал открыт.");
                }
            }
            if (State == FlowState.InWorld && _lobby != null && _lobby.gameObject.activeSelf && !_portalClosing && _worldPortal != null)
            {
                _portalTimer += Time.deltaTime;
                if (_portalTimer >= PortalClose.Value)
                {
                    _portalClosing = true;
                    _lobby.Portal.Collapse();
                }
            }
            if (_portalClosing && _lobby != null && _lobby.Portal.Collapsed && _lobby.gameObject.activeSelf)
            {
                _lobby.gameObject.SetActive(false);
                if (_anchor != null)
                    Destroy(_anchor.gameObject);
                DevConsole.Log("Портал схлопнулся.");
            }
        }

        private void OnCrossed(bool toWorld)
        {
            if (toWorld)
            {
                var motor = PlayerMotor.Local;
                // Игрок из сейва: его вещи, показатели и навыки — в момент прохода (позиция — та, куда вывел портал).
                if (_pending != null && motor != null)
                {
                    _pending.Position = motor.State.Position;
                    _pending.Yaw = motor.Look.Yaw;
                    _pending.Pitch = motor.Look.Pitch;
                    _pending.Restore(motor.gameObject);
                    _pending = null;
                }
                SetPlayerInLobby(false);
                if (_anchor != null)
                    _anchor.gameObject.SetActive(false);
                State = FlowState.InWorld;
                _portalTimer = 0f;
            }
            else
            {
                SetPlayerInLobby(true);
                if (_anchor != null)
                    _anchor.gameObject.SetActive(true);
                State = FlowState.Ready;
            }
        }

        [ConsoleCommand("lobby", "Сохранить мир и вернуться в лобби.")]
        private static string LobbyCommand()
        {
            if (SaveSystem.Instance == null)
                return "Нет системы сохранений.";
            SaveSystem.Instance.ReturnToLobby();
            return null;
        }
    }
}
