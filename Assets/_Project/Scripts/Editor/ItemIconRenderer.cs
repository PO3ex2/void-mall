using System.IO;
using UnityEditor;
using UnityEngine;
using VoidMall.Items;

namespace VoidMall.Editor
{
    /// <summary>
    /// Иконки предметов: модель (процедурная или своя) рисуется напрямую в RenderTexture (Graphics.DrawMeshNow,
    /// шейдер Hidden/VoidMall/ItemIcon — без камер и пайплайна) в ракурсе 3/4 на прозрачном фоне и сохраняется в PNG.
    /// Меню: Void Mall → Setup → Render Item Icons (после замены моделей).
    /// </summary>
    public static class ItemIconRenderer
    {
        public const string IconsFolder = "Assets/_Project/Art/Icons/Items";
        private const int Size = 128;
        private static readonly Quaternion ModelRotation = Quaternion.Euler(0f, 35f, 0f);
        private static readonly Quaternion ViewRotation = Quaternion.LookRotation(Quaternion.Euler(28f, -40f, 0f) * Vector3.back);

        [MenuItem("Void Mall/Setup/Render Item Icons", priority = 40)]
        public static void RenderAll()
        {
            var set = AssetDatabase.LoadAssetAtPath<ItemSet>(ItemAssetsSetup.ItemSetPath);
            var shader = Shader.Find("Hidden/VoidMall/ItemIcon");
            if (set == null || shader == null)
            {
                Debug.LogError("[Icons] ItemSet or icon shader not found. Run 'Void Mall/Setup/All' first.");
                return;
            }
            ProjectSetup.EnsureFolder(IconsFolder);

            var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            var target = new RenderTexture(Size * 2, Size * 2, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var small = new RenderTexture(Size, Size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var readback = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            var previous = RenderTexture.active;
            int count = 0;
            try
            {
                foreach (var def in set.Items)
                {
                    if (def == null)
                        continue;
                    var mesh = def.Mesh != null ? def.Mesh : ItemMeshes.Build(def.Shape, def.ModelSize, def.Tint);
                    Draw(mesh, material, target);
                    // Рисуем в 2× и уменьшаем — дешёвое сглаживание краёв.
                    Graphics.Blit(target, small);
                    RenderTexture.active = small;
                    readback.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
                    readback.Apply();

                    string path = $"{IconsFolder}/{def.Id}.png";
                    File.WriteAllBytes(path, readback.EncodeToPNG());
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                    if (AssetImporter.GetAtPath(path) is TextureImporter importer)
                    {
                        importer.textureType = TextureImporterType.Default;
                        importer.alphaIsTransparency = true;
                        importer.mipmapEnabled = false;
                        importer.wrapMode = TextureWrapMode.Clamp;
                        importer.SaveAndReimport();
                    }
                    def.Icon = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                    EditorUtility.SetDirty(def);
                    if (def.Mesh == null)
                        Object.DestroyImmediate(mesh);
                    count++;
                }
            }
            finally
            {
                RenderTexture.active = previous;
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(small);
                Object.DestroyImmediate(readback);
                Object.DestroyImmediate(material);
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[Icons] Rendered {count} item icons.");
        }

        private static void Draw(Mesh mesh, Material material, RenderTexture target)
        {
            // Габарит модели в пространстве вида — под него подгоняется ортографическая проекция.
            var toView = Matrix4x4.Rotate(Quaternion.Inverse(ViewRotation)) * Matrix4x4.Rotate(ModelRotation);
            var vertices = mesh.vertices;
            Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
            foreach (var v in vertices)
            {
                var p = toView.MultiplyPoint3x4(v);
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }
            if (vertices.Length == 0)
                min = max = Vector3.zero;
            var center = (min + max) * 0.5f;
            float half = Mathf.Max(max.x - min.x, max.y - min.y, 0.02f) * 0.5f * 1.08f;
            float depth = Mathf.Max(max.z - min.z, 0.02f);

            // Камера Unity смотрит вдоль −Z, поэтому вид отражается по Z; модель центрируется перед камерой.
            var view = Matrix4x4.Scale(new Vector3(1f, 1f, -1f)) * Matrix4x4.Translate(-center + Vector3.forward * (depth + 1f)) * toView;
            var projection = GL.GetGPUProjectionMatrix(Matrix4x4.Ortho(-half, half, -half, half, 0.01f, depth * 2f + 2f), true);
            material.SetMatrix("_IconMatrix", projection * view);
            material.SetMatrix("_IconNormalMatrix", toView);

            Graphics.SetRenderTarget(target);
            GL.Clear(true, true, new Color(0f, 0f, 0f, 0f));
            material.SetPass(0);
            Graphics.DrawMeshNow(mesh, Matrix4x4.identity);
        }
    }
}
