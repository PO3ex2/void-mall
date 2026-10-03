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
        public ulong Id;
    }

    /// <summary>Место для товара на полке: центр низа (локально) и свободный габарит.</summary>
    public struct PropItemSlot
    {
        public Vector3 Position;
        public Vector3 Size;
    }
}
