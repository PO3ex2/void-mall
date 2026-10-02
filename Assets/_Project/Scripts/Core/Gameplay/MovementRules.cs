using UnityEngine;

namespace VoidMall.Core
{
    /// <summary>
    /// Геймдизайн-правила влияния веса и травм на движение (GAME_DESIGN §4.3).
    /// Общие для debug-заглушек и будущих модулей Items/Survival.
    /// </summary>
    public static class MovementRules
    {
        public const float EncumbranceStart = 0.7f;
        public const float OverloadMax = 1.5f;

        /// <summary>Порог урона от падения, м/с вертикальной скорости.</summary>
        public const float FallDamageSpeed = 9f;

        /// <summary>Порог перелома от падения, м/с.</summary>
        public const float FallFractureSpeed = 13f;

        /// <param name="loadFraction">Текущий вес / лимит веса.</param>
        public static void ApplyEncumbrance(ref MovementModifiers m, float loadFraction)
        {
            if (loadFraction <= EncumbranceStart)
                return;

            if (loadFraction <= 1f)
            {
                float t = Mathf.InverseLerp(EncumbranceStart, 1f, loadFraction);
                m.SpeedMul *= Mathf.Lerp(1f, 0.75f, t);
                m.AccelMul *= Mathf.Lerp(1f, 0.8f, t);
                m.StaminaCostMul *= Mathf.Lerp(1f, 1.5f, t);
                return;
            }

            // Перевес: почти не можем двигаться и даже развернуться.
            float over = Mathf.InverseLerp(1f, OverloadMax, loadFraction);
            m.SpeedMul *= Mathf.Lerp(0.35f, 0.15f, over);
            m.AccelMul *= 0.5f;
            m.StaminaCostMul *= 2f;
            m.CanSprint = false;
            m.CanJump = false;
            m.MaxTurnRateDeg = Mathf.Min(m.MaxTurnRateDeg, Mathf.Lerp(120f, 40f, over));
        }

        public static void ApplyLegFractures(ref MovementModifiers m, int brokenLegs)
        {
            if (brokenLegs <= 0)
                return;

            m.CanSprint = false;
            m.CanJump = false;
            if (brokenLegs == 1)
            {
                m.SpeedMul *= 0.6f;
                m.LimpAmount = Mathf.Max(m.LimpAmount, 1f);
            }
            else
            {
                m.SpeedMul *= 0.5f;
                m.CanStand = false;
                m.LimpAmount = Mathf.Max(m.LimpAmount, 0.5f);
            }
        }

        public static void ApplyArmInjury(ref MovementModifiers m, int injuredArms)
        {
            m.SwayAmount += 0.35f * Mathf.Clamp(injuredArms, 0, 2);
        }

        public static void ApplyPain(ref MovementModifiers m, float pain01)
        {
            m.SwayAmount += Mathf.Clamp01(pain01);
        }

        public enum FallSeverity { None, Damage, Fracture }

        public static FallSeverity ClassifyFall(float impactSpeed) =>
            impactSpeed >= FallFractureSpeed ? FallSeverity.Fracture :
            impactSpeed >= FallDamageSpeed ? FallSeverity.Damage :
            FallSeverity.None;
    }
}
