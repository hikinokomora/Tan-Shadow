using System.Collections.Generic;
using System.IO;
using System.Linq;
using TanShadow.Seasons;
using TanShadow.Summer;
using UnityEditor;
using UnityEditor.Rendering.HighDefinition;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TanShadow.EditorTools
{
    // Вид лавы в Summer_Zhuque_02_Lava поверх потоков SummerLavaCrossings (их коллайдеры не трогаем):
    //  • текстуры корки, магмы и каймы рисуются здесь же (бесшовные, процедурные) — Art/SummerLava;
    //  • поверхность — вдвое мельче сетки потока и не ниже бугров дна (бугры не протыкают лаву);
    //  • корка (HDRP Lit, вырезы по альфе) над магмой (HDRP Unlit, эмиссия): в трещинах видна текущая магма;
    //  • кайма остывшей корки по краю потока и вокруг камней в лаве прячет стык с террейном и камнями;
    //  • пар и отсвет вдоль рек (ARCHITECTURE: лава подсвечивает низ камней, над реками пар).
    public static class SummerLavaLook
    {
        static string ScenePath = SummerLavaCrossings.Scene02;
        const string ArtDir = "Assets/_Game/Art/SummerLava";
        static string MeshDir = "Assets/_Game/Data/Summer/Lava/Look";
        const string Prefix = "Look ";

        // Цвета из ARCHITECTURE (палитра лета): лава #E34C25, жар #FFA04A.
        static readonly Color Deep = Hex(0x4A0B03), Ember = Hex(0x8A1A06), Lava = Hex(0xE34C25), Heat = Hex(0xFFA04A), White = Hex(0xFFE2A8);
        static readonly Color CrustDark = new Color(0.045f, 0.038f, 0.035f), CrustWarm = new Color(0.20f, 0.07f, 0.035f);

        const float SurfaceAboveFloor = 0.15f;   // как у потоков SummerLavaCrossings (LavaAboveFloor)
        const float MoltenBelowCrust = 0.06f;
        const float RimInside = 0.9f, RimOutside = 1.5f;     // кайма края: внутрь лавы и наружу на берег, м

        // Свечение (нит; экспозиция дневная — значения большие). Подобраны по снимкам.
        const float CrustNits = 6f, MoltenNits = 22f, RimNits = 8f;

        [MenuItem("Tools/Tan Shadow/Summer/Build Lava Look (02_Lava)")]
        public static void Build() => Build(SummerLavaCrossings.Scene02);

        [MenuItem("Tools/Tan Shadow/Summer/Build Lava Look (01_Dry)")]
        public static void Build01() => Build(SummerLavaCrossings.Scene01);

        public static void Build(string scenePath)
        {
            ScenePath = scenePath;
            MeshDir = scenePath == SummerLavaCrossings.Scene02 ? "Assets/_Game/Data/Summer/Lava/Look" : "Assets/_Game/Data/Summer/Lava/01_Dry/Look";
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var lava = Object.FindFirstObjectByType<ZhuqueLava>(FindObjectsInactive.Include);
            if (lava == null || lava.rivers == null || lava.rivers.Length == 0) throw new System.InvalidOperationException("Нет ZhuqueLava с реками — сначала Build Lava Crossings");

            var mats = BuildMaterials();
            Directory.CreateDirectory(MeshDir);

            var crust = new List<Renderer>();
            var molten = new List<Renderer>();
            var rims = new List<Renderer>();
            var steam = new List<GameObject>();
            bool wasActive = lava.lavaRoot.activeSelf;
            lava.lavaRoot.SetActive(true);
            if (lava.dryBeds != null) lava.dryBeds.SetActive(false);
            Physics.SyncTransforms();

            var steamTemplate = lava.lavaRoot.GetComponentsInChildren<ParticleSystem>(true).FirstOrDefault(p => !p.name.StartsWith(Prefix));
            var lightTemplate = lava.lavaRoot.GetComponentsInChildren<Light>(true).FirstOrDefault(l => !l.name.StartsWith(Prefix));

            for (int r = 0; r < lava.rivers.Length; r++)
            {
                var river = lava.rivers[r];
                var flow = river.transform.parent;
                foreach (Transform child in river.transform.Cast<Transform>().ToArray())
                    if (child.name.StartsWith(Prefix)) Object.DestroyImmediate(child.gameObject);
                foreach (Transform child in flow.Cast<Transform>().ToArray())
                    if (child.name.StartsWith(Prefix)) Object.DestroyImmediate(child.gameObject);

                var source = river.sharedMesh;
                var renderer = river.GetComponent<MeshRenderer>();
                if (renderer != null) renderer.enabled = false;   // коллайдер реки остаётся — по нему игра узнаёт лаву

                var surface = SurfaceMesh(source, river.transform, out var outline);
                var surfaceAsset = Save(surface, "Lava surface " + (r + 1));
                crust.Add(Child(river.transform, Prefix + "crust", surfaceAsset, mats.crust, Vector3.zero));
                molten.Add(Child(river.transform, Prefix + "molten", surfaceAsset, mats.molten, Vector3.down * MoltenBelowCrust));

                var rim = RimMesh(outline, river.transform);
                rims.Add(Child(river.transform, Prefix + "rim", Save(rim, "Lava rim " + (r + 1)), mats.rim, Vector3.zero));

                var collars = CollarMesh(river, lava);
                if (collars.vertexCount > 0)
                    rims.Add(Child(river.transform, Prefix + "collars", Save(collars, "Lava collars " + (r + 1)), mats.rim, Vector3.zero));

                AlongFlow(flow, river, outline, steamTemplate != null ? steamTemplate.gameObject : null, lightTemplate, steam);
            }

            foreach (var old in lava.lavaRoot.GetComponents<MonoBehaviour>())
                if (old != null && old.GetType().Name == "LavaTextureFlow") Object.DestroyImmediate(old);
            var look = lava.lavaRoot.GetComponent<ZhuqueLavaSurface>() ?? lava.lavaRoot.AddComponent<ZhuqueLavaSurface>();
            look.crust = crust.ToArray();
            look.molten = molten.ToArray();
            look.rims = rims.ToArray();
            look.crustEmission = mats.crust.GetColor("_EmissiveColor");
            look.moltenEmission = mats.molten.GetColor("_EmissiveColor");
            look.rimEmission = mats.rim.GetColor("_EmissiveColor");
            EditorUtility.SetDirty(look);

            var world = lava.GetComponent<DeadWorld>();
            if (world != null)
            {
                var alive = (world.aliveOnly ?? new GameObject[0]).Where(g => g != null && !g.name.StartsWith(Prefix)).ToList();
                alive.AddRange(steam);
                world.aliveOnly = alive.ToArray();
                EditorUtility.SetDirty(world);
            }

            lava.lavaRoot.SetActive(wasActive);
            if (lava.dryBeds != null) lava.dryBeds.SetActive(true);
            EditorSceneManager.MarkSceneDirty(scene);
            AssetDatabase.SaveAssets();
            if (!EditorSceneManager.SaveScene(scene)) throw new System.Exception("Не удалось сохранить " + ScenePath);
            Debug.Log($"[SummerLavaLook] Лава: {crust.Count} потоков, кайм {rims.Count}, пар {steam.Count}");
        }

        // ---------------- Поверхность ----------------

        // Каждый четырёхугольник потока делим на 2×2; высота — по самым высоким точкам дна рядом, чтобы бугры
        // между узлами не протыкали лаву. UV — в метрах: u поперёк (как у потока), v вдоль от источника.
        static Mesh SurfaceMesh(Mesh source, Transform space, out List<List<Vector3>> outline)
        {
            var sv = source.vertices;
            var suv = source.uv;
            var tris = source.triangles;
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var idx = new List<int>();
            const int sub = 2;
            for (int t = 0; t + 5 < tris.Length; t += 6)
            {
                // Пара треугольников потока: {a, a+1, c, a+1, c+1, c} — четырёхугольник a, a+1, c+1, c.
                int a = tris[t], b = tris[t + 1], c = tris[t + 2], d = tris[t + 4];
                int start = verts.Count;
                for (int i = 0; i <= sub; i++)
                for (int j = 0; j <= sub; j++)
                {
                    float x = j / (float)sub, y = i / (float)sub;
                    Vector3 p = Bilerp(sv[a], sv[b], sv[c], sv[d], x, y);
                    Vector2 q = Bilerp(suv[a], suv[b], suv[c], suv[d], x, y) * 18f;   // в метры
                    Vector3 w = space.TransformPoint(p);
                    w.y = FloorUnder(w) + SurfaceAboveFloor;
                    verts.Add(space.InverseTransformPoint(w));
                    uvs.Add(q);
                }
                for (int i = 0; i < sub; i++)
                for (int j = 0; j < sub; j++)
                {
                    int v0 = start + i * (sub + 1) + j, v1 = v0 + 1, v2 = v0 + sub + 1, v3 = v2 + 1;
                    idx.AddRange(new[] { v0, v1, v2, v1, v3, v2 });
                }
            }
            var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(idx, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            outline = Outline(sv, tris, space);
            return mesh;
        }

        // a — (0,0), b — (1,0), c — (0,1), d — (1,1)
        static Vector3 Bilerp(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float x, float y) =>
            Vector3.Lerp(Vector3.Lerp(a, b, x), Vector3.Lerp(c, d, x), y);
        static Vector2 Bilerp(Vector2 a, Vector2 b, Vector2 c, Vector2 d, float x, float y) =>
            Vector2.Lerp(Vector2.Lerp(a, b, x), Vector2.Lerp(c, d, x), y);

        // Контуры потока (мировые точки по порядку): рёбра, принадлежащие одному треугольнику.
        static List<List<Vector3>> Outline(Vector3[] v, int[] tris, Transform space)
        {
            var count = new Dictionary<(int, int), int>();
            var directed = new List<(int, int)>();
            for (int t = 0; t < tris.Length; t += 3)
                for (int e = 0; e < 3; e++)
                {
                    int a = tris[t + e], b = tris[t + (e + 1) % 3];
                    var key = a < b ? (a, b) : (b, a);
                    count[key] = count.TryGetValue(key, out int n) ? n + 1 : 1;
                    directed.Add((a, b));
                }
            var next = new Dictionary<int, List<int>>();
            foreach (var (a, b) in directed)
            {
                var key = a < b ? (a, b) : (b, a);
                if (count[key] != 1) continue;
                if (!next.TryGetValue(a, out var list)) next[a] = list = new List<int>();
                list.Add(b);
            }
            var loops = new List<List<Vector3>>();
            while (next.Count > 0)
            {
                int start = next.Keys.First(), at = start;
                var loop = new List<Vector3>();
                for (int guard = 0; guard < 100000; guard++)
                {
                    loop.Add(space.TransformPoint(v[at]));
                    if (!next.TryGetValue(at, out var list)) break;
                    int b = list[0];
                    list.RemoveAt(0);
                    if (list.Count == 0) next.Remove(at);
                    at = b;
                    if (at == start) break;
                }
                if (loop.Count >= 3) loops.Add(loop);
            }
            return loops;
        }

        // ---------------- Кайма ----------------

        // Полоса вдоль контура: от RimInside внутрь лавы до RimOutside на берег. v = 0 — со стороны лавы.
        static Mesh RimMesh(List<List<Vector3>> loops, Transform space)
        {
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var idx = new List<int>();
            foreach (var raw in loops)
            {
                var loop = Resample(raw, 0.5f);
                int n = loop.Count;
                if (n < 3) continue;
                Vector3 centroid = Vector3.zero;
                foreach (var p in loop) centroid += p;
                centroid /= n;
                // Наружу — от лавы: проверяем по самой реке (точка чуть внутрь должна быть над лавой).
                float along = 0f;
                int start = verts.Count;
                var outward = new Vector3[n];
                for (int i = 0; i < n; i++)
                {
                    Vector3 tangent = Flat(loop[(i + 1) % n] - loop[(i - 1 + n) % n]).normalized;
                    outward[i] = new Vector3(tangent.z, 0f, -tangent.x);
                }
                // Сторона — одна для всего контура: туда, где в среднем нет лавы.
                float sign = 0f;
                for (int i = 0; i < n; i += Mathf.Max(1, n / 24))
                    sign += SurfaceAt(loop[i] + outward[i] * 0.6f, out _) ? -1f : 1f;
                if (sign < 0f) for (int i = 0; i < n; i++) outward[i] = -outward[i];
                // Сглаживаем направления, чтобы на зубцах полоса не перекручивалась.
                for (int pass = 0; pass < 4; pass++)
                {
                    var copy = (Vector3[])outward.Clone();
                    for (int i = 0; i < n; i++) outward[i] = (copy[(i - 1 + n) % n] + 2f * copy[i] + copy[(i + 1) % n]).normalized;
                }
                for (int i = 0; i <= n; i++)
                {
                    int k = i % n;
                    if (i > 0) along += Flat(loop[k] - loop[i - 1]).magnitude;
                    // Валик остывшей корки: от лавы поднимается бортиком и сходит на землю (как у настоящих потоков).
                    for (int j = 0; j < RimOffsets.Length; j++)
                    {
                        float offset = RimOffsets[j] < 0f ? RimOffsets[j] * RimInside : RimOffsets[j] * RimOutside;
                        Vector3 p = loop[k] + outward[k] * offset;
                        p.y = Ground(p) + RimLift[j];
                        verts.Add(space.InverseTransformPoint(p));
                        uvs.Add(new Vector2(along, (offset + RimInside) / (RimInside + RimOutside)));
                    }
                    if (i == 0) continue;
                    int cols = RimOffsets.Length;
                    int row = start + i * cols, prev = row - cols;
                    for (int j = 0; j < cols - 1; j++)
                        idx.AddRange(new[] { prev + j, prev + j + 1, row + j, prev + j + 1, row + j + 1, row + j });
                }
            }
            return Finish(verts, uvs, idx);
        }

        // Поперёк каймы: доли RimInside (внутрь лавы, < 0) и RimOutside (на берег); высота над лавой или землёй, м.
        static readonly float[] RimOffsets = { -1f, -0.4f, 0.15f, 0.55f, 1f };
        static readonly float[] RimLift = { 0.035f, 0.13f, 0.24f, 0.1f, 0.025f };
        static readonly float[] CollarOffsets = { -0.15f, 0.1f, 0.35f, 0.7f };
        static readonly float[] CollarLift = { 0.06f, 0.2f, 0.1f, 0.025f };

        // Поверхность под точкой: лава, если она там есть, иначе сама земля (без запаса над буграми).
        static float Ground(Vector3 p) => SurfaceAt(p, out float y) ? y : TerrainHeight(p);

        // Кайма вокруг всего, что стоит в лаве (камни переправ, валуны-источники, глыбы): по кругу лучом
        // внутрь находим край камня на высоте лавы, полоса — от края наружу. Только там, где под ней лава.
        static Mesh CollarMesh(MeshCollider river, ZhuqueLava lava)
        {
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var idx = new List<int>();
            var bounds = river.bounds;
            bounds.Expand(new Vector3(2f, 6f, 2f));
            var seen = new HashSet<Collider>();
            foreach (var c in Physics.OverlapBox(bounds.center, bounds.extents, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
            {
                if (c is TerrainCollider || c is CharacterController || !seen.Add(c)) continue;
                if (lava.rivers.Contains(c as MeshCollider)) continue;
                if (lava.dryBeds != null && c.transform.IsChildOf(lava.dryBeds.transform)) continue;
                if (c.GetComponentInParent<ZhuqueBird>() != null) continue;
                Vector3 center = c.bounds.center;
                if (!SurfaceAt(center, out float lavaY) && !NearLava(c, river)) continue;
                if (!SurfaceAt(center, out lavaY)) lavaY = FloorUnder(center) + SurfaceAboveFloor;
                if (c.bounds.min.y > lavaY + 0.3f || c.bounds.max.y < lavaY) continue;   // камень не касается лавы
                float reach = c.bounds.extents.magnitude + 3f;
                // Край ищем по видимому мешу (у камней переправ коллайдер — коробка, а камень круглый).
                var shapes = VisualColliders(c);
                const int steps = 64;
                var edge = new Vector3[steps + 1];
                var ok = new bool[steps + 1];
                for (int i = 0; i <= steps; i++)
                {
                    float a = i % steps * Mathf.PI * 2f / steps;
                    Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    var ray = new Ray(new Vector3(center.x, lavaY + 0.1f, center.z) + dir * reach, -dir);
                    ok[i] = false;
                    float best = float.MaxValue;
                    edge[i] = center;
                    foreach (var shape in shapes)
                        if (shape.Raycast(ray, out var hit, reach) && hit.distance < best) { best = hit.distance; edge[i] = hit.point; ok[i] = true; }
                }
                float along = 0f;
                for (int i = 0; i <= steps; i++)
                {
                    if (i > 0) along += Flat(edge[i] - edge[i - 1]).magnitude;
                    Vector3 dir = Flat(edge[i] - center).normalized;
                    int row = verts.Count;
                    bool overLava = false;
                    for (int j = 0; j < CollarOffsets.Length; j++)
                    {
                        Vector3 p = edge[i] + dir * CollarOffsets[j];
                        overLava |= SurfaceAt(p, out _);
                        p.y = Ground(p) + CollarLift[j];
                        verts.Add(river.transform.InverseTransformPoint(p));
                        uvs.Add(new Vector2(along, Mathf.Lerp(0.12f, 0.86f, (CollarOffsets[j] - CollarOffsets[0]) / (CollarOffsets[CollarOffsets.Length - 1] - CollarOffsets[0]))));
                    }
                    if (i == 0 || !ok[i] || !ok[i - 1] || !overLava) continue;
                    int cols = CollarOffsets.Length;
                    int prev = row - cols;
                    for (int j = 0; j < cols - 1; j++)
                        idx.AddRange(new[] { prev + j, prev + j + 1, row + j, prev + j + 1, row + j + 1, row + j });
                }
                foreach (var shape in shapes)
                    if (shape != c) Object.DestroyImmediate(shape.gameObject);
            }
            return Finish(verts, uvs, idx);
        }

        // Временные коллайдеры по видимым мешам камня (удаляются после разметки каймы); нет мешей — сам коллайдер.
        static List<Collider> VisualColliders(Collider c)
        {
            var list = new List<Collider>();
            foreach (var mf in c.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null) continue;
                var go = new GameObject("_collar probe") { hideFlags = HideFlags.HideAndDontSave };
                go.transform.SetPositionAndRotation(mf.transform.position, mf.transform.rotation);
                go.transform.localScale = mf.transform.lossyScale;
                go.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
                list.Add(go.GetComponent<MeshCollider>());
            }
            if (list.Count == 0) list.Add(c);
            Physics.SyncTransforms();
            return list;
        }

        static bool NearLava(Collider c, MeshCollider river)
        {
            var b = c.bounds;
            foreach (var p in new[] { b.center, b.center + new Vector3(b.extents.x, 0, 0), b.center - new Vector3(b.extents.x, 0, 0),
                                      b.center + new Vector3(0, 0, b.extents.z), b.center - new Vector3(0, 0, b.extents.z) })
                if (SurfaceAt(p + Flat(p - b.center).normalized * 0.8f, out _)) return true;
            return false;
        }

        // ---------------- Пар и отсвет вдоль потока ----------------

        static void AlongFlow(Transform flow, MeshCollider river, List<List<Vector3>> outline, GameObject steamTemplate, Light lightTemplate, List<GameObject> steam)
        {
            // Ось потока ≈ середины рядов исходного меша (19 точек поперёк), рядов — по метру.
            var v = river.sharedMesh.vertices;
            const int across = 19;
            int rows = v.Length / across;
            for (int i = 4; i < rows - 2; i += 9)
            {
                Vector3 c = river.transform.TransformPoint(v[i * across + across / 2]);
                if (!SurfaceAt(c, out float y)) continue;
                if (steamTemplate != null)
                {
                    var fx = Object.Instantiate(steamTemplate.gameObject, flow);
                    fx.name = Prefix + "steam";
                    fx.transform.position = new Vector3(c.x, y, c.z) + Random.insideUnitSphere.Flat() * 2f;
                    var ps = fx.GetComponent<ParticleSystem>();
                    var emission = ps.emission;
                    emission.rateOverTimeMultiplier *= 0.5f;
                    steam.Add(fx);
                }
                if (lightTemplate != null && i % 18 == 4)
                {
                    var light = Object.Instantiate(lightTemplate.gameObject, flow).GetComponent<Light>();
                    light.name = Prefix + "heat";
                    light.color = Heat;
                    light.transform.position = new Vector3(c.x, y + 1.2f, c.z);
                }
            }
        }

        static Vector3 Flat(this Vector3 v) => new Vector3(v.x, 0f, v.z);

        // ---------------- Земля и лава под точкой ----------------

        static float TerrainHeight(Vector3 p)
        {
            foreach (var t in Terrain.activeTerrains)
            {
                Vector3 o = t.transform.position, s = t.terrainData.size;
                if (p.x >= o.x && p.x <= o.x + s.x && p.z >= o.z && p.z <= o.z + s.z)
                    return t.SampleHeight(p) + o.y;
            }
            return float.NegativeInfinity;
        }

        // Самое высокое дно рядом с точкой (r = 0.35 м).
        static float FloorUnder(Vector3 p)
        {
            float h = TerrainHeight(p);
            for (int i = 0; i < 6; i++)
            {
                float a = i * Mathf.PI / 3f;
                h = Mathf.Max(h, TerrainHeight(p + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.35f));
            }
            return h;
        }

        static readonly RaycastHit[] hits = new RaycastHit[16];

        // Есть ли под точкой поверхность лавы (наш меш «Look crust» ещё не имеет коллайдера — берём реку).
        static bool SurfaceAt(Vector3 p, out float y)
        {
            y = 0f;
            int n = Physics.RaycastNonAlloc(new Vector3(p.x, p.y + 40f, p.z), Vector3.down, hits, 120f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.NegativeInfinity;
            for (int i = 0; i < n; i++)
                if (hits[i].collider.name.StartsWith("Molten river") && hits[i].point.y > best) best = hits[i].point.y;
            if (float.IsNegativeInfinity(best)) return false;
            y = Mathf.Max(best, FloorUnder(p) + SurfaceAboveFloor);
            return true;
        }


        // Точки контура через равные промежутки (контур потока идёт по метровой сетке — с зубцами).
        static List<Vector3> Resample(List<Vector3> loop, float step)
        {
            var result = new List<Vector3>();
            int n = loop.Count;
            float carry = 0f;
            for (int i = 0; i < n; i++)
            {
                Vector3 a = loop[i], b = loop[(i + 1) % n];
                float len = Flat(b - a).magnitude;
                for (float d = carry; d < len; d += step) result.Add(Vector3.Lerp(a, b, d / len));
                carry = Mathf.Repeat(carry - len, step);
            }
            // Сглаживаем зубцы сетки.
            for (int pass = 0; pass < 3; pass++)
            {
                var copy = result.ToArray();
                for (int i = 0; i < copy.Length; i++)
                    result[i] = (copy[(i - 1 + copy.Length) % copy.Length] + 2f * copy[i] + copy[(i + 1) % copy.Length]) * 0.25f;
            }
            return result;
        }

        static Mesh Finish(List<Vector3> verts, List<Vector2> uvs, List<int> idx)
        {
            var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(idx, 0);
            // Лицом вверх: если треугольники смотрят вниз — разворачиваем.
            mesh.RecalculateNormals();
            var normals = mesh.normals;
            var t = mesh.triangles;
            for (int i = 0; i < t.Length; i += 3)
            {
                Vector3 n = Vector3.Cross(verts[t[i + 1]] - verts[t[i]], verts[t[i + 2]] - verts[t[i]]);
                if (n.y < 0f) (t[i + 1], t[i + 2]) = (t[i + 2], t[i + 1]);
            }
            mesh.triangles = t;
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        static Mesh Save(Mesh mesh, string name)
        {
            mesh.name = name;
            string path = MeshDir + "/" + name + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null)
            {
                EditorUtility.CopySerialized(mesh, existing);
                return existing;
            }
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        static Renderer Child(Transform parent, string name, Mesh mesh, Material material, Vector3 offset)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = parent.InverseTransformVector(offset);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return mr;
        }

        // ---------------- Текстуры и материалы ----------------

        struct Mats { public Material crust, molten, rim; }

        static Mats BuildMaterials()
        {
            Directory.CreateDirectory(ArtDir);
            LavaTextures.Write(ArtDir);
            AssetDatabase.Refresh();
            Import(ArtDir + "/T_LavaCrust_Albedo.png", false, true);
            Import(ArtDir + "/T_LavaCrust_Normal.png", true, false);
            Import(ArtDir + "/T_LavaCrust_Emission.png", false, true);
            Import(ArtDir + "/T_LavaMolten.png", false, true);
            Import(ArtDir + "/T_LavaRim_Albedo.png", false, true);
            Import(ArtDir + "/T_LavaRim_Emission.png", false, true);

            var lit = Shader.Find("HDRP/Lit");
            var unlit = Shader.Find("HDRP/Unlit");
            var crust = Mat("M_LavaCrust", lit);
            crust.SetTexture("_BaseColorMap", Tex("T_LavaCrust_Albedo"));
            crust.SetColor("_BaseColor", Color.white);
            crust.SetTexture("_NormalMap", Tex("T_LavaCrust_Normal"));
            crust.SetFloat("_NormalScale", 1.4f);
            crust.SetFloat("_Metallic", 0f);
            crust.SetFloat("_Smoothness", 0.28f);
            crust.SetFloat("_AlphaCutoffEnable", 1f);
            crust.SetFloat("_AlphaCutoff", 0.5f);
            Emission(crust, "T_LavaCrust_Emission", CrustNits);

            var molten = Mat("M_LavaMolten", unlit);
            molten.SetTexture("_UnlitColorMap", Tex("T_LavaMolten"));
            molten.SetColor("_UnlitColor", new Color(0.02f, 0.01f, 0.005f));
            Emission(molten, "T_LavaMolten", MoltenNits);

            var rim = Mat("M_LavaRim", lit);
            rim.SetTexture("_BaseColorMap", Tex("T_LavaRim_Albedo"));
            rim.SetColor("_BaseColor", Color.white);
            rim.SetTexture("_NormalMap", Tex("T_LavaCrust_Normal"));
            rim.SetFloat("_NormalScale", 0.8f);
            rim.SetFloat("_Metallic", 0f);
            rim.SetFloat("_Smoothness", 0.18f);
            rim.SetFloat("_AlphaCutoffEnable", 1f);
            rim.SetFloat("_AlphaCutoff", 0.5f);
            Emission(rim, "T_LavaRim_Emission", RimNits);

            foreach (var m in new[] { crust, molten, rim })
            {
                HDShaderUtils.ResetMaterialKeywords(m);
                EditorUtility.SetDirty(m);
            }
            AssetDatabase.SaveAssets();
            return new Mats { crust = crust, molten = molten, rim = rim };
        }

        static void Emission(Material m, string texture, float nits)
        {
            m.SetTexture("_EmissiveColorMap", Tex(texture));
            m.SetFloat("_UseEmissiveIntensity", 1f);
            m.SetFloat("_EmissiveIntensityUnit", 0f);
            m.SetFloat("_EmissiveIntensity", nits);
            m.SetColor("_EmissiveColorLDR", Color.white);
            m.SetColor("_EmissiveColor", Color.white * nits);
            m.SetFloat("_EmissiveExposureWeight", 1f);
        }

        static Material Mat(string name, Shader shader)
        {
            string path = ArtDir + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(m, path);
            }
            else m.shader = shader;
            return m;
        }

        static Texture2D Tex(string name) => AssetDatabase.LoadAssetAtPath<Texture2D>(ArtDir + "/" + name + ".png");

        static void Import(string path, bool normal, bool srgb)
        {
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            imp.sRGBTexture = srgb;
            imp.alphaIsTransparency = false;
            imp.wrapMode = TextureWrapMode.Repeat;
            imp.anisoLevel = 8;
            imp.mipmapEnabled = true;
            imp.SaveAndReimport();
        }

        static Color Hex(int rgb) => new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f);

        // ---------------- Процедурные бесшовные текстуры ----------------

        static class LavaTextures
        {
            public static void Write(string dir)
            {
                Crust(dir, 1024);
                Molten(dir, 512);
                Rim(dir, 1024, 256);
            }

            // Корка: плиты (ячейки Вороного) с трещинами; в трещинах альфа 0 — сквозь них видна магма.
            static void Crust(string dir, int size)
            {
                var albedo = new Color[size * size];
                var emission = new Color[size * size];
                var height = new float[size * size];
                const int cells = 5;
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = x / (float)size, v = y / (float)size;
                    // Плиты разного размера и формы: сильный изгиб координат, крупные и мелкие ячейки.
                    float wu = u + 0.1f * (Fbm(u * 3f, v * 3f, 3, 4, 11) - 0.5f);
                    float wv = v + 0.1f * (Fbm(u * 3f, v * 3f, 3, 4, 23) - 0.5f);
                    Voronoi(wu, wv, cells, out float f1, out float f2, out int id);
                    Voronoi(wu + 0.013f, wv - 0.021f, 17, out float g1, out float g2, out _);
                    float edge = f2 - f1;
                    float fine = 1f - Smooth(0.015f, 0.04f, g2 - g1);          // мелкие трещинки по плитам
                    float grain = Fbm(u * 16f, v * 16f, 16, 4, 5);
                    // Трещины разной ширины: местами — широкие протоки магмы.
                    float crackWidth = 0.04f + 0.2f * Mathf.Pow(Fbm(u * 3f, v * 3f, 3, 3, 31), 2.2f);
                    bool hotCell = Hash(id, 77) < 0.25f;
                    float pool = hotCell ? Smooth(0.38f, 0.58f, Fbm(u * 9f, v * 9f, 9, 3, id)) : 0f;
                    float solid = Smooth(crackWidth * 0.3f, crackWidth * 0.75f, edge) * (1f - pool);
                    float rise = Smooth(0f, 0.3f, edge);
                    height[y * size + x] = (rise * (0.7f + 0.3f * grain) - fine * 0.15f) * solid;
                    float warm = 1f - Smooth(crackWidth, crackWidth * 2.5f, edge);
                    Color c = Color.Lerp(CrustDark * (0.65f + 0.7f * grain) * (1f - 0.35f * fine), CrustWarm, warm * 0.55f);
                    albedo[y * size + x] = new Color(c.r, c.g, c.b, solid);
                    float glow = Mathf.Pow(1f - Smooth(crackWidth * 0.4f, crackWidth * 2.4f, edge), 1.6f);
                    glow = Mathf.Max(glow, fine * 0.32f * grain);
                    glow = Mathf.Max(glow, hotCell ? 0.3f * grain : 0f);
                    emission[y * size + x] = Ramp(glow);
                }
                Png(dir + "/T_LavaCrust_Albedo.png", size, size, albedo);
                Png(dir + "/T_LavaCrust_Emission.png", size, size, emission);
                Png(dir + "/T_LavaCrust_Normal.png", size, size, NormalFrom(height, size, size, 6f));
            }

            // Магма: медленные завихрения, от тёмно-красного до жёлто-белого в самых горячих жилах.
            static void Molten(string dir, int size)
            {
                var pixels = new Color[size * size];
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = x / (float)size, v = y / (float)size;
                    float wu = u + 0.12f * (Fbm(u * 3f, v * 3f, 3, 4, 41) - 0.5f);
                    float wv = v + 0.12f * (Fbm(u * 3f, v * 3f, 3, 4, 43) - 0.5f);
                    float m = Fbm(wu * 5f, wv * 5f, 5, 5, 47);
                    float veins = 1f - Mathf.Abs(Fbm(wu * 7f, wv * 7f, 7, 4, 53) - 0.5f) * 2f;
                    pixels[y * size + x] = Ramp(Mathf.Clamp01(0.25f + 0.55f * m + 0.35f * Mathf.Pow(veins, 6f)));
                }
                Png(dir + "/T_LavaMolten.png", size, size, pixels);
            }

            // Кайма: u — вдоль края (бесшовно), v — поперёк (0 — со стороны лавы). Свечение у лавы гаснет к
            // берегу, внешний край рваный (альфа).
            static void Rim(string dir, int w, int h)
            {
                var albedo = new Color[w * h];
                var emission = new Color[w * h];
                for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float u = x / (float)w, v = y / (float)h;
                    float n = Fbm(u * 24f, v * 3f, 24, 4, 61);
                    float grain = Fbm(u * 64f, v * 8f, 64, 3, 67);
                    Voronoi(u * 4f, v, 4, out float f1, out float f2, out _, 1);
                    float crack = 1f - Smooth(0.02f, 0.06f, f2 - f1);
                    float outer = 0.72f + 0.22f * (n - 0.5f) * 2f;
                    float alpha = v < outer ? 1f : 0f;
                    float inner = 0.08f + 0.12f * Fbm(u * 18f, 0.5f, 18, 3, 71);
                    if (v < inner) alpha = 0f;   // со стороны лавы край тоже рваный — лава подходит под корку
                    // Светится только полоса касания с лавой и трещины рядом с ней; сама корка тёмная.
                    float glow = Mathf.Pow(1f - Smooth(inner, inner + 0.18f + 0.1f * n, v), 2f);
                    glow = Mathf.Max(glow, crack * 0.5f * (1f - Smooth(0.15f, 0.45f, v)));
                    Color c = CrustDark * (0.55f + 0.8f * grain) * (1f - 0.3f * crack);
                    albedo[y * w + x] = new Color(c.r, c.g, c.b, alpha);
                    emission[y * w + x] = Ramp(glow * 0.9f);
                }
                Png(dir + "/T_LavaRim_Albedo.png", w, h, albedo);
                Png(dir + "/T_LavaRim_Emission.png", w, h, emission);
            }

            // 0 — нет свечения, 1 — самое горячее.
            static Color Ramp(float t)
            {
                t = Mathf.Clamp01(t);
                if (t < 0.25f) return Color.Lerp(Color.black, Deep, t / 0.25f);
                if (t < 0.45f) return Color.Lerp(Deep, Ember, (t - 0.25f) / 0.2f);
                if (t < 0.7f) return Color.Lerp(Ember, Lava, (t - 0.45f) / 0.25f);
                if (t < 0.9f) return Color.Lerp(Lava, Heat, (t - 0.7f) / 0.2f);
                return Color.Lerp(Heat, White, (t - 0.9f) / 0.1f);
            }

            static Color[] NormalFrom(float[] h, int w, int hgt, float strength)
            {
                var result = new Color[w * hgt];
                for (int y = 0; y < hgt; y++)
                for (int x = 0; x < w; x++)
                {
                    float l = h[y * w + (x - 1 + w) % w], r = h[y * w + (x + 1) % w];
                    float d = h[((y - 1 + hgt) % hgt) * w + x], u = h[((y + 1) % hgt) * w + x];
                    var n = new Vector3((l - r) * strength, (d - u) * strength, 1f).normalized;
                    result[y * w + x] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
                }
                return result;
            }

            static void Png(string path, int w, int h, Color[] pixels)
            {
                var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, false);
                tex.SetPixels(pixels);
                tex.Apply();
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
            }

            static float Hash(int x, int y, int seed = 0)
            {
                unchecked
                {
                    uint h = (uint)(x * 374761393 + y * 668265263 + seed * 982451653);
                    h = (h ^ (h >> 13)) * 1274126177u;
                    h ^= h >> 16;
                    return (h & 0xFFFFFF) / 16777216f;
                }
            }

            static float Hash(int id, int seed) => Hash(id, id * 7 + 3, seed);

            // Бесшовный шум значений: period — сколько клеток на повтор текстуры.
            static float Value(float x, float y, int period, int seed)
            {
                int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
                float fx = x - xi, fy = y - yi;
                fx = fx * fx * (3f - 2f * fx);
                fy = fy * fy * (3f - 2f * fy);
                int x0 = Mod(xi, period), x1 = Mod(xi + 1, period), y0 = Mod(yi, period), y1 = Mod(yi + 1, period);
                return Mathf.Lerp(Mathf.Lerp(Hash(x0, y0, seed), Hash(x1, y0, seed), fx),
                                  Mathf.Lerp(Hash(x0, y1, seed), Hash(x1, y1, seed), fx), fy);
            }

            static float Fbm(float x, float y, int period, int octaves, int seed)
            {
                float sum = 0f, amp = 0.5f, norm = 0f;
                for (int o = 0; o < octaves; o++)
                {
                    sum += Value(x, y, period, seed + o * 17) * amp;
                    norm += amp;
                    amp *= 0.5f;
                    x *= 2f; y *= 2f; period *= 2;
                }
                return sum / norm;
            }

            // Бесшовные ячейки Вороного: cells по u, cellsV по v (по умолчанию столько же).
            static void Voronoi(float u, float v, int cells, out float f1, out float f2, out int id, int cellsV = -1)
            {
                if (cellsV < 0) cellsV = cells;
                float x = u * cells, y = v * cellsV;
                int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
                f1 = f2 = 99f;
                id = 0;
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int cx = xi + dx, cy = yi + dy;
                    int wx = Mod(cx, cells), wy = Mod(cy, cellsV);
                    float px = cx + Hash(wx, wy, 101), py = cy + Hash(wx, wy, 202);
                    float d = Mathf.Sqrt((px - x) * (px - x) + (py - y) * (py - y));
                    if (d < f1) { f2 = f1; f1 = d; id = wy * cells + wx; }
                    else if (d < f2) f2 = d;
                }
            }

            static int Mod(int a, int m) => ((a % m) + m) % m;

            // Порог как smoothstep в шейдерах: 0 до edge0, 1 после edge1 (Mathf.SmoothStep — другое: интерполяция).
            static float Smooth(float edge0, float edge1, float x)
            {
                float t = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
                return t * t * (3f - 2f * t);
            }
        }
    }
}
