using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using VoidMall.Skills;

namespace VoidMall.UI
{
    /// <summary>
    /// Вкладка «Навыки» (GDD §5.2): подвкладки аспектов, свободные очки, карточки навыков — уровни, что даёт
    /// следующий, требования и кнопка «Изучить».
    /// </summary>
    internal sealed class SkillsPanel : VisualElement
    {
        private readonly List<Button> _aspectButtons = new();
        private readonly Label _points;
        private readonly ScrollView _cards;
        private Aspect _aspect = Aspect.Physical;
        private PlayerSkills _skills;
        private bool _dirty = true;
        private float _refresh;

        public SkillsPanel()
        {
            style.height = 440f;
            style.width = 870f;
            var tabs = new VisualElement();
            tabs.style.flexDirection = FlexDirection.Row;
            foreach (Aspect aspect in Enum.GetValues(typeof(Aspect)))
            {
                var value = aspect;
                var button = CraftPanel.SmallButton(SkillCatalog.AspectName(aspect), () =>
                {
                    _aspect = value;
                    _dirty = true;
                });
                button.style.marginRight = 4f;
                _aspectButtons.Add(button);
                tabs.Add(button);
            }
            Add(tabs);
            _points = new Label();
            _points.style.marginTop = 8f;
            _points.style.marginBottom = 6f;
            Add(_points);
            _cards = new ScrollView();
            _cards.style.flexGrow = 1f;
            Add(_cards);
        }

        public void Tick()
        {
            if (_skills == null && PlayerSkills.Local != null)
            {
                _skills = PlayerSkills.Local;
                _skills.Tree.Changed += () => _dirty = true;
            }
            if (_skills == null)
            {
                _points.text = "Нет игрока с навыками (полигон мира).";
                return;
            }
            // Дробные очки копятся — обновляем текст раз в полсекунды.
            _refresh -= Time.unscaledDeltaTime;
            if (_refresh <= 0f)
            {
                _refresh = 0.5f;
                UpdatePoints();
            }
            if (!_dirty)
                return;
            _dirty = false;
            Rebuild();
        }

        private void UpdatePoints()
        {
            var tree = _skills.Tree;
            string text = $"Очки ({SkillCatalog.AspectName(_aspect).ToLowerInvariant()}): {Mathf.FloorToInt(tree.Points(_aspect))}"
                          + $"   · заработано всего {tree.Earned(_aspect):0.#}" + PointsHint(_aspect);
            if (_points.text != text)
                _points.text = text;
        }

        private static string PointsHint(Aspect aspect) => aspect switch
        {
            Aspect.Physical => "   · за бег, спринт и переноску тяжёлого",
            Aspect.Mental => "   · за крафт и чтение чертежей",
            Aspect.Construction => "   · за ломание, стройку, гвозди и разбор",
            Aspect.Combat => "   · за бой (этап 11)",
            _ => "   · за еду, лечение, сон, холод и жару, готовку",
        };

        private void Rebuild()
        {
            var tree = _skills.Tree;
            for (int i = 0; i < _aspectButtons.Count; i++)
                _aspectButtons[i].style.backgroundColor = (Aspect)i == _aspect ? new Color(0.3f, 0.34f, 0.42f, 1f) : new Color(0.16f, 0.18f, 0.22f, 1f);
            UpdatePoints();
            _cards.Clear();
            bool any = false;
            foreach (var info in SkillCatalog.All)
            {
                if (info.Aspect != _aspect)
                    continue;
                any = true;
                _cards.Add(Card(info, tree));
            }
            if (!any)
            {
                var empty = new Label("Боевые навыки появятся вместе с боем (этап 11). Очки уже копятся.");
                empty.style.color = ItemVisuals.Muted;
                _cards.Add(empty);
            }
        }

        private VisualElement Card(SkillInfo info, SkillTree tree)
        {
            int level = tree.Level(info.Id);
            var card = new VisualElement();
            card.style.flexDirection = FlexDirection.Row;
            card.style.marginBottom = 6f;
            card.style.paddingLeft = card.style.paddingRight = 10f;
            card.style.paddingTop = card.style.paddingBottom = 6f;
            card.style.backgroundColor = new Color(1f, 1f, 1f, level > 0 ? 0.07f : 0.04f);
            ItemVisuals.Border(card, new Color(1f, 1f, 1f, 0.08f), 1f, 4f);

            var text = new VisualElement();
            text.style.flexGrow = 1f;
            text.style.flexShrink = 1f;
            var title = new Label($"{info.Name}   {Pips(level, info.MaxLevel)}");
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            text.Add(title);
            var description = new Label(info.Description);
            description.style.color = ItemVisuals.Muted;
            description.style.whiteSpace = WhiteSpace.Normal;
            text.Add(description);
            for (int i = 0; i < info.MaxLevel; i++)
            {
                var line = new Label($"{i + 1}: {info.Levels[i]}");
                line.style.fontSize = 12f;
                line.style.whiteSpace = WhiteSpace.Normal;
                line.style.color = i < level ? new Color(0.55f, 0.9f, 0.6f) : i == level ? ItemVisuals.Text : ItemVisuals.Muted;
                text.Add(line);
            }
            if (info.Requires.HasValue)
            {
                var require = new Label($"Требует для уровня {info.RequireFrom}+: {SkillCatalog.Get(info.Requires.Value).Name} {info.RequiresLevel}");
                require.style.fontSize = 11f;
                require.style.color = ItemVisuals.Muted;
                text.Add(require);
            }
            card.Add(text);

            var side = new VisualElement();
            side.style.width = 170f;
            side.style.alignItems = Align.FlexEnd;
            side.style.justifyContent = Justify.Center;
            string why = tree.WhyCannotLearn(info.Id);
            if (level < info.MaxLevel)
            {
                var learn = CraftPanel.SmallButton($"Изучить ({SkillCatalog.Cost(level + 1)} оч.)", () => tree.TryLearn(info.Id));
                learn.SetEnabled(why == null);
                side.Add(learn);
                if (why != null)
                {
                    var reason = new Label(why);
                    reason.style.fontSize = 11f;
                    reason.style.color = new Color(1f, 0.6f, 0.5f);
                    reason.style.whiteSpace = WhiteSpace.Normal;
                    reason.style.unityTextAlign = TextAnchor.MiddleRight;
                    side.Add(reason);
                }
            }
            else
            {
                var done = new Label("Изучен полностью");
                done.style.color = new Color(0.55f, 0.9f, 0.6f);
                side.Add(done);
            }
            card.Add(side);
            return card;
        }

        private static string Pips(int level, int max)
        {
            var chars = new char[max];
            for (int i = 0; i < max; i++)
                chars[i] = i < level ? '●' : '○';
            return new string(chars);
        }
    }
}
