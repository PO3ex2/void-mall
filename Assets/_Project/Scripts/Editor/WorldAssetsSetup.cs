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

        private readonly struct Spec
        {
            public readonly string Id, Name;
            public readonly MaterialKind Kind;
            public readonly BlockPattern Pattern;
            public readonly int Hardness;
            public readonly bool Transparent, Unbreakable, Placeable;

            public Spec(string id, string name, MaterialKind kind, BlockPattern pattern, int hardness,
                bool transparent = false, bool unbreakable = false, bool placeable = true)
            {
                Id = id;
                Name = name;
                Kind = kind;
                Pattern = pattern;
                Hardness = hardness;
                Transparent = transparent;
                Unbreakable = unbreakable;
                Placeable = placeable;
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
