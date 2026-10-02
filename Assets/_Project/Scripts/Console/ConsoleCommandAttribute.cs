using System;

namespace VoidMall.Console
{
    [Flags]
    public enum ConsoleFlags
    {
        None = 0,

        /// <summary>Требует sv_cheats 1.</summary>
        Cheat = 1 << 0,

        /// <summary>CVar нельзя менять из консоли.</summary>
        ReadOnly = 1 << 1,
    }

    /// <summary>
    /// Помечает static-метод как консольную команду. Параметры метода — аргументы команды
    /// (string, int, float, bool, Vector2, Vector3, enum, необязательные со значением по умолчанию,
    /// последний может быть string[] — «остаток строки»). Если метод возвращает string, она печатается.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public sealed class ConsoleCommandAttribute : Attribute
    {
        public string Name { get; }
        public string Description { get; }
        public ConsoleFlags Flags { get; set; }

        public ConsoleCommandAttribute(string name, string description)
        {
            Name = name;
            Description = description;
        }
    }
}
