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

        /// <summary>Функциональный блок (дверь, ворота): занимает клетки, рисуется своей моделью, сетка его не рисует.</summary>
        Functional,
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
        Soil,
        LampPanel,
        EmergencyLamp,
        Skylight,
        Terrazzo,
        CheckerTile,
        Linoleum,
        Shutter,
        Asphalt,
        ParkingLineU,
        ParkingLineV,
        FacadePanel,
    }

    /// <summary>Что делает функциональный блок.</summary>
    public enum FunctionalKind : byte
    {
        None,
        Door,
        Gate,
        SlidingDoor,
    }

    /// <summary>Светящийся материал: как им управляет LightingDirector (см. LampRules).</summary>
    public enum LampKind : byte
    {
        None,

        /// <summary>Обычная лампа: гаснет ночью волнами по участкам, бывает нестабильной или мёртвой.</summary>
        Lamp,

        /// <summary>Аварийная лампа: тусклая, горит всегда.</summary>
        Emergency,

        /// <summary>Световой фонарь атриума: яркость следует за временем суток.</summary>
        Skylight,
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

        [Tooltip("Гладкий двойник блока (тот же узор): им облицовываются аномалии в полу из этого материала.")]
        public MaterialDef SmoothVariant;

        [Header("Свечение")]
        public LampKind Lamp;

        [Tooltip("Яркость эмиссии (цвет — Tint).")]
        [Min(0)] public float Emission;

        [Header("Функциональный блок")]
        public FunctionalKind Function;

        [Tooltip("Размер в клетках без поворота: ширина (X), высота (Y), глубина (Z).")]
        public Vector3Int FunctionalSize = Vector3Int.one;
    }
}
