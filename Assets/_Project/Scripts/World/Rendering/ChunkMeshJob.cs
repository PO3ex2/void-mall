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

        /// <summary>Геометрия форм блоков (BlockShapes): вершины подряд и диапазоны по shape·24 + ориентация.</summary>
        [ReadOnly] public NativeArray<ShapeVertex> ShapeVertices;
        [ReadOnly] public NativeArray<int2> ShapeRanges;

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

            if (IsTriviallyEmpty())
                return;
            ChunkView center = Views[CenterView];

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
                // Блок с формой (полублок, скос…) — геометрия из таблицы форм.
                if (cell.Value != 0)
                {
                    EmitShape(new int3(x, y, z), cell, info);
                    continue;
                }

                for (int face = 0; face < 6; face++)
                {
                    int3 normal = new(FaceNormals[face * 3], FaceNormals[face * 3 + 1], FaceNormals[face * 3 + 2]);
                    int3 front = new int3(x, y, z) + normal;
                    if (ChunkOrigin.y + front.y < 0)
                        continue; // низ мира никто не видит — не рисуем
                    Cell neighbor = Sample(front);
                    if (!IsFaceVisible(cell, info, neighbor))
                        continue;
                    EmitFace(new int3(x, y, z), front, face, normal, info);
                }
            }

            BuildSmoothSurface();
        }

        // ───────────────────────── Гладкие воксели: Naive Surface Nets ─────────────────────────
        //
        // Решётка — центры клеток. Для каждого «куба» из 8 соседних центров со сменой знака ставим вершину
        // в среднее точек пересечения рёбер; для каждого ребра решётки со сменой знака — четырёхугольник из
        // вершин 4 кубов вокруг ребра. Чанк владеет рёбрами, начинающимися в его клетках, поэтому швов нет.
        // Четырёхугольник строится, только если твёрдая сторона ребра — гладкий материал: грани блоков рисует
        // блочный проход, а блоки для поля — плотность 255, так что гладкая поверхность упирается в них.

        private const int Pad = Size + 2;   // клетки −1…32
        private const int Cubes = Size + 1; // кубы с минимальным углом −1…31

        private static readonly int[] CubeEdges =
        {
            0, 1, 2, 3, 4, 5, 6, 7, // вдоль X
            0, 2, 1, 3, 4, 6, 5, 7, // вдоль Y
            0, 4, 1, 5, 2, 6, 3, 7, // вдоль Z
        };

        private void BuildSmoothSurface()
        {
            var density = new NativeArray<byte>(Pad * Pad * Pad, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            var smooth = new NativeArray<ushort>(Pad * Pad * Pad, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            var block = new NativeArray<bool>(Pad * Pad * Pad, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            bool anySmooth = false;

            for (int y = -1; y <= Size; y++)
            for (int z = -1; z <= Size; z++)
            for (int x = -1; x <= Size; x++)
            {
                Cell cell = Sample(new int3(x, y, z));
                int index = PadIndex(x, y, z);
                byte d;
                ushort smoothMaterial = 0;
                bool isBlock = false;
                if (cell.Material == 0)
                {
                    d = cell.Value >= Density.Iso ? (byte)(Density.Iso - 1) : cell.Value;
                }
                else if (Materials[cell.Material].Kind == MaterialKind.Smooth)
                {
                    d = cell.Value;
                    if (d >= Density.Iso)
                    {
                        smoothMaterial = cell.Material;
                        anySmooth = true;
                    }
                }
                else
                {
                    d = 255;
                    isBlock = true;
                }
                density[index] = d;
                smooth[index] = smoothMaterial;
                block[index] = isBlock;
            }

            if (!anySmooth)
                return;

            var vertexMap = new NativeArray<int>(Cubes * Cubes * Cubes, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            for (int i = 0; i < vertexMap.Length; i++)
                vertexMap[i] = -1;

            // Вершины.
            for (int y = -1; y < Size; y++)
            for (int z = -1; z < Size; z++)
            for (int x = -1; x < Size; x++)
            {
                int mask = 0;
                bool touchesBlock = false;
                float d0 = 0, d1 = 0, d2 = 0, d3 = 0, d4 = 0, d5 = 0, d6 = 0, d7 = 0;
                for (int k = 0; k < 8; k++)
                {
                    int corner = PadIndex(x + (k & 1), y + ((k >> 1) & 1), z + ((k >> 2) & 1));
                    float d = density[corner];
                    touchesBlock |= block[corner];
                    if (d >= Density.Iso)
                        mask |= 1 << k;
                    switch (k)
                    {
                        case 0: d0 = d; break;
                        case 1: d1 = d; break;
                        case 2: d2 = d; break;
                        case 3: d3 = d; break;
                        case 4: d4 = d; break;
                        case 5: d5 = d; break;
                        case 6: d6 = d; break;
                        default: d7 = d; break;
                    }
                }
                if (mask == 0 || mask == 255)
                    continue;

                float3 sum = float3.zero;
                int crossings = 0;
                for (int e = 0; e < 12; e++)
                {
                    int a = CubeEdges[e * 2], b = CubeEdges[e * 2 + 1];
                    if (((mask >> a) & 1) == ((mask >> b) & 1))
                        continue;
                    float da = Corner(a, d0, d1, d2, d3, d4, d5, d6, d7);
                    float db = Corner(b, d0, d1, d2, d3, d4, d5, d6, d7);
                    float t = (Density.IsoValue - da) / (db - da);
                    sum += math.lerp(CornerOffset(a), CornerOffset(b), t);
                    crossings++;
                }

                float3 gradient = new(
                    (d1 + d3 + d5 + d7) - (d0 + d2 + d4 + d6),
                    (d2 + d3 + d6 + d7) - (d0 + d1 + d4 + d5),
                    (d4 + d5 + d6 + d7) - (d0 + d1 + d2 + d3));

                // Материал вершины — у самой «наружной» твёрдой клетки (плотность ближе всего к поверхности),
                // а не у первой попавшейся: иначе на крутых склонах сквозь верхний слой проступает нижний.
                ushort material = 0;
                byte outermost = 255;
                for (int k = 0; k < 8; k++)
                {
                    int corner = PadIndex(x + (k & 1), y + ((k >> 1) & 1), z + ((k >> 2) & 1));
                    ushort candidate = smooth[corner];
                    if (candidate != 0 && (material == 0 || density[corner] < outermost))
                    {
                        material = candidate;
                        outermost = density[corner];
                    }
                }
                int layer = material != 0 ? Materials[material].TextureLayer : 0;

                vertexMap[CubeIndex(x, y, z)] = Vertices.Length;
                Vertices.Add(new ChunkVertex
                {
                    // Рядом с блоком вершину ставим в общий угол клеток — ровно в угол граней блока.
                    // Так гладкая поверхность стыкуется с гранями блоков без щелей (ценой лёгкой «ступенчатости» у блоков).
                    Position = touchesBlock ? new float3(x + 1, y + 1, z + 1) : new float3(x, y, z) + 0.5f + sum / crossings,
                    Normal = -math.normalizesafe(gradient, new float3(0, 1, 0)),
                    // Отрицательный слой — признак гладкой поверхности: шейдер текстурирует её трипланарно.
                    Uv = new float4(0f, 0f, -(layer + 1), 1f),
                });
            }

            // Четырёхугольники по рёбрам решётки, начинающимся в клетках этого чанка.
            for (int y = 0; y < Size; y++)
            for (int z = 0; z < Size; z++)
            for (int x = 0; x < Size; x++)
            {
                int3 p = new(x, y, z);
                bool solidP = density[PadIndex(x, y, z)] >= Density.Iso;
                for (int axis = 0; axis < 3; axis++)
                {
                    int3 q = p + Axis(axis);
                    bool solidQ = density[PadIndex(q.x, q.y, q.z)] >= Density.Iso;
                    if (solidP == solidQ)
                        continue;
                    int3 solidCell = solidP ? p : q;
                    if (smooth[PadIndex(solidCell.x, solidCell.y, solidCell.z)] == 0)
                        continue;

                    int3 e1 = Axis((axis + 1) % 3);
                    int3 e2 = Axis((axis + 2) % 3);
                    int v0 = vertexMap[CubeIndex(p - e1 - e2)];
                    int v1 = vertexMap[CubeIndex(p - e2)];
                    int v2 = vertexMap[CubeIndex(p)];
                    int v3 = vertexMap[CubeIndex(p - e1)];
                    if (v0 < 0 || v1 < 0 || v2 < 0 || v3 < 0)
                        continue;

                    // Наружу — от твёрдого к пустому: при твёрдом p нормаль смотрит в +axis.
                    if (solidP)
                    {
                        OpaqueIndices.Add(v0); OpaqueIndices.Add(v1); OpaqueIndices.Add(v2);
                        OpaqueIndices.Add(v0); OpaqueIndices.Add(v2); OpaqueIndices.Add(v3);
                    }
                    else
                    {
                        OpaqueIndices.Add(v0); OpaqueIndices.Add(v2); OpaqueIndices.Add(v1);
                        OpaqueIndices.Add(v0); OpaqueIndices.Add(v3); OpaqueIndices.Add(v2);
                    }
                }
            }
        }

        private static int PadIndex(int x, int y, int z) => (x + 1) + (z + 1) * Pad + (y + 1) * Pad * Pad;

        private static int CubeIndex(int x, int y, int z) => (x + 1) + (z + 1) * Cubes + (y + 1) * Cubes * Cubes;

        private static int CubeIndex(int3 m) => CubeIndex(m.x, m.y, m.z);

        private static float3 CornerOffset(int k) => new(k & 1, (k >> 1) & 1, (k >> 2) & 1);

        private static float Corner(int k, float d0, float d1, float d2, float d3, float d4, float d5, float d6, float d7) =>
            k == 0 ? d0 : k == 1 ? d1 : k == 2 ? d2 : k == 3 ? d3 : k == 4 ? d4 : k == 5 ? d5 : k == 6 ? d6 : d7;

        /// <summary>
        /// Чанк из воздуха, вокруг которого только однородные негладкие чанки, геометрии не даёт.
        /// (Воздушный чанк рядом с гладким пропускать нельзя: он владеет рёбрами на общей границе.)
        /// </summary>
        private bool IsTriviallyEmpty()
        {
            ChunkView center = Views[CenterView];
            if (center.Uniform == 0 || center.Get(0).Material != 0)
                return false;
            for (int i = 0; i < 27; i++)
            {
                ChunkView view = Views[i];
                if (view.Uniform == 0)
                    return false;
                Cell cell = view.Get(0);
                if (cell.Material != 0 && Materials[cell.Material].Kind == MaterialKind.Smooth)
                    return false;
            }
            return true;
        }

        private bool IsFaceVisible(Cell cell, MaterialInfo info, Cell neighbor)
        {
            if (neighbor.Material == 0)
                return true;
            MaterialInfo neighborInfo = Materials[neighbor.Material];
            // Сосед-форма (полублок, скос) закрывает грань не целиком — рисуем.
            if (neighborInfo.Kind == MaterialKind.Block && neighbor.Value != 0)
                return true;
            // Твёрдая гладкая клетка закрывает грань блока: стык с гладкой поверхностью
            // обеспечивает прижатие вершин Surface Nets к углам блоков (см. BuildSmoothSurface).
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
                    Uv = new float4(uv, axis == 1 ? info.TextureLayer : info.SideLayer, AoToOcclusion(ao)),
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

        /// <summary>Блок с формой: треугольники формы в его ориентации; грани на границе клетки у сплошного соседа пропускаем.</summary>
        private void EmitShape(int3 position, Cell cell, MaterialInfo info)
        {
            int shape = cell.Value;
            if (shape >= ShapeRanges.Length / BlockShapes.Orientations)
                return;
            int2 range = ShapeRanges[shape * BlockShapes.Orientations + math.min(cell.Meta & 31, BlockShapes.Orientations - 1)];
            var indices = info.IsTransparent ? TransparentIndices : OpaqueIndices;
            for (int i = 0; i < range.y; i += 3)
            {
                ShapeVertex a = ShapeVertices[range.x + i];
                if (a.Cull >= 0)
                {
                    bool partial = a.Cull >= BlockShapes.PartialCull;
                    int direction = partial ? a.Cull - BlockShapes.PartialCull : a.Cull;
                    int3 normal = new(FaceNormals[direction * 3], FaceNormals[direction * 3 + 1], FaceNormals[direction * 3 + 2]);
                    if (ChunkOrigin.y + position.y + normal.y < 0)
                        continue;
                    Cell neighbor = Sample(position + normal);
                    // Частичная грань прячется только за таким же соседом: та же форма, поворот и материал.
                    if (partial)
                    {
                        if (neighbor.Material == cell.Material && neighbor.Value == cell.Value && (neighbor.Meta & 31) == (cell.Meta & 31))
                            continue;
                    }
                    else if (neighbor.Material != 0)
                    {
                        MaterialInfo neighborInfo = Materials[neighbor.Material];
                        bool solidFull = neighborInfo.IsOpaque && (neighborInfo.Kind != MaterialKind.Block || neighbor.Value == 0);
                        bool sameGlass = info.IsTransparent && neighbor.Material == cell.Material && neighbor.Value == 0;
                        if (solidFull || sameGlass)
                            continue;
                    }
                }
                int baseVertex = Vertices.Length;
                for (int k = 0; k < 3; k++)
                {
                    ShapeVertex v = ShapeVertices[range.x + i + k];
                    float3 local = position + v.Position;
                    float3 world = ChunkOrigin + local;
                    float3 n = math.abs(v.Normal);
                    int axis = n.x >= n.y && n.x >= n.z ? 0 : n.y >= n.z ? 1 : 2;
                    float2 uv = axis == 0 ? world.zy : axis == 1 ? world.xz : world.xy;
                    Vertices.Add(new ChunkVertex { Position = local, Normal = v.Normal, Uv = new float4(uv, info.TextureLayer, 1f) });
                }
                indices.Add(baseVertex);
                indices.Add(baseVertex + 1);
                indices.Add(baseVertex + 2);
            }
        }

        private bool IsOccluder(int3 local)
        {
            Cell cell = Sample(local);
            return cell.Material != 0 && Materials[cell.Material].IsOpaque && (cell.Value == 0 || Materials[cell.Material].Kind != MaterialKind.Block);
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
