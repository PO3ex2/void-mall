using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace VoidMall.Console
{
    /// <summary>
    /// Файлы конфигурации консоли в Application.persistentDataPath (как в Source):
    /// config.cfg — сохранённые CVar с флагом Archive (пишется при выходе и командой host_writeconfig),
    /// autoexec.cfg — команды пользователя, выполняются при старте после config.cfg.
    /// </summary>
    public static class ConsoleConfig
    {
        public const string ConfigFile = "config.cfg";
        public const string AutoexecFile = "autoexec.cfg";

        public static string PathOf(string file) => Path.IsPathRooted(file) ? file : Path.Combine(Application.persistentDataPath, file);

        /// <summary>Выполнить файл построчно. Строки «//…» и пустые пропускаются.</summary>
        public static bool Exec(string file, bool quiet = false)
        {
            string path = PathOf(file);
            if (!File.Exists(path))
            {
                if (!quiet)
                    DevConsole.Error($"exec: файл не найден: {path}");
                return false;
            }
            foreach (var raw in File.ReadAllLines(path))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal))
                    continue;
                DevConsole.Execute(line);
            }
            return true;
        }

        public static void WriteConfig()
        {
            var sb = new StringBuilder("// Сохраняется автоматически. Свои команды пишите в autoexec.cfg.\n");
            foreach (var cvar in DevConsole.CVars.Where(c => (c.Flags & ConsoleFlags.Archive) != 0).OrderBy(c => c.Name))
                sb.Append($"{cvar.Name} {Quote(cvar.ValueString)}\n");
            try
            {
                Directory.CreateDirectory(Application.persistentDataPath);
                File.WriteAllText(PathOf(ConfigFile), sb.ToString());
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Console] Can't write {ConfigFile}: {e.Message}");
            }
        }

        private static string Quote(string value) => value.Contains(' ') ? $"\"{value}\"" : value;

        [ConsoleCommand("exec", "Выполнить файл команд из папки данных игры (например, exec autoexec.cfg).")]
        private static void ExecCommand(string file) => Exec(file);

        [ConsoleCommand("host_writeconfig", "Сохранить настройки (CVar с флагом Archive) в config.cfg.")]
        private static string WriteConfigCommand()
        {
            WriteConfig();
            return $"Сохранено: {PathOf(ConfigFile)}";
        }
    }
}
