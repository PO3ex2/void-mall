using System;
using System.Collections.Generic;
using VoidMall.Items;
using VoidMall.Skills;

namespace VoidMall.Crafting
{
    public enum CraftJobKind : byte
    {
        Recipe,
        Repair,
        Reheat,
    }

    /// <summary>
    /// Одна позиция очереди: ингредиенты уже списаны (Taken — вернуть при отмене), результат готов заранее
    /// (Results — выдать по завершении). Так одинаково устроены рецепты, починка и прогрев испорченной еды.
    /// </summary>
    public sealed class CraftJob
    {
        public CraftJobKind Kind;
        public RecipeDef Recipe;
        public string Title;
        public float Duration;
        public float Elapsed;
        public readonly List<ItemStack> Taken = new();
        public readonly List<ItemStack> Results = new();

        /// <summary>Очки навыков за завершение.</summary>
        public Aspect PointsAspect = Aspect.Mental;
        public float Points;

        /// <summary>Действие при завершении (починка: прочность до 100%).</summary>
        public Action Completed;

        public float Progress => Duration <= 0f ? 1f : Math.Min(1f, Elapsed / Duration);
        public float Remaining => Math.Max(0f, Duration - Elapsed);
    }

    /// <summary>
    /// Очередь крафта (чистая логика, EditMode-тесты): до 8 позиций, время идёт у первой, остаток переходит к
    /// следующей. Позиция не завершается, пока canComplete не разрешит (у станции кончилось место в выдаче).
    /// </summary>
    public sealed class CraftQueue
    {
        public const int Capacity = 8;

        private readonly List<CraftJob> _jobs = new();

        public IReadOnlyList<CraftJob> Jobs => _jobs;
        public int Count => _jobs.Count;
        public bool IsEmpty => _jobs.Count == 0;
        public bool IsFull => _jobs.Count >= Capacity;
        public CraftJob Current => _jobs.Count > 0 ? _jobs[0] : null;

        /// <summary>Позиция ждёт завершения (готова, но некуда выдать).</summary>
        public bool Blocked { get; private set; }

        public event Action Changed;

        public bool Enqueue(CraftJob job)
        {
            if (job == null || IsFull)
                return false;
            _jobs.Add(job);
            Changed?.Invoke();
            return true;
        }

        /// <summary>Убрать позицию (ингредиенты возвращает вызывающий по job.Taken).</summary>
        public CraftJob Cancel(int index)
        {
            if (index < 0 || index >= _jobs.Count)
                return null;
            var job = _jobs[index];
            _jobs.RemoveAt(index);
            Blocked = false;
            Changed?.Invoke();
            return job;
        }

        /// <summary>Продвинуть очередь на seconds (уже с учётом скорости). Завершённые — в completed.</summary>
        public void Tick(float seconds, List<CraftJob> completed, Func<CraftJob, bool> canComplete = null)
        {
            Blocked = false;
            while (seconds > 0f && _jobs.Count > 0)
            {
                var job = _jobs[0];
                float need = job.Duration - job.Elapsed;
                if (seconds < need)
                {
                    job.Elapsed += seconds;
                    return;
                }
                job.Elapsed = job.Duration;
                if (canComplete != null && !canComplete(job))
                {
                    Blocked = true;
                    return;
                }
                seconds -= need;
                _jobs.RemoveAt(0);
                completed.Add(job);
                Changed?.Invoke();
            }
        }
    }
}
