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
    /// Временный «молоток» этапа 2 (до инвентаря и инструментов): ЛКМ удержанием бьёт блок, ПКМ ставит,
    /// колесо выбирает материал, СКМ берёт материал блока под прицелом. Все правки — командами через сессию.
    /// </summary>
    public sealed class DebugBlockTool : MonoBehaviour
    {
        public static readonly CVar<int> ToolDamage = new("tool_damage", 100, "Урон временного молотка за удар.");

        [SerializeField] private float _reach = 5f;
        [SerializeField] private float _swingInterval = 0.3f;
        [SerializeField] private Material _outlineMaterial;
        [SerializeField] private Material _crackMaterial;

        private static readonly int DamageId = Shader.PropertyToID("_Damage");

        private readonly List<ushort> _placeable = new();
        private Camera _camera;
        private InputAction _primary;
        private InputAction _secondary;
        private InputAction _pick;
        private InputAction _scroll;
        private int _selected;
        private float _swingTimer;
        private bool _hasTarget;
        private VoxelHit _target;

        private Transform _outline;
        private Transform _cracks;
        private Material _crackInstance;
        private Label _info;
        private string _lastInfo;

        private ChunkManager World => ChunkManager.Instance;

        private void Start()
        {
            _camera = GetComponentInChildren<Camera>();
            if (World == null || _camera == null)
            {
                enabled = false;
                return;
            }

            foreach (var def in World.Materials.All)
                if (def.Kind == MaterialKind.Block && def.Placeable)
                    _placeable.Add((ushort)def.RuntimeIndex);

            if (Services.TryGet(out InputService input))
            {
                const string map = InputService.Maps.Player;
                _primary = input.Action(map, "Primary");
                _secondary = input.Action(map, "Secondary");
                _pick = input.Action(map, "Pick");
                _scroll = input.Action(map, "Scroll");
            }

            CreateVisuals();
            CreateHud();
        }

        private void OnDestroy()
        {
            if (_outline != null) Destroy(_outline.gameObject);
            if (_cracks != null) Destroy(_cracks.gameObject);
            if (_crackInstance != null) Destroy(_crackInstance);
        }

        private void Update()
        {
            if (World == null || _primary == null)
                return;

            var ray = _camera.transform;
            _hasTarget = World.Raycast(ray.position, ray.forward, _reach, out _target);

            HandleSelection();
            HandleBreaking();
            HandlePlacing();
            UpdateVisuals();
            UpdateHud();
        }

        private void HandleSelection()
        {
            // WasPressed — фронт: одна ступень на щелчок колеса или нажатие крестовины (а не каждый кадр удержания).
            if (_placeable.Count > 0 && _scroll.WasPressedThisFrame())
            {
                float scroll = _scroll.ReadValue<float>();
                _selected = (_selected + (scroll > 0 ? -1 : 1) + _placeable.Count) % _placeable.Count;
            }

            if (_pick.WasPressedThisFrame() && _hasTarget)
            {
                int index = _placeable.IndexOf(_target.Value.Material);
                if (index >= 0)
                    _selected = index;
            }
        }

        private void HandleBreaking()
        {
            if (!_primary.IsPressed())
            {
                _swingTimer = 0f;
                return;
            }
            _swingTimer -= Time.deltaTime;
            if (_swingTimer > 0f || !_hasTarget)
                return;
            _swingTimer = _swingInterval;
            Send(new DamageBlockCommand { Position = _target.Cell, Amount = ToolDamage.Value });
        }

        private void HandlePlacing()
        {
            if (!_secondary.WasPressedThisFrame() || !_hasTarget || _placeable.Count == 0 || math.all(_target.Normal == 0))
                return;
            Send(new PlaceBlockCommand { Position = _target.Cell + _target.Normal, Material = _placeable[_selected] });
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

            var cracks = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cracks.name = "BlockCracks";
            Destroy(cracks.GetComponent<Collider>());
            _crackInstance = new Material(_crackMaterial);
            var renderer = cracks.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = _crackInstance;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _cracks = cracks.transform;
        }

        private void UpdateVisuals()
        {
            _outline.gameObject.SetActive(_hasTarget);
            if (!_hasTarget)
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
            int hardness = World.Materials.Infos[_target.Value.Material].Hardness;
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
            string selected = _placeable.Count > 0 ? World.Materials.Get(_placeable[_selected]).DisplayName : "—";
            string target = _hasTarget
                ? $"{World.Materials.Get(_target.Value.Material).DisplayName}  {DamageFraction() * 100f:0}%"
                : "—";
            int3 chunk = WorldConstants.CellToChunk((int3)math.floor((float3)transform.position));
            string text = $"Блок: {selected}   (колесо — сменить, СКМ — взять, ЛКМ — ломать, ПКМ — ставить)\n" +
                          $"Цель: {target}   ·   чанк {chunk.x},{chunk.y},{chunk.z}   ·   колонн {World.LoadedColumns}";
            if (text != _lastInfo)
            {
                _info.text = text;
                _lastInfo = text;
            }
        }
    }
}
