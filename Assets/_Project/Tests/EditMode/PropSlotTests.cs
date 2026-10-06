using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using VoidMall.World;

namespace VoidMall.Tests
{
    /// <summary>
    /// Места под товар не должны лежать внутри коллайдера пропа: иначе при пробуждении (толкнули, задели)
    /// товары становятся физическими предметами внутри коробки и выпрыгивают (так было со стойкой у кассы).
    /// </summary>
    public sealed class PropSlotTests
    {
        [Test]
        public void ItemSlots_DoNotOverlapColliders()
        {
            var slots = new List<PropItemSlot>();
            var parts = new List<Bounds>();
            for (var shape = (PropShape)0; shape < PropShape.Count; shape++)
            {
                Vector3 size = PropMeshes.DefaultSize(shape);
                PropMeshes.GetItemSlots(shape, size, slots);
                if (slots.Count == 0)
                    continue;
                PropMeshes.GetColliderParts(shape, size, parts);
                if (parts.Count == 0)
                    parts.Add(new Bounds(new Vector3(0f, size.y * 0.5f, 0f), size));
                for (int i = 0; i < slots.Count; i++)
                {
                    var slot = slots[i];
                    // Объём над точкой места, чуть ужатый: касаться доски снизу можно, заходить внутрь — нет.
                    var volume = new Bounds(slot.Position + Vector3.up * slot.Size.y * 0.5f, slot.Size - Vector3.one * 0.02f);
                    foreach (var part in parts)
                        Assert.IsFalse(volume.Intersects(part), $"{shape}: место {i} внутри коллайдера {part}");
                }
            }
        }
    }
}
