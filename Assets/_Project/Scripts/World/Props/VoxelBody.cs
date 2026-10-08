using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace VoidMall.World
{
    /// <summary>Числа воксельных тел (этап 8Е): откол при ударе, урон игроку, плавучесть.</summary>
    public static class VoxelBodyRules
    {
        /// <summary>Скорость удара (по нормали), с которой начинают откалываться клетки, м/с: падение с высоты ~2.5 м.</summary>
        public const float ChipSpeed = 7f;

        /// <summary>Сколько джоулей на единицу прочности клетки нужно, чтобы отколоть одну клетку.</summary>
        public const float ChipEnergyPerHardness = 5f;

        public const int MaxChipCells = 6;

        /// <summary>Легче этого (кг на клетку в среднем) кусок всплывает: дерево плавает, бетон тонет.</summary>
        public const float FloatCellMass = 12f;

        /// <summary>Урон «раздавило»: насмерть.</summary>
        public const float CrushDamage = 1000f;

        /// <summary>Сколько клеток отколется: по энергии удара и прочности материала в точке удара. Слабый удар — ни одной.</summary>
        public static int ChipCount(float mass, float speed, int hardness)
        {
            if (speed < ChipSpeed || hardness <= 0)
                return 0;
            float energy = 0.5f * mass * speed * speed;
            return math.clamp((int)(energy / (hardness * ChipEnergyPerHardness)), 0, MaxChipCells);
        }

        /// <summary>
        /// Урон игроку от удара: как у пропов (масса × скорость, не больше 80). Тяжёлый кусок (crush) давит насмерть —
        /// правило прежних обломков.
        /// </summary>
        public static float ImpactDamage(float mass, float speed, bool crush)
        {
            if (crush && mass > 600f && speed > 3f)
                return CrushDamage;
            return speed > 4f && mass > 3f ? Mathf.Min(80f, mass * speed * 0.08f) : 0f;
        }

        public static bool Floats(float mass, int cells) => cells > 0 && mass / cells <= FloatCellMass;
    }

    /// <summary>
    /// Отломанный кусок — воксельное тело (этап 8Е). Живёт на объекте пропа (PropBody): переноска, гвозди, сон, стриминг —
    /// как у пропов. Здесь — сетка клеток, меш тем же мешером, что и чанки, коллайдер из коробок, масса по материалам и
    /// откол клеток при сильном ударе.
    /// </summary>
    [RequireComponent(typeof(PropBody))]
    public sealed class VoxelBody : MonoBehaviour
    {
        /// <summary>Коробки коллайдера чуть меньше клеток: не цепляем соседние блоки мира в момент отрыва.</summary>
        private const float BoxInset = 0.02f;

        /// <summary>Не чаще одного откола за это время: тело, упавшее углом, не крошится каскадом.</summary>
        private const float ChipCooldown = 0.3f;

        private static readonly List<(int3 min, int3 size)> Boxes = new();
        private static readonly List<BoxCollider> ColliderScratch = new();

        public VoxelGrid Grid { get; private set; }
        public float Mass { get; private set; }

        /// <summary>Центр масс в координатах тела.</summary>
        public Vector3 CenterOfMass { get; private set; }

        public bool Floats { get; private set; }
        public string DisplayName { get; private set; }

        private PropBody _prop;
        private Mesh _mesh;
        private float _nextChip;

        public void Setup(VoxelGrid grid, PropBody prop)
        {
            Grid = grid;
            _prop = prop;
            if (_mesh == null)
            {
                _mesh = new Mesh { name = "VoxelBody" };
                GetComponent<MeshFilter>().sharedMesh = _mesh;
            }
        }

        /// <summary>Перестроить меш, коллайдер, массу и имя по сетке (после отрыва, поломки или постройки).</summary>
        public void Rebuild(ChunkManager world)
        {
            world.BuildDetachedMesh(Grid, _mesh);
            var renderer = GetComponent<MeshRenderer>();
            renderer.sharedMaterials = new[] { world.BlockMaterial, world.TransparentBlockMaterial };
            renderer.shadowCastingMode = ShadowCastingMode.On;

            Grid.BuildBoxes(Boxes);
            GetComponents(ColliderScratch);
            while (ColliderScratch.Count < Boxes.Count)
                ColliderScratch.Add(gameObject.AddComponent<BoxCollider>());
            _prop.Colliders.Clear();
            for (int i = 0; i < ColliderScratch.Count; i++)
            {
                var box = ColliderScratch[i];
                if (i >= Boxes.Count)
                {
                    box.enabled = false;
                    Destroy(box);
                    continue;
                }
                box.enabled = PropManager.Visible.Value;
                var (min, size) = Boxes[i];
                box.center = (Vector3)(float3)min + (Vector3)(float3)size * 0.5f;
                box.size = (Vector3)(float3)size - Vector3.one * (BoxInset * 2f);
                _prop.Colliders.Add(box);
            }

            Mass = Grid.Mass(cell => CellMass(world, cell), out float3 center);
            CenterOfMass = center;
            Floats = VoxelBodyRules.Floats(Mass, Grid.Count);
            var material = world.Materials.Get(Grid.DominantMaterial());
            DisplayName = (Grid.Count == 1 ? "Блок: " : "Кусок: ") + material.DisplayName;
            _prop.RecalculateMass();
        }

        public static float CellMass(ChunkManager world, Cell cell) =>
            VoxelGrid.CellMass(cell, world.GetInfo(cell.Material).Kind, world.Materials.Get(cell.Material).CellMass);

        /// <summary>Клетка сетки под точкой попадания луча (точка чуть внутрь от грани) и нормаль грани в координатах тела.</summary>
        public bool TryGetCell(Vector3 worldPoint, Vector3 worldNormal, out int3 cell, out int3 normal)
        {
            Vector3 local = transform.InverseTransformPoint(worldPoint - worldNormal * 0.05f);
            cell = (int3)math.floor((float3)local);
            Vector3 n = transform.InverseTransformDirection(worldNormal);
            Vector3 a = new(Mathf.Abs(n.x), Mathf.Abs(n.y), Mathf.Abs(n.z));
            normal = a.x >= a.y && a.x >= a.z ? new int3((int)Mathf.Sign(n.x), 0, 0)
                : a.y >= a.z ? new int3(0, (int)Mathf.Sign(n.y), 0)
                : new int3(0, 0, (int)Mathf.Sign(n.z));
            if (Grid.Contains(cell))
                return true;
            // Луч попал в край коробки соседней клетки (зазор BoxInset) — берём клетку за гранью.
            cell -= normal;
            return Grid.Contains(cell);
        }

        /// <summary>Центр клетки в мире.</summary>
        public Vector3 CellCenter(int3 cell) => transform.TransformPoint((Vector3)(float3)cell + Vector3.one * 0.5f);

        private void OnCollisionEnter(Collision collision)
        {
            var root = _prop != null ? _prop.Root : null;
            if (root == null || !root.IsAwake || root.IsHeld || Grid == null || Grid.Count <= 1 || Time.time < _nextChip)
                return;
            var contact = collision.GetContact(0);
            if (contact.thisCollider == null || contact.thisCollider.gameObject != gameObject)
                return;
            float speed = Mathf.Abs(Vector3.Dot(collision.relativeVelocity, contact.normal));
            if (speed < VoxelBodyRules.ChipSpeed || !TryNearestCell(contact.point, out _, out int hardness))
                return;
            int count = VoxelBodyRules.ChipCount(root.GroupMass, speed, hardness);
            if (count <= 0)
                return;
            _nextChip = Time.time + ChipCooldown;
            _prop.Manager.ChipVoxel(_prop, contact.point, count);
        }

        /// <summary>Ближайшая к точке клетка сетки и прочность её материала.</summary>
        public bool TryNearestCell(Vector3 worldPoint, out int3 nearest, out int hardness)
        {
            nearest = default;
            hardness = 0;
            var world = ChunkManager.Instance;
            if (world == null || Grid.Count == 0)
                return false;
            float3 local = (float3)transform.InverseTransformPoint(worldPoint);
            float best = float.MaxValue;
            foreach (var (cell, value) in Grid.Cells)
            {
                float d = math.distancesq((float3)cell + 0.5f, local);
                if (d < best)
                {
                    best = d;
                    nearest = cell;
                    hardness = world.GetInfo(value.Material).Hardness;
                }
            }
            return true;
        }

        private void OnDestroy()
        {
            if (_mesh != null)
                Destroy(_mesh);
        }
    }
}
