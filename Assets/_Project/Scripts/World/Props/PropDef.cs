using System;
using System.Collections.Generic;
using UnityEngine;
using VoidMall.Core;

namespace VoidMall.World
{
    /// <summary>Что получается из пропа: при аккуратном разборе — Disassembled штук, при поломке — Broken.</summary>
    [Serializable]
    public struct PropYield
    {
        [Tooltip("ID материала (на этапе 6 — ID предмета), например material.plank.")]
        public string Material;
        public int Disassembled;
        public int Broken;
    }

    /// <summary>
    /// Описание физического пропа (мебель, коробки, машины): размер, масса, прочность, разбор.
    /// Модель — процедурная по Shape и Size, либо своя (Mesh + Material). Коллайдер — коробка по Size.
    /// </summary>
    [CreateAssetMenu(menuName = "Void Mall/Props/Prop", fileName = "Prop")]
    public sealed class PropDef : ContentDef
    {
        public string DisplayName;

        [Header("Модель")]
        public PropShape Shape;

        [Tooltip("Габарит (ширина X, высота Y, глубина Z), м. Опорная точка — центр низа, «вперёд» — +Z.")]
        public Vector3 Size = Vector3.one;

        [Tooltip("Своя модель вместо процедурной (опорная точка — центр низа).")]
        public Mesh Mesh;

        [Tooltip("Материал своей модели (по умолчанию — общий материал пропов с цветом вершин).")]
        public Material Material;

        [Header("Физика")]
        [Min(0.1f)] public float Mass = 10f;

        [Tooltip("Прочность, условные HP. Временный молоток наносит 100 за удар.")]
        [Min(1)] public int Hardness = 200;

        [Header("Разбор")]
        [Tooltip("Время аккуратного разбора, с.")]
        [Min(0.1f)] public float DisassembleTime = 3f;

        public List<PropYield> Yields = new();

        [Tooltip("Цвет пыли и обломков при поломке.")]
        public Color DebrisColor = new(0.6f, 0.58f, 0.55f);

        [Tooltip("На этом можно спать (кровать, спальник): восстанавливает бодрость, задаёт точку возрождения.")]
        public bool Sleepable;

        [Tooltip("Качество сна: 1 — кровать и спальник, меньше — бодрость восстанавливается медленнее (диван 0.7).")]
        [Range(0.1f, 1f)] public float SleepQuality = 1f;

        [Tooltip("Удержание E сворачивает проп обратно в предмет (выход разбора) — спальник.")]
        public bool PackUp;

        [Header("Крафтовая станция")]
        [Tooltip("Какая станция (E — открыть окно крафта). None — не станция.")]
        public CraftStation Station;

        [Tooltip("Скорость крафта станции (плита 1, горелка 0.7).")]
        [Min(0.1f)] public float StationSpeed = 1f;

        [Tooltip("Крафтит сама, без открытого окна (плита, горелка — пока есть топливо).")]
        public bool StationAutonomous;

        [Header("Тепло")]
        [Tooltip("Горит, пока есть топливо, даже без готовки (бочка-костёр). Иначе греет только во время готовки.")]
        public bool Heater;

        [Tooltip("Прибавка к температуре вплотную, пока горит, °C (0 — не греет).")]
        public float HeatOutput;

        [Tooltip("Докуда доходит тепло, м.")]
        public float HeatRadius = 2f;
    }

    /// <summary>Реестр пропов на сессию: ID ↔ индекс, меши (процедурные строятся один раз).</summary>
    public sealed class PropRegistry : IDisposable
    {
        private readonly Registry<PropDef> _registry = new();
        private readonly List<Mesh> _meshes = new();
        private readonly List<Mesh> _owned = new();

        public PropRegistry(PropSet set)
        {
            if (set == null || set.Props.Count == 0)
                throw new ArgumentException("Prop set is empty.");
            foreach (var def in set.Props)
                _registry.Register(def);
            for (int i = 0; i < _registry.Count; i++)
                _meshes.Add(null);
        }

        public int Count => _registry.Count;
        public IReadOnlyList<PropDef> All => _registry.All;

        public PropDef Get(int index) => _registry.Get(index);

        public ushort IndexOf(string id) => (ushort)_registry.Get(id).RuntimeIndex;

        public bool TryGetIndex(string id, out ushort index)
        {
            if (_registry.TryGet(id, out var def))
            {
                index = (ushort)def.RuntimeIndex;
                return true;
            }
            index = 0;
            return false;
        }

        public Vector3 Size(ushort index) => _registry.Get(index).Size;

        public Mesh GetMesh(ushort index)
        {
            var mesh = _meshes[index];
            if (mesh != null)
                return mesh;
            var def = _registry.Get(index);
            if (def.Mesh != null)
            {
                mesh = def.Mesh;
            }
            else
            {
                mesh = PropMeshes.Build(def.Shape, def.Size);
                _owned.Add(mesh);
            }
            _meshes[index] = mesh;
            return mesh;
        }

        public void Dispose()
        {
            foreach (var mesh in _owned)
                if (mesh != null)
                    UnityEngine.Object.Destroy(mesh);
            _owned.Clear();
            _meshes.Clear();
            _registry.Clear();
        }
    }
}
