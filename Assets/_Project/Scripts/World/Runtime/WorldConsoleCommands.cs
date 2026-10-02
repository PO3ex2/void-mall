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

        [ConsoleCommand("anomalies", "Ближайшие аномалии-бездны (до 5) и расстояние до них.")]
        private static string Anomalies()
        {
            if (!TryGetWorld(out var world, out var error))
                return error;
            var player = Camera.main != null ? Camera.main.transform.position : Vector3.zero;
            int cellX = (int)math.floor(player.x / TestWorldGenerationJob.AnomalyCellSize);
            int cellZ = (int)math.floor(player.z / TestWorldGenerationJob.AnomalyCellSize);
            var found = new System.Collections.Generic.List<(float distance, string text)>();
            for (int dz = -3; dz <= 3; dz++)
            for (int dx = -3; dx <= 3; dx++)
            {
                if (!TestWorldGenerationJob.TryGetShaft(world.Seed, cellX + dx, cellZ + dz, out float2 center, out float radius))
                    continue;
                float distance = math.distance(new float2(player.x, player.z), center);
                found.Add((distance, $"  бездна ({center.x:0}, {TestWorldGenerationJob.FloorBlockY + 1}, {center.y:0}): радиус шахты {radius:0} м, край пола в {TestWorldGenerationJob.ZoneRadius(radius):0} м от центра, до неё {distance:0} м"));
            }
            if (found.Count == 0)
                return "Рядом аномалий нет.";
            found.Sort((a, b) => a.distance.CompareTo(b.distance));
            return "Аномалии (tp к краю: tp x 18 z):\n" + string.Join("\n", found.Take(5).Select(f => f.text));
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
