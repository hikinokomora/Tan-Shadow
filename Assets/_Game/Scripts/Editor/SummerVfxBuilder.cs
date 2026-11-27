using System.Collections.Generic;
using System.IO;
using System.Linq;
using TanShadow.Player;
using TanShadow.Seasons;
using TanShadow.Summer;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TanShadow.EditorTools
{
    // Эффекты лета и общие эффекты героини по VFX-инструкции v3 (G-01…G-03, SU-01…SU-10) в обеих сценах Чжуцюэ.
    //  • текстуры (процедурные) и материалы TanShadow/VfxUnlit — Art/Vfx, плоский квадрат — Data/Vfx;
    //  • на героине — HeroVfx: шлейф на кинжале, вспышка отскока, пыль кувырка, вмятины от шагов;
    //  • в «Zhuque encounter» (не сереет после победы) — ZhuqueVfx: перья, огонь, пепел, полосы ветра, тень;
    //  • в каждом потоке лавы (внутри lavaRoot — включаются с лавой) — пузыри и искры с поверхности магмы,
    //    кроме мест под камнями переправ.
    // Запускать после Build Lava Look: пересобирает только свои объекты («Vfx …», «Hero VFX», «Zhuque VFX»).
    public static class SummerVfxBuilder
    {
        const string ArtDir = "Assets/_Game/Art/Vfx";
        const string DataDir = "Assets/_Game/Data/Vfx";
        const string HeroSettingsPath = DataDir + "/HeroVfxSettings.asset";
        const string SummerSettingsPath = "Assets/_Game/Data/Summer/ZhuqueVfxSettings.asset";
        const string LavaMeshDir = "Assets/_Game/Data/Summer/Lava/Vfx";
        const string Prefix = "Vfx ";

        static readonly Color Ash = new Color(0.36f, 0.33f, 0.31f);

        [MenuItem("Tools/Tan Shadow/Summer/Build Summer VFX (both scenes)")]
        public static void BuildBoth()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Build(SummerLavaCrossings.Scene01);
            Build(SummerLavaCrossings.Scene02);
        }

        public static void Build(string scenePath)
        {
            var assets = BuildAssets();
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            var encounter = Object.FindFirstObjectByType<ZhuqueEncounter>(FindObjectsInactive.Include);
            if (encounter == null) throw new System.InvalidOperationException("Нет ZhuqueEncounter в " + scenePath);

            var heroVfx = BuildHero(encounter, assets);
            BuildEncounter(encounter, heroVfx, assets);
            int emitters = BuildLava(encounter, assets, scenePath);

            EditorSceneManager.MarkSceneDirty(scene);
            AssetDatabase.SaveAssets();
            if (!EditorSceneManager.SaveScene(scene)) throw new System.Exception("Не удалось сохранить " + scenePath);
            Debug.Log($"[SummerVfx] {Path.GetFileNameWithoutExtension(scenePath)}: героиня, птица, лава ({emitters} потоков)");
        }

        // ---------------- Героиня ----------------

        static HeroVfx BuildHero(ZhuqueEncounter encounter, Assets a)
        {
            var hero = encounter.hero.transform;
            var old = hero.Find("Hero VFX");
            if (old != null) Object.DestroyImmediate(old.gameObject);
            foreach (var t in hero.GetComponentsInChildren<Transform>(true).Where(t => t.name == "Blade trail").ToArray())
                Object.DestroyImmediate(t.gameObject);

            var root = new GameObject("Hero VFX").transform;
            root.SetParent(hero, false);
            var rebound = Burst(root, "Rebound flash", a.rebound, 8, 0f, drag: 0f);
            Fade(rebound, Gradient2(Color.white, 1f, 0f), new AnimationCurve(new Keyframe(0f, 0.4f), new Keyframe(0.3f, 1f), new Keyframe(1f, 0.9f)));
            var dust = Burst(root, "Roll dust", a.dust, 40, -0.05f, drag: 3f);
            Fade(dust, Gradient3(0f, 0.55f, 0.15f, 0f), new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(1f, 1.6f)));

            var blade = hero.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Dagger_Mesh");
            TrailRenderer trail = null;
            if (blade != null)
            {
                var go = new GameObject("Blade trail");
                go.transform.SetParent(blade, false);
                go.transform.localPosition = new Vector3(0f, 0.17f, 0f);   // середина клинка (меш: y −0.09…0.25)
                trail = go.AddComponent<TrailRenderer>();
                trail.sharedMaterial = a.trail;
                trail.time = a.heroSettings.trailTime;
                trail.minVertexDistance = a.heroSettings.trailMinVertexDistance;
                trail.widthMultiplier = a.heroSettings.trailWidth;
                trail.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f));
                trail.colorGradient = Gradient2(new Color(1f, 0.93f, 0.85f), 0.85f, 0f);
                trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                trail.receiveShadows = false;
                trail.emitting = false;
                trail.autodestruct = false;
                trail.numCapVertices = 0;
            }
            else Debug.LogWarning("[SummerVfx] У героини нет Dagger_Mesh — шлейф меча не создан");

            var vfx = hero.GetComponent<HeroVfx>() ?? hero.gameObject.AddComponent<HeroVfx>();
            vfx.settings = a.heroSettings;
            vfx.trail = trail;
            vfx.rebound = rebound;
            vfx.rollDust = dust;
            vfx.footprintMesh = a.quad;
            vfx.footprintMaterial = a.footprint;
            vfx.groundColor = Ash;
            var lava = encounter.lava;
            vfx.softGround = lava != null && lava.dryBeds != null ? new[] { lava.dryBeds.transform } : new Transform[0];
            EditorUtility.SetDirty(vfx);
            return vfx;
        }

        // ---------------- Птица ----------------

        static void BuildEncounter(ZhuqueEncounter encounter, HeroVfx heroVfx, Assets a)
        {
            var host = encounter.transform;
            var old = host.Find("Zhuque VFX");
            if (old != null) Object.DestroyImmediate(old.gameObject);
            var root = new GameObject("Zhuque VFX").transform;
            root.SetParent(host, false);
            root.position = Vector3.zero;

            var s = a.summerSettings;
            var feathers = Burst(root, "Cut feathers", a.feathers, 40, 0.25f, drag: 2.2f);
            Fade(feathers, Gradient3(1f, 1f, 0.75f, 0f), new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.85f)));
            var sheet = feathers.textureSheetAnimation;
            sheet.enabled = true;
            sheet.numTilesX = 2;
            sheet.numTilesY = 2;
            sheet.frameOverTime = new ParticleSystem.MinMaxCurve(0f, 0.999f);   // у каждого пера своя форма
            var noise = feathers.noise;
            noise.enabled = true;
            noise.strength = 1.6f;
            noise.frequency = 0.7f;
            noise.quality = ParticleSystemNoiseQuality.Low;

            var ignite = Burst(root, "Ignite burst", a.ignite, 30, -0.15f, drag: 3.5f);
            var fire = new Gradient();
            fire.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.75f, 0.35f), 0f), new GradientColorKey(new Color(1f, 0.35f, 0.08f), 0.35f), new GradientColorKey(new Color(0.8f, 0.08f, 0.02f), 1f) },
                         new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.9f, 0.4f), new GradientAlphaKey(0f, 1f) });
            Fade(ignite, fire, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(0.35f, 1f), new Keyframe(1f, 0.4f)));

            var fallDust = Burst(root, "Fall dust", a.dust, 40, -0.04f, drag: 2.6f);
            var main = fallDust.main;
            main.startColor = Ash;
            Fade(fallDust, Gradient3(0f, 0.6f, 0.2f, 0f), new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(1f, 1.7f)));

            // SU-03: экранные полосы (Canvas поверх кадра, но под чёрным и белым экраном — тот рисуется в OnGUI).
            var windGo = new GameObject("Wind overlay", typeof(RectTransform));
            windGo.transform.SetParent(root, false);
            var canvas = windGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 40;
            canvas.enabled = false;

            // SU-10: силуэт тени, гасит свечение лавы.
            var shadowRoot = new GameObject("Bird shadow on lava").transform;
            shadowRoot.SetParent(root, false);
            var sil = new GameObject("Silhouette");
            sil.transform.SetParent(shadowRoot, false);
            sil.AddComponent<MeshFilter>().sharedMesh = a.quad;
            var mr = sil.AddComponent<MeshRenderer>();
            mr.sharedMaterial = a.shadow;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            shadowRoot.gameObject.SetActive(false);

            var vfx = root.gameObject.AddComponent<ZhuqueVfx>();
            vfx.settings = s;
            vfx.encounter = encounter;
            vfx.heroVfx = heroVfx;
            vfx.feathers = feathers;
            vfx.ignite = ignite;
            vfx.fallDust = fallDust;
            vfx.windCanvas = canvas;
            vfx.windTexture = a.windTex;
            vfx.shadowRoot = shadowRoot;
            vfx.shadowSilhouette = mr;
            EditorUtility.SetDirty(vfx);
        }

        // ---------------- Лава ----------------

        static int BuildLava(ZhuqueEncounter encounter, Assets a, string scenePath)
        {
            var lava = encounter.lava;
            if (lava == null || lava.rivers == null) return 0;
            var s = a.summerSettings;
            string dir = LavaMeshDir + "/" + Path.GetFileNameWithoutExtension(scenePath).Replace("Summer_Zhuque_", "");
            Directory.CreateDirectory(dir);

            bool wasActive = lava.lavaRoot.activeSelf;
            lava.lavaRoot.SetActive(true);
            if (lava.dryBeds != null) lava.dryBeds.SetActive(false);
            Physics.SyncTransforms();

            var made = new List<GameObject>();
            for (int r = 0; r < lava.rivers.Length; r++)
            {
                var river = lava.rivers[r];
                foreach (Transform child in river.transform.Cast<Transform>().ToArray())
                    if (child.name.StartsWith(Prefix)) Object.DestroyImmediate(child.gameObject);
                var molten = river.transform.Find("Look molten");
                if (molten == null) { Debug.LogWarning("[SummerVfx] Нет «Look molten» у " + river.name + " — сначала Build Lava Look"); continue; }
                var surface = molten.GetComponent<MeshFilter>().sharedMesh;
                var emit = OpenLava(surface, molten, lava);
                string path = dir + "/Lava emit " + (r + 1) + ".asset";
                AssetDatabase.DeleteAsset(path);
                AssetDatabase.CreateAsset(emit, path);

                var bubbles = LavaEmitter(river.transform, molten, emit, Prefix + "bubbles", a.bubbles, s.bubbleMax, s.bubbleRate,
                    s.bubbleLifetime, s.bubbleSize, Vector2.zero, 0.03f);
                var r1 = bubbles.GetComponent<ParticleSystemRenderer>();
                r1.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
                var sheet = bubbles.textureSheetAnimation;
                sheet.enabled = true;
                sheet.numTilesX = 4;
                sheet.numTilesY = 2;
                sheet.frameOverTime = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0f, 1f, 1f));
                Fade(bubbles, Gradient3(0f, 1f, 1f, 0.6f), null);

                var sparks = LavaEmitter(river.transform, molten, emit, Prefix + "sparks", a.sparks, s.sparkMax, s.sparkRate,
                    s.sparkLifetime, s.sparkSize, s.sparkSpeed, 0.1f);
                var r2 = sparks.GetComponent<ParticleSystemRenderer>();
                r2.renderMode = ParticleSystemRenderMode.Stretch;
                r2.velocityScale = 0.06f;
                r2.lengthScale = 1.5f;
                var sm = sparks.main;
                sm.gravityModifier = -0.04f;   // жар подхватывает искры
                var sparkColor = new Gradient();
                sparkColor.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.6f, 0.25f), 0f), new GradientColorKey(new Color(1f, 0.25f, 0.05f), 0.5f), new GradientColorKey(new Color(0.7f, 0.05f, 0.02f), 1f) },
                                   new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.1f), new GradientAlphaKey(0.8f, 0.6f), new GradientAlphaKey(0f, 1f) });
                Fade(sparks, sparkColor, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.3f)));
                var sn = sparks.noise;
                sn.enabled = true;
                sn.strength = 0.5f;
                sn.frequency = 1.2f;
                sn.quality = ParticleSystemNoiseQuality.Low;
                made.Add(bubbles.gameObject);
                made.Add(sparks.gameObject);
            }

            // Живые эффекты исчезают в сером мире.
            var world = lava.GetComponent<DeadWorld>() ?? encounter.world;
            if (world != null)
            {
                var alive = (world.aliveOnly ?? new GameObject[0]).Where(g => g != null && !g.name.StartsWith(Prefix)).ToList();
                alive.AddRange(made);
                world.aliveOnly = alive.ToArray();
                EditorUtility.SetDirty(world);
            }

            lava.lavaRoot.SetActive(wasActive);
            if (lava.dryBeds != null) lava.dryBeds.SetActive(true);
            return lava.rivers.Length;
        }

        // Поверхность магмы без треугольников под камнями переправ и валунами: искры и пузыри не вылетают из камня.
        // Камень — по видимому мешу (коллайдеры камней переправ — коробки меньше камня): эллипс в его границах
        // с запасом StoneMargin.
        const float StoneMargin = 0.35f;

        static Mesh OpenLava(Mesh surface, Transform space, ZhuqueLava lava)
        {
            var v = surface.vertices;
            var t = surface.triangles;
            var area = space.GetComponent<Renderer>().bounds;
            area.Expand(2f);
            var stones = new List<Bounds>();
            foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if (r.name.StartsWith("Look ") || r.name.StartsWith(Prefix) || !r.enabled || !area.Intersects(r.bounds)) continue;
                var col = r.GetComponentInParent<Collider>() ?? r.GetComponentInChildren<Collider>();
                if (col == null || col is TerrainCollider || System.Array.IndexOf(lava.rivers, col as MeshCollider) >= 0) continue;
                if (lava.dryBeds != null && r.transform.IsChildOf(lava.dryBeds.transform)) continue;
                if (r.bounds.size.x > 40f || r.bounds.size.z > 40f) continue;   // не камень: стены кальдеры и т. п.
                stones.Add(r.bounds);
            }
            var keep = new List<int>();
            for (int i = 0; i < t.Length; i += 3)
            {
                Vector3 c = space.TransformPoint((v[t[i]] + v[t[i + 1]] + v[t[i + 2]]) / 3f);
                bool covered = false;
                foreach (var b in stones)
                {
                    float rx = b.extents.x + StoneMargin, rz = b.extents.z + StoneMargin;
                    float dx = (c.x - b.center.x) / rx, dz = (c.z - b.center.z) / rz;
                    if (dx * dx + dz * dz <= 1f && c.y < b.max.y) { covered = true; break; }
                }
                if (!covered) { keep.Add(t[i]); keep.Add(t[i + 1]); keep.Add(t[i + 2]); }
            }
            var m = new Mesh { name = surface.name + " open" };
            m.indexFormat = v.Length > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            m.vertices = v;
            m.normals = surface.normals;
            m.triangles = keep.ToArray();
            m.RecalculateBounds();
            return m;
        }

        static ParticleSystem LavaEmitter(Transform river, Transform molten, Mesh emit, string name, Material material, int max, float rate,
                                          Vector2 lifetime, Vector2 size, Vector2 speed, float lift)
        {
            var go = new GameObject(name);
            go.transform.SetParent(river, false);
            go.transform.localPosition = molten.localPosition + river.InverseTransformVector(Vector3.up * lift);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = true;
            main.maxParticles = max;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime.x, lifetime.y);
            main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed.x, speed.y);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Shape;
            var emission = ps.emission;
            emission.rateOverTime = rate;
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Mesh;
            shape.meshShapeType = ParticleSystemMeshShapeType.Triangle;
            shape.mesh = emit;
            shape.randomDirectionAmount = 0.25f;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            Setup(renderer, material);
            return ps;
        }

        // ---------------- Частицы ----------------

        // Система под Emit из кода: всегда «играет», сама не выпускает; положение, скорость и размер — в EmitParams.
        static ParticleSystem Burst(Transform parent, string name, Material material, int max, float gravity, float drag)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = true;
            main.maxParticles = max;
            main.startSpeed = 0f;
            main.startLifetime = 1f;
            main.gravityModifier = gravity;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;   // выброс за кадром не замирает до следующего взгляда
            var emission = ps.emission;
            emission.enabled = false;
            var shape = ps.shape;
            shape.enabled = false;
            if (drag > 0f)
            {
                var limit = ps.limitVelocityOverLifetime;
                limit.enabled = true;
                limit.limit = 100f;
                limit.drag = drag;
            }
            Setup(go.GetComponent<ParticleSystemRenderer>(), material);
            return ps;
        }

        static void Setup(ParticleSystemRenderer r, Material material)
        {
            r.sharedMaterial = material;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            r.maxParticleSize = 2f;
        }

        static void Fade(ParticleSystem ps, Gradient color, AnimationCurve size)
        {
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = color;
            if (size == null) return;
            var sz = ps.sizeOverLifetime;
            sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, size);
        }

        static Gradient Gradient2(Color c, float a0, float a1)
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(c, 0f), new GradientColorKey(c, 1f) }, new[] { new GradientAlphaKey(a0, 0f), new GradientAlphaKey(a1, 1f) });
            return g;
        }

        // Белый, альфа: старт → пик (в 15 %) → к 70 % → конец.
        static Gradient Gradient3(float a0, float peak, float late, float a1)
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(a0, 0f), new GradientAlphaKey(peak, 0.15f), new GradientAlphaKey(late, 0.7f), new GradientAlphaKey(a1, 1f) });
            return g;
        }

        // ---------------- Ассеты ----------------

        class Assets
        {
            public HeroVfxSettings heroSettings;
            public ZhuqueVfxSettings summerSettings;
            public Mesh quad;
            public Texture2D windTex;
            public Material trail, rebound, dust, feathers, ignite, bubbles, sparks, shadow, footprint;
        }

        static Assets BuildAssets()
        {
            Directory.CreateDirectory(ArtDir);
            Directory.CreateDirectory(DataDir);
            VfxTextures.Write(ArtDir);
            AssetDatabase.Refresh();
            foreach (var name in VfxTextures.Names) Import(ArtDir + "/" + name + ".png");

            var a = new Assets
            {
                heroSettings = Settings<HeroVfxSettings>(HeroSettingsPath),
                summerSettings = Settings<ZhuqueVfxSettings>(SummerSettingsPath),
                quad = Quad(),
                windTex = Tex("T_VfxWindStreak"),
            };
            a.trail = Mat("M_VfxBladeTrail", "T_VfxTrail", 0, a.heroSettings.trailNits, Color.white);
            a.rebound = Mat("M_VfxRebound", "T_VfxSoftDot", 1, 2500f, new Color(1f, 0.9f, 0.75f));
            a.dust = Mat("M_VfxAshDust", "T_VfxPuff", 0, 35f, Color.white);   // пепел тёмный: ярче — белые облака
            a.feathers = Mat("M_VfxFeathers", "T_VfxFeathers", 0, 900f, Color.white);
            a.ignite = Mat("M_VfxIgnite", "T_VfxSoftDot", 1, 4500f, Color.white);
            a.bubbles = Mat("M_VfxLavaBubbles", "T_VfxBubbles", 0, 110f, Color.white);
            a.sparks = Mat("M_VfxLavaSparks", "T_VfxSoftDot", 1, 9000f, Color.white);
            a.shadow = Mat("M_VfxBirdShadow", "T_VfxBirdShadow", 2, 1f, Color.white);
            a.shadow.renderQueue = 3100;   // после лавы, пара и искр: гасит их свечение
            a.footprint = Mat("M_VfxFootprint", "T_VfxFootprint", 2, 1f, Color.white);
            a.footprint.renderQueue = 2990;
            AssetDatabase.SaveAssets();
            return a;
        }

        static T Settings<T>(string path) where T : ScriptableObject
        {
            var s = AssetDatabase.LoadAssetAtPath<T>(path);
            if (s != null) return s;
            s = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(s, path);
            return s;
        }

        static Mesh Quad()
        {
            string path = DataDir + "/VFX quad XZ.asset";
            var m = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (m != null) return m;
            m = new Mesh { name = "VFX quad XZ" };
            m.vertices = new[] { new Vector3(-0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, 0.5f), new Vector3(-0.5f, 0f, 0.5f) };
            m.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
            m.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            m.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            m.RecalculateBounds();
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        // mode: 0 — прозрачность, 1 — сложение, 2 — затемнение (см. VfxUnlit.shader).
        static Material Mat(string name, string texture, int mode, float nits, Color color)
        {
            string path = ArtDir + "/" + name + ".mat";
            var shader = Shader.Find("TanShadow/VfxUnlit");
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(shader) { name = name }; AssetDatabase.CreateAsset(m, path); }
            m.shader = shader;
            m.SetTexture("_MainTex", Tex(texture));
            m.SetColor("_Color", color);
            m.SetFloat("_Nits", nits);
            m.SetFloat("_Mode", mode);
            var src = mode == 0 ? UnityEngine.Rendering.BlendMode.SrcAlpha : mode == 1 ? UnityEngine.Rendering.BlendMode.One : UnityEngine.Rendering.BlendMode.DstColor;
            var dst = mode == 0 ? UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha : mode == 1 ? UnityEngine.Rendering.BlendMode.One : UnityEngine.Rendering.BlendMode.Zero;
            m.SetFloat("_SrcBlend", (float)src);
            m.SetFloat("_DstBlend", (float)dst);
            m.renderQueue = 3000;
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        static Texture2D Tex(string name) => AssetDatabase.LoadAssetAtPath<Texture2D>(ArtDir + "/" + name + ".png");

        static void Import(string path)
        {
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.textureType = TextureImporterType.Default;
            imp.sRGBTexture = true;
            imp.alphaIsTransparency = true;
            imp.wrapMode = TextureWrapMode.Clamp;
            imp.mipmapEnabled = true;
            imp.SaveAndReimport();
        }

        // ---------------- Процедурные текстуры эффектов ----------------

        static class VfxTextures
        {
            public static readonly string[] Names =
                { "T_VfxSoftDot", "T_VfxPuff", "T_VfxTrail", "T_VfxFeathers", "T_VfxBubbles", "T_VfxWindStreak", "T_VfxBirdShadow", "T_VfxFootprint" };

            public static void Write(string dir)
            {
                SoftDot(dir, 64);
                Puff(dir, 128);
                Trail(dir, 64, 32);
                Feathers(dir, 256);
                Bubbles(dir, 128);
                Wind(dir, 128, 16);
                BirdShadow(dir, 256);
                Footprint(dir, 64, 128);
            }

            static void SoftDot(string dir, int n)
            {
                var px = new Color[n * n];
                for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f) / n, new Vector2(0.5f, 0.5f)) * 2f;
                    float a = Mathf.Exp(-d * d * 5f) * (1f - Smooth(0.85f, 1f, d));
                    px[y * n + x] = new Color(1f, 1f, 1f, a);
                }
                Png(dir + "/T_VfxSoftDot.png", n, n, px);
            }

            static void Puff(string dir, int n)
            {
                var px = new Color[n * n];
                for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n, v = (y + 0.5f) / n;
                    float d = Vector2.Distance(new Vector2(u, v), new Vector2(0.5f, 0.5f)) * 2f;
                    float noise = Fbm(u * 4f, v * 4f, 4, 11);
                    float a = (1f - Smooth(0.25f, 1f, d + (noise - 0.5f) * 0.5f)) * (0.55f + 0.45f * noise);
                    float shade = 0.8f + 0.25f * noise;
                    px[y * n + x] = new Color(shade, shade, shade, Mathf.Clamp01(a));
                }
                Png(dir + "/T_VfxPuff.png", n, n, px);
            }

            // Шлейф: u — вдоль, v — поперёк.
            static void Trail(string dir, int w, int h)
            {
                var px = new Color[w * h];
                for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float v = (y + 0.5f) / h;
                    float a = Mathf.Exp(-Mathf.Pow((v - 0.5f) / 0.28f, 2f));
                    px[y * w + x] = new Color(1f, 1f, 1f, a);
                }
                Png(dir + "/T_VfxTrail.png", w, h, px);
            }

            // Атлас 2×2: три формы пера и вариант первой — оранжево-золотые у основания, красные к концу.
            static void Feathers(string dir, int n)
            {
                var px = new Color[n * n];
                int cell = n / 2;
                for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int id = (y / cell) * 2 + x / cell;
                    float u = ((x % cell) + 0.5f) / cell * 2f - 1f;   // −1…1 поперёк
                    float v = ((y % cell) + 0.5f) / cell;            // 0…1 вдоль (0 — основание)
                    float width = id == 1 ? 0.28f : id == 2 ? 0.48f : 0.38f;
                    float bend = id == 2 ? 0.18f : id == 3 ? -0.12f : 0.08f;
                    float shaft = u - bend * v * v;
                    float profile = Mathf.Pow(Mathf.Sin(Mathf.PI * Mathf.Clamp01(v * 1.05f)), 0.6f) * width * (shaft > 0f ? 1f : 0.8f);
                    float notch = 0.85f + 0.15f * Mathf.Sin(v * 60f + id * 3f);
                    float inside = 1f - Smooth(profile * notch - 0.06f, profile * notch, Mathf.Abs(shaft));
                    if (v < 0.04f || v > 0.98f) inside = 0f;
                    float stem = Mathf.Abs(shaft) < 0.025f && v < 0.98f ? 1f : 0f;
                    Color c = Color.Lerp(new Color(1f, 0.62f, 0.2f), new Color(0.85f, 0.12f, 0.05f), Smooth(0.2f, 0.9f, v));
                    c = Color.Lerp(c, new Color(1f, 0.85f, 0.55f), stem * 0.6f);
                    px[y * n + x] = new Color(c.r, c.g, c.b, Mathf.Max(inside, stem * 0.9f));
                }
                Png(dir + "/T_VfxFeathers.png", n, n, px);
            }

            // Флипбук 4×2 (кадры слева направо, сверху вниз): пузырь вздувается, лопается кольцом.
            static void Bubbles(string dir, int cell)
            {
                int w = cell * 4, h = cell * 2;
                var px = new Color[w * h];
                for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int frame = (1 - y / cell) * 4 + x / cell;   // верхний ряд — кадры 0…3
                    float t = frame / 7f;
                    float u = ((x % cell) + 0.5f) / cell * 2f - 1f, v = ((y % cell) + 0.5f) / cell * 2f - 1f;
                    float d = Mathf.Sqrt(u * u + v * v);
                    Color c; float a;
                    if (t < 0.72f)
                    {
                        float r = Mathf.Lerp(0.25f, 0.8f, t / 0.72f);
                        float dome = 1f - Smooth(r - 0.08f, r, d);
                        float rimDark = Smooth(r * 0.55f, r, d);
                        float highlight = Mathf.Exp(-((u + r * 0.3f) * (u + r * 0.3f) + (v - r * 0.35f) * (v - r * 0.35f)) / (0.03f * r));
                        c = Color.Lerp(new Color(1f, 0.72f, 0.2f), new Color(0.6f, 0.17f, 0.03f), rimDark * 0.85f);
                        c = Color.Lerp(c, new Color(1f, 0.92f, 0.5f), Mathf.Clamp01(highlight));
                        a = dome;
                    }
                    else
                    {
                        float k = (t - 0.72f) / 0.28f;
                        float r = Mathf.Lerp(0.8f, 0.98f, k);
                        float ring = Mathf.Exp(-Mathf.Pow((d - r) / 0.05f, 2f));
                        c = new Color(1f, 0.55f, 0.12f);
                        a = ring * (1f - k * 0.8f);
                    }
                    px[y * w + x] = new Color(c.r, c.g, c.b, Mathf.Clamp01(a));
                }
                Png(dir + "/T_VfxBubbles.png", w, h, px);
            }

            // Полоса ветра: яркая голова у внешнего (правого) конца, хвост к центру экрана.
            static void Wind(string dir, int w, int h)
            {
                var px = new Color[w * h];
                for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float u = (x + 0.5f) / w, v = (y + 0.5f) / h;
                    float a = Mathf.Exp(-Mathf.Pow((v - 0.5f) / 0.25f, 2f)) * Mathf.Pow(u, 1.6f) * (1f - Smooth(0.9f, 1f, u));
                    px[y * w + x] = new Color(1f, 1f, 1f, a);
                }
                Png(dir + "/T_VfxWindStreak.png", w, h, px);
            }

            // Силуэт птицы сверху (v вверх — клюв): корпус, голова, крылья, хвост; края мягкие, как у тени.
            static void BirdShadow(string dir, int n)
            {
                var px = new Color[n * n];
                for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    var p = new Vector2((x + 0.5f) / n, (y + 0.5f) / n);
                    float d = Ellipse(p, new Vector2(0.5f, 0.55f), new Vector2(0.06f, 0.16f));
                    d = Mathf.Min(d, Vector2.Distance(p, new Vector2(0.5f, 0.74f)) - 0.035f);
                    for (int side = -1; side <= 1; side += 2)
                    {
                        Vector2 shoulder = new Vector2(0.5f + side * 0.04f, 0.6f);
                        Vector2 tip = new Vector2(0.5f + side * 0.48f, 0.56f);
                        d = Mathf.Min(d, Tapered(p, shoulder, tip, 0.085f, 0.015f));
                        d = Mathf.Min(d, Tapered(p, new Vector2(0.5f + side * 0.1f, 0.54f), new Vector2(0.5f + side * 0.36f, 0.46f), 0.06f, 0.012f));
                    }
                    d = Mathf.Min(d, Tapered(p, new Vector2(0.5f, 0.42f), new Vector2(0.5f, 0.03f), 0.025f, 0.06f));
                    float a = 1f - Smooth(-0.01f, 0.02f, d);
                    px[y * n + x] = new Color(1f, 1f, 1f, a);
                }
                Png(dir + "/T_VfxBirdShadow.png", n, n, px);
            }

            // Вмятина: овал, темнее к центру; v — вдоль стопы.
            static void Footprint(string dir, int w, int h)
            {
                var px = new Color[w * h];
                for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    var p = new Vector2((x + 0.5f) / w * 2f - 1f, (y + 0.5f) / h * 2f - 1f);
                    float d = Mathf.Sqrt(p.x * p.x / 0.8f + p.y * p.y);
                    float a = 1f - Smooth(0.6f, 1f, d);
                    float shade = Mathf.Lerp(0.72f, 1f, Smooth(0.1f, 0.95f, d));
                    px[y * w + x] = new Color(shade, shade, shade, a);
                }
                Png(dir + "/T_VfxFootprint.png", w, h, px);
            }

            static float Ellipse(Vector2 p, Vector2 c, Vector2 r)
            {
                Vector2 q = new Vector2((p.x - c.x) / r.x, (p.y - c.y) / r.y);
                return (q.magnitude - 1f) * Mathf.Min(r.x, r.y);
            }

            // Расстояние до отрезка с радиусом, меняющимся от ra к rb.
            static float Tapered(Vector2 p, Vector2 a, Vector2 b, float ra, float rb)
            {
                Vector2 ab = b - a;
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
                return Vector2.Distance(p, a + ab * t) - Mathf.Lerp(ra, rb, t);
            }

            static void Png(string path, int w, int h, Color[] pixels)
            {
                var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, false);
                tex.SetPixels(pixels);
                tex.Apply();
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
            }

            static float Hash(int x, int y, int seed)
            {
                unchecked
                {
                    uint h = (uint)(x * 374761393 + y * 668265263 + seed * 982451653);
                    h = (h ^ (h >> 13)) * 1274126177u;
                    h ^= h >> 16;
                    return (h & 0xFFFFFF) / 16777216f;
                }
            }

            static float Value(float x, float y, int period, int seed)
            {
                int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
                float fx = x - xi, fy = y - yi;
                fx = fx * fx * (3f - 2f * fx);
                fy = fy * fy * (3f - 2f * fy);
                int x0 = Mod(xi, period), x1 = Mod(xi + 1, period), y0 = Mod(yi, period), y1 = Mod(yi + 1, period);
                return Mathf.Lerp(Mathf.Lerp(Hash(x0, y0, seed), Hash(x1, y0, seed), fx), Mathf.Lerp(Hash(x0, y1, seed), Hash(x1, y1, seed), fx), fy);
            }

            static float Fbm(float x, float y, int period, int seed)
            {
                float sum = 0f, amp = 0.5f, norm = 0f;
                for (int o = 0; o < 4; o++)
                {
                    sum += Value(x, y, period, seed + o * 17) * amp;
                    norm += amp;
                    amp *= 0.5f;
                    x *= 2f; y *= 2f; period *= 2;
                }
                return sum / norm;
            }

            static int Mod(int a, int m) => ((a % m) + m) % m;

            static float Smooth(float edge0, float edge1, float x)
            {
                float t = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
                return t * t * (3f - 2f * t);
            }
        }
    }
}
