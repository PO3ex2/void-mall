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
            if ((info.Flags & MaterialFlags.Unbreakable) != 0 || info.Kind != MaterialKind.Block)
                return; // гладкие массивы — только буром

            int damage = _world.GetDamage(command.Position) + command.Amount;
            if (damage >= info.Hardness)
            {
                _world.SetCell(command.Position, Cell.Air);
                Broadcast(command.Position, Cell.Air, 0);
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
            var cell = new Cell(command.Material);
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
            if (!def.Placeable || def.Kind != MaterialKind.Block)
                return false;
            Vector3 center = new Vector3(position.x, position.y, position.z) + Vector3.one * 0.5f;
            return !Physics.CheckBox(center, Vector3.one * 0.49f, Quaternion.identity, EntityMask, QueryTriggerInteraction.Ignore);
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
                    _world.SetCell(cell, Cell.Air);
                    Broadcast(cell, Cell.Air, 0);
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
