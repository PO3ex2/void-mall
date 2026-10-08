using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace VoidMall.World
{
    /// <summary>Изменённый проп в сейве: ID и вид — строками (JsonUtility не пишет ulong, индексы реестра могут меняться).</summary>
    [Serializable]
    public sealed class SavedProp
    {
        public string Id;
        public string Def;
        public bool Removed, Loose, WakeOnSpawn;
        public Vector3 Position;
        public Quaternion Rotation = Quaternion.identity;
        public int Damage;
        public string Parent;
        public List<Vector3Int> Anchors = new();
        public bool HasVoxel;
        public SavedVoxel Voxel = new();
    }

    /// <summary>Клетки отломанного куска (этап 8Е) в сейве.</summary>
    [Serializable]
    public sealed class SavedVoxel
    {
        public Vector3Int UvOrigin;
        public List<SavedVoxelCell> Cells = new();
    }

    [Serializable]
    public sealed class SavedVoxelCell
    {
        public Vector3Int P;
        public string Material;
        public int Value, Meta, Damage;
    }

    [Serializable]
    public sealed class PropsData
    {
        /// <summary>Следующий ID созданного игроком пропа (строкой).</summary>
        public string NextId;
        public List<SavedProp> Props = new();
    }

    /// <summary>Сейв пропов (этап 9): все изменённые за игру состояния — сдвинутые, сломанные, прибитые, группы, куски.</summary>
    public sealed partial class PropManager
    {
        public const string VoxelDefId = "prop.voxel_body";

        public PropsData Capture()
        {
            // Поза живых свободных пропов — в состояние (спящие уже записаны, летящие — где сейчас).
            foreach (var (id, body) in _live)
                if (body != null && body.Parent == null && _states.ContainsKey(id))
                    StorePose(body);

            var data = new PropsData { NextId = _nextSpawnedId.ToString() };
            foreach (var state in _states.Values)
            {
                var saved = new SavedProp
                {
                    Id = state.Id.ToString(),
                    Def = state.Def == VoxelDefIndex ? VoxelDefId : Props.Get(state.Def).Id,
                    Removed = state.Removed,
                    Loose = state.Loose,
                    WakeOnSpawn = state.WakeOnSpawn,
                    Position = state.Position,
                    Rotation = state.Rotation,
                    Damage = state.Damage,
                    Parent = state.Parent != 0 ? state.Parent.ToString() : null,
                };
                foreach (var anchor in state.Anchors)
                    saved.Anchors.Add(new Vector3Int(anchor.x, anchor.y, anchor.z));
                if (state.Voxel != null)
                {
                    saved.HasVoxel = true;
                    saved.Voxel.UvOrigin = new Vector3Int(state.Voxel.UvOrigin.x, state.Voxel.UvOrigin.y, state.Voxel.UvOrigin.z);
                    foreach (var (cell, value) in state.Voxel.Cells)
                        saved.Voxel.Cells.Add(new SavedVoxelCell
                        {
                            P = new Vector3Int(cell.x, cell.y, cell.z),
                            Material = _world.Materials.Get(value.Material).Id,
                            Value = value.Value,
                            Meta = value.Meta,
                            Damage = state.Voxel.GetDamage(cell),
                        });
                }
                data.Props.Add(saved);
            }
            return data;
        }

        /// <summary>Вернуть состояния из сейва (до появления пропов: сразу после LoadWorld). Неизвестные виды пропускаются.</summary>
        public int Restore(PropsData data)
        {
            OnWorldReset();
            int skipped = 0;
            foreach (var saved in data.Props)
            {
                if (!ulong.TryParse(saved.Id, out ulong id))
                {
                    skipped++;
                    continue;
                }
                ushort def;
                if (saved.Def == VoxelDefId)
                    def = VoxelDefIndex;
                else if (!Props.TryGetIndex(saved.Def, out def))
                {
                    skipped++;
                    continue;
                }
                var state = new PropState
                {
                    Id = id,
                    Def = def,
                    Removed = saved.Removed,
                    Loose = saved.Loose,
                    WakeOnSpawn = saved.WakeOnSpawn,
                    Position = saved.Position,
                    Rotation = saved.Rotation,
                    Damage = saved.Damage,
                    Parent = ulong.TryParse(saved.Parent, out ulong parent) ? parent : 0,
                };
                foreach (var anchor in saved.Anchors)
                    state.Anchors.Add(new int3(anchor.x, anchor.y, anchor.z));
                if (def == VoxelDefIndex)
                {
                    state.Voxel = RestoreVoxel(saved);
                    if (state.Voxel.Count == 0)
                        state.Removed = true; // все материалы куска убраны из игры
                }
                _states[id] = state;
                foreach (var cell in state.Anchors)
                {
                    if (!_anchorIndex.TryGetValue(cell, out var list))
                        _anchorIndex[cell] = list = new List<ulong>();
                    list.Add(id);
                }
            }
            foreach (var state in _states.Values)
                if (state.Parent != 0 && _states.ContainsKey(state.Parent))
                    AddChild(state.Parent, state.Id);
                else
                    state.Parent = 0;
            if (ulong.TryParse(data.NextId, out ulong next))
                _nextSpawnedId = Math.Max(next, PlayerMadeIdBase);
            return skipped;
        }

        private VoxelGrid RestoreVoxel(SavedProp saved)
        {
            var grid = new VoxelGrid { UvOrigin = new int3(saved.Voxel.UvOrigin.x, saved.Voxel.UvOrigin.y, saved.Voxel.UvOrigin.z) };
            foreach (var cell in saved.Voxel.Cells)
            {
                if (!_world.Materials.TryGetIndex(cell.Material, out ushort material))
                    continue;
                var p = new int3(cell.P.x, cell.P.y, cell.P.z);
                grid.Set(p, new Cell(material, (byte)cell.Value, (byte)cell.Meta));
                grid.SetDamage(p, cell.Damage);
            }
            return grid;
        }
    }
}
