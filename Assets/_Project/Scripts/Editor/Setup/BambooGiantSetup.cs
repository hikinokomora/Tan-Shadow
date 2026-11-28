using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ShadowTan.EditorTools
{
    // Собирает Unity-версию Quixel Megaplant Giant Bamboo: материалы, LOD-префаб и песочницу.
    public static class BambooGiantSetup
    {
        const string ArtRoot = "Assets/_Project/Art/Environment/Autumn/Bamboo";
        const string ModelPath = ArtRoot + "/Models/SM_BambooGiant_01.fbx";
        const string MaterialRoot = ArtRoot + "/Materials";
        const string PrefabRoot = "Assets/_Project/Prefabs/Environment/Autumn";
        const string ScenePath = "Assets/_Project/Scenes/Dev/Sandbox_Autumn_Bamboo.unity";

        static readonly string[] Variants = { "A", "B", "C" };

        [MenuItem("ShadowTan/Setup/Art/Build Bamboo Giant")]
        public static void Build()
        {
            EnsureFolder(ArtRoot);
            EnsureFolder(ArtRoot + "/Models");
            EnsureFolder(MaterialRoot);
            EnsureFolder(PrefabRoot);
            EnsureFolder("Assets/_Project/Scenes/Dev");

            var shader = Shader.Find("HDRP/Lit");
            if (shader == null)
            {
                Debug.LogError("ShadowTan: не найден шейдер HDRP/Lit. Проверь render pipeline.");
                return;
            }

            AssetDatabase.Refresh();

            var barkDark = CreateTexturedMaterial(shader, "M_Bamboo_Culm_Dark", "T_Bamboo_Culm", false, false, new Color(0.45f, 0.55f, 0.35f));
            var barkLight = CreateTexturedMaterial(shader, "M_Bamboo_Culm_Light", "T_Bamboo_Culm", false, false, new Color(0.80f, 0.90f, 0.68f));
            var leafGreen = CreateTexturedMaterial(shader, "M_Bamboo_Leaf_Green", "T_Bamboo_Leaf_Surface", false, true, new Color(0.90f, 1f, 0.85f));
            var leafOlive = CreateTexturedMaterial(shader, "M_Bamboo_Leaf_Olive", "T_Bamboo_Leaf_Surface", false, true, new Color(0.80f, 0.95f, 0.60f));
            var leafYellow = CreateTexturedMaterial(shader, "M_Bamboo_Leaf_Yellow", "T_Bamboo_Leaf_Surface", false, true, new Color(0.75f, 0.90f, 0.50f));
            var ground = CreateMaterial(shader, "M_Sandbox_Ground", "#5A5A5A", 0.15f, false);
            AssetDatabase.SaveAssets();

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null)
            {
                Debug.LogError($"ShadowTan: не найден FBX {ModelPath}. Сначала импортируй его.");
                return;
            }

            // ---- prefabs (A/B/C) ----
            var mats = new[] { leafGreen, leafOlive, leafYellow, barkDark, barkLight };
            bool allBuilt = true;
            for (int i = 0; i < Variants.Length; i++)
            {
                allBuilt &= BuildVariant(model, Variants[i], mats, barkLight);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            if (!allBuilt)
            {
                Debug.LogError("ShadowTan: собраны не все варианты Bamboo Giant, песочница не создана.");
                return;
            }

            // ---- sandbox scene ----
            var prefabs = new GameObject[Variants.Length];
            var culmPrefabs = new GameObject[Variants.Length];
            for (int i = 0; i < Variants.Length; i++)
            {
                prefabs[i] = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(Variants[i]));
                culmPrefabs[i] = AssetDatabase.LoadAssetAtPath<GameObject>(CulmPrefabPath(Variants[i]));
            }
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);

            var groundGo = GameObject.CreatePrimitive(PrimitiveType.Plane);
            groundGo.name = "Ground";
            groundGo.layer = LayerMask.NameToLayer("Ground");
            groundGo.transform.position = new Vector3(0f, 0f, 20f);
            groundGo.transform.localScale = new Vector3(15f, 1f, 15f);
            groundGo.GetComponent<MeshRenderer>().sharedMaterial = ground;

            var sunGo = new GameObject("Sun");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 30000f;
            sun.color = new Color(1f, 0.93f, 0.82f);
            sun.useColorTemperature = true;
            sun.colorTemperature = 5200f;
            sun.shadows = LightShadows.Soft;
            sunGo.transform.rotation = Quaternion.Euler(35f, -35f, 0f);

            var camGo = new GameObject("SandboxCamera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 55f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 500f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.58f, 0.62f, 0.58f);
            camGo.AddComponent<AudioListener>();
            camGo.transform.position = new Vector3(0f, 9f, -32f);
            camGo.transform.LookAt(new Vector3(0f, 8f, 4f));

            var rnd = new System.Random(17);
            for (int row = 0; row < 2; row++)
            {
                for (int i = 0; i < 12; i++)
                {
                    var prefab = prefabs[rnd.Next(prefabs.Length)];
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                    float x = -22f + i * 4f + Range(rnd, -0.5f, 0.5f);
                    float z = row * 5f + Range(rnd, -0.4f, 0.4f);
                    go.transform.position = new Vector3(x, 0f, z);
                    go.transform.rotation = Quaternion.Euler(0f, Range(rnd, 0f, 360f), 0f);
                    go.transform.localScale = Vector3.one * Range(rnd, 0.85f, 1.10f);
                }
            }
            for (int i = 0; i < 10; i++)
            {
                var culmPrefab = culmPrefabs[rnd.Next(culmPrefabs.Length)];
                var go = (GameObject)PrefabUtility.InstantiatePrefab(culmPrefab, scene);
                go.transform.position = new Vector3(-22f + i * 4.5f + Range(rnd, -0.3f, 0.3f), 0f, 12f + Range(rnd, -0.4f, 0.4f));
                go.transform.rotation = Quaternion.Euler(0f, Range(rnd, 0f, 360f), 0f);
                go.transform.localScale = Vector3.one * Range(rnd, 0.9f, 1.1f);
            }

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorSceneManager.CloseScene(scene, true);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"ShadowTan: Bamboo Giant собран. Префабы: {PrefabRoot}/Bamboo_Giant_A|B|C.prefab, сцена: {ScenePath}");
        }

        static bool BuildVariant(GameObject model, string variant, Material[] mats, Material barkLight)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            instance.name = $"Bamboo_Giant_{variant}";
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            instance.transform.localScale = Vector3.one;

            var lod0 = FindDeep(instance.transform, variant + "_LOD0");
            var lod1 = FindDeep(instance.transform, variant + "_LOD1");
            var lod2 = FindDeep(instance.transform, variant + "_LOD2");
            var culm = FindDeep(instance.transform, variant + "_Culm");
            if (lod0 == null || lod1 == null || lod2 == null || culm == null)
            {
                Debug.LogError($"ShadowTan: в FBX не найдены {variant}_LOD0/{variant}_LOD1/{variant}_LOD2/{variant}_Culm.");
                Object.DestroyImmediate(instance);
                return false;
            }

            // The FBX contains all three variants under one root. Keep only this variant.
            for (int i = instance.transform.childCount - 1; i >= 0; i--)
            {
                var child = instance.transform.GetChild(i);
                var n = child.name;
                if (n == variant + "_LOD0" || n == variant + "_LOD1" || n == variant + "_LOD2" || n == variant + "_Culm")
                    continue;
                Object.DestroyImmediate(child.gameObject);
            }

            var r0 = Assign(lod0.GetComponent<MeshRenderer>(), mats);
            var r1 = Assign(lod1.GetComponent<MeshRenderer>(), mats);
            var r2 = Assign(lod2.GetComponent<MeshRenderer>(), mats);
            // culm mesh has 5 slots, faces use the bark ones
            Assign(culm.GetComponent<MeshRenderer>(), mats, barkLight);

            // culm-only prefab from a copy
            var culmGo = Object.Instantiate(culm.gameObject);
            culmGo.name = $"Bamboo_Giant_{variant}_Culm";
            culmGo.transform.SetParent(null);
            PrefabUtility.SaveAsPrefabAsset(culmGo, CulmPrefabPath(variant));
            Object.DestroyImmediate(culmGo);

            // the culm is duplicated inside LOD0..2, remove it from the LOD prefab
            Object.DestroyImmediate(culm.gameObject);

            var group = instance.GetComponent<LODGroup>();
            if (group == null) group = instance.AddComponent<LODGroup>();
            group.fadeMode = LODFadeMode.None;
            group.animateCrossFading = false;
            group.SetLODs(new[]
            {
                new LOD(0.50f, new Renderer[] { r0 }),
                new LOD(0.15f, new Renderer[] { r1 }),
                new LOD(0.04f, new Renderer[] { r2 })
            });
            group.RecalculateBounds();

            Debug.Log($"ShadowTan: Bamboo {variant} bounds LOD0={Describe(r0)} LOD1={Describe(r1)} LOD2={Describe(r2)}");
            PrefabUtility.SaveAsPrefabAsset(instance, PrefabPath(variant));

            Object.DestroyImmediate(instance);
            return true;
        }

        static string PrefabPath(string variant)
        {
            return $"{PrefabRoot}/Bamboo_Giant_{variant}.prefab";
        }

        static string CulmPrefabPath(string variant)
        {
            return $"{PrefabRoot}/Bamboo_Giant_{variant}_Culm.prefab";
        }

        [MenuItem("ShadowTan/Dev/Log Bamboo Mesh")]
        public static void LogBambooMesh()
        {
            var go = GameObject.Find("LOD0");
            if (go == null) { Debug.LogError("LOD0 not found in active scene"); return; }
            var mesh = go.GetComponent<MeshFilter>().sharedMesh;
            var renderer = go.GetComponent<MeshRenderer>();
            Debug.Log($"Bamboo LOD0 submeshes={mesh.subMeshCount} verts={mesh.vertexCount}");
            for (int i = 0; i < mesh.subMeshCount; i++)
            {
                var mat = i < renderer.sharedMaterials.Length && renderer.sharedMaterials[i] != null
                    ? renderer.sharedMaterials[i].name : "null";
                Debug.Log($"  submesh {i}: {mesh.GetTriangles(i).Length / 3} tris -> {mat}");
            }

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
            var modelLod0 = FindDeep(inst.transform, "LOD0");
            var modelRenderer = modelLod0 != null ? modelLod0.GetComponent<MeshRenderer>() : null;
            if (modelRenderer != null)
            {
                var names = new System.Collections.Generic.List<string>();
                foreach (var m in modelRenderer.sharedMaterials)
                    names.Add(m != null ? m.name : "null");
                Debug.Log("FBX LOD0 materials: " + string.Join(", ", names));
            }
            Object.DestroyImmediate(inst);
        }

        static MeshRenderer Assign(MeshRenderer renderer, Material[] mats, Material unmatched = null)
        {
            if (renderer == null) return null;
            var source = renderer.sharedMaterials;
            var arr = new Material[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                var name = source[i] != null ? source[i].name : string.Empty;
                Material match = null;
                for (int j = 0; j < mats.Length; j++)
                {
                    if (mats[j] != null && mats[j].name == name)
                    {
                        match = mats[j];
                        break;
                    }
                }
                arr[i] = match != null ? match : (unmatched != null ? unmatched : mats[Mathf.Min(i, mats.Length - 1)]);
            }
            renderer.sharedMaterials = arr;
            return renderer;
        }

        static string Describe(Renderer r)
        {
            if (r == null) return "null";
            var b = r.bounds;
            return $"center={b.center} size={b.size}";
        }

        static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var found = FindDeep(root.GetChild(i), name);
                if (found != null) return found;
            }
            return null;
        }

        static float Range(System.Random rnd, float min, float max)
        {
            return min + (float)rnd.NextDouble() * (max - min);
        }

        static Material CreateTexturedMaterial(Shader shader, string name, string texPrefix, bool alpha, bool doubleSided, Color tint)
        {
            var texRoot = ArtRoot + "/Textures";
            var basePath = $"{texRoot}/{texPrefix}_BaseColor.png";
            var normalPath = $"{texRoot}/{texPrefix}_Normal.png";
            var maskPath = $"{texRoot}/{texPrefix}_Mask.png";
            ConfigureTexture(basePath, false, false, alpha);
            ConfigureTexture(normalPath, true, true, false);
            ConfigureTexture(maskPath, false, true, false);

            var baseTex = AssetDatabase.LoadAssetAtPath<Texture2D>(basePath);
            var normalTex = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
            var maskTex = AssetDatabase.LoadAssetAtPath<Texture2D>(maskPath);

            var path = $"{MaterialRoot}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = shader;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", tint);
            if (mat.HasProperty("_BaseColorMap")) mat.SetTexture("_BaseColorMap", baseTex);
            if (mat.HasProperty("_NormalMap")) mat.SetTexture("_NormalMap", normalTex);
            if (mat.HasProperty("_MaskMap")) mat.SetTexture("_MaskMap", maskTex);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 1f);
            if (mat.HasProperty("_SmoothnessRemapMin")) mat.SetFloat("_SmoothnessRemapMin", 0f);
            if (mat.HasProperty("_SmoothnessRemapMax")) mat.SetFloat("_SmoothnessRemapMax", 1f);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0f);
            if (mat.HasProperty("_UVBase")) mat.SetFloat("_UVBase", 0f);
            if (mat.HasProperty("_NormalScale")) mat.SetFloat("_NormalScale", 1f);
            if (doubleSided)
            {
                if (mat.HasProperty("_DoubleSidedEnable")) mat.SetFloat("_DoubleSidedEnable", 1f);
                if (mat.HasProperty("_DoubleSidedNormalMode")) mat.SetFloat("_DoubleSidedNormalMode", 0f);
                if (mat.HasProperty("_CullMode")) mat.SetFloat("_CullMode", 0f);
                if (mat.HasProperty("_CullModeForward")) mat.SetFloat("_CullModeForward", 0f);
            }
            if (alpha)
            {
                if (mat.HasProperty("_AlphaCutoffEnable")) mat.SetFloat("_AlphaCutoffEnable", 1f);
                if (mat.HasProperty("_AlphaCutoff")) mat.SetFloat("_AlphaCutoff", 0.4f);
                if (mat.HasProperty("_AlphaCutoffShadow")) mat.SetFloat("_AlphaCutoffShadow", 0.4f);
                if (mat.HasProperty("_SurfaceType")) mat.SetFloat("_SurfaceType", 0f);
                if (mat.HasProperty("_BlendMode")) mat.SetFloat("_BlendMode", 0f);
                if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 1f);
                if (mat.HasProperty("_CullMode")) mat.SetFloat("_CullMode", 0f);
                if (mat.HasProperty("_CullModeForward")) mat.SetFloat("_CullModeForward", 0f);
                if (mat.HasProperty("_DoubleSidedEnable")) mat.SetFloat("_DoubleSidedEnable", 1f);
                if (mat.HasProperty("_DoubleSidedNormalMode")) mat.SetFloat("_DoubleSidedNormalMode", 0f);
                mat.EnableKeyword("_ALPHATEST_ON");
            }
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static void ConfigureTexture(string path, bool normal, bool linear, bool alpha)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                Debug.LogWarning($"ShadowTan: нет TextureImporter для {path}");
                return;
            }
            importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = !linear;
            importer.alphaSource = alpha ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
            importer.alphaIsTransparency = alpha;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.mipmapEnabled = true;
            importer.maxTextureSize = 4096;
            importer.SaveAndReimport();
        }

        static Material CreateMaterial(Shader shader, string name, string hex, float smoothness, bool doubleSided)
        {
            var path = $"{MaterialRoot}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = shader;
            ColorUtility.TryParseHtmlString(hex, out var color);
            if (QualitySettings.activeColorSpace == ColorSpace.Linear)
                color = color.linear;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0f);
            if (mat.HasProperty("_DoubleSidedEnable")) mat.SetFloat("_DoubleSidedEnable", doubleSided ? 1f : 0f);
            if (mat.HasProperty("_DoubleSidedNormalMode")) mat.SetFloat("_DoubleSidedNormalMode", 0f);
            if (mat.HasProperty("_CullMode")) mat.SetFloat("_CullMode", doubleSided ? 0f : 2f);
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            var name = Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }
    }
}
