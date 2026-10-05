using System.Collections.Generic;
using UnityEngine;

namespace VoidMall.World
{
    /// <summary>Форма процедурной модели пропа (PropDef.Shape). Свои модели подставляются в PropDef.Mesh.</summary>
    public enum PropShape : byte
    {
        Shelf,
        Fridge,
        Checkout,
        DisplayTable,
        ClothesRack,
        Table,
        Chair,
        PalletRack,
        BoxStack,
        Car,
        Cabinet,
        Boiler,
        Bench,
        Planter,
        Sofa,
        CardboardBox,
        PlasticCrate,
        TrashBin,
        Switchboard,
        CableReel,
        VentUnit,
        Bed,
        SleepingBag,
        Workbench,
        SewingTable,
        ChemTable,
        Stove,
        Burner,
        Barricade,
        Crusher,
        Turnstile,
        OfficeDesk,
        OfficeChair,
        FilingCabinet,
        WaterCooler,
        CubicleWall,
        Stanchion,
        TrashPile,
        LootCrate,
        Pallet,
        Count,
    }

    /// <summary>Процедурные меши пропов по форме и размеру (опорная точка — центр низа, «вперёд» — +Z).</summary>
    public static class PropMeshes
    {
        /// <summary>Габарит по умолчанию (ширина X, высота Y, глубина Z) — для создания PropDef в редакторе.</summary>
        public static Vector3 DefaultSize(PropShape kind) => kind switch
        {
            PropShape.Shelf => new Vector3(2f, 1.9f, 0.6f),
            PropShape.Fridge => new Vector3(1.2f, 2.1f, 0.8f),
            PropShape.Checkout => new Vector3(2.2f, 0.95f, 0.9f),
            PropShape.DisplayTable => new Vector3(1.6f, 0.9f, 1f),
            PropShape.ClothesRack => new Vector3(1.4f, 1.5f, 0.5f),
            PropShape.Table => new Vector3(1.2f, 0.75f, 1.2f),
            PropShape.Chair => new Vector3(0.45f, 0.9f, 0.45f),
            PropShape.PalletRack => new Vector3(2.7f, 3.4f, 1.1f),
            PropShape.BoxStack => new Vector3(1f, 1.2f, 1f),
            PropShape.Car => new Vector3(1.8f, 1.45f, 4.3f),
            PropShape.Cabinet => new Vector3(0.8f, 2f, 0.45f),
            PropShape.Boiler => new Vector3(1.6f, 2.4f, 1.6f),
            PropShape.Bench => new Vector3(2f, 0.45f, 0.55f),
            PropShape.Planter => new Vector3(1.2f, 0.9f, 1.2f),
            PropShape.Sofa => new Vector3(2.2f, 0.85f, 0.95f),
            PropShape.CardboardBox => new Vector3(0.5f, 0.4f, 0.4f),
            PropShape.PlasticCrate => new Vector3(0.6f, 0.32f, 0.4f),
            PropShape.TrashBin => new Vector3(0.5f, 0.8f, 0.5f),
            PropShape.Switchboard => new Vector3(2.4f, 2.1f, 0.6f),
            PropShape.CableReel => new Vector3(0.7f, 1f, 1f),
            PropShape.VentUnit => new Vector3(2.2f, 1.6f, 1.2f),
            PropShape.Bed => new Vector3(1f, 0.95f, 2.1f),
            PropShape.SleepingBag => new Vector3(0.8f, 0.16f, 2f),
            PropShape.Workbench => new Vector3(1.6f, 1.6f, 0.8f),
            PropShape.SewingTable => new Vector3(1.2f, 1f, 0.7f),
            PropShape.ChemTable => new Vector3(1.4f, 1.5f, 0.7f),
            PropShape.Stove => new Vector3(0.8f, 0.9f, 0.65f),
            PropShape.Burner => new Vector3(0.3f, 0.22f, 0.3f),
            PropShape.Barricade => new Vector3(1.6f, 1.2f, 0.25f),
            PropShape.Crusher => new Vector3(1.2f, 1.4f, 1f),
            PropShape.Turnstile => new Vector3(0.9f, 1f, 0.8f),
            PropShape.OfficeDesk => new Vector3(1.4f, 0.75f, 0.7f),
            PropShape.OfficeChair => new Vector3(0.6f, 1f, 0.6f),
            PropShape.FilingCabinet => new Vector3(0.5f, 1.3f, 0.6f),
            PropShape.WaterCooler => new Vector3(0.35f, 1.3f, 0.35f),
            PropShape.CubicleWall => new Vector3(1.6f, 1.3f, 0.06f),
            PropShape.Stanchion => new Vector3(0.3f, 0.95f, 0.3f),
            PropShape.TrashPile => new Vector3(1.2f, 0.45f, 1f),
            PropShape.LootCrate => new Vector3(0.8f, 0.6f, 0.6f),
            PropShape.Pallet => new Vector3(1.2f, 0.15f, 1f),
            _ => Vector3.one,
        };

        /// <summary>
        /// Точки спавна товаров на пропе (в локальных координатах): центр низа места и свободный габарит над ним.
        /// Пусто — у пропа нет полок. Появится ли предмет в точке и какой — решает лут (ShelfLoot).
        /// </summary>
        public static void GetItemSlots(PropShape shape, Vector3 s, List<PropItemSlot> slots)
        {
            slots.Clear();
            float w = s.x, h = s.y, d = s.z;
            switch (shape)
            {
                case PropShape.Shelf:
                    // 4 полки (нижние) по 4 места; над верхней полкой — тоже место.
                    for (int level = 0; level < 5; level++)
                    {
                        float y = 0.1f + level * 0.43f + 0.04f;
                        float free = level < 4 ? 0.37f : h - y;
                        for (int k = 0; k < 4; k++)
                            slots.Add(new PropItemSlot { Position = new Vector3(-w / 2 + w * (k + 0.5f) / 4f, y, -0.02f), Size = new Vector3(w / 4f - 0.06f, free, d - 0.12f) });
                    }
                    break;
                case PropShape.Fridge:
                    for (int level = 0; level < 4; level++)
                    {
                        float y = 0.15f + level * 0.45f;
                        for (int k = 0; k < 3; k++)
                            slots.Add(new PropItemSlot { Position = new Vector3(-w / 2 + 0.05f + (w - 0.1f) * (k + 0.5f) / 3f, y, -0.05f), Size = new Vector3((w - 0.1f) / 3f - 0.04f, 0.4f, d - 0.15f) });
                    }
                    break;
                case PropShape.PalletRack:
                    for (int level = 0; level < 3; level++)
                    {
                        float y = 0.15f + level * 1.15f + 0.1f;
                        for (int k = 0; k < 2; k++)
                            slots.Add(new PropItemSlot { Position = new Vector3(-w / 2 + w * (k + 0.5f) / 2f, y, 0f), Size = new Vector3(w / 2f - 0.2f, 0.95f, d - 0.2f) });
                    }
                    break;
                case PropShape.DisplayTable:
                    for (int k = 0; k < 3; k++)
                        slots.Add(new PropItemSlot { Position = new Vector3(-w / 2 + w * (k + 0.5f) / 3f, h, 0f), Size = new Vector3(w / 3f - 0.06f, 0.4f, d - 0.1f) });
                    break;
                case PropShape.ClothesRack:
                    // Одежда «висит» на перекладине: места на уровне низа ткани.
                    for (int k = 0; k < 3; k++)
                        slots.Add(new PropItemSlot { Position = new Vector3(-w / 2 + 0.1f + (w - 0.2f) * (k + 0.5f) / 3f, 0.55f, 0f), Size = new Vector3((w - 0.2f) / 3f - 0.04f, 0.8f, d) });
                    break;
                case PropShape.Checkout:
                    slots.Add(new PropItemSlot { Position = new Vector3(-w / 4f, h, 0f), Size = new Vector3(w / 2f - 0.1f, 0.3f, d - 0.1f) });
                    break;
                case PropShape.OfficeDesk:
                    // На столешнице слева от монитора и справа — мелочь (лут офиса).
                    slots.Add(new PropItemSlot { Position = new Vector3(-w / 2 + 0.25f, h, -0.05f), Size = new Vector3(0.4f, 0.3f, d - 0.15f) });
                    slots.Add(new PropItemSlot { Position = new Vector3(w / 2 - 0.25f, h, -0.05f), Size = new Vector3(0.4f, 0.3f, d - 0.15f) });
                    break;
                case PropShape.FilingCabinet:
                    slots.Add(new PropItemSlot { Position = new Vector3(0f, h, 0f), Size = new Vector3(w - 0.05f, 0.3f, d - 0.1f) });
                    break;
                case PropShape.Pallet:
                    // Четыре места на настиле: стопки пеноблоков, доски, мешки цемента.
                    for (int k = 0; k < 4; k++)
                        slots.Add(new PropItemSlot { Position = new Vector3((k % 2 == 0 ? -1 : 1) * w / 4f, h, (k < 2 ? -1 : 1) * d / 4f), Size = new Vector3(w / 2f - 0.04f, 0.6f, d / 2f - 0.04f) });
                    break;
            }
        }

        /// <summary>
        /// Коллайдер формы из нескольких коробок (локальные габариты). Пусто — одна коробка на весь размер.
        /// Нужен тем, у кого внутри лежат товары: сдвинутая полка роняет их как настоящие предметы,
        /// и они должны стоять на досках, а не оказаться внутри сплошного куба.
        /// </summary>
        public static void GetColliderParts(PropShape shape, Vector3 s, List<Bounds> parts)
        {
            parts.Clear();
            float w = s.x, h = s.y, d = s.z;
            void Add(Vector3 min, Vector3 max) => parts.Add(new Bounds((min + max) * 0.5f, max - min));
            switch (shape)
            {
                case PropShape.Shelf:
                    Add(new Vector3(-w / 2, 0, d / 2 - 0.04f), new Vector3(w / 2, h, d / 2));
                    Add(new Vector3(-w / 2, 0, -d / 2), new Vector3(-w / 2 + 0.04f, h, d / 2));
                    Add(new Vector3(w / 2 - 0.04f, 0, -d / 2), new Vector3(w / 2, h, d / 2));
                    Add(new Vector3(-w / 2, 0, -d / 2), new Vector3(w / 2, 0.14f, d / 2)); // нижняя полка с цоколем
                    for (int i = 1; i < 5; i++)
                    {
                        float y = 0.1f + i * 0.43f;
                        Add(new Vector3(-w / 2, y, -d / 2), new Vector3(w / 2, y + 0.04f, d / 2));
                    }
                    break;
                case PropShape.Fridge:
                    Add(new Vector3(-w / 2, 0, d / 2 - 0.05f), new Vector3(w / 2, h, d / 2));
                    Add(new Vector3(-w / 2, 0, -d / 2), new Vector3(-w / 2 + 0.05f, h, d / 2));
                    Add(new Vector3(w / 2 - 0.05f, 0, -d / 2), new Vector3(w / 2, h, d / 2));
                    Add(new Vector3(-w / 2, h - 0.15f, -d / 2), new Vector3(w / 2, h, d / 2));
                    Add(new Vector3(-w / 2, 0, -d / 2), new Vector3(w / 2, 0.15f, d / 2));
                    for (int i = 1; i < 4; i++)
                        Add(new Vector3(-w / 2, 0.15f + i * 0.45f - 0.02f, -d / 2 + 0.05f), new Vector3(w / 2, 0.15f + i * 0.45f, d / 2));
                    break;
                case PropShape.PalletRack:
                    for (int i = 0; i < 4; i++)
                    {
                        float x = (i & 1) == 0 ? -w / 2 : w / 2 - 0.08f;
                        float z = (i & 2) == 0 ? -d / 2 : d / 2 - 0.08f;
                        Add(new Vector3(x, 0, z), new Vector3(x + 0.08f, h, z + 0.08f));
                    }
                    for (int level = 0; level < 3; level++)
                    {
                        float y = 0.15f + level * 1.15f;
                        Add(new Vector3(-w / 2, y, -d / 2), new Vector3(w / 2, y + 0.1f, d / 2));
                    }
                    break;
                case PropShape.ClothesRack:
                    // Ткань не держит: со сдвинутой вешалки одежда падает на пол.
                    Add(new Vector3(-w / 2, 0, -0.04f), new Vector3(-w / 2 + 0.05f, h, 0.04f));
                    Add(new Vector3(w / 2 - 0.05f, 0, -0.04f), new Vector3(w / 2, h, 0.04f));
                    Add(new Vector3(-w / 2, h - 0.05f, -0.03f), new Vector3(w / 2, h, 0.03f));
                    break;
                case PropShape.Turnstile:
                    Add(new Vector3(-w / 2, 0, -d / 2), new Vector3(-w / 2 + 0.3f, h, d / 2));
                    Add(new Vector3(-w / 2 + 0.3f, 0.66f, -0.18f), new Vector3(w / 2, 0.94f, 0.18f));
                    break;
                case PropShape.Workbench:
                case PropShape.ChemTable:
                    // Стол до столешницы и щит или экран сзади — на столешницу можно положить вещи.
                    float benchTop = h * (shape == PropShape.Workbench ? 0.55f : 0.6f);
                    Add(new Vector3(-w / 2, 0, -d / 2), new Vector3(w / 2, benchTop, d / 2));
                    Add(new Vector3(-w / 2, benchTop, d / 2 - 0.06f), new Vector3(w / 2, h, d / 2));
                    break;
                case PropShape.Bed:
                    // Лежак до верха матраса и изголовье — чтобы на кровать можно было положить вещи и запрыгнуть.
                    Add(new Vector3(-w / 2, 0, -d / 2), new Vector3(w / 2, h * 0.6f, d / 2));
                    Add(new Vector3(-w / 2, 0, d / 2 - 0.06f), new Vector3(w / 2, h, d / 2));
                    break;
            }
        }

        private static readonly Color Metal = new(0.55f, 0.57f, 0.6f, 0.55f);
        private static readonly Color DarkMetal = new(0.22f, 0.23f, 0.25f, 0.45f);
        private static readonly Color White = new(0.86f, 0.86f, 0.84f, 0.35f);
        private static readonly Color Wood = new(0.5f, 0.36f, 0.24f, 0.25f);
        private static readonly Color Cardboard = new(0.62f, 0.48f, 0.3f, 0.08f);
        private static readonly Color Fabric = new(0.32f, 0.36f, 0.42f, 0.05f);
        private static readonly Color Green = new(0.25f, 0.4f, 0.22f, 0.1f);
        private static readonly Color Glass = new(0.55f, 0.68f, 0.72f, 0.9f);

        /// <summary>Собрать меш формы под заданный габарит.</summary>
        public static Mesh Build(PropShape kind, Vector3 s)
        {
            var b = new MeshBuilder();
            float w = s.x, h = s.y, d = s.z;
            switch (kind)
            {
                case PropShape.Shelf:
                    b.BoxMinMax(new Vector3(-w / 2, 0, d / 2 - 0.04f), new Vector3(w / 2, h, d / 2), Metal); // задняя стенка
                    b.BoxMinMax(new Vector3(-w / 2, 0, -d / 2), new Vector3(-w / 2 + 0.04f, h, d / 2), DarkMetal);
                    b.BoxMinMax(new Vector3(w / 2 - 0.04f, 0, -d / 2), new Vector3(w / 2, h, d / 2), DarkMetal);
                    for (int i = 0; i < 5; i++)
                    {
                        float y = 0.1f + i * 0.43f;
                        b.BoxMinMax(new Vector3(-w / 2, y, -d / 2), new Vector3(w / 2, y + 0.04f, d / 2), Metal);
                    }
                    break;
                case PropShape.Fridge:
                    // Открытая холодильная витрина: корпус и полки, товары — настоящие предметы на полках.
                    b.BoxMinMax(new Vector3(-w / 2, 0, d / 2 - 0.05f), new Vector3(w / 2, h, d / 2), White);
                    b.BoxMinMax(new Vector3(-w / 2, 0, -d / 2), new Vector3(-w / 2 + 0.05f, h, d / 2), White);
                    b.BoxMinMax(new Vector3(w / 2 - 0.05f, 0, -d / 2), new Vector3(w / 2, h, d / 2), White);
                    b.BoxMinMax(new Vector3(-w / 2, h - 0.15f, -d / 2), new Vector3(w / 2, h, d / 2), White);
                    b.BoxMinMax(new Vector3(-w / 2, 0, -d / 2), new Vector3(w / 2, 0.15f, d / 2), DarkMetal);
                    for (int i = 1; i < 4; i++)
                        b.BoxMinMax(new Vector3(-w / 2, 0.15f + i * 0.45f - 0.02f, -d / 2 + 0.05f), new Vector3(w / 2, 0.15f + i * 0.45f, d / 2), Metal);
                    break;
                case PropShape.Checkout:
                    b.BoxMinMax(new Vector3(-w / 2, 0, -d / 2), new Vector3(w / 2, h - 0.05f, d / 2), White);
                    b.BoxMinMax(new Vector3(-w / 2, h - 0.05f, -d / 2), new Vector3(w / 2, h, d / 2), DarkMetal);
                    b.BoxMinMax(new Vector3(w / 2 - 0.5f, h, -0.15f), new Vector3(w / 2 - 0.1f, h + 0.3f, 0.15f), DarkMetal);
                    break;
                case PropShape.DisplayTable:
                    b.BoxMinMax(new Vector3(-w / 2, h - 0.08f, -d / 2), new Vector3(w / 2, h, d / 2), White);
                    b.BoxMinMax(new Vector3(-w / 2 + 0.1f, 0, -d / 2 + 0.1f), new Vector3(w / 2 - 0.1f, h - 0.08f, d / 2 - 0.1f), Wood);
                    break;
                case PropShape.ClothesRack:
                    b.BoxMinMax(new Vector3(-w / 2, 0, -0.04f), new Vector3(-w / 2 + 0.05f, h, 0.04f), Metal);
                    b.BoxMinMax(new Vector3(w / 2 - 0.05f, 0, -0.04f), new Vector3(w / 2, h, 0.04f), Metal);
                    b.BoxMinMax(new Vector3(-w / 2, h - 0.05f, -0.03f), new Vector3(w / 2, h, 0.03f), Metal);
                    b.BoxMinMax(new Vector3(-w / 2 + 0.1f, 0.5f, -d / 2), new Vector3(w / 2 - 0.1f, h - 0.1f, d / 2), Fabric);
                    break;
                case PropShape.Table:
                    b.BoxMinMax(new Vector3(-w / 2, h - 0.05f, -d / 2), new Vector3(w / 2, h, d / 2), Wood);
                    b.BoxMinMax(new Vector3(-0.06f, 0, -0.06f), new Vector3(0.06f, h - 0.05f, 0.06f), DarkMetal);
                    b.BoxMinMax(new Vector3(-0.3f, 0, -0.3f), new Vector3(0.3f, 0.03f, 0.3f), DarkMetal);
                    break;
                case PropShape.Chair:
                    b.BoxMinMax(new Vector3(-w / 2, 0.42f, -d / 2), new Vector3(w / 2, 0.46f, d / 2), Wood);
                    b.BoxMinMax(new Vector3(-w / 2, 0.46f, d / 2 - 0.04f), new Vector3(w / 2, h, d / 2), Wood);
                    for (int i = 0; i < 4; i++)
                    {
                        float x = (i & 1) == 0 ? -w / 2 : w / 2 - 0.04f;
                        float z = (i & 2) == 0 ? -d / 2 : d / 2 - 0.04f;
                        b.BoxMinMax(new Vector3(x, 0, z), new Vector3(x + 0.04f, 0.42f, z + 0.04f), DarkMetal);
                    }
                    break;
                case PropShape.PalletRack:
                    var orange = new Color(0.85f, 0.42f, 0.12f, 0.35f);
                    var blue = new Color(0.15f, 0.3f, 0.6f, 0.35f);
                    for (int i = 0; i < 4; i++)
                    {
                        float x = (i & 1) == 0 ? -w / 2 : w / 2 - 0.08f;
                        float z = (i & 2) == 0 ? -d / 2 : d / 2 - 0.08f;
                        b.BoxMinMax(new Vector3(x, 0, z), new Vector3(x + 0.08f, h, z + 0.08f), blue);
                    }
                    for (int level = 0; level < 3; level++)
                    {
                        float y = 0.15f + level * 1.15f;
                        b.BoxMinMax(new Vector3(-w / 2, y, -d / 2), new Vector3(w / 2, y + 0.1f, d / 2), orange);
                    }
                    break;
                case PropShape.BoxStack:
                    b.BoxMinMax(new Vector3(-0.5f, 0, -0.5f), new Vector3(0.45f, 0.5f, 0.45f), Cardboard);
                    b.BoxMinMax(new Vector3(-0.4f, 0.5f, -0.45f), new Vector3(0.5f, 0.95f, 0.5f), Cardboard);
                    b.BoxMinMax(new Vector3(-0.3f, 0.95f, -0.25f), new Vector3(0.2f, 1.2f, 0.25f), Cardboard);
                    break;
                case PropShape.Car:
                    var paint = new Color(0.35f, 0.38f, 0.42f, 0.7f);
                    b.BoxMinMax(new Vector3(-w / 2, 0.3f, -d / 2), new Vector3(w / 2, 0.95f, d / 2), paint);
                    b.BoxMinMax(new Vector3(-w / 2 + 0.12f, 0.95f, -d / 2 + 1.1f), new Vector3(w / 2 - 0.12f, h, d / 2 - 0.9f), Glass);
                    for (int i = 0; i < 4; i++)
                    {
                        float x = (i & 1) == 0 ? -w / 2 - 0.02f : w / 2 - 0.2f;
                        float z = (i & 2) == 0 ? -d / 2 + 0.55f : d / 2 - 0.95f;
                        b.BoxMinMax(new Vector3(x, 0, z), new Vector3(x + 0.22f, 0.62f, z + 0.62f), DarkMetal);
                    }
                    break;
                case PropShape.Cabinet:
                    b.BoxMinMax(new Vector3(-w / 2, 0, -d / 2), new Vector3(w / 2, h, d / 2), new Color(0.6f, 0.62f, 0.58f, 0.4f));
                    b.BoxMinMax(new Vector3(-0.02f, 0.2f, -d / 2 - 0.01f), new Vector3(0.02f, h - 0.2f, -d / 2), DarkMetal);
                    break;
                case PropShape.Boiler:
                    b.BoxMinMax(new Vector3(-w / 2, 0, -d / 2), new Vector3(w / 2, 0.2f, d / 2), DarkMetal);
                    b.Box(new Vector3(0, h / 2 + 0.1f, 0), new Vector3(w * 0.85f, h - 0.2f, d * 0.85f), Metal, Quaternion.Euler(0, 45f, 0));
                    b.BoxMinMax(new Vector3(-0.1f, h - 0.1f, -0.1f), new Vector3(0.1f, h + 0.6f, 0.1f), DarkMetal);
                    break;
                case PropShape.Bench:
                    b.BoxMinMax(new Vector3(-w / 2, h - 0.06f, -d / 2), new Vector3(w / 2, h, d / 2), Wood);
                    b.BoxMinMax(new Vector3(-w / 2 + 0.1f, 0, -d / 2 + 0.05f), new Vector3(-w / 2 + 0.16f, h - 0.06f, d / 2 - 0.05f), DarkMetal);
                    b.BoxMinMax(new Vector3(w / 2 - 0.16f, 0, -d / 2 + 0.05f), new Vector3(w / 2 - 0.1f, h - 0.06f, d / 2 - 0.05f), DarkMetal);
                    break;
                case PropShape.Planter:
                    b.BoxMinMax(new Vector3(-w / 2, 0, -d / 2), new Vector3(w / 2, 0.6f, d / 2), White);
                    b.BoxMinMax(new Vector3(-w / 2 + 0.15f, 0.6f, -d / 2 + 0.15f), new Vector3(w / 2 - 0.15f, h, d / 2 - 0.15f), Green);
                    break;
                case PropShape.Sofa:
                    var cloth = new Color(0.45f, 0.3f, 0.28f, 0.05f);
                    b.BoxMinMax(new Vector3(-w / 2, 0.1f, -d / 2), new Vector3(w / 2, 0.45f, d / 2), cloth);
                    b.BoxMinMax(new Vector3(-w / 2, 0.45f, d / 2 - 0.25f), new Vector3(w / 2, h, d / 2), cloth);
                    b.BoxMinMax(new Vector3(-w / 2, 0.45f, -d / 2), new Vector3(-w / 2 + 0.2f, 0.65f, d / 2 - 0.25f), cloth);
                    b.BoxMinMax(new Vector3(w / 2 - 0.2f, 0.45f, -d / 2), new Vector3(w / 2, 0.65f, d / 2 - 0.25f), cloth);
                    break;
                case PropShape.CardboardBox:
                    b.BoxMinMax(new Vector3(-w / 2, 0, -d / 2), new Vector3(w / 2, h, d / 2), Cardboard);
                    b.BoxMinMax(new Vector3(-0.04f, h, -d / 2), new Vector3(0.04f, h + 0.004f, d / 2), new Color(0.75f, 0.68f, 0.5f, 0.4f)); // скотч
                    break;
                case PropShape.PlasticCrate:
                    var plastic = new Color(0.2f, 0.45f, 0.75f, 0.35f);
                    b.BoxMinMax(new Vector3(-w / 2, 0, -d / 2), new Vector3(w / 2, 0.03f, d / 2), plastic);
                    b.BoxMinMax(new Vector3(-w / 2, 0, -d / 2), new Vector3(w / 2, h, -d / 2 + 0.03f), plastic);
                    b.BoxMinMax(new Vector3(-w / 2, 0, d / 2 - 0.03f), new Vector3(w / 2, h, d / 2), plastic);
                    b.BoxMinMax(new Vector3(-w / 2, 0, -d / 2), new Vector3(-w / 2 + 0.03f, h, d / 2), plastic);
                    b.BoxMinMax(new Vector3(w / 2 - 0.03f, 0, -d / 2), new Vector3(w / 2, h, d / 2), plastic);
                    break;
                case PropShape.TrashBin:
                    var bin = new Color(0.25f, 0.3f, 0.27f, 0.4f);
                    b.BoxMinMax(new Vector3(-w / 2 + 0.03f, 0, -d / 2 + 0.03f), new Vector3(w / 2 - 0.03f, h - 0.06f, d / 2 - 0.03f), bin);
                    b.BoxMinMax(new Vector3(-w / 2, h - 0.06f, -d / 2), new Vector3(w / 2, h, d / 2), DarkMetal);
                    break;
                case PropShape.Switchboard:
                {
                    // Главный распределительный щит: три секции с дверцами, индикаторы и табличка «опасно».
                    var panel = new Color(0.58f, 0.6f, 0.56f, 0.45f);
                    b.BoxMinMax(new Vector3(-w / 2, 0, -d / 2), new Vector3(w / 2, h, d / 2), panel);
                    b.BoxMinMax(new Vector3(-w / 2, 0, -d / 2 - 0.02f), new Vector3(w / 2, 0.1f, d / 2), DarkMetal);
                    for (int i = 0; i < 3; i++)
                    {
                        float x0 = -w / 2 + w * i / 3f;
                        if (i > 0)
                            b.BoxMinMax(new Vector3(x0 - 0.01f, 0.1f, -d / 2 - 0.012f), new Vector3(x0 + 0.01f, h, -d / 2), DarkMetal);
                        float cx = x0 + w / 6f;
                        b.BoxMinMax(new Vector3(cx + w / 6f - 0.12f, 0.9f, -d / 2 - 0.03f), new Vector3(cx + w / 6f - 0.08f, 1.15f, -d / 2), DarkMetal);
                        b.BoxMinMax(new Vector3(cx - 0.08f, h - 0.3f, -d / 2 - 0.02f), new Vector3(cx - 0.02f, h - 0.24f, -d / 2), new Color(0.9f, 0.15f, 0.1f, 0.8f));
                        b.BoxMinMax(new Vector3(cx + 0.02f, h - 0.3f, -d / 2 - 0.02f), new Vector3(cx + 0.08f, h - 0.24f, -d / 2), new Color(0.2f, 0.85f, 0.25f, 0.8f));
                    }
                    b.BoxMinMax(new Vector3(-0.15f, h - 0.55f, -d / 2 - 0.015f), new Vector3(0.15f, h - 0.4f, -d / 2), new Color(0.95f, 0.8f, 0.1f, 0.3f));
                    break;
                }
                case PropShape.CableReel:
                {
                    // Кабельный барабан на боку: ось вдоль X, щёки и намотка — восьмиугольники из повёрнутых коробок.
                    var reelWood = new Color(0.5f, 0.38f, 0.25f, 0.2f);
                    var cable = new Color(0.12f, 0.12f, 0.13f, 0.35f);
                    float r = h * 0.5f;
                    for (int k = 0; k < 4; k++)
                    {
                        var rot = Quaternion.Euler(k * 45f, 0, 0);
                        float side = r * 2f * 0.4142f;
                        b.Box(new Vector3(-w / 2 + 0.03f, r, 0), new Vector3(0.06f, r * 2f, side), reelWood, rot);
                        b.Box(new Vector3(w / 2 - 0.03f, r, 0), new Vector3(0.06f, r * 2f, side), reelWood, rot);
                        b.Box(new Vector3(0, r, 0), new Vector3(w - 0.12f, r * 1.5f, r * 1.5f * 0.4142f), cable, rot);
                    }
                    break;
                }
                case PropShape.VentUnit:
                {
                    // Вентустановка: корпус, круглая решётка вентилятора спереди, патрубок воздуховода сверху.
                    var body = new Color(0.78f, 0.8f, 0.82f, 0.55f);
                    b.BoxMinMax(new Vector3(-w / 2, 0, -d / 2), new Vector3(w / 2, 0.12f, d / 2), DarkMetal);
                    b.BoxMinMax(new Vector3(-w / 2, 0.12f, -d / 2), new Vector3(w / 2, h, d / 2), body);
                    float fr = h * 0.32f;
                    for (int k = 0; k < 4; k++)
                        b.Box(new Vector3(-w * 0.2f, h * 0.5f, -d / 2 - 0.02f), new Vector3(fr * 2f * 0.4142f, fr * 2f, 0.04f), DarkMetal, Quaternion.Euler(0, 0, k * 45f));
                    for (int k = 0; k < 3; k++)
                        b.BoxMinMax(new Vector3(w * 0.15f, 0.3f + k * 0.35f, -d / 2 - 0.01f), new Vector3(w * 0.42f, 0.36f + k * 0.35f, -d / 2), DarkMetal);
                    b.BoxMinMax(new Vector3(-0.35f, h, -0.3f), new Vector3(0.35f, h + 0.4f, 0.3f), body);
                    break;
                }
                case PropShape.Bed:
                {
                    // Односпальная кровать: каркас на ножках, матрас, подушка у изголовья (+Z), одеяло.
                    var mattress = new Color(0.82f, 0.8f, 0.74f, 0.05f);
                    var blanket = new Color(0.3f, 0.38f, 0.5f, 0.05f);
                    float top = h * 0.6f; // верх матраса, выше — только изголовье
                    float leg = 0.06f;
                    for (int ix = -1; ix <= 1; ix += 2)
                    for (int iz = -1; iz <= 1; iz += 2)
                        b.BoxMinMax(new Vector3(ix * (w / 2 - leg) - leg / 2, 0, iz * (d / 2 - leg) - leg / 2), new Vector3(ix * (w / 2 - leg) + leg / 2, 0.22f, iz * (d / 2 - leg) + leg / 2), Wood);
                    b.BoxMinMax(new Vector3(-w / 2, 0.18f, -d / 2), new Vector3(w / 2, 0.3f, d / 2), Wood);
                    b.BoxMinMax(new Vector3(-w / 2, 0.18f, d / 2 - 0.06f), new Vector3(w / 2, h, d / 2), Wood); // изголовье
                    b.BoxMinMax(new Vector3(-w / 2 + 0.03f, 0.3f, -d / 2 + 0.03f), new Vector3(w / 2 - 0.03f, top - 0.04f, d / 2 - 0.06f), mattress);
                    b.BoxMinMax(new Vector3(-w / 2 + 0.15f, top - 0.04f, d / 2 - 0.5f), new Vector3(w / 2 - 0.15f, top + 0.08f, d / 2 - 0.12f), White);
                    b.BoxMinMax(new Vector3(-w / 2 + 0.01f, top - 0.06f, -d / 2 + 0.01f), new Vector3(w / 2 - 0.01f, top, d / 2 - 0.6f), blanket);
                    break;
                }
                case PropShape.SleepingBag:
                {
                    // Спальник на полу: туловище, капюшон у изголовья (+Z), молния по краю.
                    var bag = new Color(0.2f, 0.42f, 0.32f, 0.08f);
                    b.BoxMinMax(new Vector3(-w / 2, 0, -d / 2), new Vector3(w / 2, h, d / 2 - 0.35f), bag);
                    b.BoxMinMax(new Vector3(-w / 2 + 0.06f, 0, d / 2 - 0.35f), new Vector3(w / 2 - 0.06f, h * 0.8f, d / 2), bag);
                    b.BoxMinMax(new Vector3(-w / 2 + 0.15f, h * 0.8f, d / 2 - 0.3f), new Vector3(w / 2 - 0.15f, h * 0.95f, d / 2 - 0.05f), White);
                    b.BoxMinMax(new Vector3(w / 2 - 0.02f, h * 0.4f, -d / 2), new Vector3(w / 2 + 0.005f, h * 0.5f, d / 2 - 0.35f), DarkMetal);
                    break;
                }
                case PropShape.Workbench:
                {
                    // Верстак: толстая столешница, ножки, нижняя полка, тиски на углу и щит для инструментов сзади.
                    float top = h * 0.55f;
                    for (int ix = -1; ix <= 1; ix += 2)
                    for (int iz = -1; iz <= 1; iz += 2)
                        b.BoxMinMax(new Vector3(ix * (w / 2 - 0.05f) - 0.04f, 0, iz * (d / 2 - 0.05f) - 0.04f), new Vector3(ix * (w / 2 - 0.05f) + 0.04f, top - 0.06f, iz * (d / 2 - 0.05f) + 0.04f), Wood);
                    b.BoxMinMax(new Vector3(-w / 2, top - 0.06f, -d / 2), new Vector3(w / 2, top, d / 2), Wood);
                    b.BoxMinMax(new Vector3(-w / 2 + 0.05f, 0.15f, -d / 2 + 0.05f), new Vector3(w / 2 - 0.05f, 0.18f, d / 2 - 0.05f), Wood);
                    b.BoxMinMax(new Vector3(w / 2 - 0.25f, top, -d / 2 + 0.02f), new Vector3(w / 2 - 0.05f, top + 0.12f, -d / 2 + 0.18f), DarkMetal); // тиски
                    b.BoxMinMax(new Vector3(-w / 2, top, d / 2 - 0.04f), new Vector3(w / 2, h, d / 2), new Color(0.6f, 0.5f, 0.35f, 0.15f)); // щит
                    for (int k = 0; k < 4; k++)
                        b.BoxMinMax(new Vector3(-w / 2 + 0.2f + k * 0.35f, top + 0.25f, d / 2 - 0.07f), new Vector3(-w / 2 + 0.24f + k * 0.35f, top + 0.5f, d / 2 - 0.04f), Metal);
                    break;
                }
                case PropShape.SewingTable:
                {
                    // Швейный стол: стол на тонких ножках, швейная машинка, катушки ткани.
                    float top = h * 0.75f;
                    for (int ix = -1; ix <= 1; ix += 2)
                    for (int iz = -1; iz <= 1; iz += 2)
                        b.BoxMinMax(new Vector3(ix * (w / 2 - 0.04f) - 0.025f, 0, iz * (d / 2 - 0.04f) - 0.025f), new Vector3(ix * (w / 2 - 0.04f) + 0.025f, top - 0.04f, iz * (d / 2 - 0.04f) + 0.025f), DarkMetal);
                    b.BoxMinMax(new Vector3(-w / 2, top - 0.04f, -d / 2), new Vector3(w / 2, top, d / 2), White);
                    var machine = new Color(0.85f, 0.85f, 0.82f, 0.4f);
                    b.BoxMinMax(new Vector3(-0.25f, top, -0.1f), new Vector3(0.2f, top + 0.08f, 0.12f), machine);
                    b.BoxMinMax(new Vector3(0.1f, top + 0.08f, -0.06f), new Vector3(0.2f, h - 0.02f, 0.08f), machine);
                    b.BoxMinMax(new Vector3(-0.22f, h - 0.1f, -0.05f), new Vector3(0.2f, h - 0.02f, 0.07f), machine);
                    b.BoxMinMax(new Vector3(-w / 2 + 0.08f, top, -d / 2 + 0.1f), new Vector3(-w / 2 + 0.3f, top + 0.14f, -d / 2 + 0.3f), Fabric);
                    b.BoxMinMax(new Vector3(-w / 2 + 0.08f, top, d / 2 - 0.3f), new Vector3(-w / 2 + 0.3f, top + 0.1f, d / 2 - 0.1f), new Color(0.6f, 0.25f, 0.25f, 0.05f));
                    break;
                }
                case PropShape.ChemTable:
                {
                    // Химстол: лабораторный стол, колбы и штатив, вытяжной экран сзади.
                    float top = h * 0.6f;
                    b.BoxMinMax(new Vector3(-w / 2, 0, -d / 2 + 0.05f), new Vector3(w / 2, top - 0.04f, d / 2), White);
                    b.BoxMinMax(new Vector3(-w / 2, top - 0.04f, -d / 2), new Vector3(w / 2, top, d / 2), new Color(0.2f, 0.22f, 0.24f, 0.5f));
                    b.BoxMinMax(new Vector3(-w / 2, top, d / 2 - 0.04f), new Vector3(w / 2, h, d / 2), Glass);
                    for (int k = 0; k < 4; k++)
                    {
                        float x = -w / 2 + 0.2f + k * 0.28f;
                        b.BoxMinMax(new Vector3(x - 0.04f, top, -0.05f), new Vector3(x + 0.04f, top + 0.12f + 0.04f * (k % 2), 0.03f), Glass);
                    }
                    b.BoxMinMax(new Vector3(w / 2 - 0.3f, top, 0f), new Vector3(w / 2 - 0.28f, top + 0.35f, 0.02f), DarkMetal);
                    b.BoxMinMax(new Vector3(w / 2 - 0.4f, top + 0.3f, -0.02f), new Vector3(w / 2 - 0.18f, top + 0.32f, 0.04f), DarkMetal);
                    break;
                }
                case PropShape.Stove:
                {
                    // Кухонная плита: корпус, духовка с ручкой, четыре конфорки, задняя панель с ручками.
                    var enamel = new Color(0.85f, 0.85f, 0.83f, 0.45f);
                    b.BoxMinMax(new Vector3(-w / 2, 0, -d / 2), new Vector3(w / 2, h - 0.05f, d / 2), enamel);
                    b.BoxMinMax(new Vector3(-w / 2 + 0.06f, 0.1f, -d / 2 - 0.01f), new Vector3(w / 2 - 0.06f, h * 0.6f, -d / 2), new Color(0.15f, 0.15f, 0.16f, 0.8f));
                    b.BoxMinMax(new Vector3(-w / 2 + 0.1f, h * 0.62f, -d / 2 - 0.04f), new Vector3(w / 2 - 0.1f, h * 0.65f, -d / 2 - 0.01f), Metal);
                    b.BoxMinMax(new Vector3(-w / 2, h - 0.05f, -d / 2), new Vector3(w / 2, h - 0.02f, d / 2), DarkMetal);
                    for (int ix = 0; ix < 2; ix++)
                    for (int iz = 0; iz < 2; iz++)
                        b.BoxMinMax(new Vector3(-w / 4 + ix * w / 2 - 0.09f, h - 0.02f, -d / 4 + iz * d / 2 - 0.09f), new Vector3(-w / 4 + ix * w / 2 + 0.09f, h, -d / 4 + iz * d / 2 + 0.09f), new Color(0.1f, 0.1f, 0.1f, 0.6f));
                    b.BoxMinMax(new Vector3(-w / 2, h - 0.02f, d / 2 - 0.06f), new Vector3(w / 2, h + 0.12f, d / 2), enamel);
                    break;
                }
                case PropShape.Burner:
                {
                    // Туристическая газовая горелка: баллон и решётка-подставка.
                    b.BoxMinMax(new Vector3(-w / 2 + 0.05f, 0, -d / 2 + 0.05f), new Vector3(w / 2 - 0.05f, h * 0.6f, d / 2 - 0.05f), new Color(0.75f, 0.2f, 0.15f, 0.4f));
                    b.BoxMinMax(new Vector3(-0.04f, h * 0.6f, -0.04f), new Vector3(0.04f, h * 0.8f, 0.04f), Metal);
                    b.BoxMinMax(new Vector3(-w / 2, h * 0.8f, -0.015f), new Vector3(w / 2, h, 0.015f), DarkMetal);
                    b.BoxMinMax(new Vector3(-0.015f, h * 0.8f, -d / 2), new Vector3(0.015f, h, d / 2), DarkMetal);
                    break;
                }
                case PropShape.Barricade:
                {
                    // Баррикада: три горизонтальные доски на двух стойках и косая доска.
                    for (int k = 0; k < 3; k++)
                        b.BoxMinMax(new Vector3(-w / 2, 0.15f + k * h * 0.3f, -d / 2), new Vector3(w / 2, 0.15f + k * h * 0.3f + 0.16f, -d / 2 + 0.04f), Wood);
                    b.BoxMinMax(new Vector3(-w / 2 + 0.1f, 0, -d / 2 + 0.04f), new Vector3(-w / 2 + 0.18f, h, d / 2), Wood);
                    b.BoxMinMax(new Vector3(w / 2 - 0.18f, 0, -d / 2 + 0.04f), new Vector3(w / 2 - 0.1f, h, d / 2), Wood);
                    b.Box(new Vector3(0, h * 0.5f, -d / 2 - 0.02f), new Vector3(w * 1.05f, 0.14f, 0.04f), Wood, Quaternion.Euler(0, 0, 30f));
                    break;
                }
                case PropShape.Turnstile:
                {
                    // Турникет: тумба слева, трипод из трёх штанг вправо, перегораживает проход до соседней тумбы.
                    float cab = 0.3f;
                    b.BoxMinMax(new Vector3(-w / 2, 0, -d / 2), new Vector3(-w / 2 + cab, h - 0.04f, d / 2), Metal);
                    b.BoxMinMax(new Vector3(-w / 2 - 0.01f, h - 0.04f, -d / 2 - 0.01f), new Vector3(-w / 2 + cab + 0.01f, h, d / 2 + 0.01f), DarkMetal);
                    b.BoxMinMax(new Vector3(-w / 2 + cab, 0.82f, -0.08f), new Vector3(-w / 2 + cab + 0.06f, 0.94f, 0.08f), DarkMetal);
                    float hub = -w / 2 + cab + 0.06f;
                    b.BoxMinMax(new Vector3(hub, 0.86f, -0.025f), new Vector3(w / 2, 0.91f, 0.025f), Metal);
                    b.Box(new Vector3(hub + 0.18f, 0.72f, 0.12f), new Vector3(0.4f, 0.05f, 0.05f), Metal, Quaternion.Euler(-35f, -30f, 0f));
                    b.Box(new Vector3(hub + 0.18f, 0.72f, -0.12f), new Vector3(0.4f, 0.05f, 0.05f), Metal, Quaternion.Euler(35f, 30f, 0f));
                    b.BoxMinMax(new Vector3(-w / 2 + 0.05f, h - 0.03f, -0.12f), new Vector3(-w / 2 + 0.25f, h + 0.01f, -0.02f), new Color(0.2f, 0.75f, 0.3f, 0.8f));
                    break;
                }
                case PropShape.OfficeDesk:
                {
                    // Офисный стол: столешница, тумба с ящиками, монитор с подставкой, клавиатура.
                    b.BoxMinMax(new Vector3(-w / 2, h - 0.04f, -d / 2), new Vector3(w / 2, h, d / 2), White);
                    b.BoxMinMax(new Vector3(-w / 2, 0, -d / 2 + 0.05f), new Vector3(-w / 2 + 0.04f, h - 0.04f, d / 2 - 0.05f), DarkMetal);
                    b.BoxMinMax(new Vector3(w / 2 - 0.45f, 0, -d / 2 + 0.05f), new Vector3(w / 2, h - 0.04f, d / 2 - 0.05f), White);
                    for (int i = 0; i < 3; i++)
                        b.BoxMinMax(new Vector3(w / 2 - 0.4f, 0.1f + i * 0.21f, -d / 2 + 0.03f), new Vector3(w / 2 - 0.05f, 0.27f + i * 0.21f, -d / 2 + 0.06f), Metal);
                    b.BoxMinMax(new Vector3(-0.08f, h, d / 2 - 0.25f), new Vector3(0.08f, h + 0.02f, d / 2 - 0.1f), DarkMetal);
                    b.BoxMinMax(new Vector3(-0.02f, h, d / 2 - 0.2f), new Vector3(0.02f, h + 0.15f, d / 2 - 0.16f), DarkMetal);
                    b.BoxMinMax(new Vector3(-0.28f, h + 0.12f, d / 2 - 0.2f), new Vector3(0.28f, h + 0.46f, d / 2 - 0.16f), DarkMetal);
                    b.BoxMinMax(new Vector3(-0.26f, h + 0.14f, d / 2 - 0.205f), new Vector3(0.26f, h + 0.44f, d / 2 - 0.2f), new Color(0.08f, 0.1f, 0.12f, 0.9f));
                    b.BoxMinMax(new Vector3(-0.22f, h, -0.05f), new Vector3(0.22f, h + 0.02f, 0.1f), DarkMetal);
                    break;
                }
                case PropShape.OfficeChair:
                {
                    // Офисное кресло: крестовина, газлифт, сиденье, спинка.
                    b.BoxMinMax(new Vector3(-w / 2 + 0.05f, 0.04f, -0.03f), new Vector3(w / 2 - 0.05f, 0.08f, 0.03f), DarkMetal);
                    b.BoxMinMax(new Vector3(-0.03f, 0.04f, -d / 2 + 0.05f), new Vector3(0.03f, 0.08f, d / 2 - 0.05f), DarkMetal);
                    b.BoxMinMax(new Vector3(-0.03f, 0.08f, -0.03f), new Vector3(0.03f, 0.42f, 0.03f), Metal);
                    b.BoxMinMax(new Vector3(-w / 2 + 0.04f, 0.42f, -d / 2 + 0.04f), new Vector3(w / 2 - 0.04f, 0.5f, d / 2 - 0.06f), Fabric);
                    b.BoxMinMax(new Vector3(-w / 2 + 0.06f, 0.55f, d / 2 - 0.08f), new Vector3(w / 2 - 0.06f, h, d / 2 - 0.02f), Fabric);
                    b.BoxMinMax(new Vector3(-0.03f, 0.46f, d / 2 - 0.1f), new Vector3(0.03f, 0.6f, d / 2 - 0.06f), DarkMetal);
                    break;
                }
                case PropShape.FilingCabinet:
                {
                    // Шкаф для документов: четыре ящика с ручками.
                    var cabinetPaint = new Color(0.6f, 0.62f, 0.6f, 0.4f);
                    b.BoxMinMax(new Vector3(-w / 2, 0, -d / 2), new Vector3(w / 2, h, d / 2), cabinetPaint);
                    for (int i = 0; i < 4; i++)
                    {
                        float y0 = 0.05f + i * (h - 0.1f) / 4f;
                        float y1 = y0 + (h - 0.1f) / 4f - 0.03f;
                        b.BoxMinMax(new Vector3(-w / 2 + 0.03f, y0, -d / 2 - 0.015f), new Vector3(w / 2 - 0.03f, y1, -d / 2), cabinetPaint);
                        b.BoxMinMax(new Vector3(-0.08f, (y0 + y1) * 0.5f - 0.015f, -d / 2 - 0.04f), new Vector3(0.08f, (y0 + y1) * 0.5f + 0.015f, -d / 2 - 0.015f), DarkMetal);
                    }
                    break;
                }
                case PropShape.WaterCooler:
                {
                    // Кулер: белый корпус, кранчики, голубая бутыль сверху.
                    b.BoxMinMax(new Vector3(-w / 2, 0, -d / 2), new Vector3(w / 2, h * 0.68f, d / 2), White);
                    b.BoxMinMax(new Vector3(-0.08f, h * 0.45f, -d / 2 - 0.04f), new Vector3(-0.03f, h * 0.5f, -d / 2), new Color(0.2f, 0.35f, 0.9f, 0.6f));
                    b.BoxMinMax(new Vector3(0.03f, h * 0.45f, -d / 2 - 0.04f), new Vector3(0.08f, h * 0.5f, -d / 2), new Color(0.9f, 0.2f, 0.2f, 0.6f));
                    b.BoxMinMax(new Vector3(-w / 2 + 0.04f, h * 0.68f, -d / 2 + 0.04f), new Vector3(w / 2 - 0.04f, h, d / 2 - 0.04f), new Color(0.45f, 0.65f, 0.9f, 0.9f));
                    break;
                }
                case PropShape.CubicleWall:
                    // Перегородка кабинки: тканевая панель в алюминиевой раме.
                    b.BoxMinMax(new Vector3(-w / 2, 0.05f, -d / 2), new Vector3(w / 2, h, d / 2), new Color(0.42f, 0.45f, 0.5f, 0.05f));
                    b.BoxMinMax(new Vector3(-w / 2 - 0.01f, h - 0.03f, -d / 2 - 0.01f), new Vector3(w / 2 + 0.01f, h + 0.01f, d / 2 + 0.01f), Metal);
                    b.BoxMinMax(new Vector3(-w / 2, 0, -d / 2 - 0.01f), new Vector3(w / 2, 0.05f, d / 2 + 0.01f), DarkMetal);
                    break;
                case PropShape.Stanchion:
                    // Столбик ограждения стенда: тяжёлое основание, хромированная стойка, бархатный шнур вбок.
                    b.BoxMinMax(new Vector3(-w / 2, 0, -d / 2), new Vector3(w / 2, 0.05f, d / 2), DarkMetal);
                    b.BoxMinMax(new Vector3(-0.025f, 0.05f, -0.025f), new Vector3(0.025f, h, 0.025f), Metal);
                    b.BoxMinMax(new Vector3(-0.04f, h - 0.04f, -0.04f), new Vector3(0.04f, h, 0.04f), Metal);
                    b.BoxMinMax(new Vector3(0.025f, h - 0.12f, -0.015f), new Vector3(w / 2 + 0.35f, h - 0.09f, 0.015f), new Color(0.6f, 0.08f, 0.1f, 0.1f));
                    break;
                case PropShape.TrashPile:
                {
                    // Куча мусора: мешки, коробки, бумага — заброшенный вид.
                    var bag = new Color(0.08f, 0.08f, 0.09f, 0.3f);
                    b.Box(new Vector3(-w * 0.2f, h * 0.35f, -d * 0.1f), new Vector3(w * 0.45f, h * 0.7f, d * 0.5f), bag, Quaternion.Euler(0f, 20f, 8f));
                    b.Box(new Vector3(w * 0.22f, h * 0.3f, d * 0.15f), new Vector3(w * 0.38f, h * 0.6f, d * 0.42f), bag, Quaternion.Euler(0f, -35f, -6f));
                    b.Box(new Vector3(w * 0.05f, h * 0.2f, -d * 0.3f), new Vector3(w * 0.3f, h * 0.4f, d * 0.25f), Cardboard, Quaternion.Euler(0f, 50f, 0f));
                    b.Box(new Vector3(-w * 0.32f, 0.02f, d * 0.3f), new Vector3(w * 0.25f, 0.02f, d * 0.2f), White, Quaternion.Euler(0f, 15f, 0f));
                    b.Box(new Vector3(w * 0.35f, 0.02f, -d * 0.32f), new Vector3(w * 0.2f, 0.02f, d * 0.25f), White, Quaternion.Euler(0f, -40f, 0f));
                    break;
                }
                case PropShape.LootCrate:
                {
                    // Деревянный ящик с лутом: доски, рёбра жёсткости, маркировка. Внутри — пенопласт (частицы при поломке).
                    var crate = new Color(0.62f, 0.48f, 0.3f, 0.15f);
                    b.BoxMinMax(new Vector3(-w / 2 + 0.02f, 0.02f, -d / 2 + 0.02f), new Vector3(w / 2 - 0.02f, h - 0.02f, d / 2 - 0.02f), crate);
                    for (int i = 0; i < 2; i++)
                    {
                        float x = i == 0 ? -w / 2 : w / 2 - 0.05f;
                        b.BoxMinMax(new Vector3(x, 0, -d / 2), new Vector3(x + 0.05f, h, d / 2), Wood);
                    }
                    b.BoxMinMax(new Vector3(-w / 2, 0, -d / 2), new Vector3(w / 2, 0.05f, d / 2), Wood);
                    b.BoxMinMax(new Vector3(-w / 2, h - 0.05f, -d / 2), new Vector3(w / 2, h, d / 2), Wood);
                    b.Box(new Vector3(0f, h * 0.5f, -d / 2 - 0.005f), new Vector3(w * 0.9f, 0.05f, 0.01f), Wood, Quaternion.Euler(0f, 0f, 30f));
                    b.BoxMinMax(new Vector3(-0.12f, h * 0.55f, -d / 2 - 0.012f), new Vector3(0.12f, h * 0.75f, -d / 2 - 0.006f), new Color(0.15f, 0.15f, 0.15f, 0.1f));
                    break;
                }
                case PropShape.Pallet:
                {
                    // Поддон: три бруса и настил из досок.
                    var pallet = new Color(0.66f, 0.55f, 0.38f, 0.1f);
                    for (int i = 0; i < 3; i++)
                    {
                        float z = -d / 2 + i * (d - 0.1f) / 2f;
                        b.BoxMinMax(new Vector3(-w / 2, 0, z), new Vector3(w / 2, h - 0.03f, z + 0.1f), pallet);
                    }
                    for (int i = 0; i < 5; i++)
                    {
                        float x = -w / 2 + i * (w - 0.12f) / 4f;
                        b.BoxMinMax(new Vector3(x, h - 0.03f, -d / 2), new Vector3(x + 0.12f, h, d / 2), pallet);
                    }
                    break;
                }
                case PropShape.Crusher:
                {
                    // Дробилка-компрессор: станина, корпус с валками, бункер сверху, лоток выдачи спереди, мотор сбоку.
                    var crusherPaint = new Color(0.85f, 0.6f, 0.15f, 0.4f);
                    b.BoxMinMax(new Vector3(-w / 2, 0, -d / 2), new Vector3(w / 2, 0.15f, d / 2), DarkMetal);
                    b.BoxMinMax(new Vector3(-w / 2 + 0.1f, 0.15f, -d / 2 + 0.1f), new Vector3(w / 2 - 0.25f, h * 0.65f, d / 2 - 0.1f), crusherPaint);
                    b.BoxMinMax(new Vector3(-w / 2 + 0.05f, h * 0.65f, -d / 2 + 0.05f), new Vector3(w / 2 - 0.2f, h * 0.72f, d / 2 - 0.05f), DarkMetal);
                    b.BoxMinMax(new Vector3(-w / 2, h * 0.72f, -d / 2), new Vector3(w / 2 - 0.15f, h, d / 2), Metal); // бункер
                    b.BoxMinMax(new Vector3(-0.25f, 0.2f, -d / 2 - 0.15f), new Vector3(0.15f, 0.3f, -d / 2 + 0.1f), DarkMetal); // лоток
                    b.BoxMinMax(new Vector3(w / 2 - 0.25f, 0.15f, -0.2f), new Vector3(w / 2, 0.55f, 0.2f), new Color(0.2f, 0.35f, 0.25f, 0.5f)); // мотор
                    break;
                }
            }
            return b.ToMesh("Prop_" + kind);
        }
    }

    /// <summary>
    /// Проп в плане: описание (индекс в PropRegistry), точка опоры (центр низа), поворот (градусы)
    /// и устойчивый ID (ячейка, уровень, порядковый номер) — по нему хранятся изменения пропа за сессию.
    /// </summary>
    public struct PropSpawn
    {
        public ushort Def;
        public Vector3 Position;
        public float Yaw;

        /// <summary>Наклон вокруг поперечной оси до поворота (−90 — стеллаж лежит лицом вниз; 0 — стоит).</summary>
        public float Pitch;
        public ulong Id;
    }

    /// <summary>Место для товара на полке: центр низа (локально) и свободный габарит.</summary>
    public struct PropItemSlot
    {
        public Vector3 Position;
        public Vector3 Size;
    }
}
