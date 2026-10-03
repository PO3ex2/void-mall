using System;
using System.Collections.Generic;
using UnityEngine;

namespace VoidMall.World
{
    public enum Difficulty
    {
        Easy,
        Normal,
        Hard,
        Nightmare,
    }

    public enum DeathLoss
    {
        Nothing,
        Hotbar,
        Backpack,
        Everything,
    }

    /// <summary>
    /// Настройки мира (GDD §7). Сейчас генератор читает сид, частоту аномалий и веса разделов,
    /// остальное — задел для следующих этапов (лут, враги, смерть). Длительность суток читает WorldClock.
    /// </summary>
    [CreateAssetMenu(menuName = "Void Mall/World/World Settings", fileName = "WorldSettings")]
    public sealed class WorldSettings : ScriptableObject
    {
        [Serializable]
        public struct SectionWeight
        {
            public SectionDef Section;

            [Tooltip("Множитель к базовому весу раздела. 0 — раздел не встречается.")]
            [Min(0)] public float Multiplier;
        }

        [Header("Мир")]
        public uint Seed = 1337;
        public Difficulty Difficulty = Difficulty.Normal;

        [Header("Лут")]
        [Range(0f, 3f)] public float LootAmount = 1f;
        [Range(0f, 3f)] public float LootRarity = 1f;

        [Header("Опасности")]
        [Tooltip("Частота аномалий: 0 — нет, 1 — норма, 3 — очень часто.")]
        [Range(0f, 3f)] public float AnomalyFrequency = 1f;
        [Range(0f, 3f)] public float EnemyDensity = 1f;
        public DeathLoss DeathLoss = DeathLoss.Backpack;

        [Header("Время")]
        [Tooltip("Минут реального времени на игровые сутки.")]
        [Min(1f)] public float DayLengthMinutes = 60f;

        [Tooltip("Время суток на старте, часы.")]
        [Range(0f, 24f)] public float StartHour = 9f;

        [Header("Разделы")]
        public List<SectionDef> Sections = new();
        public List<SectionWeight> SectionWeights = new();

        public float GetWeight(SectionDef section)
        {
            foreach (var entry in SectionWeights)
                if (entry.Section == section)
                    return section.Weight * entry.Multiplier;
            return section.Weight;
        }
    }
}
