using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;
using ShadowTan.World;
using static ShadowTan.EditorTools.AutumnVegetationBake;
using Object = UnityEngine.Object;
using Random = System.Random;

namespace ShadowTan.EditorTools
{
    public static partial class AutumnEntranceCave
    {
        internal const string RootName = "Entrance_Root";
        internal const string Folder = Generated + "/Entrance";
        const string Prefix = "Entrance_";
        const int Seed = 37591;
        const float MouthZ = -15f;
        const float TunnelLength = 7f;
        const float EntranceY = 50f;
        const float RampRun = 60f;
        const float ClearingZ = 0.55f;
        const float ClearingRadius = 11.4f;
        const float RampTopZ = -10.5f;
        static readonly Vector3 RampFoot = new Vector3(CenterX, 0f, 0f);
        static readonly Vector3 RampTop = new Vector3(CenterX - Mathf.Sqrt(RampRun * RampRun - RampTopZ * RampTopZ), EntranceY, RampTopZ);
        static readonly Vector3 Along = Vector3.ProjectOnPlane(RampFoot - RampTop, Vector3.up).normalized;
        static readonly Vector3 Across = Vector3.Cross(Along, Vector3.up);
        const StaticEditorFlags SolidFlags = RendererFlags | StaticEditorFlags.OccluderStatic;

        [MenuItem("ShadowTan/Setup/Art/Build Autumn Entrance")]
        public static void Build() => Run("Autumn Entrance", Build);

        static void Build(Transform props, Terrain terrain)
        {
            EnsureFolder(Folder);
            Transform greybox = props.parent;
            Scene scene = greybox.gameObject.scene;
            Transform flow = FindRoot(scene, "[Flow]");
            Transform gizmos = FindRoot(scene, "[Gizmos]");
            Transform heroine = FindRoot(scene, "Heroine");
            if (flow == null || gizmos == null || heroine == null || gizmos.Find("EntranceCamera") == null)
                throw new InvalidOperationException("Autumn_Gameplay requires Flow, Gizmos/EntranceCamera and Heroine.");

            var materials = new Materials();
            var root = Node(greybox, RootName, Vector3.zero);
            var retained = new HashSet<string>(StringComparer.Ordinal);
            var objects = new HashSet<string>(StringComparer.Ordinal);
            var counts = new Counts();
            var structure = new Geometry(Vector3.zero, 1);
            BuildWall(root, materials, retained, objects, structure);
            var slab = BuildTunnel(root, materials, retained, objects, structure);
            objects.Add(Prefix + "Structure");
            Bake(root, "Structure", structure, materials.Recess, ShadowCastingMode.On, retained, SolidFlags);
            var bed = new Geometry(Vector3.zero, 1);
            BuildApron(root, retained, objects, bed);
            BuildRamp(root, materials, retained, objects, counts, bed);
            BuildClearing(root, terrain, materials, retained, objects);
            ConfigureFlow(flow, gizmos, heroine, root, slab, objects);
            ConfigureLegacy(greybox, props);

            int removedObjects = RemoveChildren(root, Prefix, objects);
            int removedMeshes = PruneMeshes(Folder, Prefix, retained);
            Debug.Log($"ShadowTan / Autumn Entrance: wall 210 × 60 × 4 m, opening 5 × 5 m; tunnel 7 m. " +
                      $"Ramp 60 m run, 50 m drop, {Mathf.Atan2(EntranceY, RampRun) * Mathf.Rad2Deg:F2}°, " +
                      $"9–15.5 m walkable width. Clearing R={ClearingRadius:F1} m. Rocks {counts}. " +
                      $"Removed {removedObjects} obsolete owned objects and {removedMeshes} owned meshes.");
        }

        static void ConfigureFlow(Transform flow, Transform gizmos, Transform heroine, Transform root, Transform slab,
            HashSet<string> objects)
        {
            float x = RampTop.x;
            Node(flow, "SeasonEntrance", new Vector3(x, EntranceY + 0.02f, MouthZ - TunnelLength - 1.0f));
            Node(flow, "SeasonExit", new Vector3(x, EntranceY + 0.02f, MouthZ - TunnelLength + 0.85f));
            Node(flow, "RespawnPoint_Entrance", new Vector3(CenterX, 0.04f, ClearingZ + 2.7f));
            var entry = flow.Find("SeasonEntrance");
            heroine.SetPositionAndRotation(entry.position, entry.rotation);
            if (PrefabUtility.IsPartOfPrefabInstance(heroine))
                PrefabUtility.RecordPrefabInstancePropertyModifications(heroine);

            Transform trigger = Section(root, "ExitTrigger", objects);
            trigger.position = new Vector3(x, EntranceY + 2f, MouthZ + 1.50f);
            trigger.gameObject.layer = LayerMask.NameToLayer("Trigger");
            var box = Component<BoxCollider>(trigger.gameObject);
            box.size = new Vector3(4.95f, 4f, 0.28f);
            box.isTrigger = true;
            var body = Component<Rigidbody>(trigger.gameObject);
            body.isKinematic = true;
            body.useGravity = false;
            var door = new SerializedObject(Component<SeasonEntranceDoor>(trigger.gameObject));
            door.FindProperty("slab").objectReferenceValue = slab;
            door.FindProperty("entrant").objectReferenceValue = heroine;
            door.FindProperty("closedPosition").vector3Value = slab.localPosition;
            door.FindProperty("openOnStart").boolValue = true;
            door.ApplyModifiedPropertiesWithoutUndo();

            var composition = gizmos.Find("EntranceCamera").GetComponent<Camera>();
            Vector3 pos = new Vector3(74.2f, 3.65f, -1.1f);
            Vector3 aim = new Vector3(37f, 15.7f, 13.5f);
            composition.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(aim - pos));
            composition.fieldOfView = 70f;
            composition.enabled = false;
            composition.gameObject.tag = "Untagged";
            if (composition.TryGetComponent<AudioListener>(out var oldListener)) oldListener.enabled = false;
            var overview = gizmos.Find("OverviewCamera")?.GetComponent<Camera>();
            if (overview != null) overview.enabled = false;

            Transform playable = Node(gizmos, Prefix + "PlayerCamera", heroine.position + new Vector3(0f, 1.8f, -1f));
            playable.gameObject.tag = "MainCamera";
            var camera = Component<Camera>(playable.gameObject);
            camera.fieldOfView = 61f;
            camera.nearClipPlane = 0.06f;
            camera.farClipPlane = 350f;
            camera.enabled = true;
            Component<AudioListener>(playable.gameObject).enabled = true;
            var hd = Component<HDAdditionalCameraData>(playable.gameObject);
            hd.volumeLayerMask = -1;
            hd.antialiasing = HDAdditionalCameraData.AntialiasingMode.TemporalAntialiasing;
            var view = new SerializedObject(Component<SeasonEntranceView>(playable.gameObject));
            view.FindProperty("target").objectReferenceValue = heroine;
            view.FindProperty("obstacles").intValue = LayerMask.GetMask("Ground", "Wall");
            view.ApplyModifiedPropertiesWithoutUndo();
        }

        static void ConfigureLegacy(Transform greybox, Transform props)
        {
            foreach (string path in new[] { "Ground/EntranceFloor", "Ground/Slope", "Walls/Slope_Ridge", "Walls/Wall_Grey" })
            {
                Transform old = greybox.Find(path);
                if (old != null) old.gameObject.SetActive(false);
            }

            Transform oldStones = props.Find("SlopeStones");
            if (oldStones != null) Object.DestroyImmediate(oldStones.gameObject);
        }

        static Transform FindRoot(Scene scene, string name) =>
            Array.Find(scene.GetRootGameObjects(), gameObject => gameObject.name == name)?.transform;

        static Transform Section(Transform root, string name, HashSet<string> objects)
        {
            string fullName = Prefix + name;
            objects.Add(fullName);
            return Node(root, fullName, Vector3.zero);
        }

        sealed class Materials
        {
            internal readonly Material Wall = Copy("Wall", "M_Autumn_Wall", "#A3A5A7");
            internal readonly Material Recess = Copy("Recess", "M_Autumn_Wall", "#61646A");
            internal readonly Material Tunnel = Copy("Tunnel", "M_Autumn_EntranceStone", "#777D85");
            internal readonly Material Rock = Copy("Rubble", "M_Autumn_SlopeStone", "#78583D");
            internal readonly Material Soil = Copy("RampSoil", "M_Autumn_Path", "#78583D");
            internal readonly Material Sand = Copy("Sand", "M_Autumn_Path_Clearing", "#D8C18E");
            internal readonly Material Culm = Load("ForestBake_Culm");
            internal readonly Material Leaves = Load("ForestBake_Leaves");
            internal readonly Material Grass = Load("DenseGrass_Straw");

            static Material Copy(string name, string sourceName, string color)
            {
                Material source = Load(sourceName);
                return Material(Prefix + name, "HDRP/Lit", material =>
                {
                    material.CopyPropertiesFromMaterial(source);
                    material.SetColor("_BaseColor", Linear(color));
                    foreach (string property in new[] { "_BaseColorMap", "_NormalMap", "_MaskMap" })
                        material.SetTextureScale(property, Vector2.one);
                });
            }

            static Material Load(string name) =>
                AssetDatabase.LoadAssetAtPath<Material>($"{Location}/Materials/{name}.mat") ??
                throw new InvalidOperationException($"Missing autumn material {name}.");
        }
    }
}
