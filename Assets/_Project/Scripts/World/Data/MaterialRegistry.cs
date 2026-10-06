using System;
using System.Collections.Generic;
using Unity.Collections;
using VoidMall.Core;

namespace VoidMall.World
{
    [Flags]
    public enum MaterialFlags : byte
    {
        None = 0,
        Solid = 1 << 0,
        Opaque = 1 << 1,
        Transparent = 1 << 2,
        Unbreakable = 1 << 3,
    }

    /// <summary>Сжатое описание материала для Burst-джобов.</summary>
    public struct MaterialInfo
    {
        public MaterialKind Kind;
        public MaterialFlags Flags;
        public ushort TextureLayer;

        /// <summary>Слой текстуры боковых граней (±X, ±Z); у большинства — как TextureLayer.</summary>
        public ushort SideLayer;
        public int Hardness;
        public LampKind Lamp;

        /// <summary>Гладкий двойник блока (0 — нет).</summary>
        public ushort SmoothVariant;

        /// <summary>Функциональный блок: что делает и размер в клетках (без поворота).</summary>
        public FunctionalKind Function;
        public Unity.Mathematics.int3 FunctionalSize;

        public bool IsSolid => (Flags & MaterialFlags.Solid) != 0;
        public bool IsOpaque => (Flags & MaterialFlags.Opaque) != 0;
        public bool IsTransparent => (Flags & MaterialFlags.Transparent) != 0;
        public bool IsLiquid => Kind == MaterialKind.Liquid;
    }

    /// <summary>Реестр материалов мира: ID ↔ индекс ↔ описание, плюс NativeArray для джобов.</summary>
    public sealed class MaterialRegistry : IDisposable
    {
        private readonly Registry<MaterialDef> _registry = new();

        public NativeArray<MaterialInfo> Infos { get; private set; }
        public IReadOnlyList<MaterialDef> All => _registry.All;
        public int Count => _registry.Count;

        public MaterialRegistry(MaterialCatalog catalog)
        {
            if (catalog == null || catalog.Materials.Count == 0)
                throw new ArgumentException("Material catalog is empty.");
            if (catalog.Materials[0].Kind != MaterialKind.Air)
                throw new ArgumentException("The first material in the catalog must be air.");

            foreach (var def in catalog.Materials)
                _registry.Register(def);
        }

        /// <summary>Строит массив для джобов. Слои текстур назначает построитель массива текстур.</summary>
        public void BuildInfos(IReadOnlyList<ushort> textureLayers)
        {
            if (Infos.IsCreated)
                Infos.Dispose();
            var infos = new NativeArray<MaterialInfo>(Count, Allocator.Persistent);
            for (int i = 0; i < Count; i++)
            {
                var def = _registry.Get(i);
                var flags = MaterialFlags.None;
                if (def.Kind != MaterialKind.Air && def.Kind != MaterialKind.Liquid)
                    flags |= MaterialFlags.Solid;
                // Функциональные клетки (двери) не закрывают грани соседей: модель тоньше клетки.
                if (def.Kind is MaterialKind.Block or MaterialKind.Smooth && !def.Transparent)
                    flags |= MaterialFlags.Opaque;
                if (def.Transparent)
                    flags |= MaterialFlags.Transparent;
                if (def.Unbreakable)
                    flags |= MaterialFlags.Unbreakable;
                infos[i] = new MaterialInfo
                {
                    Kind = def.Kind,
                    Flags = flags,
                    TextureLayer = textureLayers[i],
                    SideLayer = def.SideMaterial != null && def.SideMaterial.RuntimeIndex >= 0 ? textureLayers[def.SideMaterial.RuntimeIndex] : textureLayers[i],
                    Hardness = def.Hardness,
                    Lamp = def.Lamp,
                    SmoothVariant = def.SmoothVariant != null && def.SmoothVariant.RuntimeIndex >= 0 ? (ushort)def.SmoothVariant.RuntimeIndex : (ushort)0,
                    Function = def.Function,
                    FunctionalSize = new Unity.Mathematics.int3(Math.Max(1, def.FunctionalSize.x), Math.Max(1, def.FunctionalSize.y), Math.Max(1, def.FunctionalSize.z)),
                };
            }
            Infos = infos;
        }

        public MaterialDef Get(int index) => _registry.Get(index);

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

        public void Dispose()
        {
            if (Infos.IsCreated)
                Infos.Dispose();
            _registry.Clear();
        }
    }
}
