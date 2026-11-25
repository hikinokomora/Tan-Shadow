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
            MatchTerrain();
        }

        public const string TerrainPrefab = "Assets/ZhunqueWideArena/Compact425/Zhunque_Dry_425.prefab";
        static readonly int BaseColorMap = Shader.PropertyToID("_BaseColorMap");

        // Камни Megascans светлее и теплее пепла арены. Каждому материалу подбирается множитель _BaseColor,
        // при котором средний цвет его текстуры равен среднему цвету слоя террейна; блеск — как у слоя.
        // Узор и объём текстуры остаются. Числа не задаются руками — всё берётся из слоя террейна.
        [MenuItem("Tools/Tan Shadow/Summer/Match Megascans To Terrain Colour")]
        public static void MatchTerrain()
        {
            var terrain = AssetDatabase.LoadAssetAtPath<GameObject>(TerrainPrefab)?.GetComponentInChildren<Terrain>(true);
            var layer = terrain != null && terrain.terrainData != null && terrain.terrainData.terrainLayers.Length > 0
                ? terrain.terrainData.terrainLayers[0] : null;
            if (layer == null || layer.diffuseTexture == null)
            {
                Debug.LogError("[SummerMegascansSetup] Нет слоя террейна в " + TerrainPrefab);
                return;
            }
            Color target = AverageLinear(layer.diffuseTexture);
            int count = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { Root }))
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                var tex = mat.GetTexture(BaseColorMap);
                if (tex == null) continue;
                Color own = AverageLinear(tex);
                var tint = new Color(Ratio(target.r, own.r), Ratio(target.g, own.g), Ratio(target.b, own.b), 1f);
                // SetColor ждёт цвет в гамме и сам переводит в линейное пространство.
                mat.SetColor("_BaseColor", tint.gamma);
                mat.SetFloat("_Smoothness", layer.smoothness);
                EditorUtility.SetDirty(mat);
                count++;
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[SummerMegascansSetup] Перекрашено материалов: {count}, цвет пепла (лин.) {target}");
        }

        static float Ratio(float target, float own) => Mathf.Clamp(target / Mathf.Max(own, 1e-4f), 0f, 2f);

        // Средний цвет текстуры в линейном пространстве: уменьшаем на GPU до 64×64 и усредняем.
        static Color AverageLinear(Texture tex)
        {
            const int size = 64;
            var rt = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            Graphics.Blit(tex, rt);
            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            var read = new Texture2D(size, size, TextureFormat.RGBAFloat, false, true);
            read.ReadPixels(new Rect(0, 0, size, size), 0, 0);
            read.Apply();
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
            Color sum = Color.clear;
            foreach (var c in read.GetPixels()) sum += c;
            Object.DestroyImmediate(read);
            return sum / (size * size);
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
