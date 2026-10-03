using UnityEngine;
using VoidMall.Core;
using VoidMall.World;

namespace VoidMall.Items
{
    public enum ItemCategory
    {
        Material,
        Block,
        Tool,
        Food,
        Drink,
        Medicine,
        Clothing,
        Armor,
        Backpack,
        Misc,
    }

    /// <summary>Форма процедурной модели предмета (свою модель можно подставить в ItemDef.Mesh).</summary>
    public enum ItemShape : byte
    {
        Box,
        Can,
        Bottle,
        Pack,
        Bag,
        Pile,
        Cube,
        Hammer,
        Sledgehammer,
        Crowbar,
        Drill,
        Flashlight,
        Clothing,
        Helmet,
        Boots,
        Backpack,
        Plank,
        Pipe,
        Coil,
    }

    public enum ToolKind : byte
    {
        None,
        Hammer,
        Sledgehammer,
        Crowbar,
        Drill,
        Flashlight,
    }

    /// <summary>Слот экипировки (GDD §5.1): у каждого — слой брони и слой одежды.</summary>
    public enum EquipSlot : byte
    {
        None,
        Head,
        Torso,
        Arms,
        Legs,
        Feet,
        Backpack,
    }

    public enum EquipLayer : byte
    {
        Armor,
        Clothing,
    }

    /// <summary>
    /// Описание предмета (ARCHITECTURE §8.1). Компоненты — группы полей: инструмент, еда, экипировка, контейнер,
    /// строительный материал. Незаданная группа просто не используется.
    /// </summary>
    [CreateAssetMenu(menuName = "Void Mall/Items/Item", fileName = "Item")]
    public sealed class ItemDef : ContentDef
    {
        public string DisplayName;
        [TextArea] public string Description;
        public ItemCategory Category;

        [Header("Модель и иконка")]
        public ItemShape Shape;
        public Color Tint = Color.white;

        [Tooltip("Габарит модели в мире, м (опорная точка — центр низа).")]
        public Vector3 ModelSize = new(0.2f, 0.2f, 0.2f);

        [Tooltip("Своя модель вместо процедурной.")]
        public Mesh Mesh;
        public Material Material;
        public Texture2D Icon;

        [Header("Инвентарь")]
        [Tooltip("Размер в клетках сетки (ширина × высота).")]
        public Vector2Int GridSize = Vector2Int.one;

        [Tooltip("Вес одной штуки, кг.")]
        [Min(0f)] public float Weight = 0.5f;

        [Min(1)] public int MaxStack = 1;

        [Header("Инструмент")]
        public ToolKind Tool;
        [Min(0)] public int ToolTier;

        [Tooltip("Урон по блокам за удар.")]
        [Min(0)] public int BlockDamage;

        [Tooltip("Урон по пропам за удар.")]
        [Min(0)] public int PropDamage;

        [Tooltip("Интервал между ударами, с.")]
        [Min(0.05f)] public float SwingInterval = 0.35f;

        [Tooltip("Сила бура за применение (0…1).")]
        [Min(0f)] public float DrillStrength;

        [Header("Еда и вода (этап 7)")]
        public float Calories;
        public float Fat;
        public float Protein;
        public float Carbs;
        public float Water;

        [Header("Экипировка")]
        public EquipSlot Slot;
        public EquipLayer Layer;

        [Tooltip("Защита (этап 7–8).")]
        [Min(0f)] public float Armor;

        [Header("Контейнер (рюкзак)")]
        [Tooltip("Размер сетки внутри, клеток. 0 — не контейнер.")]
        public Vector2Int ContainerGrid;

        [Header("Строительство")]
        [Tooltip("Блок, который ставится этим предметом (и выпадает из такого блока).")]
        public MaterialDef PlacesBlock;

        [Tooltip("Гладкий материал, который выпадает при копании и тратится буром при наращивании.")]
        public MaterialDef SmoothMaterial;

        public bool IsContainer => ContainerGrid.x > 0 && ContainerGrid.y > 0;
        public bool IsTool => Tool != ToolKind.None;
        public bool IsEquippable => Slot != EquipSlot.None;
        public bool IsConsumable => Category is ItemCategory.Food or ItemCategory.Drink or ItemCategory.Medicine;
    }
}
