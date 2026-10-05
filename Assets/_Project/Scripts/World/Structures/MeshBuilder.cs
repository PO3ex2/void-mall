using System.Collections.Generic;
using UnityEngine;

namespace VoidMall.World
{
    /// <summary>Сборка простых мешей из коробок с цветом вершин (rgb — цвет, a — гладкость) для шейдера VoidMall/Props.</summary>
    public sealed class MeshBuilder
    {
        private readonly List<Vector3> _positions = new();
        private readonly List<Vector3> _normals = new();
        private readonly List<Color> _colors = new();
        private readonly List<int> _indices = new();

        private static readonly Vector3[] FaceNormals =
        {
            Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back,
        };

        public int VertexCount => _positions.Count;

        public void Clear()
        {
            _positions.Clear();
            _normals.Clear();
            _colors.Clear();
            _indices.Clear();
        }

        /// <summary>Коробка по центру и размеру, с поворотом вокруг центра.</summary>
        public void Box(Vector3 center, Vector3 size, Color color, Quaternion? rotation = null)
        {
            Quaternion r = rotation ?? Quaternion.identity;
            Vector3 h = size * 0.5f;
            foreach (var n in FaceNormals)
            {
                // Две оси грани.
                Vector3 u = Mathf.Abs(n.y) > 0.5f ? Vector3.right : Vector3.up;
                Vector3 v = Vector3.Cross(n, u);
                Vector3 faceCenter = Vector3.Scale(n, h);
                Vector3 du = Vector3.Scale(u, h);
                Vector3 dv = Vector3.Scale(v, h);
                int start = _positions.Count;
                _positions.Add(center + r * (faceCenter - du - dv));
                _positions.Add(center + r * (faceCenter - du + dv));
                _positions.Add(center + r * (faceCenter + du + dv));
                _positions.Add(center + r * (faceCenter + du - dv));
                Vector3 normal = r * n;
                for (int i = 0; i < 4; i++)
                {
                    _normals.Add(normal);
                    _colors.Add(color);
                }
                // Проверяем ориентацию: треугольник должен смотреть по нормали.
                Vector3 a = _positions[start], b = _positions[start + 1], c = _positions[start + 2];
                bool flip = Vector3.Dot(Vector3.Cross(b - a, c - a), normal) < 0f;
                if (flip)
                {
                    _indices.Add(start); _indices.Add(start + 2); _indices.Add(start + 1);
                    _indices.Add(start); _indices.Add(start + 3); _indices.Add(start + 2);
                }
                else
                {
                    _indices.Add(start); _indices.Add(start + 1); _indices.Add(start + 2);
                    _indices.Add(start); _indices.Add(start + 2); _indices.Add(start + 3);
                }
            }
        }

        /// <summary>Коробка по двум углам (без поворота).</summary>
        public void BoxMinMax(Vector3 min, Vector3 max, Color color) => Box((min + max) * 0.5f, max - min, color);

        /// <summary>Наклонная плита между двумя точками оси (ширина поперёк, толщина вниз от линии).</summary>
        public void Slab(Vector3 from, Vector3 to, float width, float thickness, Color color)
        {
            Vector3 axis = to - from;
            float length = axis.magnitude;
            Quaternion rotation = Quaternion.LookRotation(axis / length, Vector3.up);
            Vector3 down = rotation * Vector3.down;
            Box((from + to) * 0.5f + down * thickness * 0.5f, new Vector3(width, thickness, length), color, rotation);
        }

        /// <summary>Четырёхугольник a-b-c-d, лицом по нормали (обход подбирается сам).</summary>
        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, Color color)
        {
            int start = _positions.Count;
            _positions.Add(a);
            _positions.Add(b);
            _positions.Add(c);
            _positions.Add(d);
            for (int i = 0; i < 4; i++)
            {
                _normals.Add(normal);
                _colors.Add(color);
            }
            bool flip = Vector3.Dot(Vector3.Cross(b - a, c - a), normal) < 0f;
            if (flip)
            {
                _indices.Add(start); _indices.Add(start + 2); _indices.Add(start + 1);
                _indices.Add(start); _indices.Add(start + 3); _indices.Add(start + 2);
            }
            else
            {
                _indices.Add(start); _indices.Add(start + 1); _indices.Add(start + 2);
                _indices.Add(start); _indices.Add(start + 2); _indices.Add(start + 3);
            }
        }

        /// <summary>
        /// Кольцо вокруг вертикальной оси от y0 до y1: верх и (если walls) внешняя и внутренняя стенки —
        /// бортик чаши фонтана; с нулевой высотой и без стенок — узор на полу или водная гладь.
        /// </summary>
        public void Ring(Vector3 center, float inner, float outer, float y0, float y1, int segments, Color color, bool walls = true)
        {
            for (int i = 0; i < segments; i++)
            {
                float a0 = i * Mathf.PI * 2f / segments, a1 = (i + 1) * Mathf.PI * 2f / segments;
                var d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0));
                var d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                Vector3 top = Vector3.up * y1, bottom = Vector3.up * y0;
                Quad(center + d0 * inner + top, center + d0 * outer + top, center + d1 * outer + top, center + d1 * inner + top, Vector3.up, color);
                if (!walls)
                    continue;
                Vector3 mid = (d0 + d1).normalized;
                Quad(center + d0 * outer + bottom, center + d1 * outer + bottom, center + d1 * outer + top, center + d0 * outer + top, mid, color);
                Quad(center + d0 * inner + bottom, center + d0 * inner + top, center + d1 * inner + top, center + d1 * inner + bottom, -mid, color);
            }
        }

        public Mesh ToMesh(string name)
        {
            var mesh = new Mesh { name = name };
            if (_positions.Count > 65000)
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(_positions);
            mesh.SetNormals(_normals);
            mesh.SetColors(_colors);
            mesh.SetTriangles(_indices, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
