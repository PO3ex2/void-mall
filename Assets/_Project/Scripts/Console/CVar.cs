using System;

namespace VoidMall.Console
{
    /// <summary>
    /// Консольная переменная. Объявляется static readonly полем в любом классе сборки VoidMall.*:
    /// <code>public static readonly CVar&lt;float&gt; ViewDistance = new("r_viewdistance", 8f, "...");</code>
    /// DevConsole находит такие поля сам при инициализации.
    /// </summary>
    public abstract class CVarBase
    {
        public string Name { get; }
        public string Description { get; }
        public ConsoleFlags Flags { get; }

        public event Action Changed;

        protected CVarBase(string name, string description, ConsoleFlags flags)
        {
            Name = name;
            Description = description;
            Flags = flags;
        }

        public abstract Type ValueType { get; }
        public abstract string ValueString { get; }
        public abstract string DefaultString { get; }

        public abstract bool TrySet(string[] tokens, out string error);
        public abstract void ResetToDefault();

        protected void RaiseChanged() => Changed?.Invoke();

        internal void ClearSubscribers() => Changed = null;
    }

    public sealed class CVar<T> : CVarBase
    {
        private T _value;

        public T Default { get; }

        public T Value
        {
            get => _value;
            set
            {
                if (Equals(_value, value))
                    return;
                _value = value;
                RaiseChanged();
            }
        }

        public CVar(string name, T defaultValue, string description, ConsoleFlags flags = ConsoleFlags.None)
            : base(name, description, flags)
        {
            Default = defaultValue;
            _value = defaultValue;
        }

        public override Type ValueType => typeof(T);
        public override string ValueString => ConsoleParser.Format(_value);
        public override string DefaultString => ConsoleParser.Format(Default);

        public override bool TrySet(string[] tokens, out string error)
        {
            int index = 0;
            if (!ConsoleParser.TryParse(typeof(T), tokens, ref index, out object parsed, out error))
                return false;
            if (index < tokens.Length)
            {
                error = "Too many arguments.";
                return false;
            }
            Value = (T)parsed;
            return true;
        }

        public override void ResetToDefault() => Value = Default;

        public static implicit operator T(CVar<T> cvar) => cvar.Value;
    }
}
