using System.Collections.Generic;
using UnityEngine;
using VoidMall.Core;

namespace VoidMall.World
{
    /// <summary>
    /// Физический проп в мире. Пока его не трогают, он «спит»: статичный коллайдер без Rigidbody (тысячи таких почти
    /// ничего не стоят). Просыпается, когда его берут, в него врезается движущееся тело или под ним ломают опору;
    /// успокоившись, засыпает снова (Rigidbody становится кинематическим). Прибитый к миру проп не просыпается.
    /// Пропы, прибитые друг к другу, — одна группа: у корня один Rigidbody, коллайдеры детей входят в него.
    /// </summary>
    public sealed class PropBody : MonoBehaviour
    {
        private const float SleepSpeed = 0.08f;
        private const float SleepDelay = 1f;
        private const float WakeImpactSpeed = 0.6f;

        public ulong Id { get; private set; }
        public ushort DefIndex { get; private set; }
        public PropDef Def { get; private set; }
        public PropManager Manager { get; private set; }
        public BoxCollider Collider { get; private set; }

        /// <summary>Все части коллайдера (у стеллажей — стенки и полки, у остальных — одна коробка).</summary>
        public readonly List<BoxCollider> Colliders = new();
        public Rigidbody Body { get; private set; }

        public PropBody Parent { get; private set; }
        public readonly List<PropBody> Children = new();

        /// <summary>Клетки мира, к которым прибит корень группы. Пусто — не прибит.</summary>
        public readonly List<Unity.Mathematics.int3> Anchors = new();

        public bool IsAwake { get; private set; }
        public bool IsHeld { get; set; }

        public PropBody Root => Parent != null ? Parent.Root : this;
        public bool IsNailed => Root.Anchors.Count > 0;
        public bool IsInGroup => Parent != null || Children.Count > 0;

        /// <summary>Масса всей группы.</summary>
        public float GroupMass
        {
            get
            {
                float mass = Def.Mass;
                foreach (var child in Children)
                    mass += child.GroupMass;
                return mass;
            }
        }

        private float _stillTime;

        public void Initialize(PropManager manager, ulong id, ushort defIndex, PropDef def)
        {
            Manager = manager;
            Id = id;
            DefIndex = defIndex;
            Def = def;
            Collider = GetComponent<BoxCollider>();
            Parent = null;
            Children.Clear();
            Anchors.Clear();
            IsAwake = false;
            IsHeld = false;
            _stillTime = 0f;
            // Спящий проп выключен: Unity не тратит время на вызов FixedUpdate у тысяч неподвижных пропов.
            // События столкновений приходят и выключенным компонентам — пробуждение от удара работает.
            enabled = false;
            Body = GetComponent<Rigidbody>();
        }

        // ───────────────────────────── Сон и пробуждение ─────────────────────────────

        /// <summary>Разбудить группу (кроме прибитой к миру): она становится настоящим физическим телом.</summary>
        public void Wake()
        {
            var root = Root;
            if (root != this)
            {
                root.Wake();
                return;
            }
            if (IsNailed || IsAwake)
                return;
            if (Body == null)
            {
                Body = gameObject.AddComponent<Rigidbody>();
                Body.interpolation = RigidbodyInterpolation.Interpolate;
                Body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            }
            Body.mass = GroupMass;
            Body.isKinematic = false;
            Body.ResetCenterOfMass();
            Body.ResetInertiaTensor();
            Body.WakeUp();
            IsAwake = true;
            enabled = true;
            _stillTime = 0f;
            Manager.OnWoken(this);
        }

        private void Sleep()
        {
            if (Body != null)
            {
                Body.linearVelocity = Vector3.zero;
                Body.angularVelocity = Vector3.zero;
                Body.isKinematic = true;
            }
            IsAwake = false;
            enabled = false;
            Manager.OnSettled(this);
        }

        /// <summary>Заморозить (прибит или сброшен в пул): кинематика без скорости.</summary>
        public void Freeze()
        {
            if (Body != null)
            {
                if (!Body.isKinematic)
                {
                    Body.linearVelocity = Vector3.zero;
                    Body.angularVelocity = Vector3.zero;
                }
                Body.isKinematic = true;
            }
            IsAwake = false;
            enabled = false;
        }

        private static readonly Perf.Probe FixedProbe = Perf.Register("Props.Body.Fixed");

        private void FixedUpdate()
        {
            using var perf = Perf.Measure(FixedProbe);
            if (!IsAwake || Parent != null || Body == null)
                return;
            if (transform.position.y < -40f)
            {
                Manager.Remove(this, PropRemoveReason.Void);
                return;
            }
            if (IsHeld)
            {
                _stillTime = 0f;
                return;
            }
            // Аномалии: в перевёрнутом чанке падает вверх, пульсация таскает туда-сюда (и не даёт уснуть).
            if (AnomalyPhysics.Apply(Body, Time.time, AnomalyPhysics.Any ? GroupBounds().min.y : float.NaN))
            {
                _stillTime = 0f;
                return;
            }
            bool still =Body.linearVelocity.sqrMagnitude < SleepSpeed * SleepSpeed && Body.angularVelocity.sqrMagnitude < SleepSpeed * SleepSpeed;
            _stillTime = still ? _stillTime + Time.fixedDeltaTime : 0f;
            if (_stillTime >= SleepDelay)
                Sleep();
        }

        private void OnCollisionEnter(Collision collision)
        {
            var root = Root;
            // Тяжёлое и быстрое бьёт игрока (только летящий проп, не тот, что игрок несёт).
            if (collision.collider.gameObject.layer == GameLayers.Player && root.IsAwake && !root.IsHeld)
            {
                float speed = collision.relativeVelocity.magnitude, mass = root.GroupMass;
                if (speed > 4f && mass > 3f)
                    EventBus<PlayerImpactEvent>.Publish(new PlayerImpactEvent { Player = collision.collider.gameObject, Damage = Mathf.Min(80f, mass * speed * 0.08f), Reason = $"Ударило: {root.Def.DisplayName}" });
            }
            if (root.IsAwake || root.IsNailed)
                return;
            var other = collision.rigidbody;
            if (other != null && !other.isKinematic && collision.relativeVelocity.magnitude > WakeImpactSpeed)
                root.Wake();
        }

        // ───────────────────────────── Группы ─────────────────────────────

        /// <summary>Прикрепить этот проп (вместе с его детьми) к другому: одно тело, суммарная масса.</summary>
        public void AttachTo(PropBody parent)
        {
            Freeze();
            if (Body != null)
            {
                DestroyImmediate(Body);
                Body = null;
            }
            Parent = parent;
            parent.Children.Add(this);
            transform.SetParent(parent.transform, true);
            var root = parent.Root;
            if (root.Body != null)
            {
                root.Body.mass = root.GroupMass;
                root.Body.ResetCenterOfMass();
                root.Body.ResetInertiaTensor();
            }
        }

        /// <summary>Отделить от родителя: снова самостоятельный проп.</summary>
        public void Detach(Transform newParent)
        {
            if (Parent == null)
                return;
            var root = Root;
            Parent.Children.Remove(this);
            Parent = null;
            transform.SetParent(newParent, true);
            if (root.Body != null)
            {
                root.Body.mass = root.GroupMass;
                root.Body.ResetCenterOfMass();
                root.Body.ResetInertiaTensor();
            }
        }

        public void SetLayer(int layer)
        {
            gameObject.layer = layer;
            foreach (var child in Children)
                child.SetLayer(layer);
        }

        /// <summary>Мировые габариты самого пропа (по всем частям коллайдера).</summary>
        public Bounds Bounds
        {
            get
            {
                var bounds = Collider.bounds;
                foreach (var part in Colliders)
                    if (part.enabled)
                        bounds.Encapsulate(part.bounds);
                return bounds;
            }
        }

        /// <summary>Мировые габариты группы.</summary>
        public Bounds GroupBounds()
        {
            var bounds = Bounds;
            foreach (var child in Children)
                bounds.Encapsulate(child.GroupBounds());
            return bounds;
        }
    }

    public enum PropRemoveReason
    {
        Broken,
        Disassembled,
        Void,
    }

    /// <summary>Проп разобран или сломан. Пока инвентаря нет — пишется в журнал добычи (этап 6 выдаст предметы).</summary>
    public struct PropSalvagedEvent
    {
        public PropDef Def;
        public bool Disassembled;
        public Vector3 Position;
    }
}
