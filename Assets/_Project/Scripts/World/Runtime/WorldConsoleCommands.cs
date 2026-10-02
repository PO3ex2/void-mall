using System.Linq;
using System.Text;
using Unity.Mathematics;
using UnityEngine;
using VoidMall.Console;
using VoidMall.Core;
using VoidMall.Net;

namespace VoidMall.World
{
    internal static class WorldConsoleCommands
    {
        [ConsoleCommand("setblock", "Поставить блок: setblock x y z материал (air — убрать).", Flags = ConsoleFlags.Cheat)]
        private static string SetBlock(Vector3 position, string material)
        {
            if (!TryGetWorld(out var world, out var error) || !TryResolve(world, material, out ushort index, out error))
                return error;
            Send(new SetBlockCommand { Position = ToCell(position), Cell = new Cell(index) });
            return null;
        }

        [ConsoleCommand("fill", "Заполнить область: fill x1 y1 z1 x2 y2 z2 материал (до 32768 клеток).", Flags = ConsoleFlags.Cheat)]
        private static string Fill(Vector3 from, Vector3 to, string material)
        {
            if (!TryGetWorld(out var world, out var error) || !TryResolve(world, material, out ushort index, out error))
                return error;
            int3 a = ToCell(from), b = ToCell(to);
            int3 size = math.abs(b - a) + 1;
            long volume = (long)size.x * size.y * size.z;
            if (volume > WorldAuthority.MaxFillVolume)
                return $"Слишком большая область: {volume} клеток (максимум {WorldAuthority.MaxFillVolume}).";
            Send(new FillBlocksCommand { Min = a, Max = b, Cell = new Cell(index) });
            return $"fill: {volume} клеток";
        }

        [ConsoleCommand("blocks", "Список материалов мира.")]
        private static string Blocks()
        {
            if (!TryGetWorld(out var world, out var error))
                return error;
            var sb = new StringBuilder();
            foreach (var def in world.Materials.All)
                sb.Append($"\n  {def.RuntimeIndex,3}  {def.Id,-20} {def.DisplayName}  (прочность {def.Hardness}{(def.Unbreakable ? ", неразрушим" : "")})");
            return "Материалы:" + sb;
        }

        [ConsoleCommand("world_info", "Состояние мира: колонны, очереди джобов, сид.")]
        private static string WorldInfo()
        {
            if (!TryGetWorld(out var world, out var error))
                return error;
            return $"Сид {world.Seed}, радиус {ChunkManager.ViewDistance.Value} колонн\n" +
                   $"Колонн загружено {world.LoadedColumns}, отложено изменённых {world.ParkedColumns}\n" +
                   $"Джобов: генерация {world.GenerationTasksInFlight}, мешинг {world.MeshTasksInFlight}";
        }

        private static void Send<T>(in T command) where T : struct, INetCommand
        {
            if (Services.TryGet(out INetSession session))
                session.SendToAuthority(command);
        }

        private static int3 ToCell(Vector3 position) => (int3)math.floor((float3)position);

        private static bool TryGetWorld(out ChunkManager world, out string error)
        {
            world = ChunkManager.Instance;
            error = world == null ? "В сцене нет мира (ChunkManager)." : null;
            return world != null;
        }

        /// <summary>Принимает полный ID (block.concrete), короткое имя (concrete) или индекс.</summary>
        private static bool TryResolve(ChunkManager world, string name, out ushort index, out string error)
        {
            error = null;
            if (ushort.TryParse(name, out index) && index < world.Materials.Count)
                return true;
            if (world.Materials.TryGetIndex(name, out index) || world.Materials.TryGetIndex("block." + name, out index))
                return true;
            var similar = world.Materials.All.Select(m => m.Id).Where(id => id.Contains(name)).Take(5);
            error = $"Нет материала '{name}'. Похожие: {string.Join(", ", similar)}. Список: blocks";
            return false;
        }
    }
}
