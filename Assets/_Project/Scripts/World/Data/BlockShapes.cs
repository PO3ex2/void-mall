using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace VoidMall.World
{
    /// <summary>Форма блока (Cell.Value у блоков). 0 — полный куб.</summary>
    public enum BlockShape : byte
    {
        Full,
        Slab,
        Stairs,
        Slope,
        SlopeInner,
        SlopeOuter,
        RoundOuter,
        RoundInner,
        Post,
        HalfWall,
        /// <summary>Тонкая панель 10 см по центру клетки (витрины, стеклянные ограждения).</summary>
        Pane,
        Count,
    }

    /// <summary>Вершина геометрии формы: позиция в клетке (0…1), нормаль, грань клетки, которую можно скрыть соседом (−1 — нельзя).</summary>
    public struct ShapeVertex
    {
        public float3 Position;
        public float3 Normal;
        public int Cull;
    }

    /// <summary>
    /// Геометрия форм блоков (ARCHITECTURE §4.9): треугольники для каждой формы и каждой из 24 ориентаций
    /// (Meta биты 0–4 = верх·4 + поворот: «верх» формы смотрит в одну из 6 сторон, и вокруг него 4 поворота по 90°). «Высокая» сторона формы смотрит в +Z.
    /// Грань на границе клетки, закрывающая её целиком, помечена направлением — мешер не рисует её у сплошного соседа.
    /// Одна таблица кормит мешер чанков (NativeArray) и призраки шаблонов (Mesh).
    /// </summary>
    public static class BlockShapes
    {
        public const int Orientations = 24;

        /// <summary>Куда смотрит «верх» формы: 0 +Y, 1 −Y, 2 +X, 3 −X, 4 +Z, 5 −Z.</summary>
        public const int UpDirections = 6;

        // Грани клетки — как в мешере: +X, −X, +Y, −Y, +Z, −Z.
        private static readonly int3[] Directions =
        {
            new(1, 0, 0), new(-1, 0, 0), new(0, 1, 0), new(0, -1, 0), new(0, 0, 1), new(0, 0, -1),
        };

        public static string Name(BlockShape shape) => shape switch
        {
            BlockShape.Slab => "Полублок",
            BlockShape.Stairs => "Ступенька",
            BlockShape.Slope => "Скос",
            BlockShape.SlopeInner => "Внутренний угол скоса",
            BlockShape.SlopeOuter => "Внешний угол скоса",
            BlockShape.RoundOuter => "Круглый скос (наружу)",
            BlockShape.RoundInner => "Круглый скос (внутрь)",
            BlockShape.Post => "Столб",
            BlockShape.HalfWall => "Стенка в полблока",
            BlockShape.Pane => "Тонкая панель",
            _ => "Блок",
        };

        public static int Orientation(byte meta) => math.min(meta & 31, Orientations - 1);
        public static byte Meta(int up, int yaw) => (byte)(math.clamp(up, 0, UpDirections - 1) * 4 + (yaw & 3));
        public static int Up(byte meta) => Orientation(meta) / 4;
        public static int Yaw(byte meta) => Orientation(meta) & 3;

        public static string UpName(int up) => up switch
        {
            0 => "вверх",
            1 => "вниз",
            2 => "на +X",
            3 => "на −X",
            4 => "на +Z",
            _ => "на −Z",
        };

        // ───────────────────────────── Построение (один раз) ─────────────────────────────

        private static List<ShapeVertex>[] _base;
        private static readonly Dictionary<int, Mesh> Meshes = new();

        /// <summary>Треугольники формы в ориентации (по 3 вершины). Кэшируется.</summary>
        public static List<ShapeVertex> Triangles(BlockShape shape, int orientation)
        {
            EnsureBase();
            var source = _base[(int)shape];
            var result = new List<ShapeVertex>(source.Count);
            int yaw = orientation & 3;
            int up = orientation / 4;
            for (int i = 0; i < source.Count; i += 3)
            {
                // Повороты собственные (без зеркала) — порядок обхода сохраняется.
                var a = Transform(source[i], yaw, up);
                var b = Transform(source[i + 1], yaw, up);
                var c = Transform(source[i + 2], yaw, up);
                result.Add(a);
                result.Add(b);
                result.Add(c);
            }
            return result;
        }

        /// <summary>Вся таблица для мешера: вершины подряд и диапазоны (начало, количество) по shape·8 + orientation.</summary>
        public static void BuildNative(out NativeArray<ShapeVertex> vertices, out NativeArray<int2> ranges, Allocator allocator)
        {
            var all = new List<ShapeVertex>();
            ranges = new NativeArray<int2>((int)BlockShape.Count * Orientations, allocator);
            for (int s = 1; s < (int)BlockShape.Count; s++)
            for (int o = 0; o < Orientations; o++)
            {
                var tris = Triangles((BlockShape)s, o);
                ranges[s * Orientations + o] = new int2(all.Count, tris.Count);
                all.AddRange(tris);
            }
            vertices = new NativeArray<ShapeVertex>(all.ToArray(), allocator);
        }

        /// <summary>Меш формы (для призраков шаблона), опорная точка — угол клетки (0,0,0).</summary>
        public static Mesh GetMesh(BlockShape shape, int orientation)
        {
            int key = (int)shape * Orientations + orientation;
            if (Meshes.TryGetValue(key, out var mesh) && mesh != null)
                return mesh;
            var tris = shape == BlockShape.Full ? CubeTriangles() : Triangles(shape, orientation);
            var positions = new Vector3[tris.Count];
            var normals = new Vector3[tris.Count];
            var indices = new int[tris.Count];
            for (int i = 0; i < tris.Count; i++)
            {
                positions[i] = tris[i].Position;
                normals[i] = tris[i].Normal;
                indices[i] = i;
            }
            mesh = new Mesh { name = $"Shape_{shape}_{orientation}", hideFlags = HideFlags.DontSave };
            mesh.vertices = positions;
            mesh.normals = normals;
            mesh.triangles = indices;
            mesh.RecalculateBounds();
            Meshes[key] = mesh;
            return mesh;
        }

        private static List<ShapeVertex> CubeTriangles()
        {
            var list = new List<ShapeVertex>();
            Box(list, float3.zero, new float3(1f));
            return list;
        }

        private static ShapeVertex Transform(ShapeVertex v, int yaw, int up)
        {
            float3 p = v.Position - 0.5f;
            p = Tilt(RotateYaw(p, yaw), up);
            float3 n = Tilt(RotateYaw(v.Normal, yaw), up);
            int cull = v.Cull;
            if (cull >= 0)
            {
                bool partial = cull >= PartialCull;
                int direction = DirectionIndex(Tilt(RotateYaw(Directions[partial ? cull - PartialCull : cull], yaw), up));
                cull = partial ? direction + PartialCull : direction;
            }
            return new ShapeVertex { Position = p + 0.5f, Normal = n, Cull = cull };
        }

        /// <summary>Поворот, переводящий +Y в направление up (вокруг центра клетки).</summary>
        private static float3 Tilt(float3 v, int up) => up switch
        {
            1 => new float3(v.x, -v.y, -v.z),   // 180° вокруг X
            2 => new float3(v.y, -v.x, v.z),    // −90° вокруг Z: +Y → +X
            3 => new float3(-v.y, v.x, v.z),    // +90° вокруг Z: +Y → −X
            4 => new float3(v.x, -v.z, v.y),    // +90° вокруг X: +Y → +Z
            5 => new float3(v.x, v.z, -v.y),    // −90° вокруг X: +Y → −Z
            _ => v,
        };

        /// <summary>Как FunctionalCells.Rotate: (x, z) → (z, −x) за шаг.</summary>
        private static float3 RotateYaw(float3 v, int yaw) => (yaw & 3) switch
        {
            0 => v,
            1 => new float3(v.z, v.y, -v.x),
            2 => new float3(-v.x, v.y, -v.z),
            _ => new float3(-v.z, v.y, v.x),
        };

        private static int DirectionIndex(float3 d)
        {
            for (int i = 0; i < 6; i++)
                if (math.all(math.abs(d - (float3)Directions[i]) < 0.01f))
                    return i;
            return -1;
        }

        private static void EnsureBase()
        {
            if (_base != null)
                return;
            _base = new List<ShapeVertex>[(int)BlockShape.Count];
            for (int i = 0; i < _base.Length; i++)
                _base[i] = new List<ShapeVertex>();

            Box(_base[(int)BlockShape.Slab], float3.zero, new float3(1f, 0.5f, 1f));
            Prism(_base[(int)BlockShape.Stairs], new[] { new float2(0, 0), new float2(1, 0), new float2(1, 1), new float2(0.5f, 1), new float2(0.5f, 0.5f), new float2(0, 0.5f) });
            Prism(_base[(int)BlockShape.Slope], new[] { new float2(0, 0), new float2(1, 0), new float2(1, 1) });
            Prism(_base[(int)BlockShape.RoundOuter], ArcProfile(convex: true));
            Prism(_base[(int)BlockShape.RoundInner], ArcProfile(convex: false));
            Box(_base[(int)BlockShape.Post], new float3(0.35f, 0f, 0.35f), new float3(0.65f, 1f, 0.65f));
            Box(_base[(int)BlockShape.HalfWall], new float3(0f, 0f, 0.5f), new float3(1f, 1f, 1f));
            Box(_base[(int)BlockShape.Pane], new float3(0f, 0f, 0.45f), new float3(1f, 1f, 0.55f));

            // Внутренний угол: высоко по задней (+Z) и правой (+X) кромкам, низко в переднем левом углу — «ендова».
            var inner = _base[(int)BlockShape.SlopeInner];
            Polygon(inner, new float3(0, -1, 0), new float3(0, 0, 0), new float3(1, 0, 0), new float3(1, 0, 1), new float3(0, 0, 1));
            Polygon(inner, new float3(0, 0, 1), new float3(0, 0, 1), new float3(1, 0, 1), new float3(1, 1, 1), new float3(0, 1, 1));
            Polygon(inner, new float3(1, 0, 0), new float3(1, 0, 0), new float3(1, 1, 0), new float3(1, 1, 1), new float3(1, 0, 1));
            Polygon(inner, new float3(-1, 0, 0), new float3(0, 0, 0), new float3(0, 0, 1), new float3(0, 1, 1));
            Polygon(inner, new float3(0, 0, -1), new float3(0, 0, 0), new float3(1, 1, 0), new float3(1, 0, 0));
            Polygon(inner, math.normalize(new float3(0, 1, -1)), new float3(0, 0, 0), new float3(1, 1, 1), new float3(0, 1, 1));
            Polygon(inner, math.normalize(new float3(-1, 1, 0)), new float3(0, 0, 0), new float3(1, 1, 0), new float3(1, 1, 1));

            // Внешний угол: высоко только в заднем правом углу (+X +Z).
            var outer = _base[(int)BlockShape.SlopeOuter];
            Polygon(outer, new float3(0, -1, 0), new float3(0, 0, 0), new float3(1, 0, 0), new float3(1, 0, 1), new float3(0, 0, 1));
            Polygon(outer, new float3(1, 0, 0), new float3(1, 0, 0), new float3(1, 1, 1), new float3(1, 0, 1));
            Polygon(outer, new float3(0, 0, 1), new float3(0, 0, 1), new float3(1, 0, 1), new float3(1, 1, 1));
            Polygon(outer, math.normalize(new float3(0, 1, -1)), new float3(0, 0, 0), new float3(1, 0, 0), new float3(1, 1, 1));
            Polygon(outer, math.normalize(new float3(-1, 1, 0)), new float3(0, 0, 0), new float3(1, 1, 1), new float3(0, 0, 1));
        }

        /// <summary>Профиль круглого скоса в (z, y) против часовой стрелки: выпуклый — дуга вокруг (1,0), вогнутый — вокруг (0,1).</summary>
        private static float2[] ArcProfile(bool convex)
        {
            const int segments = 6;
            var points = new List<float2> { new(1, 0) };
            for (int i = segments; i >= 0; i--)
            {
                float t = math.radians(90f * i / segments);
                points.Add(convex ? new float2(1f - math.cos(t), math.sin(t)) : new float2(math.sin(t), 1f - math.cos(t)));
            }
            return points.ToArray();
        }

        private static void Box(List<ShapeVertex> list, float3 min, float3 max)
        {
            float3 a = min, b = max;
            Polygon(list, new float3(1, 0, 0), new float3(b.x, a.y, a.z), new float3(b.x, b.y, a.z), new float3(b.x, b.y, b.z), new float3(b.x, a.y, b.z));
            Polygon(list, new float3(-1, 0, 0), new float3(a.x, a.y, a.z), new float3(a.x, a.y, b.z), new float3(a.x, b.y, b.z), new float3(a.x, b.y, a.z));
            Polygon(list, new float3(0, 1, 0), new float3(a.x, b.y, a.z), new float3(a.x, b.y, b.z), new float3(b.x, b.y, b.z), new float3(b.x, b.y, a.z));
            Polygon(list, new float3(0, -1, 0), new float3(a.x, a.y, a.z), new float3(b.x, a.y, a.z), new float3(b.x, a.y, b.z), new float3(a.x, a.y, b.z));
            Polygon(list, new float3(0, 0, 1), new float3(a.x, a.y, b.z), new float3(b.x, a.y, b.z), new float3(b.x, b.y, b.z), new float3(a.x, b.y, b.z));
            Polygon(list, new float3(0, 0, -1), new float3(a.x, a.y, a.z), new float3(a.x, b.y, a.z), new float3(b.x, b.y, a.z), new float3(b.x, a.y, a.z));
        }

        /// <summary>Призма: профиль в (z, y) против часовой стрелки, вытянут по X на всю клетку.</summary>
        private static void Prism(List<ShapeVertex> list, float2[] profile)
        {
            for (int i = 0; i < profile.Length; i++)
            {
                float2 p = profile[i], q = profile[(i + 1) % profile.Length];
                float2 d = q - p;
                var normal = math.normalize(new float3(0f, -d.x, d.y));
                Polygon(list, normal, new float3(0, p.y, p.x), new float3(1, p.y, p.x), new float3(1, q.y, q.x), new float3(0, q.y, q.x));
            }
            var cap = new float3[profile.Length];
            foreach (float x in new[] { 0f, 1f })
            {
                for (int i = 0; i < profile.Length; i++)
                    cap[i] = new float3(x, profile[i].y, profile[i].x);
                CapPolygon(list, new float3(x == 0f ? -1f : 1f, 0, 0), cap, profile);
            }
        }

        /// <summary>Выпуклый плоский многоугольник: веер треугольников, обход по нормали.</summary>
        private static void Polygon(List<ShapeVertex> list, float3 normal, params float3[] points)
        {
            int cull = CullDirection(points);
            for (int i = 1; i + 1 < points.Length; i++)
                Triangle(list, normal, points[0], points[i], points[i + 1], cull);
        }

        /// <summary>Торец призмы (профиль может быть невыпуклым, как у ступеньки): отсечение ушей в (z, y).</summary>
        private static void CapPolygon(List<ShapeVertex> list, float3 normal, float3[] points, float2[] profile)
        {
            int cull = CullDirection(points);
            var indices = new List<int>();
            for (int i = 0; i < profile.Length; i++)
                indices.Add(i);
            int guard = 0;
            while (indices.Count > 3 && guard++ < 64)
            {
                for (int i = 0; i < indices.Count; i++)
                {
                    int ia = indices[(i + indices.Count - 1) % indices.Count], ib = indices[i], ic = indices[(i + 1) % indices.Count];
                    float2 a = profile[ia], b = profile[ib], c = profile[ic];
                    if (Cross(b - a, c - b) <= 1e-6f)
                        continue; // вогнутая вершина
                    bool contains = false;
                    foreach (int j in indices)
                        if (j != ia && j != ib && j != ic && InTriangle(profile[j], a, b, c))
                            contains = true;
                    if (contains)
                        continue;
                    Triangle(list, normal, points[ia], points[ib], points[ic], cull);
                    indices.RemoveAt(i);
                    break;
                }
            }
            if (indices.Count == 3)
                Triangle(list, normal, points[indices[0]], points[indices[1]], points[indices[2]], cull);
        }

        private static float Cross(float2 a, float2 b) => a.x * b.y - a.y * b.x;

        private static bool InTriangle(float2 p, float2 a, float2 b, float2 c)
        {
            float d1 = Cross(b - a, p - a), d2 = Cross(c - b, p - b), d3 = Cross(a - c, p - c);
            return d1 > 1e-6f && d2 > 1e-6f && d3 > 1e-6f;
        }

        private static void Triangle(List<ShapeVertex> list, float3 normal, float3 a, float3 b, float3 c, int cull)
        {
            // Лицевая сторона: cross(b − a, c − a) смотрит наружу (как у граней в мешере).
            if (math.dot(math.cross(b - a, c - a), normal) < 0f)
                (b, c) = (c, b);
            list.Add(new ShapeVertex { Position = a, Normal = normal, Cull = cull });
            list.Add(new ShapeVertex { Position = b, Normal = normal, Cull = cull });
            list.Add(new ShapeVertex { Position = c, Normal = normal, Cull = cull });
        }

        /// <summary>Cull ≥ PartialCull — грань на границе клетки, но не во всю (направление = Cull − PartialCull).</summary>
        public const int PartialCull = 6;

        /// <summary>Многоугольник лежит на грани клетки: во всю → индекс грани, частично → PartialCull + индекс, иначе −1.</summary>
        private static int CullDirection(float3[] points)
        {
            for (int axis = 0; axis < 3; axis++)
            {
                foreach (float plane in new[] { 0f, 1f })
                {
                    bool onPlane = true;
                    foreach (var p in points)
                        onPlane &= math.abs(p[axis] - plane) < 1e-4f;
                    if (!onPlane)
                        continue;
                    // Площадь проекции на плоскость грани.
                    int u = (axis + 1) % 3, v = (axis + 2) % 3;
                    float area = 0f;
                    for (int i = 0; i < points.Length; i++)
                    {
                        var p = points[i];
                        var q = points[(i + 1) % points.Length];
                        area += p[u] * q[v] - q[u] * p[v];
                    }
                    // На грани клетки, но не во всю: прячется только за таким же соседом (панель к панели, полублок к полублоку).
                    if (math.abs(math.abs(area) * 0.5f - 1f) > 1e-3f)
                        return PartialCull + axis * 2 + (plane > 0.5f ? 0 : 1);
                    return axis * 2 + (plane > 0.5f ? 0 : 1);
                }
            }
            return -1;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _base = null;
            Meshes.Clear();
        }
    }
}
