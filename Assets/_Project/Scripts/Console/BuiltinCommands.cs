using System.Linq;
using System.Text;

namespace VoidMall.Console
{
    internal static class BuiltinCommands
    {
        [ConsoleCommand("help", "Список команд и переменных. help <имя> — подробнее.")]
        private static string Help(string name = "")
        {
            if (!string.IsNullOrEmpty(name))
            {
                if (DevConsole.TryGetCommand(name, out var cmd))
                    return $"{cmd.Usage}\n  {cmd.Description}{CheatSuffix(cmd.Flags)}";
                if (DevConsole.TryGetCVar(name, out var cvar))
                    return $"{cvar.Name} = {cvar.ValueString} (default {cvar.DefaultString})\n  {cvar.Description}{CheatSuffix(cvar.Flags)}";
                return $"Unknown '{name}'.";
            }

            var sb = new StringBuilder("Команды:");
            foreach (var cmd in DevConsole.Commands.OrderBy(c => c.Name))
                sb.Append($"\n  {cmd.Name,-16} {cmd.Description}{CheatSuffix(cmd.Flags)}");
            sb.Append("\nПеременные:");
            foreach (var cvar in DevConsole.CVars.OrderBy(c => c.Name))
                sb.Append($"\n  {cvar.Name,-16} = {cvar.ValueString,-8} {cvar.Description}{CheatSuffix(cvar.Flags)}");
            return sb.ToString();
        }

        [ConsoleCommand("find", "Найти команды и переменные по части имени.")]
        private static string Find(string text)
        {
            var names = DevConsole.Commands.Select(c => c.Name)
                .Concat(DevConsole.CVars.Select(c => c.Name))
                .Where(n => n.IndexOf(text, System.StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(n => n);
            return string.Join("  ", names);
        }

        [ConsoleCommand("echo", "Напечатать текст.")]
        private static string Echo(string[] text) => string.Join(" ", text);

        [ConsoleCommand("clear", "Очистить консоль.")]
        private static void Clear() => DevConsole.Clear();

        [ConsoleCommand("reset", "Сбросить переменную к значению по умолчанию.")]
        private static string Reset(string cvarName)
        {
            if (!DevConsole.TryGetCVar(cvarName, out var cvar))
                return $"Unknown cvar '{cvarName}'.";
            cvar.ResetToDefault();
            return $"{cvar.Name} = {cvar.ValueString}";
        }

        private static string CheatSuffix(ConsoleFlags flags) => (flags & ConsoleFlags.Cheat) != 0 ? " [cheat]" : string.Empty;
    }
}
