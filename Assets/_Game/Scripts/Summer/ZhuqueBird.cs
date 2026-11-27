using System.Collections.Generic;
using UnityEngine;

namespace TanShadow.Summer
{
    // Тело Чжуцюэ: модель феникса со скелетом и Animator (AC_Zhuque). Испытание двигает корень птицы,
    // выбирает клип и задаёт простые параметры; компонент сам ничего не решает.
    // Начало координат птицы — у лап, клюв смотрит по +Z.
    //
        // Хвост — игровой объект: за кончик хватается героиня, на нём она висит в полёте. Форма хвоста
    // задаётся правилом (TailTip, уложенная испытанием линия — по ней выбираются насесты), а кости
    // живут физикой: цепочка точек с тяжестью, затуханием и ветром, сталкивается с камнями и землёй.
    // Первые кости у корня держат заданную форму, дальше хвост свисает свободно. Пока героиня висит
    // на кончике, кончик закреплён в заданной точке — полёт с ней предсказуем.
    public sealed class ZhuqueBird : MonoBehaviour
    {
        // Состояния AC_Zhuque (по именам клипов из phoenix.zip).
        public const string Perch = "01_Sleepy_Perch";
        public const string Alert = "02_Alert";
        public const string TakeOff = "03_Takeoff";
        public const string Landing = "04_Landing";
        public const string Fall = "05_Fall";
        public const string Rise = "06_Rise";
        public const string Recovery = "07_Recovery";
        public const string Flight = "08_Flight";

        [Tooltip("Animator модели феникса; пусто — первый найденный в детях")]
        public Animator animator;
        [Tooltip("Время смешивания клипов, с")]
        public float crossFade = 0.25f;
        [Tooltip("[Tbd] Клип с заданной длительностью фазы проигрывается целиком: скорость = длина клипа / длительность, " +
                 "но не меньше и не больше этих значений")]
        public float minClipSpeed = 0.6f, maxClipSpeed = 2.2f;
        [Tooltip("Цвет огненного сияния (восстановление, лава под птицей)")]
        public Color glowColor = new Color(1f, 0.45f, 0.12f);
        [Tooltip("Яркость сияния при 1. Экспозиция дневная, поэтому число большое")]
        public float glowIntensity = 600f;
        [Tooltip("Необязательный точечный свет сияния")]
        public Light glowLight;
        public float glowLightIntensity = 20000f;
        [Tooltip("На валуне тревога выше этого — поза тревоги (клип Alert)")]
        [Range(0, 1)] public float alarmPose = 0.6f;
        [Tooltip("Длина линии хвоста к длине цепочки костей. 1 — кости ложатся по линии до самого кончика")]
        [Range(0.8f, 1f)] public float tailReach = 1f;
        [Tooltip("Направление хвоста в полёте (в координатах птицы, вниз и назад). Было у прокси: 2 м вниз на 6 м назад")]
        public Vector3 flightTailDirection = new Vector3(0f, -0.33f, -0.94f);
        [Header("Лёжа после падения [Tbd]")]
        [Tooltip("Период дыхания, с")]
        [Min(0.1f)] public float breathPeriod = 2.4f;
        [Tooltip("Насколько поднимается грудь при вдохе, °")]
        public float breathAngle = 3f;
        [Tooltip("Насколько птица пытается приподнять голову к небу, °")]
        public float headLiftAngle = 22f;
        [Tooltip("Как часто пытается поднять голову, с")]
        [Min(0.1f)] public float headLiftPeriod = 4.5f;
        [Tooltip("Насколько поднятое крыло опускается к земле, °")]
        public float wingDroop = 55f;
        [Tooltip("Дрожь крыльев, °")]
        public float wingTremble = 5f;
        [Tooltip("Частота дрожи крыльев, Гц")]
        public float trembleSpeed = 7f;

        [Header("Физика хвоста [Tbd]")]
        [Tooltip("Сколько первых костей держат заданную форму (дальше хвост висит свободно)")]
        [Min(0)] public int stiffBones = 3;
        [Tooltip("Насколько кость у корня тянется к заданной форме за кадр 60 Гц (0 — свободно, 1 — жёстко)")]
        [Range(0, 1)] public float rootStiffness = 0.5f;
        [Tooltip("То же у свободной части хвоста: чуть больше 0 — хвост висит свободно, но не забывает уложенную форму")]
        [Range(0, 1)] public float freeStiffness = 0.01f;
        [Tooltip("В полёте (хвост не свисает) все кости тянутся к форме полёта с этой силой")]
        [Range(0, 1)] public float flightStiffness = 0.12f;
        [Tooltip("Гашение скорости костей за кадр 60 Гц")]
        [Range(0, 1)] public float tailDamping = 0.06f;
        [Tooltip("Гашение «хлыста» на кончике: доля поправки длины следующей кости, которая не разгоняет точку")]
        [Range(0, 1)] public float whipDamping = 0.9f;
        [Tooltip("Тяжесть хвоста, м/с²")]
        public Vector3 tailGravity = new Vector3(0f, -9.81f, 0f);
        [Tooltip("Полуширина хвоста с перьями при столкновениях с камнями и землёй, м")]
        [Min(0.05f)] public float tailRadius = 0.5f;
        [Tooltip("Ветер: наибольшая сила вбок, м/с²")]
        public float windForce = 0.5f;
        [Tooltip("Скорость смены ветра, рад/с")]
        public float windSpeed = 0.7f;
        [Tooltip("За сколько секунд кончик после захвата переходит из места, где висел, в расчётную точку")]
        [Min(0.01f)] public float pinTime = 0.3f;
        [Tooltip("Корень сдвинулся за кадр дальше — птицу переставили, хвост ставится в заданную форму заново, м")]
        public float teleportDistance = 2f;

        Transform head, body, chest, neck;
        readonly List<Transform> wingBones = new List<Transform>();
        readonly List<Transform> leftWing = new List<Transform>(), rightWing = new List<Transform>();
        Transform shoulderL, shoulderR, handL, handR;
        float lying;
        readonly List<Transform> tailBones = new List<Transform>();
        Vector3 tailRootLocal;          // корень хвоста в координатах птицы
        Vector3 tipAxisLocal;           // направление последней кости в её осях
        float tailChain = 1f;           // длина цепочки хвоста в координатах птицы, м
        readonly List<Renderer> renderers = new List<Renderer>();
        MaterialPropertyBlock block;
        static readonly int EmissiveColor = Shader.PropertyToID("_EmissiveColor");

        float tailLength = 1f, tailHang = 1f, hangDown = -1f, hangBack = 1.5f;
        readonly Vector3[] drapeLocal = new Vector3[96];
        readonly float[] drapeAlong = new float[96];
        int drapeCount;
        float glow, alarm, headYaw;
        string clip, held;

        public float TailLength => tailLength;
        // Корень хвоста и его длина сейчас (с учётом отсечения), м — для укладки хвоста по камню.
        public Vector3 TailRoot => transform.TransformPoint(tailRootLocal);
        public float TailSpan => TailLengthNow();
        public Vector3 EyePoint => head != null ? head.position : transform.TransformPoint(0f, 2.4f, 0.4f);
        public Vector3 BodyCenter => body != null ? body.position : transform.TransformPoint(0f, 1.3f, 0f);
        // Куда дотягивается кончик хвоста. Считается сразу из позы корня — можно читать в том же кадре, что и PlaceBird.
        public Vector3 TailTip => transform.TransformPoint(TailTipLocal());
        // Где кончик хвоста на самом деле (физика костей). До первого кадра — TailTip.
        public Vector3 TailEnd => simReady ? sim[simCount - 1] : TailTip;

        void Awake()
        {
            block = new MaterialPropertyBlock();
            if (animator == null) animator = GetComponentInChildren<Animator>(true);
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "Head") head = t;
                else if (t.name == "Body") body = t;
                else if (t.name == "Chest") chest = t;
                else if (t.name == "Neck") neck = t;
                if (t.name.StartsWith("Forearm.") || t.name.StartsWith("Hand.")) wingBones.Add(t);
                if (t.name == "Shoulder.L") shoulderL = t;
                if (t.name == "Shoulder.R") shoulderR = t;
                if (t.name == "Hand.L") handL = t;
                if (t.name == "Hand.R") handR = t;
            }
            // Цепочка хвоста: Tail, Tail.001 … до последней кости.
            var bone = FindChild(transform, "Tail");
            while (bone != null)
            {
                tailBones.Add(bone);
                bone = NextTailBone(bone);
            }
            if (tailBones.Count >= 2)
            {
                tailRootLocal = transform.InverseTransformPoint(tailBones[0].position);
                // Куда «смотрит» последняя кость (от предыдущей к ней) — в её собственных осях: по этой оси
                // кончик потом выравнивается вдоль хвоста, чтобы перья на конце не загибались крючком.
                Transform last = tailBones[tailBones.Count - 1], before = tailBones[tailBones.Count - 2];
                tipAxisLocal = Quaternion.Inverse(last.rotation) * (last.position - before.position).normalized;
                tailChain = 0f;
                for (int i = 1; i < tailBones.Count; i++)
                    tailChain += (transform.InverseTransformPoint(tailBones[i].position) - transform.InverseTransformPoint(tailBones[i - 1].position)).magnitude;
            }
            else Debug.LogError("ZhuqueBird: в модели нет костей хвоста Tail…", this);
            // Кости крыла у модели — не цепочкой, а соседями под Chest (плечо, локоть, кисть, перья):
            // чтобы опустить крыло целиком, поворачиваем их все вокруг плеча.
            if (chest != null)
                foreach (Transform c in chest)
                {
                    if (c.name.Contains(".L")) leftWing.Add(c);
                    else if (c.name.Contains(".R")) rightWing.Add(c);
                }
            renderers.AddRange(GetComponentsInChildren<Renderer>(true));
            Play(Perch, 0f);
        }

        static Transform FindChild(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            return null;
        }

        static Transform NextTailBone(Transform bone)
        {
            foreach (Transform c in bone) if (c.name.StartsWith("Tail")) return c;
            return null;
        }

        // ---------------- Управление из испытания ----------------

        // Сменить клип (без перезапуска, если он уже играет). duration > 0 — сколько длится фаза: клип
        // ускоряется или замедляется, чтобы проиграться целиком (взлёт, посадка, падение не обрываются).
        public void Play(string state, float fade = -1f, float duration = 0f)
        {
            held = state;
            Show(state, fade, duration);
        }

        void Show(string state, float fade = -1f, float duration = 0f)
        {
            if (animator == null || state == clip) return;
            clip = state;
            animator.speed = duration > 0f ? Mathf.Clamp(ClipLength(state) / duration, minClipSpeed, maxClipSpeed) : 1f;
            animator.CrossFadeInFixedTime(state, fade < 0f ? crossFade : fade);
        }

        float ClipLength(string state)
        {
            var controller = animator.runtimeAnimatorController;
            if (controller != null)
                foreach (var c in controller.animationClips)
                    if (c != null && c.name == state) return c.length;
            return 1f;
        }

        // 0 — нет, 1 — лежит после падения: дышит, пытается поднять голову, крылья подрагивают.
        public void SetLying(float weight) { lying = Mathf.Clamp01(weight); }

        // 1 — хвост целый, cutStub — отсечён.
        public void SetTailLength(float length) { tailLength = length; }
        // 1 — свисает (сидит на валуне, лежит на земле), 0 — струится назад в полёте.
        public void SetTailHang(float hang) { tailHang = Mathf.Clamp01(hang); }
        // Свисающий хвост уходит назад на back (за край камня), падает до земли на высоте groundY
        // и остатком ложится на землю — кончик на земле.
        public void HangOver(float back, float groundY)
        {
            float rootY = transform.TransformPoint(tailRootLocal).y;
            hangBack = Mathf.Max(0.3f, back);
            hangDown = Mathf.Max(0.1f, rootY - groundY - 0.12f);
        }
        public void SetGlow(float value) { glow = value; }
        // 0 — спокойна, 1 — сейчас сорвётся. На валуне выше alarmPose — поза тревоги.
        public void SetAlarm(float value) { alarm = Mathf.Clamp01(value); }
        // Голова поворачивается к цели, не дальше ±limit градусов от клюва.
        public void LookAt(Vector3 target, float limit, float dt)
        {
            Vector3 local = transform.InverseTransformDirection(target - EyePoint);
            float want = local.sqrMagnitude > 0.01f ? Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg : 0f;
            headYaw = Mathf.MoveTowardsAngle(headYaw, Mathf.Clamp(want, -limit, limit), 240f * dt);
        }
        public void LookForward(float dt) => headYaw = Mathf.MoveTowardsAngle(headYaw, 0f, 120f * dt);

        // Для совместимости с вызовами испытания: кончик хвоста считается в TailTip, кости — в LateUpdate.
        public void Apply() { }

        // ---------------- Хвост ----------------

        Vector3 TailTipLocal()
        {
            Vector3 hang = HangPoint(TailLengthNow());
            Vector3 fly = tailRootLocal + flightTailDirection.normalized * TailLengthNow();
            return Vector3.Lerp(fly, hang, tailHang);
        }

        float TailLengthNow() => tailChain * tailLength * tailReach;

        // Свисающий хвост (в координатах птицы), distance — сколько метров хвоста от корня.
        // Если испытание уложило хвост по камню и земле (SetDrape) — по этой линии; иначе запасная форма:
        // назад на hangBack, вниз на hangDown, остаток по земле.
        Vector3 HangPoint(float distance)
        {
            if (drapeCount >= 2) return DrapePoint(distance);
            float length = TailLengthNow();
            float back = Mathf.Min(hangBack, length);
            float down = Mathf.Min(hangDown >= 0f ? hangDown : tailRootLocal.y, length - back);
            distance = Mathf.Clamp(distance, 0f, length);
            if (distance <= back) return tailRootLocal + new Vector3(0f, 0f, -distance);
            if (distance <= back + down) return tailRootLocal + new Vector3(0f, -(distance - back), -back);
            return tailRootLocal + new Vector3(0f, -down, -(back + distance - back - down));
        }

        Vector3 DrapePoint(float distance)
        {
            for (int i = 1; i < drapeCount; i++)
                if (drapeAlong[i] >= distance)
                    return Vector3.Lerp(drapeLocal[i - 1], drapeLocal[i], Mathf.InverseLerp(drapeAlong[i - 1], drapeAlong[i], distance));
            // Хвост длиннее уложенной линии — продолжаем её последним направлением.
            Vector3 last = drapeLocal[drapeCount - 1];
            Vector3 dir = (last - drapeLocal[drapeCount - 2]).normalized;
            return last + dir * (distance - drapeAlong[drapeCount - 1]);
        }

        // Линия, по которой лежит свисающий хвост (мировые точки от корня). Хранится в координатах птицы.
        public void SetDrape(Vector3[] world, int count)
        {
            drapeCount = Mathf.Min(count, drapeLocal.Length);
            for (int i = 0; i < drapeCount; i++)
            {
                drapeLocal[i] = transform.InverseTransformPoint(world[i]);
                drapeAlong[i] = i == 0 ? 0f : drapeAlong[i - 1] + (drapeLocal[i] - drapeLocal[i - 1]).magnitude;
            }
        }

        public void ClearDrape() => drapeCount = 0;

        // Форма хвоста: точки по длине; свисание и полёт смешиваются по tailHang, углы скруглены.
        void TailPath(Vector3[] path)
        {
            float length = TailLengthNow();
            Vector3 dir = flightTailDirection.normalized;
            int last = path.Length - 1;
            for (int i = 0; i <= last; i++)
            {
                float d = length * i / last;
                path[i] = Vector3.Lerp(tailRootLocal + dir * d, HangPoint(d), tailHang);
            }
            // Скругляем изломы (концы на месте): хвост гнётся дугой, а не углом. Уложенная линия уже гладкая.
            for (int pass = 0; pass < (drapeCount >= 2 && tailHang > 0.99f ? 0 : 3); pass++)
            {
                Vector3 prev = path[0];
                for (int i = 1; i < last; i++)
                {
                    Vector3 cur = path[i];
                    path[i] = 0.25f * prev + 0.5f * cur + 0.25f * path[i + 1];
                    prev = cur;
                }
            }
        }

        readonly Vector3[] tailPath = new Vector3[33];
        readonly float[] tailAlong = new float[33];

        void SolveTail()
        {
            int n = tailBones.Count;
            if (n < 2) return;
            // Отсечение укорачивает хвост от корня (масштаб корневой кости), отрастание — вытягивает обратно.
            tailBones[0].localScale = Vector3.one * Mathf.Max(0.05f, tailLength);

            // Кости — вдоль формы хвоста: каждая смотрит на точку на своём расстоянии от корня.
            TailPath(tailPath);
            Vector3 shift = tailBones[0].position - transform.TransformPoint(tailPath[0]);
            int lastPoint = tailPath.Length - 1;
            for (int i = 0; i <= lastPoint; i++)
            {
                float k = i / (float)lastPoint;
                tailPath[i] = transform.TransformPoint(tailPath[i]) + shift * (1f - k);
                tailAlong[i] = i == 0 ? 0f : tailAlong[i - 1] + (tailPath[i] - tailPath[i - 1]).magnitude;
            }
            // Каждая следующая кость встаёт на линию ровно на длину кости от предыдущей (хорда по линии):
            // кости не срезают изгибы и не уходят под землю там, где хвост переходит со склона на землю.
            int j = 1;
            for (int i = 0; i < n - 1; i++)
            {
                Vector3 origin = tailBones[i].position;
                float bone = (tailBones[i + 1].position - origin).magnitude;
                Vector3 target;
                while (j <= lastPoint && (tailPath[j] - origin).magnitude < bone) j++;
                if (j <= lastPoint)
                {
                    Vector3 a = tailPath[j - 1], b = tailPath[j];
                    float t = Mathf.InverseLerp((a - origin).magnitude, (b - origin).magnitude, bone);
                    target = Vector3.Lerp(a, b, t);
                }
                else
                {
                    // Линия кончилась — продолжаем её последним направлением.
                    target = origin + (tailPath[lastPoint] - tailPath[lastPoint - 1]).normalized * bone;
                    j = lastPoint;
                }
                Vector3 from = tailBones[i + 1].position - origin, to = target - origin;
                if (from.sqrMagnitude > 1e-6f && to.sqrMagnitude > 1e-6f)
                    tailBones[i].rotation = Quaternion.FromToRotation(from, to) * tailBones[i].rotation;
            }

            Transform end = tailBones[n - 1];
            // Последняя кость — вдоль конца линии хвоста: на земле кончик лежит, а не торчит вверх.
            Vector3 endDir = tailPath[lastPoint] - tailPath[lastPoint - 1];
            Vector3 tipDir = end.rotation * tipAxisLocal;
            if (endDir.sqrMagnitude > 1e-6f)
                end.rotation = Quaternion.FromToRotation(tipDir, endDir.normalized) * end.rotation;
        }

        // ---------------- Физика хвоста ----------------

        Vector3[] sim, simPrev, simTarget, simFix;
        float[] simLength;
        int simCount;
        bool simReady, tipPinned;
        Vector3 lastRoot;
        SphereCollider probe;
        readonly Collider[] simHits = new Collider[16];

        Vector3 pinOffset;
        float pinBlend;

        // Кончик закреплён в заданной точке (TailTip): героиня висит на хвосте. Из того места, где кончик
        // висел, он переходит в эту точку плавно, за pinTime.
        public void PinTip(bool pinned)
        {
            if (pinned && !tipPinned)
            {
                pinOffset = simReady ? sim[simCount - 1] - TailTip : Vector3.zero;
                pinBlend = 0f;
            }
            tipPinned = pinned;
        }
        // Поставить хвост в заданную форму без инерции (птицу переставили).
        public void ResetTail() => simReady = false;

        void CreateProbe()
        {
            // Шар для расчёта проникновения в камни. Стоит далеко под миром и ни с чем не сталкивается сам.
            var go = new GameObject("Zhuque tail probe") { hideFlags = HideFlags.HideAndDontSave, layer = 2 };
            go.transform.position = new Vector3(0f, -10000f, 0f);
            probe = go.AddComponent<SphereCollider>();
            probe.isTrigger = true;
        }

        void OnDestroy()
        {
            if (probe != null) Destroy(probe.gameObject);
        }

        // Кости уже стоят в заданной форме (SolveTail). Точки цепочки летят с инерцией и тяжестью,
        // у корня тянутся к заданной форме, держат длину костей и выталкиваются из камней; потом кости
        // поворачиваются по точкам.
        void SimulateTail(float dt)
        {
            int n = tailBones.Count;
            if (n < 2 || dt <= 0f) return;
            if (sim == null || sim.Length != n)
            {
                sim = new Vector3[n]; simPrev = new Vector3[n]; simTarget = new Vector3[n]; simFix = new Vector3[n]; simLength = new float[n];
                simCount = n;
                simReady = false;
            }
            for (int i = 0; i < n; i++) simTarget[i] = tailBones[i].position;
            for (int i = 1; i < n; i++) simLength[i] = (simTarget[i] - simTarget[i - 1]).magnitude;
            if (probe == null) CreateProbe();
            probe.radius = tailRadius;

            if (!simReady || (simTarget[0] - lastRoot).sqrMagnitude > teleportDistance * teleportDistance)
            {
                for (int i = 0; i < n; i++) sim[i] = simPrev[i] = simTarget[i];
                simReady = true;
            }
            lastRoot = simTarget[0];

            if (tipPinned) pinBlend = Mathf.MoveTowards(pinBlend, 1f, dt / pinTime);
            Vector3 pinAt = TailTip + pinOffset * (1f - Mathf.SmoothStep(0f, 1f, pinBlend));
            int steps = Mathf.Clamp(Mathf.CeilToInt(dt * 60f), 1, 4);
            float h = dt / steps;
            Vector3 wind = transform.right * (windForce * Mathf.Sin(Time.time * windSpeed)) * tailHang;
            for (int step = 0; step < steps; step++)
            {
                for (int i = 1; i < n; i++)
                {
                    Vector3 velocity = (sim[i] - simPrev[i]) * Mathf.Pow(1f - tailDamping, h * 60f);
                    simPrev[i] = sim[i];
                    sim[i] += velocity + (tailGravity + wind) * (h * h);
                    float keep = Mathf.Pow(1f - Stiffness(i), h * 60f);
                    sim[i] = Vector3.Lerp(simTarget[i], sim[i], keep);
                }
                sim[0] = simTarget[0];
                if (tipPinned)
                {
                    // Оба конца закреплены: тянем цепочку от кончика, потом от корня (FABRIK).
                    sim[n - 1] = pinAt;
                    for (int i = n - 2; i > 0; i--) sim[i] = sim[i + 1] + (sim[i] - sim[i + 1]).normalized * simLength[i + 1];
                }
                for (int i = 1; i < n; i++)
                {
                    Vector3 before = sim[i];
                    sim[i] = sim[i - 1] + (sim[i] - sim[i - 1]).normalized * simLength[i];
                    simFix[i] = sim[i] - before;   // только поправка длины: толчки от камня в «хлыст» не идут
                    bool touched = !tipPinned && CollideBone(i);
                    // Касание камня гасит скорость кости (трение): хвост ложится на уступ, а не дрожит на нём.
                    if (touched) simPrev[i] = sim[i];
                }
                // Цепочка тянется от корня, и поправки длины разгоняют кончик («хлыст»). Гасим: каждой точке
                // возвращаем часть поправки следующей (Müller 2012, Fast Simulation of Inextensible Hair).
                for (int i = 1; i < n - 1; i++) simPrev[i] += simFix[i + 1] * whipDamping;
            }

            // Кости — по точкам цепочки.
            for (int i = 0; i < n - 1; i++)
            {
                Vector3 from = tailBones[i + 1].position - tailBones[i].position, to = sim[i + 1] - tailBones[i].position;
                if (from.sqrMagnitude > 1e-6f && to.sqrMagnitude > 1e-6f)
                    tailBones[i].rotation = Quaternion.FromToRotation(from, to) * tailBones[i].rotation;
            }
            Transform end = tailBones[n - 1];
            Vector3 endDir = sim[n - 1] - sim[n - 2];
            if (endDir.sqrMagnitude > 1e-6f)
                end.rotation = Quaternion.FromToRotation(end.rotation * tipAxisLocal, endDir.normalized) * end.rotation;
        }

        // Насколько точка цепочки держит заданную форму: в полёте — вся одинаково, на валуне — только у корня.
        float Stiffness(int i)
        {
            float perched = i <= stiffBones
                ? Mathf.Lerp(rootStiffness, freeStiffness, (i - 1f) / Mathf.Max(1, stiffBones))
                : freeStiffness;
            return Mathf.Lerp(flightStiffness, perched, tailHang);
        }

        // Кость от точки i-1 к i не должна входить в камни и землю: проверяем по её длине и, если задела,
        // поворачиваем вокруг начала (начало уже на месте), потом восстанавливаем длину.
        bool CollideBone(int i)
        {
            bool hit = false;
            for (int k = 1; k <= 3; k++)
            {
                float t = k / 3f;
                Vector3 push = Penetration(Vector3.Lerp(sim[i - 1], sim[i], t));
                if (push.sqrMagnitude < 1e-8f) continue;
                sim[i] += push * Mathf.Min(1f / t, 1.5f);   // поворот вокруг начала кости, без перескока через камень
                sim[i] = sim[i - 1] + (sim[i] - sim[i - 1]).normalized * simLength[i];
                hit = true;
            }
            return hit;
        }

        // Насколько вытолкнуть шар хвоста в точке из камней и земли (свои коллайдеры и героиня не считаются).
        Vector3 Penetration(Vector3 p)
        {
            Vector3 push = Vector3.zero;
            int count = Physics.OverlapSphereNonAlloc(p, tailRadius, simHits, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var c = simHits[i];
                if (c == null || c.transform.IsChildOf(transform) || c is CharacterController) continue;
                if (Physics.ComputePenetration(probe, p + push, Quaternion.identity, c, c.transform.position, c.transform.rotation, out var dir, out float depth))
                    push += dir * depth;
            }
            return push;
        }

        // Поверх последнего кадра падения (птица на боку): грудь дышит, голова время от времени тянется
        // к небу и бессильно опускается, крылья подрагивают приступами. Оси — мировые: птица лежит то
        // на левом, то на правом боку, а «вверх» для головы всегда к небу.
        void Lie(float t)
        {
            float w = lying;
            if (chest != null)
                chest.rotation = Quaternion.AngleAxis(Mathf.Sin(t * 2f * Mathf.PI / breathPeriod) * breathAngle * w, transform.right) * chest.rotation;
            if (neck != null && head != null)
            {
                float lift = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(t * 2f * Mathf.PI / headLiftPeriod)), 3f) * headLiftAngle * w;
                Vector3 along = head.position - neck.position;
                if (along.sqrMagnitude > 1e-6f && lift > 0.01f)
                {
                    Vector3 raised = Vector3.RotateTowards(along, Vector3.up, lift * Mathf.Deg2Rad, 0f);
                    neck.rotation = Quaternion.FromToRotation(along, raised) * neck.rotation;
                    // Голова чуть отстаёт — движение от шеи, а не одним куском.
                    Vector3 beak = head.forward;
                    head.rotation = Quaternion.FromToRotation(beak, Vector3.RotateTowards(beak, Vector3.up, lift * 0.4f * Mathf.Deg2Rad, 0f)) * head.rotation;
                }
            }
            // Поднятое к небу крыло (птица на боку) под своей тяжестью опускается к земле.
            DroopWing(leftWing, shoulderL, handL, w);
            DroopWing(rightWing, shoulderR, handR, w);
            // Дрожь приступами: шум медленно открывает и закрывает её.
            float burst = Mathf.SmoothStep(0f, 1f, (Mathf.PerlinNoise(t * 0.35f, 3.1f) - 0.45f) / 0.2f);
            for (int i = 0; i < wingBones.Count; i++)
            {
                float shake = (Mathf.PerlinNoise(t * trembleSpeed, i * 1.7f) - 0.5f) * 2f * wingTremble * burst * w;
                wingBones[i].rotation = Quaternion.AngleAxis(shake, transform.forward) * wingBones[i].rotation;
            }
        }

        void DroopWing(List<Transform> bones, Transform shoulder, Transform hand, float w)
        {
            if (shoulder == null || hand == null || bones.Count == 0) return;
            Vector3 pivot = shoulder.position;
            Vector3 span = hand.position - pivot;
            if (span.sqrMagnitude < 1e-4f) return;
            float raised = Mathf.Clamp01((span.normalized.y + 0.2f) / 0.7f);
            float angle = wingDroop * w * raised;
            if (angle < 0.1f) return;
            // Не ниже земли под кистью: крыло ложится на землю, а не уходит в неё.
            float floor = Physics.Raycast(hand.position + Vector3.up * 6f, Vector3.down, out var hit, 30f, ~0, QueryTriggerInteraction.Ignore)
                ? hit.point.y + 0.3f : float.NegativeInfinity;
            Quaternion turn = Quaternion.identity;
            for (float a = angle; a > 0.1f; a -= 5f)
            {
                turn = Quaternion.FromToRotation(span, Vector3.RotateTowards(span, Vector3.down, a * Mathf.Deg2Rad, 0f));
                if ((pivot + turn * span).y >= floor) break;
                turn = Quaternion.identity;
            }
            foreach (var b in bones)
                b.SetPositionAndRotation(pivot + turn * (b.position - pivot), turn * b.rotation);
        }

        static Vector3 PointAt(Vector3[] samples, float[] along, float distance)
        {
            for (int i = 1; i < samples.Length; i++)
                if (along[i] >= distance)
                    return Vector3.Lerp(samples[i - 1], samples[i], Mathf.InverseLerp(along[i - 1], along[i], distance));
            return samples[samples.Length - 1];
        }

        // ---------------- Кадр ----------------

        void LateUpdate()
        {
            // На валуне поза тревоги растёт из шкалы обнаружения.
            if (held == Perch || held == Alert)
                Show(alarm >= alarmPose || held == Alert ? Alert : Perch);

            if (head != null && Mathf.Abs(headYaw) > 0.01f)
                head.rotation = Quaternion.AngleAxis(headYaw, transform.up) * head.rotation;
            if (lying > 0.001f) Lie(Time.time);
            SolveTail();
            SimulateTail(Time.deltaTime);

            // Птица светится только вспышкой восстановления и падением в лаву (хвост отдельно не подсвечивается).
            foreach (var r in renderers)
            {
                if (r == null) continue;
                var mats = r.sharedMaterials;
                for (int m = 0; m < mats.Length; m++)
                {
                    block.SetColor(EmissiveColor, glowColor * (glowIntensity * glow));
                    r.SetPropertyBlock(block, m);
                }
            }
            if (glowLight != null)
            {
                glowLight.enabled = glow > 0.01f;
                glowLight.intensity = glowLightIntensity * glow;
            }
        }
    }
}
