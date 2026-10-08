using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using VoidMall.Core;
using VoidMall.Survival;

namespace VoidMall.UI
{
    /// <summary>
    /// HUD выживания: полоски здоровья, стамины, сытости, воды, бодрости и рассудка, плашки состояний, температура;
    /// экран сна (E — проснуться), экран смерти (причина, возрождение через 3 с — E или кнопка), метка сумки с вещами.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class VitalsHud : MonoBehaviour
    {
        private static readonly Color Bad = new(0.95f, 0.35f, 0.32f);
        private static readonly Color Good = new(0.45f, 0.8f, 0.5f);

        private VisualElement _root;
        private VisualElement _panel;
        private readonly Bar[] _bars = new Bar[7];
        private Label _temperature;
        private VisualElement _chips;
        private VisualElement _sleep;
        private Label _sleepText;
        private VisualElement _death;
        private Label _deathReason;
        private Button _respawn;
        private Label _bagMarker;

        private readonly List<StatusKind> _conditions = new();
        private readonly List<StatusKind> _shown = new();
        private PlayerVitals _vitals;

        private sealed class Bar
        {
            public VisualElement Fill;
            public Label Value;
        }

        public void Initialize()
        {
            _root = GetComponent<UIDocument>().rootVisualElement;
            DevToolsStyle.FullScreenRoot(_root);

            _panel = new VisualElement { pickingMode = PickingMode.Ignore };
            _panel.style.position = Position.Absolute;
            // Справа внизу: слева внизу — отладочный HUD (F3).
            _panel.style.right = 14f;
            _panel.style.bottom = 14f;
            _panel.style.width = 230f;
            _root.Add(_panel);

            _chips = new VisualElement { pickingMode = PickingMode.Ignore };
            _chips.style.flexDirection = FlexDirection.Row;
            _chips.style.flexWrap = Wrap.Wrap;
            _chips.style.justifyContent = Justify.FlexEnd;
            _chips.style.marginBottom = 4f;
            _panel.Add(_chips);

            _bars[0] = AddBar("Здоровье", new Color(0.85f, 0.25f, 0.25f));
            _bars[1] = AddBar("Стамина", new Color(0.95f, 0.85f, 0.35f));
            _bars[2] = AddBar("Сытость", new Color(0.9f, 0.6f, 0.3f));
            _bars[3] = AddBar("Вода", new Color(0.35f, 0.6f, 0.95f));
            _bars[4] = AddBar("Бодрость", new Color(0.65f, 0.55f, 0.9f));
            _bars[5] = AddBar("Рассудок", new Color(0.5f, 0.85f, 0.85f));
            _bars[6] = AddBar("Воздух", new Color(0.75f, 0.92f, 1f));

            _temperature = new Label { pickingMode = PickingMode.Ignore };
            _temperature.style.fontSize = 11f;
            _temperature.style.color = ItemVisuals.Muted;
            _panel.Add(_temperature);

            BuildSleep();
            BuildDeath();

            _bagMarker = new Label("▼ ваши вещи") { pickingMode = PickingMode.Ignore };
            _bagMarker.style.position = Position.Absolute;
            _bagMarker.style.color = ItemVisuals.Highlight;
            _bagMarker.style.fontSize = 12f;
            _bagMarker.style.unityFontStyleAndWeight = FontStyle.Bold;
            _bagMarker.style.display = DisplayStyle.None;
            _root.Add(_bagMarker);

            _root.style.display = DisplayStyle.None;
        }

        private Bar AddBar(string name, Color color)
        {
            var row = new VisualElement { pickingMode = PickingMode.Ignore };
            row.style.marginBottom = 3f;
            var back = new VisualElement { pickingMode = PickingMode.Ignore };
            back.style.height = 14f;
            back.style.backgroundColor = new Color(0f, 0f, 0f, 0.5f);
            ItemVisuals.Border(back, new Color(1f, 1f, 1f, 0.12f), 1f, 3f);
            var fill = new VisualElement { pickingMode = PickingMode.Ignore };
            fill.style.position = Position.Absolute;
            fill.style.left = fill.style.top = fill.style.bottom = 0f;
            color.a = 0.8f;
            fill.style.backgroundColor = color;
            back.Add(fill);
            var label = new Label(name) { pickingMode = PickingMode.Ignore };
            label.style.position = Position.Absolute;
            label.style.left = 5f;
            label.style.top = -1f;
            label.style.fontSize = 10f;
            label.style.color = Color.white;
            back.Add(label);
            var value = new Label { pickingMode = PickingMode.Ignore };
            value.style.position = Position.Absolute;
            value.style.right = 5f;
            value.style.top = -1f;
            value.style.fontSize = 10f;
            value.style.color = Color.white;
            back.Add(value);
            row.Add(back);
            _panel.Add(row);
            return new Bar { Fill = fill, Value = value };
        }

        private VisualElement Overlay(Color color)
        {
            var overlay = new VisualElement();
            overlay.style.position = Position.Absolute;
            overlay.style.left = overlay.style.right = overlay.style.top = overlay.style.bottom = 0f;
            overlay.style.backgroundColor = color;
            overlay.style.alignItems = Align.Center;
            overlay.style.justifyContent = Justify.Center;
            overlay.style.display = DisplayStyle.None;
            _root.Add(overlay);
            return overlay;
        }

        private void BuildSleep()
        {
            _sleep = Overlay(new Color(0f, 0f, 0f, 0.85f));
            _sleep.pickingMode = PickingMode.Ignore;
            _sleepText = new Label { pickingMode = PickingMode.Ignore };
            _sleepText.style.color = ItemVisuals.Text;
            _sleepText.style.fontSize = 18f;
            _sleepText.style.unityTextAlign = TextAnchor.MiddleCenter;
            _sleep.Add(_sleepText);
        }

        private void BuildDeath()
        {
            _death = Overlay(new Color(0.12f, 0f, 0f, 0.7f));
            var title = new Label("ВЫ ПОГИБЛИ");
            title.style.color = new Color(1f, 0.85f, 0.85f);
            title.style.fontSize = 34f;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            _death.Add(title);
            _deathReason = new Label();
            _deathReason.style.color = ItemVisuals.Text;
            _deathReason.style.fontSize = 16f;
            _deathReason.style.marginTop = 6f;
            _deathReason.style.unityTextAlign = TextAnchor.MiddleCenter;
            _deathReason.style.whiteSpace = WhiteSpace.Normal;
            _deathReason.style.maxWidth = 560f;
            _death.Add(_deathReason);
            _respawn = new Button(() => _vitals?.Respawn());
            _respawn.style.marginTop = 18f;
            _respawn.style.fontSize = 16f;
            _respawn.style.paddingLeft = _respawn.style.paddingRight = 18f;
            _respawn.style.paddingTop = _respawn.style.paddingBottom = 6f;
            _respawn.style.backgroundColor = new Color(0.25f, 0.12f, 0.12f, 1f);
            _respawn.style.color = ItemVisuals.Text;
            ItemVisuals.Border(_respawn, new Color(1f, 1f, 1f, 0.25f), 1f, 4f);
            _death.Add(_respawn);
        }

        private static readonly Perf.Probe UpdateProbe = Perf.Register("UI.Vitals");

        private void Update()
        {
            using var perf = Perf.Measure(UpdateProbe);
            _vitals = PlayerVitals.Local;
            if (_vitals == null || !GameHud.Visible)
            {
                _root.style.display = DisplayStyle.None;
                return;
            }
            _root.style.display = DisplayStyle.Flex;
            var m = _vitals.Model;

            SetBar(_bars[0], m.Health / VitalsModel.BaseMaxHealth, $"{m.Health:0}/{m.MaxHealth:0}");
            SetBar(_bars[1], m.Stamina / VitalsModel.BaseMaxStamina, $"{m.Stamina:0}");
            SetBar(_bars[2], m.CaloriesFraction, $"{m.Calories:0} ккал");
            SetBar(_bars[3], m.WaterFraction, $"{m.Water:0} мл");
            SetBar(_bars[4], m.Rest / 100f, $"{m.Rest:0}%");
            SetBar(_bars[5], m.Sanity / 100f, $"{m.Sanity:0}%");
            // Воздух — только под водой (и пока не восстановился).
            bool airShown = m.Air < VitalsModel.MaxAir - 0.01f;
            _bars[6].Fill.parent.parent.style.display = airShown ? DisplayStyle.Flex : DisplayStyle.None;
            if (airShown)
                SetBar(_bars[6], m.Air / VitalsModel.MaxAir, $"{m.Air:0} с");
            var env = _vitals.Environment;
            SetText(_temperature, $"Тело {m.BodyTemperature:0.0} °C · воздух {env.AirTemperature:0} °C{(env.Warmth > 0f ? $" (+{env.Warmth:0} одежда)" : "")}");
            UpdateChips(m);

            // Сон.
            bool sleeping = _vitals.Sleeping;
            _sleep.style.display = sleeping ? DisplayStyle.Flex : DisplayStyle.None;
            if (sleeping)
            {
                string floor = _vitals.SleepingOnFloor ? $" на полу (выспаться можно до {VitalsModel.FloorRestCap:0}%)" : "";
                SetText(_sleepText, $"Вы спите{floor}… бодрость {m.Rest:0}%\n\nE — проснуться");
                if (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
                    _vitals.StopSleep("Вы проснулись");
            }

            // Смерть.
            bool dead = _vitals.IsDead;
            _death.style.display = dead ? DisplayStyle.Flex : DisplayStyle.None;
            if (dead)
            {
                SetText(_deathReason, $"{m.DeathReason}.\n{DropRule()}");
                float wait = PlayerVitals.RespawnDelay - (Time.unscaledTime - _vitals.DiedAt);
                _respawn.text = _vitals.CanRespawn ? (_vitals.HasSpawnPoint ? "Возродиться у кровати (E)" : "Возродиться (E)") : $"Возрождение через {Mathf.CeilToInt(wait)}…";
                _respawn.SetEnabled(_vitals.CanRespawn);
                if (_vitals.CanRespawn && Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
                    _vitals.Respawn();
            }

            UpdateBagMarker();
        }

        private string DropRule()
        {
            if (!_vitals.HasLostBag)
                return "Вещи остались при вас.";
            return "Вещи остались в сумке на месте гибели — она отмечена на экране.";
        }

        private static void SetBar(Bar bar, float fraction, string value)
        {
            bar.Fill.style.width = Length.Percent(Mathf.Clamp01(fraction) * 100f);
            SetText(bar.Value, value);
        }

        private static void SetText(Label label, string text)
        {
            if (label.text != text)
                label.text = text;
        }

        private void UpdateChips(VitalsModel m)
        {
            m.GetConditions(_conditions);
            bool same = _conditions.Count == _shown.Count;
            for (int i = 0; same && i < _conditions.Count; i++)
                same = _conditions[i] == _shown[i];
            if (same)
                return;
            _shown.Clear();
            _shown.AddRange(_conditions);
            _chips.Clear();
            foreach (var kind in _conditions)
            {
                var info = StatusCatalog.Get(kind);
                var chip = new Label(info.Name) { pickingMode = PickingMode.Ignore };
                chip.style.fontSize = 10f;
                chip.style.color = Color.white;
                chip.style.paddingLeft = chip.style.paddingRight = 5f;
                chip.style.paddingTop = chip.style.paddingBottom = 1f;
                chip.style.marginRight = 3f;
                chip.style.marginBottom = 3f;
                var color = info.Positive ? Good : Bad;
                color.a = 0.75f;
                chip.style.backgroundColor = color;
                ItemVisuals.Border(chip, new Color(0f, 0f, 0f, 0.3f), 1f, 3f);
                _chips.Add(chip);
            }
        }

        private void UpdateBagMarker()
        {
            var camera = Camera.main;
            bool show = _vitals.HasLostBag && !_vitals.IsDead && camera != null && _root.panel != null;
            if (show)
            {
                Vector3 world = _vitals.LostBagPosition + Vector3.up * 0.6f;
                Vector3 toBag = world - camera.transform.position;
                show = Vector3.Dot(toBag, camera.transform.forward) > 0f;
                if (show)
                {
                    Vector2 point = RuntimePanelUtils.CameraTransformWorldToPanel(_root.panel, world, camera);
                    _bagMarker.style.left = point.x - 40f;
                    _bagMarker.style.top = point.y - 16f;
                    SetText(_bagMarker, $"▼ ваши вещи · {toBag.magnitude:0} м");
                }
            }
            _bagMarker.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
