using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace TanShadow.EditorTools
{
    // Ассеты Quixel Megascans для лета (Fab, низкое качество): по папке на модель — FBX и текстуры 1K.
    // Для каждой модели: нормаль помечается как Normal map, собирается HDRP/Lit материал и префаб
    // с MeshCollider. Префабы лежат рядом с моделью: Cover — 5 крупных камней-укрытий, Rocks — остальное.
    // Можно запускать повторно: материалы и префабы пересобираются.
    public static class SummerMegascansSetup
    {
        public const string Root = "Assets/_Game/Art/SummerMegascans";

        [MenuItem("Tools/Tan Shadow/Summer/Build Megascans Prefabs")]
        public static void Build()
        {
            int built = 0;
            foreach (string group in new[] { "Cover", "Rocks" })
            {
                string groupDir = Root + "/" + group;
                if (!AssetDatabase.IsValidFolder(groupDir)) continue;
                foreach (string dir in AssetDatabase.GetSubFolders(groupDir))
                    if (BuildOne(dir)) built++;
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[SummerMegascansSetup] Собрано префабов: " + built);
        }

        static bool BuildOne(string dir)
        {
            string name = Path.GetFileName(dir);
            string fbx = dir + "/" + name + ".fbx";
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
            if (model == null) return false;

            var importer = (ModelImporter)AssetImporter.GetAtPath(fbx);
            if (importer.materialImportMode != ModelImporterMaterialImportMode.None || !importer.isReadable)
            {
                importer.materialImportMode = ModelImporterMaterialImportMode.None;
                importer.isReadable = true;   // MeshCollider и расстановка читают вершины
                importer.SaveAndReimport();
                model = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
            }

            Texture2D Tex(string suffix, bool normal = false)
            {
                string path = dir + "/" + name + "_1K_" + suffix + ".jpg";
                if (!File.Exists(path)) return null;
                if (normal)
                {
                    var ti = (TextureImporter)AssetImporter.GetAtPath(path);
                    if (ti.textureType != TextureImporterType.NormalMap) { ti.textureType = TextureImporterType.NormalMap; ti.SaveAndReimport(); }
                }
                return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            }

            string matPath = dir + "/" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                mat = new Material(Shader.Find("HDRP/Lit")) { name = name };
                AssetDatabase.CreateAsset(mat, matPath);
            }
            mat.SetTexture("_BaseColorMap", Tex("BaseColor"));
            mat.SetColor("_BaseColor", Color.white);
            var normalMap = Tex("Normal", true);
            mat.SetTexture("_NormalMap", normalMap);
            mat.SetFloat("_NormalScale", 1f);
            // Сухая вулканическая порода: почти матовая. Маски AO/шероховатости в отдельный MaskMap не собираем.
            mat.SetFloat("_Smoothness", 0.15f);
            mat.SetFloat("_Metallic", 0f);
            HDMaterial.ValidateMaterial(mat);
            EditorUtility.SetDirty(mat);

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            instance.name = name;
            foreach (var r in instance.GetComponentsInChildren<MeshRenderer>(true))
                r.sharedMaterials = Enumerable.Repeat(mat, r.sharedMaterials.Length).ToArray();
            foreach (var mf in instance.GetComponentsInChildren<MeshFilter>(true))
                if (mf.GetComponent<MeshCollider>() == null) mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
            PrefabUtility.SaveAsPrefabAsset(instance, dir + "/" + name + ".prefab");
            Object.DestroyImmediate(instance);
            return true;
        }
    }
}
