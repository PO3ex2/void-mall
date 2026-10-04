using UnityEngine;
using VoidMall.Console;

namespace VoidMall.Skills
{
    /// <summary>
    /// Навыки игрока (GDD §5.2, ARCHITECTURE §8.3). Очки начисляют системы, которые знают о действии
    /// (крафт, выживание, правила событий в Game): <see cref="Award"/>. Эффекты навыков спрашивают уровень через
    /// <see cref="LevelOf"/> — без игрока (тесты, полигон движения) он 0.
    /// </summary>
    public sealed class PlayerSkills : MonoBehaviour
    {
        public static readonly CVar<float> Rate = new("sv_skill_rate", 1f, "Множитель получения очков навыков.", ConsoleFlags.Cheat);

        public static PlayerSkills Local { get; private set; }

        public SkillTree Tree { get; } = new();

        private void OnEnable() => Local = this;

        private void OnDisable()
        {
            if (Local == this)
                Local = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Local = null;

        public void Award(Aspect aspect, float amount) => Tree.Award(aspect, amount * Mathf.Max(0f, Rate.Value));

        /// <summary>Уровень навыка локального игрока (0 — нет игрока или не изучен).</summary>
        public static int LevelOf(SkillId id) => Local != null ? Local.Tree.Level(id) : 0;

        /// <summary>Начислить очки локальному игроку, если он есть.</summary>
        public static void AwardLocal(Aspect aspect, float amount)
        {
            if (Local != null)
                Local.Award(aspect, amount);
        }
    }
}
