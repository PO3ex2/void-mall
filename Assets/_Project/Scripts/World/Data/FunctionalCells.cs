using System.Collections.Generic;
using Unity.Mathematics;

namespace VoidMall.World
{
    /// <summary>
    /// Упаковка функционального блока в клетки мира (ARCHITECTURE §4.9). Все клетки объёма — материал блока.
    /// Value — смещение клетки от якоря в осях блока (без поворота): dx биты 0–2, dy 3–5, dz 6–7; у якоря 0.
    /// Meta: биты 0–1 — поворот (×90° вокруг Y), 2 — открыт, 3 — заперт, 4 — сторона открывания.
    /// Состояние хранится в якоре и потому сохраняется и стримится вместе с правками мира.
    /// </summary>
    public static class FunctionalCells
    {
        public const byte OpenBit = 1 << 2;
        public const byte LockedBit = 1 << 3;
        public const byte SideBit = 1 << 4;

        public static byte PackOffset(int3 local) => (byte)((local.x & 7) | ((local.y & 7) << 3) | ((local.z & 3) << 6));

        public static int3 UnpackOffset(byte value) => new(value & 7, (value >> 3) & 7, (value >> 6) & 3);

        public static int Yaw(byte meta) => meta & 3;
        public static bool IsOpen(byte meta) => (meta & OpenBit) != 0;
        public static bool IsLocked(byte meta) => (meta & LockedBit) != 0;
        public static bool OpenSide(byte meta) => (meta & SideBit) != 0;

        public static byte Meta(int yaw, bool open = false, bool locked = false, bool side = false) =>
            (byte)((yaw & 3) | (open ? OpenBit : 0) | (locked ? LockedBit : 0) | (side ? SideBit : 0));

        /// <summary>Поворот смещения в осях блока на yaw×90° вокруг Y (как Quaternion.Euler(0, 90·yaw, 0)).</summary>
        public static int3 Rotate(int3 local, int yaw) => (yaw & 3) switch
        {
            0 => local,
            1 => new int3(local.z, local.y, -local.x),
            2 => new int3(-local.x, local.y, -local.z),
            _ => new int3(-local.z, local.y, local.x),
        };

        /// <summary>Мировая клетка части блока с якорем anchor.</summary>
        public static int3 CellOf(int3 anchor, int3 local, int yaw) => anchor + Rotate(local, yaw);

        /// <summary>Якорь по любой клетке блока (по её Value и Meta).</summary>
        public static int3 AnchorOf(int3 cell, Cell value) => cell - Rotate(UnpackOffset(value.Value), Yaw(value.Meta));

        /// <summary>Все клетки блока размера size с якорем anchor и поворотом yaw.</summary>
        public static void Footprint(int3 anchor, int3 size, int yaw, List<int3> result)
        {
            result.Clear();
            for (int y = 0; y < size.y; y++)
            for (int z = 0; z < size.z; z++)
            for (int x = 0; x < size.x; x++)
                result.Add(CellOf(anchor, new int3(x, y, z), yaw));
        }

        /// <summary>Клетка блока: материал, смещение от якоря, поворот (в частях дублируется), у якоря — ещё и состояние.</summary>
        public static Cell Part(ushort material, int3 local, byte meta) =>
            new(material, PackOffset(local), math.all(local == 0) ? meta : (byte)(meta & 3));
    }
}
