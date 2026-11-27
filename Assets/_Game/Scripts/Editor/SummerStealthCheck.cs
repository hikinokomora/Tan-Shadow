using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using TanShadow.Summer;
using UnityEditor;
using UnityEngine;

namespace TanShadow.EditorTools
{
    // Проверка скрытности лета: можно ли к каждому насесту подбежать незамеченной.
    // Запускать в Play Mode (насесты и их поворот испытание считает на старте). Для каждого насеста и каждой
    // стороны кольца ищется путь бегом от точки в 60 м по кольцу до кончика хвоста, на котором шкала
    // обнаружения ни разу не дойдёт до порога (в укрытии героиня может переждать, шкала спадает до нуля).
    // Шкала — как в ZhuqueEncounter: timeToDetect, обзор viewDistance, вблизи detectFullRange полная скорость.
    // Где пути нет, предлагается место для укрытия; «Place Proposed Covers» (вне Play Mode) их ставит.
    public static class SummerStealthCheck
    {
        const float RunSpeed = 4.5f;          // бег героини (CombatSettings.moveSpeed)
        const float Threshold = 0.8f;         // запас: на пути шкала не выше этого
        const float StartArc = 60f;           // откуда начинается подход, м по кольцу
        const string ProposalKey = "TanShadow.SummerStealthCheck.Proposals";
        const string CoverGroup = "Summer - Zhuque/03 - Basalt cover and perches/Cover";
        const string ArtDir = "Assets/_Game/Art/SummerMegascans/";
        static readonly string[] CoverModels =
        {
            "Rocks/Volcanic_Dolerite_Rocks_qhEgj_Low", "Cover/Nordic_Beach_Rock_Formation_uktjfiu_Low",
            "Rocks/Volcanic_Dolerite_Rocks_qfjwU_Low", "Rocks/Icelandic_Sandy_Volcanic_Rock_slnpT_Low",
        };

        [MenuItem("Tools/Tan Shadow/Summer/Check Stealth Routes (Play Mode)")]
        public static void CheckFromMenu() => Debug.Log(Check());

        [MenuItem("Tools/Tan Shadow/Summer/Place Proposed Covers")]
        public static void PlaceFromMenu() => Debug.Log(PlaceProposals());

        public static string Check()
        {
            if (!Application.isPlaying) return "[SummerStealthCheck] Запустите Play Mode.";
            var enc = Object.FindFirstObjectByType<ZhuqueEncounter>();
            var bf = BindingFlags.NonPublic | BindingFlags.Instance;
            var perches = (IList)typeof(ZhuqueEncounter).GetField("perches", bf).GetValue(enc);
            var poseAt = typeof(ZhuqueEncounter).GetMethod("PoseAt", bf);
            var tuning = enc.tuning;

            // Испытание и героиню убираем, чтобы не мешали лучам и не сработала лава.
            enc.enabled = false;
            var body = enc.hero.Body;
            var player = body.GetComponent<TanShadow.Player.PlayerController>();
            if (player != null) player.enabled = false;
            var cc = body.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            body.position = new Vector3(0f, 300f, 0f);
            Physics.SyncTransforms();

            var tiles = Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None);
            const int N = 261;
            const float O = -130f;
            var walk = new bool[N * N];
            var gy = new float[N * N];
            for (int i = 0; i < N; i++)
            for (int j = 0; j < N; j++)
            {
                float x = O + i, z = O + j, r = Mathf.Sqrt(x * x + z * z);
                if (r < 60f || r > 128f) continue;
                float h = Height(tiles, x, z);
                if (Mathf.Abs(h) > 0.6f) continue;
                var p = new Vector3(x, h, z);
                if (Physics.CheckCapsule(p + Vector3.up * 0.7f, p + Vector3.up * 1.6f, 0.35f, ~0, QueryTriggerInteraction.Ignore)) continue;
                walk[i * N + j] = true;
                gy[i * N + j] = h;
            }

            var report = new StringBuilder("[SummerStealthCheck] ");
            var proposals = new StringBuilder();
            int[] di = { 1, -1, 0, 0, 1, 1, -1, -1 }, dj = { 0, 0, 1, -1, 1, -1, 1, -1 };
            int fails = 0;
            var chooseSide = typeof(ZhuqueEncounter).GetMethod("ChooseSide", bf);
            var seatOf = typeof(ZhuqueEncounter).GetMethod("SeatOf", bf);
            var facingOf = typeof(ZhuqueEncounter).GetMethod("FacingOf", bf);
            for (int pi = 0; pi < perches.Count; pi++)
            {
                var pr = perches[pi];
                var pt = pr.GetType();
                var top = (Vector3)pt.GetField("top").GetValue(pr);
                var rock = (Transform)pt.GetField("rock").GetValue(pr);
                report.Append($"\n  насест {pi + 1} ({rock.name}): ");
                float angle = Mathf.Atan2(top.z, top.x), radius = new Vector2(top.x, top.z).magnitude;
                foreach (int side in new[] { -1, 1 })
                {
                    // Птица садится спиной к героине: разворот — по точке, откуда героиня подходит.
                    float a = angle + side * StartArc / Mathf.Max(60f, radius);
                    var from = new Vector3(Mathf.Cos(a) * 95f, 0f, Mathf.Sin(a) * 95f);
                    int sideIndex = (int)chooseSide.Invoke(enc, new object[] { pi, from });
                    var facing = (Vector3)facingOf.Invoke(enc, new object[] { pi, sideIndex });
                    var seat = (Vector3)seatOf.Invoke(enc, new object[] { pi, sideIndex });
                    poseAt.Invoke(enc, new object[] { seat, facing });
                    Vector3 eye = enc.bird.EyePoint, tip = enc.bird.TailTip;

                    // Как быстро растёт шкала в каждой клетке (0 — героиню не видно).
                    var w = new float[N * N];
                    for (int k = 0; k < w.Length; k++)
                    {
                        if (!walk[k]) continue;
                        var aim = new Vector3(O + k / N, gy[k] + 1.1f, O + k % N);
                        var to = aim - eye;
                        float dist = to.magnitude;
                        if (dist > tuning.viewDistance) continue;
                        if (Physics.Raycast(eye, to / dist, dist - 0.05f, ~0, QueryTriggerInteraction.Ignore)) continue;
                        w[k] = Mathf.Lerp(1f, tuning.farDetectionFactor, Mathf.InverseLerp(tuning.detectFullRange, tuning.viewDistance, dist));
                    }
                    var goal = new bool[N * N];
                    int goals = 0;
                    for (int k = 0; k < goal.Length; k++)
                    {
                        if (!walk[k]) continue;
                        float above = tip.y - gy[k];
                        if (new Vector2(O + k / N - tip.x, O + k % N - tip.z).magnitude <= tuning.grabRadius - 0.3f &&
                            above <= tuning.grabMaxHeight && above >= -1f) { goal[k] = true; goals++; }
                    }
                    string label = side < 0 ? "по часовой " : "против ";
                    if (goals == 0) { report.Append(label + "— к кончику не подойти; "); fails++; continue; }
                    int start = StartCell(walk, w, N, O, a);
                    if (start < 0) { report.Append(label + "— нет старта; "); continue; }
                    bool ok = Reachable(walk, w, goal, N, start, tuning.timeToDetect, di, dj);
                    report.Append(label + (ok ? "OK; " : "НЕТ; "));
                    if (ok) continue;
                    fails++;
                    var proposal = Propose(walk, w, goal, N, O, start, eye, tuning.timeToDetect, di, dj);
                    if (proposal != null) proposals.AppendLine((pi + 1) + ";" + proposal);
                }
            }
            SessionState.SetString(ProposalKey, proposals.ToString());
            report.Insert(0, fails == 0 ? "все подходы скрытные." : $"проблем: {fails}, предложено укрытий: {proposals.ToString().Split('\n').Length - 1}.");
            return report.ToString();
        }

        static int StartCell(bool[] walk, float[] w, int n, float o, float angle)
        {
            int start = -1;
            float best = float.MaxValue;
            for (float r = 80f; r <= 110f; r += 2f)
            {
                int i = Mathf.RoundToInt(Mathf.Cos(angle) * r - o), j = Mathf.RoundToInt(Mathf.Sin(angle) * r - o);
                if (i < 0 || j < 0 || i >= n || j >= n) continue;
                int k = i * n + j;
                if (walk[k] && w[k] == 0f && Mathf.Abs(r - 95f) < best) { best = Mathf.Abs(r - 95f); start = k; }
            }
            return start;
        }

        // Есть ли путь, на котором шкала не превышает Threshold. В укрытии (w = 0) героиня пережидает — шкала в ноль.
        static bool Reachable(bool[] walk, float[] w, bool[] goal, int n, int start, float timeToDetect, int[] di, int[] dj)
        {
            var best = new float[walk.Length];
            for (int k = 0; k < best.Length; k++) best[k] = float.MaxValue;
            var queue = new Queue<int>();
            best[start] = 0f;
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                int k = queue.Dequeue();
                if (goal[k]) return true;
                int i = k / n, j = k % n;
                for (int d = 0; d < 8; d++)
                {
                    int ni = i + di[d], nj = j + dj[d];
                    if (ni < 0 || nj < 0 || ni >= n || nj >= n) continue;
                    int nk = ni * n + nj;
                    if (!walk[nk]) continue;
                    float len = d < 4 ? 1f : 1.414f;
                    float next = w[nk] == 0f ? 0f : best[k] + w[nk] * len / RunSpeed / timeToDetect;
                    if (next > Threshold || next >= best[nk] - 0.001f) continue;
                    best[nk] = next;
                    queue.Enqueue(nk);
                }
            }
            return false;
        }

        // Путь с наименьшей открытостью; укрытие — там, где на нём шкала дошла бы до половины, на 3 м ближе к птице.
        static string Propose(bool[] walk, float[] w, bool[] goal, int n, float o, int start, Vector3 eye, float timeToDetect, int[] di, int[] dj)
        {
            var cost = new float[walk.Length];
            var prev = new int[walk.Length];
            for (int k = 0; k < cost.Length; k++) { cost[k] = float.MaxValue; prev[k] = -1; }
            var open = new SortedSet<long>();
            cost[start] = 0f;
            open.Add(start);
            int reached = -1;
            while (open.Count > 0)
            {
                long top = open.Min;
                open.Remove(top);
                int k = (int)(top & 0xFFFFF);
                if (goal[k]) { reached = k; break; }
                int i = k / n, j = k % n;
                for (int d = 0; d < 8; d++)
                {
                    int ni = i + di[d], nj = j + dj[d];
                    if (ni < 0 || nj < 0 || ni >= n || nj >= n) continue;
                    int nk = ni * n + nj;
                    if (!walk[nk]) continue;
                    float len = d < 4 ? 1f : 1.414f;
                    float c = cost[k] + len * (1f + 25f * w[nk]);
                    if (c >= cost[nk]) continue;
                    if (cost[nk] < float.MaxValue) open.Remove(((long)(cost[nk] * 100f) << 20) | (long)nk);
                    cost[nk] = c;
                    prev[nk] = k;
                    open.Add(((long)(c * 100f) << 20) | (long)nk);
                }
            }
            if (reached < 0) return null;
            var path = new List<int>();
            for (int k = reached; k >= 0; k = prev[k]) path.Add(k);
            path.Reverse();
            float detect = 0f;
            for (int m = 1; m < path.Count; m++)
            {
                int k = path[m];
                if (w[k] == 0f) { detect = 0f; continue; }
                detect += w[k] * 1.2f / RunSpeed / timeToDetect;
                if (detect < 0.45f) continue;
                float x = o + k / n, z = o + k % n;
                Vector3 toEye = new Vector3(eye.x - x, 0f, eye.z - z).normalized;
                Vector3 at = new Vector3(x, 0f, z) + toEye * 3f;
                var ci = CultureInfo.InvariantCulture;
                return $"{at.x.ToString("F1", ci)};{at.z.ToString("F1", ci)};{toEye.x.ToString("F2", ci)};{toEye.z.ToString("F2", ci)}";
            }
            return null;
        }

        // Ставит предложенные укрытия (~8 м поперёк взгляда птицы, 4.5 м высотой) в группу Cover открытой сцены.
        public static string PlaceProposals()
        {
            if (Application.isPlaying) return "[SummerStealthCheck] Выйдите из Play Mode.";
            string data = SessionState.GetString(ProposalKey, "");
            var group = GameObject.Find(CoverGroup)?.transform;
            if (group == null || string.IsNullOrWhiteSpace(data)) return "[SummerStealthCheck] Нечего ставить.";
            var tiles = Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None);
            var lava = new List<Vector2>();
            var dry = GameObject.Find("Summer - Zhuque/Dry beds (future lava)");
            if (dry != null)
                foreach (var mc in dry.GetComponentsInChildren<MeshCollider>(true))
                    foreach (var v in mc.sharedMesh.vertices) { var p = mc.transform.TransformPoint(v); lava.Add(new Vector2(p.x, p.z)); }
            var ci = CultureInfo.InvariantCulture;
            var log = new StringBuilder("[SummerStealthCheck] Поставлено:");
            int added = 0;
            foreach (var line in data.Split('\n'))
            {
                var f = line.Trim().Split(';');
                if (f.Length < 5) continue;
                var at = new Vector3(float.Parse(f[1], ci), 0f, float.Parse(f[2], ci));
                var toEye = new Vector3(float.Parse(f[3], ci), 0f, float.Parse(f[4], ci));
                // Не на оси будущей лавы: отодвигаем от птицы, пока до сетки потока не станет 2 м
                // (камень на краю потока потом станет просто валуном у лавы).
                for (int step = 0; step < 6 && NearLava(lava, at, 2f); step++) at -= toEye * 2f;

                string model = CoverModels[(group.childCount + added) % CoverModels.Length];
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ArtDir + model + "/" + System.IO.Path.GetFileName(model) + ".prefab");
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, group);
                go.name = $"Cover added (perch {f[0]})";
                var size = go.GetComponentInChildren<MeshFilter>().sharedMesh.bounds.size;
                bool longX = size.x >= size.z;
                go.transform.SetPositionAndRotation(at, Quaternion.LookRotation(toEye) * Quaternion.Euler(0f, longX ? 0f : 90f, 0f));
                var ls = go.transform.localScale;
                float wide = 8f / Mathf.Max(size.x, size.z), deep = 5f / Mathf.Min(size.x, size.z), tall = 4.5f / size.y;
                go.transform.localScale = new Vector3(ls.x * (longX ? wide : deep), ls.y * tall, ls.z * (longX ? deep : wide));
                var b = Bounds(go);
                float ground = float.MaxValue;
                for (int i = 0; i <= 2; i++)
                for (int j = 0; j <= 2; j++)
                    ground = Mathf.Min(ground, Height(tiles, Mathf.Lerp(b.min.x, b.max.x, i * 0.5f), Mathf.Lerp(b.min.z, b.max.z, j * 0.5f)));
                go.transform.position += Vector3.up * (ground - 0.3f - b.min.y);
                log.Append($"\n  {go.name} в {go.transform.position}");
                added++;
            }
            SessionState.EraseString(ProposalKey);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(group.gameObject.scene);
            return log.ToString();
        }

        static bool NearLava(List<Vector2> lava, Vector3 p, float distance)
        {
            var p2 = new Vector2(p.x, p.z);
            foreach (var l in lava) if ((l - p2).sqrMagnitude < distance * distance) return true;
            return false;
        }

        static Bounds Bounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>(true);
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        static float Height(Terrain[] tiles, float x, float z)
        {
            foreach (var t in tiles)
            {
                Vector3 o = t.transform.position, s = t.terrainData.size;
                if (x >= o.x && x < o.x + s.x && z >= o.z && z < o.z + s.z)
                    return t.SampleHeight(new Vector3(x, 0f, z)) + o.y;
            }
            return 99f;
        }
    }
}
