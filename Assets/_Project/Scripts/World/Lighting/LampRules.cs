using Unity.Mathematics;
using VoidMall.Core;

namespace VoidMall.World
{
    /// <summary>
    /// Правила ламп. Один и тот же расчёт делают шейдер блоков (LampRules.hlsl — эмиссия) и LightingDirector
    /// (реальные источники света рядом с игроком), поэтому состояние лампы нигде не хранится — оно выводится
    /// из сида, координат клетки, времени суток и времени кадра.
    ///
    /// Группа ламп — макроячейка 64×64 на одном уровне. Ночью группы гаснут волной: у каждой своя фаза.
    /// Часть групп мертва всегда («участки тьмы»), отдельные лампы мёртвые или нестабильные (мигают).
    /// </summary>
    public static class LampRules
    {
        public const float EveningStart = 22f;
        public const float EveningEnd = 23f;
        public const float MorningStart = 6f;
        public const float MorningEnd = 7f;

        /// <summary>Аварийные лампы загораются по одной с 19:45 до 20:00 и гаснут по одной с 23:30 до полуночи.</summary>
        public const float EmergencyOnStart = 19.75f;
        public const float EmergencyOnEnd = 20f;
        public const float EmergencyOffStart = 23.5f;

        public const float DarkGroupChance = 0.12f;
        public const float DeadLampChance = 0.05f;
        public const float FlickerLampChance = 0.08f;

        /// <summary>То же в поле аномалий (GDD §2.9): лампы там мерцают в несколько раз чаще.</summary>
        public const float FieldFlickerLampChance = 0.35f;

        /// <summary>Ширина зоны мерцания вокруг фазы отключения группы (доля волны).</summary>
        public const float TransitionWidth = 0.03f;
        public const float EmergencyFlickerWidth = 0.06f;

        private const int SaltPhase = 900;
        private const int SaltDark = 901;
        private const int SaltLamp = 902;
        private const int SaltEmergency = 903;

        /// <summary>Прогресс волны отключения: 0 — всё горит (день), 1 — всё погашено (ночь).</summary>
        public static float Wave(float hour)
        {
            if (hour >= EveningStart && hour < EveningEnd)
                return (hour - EveningStart) / (EveningEnd - EveningStart);
            if (hour >= EveningEnd || hour < MorningStart)
                return 1f;
            if (hour < MorningEnd)
                return 1f - (hour - MorningStart) / (MorningEnd - MorningStart);
            return 0f;
        }

        /// <summary>Прогресс отключения аварийных ламп: 0 — горят (20:00–23:30), 1 — все погашены (с полуночи до 19:45).</summary>
        public static float EmergencyWave(float hour)
        {
            if (hour >= EmergencyOffStart)
                return (hour - EmergencyOffStart) / (24f - EmergencyOffStart);
            if (hour >= EmergencyOnEnd)
                return 0f;
            if (hour >= EmergencyOnStart)
                return 1f - (hour - EmergencyOnStart) / (EmergencyOnEnd - EmergencyOnStart);
            return 1f;
        }

        /// <summary>Аварийная лампа: у каждой своя фаза отключения, рядом с ней — мигает.</summary>
        public static float EmergencyIntensity(uint seed, int3 cell, float emergencyWave, float time)
        {
            uint lamp = Hash.Get(seed, cell.x, cell.y, cell.z, SaltEmergency);
            // Фаза внутри [w, 1 − w]: к полуночи погасли все, днём ни одна не мигает.
            float phase = EmergencyFlickerWidth + Hash.ToFloat01(lamp) * (1f - 2f * EmergencyFlickerWidth);
            float margin = emergencyWave - phase;
            if (margin > EmergencyFlickerWidth)
                return 0f;
            return margin > -EmergencyFlickerWidth ? Flicker(lamp, time, 14f) : 1f;
        }

        /// <summary>Яркость световых фонарей атриумов: днём 1, ночью почти 0, плавно на рассвете и закате.</summary>
        public static float SkylightLevel(float hour)
        {
            if (hour < 5f || hour >= 21f)
                return 0.03f;
            if (hour < 8f)
                return math.lerp(0.03f, 1f, math.smoothstep(5f, 8f, hour));
            if (hour < 18f)
                return 1f;
            return math.lerp(1f, 0.03f, math.smoothstep(18f, 21f, hour));
        }

        public static int3 GroupOf(int3 cell) => new(cell.x >> MallLayout.CellShift, MallLayout.LevelAt(cell.y), cell.z >> MallLayout.CellShift);

        public static bool IsDarkGroup(uint seed, int3 group) =>
            Hash.ToFloat01(Hash.Get(seed, group.x, group.y, group.z, SaltDark)) < DarkGroupChance;

        /// <summary>Множитель яркости обычной лампы (0…1).</summary>
        public static float LampIntensity(uint seed, int3 cell, float wave, float time, bool field = false)
        {
            int3 group = GroupOf(cell);
            if (IsDarkGroup(seed, group))
                return 0f;
            uint lamp = Hash.Get(seed, cell.x, cell.y, cell.z, SaltLamp);
            float roll = lamp % 1000 / 1000f;
            if (roll < DeadLampChance)
                return 0f;

            float phase = Hash.ToFloat01(Hash.Get(seed, group.x, group.y, group.z, SaltPhase));
            float margin = wave - phase;
            if (margin > TransitionWidth)
                return 0f;
            float on = 1f;
            if (margin > -TransitionWidth)
                on = Flicker(lamp, time, 14f);
            if (roll < DeadLampChance + (field ? FieldFlickerLampChance : FlickerLampChance))
                on *= Flicker(lamp ^ 0x9E3779B9u, time, 7f);
            return on;
        }

        public static float Intensity(uint seed, int3 cell, LampKind kind, float wave, float skylight, float emergencyWave, float time, bool field = false) => kind switch
        {
            LampKind.Lamp => LampIntensity(seed, cell, wave, time, field),
            LampKind.Emergency => EmergencyIntensity(seed, cell, emergencyWave, time),
            LampKind.Skylight or LampKind.Screen => skylight,
            _ => 0f,
        };

        /// <summary>Мерцание: ступеньки по rate раз в секунду, лампа то горит, то почти гаснет.</summary>
        public static float Flicker(uint lamp, float time, float rate)
        {
            int step = (int)math.floor(time * rate + Hash.ToFloat01(lamp) * 7f);
            return Hash.ToFloat01(Hash.Get(lamp, step)) > 0.35f ? 1f : 0.08f;
        }
    }
}
