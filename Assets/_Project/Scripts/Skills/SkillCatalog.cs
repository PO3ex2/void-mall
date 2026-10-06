using System.Collections.Generic;

namespace VoidMall.Skills
{
    /// <summary>Аспект навыков (GDD §5.2): действия приносят очки своего аспекта, очки тратятся на его навыки.</summary>
    public enum Aspect : byte
    {
        Physical,
        Mental,
        Construction,
        Combat,
        Survival,
    }

    public enum SkillId : byte
    {
        // Физические.
        Loader,
        Endurance,
        Athlete,
        StrongBones,

        // Умственные.
        Craftsman,
        Thrifty,
        Bookworm,
        Technician,

        // Строительные.
        Carpenter,
        Wrecker,
        Salvager,
        Foreman,

        // Выживание.
        FieldSurgery,
        Cook,
        Hardening,
        SteadyNerves,
        Tailor,
    }

    public readonly struct SkillInfo
    {
        public readonly SkillId Id;
        public readonly string Name;
        public readonly Aspect Aspect;
        public readonly string Description;

        /// <summary>Что даёт каждый уровень (индекс = уровень − 1).</summary>
        public readonly string[] Levels;

        /// <summary>Требование для уровня 2 и выше (или для всех уровней, если RequireFrom = 1).</summary>
        public readonly SkillId? Requires;
        public readonly int RequiresLevel;
        public readonly int RequireFrom;

        public int MaxLevel => Levels.Length;

        public SkillInfo(SkillId id, string name, Aspect aspect, string description, string[] levels,
            SkillId? requires = null, int requiresLevel = 0, int requireFrom = 1)
        {
            Id = id;
            Name = name;
            Aspect = aspect;
            Description = description;
            Levels = levels;
            Requires = requires;
            RequiresLevel = requiresLevel;
            RequireFrom = requireFrom;
        }
    }

    /// <summary>
    /// Навыки и их числа (подбираются на плейтесте). Эффекты применяют системы, которые спрашивают уровень:
    /// инвентарь (Грузчик), выживание, крафт, инструменты. Боевые навыки — с боем (этап 11).
    /// </summary>
    public static class SkillCatalog
    {
        /// <summary>Цена уровня в очках аспекта: 1 → 3, 2 → 6, 3 → 10.</summary>
        public static int Cost(int level) => level switch { 1 => 3, 2 => 6, _ => 10 };

        public static string AspectName(Aspect aspect) => aspect switch
        {
            Aspect.Physical => "Физические",
            Aspect.Mental => "Умственные",
            Aspect.Construction => "Строительные",
            Aspect.Combat => "Боевые",
            _ => "Выживание",
        };

        public static readonly SkillInfo[] All =
        {
            new(SkillId.Loader, "Грузчик", Aspect.Physical, "Носите больше без штрафов.",
                new[] { "+5 кг к лимиту веса", "+10 кг", "+15 кг" }),
            new(SkillId.Endurance, "Выносливость", Aspect.Physical, "Больше стамины.",
                new[] { "+10% максимума стамины", "+20%", "+30%" }),
            new(SkillId.Athlete, "Атлет", Aspect.Physical, "Спринт и прыжки тратят меньше стамины; бхоп — прыжок в момент приземления не теряет скорость.",
                new[] { "−15% расхода стамины, бхоп до 1.5× спринта", "−30%, бхоп до 1.75×", "−45%, бхоп до 2×" }),
            new(SkillId.StrongBones, "Крепкие кости", Aspect.Physical, "Падения калечат меньше.",
                new[] { "−20% урона от падений", "−40%", "−60%" }),

            new(SkillId.Craftsman, "Мастеровой", Aspect.Mental, "Крафт идёт быстрее.",
                new[] { "Крафт быстрее на 15%", "на 30%", "на 45%" }),
            new(SkillId.Thrifty, "Экономный", Aspect.Mental, "Иногда ингредиент остаётся.",
                new[] { "10% шанс вернуть ингредиент", "20%", "30%" }),
            new(SkillId.Bookworm, "Начитанный", Aspect.Mental, "Чертежи учат большему.",
                new[] { "Чертёж: +3 умственных очка", "+6", "+9" }),
            new(SkillId.Technician, "Техник", Aspect.Mental, "Электрика и механика: нужен для сложных рецептов и починки электроинструмента.",
                new[] { "Фонарик, горелка, починка электроинструмента", "Перфоратор", "Мощный перфоратор (позже — генераторы, щитки)" },
                SkillId.Craftsman, 1, requireFrom: 2),

            new(SkillId.Carpenter, "Плотник", Aspect.Construction, "Гвозди и разбор.",
                new[] { "25% шанс сберечь гвоздь, гвозди вытаскиваются быстрее", "50%", "75%" }),
            new(SkillId.Wrecker, "Разрушитель", Aspect.Construction, "Сильнее ломаете блоки и мебель.",
                new[] { "+15% урона инструментом", "+30%", "+45%" }),
            new(SkillId.Salvager, "Аккуратный разбор", Aspect.Construction, "Больше материалов при разборе (F), разбираете быстрее.",
                new[] { "+25% материалов, разбор быстрее на 15%", "+50%, на 30%", "+75%, на 45%" }),
            new(SkillId.Foreman, "Прораб", Aspect.Construction, "Постройки и блоки.",
                new[] { "Постройки крафтятся быстрее на 25%, +1 блок к рецептам блоков", "на 50%, +2 блока", "на 75%, +3 блока" },
                SkillId.Carpenter, 1),

            new(SkillId.FieldSurgery, "Полевая хирургия", Aspect.Survival, "Лечение действует лучше.",
                new[] { "Лекарства +5 здоровья, шина срастается за 36 ч; рецепт обезболивающего", "+10, за 24 ч", "+15, за 12 ч" }),
            new(SkillId.Cook, "Повар", Aspect.Survival, "Готовая еда сытнее.",
                new[] { "Блюда +15% калорий; сложные блюда", "+30%", "+45%" }),
            new(SkillId.Hardening, "Закалка", Aspect.Survival, "Холод и жара переносятся легче.",
                new[] { "Комфорт шире на 2 °C в обе стороны", "на 4 °C", "на 6 °C" }),
            new(SkillId.SteadyNerves, "Крепкие нервы", Aspect.Survival, "Рассудок падает медленнее.",
                new[] { "−15% потери рассудка", "−30%", "−45%" }),
            new(SkillId.Tailor, "Портной", Aspect.Survival, "Шитьё: быстрее и сложнее.",
                new[] { "Шитьё быстрее на 20%; утеплённый жилет, туристический рюкзак", "на 40%", "на 60%" }),
        };

        private static Dictionary<SkillId, SkillInfo> _byId;

        public static SkillInfo Get(SkillId id)
        {
            if (_byId == null)
            {
                _byId = new Dictionary<SkillId, SkillInfo>();
                foreach (var info in All)
                    _byId[info.Id] = info;
            }
            return _byId[id];
        }

        public static int Count => All.Length;
    }
}
