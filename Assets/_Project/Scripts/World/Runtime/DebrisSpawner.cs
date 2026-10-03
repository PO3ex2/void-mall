using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using VoidMall.Core;

namespace VoidMall.World
{
    /// <summary>
    /// Превращает обрушившийся остров клеток в падающий физический обломок.
    /// Обломок рассыпается пылью при ударе (см. FallingDebris).
    /// </summary>
    [RequireComponent(typeof(ChunkManager))]
    public sealed class DebrisSpawner : MonoBehaviour
    {
        /// <summary>До этого размера коллизия — точные боксы по клеткам, крупнее — выпуклая оболочка.</summary>
        private const int MaxBoxColliders = 256;
        private const float MassPerCell = 150f;

        [SerializeField] private Material _dustMaterial;

        public Material DustMaterial => _dustMaterial;

        private ChunkManager _world;

        private void Awake() => _world = GetComponent<ChunkManager>();

        public void Spawn(IReadOnlyList<int3> cells, IReadOnlyList<Cell> values)
        {
            if (cells.Count == 0)
                return;

            int3 min = cells[0];
            foreach (var c in cells)
                min = math.min(min, c);

            var occupied = new HashSet<int3>(cells);
            var mesh = BuildMesh(cells, values, occupied, min);

            var go = new GameObject($"Debris ({cells.Count})") { layer = GameLayers.Debris };
            go.transform.position = new Vector3(min.x, min.y, min.z);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _world.BlockMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.On;

            if (cells.Count <= MaxBoxColliders)
            {
                foreach (var c in cells)
                {
                    var box = go.AddComponent<BoxCollider>();
                    box.center = new Vector3(c.x - min.x + 0.5f, c.y - min.y + 0.5f, c.z - min.z + 0.5f);
                    box.size = Vector3.one * 0.96f; // меньше клетки — не цепляем соседние блоки при старте
                }
            }
            else
            {
                var hull = go.AddComponent<MeshCollider>();
                hull.sharedMesh = mesh;
                hull.convex = true;
            }

            var body = go.AddComponent<Rigidbody>();
            body.mass = math.min(cells.Count * MassPerCell, 50000f);
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            var debris = go.AddComponent<FallingDebris>();
            debris.Initialize(_dustMaterial, CountMaterials(values), cells.Count);
        }

        private static Dictionary<ushort, float> CountMaterials(IReadOnlyList<Cell> values)
        {
            var counts = new Dictionary<ushort, float>();
            foreach (var cell in values)
            {
                counts.TryGetValue(cell.Material, out float count);
                counts[cell.Material] = count + 1f;
            }
            return counts;
        }

        private Mesh BuildMesh(IReadOnlyList<int3> cells, IReadOnlyList<Cell> values, HashSet<int3> occupied, int3 origin)
        {
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector4>();
            var indices = new List<int>();

            for (int i = 0; i < cells.Count; i++)
            {
                int3 cell = cells[i];
                float layer = _world.GetInfo(values[i].Material).TextureLayer;
                float3 local = cell - origin;
                for (int face = 0; face < 6; face++)
                {
                    int3 normal = FaceNormal(face);
                    if (occupied.Contains(cell + normal))
                        continue;
                    int start = vertices.Count;
                    for (int k = 0; k < 4; k++)
                    {
                        float3 corner = local + FaceCorner(face, k);
                        float3 world = cell + FaceCorner(face, k);
                        int axis = normal.x != 0 ? 0 : normal.y != 0 ? 1 : 2;
                        float2 uv = axis == 0 ? world.zy : axis == 1 ? world.xz : world.xy;
                        vertices.Add(corner);
                        normals.Add(new Vector3(normal.x, normal.y, normal.z));
                        uvs.Add(new Vector4(uv.x, uv.y, layer, 1f));
                    }
                    indices.Add(start);
                    indices.Add(start + 1);
                    indices.Add(start + 2);
                    indices.Add(start);
                    indices.Add(start + 2);
                    indices.Add(start + 3);
                }
            }

            var mesh = new Mesh { name = "Debris", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(indices, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        // Те же грани и порядок углов, что в ChunkMeshJob (треугольники смотрят наружу).
        private static int3 FaceNormal(int face) => face switch
        {
            0 => new int3(1, 0, 0),
            1 => new int3(-1, 0, 0),
            2 => new int3(0, 1, 0),
            3 => new int3(0, -1, 0),
            4 => new int3(0, 0, 1),
            _ => new int3(0, 0, -1),
        };

        private static readonly int[] Corners =
        {
            1, 0, 0, 1, 1, 0, 1, 1, 1, 1, 0, 1,
            0, 0, 1, 0, 1, 1, 0, 1, 0, 0, 0, 0,
            0, 1, 0, 0, 1, 1, 1, 1, 1, 1, 1, 0,
            0, 0, 0, 1, 0, 0, 1, 0, 1, 0, 0, 1,
            1, 0, 1, 1, 1, 1, 0, 1, 1, 0, 0, 1,
            0, 0, 0, 0, 1, 0, 1, 1, 0, 1, 0, 0,
        };

        private static float3 FaceCorner(int face, int corner)
        {
            int o = face * 12 + corner * 3;
            return new float3(Corners[o], Corners[o + 1], Corners[o + 2]);
        }
    }
}
