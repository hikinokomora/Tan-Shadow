using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using static ShadowTan.EditorTools.AutumnVegetationBake;

namespace ShadowTan.EditorTools
{
    public static partial class AutumnTripoPropsStudio
    {
        static void BakeStage(Transform root, List<Placement> placements, Palette palette, HashSet<string> nodes,
            HashSet<string> retained)
        {
            Transform instances = Node(root, Prefix + "Instances", root.position);
            nodes.Add(instances.name);
            var placementNames = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < placements.Count; i++)
            {
                Placement p = placements[i];
                string name = $"{Prefix}{p.Zone}_{p.Asset.Kind}_{i:000}";
                Transform node = Node(instances, name, p.Position);
                node.SetPositionAndRotation(p.Position, p.Rotation);
                node.localScale = p.Scale;
                placementNames.Add(name);
                AddCollider(node.gameObject, p.Asset);
            }

            RemoveChildren(instances, Prefix, placementNames);
            long[] counts = new long[3];
            int batches = 0;
            foreach (var cell in placements.GroupBy(p => (p.Zone, Grass: p.Asset.Kind == Kind.DryGrassClump, Cell: Cell(p))))
            {
                string name = $"{Prefix}Batch_{cell.Key.Zone}_{(cell.Key.Grass ? "Straw" : "Solid")}_{cell.Key.Cell:00}";
                Vector3 center = Vector3.zero;
                foreach (Placement p in cell) center += p.Position;
                center /= cell.Count();
                Transform chunk = Node(root, name, center);
                nodes.Add(name);
                var lods = new LOD[3];
                for (int level = 0; level < lods.Length; level++)
                {
                    var combined = new MeshData();
                    int heroClumps = 0;
                    foreach (Placement p in cell)
                    {
                        Matrix4x4 placement = Matrix4x4.TRS(p.Position - center, p.Rotation, p.Scale);
                        int modelLod = level;
                        if (cell.Key.Grass && level == 0 && heroClumps++ >= 2) modelLod = 1;
                        combined.Add(p.Asset.Meshes[modelLod], placement, modelLod < 2 ? p.Asset.Atlas : null);
                    }

                    string lodName = $"{name}_LOD{level}";
                    Mesh mesh = SaveMesh($"{Folder}/Chunks/{lodName}.asset", combined.ToMesh(), retained);
                    counts[level] += mesh.GetIndexCount(0) / 3;
                    var renderer = Render(Node(chunk, lodName, center), mesh,
                        new[] { level == 2 ? palette.Cards : palette.Solids },
                        cell.Key.Grass ? ShadowCastingMode.Off : ShadowCastingMode.On);
                    float height = cell.Key.Grass
                        ? (level == 0 ? 0.35f : level == 1 ? 0.22f : 0.09f)
                        : (level == 0 ? 0.71f : level == 1 ? 0.28f : cell.Key.Zone == Zone.Mouth ? 0.105f : 0.135f);
                    lods[level] = new LOD(height, new Renderer[] { renderer });
                }

                SetLods(chunk, lods);
                var group = chunk.GetComponent<LODGroup>();
                group.localReferencePoint = Vector3.zero;
                group.size = cell.Key.Grass ? 2.05f : cell.Key.Zone == Zone.Clearing ? 5.75f : 10.8f;
                batches++;
            }

            BakeContact(root, placements, palette.Contact, nodes, retained);
            Debug.Log($"ShadowTan / Tripo Props: {batches} baked render batches. Total stored geometry by tier " +
                      $"{counts[0]:N0} / {counts[1]:N0} / {counts[2]:N0} triangles. One material per renderer.");
        }

        static int Cell(Placement p)
        {
            switch (p.Zone)
            {
                case Zone.Mouth: return 0;
                case Zone.Scree: return Mathf.FloorToInt(p.Surface.x / 11f);
                case Zone.Clearing: return p.Surface.x < CenterX ? 0 : 1;
                case Zone.Trail: return Mathf.FloorToInt((p.Surface.z - StartZ) / 11f);
                default: return p.Surface.x < CenterX ? 0 : 1;
            }
        }

        static void BakeContact(Transform root, List<Placement> placements, Material material, HashSet<string> names,
            HashSet<string> retained)
        {
            // Project each footprint onto the actual walking surface using Ground only.
            var geometry = new Geometry(root.position, 1);
            int ground = LayerMask.GetMask("Ground");
            foreach (Placement p in placements)
            {
                bool sand = p.Zone == Zone.Clearing || p.Zone == Zone.Trail || p.Zone == Zone.ArenaEdge;
                float above = p.Zone == Zone.Clearing || p.Zone == Zone.Trail ? 0.037f : 0.022f;
                if (p.Zone == Zone.Trail && p.Asset.Kind == Kind.DryGrassClump) continue;
                var u = new Vector3(p.Foot.X.x, 0f, p.Foot.X.y) * p.Foot.Half.x * 1.12f;
                var v = new Vector3(p.Foot.Z.x, 0f, p.Foot.Z.y) * p.Foot.Half.y * 1.12f;
                Vector3 center = new Vector3(p.Foot.Center.x, p.Surface.y, p.Foot.Center.y);
                if (!Sample(center - u - v, out Vector3 a) || !Sample(center - u + v, out Vector3 b) ||
                    !Sample(center + u + v, out Vector3 c) || !Sample(center + u - v, out Vector3 d)) continue;
                if (Mathf.Abs(a.y - d.y) > 1.2f || Mathf.Abs(a.y - b.y) > 1.2f) continue;
                if (sand && p.Zone != Zone.ArenaEdge && Mathf.Min(Mathf.Abs(a.x - CenterX), Mathf.Abs(d.x - CenterX)) < 1.19f) continue;
                geometry.Quad(0, a, b, c, d, ContactTile(0));
                if (p.Asset.Kind == Kind.BambooLog || p.Asset.Kind == Kind.WallChunk)
                {
                    var lift = Vector3.up * 0.003f;
                    geometry.Quad(0, a + lift, b + lift, c + lift, d + lift, ContactTile(1));
                }

                bool Sample(Vector3 point, out Vector3 hit)
                {
                    if (Physics.Raycast(point + Vector3.up * 2f, Vector3.down, out RaycastHit ray, 4f,
                            ground, QueryTriggerInteraction.Ignore))
                    {
                        hit = ray.point + Vector3.up * above;
                        return true;
                    }

                    hit = default;
                    return false;
                }
            }

            for (int i = 0; i < 7; i++)
            {
                Vector3 p = new Vector3(CenterX + (i % 2 == 0 ? -0.13f : 0.16f), 0f, 4.65f + i * 0.42f);
                if (!Physics.Raycast(p + Vector3.up * 2f, Vector3.down, out RaycastHit hit, 3f,
                        ground, QueryTriggerInteraction.Ignore)) continue;
                Vector3 q = hit.point + Vector3.up * 0.038f;
                Vector3 u = Vector3.right * 0.12f, v = Vector3.forward * 0.21f;
                geometry.Quad(0, q - u + v, q + u + v, q + u - v, q - u - v, ContactTile(2));
            }

            string name = Prefix + "ContactPatches";
            names.Add(name);
            Mesh mesh = SaveMesh($"{Folder}/Chunks/{name}.asset", geometry.MakeMesh(), retained);
            Render(Node(root, name, root.position), mesh, new[] { material }, ShadowCastingMode.Off, 0);
        }

        static void SetFinish(Transform root, HashSet<string> names)
        {
            const string path = Folder + "/PP_TripoStage.asset";
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, path);
            }

            if (!profile.TryGet(out Vignette vignette))
            {
                vignette = profile.Add<Vignette>(false);
                AssetDatabase.AddObjectToAsset(vignette, profile);
            }
            vignette.intensity.Override(0.13f);
            vignette.smoothness.Override(0.49f);
            EditorUtility.SetDirty(vignette);
            EditorUtility.SetDirty(profile);
            string name = Prefix + "Finish";
            names.Add(name);
            var volume = Component<Volume>(Node(root, name, root.position).gameObject);
            volume.isGlobal = true;
            volume.priority = 3f;
            volume.sharedProfile = profile;

            GameObject[] roots = root.gameObject.scene.GetRootGameObjects();
            var level = Array.Find(roots, item => item.name == "[Level]");
            var floor = level != null
                ? level.transform.Find("Greybox/Entrance_Root/Entrance_Tunnel/Entrance_Tunnel_Floor")?.GetComponent<BoxCollider>()
                : null;
            if (floor != null)
            {
                string dustName = Prefix + "TunnelDust";
                names.Add(dustName);
                Bounds b = floor.bounds;
                Vector3 position = b.center + Vector3.up * 1.75f + Vector3.forward * 0.90f;
                var dust = Component<LocalVolumetricFog>(Node(root, dustName, position).gameObject);
                var authored = dust.parameters;
                authored.albedo = Color.Lerp(Color.white, Linear("#D8C18E"), 0.21f);
                authored.meanFreePath = 43f;
                authored.size = new Vector3(4.82f, 2.94f, 4.82f);
                authored.positiveFade = new Vector3(0.23f, 0.37f, 0.35f);
                authored.negativeFade = new Vector3(0.23f, 0.18f, 0.36f);
                authored.distanceFadeStart = 16f;
                authored.distanceFadeEnd = 27f;
                dust.parameters = authored;
            }

            var atmosphere = Array.Find(roots, item => item.name == "[Atmosphere]");
            var sun = atmosphere != null ? atmosphere.transform.Find("Sun")?.GetComponent<Light>() : null;
            if (sun != null)
            {
                sun.transform.rotation = Quaternion.LookRotation(new Vector3(-0.44f, -0.37f, -0.82f));
                sun.useColorTemperature = true;
                sun.colorTemperature = 5300f;
            }
        }
    }
}
