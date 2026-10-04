using System;
using System.Collections.Generic;
using UnityEngine;
using VoidMall.Core;
using VoidMall.World;

namespace VoidMall.Items
{
    /// <summary>Предмет и количество: ингредиент рецепта, цена починки.</summary>
    [Serializable]
    public struct ItemAmount
    {
        public ItemDef Item;
        [Min(1)] public int Count;

        public ItemAmount(ItemDef item, int count)
        {
            Item = item;
            Count = count;
        }
    }

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
        Blueprint,
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
        Torch,
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

    /// <summary>Что лечит лекарство (можно несколько).</summary>
    [System.Flags]
    public enum MedicalTreatment
    {
        None = 0,
        Bleeding = 1,
        Fracture = 2,
        Pain = 4,
        Infection = 8,
        Poisoning = 16,
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

        [Header("Износ и починка")]
        [Tooltip("Сколько применений выдерживает (удар, применение бура, секунда горения факела). 0 — не изнашивается.")]
        [Min(0)] public int Uses;

        [Tooltip("Чем чинится на верстаке (до 100%). Пусто — не чинится.")]
        public List<ItemAmount> RepairCost = new();

        [Tooltip("Электроинструмент: чинить можно только с навыком «Техник».")]
        public bool Electric;

        [Header("Еда и вода")]
        [Tooltip("Ккал.")] public float Calories;
        [Tooltip("Граммы жиров, белков, углеводов — для баланса рациона за сутки.")]
        public float Fat;
        public float Protein;
        public float Carbs;
        [Tooltip("Мл воды.")] public float Water;

        [Tooltip("Приготовлено на плите: +рассудок, навык «Повар» прибавляет калорий.")]
        public bool Cooked;

        [Tooltip("Сколько секунд горит в плите или горелке. 0 — не топливо.")]
        [Min(0f)] public float FuelSeconds;

        [Header("Лекарство")]
        [Tooltip("Что лечит.")]
        public MedicalTreatment Treats;

        [Tooltip("Сколько здоровья восстанавливает.")]
        [Min(0f)] public float Heal;

        [Header("Экипировка")]
        public EquipSlot Slot;
        public EquipLayer Layer;

        [Tooltip("Защита (этап 7–8).")]
        [Min(0f)] public float Armor;

        [Tooltip("Утепление одежды, °C: на столько теплее ощущается окружение.")]
        public float Warmth;

        [Header("Контейнер (рюкзак)")]
        [Tooltip("Размер сетки внутри, клеток. 0 — не контейнер.")]
        public Vector2Int ContainerGrid;

        [Header("Строительство")]
        [Tooltip("Блок, который ставится этим предметом (и выпадает из такого блока).")]
        public MaterialDef PlacesBlock;

        [Tooltip("Гладкий материал, который выпадает при копании и тратится буром при наращивании.")]
        public MaterialDef SmoothMaterial;

        [Tooltip("ID пропа, который ставится этим предметом (спальник). Пусто — не ставится.")]
        public string PlacesProp;

        [Tooltip("Контейнер, который при подборе раскладывается в инвентарь (сумка с вещами после смерти).")]
        public bool UnpackOnPickup;

        [Tooltip("Чертёж: ID рецепта, который открывается при чтении (ПКМ).")]
        public string TeachesRecipe;

        public bool IsContainer => ContainerGrid.x > 0 && ContainerGrid.y > 0;
        public bool IsTool => Tool != ToolKind.None;
        public bool IsEquippable => Slot != EquipSlot.None;
        public bool IsConsumable => Category is ItemCategory.Food or ItemCategory.Drink or ItemCategory.Medicine;
        public bool IsReadable => !string.IsNullOrEmpty(TeachesRecipe);
        public bool HasWear => Uses > 0;
        public bool IsRepairable => RepairCost != null && RepairCost.Count > 0;
    }
}
