using System;
using System.Text;
using VoidMall.Console;

namespace VoidMall.Skills
{
    internal static class SkillCommands
    {
        private static readonly string[] AspectNames = { "physical", "mental", "construction", "combat", "survival" };

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterCompleters()
        {
            DevConsole.RegisterCompleter("skill_points", token => token == 0 ? AspectNames : token == 1 ? new[] { "5", "10", "50" } : null);
            DevConsole.RegisterCompleter("skill_set", token => token == 0 ? Enum.GetNames(typeof(SkillId)) : token == 1 ? new[] { "0", "1", "2", "3" } : null);
        }

        [ConsoleCommand("skills", "Очки аспектов и уровни навыков.")]
        private static string Skills()
        {
            if (!TryGet(out var skills, out string error))
                return error;
            var tree = skills.Tree;
            var sb = new StringBuilder();
            foreach (Aspect aspect in Enum.GetValues(typeof(Aspect)))
            {
                sb.Append($"{SkillCatalog.AspectName(aspect)}: {tree.Points(aspect):0.0} очков\n");
                foreach (var info in SkillCatalog.All)
                    if (info.Aspect == aspect)
                        sb.Append($"  {info.Name} {tree.Level(info.Id)}/{info.MaxLevel}\n");
            }
            return sb.ToString().TrimEnd();
        }

        [ConsoleCommand("skill_points", "Выдать очки: skill_points <physical|mental|construction|combat|survival> <сколько>.", Flags = ConsoleFlags.Cheat)]
        private static string SkillPoints(string aspect, float amount)
        {
            if (!TryGet(out var skills, out string error))
                return error;
            int index = Array.IndexOf(AspectNames, aspect);
            if (index < 0)
                return $"Нет аспекта '{aspect}'.";
            skills.Tree.Award((Aspect)index, amount);
            return $"{SkillCatalog.AspectName((Aspect)index)}: {skills.Tree.Points((Aspect)index):0.0} очков";
        }

        [ConsoleCommand("skill_set", "Выставить уровень навыка без очков: skill_set <навык> <уровень>.", Flags = ConsoleFlags.Cheat)]
        private static string SkillSet(string skill, int level)
        {
            if (!TryGet(out var skills, out string error))
                return error;
            if (!Enum.TryParse(skill, true, out SkillId id))
                return $"Нет навыка '{skill}'.";
            skills.Tree.SetLevel(id, level);
            return $"{SkillCatalog.Get(id).Name}: {skills.Tree.Level(id)}";
        }

        [ConsoleCommand("skills_reset", "Сбросить навыки (очки вернутся).", Flags = ConsoleFlags.Cheat)]
        private static string SkillsReset()
        {
            if (!TryGet(out var skills, out string error))
                return error;
            skills.Tree.Reset();
            return "Навыки сброшены.";
        }

        private static bool TryGet(out PlayerSkills skills, out string error)
        {
            skills = PlayerSkills.Local;
            error = skills == null ? "Нет игрока с навыками (запустите полигон мира)." : null;
            return skills != null;
        }
    }
}
