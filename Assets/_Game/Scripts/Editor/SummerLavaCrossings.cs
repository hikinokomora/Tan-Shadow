using System.Collections.Generic;
using System.IO;
using System.Linq;
using TanShadow.Seasons;
using TanShadow.Summer;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TanShadow.EditorTools
{
    // Лава сцены Summer_Zhuque_02_Lava: от каждого валуна Volcanic_Dolerite_Cliff_qjycI у подножия вулкана —
    // широкий поток до стены кольца, чуть выше пола. Через каждый поток — переправа:
    // невысокий подъём → камни для рывков над лавой → камень со спуском на другом берегу.
    // Всё это — группа лавы ZhuqueLava: появляется на третьем ударе, до того русла сухие.
    // Повторный запуск пересобирает группу лавы; валуны qjycI и остальные камни не трогает.
    public static class SummerLavaCrossings
    {
        public const string Scene01 = "Assets/_Game/Scenes/Summer_Zhuque_01_Dry.unity";
        public const string Scene02 = "Assets/_Game/Scenes/Summer_Zhuque_02_Lava.unity";
        static string ScenePath = Scene02;
        const string SourceRock = "Volcanic_Dolerite_Cliff_qjycI_Low";
        const string BaseMeshDir = "Assets/_Game/Data/Summer/Lava";
        static string MeshDir = BaseMeshDir;   // у 01_Dry — своя подпапка, у 02_Lava — исторически корень
        const string ArtDir = "Assets/_Game/Art/SummerMegascans";
        const string LavaMaterial = "Assets/ZhunqueWideArena/Lava/Materials/Animated Lava HDRP.mat";
        const string RootName = "Summer - Zhuque";
        const string OldLavaName = "07 - Open lava rivers and heat";
        const string LavaGroupName = "07 - Lava flows (third cut)";
        const string DryGroupName = "Dry beds (future lava)";
        const string RocksGroup = "03 - Basalt cover and perches";
        const float MaxBend = 9f;              // насколько поток уходит вбок от прямой, м

        // Рывок 3.5 м за 0.25 с, бег 4.5 м/с (CombatSettings), гравитация −20, stepOffset 0.3, капсула r 0.4.
        // Над щелью 1.8 м шагом героиня проседает ниже края следующего камня и падает в лаву; рывком
        // проседает меньше stepOffset и заходит на камень (проверено капсулой героини на 30/60/144 fps).
        // Камни идут не по прямой, а зигзагом вдоль потока (Zigzag, м): щели по 1.8 м меряются по линии
        // от камня к камню, поэтому поперёк потока переправа чуть уже 4 × 2.6 + 5 × 1.8 = 19.4 м.
        const int Stones = 4;
        static readonly float[] Zigzag = { 1.3f, -1.1f, 1.2f, -1.3f };
        const float StoneLength = 2.6f, StoneWidth = 2.6f, Gap = 1.8f;
        const float StoneAboveLava = 0.6f;     // верх камней над лавой
        const float LavaAboveFloor = 0.15f;    // лава чуть выше пола, без русла
        const float DryBedAboveFloor = 0.08f;
        const float RampLength = 3.6f;         // подъём ~0.75 м на 3.6 м — около 12°
        const float WallCap = 1.2f;            // у стены лава заходит на склон не выше этого
        const float VolcanoCap = 3f;           // у вулкана лава уходит под валун-источник
        static float HalfWidth => (Stones * StoneLength + (Stones + 1) * Gap) * 0.5f;

        // Центры камней переправы: x — поперёк потока (от −CrossHalf до +CrossHalf), y — вдоль потока.
        // Расстояние от края подъёма до центра первого камня — Gap + половина камня, между центрами —
        // камень + Gap, от последнего до края спуска — снова Gap + половина. mirror — зигзаг в другую сторону.
        static Vector2[] StoneLayout(bool mirror, out float half)
        {
            var points = new Vector2[Stones];
            Vector2 prev = Vector2.zero;
            for (int i = 0; i < Stones; i++)
            {
                float length = i == 0 ? Gap + StoneLength * 0.5f : StoneLength + Gap;
                float y = Zigzag[i] * (mirror ? -1f : 1f);
                float dy = y - prev.y;
                prev = new Vector2(prev.x + Mathf.Sqrt(length * length - dy * dy), y);
                points[i] = prev;
            }
            float last = Gap + StoneLength * 0.5f;
            float total = prev.x + Mathf.Sqrt(last * last - prev.y * prev.y);
            half = total * 0.5f;
            for (int i = 0; i < Stones; i++) points[i].x -= half;
            return points;
        }

        static float CrossHalf { get { StoneLayout(false, out float h); return h; } }

        static readonly string[] StoneModels =
        {
            "Icelandic_Rock_Formation_tatvh_Low", "Icelandic_Sandy_Volcanic_Rock_slnpT_Low", "Icelandic_Rock_slquo_Low",
        };
        const string PlateModel = "Icelandic_Rock_Plates_taerZ_Low";
        const string RampSupportModel = "Volcanic_Dolerite_Rocks_qfjwU_Low";
        const string DescentRockModel = "Icelandic_Rock_Formation_slesW_Low";

        static Terrain[] tiles;
        static System.Random rng;

        [MenuItem("Tools/Tan Shadow/Summer/Build Lava Crossings (02_Lava)")]
        public static void BuildFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Build(Scene02);
        }

        [MenuItem("Tools/Tan Shadow/Summer/Build Lava Crossings (01_Dry)")]
        public static void BuildFromMenu01()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Build(Scene01);
        }

        public static void Build() => Build(Scene02);

        // Одинаковые потоки в обеих сценах: валуны, насесты и укрытия в них одни и те же, а случайность — с одним зерном.
        public static void Build(string scenePath)
        {
            ScenePath = scenePath;
            MeshDir = scenePath == Scene02 ? BaseMeshDir : BaseMeshDir + "/" + Path.GetFileNameWithoutExtension(scenePath).Replace("Summer_Zhuque_", "");
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var roots = scene.GetRootGameObjects();
            var root = roots.First(g => g.name == RootName).transform;
            tiles = roots.SelectMany(g => g.GetComponentsInChildren<Terrain>(true)).ToArray();
            rng = new System.Random(425);

            var sources = roots.SelectMany(g => g.GetComponentsInChildren<Transform>(true))
                .Where(t => t.name.StartsWith(SourceRock) && PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject))
                .OrderBy(t => Mathf.Repeat(Mathf.Atan2(t.position.z, t.position.x) * Mathf.Rad2Deg, 360f)).ToArray();
            if (sources.Length == 0) throw new System.InvalidOperationException("В сцене нет валунов " + SourceRock);

            var encounter = root.GetComponentInChildren<ZhuqueEncounter>(true);
            var lava = encounter.GetComponent<ZhuqueLava>();
            var world = encounter.GetComponent<DeadWorld>();

            // Свет и пар — из старой группы рек грейбокса (или из уже собранной группы при повторном запуске).
            var old = root.Find(OldLavaName) ?? root.Find(LavaGroupName);
            var heatTemplate = Object.Instantiate(old.GetComponentsInChildren<Light>(true).First(l => !l.name.StartsWith("Look ")).gameObject);
            var steamTemplate = Object.Instantiate(old.GetComponentsInChildren<ParticleSystem>(true).First(p => !p.name.StartsWith("Look ")).gameObject);
            // Засыпанные русла старой лавы (грейбокс) — на других местах: убираем, новые строятся ниже.
            if (lava.dryBeds != null && lava.dryBeds.name != DryGroupName) Object.DestroyImmediate(lava.dryBeds);
            foreach (var name in new[] { OldLavaName, LavaGroupName, DryGroupName })
            {
                var t = root.Find(name);
                if (t != null) Object.DestroyImmediate(t.gameObject);
            }

            Directory.CreateDirectory(MeshDir);
            var lavaGroup = new GameObject(LavaGroupName).transform;
            lavaGroup.SetParent(root, false);
            var dryGroup = new GameObject(DryGroupName).transform;
            dryGroup.SetParent(root, false);
            var material = AssetDatabase.LoadAssetAtPath<Material>(LavaMaterial);
            var rivers = new List<MeshCollider>();
            var steam = new List<GameObject>();

            var obstacles = root.Find(RocksGroup).GetComponentsInChildren<Renderer>(true)
                .Where(r => !r.transform.IsChildOf(root.Find(RocksGroup + "/Vulkan"))).Select(r => r.bounds).ToArray();
            for (int i = 0; i < sources.Length; i++)
            {
                var src = sources[i];
                Vector3 d = Flat(src.position).normalized;
                float rockOuter = OuterEdge(src, d);
                float wallFoot = FirstAbove(d, rockOuter, 0.6f);
                float s0 = Flat(src.position).magnitude;            // начинается из-под валуна
                float s1 = FirstAbove(d, wallFoot, WallCap) + 1.5f;  // и чуть заходит на склон стены
                float rc = (rockOuter + wallFoot) * 0.5f;           // переправа посередине дна
                float mid = rc;
                var path = ChoosePath(d, s0, s1, obstacles);
                Vector3 t = path.Normal(rc);

                var flow = new GameObject("Lava flow " + (i + 1) + " (from " + src.name + ")").transform;
                flow.SetParent(lavaGroup, false);

                var river = new GameObject("Molten river " + (i + 1));
                river.transform.SetParent(flow, false);
                var mesh = SaveMesh(FlowMesh(i, path, s0, s1, rc, mid, LavaAboveFloor), "Lava flow " + (i + 1));
                river.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = river.AddComponent<MeshRenderer>();
                mr.sharedMaterial = material;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                var mc = river.AddComponent<MeshCollider>();
                mc.sharedMesh = mesh;
                rivers.Add(mc);

                // Невидимое «будущее русло»: ZhuqueLava не открывает лаву под героиней, а птица не роняет туда хвост.
                var dry = new GameObject("Dry bed " + (i + 1));
                dry.transform.SetParent(dryGroup, false);
                dry.AddComponent<MeshCollider>().sharedMesh =
                    SaveMesh(FlowMesh(i, path, s0, s1, rc, mid, DryBedAboveFloor), "Dry bed " + (i + 1));

                BuildCrossing(flow, i, path.Tangent(rc), t, path.Center(rc));

                foreach (float s in new[] { rc - 10f, rc + 10f, rockOuter })
                {
                    var light = Object.Instantiate(heatTemplate, flow);
                    light.name = "Reflected heat";
                    Vector3 at = path.Center(s);
                    light.transform.position = new Vector3(at.x, H(at) + 1.5f, at.z);
                }
                foreach (float s in new[] { rockOuter + 1f, wallFoot - 1f })
                {
                    var fx = Object.Instantiate(steamTemplate, flow);
                    fx.name = "Fumarole";
                    Vector3 at = path.Center(s);
                    fx.transform.position = new Vector3(at.x, H(at) + LavaAboveFloor, at.z);
                    steam.Add(fx);
                }
            }
            Object.DestroyImmediate(heatTemplate);
            Object.DestroyImmediate(steamTemplate);
            lavaGroup.gameObject.AddComponent<ZhunqueWideArena.LavaTextureFlow>();

            lava.lavaRoot = lavaGroup.gameObject;
            lava.dryBeds = dryGroup.gameObject;
            lava.rivers = rivers.ToArray();
            EditorUtility.SetDirty(lava);
            if (world != null)
            {
                world.aliveOnly = steam.ToArray();
                EditorUtility.SetDirty(world);
            }
            // В редакторе лава видна, чтобы её было удобно двигать; в игре ZhuqueLava прячет её до третьего удара.
            lavaGroup.gameObject.SetActive(true);

            EditorSceneManager.MarkSceneDirty(scene);
            AssetDatabase.SaveAssets();
            if (!EditorSceneManager.SaveScene(scene)) throw new System.Exception("Не удалось сохранить " + ScenePath);
            Debug.Log($"[SummerLavaCrossings] Потоков лавы с переправами: {sources.Length}");
        }

        // ---------------- Поток ----------------

        static Mesh FlowMesh(int b, FlowPath path, float s0, float s1, float rc, float mid, float above)
        {
            const int across = 18;
            int rows = Mathf.CeilToInt(s1 - s0) + 1;
            var verts = new Vector3[rows * (across + 1)];
            var uv = new Vector2[verts.Length];
            var heights = new float[verts.Length];
            for (int i = 0; i < rows; i++)
            {
                float s = s0 + i;
                float hw = Width(b, s, rc);
                Vector3 c = path.Center(s), n = path.Normal(s);
                for (int j = 0; j <= across; j++)
                {
                    float u = Mathf.Lerp(-hw, hw, j / (float)across);
                    Vector3 p = c + n * u;
                    float h = H(p);
                    int k = i * (across + 1) + j;
                    heights[k] = h;
                    verts[k] = new Vector3(p.x, h + above, p.z);
                    uv[k] = new Vector2((u + hw) / 18f, s / 18f);
                }
            }
            var tris = new List<int>();
            for (int i = 0; i < rows - 1; i++)
            {
                float cap = s0 + i < mid ? VolcanoCap : WallCap;
                for (int j = 0; j < across; j++)
                {
                    int a = i * (across + 1) + j, c = a + across + 1;
                    if (heights[a] > cap || heights[a + 1] > cap || heights[c] > cap || heights[c + 1] > cap) continue;
                    // По часовой, если смотреть сверху: лицом вверх — и для рендера, и для лучей вниз.
                    tris.AddRange(new[] { a, a + 1, c, a + 1, c + 1, c });
                }
            }
            var mesh = new Mesh { name = "Lava flow " + (b + 1), indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.vertices = verts;
            mesh.uv = uv;
            mesh.triangles = tris.ToArray();
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        // Края неровные, но у переправы ширина ровно 2 × CrossHalf: там посчитаны щели для рывков.
        static float Width(int b, float s, float rc)
        {
            float wobble = (Mathf.PerlinNoise(s * 0.08f, b * 7.3f) - 0.5f) * 4f;
            return CrossHalf + wobble * Mathf.Clamp01((Mathf.Abs(s - rc) - 4f) / 6f);
        }

        // Ось потока — не прямая: одна плавная дуга от валуна-источника до стены, вбок до MaxBend.
        // Радиус изгиба не меньше полуширины + 3 м, иначе внутренний край потока складывается.
        struct FlowPath
        {
            public Vector3 d, t;
            public float s0, length, bend;

            float X(float s) => Mathf.Clamp01((s - s0) / length);
            float Offset(float s) => bend * Mathf.Sin(Mathf.PI * X(s));
            float Slope(float s) => bend * Mathf.PI / length * Mathf.Cos(Mathf.PI * X(s));
            public Vector3 Center(float s) => d * s + t * Offset(s);
            public Vector3 Tangent(float s) => (d + t * Slope(s)).normalized;
            public Vector3 Normal(float s) { var k = Tangent(s); return new Vector3(-k.z, 0f, k.x); }
        }

        // Изгиб в ту сторону и такой силы, чтобы поток меньше всего задевал насесты и укрытия.
        static FlowPath ChoosePath(Vector3 d, float s0, float s1, Bounds[] obstacles)
        {
            var path = new FlowPath { d = d, t = new Vector3(-d.z, 0f, d.x), s0 = s0, length = Mathf.Max(1f, s1 - s0) };
            float limit = Mathf.Min(MaxBend, path.length * path.length / (Mathf.PI * Mathf.PI) / (HalfWidth + 3f));
            FlowPath best = path;
            int bestHits = int.MaxValue;
            foreach (float k in new[] { 1f, -1f, 0.6f, -0.6f, 0.3f, -0.3f })
            {
                path.bend = limit * k;
                int hits = 0;
                for (float s = s0 + 4f; s <= s1; s += 1f)
                    for (float u = -HalfWidth - 1f; u <= HalfWidth + 1f; u += 1f)
                    {
                        Vector3 p = path.Center(s) + path.Normal(s) * u;
                        foreach (var b in obstacles)
                            if (p.x > b.min.x && p.x < b.max.x && p.z > b.min.z && p.z < b.max.z) { hits++; break; }
                    }
                if (hits < bestHits) { bestHits = hits; best = path; }
            }
            return best;
        }

        // ---------------- Переправа ----------------

        // d — вдоль потока, t — поперёк (по линии переправы), center — середина переправы на оси потока.
        static void BuildCrossing(Transform flow, int b, Vector3 d, Vector3 t, Vector3 center)
        {
            var group = new GameObject("Crossing " + (b + 1)).transform;
            group.SetParent(flow, false);
            center = Flat(center);

            var layout = StoneLayout(b % 2 == 1, out float half);
            float reach = StoneWidth * 0.5f + 1.5f;

            // Верх камней — на StoneAboveLava выше самой высокой лавы в полосе переправы.
            float lavaTop = float.MinValue;
            for (float u = -half; u <= half; u += 0.5f)
                for (float w = -reach; w <= reach; w += 0.5f)
                    lavaTop = Mathf.Max(lavaTop, H(center + d * w + t * u) + LavaAboveFloor);
            float top = lavaTop + StoneAboveLava;

            // С одной стороны — подъём (плита), с другой — камень со спуском.
            Ramp(group, center, t, -1, top, "Ramp up", false, half);
            Ramp(group, center, t, 1, top, "Descent stone", true, half);

            for (int i = 0; i < Stones; i++)
            {
                Vector3 c = center + t * layout[i].x + d * layout[i].y;
                // Камень смотрит вдоль линии «предыдущий → следующий»: щели по ходу рывка — ровно Gap.
                Vector2 prev = i == 0 ? new Vector2(-half, 0f) : layout[i - 1];
                Vector2 next = i == Stones - 1 ? new Vector2(half, 0f) : layout[i + 1];
                Vector2 k = (next - prev).normalized;
                Vector3 along = (t * k.x + d * k.y).normalized;
                var stone = new GameObject("Stone " + (i + 1));
                stone.transform.SetParent(group, false);
                stone.transform.SetPositionAndRotation(new Vector3(c.x, top - 0.7f, c.z), Quaternion.LookRotation(along));
                stone.AddComponent<BoxCollider>().size = new Vector3(StoneWidth, 1.4f, StoneLength);

                // Тело камня — из лавы, сверху плоская плита: ноги стоят на коллайдере, а не на неровной модели.
                string model = StoneModels[rng.Next(StoneModels.Length)];
                var rock = Instance(stone.transform, model, "Rock");
                Vector3 n = NativeSize(model);
                bool longZ = n.z >= n.x;
                rock.transform.SetPositionAndRotation(new Vector3(c.x, 0f, c.z),
                    Quaternion.LookRotation(along) * Quaternion.Euler(0f, longZ ? 0f : 90f, 0f));
                float lengthwise = StoneLength + 0.5f, wide = StoneWidth + 0.5f;
                float height = top - 0.12f - (H(c) - 0.5f);
                rock.transform.localScale = longZ
                    ? new Vector3(wide / n.x, height / n.y, lengthwise / n.z)
                    : new Vector3(lengthwise / n.x, height / n.y, wide / n.z);
                rock.transform.position += Vector3.up * (top - 0.12f - Bounds(rock).max.y);
                NoColliders(rock);

                var cap = Instance(stone.transform, PlateModel, "Top plate");
                Vector3 pn = NativeSize(PlateModel);
                cap.transform.SetPositionAndRotation(new Vector3(c.x, 0f, c.z),
                    Quaternion.LookRotation(along) * Quaternion.Euler(0f, 90f + Range(-6f, 6f), 0f));
                cap.transform.localScale = new Vector3((StoneLength + 0.15f) / pn.x, 1.5f, (StoneWidth + 0.15f) / pn.z);
                cap.transform.position += Vector3.up * (top + 0.03f - Bounds(cap).max.y);
                NoColliders(cap);
            }
        }

        // Наклонная опора от пола у берега до уровня камней у края лавы. Ноги — по BoxCollider, вид — каменная плита.
        static void Ramp(Transform group, Vector3 center, Vector3 t, int side, float top, string name, bool descentRock, float half)
        {
            Vector3 low = center + t * side * (half + RampLength);
            low.y = H(low);
            Vector3 high = center + t * side * half;
            high.y = top;
            Vector3 f = (high - low).normalized;
            var rot = Quaternion.LookRotation(f, Vector3.up);
            Vector3 up = rot * Vector3.up;
            float length = (high - low).magnitude;

            var ramp = new GameObject(name);
            ramp.transform.SetParent(group, false);
            ramp.transform.SetPositionAndRotation((low + high) * 0.5f - up * 0.25f, rot);
            ramp.AddComponent<BoxCollider>().size = new Vector3(StoneWidth + 0.2f, 0.5f, length + 0.1f);

            var plate = Instance(ramp.transform, PlateModel, "Plate");
            Vector3 n = NativeSize(PlateModel);
            plate.transform.SetPositionAndRotation((low + high) * 0.5f - up * 0.32f, rot * Quaternion.Euler(0f, 90f, 0f));
            plate.transform.localScale = new Vector3((length + 0.4f) / n.x, 1.5f, (StoneWidth + 0.5f) / n.z);
            NoColliders(plate);

            // Подъём держит небольшой камень под верхним краем; спуск — целый камень, на котором лежит плита.
            string model = descentRock ? DescentRockModel : RampSupportModel;
            Vector3 under = high - f * (descentRock ? length * 0.45f : 0.9f);
            var body = Instance(ramp.transform, model, descentRock ? "Rock" : "Support");
            Vector3 bn = NativeSize(model);
            float h = top - H(under) + (descentRock ? -0.05f : 0.15f);
            float foot = descentRock ? Mathf.Max(StoneWidth + 1.2f, length * 0.8f) : 2.2f;
            body.transform.SetPositionAndRotation(new Vector3(under.x, 0f, under.z), Quaternion.LookRotation(t) * Quaternion.Euler(0f, Range(-10f, 10f), 0f));
            body.transform.localScale = new Vector3(foot / bn.x, h / bn.y, foot / bn.z);
            var bb = Bounds(body);
            body.transform.position += Vector3.up * (H(under) - 0.3f - bb.min.y);
            NoColliders(body);
        }

        // ---------------- Помощники ----------------

        static float OuterEdge(Transform rock, Vector3 d)
        {
            float outer = 0f;
            foreach (var mf in rock.GetComponentsInChildren<MeshFilter>(true))
                foreach (var v in mf.sharedMesh.vertices)
                    outer = Mathf.Max(outer, Vector3.Dot(Flat(mf.transform.TransformPoint(v)), d));
            return outer;
        }

        // Первое расстояние вдоль d (от from наружу), где террейн выше height.
        static float FirstAbove(Vector3 d, float from, float height)
        {
            for (float s = from; s < 160f; s += 0.5f)
                if (H(d * s) > height) return s;
            return 160f;
        }

        static GameObject Instance(Transform parent, string model, string name)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(Prefab(model), parent);
            go.name = name;
            return go;
        }

        static GameObject Prefab(string model)
        {
            foreach (string group in new[] { "Cover", "Rocks" })
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>($"{ArtDir}/{group}/{model}/{model}.prefab");
                if (go != null) return go;
            }
            throw new FileNotFoundException("Нет префаба Megascans: " + model);
        }

        static Vector3 NativeSize(string model)
        {
            var prefab = Prefab(model);
            Bounds b = default;
            bool any = false;
            foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                var mb = mf.sharedMesh.bounds;
                var m = prefab.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                var wb = new Bounds(m.MultiplyPoint3x4(mb.min), Vector3.zero);
                wb.Encapsulate(m.MultiplyPoint3x4(mb.max));
                if (!any) { b = wb; any = true; } else b.Encapsulate(wb);
            }
            return b.size;
        }

        static Bounds Bounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>(true);
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        static void NoColliders(GameObject go)
        {
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
        }

        static Mesh SaveMesh(Mesh mesh, string name)
        {
            string path = MeshDir + "/" + name + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(mesh, path);
                return mesh;
            }
            existing.Clear();
            existing.indexFormat = mesh.indexFormat;
            existing.vertices = mesh.vertices;
            existing.uv = mesh.uv;
            existing.triangles = mesh.triangles;
            existing.normals = mesh.normals;
            existing.RecalculateBounds();
            EditorUtility.SetDirty(existing);
            return existing;
        }

        static float H(Vector3 p)
        {
            foreach (var tile in tiles)
            {
                Vector3 o = tile.transform.position, s = tile.terrainData.size;
                if (p.x >= o.x && p.x < o.x + s.x && p.z >= o.z && p.z < o.z + s.z)
                    return tile.SampleHeight(new Vector3(p.x, 0f, p.z)) + o.y;
            }
            return 0f;
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
        static float Range(float a, float b) => a + (float)rng.NextDouble() * (b - a);
    }
}
