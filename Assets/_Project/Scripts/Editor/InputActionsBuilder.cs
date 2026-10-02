using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

namespace VoidMall.Editor
{
    /// <summary>
    /// Генерирует ассет действий ввода проекта. После генерации ассет можно править в редакторе Input Actions —
    /// повторный запуск перезапишет его.
    /// </summary>
    public static class InputActionsBuilder
    {
        public const string AssetPath = "Assets/_Project/Settings/Input/VoidMallInput.inputactions";

        private const string Kbm = "KeyboardMouse";
        private const string Pad = "Gamepad";

        public static InputActionAsset BuildAndSave()
        {
            var asset = ScriptableObject.CreateInstance<InputActionAsset>();
            asset.name = Path.GetFileNameWithoutExtension(AssetPath);
            asset.AddControlScheme(Kbm).WithRequiredDevice("<Keyboard>").WithRequiredDevice("<Mouse>");
            asset.AddControlScheme(Pad).WithRequiredDevice("<Gamepad>");

            BuildPlayer(asset.AddActionMap("Player"));
            BuildGlobal(asset.AddActionMap("Global"));
            BuildTerminal(asset.AddActionMap("Terminal"));
            BuildUI(asset.AddActionMap("UI"));

            Directory.CreateDirectory(Path.GetDirectoryName(AssetPath)!);
            File.WriteAllText(AssetPath, asset.ToJson());
            Object.DestroyImmediate(asset);
            AssetDatabase.ImportAsset(AssetPath, ImportAssetOptions.ForceUpdate);
            return AssetDatabase.LoadAssetAtPath<InputActionAsset>(AssetPath);
        }

        private static void BuildPlayer(InputActionMap map)
        {
            var move = map.AddAction("Move", InputActionType.Value, expectedControlLayout: "Vector2");
            move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w", Kbm)
                .With("Down", "<Keyboard>/s", Kbm)
                .With("Left", "<Keyboard>/a", Kbm)
                .With("Right", "<Keyboard>/d", Kbm);
            move.AddBinding("<Gamepad>/leftStick", groups: Pad);

            map.AddAction("LookMouse", InputActionType.Value, expectedControlLayout: "Vector2")
                .AddBinding("<Mouse>/delta", groups: Kbm);
            map.AddAction("LookStick", InputActionType.Value, expectedControlLayout: "Vector2")
                .AddBinding("<Gamepad>/rightStick", groups: Pad);

            Button(map, "Jump", ("<Keyboard>/space", Kbm), ("<Gamepad>/buttonSouth", Pad));
            Button(map, "Crouch", ("<Keyboard>/leftCtrl", Kbm), ("<Keyboard>/c", Kbm), ("<Gamepad>/buttonEast", Pad));
            Button(map, "Sprint", ("<Keyboard>/leftShift", Kbm), ("<Gamepad>/leftStickPress", Pad));
            Button(map, "Walk", ("<Keyboard>/leftAlt", Kbm));
            Button(map, "Interact", ("<Keyboard>/f", Kbm), ("<Gamepad>/buttonWest", Pad));
            Button(map, "Primary", ("<Mouse>/leftButton", Kbm), ("<Gamepad>/rightTrigger", Pad));
            Button(map, "Secondary", ("<Mouse>/rightButton", Kbm), ("<Gamepad>/leftTrigger", Pad));
            Button(map, "Reload", ("<Keyboard>/r", Kbm), ("<Gamepad>/buttonNorth", Pad));
            Button(map, "Inventory", ("<Keyboard>/tab", Kbm), ("<Gamepad>/select", Pad));
            Button(map, "Pause", ("<Keyboard>/escape", Kbm), ("<Gamepad>/start", Pad));
        }

        private static void BuildGlobal(InputActionMap map)
        {
            Button(map, "ToggleConsole", ("<Keyboard>/backquote", Kbm));
            Button(map, "ToggleDebugHud", ("<Keyboard>/f3", Kbm));
            Button(map, "ToggleDebugPanel", ("<Keyboard>/f4", Kbm));
        }

        private static void BuildTerminal(InputActionMap map)
        {
            Button(map, "Exit", ("<Keyboard>/escape", Kbm), ("<Gamepad>/buttonEast", Pad));
        }

        /// <summary>Стандартная UI-карта (её использует UI Toolkit через project-wide actions).</summary>
        private static void BuildUI(InputActionMap map)
        {
            var navigate = map.AddAction("Navigate", InputActionType.PassThrough, expectedControlLayout: "Vector2");
            navigate.AddBinding("<Gamepad>/leftStick", groups: Pad);
            navigate.AddBinding("<Gamepad>/dpad", groups: Pad);
            // WASD сюда намеренно не добавлен: он мешал бы печатать в текстовых полях.
            navigate.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/upArrow", Kbm)
                .With("Down", "<Keyboard>/downArrow", Kbm)
                .With("Left", "<Keyboard>/leftArrow", Kbm)
                .With("Right", "<Keyboard>/rightArrow", Kbm);

            map.AddAction("Submit", InputActionType.Button, "*/{Submit}");
            map.AddAction("Cancel", InputActionType.Button, "*/{Cancel}");

            var point = map.AddAction("Point", InputActionType.PassThrough, expectedControlLayout: "Vector2");
            point.AddBinding("<Mouse>/position", groups: Kbm);
            point.AddBinding("<Pen>/position");
            point.AddBinding("<Touchscreen>/touch*/position");

            var click = map.AddAction("Click", InputActionType.PassThrough, expectedControlLayout: "Button");
            click.AddBinding("<Mouse>/leftButton", groups: Kbm);
            click.AddBinding("<Pen>/tip");
            click.AddBinding("<Touchscreen>/touch*/press");

            map.AddAction("RightClick", InputActionType.PassThrough, "<Mouse>/rightButton", expectedControlLayout: "Button");
            map.AddAction("MiddleClick", InputActionType.PassThrough, "<Mouse>/middleButton", expectedControlLayout: "Button");
            map.AddAction("ScrollWheel", InputActionType.PassThrough, "<Mouse>/scroll", expectedControlLayout: "Vector2");
            map.AddAction("TrackedDevicePosition", InputActionType.PassThrough, "<XRController>/devicePosition", expectedControlLayout: "Vector3");
            map.AddAction("TrackedDeviceOrientation", InputActionType.PassThrough, "<XRController>/deviceRotation", expectedControlLayout: "Quaternion");
        }

        private static void Button(InputActionMap map, string name, params (string path, string group)[] bindings)
        {
            var action = map.AddAction(name, InputActionType.Button);
            foreach (var (path, group) in bindings)
                action.AddBinding(path, groups: group);
        }
    }
}
