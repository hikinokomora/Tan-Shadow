using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using static ShadowTan.EditorTools.AutumnVegetationBake;
using Object = UnityEngine.Object;

namespace ShadowTan.EditorTools
{
    public static partial class AutumnTripoPropsStudio
    {
        static void Import(Prop asset, HashSet<string> retained)
        {
            string modelPath = $"{Folder}/SM_{asset.Name}.fbx";
            string colorPath = $"{Folder}/T_{asset.Name}_BaseColor.jpg";
            string normalPath = $"{Folder}/T_{asset.Name}_Normal.png";
            string download = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Downloads", asset.Archive + ".zip");

            if (File.Exists(download))
            {
                using var archive = ZipFile.OpenRead(download);
                WriteImported(modelPath, Entry(".fbx"));
                WriteImported(colorPath, Entry(".jpg", ".jpeg"));

                byte[] Entry(params string[] extensions)
                {
                    var entry = archive.Entries.Single(file =>
                        extensions.Any(extension => file.Name.EndsWith(extension, StringComparison.OrdinalIgnoreCase)));
                    using var source = entry.Open();
                    using var copy = new MemoryStream();
                    source.CopyTo(copy);
                    return copy.ToArray();
                }
            }

            if (!File.Exists(modelPath) || !File.Exists(colorPath))
                throw new FileNotFoundException($"Missing {download} and imported source assets for {asset.Name}.");

            var importer = (ModelImporter)AssetImporter.GetAtPath(modelPath);
            if (!Mathf.Approximately(importer.globalScale, 1f) || !importer.bakeAxisConversion ||
                importer.importNormals != ModelImporterNormals.Import || importer.importAnimation || importer.importBlendShapes ||
                !importer.isReadable || importer.materialImportMode != ModelImporterMaterialImportMode.None ||
                importer.importTangents != ModelImporterTangents.CalculateMikk)
            {
                importer.globalScale = 1f;
                importer.bakeAxisConversion = true;
                importer.importNormals = ModelImporterNormals.Import;
                importer.importTangents = ModelImporterTangents.CalculateMikk;
                importer.importAnimation = false;
                importer.importBlendShapes = false;
                importer.materialImportMode = ModelImporterMaterialImportMode.None;
                importer.isReadable = true;
                importer.SaveAndReimport();
            }

            var diffuse = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!ImageConversion.LoadImage(diffuse, File.ReadAllBytes(colorPath)))
                    throw new InvalidDataException($"Could not decode {colorPath}.");
                asset.Width = diffuse.width;
                asset.Height = diffuse.height;
                asset.Albedo = diffuse.GetPixels32();
            }
            finally
            {
                Object.DestroyImmediate(diffuse);
            }

            asset.Normal = Sobel(asset.Albedo, asset.Width, asset.Height, 0.42f);
            WritePng(normalPath, asset.Width, asset.Height, asset.Normal);
            var baseMap = ConfigureTexture(colorPath, false);
            var normal = ConfigureTexture(normalPath, true);
            Color tint = Linear(asset.Tint);
            if (asset.Kind == Kind.Boulder || asset.Kind == Kind.WallChunk)
                tint = Color.Lerp(tint, Linear("#78583D"), asset.Kind == Kind.Boulder ? 0.16f : 0.11f);

            asset.Material = MakeMaterial($"M_{asset.Name}", baseMap, normal, tint, asset.Smoothness, false);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            Mesh mesh = Normalize(model, asset.Kind);
            var reduced = Reduce(mesh, asset.Kind == Kind.DryGrassClump ? 0.24f : 0.40f);
            asset.Meshes = new Mesh[3];
            asset.Meshes[0] = SaveMesh($"{Folder}/SM_{asset.Name}_LOD0.asset", mesh, retained);
            asset.Meshes[1] = SaveMesh($"{Folder}/SM_{asset.Name}_LOD1.asset", reduced, retained);
            Debug.Log($"ShadowTan / Tripo import {asset.Kind}: {asset.Meshes[0].vertexCount:N0} imported vertices, " +
                      $"{asset.Meshes[0].GetIndexCount(0) / 3:N0} triangles, LOD1 {asset.Meshes[1].GetIndexCount(0) / 3:N0} triangles, " +
                      $"unity-space bounds {asset.Meshes[0].bounds.size}, source normals retained.");
        }

        static Mesh Normalize(GameObject model, Kind kind)
        {
            if (model == null) throw new InvalidOperationException("FBX failed to import.");
            var filters = model.GetComponentsInChildren<MeshFilter>(true);
            if (filters.Length != 1)
                throw new InvalidOperationException($"Expected a single textured mesh in {model.name}.");

            var filter = filters[0];
            var raw = filter.sharedMesh;
            if (!raw.HasVertexAttribute(VertexAttribute.Normal))
                throw new InvalidOperationException($"{model.name} did not include imported normals.");

            var modelFromMesh = Matrix4x4.Translate(-model.transform.position) * filter.transform.localToWorldMatrix;
            Vector3[] positions = raw.vertices;
            Vector3[] normals = raw.normals;
            var normalMatrix = modelFromMesh.inverse.transpose;
            for (int i = 0; i < positions.Length; i++)
            {
                positions[i] = modelFromMesh.MultiplyPoint3x4(positions[i]);
                normals[i] = normalMatrix.MultiplyVector(normals[i]).normalized;
            }

            Quaternion orientation = kind == Kind.BambooLog ? LayLog(positions) :
                kind == Kind.WallChunk ? Quaternion.Euler(-90f, 0f, 0f) : Quaternion.identity;
            for (int i = 0; i < positions.Length; i++)
            {
                positions[i] = orientation * positions[i];
                normals[i] = orientation * normals[i];
            }

            Bounds bounds = BoundsOf(positions);
            Vector3 pivot = bounds.center;
            if (kind != Kind.BambooLog) pivot.y = bounds.min.y;
            for (int i = 0; i < positions.Length; i++) positions[i] -= pivot;
            return Assemble(positions, normals, raw.uv, raw.triangles);
        }

        static Quaternion LayLog(Vector3[] points)
        {
            float bestHeight = float.PositiveInfinity;
            Quaternion best = Quaternion.identity;
            for (int angle = -90; angle <= 90; angle += 5)
            {
                var q = Quaternion.Euler(0f, 0f, angle);
                Vector3 axis = Quaternion.Inverse(q) * Vector3.up;
                float min = float.PositiveInfinity, max = float.NegativeInfinity;
                foreach (Vector3 p in points)
                {
                    float y = Vector3.Dot(axis, p);
                    min = Mathf.Min(min, y);
                    max = Mathf.Max(max, y);
                }

                if (max - min >= bestHeight) continue;
                bestHeight = max - min;
                best = q;
            }

            return best;
        }

        static Bounds BoundsOf(Vector3[] points)
        {
            var b = new Bounds(points[0], Vector3.zero);
            for (int i = 1; i < points.Length; i++) b.Encapsulate(points[i]);
            return b;
        }

        static Material MakeMaterial(string name, Texture baseMap, Texture normal, Color tint, float smoothness, bool cutout)
        {
            string path = $"{Folder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            var shader = Shader.Find("HDRP/Lit");
            if (shader == null) throw new InvalidOperationException("HDRP/Lit was not found.");
            if (material == null)
            {
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }

            material.SetTexture("_BaseColorMap", baseMap);
            material.SetColor("_BaseColor", tint);
            material.SetTexture("_NormalMap", normal);
            material.SetFloat("_NormalScale", 1f);
            material.SetFloat("_SurfaceType", 0f);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_AlphaCutoffEnable", cutout ? 1f : 0f);
            material.SetFloat("_AlphaCutoff", 0.44f);
            material.SetFloat("_DoubleSidedEnable", cutout ? 1f : 0f);
            material.SetFloat("_DoubleSidedNormalMode", 1f);
            material.enableInstancing = true;
            HDMaterial.ValidateMaterial(material);
            EditorUtility.SetDirty(material);
            return material;
        }

        static Texture2D ConfigureTexture(string path, bool normal, bool alpha = false, bool cutout = true)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            TextureImporterType type = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            var alphaSource = alpha ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
            if (importer.textureType != type || importer.sRGBTexture == normal || importer.maxTextureSize != 2048 ||
                importer.textureCompression != TextureImporterCompression.CompressedHQ ||
                importer.alphaSource != alphaSource || importer.alphaIsTransparency != alpha ||
                importer.mipMapsPreserveCoverage != (alpha && cutout) || importer.wrapMode != TextureWrapMode.Clamp)
            {
                importer.textureType = type;
                importer.sRGBTexture = !normal;
                importer.maxTextureSize = 2048;
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                importer.alphaSource = alphaSource;
                importer.alphaIsTransparency = alpha;
                importer.mipmapEnabled = true;
                importer.mipMapsPreserveCoverage = alpha && cutout;
                importer.alphaTestReferenceValue = 0.44f;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static Color32[] Sobel(Color32[] pixels, int width, int height, float strength)
        {
            var gray = new float[pixels.Length];
            var normal = new Color32[pixels.Length];
            for (int i = 0; i < pixels.Length; i++)
                gray[i] = (pixels[i].r * 0.2126f + pixels[i].g * 0.7152f + pixels[i].b * 0.0722f) / 255f;

            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                float a = Read(x - 1, y - 1), b = Read(x, y - 1), c = Read(x + 1, y - 1);
                float d = Read(x - 1, y), f = Read(x + 1, y);
                float g = Read(x - 1, y + 1), h = Read(x, y + 1), j = Read(x + 1, y + 1);
                float dx = c + 2f * f + j - a - 2f * d - g;
                float dy = g + 2f * h + j - a - 2f * b - c;
                var n = new Vector3(-dx * strength, -dy * strength, 1f).normalized;
                normal[x + y * width] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f);
            }

            return normal;
            float Read(int x, int y) => gray[Mathf.Clamp(x, 0, width - 1) + Mathf.Clamp(y, 0, height - 1) * width];
        }

        static void WritePng(string path, int width, int height, Color32[] pixels)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            try
            {
                texture.SetPixels32(pixels);
                texture.Apply();
                WriteImported(path, texture.EncodeToPNG());
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        static void WriteImported(string path, byte[] bytes)
        {
            if (File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes))
            {
                if (AssetImporter.GetAtPath(path) == null)
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                return;
            }
            File.WriteAllBytes(path, bytes);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        }
    }
}
