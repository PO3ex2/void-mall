using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using VoidMall.Core;

namespace VoidMall.World
{
    /// <summary>Визуал и коллизия одного чанка. Объекты переиспользуются пулом в ChunkManager.</summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
    public sealed class ChunkRenderer : MonoBehaviour
    {
        private static readonly Bounds ChunkBounds = new(
            Vector3.one * (WorldConstants.ChunkSize * 0.5f),
            Vector3.one * WorldConstants.ChunkSize);

        private Mesh _mesh;
        private MeshCollider _collider;

        // Вода — отдельный объект без коллизии (сквозь неё плывут).
        private Mesh _waterMesh;
        private MeshRenderer _waterRenderer;

        public Mesh Mesh => _mesh;
        public bool HasGeometry { get; private set; }

        public static ChunkRenderer Create(Transform parent, Material opaque, Material transparent, Material water)
        {
            var go = new GameObject("Chunk") { layer = GameLayers.World };
            go.transform.SetParent(parent, false);
            var renderer = go.AddComponent<ChunkRenderer>();
            var meshRenderer = go.GetComponent<MeshRenderer>();
            meshRenderer.sharedMaterials = new[] { opaque, transparent };
            meshRenderer.shadowCastingMode = ShadowCastingMode.On;
            renderer._collider = go.GetComponent<MeshCollider>();
            renderer._mesh = new Mesh { name = "ChunkMesh", indexFormat = IndexFormat.UInt32 };
            renderer._mesh.MarkDynamic();
            go.GetComponent<MeshFilter>().sharedMesh = renderer._mesh;

            var waterGo = new GameObject("Water") { layer = GameLayers.World };
            waterGo.transform.SetParent(go.transform, false);
            renderer._waterMesh = new Mesh { name = "ChunkWater", indexFormat = IndexFormat.UInt32 };
            renderer._waterMesh.MarkDynamic();
            waterGo.AddComponent<MeshFilter>().sharedMesh = renderer._waterMesh;
            renderer._waterRenderer = waterGo.AddComponent<MeshRenderer>();
            renderer._waterRenderer.sharedMaterial = water;
            renderer._waterRenderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer._waterRenderer.enabled = false;
            return renderer;
        }

        public void Place(int3 chunkCoord)
        {
            transform.position = new Vector3(
                chunkCoord.x * WorldConstants.ChunkSize,
                chunkCoord.y * WorldConstants.ChunkSize,
                chunkCoord.z * WorldConstants.ChunkSize);
            name = $"Chunk {chunkCoord.x},{chunkCoord.y},{chunkCoord.z}";
        }

        /// <summary>Заливает результат мешинга. Коллайдер обновляется отдельно, после запекания.</summary>
        public void Apply(NativeList<ChunkVertex> vertices, NativeList<int> opaque, NativeList<int> transparent)
        {
            int vertexCount = vertices.Length;
            int opaqueCount = opaque.Length;
            int transparentCount = transparent.Length;
            HasGeometry = vertexCount > 0;

            const MeshUpdateFlags flags = MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices;
            _mesh.Clear();
            _mesh.SetVertexBufferParams(vertexCount, ChunkVertex.Layout);
            _mesh.SetVertexBufferData(vertices.AsArray(), 0, 0, vertexCount, 0, flags);
            _mesh.SetIndexBufferParams(opaqueCount + transparentCount, IndexFormat.UInt32);
            _mesh.SetIndexBufferData(opaque.AsArray(), 0, 0, opaqueCount, flags);
            _mesh.SetIndexBufferData(transparent.AsArray(), 0, opaqueCount, transparentCount, flags);
            _mesh.subMeshCount = 2;
            _mesh.SetSubMesh(0, new SubMeshDescriptor(0, opaqueCount), flags);
            _mesh.SetSubMesh(1, new SubMeshDescriptor(opaqueCount, transparentCount), flags);
            _mesh.bounds = ChunkBounds;

            if (!HasGeometry)
                _collider.sharedMesh = null;
        }

        /// <summary>Вода чанка (без коллизии).</summary>
        public void ApplyWater(NativeList<ChunkVertex> vertices, NativeList<int> indices)
        {
            const MeshUpdateFlags flags = MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices;
            _waterMesh.Clear();
            bool any = vertices.Length > 0;
            _waterRenderer.enabled = any;
            if (!any)
                return;
            _waterMesh.SetVertexBufferParams(vertices.Length, ChunkVertex.Layout);
            _waterMesh.SetVertexBufferData(vertices.AsArray(), 0, 0, vertices.Length, 0, flags);
            _waterMesh.SetIndexBufferParams(indices.Length, IndexFormat.UInt32);
            _waterMesh.SetIndexBufferData(indices.AsArray(), 0, 0, indices.Length, flags);
            _waterMesh.subMeshCount = 1;
            _waterMesh.SetSubMesh(0, new SubMeshDescriptor(0, indices.Length), flags);
            _waterMesh.bounds = ChunkBounds;
        }

        /// <summary>Меш уже запечён (Physics.BakeMesh) — назначение мгновенное.</summary>
        public void ApplyCollider()
        {
            _collider.sharedMesh = null;
            _collider.sharedMesh = HasGeometry ? _mesh : null;
        }

        public void ResetForPool()
        {
            _mesh.Clear();
            _waterMesh.Clear();
            _waterRenderer.enabled = false;
            _collider.sharedMesh = null;
            HasGeometry = false;
            gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            if (_mesh != null)
                Destroy(_mesh);
            if (_waterMesh != null)
                Destroy(_waterMesh);
        }
    }
}
