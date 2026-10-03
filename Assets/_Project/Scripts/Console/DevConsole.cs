using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Assemblies;

namespace VoidMall.Console
{
    public enum ConsoleLineType
    {
        Input,
        Info,
        Warning,
        Error,
    }

    public readonly struct ConsoleLine
    {
        public readonly string Text;
        public readonly ConsoleLineType Type;

        public ConsoleLine(string text, ConsoleLineType type)
        {
            Text = text;
            Type = type;
        }
    }

    /// <summary>
    /// Бэкенд игровой консоли: реестр команд и CVar, выполнение строк, журнал, история, автодополнение.
    /// Один и тот же бэкенд используют оверлей (~) и приложение cmd на ПК в лобби.
    /// </summary>
    public static class DevConsole
    {
        public sealed class CommandInfo
        {
            public string Name;
            public string Description;
            public ConsoleFlags Flags;
            public MethodInfo Method;
            public ParameterInfo[] Parameters;

            public string Usage => ConsoleParser.Usage(Name, Parameters);
        }

        public const int MaxLines = 1000;
        private const int MaxHistory = 100;
        private const string AssemblyPrefix = "VoidMall";

        public static readonly CVar<bool> Cheats = new("sv_cheats", Debug.isDebugBuild, "Разрешить чит-команды.");
        public static readonly CVar<bool> MirrorUnityLog = new("con_unitylog", true, "Дублировать сообщения Unity в консоль.", ConsoleFlags.Archive);

        private static readonly Dictionary<string, CommandInfo> _commands = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, CVarBase> _cvars = new(StringComparer.OrdinalIgnoreCase);
        private static readonly List<ConsoleLine> _lines = new();
        private static readonly List<string> _history = new();

        public static bool IsInitialized { get; private set; }
        public static IReadOnlyList<ConsoleLine> Lines => _lines;
        public static IReadOnlyList<string> History => _history;
        public static IEnumerable<CommandInfo> Commands => _commands.Values;
        public static IEnumerable<CVarBase> CVars => _cvars.Values;

        public static event Action<ConsoleLine> LineAdded;
        public static event Action Cleared;

        public static void Initialize()
        {
            if (IsInitialized)
                return;
            IsInitialized = true;

            foreach (var assembly in CurrentAssemblies.GetLoadedAssemblies())
            {
                if (!assembly.GetName().Name.StartsWith(AssemblyPrefix, StringComparison.Ordinal))
                    continue;
                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException e)
                {
                    types = e.Types.Where(t => t != null).ToArray();
                }
                foreach (var type in types)
                    ScanType(type);
            }

            Application.logMessageReceived += OnUnityLog;
            Application.quitting += ConsoleConfig.WriteConfig;

            // Настройки игрока и пользовательский автозапуск (как в Source).
            ConsoleConfig.Exec(ConsoleConfig.ConfigFile, quiet: true);
            ConsoleConfig.Exec(ConsoleConfig.AutoexecFile, quiet: true);
        }

        private static void ScanType(Type type)
        {
            const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

            foreach (var method in type.GetMethods(flags))
            {
                var attr = method.GetCustomAttribute<ConsoleCommandAttribute>();
                if (attr == null)
                    continue;
                RegisterCommand(new CommandInfo
                {
                    Name = attr.Name,
                    Description = attr.Description,
                    Flags = attr.Flags,
                    Method = method,
                    Parameters = method.GetParameters(),
                });
            }

            if (type.ContainsGenericParameters)
                return;
            foreach (var field in type.GetFields(flags))
            {
                if (!typeof(CVarBase).IsAssignableFrom(field.FieldType))
                    continue;
                if (field.GetValue(null) is CVarBase cvar)
                    RegisterCVar(cvar);
            }
        }

        public static void RegisterCommand(CommandInfo command)
        {
            if (_commands.ContainsKey(command.Name) || _cvars.ContainsKey(command.Name))
            {
                Debug.LogError($"[Console] Duplicate command name '{command.Name}'.");
                return;
            }
            _commands.Add(command.Name, command);
        }

        public static void RegisterCVar(CVarBase cvar)
        {
            if (_commands.ContainsKey(cvar.Name) || _cvars.ContainsKey(cvar.Name))
            {
                Debug.LogError($"[Console] Duplicate cvar name '{cvar.Name}'.");
                return;
            }
            _cvars.Add(cvar.Name, cvar);
        }

        public static bool TryGetCommand(string name, out CommandInfo command) => _commands.TryGetValue(name, out command);
        public static bool TryGetCVar(string name, out CVarBase cvar) => _cvars.TryGetValue(name, out cvar);

        /// <summary>Выполнить строку, введённую пользователем (с эхом и записью в историю).</summary>
        public static void Submit(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;
            line = line.Trim();
            AddLine(new ConsoleLine("> " + line, ConsoleLineType.Input));
            if (_history.Count == 0 || _history[^1] != line)
            {
                _history.Add(line);
                if (_history.Count > MaxHistory)
                    _history.RemoveAt(0);
            }
            Execute(line);
        }

        /// <summary>Выполнить строку без эха (из кода, конфигов, биндов).</summary>
        public static void Execute(string line)
        {
            foreach (var statement in ConsoleParser.SplitStatements(line))
            {
                var tokens = ConsoleParser.Tokenize(statement);
                if (tokens.Length > 0)
                    ExecuteTokens(tokens);
            }
        }

        private static void ExecuteTokens(string[] tokens)
        {
            string name = tokens[0];

            if (_commands.TryGetValue(name, out var command))
            {
                if ((command.Flags & ConsoleFlags.Cheat) != 0 && !Cheats.Value)
                {
                    Error($"'{command.Name}' is a cheat command. Enable with: sv_cheats 1");
                    return;
                }
                if (!ConsoleParser.TryBindArguments(command.Parameters, tokens, out var args, out var error))
                {
                    Error(error);
                    Log("Usage: " + command.Usage);
                    return;
                }
                try
                {
                    object result = command.Method.Invoke(null, args);
                    if (result is string text && !string.IsNullOrEmpty(text))
                        Log(text);
                }
                catch (TargetInvocationException e)
                {
                    Error($"{command.Name}: {e.InnerException?.Message ?? e.Message}");
                    Debug.LogException(e.InnerException ?? e);
                }
                return;
            }

            if (_cvars.TryGetValue(name, out var cvar))
            {
                if (tokens.Length == 1)
                {
                    Log($"{cvar.Name} = {cvar.ValueString}  (default {cvar.DefaultString}) — {cvar.Description}");
                    return;
                }
                if ((cvar.Flags & ConsoleFlags.ReadOnly) != 0)
                {
                    Error($"{cvar.Name} is read-only.");
                    return;
                }
                if ((cvar.Flags & ConsoleFlags.Cheat) != 0 && !Cheats.Value)
                {
                    Error($"{cvar.Name} is cheat protected. Enable with: sv_cheats 1");
                    return;
                }
                var valueTokens = tokens.Skip(1).ToArray();
                if (cvar.TrySet(valueTokens, out var setError))
                    Log($"{cvar.Name} = {cvar.ValueString}");
                else
                    Error($"{cvar.Name}: {setError}");
                return;
            }

            Error($"Unknown command '{name}'. Type 'help' for the list.");
        }

        /// <summary>
        /// Варианты автодополнения — целые строки. Первое слово: команды и переменные. Следующие слова — аргументы:
        /// зарегистрированные варианты команды, 0/1 для bool, имена enum, имена команд для help/reset/find.
        /// </summary>
        public static List<string> GetCompletions(string line)
        {
            line = (line ?? string.Empty).TrimStart();
            int space = line.IndexOf(' ');
            if (space < 0)
            {
                return _commands.Keys
                    .Concat(_cvars.Keys)
                    .Where(n => n.StartsWith(line, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            string name = line.Substring(0, space);
            int lastSpace = line.LastIndexOf(' ');
            string head = line.Substring(0, lastSpace + 1);
            string partial = line.Substring(lastSpace + 1);
            int argIndex = line.Substring(space).Split(' ', StringSplitOptions.RemoveEmptyEntries).Length - (partial.Length > 0 ? 1 : 0);
            return ArgumentCandidates(name, argIndex)
                .Where(c => c.StartsWith(partial, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
                .Select(c => head + c)
                .ToList();
        }

        private static readonly Dictionary<string, Func<int, IEnumerable<string>>> _completers = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Варианты аргументов команды: completer(номер токена после имени) → строки.</summary>
        public static void RegisterCompleter(string command, Func<int, IEnumerable<string>> completer) => _completers[command] = completer;

        private static IEnumerable<string> ArgumentCandidates(string name, int tokenIndex)
        {
            if (_completers.TryGetValue(name, out var completer))
                return completer(tokenIndex) ?? Enumerable.Empty<string>();
            if (name.Equals("help", StringComparison.OrdinalIgnoreCase) || name.Equals("find", StringComparison.OrdinalIgnoreCase))
                return tokenIndex == 0 ? _commands.Keys.Concat(_cvars.Keys) : Enumerable.Empty<string>();
            if (name.Equals("reset", StringComparison.OrdinalIgnoreCase))
                return tokenIndex == 0 ? _cvars.Keys : Enumerable.Empty<string>();
            if (_cvars.TryGetValue(name, out var cvar))
                return tokenIndex == 0 ? ValuesOf(cvar.ValueType) : Enumerable.Empty<string>();
            if (_commands.TryGetValue(name, out var command))
            {
                // Параметр, на который попадает токен (Vector3 занимает три токена).
                int token = 0;
                foreach (var parameter in command.Parameters)
                {
                    int width = parameter.ParameterType == typeof(Vector3) ? 3 : parameter.ParameterType == typeof(Vector2) ? 2 : 1;
                    if (tokenIndex < token + width)
                        return ValuesOf(parameter.ParameterType);
                    token += width;
                }
            }
            return Enumerable.Empty<string>();
        }

        private static IEnumerable<string> ValuesOf(Type type)
        {
            if (type == typeof(bool))
                return new[] { "0", "1" };
            if (type.IsEnum)
                return Enum.GetNames(type);
            return Enumerable.Empty<string>();
        }

        public static string CommonPrefix(IReadOnlyList<string> values)
        {
            if (values.Count == 0)
                return string.Empty;
            string prefix = values[0];
            foreach (var v in values)
            {
                int i = 0;
                while (i < prefix.Length && i < v.Length && char.ToLowerInvariant(prefix[i]) == char.ToLowerInvariant(v[i]))
                    i++;
                prefix = prefix.Substring(0, i);
            }
            return prefix;
        }

        public static void Log(string text) => AddLine(new ConsoleLine(text, ConsoleLineType.Info));
        public static void Warn(string text) => AddLine(new ConsoleLine(text, ConsoleLineType.Warning));
        public static void Error(string text) => AddLine(new ConsoleLine(text, ConsoleLineType.Error));

        public static void Clear()
        {
            _lines.Clear();
            Cleared?.Invoke();
        }

        private static void AddLine(ConsoleLine line)
        {
            _lines.Add(line);
            if (_lines.Count > MaxLines)
                _lines.RemoveRange(0, _lines.Count - MaxLines);
            LineAdded?.Invoke(line);
        }

        private static void OnUnityLog(string message, string stackTrace, LogType type)
        {
            if (!MirrorUnityLog.Value)
                return;
            switch (type)
            {
                case LogType.Warning:
                    Warn(message);
                    break;
                case LogType.Error:
                case LogType.Exception:
                case LogType.Assert:
                    Error(message);
                    break;
                default:
                    Log(message);
                    break;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Application.logMessageReceived -= OnUnityLog;
            Application.quitting -= ConsoleConfig.WriteConfig;
            _completers.Clear();
            foreach (var cvar in _cvars.Values)
            {
                cvar.ClearSubscribers();
                cvar.ResetToDefault();
            }
            _commands.Clear();
            _cvars.Clear();
            _lines.Clear();
            LineAdded = null;
            Cleared = null;
            IsInitialized = false;
        }
    }
}
