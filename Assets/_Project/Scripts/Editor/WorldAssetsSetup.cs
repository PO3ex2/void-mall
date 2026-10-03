using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using VoidMall.World;

namespace VoidMall.Editor
{
    /// <summary>Ассеты мира: описания материалов (блоков), каталог, материалы рендера чанков и инструмента.</summary>
    public static class WorldAssetsSetup
    {
        private const string Root = "Assets/_Project";
        public const string BlocksFolder = Root + "/Data/World/Materials";
        public const string CatalogPath = Root + "/Data/World/MaterialCatalog.asset";
        public const string BlockOpaqueMaterialPath = Root + "/Art/Materials/Blocks_Opaque.mat";
        public const string BlockTransparentMaterialPath = Root + "/Art/Materials/Blocks_Transparent.mat";
        public const string OutlineMaterialPath = Root + "/Art/Materials/Tool_BlockOutline.mat";
        public const string CracksMaterialPath = Root + "/Art/Materials/Tool_BlockCracks.mat";
        public const string BrushMaterialPath = Root + "/Art/Materials/Tool_DrillBrush.mat";
        public const string DustMaterialPath = Root + "/Art/Materials/Fx_Dust.mat";
        public const string VoidSkyboxPath = Root + "/Art/Materials/Sky_VoidBelow.mat";
        public const string PropsMaterialPath = Root + "/Art/Materials/Props.mat";
        public const string SectionsFolder = Root + "/Data/World/Sections";
        public const string WorldSettingsPath = Root + "/Data/World/WorldSettings.asset";

        private readonly struct Spec
        {
            public readonly string Id, Name;
            public readonly MaterialKind Kind;
            public readonly BlockPattern Pattern;
            public readonly int Hardness;
            public readonly bool Transparent, Unbreakable, Placeable;
            public readonly Color Tint;
            public readonly LampKind Lamp;
            public readonly float Emission;

            public Spec(string id, string name, MaterialKind kind, BlockPattern pattern, int hardness,
                bool transparent = false, bool unbreakable = false, bool placeable = true, Color? tint = null,
                LampKind lamp = LampKind.None, float emission = 0f)
            {
                Id = id;
                Name = name;
                Kind = kind;
                Pattern = pattern;
                Hardness = hardness;
                Transparent = transparent;
                Unbreakable = unbreakable;
                Placeable = placeable;
                Tint = tint ?? Color.white;
                Lamp = lamp;
                Emission = emission;
            }
        }

        /// <summary>Порядок = индексы в Cell.Material. Новые материалы добавлять только в конец (иначе поедут сейвы).</summary>
        private static readonly Spec[] Specs =
        {
            new("block.air", "Воздух", MaterialKind.Air, BlockPattern.Flat, 1, placeable: false),
            new("block.bedrock", "Основание", MaterialKind.Block, BlockPattern.Bedrock, 1, unbreakable: true, placeable: false),
            new("block.concrete", "Бетон", MaterialKind.Block, BlockPattern.Concrete, 1200),
            new("block.carpet", "Ковёр", MaterialKind.Block, BlockPattern.Carpet, 300),
            new("block.floor_tile", "Плитка", MaterialKind.Block, BlockPattern.FloorTile, 600),
            new("block.wallpaper", "Стена с обоями", MaterialKind.Block, BlockPattern.Wallpaper, 300),
            new("block.drywall", "Гипсокартон", MaterialKind.Block, BlockPattern.Drywall, 250),
            new("block.ceiling_tile", "Потолочная панель", MaterialKind.Block, BlockPattern.CeilingTile, 100),
            new("block.metal", "Металл", MaterialKind.Block, BlockPattern.Metal, 1500),
            new("block.glass", "Стекло", MaterialKind.Block, BlockPattern.Glass, 100, transparent: true),
            new("block.wood", "Дерево", MaterialKind.Block, BlockPattern.Wood, 400),
            new("smooth.concrete", "Бетонный массив", MaterialKind.Smooth, BlockPattern.Concrete, 1200, tint: new Color(0.8f, 0.8f, 0.84f)),
            new("smooth.soil", "Грунт", MaterialKind.Smooth, BlockPattern.Soil, 300),
            new("smooth.carpet", "Ковёр (гладкий)", MaterialKind.Smooth, BlockPattern.Carpet, 300),
            // ── Этап 4: мол ──
            new("block.lamp_panel", "Светильник", MaterialKind.Block, BlockPattern.LampPanel, 80, tint: new Color(1f, 0.95f, 0.86f), lamp: LampKind.Lamp, emission: 2.2f),
            new("block.emergency_lamp", "Аварийная лампа", MaterialKind.Block, BlockPattern.EmergencyLamp, 80, lamp: LampKind.Emergency, emission: 2.5f),
            new("block.skylight", "Световой фонарь", MaterialKind.Block, BlockPattern.Skylight, 100, tint: new Color(0.86f, 0.93f, 1f), lamp: LampKind.Skylight, emission: 1.6f),
            new("block.terrazzo", "Терраццо", MaterialKind.Block, BlockPattern.Terrazzo, 700),
            new("block.tile_checker", "Плитка «шахматка»", MaterialKind.Block, BlockPattern.CheckerTile, 600),
            new("block.linoleum", "Линолеум", MaterialKind.Block, BlockPattern.Linoleum, 250, tint: new Color(0.8f, 0.88f, 0.82f)),
            new("block.tile_dark", "Тёмная плитка", MaterialKind.Block, BlockPattern.FloorTile, 600, tint: new Color(0.38f, 0.39f, 0.42f)),
            new("block.wood_floor", "Паркет", MaterialKind.Block, BlockPattern.Wood, 400, tint: new Color(0.95f, 0.88f, 0.8f)),
            new("block.paint_white", "Стена (белая)", MaterialKind.Block, BlockPattern.Drywall, 300, tint: new Color(0.97f, 0.97f, 0.95f)),
            new("block.paint_mint", "Стена (мятная)", MaterialKind.Block, BlockPattern.Drywall, 300, tint: new Color(0.75f, 0.9f, 0.82f)),
            new("block.paint_blue", "Стена (голубая)", MaterialKind.Block, BlockPattern.Drywall, 300, tint: new Color(0.68f, 0.78f, 0.92f)),
            new("block.paint_salmon", "Стена (лососевая)", MaterialKind.Block, BlockPattern.Drywall, 300, tint: new Color(0.96f, 0.78f, 0.7f)),
            new("block.paint_gray", "Стена (серая)", MaterialKind.Block, BlockPattern.Drywall, 300, tint: new Color(0.62f, 0.63f, 0.65f)),
            new("block.facade_dark", "Фасадная панель (тёмная)", MaterialKind.Block, BlockPattern.FacadePanel, 1200, tint: new Color(0.25f, 0.27f, 0.3f)),
            new("block.facade_light", "Фасадная панель (светлая)", MaterialKind.Block, BlockPattern.FacadePanel, 1200, tint: new Color(0.85f, 0.85f, 0.82f)),
            new("block.shutter", "Рольставня", MaterialKind.Block, BlockPattern.Shutter, 1500),
            new("block.asphalt", "Асфальт", MaterialKind.Block, BlockPattern.Asphalt, 1000),
            new("block.parking_line_x", "Разметка (вдоль X)", MaterialKind.Block, BlockPattern.ParkingLineU, 1000),
            new("block.parking_line_z", "Разметка (вдоль Z)", MaterialKind.Block, BlockPattern.ParkingLineV, 1000),
            new("smooth.tile", "Плитка бассейна (гладкая)", MaterialKind.Smooth, BlockPattern.FloorTile, 600, tint: new Color(0.82f, 0.92f, 0.98f)),
            // Гладкие двойники полов — облицовка аномалий (тот же узор и оттенок, что у блока).
            new("smooth.floor_tile", "Плитка (гладкая)", MaterialKind.Smooth, BlockPattern.FloorTile, 600),
            new("smooth.terrazzo", "Терраццо (гладкое)", MaterialKind.Smooth, BlockPattern.Terrazzo, 700),
            new("smooth.tile_checker", "Плитка «шахматка» (гладкая)", MaterialKind.Smooth, BlockPattern.CheckerTile, 600),
            new("smooth.linoleum", "Линолеум (гладкий)", MaterialKind.Smooth, BlockPattern.Linoleum, 250, tint: new Color(0.8f, 0.88f, 0.82f)),
            new("smooth.tile_dark", "Тёмная плитка (гладкая)", MaterialKind.Smooth, BlockPattern.FloorTile, 600, tint: new Color(0.38f, 0.39f, 0.42f)),
            new("smooth.wood_floor", "Паркет (гладкий)", MaterialKind.Smooth, BlockPattern.Wood, 400, tint: new Color(0.95f, 0.88f, 0.8f)),
            new("smooth.asphalt", "Асфальт (гладкий)", MaterialKind.Smooth, BlockPattern.Asphalt, 1000),
            new("smooth.concrete_floor", "Бетонный пол (гладкий)", MaterialKind.Smooth, BlockPattern.Concrete, 1200),
        };

        /// <summary>Блок пола → его гладкий двойник.</summary>
        private static readonly (string block, string smooth)[] SmoothVariants =
        {
            ("block.carpet", "smooth.carpet"),
            ("block.concrete", "smooth.concrete_floor"),
            ("block.floor_tile", "smooth.floor_tile"),
            ("block.terrazzo", "smooth.terrazzo"),
            ("block.tile_checker", "smooth.tile_checker"),
            ("block.linoleum", "smooth.linoleum"),
            ("block.tile_dark", "smooth.tile_dark"),
            ("block.wood_floor", "smooth.wood_floor"),
            ("block.asphalt", "smooth.asphalt"),
            ("block.parking_line_x", "smooth.asphalt"),
            ("block.parking_line_z", "smooth.asphalt"),
        };

        public static void CreateWorldAssets()
        {
            ProjectSetup.EnsureFolder(BlocksFolder);
            var catalog = AssetDatabase.LoadAssetAtPath<MaterialCatalog>(CatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<MaterialCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }
            catalog.Materials.Clear();

            foreach (var spec in Specs)
            {
                string path = $"{BlocksFolder}/{spec.Id.Replace("block.", "")}.asset";
                var def = AssetDatabase.LoadAssetAtPath<MaterialDef>(path);
                if (def == null)
                {
                    def = ScriptableObject.CreateInstance<MaterialDef>();
                    AssetDatabase.CreateAsset(def, path);
                }
                var so = new SerializedObject(def);
                so.FindProperty("_id").stringValue = spec.Id;
                so.ApplyModifiedPropertiesWithoutUndo();
                def.DisplayName = spec.Name;
                def.Kind = spec.Kind;
                def.Pattern = spec.Pattern;
                def.Hardness = spec.Hardness;
                def.Transparent = spec.Transparent;
                def.Unbreakable = spec.Unbreakable;
                def.Placeable = spec.Placeable;
                def.Tint = spec.Tint;
                def.Lamp = spec.Lamp;
                def.Emission = spec.Emission;
                EditorUtility.SetDirty(def);
                catalog.Materials.Add(def);
            }
            foreach (var def in catalog.Materials)
                def.SmoothVariant = null;
            foreach (var (block, smooth) in SmoothVariants)
            {
                var blockDef = FindMaterial(catalog, block);
                if (blockDef != null)
                {
                    blockDef.SmoothVariant = FindMaterial(catalog, smooth);
                    EditorUtility.SetDirty(blockDef);
                }
            }
            EditorUtility.SetDirty(catalog);

            var blocksShader = Shader.Find("VoidMall/Blocks");
            var opaque = GetOrCreateMaterial(BlockOpaqueMaterialPath, blocksShader);
            opaque.DisableKeyword("_BLOCKS_TRANSPARENT");
            opaque.SetFloat("_Transparent", 0f);
            opaque.SetFloat("_SrcBlend", (float)BlendMode.One);
            opaque.SetFloat("_DstBlend", (float)BlendMode.Zero);
            opaque.SetFloat("_ZWrite", 1f);
            opaque.renderQueue = (int)RenderQueue.Geometry;
            opaque.enableInstancing = false;

            var transparent = GetOrCreateMaterial(BlockTransparentMaterialPath, blocksShader);
            transparent.EnableKeyword("_BLOCKS_TRANSPARENT");
            transparent.SetFloat("_Transparent", 1f);
            transparent.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            transparent.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            transparent.SetFloat("_ZWrite", 0f);
            transparent.SetOverrideTag("RenderType", "Transparent");
            transparent.renderQueue = (int)RenderQueue.Transparent;

            var outline = GetOrCreateMaterial(OutlineMaterialPath, Shader.Find("Universal Render Pipeline/Unlit"));
            outline.SetColor("_BaseColor", new Color(0.02f, 0.02f, 0.02f, 1f));

            GetOrCreateMaterial(CracksMaterialPath, Shader.Find("VoidMall/BlockCracks"));

            var brush = GetOrCreateMaterial(BrushMaterialPath, Shader.Find("Universal Render Pipeline/Unlit"));
            MakeTransparent(brush);
            brush.SetColor("_BaseColor", new Color(0.45f, 0.8f, 1f, 0.18f));

            var dust = GetOrCreateMaterial(DustMaterialPath, Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            MakeTransparent(dust);
            dust.SetColor("_BaseColor", Color.white);
            var particleTexture = AssetDatabase.GetBuiltinExtraResource<Texture2D>("Default-Particle.psd");
            if (particleTexture != null)
                dust.SetTexture("_BaseMap", particleTexture);

            // Небо тестовых полигонов: нижняя полусфера чёрная — в шахтах-безднах видна пустота, а не «земля» skybox.
            var sky = GetOrCreateMaterial(VoidSkyboxPath, Shader.Find("Skybox/Procedural"));
            sky.SetColor("_GroundColor", Color.black);

            var props = GetOrCreateMaterial(PropsMaterialPath, Shader.Find("VoidMall/Props"));
            props.enableInstancing = true;

            CreateMallAssets(catalog);
            PropAssetsSetup.CreatePropAssets();
            ItemAssetsSetup.CreateItemAssets();
        }

        // ───────────────────────────── Разделы и настройки мира ─────────────────────────────

        private readonly struct SectionSpec
        {
            public readonly string Id, Name, Floor, Wall, Facade, Ceiling;
            public readonly SectionLayout Layout;
            public readonly float Weight;
            public readonly int Mask;
            public readonly bool Service;
            public readonly Color MapColor;

            public SectionSpec(string id, string name, SectionLayout layout, string floor, string wall, string facade, string ceiling,
                float weight, int mask, Color mapColor, bool service = false)
            {
                Id = id;
                Name = name;
                Layout = layout;
                Floor = floor;
                Wall = wall;
                Facade = facade;
                Ceiling = ceiling;
                Weight = weight;
                Mask = mask;
                MapColor = mapColor;
                Service = service;
            }
        }

        private const int AllFloors = 0b11111;

        private static readonly SectionSpec[] Sections =
        {
            new("section.grocery", "Продукты", SectionLayout.Rows, "block.floor_tile", "block.paint_white", "block.facade_dark", "block.ceiling_tile", 3f, 0b00011, new Color(0.45f, 0.75f, 0.35f)),
            new("section.pharmacy", "Аптека", SectionLayout.Perimeter, "block.linoleum", "block.paint_mint", "block.facade_light", "block.ceiling_tile", 2f, AllFloors, new Color(0.4f, 0.85f, 0.75f)),
            new("section.hardware", "Строймаркет", SectionLayout.Warehouse, "block.concrete", "block.paint_gray", "block.facade_dark", "block.ceiling_tile", 2f, 0b00111, new Color(0.9f, 0.55f, 0.2f)),
            new("section.electronics", "Электроника", SectionLayout.Islands, "block.tile_dark", "block.paint_blue", "block.facade_dark", "block.ceiling_tile", 2f, AllFloors, new Color(0.3f, 0.5f, 0.9f)),
            new("section.clothing", "Одежда", SectionLayout.Clothing, "block.wood_floor", "block.paint_salmon", "block.facade_light", "block.ceiling_tile", 3f, AllFloors, new Color(0.9f, 0.45f, 0.6f)),
            new("section.sports", "Спорттовары", SectionLayout.Rows, "block.linoleum", "block.paint_blue", "block.facade_dark", "block.ceiling_tile", 1.5f, AllFloors, new Color(0.95f, 0.85f, 0.3f)),
            new("section.furniture", "Мебель", SectionLayout.Showroom, "block.carpet", "block.wallpaper", "block.facade_light", "block.ceiling_tile", 1.5f, 0b11100, new Color(0.6f, 0.4f, 0.25f)),
            new("section.toys", "Игрушки", SectionLayout.Rows, "block.tile_checker", "block.paint_salmon", "block.facade_light", "block.ceiling_tile", 1.5f, AllFloors, new Color(0.95f, 0.4f, 0.3f)),
            new("section.foodcourt", "Фудкорт", SectionLayout.Hall, "block.tile_checker", "block.paint_white", "block.facade_dark", "block.ceiling_tile", 1f, 0b11000, new Color(0.85f, 0.3f, 0.85f)),
            new("section.empty", "Пустой зал", SectionLayout.Empty, "block.carpet", "block.wallpaper", "block.facade_light", "block.ceiling_tile", 0.6f, AllFloors, new Color(0.85f, 0.8f, 0.5f)),
            new("section.storage", "Подсобка", SectionLayout.Warehouse, "block.concrete", "block.drywall", "block.facade_dark", "block.concrete", 1f, AllFloors, new Color(0.5f, 0.5f, 0.5f), service: true),
            new("section.tech", "Техническое помещение", SectionLayout.Tech, "block.concrete", "block.concrete", "block.concrete", "block.concrete", 1f, 0, new Color(0.35f, 0.35f, 0.4f), service: true),
        };

        private static void CreateMallAssets(MaterialCatalog catalog)
        {
            ProjectSetup.EnsureFolder(SectionsFolder);
            var sections = new System.Collections.Generic.List<SectionDef>();
            foreach (var spec in Sections)
            {
                string path = $"{SectionsFolder}/{spec.Id.Replace("section.", "")}.asset";
                var def = AssetDatabase.LoadAssetAtPath<SectionDef>(path);
                if (def == null)
                {
                    def = ScriptableObject.CreateInstance<SectionDef>();
                    AssetDatabase.CreateAsset(def, path);
                }
                var so = new SerializedObject(def);
                so.FindProperty("_id").stringValue = spec.Id;
                so.ApplyModifiedPropertiesWithoutUndo();
                def.DisplayName = spec.Name;
                def.Layout = spec.Layout;
                def.Weight = spec.Weight;
                def.RetailFloorMask = spec.Mask;
                def.Service = spec.Service;
                def.MapColor = spec.MapColor;
                def.Floor = FindMaterial(catalog, spec.Floor);
                def.Wall = FindMaterial(catalog, spec.Wall);
                def.Facade = FindMaterial(catalog, spec.Facade);
                def.Ceiling = FindMaterial(catalog, spec.Ceiling);
                EditorUtility.SetDirty(def);
                sections.Add(def);
            }

            var settings = AssetDatabase.LoadAssetAtPath<WorldSettings>(WorldSettingsPath);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<WorldSettings>();
                AssetDatabase.CreateAsset(settings, WorldSettingsPath);
            }
            settings.Sections = sections;
            EditorUtility.SetDirty(settings);
        }

        private static MaterialDef FindMaterial(MaterialCatalog catalog, string id)
        {
            foreach (var def in catalog.Materials)
                if (def.Id == id)
                    return def;
            Debug.LogError($"[Setup] Material '{id}' not found in the catalog.");
            return null;
        }

        /// <summary>Прозрачный режим для стандартных URP-шейдеров (Unlit, Particles).</summary>
        private static void MakeTransparent(Material material)
        {
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = (int)RenderQueue.Transparent;
        }

        private static Material GetOrCreateMaterial(string path, Shader shader)
        {
            ProjectSetup.EnsureFolder(Path.GetDirectoryName(path));
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
            EditorUtility.SetDirty(material);
            return material;
        }
    }
}
