using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VoidMall.Core;
using VoidMall.Crafting;
using VoidMall.Items;
using VoidMall.Skills;

namespace VoidMall.Editor
{
    /// <summary>Ассеты рецептов (этап 8): RecipeDef по таблице ниже и RecipeSet для CraftingSystem.</summary>
    public static class RecipeAssetsSetup
    {
        public const string RecipesFolder = "Assets/_Project/Data/Crafting/Recipes";
        public const string RecipeSetPath = "Assets/_Project/Data/Crafting/RecipeSet.asset";

        private sealed class Spec
        {
            public string Id, Result;
            public int Count = 1;
            public CraftStation Station;
            public float Time = 5f;
            public RecipeCategory Category;
            public (string item, int count)[] Ingredients;
            public bool Blueprint;
            public SkillId Skill;
            public int Level;
        }

        private const CraftStation Hands = CraftStation.None;
        private const CraftStation Bench = CraftStation.Workbench;
        private const CraftStation Sewing = CraftStation.Sewing;
        private const CraftStation Chem = CraftStation.Chemistry;
        private const CraftStation Cook = CraftStation.Cooking;
        private const CraftStation Crush = CraftStation.Crusher;

        private static readonly Spec[] Specs =
        {
            // ── Руки ──
            new() { Id = "recipe.bandage", Result = "item.bandage", Station = Hands, Time = 4f, Category = RecipeCategory.Medicine, Ingredients = new[] { ("material.fabric", 2) } },
            new() { Id = "recipe.splint", Result = "item.splint", Station = Hands, Time = 6f, Category = RecipeCategory.Medicine, Ingredients = new[] { ("material.plank", 1), ("material.fabric", 1) } },
            new() { Id = "recipe.torch", Result = "item.torch", Station = Hands, Time = 5f, Category = RecipeCategory.Tools, Ingredients = new[] { ("material.plank", 1), ("material.fabric", 1) } },
            new() { Id = "recipe.workbench", Result = "item.workbench", Station = Hands, Time = 20f, Category = RecipeCategory.Stations, Ingredients = new[] { ("material.plank", 6), ("material.nails", 10), ("material.scrap_metal", 2) } },
            new() { Id = "recipe.barricade", Result = "item.barricade", Station = Hands, Time = 12f, Category = RecipeCategory.Building, Ingredients = new[] { ("material.plank", 4), ("material.nails", 6) } },

            // ── Верстак ──
            new() { Id = "recipe.nails", Result = "material.nails", Count = 10, Station = Bench, Time = 5f, Category = RecipeCategory.Materials, Ingredients = new[] { ("material.scrap_metal", 1) } },
            new() { Id = "recipe.hammer", Result = "item.hammer", Station = Bench, Time = 15f, Category = RecipeCategory.Tools, Ingredients = new[] { ("material.scrap_metal", 2), ("material.plank", 1) } },
            new() { Id = "recipe.crowbar", Result = "item.crowbar", Station = Bench, Time = 20f, Category = RecipeCategory.Tools, Ingredients = new[] { ("material.scrap_metal", 3), ("material.pipe", 1) } },
            new() { Id = "recipe.sledgehammer", Result = "item.sledgehammer", Station = Bench, Time = 30f, Category = RecipeCategory.Tools, Ingredients = new[] { ("material.scrap_metal", 6), ("material.pipe", 1), ("material.plank", 1) }, Blueprint = true },
            new() { Id = "recipe.flashlight", Result = "item.flashlight", Station = Bench, Time = 20f, Category = RecipeCategory.Tools, Ingredients = new[] { ("material.plastic", 2), ("material.wire", 1), ("material.glass_shard", 1) }, Skill = SkillId.Technician, Level = 1 },
            new() { Id = "recipe.drill", Result = "item.drill", Station = Bench, Time = 45f, Category = RecipeCategory.Tools, Ingredients = new[] { ("material.scrap_metal", 4), ("material.wire", 3), ("material.plastic", 3), ("material.fuse", 1) }, Blueprint = true, Skill = SkillId.Technician, Level = 2 },
            new() { Id = "recipe.drill_pro", Result = "item.drill_pro", Station = Bench, Time = 60f, Category = RecipeCategory.Tools, Ingredients = new[] { ("material.scrap_metal", 6), ("material.wire", 4), ("material.plastic", 4), ("material.fuse", 2), ("material.electrical_tape", 1) }, Blueprint = true, Skill = SkillId.Technician, Level = 3 },
            new() { Id = "recipe.sewing_table", Result = "item.sewing_table", Station = Bench, Time = 25f, Category = RecipeCategory.Stations, Ingredients = new[] { ("material.plank", 5), ("material.scrap_metal", 3), ("material.screws", 10) } },
            new() { Id = "recipe.chem_table", Result = "item.chem_table", Station = Bench, Time = 25f, Category = RecipeCategory.Stations, Ingredients = new[] { ("material.plank", 4), ("material.glass_shard", 3), ("material.pipe", 1), ("material.screws", 8) } },
            new() { Id = "recipe.burner", Result = "item.burner", Station = Bench, Time = 20f, Category = RecipeCategory.Stations, Ingredients = new[] { ("material.scrap_metal", 3), ("material.pipe", 1), ("material.screws", 4) }, Skill = SkillId.Technician, Level = 1 },
            new() { Id = "recipe.table", Result = "item.table", Station = Bench, Time = 12f, Category = RecipeCategory.Building, Ingredients = new[] { ("material.plank", 4), ("material.nails", 6) } },
            new() { Id = "recipe.shelf", Result = "item.shelf", Station = Bench, Time = 16f, Category = RecipeCategory.Building, Ingredients = new[] { ("material.scrap_metal", 4), ("material.screws", 8) } },
            new() { Id = "recipe.planks", Result = "item.block.planks", Count = 4, Station = Bench, Time = 10f, Category = RecipeCategory.Blocks, Ingredients = new[] { ("material.plank", 2), ("material.nails", 4) } },
            new() { Id = "recipe.block_wood", Result = "item.block.wood", Count = 4, Station = Bench, Time = 4f, Category = RecipeCategory.Blocks, Ingredients = new[] { ("material.plank", 2) } },
            new() { Id = "recipe.block_metal", Result = "item.block.metal", Count = 2, Station = Bench, Time = 6f, Category = RecipeCategory.Blocks, Ingredients = new[] { ("material.scrap_metal", 3) } },
            new() { Id = "recipe.door", Result = "item.door", Station = Bench, Time = 20f, Category = RecipeCategory.Building, Ingredients = new[] { ("material.plank", 4), ("material.nails", 6), ("material.screws", 2) } },
            new() { Id = "recipe.gate", Result = "item.gate", Station = Bench, Time = 30f, Category = RecipeCategory.Building, Ingredients = new[] { ("material.scrap_metal", 6), ("material.screws", 6), ("material.pipe", 1) } },
            new() { Id = "recipe.sliding_door_1", Result = "item.sliding_door_1", Station = Bench, Time = 30f, Category = RecipeCategory.Building, Ingredients = new[] { ("material.glass_shard", 3), ("material.scrap_metal", 2), ("material.wire", 2), ("material.fuse", 1) }, Skill = SkillId.Technician, Level = 1 },
            new() { Id = "recipe.sliding_door_2", Result = "item.sliding_door_2", Station = Bench, Time = 40f, Category = RecipeCategory.Building, Ingredients = new[] { ("material.glass_shard", 5), ("material.scrap_metal", 4), ("material.wire", 3), ("material.fuse", 1) }, Skill = SkillId.Technician, Level = 1 },
            new() { Id = "recipe.sliding_door_4", Result = "item.sliding_door_4", Station = Bench, Time = 60f, Category = RecipeCategory.Building, Ingredients = new[] { ("material.glass_shard", 9), ("material.scrap_metal", 7), ("material.wire", 4), ("material.fuse", 2) }, Skill = SkillId.Technician, Level = 2 },
            new() { Id = "recipe.shape_template", Result = "item.shape_template", Station = Bench, Time = 10f, Category = RecipeCategory.Tools, Ingredients = new[] { ("material.plank", 2), ("material.nails", 4) } },
            new() { Id = "recipe.crusher", Result = "item.crusher", Station = Bench, Time = 40f, Category = RecipeCategory.Stations, Ingredients = new[] { ("material.scrap_metal", 8), ("material.pipe", 2), ("material.screws", 10), ("material.wire", 2) }, Skill = SkillId.Technician, Level = 1 },

            // ── Швейный стол ──
            new() { Id = "recipe.fabric_hoodie", Result = "material.fabric", Count = 4, Station = Sewing, Time = 6f, Category = RecipeCategory.Materials, Ingredients = new[] { ("item.hoodie", 1) } },
            new() { Id = "recipe.fabric_jeans", Result = "material.fabric", Count = 4, Station = Sewing, Time = 6f, Category = RecipeCategory.Materials, Ingredients = new[] { ("item.jeans", 1) } },
            new() { Id = "recipe.gloves", Result = "item.gloves_work", Station = Sewing, Time = 12f, Category = RecipeCategory.Clothing, Ingredients = new[] { ("material.fabric", 2), ("material.rubber", 1) } },
            new() { Id = "recipe.cap", Result = "item.cap", Station = Sewing, Time = 10f, Category = RecipeCategory.Clothing, Ingredients = new[] { ("material.fabric", 2), ("material.plastic", 1) } },
            new() { Id = "recipe.school_backpack", Result = "item.school_backpack", Station = Sewing, Time = 30f, Category = RecipeCategory.Clothing, Ingredients = new[] { ("material.fabric", 6), ("material.plastic", 2) } },
            new() { Id = "recipe.sleeping_bag", Result = "item.sleeping_bag", Station = Sewing, Time = 30f, Category = RecipeCategory.Building, Ingredients = new[] { ("material.fabric", 6), ("material.foam", 3) } },
            new() { Id = "recipe.padded_vest", Result = "item.padded_vest", Station = Sewing, Time = 40f, Category = RecipeCategory.Clothing, Ingredients = new[] { ("material.fabric", 6), ("material.foam", 4) }, Blueprint = true, Skill = SkillId.Tailor, Level = 1 },
            new() { Id = "recipe.hiking_backpack", Result = "item.hiking_backpack", Station = Sewing, Time = 50f, Category = RecipeCategory.Clothing, Ingredients = new[] { ("material.fabric", 10), ("material.plastic", 3), ("material.rubber", 2) }, Blueprint = true, Skill = SkillId.Tailor, Level = 1 },

            // ── Химстол ──
            new() { Id = "recipe.antiseptic", Result = "item.antiseptic", Station = Chem, Time = 15f, Category = RecipeCategory.Medicine, Ingredients = new[] { ("material.chemicals", 1), ("item.water_bottle", 1) } },
            new() { Id = "recipe.painkillers", Result = "item.painkillers", Count = 2, Station = Chem, Time = 20f, Category = RecipeCategory.Medicine, Ingredients = new[] { ("material.chemicals", 2), ("material.plastic", 1) }, Skill = SkillId.FieldSurgery, Level = 1 },

            // ── Дробилка / компрессор ──
            new() { Id = "recipe.crush_concrete", Result = "material.concrete_rubble", Count = 2, Station = Crush, Time = 4f, Category = RecipeCategory.Materials, Ingredients = new[] { ("item.block.concrete", 1) } },
            new() { Id = "recipe.crush_terrazzo", Result = "material.concrete_rubble", Count = 2, Station = Crush, Time = 4f, Category = RecipeCategory.Materials, Ingredients = new[] { ("item.block.terrazzo", 1) } },
            new() { Id = "recipe.crush_tile", Result = "material.concrete_rubble", Count = 1, Station = Crush, Time = 3f, Category = RecipeCategory.Materials, Ingredients = new[] { ("item.block.floor_tile", 1) } },
            new() { Id = "recipe.compress_concrete", Result = "item.block.concrete", Count = 1, Station = Crush, Time = 6f, Category = RecipeCategory.Blocks, Ingredients = new[] { ("material.concrete_rubble", 3) } },
            new() { Id = "recipe.compress_concrete_cement", Result = "item.block.concrete", Count = 2, Station = Crush, Time = 8f, Category = RecipeCategory.Blocks, Ingredients = new[] { ("material.concrete_rubble", 3), ("material.cement", 1) } },

            // ── Плита и горелка ──
            new() { Id = "recipe.dish_noodles_meat", Result = "item.dish_noodles_meat", Station = Cook, Time = 30f, Category = RecipeCategory.Food, Ingredients = new[] { ("item.noodles", 1), ("item.canned_meat", 1), ("item.water_bottle", 1) } },
            new() { Id = "recipe.dish_beans_meat", Result = "item.dish_beans_meat", Station = Cook, Time = 25f, Category = RecipeCategory.Food, Ingredients = new[] { ("item.canned_beans", 1), ("item.canned_meat", 1) }, Skill = SkillId.Cook, Level = 1 },
            new() { Id = "recipe.hot_chocolate", Result = "item.hot_chocolate", Station = Cook, Time = 15f, Category = RecipeCategory.Food, Ingredients = new[] { ("item.chocolate", 1), ("item.water_bottle", 1) } },
        };

        public static void CreateRecipeAssets()
        {
            ProjectSetup.EnsureFolder(RecipesFolder);
            var items = AssetDatabase.LoadAssetAtPath<ItemSet>(ItemAssetsSetup.ItemSetPath);
            var byId = new Dictionary<string, ItemDef>();
            if (items != null)
                foreach (var item in items.Items)
                    if (item != null)
                        byId[item.Id] = item;

            var set = AssetDatabase.LoadAssetAtPath<RecipeSet>(RecipeSetPath);
            if (set == null)
            {
                set = ScriptableObject.CreateInstance<RecipeSet>();
                AssetDatabase.CreateAsset(set, RecipeSetPath);
            }
            set.Recipes.Clear();

            foreach (var spec in Specs)
            {
                string path = $"{RecipesFolder}/{spec.Id.Replace("recipe.", "")}.asset";
                var def = AssetDatabase.LoadAssetAtPath<RecipeDef>(path);
                if (def == null)
                {
                    def = ScriptableObject.CreateInstance<RecipeDef>();
                    AssetDatabase.CreateAsset(def, path);
                }
                var so = new SerializedObject(def);
                so.FindProperty("_id").stringValue = spec.Id;
                so.ApplyModifiedPropertiesWithoutUndo();
                if (!byId.TryGetValue(spec.Result, out def.Result))
                    Debug.LogError($"[Setup] Recipe {spec.Id}: unknown result '{spec.Result}'.");
                def.ResultCount = spec.Count;
                def.Station = spec.Station;
                def.Time = spec.Time;
                def.Category = spec.Category;
                def.Blueprint = spec.Blueprint;
                def.Skill = spec.Skill;
                def.SkillLevel = spec.Level;
                def.Ingredients = new List<ItemAmount>();
                foreach (var (item, count) in spec.Ingredients)
                {
                    if (byId.TryGetValue(item, out var ingredient))
                        def.Ingredients.Add(new ItemAmount(ingredient, count));
                    else
                        Debug.LogError($"[Setup] Recipe {spec.Id}: unknown ingredient '{item}'.");
                }
                EditorUtility.SetDirty(def);
                set.Recipes.Add(def);
            }
            EditorUtility.SetDirty(set);
        }
    }
}
