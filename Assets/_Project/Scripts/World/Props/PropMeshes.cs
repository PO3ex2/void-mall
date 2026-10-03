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
            _ => Vector3.one,
        };

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
                        // Товары-«коробочки» на полке.
                        for (int k = 0; k < 4; k++)
                        {
                            float x = -w / 2 + 0.15f + k * 0.45f;
                            float tall = 0.18f + (k * 7 + i * 3) % 5 * 0.04f;
                            var color = Color.HSVToRGB(((k * 37 + i * 61) % 100) / 100f, 0.45f, 0.7f);
                            color.a = 0.2f;
                            if (i < 4)
                                b.BoxMinMax(new Vector3(x, y + 0.04f, -d / 2 + 0.06f), new Vector3(x + 0.32f, y + 0.04f + tall, d / 2 - 0.08f), color);
                        }
                    }
                    break;
                case PropShape.Fridge:
                    b.BoxMinMax(new Vector3(-w / 2, 0, -d / 2 + 0.05f), new Vector3(w / 2, h, d / 2), White);
                    b.BoxMinMax(new Vector3(-w / 2 + 0.06f, 0.15f, -d / 2), new Vector3(w / 2 - 0.06f, h - 0.15f, -d / 2 + 0.05f), Glass);
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
                        b.BoxMinMax(new Vector3(-w / 2 + 0.15f, y + 0.1f, -d / 2 + 0.1f), new Vector3(-0.1f, y + 0.85f, d / 2 - 0.1f), Cardboard);
                        if (level != 1)
                            b.BoxMinMax(new Vector3(0.1f, y + 0.1f, -d / 2 + 0.1f), new Vector3(w / 2 - 0.15f, y + 0.7f, d / 2 - 0.1f), Cardboard);
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
}
