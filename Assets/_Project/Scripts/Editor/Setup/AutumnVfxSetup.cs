using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using ShadowTan.Combat;
using ShadowTan.Gameplay.Autumn;
using ShadowTan.Player;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace ShadowTan.EditorTools
{
    public static class AutumnVfxSetup
    {
        public const string ArtScene = "Assets/_Project/Scenes/Seasons/Autumn/Autumn_Gameplay.unity";
        public const string BattleScene = "Assets/_Game/Scenes/Autumn_Baihu.unity";
        const string AssetsPath = "Assets/_Project/Art/Environment/Autumn/Location/Generated/VFX";
        const string Environment = "Assets/_Project/Art/Environment/Autumn/Location";

        [MenuItem("ShadowTan/Setup/Art/Setup Autumn VFX")]
        public static void Apply()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || (scene.path != ArtScene && scene.path != BattleScene))
                throw new InvalidOperationException("Open Autumn_Gameplay or Autumn_Baihu in Edit Mode.");

            if (!AssetDatabase.IsValidFolder(AssetsPath))
                AssetDatabase.CreateFolder(Path.GetDirectoryName(AssetsPath).Replace('\\', '/'), "VFX");
            var shader = Shader.Find("ShadowTan/Autumn/Particles");
            if (!shader || !shader.isSupported) throw new InvalidOperationException("Autumn particle shader unavailable.");

            var transforms = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToArray();
            bool battle = scene.path == BattleScene;
            var atmosphere = battle ? scene.GetRootGameObjects().First().transform
                : transforms.Single(t => t.name == "Atmosphere" && t.parent != null && t.parent.name == "[Level]");
            var root = FindOrCreate(atmosphere, "[Autumn VFX]");
            var dustMaterial = Material("Autumn_Dust", shader, DustAtlas(), new Color(.68f, .53f, .33f, .66f));
            var leafMaterial = Material("Autumn_Leaves", shader, LeafAtlas(), new Color(.88f, .72f, .40f, .88f));
            var debrisMaterial = Material("Autumn_StoneDebris", Shader.Find("HDRP/Lit"), null, new Color(.31f, .23f, .15f));
            var shardMaterial = Material("Autumn_MonumentChips", Shader.Find("HDRP/Lit"), null, new Color(.27f, .25f, .22f));
            shardMaterial.SetFloat("_Metallic", .55f);
            EditorUtility.SetDirty(shardMaterial);
            var meshes = new[] { Octahedron("Chip_Rounded", .83f), Octahedron("Chip_Flat", .38f), Octahedron("Chip_Long", 1.23f) };
            var stepDust = battle ? null : Install(root, "PF_Autumn_FootstepDust", dustMaterial, 30);
            var impactDust = Install(root, "PF_Autumn_ImpactDust", dustMaterial, 40);
            var stones = battle ? null : Install(root, "PF_Autumn_StoneDebris", debrisMaterial, 6, meshes, 0f);
            var metal = Install(root, "PF_Autumn_MonumentShards", shardMaterial, 20, meshes, .75f);
            var leavesPrefab = EnsurePrefab("PF_Autumn_Leaves", leafMaterial, 24, null, 0f, true);
            var leafSystem = battle ? Instance(root, leavesPrefab) : atmosphere.Find("FallingLeaves").GetComponent<ParticleSystem>();
            if (!battle) Configure(leafSystem, leafMaterial, 24, null, 0, true);

            var director = Component<AutumnVfxDirector>(root);
            var leaves = Component<AutumnLeafFx>(root);
            var vegetation = Component<AutumnVegetationImpulse>(root);
            var monument = Component<AutumnMonumentFx>(root);
            var marker = FindOrCreate(root, "Preview Point");
            marker.SetPositionAndRotation(battle ? new Vector3(4, 0, 415) : new Vector3(72.9f, 0, 24),
                Quaternion.LookRotation(Vector3.left));
            var legacy = battle ? transforms.Select(t => t.GetComponent<MeshRenderer>())
                .Where(r => r && (r.name is "Bamboo bank" or "Interior planting")
                    && !r.GetComponentInChildren<Collider>(true)).ToArray() : Array.Empty<MeshRenderer>();

            var encounter = battle ? transforms.SelectMany(t => t.GetComponents<MonoBehaviour>())
                .Single(c => c && c.GetType().FullName == "TanShadow.Autumn.BaihuEncounter") : null;
            var hero = battle ? ((Component)new SerializedObject(encounter).FindProperty("hero").objectReferenceValue).transform
                : transforms.Single(t => t.name == "Heroine" && t.GetComponent<FootstepEmitter>());

            Set(director, so =>
            {
                Reference(so, "vegetation", vegetation); Reference(so, "monument", monument);
                Reference(so, "leaves", leaves); Reference(so, "previewPoint", marker);
                References(so, "systems", new[] { stepDust, impactDust, stones, metal, leafSystem });
            });
            Set(leaves, so =>
            {
                Reference(so, "leaves", leafSystem); Reference(so, "observer", hero);
                so.FindProperty("xRange").vector2Value = battle ? new Vector2(-12, 12) : new Vector2(65, 75);
                so.FindProperty("zRange").vector2Value = battle ? new Vector2(15, 406) : new Vector2(10, 163);
            });
            Set(vegetation, so =>
            {
                Reference(so, "wind", transforms.Select(t => t.GetComponent<AutumnWindController>()).FirstOrDefault(x => x));
                References(so, "legacyStems", legacy);
            });
            Set(monument, so =>
            {
                Reference(so, "impactDust", impactDust); Reference(so, "shards", metal);
                so.FindProperty("groundMask").intValue = LayerMask.GetMask("Default", "Ground", "Wall");
            });

            if (battle)
            {
                BindBattle(encounter, director);
                var shaft = (Component)new SerializedObject(encounter).FindProperty("monument").objectReferenceValue;
                AutumnMonumentStageSetup.Apply(shaft, AssetsPath);
            }
            else BindArt(transforms, root, hero, Component<AutumnSurfaceFx>(root), director, stepDust, stones);

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log($"Autumn VFX configured in {scene.name}. Legacy stems available to local impulses: {legacy.Length}.");
        }

        static void BindArt(Transform[] transforms, Transform root, Transform hero, AutumnSurfaceFx surface,
            AutumnVfxDirector director, ParticleSystem stepDust, ParticleSystem stones)
        {
            var terrain = transforms.Select(t => t.GetComponent<Terrain>()).Single(t => t);
            var ramp = transforms.Single(t => t.name == "RampWalk" && t.gameObject.activeInHierarchy).GetComponent<Collider>();
            var dry = FindSurface("Surface_Sand");
            var stone = FindSurface("Surface_Stone");
            Set(surface, so =>
            {
                Reference(so, "footsteps", hero.GetComponent<FootstepEmitter>()); Reference(so, "dust", stepDust);
                Reference(so, "terrain", terrain); Reference(so, "drySurface", dry);
                Reference(so, "stoneRamp", ramp); Reference(so, "stoneSurface", stone);
                References(so, "dryLayers", terrain.terrainData.terrainLayers.Where(l => l.name is "TroddenEarth" or "ArenaSand").ToArray());
            });
            var debris = Component<AutumnDebrisFx>(root);
            Set(debris, so => { Reference(so, "stones", stones); Reference(so, "slope", ramp); });
            var looseStones = transforms.Single(t => t.name == "LooseStonesOnSlope").GetComponent<AutumnEntranceMoments>();
            Set(looseStones, so => Reference(so, "debris", debris));
            var eyeCue = transforms.Single(t => t.name == "EyesAtClearing").GetComponent<AutumnEntranceMoments>();
            var eyes = transforms.Single(t => t.name == "BaihuEyes");
            var glow = Component<AutumnEyesGlow>(root);
            Set(glow, so => References(so, "eyes", eyes.GetComponentsInChildren<Renderer>(true)));
            Set(eyeCue, so => Reference(so, "eyeGlow", glow));
            Set(director, so =>
            {
                Reference(so, "eyes", glow);
                Reference(so, "surface", surface);
                Reference(so, "debris", debris);
            });
        }

        internal static void BindBattle(Component encounter, AutumnVfxDirector director, bool bindEyeSignals = true)
        {
            var type = TypeCache.GetTypesDerivedFrom<MonoBehaviour>().Single(t => t.FullName == "TanShadow.Autumn.BaihuVfxBridge");
            var binding = encounter.GetComponent(type);
            if (!binding) binding = Undo.AddComponent(encounter.gameObject, type);
            Undo.RecordObject(binding, "Bind Baihu encounter VFX");
            Set(binding, so => Reference(so, "encounter", encounter));
            Field<UnityEvent<Vector3>>(binding, "rustled", (Action<Vector3>)director.Rustle);
            if (bindEyeSignals)
            {
                Field<UnityEvent>(binding, "eyesShown", (Action)director.EyesShown);
                Field<UnityEvent>(binding, "scared", (Action)director.Scared);
            }
            Field<UnityEvent<Vector3, Vector3>>(binding, "pounced", (Action<Vector3, Vector3>)director.Leap);
            Field<UnityEvent<Vector3, Vector3>>(binding, "leaptAway", (Action<Vector3, Vector3>)director.Takeoff);
            Field<UnityEvent<Vector3>>(binding, "landed", (Action<Vector3>)director.Landing);
            Field<UnityEvent<Vector3>>(binding, "impact", (Action<Vector3>)director.MonumentHit);
            Field<UnityEvent<Vector3>>(binding, "monumentLanded", (Action<Vector3>)director.MonumentLanded);
            Field<UnityEvent>(binding, "stopped", (Action)director.StopEffects);
            Field<UnityEvent>(binding, "reset", (Action)director.ResetEffects);
            EditorUtility.SetDirty(binding);
        }

        static void Field<T>(Component bridge, string fieldName, Delegate method) where T : UnityEventBase
        {
            var signal = (T)bridge.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(bridge);
            for (int i = 0; i < signal.GetPersistentEventCount(); i++)
                if (signal.GetPersistentTarget(i) == method.Target as Object && signal.GetPersistentMethodName(i) == method.Method.Name)
                    return;
            switch (signal)
            {
                case UnityEvent ev: UnityEventTools.AddPersistentListener(ev, (UnityAction)Delegate.CreateDelegate(typeof(UnityAction), method.Target, method.Method)); break;
                case UnityEvent<Vector3> ev: UnityEventTools.AddPersistentListener(ev, (UnityAction<Vector3>)Delegate.CreateDelegate(typeof(UnityAction<Vector3>), method.Target, method.Method)); break;
                case UnityEvent<Vector3, Vector3> ev: UnityEventTools.AddPersistentListener(ev, (UnityAction<Vector3, Vector3>)Delegate.CreateDelegate(typeof(UnityAction<Vector3, Vector3>), method.Target, method.Method)); break;
            }
        }

        static ParticleSystem Install(Transform root, string name, Material material, int cap, Mesh[] meshes = null, float gravity = 0)
            => Instance(root, EnsurePrefab(name, material, cap, meshes, gravity, false));

        static ParticleSystem Instance(Transform parent, GameObject prefab)
        {
            var found = parent.Find(prefab.name);
            if (found) return found.GetComponent<ParticleSystem>();
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            Undo.RegisterCreatedObjectUndo(go, "Install " + prefab.name);
            return go.GetComponent<ParticleSystem>();
        }

        static GameObject EnsurePrefab(string name, Material material, int cap, Mesh[] meshes, float gravity, bool leaves)
        {
            string path = $"{AssetsPath}/{name}.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing)
            {
                var contents = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    Configure(contents.GetComponent<ParticleSystem>(), material, cap, meshes, gravity, leaves);
                    PrefabUtility.SaveAsPrefabAsset(contents, path);
                }
                finally { PrefabUtility.UnloadPrefabContents(contents); }
                return existing;
            }
            var go = new GameObject(name);
            try
            {
                Configure(go.AddComponent<ParticleSystem>(), material, cap, meshes, gravity, leaves);
                return PrefabUtility.SaveAsPrefabAsset(go, path);
            }
            finally { Object.DestroyImmediate(go); }
        }

        static void Configure(ParticleSystem system, Material material, int cap, Mesh[] meshes, float gravity, bool leaves)
        {
            var renderer = system.GetComponent<ParticleSystemRenderer>();
            Undo.RecordObjects(new Object[] { system, renderer }, "Autumn particle settings");
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = system.main;
            main.loop = false;
            main.playOnAwake = false;
            main.duration = 6;
            main.prewarm = false;
            main.maxParticles = cap;
            main.gravityModifier = gravity;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.cullingMode = ParticleSystemCullingMode.Automatic;
            main.startSpeed = 0;
            main.startColor = Color.white;
            main.startRotation3D = meshes != null;
            var emission = system.emission; emission.enabled = false;
            var shape = system.shape; shape.enabled = false;
            var collision = system.collision; collision.enabled = false;
            var trigger = system.trigger; trigger.enabled = false;
            var light = system.lights; light.enabled = false;
            var noise = system.noise; noise.enabled = false;
            var velocity = system.velocityOverLifetime; velocity.enabled = false;
            var trails = system.trails; trails.enabled = false;
            var rotation = system.rotationOverLifetime; rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(-1.8f, 1.8f);
            var size = system.sizeOverLifetime;
            size.enabled = true;
            size.size = meshes == null
                ? new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, .70f, 1, 1.25f))
                : new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 1f, 1, .08f));
            var color = system.colorOverLifetime;
            color.enabled = meshes == null;
            if (color.enabled)
            {
                var fade = new Gradient();
                fade.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                    new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, .12f), new GradientAlphaKey(.72f, .46f), new GradientAlphaKey(0, 1) });
                color.color = new ParticleSystem.MinMaxGradient(fade);
            }
            var tiles = system.textureSheetAnimation;
            tiles.enabled = leaves;
            if (leaves)
            {
                tiles.mode = ParticleSystemAnimationMode.Grid;
                tiles.numTilesX = 4;
                tiles.numTilesY = 1;
                tiles.frameOverTime = 0;
                tiles.startFrame = new ParticleSystem.MinMaxCurve(0, .99f);
            }
            renderer.renderMode = meshes == null ? ParticleSystemRenderMode.Billboard : ParticleSystemRenderMode.Mesh;
            if (meshes != null) renderer.SetMeshes(meshes);
            renderer.SetActiveVertexStreams(new List<ParticleSystemVertexStream>
                { ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Normal, ParticleSystemVertexStream.Color, ParticleSystemVertexStream.UV });
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            system.gameObject.layer = LayerMask.NameToLayer("Ignore Raycast");
            EditorUtility.SetDirty(system);
        }

        static Texture2D DustAtlas()
        {
            return Texture("T_Autumn_SoftDust", 128, 128, (x, y) =>
            {
                Vector2 uv = new Vector2(x - 63.5f, y - 63.5f) / 62f;
                float rad = 1f - Mathf.Clamp01(uv.sqrMagnitude);
                float n = Mathf.PerlinNoise(x * .073f, y * .073f);
                return new Color(1, 1, 1, rad * rad * Mathf.Lerp(.46f, .93f, n));
            });
        }

        static Texture2D LeafAtlas()
        {
            return Texture("T_Autumn_LeafAtlas", 512, 128, (x, y) =>
            {
                int tile = x / 128;
                float v = (y - 12f) / 104f;
                float u = (x % 128 - 63.5f) / 54f;
                float curve = (tile % 3 - 1) * .17f * Mathf.Sin(v * Mathf.PI);
                float width = Mathf.Sin(Mathf.Clamp01(v) * Mathf.PI) * (.25f + .047f * (tile % 3));
                float edge = Mathf.Clamp01((width - Mathf.Abs(u - curve)) * 70f);
                float vein = Mathf.Exp(-Mathf.Abs(u - curve) * 40f);
                float texture = Mathf.Lerp(.68f, 1f, .55f * vein + .45f * Mathf.PerlinNoise(x * .067f, y * .085f));
                return new Color(texture, texture * (.88f + .022f * tile), texture * .62f, edge);
            });
        }

        static Texture2D Texture(string name, int width, int height, Func<int, int, Color> write)
        {
            string path = $"{AssetsPath}/{name}.asset";
            var old = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (old) return old;
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, true);
            texture.wrapMode = TextureWrapMode.Clamp;
            var pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++) pixels[y * width + x] = write(x, y);
            texture.SetPixels32(pixels);
            texture.Apply(true, true);
            AssetDatabase.CreateAsset(texture, path);
            return texture;
        }

        static Mesh Octahedron(string name, float aspect)
        {
            string path = $"{AssetsPath}/{name}.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh) return mesh;
            mesh = new Mesh
            {
                name = name,
                vertices = new[] { new Vector3(.6f, 0, 0), new Vector3(0, 0, .38f), new Vector3(-.48f, 0, 0),
                    new Vector3(0, 0, -.35f), new Vector3(.05f, .48f * aspect, .08f), new Vector3(-.07f, -.36f * aspect, -.05f) },
                triangles = new[] { 0, 4, 1, 1, 4, 2, 2, 4, 3, 3, 4, 0, 1, 5, 0, 2, 5, 1, 3, 5, 2, 0, 5, 3 }
            };
            mesh.RecalculateNormals();
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        static Material Material(string name, Shader shader, Texture2D texture, Color color)
        {
            string path = $"{AssetsPath}/M_{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!mat) { mat = new Material(shader); AssetDatabase.CreateAsset(mat, path); }
            if (texture) mat.SetTexture("_MainTex", texture);
            mat.SetColor(texture ? "_Color" : "_BaseColor", color);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        internal static void Set(Object target, Action<SerializedObject> assign)
        {
            Undo.RecordObject(target, "Configure autumn scene");
            var serialized = new SerializedObject(target);
            assign(serialized);
            serialized.ApplyModifiedProperties();
        }

        internal static void Reference(SerializedObject obj, string field, Object value)
            => obj.FindProperty(field).objectReferenceValue = value;

        static void References(SerializedObject obj, string field, Object[] values)
        {
            var list = obj.FindProperty(field);
            list.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }

        internal static SurfaceDefinition FindSurface(string name)
            => AssetDatabase.LoadAssetAtPath<SurfaceDefinition>(AssetDatabase.GUIDToAssetPath(
                AssetDatabase.FindAssets($"{name} t:SurfaceDefinition").Single(g => Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(g)) == name)));

        internal static T Component<T>(Transform root) where T : Component
        {
            var component = root.GetComponent<T>();
            return component ? component : Undo.AddComponent<T>(root.gameObject);
        }

        internal static Transform FindOrCreate(Transform parent, string name)
        {
            var found = parent.Find(name);
            if (found) return found;
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "Create " + name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }
    }
}
