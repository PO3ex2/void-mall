using System;

namespace VoidMall.World
{
    /// <summary>
    /// Клетка мира, 4 байта. Material 0 — воздух.
    /// Value: для блоков — форма (0 = полный куб; плиты, ступени, скосы — позже), для гладких материалов — плотность (128 = поверхность).
    /// Meta: для блоков — поворот (биты 0–4, 24 ориентации) и флаги (биты 5–7).
    /// Повреждение хранится отдельно (разреженно в чанке): повреждённых блоков мало.
    /// </summary>
    [Serializable]
    public struct Cell : IEquatable<Cell>
    {
        public const byte FullCube = 0;
        public const byte SurfaceDensity = 128;

        public ushort Material;
        public byte Value;
        public byte Meta;

        public Cell(ushort material, byte value = FullCube, byte meta = 0)
        {
            Material = material;
            Value = value;
            Meta = meta;
        }

        public static readonly Cell Air = default;

        public bool IsAir => Material == 0;

        public byte Shape => Value;
        public byte Rotation => (byte)(Meta & 0x1F);

        public bool Equals(Cell other) => Material == other.Material && Value == other.Value && Meta == other.Meta;
        public override bool Equals(object obj) => obj is Cell other && Equals(other);
        public override int GetHashCode() => Material | (Value << 16) | (Meta << 24);
        public override string ToString() => $"Cell(mat {Material}, value {Value}, meta {Meta})";

        public static bool operator ==(Cell a, Cell b) => a.Equals(b);
        public static bool operator !=(Cell a, Cell b) => !a.Equals(b);
    }
}
