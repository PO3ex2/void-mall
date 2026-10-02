using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using VoidMall.Core;
using VoidMall.World;

namespace VoidMall.Editor
{
    /// <summary>
    /// Собирает greybox-полигон для отладки контроллера. Все размеры в метрах (1 блок = 1 м).
    /// Игрок появляется в (0, 0, -6) и смотрит на +Z. Зоны:
    /// запад (−X) — лестница, ступени, уклоны; восток (+X) — лазы, проёмы, углы;
    /// север (+Z) — пропы; дальний восток — лифт, башня падений, эскалаторы, платформа.
    /// </summary>
    public static class SandboxBuilder
    {
        public const string ScenePath = "Assets/_Project/Scenes/Sandbox_Controller.unity";

        private static Material _floor, _wall, _accent, _prop, _mechanism, _danger;
        private static Transform _level;

        [MenuItem("Void Mall/Setup/Build Controller Sandbox", priority = 22)]
        public static void BuildControllerSandbox()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            _floor = ProjectSetup.GetGreyboxMaterial("Floor");
            _wall = ProjectSetup.GetGreyboxMaterial("Wall");
            _accent = ProjectSetup.GetGreyboxMaterial("Accent");
            _prop = ProjectSetup.GetGreyboxMaterial("Prop");
            _mechanism = ProjectSetup.GetGreyboxMaterial("Mechanism");
            _danger = ProjectSetup.GetGreyboxMaterial("Danger");

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SetupLighting();

            _level = new GameObject("Level").transform;

            Box("Floor", new Vector3(0f, -0.5f, 10f), new Vector3(140f, 1f, 120f), _floor);

            BuildStairsAndSteps();
            BuildSlopes();
            BuildTightSpaces();
            BuildProps();
            BuildTowerAndElevator();
            BuildEscalators();
            BuildMovingPlatform();

            var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ProjectSetup.PlayerPrefabPath);
            if (playerPrefab != null)
            {
                var player = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab, scene);
                player.transform.position = new Vector3(0f, 0f, -6f);
            }
            else
            {
                Debug.LogError("[Sandbox] Player prefab not found. Run 'Void Mall/Setup/Player Prefab' first.");
            }

            ProjectSetup.EnsureFolder("Assets/_Project/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            AddSceneToBuild(ScenePath);
            Debug.Log($"[Sandbox] Saved {ScenePath}.");
        }

        public const string WorldScenePath = "Assets/_Project/Scenes/Sandbox_World.unity";

        /// <summary>
        /// Полигон мира из блоков (этап 2): ChunkManager с тестовым генератором, игрок с наблюдателем
        /// и временным молотком. Игрок появляется в комнате лабиринта на высоте пола (18).
        /// </summary>
        [MenuItem("Void Mall/Setup/Build World Sandbox", priority = 23)]
        public static void BuildWorldSandbox()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SetupLighting();
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.66f, 0.7f, 0.76f);
            RenderSettings.fogStartDistance = 100f;
            RenderSettings.fogEndDistance = 185f;

            var world = new GameObject("World");
            var chunkManager = world.AddComponent<ChunkManager>();
            world.AddComponent<WorldAuthority>();
            ProjectSetup.SetReference(chunkManager, "_catalog", AssetDatabase.LoadAssetAtPath<MaterialCatalog>(WorldAssetsSetup.CatalogPath));
            ProjectSetup.SetReference(chunkManager, "_opaqueMaterial", AssetDatabase.LoadAssetAtPath<Material>(WorldAssetsSetup.BlockOpaqueMaterialPath));
            ProjectSetup.SetReference(chunkManager, "_transparentMaterial", AssetDatabase.LoadAssetAtPath<Material>(WorldAssetsSetup.BlockTransparentMaterialPath));

            var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ProjectSetup.PlayerPrefabPath);
            if (playerPrefab != null)
            {
                var player = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab, scene);
                player.transform.position = new Vector3(4f, TestWorldGenerationJob.FloorBlockY + 1, 4f);
                player.AddComponent<WorldViewer>();
                var tool = player.AddComponent<VoidMall.Game.DebugBlockTool>();
                ProjectSetup.SetReference(tool, "_outlineMaterial", AssetDatabase.LoadAssetAtPath<Material>(WorldAssetsSetup.OutlineMaterialPath));
                ProjectSetup.SetReference(tool, "_crackMaterial", AssetDatabase.LoadAssetAtPath<Material>(WorldAssetsSetup.CracksMaterialPath));
            }
            else
            {
                Debug.LogError("[Sandbox] Player prefab not found. Run 'Void Mall/Setup/Player Prefab' first.");
            }

            EditorSceneManager.SaveScene(scene, WorldScenePath);
            AddSceneToBuild(WorldScenePath);
            Debug.Log($"[Sandbox] Saved {WorldScenePath}.");
        }

        private static void SetupLighting()
        {
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.6f;
            sun.color = new Color(1f, 0.96f, 0.9f);
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(52f, -35f, 0f);

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.55f, 0.6f, 0.68f);
            RenderSettings.ambientEquatorColor = new Color(0.42f, 0.42f, 0.44f);
            RenderSettings.ambientGroundColor = new Color(0.22f, 0.21f, 0.2f);
            RenderSettings.skybox = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Skybox.mat");
            RenderSettings.sun = sun;
            RenderSettings.fog = false;
        }

        // ── Запад: лестница, ступени, блоки ────────────────────────────────────────

        private static void BuildStairsAndSteps()
        {
            var group = Group("Stairs & Steps");

            // Лестница: подъём 0.25, проступь 0.35, 10 ступеней → площадка 2.5 м.
            const float rise = 0.25f, tread = 0.35f;
            for (int i = 0; i < 10; i++)
            {
                float h = rise * (i + 1);
                Box($"Stair_{i}", new Vector3(-12f, h * 0.5f, 2f + tread * (i + 0.5f)), new Vector3(3f, h, tread), _accent, group);
            }
            Box("StairsLanding", new Vector3(-12f, 1.25f, 2f + tread * 10f + 2f), new Vector3(3f, 2.5f, 4f), _wall, group);

            // Одиночные ступени разной высоты: 0.25/0.5 — шагом, 0.75 и выше — прыжком, 2.0 — только присед-прыжком.
            float[] heights = { 0.25f, 0.5f, 0.75f, 1f, 1.5f, 2f };
            for (int i = 0; i < heights.Length; i++)
            {
                float h = heights[i];
                var material = h <= 0.55f ? _accent : h <= 1f ? _wall : _danger;
                Box($"Step_{h:0.##}m", new Vector3(-20f, h * 0.5f, 2f + i * 3.5f), new Vector3(2f, h, 2f), material, group);
            }

            // Ряд полублоков 0.5 м — «лестница» из блоков.
            for (int i = 0; i < 4; i++)
            {
                float h = 0.5f * (i + 1);
                Box($"HalfBlockStair_{i}", new Vector3(-26f, h * 0.5f, 2f + i * 1f), new Vector3(2f, h, 1f), _accent, group);
            }
        }

        // ── Запад-юг: уклоны ───────────────────────────────────────────────────────

        private static void BuildSlopes()
        {
            var group = Group("Slopes");
            float[] angles = { 15f, 30f, 40f, 45f, 50f, 60f };
            for (int i = 0; i < angles.Length; i++)
            {
                float angle = angles[i];
                const float length = 7f, thickness = 0.4f, width = 3f;
                float rad = angle * Mathf.Deg2Rad;
                // Нижний край пандуса лежит на полу, пандус поднимается к +Z.
                Vector3 center = new(
                    -14f - i * 4f,
                    Mathf.Sin(rad) * length * 0.5f - Mathf.Cos(rad) * thickness * 0.5f,
                    -14f + Mathf.Cos(rad) * length * 0.5f + Mathf.Sin(rad) * thickness * 0.5f);
                var material = angle <= 45f ? _accent : _danger;
                Box($"Slope_{angle:0}deg", center, new Vector3(width, thickness, length), material, group, Quaternion.Euler(-angle, 0f, 0f));
            }
        }

        // ── Восток: лазы, проёмы, коридоры, углы ──────────────────────────────────

        private static void BuildTightSpaces()
        {
            var group = Group("Tight Spaces");

            // Лаз высотой 1 блок (только пригнувшись).
            Box("Crawl_Ceiling", new Vector3(10f, 1.25f, 4f), new Vector3(2.4f, 0.5f, 4f), _wall, group);
            Box("Crawl_WallL", new Vector3(8.6f, 0.75f, 4f), new Vector3(0.4f, 1.5f, 4f), _wall, group);
            Box("Crawl_WallR", new Vector3(11.4f, 0.75f, 4f), new Vector3(0.4f, 1.5f, 4f), _wall, group);

            // Низкий потолок 1.5 м (не встать).
            Box("LowCeiling", new Vector3(10f, 1.75f, 11f), new Vector3(4f, 0.5f, 4f), _wall, group);
            Box("LowCeiling_PillarA", new Vector3(8.2f, 0.75f, 9.2f), new Vector3(0.4f, 1.5f, 0.4f), _wall, group);
            Box("LowCeiling_PillarB", new Vector3(11.8f, 0.75f, 12.8f), new Vector3(0.4f, 1.5f, 0.4f), _wall, group);

            // Стена с дверным проёмом 1×2 м.
            Box("Door_WallL", new Vector3(14.75f, 1.5f, 4f), new Vector3(1.5f, 3f, 0.4f), _wall, group);
            Box("Door_WallR", new Vector3(17.25f, 1.5f, 4f), new Vector3(1.5f, 3f, 0.4f), _wall, group);
            Box("Door_Lintel", new Vector3(16f, 2.5f, 4f), new Vector3(1f, 1f, 0.4f), _wall, group);

            // Узкий коридор 0.8 м.
            Box("Corridor_WallL", new Vector3(15.6f, 1.5f, 11f), new Vector3(0.4f, 3f, 6f), _wall, group);
            Box("Corridor_WallR", new Vector3(16.8f, 1.5f, 11f), new Vector3(0.4f, 3f, 6f), _wall, group);

            // Острый угол (V) — проверка «складки» из двух плоскостей.
            Box("Wedge_A", new Vector3(21f, 1.5f, 8f), new Vector3(0.4f, 3f, 6f), _wall, group, Quaternion.Euler(0f, 15f, 0f));
            Box("Wedge_B", new Vector3(23f, 1.5f, 8f), new Vector3(0.4f, 3f, 6f), _wall, group, Quaternion.Euler(0f, -15f, 0f));
        }

        // ── Север: физические пропы ────────────────────────────────────────────────

        private static void BuildProps()
        {
            var group = Group("Props");
            Prop("Box_5kg", new Vector3(-3f, 0.25f, 10f), new Vector3(0.5f, 0.5f, 0.5f), 5f, group);
            Prop("Box_20kg", new Vector3(-1f, 0.4f, 10f), new Vector3(0.8f, 0.8f, 0.8f), 20f, group);
            Prop("Box_80kg", new Vector3(1.5f, 0.5f, 10f), Vector3.one, 80f, group);
            Prop("Crate_300kg", new Vector3(4.5f, 0.75f, 10f), new Vector3(1.5f, 1.5f, 1.5f), 300f, group);
            Prop("Shelf_200kg", new Vector3(0f, 1f, 15f), new Vector3(3f, 2f, 0.6f), 200f, group);
            for (int i = 0; i < 5; i++)
                Prop($"Stack_5kg_{i}", new Vector3(-5f, 0.25f + i * 0.5f, 14f), new Vector3(0.5f, 0.5f, 0.5f), 5f, group);
        }

        // ── Дальний восток: лифт и башня для проверки урона от падения ──────────────

        private static void BuildTowerAndElevator()
        {
            var group = Group("Tower & Elevator");

            // Кабина лифта едет с 0 до 12 м.
            var lift = Box("Elevator", new Vector3(30f, 0.15f, 0f), new Vector3(3f, 0.3f, 3f), _mechanism, group);
            lift.layer = GameLayers.World;
            GameObjectUtility.SetStaticEditorFlags(lift, 0);
            var liftBody = lift.AddComponent<Rigidbody>();
            liftBody.isKinematic = true;
            var platform = lift.AddComponent<MovingPlatform>();
            SetVector(platform, "_offset", new Vector3(0f, 12f, 0f));

            // Уступы 12 → 9 → 6 → 3 м: каждый спуск — падение 3 м (урон), с 12 м сразу на пол — перелом.
            float[] tops = { 12f, 9f, 6f, 3f };
            for (int i = 0; i < tops.Length; i++)
            {
                float top = tops[i];
                Box($"Ledge_{top:0}m", new Vector3(33f + i * 3f, top * 0.5f, 0f), new Vector3(3f, top, 3f), i == 0 ? _danger : _wall, group);
            }
        }

        // ── Эскалаторы ─────────────────────────────────────────────────────────────

        private static void BuildEscalators()
        {
            var group = Group("Escalators");
            const float angle = 30f, length = 8f, thickness = 0.3f, width = 1.4f;
            float rad = angle * Mathf.Deg2Rad;
            float top = Mathf.Sin(rad) * length;

            for (int i = 0; i < 2; i++)
            {
                float x = 30f + i * 1.8f;
                Vector3 center = new(
                    x,
                    Mathf.Sin(rad) * length * 0.5f - Mathf.Cos(rad) * thickness * 0.5f,
                    10f + Mathf.Cos(rad) * length * 0.5f + Mathf.Sin(rad) * thickness * 0.5f);
                var belt = Box(i == 0 ? "Escalator_Up" : "Escalator_Down", center, new Vector3(width, thickness, length), _mechanism, group, Quaternion.Euler(-angle, 0f, 0f));
                var escalator = belt.AddComponent<Escalator>();
                SetFloat(escalator, "_speed", i == 0 ? 0.75f : -0.75f);
            }

            float topZ = 10f + Mathf.Cos(rad) * length;
            Box("Escalator_TopFloor", new Vector3(31f, top * 0.5f, topZ + 3f), new Vector3(6f, top, 6f), _wall, group);
        }

        private static void BuildMovingPlatform()
        {
            var group = Group("Moving Platform");
            var deck = Box("SlidingDeck", new Vector3(42f, 1f, 10f), new Vector3(3f, 0.3f, 3f), _mechanism, group);
            GameObjectUtility.SetStaticEditorFlags(deck, 0);
            var body = deck.AddComponent<Rigidbody>();
            body.isKinematic = true;
            var platform = deck.AddComponent<MovingPlatform>();
            SetVector(platform, "_offset", new Vector3(0f, 0f, 12f));
            SetFloat(platform, "_speed", 3f);
            Box("SlidingDeck_StartStep", new Vector3(42f, 0.5f, 7f), new Vector3(3f, 1f, 2.4f), _accent, group);
        }

        // ── Утилиты ───────────────────────────────────────────────────────────────

        private static Transform Group(string name)
        {
            var t = new GameObject(name).transform;
            t.SetParent(_level, false);
            return t;
        }

        private static GameObject Box(string name, Vector3 center, Vector3 size, Material material, Transform parent = null, Quaternion? rotation = null)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.layer = GameLayers.World;
            go.transform.SetParent(parent != null ? parent : _level, false);
            go.transform.SetPositionAndRotation(center, rotation ?? Quaternion.identity);
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
            return go;
        }

        private static void Prop(string name, Vector3 center, Vector3 size, float mass, Transform parent)
        {
            var go = Box(name, center, size, _prop, parent);
            go.layer = GameLayers.Props;
            GameObjectUtility.SetStaticEditorFlags(go, 0);
            var body = go.AddComponent<Rigidbody>();
            body.mass = mass;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        }

        private static void SetVector(Object target, string field, Vector3 value)
        {
            var so = new SerializedObject(target);
            so.FindProperty(field).vector3Value = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetFloat(Object target, string field, float value)
        {
            var so = new SerializedObject(target);
            so.FindProperty(field).floatValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void AddSceneToBuild(string path)
        {
            var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            scenes.RemoveAll(s => s.path == path);
            scenes.Insert(0, new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
