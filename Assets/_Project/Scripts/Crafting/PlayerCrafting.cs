using System;
using System.Collections.Generic;
using UnityEngine;
using VoidMall.Console;
using VoidMall.Core;
using VoidMall.Items;
using VoidMall.Skills;
using Random = UnityEngine.Random;

namespace VoidMall.Crafting
{
    /// <summary>
    /// Крафт игрока: очередь «в руках» (идёт всегда, готовое — в инвентарь), изученные чертежи, постановка
    /// рецептов, починки и прогрева в очередь рук или станции. Ингредиенты списываются при постановке,
    /// отмена их возвращает. Навыки: «Мастеровой», «Портной», «Прораб» ускоряют, «Экономный» иногда возвращает
    /// ингредиент, «Прораб» добавляет блоки; рецепты могут требовать уровень навыка.
    /// </summary>
    [RequireComponent(typeof(PlayerInventory))]
    public sealed class PlayerCrafting : MonoBehaviour
    {
        public static PlayerCrafting Local { get; private set; }

        public CraftQueue Queue { get; } = new();

        private readonly HashSet<string> _learned = new();
        private readonly List<CraftJob> _completed = new();
        private PlayerInventory _inventory;

        /// <summary>Изучили чертёж, очередь рук изменилась.</summary>
        public event Action Changed;

        private void Awake() => _inventory = GetComponent<PlayerInventory>();

        private void OnEnable()
        {
            Local = this;
            Queue.Changed += RaiseChanged;
            PlayerInventory.UseBlockers.Add(WhyCannotRead);
            EventBus<ItemConsumedEvent>.Subscribe(OnConsumed);
        }

        private void OnDisable()
        {
            Queue.Changed -= RaiseChanged;
            PlayerInventory.UseBlockers.Remove(WhyCannotRead);
            EventBus<ItemConsumedEvent>.Unsubscribe(OnConsumed);
            if (Local == this)
                Local = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Local = null;

        private void RaiseChanged() => Changed?.Invoke();

        private static readonly Perf.Probe UpdateProbe = Perf.Register("Crafting.Hands");

        private void Update()
        {
            using var perf = Perf.Measure(UpdateProbe);
            if (Queue.IsEmpty)
                return;
            _completed.Clear();
            Queue.Tick(CraftingSystem.Instant.Value ? 1e6f : Time.deltaTime, _completed);
            foreach (var job in _completed)
            {
                CraftingSystem.Complete(job);
                foreach (var stack in job.Results)
                    Give(stack);
            }
        }

        /// <summary>В инвентарь, а что не влезло — на пол под ноги.</summary>
        private void Give(ItemStack stack)
        {
            if (_inventory.Add(stack) > 0)
                WorldItems.Instance?.Spawn(stack, transform.position + Vector3.up * 0.8f + transform.forward * 0.4f, Vector3.zero);
        }

        // ───────────────────────────── Знание рецептов ─────────────────────────────

        public bool IsLearned(RecipeDef recipe) => !recipe.Blueprint || _learned.Contains(recipe.Id) || CraftingSystem.UnlockAll.Value;

        public bool Learn(string recipeId) => _learned.Add(recipeId);

        public int LearnedCount => _learned.Count;

        /// <summary>Изученные чертежи (для сейва).</summary>
        public IReadOnlyCollection<string> Learned => _learned;

        /// <summary>Вернуть изученные чертежи из сейва.</summary>
        public void RestoreLearned(IEnumerable<string> recipes)
        {
            _learned.Clear();
            foreach (var id in recipes)
                _learned.Add(id);
        }

        private string WhyCannotRead(PlayerInventory inventory, ItemDef item, ItemStack stack)
        {
            if (inventory != _inventory || !item.IsReadable)
                return null;
            return _learned.Contains(item.TeachesRecipe) ? "Этот чертёж уже изучен" : null;
        }

        private void OnConsumed(in ItemConsumedEvent evt)
        {
            if (evt.Player != gameObject || !evt.Item.IsReadable)
                return;
            Learn(evt.Item.TeachesRecipe);
            // «Начитанный»: чертёж учит большему.
            PlayerSkills.AwardLocal(Aspect.Mental, 2f + 3f * PlayerSkills.LevelOf(SkillId.Bookworm));
            string name = CraftingSystem.Instance != null && CraftingSystem.Instance.TryGetRecipe(evt.Item.TeachesRecipe, out var recipe)
                ? recipe.DisplayName : evt.Item.TeachesRecipe;
            DevConsole.Log($"Изучен рецепт: {name}");
            RaiseChanged();
        }

        // ───────────────────────────── Проверки ─────────────────────────────

        /// <summary>Почему рецепт нельзя поставить count раз (null — можно). station — станция или null (руки).</summary>
        public string WhyCannotCraft(RecipeDef recipe, StationState station, int count = 1)
        {
            if (!IsLearned(recipe))
                return "Нужен чертёж";
            if (recipe.SkillLevel > 0 && !CraftingSystem.UnlockAll.Value && PlayerSkills.LevelOf(recipe.Skill) < recipe.SkillLevel)
                return $"Нужен навык: {SkillCatalog.Get(recipe.Skill).Name} {recipe.SkillLevel}";
            var where = station?.Kind ?? CraftStation.None;
            if (recipe.Station != where)
                return recipe.Station == CraftStation.None ? "Крафтится в руках (вкладка «Крафт»)" : $"Нужна станция: {CraftNames.Station(recipe.Station)}";
            var queue = station?.Queue ?? Queue;
            if (queue.Count + count > CraftQueue.Capacity)
                return "Очередь заполнена";
            if (!ItemSystem.Creative.Value)
                foreach (var ingredient in recipe.Ingredients)
                    if (ingredient.Item != null && Have(ingredient.Item) < ingredient.Count * count)
                        return $"Не хватает: {ingredient.Item.DisplayName}";
            return null;
        }

        /// <summary>Сколько таких предметов есть в инвентаре.</summary>
        public int Have(ItemDef item) => _inventory.Items != null && _inventory.Items.TryGetIndex(item.Id, out ushort index) ? _inventory.CountOf(index) : 0;

        /// <summary>Сколько штук можно поставить сейчас (по ингредиентам и месту в очереди).</summary>
        public int MaxCraftable(RecipeDef recipe, StationState station)
        {
            var queue = station?.Queue ?? Queue;
            int max = CraftQueue.Capacity - queue.Count;
            if (!ItemSystem.Creative.Value)
                foreach (var ingredient in recipe.Ingredients)
                    if (ingredient.Item != null)
                        max = Mathf.Min(max, Have(ingredient.Item) / Mathf.Max(1, ingredient.Count));
            return Mathf.Max(0, max);
        }

        /// <summary>Время одной штуки с бонусами навыков (без скорости станции).</summary>
        public static float CraftTime(RecipeDef recipe)
        {
            float time = recipe.Time * (1f - 0.15f * PlayerSkills.LevelOf(SkillId.Craftsman));
            if (recipe.Station == CraftStation.Sewing)
                time *= 1f - 0.2f * PlayerSkills.LevelOf(SkillId.Tailor);
            if (recipe.Category is RecipeCategory.Building or RecipeCategory.Stations)
                time *= 1f - 0.25f * PlayerSkills.LevelOf(SkillId.Foreman);
            return Mathf.Max(0.5f, time);
        }

        /// <summary>Сколько штук выйдет («Прораб» добавляет блоки).</summary>
        public static int ResultCount(RecipeDef recipe) =>
            recipe.ResultCount + (recipe.Category == RecipeCategory.Blocks ? PlayerSkills.LevelOf(SkillId.Foreman) : 0);

        // ───────────────────────────── Постановка в очередь ─────────────────────────────

        /// <summary>Поставить рецепт count раз. Null — поставлено, иначе — почему нет.</summary>
        public string Craft(RecipeDef recipe, StationState station, int count = 1)
        {
            string refusal = WhyCannotCraft(recipe, station, count);
            if (refusal != null)
                return refusal;
            var items = _inventory.Items;
            if (!items.TryGetIndex(recipe.Result.Id, out ushort result))
                return "Неизвестный предмет";
            var queue = station?.Queue ?? Queue;
            for (int n = 0; n < count; n++)
            {
                var job = new CraftJob
                {
                    Kind = CraftJobKind.Recipe,
                    Recipe = recipe,
                    Title = recipe.DisplayName,
                    Duration = CraftTime(recipe),
                    PointsAspect = recipe.Category switch
                    {
                        RecipeCategory.Building or RecipeCategory.Stations or RecipeCategory.Blocks => Aspect.Construction,
                        RecipeCategory.Food or RecipeCategory.Medicine => Aspect.Survival,
                        _ => Aspect.Mental,
                    },
                    Points = Mathf.Max(0.2f, recipe.Time / 15f),
                };
                foreach (var ingredient in recipe.Ingredients)
                {
                    if (ingredient.Item == null || !items.TryGetIndex(ingredient.Item.Id, out ushort def))
                        continue;
                    if (_inventory.TryConsume(def, ingredient.Count))
                        job.Taken.Add(items.Create(def, ingredient.Count));
                }
                job.Results.Add(items.Create(result, ResultCount(recipe)));
                // «Экономный»: шанс, что одна штука случайного ингредиента вернётся вместе с результатом.
                if (job.Taken.Count > 0 && Random.value < 0.1f * PlayerSkills.LevelOf(SkillId.Thrifty))
                    job.Results.Add(items.Create(job.Taken[Random.Range(0, job.Taken.Count)].Def, 1));
                queue.Enqueue(job);
            }
            return null;
        }

        /// <summary>Отменить позицию очереди: ингредиенты (или чинимый инструмент) — обратно.</summary>
        public void Cancel(CraftQueue queue, int index)
        {
            var job = queue.Cancel(index);
            if (job == null)
                return;
            foreach (var stack in job.Taken)
                Give(stack);
        }

        // ───────────────────────────── Починка (верстак) ─────────────────────────────

        /// <summary>Почему инструмент нельзя починить (null — можно).</summary>
        public string WhyCannotRepair(ItemStack stack, StationState station)
        {
            var def = _inventory.Items.Get(stack.Def);
            if (!def.IsRepairable)
                return "Не чинится";
            if (station == null || station.Kind != CraftStation.Workbench)
                return "Чинится на верстаке";
            if (stack.Durability >= 0.999f)
                return "Целый";
            if (def.Electric && PlayerSkills.LevelOf(SkillId.Technician) < 1 && !CraftingSystem.UnlockAll.Value)
                return "Электроинструмент: нужен навык «Техник» 1";
            if (station.Queue.IsFull)
                return "Очередь заполнена";
            if (!ItemSystem.Creative.Value)
                foreach (var cost in def.RepairCost)
                    if (cost.Item != null && Have(cost.Item) < cost.Count)
                        return $"Не хватает: {cost.Item.DisplayName}";
            return null;
        }

        public string Repair(ItemStack stack, StationState station)
        {
            string refusal = WhyCannotRepair(stack, station);
            if (refusal != null)
                return refusal;
            var items = _inventory.Items;
            var def = items.Get(stack.Def);
            var job = new CraftJob
            {
                Kind = CraftJobKind.Repair,
                Title = $"Починка: {def.DisplayName}",
                Duration = (8f + 25f * (1f - stack.Durability)) * (1f - 0.15f * PlayerSkills.LevelOf(SkillId.Craftsman)),
                PointsAspect = Aspect.Mental,
                Points = 0.5f,
            };
            foreach (var cost in def.RepairCost)
                if (cost.Item != null && items.TryGetIndex(cost.Item.Id, out ushort index) && _inventory.TryConsume(index, cost.Count))
                    job.Taken.Add(items.Create(index, cost.Count));
            // Сам инструмент уходит на верстак: при отмене вернётся как был, по готовности — целым.
            _inventory.Remove(stack);
            job.Taken.Add(stack);
            job.Results.Add(stack);
            job.Completed = () => stack.Durability = 1f;
            station.Queue.Enqueue(job);
            return null;
        }

        // ───────────────────────────── Прогрев испорченного (плита) ─────────────────────────────

        public string WhyCannotReheat(ItemStack stack, StationState station)
        {
            var def = _inventory.Items.Get(stack.Def);
            if (!stack.Spoiled || def.Category != ItemCategory.Food)
                return "Не испорчено";
            if (station == null || station.Kind != CraftStation.Cooking)
                return "Прогреть можно на плите или горелке";
            if (station.Queue.IsFull)
                return "Очередь заполнена";
            return null;
        }

        /// <summary>Прогреть одну штуку испорченной еды: она перестаёт отравлять.</summary>
        public string Reheat(ItemStack stack, StationState station)
        {
            string refusal = WhyCannotReheat(stack, station);
            if (refusal != null)
                return refusal;
            var items = _inventory.Items;
            var grid = _inventory.GridOf(stack);
            if (grid == null)
                return "Предмета нет в инвентаре";
            ItemStack taken;
            if (stack.Count > 1)
            {
                stack.Count--;
                grid.NotifyChanged();
                taken = items.Create(stack.Def, 1);
                taken.Spoiled = true;
            }
            else
            {
                _inventory.Remove(stack);
                taken = stack;
            }
            var job = new CraftJob
            {
                Kind = CraftJobKind.Reheat,
                Title = $"Прогреть: {items.Get(stack.Def).DisplayName}",
                Duration = 15f,
                PointsAspect = Aspect.Survival,
                Points = 0.3f,
            };
            job.Taken.Add(taken);
            var result = items.Create(taken.Def, 1);
            job.Results.Add(result);
            station.Queue.Enqueue(job);
            return null;
        }

        /// <summary>Забрать готовое из выдачи станции.</summary>
        public void TakeOutput(StationState station, ItemStack stack)
        {
            if (!station.Output.Remove(stack))
                return;
            int left = _inventory.Add(stack);
            if (left > 0)
                station.Output.TryAdd(stack);
        }

        public void TakeAllOutput(StationState station)
        {
            var stacks = new List<ItemStack>();
            foreach (var placement in station.Output.Items)
                stacks.Add(placement.Stack);
            foreach (var stack in stacks)
                TakeOutput(station, stack);
        }
    }
}
