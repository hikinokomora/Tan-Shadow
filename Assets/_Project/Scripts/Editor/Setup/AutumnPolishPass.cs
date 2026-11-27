using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ShadowTan.Gameplay.Autumn;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;
using static ShadowTan.EditorTools.AutumnVegetationBake;
using Object = UnityEngine.Object;
using Random = System.Random;

namespace ShadowTan.EditorTools
{
    public static class AutumnPolishPass
    {
        const string Folder = Generated + "/ScenePolish";
        const string GroupName = "Autumn_ScenePolish";
        const string EntranceName = "Entrance_Polish";
        const string TerrainFolder = Location + "/Terrain/";
        const string Materials = Location + "/Materials/";

        [MenuItem("ShadowTan/Setup/Art/Polish Autumn Scene")]
        public static void Apply()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Open Autumn_Gameplay in Edit Mode.");

            Transform greybox = Require(Root(scene, "[Level]"), "Greybox");
            Transform props = Require(greybox, "Props");
            Transform walls = Require(greybox, "Walls");
            Transform entrance = Require(greybox, "Entrance_Fix");
            Transform art = Require(entrance, EntranceName);
            Transform forest = Require(props, "ForestBake_Root");
            Terrain terrain = Require(greybox, "Ground/AutumnTerrain").GetComponent<Terrain>();
            Transform bank = Require(art, "ForestFloorAtEntrance");
            var bankMesh = bank.GetComponent<MeshFilter>();
            Transform atmosphere = Root(scene, "[Atmosphere]");
            var global = Root(scene, "GlobalVolume").GetComponent<Volume>();
            var trailFog = Require(forest, "ForestBake_TrailAtmosphere").GetComponent<Volume>();
            var rockySoil = Load<Material>(Materials + "Entrance_Sand.mat");
            var wildGrass = Load<TerrainLayer>(TerrainFolder + "TL_Autumn_Grass.terrainlayer");
            var grassMacro = Load<TerrainLayer>(TerrainFolder + "TL_Autumn_Grass_B.terrainlayer");

            Undo.IncrementCurrentGroup();
            int undo = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Polish Autumn Scene");
            try
            {
                EnsureFolder(Folder);
                SetForestCollision(walls);
                RemoveExactDuplicates(props);
                BakeTerrain(terrain, wildGrass, grassMacro, rockySoil);
                SetVisible(Require(props, "TripoProp_Root/TripoProp_ContactPatches").GetComponent<Renderer>(), false);
                SetVisible(Require(forest, "ForestBake_TrailSurface").GetComponent<Renderer>(), false);
                SetVisible(Require(greybox, "Ground/AutumnGdd_ArenaSand").GetComponent<Renderer>(), false);

                foreach (Transform item in forest)
                    if (item.name.StartsWith("ForestBake_Depth_", StringComparison.Ordinal))
                        SetVisible(item.GetComponent<Renderer>(), false);

                var bankSurface = Variant("EntranceBank", Generated + "/EntrancePolish/ForestFloor.mat", material =>
                {
                    material.SetColor("_BaseColor", new Color(0.62f, 0.69f, 0.52f));
                    material.SetFloat("_NormalScale", 0.36f);
                    material.SetFloat("_SmoothnessRemapMax", 0.13f);
                    material.SetFloat("_SmoothnessRemapMin", 0.03f);
                });
                Replace(bank.GetComponent<Renderer>(), bankSurface);

                var culms = Variant("BambooCulms", Materials + "ForestBake_Culm.mat", material =>
                {
                    material.SetColor("_BaseColor", Linear("#A3A166"));
                    material.SetFloat("_Smoothness", 0.20f);
                });
                var leaves = Variant("BambooLeaves", Materials + "ForestBake_Leaves.mat", material =>
                {
                    var tint = Linear("#A4A46B");
                    material.SetColor("_BaseColor", tint);
                    material.SetFloat("_DoubleSidedEnable", 1f);
                    material.SetFloat("_DoubleSidedNormalMode", 1f);
                    material.SetColor("_EmissiveColor", tint * 320f);
                    material.doubleSidedGI = true;
                });
                var straw = Variant("DryGrass", Materials + "DenseGrass_Straw.mat", material =>
                {
                    material.SetColor("_BaseColor", Linear("#796345"));
                    material.SetFloat("_Smoothness", 0.12f);
                });

                ReplaceUses(forest, "ForestBake_Culm", culms);
                ReplaceUses(forest, "ForestBake_Leaves", leaves);
                ReplaceUses(art, "ForestBake_Culm", culms);
                ReplaceUses(art, "ForestBake_Leaves", leaves);
                Transform clearing = Require(greybox, "Entrance_Root/Entrance_Clearing");
                ReplaceUses(clearing, "ForestBake_Culm", culms);
                ReplaceUses(clearing, "ForestBake_Leaves", leaves);
                ReplaceUses(Require(props, "DenseGrass_Root"), "DenseGrass_Straw", straw);

                var wallMaterial = Variant("PortalWall", Materials + "EntranceFix_Wall.mat", material =>
                {
                    material.SetColor("_BaseColor", Linear("#92938A"));
                    material.SetFloat("_NormalScale", 0.57f);
                    material.SetFloat("_SmoothnessRemapMax", 0.27f);
                });
                ReplaceUses(entrance, "EntranceFix_Wall", wallMaterial);
                var iron = Variant("MonumentMetal", Materials + "M_Autumn_Metal.mat", material =>
                {
                    material.SetColor("_BaseColor", Linear("#818C96"));
                    material.SetFloat("_NormalScale", 0.46f);
                    material.SetFloat("_SmoothnessRemapMax", 0.40f);
                });
                ReplaceUses(Require(props, "SM_Monument"), "M_Autumn_Metal", iron);

                Transform output = RebuildGroup(props, GroupName);
                BatchDecals(Require(props, "GroundDecals"), output, terrain);
                SeatContactPatches(Require(props, "TripoProp_Root/TripoProp_ContactPatches"), output, terrain);
                DressMargins(output, terrain, bankMesh, culms, leaves);
                Transform moments = Require(Root(scene, "[Flow]"), "Triggers/Entrance_Moments");
                DressAir(output, Require(moments, "LooseStonesOnSlope"));
                var glints = Variant("BaihuGlints", Generated + "/EntrancePolish/EyeGlow.mat", material =>
                {
                    material.SetFloat("_DoubleSidedEnable", 1f);
                    material.SetColor("_UnlitColor", new Color(0.84f, 0.012f, 0.01f));
                    material.SetColor("_EmissiveColor", new Color(2.15f, 0.013f, 0.009f));
                });
                Replace(Require(moments, "BaihuEyes/EyeGlints").GetComponent<Renderer>(), glints);
                SetAtmosphere(scene, atmosphere, entrance, global, trailFog);

                Undo.FlushUndoRecordObjects();
                AssetDatabase.SaveAssets();
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene))
                    throw new IOException($"Could not save {scene.path}.");
                Debug.Log("Autumn polish saved. Terrain heightmap and route collision retained.");
            }
            catch
            {
                Undo.RevertAllDownToGroup(undo);
                throw;
            }
            finally
            {
                Undo.CollapseUndoOperations(undo);
            }
        }

        static void BakeTerrain(Terrain terrain, TerrainLayer wildGrass, TerrainLayer macro, Material soil)
        {
            TerrainData data = terrain.terrainData;
            var grass = Layer("WildGrassDetail", wildGrass, 7.1f, new Vector2(0.37f, 0.61f),
                new Vector4(0.92f, 0.36f, 0.72f, 1f), 0.46f);
            var dry = Layer("WildGrassMacro", macro, 19.4f, new Vector2(2.1f, 3.6f),
                new Vector4(0.75f, 0.59f, 0.62f, 1f), 0.26f,
                diffuse: Load<Texture2D>(Location + "/Textures/T_WildGrass_BaseColor_Bright.png"));
            var track = Layer("TroddenEarth", wildGrass, 4.4f, new Vector2(0.49f, 1.9f),
                new Vector4(0.54f, 0.46f, 0.33f, 1f), 0.34f, soil);
            var arena = Layer("ArenaSand", wildGrass, 10.3f, new Vector2(2.7f, 0.24f),
                new Vector4(0.76f, 0.67f, 0.53f, 1f), 0.23f, soil);

            int width = data.alphamapWidth;
            int height = data.alphamapHeight;
            var weights = new float[height, width, 4];
            Vector3 origin = terrain.transform.position, size = data.size;
            for (int iz = 0; iz < height; iz++)
            {
                float z = origin.z + size.z * iz / (height - 1f);
                for (int ix = 0; ix < width; ix++)
                {
                    float x = origin.x + size.x * ix / (width - 1f);
                    float offset = x - CenterX;
                    float patch = Mathf.PerlinNoise(x * 0.13f + 14f, z * 0.15f + 23f);
                    float broadPatch = Mathf.PerlinNoise(x * 0.052f + 31f, z * 0.055f + 17f);
                    float trailCenter = Mathf.Sin(z * 0.27f) * 0.13f;
                    float tread = 1f - Fade(0.57f, 1.56f + 0.44f * (patch - 0.5f), Mathf.Abs(offset - trailCenter));
                    tread *= Fade(-7.2f, -3.4f, z) * (1f - Fade(158f, 168f, z));
                    tread *= Mathf.Lerp(0.70f, 0.96f, patch);
                    float clearing = Mathf.Sqrt(Mathf.Pow((offset + 0.42f) / 6.1f, 2f) +
                        Mathf.Pow((z - 0.6f) / 9.5f, 2f));
                    float wear = (1f - Fade(0.17f, 1.15f, clearing + (broadPatch - 0.5f) * 0.33f)) *
                        Mathf.Lerp(0.28f, 0.64f, patch);
                    tread = Mathf.Max(tread, wear);

                    float distance = Mathf.Sqrt(offset * offset + (z - 188f) * (z - 188f));
                    float sand = 1f - Fade(24.1f, 27.0f, distance + (patch - 0.5f) * 0.85f);
                    float sandDetail = sand * Mathf.Lerp(0.09f, 0.21f, broadPatch);
                    float grassWeight = (1f - sand) * (1f - tread);
                    float dryBlend = Mathf.Lerp(0.16f, 0.72f, broadPatch * 0.74f + patch * 0.26f);

                    weights[iz, ix, 0] = grassWeight * (1f - dryBlend);
                    weights[iz, ix, 1] = grassWeight * dryBlend;
                    weights[iz, ix, 2] = tread * (1f - sand) + sandDetail;
                    weights[iz, ix, 3] = sand - sandDetail;
                }
            }

            Undo.RegisterCompleteObjectUndo(data, "Paint autumn terrain");
            data.terrainLayers = new[] { grass, dry, track, arena };
            data.SetAlphamaps(0, 0, weights);
            EditorUtility.SetDirty(data);
            terrain.Flush();
        }

        static TerrainLayer Layer(string name, TerrainLayer source, float tile, Vector2 offset,
            Vector4 tint, float normal, Material replacement = null, Texture2D diffuse = null)
        {
            string path = Folder + "/" + name + ".terrainlayer";
            TerrainLayer layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
            if (layer == null)
            {
                layer = Object.Instantiate(source);
                AssetDatabase.CreateAsset(layer, path);
            }
            Undo.RecordObject(layer, "Update " + name);
            layer.name = name;
            layer.tileSize = Vector2.one * tile;
            layer.tileOffset = offset;
            layer.diffuseTexture = diffuse ?? (replacement == null ? source.diffuseTexture : replacement.GetTexture("_BaseColorMap") as Texture2D);
            layer.normalMapTexture = replacement == null ? source.normalMapTexture : replacement.GetTexture("_NormalMap") as Texture2D;
            layer.maskMapTexture = null;
            layer.diffuseRemapMin = Vector4.zero;
            layer.diffuseRemapMax = tint;
            layer.normalScale = normal;
            layer.metallic = 0f;
            layer.smoothness = 0.09f;
            EditorUtility.SetDirty(layer);
            return layer;
        }

        static void SetForestCollision(Transform walls)
        {
            foreach (var name in new[] { "BambooWalls", "BambooRing" })
            foreach (var renderer in Require(walls, name).GetComponentsInChildren<Renderer>(true))
                SetVisible(renderer, false);
        }

        static void RemoveExactDuplicates(Transform props)
        {
            var duplicates = new Regex(@"^(Foliage_DryGrass_Batched|StoneDecor_Batched|GroundDecals|StoneCurbs|BambooEdge) \(\d+\)$");
            foreach (Transform copy in props.Cast<Transform>().Where(t => duplicates.IsMatch(t.name)).ToArray())
            {
                Transform original = props.Find(duplicates.Match(copy.name).Groups[1].Value);
                if (original == null || original.childCount != copy.childCount) continue;
                MeshFilter[] originalMeshes = original.GetComponentsInChildren<MeshFilter>(true);
                MeshFilter[] copiedMeshes = copy.GetComponentsInChildren<MeshFilter>(true);
                if (originalMeshes.Length != copiedMeshes.Length ||
                    !originalMeshes.Zip(copiedMeshes, (a, b) => a.sharedMesh == b.sharedMesh &&
                        a.transform.localToWorldMatrix == b.transform.localToWorldMatrix).All(equal => equal))
                    continue;
                if (copy.GetComponentsInChildren<Collider>(true).Length == 0)
                    Undo.DestroyObjectImmediate(copy.gameObject);
            }
        }

        static void BatchDecals(Transform source, Transform output, Terrain terrain)
        {
            var groups = source.GetComponentsInChildren<MeshFilter>(true).Where(filter =>
                filter.sharedMesh != null && filter.GetComponent<MeshRenderer>()?.sharedMaterial != null).
                GroupBy(filter => Mathf.FloorToInt(filter.transform.position.z / 18f));
            foreach (var group in groups)
            {
                Vector3 origin = new Vector3(CenterX, 0f, group.Key * 18f + 9f);
                var subsets = group.GroupBy(filter => filter.GetComponent<MeshRenderer>().sharedMaterial)
                    .OrderBy(set => set.Key.name, StringComparer.Ordinal).ToArray();
                var geometry = new Geometry(origin, subsets.Length);
                for (int slot = 0; slot < subsets.Length; slot++)
                foreach (MeshFilter filter in subsets[slot])
                {
                    Mesh mesh = filter.sharedMesh;
                    Vector3[] positions = mesh.vertices;
                    Vector2[] uv = mesh.uv;
                    var indices = new int[positions.Length];
                    for (int i = 0; i < positions.Length; i++)
                    {
                        Vector3 point = filter.transform.TransformPoint(positions[i]);
                        point.y = Ground(terrain, point) + 0.014f;
                        indices[i] = geometry.Vertex(point, Vector3.up, uv[i]);
                    }
                    int[] triangles = mesh.triangles;
                    for (int i = 0; i < triangles.Length; i += 3)
                        geometry.Triangle(slot, indices[triangles[i]], indices[triangles[i + 1]], indices[triangles[i + 2]]);
                    SetVisible(filter.GetComponent<MeshRenderer>(), false);
                }
                var meshBatch = geometry.MakeMesh();
                meshBatch.RecalculateTangents();
                SaveRender(output, $"Litter_{group.Key:00}", origin, meshBatch, subsets.Select(set => set.Key).ToArray());
            }
        }

        static void DressMargins(Transform output, Terrain terrain, MeshFilter bank, Material culms, Material leaves)
        {
            var random = new Random(82731);
            var groves = new Geometry(Vector3.zero, 2);
            var foliage = new Geometry(Vector3.zero, 1);
            var dryGrass = new Geometry(Vector3.zero, 1);
            Mesh mesh = bank.sharedMesh;
            Vector3[] groundPoints = mesh.vertices.Select(bank.transform.TransformPoint).ToArray();
            int[] groundTriangles = mesh.triangles;

            Vector3 Site(float x, float z)
            {
                Vector3 point = new Vector3(CenterX + x, 0f, z);
                point.y = Ground(terrain, point);
                if (z < -6.73f || z > 13.6f || Mathf.Abs(x) > 12.5f) return point;
                for (int i = 0; i < groundTriangles.Length; i += 3)
                {
                    Vector3 a = groundPoints[groundTriangles[i]];
                    Vector3 b = groundPoints[groundTriangles[i + 1]] - a;
                    Vector3 c = groundPoints[groundTriangles[i + 2]] - a;
                    float determinant = b.x * c.z - c.x * b.z;
                    if (Mathf.Abs(determinant) < 0.0001f) continue;
                    float dx = point.x - a.x, dz = point.z - a.z;
                    float u = (dx * c.z - dz * c.x) / determinant;
                    float v = (b.x * dz - b.z * dx) / determinant;
                    if (u < 0f || v < 0f || u + v > 1f) continue;
                    point.y = Mathf.Max(point.y, a.y + u * b.y + v * c.y);
                    break;
                }
                return point;
            }

            // The shallow pockets keep the sightline open and bring cover down to eye level.
            Vector2[] pockets = {
                new Vector2(-5.9f, -4.5f), new Vector2(4.65f, -3.8f), new Vector2(-4.35f, -0.55f),
                new Vector2(4.05f, 0.9f), new Vector2(-5.4f, 4.3f), new Vector2(4.25f, 5.45f),
                new Vector2(-3.75f, 8.65f), new Vector2(4.6f, 10.2f), new Vector2(-2.65f, 12.7f)
            };
            foreach (Vector2 pocket in pockets)
            {
                float side = Mathf.Sign(pocket.x);
                var outward = new Vector3(side, 0f, 0f);
                for (int i = 0; i < 6; i++)
                {
                    float x = pocket.x + Range(random, -0.56f, 0.56f);
                    float z = pocket.y + Range(random, -0.67f, 0.67f);
                    Vector3 foot = Site(x, z) - Vector3.up * 0.045f;
                    Vector3 lean = new Vector3(Range(random, -0.74f, 0.58f) * side, 0f, Range(random, -0.51f, 0.57f));
                    AutumnDenseForest.AddEntrancePlant(groves, foot, outward, lean,
                        Range(random, 2.85f, 5.1f), Range(random, 0.030f, 0.058f), random);
                }
                for (int i = 0; i < 5; i++)
                {
                    float x = pocket.x - side * Range(random, 0.45f, 1.12f);
                    float z = pocket.y + Range(random, -0.85f, 0.95f);
                    Vector3 foot = Site(x, z) - Vector3.up * 0.025f;
                    AutumnEntrancePolish.Tuft(dryGrass, foot, Range(random, 0.23f, 0.59f), random);
                    if (i % 2 == 0) AutumnEntrancePolish.Fern(foliage, foot, Range(random, 0.28f, 0.59f), random);
                }
            }
            SaveRender(output, "EntranceUnderstory", Vector3.zero, groves.MakeMesh(), new[] { culms, leaves });
            SaveRender(output, "EntranceFerns", Vector3.zero, foliage.MakeMesh(),
                new[] { Load<Material>(Generated + "/EntrancePolish/Foliage.mat") });
            SaveRender(output, "EntranceStraw", Vector3.zero, dryGrass.MakeMesh(),
                new[] { Load<Material>(Generated + "/EntrancePolish/Straw.mat") });

            for (int start = 0; start < 3; start++)
            {
                float z0 = 12.1f + start * 46f;
                Vector3 origin = new Vector3(CenterX, 0, z0 + 24f);
                var arch = new Geometry(origin, 2);
                for (int i = 0; i < 12; i++)
                {
                    float side = i % 2 == 0 ? -1f : 1f;
                    float z = z0 + i * 3.9f + Range(random, 0.1f, 2f);
                    Vector3 foot = Site(side * Range(random, 2.08f, 2.61f), z) - Vector3.up * 0.09f;
                    AutumnDenseForest.AddEntrancePlant(arch, foot, new Vector3(-side, 0f, 0f),
                        new Vector3(-side * Range(random, 2.5f, 3.9f), 0f, Range(random, -0.69f, 0.67f)),
                        Range(random, 8.5f, 12f), Range(random, 0.041f, 0.071f), random);
                }
                SaveRender(output, $"Canopy_{start}", origin, arch.MakeMesh(), new[] { culms, leaves });
            }

            Seat("Boulder", "EntranceStone", new[] {
                new Vector3(-3.07f, 0.57f, -2.31f), new Vector3(3.64f, 0.73f, -1.63f),
                new Vector3(-4.24f, 0.84f, 2.75f), new Vector3(4.87f, 0.49f, 4.05f),
                new Vector3(-4.79f, 0.48f, 3.20f), new Vector3(3.15f, 0.59f, 2.32f)
            });
            Seat("BambooLog", "BrokenBamboo", new[] {
                new Vector3(4.04f, 0.94f, 4.33f), new Vector3(-4.83f, 0.68f, 5.80f),
                new Vector3(3.28f, 0.71f, 9.38f)
            });

            void Seat(string asset, string label, Vector3[] positions)
            {
                var source = Load<Mesh>($"{Generated}/Tripo/SM_Autumn_{asset}_LOD1.asset");
                var material = Load<Material>($"{Generated}/Tripo/M_Autumn_{asset}.mat");
                Vector3 center = new Vector3(CenterX, 0f, 3f);
                var instances = new CombineInstance[positions.Length];
                for (int i = 0; i < positions.Length; i++)
                {
                    Vector3 point = Site(positions[i].x, positions[i].z);
                    float span = Mathf.Max(source.bounds.size.x, Mathf.Max(source.bounds.size.y, source.bounds.size.z));
                    float scale = positions[i].y / span;
                    var rotation = Quaternion.Euler(0f, Range(random, 0f, 360f), 0f);
                    var baseCenter = new Vector3(source.bounds.center.x, source.bounds.min.y, source.bounds.center.z);
                    point -= Vector3.up * 0.035f + rotation * (baseCenter * scale);
                    instances[i] = new CombineInstance
                    {
                        mesh = source,
                        transform = Matrix4x4.TRS(point - center, rotation, Vector3.one * scale)
                    };
                }
                var combined = new Mesh();
                combined.CombineMeshes(instances);
                SaveRender(output, label, center, combined, new[] { material });
            }
        }

        static void SeatContactPatches(Transform source, Transform output, Terrain terrain)
        {
            var filter = source.GetComponent<MeshFilter>();
            Mesh mesh = filter.sharedMesh;
            Vector3[] points = mesh.vertices;
            Vector2[] uv = mesh.uv;
            var geometry = new Geometry(Vector3.zero, 1);
            for (int i = 0; i < points.Length; i += 4)
            {
                var a = source.TransformPoint(points[i]);
                var b = source.TransformPoint(points[i + 1]);
                var c = source.TransformPoint(points[i + 2]);
                var d = source.TransformPoint(points[i + 3]);
                if (Mathf.Min(a.z, b.z, c.z, d.z) < 2.3f ||
                    Mathf.Max(Gap(a), Gap(b), Gap(c), Gap(d)) > 0.08f) continue;
                a.y = Ground(terrain, a) + 0.018f;
                b.y = Ground(terrain, b) + 0.018f;
                c.y = Ground(terrain, c) + 0.018f;
                d.y = Ground(terrain, d) + 0.018f;
                geometry.Quad(0, a, b, c, d, Rect.MinMaxRect(uv[i].x, uv[i].y, uv[i + 2].x, uv[i + 2].y));
            }
            SaveRender(output, "GroundContact", Vector3.zero, geometry.MakeMesh(),
                new[] { source.GetComponent<Renderer>().sharedMaterial });

            float Gap(Vector3 point) => Mathf.Abs(point.y - Ground(terrain, point));
        }

        static void DressAir(Transform output, Transform stoneCue)
        {
            var motes = Variant("EntranceMotes", Materials + "M_Autumn_LeafParticle.mat",
                material => material.SetColor("_UnlitColor", new Color(0.76f, 0.66f, 0.44f, 0.34f)));
            Configure("EntranceMotes", new Vector3(CenterX, 1.85f, -4.45f),
                new Vector3(6.3f, 2.5f, 3.2f), true);
            ParticleSystem grit = Configure("RampDust", new Vector3(CenterX + 1.92f, 0.38f, -2.55f),
                new Vector3(0.8f, 0.12f, 1.2f), false);
            var cue = new SerializedObject(stoneCue.GetComponent<AutumnEntranceMoments>());
            cue.FindProperty("particles").objectReferenceValue = grit;
            cue.ApplyModifiedProperties();

            ParticleSystem Configure(string name, Vector3 position, Vector3 size, bool ambient)
            {
                var node = Node(output, name, position);
                Undo.RegisterCreatedObjectUndo(node.gameObject, "Create entrance particles");
                node.rotation = Quaternion.LookRotation(new Vector3(0.22f, 0.6f, 1));
                var system = node.gameObject.AddComponent<ParticleSystem>();
                system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = system.main;
                main.playOnAwake = ambient;
                main.loop = ambient;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.maxParticles = 16;
                main.startLifetime = ambient ? new ParticleSystem.MinMaxCurve(1.8f, 3.2f) :
                    new ParticleSystem.MinMaxCurve(0.35f, 0.75f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(0.14f, 0.42f);
                main.startSize = ambient ? new ParticleSystem.MinMaxCurve(0.022f, 0.049f) :
                    new ParticleSystem.MinMaxCurve(0.028f, 0.056f);
                var emission = system.emission;
                emission.enabled = ambient;
                emission.rateOverTime = 3f;
                var shape = system.shape;
                shape.shapeType = ParticleSystemShapeType.Box;
                shape.scale = size;
                var renderer = node.GetComponent<ParticleSystemRenderer>();
                renderer.sharedMaterial = motes;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.renderMode = ParticleSystemRenderMode.Billboard;
                return system;
            }
        }

        static void SetAtmosphere(Scene scene, Transform atmosphere, Transform entrance, Volume global, Volume trailFog)
        {
            var sun = Require(atmosphere, "Sun").GetComponent<Light>();
            TuneLight(sun, 42200f, new Color(1f, 0.965f, 0.895f));
            Undo.RecordObject(sun.transform, "Soften autumn sun");
            sun.transform.rotation = Quaternion.Euler(32f, 224f, 0f);
            if (sun.TryGetComponent(out HDAdditionalLightData sunData))
            {
                Undo.RecordObject(sunData, "Soften autumn sun");
                sunData.angularDiameter = 1.65f;
                sunData.shadowDimmer = 0.88f;
            }

            var fill = Require(atmosphere, "FillLight").GetComponent<Light>();
            TuneLight(fill, 20300f, new Color(0.98f, 0.99f, 1f));
            Undo.RecordObject(fill.transform, "Fill the shaded jamb of the stone portal");
            fill.transform.rotation = Quaternion.Euler(26f, 63f, 0f);
            TuneLight(Require(entrance, "EntranceWarmFill").GetComponent<Light>(), 1200f, new Color(1f, 0.91f, 0.80f));
            TuneLight(Require(entrance, "EntranceRevealFill").GetComponent<Light>(), 2200f, new Color(0.96f, 0.94f, 0.89f));
            TuneLight(Root(scene, "MonumentLight").GetComponent<Light>(), 1750f, new Color(0.93f, 0.96f, 1f));

            VolumeProfile profile = global.sharedProfile;
            Change<Exposure>(profile, value => { value.mode.Override(ExposureMode.Fixed); value.fixedExposure.Override(12.15f); });
            Change<ColorAdjustments>(profile, value =>
            {
                value.contrast.Override(-5f);
                value.saturation.Override(-7f);
                value.colorFilter.Override(Color.white);
            });
            Change<VisualEnvironment>(profile, value => value.skyType.Override((int)SkyType.Gradient));
            Change<GradientSky>(profile, value =>
            {
                value.top.Override(new Color(0.37f, 0.53f, 0.66f));
                value.middle.Override(new Color(0.57f, 0.65f, 0.71f));
                value.bottom.Override(Linear("#C9C1B0"));
                value.exposure.Override(11.95f);
            });
            Change<Fog>(profile, value =>
            {
                value.tint.Override(new Color(0.96f, 0.94f, 0.87f));
                value.meanFreePath.Override(142f);
                value.maximumHeight.Override(37f);
                value.enableVolumetricFog.Override(false);
            });
            Change<Fog>(trailFog.sharedProfile, value => value.active = false);
            Change<Bloom>(profile, value => value.intensity.Override(0.048f));
        }

        static void TuneLight(Light light, float intensity, Color color)
        {
            Undo.RecordObject(light, "Balance autumn lighting");
            light.color = color;
            light.intensity = intensity;
        }

        static void Change<T>(VolumeProfile profile, Action<T> apply) where T : VolumeComponent
        {
            if (profile == null || !profile.TryGet(out T value))
                throw new InvalidOperationException($"Autumn volume lacks {typeof(T).Name}.");
            Undo.RecordObject(value, "Balance autumn volume");
            apply(value);
            EditorUtility.SetDirty(value);
        }

        static void ReplaceUses(Transform root, string source, Material replacement)
        {
            foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                var items = renderer.sharedMaterials;
                if (!items.Any(material => material != null && material.name == source)) continue;
                Undo.RecordObject(renderer, "Apply autumn palette");
                for (int i = 0; i < items.Length; i++)
                    if (items[i] != null && items[i].name == source) items[i] = replacement;
                renderer.sharedMaterials = items;
            }
        }

        static void Replace(Renderer renderer, Material material)
        {
            if (renderer.sharedMaterial == material) return;
            Undo.RecordObject(renderer, "Apply autumn palette");
            renderer.sharedMaterial = material;
        }

        static Material Variant(string name, string sourcePath, Action<Material> tune)
        {
            var source = Load<Material>(sourcePath);
            string path = Folder + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(source) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            Undo.RecordObject(material, "Update " + name);
            material.CopyPropertiesFromMaterial(source);
            tune(material);
            HDMaterial.ValidateMaterial(material);
            EditorUtility.SetDirty(material);
            return material;
        }

        static Transform RebuildGroup(Transform parent, string name)
        {
            var pattern = new Regex("^" + Regex.Escape(name) + @"(?: \(\d+\))?$");
            foreach (Transform child in parent.Cast<Transform>().Where(t => pattern.IsMatch(t.name)).ToArray())
                Undo.DestroyObjectImmediate(child.gameObject);
            var group = Node(parent, name, Vector3.zero);
            Undo.RegisterCreatedObjectUndo(group.gameObject, "Create autumn scene polish");
            return group;
        }

        static void SaveRender(Transform parent, string name, Vector3 origin, Mesh mesh, Material[] materials,
            bool castShadows = false)
        {
            var saved = SaveMesh(Folder + "/" + name + ".asset", mesh, new HashSet<string>());
            var node = Node(parent, name, origin);
            Undo.RegisterCreatedObjectUndo(node.gameObject, "Create " + name);
            Render(node, saved, materials, castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off);
        }

        static void SetVisible(Renderer renderer, bool visible)
        {
            if (renderer == null || renderer.enabled == visible) return;
            Undo.RecordObject(renderer, "Set autumn renderer visibility");
            renderer.enabled = visible;
        }

        static float Fade(float min, float max, float value) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(min, max, value));

        static T Load<T>(string path) where T : Object =>
            AssetDatabase.LoadAssetAtPath<T>(path) ?? throw new InvalidOperationException("Missing asset " + path);

        static Transform Root(Scene scene, string name) =>
            scene.GetRootGameObjects().FirstOrDefault(root => root.name == name)?.transform ??
            throw new InvalidOperationException("Missing scene root " + name);

        static Transform Require(Transform parent, string path) =>
            parent.Find(path) ?? throw new InvalidOperationException($"Missing {parent.name}/{path}");
    }
}
