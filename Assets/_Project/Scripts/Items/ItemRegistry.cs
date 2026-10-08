using System;
using System.Collections.Generic;
using UnityEngine;
using VoidMall.Core;

namespace VoidMall.Items
{
    /// <summary>
    /// Реестр предметов на сессию: ID ↔ индекс, размеры и веса для сетки, связи с миром
    /// (какой предмет ставит блок и выпадает из него, какой — гладкий материал), меши моделей.
    /// </summary>
    public sealed class ItemRegistry : IItemCatalog, IItemIds, IDisposable
    {
        private readonly Registry<ItemDef> _registry = new();
        private readonly Dictionary<string, ushort> _blockItems = new();
        private readonly Dictionary<string, ushort> _smoothItems = new();
        private readonly List<Mesh> _meshes = new();
        private readonly List<Mesh> _owned = new();

        public ItemRegistry(ItemSet set)
        {
            if (set == null || set.Items.Count == 0)
                throw new ArgumentException("Item set is empty.");
            foreach (var def in set.Items)
            {
                _registry.Register(def);
                _meshes.Add(null);
                if (def.PlacesBlock != null)
                    _blockItems[def.PlacesBlock.Id] = (ushort)def.RuntimeIndex;
                if (def.SmoothMaterial != null)
                    _smoothItems[def.SmoothMaterial.Id] = (ushort)def.RuntimeIndex;
            }
        }

        public int Count => _registry.Count;
        public IReadOnlyList<ItemDef> All => _registry.All;

        public ItemDef Get(int index) => _registry.Get(index);

        public string IdOf(ushort def) => _registry.Get(def).Id;

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

        /// <summary>Предмет, который выпадает из блока материала (и ставит его).</summary>
        public bool TryGetBlockItem(string materialId, out ushort item) => _blockItems.TryGetValue(materialId, out item);

        /// <summary>Предмет, который выпадает при копании гладкого материала.</summary>
        public bool TryGetSmoothItem(string materialId, out ushort item) => _smoothItems.TryGetValue(materialId, out item);

        public (int width, int height) GridSize(ushort def)
        {
            var size = _registry.Get(def).GridSize;
            return (Mathf.Max(1, size.x), Mathf.Max(1, size.y));
        }

        public int MaxStack(ushort def) => Mathf.Max(1, _registry.Get(def).MaxStack);

        public float Weight(ushort def) => _registry.Get(def).Weight;

        /// <summary>Новый стак; у контейнеров (рюкзаков) — с пустой сеткой внутри.</summary>
        public ItemStack Create(ushort def, int count = 1)
        {
            var stack = new ItemStack(def, count);
            var item = _registry.Get(def);
            if (item.IsContainer)
                stack.Contents = new GridInventory(this, item.ContainerGrid.x, item.ContainerGrid.y);
            if (item.ShelfLifeHours > 0f)
                Freshness.Start(stack, item.ShelfLifeHours);
            return stack;
        }

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
                mesh = ItemMeshes.Build(def.Shape, def.ModelSize, def.Tint);
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
