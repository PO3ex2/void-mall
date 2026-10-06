namespace VoidMall.Survival
{
    /// <summary>
    /// Состояния игрока (GDD §4.1). Хранимые (травмы, болезни, эффекты лекарств) живут в списке VitalsModel;
    /// вычисляемые (голод, холод, усталость...) выводятся из показателей и нужны для интерфейса.
    /// </summary>
    public enum StatusKind : byte
    {
        // Хранимые.
        BleedingLight,
        BleedingHeavy,
        FractureLeg,
        FractureArm,
        Pain,
        Infection,
        Poisoning,
        Painkiller,
        Unrested,
        NearDeathTrauma,

        /// <summary>«Отпечаток» аномалии «Смотри на меня»: максимум рассудка — половина. Снимается сном.</summary>
        Imprint,

        // Вычисляемые.
        Hungry,
        Starving,
        Thirsty,
        Dehydrated,
        Tired,
        Exhausted,
        Cold,
        Hypothermia,
        Hot,
        Heatstroke,
        PoorDiet,
        WellFed,
        Anxious,
        Panic,
    }

    public readonly struct StatusInfo
    {
        public readonly string Name;
        public readonly string Description;
        public readonly string Treatment;
        public readonly bool Positive;

        public StatusInfo(string name, string description, string treatment, bool positive = false)
        {
            Name = name;
            Description = description;
            Treatment = treatment;
            Positive = positive;
        }
    }

    /// <summary>Названия и описания состояний для окна «Состояние» и HUD.</summary>
    public static class StatusCatalog
    {
        public static StatusInfo Get(StatusKind kind) => kind switch
        {
            StatusKind.BleedingLight => new("Кровотечение", "Медленно теряете здоровье. Иногда останавливается само; открытая рана может загноиться.", "Бинт"),
            StatusKind.BleedingHeavy => new("Сильное кровотечение", "Быстро теряете здоровье.", "Бинт (станет лёгким), затем ещё один"),
            StatusKind.FractureLeg => new("Перелом ноги", "Хромота: нет бега и прыжков. Без шины не срастается.", "Шина, затем время"),
            StatusKind.FractureArm => new("Перелом руки", "Руки дрожат, удары медленнее. Без шины не срастается.", "Шина, затем время"),
            StatusKind.Pain => new("Боль", "Прицел качается, стамина восстанавливается медленнее. Проходит со временем.", "Обезболивающее"),
            StatusKind.Infection => new("Инфекция", "Из-за необработанной раны. Слабость, со временем — потеря здоровья.", "Антисептик"),
            StatusKind.Poisoning => new("Отравление", "Испорченная еда: потеря здоровья и воды. Проходит само за полтора игровых часа.", "Пить воду: каждые 500 мл сокращают отравление на час"),
            StatusKind.Painkiller => new("Обезболивающее", "Боль почти не мешает.", "", positive: true),
            StatusKind.Unrested => new("Не выспался", "Спали на полу: стамина −15%, рассудок и бодрость падают быстрее.", "Выспаться на кровати, диване или спальнике (или подождать 6 ч)"),
            StatusKind.NearDeathTrauma => new("Травма после смерти", "Максимум здоровья и стамины снижен. Складывается при повторных смертях.", "Пройдёт со временем"),
            StatusKind.Imprint => new("Отпечаток", "Вы слишком долго смотрели на фиолетовую вспышку. Рассудок не поднимается выше половины.", "Выспаться"),
            StatusKind.Hungry => new("Голод", "Стамина ниже обычного.", "Поесть"),
            StatusKind.Starving => new("Истощение", "Нет сил бегать, здоровье тает.", "Срочно поесть"),
            StatusKind.Thirsty => new("Жажда", "Стамина восстанавливается медленнее.", "Попить"),
            StatusKind.Dehydrated => new("Обезвоживание", "Здоровье быстро тает.", "Срочно попить"),
            StatusKind.Tired => new("Усталость", "Чуть медленнее, рассудок падает быстрее.", "Поспать (кровать, спальник)"),
            StatusKind.Exhausted => new("Изнеможение", "Нет бега, рассудок падает вдвое быстрее.", "Поспать"),
            StatusKind.Cold => new("Холодно", "Стамина восстанавливается медленнее, еда тратится быстрее.", "Тёплая одежда, уйти в тепло"),
            StatusKind.Hypothermia => new("Переохлаждение", "Дрожь и потеря здоровья.", "Срочно в тепло, утеплиться"),
            StatusKind.Hot => new("Жарко", "Вода тратится быстрее.", "Уйти в прохладу, снять тёплое"),
            StatusKind.Heatstroke => new("Перегрев", "Потеря здоровья.", "Срочно в прохладу, пить"),
            StatusKind.PoorDiet => new("Однообразная еда", "Стамина восстанавливается медленнее.", "Есть разное: жиры, белки, углеводы"),
            StatusKind.WellFed => new("Сыт и доволен", "Больше стамины, быстрее заживление.", "", positive: true),
            StatusKind.Anxious => new("Тревога", "Мерещится всякое. Падает в темноте, без сна и в аномалиях.", "Свет, сон, еда"),
            StatusKind.Panic => new("Паника", "Руки трясутся, бег срывается.", "Свет и сон"),
            _ => new(kind.ToString(), "", ""),
        };

        public static bool IsStored(StatusKind kind) => kind <= StatusKind.NearDeathTrauma;
    }
}
