using System.Collections.Generic;
using UnityEngine;

namespace VoidMall.Items
{
    /// <summary>Таблицы лута по разделам.</summary>
    [CreateAssetMenu(menuName = "Void Mall/Items/Loot Table Set", fileName = "LootTableSet")]
    public sealed class LootTableSet : ScriptableObject
    {
        public LootTable Default;
        public List<LootTable> Tables = new();

        private readonly Dictionary<string, LootTable> _bySection = new();

        public void Build(ItemRegistry items)
        {
            _bySection.Clear();
            Default?.Build();
            foreach (var table in Tables)
            {
                if (table == null)
                    continue;
                table.Build();
                if (!string.IsNullOrEmpty(table.Section))
                    _bySection[table.Section] = table;
            }
        }

        public LootTable ForSection(string sectionId) =>
            sectionId != null && _bySection.TryGetValue(sectionId, out var table) ? table : Default;
    }
}
