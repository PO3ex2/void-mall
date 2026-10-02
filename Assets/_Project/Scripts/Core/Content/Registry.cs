using System;
using System.Collections.Generic;

namespace VoidMall.Core
{
    /// <summary>
    /// Реестр описаний контента: Id ↔ runtime-индекс ↔ описание.
    /// </summary>
    public sealed class Registry<T> where T : ContentDef
    {
        private readonly List<T> _byIndex = new();
        private readonly Dictionary<string, T> _byId = new(StringComparer.Ordinal);

        public int Count => _byIndex.Count;
        public IReadOnlyList<T> All => _byIndex;

        public void Register(T def)
        {
            if (def == null)
                throw new ArgumentNullException(nameof(def));
            if (string.IsNullOrWhiteSpace(def.Id))
                throw new ArgumentException($"{typeof(T).Name} '{def.name}' has empty Id.");
            if (_byId.ContainsKey(def.Id))
                throw new ArgumentException($"Duplicate {typeof(T).Name} Id '{def.Id}'.");

            def.RuntimeIndex = _byIndex.Count;
            _byIndex.Add(def);
            _byId.Add(def.Id, def);
        }

        public T Get(int index) => _byIndex[index];

        public T Get(string id) =>
            _byId.TryGetValue(id, out var def) ? def : throw new KeyNotFoundException($"{typeof(T).Name} '{id}' not found.");

        public bool TryGet(string id, out T def) => _byId.TryGetValue(id, out def);

        public void Clear()
        {
            foreach (var def in _byIndex)
                def.RuntimeIndex = -1;
            _byIndex.Clear();
            _byId.Clear();
        }
    }
}
