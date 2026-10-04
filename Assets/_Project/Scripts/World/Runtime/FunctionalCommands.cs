using System.Text;
using Unity.Mathematics;
using UnityEngine;
using VoidMall.Console;
using VoidMall.Core;
using VoidMall.Net;

namespace VoidMall.World
{
    internal static class FunctionalCommands
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterCompleters() =>
            DevConsole.RegisterCompleter("fblock_spawn", token => token == 0 ? new[] { "door", "gate", "sliding_door_1", "sliding_door_2", "sliding_door_4" } : null);

        [ConsoleCommand("shapes", "Формы блоков (для шаблона): номер и название.")]
        private static string Shapes()
        {
            var sb = new StringBuilder("Формы блоков:");
            for (int i = 1; i < (int)BlockShape.Count; i++)
                sb.Append($"\n  {i} — {BlockShapes.Name((BlockShape)i)}");
            return sb.ToString();
        }

        [ConsoleCommand("fblock_spawn", "Поставить функциональный блок под прицелом: fblock_spawn <door|gate|sliding_door_1|sliding_door_2|sliding_door_4>.", Flags = ConsoleFlags.Cheat)]
        private static string Spawn(string kind)
        {
            var world = ChunkManager.Instance;
            var camera = Camera.main;
            if (world == null || camera == null)
                return "Нет мира или камеры.";
            if (!world.Materials.TryGetIndex("fblock." + kind, out ushort material))
                return $"Нет функционального блока '{kind}'.";
            if (!world.Raycast(camera.transform.position, camera.transform.forward, 8f, out var hit) || math.all(hit.Normal == 0))
                return "Наведите прицел на поверхность.";
            int3 anchor = hit.Cell + hit.Normal;
            int yaw = (int)math.round(camera.transform.eulerAngles.y / 90f) & 3;
            if (!WorldAuthority.CanPlaceFunctional(world, anchor, material, yaw))
                return "Не помещается.";
            Send(new PlaceFunctionalCommand { Anchor = anchor, Material = material, Yaw = (byte)yaw });
            return $"Поставлено: {world.Materials.Get(material).DisplayName}";
        }

        [ConsoleCommand("door_open", "Открыть или закрыть дверь или ворота под прицелом (замок снимается).", Flags = ConsoleFlags.Cheat)]
        private static string Toggle()
        {
            var world = ChunkManager.Instance;
            var camera = Camera.main;
            if (world == null || camera == null)
                return "Нет мира или камеры.";
            if (!Physics.Raycast(camera.transform.position, camera.transform.forward, out var physicsHit, 8f, 1 << GameLayers.World, QueryTriggerInteraction.Ignore)
                || !FunctionalBlocks.TryGetView(physicsHit.collider, out var view))
                return "Наведите прицел на дверь или ворота.";
            Cell anchor = world.GetCell(view.Anchor);
            bool open = !FunctionalCells.IsOpen(anchor.Meta);
            Send(new SetFunctionalStateCommand { Anchor = view.Anchor, Open = open, Locked = false });
            return open ? "Открыто" : "Закрыто";
        }

        private static void Send<T>(in T command) where T : struct, INetCommand
        {
            if (Services.TryGet(out INetSession session))
                session.SendToAuthority(command);
        }
    }
}
