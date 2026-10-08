using System;
using VoidMall.World;

namespace VoidMall.Items
{
    /// <summary>
    /// Порча еды (этап 8Ж). У скоропортящегося стака хранится момент порчи (SpoilAt, игровые часы), а не таймер: ничего не
    /// тикает, испорченность считается при чтении. В холоде (холодильник) оставшийся срок растягивается в ColdSlowdown раз,
    /// при выносе — сжимается обратно. При сложении стаков берётся более ранний срок.
    /// </summary>
    public static class Freshness
    {
        public const float ColdSlowdown = 4f;

        /// <summary>Часы мира. Тесты подменяют.</summary>
        public static Func<double> Clock = WorldHours;

        public static double Now => Clock();

        private static double WorldHours() => WorldClock.Instance != null ? WorldClock.Instance.TotalHours : 0.0;

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Clock = WorldHours;

        /// <summary>Новый стак: срок хранения пошёл.</summary>
        public static void Start(ItemStack stack, float shelfLifeHours)
        {
            stack.ShelfLife = shelfLifeHours;
            stack.SpoilAt = shelfLifeHours > 0f ? Now + shelfLifeHours * (stack.Chilled ? ColdSlowdown : 1f) : 0;
        }

        /// <summary>Положили в холод или вынули: пересчитать оставшийся срок.</summary>
        public static void SetCold(ItemStack stack, bool cold)
        {
            if (stack.Chilled == cold)
                return;
            stack.Chilled = cold;
            if (stack.SpoilAt <= 0)
                return;
            double now = Now;
            double left = stack.SpoilAt - now;
            if (left <= 0)
                return; // уже испорчено — так и останется
            stack.SpoilAt = now + left * (cold ? ColdSlowdown : 1.0 / ColdSlowdown);
        }

        /// <summary>Сложили source в target: у стака — срок того, что испортится раньше.</summary>
        public static void Combine(ItemStack target, ItemStack source)
        {
            if (source.SpoilAt > 0 && (target.SpoilAt <= 0 || source.SpoilAt < target.SpoilAt))
                target.SpoilAt = source.SpoilAt;
        }

        /// <summary>Доля оставшейся свежести 0…1 (1 — не портится или свежее; 0 — испорчено).</summary>
        public static float Fraction(ItemStack stack)
        {
            if (stack.Spoiled)
                return 0f;
            if (stack.ShelfLife <= 0f || stack.SpoilAt <= 0)
                return 1f;
            double total = stack.ShelfLife * (stack.Chilled ? ColdSlowdown : 1f);
            return (float)Math.Clamp((stack.SpoilAt - Now) / total, 0.0, 1.0);
        }

        /// <summary>Сколько игровых часов осталось до порчи (с учётом холода); 0 — испорчено или не портится.</summary>
        public static float HoursLeft(ItemStack stack) =>
            stack.Spoiled || stack.SpoilAt <= 0 ? 0f : (float)Math.Max(0.0, stack.SpoilAt - Now);
    }
}
