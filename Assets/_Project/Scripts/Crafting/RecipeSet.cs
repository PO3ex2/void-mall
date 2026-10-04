using System.Collections.Generic;
using UnityEngine;

namespace VoidMall.Crafting
{
    /// <summary>Набор рецептов мира (CraftingSystem).</summary>
    [CreateAssetMenu(menuName = "Void Mall/Crafting/Recipe Set", fileName = "RecipeSet")]
    public sealed class RecipeSet : ScriptableObject
    {
        public List<RecipeDef> Recipes = new();
    }
}
