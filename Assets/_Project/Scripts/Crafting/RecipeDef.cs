using System.Collections.Generic;
using UnityEngine;
using VoidMall.Core;
using VoidMall.Items;
using VoidMall.Skills;

namespace VoidMall.Crafting
{
    public enum RecipeCategory : byte
    {
        Tools,
        Materials,
        Medicine,
        Clothing,
        Food,
        Building,
        Stations,
        Blocks,
    }

    /// <summary>
    /// Рецепт (ARCHITECTURE §8.2): что получится, из чего, где (руки или станция), сколько секунд.
    /// Может требовать чертёж (изучается из лута) и уровень навыка (перфоратор — «Техник» 2).
    /// </summary>
    [CreateAssetMenu(menuName = "Void Mall/Crafting/Recipe", fileName = "Recipe")]
    public sealed class RecipeDef : ContentDef
    {
        public ItemDef Result;
        [Min(1)] public int ResultCount = 1;
        public List<ItemAmount> Ingredients = new();

        [Tooltip("Где крафтится: None — в руках.")]
        public CraftStation Station;

        [Tooltip("Время одной штуки, с (до бонусов навыков и скорости станции).")]
        [Min(0.5f)] public float Time = 5f;

        public RecipeCategory Category;

        [Tooltip("Открывается чертежом из лута (иначе известен сразу).")]
        public bool Blueprint;

        [Tooltip("Нужный навык (уровень 0 — не нужен).")]
        public SkillId Skill;
        [Range(0, 3)] public int SkillLevel;

        public string DisplayName => Result != null ? Result.DisplayName : Id;
    }

    public static class CraftNames
    {
        public static string Station(CraftStation station) => station switch
        {
            CraftStation.Workbench => "Верстак",
            CraftStation.Sewing => "Швейный стол",
            CraftStation.Chemistry => "Химстол",
            CraftStation.Cooking => "Плита или горелка",
            CraftStation.Crusher => "Дробилка",
            _ => "Руки",
        };

        public static string Category(RecipeCategory category) => category switch
        {
            RecipeCategory.Tools => "Инструменты",
            RecipeCategory.Materials => "Материалы",
            RecipeCategory.Medicine => "Медицина",
            RecipeCategory.Clothing => "Одежда",
            RecipeCategory.Food => "Еда",
            RecipeCategory.Building => "Постройки",
            RecipeCategory.Stations => "Станции",
            _ => "Блоки",
        };
    }
}
