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

        private readonly struct Spec
        {
            public readonly string Id, Name;
            public readonly MaterialKind Kind;
            public readonly BlockPattern Pattern;
            public readonly int Hardness;
            public readonly bool Transparent, Unbreakable, Placeable;
            public readonly Color Tint;

            public Spec(string id, string name, MaterialKind kind, BlockPattern pattern, int hardness,
                bool transparent = false, bool unbreakable = false, bool placeable = true, Color? tint = null)
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
                EditorUtility.SetDirty(def);
                catalog.Materials.Add(def);
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
