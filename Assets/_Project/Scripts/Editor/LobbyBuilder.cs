using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using VoidMall.Core;
using VoidMall.Game;
using VoidMall.World;

namespace VoidMall.Editor
{
    /// <summary>
    /// Лобби (этап 10): комната поста охраны Hexaryte — линолеум, потолочная плитка, мигающая лампа, стол с ЭЛТ-ПК, стул,
    /// шкаф, плакат с логотипом, арка портала за столом. Префаб создаётся один раз (Lobby.prefab) и дальше не
    /// перезаписывается: его можно обставлять по-своему. Ставится в сцену мира на высоте LobbyHeight (над молом, за туманом).
    /// </summary>
    public static class LobbyBuilder
    {
        public const string PrefabPath = "Assets/_Project/Prefabs/Lobby.prefab";
        public const string ArtFolder = "Assets/_Project/Art/Lobby";
        public const string LogoPath = "Assets/_Project/Art/UI/HEXALOGO.png";
        public static readonly Vector3 LobbyPosition = new(0f, 1000f, 0f);

        private const float HalfWidth = 3f, HalfDepth = 4f, RoomHeight = 3.6f, Wall = 0.2f;
        private const float PortalZ = 3.5f;

        private static readonly Color Linoleum = new(0.42f, 0.47f, 0.43f);
        private static readonly Color WallColor = new(0.78f, 0.75f, 0.66f);
        private static readonly Color Baseboard = new(0.25f, 0.24f, 0.22f);
        private static readonly Color CeilingColor = new(0.86f, 0.86f, 0.83f);
        private static readonly Color Grid = new(0.62f, 0.62f, 0.6f);
        private static readonly Color Frame = new(0.18f, 0.2f, 0.23f);
        private static readonly Color Beige = new(0.8f, 0.77f, 0.68f);
        private static readonly Color DarkPlastic = new(0.15f, 0.15f, 0.16f);

        /// <summary>Поставить лобби в открытую сцену (префаб создаётся, если его ещё нет).</summary>
        public static GameObject Place()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) ?? CreatePrefab();
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.transform.position = LobbyPosition;
            return instance;
        }

        [MenuItem("Void Mall/Setup/Rebuild Lobby Prefab (overwrites!)", priority = 24)]
        private static void RebuildMenu()
        {
            if (!EditorUtility.DisplayDialog("Лобби", "Пересоздать Lobby.prefab? Ваши правки в нём пропадут.", "Пересоздать", "Отмена"))
                return;
            AssetDatabase.DeleteAsset(PrefabPath);
            CreatePrefab();
        }

        public static GameObject CreatePrefab()
        {
            ProjectSetup.EnsureFolder(ArtFolder);
            ProjectSetup.EnsureFolder("Assets/_Project/Prefabs");
            var props = AssetDatabase.LoadAssetAtPath<Material>(WorldAssetsSetup.PropsMaterialPath);
            var logo = AssetDatabase.LoadAssetAtPath<Texture2D>(LogoPath);
            var logoMaterial = UnlitMaterial("Lobby_Logo", Color.white, logo, alphaClip: true);
            var lampMaterial = UnlitMaterial("Lobby_Lamp", new Color(1f, 0.98f, 0.9f), null, alphaClip: false);
            var portalMaterial = GetOrCreate($"{ArtFolder}/Portal.mat", Shader.Find("VoidMall/Portal"));

            var root = new GameObject("Lobby");
            var room = root.AddComponent<LobbyRoom>();

            // ── Комната: пол, потолок, стены (в задней стене — ниша под портал) ──
            var shell = new MeshBuilder();
            shell.BoxMinMax(new Vector3(-HalfWidth, -0.2f, -HalfDepth), new Vector3(HalfWidth, 0f, HalfDepth + 0.6f), Linoleum);
            shell.BoxMinMax(new Vector3(-HalfWidth, RoomHeight, -HalfDepth), new Vector3(HalfWidth, RoomHeight + 0.2f, HalfDepth + 0.6f), CeilingColor);
            for (float x = -HalfWidth + 0.6f; x < HalfWidth; x += 0.6f)
                shell.BoxMinMax(new Vector3(x - 0.015f, RoomHeight - 0.01f, -HalfDepth), new Vector3(x + 0.015f, RoomHeight, HalfDepth), Grid);
            for (float z = -HalfDepth + 0.6f; z < HalfDepth; z += 0.6f)
                shell.BoxMinMax(new Vector3(-HalfWidth, RoomHeight - 0.01f, z - 0.015f), new Vector3(HalfWidth, RoomHeight, z + 0.015f), Grid);
            shell.BoxMinMax(new Vector3(-HalfWidth - Wall, 0f, -HalfDepth), new Vector3(-HalfWidth, RoomHeight, HalfDepth), WallColor);
            shell.BoxMinMax(new Vector3(HalfWidth, 0f, -HalfDepth), new Vector3(HalfWidth + Wall, RoomHeight, HalfDepth), WallColor);
            shell.BoxMinMax(new Vector3(-HalfWidth, 0f, -HalfDepth - Wall), new Vector3(HalfWidth, RoomHeight, -HalfDepth), WallColor);
            // Задняя стена с проёмом 2.4 × 3.2 и нишей глубиной 0.6 м (за плоскостью портала).
            shell.BoxMinMax(new Vector3(-HalfWidth, 0f, HalfDepth), new Vector3(-1.2f, RoomHeight, HalfDepth + Wall), WallColor);
            shell.BoxMinMax(new Vector3(1.2f, 0f, HalfDepth), new Vector3(HalfWidth, RoomHeight, HalfDepth + Wall), WallColor);
            shell.BoxMinMax(new Vector3(-1.2f, 3.2f, HalfDepth), new Vector3(1.2f, RoomHeight, HalfDepth + Wall), WallColor);
            shell.BoxMinMax(new Vector3(-1.2f, 0f, HalfDepth + 0.6f), new Vector3(1.2f, 3.2f, HalfDepth + 0.8f), Frame);
            // Плинтус.
            shell.BoxMinMax(new Vector3(-HalfWidth, 0f, -HalfDepth), new Vector3(-HalfWidth + 0.02f, 0.1f, HalfDepth), Baseboard);
            shell.BoxMinMax(new Vector3(HalfWidth - 0.02f, 0f, -HalfDepth), new Vector3(HalfWidth, 0.1f, HalfDepth), Baseboard);
            var shellObject = MeshObject("Room", root.transform, shell, props);
            AddBox(shellObject, new Vector3(0f, -0.1f, 0.3f), new Vector3(HalfWidth * 2f, 0.2f, HalfDepth * 2f + 0.6f));
            AddBox(shellObject, new Vector3(0f, RoomHeight + 0.1f, 0.3f), new Vector3(HalfWidth * 2f, 0.2f, HalfDepth * 2f + 0.6f));
            AddBox(shellObject, new Vector3(-HalfWidth - Wall * 0.5f, RoomHeight * 0.5f, 0f), new Vector3(Wall, RoomHeight, HalfDepth * 2f));
            AddBox(shellObject, new Vector3(HalfWidth + Wall * 0.5f, RoomHeight * 0.5f, 0f), new Vector3(Wall, RoomHeight, HalfDepth * 2f));
            AddBox(shellObject, new Vector3(0f, RoomHeight * 0.5f, -HalfDepth - Wall * 0.5f), new Vector3(HalfWidth * 2f, RoomHeight, Wall));
            AddBox(shellObject, new Vector3(-2.1f, RoomHeight * 0.5f, HalfDepth + Wall * 0.5f), new Vector3(1.8f, RoomHeight, Wall));
            AddBox(shellObject, new Vector3(2.1f, RoomHeight * 0.5f, HalfDepth + Wall * 0.5f), new Vector3(1.8f, RoomHeight, Wall));
            AddBox(shellObject, new Vector3(0f, 0f, HalfDepth + 0.7f) + Vector3.up * 1.6f, new Vector3(2.4f, 3.2f, 0.2f));

            // ── Лампа ──
            var lamp = new GameObject("Lamp");
            lamp.transform.SetParent(root.transform, false);
            lamp.transform.localPosition = new Vector3(0f, RoomHeight - 0.05f, -0.8f);
            var fixtureBuilder = new MeshBuilder();
            fixtureBuilder.Box(Vector3.zero, new Vector3(1.2f, 0.06f, 0.3f), Color.white);
            var fixture = MeshObject("Fixture", lamp.transform, fixtureBuilder, lampMaterial);
            var lightObject = new GameObject("Light");
            lightObject.transform.SetParent(lamp.transform, false);
            lightObject.transform.localPosition = new Vector3(0f, -0.3f, 0f);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 10f;
            light.intensity = 2.2f;
            light.color = new Color(1f, 0.95f, 0.85f);
            light.shadows = LightShadows.Soft;
            var flicker = lightObject.AddComponent<FlickerLight>();
            ProjectSetup.SetReference(flicker, "_fixture", fixture.GetComponent<Renderer>());

            // ── Мебель ──
            var desk = PropObject("Desk", root.transform, PropShape.Table, new Vector3(1.4f, 0.75f, 0.7f), props, new Vector3(0f, 0f, 0.4f), 180f);
            PropObject("Chair", root.transform, PropShape.OfficeChair, PropMeshes.DefaultSize(PropShape.OfficeChair), props, new Vector3(0.95f, 0f, -0.35f), 150f);
            PropObject("Cabinet", root.transform, PropShape.FilingCabinet, PropMeshes.DefaultSize(PropShape.FilingCabinet), props, new Vector3(HalfWidth - 0.35f, 0f, 1.6f), 270f);
            PropObject("Cooler", root.transform, PropShape.WaterCooler, PropMeshes.DefaultSize(PropShape.WaterCooler), props, new Vector3(-HalfWidth + 0.35f, 0f, -2.6f), 90f);

            // ── ПК: ЭЛТ-монитор, клавиатура, системный блок ──
            float top = 0.75f;
            var pc = new GameObject("PC");
            pc.transform.SetParent(root.transform, false);
            pc.transform.localPosition = new Vector3(0f, top, 0.5f);
            var pcBuilder = new MeshBuilder();
            pcBuilder.Box(new Vector3(0f, 0.2f, 0.05f), new Vector3(0.44f, 0.38f, 0.42f), Beige);
            pcBuilder.Box(new Vector3(0f, 0.2f, 0.32f), new Vector3(0.3f, 0.28f, 0.14f), Beige * 0.92f);
            pcBuilder.Box(new Vector3(0f, 0.005f, 0.05f), new Vector3(0.26f, 0.01f, 0.22f), Beige * 0.85f);
            pcBuilder.Box(new Vector3(0f, 0.012f, -0.32f), new Vector3(0.44f, 0.025f, 0.15f), Beige * 0.95f);
            pcBuilder.Box(new Vector3(0.48f, 0.19f, 0.05f), new Vector3(0.18f, 0.38f, 0.4f), Beige * 0.9f);
            pcBuilder.Box(new Vector3(0.48f, 0.3f, -0.151f), new Vector3(0.12f, 0.02f, 0.01f), DarkPlastic);
            var body = MeshObject("Body", pc.transform, pcBuilder, props);
            AddBox(body, new Vector3(0f, 0.2f, 0.1f), new Vector3(0.44f, 0.38f, 0.52f));
            var screen = GameObject.CreatePrimitive(PrimitiveType.Quad);
            screen.name = "Screen";
            screen.transform.SetParent(pc.transform, false);
            screen.transform.localPosition = new Vector3(0f, 0.205f, -0.162f);
            screen.transform.localScale = new Vector3(0.34f, 0.255f, 1f);
            Object.DestroyImmediate(screen.GetComponent<MeshCollider>());
            var screenCollider = screen.AddComponent<MeshCollider>();
            var screenRenderer = screen.GetComponent<MeshRenderer>();
            screenRenderer.sharedMaterial = logoMaterial;
            screenRenderer.shadowCastingMode = ShadowCastingMode.Off;
            var view = new GameObject("View Point");
            view.transform.SetParent(pc.transform, false);
            view.transform.localPosition = new Vector3(0f, 0.205f, -0.62f);
            var terminal = pc.AddComponent<PcTerminal>();
            terminal.Configure(screenRenderer, screenCollider, view.transform, logo, inWorld: false, broken: false);

            // ── Плакат с логотипом ──
            var poster = new GameObject("Poster");
            poster.transform.SetParent(root.transform, false);
            poster.transform.localPosition = new Vector3(-HalfWidth + 0.01f, 1.75f, 0.8f);
            poster.transform.localRotation = Quaternion.Euler(0f, -90f, 0f);
            var posterBack = new MeshBuilder();
            posterBack.Box(new Vector3(0f, 0f, 0.004f), new Vector3(0.8f, 1.05f, 0.008f), new Color(0.12f, 0.14f, 0.17f));
            MeshObject("Back", poster.transform, posterBack, props);
            LogoQuad("Logo", poster.transform, new Vector3(0f, 0.12f, -0.002f), 0.52f, logo, logoMaterial);

            // ── Портал: рамка, плоскость, эмблема ──
            var portalRoot = new GameObject("Portal");
            portalRoot.transform.SetParent(root.transform, false);
            portalRoot.transform.localPosition = new Vector3(0f, 0f, PortalZ);
            var frameObject = new GameObject("Frame");
            frameObject.transform.SetParent(portalRoot.transform, false);
            var frameBuilder = new MeshBuilder();
            float w = PortalLink.Width * 0.5f, h = PortalLink.Height;
            frameBuilder.BoxMinMax(new Vector3(-w - 0.15f, 0f, -0.1f), new Vector3(-w, h + 0.15f, 0.1f), Frame);
            frameBuilder.BoxMinMax(new Vector3(w, 0f, -0.1f), new Vector3(w + 0.15f, h + 0.15f, 0.1f), Frame);
            frameBuilder.BoxMinMax(new Vector3(-w, h, -0.1f), new Vector3(w, h + 0.15f, 0.1f), Frame);
            MeshObject("Arch", frameObject.transform, frameBuilder, props);
            var surface = GameObject.CreatePrimitive(PrimitiveType.Quad);
            surface.name = "Surface";
            surface.transform.SetParent(frameObject.transform, false);
            surface.transform.localPosition = new Vector3(0f, h * 0.5f, 0f);
            surface.transform.localScale = new Vector3(PortalLink.Width, h, 1f);
            Object.DestroyImmediate(surface.GetComponent<MeshCollider>());
            var surfaceRenderer = surface.GetComponent<MeshRenderer>();
            surfaceRenderer.sharedMaterial = portalMaterial;
            surfaceRenderer.shadowCastingMode = ShadowCastingMode.Off;
            surface.layer = GameLayers.Portal;
            LogoQuad("Emblem", frameObject.transform, new Vector3(0f, h + 0.45f, -0.11f), 0.42f, logo, logoMaterial);
            var link = portalRoot.AddComponent<PortalLink>();
            link.Configure(portalRoot.transform, surfaceRenderer);

            var spawn = new GameObject("Spawn");
            spawn.transform.SetParent(root.transform, false);
            spawn.transform.localPosition = new Vector3(0f, 0.05f, -2.8f);

            ProjectSetup.SetReference(room, "_spawn", spawn.transform);
            ProjectSetup.SetReference(room, "_terminal", terminal);
            ProjectSetup.SetReference(room, "_portal", link);
            ProjectSetup.SetReference(room, "_portalFrame", frameObject);

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            Debug.Log($"[Lobby] Prefab saved to {PrefabPath}.");
            return prefab;
        }

        private static GameObject MeshObject(string name, Transform parent, MeshBuilder builder, Material material)
        {
            var mesh = builder.ToMesh("Lobby_" + name);
            string path = $"{ArtFolder}/{parent.name}_{name}.asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mesh, path);
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            return go;
        }

        private static GameObject PropObject(string name, Transform parent, PropShape shape, Vector3 size, Material material, Vector3 position, float yaw)
        {
            var mesh = PropMeshes.Build(shape, size);
            string path = $"{ArtFolder}/Prop_{name}.asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mesh, path);
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            var box = go.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, size.y * 0.5f, 0f);
            box.size = size;
            return go;
        }

        private static void LogoQuad(string name, Transform parent, Vector3 position, float height, Texture2D logo, Material material)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = name;
            quad.transform.SetParent(parent, false);
            quad.transform.localPosition = position;
            float aspect = logo != null ? logo.width / (float)logo.height : 1f;
            quad.transform.localScale = new Vector3(height * aspect, height, 1f);
            Object.DestroyImmediate(quad.GetComponent<MeshCollider>());
            var renderer = quad.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
        }

        private static void AddBox(GameObject go, Vector3 center, Vector3 size)
        {
            var box = go.AddComponent<BoxCollider>();
            box.center = center;
            box.size = size;
        }

        private static Material UnlitMaterial(string name, Color color, Texture2D texture, bool alphaClip)
        {
            var material = GetOrCreate($"{ArtFolder}/{name}.mat", Shader.Find("Universal Render Pipeline/Unlit"));
            material.SetColor("_BaseColor", color);
            material.SetTexture("_BaseMap", texture);
            material.SetFloat("_AlphaClip", alphaClip ? 1f : 0f);
            material.SetFloat("_Cutoff", 0.5f);
            if (alphaClip)
                material.EnableKeyword("_ALPHATEST_ON");
            else
                material.DisableKeyword("_ALPHATEST_ON");
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material GetOrCreate(string path, Shader shader)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
            }
            return material;
        }
    }
}
