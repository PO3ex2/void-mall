using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using VoidMall.Core;
using VoidMall.Game;
using VoidMall.Player;

namespace VoidMall.Editor
{
    /// <summary>
    /// Воспроизводимая настройка проекта: слои, частота тика, ввод, UI, конфиг, ассеты движения, префаб игрока.
    /// </summary>
    public static class ProjectSetup
    {
        private const string Root = "Assets/_Project";
        public const string PanelSettingsPath = Root + "/Settings/UI/PanelSettings.asset";
        public const string ThemePath = Root + "/Settings/UI/VoidMallRuntimeTheme.tss";
        public const string GameConfigPath = Root + "/Resources/VoidMall/GameConfig.asset";
        public const string MovementSettingsPath = Root + "/Data/Movement/DefaultMovement.asset";
        public const string PlayerPrefabPath = Root + "/Prefabs/Player.prefab";
        public const string GreyboxShaderPath = Root + "/Art/Shaders/Greybox.shader";
        public const string MaterialsFolder = Root + "/Art/Materials";

        private const string ProjectWideActionsKey = "com.unity.input.settings.actions";
        private const string TemplateActionsPath = "Assets/InputSystem_Actions.inputactions";

        [MenuItem("Void Mall/Setup/All (project + player + sandbox)", priority = 0)]
        public static void SetupAll()
        {
            SetupProjectAssets();
            CreatePlayerPrefab();
            SandboxBuilder.BuildControllerSandbox();
            SandboxBuilder.BuildWorldSandbox();
        }

        [MenuItem("Void Mall/Setup/Project Assets", priority = 20)]
        public static void SetupProjectAssets()
        {
            SetupLayers();
            SetupTime();
            PlayerSettings.productName = "Void Mall";
            // Хост коопа должен симулировать мир, даже когда окно свёрнуто.
            PlayerSettings.runInBackground = true;

            SetupInput();
            var panelSettings = CreatePanelSettings();
            CreateGameConfig(panelSettings);
            GetOrCreateMovementSettings();
            CreateGreyboxMaterials();
            WorldAssetsSetup.CreateWorldAssets();

            AssetDatabase.SaveAssets();
            Debug.Log("[Setup] Project assets are ready.");
        }

        private static void SetupLayers()
        {
            var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = tagManager.FindProperty("layers");
            SetLayer(layers, GameLayers.World, "World");
            SetLayer(layers, GameLayers.Props, "Props");
            SetLayer(layers, GameLayers.Player, "Player");
            SetLayer(layers, GameLayers.Enemy, "Enemy");
            SetLayer(layers, GameLayers.Trigger, "Trigger");
            SetLayer(layers, GameLayers.Debris, "Debris");
            SetLayer(layers, GameLayers.Portal, "Portal");
            tagManager.ApplyModifiedProperties();

            Physics.IgnoreLayerCollision(GameLayers.Player, GameLayers.Debris, true);
            Physics.IgnoreLayerCollision(GameLayers.Player, GameLayers.Trigger, false);
        }

        private static void SetLayer(SerializedProperty layers, int index, string name)
        {
            var element = layers.GetArrayElementAtIndex(index);
            if (!string.IsNullOrEmpty(element.stringValue) && element.stringValue != name)
                Debug.LogWarning($"[Setup] Layer {index} '{element.stringValue}' renamed to '{name}'.");
            element.stringValue = name;
        }

        private static void SetupTime()
        {
            Time.fixedDeltaTime = 1f / 60f;
            var timeManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TimeManager.asset")[0]);
            timeManager.Update();
            var maxDelta = timeManager.FindProperty("Maximum Allowed Timestep");
            if (maxDelta != null)
            {
                maxDelta.floatValue = 0.1f;
                timeManager.ApplyModifiedProperties();
            }
        }

        private static void SetupInput()
        {
            var actions = InputActionsBuilder.BuildAndSave();
            InputSystem.actions = actions;
            EditorBuildSettings.AddConfigObject(ProjectWideActionsKey, actions, true);

            if (AssetDatabase.LoadAssetAtPath<InputActionAsset>(TemplateActionsPath) != null)
                AssetDatabase.DeleteAsset(TemplateActionsPath);
        }

        private static PanelSettings CreatePanelSettings()
        {
            EnsureFolder(Path.GetDirectoryName(ThemePath));
            if (!File.Exists(ThemePath))
            {
                File.WriteAllText(ThemePath, "@import url(\"unity-theme://default\");\n");
                AssetDatabase.ImportAsset(ThemePath);
            }
            var theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);

            var panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            if (panelSettings == null)
            {
                panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
                AssetDatabase.CreateAsset(panelSettings, PanelSettingsPath);
            }
            panelSettings.themeStyleSheet = theme;
            panelSettings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panelSettings.referenceResolution = new Vector2Int(1920, 1080);
            panelSettings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            panelSettings.match = 0.5f;
            EditorUtility.SetDirty(panelSettings);
            return panelSettings;
        }

        private static void CreateGameConfig(PanelSettings panelSettings)
        {
            EnsureFolder(Path.GetDirectoryName(GameConfigPath));
            var config = AssetDatabase.LoadAssetAtPath<GameConfig>(GameConfigPath);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<GameConfig>();
                AssetDatabase.CreateAsset(config, GameConfigPath);
            }
            config.PanelSettings = panelSettings;
            EditorUtility.SetDirty(config);
        }

        public static MovementSettings GetOrCreateMovementSettings()
        {
            var settings = AssetDatabase.LoadAssetAtPath<MovementSettings>(MovementSettingsPath);
            if (settings != null)
                return settings;
            EnsureFolder(Path.GetDirectoryName(MovementSettingsPath));
            settings = ScriptableObject.CreateInstance<MovementSettings>();
            AssetDatabase.CreateAsset(settings, MovementSettingsPath);
            return settings;
        }

        public static Material GetGreyboxMaterial(string name) =>
            AssetDatabase.LoadAssetAtPath<Material>($"{MaterialsFolder}/Greybox_{name}.mat");

        private static void CreateGreyboxMaterials()
        {
            CreateGreybox("Floor", new Color(0.52f, 0.53f, 0.55f), new Color(0.3f, 0.31f, 0.33f));
            CreateGreybox("Wall", new Color(0.78f, 0.77f, 0.72f), new Color(0.5f, 0.49f, 0.45f));
            CreateGreybox("Accent", new Color(0.55f, 0.66f, 0.78f), new Color(0.3f, 0.38f, 0.48f));
            CreateGreybox("Prop", new Color(0.9f, 0.55f, 0.25f), new Color(0.55f, 0.3f, 0.12f));
            CreateGreybox("Mechanism", new Color(0.3f, 0.75f, 0.7f), new Color(0.12f, 0.42f, 0.38f));
            CreateGreybox("Danger", new Color(0.85f, 0.3f, 0.3f), new Color(0.5f, 0.15f, 0.15f));
        }

        private static void CreateGreybox(string name, Color baseColor, Color lineColor)
        {
            EnsureFolder(MaterialsFolder);
            string path = $"{MaterialsFolder}/Greybox_{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                var shader = AssetDatabase.LoadAssetAtPath<Shader>(GreyboxShaderPath);
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetColor("_BaseColor", baseColor);
            material.SetColor("_LineColor", lineColor);
            EditorUtility.SetDirty(material);
        }

        [MenuItem("Void Mall/Setup/Player Prefab", priority = 21)]
        public static GameObject CreatePlayerPrefab()
        {
            var settings = GetOrCreateMovementSettings();

            var root = new GameObject("Player") { layer = GameLayers.Player };
            var look = root.AddComponent<PlayerLook>();
            var motor = root.AddComponent<PlayerMotor>();
            var reader = root.AddComponent<PlayerInputReader>();
            root.AddComponent<SimpleStaminaPool>();
            root.AddComponent<DebugStatusModifierSource>();

            var capsule = root.GetComponent<CapsuleCollider>();
            capsule.radius = settings.Radius;
            capsule.height = settings.StandHeight;
            capsule.center = new Vector3(0f, settings.StandHeight * 0.5f, 0f);
            var body = root.GetComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;

            var cameraObject = new GameObject("PlayerCamera") { tag = "MainCamera" };
            cameraObject.transform.SetParent(root.transform, false);
            cameraObject.transform.localPosition = new Vector3(0f, settings.StandEyeHeight, 0f);
            var camera = cameraObject.AddComponent<Camera>();
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 600f;
            camera.fieldOfView = 80f;
            cameraObject.AddComponent<AudioListener>();
            var rig = cameraObject.AddComponent<PlayerCameraRig>();

            SetReference(motor, "_settings", settings);
            SetReference(motor, "_look", look);
            SetReference(reader, "_look", look);
            SetReference(reader, "_motor", motor);
            SetReference(rig, "_motor", motor);
            SetReference(rig, "_look", look);
            SetReference(rig, "_camera", camera);

            EnsureFolder(Path.GetDirectoryName(PlayerPrefabPath));
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
            Object.DestroyImmediate(root);
            Debug.Log($"[Setup] Player prefab saved to {PlayerPrefabPath}.");
            return prefab;
        }

        public static void SetReference(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            var property = so.FindProperty(field);
            if (property == null)
            {
                Debug.LogError($"[Setup] Field '{field}' not found on {target.GetType().Name}.");
                return;
            }
            property.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        public static void EnsureFolder(string path)
        {
            path = path.Replace('\\', '/');
            if (AssetDatabase.IsValidFolder(path))
                return;
            string parent = Path.GetDirectoryName(path)!.Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
