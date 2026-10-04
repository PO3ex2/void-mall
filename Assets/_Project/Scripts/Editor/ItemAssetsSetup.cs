using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VoidMall.Items;
using VoidMall.World;

namespace VoidMall.Editor
{
    /// <summary>
    /// Ассеты предметов: описания ItemDef (инструменты, материалы, еда, медикаменты, одежда, броня, рюкзаки),
    /// блоки-предметы для каждого ставимого материала мира, набор ItemSet и таблицы лута по разделам.
    /// </summary>
    public static class ItemAssetsSetup
    {
        public const string ItemsFolder = "Assets/_Project/Data/Items";
        public const string BlocksFolder = ItemsFolder + "/Blocks";
        public const string LootFolder = ItemsFolder + "/Loot";
        public const string ItemSetPath = ItemsFolder + "/ItemSet.asset";
        public const string LootSetPath = LootFolder + "/LootTableSet.asset";

        private sealed class Spec
        {
            public string Id, Name, Description;
            public ItemCategory Category;
            public ItemShape Shape;
            public Vector3 Size;
            public Vector2Int Grid = Vector2Int.one;
            public float Weight;
            public int Stack = 1;
            public Color Tint = Color.white;
            public ToolKind Tool;
            public int Tier, BlockDamage, PropDamage;
            public float Swing = 0.35f, Drill;
            public float Calories, Fat, Protein, Carbs, Water;
            public EquipSlot Slot;
            public EquipLayer Layer;
            public float Armor;
            public Vector2Int Container;
            public string Smooth;
            public MedicalTreatment Treats;
            public float Heal, Warmth;
            public string PlacesProp;
            public bool Unpack;
            public int Uses;
            public (string item, int count)[] Repair;
            public bool Electric, Cooked;
            public float Fuel;
            public string Teaches;
        }

        private static readonly Color Steel = new(0.62f, 0.64f, 0.68f);
        private static readonly Color WoodColor = new(0.6f, 0.45f, 0.3f);
        private static readonly Color Blueprint = new(0.3f, 0.5f, 0.85f);

        private static readonly Spec[] Specs =
        {
            // ── Инструменты ──
            new() { Id = "item.hammer", Name = "Молоток", Description = "Ломает несложное, прибивает пропы гвоздями (ПКМ с пропом в руках).", Category = ItemCategory.Tool, Shape = ItemShape.Hammer, Size = new(0.32f, 0.12f, 0.05f), Grid = new(2, 1), Weight = 0.8f, Tint = Steel, Tool = ToolKind.Hammer, Tier = 1, BlockDamage = 60, PropDamage = 70, Swing = 0.35f, Uses = 300, Repair = new[] { ("material.scrap_metal", 1), ("material.plank", 1) } },
            new() { Id = "item.sledgehammer", Name = "Кувалда", Description = "Тяжело и медленно, зато бетон крошится.", Category = ItemCategory.Tool, Shape = ItemShape.Sledgehammer, Size = new(0.8f, 0.14f, 0.08f), Grid = new(3, 1), Weight = 5f, Tint = Steel, Tool = ToolKind.Sledgehammer, Tier = 2, BlockDamage = 220, PropDamage = 180, Swing = 0.7f, Uses = 350, Repair = new[] { ("material.scrap_metal", 3) } },
            new() { Id = "item.crowbar", Name = "Лом", Description = "Разбирает мебель на раз-два.", Category = ItemCategory.Tool, Shape = ItemShape.Crowbar, Size = new(0.7f, 0.12f, 0.03f), Grid = new(3, 1), Weight = 2.2f, Tint = new(0.55f, 0.12f, 0.1f), Tool = ToolKind.Crowbar, Tier = 1, BlockDamage = 45, PropDamage = 300, Swing = 0.45f, Uses = 400, Repair = new[] { ("material.scrap_metal", 2) } },
            new() { Id = "item.drill", Name = "Перфоратор", Description = "Срезает (ЛКМ) и наращивает (ПКМ, тратит грунт или бетонную крошку) гладкий массив.", Category = ItemCategory.Tool, Shape = ItemShape.Drill, Size = new(0.32f, 0.26f, 0.08f), Grid = new(2, 2), Weight = 2.8f, Tint = new(0.9f, 0.75f, 0.15f), Tool = ToolKind.Drill, Tier = 1, Drill = 0.25f, Swing = 0.08f, Uses = 2000, Electric = true, Repair = new[] { ("material.scrap_metal", 1), ("material.wire", 1) } },
            new() { Id = "item.drill_pro", Name = "Мощный перфоратор", Description = "Копает почти вдвое быстрее обычного.", Category = ItemCategory.Tool, Shape = ItemShape.Drill, Size = new(0.36f, 0.28f, 0.09f), Grid = new(2, 2), Weight = 3.6f, Tint = new(0.95f, 0.45f, 0.1f), Tool = ToolKind.Drill, Tier = 2, Drill = 0.42f, Swing = 0.08f, Uses = 3000, Electric = true, Repair = new[] { ("material.scrap_metal", 2), ("material.wire", 2), ("material.electrical_tape", 1) } },
            new() { Id = "item.flashlight", Name = "Фонарик", Description = "ЛКМ — включить. Светит, пока лежит в инвентаре.", Category = ItemCategory.Tool, Shape = ItemShape.Flashlight, Size = new(0.2f, 0.05f, 0.05f), Weight = 0.3f, Tint = new(0.15f, 0.15f, 0.17f), Tool = ToolKind.Flashlight },

            // ── Материалы ──
            new() { Id = "material.nails", Name = "Гвозди", Description = "Один гвоздь — одно прибивание.", Category = ItemCategory.Material, Shape = ItemShape.Pack, Size = new(0.12f, 0.05f, 0.08f), Weight = 0.01f, Stack = 200, Tint = Steel },
            new() { Id = "material.screws", Name = "Шурупы", Category = ItemCategory.Material, Shape = ItemShape.Pack, Size = new(0.1f, 0.05f, 0.08f), Weight = 0.01f, Stack = 200, Tint = new(0.75f, 0.7f, 0.5f) },
            new() { Id = "material.plank", Name = "Доска", Category = ItemCategory.Material, Shape = ItemShape.Plank, Size = new(1f, 0.03f, 0.12f), Grid = new(3, 1), Weight = 1.5f, Stack = 10, Tint = WoodColor, Fuel = 60f },
            new() { Id = "material.scrap_metal", Name = "Металлолом", Category = ItemCategory.Material, Shape = ItemShape.Pile, Size = new(0.25f, 0.12f, 0.2f), Weight = 0.6f, Stack = 20, Tint = Steel },
            new() { Id = "material.glass_shard", Name = "Осколки стекла", Category = ItemCategory.Material, Shape = ItemShape.Pile, Size = new(0.2f, 0.06f, 0.15f), Weight = 0.2f, Stack = 30, Tint = new(0.7f, 0.85f, 0.9f) },
            new() { Id = "material.plastic", Name = "Пластик", Category = ItemCategory.Material, Shape = ItemShape.Pile, Size = new(0.2f, 0.08f, 0.15f), Weight = 0.1f, Stack = 30, Tint = new(0.25f, 0.5f, 0.85f) },
            new() { Id = "material.cardboard", Name = "Картон", Category = ItemCategory.Material, Shape = ItemShape.Plank, Size = new(0.4f, 0.02f, 0.3f), Weight = 0.1f, Stack = 30, Tint = new(0.65f, 0.5f, 0.32f), Fuel = 20f },
            new() { Id = "material.fabric", Name = "Ткань", Category = ItemCategory.Material, Shape = ItemShape.Pack, Size = new(0.3f, 0.05f, 0.25f), Weight = 0.1f, Stack = 20, Tint = new(0.5f, 0.35f, 0.55f) },
            new() { Id = "material.wire", Name = "Провод", Category = ItemCategory.Material, Shape = ItemShape.Coil, Size = new(0.15f, 0.06f, 0.15f), Weight = 0.15f, Stack = 20, Tint = new(0.85f, 0.3f, 0.15f) },
            new() { Id = "material.fuse", Name = "Предохранитель", Description = "Из щитовых. Понадобится, чтобы оживить щиток.", Category = ItemCategory.Material, Shape = ItemShape.Can, Size = new(0.03f, 0.07f, 0.03f), Weight = 0.03f, Stack = 20, Tint = new(0.85f, 0.8f, 0.7f) },
            new() { Id = "material.electrical_tape", Name = "Изолента", Category = ItemCategory.Material, Shape = ItemShape.Coil, Size = new(0.08f, 0.03f, 0.08f), Weight = 0.05f, Stack = 10, Tint = new(0.15f, 0.25f, 0.75f) },
            new() { Id = "material.rubber", Name = "Резина", Category = ItemCategory.Material, Shape = ItemShape.Pile, Size = new(0.2f, 0.08f, 0.15f), Weight = 0.3f, Stack = 20, Tint = new(0.12f, 0.12f, 0.12f) },
            new() { Id = "material.foam", Name = "Поролон", Category = ItemCategory.Material, Shape = ItemShape.Pack, Size = new(0.3f, 0.08f, 0.25f), Weight = 0.05f, Stack = 20, Tint = new(0.95f, 0.88f, 0.5f) },
            new() { Id = "material.pipe", Name = "Труба", Category = ItemCategory.Material, Shape = ItemShape.Pipe, Size = new(0.8f, 0.05f, 0.05f), Grid = new(3, 1), Weight = 1.2f, Stack = 10, Tint = Steel },
            new() { Id = "material.concrete_rubble", Name = "Бетонная крошка", Description = "Из копания массивов. Перфоратор наращивает ею бетон.", Category = ItemCategory.Material, Shape = ItemShape.Pile, Size = new(0.25f, 0.12f, 0.2f), Weight = 1.5f, Stack = 20, Tint = new(0.6f, 0.6f, 0.62f), Smooth = "smooth.concrete" },
            new() { Id = "material.soil", Name = "Грунт", Description = "Перфоратор наращивает им грунт.", Category = ItemCategory.Material, Shape = ItemShape.Bag, Size = new(0.25f, 0.2f, 0.15f), Weight = 1.2f, Stack = 20, Tint = new(0.45f, 0.34f, 0.22f), Smooth = "smooth.soil" },

            // ── Еда и напитки ──
            new() { Id = "item.canned_beans", Name = "Банка фасоли", Category = ItemCategory.Food, Shape = ItemShape.Can, Size = new(0.08f, 0.11f, 0.08f), Weight = 0.45f, Stack = 5, Tint = new(0.75f, 0.35f, 0.2f), Calories = 350, Fat = 2, Protein = 20, Carbs = 60, Water = 50 },
            new() { Id = "item.canned_meat", Name = "Тушёнка", Category = ItemCategory.Food, Shape = ItemShape.Can, Size = new(0.1f, 0.09f, 0.1f), Weight = 0.35f, Stack = 5, Tint = new(0.6f, 0.62f, 0.66f), Calories = 600, Fat = 40, Protein = 45, Carbs = 2, Water = 30 },
            new() { Id = "item.crackers", Name = "Крекеры", Category = ItemCategory.Food, Shape = ItemShape.Box, Size = new(0.16f, 0.2f, 0.05f), Weight = 0.2f, Stack = 5, Tint = new(0.85f, 0.65f, 0.2f), Calories = 450, Fat = 15, Protein = 8, Carbs = 70 },
            new() { Id = "item.chocolate", Name = "Шоколадка", Category = ItemCategory.Food, Shape = ItemShape.Pack, Size = new(0.15f, 0.015f, 0.07f), Weight = 0.1f, Stack = 10, Tint = new(0.35f, 0.2f, 0.12f), Calories = 540, Fat = 30, Protein = 6, Carbs = 58 },
            new() { Id = "item.chips", Name = "Чипсы", Category = ItemCategory.Food, Shape = ItemShape.Bag, Size = new(0.18f, 0.24f, 0.06f), Weight = 0.15f, Stack = 5, Tint = new(0.95f, 0.8f, 0.15f), Calories = 530, Fat = 34, Protein = 6, Carbs = 52 },
            new() { Id = "item.noodles", Name = "Лапша быстрого приготовления", Category = ItemCategory.Food, Shape = ItemShape.Box, Size = new(0.12f, 0.06f, 0.1f), Weight = 0.1f, Stack = 10, Tint = new(0.9f, 0.3f, 0.2f), Calories = 450, Fat = 18, Protein = 9, Carbs = 62 },
            new() { Id = "item.water_bottle", Name = "Бутылка воды", Category = ItemCategory.Drink, Shape = ItemShape.Bottle, Size = new(0.07f, 0.25f, 0.07f), Grid = new(1, 2), Weight = 0.55f, Stack = 3, Tint = new(0.6f, 0.8f, 0.95f), Water = 500 },
            new() { Id = "item.soda", Name = "Газировка", Category = ItemCategory.Drink, Shape = ItemShape.Can, Size = new(0.065f, 0.12f, 0.065f), Weight = 0.35f, Stack = 6, Tint = new(0.8f, 0.1f, 0.15f), Calories = 140, Carbs = 35, Water = 330 },

            // ── Медикаменты ──
            new() { Id = "item.bandage", Name = "Бинт", Description = "Сильное кровотечение делает слабым, слабое — останавливает.", Treats = MedicalTreatment.Bleeding, Heal = 3f, Category = ItemCategory.Medicine, Shape = ItemShape.Pack, Size = new(0.08f, 0.04f, 0.08f), Weight = 0.05f, Stack = 10, Tint = new(0.95f, 0.95f, 0.92f) },
            new() { Id = "item.painkillers", Name = "Обезболивающее", Description = "Глушит боль на 3 часа: меньше хромоты и тряски.", Treats = MedicalTreatment.Pain, Category = ItemCategory.Medicine, Shape = ItemShape.Box, Size = new(0.08f, 0.04f, 0.05f), Weight = 0.05f, Stack = 10, Tint = new(0.3f, 0.6f, 0.9f) },
            new() { Id = "item.splint", Name = "Шина", Description = "Фиксирует перелом: меньше мешает, за двое суток срастается.", Treats = MedicalTreatment.Fracture, Category = ItemCategory.Medicine, Shape = ItemShape.Plank, Size = new(0.4f, 0.02f, 0.08f), Grid = new(2, 1), Weight = 0.4f, Stack = 3, Tint = new(0.85f, 0.85f, 0.8f) },
            new() { Id = "item.antiseptic", Name = "Антисептик", Description = "Убивает инфекцию в ране.", Treats = MedicalTreatment.Infection, Heal = 2f, Category = ItemCategory.Medicine, Shape = ItemShape.Bottle, Size = new(0.05f, 0.12f, 0.05f), Weight = 0.15f, Stack = 5, Tint = new(0.4f, 0.75f, 0.45f) },

            // ── Одежда ──
            new() { Id = "item.cap", Name = "Кепка", Category = ItemCategory.Clothing, Shape = ItemShape.Helmet, Size = new(0.2f, 0.1f, 0.22f), Weight = 0.1f, Tint = new(0.2f, 0.3f, 0.6f), Slot = EquipSlot.Head, Layer = EquipLayer.Clothing, Warmth = 2f },
            new() { Id = "item.hoodie", Name = "Толстовка", Category = ItemCategory.Clothing, Shape = ItemShape.Clothing, Size = new(0.35f, 0.08f, 0.3f), Grid = new(2, 2), Weight = 0.6f, Tint = new(0.35f, 0.38f, 0.42f), Slot = EquipSlot.Torso, Layer = EquipLayer.Clothing, Warmth = 6f },
            new() { Id = "item.gloves_work", Name = "Рабочие перчатки", Category = ItemCategory.Clothing, Shape = ItemShape.Pack, Size = new(0.15f, 0.04f, 0.12f), Weight = 0.15f, Tint = new(0.75f, 0.6f, 0.2f), Slot = EquipSlot.Arms, Layer = EquipLayer.Clothing, Warmth = 1f },
            new() { Id = "item.jeans", Name = "Джинсы", Category = ItemCategory.Clothing, Shape = ItemShape.Clothing, Size = new(0.35f, 0.08f, 0.3f), Grid = new(2, 2), Weight = 0.7f, Tint = new(0.25f, 0.35f, 0.6f), Slot = EquipSlot.Legs, Layer = EquipLayer.Clothing, Warmth = 4f },
            new() { Id = "item.sneakers", Name = "Кроссовки", Category = ItemCategory.Clothing, Shape = ItemShape.Boots, Size = new(0.25f, 0.12f, 0.3f), Grid = new(2, 1), Weight = 0.8f, Tint = new(0.9f, 0.9f, 0.88f), Slot = EquipSlot.Feet, Layer = EquipLayer.Clothing, Warmth = 2f },

            // ── Броня ──
            new() { Id = "item.hard_hat", Name = "Строительная каска", Category = ItemCategory.Armor, Shape = ItemShape.Helmet, Size = new(0.25f, 0.16f, 0.28f), Grid = new(2, 2), Weight = 0.4f, Tint = new(0.95f, 0.8f, 0.1f), Slot = EquipSlot.Head, Layer = EquipLayer.Armor, Armor = 10 },
            new() { Id = "item.padded_vest", Name = "Утеплённый жилет", Category = ItemCategory.Armor, Shape = ItemShape.Clothing, Size = new(0.4f, 0.12f, 0.35f), Grid = new(2, 3), Weight = 1.8f, Tint = new(0.3f, 0.35f, 0.25f), Slot = EquipSlot.Torso, Layer = EquipLayer.Armor, Armor = 12, Warmth = 8f },
            new() { Id = "item.arm_guards", Name = "Налокотники", Category = ItemCategory.Armor, Shape = ItemShape.Pack, Size = new(0.15f, 0.06f, 0.12f), Weight = 0.3f, Tint = new(0.15f, 0.15f, 0.17f), Slot = EquipSlot.Arms, Layer = EquipLayer.Armor, Armor = 5 },
            new() { Id = "item.knee_pads", Name = "Наколенники", Category = ItemCategory.Armor, Shape = ItemShape.Pack, Size = new(0.2f, 0.06f, 0.12f), Grid = new(2, 1), Weight = 0.4f, Tint = new(0.15f, 0.15f, 0.17f), Slot = EquipSlot.Legs, Layer = EquipLayer.Armor, Armor = 6 },
            new() { Id = "item.work_boots", Name = "Рабочие ботинки", Category = ItemCategory.Armor, Shape = ItemShape.Boots, Size = new(0.28f, 0.18f, 0.32f), Grid = new(2, 2), Weight = 1.4f, Tint = new(0.4f, 0.28f, 0.15f), Slot = EquipSlot.Feet, Layer = EquipLayer.Armor, Armor = 8, Warmth = 3f },

            // ── Рюкзаки ──
            new() { Id = "item.school_backpack", Name = "Школьный рюкзак", Description = "Надеть — получить сетку 5×4.", Category = ItemCategory.Backpack, Shape = ItemShape.Backpack, Size = new(0.3f, 0.4f, 0.15f), Grid = new(2, 2), Weight = 0.6f, Tint = new(0.2f, 0.45f, 0.75f), Slot = EquipSlot.Backpack, Container = new(5, 4) },
            new() { Id = "item.hiking_backpack", Name = "Туристический рюкзак", Description = "Надеть — получить сетку 7×6.", Category = ItemCategory.Backpack, Shape = ItemShape.Backpack, Size = new(0.35f, 0.6f, 0.25f), Grid = new(3, 3), Weight = 1.5f, Tint = new(0.3f, 0.45f, 0.25f), Slot = EquipSlot.Backpack, Container = new(7, 6) },

            // ── Сон и смерть ──
            new() { Id = "item.sleeping_bag", Name = "Спальник", Description = "ПКМ — разложить на полу. На нём можно спать (E): сон восстанавливает бодрость и сохраняет точку возрождения.", Category = ItemCategory.Misc, Shape = ItemShape.Bag, Size = new(0.25f, 0.45f, 0.25f), Grid = new(2, 3), Weight = 1.8f, Tint = new(0.2f, 0.42f, 0.32f), PlacesProp = "prop.sleeping_bag" },
            new() { Id = "item.lost_bag", Name = "Сумка с вещами", Description = "Ваши вещи с места гибели. Подберите — содержимое разложится по инвентарю.", Category = ItemCategory.Misc, Shape = ItemShape.Bag, Size = new(0.4f, 0.3f, 0.3f), Grid = new(2, 2), Weight = 0.5f, Tint = new(0.85f, 0.65f, 0.15f), Container = new(12, 12), Unpack = true },

            // ── Крафт (этап 8) ──
            new() { Id = "material.chemicals", Name = "Бытовая химия", Description = "Для химстола: антисептик, обезболивающее.", Category = ItemCategory.Material, Shape = ItemShape.Bottle, Size = new(0.08f, 0.2f, 0.08f), Weight = 0.6f, Stack = 5, Tint = new(0.85f, 0.9f, 0.95f) },
            new() { Id = "item.gas_canister", Name = "Газовый баллончик", Description = "Топливо для плиты и горелки: 5 минут готовки.", Category = ItemCategory.Misc, Shape = ItemShape.Can, Size = new(0.07f, 0.18f, 0.07f), Weight = 0.4f, Stack = 5, Tint = new(0.95f, 0.55f, 0.15f), Fuel = 300f },
            new() { Id = "item.torch", Name = "Факел", Description = "ЛКМ — зажечь. Горит, только пока в руке, около четырёх минут.", Category = ItemCategory.Tool, Shape = ItemShape.Pipe, Size = new(0.45f, 0.06f, 0.06f), Grid = new(2, 1), Weight = 0.4f, Tint = new(0.55f, 0.35f, 0.2f), Tool = ToolKind.Torch, Uses = 240 },
            new() { Id = "item.dish_noodles_meat", Name = "Лапша с тушёнкой", Description = "Горячее блюдо: сытно, сбалансировано, успокаивает.", Category = ItemCategory.Food, Shape = ItemShape.Can, Size = new(0.14f, 0.08f, 0.14f), Weight = 0.6f, Stack = 3, Tint = new(0.9f, 0.75f, 0.45f), Calories = 835, Fat = 35, Protein = 50, Carbs = 80, Water = 300, Cooked = true },
            new() { Id = "item.dish_beans_meat", Name = "Фасоль с мясом", Description = "Горячее блюдо: сытно, сбалансировано, успокаивает.", Category = ItemCategory.Food, Shape = ItemShape.Can, Size = new(0.14f, 0.08f, 0.14f), Weight = 0.7f, Stack = 3, Tint = new(0.65f, 0.35f, 0.2f), Calories = 790, Fat = 30, Protein = 60, Carbs = 70, Water = 150, Cooked = true },
            new() { Id = "item.hot_chocolate", Name = "Горячий шоколад", Description = "Горячий напиток: вода и заметный плюс к рассудку.", Category = ItemCategory.Drink, Shape = ItemShape.Can, Size = new(0.08f, 0.1f, 0.08f), Weight = 0.35f, Stack = 3, Tint = new(0.45f, 0.28f, 0.18f), Calories = 248, Fat = 8, Protein = 4, Carbs = 40, Water = 400, Cooked = true },
            new() { Id = "item.workbench", Name = "Верстак", Description = "ПКМ — поставить (R — повернуть). Инструменты, гвозди, мебель, блоки, починка.", Category = ItemCategory.Misc, Shape = ItemShape.Box, Size = new(0.5f, 0.3f, 0.3f), Grid = new(3, 2), Weight = 15f, Tint = WoodColor, PlacesProp = "prop.workbench" },
            new() { Id = "item.sewing_table", Name = "Швейный стол", Description = "ПКМ — поставить. Одежда, рюкзаки, спальники, ткань из одежды.", Category = ItemCategory.Misc, Shape = ItemShape.Box, Size = new(0.45f, 0.3f, 0.3f), Grid = new(2, 2), Weight = 12f, Tint = new(0.85f, 0.85f, 0.82f), PlacesProp = "prop.sewing_table" },
            new() { Id = "item.chem_table", Name = "Химстол", Description = "ПКМ — поставить. Антисептик и обезболивающее из бытовой химии.", Category = ItemCategory.Misc, Shape = ItemShape.Box, Size = new(0.45f, 0.3f, 0.3f), Grid = new(2, 2), Weight = 12f, Tint = new(0.7f, 0.85f, 0.9f), PlacesProp = "prop.chem_table" },
            new() { Id = "item.burner", Name = "Туристическая горелка", Description = "ПКМ — поставить. Готовка и прогрев еды, нужно топливо. Удержание E — свернуть.", Category = ItemCategory.Misc, Shape = ItemShape.Can, Size = new(0.15f, 0.12f, 0.15f), Grid = new(2, 2), Weight = 1.5f, Tint = new(0.75f, 0.2f, 0.15f), PlacesProp = "prop.burner" },
            new() { Id = "item.barricade", Name = "Баррикада из досок", Description = "ПКМ — поставить. Перекрывает проход; прибейте молотком.", Category = ItemCategory.Misc, Shape = ItemShape.Plank, Size = new(0.6f, 0.08f, 0.3f), Grid = new(4, 2), Weight = 8f, Tint = WoodColor, PlacesProp = "prop.barricade" },
            new() { Id = "item.table", Name = "Стол", Description = "ПКМ — поставить.", Category = ItemCategory.Misc, Shape = ItemShape.Box, Size = new(0.4f, 0.2f, 0.4f), Grid = new(2, 2), Weight = 8f, Tint = WoodColor, PlacesProp = "prop.table" },
            new() { Id = "item.shelf", Name = "Стеллаж", Description = "ПКМ — поставить.", Category = ItemCategory.Misc, Shape = ItemShape.Box, Size = new(0.5f, 0.3f, 0.2f), Grid = new(3, 2), Weight = 15f, Tint = Steel, PlacesProp = "prop.shelf" },
            new() { Id = "item.blueprint_sledgehammer", Name = "Чертёж: кувалда", Category = ItemCategory.Blueprint, Shape = ItemShape.Box, Size = new(0.21f, 0.01f, 0.3f), Weight = 0.05f, Tint = Blueprint, Teaches = "recipe.sledgehammer" },
            new() { Id = "item.blueprint_drill", Name = "Чертёж: перфоратор", Category = ItemCategory.Blueprint, Shape = ItemShape.Box, Size = new(0.21f, 0.01f, 0.3f), Weight = 0.05f, Tint = Blueprint, Teaches = "recipe.drill" },
            new() { Id = "item.blueprint_drill_pro", Name = "Чертёж: мощный перфоратор", Category = ItemCategory.Blueprint, Shape = ItemShape.Box, Size = new(0.21f, 0.01f, 0.3f), Weight = 0.05f, Tint = Blueprint, Teaches = "recipe.drill_pro" },
            new() { Id = "item.blueprint_padded_vest", Name = "Чертёж: утеплённый жилет", Category = ItemCategory.Blueprint, Shape = ItemShape.Box, Size = new(0.21f, 0.01f, 0.3f), Weight = 0.05f, Tint = Blueprint, Teaches = "recipe.padded_vest" },
            new() { Id = "item.blueprint_hiking_backpack", Name = "Чертёж: туристический рюкзак", Category = ItemCategory.Blueprint, Shape = ItemShape.Box, Size = new(0.21f, 0.01f, 0.3f), Weight = 0.05f, Tint = Blueprint, Teaches = "recipe.hiking_backpack" },
        };

        /// <summary>Таблицы лута: раздел → (предмет, вес, мин, макс). Fill — доля мест на полках, где вообще что-то лежит.</summary>
        private static readonly (string section, float fill, (string item, float weight, int min, int max)[] entries)[] Loot =
        {
            ("section.grocery", 0.4f, new[] { ("item.canned_beans", 10f, 1, 2), ("item.canned_meat", 6f, 1, 2), ("item.crackers", 8f, 1, 1), ("item.chocolate", 8f, 1, 3), ("item.chips", 8f, 1, 1), ("item.noodles", 8f, 1, 3), ("item.water_bottle", 10f, 1, 2), ("item.soda", 8f, 1, 3) }),
            ("section.pharmacy", 0.35f, new[] { ("material.chemicals", 4f, 1, 2), ("item.bandage", 10f, 1, 3), ("item.painkillers", 8f, 1, 2), ("item.splint", 3f, 1, 1), ("item.antiseptic", 6f, 1, 1), ("item.water_bottle", 3f, 1, 1), ("item.chocolate", 2f, 1, 1) }),
            ("section.hardware", 0.3f, new[] { ("material.chemicals", 3f, 1, 2), ("item.gas_canister", 1f, 1, 1), ("item.blueprint_sledgehammer", 0.4f, 1, 1), ("item.blueprint_drill", 0.3f, 1, 1), ("item.blueprint_drill_pro", 0.15f, 1, 1), ("item.sleeping_bag", 0.6f, 1, 1), ("material.nails", 10f, 10, 40), ("material.screws", 8f, 10, 40), ("item.hammer", 3f, 1, 1), ("item.crowbar", 2f, 1, 1), ("item.sledgehammer", 1f, 1, 1), ("item.drill", 1f, 1, 1), ("item.drill_pro", 0.3f, 1, 1), ("item.flashlight", 3f, 1, 1), ("material.plank", 6f, 1, 4), ("material.wire", 5f, 1, 3), ("material.pipe", 3f, 1, 2), ("item.gloves_work", 3f, 1, 1), ("item.hard_hat", 2f, 1, 1), ("item.work_boots", 1f, 1, 1) }),
            ("section.electronics", 0.3f, new[] { ("item.blueprint_drill", 0.3f, 1, 1), ("item.blueprint_drill_pro", 0.15f, 1, 1), ("item.flashlight", 6f, 1, 1), ("material.wire", 8f, 1, 3), ("material.plastic", 5f, 1, 3), ("item.drill", 1f, 1, 1) }),
            ("section.clothing", 0.35f, new[] { ("item.blueprint_padded_vest", 0.3f, 1, 1), ("item.blueprint_hiking_backpack", 0.3f, 1, 1), ("item.hoodie", 8f, 1, 1), ("item.jeans", 8f, 1, 1), ("item.sneakers", 6f, 1, 1), ("item.cap", 6f, 1, 1), ("item.gloves_work", 3f, 1, 1), ("item.school_backpack", 2f, 1, 1), ("item.hiking_backpack", 0.6f, 1, 1), ("item.padded_vest", 1f, 1, 1), ("material.fabric", 3f, 1, 3) }),
            ("section.sports", 0.3f, new[] { ("item.burner", 1f, 1, 1), ("item.gas_canister", 2f, 1, 2), ("item.sleeping_bag", 2f, 1, 1), ("item.school_backpack", 4f, 1, 1), ("item.hiking_backpack", 2f, 1, 1), ("item.knee_pads", 4f, 1, 1), ("item.arm_guards", 3f, 1, 1), ("item.sneakers", 4f, 1, 1), ("item.water_bottle", 5f, 1, 2), ("item.cap", 3f, 1, 1) }),
            ("section.furniture", 0.2f, new[] { ("material.fabric", 5f, 1, 3), ("material.foam", 5f, 1, 3), ("material.plank", 3f, 1, 2), ("material.screws", 3f, 5, 20) }),
            ("section.toys", 0.3f, new[] { ("material.plastic", 8f, 1, 3), ("material.cardboard", 5f, 1, 3), ("item.chocolate", 2f, 1, 2) }),
            ("section.foodcourt", 0.25f, new[] { ("item.soda", 8f, 1, 2), ("item.water_bottle", 6f, 1, 1), ("item.noodles", 5f, 1, 2), ("item.chips", 5f, 1, 1), ("item.crackers", 3f, 1, 1) }),
            ("section.empty", 0.05f, new[] { ("material.cardboard", 5f, 1, 2), ("material.plastic", 3f, 1, 2) }),
            ("section.storage", 0.3f, new[] { ("material.chemicals", 2f, 1, 1), ("item.gas_canister", 1f, 1, 1), ("item.blueprint_sledgehammer", 0.2f, 1, 1), ("item.blueprint_drill", 0.15f, 1, 1), ("item.sleeping_bag", 0.8f, 1, 1), ("material.cardboard", 6f, 1, 4), ("material.nails", 3f, 5, 20), ("material.screws", 3f, 5, 20), ("item.canned_beans", 3f, 1, 3), ("item.water_bottle", 3f, 1, 2), ("material.plank", 4f, 1, 3), ("item.hammer", 1f, 1, 1), ("item.flashlight", 1f, 1, 1), ("item.hiking_backpack", 0.3f, 1, 1) }),
            ("section.tech", 0.25f, new[] { ("material.wire", 8f, 1, 4), ("material.pipe", 6f, 1, 2), ("material.scrap_metal", 6f, 1, 3), ("item.flashlight", 2f, 1, 1), ("item.crowbar", 1f, 1, 1), ("item.gloves_work", 2f, 1, 1), ("item.hard_hat", 1f, 1, 1) }),
            ("section.electrical", 0.3f, new[] { ("material.wire", 10f, 1, 4), ("material.fuse", 8f, 1, 4), ("material.electrical_tape", 6f, 1, 2), ("material.screws", 4f, 5, 20), ("item.flashlight", 2f, 1, 1), ("item.gloves_work", 2f, 1, 1), ("item.crowbar", 1f, 1, 1), ("item.hard_hat", 1f, 1, 1) }),
            ("", 0.2f, new[] { ("material.cardboard", 4f, 1, 2), ("material.plastic", 4f, 1, 2), ("item.water_bottle", 2f, 1, 1), ("item.chips", 2f, 1, 1) }),
        };

        public static void CreateItemAssets()
        {
            ProjectSetup.EnsureFolder(ItemsFolder);
            ProjectSetup.EnsureFolder(BlocksFolder);
            ProjectSetup.EnsureFolder(LootFolder);
            var catalog = AssetDatabase.LoadAssetAtPath<MaterialCatalog>(WorldAssetsSetup.CatalogPath);
            var set = LoadOrCreate<ItemSet>(ItemSetPath);
            set.Items.Clear();
            var byId = new Dictionary<string, ItemDef>();

            foreach (var spec in Specs)
            {
                var def = LoadOrCreate<ItemDef>($"{ItemsFolder}/{spec.Id.Replace("item.", "").Replace("material.", "material_")}.asset");
                Apply(def, spec, catalog);
                set.Items.Add(def);
                byId[spec.Id] = def;
            }

            // Блоки-предметы: каждый ставимый блок мира выпадает при поломке и ставится обратно.
            foreach (var material in catalog.Materials)
            {
                if (material.Kind != MaterialKind.Block || !material.Placeable)
                    continue;
                string shortId = material.Id.Replace("block.", "");
                var def = LoadOrCreate<ItemDef>($"{BlocksFolder}/{shortId}.asset");
                Apply(def, new Spec
                {
                    Id = "item.block." + shortId,
                    Name = material.DisplayName,
                    Description = "Строительный блок: ПКМ — поставить.",
                    Category = ItemCategory.Block,
                    Shape = ItemShape.Cube,
                    Size = new Vector3(0.25f, 0.25f, 0.25f),
                    // Условный вес: строить из блоков должно быть реально — стак бетона (32) весит 16 кг.
                    Weight = material.Hardness >= 1000 ? 0.5f : material.Transparent ? 0.25f : 0.3f,
                    Stack = 32,
                    Tint = PatternColor(material.Pattern) * material.Tint,
                }, catalog);
                def.PlacesBlock = material;
                EditorUtility.SetDirty(def);
                set.Items.Add(def);
            }
            // Цена починки ссылается на другие предметы — второй проход, когда все созданы.
            foreach (var spec in Specs)
            {
                if (spec.Repair == null)
                    continue;
                var def = byId[spec.Id];
                foreach (var (item, count) in spec.Repair)
                    if (byId.TryGetValue(item, out var part))
                        def.RepairCost.Add(new ItemAmount(part, count));
                    else
                        Debug.LogError($"[Setup] Repair: unknown item '{item}'.");
                EditorUtility.SetDirty(def);
            }
            EditorUtility.SetDirty(set);

            CreateLootTables(byId);
        }

        private static void Apply(ItemDef def, Spec spec, MaterialCatalog catalog)
        {
            var so = new SerializedObject(def);
            so.FindProperty("_id").stringValue = spec.Id;
            so.ApplyModifiedPropertiesWithoutUndo();
            def.DisplayName = spec.Name;
            def.Description = spec.Description;
            def.Category = spec.Category;
            def.Shape = spec.Shape;
            def.ModelSize = spec.Size;
            def.GridSize = spec.Grid;
            def.Weight = spec.Weight;
            def.MaxStack = spec.Stack;
            var tint = spec.Tint;
            tint.a = 1f;
            def.Tint = tint;
            def.Tool = spec.Tool;
            def.ToolTier = spec.Tier;
            def.BlockDamage = spec.BlockDamage;
            def.PropDamage = spec.PropDamage;
            def.SwingInterval = spec.Swing;
            def.DrillStrength = spec.Drill;
            def.Calories = spec.Calories;
            def.Fat = spec.Fat;
            def.Protein = spec.Protein;
            def.Carbs = spec.Carbs;
            def.Water = spec.Water;
            def.Slot = spec.Slot;
            def.Layer = spec.Layer;
            def.Armor = spec.Armor;
            def.ContainerGrid = spec.Container;
            def.Treats = spec.Treats;
            def.Heal = spec.Heal;
            def.Warmth = spec.Warmth;
            def.PlacesProp = spec.PlacesProp;
            def.UnpackOnPickup = spec.Unpack;
            def.Uses = spec.Uses;
            def.Electric = spec.Electric;
            def.Cooked = spec.Cooked;
            def.FuelSeconds = spec.Fuel;
            def.TeachesRecipe = spec.Teaches;
            def.RepairCost = new List<ItemAmount>();
            def.PlacesBlock = null;
            def.SmoothMaterial = null;
            if (!string.IsNullOrEmpty(spec.Smooth))
                foreach (var material in catalog.Materials)
                    if (material.Id == spec.Smooth)
                        def.SmoothMaterial = material;
            EditorUtility.SetDirty(def);
        }

        private static void CreateLootTables(Dictionary<string, ItemDef> items)
        {
            var lootSet = LoadOrCreate<LootTableSet>(LootSetPath);
            lootSet.Tables.Clear();
            foreach (var (section, fill, entries) in Loot)
            {
                string name = string.IsNullOrEmpty(section) ? "default" : section.Replace("section.", "");
                var table = LoadOrCreate<LootTable>($"{LootFolder}/{name}.asset");
                table.Section = section;
                table.Fill = fill;
                table.Entries = new List<LootEntry>();
                foreach (var (item, weight, min, max) in entries)
                {
                    if (!items.TryGetValue(item, out var def))
                    {
                        Debug.LogError($"[Setup] Loot: unknown item '{item}'.");
                        continue;
                    }
                    table.Entries.Add(new LootEntry { Item = def, Weight = weight, Min = min, Max = max });
                }
                EditorUtility.SetDirty(table);
                if (string.IsNullOrEmpty(section))
                    lootSet.Default = table;
                else
                    lootSet.Tables.Add(table);
            }
            EditorUtility.SetDirty(lootSet);
        }

        /// <summary>Средний цвет процедурной текстуры блока — для иконки и модели блока-предмета.</summary>
        private static Color PatternColor(BlockPattern pattern) => pattern switch
        {
            BlockPattern.Concrete => new Color(0.56f, 0.56f, 0.55f),
            BlockPattern.Carpet => new Color(0.52f, 0.46f, 0.32f),
            BlockPattern.Wallpaper => new Color(0.75f, 0.68f, 0.43f),
            BlockPattern.Drywall or BlockPattern.CeilingTile or BlockPattern.FloorTile or BlockPattern.FacadePanel => new Color(0.88f, 0.88f, 0.86f),
            BlockPattern.Metal => new Color(0.62f, 0.63f, 0.66f),
            BlockPattern.Glass => new Color(0.75f, 0.88f, 0.94f),
            BlockPattern.Wood => new Color(0.5f, 0.36f, 0.25f),
            BlockPattern.Terrazzo => new Color(0.8f, 0.78f, 0.74f),
            BlockPattern.CheckerTile => new Color(0.5f, 0.5f, 0.5f),
            BlockPattern.Linoleum => new Color(0.86f, 0.86f, 0.86f),
            BlockPattern.Shutter => new Color(0.7f, 0.71f, 0.73f),
            BlockPattern.Asphalt or BlockPattern.ParkingLineU or BlockPattern.ParkingLineV => new Color(0.28f, 0.28f, 0.29f),
            BlockPattern.LampPanel or BlockPattern.Skylight => new Color(0.95f, 0.95f, 0.92f),
            BlockPattern.EmergencyLamp => new Color(0.6f, 0.2f, 0.15f),
            BlockPattern.Soil => new Color(0.45f, 0.34f, 0.22f),
            _ => new Color(0.8f, 0.8f, 0.8f),
        };

        private static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null)
                return asset;
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }
    }
}
