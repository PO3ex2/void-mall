using System;
using System.Collections.Generic;
using UnityEngine;
using VoidMall.Console;
using VoidMall.Core;
using VoidMall.Input;
using VoidMall.Items;
using VoidMall.Player;
using VoidMall.Skills;
using VoidMall.World;
using Random = UnityEngine.Random;

namespace VoidMall.Survival
{
    /// <summary>
    /// Выживание игрока (GDD §4.1, ARCHITECTURE §8.4–8.5): тикает VitalsModel, собирает окружение (температура,
    /// свет, аномалии), принимает урон (падения, обломки, летящие пропы, порезы, глитч), еду и лекарства.
    /// Отвечает за сон (ускорение времени, точка возрождения) и смерть: сумка с вещами по правилу мира,
    /// возрождение у кровати или рядом с местом смерти, «Травма после смерти». Это и пул стамины мотора.
    /// </summary>
    [RequireComponent(typeof(PlayerMotor))]
    [DefaultExecutionOrder(-40)]
    public sealed class PlayerVitals : MonoBehaviour, IStaminaPool, IMovementModifierSource
    {
        public static readonly CVar<bool> GodMode = new("god", false, "Бессмертие: здоровье не падает ниже 1.", ConsoleFlags.Cheat);
        public static readonly CVar<int> DeathDrop = new("sv_death_drop", 2, "Что теряется при смерти: 0 — ничего, 1 — хотбар, 2 — рюкзак, 3 — всё.", ConsoleFlags.Cheat);
        public static readonly CVar<float> SleepSpeed = new("sv_sleep_speed", 30f, "Во сколько раз быстрее идёт время во сне (одиночная игра).", ConsoleFlags.Cheat);

        public const float RespawnDelay = 3f;
        private const float EnvironmentInterval = 0.5f;
        private const float GlitchInterval = 2f;
        private const float RespawnSearchRadius = 25f;

        public static PlayerVitals Local { get; private set; }

        public VitalsModel Model { get; } = new(System.Environment.TickCount);

        /// <summary>Что вокруг (обновляется дважды в секунду).</summary>
        public VitalsEnvironment Environment => _env;

        public bool Sleeping { get; private set; }

        /// <summary>Спит на полу (выспаться можно только до 60%).</summary>
        public bool SleepingOnFloor => Sleeping && _floorSleep;
        public bool IsDead => Model.IsDead;

        /// <summary>Температура воздуха вокруг (последний замер окружения), °C.</summary>
        public float AirTemperature => _env.AirTemperature;

        /// <summary>Когда погиб (Time.unscaledTime) — экран смерти ждёт RespawnDelay.</summary>
        public float DiedAt { get; private set; }

        public bool CanRespawn => IsDead && Time.unscaledTime - DiedAt >= RespawnDelay;

        /// <summary>Сумка с вещами после смерти ещё лежит в мире.</summary>
        public bool HasLostBag => _lostBag != null;
        public Vector3 LostBagPosition { get; private set; }

        public bool HasSpawnPoint => _spawnPropId != 0;

        /// <summary>Последний полученный урон (для красной вспышки HUD): время и величина.</summary>
        public float LastDamageTime { get; private set; } = -100f;
        public float LastDamage { get; private set; }

        /// <summary>Когда последний раз сработал глитч (для эффекта на экране).</summary>
        public float LastGlitchTime { get; private set; } = -100f;

        /// <summary>Воздействие аномалий на игрока (взгляд, снос, лужа, оазис).</summary>
        public AnomalyExposure Exposure => _exposure;

        public event Action Died;
        public event Action Respawned;

        private PlayerMotor _motor;
        private PlayerInventory _inventory;
        private InputService _input;
        private readonly SurvivalEnvironment _probe = new();
        private readonly AnomalyExposure _exposure = new();
        private VitalsEnvironment _env;
        private float _envTimer, _glitchTimer, _safeTimer;
        private double _lastHours = double.NaN;
        private float _lastHealth;
        private bool _deathHandled, _inputBlocked;
        private DeathCause _cause = DeathCause.Damage;
        private Vector3 _lastSafe;
        private bool _hasSafe;
        private ItemStack _lostBag;
        private ulong _spawnPropId;
        private float _sleepQuality = 1f, _sleepCap = 100f, _restAtSleep;
        private bool _floorSleep;
        private Vector3 _spawnPosition;
        private readonly List<ItemStack> _drop = new();

        private static readonly Perf.Probe UpdateProbe = Perf.Register("Survival.Vitals");

        private void Awake()
        {
            _motor = GetComponent<PlayerMotor>();
            _inventory = GetComponent<PlayerInventory>();
            _lastHealth = Model.Health;
        }

        private void Start()
        {
            // Выживание заменяет простой пул стамины префаба и даёт свои модификаторы движения.
            _motor.SetStaminaPool(this);
            _motor.AddModifierSource(this);
            if (GetComponent<VitalsEffects>() == null)
                gameObject.AddComponent<VitalsEffects>();
            if (GetComponent<BreathVapor>() == null)
                gameObject.AddComponent<BreathVapor>();
        }

        private void OnEnable()
        {
            Local = this;
            EventBus<PlayerLandedEvent>.Subscribe(OnLanded);
            EventBus<DebrisImpactEvent>.Subscribe(OnDebris);
            EventBus<PlayerImpactEvent>.Subscribe(OnImpact);
            EventBus<ItemConsumedEvent>.Subscribe(OnConsumed);
            PlayerInventory.UseBlockers.Add(WhyCannotUse);
        }

        private void OnDisable()
        {
            EventBus<PlayerLandedEvent>.Unsubscribe(OnLanded);
            EventBus<DebrisImpactEvent>.Unsubscribe(OnDebris);
            EventBus<PlayerImpactEvent>.Unsubscribe(OnImpact);
            EventBus<ItemConsumedEvent>.Unsubscribe(OnConsumed);
            PlayerInventory.UseBlockers.Remove(WhyCannotUse);
            SetInputBlocked(false);
            if (WorldClock.Instance != null)
                WorldClock.Instance.TimeScale = 1f;
            if (Local == this)
                Local = null;
        }

        private void OnDestroy() => _probe.Dispose();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Local = null;

        // ───────────────────────────── Тик ─────────────────────────────

        private void Update()
        {
            using var perf = Perf.Measure(UpdateProbe);
            var world = ChunkManager.Instance;
            var clock = WorldClock.Instance;
            float realDt = Time.deltaTime;

            double hours = clock != null ? clock.TotalHours : 0.0;
            float gameHours = double.IsNaN(_lastHours) ? 0f : (float)Math.Max(0.0, hours - _lastHours);
            // Скачок времени командой time (больше получаса за кадр) — не прожитые часы: нужды не трогаем.
            if (gameHours > 0.5f)
                gameHours = 0f;
            _lastHours = hours;

            Model.God = GodMode.Value;
            Model.Perks = PerksFromSkills();
            _envTimer -= realDt;
            if (_envTimer <= 0f)
            {
                _envTimer = EnvironmentInterval;
                SampleEnvironment(world, clock);
            }
            _env.Sleeping = Sleeping;
            _env.SleepQuality = _sleepQuality;
            _env.SleepRestCap = _sleepCap;
            _exposure.Tick(world, _motor, _probe, Model, !IsDead && !Sleeping, realDt);
            _env.LookingAtAnomaly = _exposure.Staring;
            _env.InOasis = _exposure.InOasis;
            _env.PuddleHeal = _exposure.PuddleHeal;
            ref readonly var motorState = ref _motor.State;
            _env.InWater = motorState.InWater;
            _env.HeadUnderwater = motorState.HeadUnderwater;

            if (!IsDead)
            {
                RememberSafePosition();
                TickGlitch(world, realDt);
                Model.Tick(realDt, gameHours, _env);
                // Очки выживания: сон и терпение холода или жары (за игровые часы).
                if (Sleeping)
                    PlayerSkills.AwardLocal(Aspect.Survival, 0.5f * gameHours);
                if (Model.BodyTemperature < 36f || Model.BodyTemperature > 37.4f)
                    PlayerSkills.AwardLocal(Aspect.Survival, 0.5f * gameHours);
            }

            if (Model.Health < _lastHealth - 0.01f)
            {
                LastDamageTime = Time.unscaledTime;
                LastDamage = _lastHealth - Model.Health;
                // Во сне боль будит (кровотечение, удар).
                if (Sleeping && LastDamage > 0.5f)
                    StopSleep("Вас разбудила боль");
            }
            _lastHealth = Model.Health;

            if (Sleeping && Model.Rest >= _sleepCap - 0.01f)
                StopSleep(_floorSleep ? "Больше на полу не поспать: всё затекло" : "Вы выспались");
            if (IsDead && !_deathHandled)
                HandleDeath();
            if (IsDead)
                HoldInVoid();
            UpdateLostBag();
        }

        private void SampleEnvironment(ChunkManager world, WorldClock clock)
        {
            Vector3 position = transform.position;
            _env.IsNight = clock != null && clock.IsNight;
            _env.AirTemperature = _probe.AirTemperature(world, position, SurvivalEnvironment.NightFactor(clock != null ? clock.Hour : 12f));
            _env.InAnomaly = _probe.InAnomaly(world, position);
            _env.Light = Services.TryGet(out ILightProbe light) ? light.SampleLight(position + Vector3.up * 1.6f) : 1f;
            _env.HasOwnLight = false; // фонарик уже учтён в освещённости (LightingDirector.DynamicLights)
            _env.Warmth = ClothingWarmth();
            _env.HeatNearby = HeatSources.HeatAt(position + Vector3.up);
        }

        private float ClothingWarmth()
        {
            if (_inventory == null || _inventory.Items == null)
                return 0f;
            float warmth = 0f;
            for (var slot = EquipSlot.Head; slot <= EquipSlot.Feet; slot++)
            for (var layer = EquipLayer.Armor; layer <= EquipLayer.Clothing; layer++)
            {
                var stack = _inventory.Equipment.Get(slot, layer);
                if (stack != null)
                    warmth += _inventory.Items.Get(stack.Def).Warmth;
            }
            return warmth;
        }

        private void TickGlitch(ChunkManager world, float realDt)
        {
            // Аномалия «глитч»: раз в 2 секунды здоровье — случайное значение, выпал ноль — смерть.
            _glitchTimer -= realDt;
            if (_glitchTimer > 0f)
                return;
            _glitchTimer = GlitchInterval;
            if (!_probe.InMarker(world, transform.position, AnomalyMarkerKind.Glitch))
                return;
            LastGlitchTime = Time.unscaledTime;
            float value = Mathf.Round(Random.Range(0f, Model.MaxHealth));
            if (value <= 0f)
                _cause = DeathCause.Glitch;
            Model.SetHealth(value, "Глитч");
            // Резкий скачок вниз — не «урон» для вспышки: глитч рисует свой эффект.
            _lastHealth = Model.Health;
        }

        private void RememberSafePosition()
        {
            _safeTimer -= Time.deltaTime;
            if (_safeTimer > 0f || _motor == null)
                return;
            _safeTimer = 0.5f;
            ref readonly var state = ref _motor.State;
            if (state.IsGrounded && state.Mode != MoveMode.Noclip && state.Position.y > 0f)
            {
                _lastSafe = state.Position + Vector3.up * 0.05f;
                _hasSafe = true;
            }
        }

        // ───────────────────────────── События урона и еды ─────────────────────────────

        private void OnLanded(in PlayerLandedEvent evt)
        {
            if (evt.Player != gameObject || IsDead)
                return;
            float before = Model.Health;
            Model.ApplyFall(evt.ImpactSpeed);
            if (Model.IsDead && before > 0f)
                _cause = DeathCause.Fall;
        }

        private void OnDebris(in DebrisImpactEvent evt)
        {
            if (IsDead)
                return;
            var bounds = evt.Bounds;
            bounds.Expand(0.4f);
            Vector3 center = transform.position + Vector3.up;
            if (!bounds.Contains(center) && !bounds.Contains(transform.position + Vector3.up * 0.2f))
                return;
            // Тяжёлый обломок давит насмерть, лёгкий — калечит.
            float damage = evt.Mass > 600f && evt.Speed > 3f ? 1000f : Mathf.Min(90f, evt.Mass * evt.Speed * 0.02f);
            Model.ApplyImpact(damage, "Раздавило обломком");
        }

        private void OnImpact(in PlayerImpactEvent evt)
        {
            if (evt.Player != gameObject || IsDead)
                return;
            Model.ApplyImpact(evt.Damage, evt.Reason);
        }

        private void OnConsumed(in ItemConsumedEvent evt)
        {
            if (evt.Player != gameObject || IsDead)
                return;
            Model.Consume(evt.Item, evt.Spoiled);
            if (evt.Item.Category == ItemCategory.Medicine)
                PlayerSkills.AwardLocal(Aspect.Survival, 1f);
            else if (evt.Item.Category is ItemCategory.Food or ItemCategory.Drink)
                PlayerSkills.AwardLocal(Aspect.Survival, 0.2f);
        }

        /// <summary>Бонусы навыков к выживанию.</summary>
        private static VitalsPerks PerksFromSkills()
        {
            var perks = VitalsPerks.None;
            perks.StaminaBonus = 0.1f * PlayerSkills.LevelOf(SkillId.Endurance);
            perks.StaminaCostMul = 1f - 0.15f * PlayerSkills.LevelOf(SkillId.Athlete);
            perks.FallDamageMul = 1f - 0.2f * PlayerSkills.LevelOf(SkillId.StrongBones);
            int surgery = PlayerSkills.LevelOf(SkillId.FieldSurgery);
            perks.HealBonus = 5f * surgery;
            perks.SplintHours = 48f - 12f * surgery;
            perks.CookMul = 1f + 0.15f * PlayerSkills.LevelOf(SkillId.Cook);
            perks.ComfortWiden = 2f * PlayerSkills.LevelOf(SkillId.Hardening);
            perks.SanityDrainMul = 1f - 0.15f * PlayerSkills.LevelOf(SkillId.SteadyNerves);
            perks.BhopLevel = PlayerSkills.LevelOf(SkillId.Athlete);
            return perks;
        }

        private string WhyCannotUse(PlayerInventory inventory, ItemDef item, ItemStack stack)
        {
            if (inventory.gameObject != gameObject)
                return null;
            if (IsDead)
                return "Вы мертвы";
            if (Sleeping)
                return "Во сне нельзя";
            if (item.Category == ItemCategory.Medicine)
                return Model.WhyUseless(item);
            return null;
        }

        /// <summary>Порез голыми руками (стекло, острый металл) — вызывает инструмент.</summary>
        public void Cut(float damage)
        {
            if (!IsDead)
                Model.ApplyCut(damage);
        }

        /// <summary>Мгновенная смерть (пустота, команда kill).</summary>
        public void Kill(DeathCause cause, string reason)
        {
            if (IsDead)
                return;
            _cause = cause;
            if (GodMode.Value && cause != DeathCause.Void)
                return;
            Model.Kill(reason);
        }

        // ───────────────────────────── Сон ─────────────────────────────

        /// <summary>Лечь спать на кровать, диван или спальник. Null — уснул, иначе — почему нет.</summary>
        public string TrySleep(PropBody bed)
        {
            if (IsDead)
                return "Вы мертвы";
            if (Sleeping)
                return null;
            SetSpawnPoint(bed);
            if (Model.Rest > 90f)
                return "Спать не хочется (точка возрождения сохранена)";
            BeginSleep(bed != null ? bed.Def.SleepQuality : 1f, 100f, floor: false);
            DevConsole.Log("Вы уснули. Точка возрождения сохранена.");
            return null;
        }

        /// <summary>
        /// Поспать прямо на полу (кнопка во вкладке «Состояние»): бодрость растёт вдвое медленнее и только до 60%,
        /// точка возрождения не сохраняется, после пробуждения — «Не выспался».
        /// </summary>
        public string TrySleepOnFloor()
        {
            if (IsDead)
                return "Вы мертвы";
            if (Sleeping)
                return null;
            if (Model.Rest >= VitalsModel.FloorRestCap - 1f)
                return $"На полу больше не выспаться (бодрость уже {Model.Rest:0}%)";
            if (_motor != null && !_motor.State.IsGrounded)
                return "Нужно стоять на полу";
            BeginSleep(VitalsModel.FloorSleepQuality, VitalsModel.FloorRestCap, floor: true);
            DevConsole.Log("Вы уснули на полу. Выспаться так не выйдет.");
            return null;
        }

        private void BeginSleep(float quality, float cap, bool floor)
        {
            _sleepQuality = Mathf.Clamp(quality, 0.05f, 1f);
            _sleepCap = cap;
            _floorSleep = floor;
            _restAtSleep = Model.Rest;
            Sleeping = true;
            SetInputBlocked(true);
            if (WorldClock.Instance != null)
                WorldClock.Instance.TimeScale = Mathf.Max(1f, SleepSpeed.Value);
        }

        public void StopSleep(string reason = null)
        {
            if (!Sleeping)
                return;
            Sleeping = false;
            SetInputBlocked(IsDead);
            if (WorldClock.Instance != null)
                WorldClock.Instance.TimeScale = 1f;
            // Сон на полу оставляет «Не выспался»; полноценный сон до конца его снимает.
            if (_floorSleep && Model.Rest > _restAtSleep + 1f)
                Model.Add(StatusKind.Unrested, hours: VitalsModel.UnrestedHours);
            else if (!_floorSleep && Model.Rest >= 99f)
                Model.Remove(StatusKind.Unrested);
            if (!string.IsNullOrEmpty(reason))
                DevConsole.Log(reason);
        }

        private void SetSpawnPoint(PropBody bed)
        {
            _spawnPropId = bed != null ? bed.Id : 0;
            _spawnPosition = transform.position + Vector3.up * 0.05f;
        }

        // ───────────────────────────── Смерть и возрождение ─────────────────────────────

        private void HandleDeath()
        {
            _deathHandled = true;
            DiedAt = Time.unscaledTime;
            StopSleep();
            SetInputBlocked(true);
            DropOnDeath();
            EventBus<PlayerDiedEvent>.Publish(new PlayerDiedEvent { Player = gameObject, Cause = _cause, Position = transform.position });
            DevConsole.Error($"Вы погибли: {Model.DeathReason}.");
            Died?.Invoke();
        }

        /// <summary>Вещи по правилу мира — в сумку на месте смерти (в пустоте — на последнем безопасном месте).</summary>
        private void DropOnDeath()
        {
            var system = ItemSystem.Instance;
            if (_inventory == null || system == null || WorldItems.Instance == null)
                return;
            _drop.Clear();
            int rule = Mathf.Clamp(DeathDrop.Value, 0, 3);
            if (rule == 1)
            {
                for (int i = 0; i < Hotbar.Size; i++)
                    if (_inventory.Hotbar[i] != null && !_drop.Contains(_inventory.Hotbar[i]))
                        _drop.Add(_inventory.Hotbar[i]);
            }
            else if (rule == 2)
            {
                if (_inventory.Equipment.Backpack != null)
                    _drop.Add(_inventory.Equipment.Backpack);
            }
            else if (rule == 3)
            {
                foreach (var placement in _inventory.Pockets.Items)
                    _drop.Add(placement.Stack);
                for (var slot = EquipSlot.Head; slot <= EquipSlot.Backpack; slot++)
                for (var layer = EquipLayer.Armor; layer <= EquipLayer.Clothing; layer++)
                {
                    var stack = _inventory.Equipment.Get(slot, layer);
                    if (stack != null && !_drop.Contains(stack))
                        _drop.Add(stack);
                }
            }
            if (_drop.Count == 0 || !system.TryResolve("item.lost_bag", out ushort bagDef))
                return;

            var bag = system.Items.Create(bagDef, 1);
            Vector3 position = transform.position.y > PlayerFallLimit ? transform.position + Vector3.up * 0.5f : _lastSafe + Vector3.up * 0.5f;
            foreach (var stack in _drop)
            {
                if (!_inventory.Remove(stack))
                    continue;
                if (bag.Contents == null || bag.Contents.TryAdd(stack) > 0)
                    WorldItems.Instance.Spawn(stack, position, Vector3.zero);
            }
            WorldItems.Instance.Spawn(bag, position, Vector3.zero);
            _lostBag = bag;
            LostBagPosition = position;
        }

        private const float PlayerFallLimit = -30f;

        /// <summary>Погибший в пустоте не падает бесконечно: висит под миром до возрождения.</summary>
        private void HoldInVoid()
        {
            Vector3 position = transform.position;
            if (position.y < PlayerFallLimit * 2f)
                _motor.Teleport(new Vector3(position.x, PlayerFallLimit * 2f, position.z));
        }

        private void UpdateLostBag()
        {
            if (_lostBag == null)
                return;
            if (WorldItems.Instance != null && WorldItems.Instance.TryGetPosition(_lostBag, out var position))
                LostBagPosition = position;
            else
                _lostBag = null; // подобрали
        }

        /// <summary>Возродиться (после RespawnDelay): у кровати или в безопасном месте рядом с местом смерти.</summary>
        public bool Respawn()
        {
            if (!CanRespawn)
                return false;
            Vector3 position = ChooseRespawn(out string where);
            _motor.Teleport(position);
            Model.Respawn();
            _lastHealth = Model.Health;
            _deathHandled = false;
            _cause = DeathCause.Damage;
            SetInputBlocked(false);
            DevConsole.Log($"Вы возродились {where}. Травма после смерти: −{VitalsModel.TraumaPerStack * Model.TraumaStacks * 100f:0}% здоровья и стамины.");
            Respawned?.Invoke();
            return true;
        }

        private Vector3 ChooseRespawn(out string where)
        {
            var props = PropManager.Instance;
            if (_spawnPropId != 0 && props != null && !props.IsRemoved(_spawnPropId))
            {
                where = "у кровати";
                return _spawnPosition;
            }
            if (_spawnPropId != 0)
            {
                _spawnPropId = 0;
                DevConsole.Warn("Ваша кровать уничтожена — точка возрождения потеряна.");
            }
            where = "рядом с местом смерти";
            Vector3 origin = transform.position.y > PlayerFallLimit || !_hasSafe ? transform.position : _lastSafe;
            for (int attempt = 0; attempt < 24; attempt++)
            {
                Vector2 offset = Random.insideUnitCircle.normalized * Random.Range(5f, RespawnSearchRadius);
                if (TryFindFloor(origin + new Vector3(offset.x, 0f, offset.y), out var floor))
                    return floor;
            }
            return _hasSafe ? _lastSafe : origin + Vector3.up;
        }

        private bool TryFindFloor(Vector3 around, out Vector3 floor)
        {
            floor = default;
            const int mask = (1 << GameLayers.World) | (1 << GameLayers.Default);
            if (!Physics.Raycast(around + Vector3.up * 1.5f, Vector3.down, out var hit, 5f, mask, QueryTriggerInteraction.Ignore) || hit.normal.y < 0.7f)
                return false;
            Vector3 feet = hit.point + Vector3.up * 0.05f;
            if (Physics.CheckCapsule(feet + Vector3.up * 0.4f, feet + Vector3.up * 1.5f, 0.35f, GameLayers.PlayerCollisionMask, QueryTriggerInteraction.Ignore))
                return false;
            if (_probe.InAnomaly(ChunkManager.Instance, feet) || _probe.InMarker(ChunkManager.Instance, feet, AnomalyMarkerKind.Glitch))
                return false;
            floor = feet;
            return true;
        }

        private void SetInputBlocked(bool blocked)
        {
            if (_inputBlocked == blocked)
                return;
            if (_input == null && !Services.TryGet(out _input))
                return;
            _inputBlocked = blocked;
            if (blocked)
                _input.Push(InputContext.Menu);
            else
                _input.Pop(InputContext.Menu);
        }

        // ───────────────────────────── IStaminaPool, модификаторы ─────────────────────────────

        public float Current => Model.Stamina;
        public float Max => Model.MaxStamina;
        public bool IsExhausted => Model.StaminaExhausted;
        public bool TryConsume(float amount) => Model.TryConsumeStamina(amount);
        public void Drain(float amount) => Model.DrainStamina(amount);

        public bool Infinite
        {
            get => Model.InfiniteStamina;
            set => Model.InfiniteStamina = value;
        }

        public void Refill() => Model.SetStamina(Model.MaxStamina);

        public void ApplyModifiers(ref MovementModifiers modifiers) => Model.ApplyModifiers(ref modifiers);
    }
}
