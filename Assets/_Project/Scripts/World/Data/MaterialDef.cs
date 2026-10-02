using UnityEngine;
using VoidMall.Core;

namespace VoidMall.World
{
    public enum MaterialKind : byte
    {
        Air,

        /// <summary>Блок как в Minecraft: куб (позже — формы) по сетке.</summary>
        Block,

        /// <summary>Гладкий воксель (Surface Nets) — этап 3.</summary>
        Smooth,
    }

    /// <summary>Процедурные текстуры-заглушки. Настоящие PBR-текстуры заменят их на этапе генерации мола.</summary>
    public enum BlockPattern : byte
    {
        Flat,
        Bedrock,
        Concrete,
        Carpet,
        Wallpaper,
        Drywall,
        CeilingTile,
        FloorTile,
        Metal,
        Glass,
        Wood,
    }

    /// <summary>Описание материала мира (блок или гладкий воксель). Порядок в каталоге = runtime-индекс в Cell.Material.</summary>
    [CreateAssetMenu(menuName = "Void Mall/World/Material", fileName = "Material")]
    public sealed class MaterialDef : ContentDef
    {
        public string DisplayName;
        public MaterialKind Kind = MaterialKind.Block;

        [Tooltip("Прозрачный (стекло): не скрывает грани соседей, рисуется отдельным проходом.")]
        public bool Transparent;

        [Tooltip("Нельзя сломать (дно мира).")]
        public bool Unbreakable;

        [Tooltip("Прочность, условные HP. Временный молоток наносит 100 за удар.")]
        [Min(1)] public int Hardness = 300;

        [Tooltip("Можно ставить временным инструментом.")]
        public bool Placeable = true;

        [Header("Текстура")]
        public BlockPattern Pattern = BlockPattern.Flat;
        public Color Tint = Color.white;
    }
}
