using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using VoidMall.Core;
using VoidMall.Net;

namespace VoidMall.World
{
    /// <summary>
    /// Принимает команды правки мира и применяет их на авторитете (хосте), затем рассылает BlockChangedEvent.
    /// Клиент (в будущем коопе) применяет события у себя. В одиночной игре всё происходит в одном процессе.
    /// </summary>
    [RequireComponent(typeof(ChunkManager))]
    public sealed class WorldAuthority : MonoBehaviour
    {
        public const int MaxFillVolume = 32 * 32 * 32;
        private const int EntityMask = (1 << GameLayers.Player) | (1 << GameLayers.Props) | (1 << GameLayers.Enemy);

        private readonly List<int3> _removed = new();
        private readonly List<List<int3>> _islands = new();
        private readonly Dictionary<ushort, float> _mined = new();
        private readonly List<Cell> _islandValues = new();

        private ChunkManager _world;
        private DebrisSpawner _debris;
        private StructuralIntegrity _integrity;
        private INetSession _session;

        private void Awake()
        {
            _world = GetComponent<ChunkManager>();
            _debris = GetComponent<DebrisSpawner>();
            _integrity = new StructuralIntegrity(_world);
        }

        private void OnEnable()
        {
            if (!Services.TryGet(out _session))
                return;
            _session.RegisterCommandHandler<DamageBlockCommand>(OnDamage);
            _session.RegisterCommandHandler<PlaceBlockCommand>(OnPlace);
            _session.RegisterCommandHandler<SetBlockCommand>(OnSet);
            _session.RegisterCommandHandler<FillBlocksCommand>(OnFill);
            _session.RegisterCommandHandler<SmoothBrushCommand>(OnBrush);
            _session.RegisterCommandHandler<PlaceFunctionalCommand>(OnPlaceFunctional);
            _session.RegisterCommandHandler<SetFunctionalStateCommand>(OnSetFunctionalState);
            _session.RegisterEventHandler<BlockChangedEvent>(OnBlockChanged);
        }

        private void OnDisable()
        {
            if (_session == null)
                return;
            _session.UnregisterCommandHandler<DamageBlockCommand>(OnDamage);
            _session.UnregisterCommandHandler<PlaceBlockCommand>(OnPlace);
            _session.UnregisterCommandHandler<SetBlockCommand>(OnSet);
            _session.UnregisterCommandHandler<FillBlocksCommand>(OnFill);
            _session.UnregisterCommandHandler<SmoothBrushCommand>(OnBrush);
            _session.UnregisterCommandHandler<PlaceFunctionalCommand>(OnPlaceFunctional);
            _session.UnregisterCommandHandler<SetFunctionalStateCommand>(OnSetFunctionalState);
            _session.UnregisterEventHandler<BlockChangedEvent>(OnBlockChanged);
            _session = null;
        }

        private void OnDamage(PlayerId sender, in DamageBlockCommand command)
        {
            if (!_session.IsAuthority || command.Amount <= 0)
                return;
            Cell cell = _world.GetCell(command.Position);
            if (cell.IsAir || !_world.IsLoaded(command.Position))
                return;
            var info = _world.Materials.Infos[cell.Material];
            if ((info.Flags & MaterialFlags.Unbreakable) != 0)
                return;
            if (info.Kind == MaterialKind.Liquid)
            {
                // Набрали воду ведром: клетка пустеет сразу.
                Cell empty = _world.EmptyCellAt(command.Position);
                _world.SetCell(command.Position, empty);
                Broadcast(command.Position, empty, 0);
                return;
            }
            if (info.Kind == MaterialKind.Functional)
            {
                DamageFunctional(command.Position, cell, info, command.Amount);
                return;
            }
            if (info.Kind != MaterialKind.Block)
                return; // гладкие массивы — только буром

            int damage = _world.GetDamage(command.Position) + command.Amount;
            if (damage >= info.Hardness)
            {
                Cell empty = _world.EmptyCellAt(command.Position);
                _world.SetCell(command.Position, empty);
                Broadcast(command.Position, empty, 0);
                PublishMined(cell.Material, 1f, command.Position);
                _removed.Clear();
                _removed.Add(command.Position);
                CheckIntegrity(_removed);
            }
            else
            {
                _world.SetDamage(command.Position, damage);
                Broadcast(command.Position, cell, damage);
            }
        }

        private void OnPlace(PlayerId sender, in PlaceBlockCommand command)
        {
            if (!_session.IsAuthority || !CanPlaceBlock(_world, command.Position, command.Material))
                return;
            byte shape = command.Shape < (byte)BlockShape.Count ? command.Shape : (byte)0;
            var cell = new Cell(command.Material, shape, shape != 0 ? (byte)BlockShapes.Orientation(command.Meta) : (byte)0);
            if (_world.SetCell(command.Position, cell))
                Broadcast(command.Position, cell, 0);
        }

        /// <summary>
        /// Можно ли поставить блок: клетка загружена и пуста, материал ставится, внутри нет игрока, пропа или врага.
        /// Инструмент проверяет то же самое до того, как списать блок из инвентаря.
        /// </summary>
        public static bool CanPlaceBlock(ChunkManager world, int3 position, ushort material)
        {
            if (world == null || material == 0 || material >= world.Materials.Count)
                return false;
            if (!world.IsLoaded(position) || !world.GetCell(position).IsAir)
                return false;
            var def = world.Materials.Get(material);
            // Блоки и вода (из ведра).
            if (!def.Placeable || (def.Kind != MaterialKind.Block && def.Kind != MaterialKind.Liquid))
                return false;
            if (def.Kind == MaterialKind.Liquid)
                return true; // в воду можно ставить и там, где стоишь
            Vector3 center = new Vector3(position.x, position.y, position.z) + Vector3.one * 0.5f;
            return !Physics.CheckBox(center, Vector3.one * 0.49f, Quaternion.identity, EntityMask, QueryTriggerInteraction.Ignore);
        }

        // ───────────────────────────── Функциональные блоки ─────────────────────────────

        private readonly List<int3> _footprint = new();

        /// <summary>Удар по любой клетке функционального блока: урон копится в якоре, на прочности ломается весь блок.</summary>
        private void DamageFunctional(int3 position, Cell cell, MaterialInfo info, int amount)
        {
            int3 anchor = FunctionalCells.AnchorOf(position, cell);
            Cell anchorCell = _world.GetCell(anchor);
            if (anchorCell.Material != cell.Material)
                return;
            int damage = _world.GetDamage(anchor) + amount;
            if (damage < info.Hardness)
            {
                _world.SetDamage(anchor, damage);
                Broadcast(anchor, anchorCell, damage);
                return;
            }
            FunctionalCells.Footprint(anchor, info.FunctionalSize, FunctionalCells.Yaw(anchorCell.Meta), _footprint);
            _removed.Clear();
            using (_world.BeginBatch())
            {
                foreach (var part in _footprint)
                {
                    Cell value = _world.GetCell(part);
                    if (value.Material != cell.Material || !math.all(FunctionalCells.AnchorOf(part, value) == anchor))
                        continue;
                    Cell empty = _world.EmptyCellAt(part);
                    _world.SetCell(part, empty);
                    Broadcast(part, empty, 0);
                    _removed.Add(part);
                }
            }
            PublishMined(cell.Material, 1f, anchor);
            CheckIntegrity(_removed);
        }

        /// <summary>
        /// Можно ли поставить функциональный блок: материал функциональный, весь объём загружен и пуст, внутри никого.
        /// Инструмент проверяет то же самое до того, как списать предмет.
        /// </summary>
        public static bool CanPlaceFunctional(ChunkManager world, int3 anchor, ushort material, int yaw)
        {
            if (world == null || material == 0 || material >= world.Materials.Count)
                return false;
            var info = world.GetInfo(material);
            if (info.Kind != MaterialKind.Functional)
                return false;
            var cells = new List<int3>();
            FunctionalCells.Footprint(anchor, info.FunctionalSize, yaw, cells);
            foreach (var cell in cells)
            {
                if (!WorldConstants.IsInsideWorldHeight(cell.y) || !world.IsLoaded(cell) || !world.GetCell(cell).IsAir)
                    return false;
                Vector3 center = new Vector3(cell.x, cell.y, cell.z) + Vector3.one * 0.5f;
                if (Physics.CheckBox(center, Vector3.one * 0.45f, Quaternion.identity, EntityMask, QueryTriggerInteraction.Ignore))
                    return false;
            }
            return true;
        }

        private void OnPlaceFunctional(PlayerId sender, in PlaceFunctionalCommand command)
        {
            if (!_session.IsAuthority || !CanPlaceFunctional(_world, command.Anchor, command.Material, command.Yaw))
                return;
            var info = _world.GetInfo(command.Material);
            byte meta = FunctionalCells.Meta(command.Yaw);
            using (_world.BeginBatch())
            {
                for (int y = 0; y < info.FunctionalSize.y; y++)
                for (int z = 0; z < info.FunctionalSize.z; z++)
                for (int x = 0; x < info.FunctionalSize.x; x++)
                {
                    var local = new int3(x, y, z);
                    var cell = FunctionalCells.Part(command.Material, local, meta);
                    int3 position = FunctionalCells.CellOf(command.Anchor, local, command.Yaw);
                    if (_world.SetCell(position, cell))
                        Broadcast(position, cell, 0);
                }
            }
        }

        /// <summary>
        /// Открыть или закрыть: дверь — одна, ворота — весь проём (соседние сегменты в линию). Закрыть нельзя,
        /// если в проёме кто-то стоит.
        /// </summary>
        private void OnSetFunctionalState(PlayerId sender, in SetFunctionalStateCommand command)
        {
            if (!_session.IsAuthority)
                return;
            Cell anchor = _world.GetCell(command.Anchor);
            if (anchor.IsAir || anchor.Value != 0)
                return;
            var info = _world.GetInfo(anchor.Material);
            if (info.Kind != MaterialKind.Functional || info.Function == FunctionalKind.SlidingDoor)
                return; // автоматическая дверь открывается сама, по близости игрока
            int yaw = FunctionalCells.Yaw(anchor.Meta);
            _footprint.Clear();
            _footprint.Add(command.Anchor);
            if (info.Function == FunctionalKind.Gate)
            {
                int3 step = FunctionalCells.Rotate(new int3(info.FunctionalSize.x, 0, 0), yaw);
                foreach (int dir in new[] { 1, -1 })
                {
                    int3 next = command.Anchor + step * dir;
                    for (int i = 0; i < 16; i++, next += step * dir)
                    {
                        Cell value = _world.GetCell(next);
                        if (value.Material != anchor.Material || value.Value != 0 || FunctionalCells.Yaw(value.Meta) != yaw)
                            break;
                        _footprint.Add(next);
                    }
                }
            }
            if (!command.Open)
                foreach (var a in _footprint)
                    if (IsOccupied(a, info, yaw))
                        return;
            using (_world.BeginBatch())
            {
                foreach (var a in _footprint)
                {
                    var cell = new Cell(anchor.Material, 0, FunctionalCells.Meta(yaw, command.Open, command.Locked, command.Side));
                    int damage = _world.GetDamage(a);
                    if (_world.SetCell(a, cell))
                    {
                        _world.SetDamage(a, damage);
                        Broadcast(a, cell, damage);
                    }
                }
            }
        }

        /// <summary>В объёме блока стоит игрок, проп или враг.</summary>
        private bool IsOccupied(int3 anchor, MaterialInfo info, int yaw)
        {
            var cells = new List<int3>();
            FunctionalCells.Footprint(anchor, info.FunctionalSize, yaw, cells);
            foreach (var cell in cells)
            {
                Vector3 center = new Vector3(cell.x, cell.y, cell.z) + Vector3.one * 0.5f;
                if (Physics.CheckBox(center, new Vector3(0.45f, 0.49f, 0.45f), Quaternion.identity, EntityMask, QueryTriggerInteraction.Ignore))
                    return true;
            }
            return false;
        }

        private void OnSet(PlayerId sender, in SetBlockCommand command)
        {
            if (!_session.IsAuthority)
                return;
            if (!_world.SetCell(command.Position, command.Cell))
                return;
            Broadcast(command.Position, command.Cell, 0);
            if (command.Cell.IsAir)
            {
                _removed.Clear();
                _removed.Add(command.Position);
                CheckIntegrity(_removed);
            }
        }

        private void OnFill(PlayerId sender, in FillBlocksCommand command)
        {
            if (!_session.IsAuthority)
                return;
            int3 min = math.min(command.Min, command.Max);
            int3 max = math.max(command.Min, command.Max);
            int3 size = max - min + 1;
            if ((long)size.x * size.y * size.z > MaxFillVolume)
                return;

            using (_world.BeginBatch())
            {
                for (int y = min.y; y <= max.y; y++)
                for (int z = min.z; z <= max.z; z++)
                for (int x = min.x; x <= max.x; x++)
                {
                    var p = new int3(x, y, z);
                    if (_world.SetCell(p, command.Cell))
                        Broadcast(p, command.Cell, 0);
                }
            }
        }

        private void OnBrush(PlayerId sender, in SmoothBrushCommand command)
        {
            if (!_session.IsAuthority)
                return;
            _removed.Clear();
            _mined.Clear();
            // Изменения клеток рассылаются через BlockChangedEvent внутри SetCell-подписки ниже.
            _world.CellChanged += BroadcastChange;
            try
            {
                SmoothBrush.Apply(_world, command, _removed, _mined);
            }
            finally
            {
                _world.CellChanged -= BroadcastChange;
            }

            Vector3 center = new(command.Center.x, command.Center.y, command.Center.z);
            foreach (var (material, amount) in _mined)
                PublishMined(material, amount, center);
            if (_removed.Count > 0)
                CheckIntegrity(_removed);
        }

        /// <summary>Ищет куски, оставшиеся без опоры после удаления клеток, и обрушивает их.</summary>
        private void CheckIntegrity(List<int3> removed)
        {
            _islands.Clear();
            _integrity.FindUnsupportedIslands(removed, _islands);
            foreach (var island in _islands)
                Collapse(island);
        }

        private void Collapse(List<int3> island)
        {
            _islandValues.Clear();
            foreach (var cell in island)
                _islandValues.Add(_world.GetCell(cell));

            using (_world.BeginBatch())
            {
                foreach (var cell in island)
                {
                    Cell empty = _world.EmptyCellAt(cell);
                    _world.SetCell(cell, empty);
                    Broadcast(cell, empty, 0);
                }
            }

            if (_debris != null)
                _debris.Spawn(island, _islandValues);
        }

        private void BroadcastChange(int3 position, Cell cell) => Broadcast(position, cell, 0);

        private static void PublishMined(ushort material, float amount, int3 position) =>
            PublishMined(material, amount, new Vector3(position.x + 0.5f, position.y + 0.5f, position.z + 0.5f));

        private static void PublishMined(ushort material, float amount, Vector3 position) =>
            EventBus<MaterialMinedEvent>.Publish(new MaterialMinedEvent { Material = material, Amount = amount, Position = position });

        private void OnBlockChanged(in BlockChangedEvent evt)
        {
            // Авторитет уже применил изменение. Клиент (кооп) применяет его здесь.
            if (_session.IsAuthority)
                return;
            _world.SetCell(evt.Position, evt.Cell);
            _world.SetDamage(evt.Position, evt.Damage);
        }

        private void Broadcast(int3 position, Cell cell, int damage) =>
            _session.Broadcast(new BlockChangedEvent { Position = position, Cell = cell, Damage = damage });
    }
}
