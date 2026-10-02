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

        private ChunkManager _world;
        private INetSession _session;

        private void Awake() => _world = GetComponent<ChunkManager>();

        private void OnEnable()
        {
            if (!Services.TryGet(out _session))
                return;
            _session.RegisterCommandHandler<DamageBlockCommand>(OnDamage);
            _session.RegisterCommandHandler<PlaceBlockCommand>(OnPlace);
            _session.RegisterCommandHandler<SetBlockCommand>(OnSet);
            _session.RegisterCommandHandler<FillBlocksCommand>(OnFill);
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

            int damage = _world.GetDamage(command.Position) + command.Amount;
            if (damage >= info.Hardness)
            {
                _world.SetCell(command.Position, Cell.Air);
                Broadcast(command.Position, Cell.Air, 0);
            }
            else
            {
                _world.SetDamage(command.Position, damage);
                Broadcast(command.Position, cell, damage);
            }
        }

        private void OnPlace(PlayerId sender, in PlaceBlockCommand command)
        {
            if (!_session.IsAuthority || command.Material == 0 || command.Material >= _world.Materials.Count)
                return;
            if (!_world.IsLoaded(command.Position) || !_world.GetCell(command.Position).IsAir)
                return;
            var def = _world.Materials.Get(command.Material);
            if (!def.Placeable || def.Kind != MaterialKind.Block)
                return;

            // Нельзя поставить блок внутрь игрока, пропа или врага.
            Vector3 center = new Vector3(command.Position.x, command.Position.y, command.Position.z) + Vector3.one * 0.5f;
            if (Physics.CheckBox(center, Vector3.one * 0.49f, Quaternion.identity, EntityMask, QueryTriggerInteraction.Ignore))
                return;

            var cell = new Cell(command.Material);
            if (_world.SetCell(command.Position, cell))
                Broadcast(command.Position, cell, 0);
        }

        private void OnSet(PlayerId sender, in SetBlockCommand command)
        {
            if (!_session.IsAuthority)
                return;
            if (_world.SetCell(command.Position, command.Cell))
                Broadcast(command.Position, command.Cell, 0);
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
