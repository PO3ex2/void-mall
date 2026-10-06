using System;
using System.Collections.Generic;
using UnityEngine;
using VoidMall.Core;

namespace VoidMall.Player
{
    /// <summary>
    /// Кинематический контроллер игрока с физикой в духе Source.
    /// Работает в фиксированном тике (60 Гц): ввод → модификаторы → движение → события.
    /// Simulate() зависит только от состояния, ввода и физического мира — это задел под предсказание в коопе.
    /// </summary>
    [DefaultExecutionOrder(-10)]
    [RequireComponent(typeof(CapsuleCollider), typeof(Rigidbody))]
    public sealed class PlayerMotor : MonoBehaviour
    {
        [SerializeField] private MovementSettings _settings;
        [SerializeField] private PlayerLook _look;
        [SerializeField] private LayerMask _collisionMask = GameLayers.PlayerCollisionMask;
        [Tooltip("Локальный игрок этого клиента (для консоли, HUD, камеры).")]
        [SerializeField] private bool _isLocalPlayer = true;

        public static PlayerMotor Local { get; private set; }

        private readonly List<IMovementModifierSource> _modifierSources = new();
        private readonly HashSet<Rigidbody> _pushedBodies = new();

        private KinematicMover _mover;
        private CapsuleCollider _capsule;
        private IPlayerInputSource _input;
        private IStaminaPool _stamina;

        private MotorState _state;
        private Vector3 _previousPosition;
        private MovementModifiers _modifiers = MovementModifiers.Default;
        private float _jumpBuffer;
        private float _timeSinceGrounded;
        private bool _jumpedThisTick;
        private float _appliedCapsuleHeight = -1f;
        private float _stepOffset;
        private float _previousStepOffset;

        /// <summary>Приземлились: вертикальная скорость удара, м/с.</summary>
        public event Action<float> Landed;

        public event Action Jumped;

        public MovementSettings Settings => _settings;
        public ref readonly MotorState State => ref _state;
        public MovementModifiers Modifiers => _modifiers;
        public IStaminaPool Stamina => _stamina;
        public PlayerLook Look => _look;
        public KinematicMover Mover => _mover;
        public float LastLandingSpeed { get; private set; }

        /// <summary>
        /// Внешний снос по горизонтали, м/с (пульсирующее искажение): двигает игрока поверх его скорости, не копится
        /// и не гасится трением — пропал источник, пропал и снос. Выставляет модуль аномалий каждый кадр.
        /// </summary>
        public Vector3 ExternalDrift { get; set; }

        /// <summary>Окно бхопа: прыжок не позже стольких секунд после приземления — без трения.</summary>
        public const float BhopWindow = 0.1f;

        private float _sinceLanded = float.MaxValue;

        public float CurrentHeight => _state.IsCrouched ? _settings.CrouchHeight : _settings.StandHeight;
        public float TargetEyeHeight => _state.IsCrouched ? _settings.CrouchEyeHeight : _settings.StandEyeHeight;

        /// <summary>Позиция ступней, интерполированная между тиками — для камеры и визуала.</summary>
        public Vector3 InterpolatedPosition => Vector3.LerpUnclamped(_previousPosition, _state.Position, InterpolationAlpha);

        /// <summary>
        /// Вертикальная поправка взгляда, гасящая резкие смещения ступней (ступенька, присед в воздухе).
        /// Интерполируется так же, как позиция, поэтому в момент шага камера не проседает и не дёргается.
        /// </summary>
        public float InterpolatedStepOffset => Mathf.LerpUnclamped(_previousStepOffset, _stepOffset, InterpolationAlpha);

        private static float InterpolationAlpha => Mathf.Clamp01((Time.time - Time.fixedTime) / Time.fixedDeltaTime);

        private void Awake()
        {
            if (_settings == null)
            {
                Debug.LogError($"{nameof(PlayerMotor)}: MovementSettings is not assigned.", this);
                enabled = false;
                return;
            }

            _capsule = GetComponent<CapsuleCollider>();
            var body = GetComponent<Rigidbody>();
            body.isKinematic = true;
            body.interpolation = RigidbodyInterpolation.None;

            if (_look == null)
                _look = GetComponent<PlayerLook>();
            _input = GetComponent<IPlayerInputSource>();
            _stamina = GetComponent<IStaminaPool>();
            GetComponents(_modifierSources);

            _mover = new KinematicMover(_settings, _collisionMask);

            transform.rotation = Quaternion.identity;
            _state.Position = transform.position;
            _state.GroundNormal = Vector3.up;
            _previousPosition = _state.Position;
            ApplyCapsule();
        }

        private void OnEnable()
        {
            if (_isLocalPlayer)
                Local = this;
        }

        private void OnDisable()
        {
            if (Local == this)
                Local = null;
        }

        private static readonly Perf.Probe FixedProbe = Perf.Register("Player.Motor");

        private void FixedUpdate()
        {
            using var perf = Perf.Measure(FixedProbe);
            var input = _input != null ? _input.ConsumeFrame() : default;

            if (_state.Mode != MoveMode.Noclip && Services.TryGet(out IWorldCollisionQuery world))
            {
                // Мир под ногами ещё не прогружен — ждём на месте.
                if (!world.IsCollisionReady(_state.Position))
                {
                    _previousPosition = _state.Position;
                    _state.Velocity = Vector3.zero;
                    return;
                }
                EscapeIfEmbedded(world);
            }

            Simulate(in input, Time.fixedDeltaTime);
        }

        /// <summary>
        /// Замурованы в массиве (нарастили вокруг себя, вышли из noclip внутри стены): физика оболочки мира
        /// изнутри не выталкивает, поэтому переносим игрока в ближайшее свободное место по данным вокселей.
        /// </summary>
        private void EscapeIfEmbedded(IWorldCollisionQuery world)
        {
            if (!world.TryGetEmbeddedEscape(_state.Position, CurrentHeight, _settings.Radius, out var free))
                return;
            float rise = free.y - _state.Position.y;
            _state.Position = free;
            _state.Velocity = Vector3.zero;
            _state.IsGrounded = false;
            transform.position = free;
            ShiftFeetVisual(rise);
        }

        // ───────────────────────────── Публичное API ─────────────────────────────

        public void SetInputSource(IPlayerInputSource source) => _input = source;

        /// <summary>Заменить пул стамины (модуль выживания подменяет простой пул префаба).</summary>
        public void SetStaminaPool(IStaminaPool pool) => _stamina = pool;

        public void AddModifierSource(IMovementModifierSource source)
        {
            if (!_modifierSources.Contains(source))
                _modifierSources.Add(source);
        }

        public void RemoveModifierSource(IMovementModifierSource source) => _modifierSources.Remove(source);

        public void SetMode(MoveMode mode)
        {
            if (_state.Mode == mode)
                return;
            _state.Mode = mode;
            _state.IsGrounded = false;
            _state.IsSprinting = false;
            if (mode != MoveMode.Walk && _state.IsCrouched)
            {
                _state.IsCrouched = false;
                ApplyCapsule();
            }
            if (mode == MoveMode.Walk)
                _mover.Depenetrate(ref _state.Position, CurrentHeight, _capsule);
        }

        public void Teleport(Vector3 feetPosition, bool resetVelocity = true)
        {
            _state.Position = feetPosition;
            _previousPosition = feetPosition;
            _stepOffset = 0f;
            _previousStepOffset = 0f;
            if (resetVelocity)
                _state.Velocity = Vector3.zero;
            _state.IsGrounded = false;
            transform.position = feetPosition;
        }

        public void SetVelocity(Vector3 velocity) => _state.Velocity = velocity;

        // ───────────────────────────── Симуляция ─────────────────────────────

        public void Simulate(in PlayerInputFrame input, float dt)
        {
            _sinceLanded += dt;
            _previousPosition = _state.Position;
            _previousStepOffset = _stepOffset;
            _stepOffset *= Mathf.Exp(-_settings.StepSmoothing * dt);
            _modifiers = AggregateModifiers();
            _mover.Hits.Clear();
            _jumpedThisTick = false;
            _state.Yaw = input.Yaw;
            _state.Pitch = input.Pitch;

            UpdateWater();
            if (_state.Mode != MoveMode.Walk)
                SimulateFly(in input, dt);
            else if (_state.IsSwimming)
                SimulateSwim(in input, dt);
            else
                SimulateWalk(in input, dt);

            PushRigidbodies(dt);
            transform.position = _state.Position;
        }

        private MovementModifiers AggregateModifiers()
        {
            var m = MovementModifiers.Default;
            for (int i = 0; i < _modifierSources.Count; i++)
            {
                if (_modifierSources[i] is UnityEngine.Object unityObject && unityObject == null)
                    continue;
                _modifierSources[i].ApplyModifiers(ref m);
            }
            if (_stamina != null && _stamina.IsExhausted)
                m.CanSprint = false;
            return m;
        }

        private void SimulateWalk(in PlayerInputFrame input, float dt)
        {
            var s = _settings;
            var m = _modifiers;

            _mover.Depenetrate(ref _state.Position, CurrentHeight, _capsule);
            CategorizeGround();

            // Присед.
            UpdateCrouch(input.IsHeld(PlayerButtons.Crouch) || !m.CanStand);

            // Желаемое направление и скорость.
            Vector2 move = Vector2.ClampMagnitude(input.Move, 1f);
            Vector3 wish = Quaternion.Euler(0f, input.Yaw, 0f) * new Vector3(move.x, 0f, move.y);
            float wishAmount = wish.magnitude;
            Vector3 wishDir = wishAmount > 1e-4f ? wish / wishAmount : Vector3.zero;

            bool wantsSprint = input.IsHeld(PlayerButtons.Sprint) && move.y > 0.1f;
            _state.IsSprinting = wantsSprint && m.CanSprint && !_state.IsCrouched && _state.IsGrounded && wishAmount > 0.1f;

            float baseSpeed =
                _state.IsCrouched ? s.CrouchSpeed :
                input.IsHeld(PlayerButtons.Walk) ? s.WalkSpeed :
                _state.IsSprinting ? s.SprintSpeed :
                s.RunSpeed;
            float wishSpeed = baseSpeed * m.SpeedMul * Mathf.Min(wishAmount, 1f);
            if (_state.InWater)
                wishSpeed *= s.WadeSpeedMul; // вброд

            // Трение до прыжка: так приземление всегда стоит скорости и банихоп ограничен. С бхопом (навык) прыжок в
            // момент приземления (окно BhopWindow, нажатие чуть раньше — из буфера) трения не получает: скорость сохраняется.
            bool jumpQueued = input.WasPressed(PlayerButtons.Jump) || _jumpBuffer > 0f;
            bool bhop = m.Bhop && _state.IsGrounded && jumpQueued && _sinceLanded <= BhopWindow;
            if (_state.IsGrounded && !bhop)
            {
                _state.Velocity.y = 0f;
                SourceMovement.Friction(ref _state.Velocity, dt, s.Friction, s.StopSpeed);
            }

            // Прыжок с буфером нажатия и «койот-таймом».
            if (input.WasPressed(PlayerButtons.Jump))
                _jumpBuffer = s.JumpBufferTime;
            else
                _jumpBuffer -= dt;
            _timeSinceGrounded = _state.IsGrounded ? 0f : _timeSinceGrounded + dt;

            if (_jumpBuffer > 0f && _timeSinceGrounded <= s.CoyoteTime && m.CanJump && TryPayJumpStamina())
                DoJump();

            if (_state.IsGrounded)
            {
                SourceMovement.Accelerate(ref _state.Velocity, wishDir, wishSpeed, s.GroundAccelerate * m.AccelMul, dt);
                if (_state.IsSprinting)
                    _stamina?.Drain(s.SprintStaminaPerSecond * m.StaminaCostMul * dt);
                GroundMove(dt);
            }
            else
            {
                _state.Velocity.y -= s.Gravity * dt * 0.5f;
                SourceMovement.AirAccelerate(ref _state.Velocity, wishDir, wishSpeed, s.AirAccelerate * m.AccelMul, s.AirWishSpeedCap, dt);
                AirMove(dt);
                _state.Velocity.y = Mathf.Max(_state.Velocity.y - s.Gravity * dt * 0.5f, -s.MaxFallSpeed);
            }

            // Итоговое состояние земли и события.
            bool wasGrounded = _state.IsGrounded;
            Vector3 groundVelocityBefore = _state.GroundVelocity;
            float verticalSpeedBefore = _state.Velocity.y;
            CategorizeGround();

            if (!wasGrounded && _state.IsGrounded)
            {
                _sinceLanded = 0f;
                LastLandingSpeed = -verticalSpeedBefore;
                Landed?.Invoke(LastLandingSpeed);
                EventBus<PlayerLandedEvent>.Publish(new PlayerLandedEvent
                {
                    Player = gameObject,
                    ImpactSpeed = LastLandingSpeed,
                    Position = _state.Position,
                });
            }
            else if (wasGrounded && !_state.IsGrounded && !_jumpedThisTick)
            {
                // Сошли с движущейся поверхности — сохраняем её импульс.
                _state.Velocity += groundVelocityBefore;
            }

            ApplyStandOnForce();
        }

        /// <summary>Вода по точкам тела: ноги — вброд, грудь — плавание, глаза — под водой (LiquidProbe от мира).</summary>
        private void UpdateWater()
        {
            if (_state.Mode != MoveMode.Walk)
            {
                _state.InWater = _state.IsSwimming = _state.HeadUnderwater = false;
                return;
            }
            Vector3 position = _state.Position;
            _state.IsSwimming = LiquidProbe.At(position + Vector3.up * (CurrentHeight * 0.55f));
            _state.HeadUnderwater = LiquidProbe.At(position + Vector3.up * TargetEyeHeight);
            _state.InWater = _state.IsSwimming || LiquidProbe.At(position + Vector3.up * 0.25f);
        }

        /// <summary>
        /// Плавание: направление — по взгляду (вверх и вниз тоже), прыжок — всплыть, присед — нырнуть; без движения
        /// медленно тонешь. Спринт быстрее и тратит стамину. Падение в воду не калечит — вода гасит скорость.
        /// </summary>
        private void SimulateSwim(in PlayerInputFrame input, float dt)
        {
            var s = _settings;
            var m = _modifiers;
            Vector2 move = Vector2.ClampMagnitude(input.Move, 1f);
            Vector3 wish = Quaternion.Euler(input.Pitch, input.Yaw, 0f) * new Vector3(move.x, 0f, move.y);
            if (input.IsHeld(PlayerButtons.Jump))
                wish += Vector3.up;
            if (input.IsHeld(PlayerButtons.Crouch))
                wish += Vector3.down;
            float wishAmount = wish.magnitude;
            Vector3 wishDir = wishAmount > 1e-4f ? wish / wishAmount : Vector3.zero;

            bool sprint = input.IsHeld(PlayerButtons.Sprint) && m.CanSprint && wishAmount > 0.1f;
            _state.IsSprinting = sprint;
            if (sprint)
                _stamina?.Drain(s.SprintStaminaPerSecond * m.StaminaCostMul * dt);
            float speed = s.SwimSpeed * (sprint ? s.SwimSprintMultiplier : 1f) * m.SpeedMul * Mathf.Min(wishAmount, 1f);

            SourceMovement.Friction(ref _state.Velocity, dt, s.SwimFriction, s.StopSpeed);
            SourceMovement.Accelerate(ref _state.Velocity, wishDir, speed, s.SwimAccelerate * m.AccelMul, dt);
            if (wishAmount < 0.1f)
                _state.Velocity.y = Mathf.MoveTowards(_state.Velocity.y, -s.SwimSinkSpeed, s.SwimAccelerate * dt);

            Vector3 drift = new Vector3(ExternalDrift.x, 0f, ExternalDrift.z) * dt;
            _mover.SlideMove(ref _state.Position, ref _state.Velocity, _state.Velocity * dt + drift, CurrentHeight, keepHorizontal: false);
            _state.IsGrounded = false;
            _state.IsSliding = false;
            _state.GroundCollider = null;
            _state.GroundVelocity = Vector3.zero;
            _jumpBuffer = 0f;
        }

        private void SimulateFly(in PlayerInputFrame input, float dt)
        {
            var s = _settings;
            Vector2 move = Vector2.ClampMagnitude(input.Move, 1f);
            float vertical = (input.IsHeld(PlayerButtons.Jump) ? 1f : 0f) - (input.IsHeld(PlayerButtons.Crouch) ? 1f : 0f);
            Vector3 wish = Quaternion.Euler(input.Pitch, input.Yaw, 0f) * new Vector3(move.x, 0f, move.y) + Vector3.up * vertical;
            float wishAmount = wish.magnitude;
            Vector3 wishDir = wishAmount > 1e-4f ? wish / wishAmount : Vector3.zero;
            float speed = s.FlySpeed * (input.IsHeld(PlayerButtons.Sprint) ? s.FlySprintMultiplier : 1f) * Mathf.Min(wishAmount, 1f);

            SourceMovement.Friction(ref _state.Velocity, dt, s.FlyFriction, s.StopSpeed);
            SourceMovement.Accelerate(ref _state.Velocity, wishDir, speed, s.FlyAccelerate, dt);

            if (_state.Mode == MoveMode.Noclip)
                _state.Position += _state.Velocity * dt;
            else
                _mover.SlideMove(ref _state.Position, ref _state.Velocity, _state.Velocity * dt, CurrentHeight, keepHorizontal: false);

            _state.IsGrounded = false;
            _state.IsSliding = false;
            _state.IsSprinting = false;
            _state.GroundCollider = null;
            _state.GroundVelocity = Vector3.zero;
        }

        private void CategorizeGround()
        {
            var s = _settings;
            if (_state.Velocity.y > s.MaxGroundedUpSpeed || _jumpedThisTick)
            {
                SetAirborne();
                return;
            }

            bool found = _mover.ProbeGround(_state.Position, CurrentHeight, s.GroundProbeDistance, out var ground);
            if (found && ground.IsWalkable)
            {
                _state.IsGrounded = true;
                _state.IsSliding = false;
                _state.GroundNormal = ground.Normal;
                _state.GroundPoint = ground.Point;
                _state.GroundCollider = ground.Collider;
                _state.Position.y -= ground.Distance;
                if (_state.Velocity.y < 0f)
                    _state.Velocity.y = 0f;
                _state.GroundVelocity = GetSurfaceVelocity(ground.Collider, ground.Point);
            }
            else
            {
                SetAirborne();
                if (found)
                {
                    // Стоим на слишком крутом склоне: физически это полёт со скольжением вдоль поверхности.
                    _state.IsSliding = true;
                    _state.GroundNormal = ground.Normal;
                }
            }
        }

        private void SetAirborne()
        {
            _state.IsGrounded = false;
            _state.IsSliding = false;
            _state.GroundNormal = Vector3.up;
            _state.GroundCollider = null;
            _state.GroundVelocity = Vector3.zero;
        }

        private static Vector3 GetSurfaceVelocity(Collider collider, Vector3 point)
        {
            if (collider == null)
                return Vector3.zero;
            var surface = collider.GetComponentInParent<IMovingSurface>();
            if (surface != null)
                return surface.GetVelocityAt(point);
            var body = collider.attachedRigidbody;
            if (body != null && !body.isKinematic)
                return body.GetPointVelocity(point);
            return Vector3.zero;
        }

        private void UpdateCrouch(bool wantCrouch)
        {
            var s = _settings;
            float delta = s.StandHeight - s.CrouchHeight;

            if (wantCrouch && !_state.IsCrouched)
            {
                _state.IsCrouched = true;
                if (!_state.IsGrounded)
                {
                    // Присед в воздухе поднимает ноги, голова остаётся на месте (как в Source).
                    _state.Position.y += delta;
                    ShiftFeetVisual(delta);
                }
                ApplyCapsule();
            }
            else if (!wantCrouch && _state.IsCrouched)
            {
                if (_state.IsGrounded)
                {
                    if (_mover.CanFit(_state.Position, s.StandHeight))
                        _state.IsCrouched = false;
                }
                else
                {
                    Vector3 lowered = _state.Position - Vector3.up * delta;
                    if (_mover.CanFit(lowered, s.StandHeight))
                    {
                        _state.Position = lowered;
                        _state.IsCrouched = false;
                        ShiftFeetVisual(-delta);
                    }
                    else if (_mover.CanFit(_state.Position, s.StandHeight))
                    {
                        _state.IsCrouched = false;
                    }
                }
                if (!_state.IsCrouched)
                    ApplyCapsule();
            }
        }

        private bool TryPayJumpStamina() =>
            _stamina == null || _stamina.TryConsume(_settings.JumpStaminaCost * _modifiers.StaminaCostMul);

        private void DoJump()
        {
            var s = _settings;
            Vector3 horizontal = _state.HorizontalVelocity;
            float factor = _modifiers.JumpSpeedFactor > 0f ? _modifiers.JumpSpeedFactor : s.MaxJumpSpeedFactor;
            float maxHorizontal = s.SprintSpeed * factor * Mathf.Max(_modifiers.SpeedMul, 0.01f);
            if (horizontal.sqrMagnitude > maxHorizontal * maxHorizontal)
                horizontal = horizontal.normalized * maxHorizontal;

            Vector3 velocity = horizontal + _state.GroundVelocity;
            velocity.y = s.JumpSpeed * Mathf.Sqrt(Mathf.Max(_modifiers.JumpMul, 0f)) + Mathf.Max(_state.GroundVelocity.y, 0f);
            _state.Velocity = velocity;

            _jumpBuffer = 0f;
            _timeSinceGrounded = float.MaxValue;
            _jumpedThisTick = true;
            SetAirborne();
            Jumped?.Invoke();
        }

        private void GroundMove(float dt)
        {
            _state.Velocity.y = 0f;
            Vector3 delta = _state.Velocity * dt;

            // Идём вдоль склона, сохраняя горизонтальный темп.
            Vector3 normal = _state.GroundNormal;
            if (normal.y < 0.999f && delta.sqrMagnitude > 1e-10f)
            {
                Vector3 along = Vector3.ProjectOnPlane(delta, normal);
                if (along.sqrMagnitude > 1e-10f)
                    delta = along.normalized * delta.magnitude;
            }

            // Перенос поверхностью: вертикаль — напрямую (лифт не должен упираться сам в себя), горизонталь — с коллизиями.
            Vector3 carry = _state.GroundVelocity * dt;
            _state.Position.y += carry.y;
            delta += new Vector3(carry.x, 0f, carry.z);
            delta += new Vector3(ExternalDrift.x, 0f, ExternalDrift.z) * dt;

            StepSlideMove(delta);
            StayOnGround();
        }

        /// <summary>Двигаемся; если упёрлись в стену — пробуем подняться на ступеньку и выбираем лучший результат.</summary>
        private void StepSlideMove(Vector3 delta)
        {
            float height = CurrentHeight;
            Vector3 startPosition = _state.Position;
            Vector3 startVelocity = _state.Velocity;

            bool hitWall = _mover.SlideMove(ref _state.Position, ref _state.Velocity, delta, height, keepHorizontal: true);
            if (!hitWall)
                return;

            var s = _settings;
            Vector3 stepPosition = startPosition;
            Vector3 stepVelocity = startVelocity;

            // Вверх.
            float up = s.StepHeight;
            if (_mover.Cast(stepPosition, height, Vector3.up, up + s.SkinWidth, out var ceiling))
                up = Mathf.Max(0f, ceiling.distance - s.SkinWidth);
            if (up < 0.05f)
                return;
            stepPosition.y += up;

            // Вперёд.
            _mover.SlideMove(ref stepPosition, ref stepVelocity, new Vector3(delta.x, 0f, delta.z), height, keepHorizontal: true);

            // Вниз: должны встать на проходимую поверхность.
            if (!_mover.ProbeGround(stepPosition, height, up + s.SkinWidth, out var ground) || !ground.IsWalkable)
                return;
            stepPosition.y -= ground.Distance;
            if (stepPosition.y - startPosition.y < 0.01f)
                return;

            if (HorizontalDistanceSqr(startPosition, stepPosition) > HorizontalDistanceSqr(startPosition, _state.Position) + 1e-6f)
            {
                float rise = stepPosition.y - _state.Position.y;
                _state.Position = stepPosition;
                _state.Velocity = stepVelocity;
                ShiftFeetVisual(rise);
            }
        }

        /// <summary>Прижимаемся к земле на спусках и ступеньках вниз, чтобы не «подлетать».</summary>
        private void StayOnGround()
        {
            if (_jumpedThisTick)
                return;
            var s = _settings;
            if (!_mover.ProbeGround(_state.Position, CurrentHeight, s.StepHeight, out var ground) || !ground.IsWalkable)
                return;
            if (ground.Distance <= 0.001f)
                return;
            _state.Position.y -= ground.Distance;
            if (ground.Distance > 0.08f && ground.Normal.y > 0.95f)
                ShiftFeetVisual(-ground.Distance);
        }

        private void AirMove(float dt)
        {
            Vector3 drift = new Vector3(ExternalDrift.x, 0f, ExternalDrift.z) * dt;
            _mover.SlideMove(ref _state.Position, ref _state.Velocity, _state.Velocity * dt + drift, CurrentHeight, keepHorizontal: false);
        }

        private void PushRigidbodies(float dt)
        {
            _pushedBodies.Clear();
            var hits = _mover.Hits;
            for (int i = 0; i < hits.Count; i++)
            {
                var hit = hits[i];
                var body = hit.Collider != null ? hit.Collider.attachedRigidbody : null;
                if (body == null || body.isKinematic || !_pushedBodies.Add(body))
                    continue;

                Vector3 direction = -hit.Normal;
                direction.y = 0f;
                if (direction.sqrMagnitude < 1e-4f)
                    continue;
                direction.Normalize();

                float playerSpeed = Vector3.Dot(hit.VelocityBefore, direction);
                if (playerSpeed <= 0f)
                    continue;
                float bodySpeed = Vector3.Dot(body.GetPointVelocity(hit.Point), direction);
                float deltaSpeed = playerSpeed - bodySpeed;
                if (deltaSpeed <= 0f)
                    continue;

                float impulse = Mathf.Min(deltaSpeed * body.mass, _settings.PushForce * dt);
                body.AddForceAtPosition(direction * impulse, hit.Point, ForceMode.Impulse);
            }
        }

        private void ApplyStandOnForce()
        {
            if (!_state.IsGrounded || _state.GroundCollider == null)
                return;
            var body = _state.GroundCollider.attachedRigidbody;
            if (body != null && !body.isKinematic)
                body.AddForceAtPosition(Vector3.down * (_settings.Mass * Physics.gravity.magnitude), _state.GroundPoint, ForceMode.Force);
        }

        /// <summary>Ступни резко сместились по вертикали — гасим это для камеры (см. InterpolatedStepOffset).</summary>
        private void ShiftFeetVisual(float delta) => _stepOffset = Mathf.Clamp(_stepOffset - delta, -1f, 1f);

        private void ApplyCapsule()
        {
            float height = CurrentHeight;
            if (Mathf.Approximately(height, _appliedCapsuleHeight))
                return;
            _appliedCapsuleHeight = height;
            _capsule.radius = _settings.Radius;
            _capsule.height = height;
            _capsule.center = new Vector3(0f, height * 0.5f, 0f);
        }

        private static float HorizontalDistanceSqr(Vector3 a, Vector3 b)
        {
            float dx = b.x - a.x;
            float dz = b.z - a.z;
            return dx * dx + dz * dz;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Local = null;

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (_settings == null)
                return;
            Vector3 feet = Application.isPlaying ? _state.Position : transform.position;
            float height = Application.isPlaying ? CurrentHeight : _settings.StandHeight;
            Gizmos.color = Application.isPlaying && _state.IsGrounded ? Color.green : Color.yellow;
            Vector3 bottom = feet + Vector3.up * _settings.Radius;
            Vector3 top = feet + Vector3.up * (height - _settings.Radius);
            Gizmos.DrawWireSphere(bottom, _settings.Radius);
            Gizmos.DrawWireSphere(top, _settings.Radius);
            if (Application.isPlaying)
            {
                Gizmos.color = Color.cyan;
                Gizmos.DrawLine(feet, feet + _state.Velocity * 0.25f);
            }
        }
#endif
    }
}
