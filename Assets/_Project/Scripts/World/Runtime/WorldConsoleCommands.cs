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

        [ConsoleCommand("anomalies", "Ближайшие аномалии (до 8): вид, этаж, координаты. Телепорт: goto_anomaly <вид>.")]
        private static string Anomalies()
        {
            if (!TryGetWorld(out var world, out var error))
                return error;
            var found = FindAnomalies(world, PlayerPosition(), 4);
            if (found.Count == 0)
                return "Рядом аномалий нет.";
            return "Аномалии:\n" + string.Join("\n", found.Take(8).Select(a =>
                $"  {a.Name,-24} {a.Class.ClassName(),-15} этаж {MallLayout.LevelName(a.Level),-2} ({a.Target.x:0}, {a.Target.y:0}, {a.Target.z:0}), до неё {a.Distance:0} м"));
        }

        [ConsoleCommand("goto_anomaly", "Телепорт к ближайшей аномалии: abyss, pool, cutout, inverted, oasis, distortion, glitch, pulse, lookme, puddle.", Flags = ConsoleFlags.Cheat)]
        private static string GotoAnomaly(string kind)
        {
            if (!TryGetWorld(out var world, out var error))
                return error;
            var match = FindAnomalies(world, PlayerPosition(), 6).FirstOrDefault(a => a.Id == kind.ToLowerInvariant());
            if (match.Id == null)
                return $"Рядом нет аномалии '{kind}'.";
            DevConsole.Execute(string.Format(System.Globalization.CultureInfo.InvariantCulture, "tp {0:0.##} {1:0.##} {2:0.##}", match.Target.x, match.Target.y, match.Target.z));
            return $"{match.Name}: этаж {MallLayout.LevelName(match.Level)}";
        }

        [ConsoleCommand("where", "Где я: этаж, комната и раздел, макроячейка.")]
        private static string Where()
        {
            if (!TryGetWorld(out var world, out var error))
                return error;
            return MallDebug.Describe(world, PlayerPosition());
        }

        [ConsoleCommand("seed", "Сид мира. seed <число> — пересоздать мир с новым сидом (правки теряются).", Flags = ConsoleFlags.Cheat)]
        private static string Seed(string value = "")
        {
            if (!TryGetWorld(out var world, out var error))
                return error;
            if (string.IsNullOrEmpty(value))
                return $"seed {world.Seed}";
            uint seed = uint.TryParse(value, out uint parsed) ? parsed : Core.Hash.String(value);
            world.ResetWorld(seed);
            return $"Мир пересоздан, seed {seed}";
        }

        [ConsoleCommand("regen_chunk", "Перегенерировать колонну под игроком (правки в ней теряются).", Flags = ConsoleFlags.Cheat)]
        private static string RegenChunk()
        {
            if (!TryGetWorld(out var world, out var error))
                return error;
            int2 column = WorldConstants.CellToColumn((int3)math.floor((float3)PlayerPosition()));
            return world.RegenerateColumn(column) ? $"Колонна ({column.x}, {column.y}) перегенерируется." : "Колонна не загружена.";
        }

        [ConsoleCommand("biome", "Биом ячейки под игроком и карта биомов 9×9 вокруг (Н — обычный, П — пустой, А — поле аномалий).")]
        private static string Biome()
        {
            if (!TryGetWorld(out var world, out var error))
                return error;
            int2 center = MallLayout.CellOf(PlayerPosition());
            var sb = new System.Text.StringBuilder();
            sb.Append($"Ячейка ({center.x}, {center.y}): {world.GetCellInfo(center).Biome.DisplayName()}\n");
            for (int dz = 4; dz >= -4; dz--)
            {
                for (int dx = -4; dx <= 4; dx++)
                {
                    var biome = world.GetCellInfo(center + new int2(dx, dz)).Biome;
                    char c = biome switch { MallBiome.Empty => 'П', MallBiome.AnomalyField => 'А', _ => 'Н' };
                    sb.Append(dx == 0 && dz == 0 ? '[' : ' ').Append(c).Append(dx == 0 && dz == 0 ? ']' : ' ');
                }
                sb.Append('\n');
            }
            return sb.ToString().TrimEnd();
        }

        private struct AnomalyEntry
        {
            public string Id, Name;
            public AnomalyClass Class;
            public int Level;
            public Vector3 Target;
            public float Distance;
        }

        /// <summary>Аномалии в макроячейках вокруг точки. Target — точка для телепорта (у края, на полу).</summary>
        private static System.Collections.Generic.List<AnomalyEntry> FindAnomalies(ChunkManager world, Vector3 from, int radius)
        {
            var list = new System.Collections.Generic.List<AnomalyEntry>();
            int2 center = MallLayout.CellOf(from);
            for (int dz = -radius; dz <= radius; dz++)
            for (int dx = -radius; dx <= radius; dx++)
            {
                var info = world.GetCellInfo(center + new int2(dx, dz));
                if (info.Anomaly != AnomalyKind.None)
                {
                    // Перевёрнутый чанк — на всех этажах: ведём к его краю на первом торговом.
                    int level = info.Anomaly == AnomalyKind.Inverted ? MallLayout.FirstRetailLevel : info.AnomalyLevel;
                    float2 c = info.AnomalyCenter;
                    float edge = info.Anomaly == AnomalyKind.Abyss ? MallAnomalies.AbyssZoneRadius(info.AnomalyRadius) + 1f
                        : info.Anomaly == AnomalyKind.Pool ? MallAnomalies.PoolZoneRadius(info.AnomalyRadius) + 1f
                        : info.Anomaly == AnomalyKind.Oasis ? info.AnomalyRadius * 0.5f
                        : 0f;
                    Vector3 target = info.Anomaly is AnomalyKind.Cutout or AnomalyKind.Inverted
                        ? new Vector3(c.x - 1.5f, MallLayout.FloorTop(level), c.y + 16f)
                        : new Vector3(c.x + edge, MallLayout.FloorTop(level), c.y);
                    list.Add(new AnomalyEntry { Id = info.Anomaly.Id(), Name = info.Anomaly.DisplayName(), Class = info.Anomaly.Class(), Level = level, Target = target });
                }
                for (int i = 0; i < info.MarkerCount; i++)
                {
                    var marker = info.GetMarker(i);
                    int level = MallLayout.LevelAtFeet(marker.Center.y);
                    list.Add(new AnomalyEntry
                    {
                        Id = marker.Kind.Id(),
                        Name = marker.Kind.DisplayName(),
                        Class = marker.Kind.Class(),
                        Level = level,
                        // Лужа — рядом с ней, вспышка — в нескольких метрах (чтобы было видно), объёмы — в центре.
                        Target = new Vector3(marker.Center.x + (marker.Kind == AnomalyMarkerKind.Puddle ? marker.Radius + 1f : marker.Kind == AnomalyMarkerKind.LookAtMe ? 6f : 0f),
                            MallLayout.FloorTop(level), marker.Center.z),
                    });
                }
            }
            for (int i = 0; i < list.Count; i++)
            {
                var entry = list[i];
                entry.Distance = Vector3.Distance(from, entry.Target);
                list[i] = entry;
            }
            list.Sort((a, b) => a.Distance.CompareTo(b.Distance));
            return list;
        }

        private static Vector3 PlayerPosition()
        {
            var viewer = WorldViewer.Primary;
            if (viewer != null)
                return viewer.transform.position;
            return Camera.main != null ? Camera.main.transform.position : Vector3.zero;
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
