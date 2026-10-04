using UnityEngine;
using VoidMall.Core;
using VoidMall.Player;
using VoidMall.Skills;
using VoidMall.World;

namespace VoidMall.Game
{
    /// <summary>
    /// Правила очков навыков за действия, о которых знает только игровой слой (ARCHITECTURE §8.3):
    /// ходьба и спринт, переноска тяжёлого — физические; ломание блоков и пропов рядом с игроком — строительные.
    /// Остальное начисляют сами системы: крафт и чертежи (умственные), стройка и гвозди (строительные),
    /// еда, лечение, сон, холод (выживание).
    /// </summary>
    [RequireComponent(typeof(PlayerSkills))]
    public sealed class SkillRules : MonoBehaviour
    {
        private const float WalkMetersPerPoint = 400f;
        private const float SprintMetersPerPoint = 150f;
        private const float CarryMetersPerPoint = 100f;
        private const float NearbyDistance = 6f;

        private PlayerSkills _skills;
        private PlayerMotor _motor;
        private PropInteractor _props;
        private Vector3 _last;
        private bool _hasLast;

        private void Awake()
        {
            _skills = GetComponent<PlayerSkills>();
            _motor = GetComponent<PlayerMotor>();
            _props = GetComponent<PropInteractor>();
        }

        private void OnEnable()
        {
            EventBus<MaterialMinedEvent>.Subscribe(OnMined);
            EventBus<PropSalvagedEvent>.Subscribe(OnSalvaged);
        }

        private void OnDisable()
        {
            EventBus<MaterialMinedEvent>.Unsubscribe(OnMined);
            EventBus<PropSalvagedEvent>.Unsubscribe(OnSalvaged);
        }

        private void Update()
        {
            if (_motor == null)
                return;
            ref readonly var state = ref _motor.State;
            Vector3 position = state.Position;
            if (!_hasLast || state.Mode != MoveMode.Walk)
            {
                _last = position;
                _hasLast = true;
                return;
            }
            Vector3 delta = position - _last;
            _last = position;
            delta.y = 0f;
            float meters = delta.magnitude;
            // Телепорты и платформы не в счёт.
            if (!state.IsGrounded || meters > 2f || meters <= 0f)
                return;
            _skills.Award(Aspect.Physical, meters / (state.IsSprinting ? SprintMetersPerPoint : WalkMetersPerPoint));
            float carried = _props != null ? _props.CarriedMass : 0f;
            if (carried > 10f)
                _skills.Award(Aspect.Physical, meters / CarryMetersPerPoint * Mathf.Min(3f, carried / 50f));
        }

        private bool Near(Vector3 position) => Vector3.Distance(position, transform.position) <= NearbyDistance;

        private void OnMined(in MaterialMinedEvent evt)
        {
            if (Near(evt.Position))
                _skills.Award(Aspect.Construction, Mathf.Min(1f, evt.Amount) * 0.1f);
        }

        private void OnSalvaged(in PropSalvagedEvent evt)
        {
            // Разбор (F) начисляет PropInteractor, здесь — поломка инструментом.
            if (!evt.Disassembled && Near(evt.Position))
                _skills.Award(Aspect.Construction, 0.3f);
        }
    }
}
