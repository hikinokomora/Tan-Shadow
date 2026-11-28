using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using static ShadowTan.EditorTools.AutumnVegetationBake;
using Object = UnityEngine.Object;

namespace ShadowTan.EditorTools
{
    public static class AutumnGddArt
    {
        const string Yellow = "#B0A15E";
        const string Green = "#33573E";
        const string Straw = "#5C392A";
        const string Sand = "#D8C18E";
        const float GrassScale = 1.43f;

        [MenuItem("ShadowTan/Setup/Art/Apply Autumn GDD Art")]
        public static void Apply() => Run("Autumn GDD Art", Apply);

        static void Apply(Transform props, Terrain terrain)
        {
            EnsureFolder(AutumnEntranceCave.Folder);
            ApplyAtlas("ForestBake_Culm", Generated + "/Forest/ForestBake_Culm.png", Yellow, 8, 0.102f, 0f);
            ApplyAtlas("ForestBake_Leaves", Generated + "/Forest/ForestBake_Leaves.png", Yellow, 4, 0.216f, 130f);
            ApplyAtlas("DenseGrass_Straw", Generated + "/DenseGrass/DenseGrass_Straw.png", Straw, 4, 0.30f, 190f, false);
            Tint("ForestBake_TrailSurface", Sand, 690f);
            Tint("M_Autumn_Metal", "#727B80", 0f);
            var forest = props.Find(AutumnDenseForest.RootName);
            var grass = props.Find("DenseGrass_Root");
            if (forest == null || grass == null)
                throw new InvalidOperationException("Apply the GDD palette after building the autumn forest and grass.");

            int scaled = 0;
            foreach (Transform chunk in grass)
            {
                if (!chunk.name.StartsWith("DenseGrass_Chunk_", StringComparison.Ordinal) ||
                    !chunk.TryGetComponent<LODGroup>(out var group)) continue;
                chunk.localScale = new Vector3(1f, GrassScale, 1f);
                group.size = 1.23f / GrassScale;
                scaled++;
            }

            var barriers = props.Find("Barriers_Generated");
            if (barriers != null) barriers.gameObject.SetActive(false);
            var batchedBarriers = props.Find("Barriers_Batched");
            if (batchedBarriers != null) batchedBarriers.gameObject.SetActive(false);
            ApplyArena(props, terrain);
            Atmosphere(props, forest);
            Debug.Log($"ShadowTan / Autumn GDD Art: palette updated, {scaled} existing grass chunks scaled, " +
                      "trail railings disabled in this scene; vegetation mesh assets retained.");
        }

        static void ApplyAtlas(string materialName, string source, string palette, int tiles, float luminanceReference,
            float fillNits, bool greenTiles = true)
        {
            var material = Load(materialName);
            var sourceTexture = AssetImporter.GetAtPath(source) as TextureImporter;
            if (sourceTexture == null) throw new InvalidOperationException($"Missing source atlas {source}.");

            Color hue = Linear(palette);
            Color remainingGreen = Linear(Green);
            string output = $"{AutumnEntranceCave.Folder}/Entrance_GDD_{materialName}.png";
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!ImageConversion.LoadImage(texture, File.ReadAllBytes(source)))
                    throw new InvalidOperationException($"Failed to decode {source}.");

                Color32[] pixels = texture.GetPixels32();
                int tileWidth = texture.width / tiles;
                for (int y = 0; y < texture.height; y++)
                {
                    for (int x = 0; x < texture.width; x++)
                    {
                        int index = y * texture.width + x;
                        Color32 pixel = pixels[index];
                        if (pixel.a == 0) continue;
                        Color color = ((Color)pixel).linear;
                        float luma = color.r * 0.2126f + color.g * 0.7152f + color.b * 0.0722f;
                        float gain = Mathf.Clamp(Mathf.Pow(luma / luminanceReference, 0.68f), 0.24f, 1f);
                        bool green = greenTiles && (x / tileWidth) % 4 == 0;
                        Color multiplier = green
                            ? new Color(remainingGreen.r / hue.r, remainingGreen.g / hue.g, remainingGreen.b / hue.b)
                            : Color.white;
                        Color replacement = (multiplier * gain).gamma;
                        replacement.a = pixel.a / 255f;
                        pixels[index] = replacement;
                    }
                }

                texture.SetPixels32(pixels);
                texture.Apply();
                byte[] png = texture.EncodeToPNG();
                if (!File.Exists(output) || !File.ReadAllBytes(output).AsSpan().SequenceEqual(png))
                {
                    File.WriteAllBytes(output, png);
                    AssetDatabase.ImportAsset(output, ImportAssetOptions.ForceSynchronousImport);
                }
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }

            var importer = (TextureImporter)AssetImporter.GetAtPath(output);
            if (importer.wrapMode != sourceTexture.wrapMode ||
                importer.alphaIsTransparency != sourceTexture.alphaIsTransparency ||
                importer.mipMapsPreserveCoverage != sourceTexture.mipMapsPreserveCoverage ||
                !Mathf.Approximately(importer.alphaTestReferenceValue, sourceTexture.alphaTestReferenceValue) ||
                importer.anisoLevel != 4 || importer.textureCompression != TextureImporterCompression.CompressedHQ)
            {
                importer.wrapMode = sourceTexture.wrapMode;
                importer.alphaIsTransparency = sourceTexture.alphaIsTransparency;
                importer.mipMapsPreserveCoverage = sourceTexture.mipMapsPreserveCoverage;
                importer.alphaTestReferenceValue = sourceTexture.alphaTestReferenceValue;
                importer.mipmapEnabled = true;
                importer.anisoLevel = 4;
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                importer.SaveAndReimport();
            }

            var atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(output);
            material.SetTexture("_BaseColorMap", atlas);
            material.SetColor("_BaseColor", hue);
            material.SetTexture("_EmissiveColorMap", fillNits > 0f ? atlas : null);
            material.SetColor("_EmissiveColor", hue * fillNits);
            HDMaterial.ValidateMaterial(material);
            EditorUtility.SetDirty(material);
        }

        static void Tint(string name, string hex, float existingEmission)
        {
            var material = Load(name);
            Color color = Linear(hex);
            material.SetColor("_BaseColor", color);
            if (existingEmission > 0f) material.SetColor("_EmissiveColor", color * existingEmission);
            HDMaterial.ValidateMaterial(material);
            EditorUtility.SetDirty(material);
        }

        static void ApplyArena(Transform props, Terrain terrain)
        {
            const string name = "AutumnGdd_ArenaSand";
            const float radius = 26f;
            const int sectors = 96;
            Vector3 center = new Vector3(CenterX, 0f, 188f);
            var geometry = new Geometry(center, 1);
            int origin = Vertex(center);
            int prior = Vertex(center + Vector3.right * radius);
            for (int i = 1; i <= sectors; i++)
            {
                float angle = i * (2f * Mathf.PI / sectors);
                int current = Vertex(center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius);
                geometry.Triangle(0, origin, current, prior);
                prior = current;
            }

            var saved = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
            Mesh mesh = geometry.MakeMesh();
            mesh.RecalculateTangents();
            Mesh surface = SaveMesh(AutumnEntranceCave.Folder + "/" + name + ".asset", mesh, saved);
            Render(Node(props.parent.Find("Ground"), name, center), surface,
                new[] { Load("Entrance_Sand") }, ShadowCastingMode.Off);

            Transform decor = props.Find("StoneDecor_Batched");
            if (decor != null) decor.gameObject.SetActive(false);

            var monument = props.Find("SM_Monument");
            if (monument != null)
            {
                var model = monument.GetComponentInChildren<MeshFilter>();
                if (model != null)
                {
                    Vector3 bounds = model.sharedMesh.bounds.size;
                    Vector3 scale = model.transform.localScale;
                    scale.y = scale.x * bounds.x / bounds.y;
                    model.transform.localScale = scale;
                }

                if (monument.TryGetComponent<BoxCollider>(out var box))
                {
                    Vector3 size = box.size;
                    size.z = size.x;
                    box.size = size;
                }
            }

            int Vertex(Vector3 position)
            {
                position.y = Ground(terrain, position) + 0.028f;
                return geometry.Vertex(position, Vector3.up, new Vector2(position.x / 3.4f, position.z / 3.4f));
            }
        }

        static void Atmosphere(Transform props, Transform forest)
        {
            var scene = props.gameObject.scene;
            var roots = scene.GetRootGameObjects();
            var global = Array.Find(roots, root => root.name == "GlobalVolume")?.GetComponent<Volume>();
            var profile = global != null ? global.sharedProfile : null;
            if (profile == null || !profile.TryGet(out VisualEnvironment environment) ||
                !profile.TryGet(out GradientSky sky) || !profile.TryGet(out Fog fog))
                throw new InvalidOperationException("Expected the existing HDRP GlobalVolume with GradientSky and Fog.");

            environment.skyType.Override((int)SkyType.Gradient);
            sky.active = true;
            sky.exposure.Override(11.4f);
            sky.top.Override(Linear("#A1C2DA"));
            sky.middle.Override(Linear("#C4DBE8"));
            sky.bottom.Override(Linear("#DBDECE"));
            if (profile.TryGet(out PhysicallyBasedSky physicalSky)) physicalSky.active = false;
            if (profile.TryGet(out Exposure exposure))
                exposure.fixedExposure.Override(11.1f);
            if (profile.TryGet(out ColorAdjustments grading))
                grading.colorFilter.Override(Color.white);

            Color haze = Color.Lerp(Linear("#DBDECE"), Linear(Yellow), 0.27f);
            ConfigureFog(fog, haze);
            fog.baseHeight.Override(0f);
            fog.maximumHeight.Override(69f);
            fog.meanFreePath.Override(92f);
            fog.depthExtent.Override(90f);
            foreach (var volume in forest.GetComponentsInChildren<Volume>(true))
            {
                if (volume.sharedProfile != null && volume.sharedProfile.TryGet(out Fog trailFog))
                    ConfigureFog(trailFog, haze);
            }

            var sun = Array.Find(roots, root => root.name == "[Atmosphere]")?.transform.Find("Sun")?.GetComponent<Light>();
            if (sun != null)
            {
                sun.transform.rotation = Quaternion.LookRotation(new Vector3(-0.60f, -0.48f, -0.63f));
                sun.intensity = 36000f;
                sun.color = Color.white;
                sun.useColorTemperature = true;
                sun.colorTemperature = 5500f;
            }

            var fill = Array.Find(roots, root => root.name == "[Atmosphere]")?.transform.Find("FillLight")?.GetComponent<Light>();
            if (fill != null) fill.intensity = 12000f;
            foreach (var component in profile.components) EditorUtility.SetDirty(component);
            EditorUtility.SetDirty(profile);
        }

        static void ConfigureFog(Fog fog, Color haze)
        {
            fog.enabled.Override(true);
            fog.colorMode.Override(FogColorMode.SkyColor);
            fog.color.Override(haze * 1250f);
            fog.tint.Override(Color.Lerp(Color.white, haze, 0.52f));
            fog.albedo.Override(haze);
            EditorUtility.SetDirty(fog);
        }

        static Material Load(string name) =>
            AssetDatabase.LoadAssetAtPath<Material>($"{Location}/Materials/{name}.mat") ??
            throw new InvalidOperationException($"Missing autumn material {name}.");
    }
}
