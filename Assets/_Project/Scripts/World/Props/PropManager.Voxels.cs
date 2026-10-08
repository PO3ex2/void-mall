using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using VoidMall.Console;
using VoidMall.Core;

namespace VoidMall.World
{
    /// <summary>
    /// Отломанные куски — воксельные тела (этап 8Е). Кусок — особый проп (Def = VoxelDefIndex) со своей сеткой клеток в
    /// состоянии: переноска по весу, гвозди, сон, стриминг по позиции и будущий сейв — общие с пропами. Здесь — появление
    /// из обрушения, поломка и откол клеток (тело может развалиться на части), постройка на теле.
    /// </summary>
    public sealed partial class PropManager
    {
        /// <summary>Индекс «описания» куска: не из реестра пропов, модель строится по клеткам.</summary>
        public const ushort VoxelDefIndex = ushort.MaxValue;

        private const int MaxNailCells = 16;
        private const int PlaceMask = (1 << GameLayers.World) | (1 << GameLayers.Props) | (1 << GameLayers.Player) | (1 << GameLayers.Enemy);

        private static readonly int3[] VoxelNeighbors =
        {
            new(1, 0, 0), new(-1, 0, 0), new(0, 1, 0), new(0, -1, 0), new(0, 0, 1), new(0, 0, -1),
        };

        private PropDef _voxelDef;
        private readonly List<List<int3>> _voxelParts = new();
        private readonly List<int3> _voxelScratch = new();

        /// <summary>Общее описание кусков: не станция, не кровать, без разбора (ломают по клеткам).</summary>
        private PropDef VoxelDef
        {
            get
            {
                if (_voxelDef == null)
                {
                    _voxelDef = ScriptableObject.CreateInstance<PropDef>();
                    _voxelDef.name = "prop.voxel_body";
                    _voxelDef.DisplayName = "Кусок";
                    _voxelDef.Shape = PropShape.Count;
                    _voxelDef.Mass = 1f;
                    _voxelDef.Hardness = int.MaxValue;
                }
                return _voxelDef;
            }
        }

        // ───────────────────────────── Появление ─────────────────────────────

        private PropBody SpawnVoxelObject(ulong id, VoxelGrid grid, Vector3 position, Quaternion rotation, Transform parent)
        {
            // Собираем в выключенном пуле и включаем сразу на месте (как обычные пропы).
            var go = new GameObject("VoxelBody") { layer = GameLayers.Props };
            go.transform.SetParent(_poolRoot, false);
            go.AddComponent<MeshFilter>();
            go.AddComponent<MeshRenderer>().enabled = Visible.Value;
            go.AddComponent<BoxCollider>();
            var body = go.AddComponent<PropBody>();
            var voxel = go.AddComponent<VoxelBody>();
            voxel.Setup(grid, body);
            voxel.Rebuild(_world);
            body.transform.SetPositionAndRotation(position, rotation);
            body.transform.SetParent(parent, true);
            body.Initialize(this, id, VoxelDefIndex, VoxelDef);
            PropSpawned?.Invoke(body);
            _live[id] = body;
            return body;
        }

        /// <summary>Новый кусок в мире: сразу физическое тело с заданной скоростью.</summary>
        public PropBody SpawnVoxel(VoxelGrid grid, Vector3 position, Quaternion rotation, Vector3 velocity = default, Vector3 angularVelocity = default)
        {
            ulong id = _nextSpawnedId++;
            var state = new PropState { Id = id, Def = VoxelDefIndex, Loose = true, Position = position, Rotation = rotation, Voxel = grid };
            _states.Add(id, state);
            var body = SpawnGroup(state);
            body.Wake();
            if (body.Body != null)
            {
                body.Body.linearVelocity = velocity;
                body.Body.angularVelocity = angularVelocity;
            }
            return body;
        }

        /// <summary>Пропы, прибитые к этим клеткам мира (до обрушения: потом гвозди к ним выдёргиваются).</summary>
        public void CollectAnchoredTo(IReadOnlyList<int3> cells, List<(ulong id, int3 anchor)> result)
        {
            result.Clear();
            foreach (var cell in cells)
                if (_anchorIndex.TryGetValue(cell, out var ids))
                    foreach (var id in ids)
                        result.Add((id, cell));
        }

        /// <summary>
        /// Обрушившийся остров (клетки уже убраны из мира) — куском или несколькими, если остров несвязный. false — не
        /// помещается в одно тело (больше VoxelGrid.MaxCells или шире MaxSpan): пусть рассыпается обломками.
        /// Пропы, державшиеся только на гвоздях к острову, остаются прибитыми — уже к куску.
        /// </summary>
        public bool SpawnIsland(IReadOnlyList<int3> cells, IReadOnlyList<Cell> values, IReadOnlyList<(ulong id, int3 anchor)> anchored)
        {
            if (_world == null || !VoxelGrid.Fits(cells))
                return false;
            int3 min = cells[0];
            foreach (var cell in cells)
                min = math.min(min, cell);
            var grid = new VoxelGrid { UvOrigin = min };
            for (int i = 0; i < cells.Count; i++)
                grid.Set(cells[i] - min, values[i]);

            var origin = new Vector3(min.x, min.y, min.z);
            var spawned = new List<PropBody>();
            grid.Components(_voxelParts);
            var parts = new List<List<int3>>(_voxelParts);
            for (int i = 1; i < parts.Count; i++)
                spawned.Add(SpawnVoxel(grid.Extract(parts[i]), origin, Quaternion.identity));
            spawned.Add(SpawnVoxel(grid, origin, Quaternion.identity));

            foreach (var (id, anchor) in anchored)
            {
                if (!_live.TryGetValue(id, out var prop) || prop == null || prop.IsHeld || prop.IsNailed)
                    continue;
                foreach (var body in spawned)
                {
                    if (!body.Voxel.Grid.Contains(anchor - min) || prop.Root == body.Root)
                        continue;
                    NailToProp(prop, body);
                    break;
                }
            }
            return true;
        }

        // ───────────────────────────── Поломка ─────────────────────────────

        /// <summary>Удар по клетке куска. Возвращает долю оставшейся прочности клетки (0 — выбита).</summary>
        public float DamageVoxel(PropBody body, int3 cell, int amount)
        {
            var voxel = body.Voxel;
            if (voxel == null || _world == null || !voxel.Grid.TryGet(cell, out var value) || amount <= 0)
                return 1f;
            int hardness = _world.GetInfo(value.Material).Hardness;
            int damage = voxel.Grid.GetDamage(cell) + amount;
            if (damage < hardness)
            {
                voxel.Grid.SetDamage(cell, damage);
                return 1f - damage / (float)hardness;
            }
            _voxelScratch.Clear();
            _voxelScratch.Add(cell);
            RemoveVoxelCells(body, _voxelScratch);
            return 0f;
        }

        /// <summary>Доля урона клетки куска (для трещин).</summary>
        public float VoxelDamageFraction(PropBody body, int3 cell)
        {
            var voxel = body.Voxel;
            if (voxel == null || _world == null || !voxel.Grid.TryGet(cell, out var value))
                return 0f;
            int hardness = _world.GetInfo(value.Material).Hardness;
            return hardness > 0 ? Mathf.Clamp01(voxel.Grid.GetDamage(cell) / (float)hardness) : 0f;
        }

        /// <summary>Сильный удар о мир: откалываются клетки, ближайшие к точке удара (хотя бы одна клетка остаётся).</summary>
        public void ChipVoxel(PropBody body, Vector3 point, int count)
        {
            var voxel = body.Voxel;
            if (voxel == null || voxel.Grid.Count <= 1)
                return;
            count = math.min(count, voxel.Grid.Count - 1);
            float3 local = (float3)body.transform.InverseTransformPoint(point);
            _voxelScratch.Clear();
            _voxelScratch.AddRange(voxel.Grid.Cells.Keys);
            _voxelScratch.Sort((a, b) => math.distancesq((float3)a + 0.5f, local).CompareTo(math.distancesq((float3)b + 0.5f, local)));
            _voxelScratch.RemoveRange(count, _voxelScratch.Count - count);
            RemoveVoxelCells(body, _voxelScratch);
        }

        /// <summary>Убрать клетки куска: дроп, пыль, затем проверка — не развалился ли он на части.</summary>
        private void RemoveVoxelCells(PropBody body, List<int3> cells)
        {
            var voxel = body.Voxel;
            var grid = voxel.Grid;
            foreach (var cell in cells)
            {
                if (!grid.TryGet(cell, out var value))
                    continue;
                Vector3 center = voxel.CellCenter(cell);
                grid.Remove(cell);
                PublishVoxelMined(value, center);
                DustFx.Burst(_dustMaterial, new Bounds(center, Vector3.one), 14, new Color(0.55f, 0.53f, 0.5f, 0.9f), new Color(0.75f, 0.73f, 0.7f, 0.5f));
            }
            if (grid.Count == 0)
            {
                Remove(body, PropRemoveReason.Broken);
                return;
            }
            SplitVoxel(body);
        }

        private void PublishVoxelMined(Cell value, Vector3 position)
        {
            float amount = _world.GetInfo(value.Material).Kind == MaterialKind.Smooth ? value.Value / 255f : 1f;
            EventBus<MaterialMinedEvent>.Publish(new MaterialMinedEvent { Material = value.Material, Amount = amount, Position = position });
        }

        /// <summary>
        /// Кусок развалился на несвязные части: самая большая остаётся этим телом, остальные — новые тела на том же месте и
        /// с той же скоростью. Прибитое остаётся на большой части.
        /// </summary>
        private void SplitVoxel(PropBody body)
        {
            var voxel = body.Voxel;
            var grid = voxel.Grid;
            grid.Components(_voxelParts);
            if (_voxelParts.Count > 1)
            {
                var parts = new List<List<int3>>(_voxelParts);
                parts.Sort((a, b) => b.Count.CompareTo(a.Count));
                var rb = body.Root.Body;
                bool moving = rb != null && !rb.isKinematic;
                for (int i = 1; i < parts.Count; i++)
                {
                    Vector3 velocity = moving ? rb.GetPointVelocity(voxel.CellCenter(parts[i][0])) : Vector3.zero;
                    SpawnVoxel(grid.Extract(parts[i]), body.transform.position, body.transform.rotation, velocity, moving ? rb.angularVelocity : Vector3.zero);
                }
            }
            voxel.Rebuild(_world);
            // Опора внутри куска могла измениться — пусть физика уложит его заново (прибитый держится).
            body.Wake();
        }

        // ───────────────────────────── Подобрать и построить ─────────────────────────────

        /// <summary>Кусок из одной клетки: можно забрать в инвентарь (дроп блока) без инструмента.</summary>
        public static bool IsPickupBlock(PropBody body) =>
            body != null && body.Voxel != null && body.Voxel.Grid.Count == 1 && !body.IsInGroup && !body.IsNailed;

        public void PickUpVoxel(PropBody body)
        {
            if (!IsPickupBlock(body))
                return;
            foreach (var (cell, value) in body.Voxel.Grid.Cells)
            {
                PublishVoxelMined(value, body.Voxel.CellCenter(cell));
                break;
            }
            Remove(body, PropRemoveReason.Disassembled);
        }

        /// <summary>Можно ли пристроить блок к куску: клетка свободна, кусок не вырастет за пределы, внутри никого.</summary>
        public bool CanPlaceVoxel(PropBody body, int3 cell, ushort material, out string reason)
        {
            reason = null;
            var voxel = body.Voxel;
            if (voxel == null || _world == null || material == 0 || material >= _world.Materials.Count)
                return false;
            var def = _world.Materials.Get(material);
            if (def.Kind != MaterialKind.Block || !def.Placeable)
            {
                reason = "К куску пристраиваются только блоки";
                return false;
            }
            if (!voxel.Grid.CanAdd(cell))
            {
                reason = voxel.Grid.Contains(cell) ? "Здесь занято" : "Кусок слишком большой";
                return false;
            }
            var root = body.Root;
            int count = Physics.OverlapBoxNonAlloc(voxel.CellCenter(cell), Vector3.one * 0.45f, _overlap, body.transform.rotation, PlaceMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                if (TryGetProp(_overlap[i], out var other) && other.Root == root)
                    continue;
                reason = "Мешает: стена, игрок или предмет";
                return false;
            }
            return true;
        }

        public bool PlaceVoxel(PropBody body, int3 cell, ushort material)
        {
            if (!CanPlaceVoxel(body, cell, material, out _))
                return false;
            body.Voxel.Grid.Set(cell, new Cell(material));
            body.Voxel.Rebuild(_world);
            return true;
        }

        /// <summary>
        /// Клетки мира, к которым можно прибить кусок: твёрдые соседи его открытых граней (по клеткам, а не по габариту —
        /// повёрнутый кусок цепляется точно).
        /// </summary>
        public void CollectVoxelNailCells(PropBody body, List<int3> cells)
        {
            cells.Clear();
            var voxel = body.Voxel;
            if (voxel == null || _world == null)
                return;
            var t = body.transform;
            foreach (var cell in voxel.Grid.Cells.Keys)
            {
                foreach (var dir in VoxelNeighbors)
                {
                    if (voxel.Grid.Contains(cell + dir))
                        continue;
                    var p = t.TransformPoint((Vector3)(float3)cell + Vector3.one * 0.5f + (Vector3)(float3)dir * 0.75f);
                    var world = (int3)math.floor((float3)p);
                    if (cells.Contains(world) || !IsSolidWorldCell(world))
                        continue;
                    cells.Add(world);
                    if (cells.Count >= MaxNailCells)
                        return;
                }
            }
        }

        private bool IsSolidWorldCell(int3 cell)
        {
            Cell value = _world.GetCell(cell);
            if (value.IsAir)
                return false;
            var info = _world.GetInfo(value.Material);
            return info.IsSolid && (info.Kind != MaterialKind.Smooth || Density.IsSolid(value.Value));
        }

        // ───────────────────────────── Консоль ─────────────────────────────

        [ConsoleCommand("body_spawn", "Создать кусок перед игроком: body_spawn <материал> [x y z] (например, body_spawn concrete 3 1 1).", Flags = ConsoleFlags.Cheat)]
        private static string BodySpawnCommand(string material, int x = 2, int y = 2, int z = 2)
        {
            var m = Instance;
            var camera = Camera.main;
            if (m == null || m._world == null || camera == null)
                return "Нет PropManager, мира или камеры.";
            var materials = m._world.Materials;
            if (!materials.TryGetIndex(material, out ushort index) && !materials.TryGetIndex("block." + material, out index))
                return $"Нет материала '{material}'.";
            var kind = materials.Get(index).Kind;
            if (kind != MaterialKind.Block && kind != MaterialKind.Smooth)
                return "Кусок собирается только из блоков и гладких материалов.";
            x = math.clamp(x, 1, VoxelGrid.MaxSpan);
            y = math.clamp(y, 1, VoxelGrid.MaxSpan);
            z = math.clamp(z, 1, VoxelGrid.MaxSpan);
            if (x * y * z > VoxelGrid.MaxCells)
                return $"Не больше {VoxelGrid.MaxCells} клеток.";
            var grid = new VoxelGrid();
            var cell = kind == MaterialKind.Smooth ? new Cell(index, 255) : new Cell(index);
            for (int cy = 0; cy < y; cy++)
            for (int cz = 0; cz < z; cz++)
            for (int cx = 0; cx < x; cx++)
                grid.Set(new int3(cx, cy, cz), cell);
            Vector3 forward = camera.transform.forward;
            forward.y = 0f;
            forward = forward.sqrMagnitude > 0.001f ? forward.normalized : Vector3.forward;
            Vector3 position = camera.transform.position + forward * (1.5f + math.max(x, z)) + Vector3.up * 0.5f;
            var body = m.SpawnVoxel(grid, position - new Vector3(x, 0f, z) * 0.5f, Quaternion.identity);
            return $"Создан: {body.DisplayName}, {body.Mass:0} кг";
        }

        [ConsoleCommand("bodies_info", "Отломанные куски в мире: сколько, клеток, масса.")]
        private static string BodiesInfo()
        {
            var m = Instance;
            if (m == null)
                return "Нет PropManager.";
            int bodies = 0, cells = 0, awake = 0;
            float mass = 0f;
            foreach (var body in m._live.Values)
            {
                if (body == null || body.Voxel == null)
                    continue;
                bodies++;
                cells += body.Voxel.Grid.Count;
                mass += body.Mass;
                if (body.IsAwake)
                    awake++;
            }
            int stored = 0;
            foreach (var state in m._states.Values)
                if (state.Voxel != null && !state.Removed)
                    stored++;
            return $"Кусков в мире {bodies} (активных {awake}), клеток {cells}, масса {mass:0} кг; всего за сессию {stored}";
        }
    }
}
