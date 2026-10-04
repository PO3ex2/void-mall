using System.Text;
using VoidMall.Console;
using VoidMall.Skills;

namespace VoidMall.Crafting
{
    internal static class CraftCommands
    {
        [ConsoleCommand("recipes", "Рецепты: где крафтятся, из чего, известны ли. recipes [фильтр].")]
        private static string Recipes(string filter = "")
        {
            var system = CraftingSystem.Instance;
            if (system == null)
                return "Нет системы крафта (запустите полигон мира).";
            var crafting = PlayerCrafting.Local;
            var sb = new StringBuilder();
            foreach (var recipe in system.Recipes)
            {
                if (!string.IsNullOrEmpty(filter) && !recipe.Id.Contains(filter) && !recipe.DisplayName.ToLowerInvariant().Contains(filter.ToLowerInvariant()))
                    continue;
                sb.Append($"{recipe.Id,-28} {recipe.DisplayName} ×{recipe.ResultCount} — {CraftNames.Station(recipe.Station)}, {recipe.Time:0} с:");
                foreach (var ingredient in recipe.Ingredients)
                    if (ingredient.Item != null)
                        sb.Append($" {ingredient.Item.DisplayName} ×{ingredient.Count}");
                if (recipe.Blueprint)
                    sb.Append(crafting != null && crafting.IsLearned(recipe) ? " [чертёж изучен]" : " [нужен чертёж]");
                if (recipe.SkillLevel > 0)
                    sb.Append($" [{SkillCatalog.Get(recipe.Skill).Name} {recipe.SkillLevel}]");
                sb.Append('\n');
            }
            return sb.Length > 0 ? sb.ToString().TrimEnd() : "Ничего не найдено.";
        }

        [ConsoleCommand("craft_learn", "Изучить рецепт без чертежа: craft_learn <id рецепта>.", Flags = ConsoleFlags.Cheat)]
        private static string Learn(string id)
        {
            var system = CraftingSystem.Instance;
            var crafting = PlayerCrafting.Local;
            if (system == null || crafting == null)
                return "Нет игрока с крафтом.";
            if (!system.TryGetRecipe(id, out var recipe) && !system.TryGetRecipe("recipe." + id, out recipe))
                return $"Нет рецепта '{id}'. Список: recipes";
            crafting.Learn(recipe.Id);
            return $"Изучен: {recipe.DisplayName}";
        }
    }
}
