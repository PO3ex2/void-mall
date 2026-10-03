using UnityEngine;
using VoidMall.Console;

namespace VoidMall.Items
{
    /// <summary>
    /// Точка входа системы предметов в сцене мира: реестр предметов, таблицы лута, общий материал моделей.
    /// Полки с товарами (ShelfLoot) и брошенные предметы (WorldItems) — соседние компоненты.
    /// </summary>
    [DefaultExecutionOrder(-170)]
    public sealed class ItemSystem : MonoBehaviour
    {
        public static readonly CVar<bool> Creative = new("sv_creative", false, "Режим творчества: блоки и гвозди не тратятся.", ConsoleFlags.Cheat);

        [SerializeField] private ItemSet _items;
        [SerializeField] private LootTableSet _loot;
        [SerializeField] private Material _material;

        public static ItemSystem Instance { get; private set; }

        public ItemRegistry Items { get; private set; }
        public LootTableSet Loot => _loot;
        public Material Material => _material;

        private void Awake()
        {
            Instance = this;
            if (_items == null)
            {
                Debug.LogError("[Items] ItemSet is not assigned.", this);
                enabled = false;
                return;
            }
            Items = new ItemRegistry(_items);
            var ids = new System.Collections.Generic.List<string>();
            foreach (var def in Items.All)
                ids.Add(def.Id);
            DevConsole.RegisterCompleter("give", token => token == 0 ? ids : null);
            DevConsole.RegisterCompleter("items_list", token => token == 0 ? new[] { "tool", "food", "drink", "medicine", "clothing", "armor", "backpack", "material", "block" } : null);
            _loot?.Build(Items);
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
            Items?.Dispose();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Instance = null;

        /// <summary>Найти предмет по полному ID (item.hammer) или короткому имени (hammer).</summary>
        public bool TryResolve(string name, out ushort index)
        {
            if (Items.TryGetIndex(name, out index))
                return true;
            foreach (var prefix in new[] { "item.", "material.", "item.block." })
                if (Items.TryGetIndex(prefix + name, out index))
                    return true;
            return false;
        }
    }
}
