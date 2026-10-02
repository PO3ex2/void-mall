using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine.Rendering;

namespace VoidMall.World
{
    /// <summary>Вершина чанка: позиция, нормаль, uv = (u, v, слой массива текстур, AO).</summary>
    public struct ChunkVertex
    {
        public float3 Position;
        public float3 Normal;
        public float4 Uv;

        public static readonly VertexAttributeDescriptor[] Layout =
        {
            new(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3),
            new(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 4),
        };
    }

    /// <summary>
    /// Мешинг блоков чанка: видимые грани (сосед — воздух или прозрачный), вершинный AO,
    /// мировые UV (1 текстура на блок). Соседние чанки читаются через 27 видов (3×3×3), чтобы грани и AO
    /// на границах чанков были бесшовными.
    /// </summary>
    [BurstCompile]
    public struct ChunkMeshJob : IJob
    {
        private const int Size = WorldConstants.ChunkSize;
        private const int CenterView = 13;

        [ReadOnly] public NativeArray<MaterialInfo> Materials;

        /// <summary>27 соседей: индекс (dx+1) + (dy+1)*3 + (dz+1)*9.</summary>
        [ReadOnly] public NativeArray<ChunkView> Views;

        /// <summary>Мировая координата клетки (0,0,0) чанка — для UV.</summary>
        public int3 ChunkOrigin;

        public NativeList<ChunkVertex> Vertices;
        public NativeList<int> OpaqueIndices;
        public NativeList<int> TransparentIndices;

        // Грани: +X, −X, +Y, −Y, +Z, −Z. Углы перечислены так, что треугольники (0,1,2),(0,2,3) смотрят наружу.
        private static readonly int[] FaceNormals =
        {
            1, 0, 0, -1, 0, 0, 0, 1, 0, 0, -1, 0, 0, 0, 1, 0, 0, -1,
        };

        private static readonly int[] FaceCorners =
        {
            1, 0, 0, 1, 1, 0, 1, 1, 1, 1, 0, 1, // +X
            0, 0, 1, 0, 1, 1, 0, 1, 0, 0, 0, 0, // −X
            0, 1, 0, 0, 1, 1, 1, 1, 1, 1, 1, 0, // +Y
            0, 0, 0, 1, 0, 0, 1, 0, 1, 0, 0, 1, // −Y
            1, 0, 1, 1, 1, 1, 0, 1, 1, 0, 0, 1, // +Z
            0, 0, 0, 0, 1, 0, 1, 1, 0, 1, 0, 0, // −Z
        };

        public void Execute()
        {
            Vertices.Clear();
            OpaqueIndices.Clear();
            TransparentIndices.Clear();

            ChunkView center = Views[CenterView];
            if (center.Uniform != 0 && center.Get(0).Material == 0)
                return;

            for (int y = 0; y < Size; y++)
            for (int z = 0; z < Size; z++)
            for (int x = 0; x < Size; x++)
            {
                Cell cell = center.Get(WorldConstants.CellIndex(x, y, z));
                if (cell.Material == 0)
                    continue;
                MaterialInfo info = Materials[cell.Material];
                if (info.Kind != MaterialKind.Block)
                    continue;

                for (int face = 0; face < 6; face++)
                {
                    int3 normal = new(FaceNormals[face * 3], FaceNormals[face * 3 + 1], FaceNormals[face * 3 + 2]);
                    int3 front = new int3(x, y, z) + normal;
                    Cell neighbor = Sample(front);
                    if (!IsFaceVisible(cell, info, neighbor))
                        continue;
                    EmitFace(new int3(x, y, z), front, face, normal, info);
                }
            }
        }

        private bool IsFaceVisible(Cell cell, MaterialInfo info, Cell neighbor)
        {
            if (neighbor.Material == 0)
                return true;
            MaterialInfo neighborInfo = Materials[neighbor.Material];
            if (neighborInfo.IsOpaque)
                return false;
            // Стекло к стеклу того же материала — внутренние грани не рисуем.
            if (info.IsTransparent && neighbor.Material == cell.Material)
                return false;
            return true;
        }

        private void EmitFace(int3 position, int3 front, int face, int3 normal, MaterialInfo info)
        {
            int axis = normal.x != 0 ? 0 : normal.y != 0 ? 1 : 2;
            int axisB = axis == 0 ? 1 : 0;
            int axisC = axis == 2 ? 1 : 2;

            int baseVertex = Vertices.Length;
            int ao0 = 0, ao1 = 0, ao2 = 0, ao3 = 0;

            for (int i = 0; i < 4; i++)
            {
                int offset = face * 12 + i * 3;
                int3 corner = new(FaceCorners[offset], FaceCorners[offset + 1], FaceCorners[offset + 2]);

                // AO: три клетки перед гранью, прилегающие к углу.
                int3 stepB = Axis(axisB) * (corner[axisB] == 1 ? 1 : -1);
                int3 stepC = Axis(axisC) * (corner[axisC] == 1 ? 1 : -1);
                int side1 = IsOccluder(front + stepB) ? 1 : 0;
                int side2 = IsOccluder(front + stepC) ? 1 : 0;
                int diagonal = IsOccluder(front + stepB + stepC) ? 1 : 0;
                int ao = side1 == 1 && side2 == 1 ? 0 : 3 - (side1 + side2 + diagonal);

                if (i == 0) ao0 = ao;
                else if (i == 1) ao1 = ao;
                else if (i == 2) ao2 = ao;
                else ao3 = ao;

                float3 local = position + corner;
                float3 world = ChunkOrigin + local;
                float2 uv = axis == 0 ? world.zy : axis == 1 ? world.xz : world.xy;

                Vertices.Add(new ChunkVertex
                {
                    Position = local,
                    Normal = normal,
                    Uv = new float4(uv, info.TextureLayer, AoToOcclusion(ao)),
                });
            }

            var indices = info.IsTransparent ? TransparentIndices : OpaqueIndices;
            // Диагональ выбираем по AO, иначе на углах появляется анизотропный «шов».
            if (ao0 + ao2 >= ao1 + ao3)
            {
                indices.Add(baseVertex);
                indices.Add(baseVertex + 1);
                indices.Add(baseVertex + 2);
                indices.Add(baseVertex);
                indices.Add(baseVertex + 2);
                indices.Add(baseVertex + 3);
            }
            else
            {
                indices.Add(baseVertex + 1);
                indices.Add(baseVertex + 2);
                indices.Add(baseVertex + 3);
                indices.Add(baseVertex + 1);
                indices.Add(baseVertex + 3);
                indices.Add(baseVertex);
            }
        }

        private bool IsOccluder(int3 local)
        {
            Cell cell = Sample(local);
            return cell.Material != 0 && Materials[cell.Material].IsOpaque;
        }

        /// <summary>Клетка по локальным координатам центрального чанка, допустимы −1…32 (соседи).</summary>
        private Cell Sample(int3 local)
        {
            int3 offset = new(
                local.x < 0 ? -1 : local.x >= Size ? 1 : 0,
                local.y < 0 ? -1 : local.y >= Size ? 1 : 0,
                local.z < 0 ? -1 : local.z >= Size ? 1 : 0);
            int view = (offset.x + 1) + (offset.y + 1) * 3 + (offset.z + 1) * 9;
            int3 inner = local - offset * Size;
            return Views[view].Get(WorldConstants.CellIndex(inner));
        }

        private static int3 Axis(int axis) => axis == 0 ? new int3(1, 0, 0) : axis == 1 ? new int3(0, 1, 0) : new int3(0, 0, 1);

        private static float AoToOcclusion(int ao) => ao == 0 ? 0.45f : ao == 1 ? 0.65f : ao == 2 ? 0.83f : 1f;
    }
}
