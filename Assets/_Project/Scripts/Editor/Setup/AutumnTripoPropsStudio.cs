using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using static ShadowTan.EditorTools.AutumnVegetationBake;

namespace ShadowTan.EditorTools
{
    public static partial class AutumnTripoPropsStudio
    {
        const string Folder = Generated + "/Tripo";
        const string Prefabs = "Assets/_Project/Prefabs/Environment/Autumn/Tripo";
        const string Prefix = "TripoProp_";
        const string Root = Prefix + "Root";
        const int Seed = 73819;

        enum Kind { BambooLog, Boulder, BambooStump, WallChunk, DryGrassClump }
        enum Zone { Mouth, Scree, Clearing, Trail, ArenaEdge }

        sealed class Prop
        {
            internal readonly Kind Kind;
            internal readonly string Archive;
            internal readonly string Tint;
            internal readonly float Smoothness;
            internal string Name => "Autumn_" + Kind;
            internal Color32[] Albedo;
            internal Color32[] Normal;
            internal int Width;
            internal int Height;
            internal Mesh[] Meshes;
            internal Material Material;
            internal Material CardMaterial;
            internal Rect Atlas;
            internal Rect[] CardAtlas;

            internal Prop(Kind kind, string archive, string tint, float smoothness)
            {
                Kind = kind;
                Archive = archive;
                Tint = tint;
                Smoothness = smoothness;
            }
        }

        static Prop[] Definitions() => new[]
        {
            new Prop(Kind.BambooLog, "bamboo", "#B0A15E", 0.21f),
            new Prop(Kind.Boulder, "boulder", "#C2B7A8", 0.27f),
            new Prop(Kind.BambooStump, "penb", "#BBA17D", 0.18f),
            new Prop(Kind.WallChunk, "wallPeice", "#A6A7A4", 0.19f),
            new Prop(Kind.DryGrassClump, "wheat", "#5C392A", 0.10f)
        };

        [MenuItem("ShadowTan/Setup/Art/Stage Tripo Props")]
        public static void Stage() => Run("Tripo Props", Stage);

        static void Stage(Transform props, Terrain terrain)
        {
            Transform entrance = props.parent.Find("Entrance_Root");
            if (entrance == null || entrance.Find("Entrance_Ramp/Entrance_RampWalk") == null)
                throw new InvalidOperationException("Build the autumn entrance and its walking collision first.");

            EnsureFolder(Folder);
            EnsureFolder(Prefabs);
            EnsureFolder(Folder + "/Chunks");
            var assets = Definitions();
            var retained = new HashSet<string>(StringComparer.Ordinal);
            foreach (Prop asset in assets) Import(asset, retained);
            var palette = MakeAtlases(assets, retained);
            foreach (Prop asset in assets) SavePrefab(asset);
            var placements = Compose(entrance, terrain, assets);
            if (placements.Count == 0) throw new InvalidOperationException("No placements landed on the scene surfaces.");

            Transform root = Node(props, Root, props.position);
            var nodes = new HashSet<string>(StringComparer.Ordinal);
            BakeStage(root, placements, palette, nodes, retained);
            SetFinish(root, nodes);
            int prunedNodes = RemoveChildren(root, Prefix, nodes);
            int prunedMeshes = PruneMeshes(Folder + "/Chunks", Prefix, retained);
            var summary = string.Join("\n", placements.GroupBy(p => p.Zone).Select(group =>
                $"  {group.Key}: " + string.Join(", ", group.GroupBy(p => p.Asset.Kind).Select(typed => $"{typed.Key} {typed.Count()}"))));
            Debug.Log($"ShadowTan / Tripo Props: {placements.Count} placements.\n{summary}\n" +
                      $"Removed {prunedNodes} obsolete staging groups and {prunedMeshes} obsolete meshes.");
        }
    }
}
