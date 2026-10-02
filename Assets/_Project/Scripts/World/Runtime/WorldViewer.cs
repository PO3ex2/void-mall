using UnityEngine;

namespace VoidMall.World
{
    /// <summary>Вокруг этого объекта прогружается мир (игрок; позже — камера портала, ИИ-якоря).</summary>
    public sealed class WorldViewer : MonoBehaviour
    {
        private void OnEnable() => ChunkManager.RegisterViewer(transform);

        private void OnDisable() => ChunkManager.UnregisterViewer(transform);
    }
}
