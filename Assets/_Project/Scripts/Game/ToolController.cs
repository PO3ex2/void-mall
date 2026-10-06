using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using VoidMall.Console;
using VoidMall.Core;
using VoidMall.Input;
using VoidMall.Items;
using VoidMall.Net;
using VoidMall.Skills;
using VoidMall.Survival;
using Random = UnityEngine.Random;
using VoidMall.World;

namespace VoidMall.Game
{
    /// <summary>
    /// Действия предметом в руке (выбранным в хотбаре: 1–9, колесо):
    /// руки — слабый удар; молоток, кувалда, лом — урон по блокам и пропам по своим характеристикам;
    /// бур — срезать (ЛКМ) и наращивать (ПКМ, тратит грунт или бетонную крошку) гладкий массив;
    /// блок — ставится ПКМ (тратится); постройки (станции, мебель, баррикады, спальник) — призрак: R — повернуть, ПКМ — поставить; факел — ЛКМ; еда и медикаменты — ЛКМ, применить; одежда — ЛКМ, надеть; фонарик — ЛКМ, вкл/выкл.
    /// Инструменты изнашиваются (ItemDef.Uses) и ломаются на нуле; «Разрушитель» бьёт сильнее.
    /// Голыми руками по стеклу и острому металлу можно порезаться (перчатки защищают), сломанная рука бьёт медленнее.
    /// Все правки мира — командами через сессию (готово к коопу). В творческом режиме блоки и материалы не тратятся.
    /// </summary>
    public sealed partial class ToolController : MonoBehaviour
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

        private SwingTimer _swing;
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
        private Light _torch;
        private bool _torchOn;
        private float _torchFlicker;

        // Призрак постройки.
        private InputAction _rotate;
        private Material _ghostMaterial;
        private Mesh _ghostMesh;
        private Matrix4x4 _ghostMatrix;
        private bool _ghostVisible, _ghostValid;
        private float _ghostYaw = float.NaN;
        private Vector3 _ghostPosition;
        private Quaternion _ghostRotation;
        private ushort _ghostProp;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

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
                _rotate = input.Action(map, "Reload");
                for (int i = 0; i < Hotbar.Size; i++)
                    _slots[i] = input.Action(map, $"Slot{i + 1}");
            }
            CreateVisuals();
            CreateFlashlight();
            CreateTorch();
            if (_brushMaterial != null)
                _ghostMaterial = new Material(_brushMaterial) { name = "BuildGhost" };
            CreateBuildMaterials();
            CreateHud();
        }

        private void OnDestroy()
        {
            if (_outline != null) Destroy(_outline.gameObject);
            if (_cracks != null) Destroy(_cracks.gameObject);
            if (_brush != null) Destroy(_brush.gameObject);
            if (_crackInstance != null) Destroy(_crackInstance);
            if (_torch != null)
                LightingDirector.DynamicLights.Remove(_torch);
            if (_ghostMaterial != null) Destroy(_ghostMaterial);
            DestroyBuildMaterials();
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

            _ghostVisible = false;
            _fblockVisible = false;
            FindGhostTarget();
            if (!InputBlocked)
            {
                HandleSelection();
                HandleActions();
            }
            UpdateFlashlight();
            UpdateTorch();
            DrawGhost();
            DrawBuildGhosts();
            UpdateVisuals();
            UpdateHud();
        }

        private void HandleSelection()
        {
            var hotbar = Inventory.Hotbar;
            for (int i = 0; i < Hotbar.Size; i++)
                if (_slots[i] != null && _slots[i].WasPressedThisFrame())
                    hotbar.Select(i);
            if (_scroll != null && _scroll.WasPressedThisFrame() && !TemplateScrollActive())
                hotbar.Select(hotbar.Selected + (_scroll.ReadValue<float>() > 0 ? -1 : 1));
        }

        private void HandleActions()
        {
            var item = ActiveItem;
            if (item != null && item.Tool == ToolKind.Bucket)
            {
                HandleBucket();
                return;
            }
            if (item != null && item.Tool == ToolKind.Drill)
            {
                HandleDrilling(item);
                return;
            }
            if (HandleTemplate(item))
                return;
            if (item != null && item.PlacesBlock != null && !HandleBlockItem(item))
                HandlePlacing(item);
            if (item != null && !string.IsNullOrEmpty(item.PlacesProp))
                HandleBuilding(item);
            else
                _ghostYaw = float.NaN;
            if (item != null && _primary.WasPressedThisFrame())
            {
                if (item.Tool == ToolKind.Flashlight)
                {
                    _flashlightOn = !_flashlightOn;
                    return;
                }
                if (item.Tool == ToolKind.Torch)
                {
                    _torchOn = !_torchOn;
                    return;
                }
                if (item.IsConsumable || item.IsEquippable || item.IsReadable)
                {
                    var stack = Inventory.ActiveStack;
                    int slot = Inventory.Hotbar.Selected;
                    if (!Inventory.Use(stack) && !string.IsNullOrEmpty(Inventory.LastRefusal))
                        Say(Inventory.LastRefusal);
                    Inventory.RefillHotbar(slot, stack.Def);
                    return;
                }
            }
            HandleStriking(item);
        }

        /// <summary>Удар ЛКМ (удержание): по пропу или блоку, сила — от инструмента (или руки).</summary>
        private void HandleStriking(ItemDef item)
        {
            bool striker = item != null && (item.Tool == ToolKind.Hammer || item.Tool == ToolKind.Sledgehammer || item.Tool == ToolKind.Crowbar);
            float interval = striker ? item.SwingInterval : 0.45f;
            var vitals = PlayerVitals.Local;
            if (vitals != null)
                interval *= vitals.Model.SwingMultiplier;
            // Перезарядка не сбрасывается при отпускании кнопки: частые клики не бьют быстрее удержания.
            bool hasVictim = (_propTarget != null && PropManager.Instance != null) || _hasTarget;
            if (!_swing.Tick(_primary.IsPressed() && hasVictim, Time.deltaTime, interval))
                return;
            float power = 1f + 0.15f * PlayerSkills.LevelOf(SkillId.Wrecker);
            if (_propTarget != null && PropManager.Instance != null)
            {
                PropManager.Instance.Damage(_propTarget, Mathf.RoundToInt((striker ? item.PropDamage : HandDamage.Value) * power));
                if (striker)
                    WearActive();
                return;
            }
            if (item == null)
                TryCutHands(_target.Value.Material);
            Send(new DamageBlockCommand { Position = _target.Cell, Amount = Mathf.RoundToInt((striker ? item.BlockDamage : HandDamage.Value) * power) });
            if (striker)
                WearActive();
        }

        /// <summary>Стекло и острый металл голыми руками: шанс порезаться, если не надеты перчатки.</summary>
        private void TryCutHands(ushort material)
        {
            var vitals = PlayerVitals.Local;
            if (vitals == null || Inventory.Equipment.Get(EquipSlot.Arms, EquipLayer.Clothing) != null || Inventory.Equipment.Get(EquipSlot.Arms, EquipLayer.Armor) != null)
                return;
            string id = World.Materials.Get(material).Id;
            bool glass = id == "block.glass" || id == "block.skylight";
            bool sharp = id == "block.metal" || id == "block.shutter" || id == "block.duct";
            if ((glass && Random.value < 0.5f) || (sharp && Random.value < 0.2f))
            {
                vitals.Cut(glass ? Random.Range(4f, 9f) : Random.Range(2f, 5f));
                Say(glass ? "Порезались о стекло — наденьте перчатки" : "Порезались об острый край — наденьте перчатки");
            }
        }

        // ── Стройка: призрак-предпросмотр ──

        /// <summary>
        /// Предмет-постройка в руке (станция, мебель, баррикада, спальник): полупрозрачный проп на полу под прицелом —
        /// зелёный, если помещается, красный — если мешает. R — повернуть на 45°, ПКМ — поставить.
        /// </summary>
        private void HandleBuilding(ItemDef item)
        {
            var props = PropManager.Instance;
            if (props == null || !props.Props.TryGetIndex(item.PlacesProp, out ushort propIndex))
                return;
            var view = _camera.transform;
            if (float.IsNaN(_ghostYaw))
                _ghostYaw = Mathf.Round(view.eulerAngles.y / 45f) * 45f + 180f; // лицом к игроку
            if (_rotate != null && _rotate.WasPressedThisFrame())
                _ghostYaw += 45f;
            if (!Physics.Raycast(view.position, view.forward, out var hit, _reach + 1f, WorldMask | PropMask, QueryTriggerInteraction.Ignore) || hit.normal.y < 0.7f)
            {
                if (_secondary.WasPressedThisFrame())
                    Say("Поставить можно только на ровную поверхность");
                return;
            }
            Vector3 size = props.Props.Size(propIndex);
            _ghostProp = propIndex;
            _ghostPosition = hit.point + Vector3.up * 0.02f;
            _ghostRotation = Quaternion.Euler(0f, _ghostYaw, 0f);
            Vector3 center = _ghostPosition + Vector3.up * (size.y * 0.5f + 0.03f);
            _ghostValid = !Physics.CheckBox(center, size * 0.47f, _ghostRotation,
                GameLayers.PlayerCollisionMask | PropMask | (1 << GameLayers.Player), QueryTriggerInteraction.Ignore);
            _ghostMesh = props.Props.GetMesh(propIndex);
            _ghostMatrix = Matrix4x4.TRS(_ghostPosition, _ghostRotation, Vector3.one);
            _ghostVisible = true;

            if (!_secondary.WasPressedThisFrame())
                return;
            if (!_ghostValid)
            {
                Say("Не помещается: мешает стена, игрок или предмет");
                return;
            }
            var stack = Inventory.ActiveStack;
            int slot = Inventory.Hotbar.Selected;
            if (!Inventory.TryConsume(stack.Def, 1))
                return;
            Inventory.RefillHotbar(slot, stack.Def);
            props.SpawnNew(_ghostProp, _ghostPosition, _ghostRotation);
            PlayerSkills.AwardLocal(Aspect.Construction, 1f);
            var def = props.Props.Get(_ghostProp);
            Say(def.Sleepable ? $"Поставлено: {def.DisplayName} · E — спать"
                : def.Station != CraftStation.None ? $"Поставлено: {def.DisplayName} · E — открыть"
                : $"Поставлено: {def.DisplayName}");
        }

        private void DrawGhost()
        {
            if (!_ghostVisible || InputBlocked || _ghostMesh == null || _ghostMaterial == null)
                return;
            _ghostMaterial.SetColor(BaseColorId, _ghostValid ? new Color(0.35f, 1f, 0.45f, 0.35f) : new Color(1f, 0.3f, 0.3f, 0.35f));
            var rp = new RenderParams(_ghostMaterial) { shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off, receiveShadows = false };
            Graphics.RenderMesh(rp, _ghostMesh, 0, _ghostMatrix);
        }

        /// <summary>Износ инструмента в руке; сломался — сообщение.</summary>
        private void WearActive()
        {
            var stack = Inventory.ActiveStack;
            if (stack == null)
                return;
            var def = Inventory.Items.Get(stack.Def);
            if (Inventory.Wear(stack))
                Say($"Сломалось: {def.DisplayName}");
        }

        // ── Факел ──

        private void CreateTorch()
        {
            var go = new GameObject("Torch");
            go.transform.SetParent(_camera.transform, false);
            go.transform.localPosition = new Vector3(0.3f, -0.15f, 0.35f);
            _torch = go.AddComponent<Light>();
            _torch.type = LightType.Point;
            _torch.range = 9f;
            _torch.intensity = 2.2f;
            _torch.color = new Color(1f, 0.62f, 0.3f);
            _torch.shadows = LightShadows.None;
            _torch.enabled = false;
        }

        /// <summary>Факел горит, только пока в руке, и сгорает (износ — секунды горения).</summary>
        private void UpdateTorch()
        {
            var item = ActiveItem;
            bool inHand = item != null && item.Tool == ToolKind.Torch;
            if (!inHand)
                _torchOn = false;
            bool on = _torchOn && inHand;
            if (on)
            {
                _torchFlicker += Time.deltaTime * 9f;
                _torch.intensity = 2.2f + Mathf.PerlinNoise(_torchFlicker, 0.3f) * 0.8f;
                var stack = Inventory.ActiveStack;
                if (stack != null && Inventory.Wear(stack, Time.deltaTime))
                {
                    Say("Факел догорел");
                    _torchOn = on = false;
                }
            }
            if (_torch.enabled == on)
                return;
            _torch.enabled = on;
            if (on)
                LightingDirector.DynamicLights.Add(_torch);
            else
                LightingDirector.DynamicLights.Remove(_torch);
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
            Inventory.GiveLeftover(item);
            PlayerSkills.AwardLocal(Aspect.Construction, 0.15f);
        }

        /// <summary>Пустое ведро: ПКМ по воде — набрать (блок воды исчезает, воду водного чанка не убрать).</summary>
        private void HandleBucket()
        {
            if (!_secondary.WasPressedThisFrame())
                return;
            var view = _camera.transform;
            if (!World.RaycastLiquid(view.position, view.forward, _reach, out var water))
            {
                Say("Тут нет воды");
                return;
            }
            if (!ItemSystem.Instance.Items.TryGetIndex("item.bucket_water", out ushort full))
                return;
            var stack = Inventory.ActiveStack;
            int slot = Inventory.Hotbar.Selected;
            if (!Inventory.TryConsume(stack.Def, 1))
                return;
            Inventory.RefillHotbar(slot, stack.Def);
            Inventory.Add(new ItemStack(full, 1));
            Send(new DamageBlockCommand { Position = water.Cell, Amount = 1 });
            Say("Набрали воды");
        }

        private void HandleDrilling(ItemDef drill)
        {
            bool dig = _primary.IsPressed();
            bool add = _secondary.IsPressed() && !dig;
            if (!_swing.Tick((dig || add) && _hasDrillTarget, Time.deltaTime, drill.SwingInterval))
                return;

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
            WearActive();
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
            bool building = item != null && !string.IsNullOrEmpty(item.PlacesProp);
            bool showOutline = !drill && !building && _hasTarget && !InputBlocked;
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
            // Функциональный блок (дверь, ворота) — рамка и трещины на весь блок, урон копится в его якоре.
            TargetBounds(out Vector3 center, out Vector3 size, out int3 damageCell);
            _outline.position = center;
            _outline.localScale = size;
            float damage = DamageFraction(damageCell);
            _cracks.gameObject.SetActive(damage > 0f);
            if (damage > 0f)
            {
                _cracks.position = center;
                _cracks.localScale = size * 1.004f;
                _crackInstance.SetFloat(DamageId, damage);
            }
        }

        private static readonly List<int3> FootprintCells = new();

        private void TargetBounds(out Vector3 center, out Vector3 size, out int3 damageCell)
        {
            damageCell = _target.Cell;
            center = new Vector3(_target.Cell.x, _target.Cell.y, _target.Cell.z) + Vector3.one * 0.5f;
            size = Vector3.one;
            var info = World.GetInfo(_target.Value.Material);
            if (info.Kind != MaterialKind.Functional)
                return;
            int3 anchor = FunctionalCells.AnchorOf(_target.Cell, _target.Value);
            damageCell = anchor;
            FunctionalCells.Footprint(anchor, info.FunctionalSize, FunctionalCells.Yaw(World.GetCell(anchor).Meta), FootprintCells);
            int3 min = anchor, max = anchor;
            foreach (var cell in FootprintCells)
            {
                min = math.min(min, cell);
                max = math.max(max, cell);
            }
            center = ((float3)min + (float3)(max + 1)) * 0.5f;
            size = (float3)(max + 1 - min);
        }

        private float TargetDamage()
        {
            TargetBounds(out _, out _, out int3 cell);
            return DamageFraction(cell);
        }

        private float DamageFraction(int3 cell)
        {
            int hardness = World.GetInfo(_target.Value.Material).Hardness;
            return hardness > 0 ? Mathf.Clamp01(World.GetDamage(cell) / (float)hardness) : 0f;
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

        private void Say(string message)
        {
            _message = message;
            _messageTime = 2f;
        }

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
            var activeStack = Inventory.ActiveStack;
            string wear = item != null && item.HasWear && activeStack != null ? $" ({activeStack.Durability * 100f:0}%)" : "";
            string hand = item == null ? "Руки" : $"{item.DisplayName}{(total > 1 ? $" ×{total}" : "")}{wear}";
            string action = item != null && BuildHint(item) is { } build ? build
                : item == null ? "ЛКМ — ударить"
                : item.Tool == ToolKind.Drill ? "ЛКМ — срезать, ПКМ — нарастить"
                : item.Tool == ToolKind.Flashlight ? $"ЛКМ — {(_flashlightOn ? "выключить" : "включить")}"
                : item.Tool == ToolKind.Torch ? $"ЛКМ — {(_torchOn ? "потушить" : "зажечь")}"
                : item.Tool == ToolKind.Bucket ? "ПКМ по воде — набрать"
                : item.IsReadable ? "ЛКМ — изучить чертёж"
                : item.PlacesBlock != null && item.IsConsumable ? "ЛКМ — выпить, ПКМ — вылить"
                : item.PlacesBlock != null ? "ПКМ — поставить, ЛКМ — ударить"
                : !string.IsNullOrEmpty(item.PlacesProp) ? (_ghostVisible && !_ghostValid ? "Не помещается" : "ПКМ — поставить, R — повернуть")
                : item.IsConsumable ? "ЛКМ — использовать"
                : item.IsEquippable ? "ЛКМ — надеть"
                : item.IsTool ? "ЛКМ — бить" + (item.Tool == ToolKind.Hammer ? ", ПКМ с пропом в руках — прибить" : "")
                : "ЛКМ — ударить";
            string target = _propTarget != null && PropManager.Instance != null
                ? $"{_propTarget.Def.DisplayName}  {(1f - PropManager.Instance.Health(_propTarget)) * 100f:0}%"
                : _hasTarget
                ? $"{World.Materials.Get(_target.Value.Material).DisplayName}  {TargetDamage() * 100f:0}%"
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
