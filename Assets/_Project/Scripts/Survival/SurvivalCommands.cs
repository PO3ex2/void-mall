using System;
using System.Collections.Generic;
using System.Text;
using VoidMall.Console;
using VoidMall.Core;

namespace VoidMall.Survival
{
    internal static class SurvivalCommands
    {
        private static readonly List<StatusKind> Conditions = new();

        private static readonly string[] StatusNames = { "bleeding", "bleeding_heavy", "fracture_leg", "fracture_arm", "pain", "infection", "poisoning", "trauma", "unrested" };
        private static readonly string[] NeedNames = { "food", "water", "rest", "sanity", "temp" };

        // Подсказки Tab для аргументов (список дополнений очищается в SubsystemRegistration — регистрируем после).
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterCompleters()
        {
            DevConsole.RegisterCompleter("status_add", token => token == 0 ? StatusNames : null);
            DevConsole.RegisterCompleter("set_need", token => token == 0 ? NeedNames : null);
            DevConsole.RegisterCompleter("hurt", token => token == 0 ? new[] { "10", "30", "50", "99" } : null);
        }

        [ConsoleCommand("vitals", "Показатели игрока: здоровье, сытость, вода, бодрость, рассудок, температура, состояния.")]
        private static string Vitals()
        {
            if (!TryGet(out var vitals, out string error))
                return error;
            var m = vitals.Model;
            var env = vitals.Environment;
            var sb = new StringBuilder();
            sb.Append($"Здоровье {m.Health:0}/{m.MaxHealth:0}, стамина {m.Stamina:0}/{m.MaxStamina:0}\n");
            sb.Append($"Сытость {m.Calories:0}/{VitalsModel.MaxCalories:0} ккал, вода {m.Water:0}/{VitalsModel.MaxWater:0} мл, бодрость {m.Rest:0}%, рассудок {m.Sanity:0}%\n");
            sb.Append($"Тело {m.BodyTemperature:0.0} °C, воздух {env.AirTemperature:0} °C + одежда {env.Warmth:0} °C, свет {env.Light:0.00}{(env.InAnomaly ? ", в аномалии" : "")}\n");
            var (fat, protein, carbs) = m.DietShares;
            sb.Append(fat + protein + carbs > 0f
                ? $"Рацион за сутки: жиры {fat * 100f:0}%, белки {protein * 100f:0}%, углеводы {carbs * 100f:0}% — {(m.DietBalanced == true ? "сбалансирован" : "однообразно")}\n"
                : "Рацион за сутки: почти ничего не ели\n");
            m.GetConditions(Conditions);
            sb.Append(Conditions.Count == 0 ? "Состояния: нет" : "Состояния: " + string.Join(", ", Conditions.ConvertAll(k => StatusCatalog.Get(k).Name)));
            return sb.ToString();
        }

        [ConsoleCommand("hurt", "Нанести урон себе: hurt <здоровье>.", Flags = ConsoleFlags.Cheat)]
        private static string Hurt(float amount)
        {
            if (!TryGet(out var vitals, out string error))
                return error;
            vitals.Model.Hurt(amount, "Команда hurt");
            return $"Здоровье {vitals.Model.Health:0}";
        }

        [ConsoleCommand("heal", "Полностью вылечить и накормить.", Flags = ConsoleFlags.Cheat)]
        private static string Heal()
        {
            if (!TryGet(out var vitals, out string error))
                return error;
            vitals.Model.HealAll();
            return "Здоровы, сыты и бодры.";
        }

        [ConsoleCommand("status_add", "Добавить состояние: status_add <bleeding|bleeding_heavy|fracture_leg|fracture_arm|pain|infection|poisoning|trauma|unrested>.", Flags = ConsoleFlags.Cheat)]
        private static string StatusAdd(string kind)
        {
            if (!TryGet(out var vitals, out string error))
                return error;
            var m = vitals.Model;
            switch (kind)
            {
                case "bleeding": m.Add(StatusKind.BleedingLight); break;
                case "bleeding_heavy": m.Add(StatusKind.BleedingHeavy); break;
                case "fracture_leg": m.Add(StatusKind.FractureLeg); break;
                case "fracture_arm": m.Add(StatusKind.FractureArm); break;
                case "pain": m.Add(StatusKind.Pain, intensity: 1f); break;
                case "infection": m.Add(StatusKind.Infection, intensity: 0.6f); break;
                case "poisoning": m.Add(StatusKind.Poisoning, hours: 1.5f); break;
                case "trauma": m.Add(StatusKind.NearDeathTrauma, hours: VitalsModel.TraumaHours); break;
                case "unrested": m.Add(StatusKind.Unrested, hours: VitalsModel.UnrestedHours); break;
                default: return $"Нет состояния '{kind}'.";
            }
            return $"Добавлено: {kind}";
        }

        [ConsoleCommand("status_clear", "Снять все травмы и болезни.", Flags = ConsoleFlags.Cheat)]
        private static string StatusClear()
        {
            if (!TryGet(out var vitals, out string error))
                return error;
            vitals.Model.Statuses.Clear();
            return "Состояния сняты.";
        }

        [ConsoleCommand("set_need", "Задать показатель: set_need <food|water|rest|sanity|temp> <значение> (food — ккал, water — мл, temp — °C).", Flags = ConsoleFlags.Cheat)]
        private static string SetNeed(string need, float value)
        {
            if (!TryGet(out var vitals, out string error))
                return error;
            var m = vitals.Model;
            switch (need)
            {
                case "food": m.SetNeeds(calories: value); break;
                case "water": m.SetNeeds(water: value); break;
                case "rest": m.SetNeeds(rest: value); break;
                case "sanity": m.SetNeeds(sanity: value); break;
                case "temp": m.SetNeeds(bodyTemperature: value); break;
                default: return $"Нет показателя '{need}'.";
            }
            return $"{need} = {value}";
        }

        [ConsoleCommand("kill", "Умереть (проверка смерти и возрождения).", Flags = ConsoleFlags.Cheat)]
        private static string Kill()
        {
            if (!TryGet(out var vitals, out string error))
                return error;
            vitals.Kill(DeathCause.Damage, "Команда kill");
            return null;
        }

        [ConsoleCommand("respawn", "Возродиться после смерти.")]
        private static string Respawn()
        {
            if (!TryGet(out var vitals, out string error))
                return error;
            if (!vitals.IsDead)
                return "Вы живы.";
            return vitals.Respawn() ? null : $"Подождите {PlayerVitals.RespawnDelay:0} с.";
        }

        private static bool TryGet(out PlayerVitals vitals, out string error)
        {
            vitals = PlayerVitals.Local;
            error = vitals == null ? "Нет игрока с выживанием (запустите полигон мира)." : null;
            return vitals != null;
        }
    }
}
