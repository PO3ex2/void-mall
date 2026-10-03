using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using VoidMall.Console;
using VoidMall.Core;
using VoidMall.Input;
using VoidMall.Items;
using VoidMall.Net;
using VoidMall.World;

namespace VoidMall.Game
{
    /// <summary>
    /// Действия предметом в руке (выбранным в хотбаре: 1–9, колесо):
    /// руки — слабый удар; молоток, кувалда, лом — урон по блокам и пропам по своим характеристикам;
    /// бур — срезать (ЛКМ) и наращивать (ПКМ, тратит грунт или бетонную крошку) гладкий массив;
    /// блок — ставится ПКМ (тратится); еда и медикаменты — ЛКМ, применить; одежда — ЛКМ, надеть; фонарик — ЛКМ, вкл/выкл.
    /// Все правки мира — командами через сессию (готово к коопу). В творческом режиме блоки и материалы не тратятся.
    /// </summary>
    public sealed class ToolController : MonoBehaviour
    {
        public static readonly CVar<int> HandDamage = new("tool_hand_damage", 15, "Урон голыми руками.", ConsoleFlags.Cheat);
        public static readonly CVar<float> DrillRadius = new("drill_radius", 1.4f, "Радиус кисти бура, м.");

        [SerializeField] private float _reach = 4f;
        [SerializeField] private Material _outlineMaterial;
        [SerializeField] private Material _crackMaterial;
        [SerializeField] private Material _brushMaterial;

        private static readonly int DamageId = Shader.PropertyToID("_Damage");
        private const int WorldMask = 1 << GameLayers.World;
        private const int PropMask = 1 << GameLayers.Props;
        private const float SmoothPerApplication = 0.25f;

        private Camera _camera;
        private InputAction _primary;
        private InputAction _secondary;
        private InputAction _scroll;
        private readonly InputAction[] _slots = new InputAction[Hotbar.Size];

        private float _actionTimer;
        private bool _hasTarget;
        private VoxelHit _target;
        private bool _hasDrillTarget;
        private RaycastHit _drillHit;
        private PropBody _propTarget;
        private float _smoothDebt;

        private Transform _outline;
        private Transform _cracks;
        private Transform _brush;
        private Material _crackInstance;
        private Label _info;
        private string _lastInfo;
        private Light _flashlight;
        private bool _flashlightOn;

        private ChunkManager World => ChunkManager.Instance;
        private PlayerInventory Inventory => PlayerInventory.Local;

        /// <summary>Ввод занят другим (игрок держит проп, разбирает его, открыто окно): инструмент не реагирует.</summary>
        public bool InputBlocked { get; set; }

        public ItemDef ActiveItem => Inventory != null ? Inventory.ActiveItem : null;
        public bool HammerActive => ActiveItem != null && ActiveItem.Tool == ToolKind.Hammer;

        private void Start()
        {
            _camera = GetComponentInChildren<Camera>();
            if (World == null || _camera == null)
            {
                enabled = false;
                return;
            }
            if (Services.TryGet(out InputService input))
            {
                const string map = InputService.Maps.Player;
                _primary = input.Action(map, "Primary");
                _secondary = input.Action(map, "Secondary");
                _scroll = input.Action(map, "Scroll");
                for (int i = 0; i < Hotbar.Size; i++)
                    _slots[i] = input.Action(map, $"Slot{i + 1}");
            }
            CreateVisuals();
            CreateFlashlight();
            CreateHud();
        }

        private void OnDestroy()
        {
            if (_outline != null) Destroy(_outline.gameObject);
            if (_cracks != null) Destroy(_cracks.gameObject);
            if (_brush != null) Destroy(_brush.gameObject);
            if (_crackInstance != null) Destroy(_crackInstance);
            if (_flashlight != null)
                LightingDirector.DynamicLights.Remove(_flashlight);
        }

        private static readonly Perf.Probe UpdateProbe = Perf.Register("Game.ToolController");

        private void Update()
        {
            using var perf = Perf.Measure(UpdateProbe);
            if (World == null || _primary == null || Inventory == null)
                return;

            var view = _camera.transform;
            _hasTarget = World.Raycast(view.position, view.forward, _reach, out _target);
            _hasDrillTarget = Physics.Raycast(view.position, view.forward, out _drillHit, _reach, WorldMask, QueryTriggerInteraction.Ignore);
            _propTarget = null;
            if (Physics.Raycast(view.position, view.forward, out var propHit, _reach, WorldMask | PropMask, QueryTriggerInteraction.Ignore)
                && PropManager.TryGetProp(propHit.collider, out var prop)
                && (!_hasTarget || propHit.distance < _target.Distance))
            {
                _propTarget = prop;
                _hasTarget = false;
            }

            if (!InputBlocked)
            {
                HandleSelection();
                HandleActions();
            }
            UpdateFlashlight();
            UpdateVisuals();
            UpdateHud();
        }

        private void HandleSelection()
        {
            var hotbar = Inventory.Hotbar;
            for (int i = 0; i < Hotbar.Size; i++)
                if (_slots[i] != null && _slots[i].WasPressedThisFrame())
                    hotbar.Select(i);
            if (_scroll != null && _scroll.WasPressedThisFrame())
                hotbar.Select(hotbar.Selected + (_scroll.ReadValue<float>() > 0 ? -1 : 1));
        }

        private void HandleActions()
        {
            var item = ActiveItem;
            if (item != null && item.Tool == ToolKind.Drill)
            {
                HandleDrilling(item);
                return;
            }
            if (item != null && item.PlacesBlock != null)
                HandlePlacing(item);
            if (item != null && _primary.WasPressedThisFrame())
            {
                if (item.Tool == ToolKind.Flashlight)
                {
                    _flashlightOn = !_flashlightOn;
                    return;
                }
                if (item.IsConsumable || item.IsEquippable)
                {
                    var stack = Inventory.ActiveStack;
                    int slot = Inventory.Hotbar.Selected;
                    Inventory.Use(stack);
                    Inventory.RefillHotbar(slot, stack.Def);
                    return;
                }
            }
            HandleStriking(item);
        }

        /// <summary>Удар ЛКМ (удержание): по пропу или блоку, сила — от инструмента (или руки).</summary>
        private void HandleStriking(ItemDef item)
        {
            if (!_primary.IsPressed())
            {
                _actionTimer = 0f;
                return;
            }
            _actionTimer -= Time.deltaTime;
            if (_actionTimer > 0f)
                return;
            bool striker = item != null && (item.Tool == ToolKind.Hammer || item.Tool == ToolKind.Sledgehammer || item.Tool == ToolKind.Crowbar);
            float interval = striker ? item.SwingInterval : 0.45f;
            if (_propTarget != null && PropManager.Instance != null)
            {
                _actionTimer = interval;
                PropManager.Instance.Damage(_propTarget, striker ? item.PropDamage : HandDamage.Value);
                return;
            }
            if (!_hasTarget)
                return;
            _actionTimer = interval;
            Send(new DamageBlockCommand { Position = _target.Cell, Amount = striker ? item.BlockDamage : HandDamage.Value });
        }

        private void HandlePlacing(ItemDef item)
        {
            if (!_secondary.WasPressedThisFrame() || !_hasTarget || math.all(_target.Normal == 0))
                return;
            if (!World.Materials.TryGetIndex(item.PlacesBlock.Id, out ushort material))
                return;
            int3 cell = _target.Cell + _target.Normal;
            // Та же проверка, что у сервера мира: иначе блок списывался, а внутрь игрока или пропа не ставился.
            if (!WorldAuthority.CanPlaceBlock(World, cell, material))
            {
                _message = "Мешает: игрок или предмет";
                _messageTime = 1.5f;
                return;
            }
            var stack = Inventory.ActiveStack;
            int slot = Inventory.Hotbar.Selected;
            if (!Inventory.TryConsume(stack.Def, 1))
                return;
            Inventory.RefillHotbar(slot, stack.Def);
            Send(new PlaceBlockCommand { Position = cell, Material = material });
        }

        private void HandleDrilling(ItemDef drill)
        {
            bool dig = _primary.IsPressed();
            bool add = _secondary.IsPressed() && !dig;
            if (!dig && !add)
            {
                _actionTimer = 0f;
                return;
            }
            _actionTimer -= Time.deltaTime;
            if (_actionTimer > 0f || !_hasDrillTarget)
                return;
            _actionTimer = drill.SwingInterval;

            ushort material = 0;
            if (add)
            {
                // Наращивание тратит материал: грунт или бетонную крошку (по четверти штуки за применение).
                if (!TryGetFillMaterial(out ushort fillItem, out material))
                {
                    _message = "Нечем наращивать: нужен грунт или бетонная крошка";
                    _messageTime = 2f;
                    return;
                }
                _smoothDebt += SmoothPerApplication;
                if (_smoothDebt >= 1f)
                {
                    _smoothDebt -= 1f;
                    Inventory.TryConsume(fillItem, 1);
                }
            }
            Send(new SmoothBrushCommand
            {
                Center = BrushCenter(add),
                Radius = DrillRadius.Value,
                Strength = drill.DrillStrength,
                Add = add,
                Material = material,
            });
        }

        private bool TryGetFillMaterial(out ushort item, out ushort material)
        {
            item = 0;
            material = 0;
            var registry = Inventory.Items;
            for (int i = 0; i < registry.Count; i++)
            {
                var def = registry.Get(i);
                if (def.SmoothMaterial == null || (!ItemSystem.Creative.Value && Inventory.CountOf((ushort)i) <= 0))
                    continue;
                if (!World.Materials.TryGetIndex(def.SmoothMaterial.Id, out material))
                    continue;
                item = (ushort)i;
                return true;
            }
            return false;
        }

        /// <summary>Срезаем чуть «внутрь» поверхности, наращиваем — чуть «наружу».</summary>
        private float3 BrushCenter(bool add)
        {
            Vector3 offset = add ? _drillHit.normal * (DrillRadius.Value * 0.4f) : -_drillHit.normal * (DrillRadius.Value * 0.3f);
            return _drillHit.point + offset;
        }

        private static void Send<T>(in T command) where T : struct, INetCommand
        {
            if (Services.TryGet(out INetSession session))
                session.SendToAuthority(command);
        }

        // ── Фонарик ──

        private void CreateFlashlight()
        {
            var go = new GameObject("Flashlight");
            go.transform.SetParent(_camera.transform, false);
            go.transform.localPosition = new Vector3(0.25f, -0.2f, 0.1f);
            _flashlight = go.AddComponent<Light>();
            _flashlight.type = LightType.Spot;
            _flashlight.spotAngle = 55f;
            _flashlight.innerSpotAngle = 25f;
            _flashlight.range = 22f;
            _flashlight.intensity = 6f;
            _flashlight.color = new Color(1f, 0.95f, 0.85f);
            _flashlight.shadows = LightShadows.Soft;
            _flashlight.enabled = false;
        }

        /// <summary>Включённый фонарик светит, пока лежит в инвентаре (висит на поясе), даже если в руке другое.</summary>
        private void UpdateFlashlight()
        {
            bool owned = false;
            if (_flashlightOn)
            {
                var registry = Inventory.Items;
                for (int i = 0; i < registry.Count && !owned; i++)
                    owned = registry.Get(i).Tool == ToolKind.Flashlight && Inventory.CountOf((ushort)i) > 0;
            }
            bool on = _flashlightOn && owned;
            if (!owned)
                _flashlightOn = false;
            if (_flashlight.enabled == on)
                return;
            _flashlight.enabled = on;
            if (on)
                LightingDirector.DynamicLights.Add(_flashlight);
            else
                LightingDirector.DynamicLights.Remove(_flashlight);
        }

        // ── Визуал ──

        private void CreateVisuals()
        {
            var outline = new GameObject("BlockOutline");
            outline.AddComponent<MeshFilter>().sharedMesh = BuildWireCube();
            outline.AddComponent<MeshRenderer>().sharedMaterial = _outlineMaterial;
            _outline = outline.transform;

            _cracks = CreatePrimitive(PrimitiveType.Cube, "BlockCracks", _crackInstance = new Material(_crackMaterial));
            _brush = CreatePrimitive(PrimitiveType.Sphere, "DrillBrush", _brushMaterial);
        }

        private static Transform CreatePrimitive(PrimitiveType type, string name, Material material)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            Destroy(go.GetComponent<Collider>());
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.SetActive(false);
            return go.transform;
        }

        private void UpdateVisuals()
        {
            var item = ActiveItem;
            bool drill = item != null && item.Tool == ToolKind.Drill;
            bool showOutline = !drill && _hasTarget && !InputBlocked;
            _outline.gameObject.SetActive(showOutline);
            _brush.gameObject.SetActive(drill && _hasDrillTarget && !InputBlocked);
            if (drill && _hasDrillTarget)
            {
                bool adding = _secondary.IsPressed() && !_primary.IsPressed();
                _brush.position = (Vector3)BrushCenter(adding);
                _brush.localScale = Vector3.one * (DrillRadius.Value * 2f);
            }

            if (!showOutline)
            {
                _cracks.gameObject.SetActive(false);
                return;
            }
            Vector3 center = new Vector3(_target.Cell.x, _target.Cell.y, _target.Cell.z) + Vector3.one * 0.5f;
            _outline.position = center;
            float damage = DamageFraction();
            _cracks.gameObject.SetActive(damage > 0f);
            if (damage > 0f)
            {
                _cracks.position = center;
                _cracks.localScale = Vector3.one * 1.004f;
                _crackInstance.SetFloat(DamageId, damage);
            }
        }

        private float DamageFraction()
        {
            int hardness = World.GetInfo(_target.Value.Material).Hardness;
            return hardness > 0 ? Mathf.Clamp01(World.GetDamage(_target.Cell) / (float)hardness) : 0f;
        }

        private static Mesh BuildWireCube()
        {
            const float h = 0.502f;
            var corners = new Vector3[8];
            for (int i = 0; i < 8; i++)
                corners[i] = new Vector3((i & 1) == 0 ? -h : h, (i & 2) == 0 ? -h : h, (i & 4) == 0 ? -h : h);
            int[] edges = { 0, 1, 2, 3, 4, 5, 6, 7, 0, 2, 1, 3, 4, 6, 5, 7, 0, 4, 1, 5, 2, 6, 3, 7 };
            var mesh = new Mesh { name = "WireCube" };
            mesh.SetVertices(corners);
            mesh.SetIndices(edges, MeshTopology.Lines, 0);
            return mesh;
        }

        // ── HUD ──

        private string _message;
        private float _messageTime;

        private void CreateHud()
        {
            if (GameBootstrap.Config == null || GameBootstrap.Config.PanelSettings == null)
                return;
            var go = new GameObject("ToolHud");
            go.SetActive(false);
            go.transform.SetParent(transform, false);
            var document = go.AddComponent<UIDocument>();
            document.panelSettings = GameBootstrap.Config.PanelSettings;
            document.sortingOrder = 5;
            go.SetActive(true);

            var root = document.rootVisualElement;
            root.pickingMode = PickingMode.Ignore;
            root.style.position = Position.Absolute;
            root.style.left = root.style.right = root.style.top = root.style.bottom = 0f;

            var crosshair = new VisualElement { pickingMode = PickingMode.Ignore };
            crosshair.style.position = Position.Absolute;
            crosshair.style.left = Length.Percent(50);
            crosshair.style.top = Length.Percent(50);
            crosshair.style.width = crosshair.style.height = 4f;
            crosshair.style.marginLeft = crosshair.style.marginTop = -2f;
            crosshair.style.backgroundColor = new Color(1f, 1f, 1f, 0.85f);
            crosshair.style.borderBottomLeftRadius = crosshair.style.borderBottomRightRadius =
                crosshair.style.borderTopLeftRadius = crosshair.style.borderTopRightRadius = 2f;
            root.Add(crosshair);

            _info = new Label { pickingMode = PickingMode.Ignore, enableRichText = false };
            _info.style.position = Position.Absolute;
            _info.style.bottom = 96f;
            _info.style.left = 0f;
            _info.style.right = 0f;
            _info.style.unityTextAlign = TextAnchor.LowerCenter;
            _info.style.color = new Color(1f, 1f, 1f, 0.9f);
            _info.style.fontSize = 14f;
            root.Add(_info);
        }

        private void UpdateHud()
        {
            if (_info == null)
                return;
            _messageTime -= Time.deltaTime;
            var item = ActiveItem;
            int total = item == null ? 0 : Inventory.CountOf(Inventory.ActiveStack.Def);
            string hand = item == null ? "Руки" : $"{item.DisplayName}{(total > 1 ? $" ×{total}" : "")}";
            string action = item == null ? "ЛКМ — ударить"
                : item.Tool == ToolKind.Drill ? "ЛКМ — срезать, ПКМ — нарастить"
                : item.Tool == ToolKind.Flashlight ? $"ЛКМ — {(_flashlightOn ? "выключить" : "включить")}"
                : item.PlacesBlock != null ? "ПКМ — поставить, ЛКМ — ударить"
                : item.IsConsumable ? "ЛКМ — использовать"
                : item.IsEquippable ? "ЛКМ — надеть"
                : item.IsTool ? "ЛКМ — бить" + (item.Tool == ToolKind.Hammer ? ", ПКМ с пропом в руках — прибить" : "")
                : "ЛКМ — ударить";
            string target = _propTarget != null && PropManager.Instance != null
                ? $"{_propTarget.Def.DisplayName}  {(1f - PropManager.Instance.Health(_propTarget)) * 100f:0}%"
                : _hasTarget
                ? $"{World.Materials.Get(_target.Value.Material).DisplayName}  {DamageFraction() * 100f:0}%"
                : "—";
            string creative = ItemSystem.Creative.Value ? "   [творчество]" : "";
            string text = _messageTime > 0f && !string.IsNullOrEmpty(_message)
                ? _message
                : $"{hand} · {action}{creative}\nЦель: {target}";
            if (text != _lastInfo)
            {
                _info.text = text;
                _lastInfo = text;
            }
        }
    }
}
