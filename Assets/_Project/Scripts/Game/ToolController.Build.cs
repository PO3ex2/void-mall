using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;
using VoidMall.Items;
using VoidMall.Skills;
using VoidMall.World;

namespace VoidMall.Game
{
    /// <summary>Призраки форм: где игрок поставил шаблон (форма + ориентация), ждут заполнения блоком. До сохранений — в сессии.</summary>
    public static class ShapeGhosts
    {
        public struct Ghost
        {
            public BlockShape Shape;
            public byte Meta;
        }

        public static readonly Dictionary<int3, Ghost> All = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => All.Clear();
    }

    /// <summary>
    /// Стройка формами и функциональными блоками (ARCHITECTURE §4.9):
    /// шаблон — Shift+колесо форма, R поворот, Shift+R наклон (верх формы в одну из 6 сторон), ПКМ поставить призрак
    /// (у потолка — перевёрнутый), ЛКМ убрать призрак;
    /// блок в руке по призраку — ПКМ заполнить (блок тратится, форма берёт его материал);
    /// дверь или ворота в руке — призрак по сетке, R поворот, ПКМ поставить.
    /// </summary>
    public sealed partial class ToolController
    {
        private const float GhostDrawDistance = 40f;

        private BlockShape _templateShape = BlockShape.Slab;
        private int _templateYaw;
        private int _templateUp;
        private Material _shapeGhostMaterial, _shapeTargetMaterial, _fblockGhostMaterial;
        private bool _hasGhostTarget;
        private int3 _ghostTarget, _ghostNormal;
        private bool _fblockVisible, _fblockValid;
        private Matrix4x4 _fblockMatrix;
        private Mesh _fblockMesh;
        private int _fblockYaw;
        private readonly List<int3> _ghostsToRemove = new();

        private void CreateBuildMaterials()
        {
            if (_brushMaterial == null)
                return;
            _shapeGhostMaterial = new Material(_brushMaterial) { name = "ShapeGhost" };
            _shapeGhostMaterial.SetColor(BaseColorId, new Color(0.45f, 0.75f, 1f, 0.22f));
            _shapeTargetMaterial = new Material(_brushMaterial) { name = "ShapeGhostTarget" };
            _shapeTargetMaterial.SetColor(BaseColorId, new Color(1f, 0.85f, 0.35f, 0.4f));
            _fblockGhostMaterial = new Material(_brushMaterial) { name = "FunctionalGhost" };
        }

        private void DestroyBuildMaterials()
        {
            if (_shapeGhostMaterial != null) Destroy(_shapeGhostMaterial);
            if (_shapeTargetMaterial != null) Destroy(_shapeTargetMaterial);
            if (_fblockGhostMaterial != null) Destroy(_fblockGhostMaterial);
        }

        /// <summary>Шаблон в руке. true — действия кадра обработаны (шаблоном не бьют).</summary>
        private bool HandleTemplate(ItemDef item)
        {
            if (item == null || item.Tool != ToolKind.Template)
                return false;
            bool shift = Keyboard.current != null && Keyboard.current.shiftKey.isPressed;
            if (_rotate != null && _rotate.WasPressedThisFrame())
            {
                if (shift)
                    _templateUp = (_templateUp + 1) % BlockShapes.UpDirections;
                else
                    _templateYaw = (_templateYaw + 1) & 3;
            }
            // Shift + колесо — сменить форму (хотбар в это время не листается).
            if (shift && _scroll != null && _scroll.WasPressedThisFrame())
            {
                int count = (int)BlockShape.Count - 1;
                int step = _scroll.ReadValue<float>() > 0 ? -1 : 1;
                _templateShape = (BlockShape)(((int)_templateShape - 1 + step + count) % count + 1);
            }
            if (_primary.WasPressedThisFrame() && _hasGhostTarget)
            {
                ShapeGhosts.All.Remove(_ghostTarget);
                Say("Призрак убран");
                return true;
            }
            if (!_secondary.WasPressedThisFrame())
                return true;
            int3 cell, normal;
            if (_hasGhostTarget)
            {
                cell = _ghostTarget + _ghostNormal;
                normal = _ghostNormal;
            }
            else if (_hasTarget && !math.all(_target.Normal == 0))
            {
                cell = _target.Cell + _target.Normal;
                normal = _target.Normal;
            }
            else
            {
                return true;
            }
            if (!World.IsLoaded(cell) || !World.GetCell(cell).IsAir || ShapeGhosts.All.ContainsKey(cell))
            {
                Say("Здесь занято");
                return true;
            }
            ShapeGhosts.All[cell] = new ShapeGhosts.Ghost { Shape = _templateShape, Meta = TemplateMeta(normal) };
            return true;
        }

        /// <summary>Ориентация шаблона; под потолком (верх не задан наклоном) форма ставится перевёрнутой.</summary>
        private byte TemplateMeta(int3 normal) => BlockShapes.Meta(_templateUp == 0 && normal.y < 0 ? 1 : _templateUp, _templateYaw);

        /// <summary>Шаблон в руке и зажат Shift: колесо меняет форму, а не слот хотбара.</summary>
        private bool TemplateScrollActive() =>
            ActiveItem != null && ActiveItem.Tool == ToolKind.Template && Keyboard.current != null && Keyboard.current.shiftKey.isPressed;

        /// <summary>
        /// Блок в руке: заполнить призрак формы или поставить функциональный блок. true — ПКМ обработан здесь
        /// (обычную постановку блока пропускаем).
        /// </summary>
        private bool HandleBlockItem(ItemDef item)
        {
            if (!World.Materials.TryGetIndex(item.PlacesBlock.Id, out ushort material))
                return false;
            var info = World.GetInfo(material);
            // Вода из ведра ставится просто в клетку перед гранью — без шаблонов формы.
            if (info.Kind == MaterialKind.Liquid)
                return false;
            if (info.Kind == MaterialKind.Functional)
            {
                HandleFunctionalPlacing(item, material, info);
                return true;
            }
            if (!_hasGhostTarget)
                return false;
            if (!_secondary.WasPressedThisFrame())
                return true;
            var ghost = ShapeGhosts.All[_ghostTarget];
            if (!WorldAuthority.CanPlaceBlock(World, _ghostTarget, material))
            {
                Say("Мешает: игрок или предмет");
                return true;
            }
            var stack = Inventory.ActiveStack;
            int slot = Inventory.Hotbar.Selected;
            if (!Inventory.TryConsume(stack.Def, 1))
                return true;
            Inventory.RefillHotbar(slot, stack.Def);
            Send(new PlaceBlockCommand { Position = _ghostTarget, Material = material, Shape = (byte)ghost.Shape, Meta = ghost.Meta });
            ShapeGhosts.All.Remove(_ghostTarget);
            PlayerSkills.AwardLocal(Aspect.Construction, 0.2f);
            return true;
        }

        private void HandleFunctionalPlacing(ItemDef item, ushort material, MaterialInfo info)
        {
            if (_rotate != null && _rotate.WasPressedThisFrame())
                _fblockYaw = (_fblockYaw + 1) & 3;
            if (!_hasTarget || math.all(_target.Normal == 0))
                return;
            // Лицом к игроку: поворот по взгляду, R добавляет четверти. Широкий блок — серединой на прицел.
            int lookYaw = (int)math.round(_camera.transform.eulerAngles.y / 90f) & 3;
            int yaw = (lookYaw + _fblockYaw) & 3;
            int3 anchor = _target.Cell + _target.Normal - FunctionalCells.Rotate(new int3((info.FunctionalSize.x - 1) / 2, 0, 0), yaw);
            _fblockValid = WorldAuthority.CanPlaceFunctional(World, anchor, material, yaw);
            var view = FunctionalBlocks.Instance;
            _fblockMesh = view != null ? view.GhostMesh(info.Function, info.FunctionalSize.x) : null;
            var rotation = Quaternion.Euler(0f, 90f * yaw, 0f);
            var position = new Vector3(anchor.x + 0.5f, anchor.y, anchor.z + 0.5f);
            Vector3 pivot = info.Function == FunctionalKind.Door ? new Vector3(-0.5f, 0f, 0f) : Vector3.zero;
            _fblockMatrix = Matrix4x4.TRS(position + rotation * pivot, rotation, Vector3.one);
            _fblockVisible = _fblockMesh != null;

            if (!_secondary.WasPressedThisFrame())
                return;
            if (!_fblockValid)
            {
                Say("Не помещается: нужно свободное место во весь размер");
                return;
            }
            var stack = Inventory.ActiveStack;
            int slot = Inventory.Hotbar.Selected;
            if (!Inventory.TryConsume(stack.Def, 1))
                return;
            Inventory.RefillHotbar(slot, stack.Def);
            Send(new PlaceFunctionalCommand { Anchor = anchor, Material = material, Yaw = (byte)yaw });
            PlayerSkills.AwardLocal(Aspect.Construction, 0.5f);
            Say($"Поставлено: {item.DisplayName} · E — открыть");
        }

        /// <summary>Луч по призракам: первый призрак до твёрдой клетки (в пределах досягаемости).</summary>
        private void FindGhostTarget()
        {
            _hasGhostTarget = false;
            if (ShapeGhosts.All.Count == 0)
                return;
            var view = _camera.transform;
            float3 o = view.position, d = math.normalizesafe((float3)view.forward);
            int3 cell = (int3)math.floor(o);
            int3 step = (int3)math.sign(d);
            float3 tDelta = math.abs(1f / d);
            float3 tMax = new(Boundary(o.x, cell.x, d.x), Boundary(o.y, cell.y, d.y), Boundary(o.z, cell.z, d.z));
            int3 normal = int3.zero;
            float t = 0f;
            while (t <= _reach)
            {
                if (ShapeGhosts.All.ContainsKey(cell))
                {
                    _hasGhostTarget = true;
                    _ghostTarget = cell;
                    _ghostNormal = normal;
                    return;
                }
                if (!World.GetCell(cell).IsAir)
                    return;
                if (tMax.x < tMax.y && tMax.x < tMax.z) { t = tMax.x; tMax.x += tDelta.x; cell.x += step.x; normal = new int3(-step.x, 0, 0); }
                else if (tMax.y < tMax.z) { t = tMax.y; tMax.y += tDelta.y; cell.y += step.y; normal = new int3(0, -step.y, 0); }
                else { t = tMax.z; tMax.z += tDelta.z; cell.z += step.z; normal = new int3(0, 0, -step.z); }
            }
        }

        private static float Boundary(float origin, int cell, float direction) =>
            direction > 0f ? (cell + 1 - origin) / direction : direction < 0f ? (origin - cell) / -direction : float.PositiveInfinity;

        /// <summary>Призраки рядом (снятые блоком — убираем), выделенный, призрак функционального блока.</summary>
        private void DrawBuildGhosts()
        {
            if (_shapeGhostMaterial == null)
                return;
            Vector3 eye = _camera.transform.position;
            _ghostsToRemove.Clear();
            var normalParams = new RenderParams(_shapeGhostMaterial) { shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off, receiveShadows = false };
            var targetParams = new RenderParams(_shapeTargetMaterial) { shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off, receiveShadows = false };
            foreach (var (cell, ghost) in ShapeGhosts.All)
            {
                if (World.IsLoaded(cell) && !World.GetCell(cell).IsAir)
                {
                    _ghostsToRemove.Add(cell);
                    continue;
                }
                var position = new Vector3(cell.x, cell.y, cell.z);
                if ((position - eye).sqrMagnitude > GhostDrawDistance * GhostDrawDistance)
                    continue;
                var mesh = BlockShapes.GetMesh(ghost.Shape, BlockShapes.Orientation(ghost.Meta));
                bool target = _hasGhostTarget && math.all(cell == _ghostTarget);
                Graphics.RenderMesh(target ? targetParams : normalParams, mesh, 0, Matrix4x4.Translate(position));
            }
            foreach (var cell in _ghostsToRemove)
                ShapeGhosts.All.Remove(cell);

            // Предпросмотр формы шаблона там, куда встанет призрак.
            var item = ActiveItem;
            if (!InputBlocked && item != null && item.Tool == ToolKind.Template && (_hasGhostTarget || (_hasTarget && !math.all(_target.Normal == 0))))
            {
                int3 cell = _hasGhostTarget ? _ghostTarget + _ghostNormal : _target.Cell + _target.Normal;
                int3 normal = _hasGhostTarget ? _ghostNormal : _target.Normal;
                if (World.GetCell(cell).IsAir && !ShapeGhosts.All.ContainsKey(cell))
                {
                    var mesh = BlockShapes.GetMesh(_templateShape, BlockShapes.Orientation(TemplateMeta(normal)));
                    Graphics.RenderMesh(targetParams, mesh, 0, Matrix4x4.Translate(new Vector3(cell.x, cell.y, cell.z)));
                }
            }

            if (_fblockVisible && !InputBlocked && _fblockGhostMaterial != null)
            {
                _fblockGhostMaterial.SetColor(BaseColorId, _fblockValid ? new Color(0.35f, 1f, 0.45f, 0.35f) : new Color(1f, 0.3f, 0.3f, 0.35f));
                var rp = new RenderParams(_fblockGhostMaterial) { shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off, receiveShadows = false };
                Graphics.RenderMesh(rp, _fblockMesh, 0, _fblockMatrix);
            }
        }

        private string BuildHint(ItemDef item)
        {
            if (item.Tool == ToolKind.Template)
                return $"Шаблон: {BlockShapes.Name(_templateShape)} (верх {BlockShapes.UpName(_templateUp)}) · ПКМ — призрак · R — повернуть · Shift+R — наклонить · Shift+колесо — форма"
                       + (_hasGhostTarget ? " · ЛКМ — убрать призрак" : "");
            if (item.PlacesBlock != null && World.Materials.TryGetIndex(item.PlacesBlock.Id, out ushort material)
                && World.GetInfo(material).Kind == MaterialKind.Functional)
                return _fblockVisible && !_fblockValid ? "Не помещается" : "ПКМ — поставить, R — повернуть, ЛКМ — ударить";
            if (item.PlacesBlock != null && _hasGhostTarget)
                return $"ПКМ — заполнить призрак ({BlockShapes.Name(ShapeGhosts.All[_ghostTarget].Shape).ToLowerInvariant()})";
            return null;
        }
    }
}
