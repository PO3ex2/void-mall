using UnityEngine;
using VoidMall.World;

namespace VoidMall.Items
{
    /// <summary>
    /// Процедурные модели предметов по форме, габариту и цвету (шейдер VoidMall/Props, цвет — в вершинах).
    /// Опорная точка — центр низа, длинная сторона инструментов — вдоль X.
    /// </summary>
    public static class ItemMeshes
    {
        private static readonly Color Dark = new(0.15f, 0.15f, 0.16f, 0.4f);
        private static readonly Color Steel = new(0.6f, 0.62f, 0.65f, 0.7f);
        private static readonly Color Wood = new(0.55f, 0.4f, 0.26f, 0.25f);
        private static readonly Color Label = new(0.92f, 0.9f, 0.84f, 0.2f);

        public static Mesh Build(ItemShape shape, Vector3 s, Color tint)
        {
            var b = new MeshBuilder();
            float w = s.x, h = s.y, d = s.z;
            Color c = new(tint.r, tint.g, tint.b, 0.3f);
            switch (shape)
            {
                case ItemShape.Box:
                    b.BoxMinMax(new Vector3(-w / 2, 0, -d / 2), new Vector3(w / 2, h, d / 2), c);
                    b.BoxMinMax(new Vector3(-w / 2 + 0.01f, h * 0.35f, -d / 2 - 0.002f), new Vector3(w / 2 - 0.01f, h * 0.75f, -d / 2), Label);
                    break;
                case ItemShape.Can:
                    Cylinder(b, Vector3.zero, w * 0.5f, h, c, 10);
                    Cylinder(b, new Vector3(0, h * 0.25f, 0), w * 0.51f, h * 0.5f, Label, 10);
                    break;
                case ItemShape.Bottle:
                    Cylinder(b, Vector3.zero, w * 0.5f, h * 0.68f, c, 10);
                    Cylinder(b, new Vector3(0, h * 0.68f, 0), w * 0.22f, h * 0.24f, c, 8);
                    Cylinder(b, new Vector3(0, h * 0.92f, 0), w * 0.24f, h * 0.08f, Dark, 8);
                    break;
                case ItemShape.Pack:
                    b.BoxMinMax(new Vector3(-w / 2, 0, -d / 2), new Vector3(w / 2, h, d / 2), c);
                    b.BoxMinMax(new Vector3(-w * 0.15f, h, -d / 2), new Vector3(w * 0.15f, h + 0.003f, d / 2), new Color(0.85f, 0.2f, 0.2f, 0.3f));
                    break;
                case ItemShape.Bag:
                    b.BoxMinMax(new Vector3(-w / 2, 0, -d / 2), new Vector3(w / 2, h * 0.85f, d / 2), c);
                    b.Box(new Vector3(0, h * 0.9f, 0), new Vector3(w * 0.8f, h * 0.15f, d * 0.6f), c, Quaternion.Euler(0, 0, 4f));
                    break;
                case ItemShape.Pile:
                    for (int i = 0; i < 5; i++)
                    {
                        float x = (i % 3 - 1) * w * 0.28f, z = (i / 3 - 0.5f) * d * 0.4f;
                        float size = Mathf.Lerp(0.45f, 0.65f, (i * 37 % 10) / 10f);
                        b.Box(new Vector3(x, h * size * 0.5f, z), new Vector3(w * size * 0.6f, h * size, d * size * 0.6f), c, Quaternion.Euler(i * 13f, i * 47f, i * 29f));
                    }
                    break;
                case ItemShape.Cube:
                    b.BoxMinMax(new Vector3(-w / 2, 0, -d / 2), new Vector3(w / 2, h, d / 2), c);
                    break;
                case ItemShape.Hammer:
                    b.BoxMinMax(new Vector3(-w / 2, h * 0.4f, -d * 0.12f), new Vector3(w * 0.3f, h * 0.6f, d * 0.12f), Wood);
                    b.BoxMinMax(new Vector3(w * 0.3f, 0, -d / 2), new Vector3(w / 2, h, d / 2), Steel);
                    break;
                case ItemShape.Sledgehammer:
                    b.BoxMinMax(new Vector3(-w / 2, h * 0.42f, -d * 0.1f), new Vector3(w * 0.25f, h * 0.58f, d * 0.1f), Wood);
                    b.BoxMinMax(new Vector3(w * 0.25f, 0, -d / 2), new Vector3(w / 2, h, d / 2), Dark);
                    break;
                case ItemShape.Crowbar:
                    b.BoxMinMax(new Vector3(-w / 2, 0, -d / 2), new Vector3(w * 0.4f, h * 0.35f, d / 2), c);
                    b.Box(new Vector3(w * 0.45f, h * 0.5f, 0), new Vector3(w * 0.12f, h, d), c, Quaternion.Euler(0, 0, -25f));
                    break;
                case ItemShape.Drill:
                    b.BoxMinMax(new Vector3(-w / 2, h * 0.45f, -d / 2), new Vector3(w * 0.25f, h, d / 2), c);
                    b.BoxMinMax(new Vector3(-w * 0.35f, 0, -d * 0.35f), new Vector3(-w * 0.1f, h * 0.45f, d * 0.35f), Dark);
                    b.BoxMinMax(new Vector3(w * 0.25f, h * 0.68f, -d * 0.08f), new Vector3(w / 2, h * 0.78f, d * 0.08f), Steel);
                    break;
                case ItemShape.Flashlight:
                    b.BoxMinMax(new Vector3(-w / 2, h * 0.2f, -d * 0.3f), new Vector3(w * 0.25f, h * 0.8f, d * 0.3f), c);
                    b.BoxMinMax(new Vector3(w * 0.25f, 0, -d / 2), new Vector3(w / 2, h, d / 2), Dark);
                    b.BoxMinMax(new Vector3(w / 2, h * 0.1f, -d * 0.4f), new Vector3(w / 2 + 0.005f, h * 0.9f, d * 0.4f), new Color(1f, 0.95f, 0.7f, 0.9f));
                    break;
                case ItemShape.Clothing:
                    // Сложенная вещь: стопка из двух слоёв и воротник.
                    b.BoxMinMax(new Vector3(-w / 2, 0, -d / 2), new Vector3(w / 2, h * 0.55f, d / 2), c);
                    b.BoxMinMax(new Vector3(-w * 0.45f, h * 0.55f, -d * 0.45f), new Vector3(w * 0.45f, h, d * 0.45f), c * 0.9f);
                    b.BoxMinMax(new Vector3(-w * 0.15f, h, -d * 0.45f), new Vector3(w * 0.15f, h + 0.01f, -d * 0.2f), Dark);
                    break;
                case ItemShape.Helmet:
                    b.BoxMinMax(new Vector3(-w * 0.4f, 0, -d * 0.4f), new Vector3(w * 0.4f, h * 0.8f, d * 0.4f), c);
                    b.BoxMinMax(new Vector3(-w / 2, 0, -d / 2), new Vector3(w / 2, h * 0.12f, d / 2), c);
                    b.BoxMinMax(new Vector3(-w * 0.3f, h * 0.8f, -d * 0.3f), new Vector3(w * 0.3f, h, d * 0.3f), c);
                    break;
                case ItemShape.Boots:
                    for (int i = -1; i <= 1; i += 2)
                    {
                        float x = i * w * 0.25f;
                        b.BoxMinMax(new Vector3(x - w * 0.2f, 0, -d / 2), new Vector3(x + w * 0.2f, h * 0.35f, d / 2), c);
                        b.BoxMinMax(new Vector3(x - w * 0.2f, h * 0.35f, d * 0.05f), new Vector3(x + w * 0.2f, h, d / 2), c);
                        b.BoxMinMax(new Vector3(x - w * 0.21f, 0, -d / 2), new Vector3(x + w * 0.21f, h * 0.08f, d / 2), Dark);
                    }
                    break;
                case ItemShape.Backpack:
                    b.BoxMinMax(new Vector3(-w / 2, 0, -d / 2), new Vector3(w / 2, h * 0.9f, d / 2), c);
                    b.BoxMinMax(new Vector3(-w * 0.4f, h * 0.15f, -d / 2 - 0.05f), new Vector3(w * 0.4f, h * 0.5f, -d / 2), c * 0.85f);
                    b.Box(new Vector3(0, h * 0.95f, 0), new Vector3(w * 0.9f, h * 0.12f, d * 0.9f), c * 0.9f);
                    b.BoxMinMax(new Vector3(-w * 0.35f, h * 0.2f, d / 2), new Vector3(-w * 0.25f, h * 0.85f, d / 2 + 0.02f), Dark);
                    b.BoxMinMax(new Vector3(w * 0.25f, h * 0.2f, d / 2), new Vector3(w * 0.35f, h * 0.85f, d / 2 + 0.02f), Dark);
                    break;
                case ItemShape.Plank:
                    b.BoxMinMax(new Vector3(-w / 2, 0, -d / 2), new Vector3(w / 2, h, d / 2), c);
                    break;
                case ItemShape.Pipe:
                    Cylinder(b, new Vector3(0, 0, 0), Mathf.Min(h, d) * 0.5f, w, c, 8, alongX: true);
                    break;
                case ItemShape.Coil:
                    for (int i = 0; i < 4; i++)
                        Cylinder(b, new Vector3(0, i * h * 0.25f, 0), w * (0.5f - i * 0.02f), h * 0.2f, c, 10);
                    Cylinder(b, Vector3.zero, w * 0.2f, h, Dark, 8);
                    break;
                default:
                    b.BoxMinMax(new Vector3(-w / 2, 0, -d / 2), new Vector3(w / 2, h, d / 2), c);
                    break;
            }
            return b.ToMesh("Item_" + shape);
        }

        /// <summary>Многогранная «бочка» из коробок по кругу: на мелких предметах цилиндр не отличить, а MeshBuilder умеет только коробки.</summary>
        private static void Cylinder(MeshBuilder b, Vector3 bottom, float radius, float height, Color color, int sides, bool alongX = false)
        {
            float side = 2f * radius * Mathf.Tan(Mathf.PI / sides) * 1.02f;
            float thickness = radius * 0.5f;
            for (int i = 0; i < sides; i++)
            {
                float angle = i * 360f / sides;
                if (alongX)
                {
                    var rotation = Quaternion.Euler(angle, 0f, 0f);
                    Vector3 offset = rotation * new Vector3(0f, radius - thickness * 0.5f, 0f);
                    b.Box(bottom + new Vector3(0f, radius, 0f) + offset, new Vector3(height, thickness, side), color, rotation);
                }
                else
                {
                    var rotation = Quaternion.Euler(0f, angle, 0f);
                    Vector3 offset = rotation * new Vector3(0f, 0f, radius - thickness * 0.5f);
                    b.Box(bottom + new Vector3(0f, height * 0.5f, 0f) + offset, new Vector3(side, height, thickness), color, rotation);
                }
            }
            // Крышки.
            if (!alongX)
            {
                b.Box(bottom + new Vector3(0f, height - 0.002f, 0f), new Vector3(radius * 1.6f, 0.004f, radius * 1.6f), color);
                b.Box(bottom + new Vector3(0f, 0.002f, 0f), new Vector3(radius * 1.6f, 0.004f, radius * 1.6f), color);
            }
        }
    }
}
