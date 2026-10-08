using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using VoidMall.Console;
using VoidMall.UI;
using VoidMall.World;

namespace VoidMall.Game
{
    /// <summary>
    /// Приложения ОС ПК (этап 10): генератор миров (в лобби), сейвы, консоль, окно загрузки мира; меню «Пуск».
    /// Настройки и клавиши — PcSettingsApp.
    /// </summary>
    public static class PcApps
    {
        public const string Generator = "generator", Saves = "saves", Cmd = "cmd", Loading = "loading", Settings = "settings";

        public static IEnumerable<(string id, string glyph, string title)> AppsFor(bool inWorld)
        {
            if (!inWorld)
                yield return (Generator, "[*]", "World Generator");
            yield return (Saves, "[=]", "Saves");
            yield return (Settings, "[~]", "Settings");
            yield return (Cmd, ">_", "cmd");
        }

        public static IEnumerable<(string title, Action action)> StartMenu(PcOs os)
        {
            yield return ("Закрыть ПК (Esc)", () => os.Terminal.Leave());
            if (os.Terminal.InWorld)
            {
                yield return ("Сохранить и в лобби", () => SaveSystem.Instance?.ReturnToLobby());
                yield return ("Сохранить и выйти", Quit);
            }
            else
            {
                yield return ("Выключить игру", Quit);
            }
        }

        public static void Quit()
        {
            SaveSystem.Instance?.Save(wait: true, quiet: true);
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        public static PcApp Create(string id, PcOs os) => id switch
        {
            Generator => WorldGenerator(os),
            Saves => SavesApp(os),
            Cmd => CmdApp(),
            Loading => LoadingApp(os),
            Settings => PcSettingsApp.Create(os),
            _ => null,
        };

        // ───────────────────────────── World Generator ─────────────────────────────

        private static PcApp WorldGenerator(PcOs os)
        {
            var world = ChunkManager.Instance;
            var defaults = SaveSystem.DefaultMeta(world);
            var content = new VisualElement();
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            var left = new VisualElement();
            left.style.width = 330f;
            var right = new VisualElement();
            right.style.flexGrow = 1f;
            right.style.alignItems = Align.Center;
            row.Add(left);
            row.Add(right);
            content.Add(row);

            var name = TextInput(left, "Имя мира", NextWorldName());
            var seedRow = new VisualElement();
            seedRow.style.flexDirection = FlexDirection.Row;
            seedRow.style.alignItems = Align.FlexEnd;
            var seed = TextInput(seedRow, "Сид (пусто — случайный)", "");
            seed.parent.style.flexGrow = 1f;
            seedRow.Add(Button("Случайный", () => seed.value = UnityEngine.Random.Range(1, int.MaxValue).ToString()));
            left.Add(seedRow);
            var loot = SliderRow(left, "Количество лута", 0f, 3f, defaults.LootAmount, "0.0");
            var rarity = SliderRow(left, "Редкость лута", 0f, 3f, defaults.LootRarity, "0.0");
            var anomalies = SliderRow(left, "Частота аномалий", 0f, 3f, defaults.AnomalyFrequency, "0.0");
            var enemies = SliderRow(left, "Плотность врагов", 0f, 3f, defaults.EnemyDensity, "0.0");
            var day = SliderRow(left, "Сутки, минут", 10f, 180f, defaults.DayLengthMinutes, "0");

            var preview = new Image { scaleMode = ScaleMode.ScaleToFit };
            preview.style.width = preview.style.height = 190f;
            preview.style.backgroundColor = PcOs.Back;
            PcOs.Border(preview, PcOs.Accent * 0.4f, 1f, 2f);
            right.Add(preview);
            var previewCaption = Muted("Превью: торговый этаж у входа");
            previewCaption.style.marginTop = 4f;
            right.Add(previewCaption);
            Texture2D previewTexture = null;
            right.Add(Button("Generate (превью)", () =>
            {
                if (world == null || world.Sections == null)
                    return;
                uint s = ParseSeed(seed.value, out bool random);
                if (random)
                    seed.value = s.ToString();
                var cell = MallPlanner.SpawnCell(s, anomalies.value);
                previewTexture = LayoutPreview.Render(s, anomalies.value, world.Sections, cell, 2, MallLayout.FirstRetailLevel, previewTexture);
                preview.image = previewTexture;
            }));

            var error = new Label();
            error.style.color = PcOs.Danger;
            error.style.marginTop = 6f;
            error.style.whiteSpace = WhiteSpace.Normal;
            content.Add(error);
            var create = Button("Создать мир и открыть портал", () =>
            {
                uint s = ParseSeed(seed.value, out _);
                var meta = SaveSystem.DefaultMeta(world);
                meta.LootAmount = loot.value;
                meta.LootRarity = rarity.value;
                meta.AnomalyFrequency = anomalies.value;
                meta.EnemyDensity = enemies.value;
                meta.DayLengthMinutes = Mathf.Round(day.value);
                string refusal = GameFlow.Instance != null ? GameFlow.Instance.CreateWorld(name.value.Trim(), s, meta) : "Нет потока игры";
                if (refusal != null)
                {
                    error.text = refusal;
                    return;
                }
                os.CloseApp(Generator);
                os.OpenApp(Loading);
            }, accent: true);
            create.style.marginTop = 10f;
            content.Add(create);
            return new PcApp
            {
                Title = "World Generator",
                Width = 600f,
                Height = 520f,
                Content = content,
                Closed = () =>
                {
                    if (previewTexture != null)
                        UnityEngine.Object.Destroy(previewTexture);
                },
            };
        }

        private static uint ParseSeed(string text, out bool random)
        {
            random = string.IsNullOrWhiteSpace(text);
            if (random)
                return (uint)UnityEngine.Random.Range(1, int.MaxValue);
            return uint.TryParse(text.Trim(), out uint seed) ? seed : (uint)text.Trim().GetHashCode();
        }

        private static string NextWorldName()
        {
            for (int i = 1; ; i++)
                if (!SaveSystem.Exists($"Мир {i}"))
                    return $"Мир {i}";
        }

        // ───────────────────────────── Saves ─────────────────────────────

        private static PcApp SavesApp(PcOs os)
        {
            var content = new VisualElement();
            var status = new Label();
            status.style.color = PcOs.Muted;
            status.style.whiteSpace = WhiteSpace.Normal;
            var list = new ScrollView();
            list.style.flexGrow = 1f;
            content.Add(list);
            content.Add(status);

            void Refresh()
            {
                list.Clear();
                var worlds = SaveSystem.ListWorlds();
                if (worlds.Count == 0)
                    list.Add(Muted(os.Terminal.InWorld ? "Сейвов нет." : "Сейвов нет — создайте мир в World Generator."));
                foreach (var (worldName, meta) in worlds)
                    list.Add(SaveRow(os, worldName, meta, status, Refresh));
            }

            Refresh();
            return new PcApp { Title = "Saves", Width = 640f, Height = 460f, Content = content };
        }

        private static VisualElement SaveRow(PcOs os, string name, Game.WorldMeta meta, Label status, Action refresh)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.paddingTop = row.style.paddingBottom = 6f;
            row.style.borderBottomWidth = 1f;
            row.style.borderBottomColor = PcOs.TitleColor;
            var info = new VisualElement();
            info.style.flexGrow = 1f;
            var title = new Label(name + (SaveSystem.Instance != null && SaveSystem.Instance.WorldName == name ? "  (текущий)" : ""));
            title.style.color = PcOs.Accent;
            info.Add(title);
            info.Add(Muted(meta == null ? "повреждён"
                : $"сид {meta.Seed} · день {Mathf.FloorToInt((float)(meta.TotalHours / 24.0)) + 1} · в игре {meta.PlaySeconds / 3600f:0.0} ч · {SavedAt(meta.SavedUtc)}"));
            row.Add(info);

            row.Add(Button("Загрузить", () =>
            {
                string refusal = LoadWorld(os, name);
                if (refusal != null)
                    status.text = refusal;
            }, accent: true));
            row.Add(Button("Имя", () =>
            {
                info.Clear();
                var field = new TextField { value = name };
                field.style.width = 220f;
                StyleInput(field);
                info.Add(field);
                info.Add(Button("OK", () =>
                {
                    status.text = SaveSystem.Rename(name, field.value) ?? $"Переименован: {field.value}";
                    refresh();
                }));
            }));
            bool confirm = false;
            Button delete = null;
            delete = Button("Удалить", () =>
            {
                if (!confirm)
                {
                    confirm = true;
                    delete.text = "Точно?";
                    delete.style.color = PcOs.Danger;
                    return;
                }
                status.text = SaveSystem.Delete(name) ?? $"Мир «{name}» удалён.";
                refresh();
            });
            row.Add(delete);
            return row;
        }

        private static string LoadWorld(PcOs os, string name)
        {
            if (SaveSystem.Instance != null && SaveSystem.Instance.WorldName == name)
                return "Этот мир уже открыт.";
            if (os.Terminal.InWorld || GameFlow.Instance == null || GameFlow.Instance.State != FlowState.Lobby)
            {
                // Из мира: сохранить текущий и перейти в другой (сцена перезагружается).
                DevConsole.Execute($"world_load \"{name}\"");
                return null;
            }
            string refusal = GameFlow.Instance.LoadWorld(name);
            if (refusal == null)
            {
                os.CloseApp(Saves);
                os.OpenApp(Loading);
            }
            return refusal;
        }

        private static string SavedAt(string utc) =>
            DateTime.TryParse(utc, null, System.Globalization.DateTimeStyles.RoundtripKind, out var time) ? time.ToLocalTime().ToString("dd.MM.yyyy HH:mm") : "не сохранялся";

        // ───────────────────────────── cmd, загрузка ─────────────────────────────

        private static PcApp CmdApp()
        {
            var console = new ConsoleView();
            console.style.flexGrow = 1f;
            console.schedule.Execute(console.FocusInput).StartingIn(50);
            return new PcApp { Title = "cmd", Width = 720f, Height = 440f, Content = console };
        }

        private static PcApp LoadingApp(PcOs os)
        {
            var content = new VisualElement();
            var status = new Label("Подготовка мира…");
            status.style.whiteSpace = WhiteSpace.Normal;
            status.style.marginBottom = 8f;
            content.Add(status);

            // Карта зоны портала: колонны проявляются по мере готовности (LoadingMap).
            var map = new LoadingMap();
            content.Add(map.Element);

            // Тонкая полоска до открытия портала и проценты.
            var barRow = new VisualElement();
            barRow.style.flexDirection = FlexDirection.Row;
            barRow.style.alignItems = Align.Center;
            barRow.style.marginTop = 8f;
            var track = new VisualElement();
            track.style.flexGrow = 1f;
            track.style.height = 6f;
            track.style.backgroundColor = PcOs.Back;
            PcOs.Border(track, PcOs.Accent * 0.35f, 1f, 3f);
            var fill = new VisualElement();
            fill.style.height = Length.Percent(100);
            fill.style.width = Length.Percent(0);
            fill.style.backgroundColor = PcOs.Accent;
            track.Add(fill);
            var percent = new Label("0%");
            percent.style.width = 48f;
            percent.style.unityTextAlign = TextAnchor.MiddleRight;
            barRow.Add(track);
            barRow.Add(percent);
            content.Add(barRow);

            var legend = new VisualElement();
            legend.style.flexDirection = FlexDirection.Row;
            legend.style.justifyContent = Justify.Center;
            legend.style.alignItems = Align.Center;
            legend.style.marginTop = 6f;
            foreach (var (caption, color) in LoadingMap.Legend)
            {
                var swatch = new VisualElement();
                swatch.style.width = swatch.style.height = 10f;
                swatch.style.backgroundColor = color;
                swatch.style.marginLeft = 10f;
                swatch.style.marginRight = 4f;
                PcOs.Border(swatch, PcOs.Accent * 0.3f, 1f, 1f);
                legend.Add(swatch);
                legend.Add(Muted(caption));
            }
            legend.Add(Muted("  ▪ — портал"));
            content.Add(legend);

            var hint = Muted("");
            hint.style.marginTop = 8f;
            hint.style.whiteSpace = WhiteSpace.Normal;
            content.Add(hint);
            var leave = Button("Выйти из ПК", () => os.Terminal.Leave(), accent: true);
            leave.style.marginTop = 8f;
            leave.style.display = DisplayStyle.None;
            content.Add(leave);
            return new PcApp
            {
                Title = "Загрузка мира",
                Width = 440f,
                Height = 600f,
                Content = content,
                Closed = map.Dispose,
                Tick = () =>
                {
                    var flow = GameFlow.Instance;
                    if (flow == null)
                        return;
                    map.Tick(ChunkManager.Instance, flow.LoadCenter);
                    fill.style.width = Length.Percent(flow.Progress * 100f);
                    percent.text = $"{flow.Progress * 100f:0}%";
                    bool ready = flow.State is FlowState.Ready or FlowState.InWorld;
                    status.text = ready ? $"Мир «{flow.WorldName}» готов. Портал открыт." : $"Генерация мира «{flow.WorldName}» вокруг портала…";
                    hint.text = ready ? "Пройдите через арку за столом." : "Мир строится вокруг точки выхода портала.";
                    leave.style.display = ready ? DisplayStyle.Flex : DisplayStyle.None;
                },
            };
        }

        // ───────────────────────────── Элементы ─────────────────────────────

        public static Button Button(string text, Action action, bool accent = false)
        {
            var button = new Button(action) { text = text };
            button.style.backgroundColor = accent ? new Color(0.2f, 0.36f, 0.48f) : PcOs.TitleColor;
            button.style.color = PcOs.TextColor;
            button.style.unityFontDefinition = FontDefinition.FromFont(PcOs.MonoFont);
            PcOs.Border(button, PcOs.Accent * (accent ? 0.8f : 0.4f), 1f, 3f);
            return button;
        }

        public static Label Muted(string text)
        {
            var label = new Label(text);
            label.style.color = PcOs.Muted;
            label.style.fontSize = 12f;
            return label;
        }

        public static TextField TextInput(VisualElement parent, string caption, string value)
        {
            var wrap = new VisualElement();
            wrap.style.marginBottom = 6f;
            wrap.Add(Muted(caption));
            var field = new TextField { value = value };
            field.style.unityFontDefinition = FontDefinition.FromFont(PcOs.MonoFont);
            StyleInput(field);
            wrap.Add(field);
            parent.Add(wrap);
            return field;
        }

        /// <summary>Поле ввода в тёмной теме ОС (по умолчанию — белое со светлым текстом).</summary>
        public static void StyleInput(TextField field)
        {
            var input = field.Q(className: TextField.inputUssClassName);
            if (input == null)
                return;
            input.style.backgroundColor = new Color(0.05f, 0.07f, 0.09f);
            input.style.color = PcOs.TextColor;
            PcOs.Border(input, PcOs.Accent * 0.45f, 1f, 2f);
        }

        public static Slider SliderRow(VisualElement parent, string caption, float min, float max, float value, string format)
        {
            var wrap = new VisualElement();
            wrap.style.marginBottom = 4f;
            var head = new VisualElement();
            head.style.flexDirection = FlexDirection.Row;
            var label = Muted(caption);
            label.style.flexGrow = 1f;
            var number = new Label(value.ToString(format));
            number.style.color = PcOs.Accent;
            number.style.fontSize = 12f;
            head.Add(label);
            head.Add(number);
            var slider = new Slider(min, max) { value = value };
            slider.RegisterValueChangedCallback(e => number.text = e.newValue.ToString(format));
            wrap.Add(head);
            wrap.Add(slider);
            parent.Add(wrap);
            return slider;
        }
    }
}
