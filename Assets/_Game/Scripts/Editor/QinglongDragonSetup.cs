using System.Collections.Generic;
using System.IO;
using System.Linq;
using TanShadow.Spring;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TanShadow.EditorTools
{
    // Импорт FBX Цинлуна (экспорт из Blender, dragon_anim/export_unity.py):
    // Generic-риг, root motion на кости Root, имена клипов, луп спокойного полёта, событие касания жемчужины.
    public sealed class QinglongModelImport : AssetPostprocessor
    {
        public const string Dir = "Assets/_Game/Art/Characters/Qinglong";
        public const string ModelPath = Dir + "/Qinglong.fbx";
        public const string TexDir = Dir + "/Textures";     // PNG из Blender (dragon_anim), в FBX текстуры не доходят
        public const int PearlContactFrame = 54, PearlClipFrames = 120;

        // take из Blender -> клип в Unity
        public static readonly (string take, string clip, bool loop)[] Clips =
        {
            ("Dragon_FlyCalm", "A_FlyCalm", true),
            ("Dragon_LookAround", "A_LookAround", false),
            ("Dragon_PearlPush_R", "A_PearlPush_R", false),
            ("Dragon_PearlPush_L", "A_PearlPush_L", false),
            ("Dragon_DashUp", "A_DashUp", false),
            ("Dragon_DiveDown", "A_DiveDown", false),
        };

        public override uint GetVersion() => 1;

        void OnPreprocessModel()
        {
            if (assetPath != ModelPath) return;
            var mi = (ModelImporter)assetImporter;
            mi.animationType = ModelImporterAnimationType.Generic;
            mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            if (string.IsNullOrEmpty(mi.motionNodeName)) mi.motionNodeName = "Armature/Root";
            mi.importAnimation = true;
            mi.importCameras = false;
            mi.importLights = false;
            mi.animationCompression = ModelImporterAnimationCompression.KeyframeReduction;
            mi.animationRotationError = .05f;
            mi.animationPositionError = .05f;
            mi.animationScaleError = .05f;
        }

        void OnPreprocessAnimation()
        {
            if (assetPath != ModelPath) return;
            var mi = (ModelImporter)assetImporter;
            var source = mi.defaultClipAnimations;
            if (source == null || source.Length == 0) return;
            var result = new List<ModelImporterClipAnimation>();
            foreach (var clip in source)
            {
                var spec = Clips.FirstOrDefault(s => clip.takeName.EndsWith(s.take));
                if (spec.clip == null) continue;
                clip.name = spec.clip;
                clip.loopTime = spec.loop;
                clip.loopPose = false;               // стык уже точный, без подмешивания
                clip.lockRootRotation = false;
                clip.lockRootHeightY = false;        // высота рывка — root motion
                clip.lockRootPositionXZ = false;
                if (spec.clip.StartsWith("A_PearlPush"))
                    clip.events = new[]
                    {
                        new AnimationEvent
                        {
                            functionName = "OnPearlPush",
                            time = (float)PearlContactFrame / PearlClipFrames,
                            intParameter = spec.clip.EndsWith("_R") ? 1 : 0,
                        }
                    };
                result.Add(clip);
            }
            mi.clipAnimations = result.ToArray();
        }

        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            if (imported.Any(p => p == ModelPath || p.StartsWith(TexDir + "/")))
                EditorApplication.delayCall += QinglongDragonSetup.TryFirstRun;
        }
    }

    // Animator Controller, префаб и дракон в сцене Spring_Qinglong на точках «06 · Dragon flight reference points».
    // Первый раз запускается сам после импорта FBX; повторно — Tools/Tan Shadow/Spring/Setup Qinglong Dragon
    // (контроллер и префаб пересоздаются, дракон в открытой сцене заменяется; сцену сохранить вручную).
    [InitializeOnLoad]
    public static class QinglongDragonSetup
    {
        const string ScenePath = "Assets/_Game/Scenes/Spring_Qinglong.unity";
        const string ControllerPath = QinglongModelImport.Dir + "/AC_Qinglong.controller";
        const string PrefabPath = QinglongModelImport.Dir + "/Qinglong.prefab";
        const string MaterialPath = QinglongModelImport.Dir + "/M_Qinglong.mat";
        const string BaseColorPath = QinglongModelImport.TexDir + "/Qinglong_BaseColor.png";
        const string NormalPath = QinglongModelImport.TexDir + "/Qinglong_Normal.png";
        const string MaskPath = QinglongModelImport.TexDir + "/Qinglong_Mask.png";   // R металл, G AO, B детали, A гладкость
        const string InstanceName = "Qinglong";
        const string PathGroupKey = "Dragon flight reference points";
        // Модель 1.88 м -> ~30 м. Root motion масштабируется вместе с ним: рывок вверх ~290 м, крейсер ~8 м/с.
        const float Scale = 16f;
        const int StartWaypoint = 1;    // «Upper third · 15m»
        const float DefaultFlightHeight = 18f;   // м над дном ущелья (выбрано по сцене 2026-11-27); дальше — по месту дракона

        // Зона полёта по скрину пользователя 2026-11-27 (м вдоль линии полёта): жёлтые — развороты, красные — вылеты.
        const string ZoneGroupName = "06b · Dragon zone (yellow turns, red exits)";
        static readonly (string name, Vector3 pos)[] TurnMarks =
        {
            ("Turn A · 136m", new Vector3(-17.3f, 18.1f, 120.1f)),
            ("Turn B · 556m", new Vector3(6.5f, 18.1f, 457.1f)),
        };
        // 2026-11-28: оставлены две (было пять) — примерно на трети и двух третях зоны.
        static readonly (string name, Vector3 pos)[] ExitMarks =
        {
            ("Exit 2 · 283m", new Vector3(43.9f, 18.1f, 231.3f)),
            ("Exit 4 · 424m", new Vector3(-38.1f, 18.1f, 341.4f)),
        };

        static QinglongDragonSetup()
        {
            EditorApplication.delayCall += TryFirstRun;
            EditorApplication.delayCall += EnsureZoneMarkers;
            EditorSceneManager.sceneOpened += (s, m) => EnsureZoneMarkers();
        }

        // Маркеры зоны в сцене + привязка к дракону. Только если у дракона их ещё нет — потом двигайте маркеры руками.
        internal static void EnsureZoneMarkers()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != ScenePath) return;
            var dragon = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<QinglongDragon>(true)).FirstOrDefault();
            if (!dragon) return;
            if ((dragon.turnBounds?.Any(t => t) ?? false) || (dragon.exitPoints?.Any(t => t) ?? false)) return;

            var all = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToArray();
            var group = all.FirstOrDefault(t => t.name == ZoneGroupName);
            if (!group)
            {
                var pathGroup = all.FirstOrDefault(t => t.name.Contains(PathGroupKey));
                group = new GameObject(ZoneGroupName).transform;
                Undo.RegisterCreatedObjectUndo(group.gameObject, "Qinglong zone");
                if (pathGroup && pathGroup.parent) group.SetParent(pathGroup.parent, false);
                group.position = Vector3.zero;
            }
            Transform Mark((string name, Vector3 pos) m)
            {
                var t = group.Find(m.name);
                if (!t)
                {
                    t = new GameObject(m.name).transform;
                    Undo.RegisterCreatedObjectUndo(t.gameObject, "Qinglong zone");
                    t.SetParent(group, false);
                    t.position = m.pos;
                }
                return t;
            }
            Undo.RecordObject(dragon, "Qinglong zone");
            dragon.turnBounds = TurnMarks.Select(Mark).ToArray();
            dragon.exitPoints = ExitMarks.Select(Mark).ToArray();
            PrefabUtility.RecordPrefabInstancePropertyModifications(dragon);
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log($"[Qinglong] Зона полёта: {TurnMarks.Length} жёлтых границы и {ExitMarks.Length} красных точек в «{ZoneGroupName}», привязаны к дракону. Сцену нужно сохранить.");
        }

        // Сам перезапускается, пока результат не совпадает с этим скриптом (контроллер, масштаб, материал с текстурами).
        internal static void TryFirstRun()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (AssetDatabase.LoadAssetAtPath<GameObject>(QinglongModelImport.ModelPath) == null) return;
            if (!LoadTextures(out _, out _, out _)) return;    // дождаться импорта PNG — вызовется ещё раз
            if (!NeedsSetup()) return;
            Apply();
        }

        static bool NeedsSetup()
        {
            if (!File.Exists(ControllerPath)) return true;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (!prefab || Mathf.Abs(prefab.transform.localScale.x - Scale) > 1e-3f) return true;
            var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (!mat || !mat.GetTexture("_BaseColorMap") || !mat.GetTexture("_MaskMap")) return true;
            return prefab.GetComponentsInChildren<Renderer>(true).Any(r => r.sharedMaterials.Any(m => m != mat));
        }

        static bool LoadTextures(out Texture2D baseColor, out Texture2D normal, out Texture2D mask)
        {
            baseColor = AssetDatabase.LoadAssetAtPath<Texture2D>(BaseColorPath);
            normal = AssetDatabase.LoadAssetAtPath<Texture2D>(NormalPath);
            mask = AssetDatabase.LoadAssetAtPath<Texture2D>(MaskPath);
            return baseColor && normal && mask;
        }

        static void ConfigureTexture(string path, bool linear, bool normalMap)
        {
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null) return;
            var type = normalMap ? TextureImporterType.NormalMap : TextureImporterType.Default;
            if (ti.textureType == type && ti.sRGBTexture == !linear && ti.maxTextureSize == 4096) return;
            ti.textureType = type;
            ti.sRGBTexture = !linear;
            ti.alphaSource = linear && !normalMap ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
            ti.maxTextureSize = 4096;
            ti.mipmapEnabled = true;
            ti.SaveAndReimport();
        }

        // HDRP/Lit с картами из Blender (glTF metallic-roughness уже переложен в маску HDRP).
        static Material BuildMaterial()
        {
            ConfigureTexture(BaseColorPath, false, false);
            ConfigureTexture(NormalPath, true, true);
            ConfigureTexture(MaskPath, true, false);
            if (!LoadTextures(out var baseColor, out var normal, out var mask))
                throw new System.InvalidOperationException("Нет текстур дракона в " + QinglongModelImport.TexDir);
            var shader = Shader.Find("HDRP/Lit");
            if (!shader) throw new System.InvalidOperationException("Не найден шейдер HDRP/Lit");

            var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (!mat)
            {
                mat = new Material(shader) { name = "M_Qinglong" };
                AssetDatabase.CreateAsset(mat, MaterialPath);
            }
            mat.shader = shader;
            mat.SetColor("_BaseColor", Color.white);
            mat.SetTexture("_BaseColorMap", baseColor);
            mat.SetTexture("_NormalMap", normal);
            mat.SetFloat("_NormalScale", 1f);
            mat.SetTexture("_MaskMap", mask);
            foreach (var p in new[] { "_Metallic", "_Smoothness", "_AO" })
            {
                mat.SetFloat(p + "RemapMin", 0f);
                mat.SetFloat(p + "RemapMax", 1f);
            }
            mat.SetFloat("_DoubleSidedEnable", 1f);    // как в Blender: тонкие гребни и усы видны с обеих сторон
            UnityEngine.Rendering.HighDefinition.HDMaterial.ValidateMaterial(mat);
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssetIfDirty(mat);
            return mat;
        }

        // Все материалы из FBX -> M_Qinglong (внешний ремап импортёра, префаб-вариант подхватывает сам).
        static void RemapMaterials(ModelImporter importer, Material mat)
        {
            var map = importer.GetExternalObjectMap();
            var names = AssetDatabase.LoadAllAssetsAtPath(importer.assetPath).OfType<Material>().Select(m => m.name)
                .Concat(map.Keys.Where(k => k.type == typeof(Material)).Select(k => k.name)).Distinct().ToArray();
            bool changed = false;
            foreach (var n in names)
            {
                var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), n);
                if (map.TryGetValue(id, out var current) && current == mat) continue;
                importer.AddRemap(id, mat);
                changed = true;
            }
            if (changed) importer.SaveAndReimport();
        }

        [MenuItem("Tools/Tan Shadow/Spring/Setup Qinglong Dragon")]
        public static void Apply()
        {
            string modelPath = QinglongModelImport.ModelPath;
            var importer = AssetImporter.GetAtPath(modelPath) as ModelImporter;
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (importer == null || model == null) throw new System.InvalidOperationException("Нет модели " + modelPath);

            // 1. Узел root motion = кость Root (путь зависит от иерархии FBX).
            var rootBone = model.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Root");
            if (rootBone == null) throw new System.InvalidOperationException("В " + modelPath + " нет кости Root");
            string rootPath = AnimationUtility.CalculateTransformPath(rootBone, model.transform);
            if (importer.motionNodeName != rootPath)
            {
                importer.motionNodeName = rootPath;
                importer.SaveAndReimport();
                model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            }

            // 1b. Материал с текстурами.
            var material = BuildMaterial();
            RemapMaterials(importer, material);
            model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);

            // 2. Клипы.
            var clips = AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__preview__")).ToDictionary(c => c.name);
            foreach (var spec in QinglongModelImport.Clips)
                if (!clips.ContainsKey(spec.clip))
                    throw new System.InvalidOperationException($"Нет клипа {spec.clip}; есть: {string.Join(", ", clips.Keys)}");

            // 3. Animator Controller.
            AssetDatabase.DeleteAsset(ControllerPath);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            foreach (var p in new[] { "Look", "PushR", "PushL", "DashUp", "Dive" })
                controller.AddParameter(p, AnimatorControllerParameterType.Trigger);
            var sm = controller.layers[0].stateMachine;
            AnimatorState State(string name, string clip, float x, float y)
            {
                var s = sm.AddState(name, new Vector3(x, y, 0));
                s.motion = clips[clip];
                return s;
            }
            var calm = State(QinglongDragon.CalmState, "A_FlyCalm", 300, 120);
            var look = State("LookAround", "A_LookAround", 620, 0);
            var pushR = State("PearlPush_R", "A_PearlPush_R", 620, 80);
            var pushL = State("PearlPush_L", "A_PearlPush_L", 620, 160);
            var dash = State(QinglongDragon.DashState, "A_DashUp", 300, 300);
            var dive = State(QinglongDragon.DiveState, "A_DiveDown", 620, 300);
            sm.defaultState = calm;

            // Из спокойного полёта — сразу по триггеру с коротким бленом.
            void Leave(AnimatorState to, string trigger)
            {
                var t = calm.AddTransition(to);
                t.hasExitTime = false; t.duration = .25f; t.hasFixedDuration = true;
                t.AddCondition(AnimatorConditionMode.If, 0, trigger);
            }
            // Обратно — в конце клипа без блена: последний кадр = первому кадру спокойного полёта.
            void Back(AnimatorState from, AnimatorState to, string trigger = null)
            {
                var t = from.AddTransition(to);
                t.hasExitTime = true; t.exitTime = 1f; t.duration = 0f; t.hasFixedDuration = true;
                if (trigger != null) t.AddCondition(AnimatorConditionMode.If, 0, trigger);
            }
            Leave(look, "Look"); Leave(pushR, "PushR"); Leave(pushL, "PushL"); Leave(dash, "DashUp");
            Back(look, calm); Back(pushR, calm); Back(pushL, calm);
            Back(dash, dive, "Dive");       // рывок держит верхнюю точку, пока не придёт Dive
            Back(dive, calm);
            EditorUtility.SetDirty(controller);

            // 4. Префаб (вариант модели).
            var temp = (GameObject)PrefabUtility.InstantiatePrefab(model);
            temp.name = InstanceName;
            temp.transform.localScale = Vector3.one * Scale;
            var animator = temp.GetComponent<Animator>();
            if (!animator)
            {
                animator = temp.AddComponent<Animator>();
                animator.avatar = AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<Avatar>().FirstOrDefault();
            }
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;     // полёт процедурный (QinglongDragon), клипы — только лапы, грива, пасть
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;    // над ущельем вне кадра тоже должен лететь
            foreach (var smr in temp.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                smr.updateWhenOffscreen = true;
                smr.sharedMaterials = smr.sharedMaterials.Select(_ => material).ToArray();
            }
            if (!temp.GetComponent<QinglongDragon>()) temp.AddComponent<QinglongDragon>();
            var prefab = PrefabUtility.SaveAsPrefabAsset(temp, PrefabPath);
            Object.DestroyImmediate(temp);
            AssetDatabase.SaveAssetIfDirty(controller);     // только свои ассеты: чужие несохранённые не трогаем

            // 5. Сцена.
            string placed = Place(prefab);

            var report = string.Join(" | ", QinglongModelImport.Clips.Select(s =>
            {
                var c = clips[s.clip];
                var ev = AnimationUtility.GetAnimationEvents(c);
                return $"{s.clip} {c.length:F2}s loop={c.isLooping} v={c.averageSpeed}" + (ev.Length > 0 ? $" event@{ev[0].time:F2}s" : "");
            }));
            Debug.Log($"[Qinglong] root motion node '{rootPath}', controller {ControllerPath}, prefab {PrefabPath} (×{Scale}), " +
                      $"material {MaterialPath} [{material.GetTexture("_BaseColorMap")?.name}, {material.GetTexture("_NormalMap")?.name}, {material.GetTexture("_MaskMap")?.name}]. {placed}\n{report}");
        }

        static string Place(GameObject prefab)
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != ScenePath) return "Сцена " + ScenePath + " не открыта — дракон в сцену не поставлен.";
            var all = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToArray();
            // Уже стоящий дракон задаёт высоту полёта своим местом — при пересборке сохраняем его позу.
            var old = all.Where(t => t && t.GetComponent<QinglongDragon>()).ToArray();
            var keep = old.Length > 0 ? (pos: old[0].position, rot: old[0].rotation, parent: old[0].parent) : default;
            foreach (var t in old) Object.DestroyImmediate(t.gameObject);
            var group = all.FirstOrDefault(t => t && t.name.Contains(PathGroupKey));

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            Undo.RegisterCreatedObjectUndo(go, "Place Qinglong");
            var dragon = go.GetComponent<QinglongDragon>();
            dragon.flightPath = group;
            string where = "без траектории (группа точек полёта не найдена)";
            if (old.Length > 0)
            {
                if (keep.parent) go.transform.SetParent(keep.parent, true);
                go.transform.SetPositionAndRotation(keep.pos, keep.rot);
                where = $"на прежнем месте {keep.pos}";
            }
            else if (group && group.childCount > 1)
            {
                var pts = group.Cast<Transform>().OrderBy(QinglongDragon.WaypointOrder).ToArray();
                int i = Mathf.Clamp(StartWaypoint, 0, pts.Length - 2);
                var a = pts[i].position; var b = pts[i + 1].position;
                if (Physics.Raycast(a, Vector3.down, out var ground, 2000f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    a.y = ground.point.y + DefaultFlightHeight;
                b.y = a.y;
                go.transform.SetPositionAndRotation(a, Quaternion.LookRotation(b - a, Vector3.up));
                where = $"у точки «{pts[i].name}», {DefaultFlightHeight} м над дном {a}";
            }
            var bounds = go.GetComponentsInChildren<Renderer>().Select(r => r.bounds).Aggregate((x, y) => { x.Encapsulate(y); return x; });
            EditorSceneManager.MarkSceneDirty(scene);
            EnsureZoneMarkers();
            Selection.activeGameObject = go;
            return $"Дракон поставлен {where}, размер {bounds.size}. Сцену нужно сохранить.";
        }
    }
}
