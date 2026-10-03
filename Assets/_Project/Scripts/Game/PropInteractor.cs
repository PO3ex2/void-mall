using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using VoidMall.Console;
using VoidMall.Core;
using VoidMall.Input;
using VoidMall.Items;
using VoidMall.Player;
using VoidMall.World;

namespace VoidMall.Game
{
    /// <summary>
    /// Взаимодействие с пропами в духе HL2 (GDD §2.6):
    /// E — взять лёгкий проп (держится перед камерой) или тащить тяжёлый за точку захвата, E ещё раз — отпустить;
    /// ЛКМ с пропом в руках — бросить; R — повернуть на 45° (Shift+R — наклонить); колесо — ближе/дальше;
    /// ПКМ с молотком — прибить проп к тому, чего он касается; удержание E на прибитом — вытащить гвозди;
    /// удержание F — аккуратно разобрать (материалы — в журнал добычи до появления инвентаря).
    /// Тяжёлая ноша замедляет игрока (IMovementModifierSource).
    /// </summary>
    [DefaultExecutionOrder(-10)]
    [RequireComponent(typeof(PlayerMotor))]
    public sealed class PropInteractor : MonoBehaviour, IMovementModifierSource
    {
        public static readonly CVar<float> HoldMass = new("sv_hold_mass", 20f, "Максимальная масса, которую можно держать в руках, кг.", ConsoleFlags.Cheat);
        public static readonly CVar<float> DragMass = new("sv_drag_mass", 300f, "Максимальная масса, которую можно тащить, кг.", ConsoleFlags.Cheat);

        private const float Reach = 2.6f;
        private const float UnnailTime = 1f;
        private const float MaxHoldError = 1.6f;
        private const float MaxDragDistance = 3.4f;
        private const float ThrowImpulse = 260f;
        private const float MaxThrowSpeed = 10f;

        private enum Mode { None, Hold, Drag }

        private PlayerMotor _motor;
        private ToolController _tool;
        private Camera _camera;
        private InputAction _interact;
        private InputAction _primary;
        private InputAction _secondary;
        private InputAction _rotate;
        private InputAction _disassemble;
        private InputAction _scroll;

        private Mode _mode;
        private PropBody _held;
        private Vector3 _grabLocal;
        private float _holdDistance;
        private Quaternion _holdRotation;
        private float _dragDistance;
        private float _stuckTime;

        /// <summary>После броска или гвоздя кнопки мыши ещё зажаты: инструмент ждёт, пока их отпустят.</summary>
        private bool _waitRelease;

        private PropBody _target;
        private float _interactHeld;
        private bool _interactConsumed;
        private float _disassembleProgress;
        private PropBody _disassembleTarget;

        private Label _hint;
        private string _lastHint;
        private string _message;
        private float _messageTime;

        private readonly Collider[] _overlap = new Collider[32];

        public bool IsHolding => _mode != Mode.None;

        public bool IsBusy => _mode != Mode.None || _waitRelease || _disassembleTarget != null || (_interactHeld > 0.15f && _target != null && _target.IsNailed);

        private void Awake() => _motor = GetComponent<PlayerMotor>();

        private void Start()
        {
            _camera = GetComponentInChildren<Camera>();
            _tool = GetComponent<ToolController>();
            if (Services.TryGet(out InputService input))
            {
                const string map = InputService.Maps.Player;
                _interact = input.Action(map, "Interact");
                _primary = input.Action(map, "Primary");
                _secondary = input.Action(map, "Secondary");
                _rotate = input.Action(map, "Reload");
                _disassemble = input.Action(map, "Disassemble");
                _scroll = input.Action(map, "Scroll");
            }
            CreateHud();
        }

        private void OnDisable() => Release();

        // ───────────────────────────── Ввод ─────────────────────────────

        private void Update()
        {
            if (_interact == null || _camera == null || PropManager.Instance == null)
                return;

            if (_waitRelease && !_primary.IsPressed() && !_secondary.IsPressed())
                _waitRelease = false;

            if (_held != null && !_held.isActiveAndEnabled)
                Release();

            _target = _mode == Mode.None ? FindTarget() : null;
            if (_mode != Mode.None)
            {
                _worldItemTarget = null;
                _shelfItemTarget = -1;
            }

            if (_mode != Mode.None)
                UpdateCarrying();
            else
                UpdateFreeHands();

            if (_tool != null)
                _tool.InputBlocked = IsBusy;
            UpdateHud();
        }

        private PropBody FindTarget()
        {
            var view = _camera.transform;
            var ray = new Ray(view.position, view.forward);
            _worldItemTarget = null;
            _shelfItemTarget = -1;
            int mask = (1 << GameLayers.World) | (1 << GameLayers.Props) | (1 << GameLayers.Debris);
            if (!Physics.Raycast(ray, out var hit, Reach, mask, QueryTriggerInteraction.Ignore))
                return null;
            // Предмет на полу — сразу цель подбора.
            if (WorldItems.TryGet(hit.collider, out var worldItem))
            {
                _worldItemTarget = worldItem;
                return null;
            }
            if (!PropManager.TryGetProp(hit.collider, out var body))
                return null;
            _targetPoint = hit.point;
            // Товар на полке перед лучом важнее самой полки.
            if (ShelfLoot.Instance != null && ShelfLoot.Instance.TryPick(body, ray, Reach, out int item, out _))
                _shelfItemTarget = item;
            return body;
        }

        private WorldItem _worldItemTarget;
        private int _shelfItemTarget = -1;

        /// <summary>E по предмету: с полки или с пола — в инвентарь.</summary>
        private bool TryPickItem()
        {
            var inventory = PlayerInventory.Local;
            if (inventory == null)
                return false;
            ItemStack stack = null;
            Vector3 dropPoint = default;
            if (_worldItemTarget != null)
            {
                dropPoint = _worldItemTarget.transform.position;
                stack = WorldItems.Instance.Take(_worldItemTarget);
                _worldItemTarget = null;
            }
            else if (_target != null && _shelfItemTarget >= 0)
            {
                dropPoint = _targetPoint;
                stack = ShelfLoot.Instance.Take(_target, _shelfItemTarget);
                _shelfItemTarget = -1;
            }
            if (stack == null)
                return false;
            var def = inventory.Items.Get(stack.Def);
            int before = stack.Count;
            int left = inventory.Add(stack);
            if (left > 0)
            {
                WorldItems.Instance?.Spawn(stack, dropPoint + Vector3.up * 0.1f, Vector3.zero);
                Say(left == before ? $"Некуда положить: {def.DisplayName}" : $"Взято {before - left} из {before}: {def.DisplayName} (больше не помещается)");
            }
            else
            {
                Say($"Взято: {def.DisplayName}{(before > 1 ? $" ×{before}" : "")}");
            }
            return true;
        }

        private Vector3 _targetPoint;

        private void UpdateFreeHands()
        {
            // E: короткое нажатие — взять; удержание на прибитом — вытащить гвозди.
            if (_interact.WasPressedThisFrame())
            {
                _interactHeld = 0f;
                _interactConsumed = false;
                // Предмет под прицелом (на полу или на полке) подбирается сразу.
                if ((_worldItemTarget != null || _shelfItemTarget >= 0) && TryPickItem())
                    _interactConsumed = true;
            }
            if (_interact.IsPressed() && _target != null && !_interactConsumed)
            {
                _interactHeld += Time.deltaTime;
                if (_target.IsNailed || _target.IsInGroup)
                {
                    if (_interactHeld >= UnnailTime)
                    {
                        _interactConsumed = true;
                        if (PropManager.Instance.Unnail(_target))
                            Say("Гвозди вытащены");
                    }
                }
                else
                {
                    _interactConsumed = true;
                    TryGrab(_target);
                }
            }
            if (_interact.WasReleasedThisFrame())
            {
                // Прибитую группу можно схватить коротким нажатием — она не сдвинется, но подсказка объяснит почему.
                if (!_interactConsumed && _target != null && _target.IsInGroup && !_target.IsNailed && _interactHeld < UnnailTime)
                    TryGrab(_target);
                else if (!_interactConsumed && _target != null && _target.IsNailed)
                    Say("Прибито. Удерживайте E, чтобы вытащить гвозди");
                _interactHeld = 0f;
            }

            // F: аккуратный разбор.
            if (_disassemble != null && _disassemble.IsPressed() && _target != null)
            {
                if (_disassembleTarget != _target)
                {
                    _disassembleTarget = _target;
                    _disassembleProgress = 0f;
                }
                _disassembleProgress += Time.deltaTime / Mathf.Max(0.1f, _target.Def.DisassembleTime);
                if (_disassembleProgress >= 1f)
                {
                    var def = _target.Def;
                    PropManager.Instance.Remove(_target, PropRemoveReason.Disassembled);
                    Say($"Разобрано: {def.DisplayName}");
                    _disassembleTarget = null;
                    _disassembleProgress = 0f;
                }
            }
            else
            {
                _disassembleTarget = null;
                _disassembleProgress = 0f;
            }
        }

        private void TryGrab(PropBody body)
        {
            var root = body.Root;
            float mass = root.GroupMass;
            if (root.IsNailed)
            {
                Say("Прибито. Удерживайте E, чтобы вытащить гвозди");
                return;
            }
            if (mass > DragMass.Value)
            {
                Say($"Слишком тяжело: {mass:0} кг");
                return;
            }
            root.Wake();
            if (root.Body == null)
                return;
            _held = root;
            _stuckTime = 0f;
            root.IsHeld = true;
            var view = _camera.transform;
            if (mass <= HoldMass.Value)
            {
                _mode = Mode.Hold;
                Vector3 extents = root.GroupBounds().extents;
                _holdDistance = Mathf.Clamp(1f + extents.magnitude * 0.8f, 1.1f, 2.4f);
                _holdRotation = Quaternion.Inverse(YawRotation()) * root.transform.rotation;
                root.SetLayer(GameLayers.Debris); // в руках не толкает игрока
                root.Body.useGravity = false;
            }
            else
            {
                _mode = Mode.Drag;
                _grabLocal = root.transform.InverseTransformPoint(_targetPoint);
                _dragDistance = Mathf.Clamp(Vector3.Distance(view.position, _targetPoint), 1f, Reach);
            }
        }

        private void UpdateCarrying()
        {
            if (_held == null)
            {
                Release();
                return;
            }
            if (_interact.WasPressedThisFrame())
            {
                Release();
                return;
            }
            if (_mode == Mode.Hold && _primary.WasPressedThisFrame())
            {
                _waitRelease = true;
                Throw();
                return;
            }
            if (_secondary.WasPressedThisFrame() && _tool != null && _tool.HammerActive)
            {
                _waitRelease = true;
                Nail();
                return;
            }
            if (_mode == Mode.Hold && _rotate != null && _rotate.WasPressedThisFrame())
            {
                bool tilt = Keyboard.current != null && Keyboard.current.shiftKey.isPressed;
                _holdRotation = (tilt ? Quaternion.Euler(45f, 0f, 0f) : Quaternion.Euler(0f, 45f, 0f)) * _holdRotation;
            }
            if (_scroll != null && _scroll.WasPressedThisFrame())
            {
                float step = _scroll.ReadValue<float>() > 0 ? 0.25f : -0.25f;
                if (_mode == Mode.Hold)
                    _holdDistance = Mathf.Clamp(_holdDistance + step, 0.9f, 3f);
                else
                    _dragDistance = Mathf.Clamp(_dragDistance + step, 1f, Reach);
            }
        }

        // ───────────────────────────── Физика ноши ─────────────────────────────

        private void FixedUpdate()
        {
            if (_held == null || _held.Body == null || _mode == Mode.None)
                return;
            var body = _held.Body;
            var view = _camera.transform;
            float dt = Time.fixedDeltaTime;

            if (_mode == Mode.Hold)
            {
                Vector3 target = view.position + view.forward * _holdDistance;
                Vector3 center = _held.GroupBounds().center;
                Vector3 error = target - center;
                // Отпускаем, только если проп застрял (упёрся в стену) дольше полусекунды, а не при резком повороте.
                _stuckTime = error.magnitude > MaxHoldError ? _stuckTime + dt : 0f;
                if (_stuckTime > 0.5f)
                {
                    Release();
                    return;
                }
                // ПД-регулятор на скорость: быстро догоняет точку, но упирается в стены (физика сама разрешит контакт).
                Vector3 desired = Vector3.ClampMagnitude(error / dt * 0.5f, 12f);
                body.linearVelocity = Vector3.Lerp(body.linearVelocity, desired, 0.6f);

                Quaternion targetRotation = YawRotation() * _holdRotation;
                Quaternion delta = targetRotation * Quaternion.Inverse(body.rotation);
                delta.ToAngleAxis(out float angle, out Vector3 axis);
                if (angle > 180f)
                    angle -= 360f;
                Vector3 angular = Mathf.Abs(angle) > 0.01f ? axis.normalized * (angle * Mathf.Deg2Rad / dt * 0.4f) : Vector3.zero;
                body.angularVelocity = Vector3.ClampMagnitude(angular, 20f);
            }
            else
            {
                Vector3 grab = _held.transform.TransformPoint(_grabLocal);
                if (Vector3.Distance(view.position, grab) > MaxDragDistance)
                {
                    Release();
                    return;
                }
                Vector3 target = view.position + view.forward * _dragDistance;
                Vector3 pointVelocity = body.GetPointVelocity(grab);
                float mass = body.mass;
                Vector3 force = (target - grab) * (mass * 40f) - pointVelocity * (mass * 6f);
                // Сила рук ограничена: тяжёлое волочится, а не взлетает.
                force = Vector3.ClampMagnitude(force, 900f + DragMass.Value * 6f);
                force.y = Mathf.Min(force.y, mass * 9.81f * 0.6f);
                body.AddForceAtPosition(force, grab, ForceMode.Force);
            }
        }

        private Quaternion YawRotation()
        {
            float yaw = _camera.transform.eulerAngles.y;
            return Quaternion.Euler(0f, yaw, 0f);
        }

        private void Throw()
        {
            var body = _held.Body;
            var view = _camera.transform;
            Release();
            if (body == null)
                return;
            float speed = Mathf.Min(MaxThrowSpeed, ThrowImpulse / Mathf.Max(1f, body.mass));
            body.linearVelocity = view.forward * speed + _motor.State.Velocity * 0.5f;
        }

        private void Release()
        {
            if (_held != null)
            {
                _held.IsHeld = false;
                if (_mode == Mode.Hold)
                {
                    _held.SetLayer(GameLayers.Props);
                    if (_held.Body != null)
                        _held.Body.useGravity = true;
                }
            }
            _held = null;
            _mode = Mode.None;
        }

        // ───────────────────────────── Гвозди ─────────────────────────────

        /// <summary>Прибить ношу к тому, чего она касается: к клеткам мира или к другому пропу.</summary>
        private void Nail()
        {
            var inventory = PlayerInventory.Local;
            ushort nails = 0;
            bool needNails = inventory != null && ItemSystem.Instance != null && ItemSystem.Instance.TryResolve("material.nails", out nails) && !ItemSystem.Creative.Value;
            if (needNails && inventory.CountOf(nails) <= 0)
            {
                Say("Нет гвоздей");
                return;
            }
            if (NailHeld() && needNails)
                inventory.TryConsume(nails, 1);
        }

        /// <summary>Прибить ношу. true — прибито.</summary>
        private bool NailHeld()
        {
            var held = _held;
            Bounds bounds = held.GroupBounds();
            Vector3 halfExtents = bounds.extents + Vector3.one * 0.08f;
            int mask = (1 << GameLayers.World) | (1 << GameLayers.Props);

            // Касание другого пропа — ближайший к точке прицела.
            PropBody targetProp = null;
            float best = float.MaxValue;
            int count = Physics.OverlapBoxNonAlloc(bounds.center, halfExtents, _overlap, Quaternion.identity, mask, QueryTriggerInteraction.Ignore);
            bool touchesWorld = false;
            for (int i = 0; i < count; i++)
            {
                var collider = _overlap[i];
                if (collider.gameObject.layer == GameLayers.World)
                {
                    touchesWorld = true;
                    continue;
                }
                if (PropManager.TryGetProp(collider, out var other) && other.Root != held)
                {
                    float d = Vector3.Distance(collider.ClosestPoint(bounds.center), bounds.center);
                    if (d < best)
                    {
                        best = d;
                        targetProp = other;
                    }
                }
            }

            var cells = new List<int3>();
            if (touchesWorld && ChunkManager.Instance != null)
                CollectAnchorCells(bounds, cells);

            Release();
            if (targetProp != null && (cells.Count == 0 || best < 0.05f))
            {
                PropManager.Instance.NailToProp(held, targetProp);
                Say($"Прибито к: {targetProp.Def.DisplayName}");
                return true;
            }
            else if (cells.Count > 0)
            {
                PropManager.Instance.NailToWorld(held, cells);
                Say("Прибито");
                return true;
            }
            else
            {
                Say("Не к чему прибить — поднесите проп вплотную к стене, полу или другому пропу");
                return false;
            }
        }

        /// <summary>Твёрдые клетки мира, которых касается габарит ноши (до 16 штук).</summary>
        private static void CollectAnchorCells(Bounds bounds, List<int3> cells)
        {
            var world = ChunkManager.Instance;
            Vector3 min = bounds.min - Vector3.one * 0.1f;
            Vector3 max = bounds.max + Vector3.one * 0.1f;
            int3 a = (int3)math.floor((float3)min);
            int3 b = (int3)math.floor((float3)max);
            for (int y = a.y; y <= b.y; y++)
            for (int z = a.z; z <= b.z; z++)
            for (int x = a.x; x <= b.x; x++)
            {
                var cell = new int3(x, y, z);
                Cell value = world.GetCell(cell);
                if (value.IsAir)
                    continue;
                var info = world.GetInfo(value.Material);
                if (!info.IsSolid || (info.Kind == MaterialKind.Smooth && !Density.IsSolid(value.Value)))
                    continue;
                cells.Add(cell);
                if (cells.Count >= 16)
                    return;
            }
        }

        // ───────────────────────────── Модификаторы движения ─────────────────────────────

        public void ApplyModifiers(ref MovementModifiers modifiers)
        {
            if (_held == null || _mode == Mode.None)
                return;
            float mass = _held.GroupMass;
            if (_mode == Mode.Hold)
            {
                modifiers.SpeedMul *= 1f - 0.2f * Mathf.Clamp01(mass / Mathf.Max(1f, HoldMass.Value));
            }
            else
            {
                float load = Mathf.Clamp01(mass / Mathf.Max(1f, DragMass.Value));
                modifiers.SpeedMul *= Mathf.Lerp(0.8f, 0.35f, load);
                modifiers.CanSprint = false;
                modifiers.CanJump = false;
            }
        }

        // ───────────────────────────── Подсказки ─────────────────────────────

        private void Say(string message)
        {
            _message = message;
            _messageTime = 2.5f;
        }

        private void CreateHud()
        {
            if (GameBootstrap.Config == null || GameBootstrap.Config.PanelSettings == null)
                return;
            var go = new GameObject("PropHud");
            go.SetActive(false);
            go.transform.SetParent(transform, false);
            var document = go.AddComponent<UIDocument>();
            document.panelSettings = GameBootstrap.Config.PanelSettings;
            document.sortingOrder = 6;
            go.SetActive(true);

            var root = document.rootVisualElement;
            root.pickingMode = PickingMode.Ignore;
            root.style.position = Position.Absolute;
            root.style.left = root.style.right = root.style.top = root.style.bottom = 0f;

            _hint = new Label { pickingMode = PickingMode.Ignore, enableRichText = false };
            _hint.style.position = Position.Absolute;
            _hint.style.top = Length.Percent(56);
            _hint.style.left = 0f;
            _hint.style.right = 0f;
            _hint.style.unityTextAlign = TextAnchor.UpperCenter;
            _hint.style.color = new Color(1f, 1f, 1f, 0.92f);
            _hint.style.fontSize = 15f;
            root.Add(_hint);
        }

        private void UpdateHud()
        {
            if (_hint == null)
                return;
            _messageTime -= Time.deltaTime;
            string text;
            if (_messageTime > 0f && !string.IsNullOrEmpty(_message))
            {
                text = _message;
            }
            else if (_mode == Mode.Hold)
            {
                text = $"{_held.Def.DisplayName} ({_held.GroupMass:0} кг) · ЛКМ — бросить · E — отпустить · R — повернуть · колесо — ближе/дальше"
                       + (_tool != null && _tool.HammerActive ? " · ПКМ — прибить" : "");
            }
            else if (_mode == Mode.Drag)
            {
                text = $"Тащите: {_held.Def.DisplayName} ({_held.GroupMass:0} кг) · E — отпустить"
                       + (_tool != null && _tool.HammerActive ? " · ПКМ — прибить" : "");
            }
            else if (!_interact.actionMap.enabled)
            {
                // Открыто окно или консоль — подсказки прицела не нужны.
                text = string.Empty;
            }
            else if (_disassembleTarget != null)
            {
                text = $"Разбор: {_disassembleTarget.Def.DisplayName} {_disassembleProgress * 100f:0}%";
            }
            else if (_worldItemTarget != null && PlayerInventory.Local != null)
            {
                var stack = _worldItemTarget.Stack;
                text = $"{PlayerInventory.Local.Items.Get(stack.Def).DisplayName}{(stack.Count > 1 ? $" ×{stack.Count}" : "")} · E — взять";
            }
            else if (_target != null && _shelfItemTarget >= 0 && ShelfLoot.Instance != null && PlayerInventory.Local != null
                     && ShelfLoot.Instance.TryGetItem(_target, _shelfItemTarget, out var shelfItem))
            {
                text = $"{PlayerInventory.Local.Items.Get(shelfItem.Def).DisplayName}{(shelfItem.Count > 1 ? $" ×{shelfItem.Count}" : "")} · E — взять";
            }
            else if (_target != null)
            {
                float mass = _target.Root.GroupMass;
                float health = PropManager.Instance.Health(_target);
                string action = _target.IsNailed
                    ? (_interactHeld > 0.15f ? $"Вытаскиваем гвозди… {_interactHeld / UnnailTime * 100f:0}%" : "прибито · удерживайте E — вытащить гвозди")
                    : (mass <= HoldMass.Value ? "E — взять" : mass <= DragMass.Value ? "E — тащить" : "слишком тяжело")
                      + (_target.IsInGroup ? (_interactHeld > 0.15f ? $" · вытаскиваем гвозди… {_interactHeld / UnnailTime * 100f:0}%" : " · удерживайте E — вытащить гвозди") : "");
                text = $"{_target.Def.DisplayName} · {mass:0} кг · прочность {health * 100f:0}% · {action} · удерживайте F — разобрать";
            }
            else
            {
                text = string.Empty;
            }
            if (text != _lastHint)
            {
                _hint.text = text;
                _lastHint = text;
            }
        }
    }
}
