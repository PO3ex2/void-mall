using System.Collections.Generic;
using UnityEngine;

namespace VoidMall.Items
{
    /// <summary>Набор предметов мира (порядок = индекс в ItemStack.Def).</summary>
    [CreateAssetMenu(menuName = "Void Mall/Items/Item Set", fileName = "ItemSet")]
    public sealed class ItemSet : ScriptableObject
    {
        public List<ItemDef> Items = new();
    }
}
