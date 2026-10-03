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
