using System.Collections.Generic;
using UnityEngine;
using VoidMall.Console;
using VoidMall.Core;
using VoidMall.Items;
using VoidMall.Skills;
using VoidMall.World;

namespace VoidMall.Crafting
{
    /// <summary>
    /// Система крафта в сцене мира (рядом с ItemSystem): рецепты, состояния станций по ID пропа, тик очередей
    /// станций. Плита и горелка готовят сами, пока есть топливо; верстак, швейный стол и химстол — только пока
    /// у кого-то открыто окно станции. Сломали станцию — готовое, очередь (ингредиенты) и выпадают на пол.
    /// </summary>
    [DefaultExecutionOrder(-160)]
    public sealed class CraftingSystem : MonoBehaviour
    {
        public static readonly CVar<bool> Instant = new("craft_instant", false, "Крафт без ожидания.", ConsoleFlags.Cheat);
        public static readonly CVar<bool> UnlockAll = new("craft_unlock_all", false, "Все рецепты известны (без чертежей и навыков).", ConsoleFlags.Cheat);

        [SerializeField] private RecipeSet _recipes;

        public static CraftingSystem Instance { get; private set; }

        public IReadOnlyList<RecipeDef> Recipes => _list;

        private readonly List<RecipeDef> _list = new();
        private readonly Dictionary<string, RecipeDef> _byId = new();
        private readonly Dictionary<ulong, StationState> _stations = new();
        private readonly List<CraftJob> _completed = new();
        private readonly List<ulong> _removed = new();
        private PropManager _props;

        private static readonly Perf.Probe UpdateProbe = Perf.Register("Crafting.Stations");

        private void Awake()
        {
            Instance = this;
            if (_recipes == null)
            {
                Debug.LogError("[Crafting] RecipeSet is not assigned.", this);
                return;
            }
            foreach (var recipe in _recipes.Recipes)
            {
                if (recipe == null || recipe.Result == null || _byId.ContainsKey(recipe.Id))
                    continue;
                _list.Add(recipe);
                _byId.Add(recipe.Id, recipe);
            }
            var ids = new List<string>();
            foreach (var recipe in _list)
                ids.Add(recipe.Id);
            DevConsole.RegisterCompleter("craft_learn", token => token == 0 ? ids : null);
        }

        private void Start()
        {
            _props = PropManager.Instance;
            if (_props != null)
                _props.PropRemoved += OnPropRemoved;
        }

        private void OnDestroy()
        {
            if (_props != null)
                _props.PropRemoved -= OnPropRemoved;
            if (Instance == this)
                Instance = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Instance = null;

        public bool TryGetRecipe(string id, out RecipeDef recipe) => _byId.TryGetValue(id, out recipe);

        // ───────────────────────────── Станции ─────────────────────────────

        /// <summary>Состояние станции для пропа (создаётся при первом обращении). null — проп не станция.</summary>
        public StationState GetStation(PropBody prop)
        {
            if (prop == null || prop.Def.Station == CraftStation.None || ItemSystem.Instance == null)
                return null;
            if (!_stations.TryGetValue(prop.Id, out var state))
            {
                state = new StationState(prop.Id, prop.Def.Station, prop.Def.DisplayName, prop.Def.StationSpeed,
                    prop.Def.StationAutonomous, ItemSystem.Instance.Items);
                _stations.Add(prop.Id, state);
            }
            state.Position = prop.Bounds.center;
            state.Heater = prop.Def.Heater;
            state.Heat = prop.Def.HeatOutput;
            state.HeatRadius = prop.Def.HeatRadius;
            return state;
        }

        /// <summary>Занять станцию (открыть окно). Null — можно, иначе — почему нет.</summary>
        public string TryOccupy(StationState station, GameObject player)
        {
            if (station.Occupant != null && station.Occupant != player)
                return "Станция занята";
            station.Occupant = player;
            return null;
        }

        public void Release(StationState station, GameObject player)
        {
            if (station != null && station.Occupant == player)
                station.Occupant = null;
        }

        private void Update()
        {
            using var perf = Perf.Measure(UpdateProbe);
            float dt = Time.deltaTime;
            foreach (var station in _stations.Values)
            {
                bool burned = TickQueue(station, dt);
                // Бочка-костёр горит и без готовки, пока есть топливо.
                if (!burned && station.Heater && station.Fuel > 0f)
                {
                    station.Fuel = Mathf.Max(0f, station.Fuel - dt);
                    burned = true;
                    station.RaiseChanged();
                }
                station.Burning = burned && station.NeedsFuel;
                if (station.Burning && station.Heat > 0f)
                    HeatSources.Report(station.PropId, station.Position, station.Heat, station.HeatRadius);
            }
        }

        /// <summary>Шаг очереди станции. True — топливо горело (шла готовка).</summary>
        private bool TickQueue(StationState station, float dt)
        {
            if (station.Queue.IsEmpty)
                return false;
            // Верстак, швейный, химстол — только с открытым окном; плита — сама.
            if (!station.Autonomous && station.Occupant == null)
                return false;
            float seconds = dt;
            if (station.NeedsFuel)
            {
                seconds = Mathf.Min(dt, station.Fuel);
                if (seconds <= 0f)
                    return false;
            }
            float before = station.Queue.Current.Elapsed;
            _completed.Clear();
            station.Queue.Tick(Instant.Value ? 1e6f : seconds * station.Speed, _completed, station.TryDeliver);
            // Топливо горит, только пока очередь реально двигалась.
            bool burned = station.NeedsFuel && (_completed.Count > 0 || station.Queue.IsEmpty || station.Queue.Current.Elapsed > before);
            if (burned)
                station.Fuel = Mathf.Max(0f, station.Fuel - seconds);
            foreach (var job in _completed)
                Complete(job);
            station.RaiseChanged();
            return burned;
        }

        /// <summary>Завершение позиции: действие (починка), очки навыков, «Экономный» — вернуть ингредиент.</summary>
        internal static void Complete(CraftJob job)
        {
            job.Completed?.Invoke();
            PlayerSkills.AwardLocal(job.PointsAspect, job.Points);
        }

        /// <summary>Подбросить топливо из инвентаря (1 штуку). Null — получилось, иначе — почему нет.</summary>
        public string AddFuel(StationState station, PlayerInventory inventory, ushort fuelItem)
        {
            if (!station.NeedsFuel)
                return "Этой станции топливо не нужно";
            var def = inventory.Items.Get(fuelItem);
            if (def.FuelSeconds <= 0f)
                return $"{def.DisplayName} не горит";
            if (station.Fuel + def.FuelSeconds > StationState.MaxFuel)
                return "Топлива и так достаточно";
            if (!inventory.TryConsume(fuelItem, 1, respectCreative: false))
                return $"Нет: {def.DisplayName}";
            station.Fuel += def.FuelSeconds;
            station.RaiseChanged();
            return null;
        }

        /// <summary>Станцию сломали или разобрали: готовое и ингредиенты очереди — на пол.</summary>
        // ───────────────────────────── Сейв (этап 9) ─────────────────────────────

        public StationsData Capture()
        {
            var data = new StationsData();
            var items = ItemSystem.Instance.Items;
            foreach (var (id, station) in _stations)
            {
                var saved = new SavedStation
                {
                    Prop = id.ToString(), Kind = station.Kind.ToString(), Name = station.Name, Speed = station.Speed,
                    Autonomous = station.Autonomous, Heater = station.Heater, Heat = station.Heat, HeatRadius = station.HeatRadius,
                    Fuel = station.Fuel, Position = station.Position,
                    Queue = CraftSave.Write(station.Queue, items),
                };
                ItemSnapshot.WriteGrid(station.Output, "output", -1, items, saved.Output);
                data.Stations.Add(saved);
            }
            return data;
        }

        /// <summary>Вернуть станции: очереди, выдачу и топливо (до того, как их откроют).</summary>
        public void Restore(StationsData data)
        {
            _stations.Clear();
            var items = ItemSystem.Instance.Items;
            foreach (var saved in data.Stations)
            {
                if (!ulong.TryParse(saved.Prop, out ulong id) || !System.Enum.TryParse(saved.Kind, out CraftStation kind))
                    continue;
                var station = new StationState(id, kind, saved.Name, saved.Speed, saved.Autonomous, items)
                {
                    Fuel = saved.Fuel, Position = saved.Position, Heater = saved.Heater, Heat = saved.Heat, HeatRadius = saved.HeatRadius,
                };
                var output = ItemSnapshot.ReadAll(saved.Output, items);
                foreach (var entry in saved.Output)
                    if (output.TryGetValue(entry.Id, out var stack))
                        ItemSnapshot.PlaceIn(station.Output, stack, entry);
                CraftSave.Read(saved.Queue, station.Queue, items, recipe => _byId.TryGetValue(recipe, out var def) ? def : null);
                _stations[id] = station;
            }
        }

        private void OnPropRemoved(PropBody prop, PropRemoveReason reason)
        {
            if (!_stations.TryGetValue(prop.Id, out var station))
                return;
            _stations.Remove(prop.Id);
            var items = WorldItems.Instance;
            if (items == null)
                return;
            Vector3 position = prop.Bounds.center + Vector3.up * 0.3f;
            foreach (var placement in station.Output.Items)
                items.Spawn(placement.Stack, position, Random.insideUnitSphere);
            foreach (var job in station.Queue.Jobs)
                foreach (var stack in job.Taken)
                    items.Spawn(stack, position, Random.insideUnitSphere);
        }

        public int StationCount => _stations.Count;
    }
}
