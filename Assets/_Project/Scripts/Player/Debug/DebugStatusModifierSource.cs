using UnityEngine;
using VoidMall.Core;

namespace VoidMall.Player
{
    /// <summary>
    /// Заглушка «вес и травмы» для отладки контроллера (панель F4).
    /// Будет заменена EncumbranceModifier (Items) и InjuryModifier (Survival) — правила те же, из MovementRules.
    /// </summary>
    public sealed class DebugStatusModifierSource : MonoBehaviour, IMovementModifierSource
    {
        [Tooltip("Вес / лимит веса. > 1 — перевес.")]
        [Range(0f, MovementRules.OverloadMax)] public float LoadFraction;
        public bool LeftLegBroken;
        public bool RightLegBroken;
        [Range(0, 2)] public int InjuredArms;
        [Range(0f, 1f)] public float Pain;

        public void ApplyModifiers(ref MovementModifiers modifiers)
        {
            MovementRules.ApplyEncumbrance(ref modifiers, LoadFraction);
            MovementRules.ApplyLegFractures(ref modifiers, (LeftLegBroken ? 1 : 0) + (RightLegBroken ? 1 : 0));
            MovementRules.ApplyArmInjury(ref modifiers, InjuredArms);
            MovementRules.ApplyPain(ref modifiers, Pain);
        }

        public void ResetAll()
        {
            LoadFraction = 0f;
            LeftLegBroken = false;
            RightLegBroken = false;
            InjuredArms = 0;
            Pain = 0f;
        }
    }
}
