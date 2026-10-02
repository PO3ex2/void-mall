using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace VoidMall.Console
{
    /// <summary>Разбор строки консоли на токены и конвертация токенов в аргументы.</summary>
    public static class ConsoleParser
    {
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        /// <summary>Делит строку на команды по ';' вне кавычек.</summary>
        public static List<string> SplitStatements(string line)
        {
            var result = new List<string>();
            var current = new StringBuilder();
            bool quoted = false;
            foreach (char c in line)
            {
                if (c == '"')
                    quoted = !quoted;
                if (c == ';' && !quoted)
                {
                    result.Add(current.ToString());
                    current.Clear();
                    continue;
                }
                current.Append(c);
            }
            result.Add(current.ToString());
            return result;
        }

        /// <summary>Токены через пробел; "строка в кавычках" — один токен.</summary>
        public static string[] Tokenize(string statement)
        {
            var tokens = new List<string>();
            var current = new StringBuilder();
            bool quoted = false;
            bool hasToken = false;
            foreach (char c in statement)
            {
                if (c == '"')
                {
                    quoted = !quoted;
                    hasToken = true;
                    continue;
                }
                if (char.IsWhiteSpace(c) && !quoted)
                {
                    if (hasToken)
                    {
                        tokens.Add(current.ToString());
                        current.Clear();
                        hasToken = false;
                    }
                    continue;
                }
                current.Append(c);
                hasToken = true;
            }
            if (hasToken)
                tokens.Add(current.ToString());
            return tokens.ToArray();
        }

        /// <summary>Конвертирует токены (начиная с 1, т.к. 0 — имя команды) в аргументы метода.</summary>
        public static bool TryBindArguments(ParameterInfo[] parameters, string[] tokens, out object[] values, out string error)
        {
            values = new object[parameters.Length];
            int index = 1;
            for (int i = 0; i < parameters.Length; i++)
            {
                var p = parameters[i];
                bool isLast = i == parameters.Length - 1;

                if (isLast && p.ParameterType == typeof(string[]))
                {
                    int count = Math.Max(0, tokens.Length - index);
                    var rest = new string[count];
                    Array.Copy(tokens, index, rest, 0, count);
                    values[i] = rest;
                    index = tokens.Length;
                    continue;
                }

                if (index >= tokens.Length)
                {
                    if (p.HasDefaultValue)
                    {
                        values[i] = p.DefaultValue;
                        continue;
                    }
                    error = $"Missing argument '{p.Name}'.";
                    return false;
                }

                if (!TryParse(p.ParameterType, tokens, ref index, out values[i], out error))
                {
                    error = $"Argument '{p.Name}': {error}";
                    return false;
                }
            }

            if (index < tokens.Length)
            {
                error = "Too many arguments.";
                return false;
            }
            error = null;
            return true;
        }

        public static bool TryParse(Type type, string[] tokens, ref int index, out object value, out string error)
        {
            value = null;
            error = null;
            if (index >= tokens.Length)
            {
                error = "Missing value.";
                return false;
            }

            if (type == typeof(Vector3) || type == typeof(Vector2))
            {
                int components = type == typeof(Vector3) ? 3 : 2;
                var c = new float[components];
                for (int k = 0; k < components; k++)
                {
                    if (index >= tokens.Length || !TryParseFloat(tokens[index], out c[k]))
                    {
                        error = $"Expected {components} numbers.";
                        return false;
                    }
                    index++;
                }
                value = components == 3 ? new Vector3(c[0], c[1], c[2]) : new Vector2(c[0], c[1]);
                return true;
            }

            string token = tokens[index];
            bool ok = TryParseSingle(type, token, out value);
            if (!ok)
            {
                error = $"'{token}' is not a valid {TypeName(type)}.";
                return false;
            }
            index++;
            return true;
        }

        private static bool TryParseSingle(Type type, string token, out object value)
        {
            value = null;
            if (type == typeof(string))
            {
                value = token;
                return true;
            }
            if (type == typeof(int))
            {
                bool ok = int.TryParse(token, NumberStyles.Integer, Invariant, out int v);
                value = v;
                return ok;
            }
            if (type == typeof(long))
            {
                bool ok = long.TryParse(token, NumberStyles.Integer, Invariant, out long v);
                value = v;
                return ok;
            }
            if (type == typeof(uint))
            {
                bool ok = uint.TryParse(token, NumberStyles.Integer, Invariant, out uint v);
                value = v;
                return ok;
            }
            if (type == typeof(float))
            {
                bool ok = TryParseFloat(token, out float v);
                value = v;
                return ok;
            }
            if (type == typeof(bool))
            {
                switch (token.ToLowerInvariant())
                {
                    case "1": case "true": case "on": case "yes":
                        value = true;
                        return true;
                    case "0": case "false": case "off": case "no":
                        value = false;
                        return true;
                }
                return false;
            }
            if (type.IsEnum)
            {
                if (Enum.TryParse(type, token, ignoreCase: true, out object e) && Enum.IsDefined(type, e))
                {
                    value = e;
                    return true;
                }
                return false;
            }
            return false;
        }

        private static bool TryParseFloat(string token, out float value) =>
            float.TryParse(token.Replace(',', '.'), NumberStyles.Float, Invariant, out value);

        public static string Format(object value) => value switch
        {
            null => "null",
            bool b => b ? "1" : "0",
            float f => f.ToString("0.###", Invariant),
            Vector3 v => string.Format(Invariant, "{0:0.##} {1:0.##} {2:0.##}", v.x, v.y, v.z),
            Vector2 v => string.Format(Invariant, "{0:0.##} {1:0.##}", v.x, v.y),
            IFormattable f => f.ToString(null, Invariant),
            _ => value.ToString(),
        };

        public static string Usage(string name, ParameterInfo[] parameters)
        {
            var sb = new StringBuilder(name);
            foreach (var p in parameters)
            {
                sb.Append(' ');
                string inner = p.ParameterType == typeof(string[]) ? $"{p.Name}..." : $"{p.Name}:{TypeName(p.ParameterType)}";
                sb.Append(p.HasDefaultValue ? $"[{inner}={Format(p.DefaultValue)}]" : $"<{inner}>");
            }
            return sb.ToString();
        }

        public static string TypeName(Type type)
        {
            if (type == typeof(float)) return "float";
            if (type == typeof(int)) return "int";
            if (type == typeof(bool)) return "bool";
            if (type == typeof(string)) return "string";
            if (type == typeof(Vector3)) return "x y z";
            if (type == typeof(Vector2)) return "x y";
            if (type.IsEnum) return string.Join("|", Enum.GetNames(type));
            return type.Name;
        }
    }
}
