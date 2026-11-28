using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Events;

namespace TanShadow.Spring
{
    // Лазурный дракон (Цинлун) — процедурный полёт.
    // Голова летит по плавной 3D-траектории, тело повторяет её путь (каждый позвонок проходит там, где была голова),
    // поверх — бегущая волна, крен в поворотах и повороты головы. Клипы аниматора отвечают только за лапы, гриву, усы
    // и пасть: позвоночник перезаписывается в LateUpdate, root motion не используется.
    // В ущелье — линия полёта (точки полёта + дно и стены лучами). У красной точки (exitPoints) — свечкой вверх вдоль
    // ущелья, над стенами петля, пике обратно в эту же точку. У жёлтой границы (turnBounds) — так же, но петля-разворот,
    // и обратно уже в другую сторону. Вдоль ущелья за жёлтые границы не залетает.
    // Жемчужина (pearl): дракон спускается к лежащему шару, толкает его лапой (по очереди правой и левой), шар катится
    // вдоль ущелья и останавливается, дракон догоняет и толкает снова. Шар у жёлтой границы достаётся пике после разворота.
    [RequireComponent(typeof(Animator))]
    public sealed class QinglongDragon : MonoBehaviour
    {
        public const string CalmState = "FlyCalm", DashState = "DashUp", DiveState = "DiveDown",
            PushStateR = "PearlPush_R", PushStateL = "PearlPush_L";
        static readonly int PushRId = Animator.StringToHash("PushR"), PushLId = Animator.StringToHash("PushL"),
            DashUpId = Animator.StringToHash("DashUp"), DiveId = Animator.StringToHash("Dive"),
            PushStateRId = Animator.StringToHash(PushStateR), PushStateLId = Animator.StringToHash(PushStateL);

        // Суставы позвоночника: J0 — основание морды (кончик кости Head), J1 Head, J2 Neck, J3 Chest, J4..J18 — Body.02..Tail.08,
        // J19 — кончик хвоста. Кость i идёт от сустава BoneFrom[i] к BoneTo[i].
        static readonly string[] JointBones =
        {
            "Muzzle", "Head", "Neck", "Chest", "Body.02", "Body.03", "Body.04", "Body.05", "Body.06", "Body.07", "Body.08",
            "Tail.01", "Tail.02", "Tail.03", "Tail.04", "Tail.05", "Tail.06", "Tail.07", "Tail.08",
        };
        static readonly string[] SpineBones =
        {
            "Head", "Neck", "Chest", "Body.01", "Body.02", "Body.03", "Body.04", "Body.05", "Body.06", "Body.07", "Body.08",
            "Tail.01", "Tail.02", "Tail.03", "Tail.04", "Tail.05", "Tail.06", "Tail.07", "Tail.08",
        };
        static readonly int[] BoneFrom = { 1, 2, 3, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18 };
        static readonly int[] BoneTo = { 0, 1, 2, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19 };
        static readonly int[] WriteOrder = { 2, 1, 0, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18 };   // родители раньше детей
        const float TailTipLength = .068f;  // длина Tail.08 в масштабе модели (Blender 0.531 × 0.1281)

        const float MaxCenterShift = 30f, GroundProbe = 3f;
        const float WallKeep = .5f;         // до стены не ближе, м при масштабе 1
        const float ZoneMargin = 3f;        // м — запас к жёлтым границам
        const float RimStep = 5f;           // шаг профиля высоты стен, м вдоль ущелья
        const float PlanAhead = 30f;        // за сколько метров до точки решать, что делать
        const int LoopSteps = 512;

        // Клип толчка A_PearlPush_* (30 к/с): касание на кадре 54 — через 1.8 с от начала клипа. Ниже — суставы правой лапы
        // на этом кадре в пространстве кости Chest (единицы кости; × её lossyScale = метры), левая — зеркально по X.
        const float PushLead = 1.8f, PushFade = .25f;
        static readonly Vector3 ShoulderLocal = new Vector3(.18f, .26f, -.04f);     // UpperArm.R
        static readonly Vector3 EffectorLocal = new Vector3(.40f, 1.36f, .91f);     // Digit.03.R — основание среднего пальца
        const float ClawFwd = .32f, ClawDown = .21f;    // кончики когтей от основания пальца на кадре касания: вперёд и вниз, ед. кости
        const float PawSwing = 30f;         // лапа отведена от тела (градусы вокруг оси груди): шар сбоку, голова проходит мимо
        const float PawPad = .02f;          // от кончиков когтей до поверхности шара, единицы кости
        const float BodyClearBone = .6f;    // низ груди над землёй, единицы кости
        const float PearlBoundKeep = 32f;   // шар не докатывается до жёлтых ближе, м
        const float MinRoll = 20f;          // меньше некуда катить — толкнёт после разворота
        const float StoopBrake = 6f;        // торможение в пике к шару, м/с²

        [Header("Траектория")]
        [Tooltip("Родитель точек полёта (дети — точки вдоль ущелья).")]
        public Transform flightPath;
        [Tooltip("Высота полёта над дном ущелья, м. 0 — та, на которой дракон стоит в сцене.")]
        [Min(0)] public float flightHeight = 0f;
        [Tooltip("Держаться посередине между стенами на высоте полёта.")]
        public bool centerBetweenWalls = true;
        [Tooltip("Слои дна и стен ущелья (для лучей).")]
        public LayerMask canyonLayers = Physics.DefaultRaycastLayers;

        [Header("Зона полёта")]
        [Tooltip("Жёлтые границы (2 шт.). Дальше них вдоль ущелья не летает: свечка вверх, петля-разворот, обратно в другую сторону.")]
        public Transform[] turnBounds = new Transform[0];
        [Tooltip("Красные точки: свечка вверх, петля над ущельем, пике обратно в эту же точку и дальше в ту же сторону.")]
        public Transform[] exitPoints = new Transform[0];
        [Tooltip("Вероятность вылететь у очередной красной точки (иначе пролетает её).")]
        [Range(0, 1)] public float exitChance = .75f;

        [Header("Полёт")]
        [Tooltip("Скорость спокойного полёта по ущелью, м/с.")]
        [Min(1)] public float cruiseSpeed = 5f;
        [Tooltip("Скорость в свечке, петле и пике, м/с.")]
        [Min(1)] public float fastSpeed = 36f;
        [Tooltip("Разгон и торможение, м/с².")]
        [Min(.5f)] public float acceleration = 14f, braking = 9f;
        [Tooltip("Крутизна свечки и пике, градусы.")]
        [Range(45, 85)] public float climbAngle = 72f;
        [Tooltip("Насколько выше стен выходит из свечки, м.")]
        [Min(0)] public float rimClearance = 15f;
        [Tooltip("Радиус поворотов в петле над ущельем, м. От него же — плавность выхода из пике и свечки.")]
        [Min(8)] public float loopRadius = 17f;
        [Tooltip("Крен в поворотах (1 — как самолёт).")]
        [Range(0, 2)] public float bankAmount = 1f;
        [Tooltip("Волна по телу (1 — как в клипах).")]
        [Range(0, 2)] public float waveAmount = 1f;
        [Tooltip("Плавное виляние линии полёта в ущелье (доля длины тела).")]
        [Range(0, .3f)] public float weave = .1f;

        [Header("Жемчужина")]
        [Tooltip("Шар, который дракон катает лапой. Пусто — ищется по тегу ниже.")]
        public Transform pearl;
        [Tooltip("Тег шара (если поле выше пустое).")]
        public string pearlTag = "BallElidtest";
        [Tooltip("Сколько шар катится после толчка, м (мин, макс). У жёлтых — меньше, чтобы не укатился за них.")]
        public Vector2 pearlRoll = new Vector2(85f, 110f);
        [Tooltip("Замедление шара, м/с²: больше — быстрее останавливается.")]
        [Min(.2f)] public float pearlFriction = 1.1f;
        [Tooltip("Скорость дракона в момент толчка, м/с.")]
        [Min(1)] public float pushSpeed = 4.2f;
        [Tooltip("Длина спуска к шару вдоль ущелья, м (подъём после толчка чуть короче). Меньше — круче.")]
        [Min(20)] public float swoopLength = 40f;

        [Header("Голова")]
        [Tooltip("Смотрит на игрока (Main Camera), когда тот близко.")]
        public bool lookAtPlayer = true;
        [Min(0)] public float lookAtPlayerDistance = 120f;
        [Tooltip("Иногда оглядывается по сторонам (с, мин-макс между взглядами).")]
        public Vector2 glanceSeconds = new Vector2(5f, 10f);

        [Header("Поведение")]
        public bool autoBehaviour = true;
        [Tooltip("Если шара нет: секунды между взмахами лапой в воздухе (мин, макс).")]
        public Vector2 calmSeconds = new Vector2(8f, 16f);

        [Header("События")]
        [Tooltip("Касание шара лапой. true — правая лапа.")]
        public UnityEvent<bool> pearlPushed = new UnityEvent<bool>();

        struct Pt { public Vector3 p; public float u, cs, v, low; }             // точка траектории головы (low — у земли, 0..1)
        struct Hist { public float u, phase, amp; public Vector3 up; }        // что было у головы, когда она здесь пролетала
        struct Seg { public float len, turn; public Seg(float l, float t) { len = l; turn = t; } }
        // C — грудь в момент касания; vStart/latStart — откуда начинается спуск (над/под и вбок от линии полёта)
        struct SwoopPlan { public float start, xc, Ld, Lu, vStart, latStart; public Vector3 C, f; public bool right, turn; public int d; }

        Animator animator;
        // ущелье
        readonly List<Vector3> points = new List<Vector3>();
        readonly List<float> along = new List<float>();
        float[] rim = new float[0];                     // верх стен над линией полёта, м (шаг RimStep)
        float zoneMin, zoneMax;
        float[] exits = new float[0];
        // скелет
        Transform[] spine;
        Quaternion[] boneOffset;
        float[] chord;
        float bodyLength, chestArc, boneScale = 1f;
        bool rigReady;
        readonly Transform[] armUpper = new Transform[2], armFore = new Transform[2], armPaw = new Transform[2], armEff = new Transform[2];
        // траектория и история головы
        readonly List<Pt> track = new List<Pt>();
        readonly List<Hist> hist = new List<Hist>();
        float headU, speed, speedVel, phase, amp = 1f, clock;
        Vector3 upCur = Vector3.up;
        int dir = 1;
        float cursorCs;                                 // ущелье: до куда траектория уже продолжена
        float maneuverStartU = float.MaxValue, diveStartU = float.MaxValue, maneuverEndU = -1f;
        int passedExit = -1;
        // голова
        Quaternion lookQ = Quaternion.identity;
        Vector3 glanceDir; float glanceUntil, nextGlance, nextPush;
        bool rightPaw = true;
        // жемчужина
        Transform pearlT;
        Collider[] pearlCols = new Collider[0];
        bool pearlOk, rolling;
        float pearlR = .4f, pearlCs, pearlLat, pearlMin, pearlMax, pearlYVel;
        Vector3 pearlLocalCenter, pearlC, pearlLastPos;
        float rollT, rollV0, rollA, rollD, rollCs0;
        float[] rollLats = new float[0];               // смещение поперёк ущелья вдоль пути качения (шаг RollStep)
        int rollDir = 1;
        const float RollStep = 4f;
        // толчок
        bool pushArmed, pushTriggered, pushDone, pushRight;
        int pushDir = 1, pushCount;
        float pushU, pushClock, lookPearlUntil = -1f;
        Vector3 pushFwd = Vector3.forward, pushContact, pushTarget;
        float skipCs = float.NaN; int skipDir;           // шар, к которому с этой стороны не подлететь (камни)
        bool swooping;                                   // текущий манёвр — к шару (после толчка можно сразу к нему снова)
        float lastPushClock;                             // давно не толкал — подлетает и не к идеальному месту
        bool Relaxed => clock - lastPushClock > 100f;
        static readonly RaycastHit[] rayHits = new RaycastHit[24];
        static readonly Collider[] overlapBuf = new Collider[16];
        // поза (буферы)
        readonly List<Vector3> bodyP = new List<Vector3>(), bodyUp = new List<Vector3>();
        Vector3[] J = new Vector3[20];
        Vector3[] jointUp = new Vector3[20];

        public float PathLength => along.Count > 0 ? along[along.Count - 1] : 0f;
        public int Direction => dir;
        public bool InManeuver => headU < maneuverEndU;
        public IReadOnlyList<Vector3> PathPoints => points;
        public Vector3 HeadPosition => J[0];
        public IReadOnlyList<Vector3> Joints => J;
        public float HeadCanyonDistance => track.Count > 1 ? TrackCs(headU) : float.NaN;
        public float Speed => speed;
        public float BodyLength => bodyLength;
        public bool RigReady => rigReady;
        public Vector2 Zone => new Vector2(zoneMin, zoneMax);
        public float RimHeightAt(float cs) => RimAt(cs);
        public Vector3 CanyonAt(float cs) => Sample(cs);
        // для проверки из редактора
        public bool HasPearl => pearlOk;
        public Vector3 PearlCenter => pearlC;
        public float PearlRadius => pearlR;
        public float PearlCanyonDistance => pearlCs;
        public bool PearlRolling => rolling;
        public int PushCount => pushCount;
        public bool PushRight => pushRight;
        public float PushClipTime => pushArmed && pushTriggered ? clock - pushClock : -1f;
        public Vector3 PushTarget => pushTarget;
        public Vector3 PawEffector(bool right) => armEff[right ? 0 : 1] ? armEff[right ? 0 : 1].position : Vector3.zero;
        public float GroundBelowPoint(Vector3 p) => GroundAt(p, out float y) ? y : float.NaN;

        // ------------------------------------------------------------------ жизненный цикл
        void Awake()
        {
            animator = GetComponent<Animator>();
            animator.applyRootMotion = false;
            Init();
        }

        // Публично — для проверки из редактора без Play.
        public void Init()
        {
            animator = animator ? animator : GetComponent<Animator>();
            BuildPath();
            BuildRim();
            ResolveZone();
            rigReady = ReadRig();
            FindPearl();
            StartFlight();
        }

        void LateUpdate()
        {
            if (!rigReady || track.Count < 2) return;
            Step(Mathf.Min(Time.deltaTime, .05f));
        }

        public void Step(float dt)
        {
            clock += dt;
            if (autoBehaviour) Plan();
            else if (!InManeuver) { float cs = TrackCs(headU); if (!float.IsNaN(cs)) ExtendCruise(cs, 160f); }
            Advance(dt);
            PearlStep(dt);
            Look(dt);
            Pose();
            PawIK();
        }

        // Взмах лапой без шара (в воздухе).
        public void PushPearl(bool right)
        {
            if (animator && animator.runtimeAnimatorController && Application.isPlaying)
                animator.CrossFadeInFixedTime(right ? PushStateRId : PushStateLId, PushFade, 0, 0f);
        }

        // Animation Event из клипов A_PearlPush_* (кадр касания): intParameter 1 — правая лапа.
        public void OnPearlPush(int right)
        {
            if (pushArmed && pushTriggered && !pushDone) DoPush();
            else pearlPushed.Invoke(right != 0);
        }

        // ------------------------------------------------------------------ скелет
        bool ReadRig()
        {
            var smr = GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault(r => r.sharedMesh && r.bones.Length > 0);
            if (!smr) return false;
            var bind = new Dictionary<string, Matrix4x4>();
            var byName = new Dictionary<string, Transform>();
            var bp = smr.sharedMesh.bindposes;
            for (int i = 0; i < smr.bones.Length && i < bp.Length; i++)
            {
                var b = smr.bones[i];
                if (!b || bind.ContainsKey(b.name)) continue;
                bind[b.name] = transform.worldToLocalMatrix * smr.transform.localToWorldMatrix * bp[i].inverse;   // поза привязки, пространство дракона
                byName[b.name] = b;
            }
            if (SpineBones.Any(n => !bind.ContainsKey(n)) || JointBones.Any(n => !bind.ContainsKey(n))) return false;

            var rest = new Vector3[20];
            for (int j = 0; j < JointBones.Length; j++) rest[j] = bind[JointBones[j]].GetColumn(3);
            var tail = bind["Tail.08"];
            rest[19] = rest[18] + ((Vector3)tail.GetColumn(1)).normalized * TailTipLength;

            float scale = transform.lossyScale.x;
            chord = new float[19];
            bodyLength = 0f;
            for (int k = 0; k < 19; k++) { chord[k] = (rest[k + 1] - rest[k]).magnitude * scale; bodyLength += chord[k]; }
            chestArc = chord[0] + chord[1] + chord[2];

            spine = new Transform[SpineBones.Length];
            boneOffset = new Quaternion[SpineBones.Length];
            for (int i = 0; i < SpineBones.Length; i++)
            {
                var m = bind[SpineBones[i]];
                var rot = Quaternion.LookRotation(m.GetColumn(2), m.GetColumn(1));
                var frame = Quaternion.LookRotation(rest[BoneTo[i]] - rest[BoneFrom[i]], Vector3.up);
                boneOffset[i] = Quaternion.Inverse(frame) * rot;
                spine[i] = byName[SpineBones[i]];
            }
            boneScale = spine[2].lossyScale.x;

            string[] sides = { "R", "L" };
            for (int k = 0; k < 2; k++)
            {
                byName.TryGetValue("UpperArm." + sides[k], out armUpper[k]);
                byName.TryGetValue("Forearm." + sides[k], out armFore[k]);
                byName.TryGetValue("Paw." + sides[k], out armPaw[k]);
                byName.TryGetValue("Digit.03." + sides[k], out armEff[k]);
            }
            return true;
        }

        // ------------------------------------------------------------------ старт
        void StartFlight()
        {
            track.Clear(); hist.Clear();
            headU = 0f;
            pushArmed = pushTriggered = pushDone = false;
            lookPearlUntil = -1f;
            lastPushClock = clock;
            if (points.Count < 2 || !rigReady) return;
            float cs = Closest(transform.position);
            Vector3 tangent = Sample(cs + 2f) - Sample(cs - 2f);
            dir = Vector3.Dot(transform.forward, tangent) >= 0f ? 1 : -1;
            float head = cs + dir * bodyLength * .55f;
            float lo = zoneMin + ZoneMargin, hi = zoneMax - ZoneMargin;
            if (head < lo) { head = lo + bodyLength * .2f; dir = 1; }
            if (head > hi) { head = hi - bodyLength * .2f; dir = -1; }
            if (pearlOk && autoBehaviour) StartNearPearl(ref head, lo + bodyLength * .2f, hi - bodyLength * .2f);

            // Траектория уже пройдена головой на длину тела назад — тело сразу лежит на линии полёта.
            float back = head - dir * (bodyLength + 20f);
            cursorCs = Mathf.Clamp(back, 0f, PathLength);
            Append(CanyonPoint(cursorCs, 0f), cursorCs, cruiseSpeed);
            ExtendCruise(head, 0f);
            headU = UAtCs(head);
            speed = cruiseSpeed; speedVel = 0f;
            float lambda = Wavelength(cruiseSpeed);
            foreach (var p in track)
                if (p.u <= headU) hist.Add(new Hist { u = p.u, phase = 2f * Mathf.PI * p.u / lambda, amp = 1f, up = Vector3.up });
            phase = 2f * Mathf.PI * headU / lambda; amp = 1f; upCur = Vector3.up;
            maneuverStartU = diveStartU = float.MaxValue; maneuverEndU = -1f; passedExit = -1;
            nextPush = clock + Random.Range(calmSeconds.x, calmSeconds.y);
            nextGlance = clock + Random.Range(glanceSeconds.x, glanceSeconds.y);
            Pose();
        }

        // С шаром — начать так, чтобы скоро до него добраться: спуском вдоль ущелья (с той стороны, куда больше места катить)
        // или, если шар у жёлтой границы, — разворотом у неё и пике прямо к шару.
        void StartNearPearl(ref float head, float lo, float hi)
        {
            float b = pearlCs;
            int far = zoneMax - b > b - zoneMin ? 1 : -1;
            float h = b - far * (chestArc + swoopLength + PlanAhead * .5f + 8f);
            if (h > lo && h < hi) { head = h; dir = far; return; }
            int near = -far;
            float nb = near > 0 ? zoneMax : zoneMin;
            float s0 = nb - near * (ClimbReach(nb) + ZoneMargin);
            head = Mathf.Clamp(s0 - near * (PlanAhead + 10f), lo, hi);
            dir = near;
        }

        // ------------------------------------------------------------------ траектория головы
        void Append(Vector3 p, float cs, float v, float low = 0f)
        {
            if (track.Count == 0) { track.Add(new Pt { p = p, u = 0f, cs = cs, v = v, low = low }); return; }
            var last = track[track.Count - 1];
            float d = Vector3.Distance(last.p, p);
            if (d < 1e-3f) return;
            track.Add(new Pt { p = p, u = last.u + d, cs = cs, v = v, low = low });
        }

        // Продолжить по ущелью в сторону dir (до точки headCs + запас).
        void ExtendCruise(float untilCs, float extra)
        {
            float need = untilCs + dir * extra;
            for (int guard = 0; guard < 4000; guard++)
            {
                if ((cursorCs - need) * dir >= 0f && track.Count > 1 && track[track.Count - 1].u - headU > 140f) break;
                float next = Mathf.Clamp(cursorCs + dir * 2f, 0f, PathLength);
                if (Mathf.Abs(next - cursorCs) < 1e-3f) break;   // конец ущелья
                cursorCs = next;
                Append(CanyonPoint(cursorCs, 0f), cursorCs, cruiseSpeed);
            }
        }

        int IndexAt(float u)
        {
            int lo = 0, hi = track.Count - 1;
            if (u <= track[0].u) return 0;
            if (u >= track[hi].u) return Mathf.Max(hi - 1, 0);
            while (hi - lo > 1) { int mid = (lo + hi) >> 1; if (track[mid].u <= u) lo = mid; else hi = mid; }
            return lo;
        }

        Vector3 TrackPos(float u)
        {
            int i = IndexAt(u);
            var a = track[i]; var b = track[Mathf.Min(i + 1, track.Count - 1)];
            float span = b.u - a.u;
            return Vector3.Lerp(a.p, b.p, span > 0f ? Mathf.Clamp01((u - a.u) / span) : 0f);
        }

        Vector3 TrackTangent(float u, float h = 1f)
        {
            Vector3 t = TrackPos(u + h) - TrackPos(u - h);
            return t.sqrMagnitude > 1e-8f ? t.normalized : Vector3.forward;
        }

        float TrackCs(float u)
        {
            int i = IndexAt(u);
            var a = track[i]; var b = track[Mathf.Min(i + 1, track.Count - 1)];
            if (float.IsNaN(a.cs) || float.IsNaN(b.cs)) return float.NaN;
            float span = b.u - a.u;
            return Mathf.Lerp(a.cs, b.cs, span > 0f ? Mathf.Clamp01((u - a.u) / span) : 0f);
        }

        float TrackSpeed(float u) => track[IndexAt(u)].v;
        float TrackLow(float u) => track[IndexAt(u)].low;

        float UAtCs(float cs)
        {
            for (int i = 0; i < track.Count - 1; i++)
            {
                var a = track[i]; var b = track[i + 1];
                if (float.IsNaN(a.cs) || float.IsNaN(b.cs)) continue;
                if ((cs - a.cs) * (cs - b.cs) <= 0f)
                {
                    float span = b.cs - a.cs;
                    return Mathf.Lerp(a.u, b.u, Mathf.Abs(span) > 1e-5f ? (cs - a.cs) / span : 0f);
                }
            }
            return track[track.Count - 1].u;
        }

        // Убрать всё впереди, начиная с точки ущелья cs (по направлению dir) — туда встанет манёвр.
        void TruncateAtCs(float cs)
        {
            int i = IndexAt(headU) + 1;
            for (; i < track.Count; i++)
                if (!float.IsNaN(track[i].cs) && (track[i].cs - cs) * dir >= 0f) break;
            if (i < track.Count) track.RemoveRange(i, track.Count - i);
        }

        void Trim()
        {
            float keep = headU - bodyLength - 15f;
            int n = 0;
            while (n < track.Count - 2 && track[n + 1].u < keep) n++;
            if (n > 64) track.RemoveRange(0, n);
            n = 0;
            while (n < hist.Count - 2 && hist[n + 1].u < keep) n++;
            if (n > 64) hist.RemoveRange(0, n);
            if (headU > 20000f) Rebase(headU - 1000f);
        }

        void Rebase(float off)
        {
            for (int i = 0; i < track.Count; i++) { var p = track[i]; p.u -= off; track[i] = p; }
            for (int i = 0; i < hist.Count; i++) { var h = hist[i]; h.u -= off; hist[i] = h; }
            headU -= off;
            pushU -= off;
            if (maneuverStartU != float.MaxValue) maneuverStartU -= off;
            if (diveStartU != float.MaxValue) diveStartU -= off;
            maneuverEndU -= off;
        }

        // ------------------------------------------------------------------ решения
        void Plan()
        {
            if (InManeuver)
            {
                // подъём после толчка: шар остановится недалеко впереди — снова вниз к нему, не набирая высоту до конца
                if (!pearlOk || !swooping || pushArmed || headU < pushU + chestArc + 4f) return;
                float ccs = TrackCs(headU);
                if (float.IsNaN(ccs)) return;
                float cb = dir > 0 ? zoneMax : zoneMin;
                float cs0 = cb - dir * (ClimbReach(cb) + ZoneMargin);
                if (PlanSwoop(ccs, cs0, 0, out var chain) && (chain.start - ccs) * dir <= PlanAhead)
                {
                    if (TrySwoop(chain)) return;
                    if (PlanSwoop(ccs, cs0, chain.right ? -1 : 1, out var chain2) && TrySwoop(chain2)) return;
                    PearlRest(out skipCs, out _, out _); skipDir = dir;
                }
                return;
            }
            float cs = TrackCs(headU);
            if (float.IsNaN(cs)) return;
            ExtendCruise(cs, 160f);

            float bound = dir > 0 ? zoneMax : zoneMin;
            float s0 = bound - dir * (ClimbReach(bound) + ZoneMargin);

            // 0. Шар впереди: спуск к нему, толчок лапой, подъём — если всё это помещается до разворота.
            float swoopAt = float.NaN;
            if (pearlOk && !pushArmed && PlanSwoop(cs, s0, 0, out var swoop))
            {
                if ((swoop.start - cs) * dir > PlanAhead) swoopAt = swoop.start;
                else
                {
                    if (TrySwoop(swoop)) return;
                    if (PlanSwoop(cs, s0, swoop.right ? -1 : 1, out var other) && TrySwoop(other)) return;    // с другой стороны шара
                    PearlRest(out skipCs, out _, out _); skipDir = dir;      // камни у шара — с этой стороны не подлететь
                }
            }

            // 1. Жёлтая граница впереди: свечка так, чтобы её верх не вышел за границу. Если шар лежит позади разворота —
            //    пике после петли прямо к нему.
            if ((s0 - cs) * dir <= PlanAhead)
            {
                if ((s0 - cs) * dir < 3f) s0 = cs + dir * 3f;
                var up = BuildRun(s0, dir, out var upCs);
                bool stooped = false;
                if (pearlOk && !pushArmed)
                {
                    bool ok = PlanStoop(bound, s0, 0, out var stoop, out var stoopDown, out var stoopCs);
                    if (!ok && stoop.d != 0) ok = PlanStoop(bound, s0, stoop.right ? -1 : 1, out stoop, out stoopDown, out stoopCs);
                    if (ok) { StartManeuver(s0, up, upCs, stoopDown, stoopCs, false, -dir, stoop); stooped = true; }
                }
                if (!stooped)
                {
                    var down = BuildRun(s0, dir, out var downCs);       // пике обратно над тем же участком
                    down.Reverse(); downCs.Reverse();
                    StartManeuver(s0, up, upCs, down, downCs, false, -dir, null);
                }
                return;
            }

            // 2. Красная точка впереди (если сразу за ней не ждёт шар).
            int e = NextExit(cs, passedExit);
            if (e >= 0)
            {
                float r = exits[e], ahead = (r - cs) * dir;
                if (ahead <= PlanAhead)
                {
                    bool pearlNext = !float.IsNaN(swoopAt) && (swoopAt - r) * dir < 15f;
                    if (!pearlNext && ahead > 2f && Random.value <= exitChance && TryExit(r)) return;
                    passedExit = e;
                }
            }

            // 3. Без шара — иногда взмах лапой в воздухе.
            if (!pearlOk && clock >= nextPush)
            {
                float room = Mathf.Min((s0 - cs) * dir, e >= 0 ? (exits[e] - cs) * dir : float.MaxValue);
                if (room > speed * 5f + PlanAhead)
                {
                    PushPearl(rightPaw); rightPaw = !rightPaw;
                    nextPush = clock + Random.Range(calmSeconds.x, calmSeconds.y);
                }
            }
        }

        // Вылет у красной точки r: свечка от r, петля, пике обратно в r — и дальше в ту же сторону (разворот только
        // у жёлтых). Если до границы не хватает места на свечку после возврата, точку пролетает.
        bool TryExit(float r)
        {
            float lo = zoneMin + ZoneMargin, hi = zoneMax - ZoneMargin;
            float need = ClimbReach(r) + ZoneMargin + 15f;
            if (((dir > 0 ? zoneMax : zoneMin) - r) * dir < need) return false;
            var up = BuildRun(r, dir, out var upCs);
            if (!Within(upCs[upCs.Count - 1], lo, hi)) return false;
            var down = BuildRun(r, -dir, out var downCs);          // пике над участком позади r
            if (!Within(downCs[downCs.Count - 1], lo, hi)) return false;
            down.Reverse(); downCs.Reverse();
            StartManeuver(r, up, upCs, down, downCs, true, dir, null);
            return true;
        }

        static bool Within(float x, float lo, float hi) => x >= lo - .5f && x <= hi + .5f;

        int NextExit(float cs, int skip)
        {
            int best = -1; float bestAhead = float.MaxValue;
            for (int i = 0; i < exits.Length; i++)
            {
                float ahead = (exits[i] - cs) * dir;
                if (i == skip || ahead < 0f || ahead >= bestAhead) continue;
                best = i; bestAhead = ahead;
            }
            return best;
        }

        void StartManeuver(float s0, List<Vector3> up, List<float> upCs, List<Vector3> down, List<float> downCs, bool sameDir, int newDir,
            SwoopPlan? push)
        {
            TruncateAtCs(s0);
            int side = Random.value < .5f ? 1 : -1;
            Vector3 tA = (up[up.Count - 1] - up[up.Count - 2]).normalized;
            Vector3 tB = (down[1] - down[0]).normalized;
            var loop = BuildLoop(up[up.Count - 1], tA, down[0], tB, sameDir, side);

            maneuverStartU = track.Count > 0 ? track[track.Count - 1].u : 0f;
            swooping = push.HasValue;
            float climbLen = 0f;
            for (int i = 0; i < up.Count; i++)
            {
                if (i > 0) climbLen += Vector3.Distance(up[i - 1], up[i]);
                Append(up[i], upCs[i], Mathf.Lerp(cruiseSpeed, fastSpeed, S5(climbLen / (loopRadius * 1.5f))));
            }
            for (int i = 1; i < loop.Count; i++) Append(loop[i], float.NaN, fastSpeed * .9f);
            diveStartU = track[track.Count - 1].u;
            float diveLen = 0f, diveTotal = 0f;
            for (int i = 1; i < down.Count; i++) diveTotal += Vector3.Distance(down[i - 1], down[i]);
            for (int i = 1; i < down.Count; i++)
            {
                diveLen += Vector3.Distance(down[i - 1], down[i]);
                float x = diveLen / Mathf.Max(diveTotal, 1f);
                float v = Mathf.Lerp(fastSpeed * 1.1f, cruiseSpeed * 1.3f, S5((x - .55f) / .45f)), low = 0f;
                if (push.HasValue)
                {
                    // пике к шару: тормозит так, чтобы у шара идти со скоростью толчка
                    v = Mathf.Min(fastSpeed * 1.1f, Mathf.Sqrt(pushSpeed * pushSpeed + 2f * StoopBrake * (diveTotal - diveLen)));
                    low = 1f - S5((down[i].y - push.Value.C.y) / 12f);
                }
                Append(down[i], downCs[i], v, low);
            }
            if (push.HasValue)
            {
                var pv = push.Value;
                ArmPush(pv, track[track.Count - 1].u);
                var rise = SwoopPoints(pv, 1f, pv.Lu, out var rx, out var rw, out _);
                AppendSwoopPoints(pv, rise, rx, rw);
            }
            maneuverEndU = track[track.Count - 1].u;
            dir = newDir;
            cursorCs = track[track.Count - 1].cs;
            passedExit = -1;
            ExtendCruise(cursorCs, 160f);
            if (animator) { animator.ResetTrigger(PushRId); animator.ResetTrigger(PushLId); }
        }

        // ------------------------------------------------------------------ свечка / пике вдоль ущелья
        // От точки ущелья s0 в сторону d: тангаж плавно до climbAngle, вверх до верха стен + rimClearance.
        // По ходу — посередине между стенами на этой высоте. Пике = та же кривая задом наперёд.
        List<Vector3> BuildRun(float s0, int d, out List<float> csOut) =>
            BuildRun(s0, d, out csOut, 0f, 0f, climbAngle, loopRadius * 2.15f);

        // h0, lat0 — старт ниже линии полёта и в стороне от неё (пике к шару: от шара назад вверх, потом задом наперёд).
        List<Vector3> BuildRun(float s0, int d, out List<float> csOut, float h0, float lat0, float angle, float pull)
        {
            var pts = new List<Vector3>(); csOut = new List<float>();
            var heights = new List<float>();
            float theta = angle * Mathf.Deg2Rad;
            float l = 0f, h = h0, s = s0, top = 0f;
            for (int guard = 0; guard < 2000; guard++)
            {
                top = Mathf.Max(top, RimAt(s));
                pts.Add(CanyonPoint(s, h)); csOut.Add(s); heights.Add(h);
                if (h >= top + rimClearance) break;
                float th = theta * S5(l / pull);
                s = Mathf.Clamp(s + d * Mathf.Cos(th), 0f, PathLength);
                h += Mathf.Sin(th);
                l += 1f;
            }
            // посередине между стенами на высоте точки (сглажено, у дна — без сдвига)
            int n = pts.Count;
            var shift = new float[n];
            var side = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                side[i] = SideAt(csOut[i]);
                if (heights[i] < 4f) continue;
                bool hl = Physics.Raycast(pts[i], -side[i], out var lh, 80f, canyonLayers, QueryTriggerInteraction.Ignore);
                bool hr = Physics.Raycast(pts[i], side[i], out var rh, 80f, canyonLayers, QueryTriggerInteraction.Ignore);
                float keep = bodyLength * .25f;
                if (hl && hr) shift[i] = Mathf.Clamp((rh.distance - lh.distance) * .5f, -MaxCenterShift, MaxCenterShift);
                else if (hl && lh.distance < keep) shift[i] = keep - lh.distance;
                else if (hr && rh.distance < keep) shift[i] = rh.distance - keep;
            }
            Smooth(shift, 4, 3);
            for (int i = 0; i < n; i++)
                pts[i] += side[i] * (shift[i] * S5(heights[i] / 12f) + lat0 * (1f - S5((heights[i] - h0) / 12f)));
            if (h0 < -1f) LiftAboveGround(pts, 0, 6);
            return pts;
        }

        // Сколько метров вдоль ущелья занимает свечка (оценка без лучей).
        float RunReach(float s0, int d, float h0, float angle, float pull)
        {
            float theta = angle * Mathf.Deg2Rad;
            float l = 0f, h = h0, x = 0f, top = 0f;
            for (int guard = 0; guard < 2000; guard++)
            {
                top = Mathf.Max(top, RimAt(s0 + d * x));
                if (h >= top + rimClearance) break;
                float th = theta * S5(l / pull);
                x += Mathf.Cos(th); h += Mathf.Sin(th); l += 1f;
            }
            return x;
        }

        float ClimbReach(float s0) => RunReach(s0, s0 > (zoneMin + zoneMax) * .5f ? -1 : 1, 0f, climbAngle, loopRadius * 2.15f);

        // Точка линии полёта в ущелье на высоте h над ней (с лёгким вилянием, которое уходит при наборе высоты).
        Vector3 CanyonPoint(float cs, float h)
        {
            Vector3 p = Sample(cs);
            if (weave > 0f && rigReady)
            {
                Vector3 side = SideAt(cs);
                float fade = 1f - S5(h / 20f), a = weave * bodyLength;
                p += side * (a * Mathf.Sin(cs * 2f * Mathf.PI / (bodyLength * 3.3f))) * fade
                     + Vector3.up * (a * .35f * Mathf.Sin(cs * 2f * Mathf.PI / (bodyLength * 2.1f) + 1.3f)) * fade;
            }
            return p + Vector3.up * h;
        }

        Vector3 SideAt(float cs)
        {
            Vector3 tg = Sample(cs + 2f) - Sample(cs - 2f); tg.y = 0f;
            return tg.sqrMagnitude > 1e-6f ? Vector3.Cross(Vector3.up, tg.normalized) : Vector3.right;
        }

        Vector3 FwdAt(float cs, int d)
        {
            Vector3 tg = (Sample(cs + 2f) - Sample(cs - 2f)) * d; tg.y = 0f;
            return tg.sqrMagnitude > 1e-6f ? tg.normalized : Vector3.forward;
        }

        // ------------------------------------------------------------------ жемчужина: спуск и толчок
        // Где должна быть грудь в момент касания: палец лапы (отведённой в сторону на PawSwing) — у шара сзади.
        Vector3 ArmFromChest(bool right)
        {
            var m = right ? Vector3.one : new Vector3(-1f, 1f, 1f);
            Vector3 sh = boneOffset[2] * Vector3.Scale(ShoulderLocal, m) * boneScale;
            Vector3 ef = boneOffset[2] * Vector3.Scale(EffectorLocal, m) * boneScale;
            return sh + Quaternion.AngleAxis(right ? PawSwing : -PawSwing, Vector3.forward) * (ef - sh);
        }

        // Куда основание пальца в момент касания: так, чтобы кончики когтей легли на шар сзади чуть ниже середины,
        // ладонь и запястье — сзади-сверху.
        Vector3 PawTarget(Vector3 ball, Vector3 f) =>
            ball - f * (pearlR + (PawPad + ClawFwd) * boneScale) + Vector3.up * (ClawDown * boneScale - pearlR * .25f);

        bool ContactChest(Vector3 ball, float bcs, int d, bool right, out Vector3 C, out Vector3 f, out float xc)
        {
            f = FwdAt(bcs, d);
            Vector3 E = PawTarget(ball, f);
            C = E - Quaternion.LookRotation(f, Vector3.up) * ArmFromChest(right);
            xc = ClosestFlat(C);
            if (!GroundAt(new Vector3(C.x, Sample(xc).y + 2f, C.z), out float gy)) return false;
            float need = gy + BodyClearBone * boneScale;
            if (need - C.y > .35f) return false;               // грудь пришлась бы над камнем — лапа не дотянется
            C.y = Mathf.Max(C.y, need);
            return Within(xc, zoneMin + ZoneMargin, zoneMax - ZoneMargin);
        }

        // Правой или левой: по очереди, но тело должно пройти мимо шара не впритык к стене.
        bool ChoosePaw(Vector3 bc, float bcs, float blat, int d)
        {
            bool want = !rightPaw;
            if (!FloorBandAt(bc, bcs, blat, out float lo, out float hi)) return want;
            float need = Mathf.Abs(ArmFromChest(true).x) + 1f * boneScale;     // ось тела от шара + полтела с лапами
            bool Fits(bool right)
            {
                float s = (right ? 1f : -1f) * d;      // шар справа от дракона = в сторону +side·d
                return s > 0f ? blat - need >= lo : blat + need <= hi;
            }
            if (Fits(want)) return want;
            return Fits(!want) ? !want : want;
        }

        // paw: 0 — по очереди (ChoosePaw), 1 — правой, -1 — левой.
        bool PlanSwoop(float cs, float s0, int paw, out SwoopPlan p)
        {
            p = default;
            PearlRest(out float bcs, out float blat, out Vector3 bc);
            if ((bcs - cs) * dir < 0f) return false;
            if (!Relaxed && dir == skipDir && Mathf.Abs(bcs - skipCs) < .5f) return false;  // отсюда не подлететь — после разворота
            if (((dir > 0 ? pearlMax : pearlMin) - bcs) * dir < MinRoll) return false;     // некуда катить — толкнёт после разворота
            bool right = paw == 0 ? ChoosePaw(bc, bcs, blat, dir) : paw > 0;
            Vector3 C, f; float xc;
            if (!ContactChest(bc, bcs, dir, right, out C, out f, out xc))
            {
                if (paw != 0) return false;
                right = !right;
                if (!ContactChest(bc, bcs, dir, right, out C, out f, out xc)) return false;
            }
            // подъём после толчка не помещается до разворота — тогда от шара сразу свечкой в разворот (если есть где)
            bool turn = (s0 - (xc + dir * swoopLength * .9f)) * dir < 5f;
            if (turn && ((dir > 0 ? zoneMax : zoneMin) - xc) * dir - ZoneMargin < 40f) return false;
            p = new SwoopPlan { xc = xc, Ld = Mathf.Min(swoopLength, (xc - cs) * dir - 6f), Lu = swoopLength * .9f, C = C, f = f, right = right, d = dir, turn = turn };
            return SetStart(ref p);                                                    // шар близко — спуск короче и круче
        }

        // Начало спуска: с того, что уже впереди на траектории — с линии полёта или (сразу после толчка) с подъёма.
        // Слишком круто для такой глубины — нельзя.
        bool SetStart(ref SwoopPlan p)
        {
            p.start = p.xc - p.d * p.Ld;
            Vector3 from = TrackPos(UAheadAtCs(p.start)), b0 = CanyonPoint(p.start, 0f);
            p.vStart = from.y - b0.y;
            p.latStart = Vector3.Dot(Hz(from - b0), SideAt(p.start));
            float h0 = p.C.y - CanyonPoint(p.xc, 0f).y;
            return p.Ld >= Mathf.Max(12f, Mathf.Abs(h0 - p.vStart) * 1.5f);
        }

        float UAheadAtCs(float cs)
        {
            for (int i = IndexAt(headU); i < track.Count - 1; i++)
            {
                var a = track[i]; var b = track[i + 1];
                if (float.IsNaN(a.cs) || float.IsNaN(b.cs) || (cs - a.cs) * (cs - b.cs) > 0f) continue;
                float span = b.cs - a.cs;
                return Mathf.Lerp(a.u, b.u, Mathf.Abs(span) > 1e-5f ? (cs - a.cs) / span : 0f);
            }
            return track[track.Count - 1].u;
        }

        // доли swoopLength: спуск, подъём
        static readonly (float, float)[] SwoopVariants = { (1f, .9f), (1f, .68f), (.8f, .72f), (.65f, .58f), (.65f, .45f) };

        // Спуск по ущелью к шару, касание, подъём обратно на линию полёта. Камни на пути — не выйдет.
        bool TrySwoop(SwoopPlan p)
        {
            PearlRest(out float bcs, out float blat, out Vector3 bc);
            if (!CanRoll(bc, bcs, blat, p.d, p.right)) return false;            // в камень не толкает
            if (p.turn) return TrySwoopTurn(p);
            // камень на подлёте или подъёме — спуск и подъём короче и круче, тело держится выше до самого шара
            List<Vector3> pts = null; List<float> xs = null, ws = null;
            bool clear = false;
            float ld0 = p.Ld;
            foreach (var (ld, lu) in SwoopVariants)
            {
                var q = p;
                q.Ld = Mathf.Min(ld0, swoopLength * ld); q.Lu = swoopLength * lu;
                if (!SetStart(ref q)) continue;
                pts = SwoopPoints(q, -q.Ld, q.Lu, out xs, out ws, out clear);
                if (clear) { p = q; break; }
            }
            if (!clear) return false;
            TruncateAtCs(p.start);
            maneuverStartU = diveStartU = float.MaxValue;     // без клипов рывка
            swooping = true;
            AppendSwoopPoints(p, pts, xs, ws);
            maneuverEndU = track[track.Count - 1].u;
            cursorCs = track[track.Count - 1].cs;
            ExtendCruise(cursorCs, 160f);
            return true;
        }

        // Шар у самой жёлтой впереди: спуск, толчок — и от шара сразу свечкой вверх, петля-разворот, пике обратно.
        bool TrySwoopTurn(SwoopPlan p)
        {
            float bound = p.d > 0 ? zoneMax : zoneMin;
            float room = (bound - p.xc) * p.d - ZoneMargin;
            Vector3 baseC = CanyonPoint(p.xc, 0f);
            float h0 = p.C.y - baseC.y, lat0 = Vector3.Dot(Hz(p.C - baseC), SideAt(p.xc));
            List<Vector3> desc = null; List<float> xs = null, ws = null;
            bool clear = false;
            float ld0 = p.Ld;
            foreach (var (ld, _) in SwoopVariants)
            {
                var q = p;
                q.Ld = Mathf.Min(ld0, swoopLength * ld);
                if (!SetStart(ref q)) continue;
                desc = SwoopPoints(q, -q.Ld, 0f, out xs, out ws, out clear);
                if (clear) { p = q; break; }
            }
            if (!clear) return false;
            var shapes = new List<(float th, float pull)>();
            foreach (float k in new[] { 2.15f, 1.7f, 1.35f, 1.1f, .9f })
                for (float th = climbAngle; th <= 88.5f; th += 4f) shapes.Add((th, loopRadius * k));
            shapes.Sort((x, y) => (x.th / x.pull).CompareTo(y.th / y.pull));
            List<Vector3> up = null; List<float> upCs = null;
            int tried = 0;
            foreach (var (th, pull) in shapes)
            {
                if (RunReach(p.xc, p.d, h0, th, pull) > room) continue;
                if (++tried > 6) break;
                var run = BuildRun(p.xc, p.d, out var rc, h0, lat0, th, pull);
                if (!Within(rc[rc.Count - 1], zoneMin + ZoneMargin, zoneMax - ZoneMargin)) continue;
                if (!TrailClear(run.Where(q => q.y - p.C.y < 15f).ToList(), null, 0f)) continue;
                up = run; upCs = rc;
                break;
            }
            if (up == null) return false;
            var down = BuildRun(p.xc, p.d, out var downCs);          // пике обратно над тем же участком на линию полёта
            down.Reverse(); downCs.Reverse();

            TruncateAtCs(p.start);
            swooping = false;
            AppendSwoopPoints(p, desc, xs, ws);                      // касание — последняя точка спуска
            maneuverStartU = track[track.Count - 1].u + 3f;          // лапы поджимает уже после толчка
            float climbLen = 0f;
            for (int i = 1; i < up.Count; i++)
            {
                climbLen += Vector3.Distance(up[i - 1], up[i]);
                Append(up[i], upCs[i], Mathf.Lerp(pushSpeed, fastSpeed, S5(climbLen / (loopRadius * 1.5f))), 1f - S5((up[i].y - p.C.y) / 12f));
            }
            Vector3 tA = (up[up.Count - 1] - up[up.Count - 2]).normalized, tB = (down[1] - down[0]).normalized;
            var loop = BuildLoop(up[up.Count - 1], tA, down[0], tB, false, Random.value < .5f ? 1 : -1);
            for (int i = 1; i < loop.Count; i++) Append(loop[i], float.NaN, fastSpeed * .9f);
            diveStartU = track[track.Count - 1].u;
            float diveLen = 0f, diveTotal = 0f;
            for (int i = 1; i < down.Count; i++) diveTotal += Vector3.Distance(down[i - 1], down[i]);
            for (int i = 1; i < down.Count; i++)
            {
                diveLen += Vector3.Distance(down[i - 1], down[i]);
                float x = diveLen / Mathf.Max(diveTotal, 1f);
                Append(down[i], downCs[i], Mathf.Lerp(fastSpeed * 1.1f, cruiseSpeed * 1.3f, S5((x - .55f) / .45f)));
            }
            maneuverEndU = track[track.Count - 1].u;
            dir = -p.d;
            cursorCs = downCs[downCs.Count - 1];
            passedExit = -1;
            ExtendCruise(cursorCs, 160f);
            return true;
        }

        // Шар позади разворота у жёлтой: пике после петли сразу к нему. Крутизна и выход из пике — какие поместятся
        // между границей и шаром.
        bool PlanStoop(float bound, float s0, int paw, out SwoopPlan p, out List<Vector3> down, out List<float> downCs)
        {
            p = default; down = null; downCs = null;
            int nd = -dir;
            PearlRest(out float bcs, out float blat, out Vector3 bc);
            float fromBound = (bcs - bound) * nd;
            if (fromBound < 0f || fromBound > (s0 - bound) * nd + swoopLength + 8f) return false;
            if (((nd > 0 ? pearlMax : pearlMin) - bcs) * nd < MinRoll) return false;
            bool right = paw == 0 ? ChoosePaw(bc, bcs, blat, nd) : paw > 0;
            Vector3 C, f; float xc;
            if (!ContactChest(bc, bcs, nd, right, out C, out f, out xc))
            {
                if (paw != 0) return false;
                right = !right;
                if (!ContactChest(bc, bcs, nd, right, out C, out f, out xc)) return false;
            }
            p.right = right; p.d = nd;
            if (!CanRoll(bc, bcs, blat, nd, right)) return false;   // в камень не толкает
            if (!Within(xc + nd * swoopLength * .9f, zoneMin + ZoneMargin, zoneMax - ZoneMargin)) return false;
            float room = (xc - bound) * nd - ZoneMargin;
            Vector3 baseC = CanyonPoint(xc, 0f);
            float h0 = C.y - baseC.y, lat0 = Vector3.Dot(Hz(C - baseC), SideAt(xc));
            // формы пике от пологой к крутой: первая, что помещается до границы и не задевает камни у дна
            var shapes = new List<(float th, float pull)>();
            foreach (float k in new[] { 2.15f, 1.7f, 1.35f, 1.1f, .9f })
                for (float th = climbAngle; th <= 88.5f; th += 4f) shapes.Add((th, loopRadius * k));
            shapes.Sort((x, y) => (x.th / x.pull).CompareTo(y.th / y.pull));
            p = new SwoopPlan { xc = xc, Ld = swoopLength, Lu = swoopLength * .9f, C = C, f = f, right = right, d = nd };
            // подъём после толчка (можно круче)
            float lu = 0f;
            foreach (float l in new[] { swoopLength * .9f, swoopLength * .7f, swoopLength * .5f })
            {
                p.Lu = l;
                SwoopPoints(p, 1f, l, out _, out _, out bool clear);
                if (clear) { lu = l; break; }
            }
            if (lu <= 0f) return false;
            int tried = 0;
            foreach (var (th, pull) in shapes)
            {
                if (RunReach(xc, -nd, h0, th, pull) > room) continue;
                if (++tried > 6) break;
                var run = BuildRun(xc, -nd, out var runCs, h0, lat0, th, pull);
                if (!Within(runCs[runCs.Count - 1], zoneMin + ZoneMargin, zoneMax - ZoneMargin)) continue;
                if (!TrailClear(run.Where(q => q.y - C.y < 15f).ToList(), null, 0f)) continue;
                run.Reverse(); runCs.Reverse();
                run[run.Count - 1] = C;
                down = run; downCs = runCs;
                p.start = downCs[0];
                return true;
            }
            return false;
        }

        // Точки траектории для x от x0 до x1 (м вдоль ущелья от касания груди, по ходу полёта). x = 0 — точно грудь у шара.
        // clear — у шара (±15–18 м) не пришлось поднимать над камнями и ничего не задевает.
        List<Vector3> SwoopPoints(SwoopPlan p, float x0, float x1, out List<float> xs, out List<float> ws, out bool clear)
        {
            float Ld = p.Ld, Lu = p.Lu;
            Vector3 baseC = CanyonPoint(p.xc, 0f);
            float h0 = p.C.y - baseC.y, latC = Vector3.Dot(Hz(p.C - baseC), SideAt(p.xc));
            var pts = new List<Vector3>(); xs = new List<float>(); ws = new List<float>();
            int keepFrom = -1, keepTo = -1;
            for (float x = x0; x < x1 + .5f; x += 1f)
            {
                float cs = p.xc + p.d * x;
                float wv = x < 0f ? S5(1f + x / Ld) : 1f - S5(x / Lu);
                float wl = x < 0f ? S5((x + Ld) / (Ld * .7f)) : 1f - S5((x - 3f) / (Lu - 3f));
                float vOff = x < 0f ? Mathf.Lerp(p.vStart, h0, wv) : h0 * wv;
                float lOff = x < 0f ? Mathf.Lerp(p.latStart, latC, wl) : latC * wl;
                pts.Add(Mathf.Abs(x) < .5f ? p.C : CanyonPoint(cs, 0f) + Vector3.up * vOff + SideAt(cs) * lOff);
                xs.Add(x); ws.Add(Mathf.Clamp01(vOff / Mathf.Min(h0, -1f)));          // насколько низко (0 — линия полёта, 1 — у шара)
                if (x > -6.5f && x < 2.5f) { if (keepFrom < 0) keepFrom = pts.Count - 1; keepTo = pts.Count - 1; }
            }
            var lift = LiftAboveGround(pts, keepFrom, keepTo);
            float before = 0f, after = 0f;
            for (int i = 0; i < pts.Count; i++)
            {
                if (xs[i] < 0f && xs[i] > -15f) before = Mathf.Max(before, lift[i]);
                if (xs[i] > 0f && xs[i] < 15f) after = Mathf.Max(after, lift[i]);
            }
            // до касания — почти без подъёма (иначе рука не дотянется), после — можно перелететь камень;
            // большой камень где угодно на пути — горб, тело срежет угол: не годится
            clear = before < 1.2f && after < 3f && lift.Max() < 3f && TrailClear(pts, xs, 18f);
            return pts;
        }

        void AppendSwoopPoints(SwoopPlan p, List<Vector3> pts, List<float> xs, List<float> ws)
        {
            for (int i = 0; i < pts.Count; i++)
            {
                float v = Mathf.Lerp(cruiseSpeed, Mathf.Min(pushSpeed, cruiseSpeed), ws[i]);
                Append(pts[i], p.xc + p.d * xs[i], v, ws[i]);
                if (Mathf.Abs(xs[i]) < .5f) ArmPush(p, track[track.Count - 1].u);
            }
        }

        // Тело не ниже BodyClear над землёй (кроме точек около касания — там высоту задаёт шар). Возвращает подъём точек.
        float[] LiftAboveGround(List<Vector3> pts, int keepFrom, int keepTo)
        {
            float clear = BodyClearBone * boneScale * 1.2f;
            int n = pts.Count;
            var lift = new float[n];
            bool any = false;
            for (int i = 0; i < n; i++)
            {
                if (i >= keepFrom && i <= keepTo) continue;
                if (!GroundAt(pts[i] + Vector3.up * 25f, out float gy) || pts[i].y >= gy + clear) continue;
                lift[i] = gy + clear - pts[i].y; any = true;
            }
            if (!any) return lift;
            var m = new float[n];
            for (int i = 0; i < n; i++)
                for (int k = Mathf.Max(0, i - 4); k <= Mathf.Min(n - 1, i + 4); k++) m[i] = Mathf.Max(m[i], lift[k]);
            Smooth(m, 4, 2);
            for (int i = 0; i < n; i++)
            {
                if (i >= keepFrom && i <= keepTo) { lift[i] = 0f; continue; }
                lift[i] = Mathf.Max(m[i], lift[i]);
                pts[i] += Vector3.up * lift[i];
            }
            return lift;
        }

        // Тело (сфера ~0.9 м по траектории головы) не задевает ничего, кроме шара.
        bool TrailClear(List<Vector3> pts, List<float> xs, float range)
        {
            float rad = (Relaxed ? .36f : .48f) * boneScale;     // толщина груди ~1 м; давно не толкал — впритык к камням тоже можно
            for (int i = 0; i < pts.Count; i++)
            {
                if (xs != null && Mathf.Abs(xs[i]) > range) continue;
                int n = Physics.OverlapSphereNonAlloc(pts[i], rad, overlapBuf, canyonLayers, QueryTriggerInteraction.Ignore);
                for (int k = 0; k < n; k++)
                {
                    var col = overlapBuf[k];
                    if (col is CharacterController || System.Array.IndexOf(pearlCols, col) >= 0) continue;
                    return false;
                }
            }
            return true;
        }

        void ArmPush(SwoopPlan p, float u)
        {
            pushArmed = true; pushTriggered = pushDone = false;
            pushU = u; pushRight = p.right; pushDir = p.d; pushFwd = p.f;
        }

        // Клип толчка запускается заранее (касание через PushLead), скорость подгоняется так, чтобы грудь пришла к точке
        // касания ровно в этот момент; руку довершает IK.
        void PushTiming(float dt)
        {
            if (!pushArmed) return;
            float chestU = headU - chestArc;
            if (!pushTriggered)
            {
                if (chestU > pushU + 3f) { pushArmed = false; return; }
                if (pushU - chestU < 120f && PlannedTime(chestU, pushU) <= PushLead + dt)
                {
                    pushTriggered = true; pushClock = clock; rightPaw = pushRight;
                    if (animator && animator.runtimeAnimatorController && Application.isPlaying)
                    {
                        animator.ResetTrigger(PushRId); animator.ResetTrigger(PushLId);
                        animator.CrossFadeInFixedTime(pushRight ? PushStateRId : PushStateLId, PushFade, 0, 0f);
                    }
                }
                return;
            }
            float tau = clock - pushClock;
            if (!pushDone)
            {
                // по плановому графику скорости (торможение в пике), растянутому так, чтобы успеть точно к касанию
                float left = PushLead - tau;
                if (left > .04f)
                {
                    float k = Mathf.Clamp(PlannedTime(chestU, pushU) / left, .5f, 2f);
                    speed = Mathf.MoveTowards(speed, Mathf.Max(TrackSpeed(headU) * k, .3f), 16f * dt);
                    speedVel = 0f;
                }
                if (tau > PushLead + .25f) DoPush();          // событие клипа не пришло (нет аниматора) — толкнуть самим
            }
            else if (tau > 3.2f) pushArmed = pushTriggered = false;
        }

        // Сколько секунд груди от from до to по плановым скоростям траектории (скорость задана там, где голова).
        float PlannedTime(float from, float to)
        {
            float t = 0f;
            for (float u = from; u < to; u += .5f)
                t += Mathf.Min(.5f, to - u) / Mathf.Max(TrackSpeed(u + chestArc), .5f);
            return t;
        }

        void DoPush()
        {
            if (pushDone) return;
            pushDone = true; pushCount++; lastPushClock = clock;
            pushContact = pearlC;
            skipCs = float.NaN;
            StartRoll(pushDir, pushRight);
            lookPearlUntil = clock + 3f;
            pearlPushed.Invoke(pushRight);
        }

        // Рука к шару: двухзвенный IK плечо-предплечье поверх клипа, вес — вокруг кадра касания.
        void PawIK()
        {
            if (!pushArmed || !pushTriggered) return;
            int k = pushRight ? 0 : 1;
            Transform a = armUpper[k], b = armFore[k], c = armPaw[k], e = armEff[k];
            if (!a || !b || !c || !e) return;
            float tau = clock - pushClock;
            float w = S5((tau - 1.05f) / .6f) * (1f - S5((tau - 1.9f) / .55f));
            if (w <= 1e-3f) return;
            pushTarget = pushDone
                ? PawTarget(pushContact, pushFwd) + pushFwd * ((tau - PushLead) * 3f)          // проводка за шаром
                : PawTarget(pearlC, pushFwd) - pushFwd * (Mathf.Max(0f, PushLead - tau) * 2.5f);   // замах сзади к шару
            Quaternion la = a.localRotation, lb = b.localRotation;
            for (int it = 0; it < 2; it++) TwoBoneIK(a, b, c, pushTarget - (e.position - c.position));   // 2-й раз — палец после поворота кисти
            a.localRotation = Quaternion.Slerp(la, a.localRotation, w);
            b.localRotation = Quaternion.Slerp(lb, b.localRotation, w);
        }

        static void TwoBoneIK(Transform a, Transform b, Transform c, Vector3 target)
        {
            Vector3 pa = a.position, pb = b.position, pc = c.position;
            float lab = (pb - pa).magnitude, lbc = (pc - pb).magnitude;
            if (lab < 1e-4f || lbc < 1e-4f) return;
            float lat = Mathf.Clamp((target - pa).magnitude, Mathf.Abs(lab - lbc) + 1e-3f, (lab + lbc) * .998f);
            Vector3 n = Vector3.Cross(pa - pb, pc - pb);
            if (n.sqrMagnitude < 1e-10f) return;
            n.Normalize();
            float cur = Vector3.Angle(pa - pb, pc - pb);
            float want = Mathf.Acos(Mathf.Clamp((lab * lab + lbc * lbc - lat * lat) / (2f * lab * lbc), -1f, 1f)) * Mathf.Rad2Deg;
            b.rotation = Quaternion.AngleAxis(want - cur, n) * b.rotation;
            a.rotation = Quaternion.FromToRotation(c.position - pa, target - pa) * a.rotation;
        }

        // ------------------------------------------------------------------ жемчужина: шар
        void FindPearl()
        {
            pearlT = pearl;
            if (!pearlT && !string.IsNullOrEmpty(pearlTag))
            {
                try { var go = GameObject.FindWithTag(pearlTag); if (go) pearlT = go.transform; }
                catch (UnityException) { }     // тега нет в проекте
            }
            rolling = false;
            pearlOk = pearlT && points.Count > 1 && rigReady;
            if (!pearlOk) return;
            pearlCols = pearlT.GetComponentsInChildren<Collider>(true);
            var rends = pearlT.GetComponentsInChildren<Renderer>();
            var bounds = rends.Length > 0 ? rends[0].bounds : new Bounds(pearlT.position, Vector3.one * .8f);
            foreach (var r in rends) bounds.Encapsulate(r.bounds);
            pearlR = Mathf.Max(.05f, Mathf.Max(bounds.extents.x, Mathf.Max(bounds.extents.y, bounds.extents.z)));
            pearlLocalCenter = pearlT.InverseTransformPoint(bounds.center);
            pearlC = bounds.center;
            ToCanyon(pearlC, out pearlCs, out pearlLat);
            pearlLastPos = pearlT.position;
            if (Application.isPlaying)
            {
                // двигаем сами — коллайдер шара пусть едет как кинематическое тело
                var rb = pearlT.GetComponent<Rigidbody>();
                if (!rb) rb = pearlT.gameObject.AddComponent<Rigidbody>();
                rb.isKinematic = true;
            }
        }

        // Где шар остановится (катится — конец качения, лежит — где лежит).
        void PearlRest(out float cs, out float lat, out Vector3 c)
        {
            if (!rolling) { cs = pearlCs; lat = pearlLat; c = pearlC; return; }
            cs = rollCs0 + rollDir * rollD; lat = rollLats[rollLats.Length - 1];
            c = Sample(cs) + SideAt(cs) * lat;
            c.y = GroundAt(c, out float gy) ? gy + pearlR : pearlC.y;
        }

        // Путь качения вдоль ущелья: чуть в сторону от тела дракона, дальше — по свободной полосе дна (камни и стены
        // обходит, как шар по вогнутому дну). Загорожено — остановится раньше.
        void StartRoll(int d, bool right)
        {
            ToCanyon(pearlC, out pearlCs, out pearlLat);
            // уводит в сторону толкнувшей лапы; если упирается в камни — пробует покороче и в другую сторону
            float want = Random.Range(pearlRoll.x, pearlRoll.y), D = -1f;
            float[] lat = null;
            foreach (float len in new[] { want, pearlRoll.x })
                foreach (bool side in new[] { right, !right })
                {
                    if (D >= len * .9f) break;
                    float Dc = PlanRoll(pearlC, pearlCs, pearlLat, d, len, side, out var lc);
                    if (Dc > D * 1.15f || lat == null) { D = Dc; lat = lc; }
                }
            if (D < .05f) { rolling = false; return; }
            var raw = (float[])lat.Clone();
            Smooth(lat, 1, 3);
            for (int i = 0; i < lat.Length; i++) lat[i] = Mathf.Lerp(raw[0], lat[i], S5(i / 1.5f));    // старт ровно с места шара
            Vector3 RollPoint(int i)
            {
                float cs = pearlCs + d * D * i / Mathf.Max(lat.Length - 1, 1);
                Vector3 q = Sample(cs) + SideAt(cs) * lat[i];
                q.y = GroundAt(q, out float gy) ? gy + pearlR : pearlC.y;
                return q;
            }
            for (int i = 1; i < lat.Length; i++)          // сглаживание срезало угол о камень — вернуть как было
                if (!RollFree(RollPoint(i - 1), RollPoint(i))) lat[i] = raw[i];
            rollLats = lat; rollD = D; rollCs0 = pearlCs; rollDir = d;
            rollA = Mathf.Max(pearlFriction, .2f);
            rollV0 = Mathf.Sqrt(2f * rollA * D);
            rollT = 0f;
            rolling = D > .05f;
        }

        // Сетка поперёк ущелья для пути качения: смещения от -GridHalf до +GridHalf (м вдоль SideAt от линии полёта).
        const float GridHalf = 16f, GridStep = .5f;
        const int GridN = 65;
        readonly float[] gridGround = new float[GridN], gridSorted = new float[GridN];

        // Где поперёк ущелья в точке cs может катиться шар: земля не выше дна + 0.25 м (не камень), с запасом на радиус.
        // Лучи вниз с высоты линии полёта — она в открытом воздухе между стенами.
        void FreeRow(float cs, bool[] row)
        {
            Vector3 c = Sample(cs), side = SideAt(cs);
            float wl = Cast(c, -side, 40f, out var lh) ? lh.distance - .3f : GridHalf;
            float wr = Cast(c, side, 40f, out var rh) ? rh.distance - .3f : GridHalf;
            for (int g = 0; g < GridN; g++)
            {
                float o = -GridHalf + g * GridStep;
                gridGround[g] = o >= -wl && o <= wr && GroundAt(c + side * o, out float y) ? y : float.PositiveInfinity;
                gridSorted[g] = gridGround[g];
            }
            System.Array.Sort(gridSorted);
            int finite = 0;
            while (finite < GridN && !float.IsInfinity(gridSorted[finite])) finite++;
            float floor = finite > 0 ? gridSorted[finite * 15 / 100] : float.MaxValue;   // дно — не самая глубокая ямка
            int m = Mathf.CeilToInt((pearlR + .1f) / GridStep);
            for (int g = 0; g < GridN; g++)
            {
                bool ok = g >= m && g < GridN - m;
                for (int k = Mathf.Max(0, g - m); k <= Mathf.Min(GridN - 1, g + m) && ok; k++) ok = gridGround[k] <= floor + .25f;
                row[g] = ok;
            }
        }

        // Путь качения на D метров в сторону d (не дальше коридора у жёлтых): по сетке поперёк дна ищется путь, который
        // уходит дальше всех, с наименьшими вилянием и отходом от желаемого (чуть в сторону толкнувшей лапы, к середине).
        // Потом проверка настоящим шаром. Упёрлось в камень — остановится метров за 20 до него.
        // Возвращает, сколько метров на самом деле прокатится.
        float PlanRoll(Vector3 from, float cs0, float lat0, int d, float D, bool right, out float[] lats)
        {
            float room = ((d > 0 ? pearlMax : pearlMin) - cs0) * d;
            D = Mathf.Min(D, Mathf.Max(room, 3f));
            // путь просчитывается на 24 м дальше: камень сразу за местом остановки — остановится раньше, метров за 20
            // до него (иначе к шару не подлететь ни с одной стороны)
            float Dx = Mathf.Min(D + 24f, Mathf.Max(room, D));
            float outward = (right ? 1f : -1f) * d;
            int n = Mathf.Max(2, Mathf.CeilToInt(Dx / RollStep) + 1);
            float step = Dx / (n - 1);
            int nD = Mathf.Clamp(Mathf.RoundToInt(D / step), 1, n - 1);
            const int MaxShift = 8;                              // поперёк не больше 4 м за шаг
            var rows = new bool[n][];
            var alt = new bool[GridN];
            for (int i = 1; i < n; i++)
            {
                rows[i] = new bool[GridN];
                float cs = cs0 + d * step * i;
                FreeRow(cs, rows[i]);
                foreach (float o in new[] { -1.2f, 1.2f })           // строка могла лечь ровно на гребень камней — рядом проход
                {
                    FreeRow(cs + o, alt);
                    for (int g = 0; g < GridN; g++) rows[i][g] |= alt[g];
                }
            }
            var cost = new float[n, GridN];
            var back = new int[n, GridN];
            var clear = new float[GridN];
            int g0 = Mathf.Clamp(Mathf.RoundToInt((lat0 + GridHalf) / GridStep), 0, GridN - 1);
            float[] lat = { lat0 };
            int last = 0, reach = 0;
            for (int attempt = 0; attempt < 40; attempt++)
            {
                for (int i = 0; i < n; i++)
                    for (int g = 0; g < GridN; g++) cost[i, g] = float.PositiveInfinity;
                cost[0, g0] = 0f;
                reach = 0;
                for (int i = 1; i < n; i++)
                {
                    float x = i / (float)(n - 1);
                    float want = lat0 * (1f - .6f * x) + outward * .5f * S5(x * 2.5f);
                    // до ближайшего камня/стены поперёк, м — у края дна дороже: шар держится середины, дракону есть где пройти
                    var row = rows[i];
                    float run = 0f;
                    for (int g = 0; g < GridN; g++) { run = row[g] ? run + GridStep : 0f; clear[g] = run; }
                    run = 0f;
                    for (int g = GridN - 1; g >= 0; g--) { run = row[g] ? run + GridStep : 0f; clear[g] = Mathf.Min(clear[g], run); }
                    bool any = false;
                    for (int g = 0; g < GridN; g++)
                    {
                        if (!row[g]) continue;
                        float l = -GridHalf + g * GridStep, best = float.PositiveInfinity;
                        int bi = -1;
                        for (int k = Mathf.Max(0, g - MaxShift); k <= Mathf.Min(GridN - 1, g + MaxShift); k++)
                        {
                            float c = cost[i - 1, k];
                            if (float.IsInfinity(c)) continue;
                            c += Mathf.Abs(k - g) * GridStep + .3f * Mathf.Abs(l - want) + 1.2f * Mathf.Max(0f, 5.5f - clear[g]);
                            if (c < best) { best = c; bi = k; }
                        }
                        if (bi < 0) continue;
                        if (i == nD) best += 4f * Mathf.Max(0f, 7f - clear[g]);     // где остановится — подальше от стен
                        cost[i, g] = best; back[i, g] = bi; any = true;
                    }
                    if (!any) break;
                    reach = i;
                }
                int gEnd = g0;
                float bestEnd = float.PositiveInfinity;
                for (int g = 0; g < GridN; g++) if (cost[reach, g] < bestEnd) { bestEnd = cost[reach, g]; gEnd = g; }
                lat = new float[reach + 1];
                var cell = new int[reach + 1];
                for (int i = reach, g = gEnd; i >= 0; i--) { cell[i] = g; lat[i] = -GridHalf + g * GridStep; if (i > 0) g = back[i, g]; }
                lat[0] = lat0;
                // проверка настоящим шаром; упёрлось (камень нависает над дном) — это место занято, искать заново
                last = reach;
                int bad = -1;
                Vector3 P = from;
                for (int i = 1; i <= reach; i++)
                {
                    float cs = cs0 + d * step * i;
                    Vector3 Q = Sample(cs) + SideAt(cs) * lat[i];
                    if (!GroundAt(Q, out float gy)) { bad = i; break; }
                    Q.y = gy + pearlR;
                    if (Q.y - P.y > 1.2f || !RollFree(P, Q)) { bad = i; break; }
                    P = Q;
                }
                if (bad < 0) break;
                last = bad - 1;
                for (int k = Mathf.Max(0, cell[bad] - 1); k <= Mathf.Min(GridN - 1, cell[bad] + 1); k++) rows[bad][k] = false;
                if (bad > 1 && (attempt & 1) == 1)                // мешает, видимо, откуда заходит — и предыдущее место
                    for (int k = Mathf.Max(0, cell[bad - 1] - 1); k <= Mathf.Min(GridN - 1, cell[bad - 1] + 1); k++) rows[bad - 1][k] = false;
            }
            if (last < n - 1) last = Mathf.Max(last - 5, Mathf.Min(last, 1));
            last = Mathf.Min(last, nD);
            if (last < 1) { lats = new[] { lat0 }; return 0f; }
            if (last + 1 < lat.Length) System.Array.Resize(ref lat, last + 1);
            lats = lat;
            return step * last;
        }

        // Шар вообще покатится в сторону d (хотя бы MinRoll, можно в обход)? Иначе толкать в ту сторону незачем.
        bool CanRoll(Vector3 from, float cs0, float lat0, int d, bool right) =>
            PlanRoll(from, cs0, lat0, d, pearlRoll.x, right, out _) >= MinRoll ||
            PlanRoll(from, cs0, lat0, d, pearlRoll.x, !right, out _) >= MinRoll;

        // Прокатится ли шар из a в b (сфера чуть больше шара, поднятая над землёй: мелкие кочки не мешают).
        bool RollFree(Vector3 a, Vector3 b) => RollFree(a, b, out _);

        bool RollFree(Vector3 a, Vector3 b, out RaycastHit blocker)
        {
            blocker = default;
            Vector3 lift = Vector3.up * .5f, d = b - a;
            float len = d.magnitude;
            if (len < 1e-4f) return true;
            int n = Physics.SphereCastNonAlloc(a + lift, pearlR + .25f, d / len, rayHits, len, canyonLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var col = rayHits[i].collider;
                if (col is CharacterController || System.Array.IndexOf(pearlCols, col) >= 0) continue;
                blocker = rayHits[i];
                return false;
            }
            return true;
        }

        // Смещение поперёк на расстоянии s от старта качения (Catmull-Rom по точкам через RollStep).
        float RollLatAt(float s)
        {
            int n = rollLats.Length;
            if (n == 1) return rollLats[0];
            float x = Mathf.Clamp(s / Mathf.Max(rollD, 1e-3f), 0f, 1f) * (n - 1);
            int i = Mathf.Min((int)x, n - 2);
            float t = x - i;
            float p0 = rollLats[Mathf.Max(i - 1, 0)], p1 = rollLats[i], p2 = rollLats[i + 1], p3 = rollLats[Mathf.Min(i + 2, n - 1)];
            return .5f * (2f * p1 + (p2 - p0) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t * t + (3f * p1 - p0 - 3f * p2 + p3) * t * t * t);
        }

        void PearlStep(float dt)
        {
            if (!pearlOk) return;
            if (!pearlT) { pearlOk = false; return; }
            if (!rolling)
            {
                if ((pearlT.position - pearlLastPos).sqrMagnitude > .01f)        // передвинули руками / игрок
                {
                    pearlC = pearlT.TransformPoint(pearlLocalCenter);
                    ToCanyon(pearlC, out pearlCs, out pearlLat);
                    pearlLastPos = pearlT.position;
                    skipCs = float.NaN;
                }
                return;
            }
            rollT += dt;
            float tStop = rollV0 / rollA, t = Mathf.Min(rollT, tStop);
            float s = Mathf.Min(rollD, rollV0 * t - .5f * rollA * t * t);
            if (rollT >= tStop) { s = rollD; rolling = false; }
            float cs = rollCs0 + rollDir * s, lat = RollLatAt(s);
            Vector3 c = Sample(cs) + SideAt(cs) * lat;
            float yT = GroundAt(c, out float gy) ? gy + pearlR : pearlC.y;
            c.y = Mathf.SmoothDamp(pearlC.y, yT, ref pearlYVel, .08f, 50f, Mathf.Max(dt, 1e-4f));
            Vector3 mv = c - pearlC; mv.y = 0f;
            float dist = mv.magnitude;
            pearlC = c; pearlCs = cs; pearlLat = lat;
            Quaternion rot = pearlT.rotation;
            if (dist > 1e-5f) rot = Quaternion.AngleAxis(dist / pearlR * Mathf.Rad2Deg, Vector3.Cross(Vector3.up, mv / dist)) * rot;
            pearlT.SetPositionAndRotation(pearlC - rot * Vector3.Scale(pearlLocalCenter, pearlT.lossyScale), rot);
            pearlLastPos = pearlT.position;
        }

        // Луч, не видящий сам шар и капсулы персонажей.
        bool Cast(Vector3 o, Vector3 d, float max, out RaycastHit hit)
        {
            hit = default;
            int n = Physics.RaycastNonAlloc(o, d, rayHits, max, canyonLayers, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                var col = rayHits[i].collider;
                if (col is CharacterController || System.Array.IndexOf(pearlCols, col) >= 0) continue;
                if (rayHits[i].distance < best) { best = rayHits[i].distance; hit = rayHits[i]; }
            }
            return best < float.MaxValue;
        }

        bool GroundAt(Vector3 above, out float y)
        {
            bool ok = Cast(above, Vector3.down, 400f, out var h);
            y = ok ? h.point.y : 0f;
            return ok;
        }

        // Свободно поперёк ущелья от шара (центр ball, смещение lat): от lo до hi (м вдоль SideAt от линии полёта),
        // на высоте 1 м над шаром — там, где пройдёт тело.
        bool FloorBandAt(Vector3 ball, float cs, float lat, out float lo, out float hi)
        {
            lo = lat - 30f; hi = lat + 30f;
            Vector3 side = SideAt(cs), p = ball + Vector3.up * 1f;
            if (Cast(p, -side, 30f, out var lh)) lo = lat - lh.distance;
            if (Cast(p, side, 30f, out var rh)) hi = lat + rh.distance;
            return true;
        }

        void ToCanyon(Vector3 p, out float cs, out float lat)
        {
            cs = ClosestFlat(p);
            lat = Vector3.Dot(Hz(p - Sample(cs)), SideAt(cs));
        }

        // ------------------------------------------------------------------ петля над ущельем
        // Из верха свечки A (направление tA) в верх пике B (tB). Обратно — петля-«капля» в сторону side;
        // в ту же сторону — «овал»: разворот, назад вдоль ущелья, разворот. Курс и тангаж — плавные профили по длине,
        // длины подбираются так, чтобы петля точно пришла в B (Ньютон), остаток — плавным сдвигом.
        List<Vector3> BuildLoop(Vector3 A, Vector3 tA, Vector3 B, Vector3 tB, bool sameDir, int side)
        {
            Vector3 th = Flat(tA), n = Vector3.Cross(Vector3.up, th) * side;
            float thA = Mathf.Asin(Mathf.Clamp(tA.y, -1f, 1f)), thB = Mathf.Asin(Mathf.Clamp(tB.y, -1f, 1f));
            Vector3 tBh = Flat(tB);
            float ang = Mathf.Atan2(Vector3.Dot(tBh, n), Vector3.Dot(tBh, th));
            float dpsi = sameDir ? WrapPi(ang) : WrapPi(ang - Mathf.PI);
            Vector3 rel = B - A, target = new Vector3(Vector3.Dot(rel, th), rel.y, Vector3.Dot(rel, n));
            float R = loopRadius, pull = R * 2.4f, peak = ShapePeak;
            var segs = new Seg[3];
            System.Func<Vector3, Vector3> f;
            Vector3 v, scale;
            if (!sameDir)
            {
                f = q =>
                {
                    float a = q.x, l1 = Mathf.Max(q.y, 2f), big = Mathf.PI + 2f * a + dpsi;
                    segs[0] = new Seg(l1, -a); segs[1] = new Seg(Mathf.Max(big, .5f) * R * peak, big); segs[2] = new Seg(l1, -a);
                    return Integrate(segs, thA, thB, q.z, pull, null) - target;
                };
                v = new Vector3(.57f, 33.4f * R / 20f, 0f); scale = new Vector3(.5f, R, .2f);
            }
            else
            {
                float lt = Mathf.PI * R * peak;
                f = q =>
                {
                    segs[0] = new Seg(lt, Mathf.PI + q.y); segs[1] = new Seg(Mathf.Max(q.x, 1f), 0f); segs[2] = new Seg(lt, Mathf.PI - q.y + dpsi);
                    return Integrate(segs, thA, thB, q.z, pull, null) - target;
                };
                v = new Vector3(Mathf.Max(-target.x, 5f), 0f, 0f); scale = new Vector3(R * 1.5f, .3f, .2f);
            }
            Newton(f, ref v, scale);
            f(v);
            var local = new List<Vector3>(LoopSteps + 1);
            Vector3 err = target - Integrate(segs, thA, thB, v.z, pull, local);
            var pts = new List<Vector3>(local.Count);
            for (int i = 0; i < local.Count; i++)
            {
                Vector3 q = local[i] + err * S5(i / (float)(local.Count - 1));
                pts.Add(A + th * q.x + Vector3.up * q.y + n * q.z);
            }
            return pts;
        }

        // Локально: x — вперёд (курс 0), y — вверх, z — в сторону петли. Тангаж: thA -> 0 за pull, thC, 0 -> thB за pull.
        static Vector3 Integrate(Seg[] segs, float thA, float thB, float thC, float pull, List<Vector3> output)
        {
            float total = 0f;
            foreach (var s in segs) total += s.len;
            float step = total / LoopSteps, start = 0f, before = 0f;
            int k = 0;
            Vector3 p = Vector3.zero;
            output?.Add(p);
            for (int i = 1; i <= LoopSteps; i++)
            {
                float l = (i - .5f) * step;
                while (k < segs.Length - 1 && l > start + segs[k].len) { before += segs[k].turn; start += segs[k].len; k++; }
                float psi = before + segs[k].turn * ShapeInt((l - start) / Mathf.Max(segs[k].len, 1e-4f));
                float endW = S5((l - (total - pull)) / pull);
                float pitch = thA * (1f - S5(l / pull)) + thB * endW + thC * S5(l / pull) * (1f - endW);
                float cp = Mathf.Cos(pitch);
                p += step * new Vector3(cp * Mathf.Cos(psi), Mathf.Sin(pitch), cp * Mathf.Sin(psi));
                output?.Add(p);
            }
            return p;
        }

        static void Newton(System.Func<Vector3, Vector3> f, ref Vector3 v, Vector3 scale)
        {
            for (int it = 0; it < 40; it++)
            {
                Vector3 r = f(v);
                if (r.magnitude < .02f) return;
                var m = new float[3, 3];
                for (int c = 0; c < 3; c++)
                {
                    Vector3 dv = Vector3.zero; dv[c] = scale[c] * 1e-3f;
                    Vector3 r2 = f(v + dv);
                    for (int row = 0; row < 3; row++) m[row, c] = (r2[row] - r[row]) / dv[c];
                }
                if (!Solve3(m, -r, out Vector3 stepV)) return;
                float lim = 1f;
                for (int c = 0; c < 3; c++)
                    if (Mathf.Abs(stepV[c]) > scale[c]) lim = Mathf.Min(lim, scale[c] / Mathf.Abs(stepV[c]));
                v += stepV * lim;
            }
        }

        static bool Solve3(float[,] m, Vector3 b, out Vector3 x)
        {
            float det = m[0, 0] * (m[1, 1] * m[2, 2] - m[1, 2] * m[2, 1]) - m[0, 1] * (m[1, 0] * m[2, 2] - m[1, 2] * m[2, 0]) + m[0, 2] * (m[1, 0] * m[2, 1] - m[1, 1] * m[2, 0]);
            x = Vector3.zero;
            if (Mathf.Abs(det) < 1e-9f) return false;
            for (int c = 0; c < 3; c++)
            {
                float[,] t = (float[,])m.Clone();
                for (int r = 0; r < 3; r++) t[r, c] = b[r];
                x[c] = (t[0, 0] * (t[1, 1] * t[2, 2] - t[1, 2] * t[2, 1]) - t[0, 1] * (t[1, 0] * t[2, 2] - t[1, 2] * t[2, 0]) + t[0, 2] * (t[1, 0] * t[2, 1] - t[1, 1] * t[2, 0])) / det;
            }
            return true;
        }

        // Профиль кривизны «плато с плавными краями»: накопленный интеграл 0..1 и отношение пика к среднему.
        const float Ramp = .35f;
        static float[] shapeTable;
        static float shapePeak;
        static float ShapePeak { get { EnsureShape(); return shapePeak; } }
        static float ShapeInt(float x)
        {
            EnsureShape();
            x = Mathf.Clamp01(x) * 256f;
            int i = Mathf.Min((int)x, 255);
            return Mathf.Lerp(shapeTable[i], shapeTable[i + 1], x - i);
        }
        static void EnsureShape()
        {
            if (shapeTable != null) return;
            var t = new float[257];
            float acc = 0f, prev = 0f;
            for (int i = 0; i <= 256; i++)
            {
                float x = i / 256f, g = Mathf.Min(S5(x / Ramp), S5((1f - x) / Ramp));
                if (i > 0) acc += (g + prev) * .5f / 256f;
                t[i] = acc; prev = g;
            }
            for (int i = 0; i <= 256; i++) t[i] /= acc;
            shapePeak = 1f / acc;
            shapeTable = t;
        }

        // ------------------------------------------------------------------ движение головы
        void Advance(float dt)
        {
            // аниматор: лапы поджаты в свечке и пике (клипы DashUp / DiveDown), спуск — по началу пике
            float before = headU;
            float low = TrackLow(headU);
            float vT = Mathf.Max(TrackSpeed(headU), TrackSpeed(headU + speed * .8f + 2f));
            speed = Mathf.SmoothDamp(speed, vT, ref speedVel, low > .01f ? .35f : .7f, vT > speed ? acceleration : braking, dt);
            PushTiming(dt);
            float maxU = track[track.Count - 1].u - 1f;
            headU = Mathf.Min(headU + speed * dt, maxU);
            if (animator && before < maneuverStartU && headU >= maneuverStartU) animator.SetTrigger(DashUpId);
            if (animator && before < diveStartU && headU >= diveStartU) animator.SetTrigger(DiveId);

            // крен: горизонтальное ускорение в повороте наклоняет «верх» внутрь (у земли — меньше)
            Vector3 T = TrackTangent(headU, 1.5f);
            Vector3 k = (TrackTangent(headU + 4f, 1.5f) - TrackTangent(headU - 4f, 1.5f)) / 8f;
            Vector3 lat = Vector3.ProjectOnPlane(k * speed * speed, Vector3.up);
            Vector3 level = Vector3.ProjectOnPlane(Vector3.up, T);
            if (level.sqrMagnitude < 1e-4f) level = Vector3.ProjectOnPlane(upCur, T);
            level.Normalize();
            Vector3 want = Vector3.ProjectOnPlane(Vector3.up * 9.81f + Vector3.ProjectOnPlane(lat, T) * bankAmount, T);
            want = want.sqrMagnitude > 1e-6f ? want.normalized : level;
            float bank = Vector3.Angle(level, want), maxBank = Mathf.Lerp(65f, 15f, low);
            if (bank > maxBank) want = Vector3.Slerp(level, want, maxBank / bank);
            upCur = Vector3.Slerp(upCur, want, 1f - Mathf.Exp(-dt * 4f));
            upCur = Vector3.ProjectOnPlane(upCur, T).normalized;

            // волна: в быстром полёте длиннее и мельче, у земли — почти без волны
            phase += 2f * Mathf.PI * speed * dt / Wavelength(speed);
            float ampT = Mathf.Lerp(1f, .45f, Mathf.InverseLerp(cruiseSpeed, fastSpeed, speed));
            ampT = Mathf.Lerp(ampT, .2f, S5(low * 2.5f));          // к шару — вытягивается, почти без волны
            amp = Mathf.Lerp(amp, ampT, 1f - Mathf.Exp(-dt * 2f));
            hist.Add(new Hist { u = headU, phase = phase, amp = amp, up = upCur });
            Trim();
        }

        float Wavelength(float v) => bodyLength * .96f * Mathf.Sqrt(Mathf.Max(1f, v / Mathf.Max(cruiseSpeed, .1f)));

        Hist HistAt(float u)
        {
            if (hist.Count == 0) return new Hist { u = u, phase = 0f, amp = 1f, up = Vector3.up };
            if (u <= hist[0].u) return hist[0];
            int hi = hist.Count - 1;
            if (u >= hist[hi].u) return hist[hi];
            int lo = 0;
            while (hi - lo > 1) { int mid = (lo + hi) >> 1; if (hist[mid].u <= u) lo = mid; else hi = mid; }
            var a = hist[lo]; var b = hist[hi];
            float t = b.u > a.u ? (u - a.u) / (b.u - a.u) : 0f;
            return new Hist { u = u, phase = Mathf.Lerp(a.phase, b.phase, t), amp = Mathf.Lerp(a.amp, b.amp, t), up = Vector3.Lerp(a.up, b.up, t) };
        }

        // ------------------------------------------------------------------ голова: шар, игрок, взгляды по сторонам
        void Look(float dt)
        {
            Vector3 fwd = (J[0] - J[1]).sqrMagnitude > 1e-6f ? (J[0] - J[1]).normalized : transform.forward;
            Vector3 target = Vector3.zero;
            float maxAngle = 55f;
            if (pearlOk && ((pushArmed && (pearlC - J[1]).magnitude < 45f) || clock < lookPearlUntil))
            {
                Vector3 to = pearlC - J[1];
                if (Vector3.Angle(fwd, to) < 120f) { target = to.normalized; maxAngle = 30f; }
            }
            var cam = lookAtPlayer && target == Vector3.zero ? Camera.main : null;
            if (cam)
            {
                Vector3 to = cam.transform.position - J[1];
                if (to.magnitude < lookAtPlayerDistance && Vector3.Angle(fwd, to) < 100f) target = to.normalized;
            }
            if (target == Vector3.zero && !InManeuver)
            {
                if (clock >= nextGlance)
                {
                    float yaw = Random.Range(25f, 50f) * (Random.value < .5f ? -1f : 1f), pitch = Random.Range(-12f, 8f);
                    glanceDir = Quaternion.AngleAxis(yaw, Vector3.up) * Quaternion.AngleAxis(pitch, Vector3.Cross(Vector3.up, fwd)) * fwd;
                    glanceUntil = clock + Random.Range(1.4f, 2.6f);
                    nextGlance = glanceUntil + Random.Range(glanceSeconds.x, glanceSeconds.y);
                }
                if (clock < glanceUntil) target = glanceDir;
            }
            Quaternion want = Quaternion.identity;
            if (target != Vector3.zero)
            {
                want = Quaternion.FromToRotation(fwd, target);
                want.ToAngleAxis(out float angle, out _);
                if (angle > maxAngle) want = Quaternion.Slerp(Quaternion.identity, want, maxAngle / angle);
            }
            lookQ = Quaternion.Slerp(lookQ, want, 1f - Mathf.Exp(-dt * 2.5f));
        }

        // ------------------------------------------------------------------ поза
        void Pose()
        {
            if (!rigReady || track.Count < 2) return;
            float ds = .5f;
            int n = Mathf.CeilToInt((bodyLength + 4f) / ds) + 1;
            bodyP.Clear(); bodyUp.Clear();
            float aLat = .08f * bodyLength * waveAmount, aVer = .056f * bodyLength * waveAmount, rollMax = .25f * waveAmount;
            float envLen = .4f * bodyLength;
            for (int i = 0; i < n; i++)
            {
                float s = i * ds, u = headU - s;
                Vector3 p = TrackPos(u), T = TrackTangent(u, 1f);
                var h = HistAt(u);
                Vector3 up = Vector3.ProjectOnPlane(h.up, T);
                if (up.sqrMagnitude < 1e-6f) up = Vector3.ProjectOnPlane(Vector3.up, T);
                up.Normalize();
                Vector3 side = Vector3.Cross(up, T).normalized;
                float env = (.12f + .88f * S5(s / envLen)) * h.amp;
                float sn = Mathf.Sin(h.phase), cs = Mathf.Cos(h.phase);
                p += side * (aLat * env * sn) + up * (aVer * env * cs);
                float roll = rollMax * env * sn;
                bodyP.Add(p);
                bodyUp.Add(up * Mathf.Cos(roll) - side * Mathf.Sin(roll));
            }

            // суставы на точных расстояниях вдоль тела
            J[0] = bodyP[0]; jointUp[0] = bodyUp[0];
            int seg = 0;
            for (int k = 0; k < 19; k++)
            {
                Vector3 a = J[k];
                float c = chord[k];
                while (seg < bodyP.Count - 2 && (bodyP[seg + 1] - a).sqrMagnitude < c * c) seg++;
                Vector3 p0 = bodyP[seg], p1 = bodyP[seg + 1], d = p1 - p0, w = p0 - a;
                float A = Vector3.Dot(d, d), B = 2f * Vector3.Dot(w, d), C = Vector3.Dot(w, w) - c * c;
                float x = A > 1e-8f ? Mathf.Clamp01((-B + Mathf.Sqrt(Mathf.Max(B * B - 4f * A * C, 0f))) / (2f * A)) : 0f;
                J[k + 1] = p0 + d * x;
                jointUp[k + 1] = Vector3.Lerp(bodyUp[seg], bodyUp[seg + 1], x);
            }

            // поворот головы: 45 % шея, остальное голова
            var rot = new Quaternion[19];
            var pos = new Vector3[19];
            for (int i = 0; i < 19; i++)
            {
                int hJ = BoneFrom[i], tJ = BoneTo[i];
                Vector3 aim = J[tJ] - J[hJ];
                Vector3 up = (jointUp[hJ] + jointUp[tJ]).normalized;
                rot[i] = Quaternion.LookRotation(aim, up) * boneOffset[i];
                pos[i] = J[hJ];
            }
            Quaternion qn = Quaternion.Slerp(Quaternion.identity, lookQ, .45f);
            Quaternion qh = lookQ * Quaternion.Inverse(qn);
            rot[1] = qn * rot[1];
            pos[0] = J[2] + qn * (pos[0] - J[2]);
            rot[0] = qh * qn * rot[0];

            // корень объекта — у груди, чтобы transform.position был там, где дракон
            Vector3 chestFwd = J[2] - J[3];
            if (chestFwd.sqrMagnitude > 1e-6f) transform.SetPositionAndRotation(J[3], Quaternion.LookRotation(chestFwd, jointUp[3]));
            foreach (int i in WriteOrder) spine[i].SetPositionAndRotation(pos[i], rot[i]);
        }

        // ------------------------------------------------------------------ ущелье
        void ResolveZone()
        {
            zoneMin = 0f;
            zoneMax = PathLength;
            var bounds = (turnBounds ?? new Transform[0]).Where(t => t).Select(t => Closest(t.position)).OrderBy(d => d).ToArray();
            if (bounds.Length >= 2) { zoneMin = bounds[0]; zoneMax = bounds[bounds.Length - 1]; }
            else if (bounds.Length == 1) { if (bounds[0] < PathLength * .5f) zoneMin = bounds[0]; else zoneMax = bounds[0]; }
            exits = (exitPoints ?? new Transform[0]).Where(t => t).Select(t => Closest(t.position))
                .Where(d => d > zoneMin && d < zoneMax).OrderBy(d => d).ToArray();
            pearlMin = zoneMin + PearlBoundKeep;
            pearlMax = Mathf.Max(zoneMax - PearlBoundKeep, pearlMin);
        }

        // Высота стен над линией полёта вдоль ущелья: где лучи в обе стороны (до 60 м) перестают попадать.
        void BuildRim()
        {
            int n = Mathf.Max(1, Mathf.CeilToInt(PathLength / RimStep) + 1);
            var raw = new float[n];
            for (int k = 0; k < n; k++)
            {
                float cs = Mathf.Min(k * RimStep, PathLength);
                Vector3 p = Sample(cs);
                Vector3 tg = Sample(cs + 2f) - Sample(cs - 2f); tg.y = 0f;
                Vector3 side = tg.sqrMagnitude > 1e-6f ? Vector3.Cross(Vector3.up, tg.normalized) : Vector3.right;
                float top = 60f;
                int open = 0;
                for (float h = 0f; h <= 300f; h += 5f)
                {
                    Vector3 o = p + Vector3.up * h;
                    bool hit = Physics.Raycast(o, side, 60f, canyonLayers, QueryTriggerInteraction.Ignore) ||
                               Physics.Raycast(o, -side, 60f, canyonLayers, QueryTriggerInteraction.Ignore);
                    if (hit) { open = 0; top = h + 5f; }
                    else if (++open >= 4) break;
                }
                raw[k] = top;
            }
            rim = new float[n];
            for (int k = 0; k < n; k++)
            {
                float m = 0f;
                for (int j = Mathf.Max(0, k - 2); j <= Mathf.Min(n - 1, k + 2); j++) m = Mathf.Max(m, raw[j]);
                rim[k] = m;
            }
        }

        float RimAt(float cs)
        {
            if (rim.Length == 0) return 80f;
            float x = Mathf.Clamp(cs / RimStep, 0f, rim.Length - 1);
            int i = Mathf.Min((int)x, rim.Length - 2);
            return i < 0 ? rim[0] : Mathf.Lerp(rim[i], rim[i + 1], x - i);
        }

        public static float WaypointOrder(Transform t)
        {
            var m = Regex.Match(t.name, @"(\d+(?:[.,]\d+)?)\s*m\b");
            return m.Success ? float.Parse(m.Groups[1].Value.Replace(',', '.'), CultureInfo.InvariantCulture) : t.GetSiblingIndex();
        }

        public void BuildPath()
        {
            points.Clear();
            along.Clear();
            if (!flightPath || flightPath.childCount < 2) return;
            var raw = new List<Transform>();
            foreach (Transform child in flightPath) raw.Add(child);
            raw.Sort((a, b) => WaypointOrder(a).CompareTo(WaypointOrder(b)));
            var ctrl = raw.ConvertAll(t => t.position);
            FitToCanyon(ctrl);
            for (int i = 0; i < ctrl.Count - 1; i++)
            {
                Vector3 p0 = ctrl[Mathf.Max(i - 1, 0)], p1 = ctrl[i];
                Vector3 p2 = ctrl[i + 1], p3 = ctrl[Mathf.Min(i + 2, ctrl.Count - 1)];
                for (int j = 0; j < 4; j++)
                {
                    float t = j / 4f;
                    points.Add(.5f * (2f * p1 + (p2 - p0) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t * t + (3f * p1 - p0 - 3f * p2 + p3) * t * t * t));
                }
            }
            points.Add(ctrl[ctrl.Count - 1]);
            along.Add(0f);
            for (int i = 1; i < points.Count; i++) along.Add(along[i - 1] + Vector3.Distance(points[i - 1], points[i]));
        }

        // Высота полёта над дном: задана числом или снята с того места, где дракон стоит в сцене (один раз за игру).
        float clearanceCache = -1f;
        float Clearance()
        {
            if (flightHeight > 0f) return flightHeight;
            if (Application.isPlaying && clearanceCache > 0f) return clearanceCache;
            float c = GroundBelow(transform.position, out float gy) ? transform.position.y - gy : -1f;
            if (Application.isPlaying) clearanceCache = c;
            return c;
        }

        bool GroundBelow(Vector3 p, out float y)
        {
            bool hit = Physics.Raycast(p, Vector3.down, out var h, 2000f, canyonLayers, QueryTriggerInteraction.Ignore);
            y = hit ? h.point.y : 0f;
            return hit;
        }

        // Точки -> линия полёта: дно под каждой точкой (минимум из трёх лучей — мимо валунов) + высота полёта,
        // потом сдвиг на середину между стенами. Обе поправки сглажены вдоль ущелья.
        bool FitToCanyon(List<Vector3> ctrl)
        {
            float clearance = Clearance();
            if (clearance <= 0f) return false;
            int n = ctrl.Count, found = 0;
            var ground = new float[n];
            var side = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                Vector3 tangent = ctrl[Mathf.Min(i + 1, n - 1)] - ctrl[Mathf.Max(i - 1, 0)];
                tangent.y = 0f;
                side[i] = tangent.sqrMagnitude > 1e-6f ? Vector3.Cross(Vector3.up, tangent.normalized) : Vector3.right;
                float best = float.PositiveInfinity;
                foreach (float o in new[] { 0f, -GroundProbe, GroundProbe })
                    if (GroundBelow(ctrl[i] + side[i] * o, out float y)) best = Mathf.Min(best, y);
                ground[i] = best;
                if (!float.IsInfinity(best)) found++;
            }
            if (found == 0) return false;
            FillGaps(ground);
            Smooth(ground, 2, 2);
            for (int i = 0; i < n; i++) ctrl[i] = new Vector3(ctrl[i].x, ground[i] + clearance, ctrl[i].z);

            if (centerBetweenWalls)
            {
                var shift = new float[n];
                for (int i = 0; i < n; i++)
                {
                    bool l = Physics.Raycast(ctrl[i], -side[i], out var lh, 300f, canyonLayers, QueryTriggerInteraction.Ignore);
                    bool r = Physics.Raycast(ctrl[i], side[i], out var rh, 300f, canyonLayers, QueryTriggerInteraction.Ignore);
                    shift[i] = l && r ? Mathf.Clamp((rh.distance - lh.distance) * .5f, -MaxCenterShift, MaxCenterShift) : 0f;
                }
                Smooth(shift, 1, 2);
                for (int i = 0; i < n; i++) ctrl[i] += side[i] * shift[i];

                float keep = WallKeep * transform.lossyScale.x;
                for (int pass = 0; pass < 2; pass++)
                    for (int i = 0; i < n; i++)
                    {
                        float dl = Physics.Raycast(ctrl[i], -side[i], out var lh, 300f, canyonLayers, QueryTriggerInteraction.Ignore) ? lh.distance : float.PositiveInfinity;
                        float dr = Physics.Raycast(ctrl[i], side[i], out var rh, 300f, canyonLayers, QueryTriggerInteraction.Ignore) ? rh.distance : float.PositiveInfinity;
                        float s = 0f;
                        if (dl < keep && dr > dl) s = Mathf.Min(keep - dl, (Mathf.Min(dr, 2f * keep) - dl) * .5f);
                        else if (dr < keep && dl > dr) s = -Mathf.Min(keep - dr, (Mathf.Min(dl, 2f * keep) - dr) * .5f);
                        ctrl[i] += side[i] * s;
                    }
            }
            return true;
        }

        static void FillGaps(float[] a)
        {
            int n = a.Length;
            for (int i = 0; i < n; i++)
            {
                if (!float.IsInfinity(a[i])) continue;
                int lo = i - 1, hi = i + 1;
                while (lo >= 0 && float.IsInfinity(a[lo])) lo--;
                while (hi < n && float.IsInfinity(a[hi])) hi++;
                a[i] = lo < 0 ? a[hi] : hi >= n ? a[lo] : Mathf.Lerp(a[lo], a[hi], (i - lo) / (float)(hi - lo));
            }
        }

        static void Smooth(float[] a, int radius, int passes)
        {
            var tmp = new float[a.Length];
            for (int p = 0; p < passes; p++)
            {
                for (int i = 0; i < a.Length; i++)
                {
                    float sum = 0f; int cnt = 0;
                    for (int k = Mathf.Max(0, i - radius); k <= Mathf.Min(a.Length - 1, i + radius); k++) { sum += a[k]; cnt++; }
                    tmp[i] = sum / cnt;
                }
                System.Array.Copy(tmp, a, a.Length);
            }
        }

        Vector3 Sample(float d)
        {
            if (points.Count == 0) return transform.position;
            d = Mathf.Clamp(d, 0f, PathLength);
            int lo = 0, hi = along.Count - 1;
            while (hi - lo > 1) { int mid = (lo + hi) / 2; if (along[mid] <= d) lo = mid; else hi = mid; }
            float span = along[hi] - along[lo];
            return Vector3.Lerp(points[lo], points[hi], span > 0f ? (d - along[lo]) / span : 0f);
        }

        float Closest(Vector3 p)
        {
            float best = 0f, bestSq = float.MaxValue;
            for (int i = 0; i < points.Count - 1; i++)
            {
                Vector3 a = points[i], ab = points[i + 1] - a;
                float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
                float sq = (a + ab * t - p).sqrMagnitude;
                if (sq < bestSq) { bestSq = sq; best = along[i] + t * (along[i + 1] - along[i]); }
            }
            return best;
        }

        // То же, но по горизонтали (для того, что внизу, у дна).
        float ClosestFlat(Vector3 p)
        {
            float best = 0f, bestSq = float.MaxValue;
            p.y = 0f;
            for (int i = 0; i < points.Count - 1; i++)
            {
                Vector3 a = Hz(points[i]), ab = Hz(points[i + 1]) - a;
                float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
                float sq = (a + ab * t - p).sqrMagnitude;
                if (sq < bestSq) { bestSq = sq; best = along[i] + t * (along[i + 1] - along[i]); }
            }
            return best;
        }

        // ------------------------------------------------------------------ утилиты
        static float S5(float x)
        {
            x = Mathf.Clamp01(x);
            return x * x * x * (x * (x * 6f - 15f) + 10f);
        }

        static float WrapPi(float a)
        {
            a = Mathf.Repeat(a + Mathf.PI, 2f * Mathf.PI) - Mathf.PI;
            return a;
        }

        static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 1e-8f ? v.normalized : Vector3.forward;
        }

        static Vector3 Hz(Vector3 v) { v.y = 0f; return v; }

        // Выделите дракона — в Scene view видна линия полёта, в Play — ещё и путь головы впереди и где остановится шар.
        void OnDrawGizmosSelected()
        {
            if (!Application.isPlaying) { BuildPath(); ResolveZone(); }
            if (points.Count < 2) return;
            for (int i = 0; i < points.Count - 1; i++)
            {
                bool inZone = along[i] >= zoneMin && along[i + 1] <= zoneMax;
                Gizmos.color = inZone ? new Color(.3f, .8f, .9f) : new Color(.3f, .8f, .9f, .25f);
                Gizmos.DrawLine(points[i], points[i + 1]);
            }
            if (Application.isPlaying && track.Count > 1)
            {
                Gizmos.color = new Color(1f, .4f, 1f);
                for (int i = Mathf.Max(IndexAt(headU) - 40, 0); i < track.Count - 1; i++) Gizmos.DrawLine(track[i].p, track[i + 1].p);
                if (pearlOk)
                {
                    PearlRest(out _, out _, out Vector3 rest);
                    Gizmos.color = new Color(.4f, 1f, .6f);
                    Gizmos.DrawWireSphere(rest, pearlR * 1.5f);
                }
            }
        }

        // Жёлтые границы и красные точки видны всегда.
        void OnDrawGizmos()
        {
            if (turnBounds != null)
                foreach (var t in turnBounds) if (t) { Gizmos.color = new Color(1f, .85f, .1f); Gizmos.DrawSphere(t.position, 3f); }
            if (exitPoints != null)
                foreach (var t in exitPoints) if (t) { Gizmos.color = new Color(1f, .2f, .15f); Gizmos.DrawSphere(t.position, 2.5f); }
        }
    }
}
