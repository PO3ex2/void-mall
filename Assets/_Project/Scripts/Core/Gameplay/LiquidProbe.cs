using System;
using UnityEngine;

namespace VoidMall.Core
{
    /// <summary>
    /// Вода в точке мира — для модулей, которые не видят мир напрямую (мотор игрока): мир (ChunkManager) подставляет
    /// проверку при запуске. Без мира воды нет.
    /// </summary>
    public static class LiquidProbe
    {
        public static Func<Vector3, bool> IsLiquidAt;

        public static bool At(Vector3 position) => IsLiquidAt != null && IsLiquidAt(position);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => IsLiquidAt = null;
    }
}
