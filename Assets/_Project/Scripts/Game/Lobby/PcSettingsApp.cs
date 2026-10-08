using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using VoidMall.Console;
using VoidMall.Items;
using VoidMall.Player;
using VoidMall.World;

namespace VoidMall.Game
{
    /// <summary>
    /// Настройки (этап 10) — приложение ПК и окно Esc-меню: графика, звук, управление, клавиши. Значения — существующие
    /// CVar'ы (сохраняются в config.cfg), клавиши — KeyBindings.
    /// </summary>
    public static class PcSettingsApp
    {
        public static readonly CVar<float> Volume = new("snd_volume", 1f, "Общая громкость (0–1).", ConsoleFlags.Archive);

        /// <summary>Применить громкость сейчас и при каждом изменении.</summary>
        public static void ApplyVolume()
        {
            AudioListener.volume = Mathf.Clamp01(Volume.Value);
            Volume.Changed -= OnVolumeChanged;
            Volume.Changed += OnVolumeChanged;
        }

        private static void OnVolumeChanged() => AudioListener.volume = Mathf.Clamp01(Volume.Value);

        public static PcApp Create(PcOs os) => new() { Title = "Settings", Width = 660f, Height = 540f, Content = Build() };

        /// <summary>Содержимое настроек (общее для ПК и Esc-меню).</summary>
        public static VisualElement Build()
        {
            var content = new VisualElement();
            content.style.flexGrow = 1f;
            var tabs = new VisualElement();
            tabs.style.flexDirection = FlexDirection.Row;
            tabs.style.marginBottom = 8f;
            var page = new ScrollView();
            page.style.flexGrow = 1f;
            content.Add(tabs);
            content.Add(page);

            (string title, Action<VisualElement> build)[] pages =
            {
                ("Графика", Graphics),
                ("Звук", Sound),
                ("Управление", Controls),
                ("Клавиши", Keys),
            };
            var buttons = new Button[pages.Length];
            void Show(int index)
            {
                page.Clear();
                pages[index].build(page.contentContainer);
                for (int i = 0; i < buttons.Length; i++)
                    buttons[i].style.backgroundColor = i == index ? new Color(0.2f, 0.36f, 0.48f) : PcOs.TitleColor;
            }
            for (int i = 0; i < pages.Length; i++)
            {
                int index = i;
                buttons[i] = PcApps.Button(pages[i].title, () => Show(index));
                buttons[i].style.marginRight = 4f;
                tabs.Add(buttons[i]);
            }
            Show(0);
            return content;
        }

        private static void Graphics(VisualElement page)
        {
            IntCVar(page, "Дальность мира (колонн по 32 м)", ChunkManager.ViewDistance, 3, 12);
            IntCVar(page, "Ламп со светом рядом", LightingDirector.MaxLights, 8, 80);
            IntCVar(page, "Ламп с тенями", LightingDirector.ShadowLights, 0, 8);
            FloatCVar(page, "Дальность товаров на полках, м", ShelfLoot.DrawDistance, 10f, 80f, "0");
            Toggle(page, "Полный экран", Screen.fullScreen, value => Screen.fullScreen = value);
            Toggle(page, "Вертикальная синхронизация", QualitySettings.vSyncCount > 0, value => QualitySettings.vSyncCount = value ? 1 : 0);
        }

        private static void Sound(VisualElement page)
        {
            FloatCVar(page, "Общая громкость", Volume, 0f, 1f, "0.00");
        }

        private static void Controls(VisualElement page)
        {
            FloatCVar(page, "Чувствительность мыши", PlayerInputReader.MouseSensitivity, 0.01f, 0.3f, "0.000");
            FloatCVar(page, "Чувствительность стика, °/с", PlayerInputReader.StickSensitivity, 60f, 480f, "0");
            FloatCVar(page, "Угол обзора (FOV)", PlayerCameraRig.Fov, 60f, 110f, "0");
            BoolCVar(page, "Инвертировать Y", PlayerInputReader.InvertY);
            BoolCVar(page, "Покачивание камеры", PlayerCameraRig.ViewBob);
            BoolCVar(page, "Присед переключением", PlayerInputReader.CrouchToggle);
        }

        // ───────────────────────────── Клавиши ─────────────────────────────

        private static void Keys(VisualElement page)
        {
            var asset = InputSystem.actions;
            var status = PcApps.Muted("Нажмите на клавишу действия, затем новую клавишу. Esc — отмена.");
            status.style.whiteSpace = WhiteSpace.Normal;
            status.style.marginBottom = 6f;
            page.Add(status);
            var group = KeyBindings.KeyboardGroup;
            var list = new VisualElement();
            var switcher = new VisualElement();
            switcher.style.flexDirection = FlexDirection.Row;
            switcher.style.marginBottom = 6f;
            void Fill()
            {
                list.Clear();
                foreach (var (action, index, caption) in KeyBindings.Rows(asset, group))
                    list.Add(KeyRow(asset, action, index, caption, status, Fill));
            }
            switcher.Add(PcApps.Button("Клавиатура и мышь", () => { group = KeyBindings.KeyboardGroup; Fill(); }));
            switcher.Add(PcApps.Button("Геймпад", () => { group = KeyBindings.GamepadGroup; Fill(); }));
            switcher.Add(PcApps.Button("Сбросить всё", () =>
            {
                KeyBindings.ResetAll(asset);
                status.text = "Клавиши — по умолчанию.";
                Fill();
            }));
            page.Add(switcher);
            page.Add(list);
            Fill();
        }

        private static VisualElement KeyRow(InputActionAsset asset, InputAction action, int index, string caption, Label status, Action refresh)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 2f;
            var name = new Label(caption);
            name.style.flexGrow = 1f;
            row.Add(name);
            Button key = null;
            key = PcApps.Button(KeyBindings.Display(action, index), () =>
            {
                key.text = "…нажмите клавишу";
                bool wasEnabled = action.enabled;
                action.Disable();
                action.PerformInteractiveRebinding(index)
                    .WithControlsExcluding("<Mouse>/position")
                    .WithControlsExcluding("<Mouse>/delta")
                    .WithCancelingThrough("<Keyboard>/escape")
                    .OnComplete(operation =>
                    {
                        operation.Dispose();
                        if (wasEnabled)
                            action.Enable();
                        KeyBindings.Save(asset);
                        status.text = $"{caption}: {KeyBindings.Display(action, index)}";
                        refresh();
                    })
                    .OnCancel(operation =>
                    {
                        operation.Dispose();
                        if (wasEnabled)
                            action.Enable();
                        refresh();
                    })
                    .Start();
            });
            key.style.width = 200f;
            row.Add(key);
            return row;
        }

        // ───────────────────────────── Строки ─────────────────────────────

        private static void FloatCVar(VisualElement page, string caption, CVar<float> cvar, float min, float max, string format)
        {
            var slider = PcApps.SliderRow(page, caption, min, max, cvar.Value, format);
            slider.RegisterValueChangedCallback(e => cvar.Value = e.newValue);
        }

        private static void IntCVar(VisualElement page, string caption, CVar<int> cvar, int min, int max)
        {
            var slider = PcApps.SliderRow(page, caption, min, max, cvar.Value, "0");
            slider.RegisterValueChangedCallback(e => cvar.Value = Mathf.RoundToInt(e.newValue));
        }

        private static void BoolCVar(VisualElement page, string caption, CVar<bool> cvar) =>
            Toggle(page, caption, cvar.Value, value => cvar.Value = value);

        private static void Toggle(VisualElement page, string caption, bool value, Action<bool> set)
        {
            var toggle = new Toggle(caption) { value = value };
            toggle.style.marginBottom = 4f;
            toggle.RegisterValueChangedCallback(e => set(e.newValue));
            page.Add(toggle);
        }
    }
}
