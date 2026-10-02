using UnityEngine;

namespace VoidMall.Core
{
    /// <summary>
    /// Базовый класс описаний контента (предметы, блоки, рецепты...).
    /// Id — стабильный строковый ключ для сейвов и сети, например "item.food.canned_beans".
    /// </summary>
    public abstract class ContentDef : ScriptableObject
    {
        [SerializeField] private string _id;

        public string Id => _id;

        /// <summary>Индекс в реестре на время сессии. Не сохранять — в сейв пишется Id.</summary>
        public int RuntimeIndex { get; internal set; } = -1;
    }
}
