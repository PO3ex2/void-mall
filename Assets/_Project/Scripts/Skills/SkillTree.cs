using System;

namespace VoidMall.Skills
{
    /// <summary>
    /// Очки аспектов и уровни навыков одного игрока. Чистая логика (EditMode-тесты): очки копятся дробно,
    /// тратятся целыми; уровень навыка покупается по цене SkillCatalog.Cost, с учётом требований.
    /// </summary>
    public sealed class SkillTree
    {
        private readonly float[] _points = new float[Enum.GetValues(typeof(Aspect)).Length];
        private readonly float[] _earned = new float[Enum.GetValues(typeof(Aspect)).Length];
        private readonly int[] _levels = new int[Enum.GetValues(typeof(SkillId)).Length];

        /// <summary>Что-то изменилось (очки или уровни).</summary>
        public event Action Changed;

        /// <summary>Свободные очки аспекта (дробные — копятся).</summary>
        public float Points(Aspect aspect) => _points[(int)aspect];

        /// <summary>Всего заработано за игру.</summary>
        public float Earned(Aspect aspect) => _earned[(int)aspect];

        public int Level(SkillId id) => _levels[(int)id];

        public void Award(Aspect aspect, float amount)
        {
            if (amount <= 0f)
                return;
            int before = (int)_points[(int)aspect];
            _points[(int)aspect] += amount;
            _earned[(int)aspect] += amount;
            // Интерфейсу интересны только целые очки.
            if ((int)_points[(int)aspect] != before)
                Changed?.Invoke();
        }

        /// <summary>Почему нельзя взять следующий уровень (null — можно).</summary>
        public string WhyCannotLearn(SkillId id)
        {
            var info = SkillCatalog.Get(id);
            int next = Level(id) + 1;
            if (next > info.MaxLevel)
                return "Навык изучен полностью";
            if (info.Requires.HasValue && next >= info.RequireFrom && Level(info.Requires.Value) < info.RequiresLevel)
                return $"Нужно: {SkillCatalog.Get(info.Requires.Value).Name} {info.RequiresLevel}";
            int cost = SkillCatalog.Cost(next);
            if (Points(info.Aspect) < cost)
                return $"Нужно очков: {cost} ({SkillCatalog.AspectName(info.Aspect).ToLowerInvariant()})";
            return null;
        }

        public bool TryLearn(SkillId id)
        {
            if (WhyCannotLearn(id) != null)
                return false;
            var info = SkillCatalog.Get(id);
            int next = Level(id) + 1;
            _points[(int)info.Aspect] -= SkillCatalog.Cost(next);
            _levels[(int)id] = next;
            Changed?.Invoke();
            return true;
        }

        /// <summary>Сбросить уровни и вернуть потраченные очки.</summary>
        public void Reset()
        {
            foreach (var info in SkillCatalog.All)
            {
                for (int level = 1; level <= _levels[(int)info.Id]; level++)
                    _points[(int)info.Aspect] += SkillCatalog.Cost(level);
                _levels[(int)info.Id] = 0;
            }
            Changed?.Invoke();
        }

        /// <summary>Отладка: выставить уровень без очков.</summary>
        public void SetLevel(SkillId id, int level)
        {
            _levels[(int)id] = Math.Clamp(level, 0, SkillCatalog.Get(id).MaxLevel);
            Changed?.Invoke();
        }
    }
}
