using System;
using System.Collections.Generic;
using UnityEngine;
using VoidMall.Items;
using VoidMall.Skills;

namespace VoidMall.Crafting
{
    /// <summary>Позиция очереди крафта в сейве: ингредиенты (вернуть при отмене) и результат — Id в общем списке стаков.</summary>
    [Serializable]
    public sealed class SavedJob
    {
        public string Kind;
        public string Recipe;
        public string Title;
        public float Duration, Elapsed;
        public List<int> Taken = new();
        public List<int> Results = new();
        public string Aspect;
        public float Points;
    }

    /// <summary>Очередь крафта в сейве (крафт в руках или на станции).</summary>
    [Serializable]
    public sealed class QueueData
    {
        public List<SavedStack> Stacks = new();
        public List<SavedJob> Jobs = new();
    }

    /// <summary>Станция в сейве: очередь, выдача, топливо (этап 9).</summary>
    [Serializable]
    public sealed class SavedStation
    {
        public string Prop;
        public string Kind;
        public string Name;
        public float Speed;
        public bool Autonomous, Heater;
        public float Heat, HeatRadius, Fuel;
        public Vector3 Position;
        public QueueData Queue = new();
        public List<SavedStack> Output = new();
    }

    [Serializable]
    public sealed class StationsData
    {
        public List<SavedStation> Stations = new();
    }

    /// <summary>Запись и чтение очередей крафта: вид позиции и рецепт — по имени и ID, действие завершения — по виду.</summary>
    public static class CraftSave
    {
        public static QueueData Write(CraftQueue queue, IItemIds items)
        {
            var data = new QueueData();
            foreach (var job in queue.Jobs)
            {
                var saved = new SavedJob
                {
                    Kind = job.Kind.ToString(),
                    Recipe = job.Recipe != null ? job.Recipe.Id : null,
                    Title = job.Title,
                    Duration = job.Duration,
                    Elapsed = job.Elapsed,
                    Aspect = job.PointsAspect.ToString(),
                    Points = job.Points,
                };
                foreach (var stack in job.Taken)
                    saved.Taken.Add(ItemSnapshot.WriteStack(stack, "taken", -1, items, data.Stacks).Id);
                foreach (var stack in job.Results)
                    saved.Results.Add(ItemSnapshot.WriteStack(stack, "result", -1, items, data.Stacks).Id);
                data.Jobs.Add(saved);
            }
            return data;
        }

        /// <summary>Вернуть позиции в очередь (очередь должна быть пустой). recipe — поиск рецепта по ID.</summary>
        public static void Read(QueueData data, CraftQueue queue, IItemIds items, Func<string, RecipeDef> recipe)
        {
            if (data == null)
                return;
            var stacks = ItemSnapshot.ReadAll(data.Stacks, items);
            foreach (var saved in data.Jobs)
            {
                if (!Enum.TryParse(saved.Kind, out CraftJobKind kind))
                    continue;
                var job = new CraftJob
                {
                    Kind = kind,
                    Recipe = string.IsNullOrEmpty(saved.Recipe) ? null : recipe(saved.Recipe),
                    Title = saved.Title,
                    Duration = saved.Duration,
                    Elapsed = saved.Elapsed,
                    PointsAspect = Enum.TryParse(saved.Aspect, out Aspect aspect) ? aspect : Aspect.Mental,
                    Points = saved.Points,
                };
                if (kind == CraftJobKind.Recipe && job.Recipe == null)
                    continue; // рецепт убрали из игры — позиция теряется (ингредиенты тоже: вернуть их некуда безопасно)
                foreach (int id in saved.Taken)
                    if (stacks.TryGetValue(id, out var stack))
                        job.Taken.Add(stack);
                foreach (int id in saved.Results)
                    if (stacks.TryGetValue(id, out var stack))
                        job.Results.Add(stack);
                // Починка: по завершении прочность до 100% (у прогрева результат уже свежий).
                if (kind == CraftJobKind.Repair && job.Results.Count > 0)
                {
                    var repaired = job.Results[0];
                    job.Completed = () => repaired.Durability = 1f;
                }
                queue.Enqueue(job);
            }
        }
    }
}
