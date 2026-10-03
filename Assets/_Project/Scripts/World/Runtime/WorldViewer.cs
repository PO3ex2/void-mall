using System.Collections.Generic;
using UnityEngine;

namespace VoidMall.World
{
    /// <summary>Вокруг этого объекта прогружается мир (игрок; позже — камера портала, ИИ-якоря).</summary>
    public sealed class WorldViewer : MonoBehaviour
    {
        private static readonly List<WorldViewer> Active = new();

        /// <summary>Первый активный наблюдатель (локальный игрок) — для отладочных команд и освещения.</summary>
        public static WorldViewer Primary => Active.Count > 0 ? Active[0] : null;

        private void OnEnable()
        {
            Active.Add(this);
            ChunkManager.RegisterViewer(transform);
        }

        private void OnDisable()
        {
            Active.Remove(this);
            ChunkManager.UnregisterViewer(transform);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Active.Clear();
    }
}
