using System.Collections.Generic;
using UnityEngine;

namespace VoidMall.Player
{
    /// <summary>
    /// Геометрия кинематической капсулы: collide-and-slide на CapsuleCast, поиск земли,
    /// выталкивание из геометрии, проверка места для вставания. Состояния не хранит.
    /// Позиция везде — низ капсулы (ступни). Между капсулой и миром держится зазор SkinWidth.
    /// </summary>
    public sealed class KinematicMover
    {
        public struct Hit
        {
            public Collider Collider;
            public Vector3 Point;
            public Vector3 Normal;
            public Vector3 VelocityBefore;
        }

        public struct GroundInfo
        {
            public Collider Collider;
            public Vector3 Point;
            public Vector3 Normal;

            /// <summary>Расстояние до земли сверх зазора SkinWidth.</summary>
            public float Distance;

            public bool IsWalkable;
        }

        private const int MaxBumps = 4;
        private const int MaxPlanes = 5;
        private const float MinMove = 1e-5f;

        private readonly MovementSettings _settings;
        private readonly Vector3[] _planes = new Vector3[MaxPlanes];
        private readonly Collider[] _overlaps = new Collider[16];

        public int CollisionMask { get; set; }

        /// <summary>Столкновения за текущий тик (для толкания пропов). Очищает владелец.</summary>
        public List<Hit> Hits { get; } = new(16);

        public KinematicMover(MovementSettings settings, int collisionMask)
        {
            _settings = settings;
            CollisionMask = collisionMask;
        }

        private float Radius => _settings.Radius;
        private float Skin => _settings.SkinWidth;

        /// <summary>
        /// Допуск нужен, чтобы склон ровно MaxSlopeAngle (45°) стабильно считался проходимым —
        /// без него нормаль из физики колеблется вокруг границы и игрок «мигает» между землёй и воздухом.
        /// </summary>
        private const float WalkableTolerance = 1e-3f;

        public bool IsWalkable(Vector3 normal) => normal.y >= _settings.MinWalkableNormalY - WalkableTolerance;

        public void GetCapsule(Vector3 feet, float height, out Vector3 bottom, out Vector3 top)
        {
            bottom = feet + Vector3.up * Radius;
            top = feet + Vector3.up * Mathf.Max(height - Radius, Radius);
        }

        public bool Cast(Vector3 feet, float height, Vector3 direction, float distance, out RaycastHit hit)
        {
            GetCapsule(feet, height, out var bottom, out var top);
            return Physics.CapsuleCast(bottom, top, Radius, direction, out hit, distance, CollisionMask, QueryTriggerInteraction.Ignore);
        }

        /// <summary>
        /// Collide-and-slide: двигаемся на delta, скользя вдоль препятствий (как TryPlayerMove в Source).
        /// Скорость обрезается о стены. keepHorizontal — для движения по земле: скорость остаётся горизонтальной
        /// и не обрезается о проходимые склоны.
        /// </summary>
        /// <returns>true, если упёрлись в непроходимую поверхность (стену).</returns>
        public bool SlideMove(ref Vector3 position, ref Vector3 velocity, Vector3 delta, float height, bool keepHorizontal)
        {
            bool hitWall = false;
            int numPlanes = 0;
            Vector3 originalDelta = delta;

            for (int bump = 0; bump < MaxBumps; bump++)
            {
                float distance = delta.magnitude;
                if (distance < MinMove)
                    break;

                Vector3 direction = delta / distance;
                if (!Cast(position, height, direction, distance + Skin, out var hit))
                {
                    position += delta;
                    break;
                }

                float travel = Mathf.Max(0f, hit.distance - Skin);
                position += direction * travel;

                Vector3 normal = hit.normal;
                bool walkable = IsWalkable(normal);
                if (walkable && keepHorizontal && IsStepEdge(hit.point, direction))
                {
                    // Нижняя сфера упёрлась в ребро ступеньки: нормаль «скруглённая» и выглядит как склон.
                    // Считаем это стеной — подъём сделает step-up дискретно, как в Source, а не «въездом на ребро».
                    walkable = false;
                    normal = new Vector3(normal.x, 0f, normal.z);
                    normal = normal.sqrMagnitude > 1e-6f ? normal.normalized : -new Vector3(direction.x, 0f, direction.z).normalized;
                }
                if (!walkable)
                    hitWall = true;

                Hits.Add(new Hit
                {
                    Collider = hit.collider,
                    Point = hit.point,
                    Normal = normal,
                    VelocityBefore = velocity,
                });

                if (numPlanes >= MaxPlanes)
                {
                    velocity = Vector3.zero;
                    break;
                }
                _planes[numPlanes++] = normal;

                Vector3 remaining = direction * (distance - travel);
                Vector3 crease = numPlanes >= 2 ? Vector3.Cross(_planes[numPlanes - 2], normal) : Vector3.zero;

                if (crease.sqrMagnitude < 1e-6f)
                {
                    // Одна плоскость: скользим вдоль неё.
                    remaining = Vector3.ProjectOnPlane(remaining, normal);
                    if (!keepHorizontal || !walkable)
                        velocity = SourceMovement.ClipVelocity(velocity, normal);
                }
                else
                {
                    // Две плоскости (угол, «складка»): двигаемся только вдоль линии их пересечения.
                    crease.Normalize();
                    remaining = crease * Vector3.Dot(remaining, crease);
                    velocity = crease * Vector3.Dot(velocity, crease);
                }

                if (keepHorizontal)
                    velocity.y = 0f;

                // Не даём оттолкнуться назад — так в углах не возникает дрожания.
                if (Vector3.Dot(remaining, originalDelta) <= 0f)
                    break;

                delta = remaining;
            }

            return hitWall;
        }

        /// <summary>
        /// Точка касания — ребро ступеньки (под ней вертикальная грань), а не поверхность склона?
        /// Луч пускаем горизонтально по ходу движения чуть ниже и позади точки касания.
        /// </summary>
        private bool IsStepEdge(Vector3 point, Vector3 moveDirection)
        {
            Vector3 flat = new(moveDirection.x, 0f, moveDirection.z);
            if (flat.sqrMagnitude < 1e-6f)
                return false;
            flat.Normalize();
            Vector3 origin = point - flat * 0.05f + Vector3.down * 0.02f;
            return Physics.Raycast(origin, flat, out var ray, 0.1f, CollisionMask, QueryTriggerInteraction.Ignore)
                   && !IsWalkable(ray.normal);
        }

        /// <summary>Ищет землю под ногами в пределах distance (сверх зазора).</summary>
        public bool ProbeGround(Vector3 feet, float height, float distance, out GroundInfo ground)
        {
            ground = default;
            // Кастуем с небольшого подъёма, чтобы поймать землю, даже если стоим вплотную к ней.
            Vector3 origin = feet + Vector3.up * Skin;
            if (!Cast(origin, height, Vector3.down, distance + Skin * 2f, out var hit))
                return false;

            ground.Collider = hit.collider;
            ground.Point = hit.point;
            ground.Normal = hit.normal;
            ground.Distance = hit.distance - Skin * 2f;
            ground.IsWalkable = IsWalkable(hit.normal);

            if (!ground.IsWalkable)
            {
                // Нижняя сфера капсулы касается ребра ступеньки — нормаль каста «скруглённая».
                // Уточняем нормаль лучом в настоящую поверхность чуть дальше от оси капсулы.
                Vector3 outward = hit.point - feet;
                outward.y = 0f;
                Vector3 nudge = outward.sqrMagnitude > 1e-6f ? outward.normalized * 0.01f : Vector3.zero;
                if (Physics.Raycast(hit.point + nudge + Vector3.up * 0.05f, Vector3.down, out var ray, 0.1f, CollisionMask, QueryTriggerInteraction.Ignore)
                    && IsWalkable(ray.normal))
                {
                    ground.Normal = ray.normal;
                    ground.IsWalkable = true;
                }
            }
            return true;
        }

        /// <summary>Есть ли место для капсулы указанной высоты.</summary>
        public bool CanFit(Vector3 feet, float height)
        {
            GetCapsule(feet + Vector3.up * Skin, height - Skin, out var bottom, out var top);
            return !Physics.CheckCapsule(bottom, top, Radius - Skin * 0.5f, CollisionMask, QueryTriggerInteraction.Ignore);
        }

        /// <summary>Выталкивает капсулу из пересечений (после телепорта, ремеша чанка, въехавшей платформы).</summary>
        public bool Depenetrate(ref Vector3 feet, float height, CapsuleCollider self)
        {
            bool moved = false;
            for (int iteration = 0; iteration < 4; iteration++)
            {
                GetCapsule(feet, height, out var bottom, out var top);
                int count = Physics.OverlapCapsuleNonAlloc(bottom, top, Radius, _overlaps, CollisionMask, QueryTriggerInteraction.Ignore);
                bool any = false;
                for (int i = 0; i < count; i++)
                {
                    var other = _overlaps[i];
                    if (other == self)
                        continue;
                    var otherTransform = other.transform;
                    if (Physics.ComputePenetration(self, feet, Quaternion.identity,
                            other, otherTransform.position, otherTransform.rotation,
                            out var direction, out float distance))
                    {
                        feet += direction * (distance + Skin * 0.5f);
                        any = true;
                        moved = true;
                    }
                }
                if (!any)
                    break;
            }
            return moved;
        }
    }
}
