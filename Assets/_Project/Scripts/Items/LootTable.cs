using System;
using System.Collections.Generic;
using UnityEngine;
using VoidMall.Core;

namespace VoidMall.Items
{
    [Serializable]
    public struct LootEntry
    {
        public ItemDef Item;
        [Min(0f)] public float Weight;
        [Min(1)] public int Min;
        [Min(1)] public int Max;
    }

    /// <summary>Таблица лута раздела: что лежит на полках (взвешенный выбор, количество в стаке).</summary>
    [CreateAssetMenu(menuName = "Void Mall/Items/Loot Table", fileName = "LootTable")]
    public sealed class LootTable : ScriptableObject
    {
        [Tooltip("ID раздела (section.grocery) или пусто — таблица по умолчанию.")]
        public string Section;

        [Tooltip("Доля заполненных мест на полках (умножается на WorldSettings.LootAmount).")]
        [Range(0f, 1f)] public float Fill = 0.6f;

        public List<LootEntry> Entries = new();

        private float _total;

        public void Build()
        {
            _total = 0f;
            foreach (var entry in Entries)
                if (entry.Item != null)
                    _total += entry.Weight;
        }

        /// <summary>Детерминированный выбор по хешу (одинаков у всех игроков и при повторном появлении полки).</summary>
        public bool Roll(uint hash, out ItemDef item, out int count)
        {
            item = null;
            count = 0;
            if (_total <= 0f)
                return false;
            float roll = Hash.ToFloat01(hash) * _total;
            foreach (var entry in Entries)
            {
                if (entry.Item == null)
                    continue;
                roll -= entry.Weight;
                if (roll > 0f)
                    continue;
                item = entry.Item;
                int min = Mathf.Max(1, entry.Min), max = Mathf.Max(min, entry.Max);
                count = min + (int)(Hash.Get(hash, 77) % (uint)(max - min + 1));
                return true;
            }
            return false;
        }
    }
}
