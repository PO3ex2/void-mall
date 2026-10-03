using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using VoidMall.Console;
using VoidMall.Core;
using VoidMall.Input;
using VoidMall.Net;
using VoidMall.World;

namespace VoidMall.Game
{
    /// <summary>
    /// Временные инструменты этапов 2–3 (до инвентаря и настоящих предметов), переключение клавишами 1/2.
    /// Молоток: ЛКМ удержанием бьёт блок, ПКМ ставит, колесо выбирает блок, СКМ берёт блок под прицелом.
    /// Бур: ЛКМ удержанием срезает гладкий массив, ПКМ наращивает, колесо выбирает материал.
    /// Все правки — командами через сессию (готово к коопу).
    /// </summary>
    public sealed class DebugBlockTool : MonoBehaviour
    {
        public static readonly CVar<int> ToolDamage = new("tool_damage", 100, "Урон временного молотка за удар.");
        public static readonly CVar<float> DrillRadius = new("drill_radius", 1.4f, "Радиус кисти бура, м.");
        public static readonly CVar<float> DrillStrength = new("drill_strength", 0.25f, "Сила бура за одно применение (0…1).");

        private enum ToolMode { Hammer, Drill }

        [SerializeField] private float _reach = 5f;
        [SerializeField] private float _swingInterval = 0.3f;
        [SerializeField] private float _drillInterval = 0.08f;
        [SerializeField] private Material _outlineMaterial;
        [SerializeField] private Material _crackMaterial;
        [SerializeField] private Material _brushMaterial;

        private static readonly int DamageId = Shader.PropertyToID("_Damage");
        private const int WorldMask = 1 << GameLayers.World;
        private const int PropMask = 1 << GameLayers.Props;

        private readonly List<ushort> _blocks = new();
        private readonly List<ushort> _smoothMaterials = new();
        private Camera _camera;
        private InputAction _primary;
        private InputAction _secondary;
        private InputAction _pick;
        private InputAction _scroll;
        private InputAction _slot1;
        private InputAction _slot2;

        private ToolMode _mode;
        private int _selectedBlock;
        private int _selectedSmooth;
        private float _actionTimer;

        private bool _hasTarget;
        private VoxelHit _target;
        private bool _hasDrillTarget;
        private RaycastHit _drillHit;

        private Transform _outline;
        private Transform _cracks;
        private Transform _brush;
        private Material _crackInstance;
        private Label _info;
        private string _lastInfo;

        private ChunkManager World => ChunkManager.Instance;

        /// <summary>Ввод занят другим (игрок держит проп, разбирает его): инструмент не реагирует на кнопки.</summary>
        public bool InputBlocked { get; set; }

        public bool HammerActive => _mode == ToolMode.Hammer;

        private PropBody _propTarget;

        private void Start()
        {
            _camera = GetComponentInChildren<Camera>();
            if (World == null || _camera == null)
            {
                enabled = false;
                return;
            }

            foreach (var def in World.Materials.All)
            {
                if (def.Kind == MaterialKind.Block && def.Placeable)
                    _blocks.Add((ushort)def.RuntimeIndex);
                else if (def.Kind == MaterialKind.Smooth && def.Placeable)
                    _smoothMaterials.Add((ushort)def.RuntimeIndex);
            }

            if (Services.TryGet(out InputService input))
            {
                const string map = InputService.Maps.Player;
                _primary = input.Action(map, "Primary");
                _secondary = input.Action(map, "Secondary");
                _pick = input.Action(map, "Pick");
                _scroll = input.Action(map, "Scroll");
                _slot1 = input.Action(map, "Slot1");
                _slot2 = input.Action(map, "Slot2");
            }

            CreateVisuals();
            CreateHud();
        }

        private void OnDestroy()
        {
            if (_outline != null) Destroy(_outline.gameObject);
            if (_cracks != null) Destroy(_cracks.gameObject);
            if (_brush != null) Destroy(_brush.gameObject);
            if (_crackInstance != null) Destroy(_crackInstance);
        }

        private void Update()
        {
            if (World == null || _primary == null)
                return;

            if (_slot1.WasPressedThisFrame())
                SetMode(ToolMode.Hammer);
            if (_slot2.WasPressedThisFrame())
                SetMode(ToolMode.Drill);

            var view = _camera.transform;
            _hasTarget = World.Raycast(view.position, view.forward, _reach, out _target);
            _hasDrillTarget = Physics.Raycast(view.position, view.forward, out _drillHit, _reach, WorldMask, QueryTriggerInteraction.Ignore);

            // Проп ближе блока — молоток бьёт по пропу.
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
                if (_mode == ToolMode.Hammer)
                {
                    HandleBreaking();
                    HandlePlacing();
                }
                else
                {
                    HandleDrilling();
                }
            }
            UpdateVisuals();
            UpdateHud();
        }

        private void SetMode(ToolMode mode)
        {
            _mode = mode;
            _actionTimer = 0f;
        }

        private void HandleSelection()
        {
            // WasPressed — фронт: одна ступень на щелчок колеса или нажатие крестовины.
            if (_scroll.WasPressedThisFrame())
            {
                int step = _scroll.ReadValue<float>() > 0 ? -1 : 1;
                if (_mode == ToolMode.Hammer && _blocks.Count > 0)
                    _selectedBlock = (_selectedBlock + step + _blocks.Count) % _blocks.Count;
                else if (_mode == ToolMode.Drill && _smoothMaterials.Count > 0)
                    _selectedSmooth = (_selectedSmooth + step + _smoothMaterials.Count) % _smoothMaterials.Count;
            }

            if (_pick.WasPressedThisFrame() && _hasTarget)
            {
                int block = _blocks.IndexOf(_target.Value.Material);
                int smooth = _smoothMaterials.IndexOf(_target.Value.Material);
                if (block >= 0)
                {
                    _selectedBlock = block;
                    SetMode(ToolMode.Hammer);
                }
                else if (smooth >= 0)
                {
                    _selectedSmooth = smooth;
                    SetMode(ToolMode.Drill);
                }
            }
        }

        private void HandleBreaking()
        {
            if (!_primary.IsPressed())
            {
                _actionTimer = 0f;
                return;
            }
            _actionTimer -= Time.deltaTime;
            if (_actionTimer > 0f)
                return;
            if (_propTarget != null && PropManager.Instance != null)
            {
                _actionTimer = _swingInterval;
                PropManager.Instance.Damage(_propTarget, ToolDamage.Value);
                return;
            }
            if (!_hasTarget)
                return;
            _actionTimer = _swingInterval;
            Send(new DamageBlockCommand { Position = _target.Cell, Amount = ToolDamage.Value });
        }

        private void HandlePlacing()
        {
            if (!_secondary.WasPressedThisFrame() || !_hasTarget || _blocks.Count == 0 || math.all(_target.Normal == 0))
                return;
            Send(new PlaceBlockCommand { Position = _target.Cell + _target.Normal, Material = _blocks[_selectedBlock] });
        }

        private void HandleDrilling()
        {
            bool dig = _primary.IsPressed();
            bool add = _secondary.IsPressed();
            if (!dig && !add)
            {
                _actionTimer = 0f;
                return;
            }
            _actionTimer -= Time.deltaTime;
            if (_actionTimer > 0f || !_hasDrillTarget)
                return;
            _actionTimer = _drillInterval;
            Send(new SmoothBrushCommand
            {
                Center = BrushCenter(add),
                Radius = DrillRadius.Value,
                Strength = DrillStrength.Value,
                Add = add && !dig,
                Material = _smoothMaterials.Count > 0 ? _smoothMaterials[_selectedSmooth] : (ushort)0,
            });
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
            bool hammer = _mode == ToolMode.Hammer;
            _outline.gameObject.SetActive(hammer && _hasTarget);
            _brush.gameObject.SetActive(!hammer && _hasDrillTarget);
            if (!hammer && _hasDrillTarget)
            {
                bool adding = _secondary.IsPressed() && !_primary.IsPressed();
                _brush.position = (Vector3)BrushCenter(adding);
                _brush.localScale = Vector3.one * (DrillRadius.Value * 2f);
            }

            if (!hammer || !_hasTarget)
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

        private void CreateHud()
        {
            if (GameBootstrap.Config == null || GameBootstrap.Config.PanelSettings == null)
                return;
            var go = new GameObject("BlockToolHud");
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
            _info.style.bottom = 24f;
            _info.style.left = 0f;
            _info.style.right = 0f;
            _info.style.unityTextAlign = TextAnchor.LowerCenter;
            _info.style.color = new Color(1f, 1f, 1f, 0.9f);
            _info.style.fontSize = 15f;
            root.Add(_info);
        }

        private void UpdateHud()
        {
            if (_info == null)
                return;
            string tool = _mode == ToolMode.Hammer
                ? $"[1] МОЛОТОК · [2] бур   Блок: {Name(_blocks, _selectedBlock)}   (ЛКМ — ломать, ПКМ — ставить, колесо — блок, СКМ — взять)"
                : $"[1] молоток · [2] БУР   Материал: {Name(_smoothMaterials, _selectedSmooth)}   (ЛКМ — срезать, ПКМ — нарастить, колесо — материал)";
            string target = _propTarget != null && PropManager.Instance != null
                ? $"{_propTarget.Def.DisplayName}  {(1f - PropManager.Instance.Health(_propTarget)) * 100f:0}%"
                : _hasTarget
                ? $"{World.Materials.Get(_target.Value.Material).DisplayName}" + (_mode == ToolMode.Hammer ? $"  {DamageFraction() * 100f:0}%" : "")
                : "—";
            int3 chunk = WorldConstants.CellToChunk((int3)math.floor((float3)transform.position));
            string text = $"{tool}\nЦель: {target}   ·   чанк {chunk.x},{chunk.y},{chunk.z}   ·   колонн {World.LoadedColumns}";
            if (text != _lastInfo)
            {
                _info.text = text;
                _lastInfo = text;
            }
        }

        private string Name(List<ushort> list, int index) => list.Count > 0 ? World.Materials.Get(list[index]).DisplayName : "—";
    }
}
