using System;
using System.Collections;
using System.Collections.Generic;
using TanShadow.Seasons;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TanShadow.Summer
{
    public enum ZhuqueState
    {
        Perched,     // сидит на валуне, хвост свисает до земли, следит за кольцом на 360°
        Alert,       // заметила героиню: замирает и кричит, сейчас сорвётся
        Relocating,  // перелетает на другой валун
        TakeOff,     // героиня схватила хвост: взлёт на кольцо
        Flight,      // летит по кольцу, героиня висит на хвосте
        ShakeOff,    // стряхивает героиню (долго висит или 4-й удар над сушей)
        Falling,     // перья отсечены (удары 1–3): падает на землю и лежит
        Regenerate,  // встаёт, вспыхивает, хвост отрастает; на третьем — открывает лаву
        FinalFall,   // 4-й удар над лавой: падает в магму
        Defeated,    // мир серый, испытание пройдено
        HeroDown     // героиня погибла в лаве, идёт чёрная инь
    }

    // Испытание Чжуцюэ (GDD лета). Отвечает за птицу, фазы арены и исход. Героиню видит через HeroBridge,
    // звук и обратную связь отдаёт событиями — их слушают ZhuqueSounds и ZhuqueFeedback.
    [DefaultExecutionOrder(160)]
    public sealed class ZhuqueEncounter : MonoBehaviour
    {
        [Header("Данные")]
        public ZhuqueTuning tuning;

        [Header("Сцена")]
        public HeroBridge hero;
        public ZhuqueBird bird;
        public ZhuqueLava lava;
        [Tooltip("Группа крупных валунов. Садится птица на «First perch» и «Cover and perch N»")]
        public Transform perchRoot;
        [Tooltip("Стартовый валун (на нём птица в начале и после смерти героини). Пусто — валун на startPerchDistance от входа")]
        public Transform firstPerch;
        [Tooltip("Кольцо полёта: точки по середине дна долины на уровне земли, по порядку, замкнуто. Высоту добавляет flightHeight")]
        public Vector3[] flightRing;
        [Tooltip("Солнце: тень птицы падает по его лучу. Пусто — тень строго под птицей")]
        public Light sun;
        public YinYangCover cover;
        public DeadWorld world;
        [Tooltip("Где героиня возрождается. Пусто — где стояла в начале сцены")]
        public Transform heroSpawn;

        [Header("Отладка")]
        public bool showDebug = true;

        public event Action<ZhuqueState> StateChanged;
        public event Action<Vector3> Alerted;              // заметила, сейчас сорвётся
        public event Action<Vector3> TookOff;              // взлетела с валуна или с земли
        public event Action<Vector3> Landed;               // села на валун
        public event Action<Vector3> Grabbed;              // героиня схватила хвост
        public event Action<int, int, Vector3> Cut;        // отсечено перьев (засчитано, нужно, точка)
        public event Action<Vector3> CutRefused;           // 4-й удар над сушей: не засчитан
        public event Action<Vector3> ShakenOff;            // героиня сброшена с хвоста
        public event Action<Vector3> BirdFell;             // птица рухнула на землю
        public event Action<Vector3, bool> Regenerating;   // вспышка восстановления; true — эта откроет лаву
        public event Action<Vector3> Ignited;              // пик вспышки: крылья расправлены, хвост начинает отрастать
        public event Action<Vector3> LavaOpened;
        public event Action<Vector3> FellIntoLava;
        public event Action HeroKilled;
        public event Action Defeated;
        public event Action EncounterReset;

        public ZhuqueState State { get; private set; }
        public int Cuts { get; private set; }
        public float Detection { get; private set; }       // 0..1: насколько птица уверена, что видит героиню
        public bool HeroInSight { get; private set; }
        public bool TailInReach { get; private set; }
        public Vector3 ShadowPoint { get; private set; }
        public Vector3 ShadowNormal { get; private set; } = Vector3.up;
        public bool ShadowOverLava { get; private set; }
        // Сколько осталось до посадки на валун (звук когтей выравнивается по касанию).
        public float RelocateTimeLeft => State == ZhuqueState.Relocating ? Mathf.Max(0f, moveDuration - timer) : 0f;
        public float HoldTime => State == ZhuqueState.Flight || State == ZhuqueState.ShakeOff ? holdTime : 0f;

        // Валун: где стоят лапы (на самой поверхности камня) и два разворота — птица садится тем,
        // при котором она спиной к героине.
        struct Perch
        {
            public Transform rock;
            public Vector3 top;
            public Vector3 facing;
            public Vector3 facingAlt;
            public Vector3 seat;      // где стоят лапы при facing: у края камня, хвост свисает за край
            public Vector3 seatAlt;   // то же для facingAlt
        }

        readonly List<Perch> perches = new List<Perch>();
        readonly List<int> recentPerches = new List<int>();
        int perchSide;                                         // на каком из двух мест валуна сидит птица   // последние валуны, куда садилась птица
        readonly List<int> candidates = new List<int>();
        readonly List<float> weights = new List<float>();
        int perch, startPerch;
        int ringDirection;            // в какую сторону кольца был последний перелёт: +1, −1, 0 — ещё не летала
        float[] ringAt;               // накопленная длина кольца у каждой точки
        float ringLength, ringPos;    // ringPos — где птица на кольце, м
        Vector3 heroStartPosition;
        Quaternion heroStartRotation;

        float timer, holdTime, moveDuration;
        Vector3 moveFrom, moveTo, faceFrom, faceTo;
        float takeOffTurn, departTurn, landTurn;
        float ringStart, takeOffLift, takeOffDuration;   // взлёт с героиней: откуда на кольце, запас высоты, длительность   // на сколько поворачивает птица, с учётом стороны (Turn), °
        float arcHeight, fallRoll;
        float angleFrom, angleTo, radiusFrom, radiusTo;   // перелёт по кольцу
        Vector3 grabGround;           // где стояла героиня, когда схватила хвост: безопасная земля
        bool carrying, lavaRequested, shakeAfterRefuse;
        float lavaArmedAt = -1f;
        bool throwing;                // героиню отбросило отсечёнными перьями: летит по дуге назад
        float throwTimer;
        Vector3 throwFrom, throwTo, throwFacing;

        void Start()
        {
            heroStartPosition = heroSpawn != null ? heroSpawn.position : hero.Body.position;
            heroStartRotation = heroSpawn != null ? heroSpawn.rotation : hero.Body.rotation;
            BuildRing();
            CollectPerches();
            if (perches.Count == 0)
            {
                Debug.LogError("ZhuqueEncounter: в perchRoot нет валунов «First perch» / «Cover and perch N»", this);
                enabled = false;
                return;
            }
            if (lava != null) lava.HeroBurned += OnHeroBurned;
            if (cover == null) cover = FindFirstObjectByType<YinYangCover>();
            ResetEncounter();
        }

        void OnDestroy()
        {
            if (heroProbe != null) Destroy(heroProbe.gameObject);
            if (lava != null) lava.HeroBurned -= OnHeroBurned;
        }

        void Update()
        {
            if (Keyboard.current != null && Keyboard.current.f3Key.wasPressedThisFrame) showDebug = !showDebug;
            if (hero == null || tuning == null || bird == null) return;
            float dt = Time.deltaTime;
            if (throwing) TickThrow(dt);

            switch (State)
            {
                case ZhuqueState.Perched: TickPerched(dt); break;
                case ZhuqueState.Alert: TickAlert(dt); break;
                case ZhuqueState.Relocating: TickRelocating(dt); break;
                case ZhuqueState.TakeOff: TickTakeOff(dt); break;
                case ZhuqueState.Flight: TickFlight(dt); break;
                case ZhuqueState.ShakeOff: TickShakeOff(dt); break;
                case ZhuqueState.Falling: TickFalling(dt); break;
                case ZhuqueState.Regenerate: TickRegenerate(dt); break;
                case ZhuqueState.FinalFall: TickFinalFall(dt); break;
            }

            if (lava != null)
            {
                bool riverReady = lava.IsOpen && lavaArmedAt >= 0f && Time.time >= lavaArmedAt;
                bool alive = State != ZhuqueState.HeroDown && State != ZhuqueState.FinalFall && State != ZhuqueState.Defeated;
                // Пока русла заливаются, лава не убивает: героиня успевает выйти из русла.
                lava.HazardArmed = alive && !carrying && (riverReady || !lava.IsOpen);
            }
        }

        // ---------------- На валуне: скрытность ----------------

        void TickPerched(float dt)
        {
            // Под заливкой (рестарт после смерти) птица не следит и хвост не хватается.
            if (cover != null && cover.IsCovering) { Detection = 0f; TailInReach = false; return; }
            float sight = SightWeight();
            HeroInSight = sight > 0f;
            Detection = HeroInSight
                ? Mathf.Min(1f, Detection + sight * dt / tuning.timeToDetect)
                : Mathf.Max(0f, Detection - tuning.detectionRecoverPerSecond * dt);

            if (HeroInSight) bird.LookAt(hero.AimPoint, 110f, dt);
            else bird.LookForward(dt);
            bird.SetGlow(0f);
            // Встревоженность растёт со шкалой: заметна уже с первой трети.
            bird.SetAlarm(Mathf.SmoothStep(0f, 1f, Detection / 0.7f));
            bird.SetTailLength(Mathf.MoveTowards(bird.TailLength, 1f, dt));
            bird.Apply();

            TailInReach = InReach();
            if (TailInReach && hero.AttackPressed)
            {
                Grab();
                return;
            }
            if (Detection >= 1f)
            {
                timer = tuning.alertTime;
                bird.Play(ZhuqueBird.Alert);
                Enter(ZhuqueState.Alert);
                Alerted?.Invoke(bird.EyePoint);
            }
        }

        void TickAlert(float dt)
        {
            bird.LookAt(hero.AimPoint, 150f, dt);
            bird.SetGlow(0f);
            bird.SetAlarm(1f);
            TailInReach = false;
            timer -= dt;
            if (timer <= 0f) FlyToPerch(ChoosePerch(hero.Body.position));
        }

        // 0 — героини не видно; иначе во сколько раз быстрее растёт шкала (1 вблизи, farDetectionFactor на краю обзора).
        float SightWeight()
        {
            Vector3 eye = bird.EyePoint;
            Vector3 to = hero.AimPoint - eye;
            float distance = to.magnitude;
            if (distance > tuning.viewDistance) return 0f;
            // Обзор круговой: значение имеет только, заслоняет ли героиню камень.
            if (Physics.Raycast(eye, to / distance, out var hit, distance, ~0, QueryTriggerInteraction.Ignore) &&
                hit.transform != hero.Body && !hit.transform.IsChildOf(hero.Body))
                return 0f;
            float far = Mathf.InverseLerp(tuning.detectFullRange, tuning.viewDistance, distance);
            return Mathf.Lerp(1f, tuning.farDetectionFactor, far);
        }

        bool InReach()
        {
            Vector3 tip = bird.TailEnd;   // где кончик виден (физика хвоста)
            Vector3 feet = hero.Body.position;
            float above = tip.y - feet.y;
            return Flat(tip - feet).magnitude <= tuning.grabRadius && above <= tuning.grabMaxHeight && above >= -1f;
        }

        // ---------------- Перелёт ----------------

        // Не на другой конец кольца, а на «некотором расстоянии» (GDD-SU) — но не всегда на один и тот же валун:
        // выбор случайный, чаще те, что ближе к птице (по кольцу) и дальше в ту же сторону, куда она летела; последние relocateMemory валунов
        // пропускаются, пока есть другие подходящие.
        int ChoosePerch(Vector3 heroPosition)
        {
            int farthest = -1;
            float farDistance = -1f;
            candidates.Clear();
            weights.Clear();
            Vector3 c = RingCenter();
            Vector3 from = bird.transform.position;
            float angleNow = Mathf.Atan2(from.z - c.z, from.x - c.x) * Mathf.Rad2Deg;
            for (int pass = 0; pass < 2 && candidates.Count == 0; pass++)
            {
                for (int i = 0; i < perches.Count; i++)
                {
                    if (i == perch) continue;
                    if (pass == 0 && recentPerches.Contains(i)) continue;
                    float d = Flat(perches[i].top - heroPosition).magnitude;
                    if (d > farDistance) { farDistance = d; farthest = i; }
                    if (d < tuning.minRelocateDistance) continue;
                    // Ближе к птице — чаще: длина перелёта по кольцу (как летит FlyToPerch).
                    Vector3 to = perches[i].top;
                    float turn = Mathf.DeltaAngle(angleNow, Mathf.Atan2(to.z - c.z, to.x - c.x) * Mathf.Rad2Deg);
                    float flight = Mathf.Abs(turn) * Mathf.Deg2Rad * (Flat(from - c).magnitude + Flat(to - c).magnitude) * 0.5f;
                    // Спад крутой (4-я степень): вдвое дальше — в 9 раз реже; дальние валуны — только если ближних нет.
                    float far = flight / tuning.relocateNearDistance;
                    float w = 1f / (1f + far * far * far * far);
                    // Не мечется: улетев вперёд по кольцу, назад возвращается редко.
                    if (ringDirection != 0 && Mathf.Abs(turn) > ReverseTolerance && (int)Mathf.Sign(turn) != ringDirection)
                        w *= tuning.relocateReverseWeight;
                    candidates.Add(i);
                    weights.Add(w);
                }
            }
            if (candidates.Count == 0) return Mathf.Max(0, farthest);

            float total = 0f;
            foreach (float w in weights) total += w;
            float pick = UnityEngine.Random.value * total;
            for (int k = 0; k < candidates.Count; k++)
            {
                pick -= weights[k];
                if (pick <= 0f) return candidates[k];
            }
            return candidates[candidates.Count - 1];
        }

        const float ReverseTolerance = 8f;   // перелёт короче этого угла по кольцу — не разворот, °

        void RememberPerch(int index)
        {
            recentPerches.Remove(index);
            recentPerches.Add(index);
            while (recentPerches.Count > tuning.relocateMemory) recentPerches.RemoveAt(0);
        }

        void FlyToPerch(int index)
        {
            perch = index;
            moveFrom = bird.transform.position;
            perchSide = ChooseSide(index, hero.Body.position);
            moveTo = SeatOf(index, perchSide);
            faceFrom = bird.transform.forward;
            bird.SetAlarm(0f);
            bird.SetLying(0f);
            faceTo = FacingOf(index, perchSide);
            // Перелёт — по кольцу вокруг вулкана (короткой стороной), а не по прямой через конус.
            Vector3 c = RingCenter();
            angleFrom = Mathf.Atan2(moveFrom.z - c.z, moveFrom.x - c.x) * Mathf.Rad2Deg;
            angleTo = angleFrom + Mathf.DeltaAngle(angleFrom, Mathf.Atan2(moveTo.z - c.z, moveTo.x - c.x) * Mathf.Rad2Deg);
            radiusFrom = Flat(moveFrom - c).magnitude;
            radiusTo = Flat(moveTo - c).magnitude;
            if (Mathf.Abs(angleTo - angleFrom) > ReverseTolerance) ringDirection = angleTo > angleFrom ? 1 : -1;
            float distance = Mathf.Abs(angleTo - angleFrom) * Mathf.Deg2Rad * (radiusFrom + radiusTo) * 0.5f
                             + Mathf.Abs(radiusTo - radiusFrom);
            moveDuration = Mathf.Max(0.8f, distance / tuning.relocateSpeed);
            arcHeight = tuning.relocateArcHeight * Mathf.Clamp01(distance / 30f);
            departTurn = TurnAngle(faceFrom, RelocateTravel(0f));
            landTurn = TurnAngle(RelocateTravel(1f), faceTo);
            timer = 0f;
            Detection = 0f;
            HeroInSight = false;
            TailInReach = false;
            Enter(ZhuqueState.Relocating);
            TookOff?.Invoke(moveFrom);
        }

        // Куда летит птица в точке перелёта e (по горизонтали).
        Vector3 RelocateTravel(float e)
        {
            Vector3 travel = Flat(RelocatePoint(Mathf.Min(1f, e + 0.02f)) - RelocatePoint(Mathf.Max(0f, e - 0.02f)));
            return travel.sqrMagnitude > 0.0001f ? travel.normalized : faceTo;
        }

        // Поворот птицы от from к to на долю s вокруг вертикали. turn — угол прошлого кадра: из углов a, a±360
        // берём ближайший к нему, поэтому сторона поворота не меняется. (Slerp между почти противоположными
        // направлениями перескакивал с одной стороны на другую — птица с хвостом и героиней разворачивалась рывком.)
        static Vector3 Turn(Vector3 from, Vector3 to, float s, ref float turn)
        {
            float a = TurnAngle(from, to);
            if (a - turn > 180f) a -= 360f;
            else if (turn - a > 180f) a += 360f;
            turn = a;
            return Quaternion.AngleAxis(a * s, Vector3.up) * Flat(from).normalized;
        }

        static float TurnAngle(Vector3 from, Vector3 to) => Vector3.SignedAngle(Flat(from), Flat(to), Vector3.up);

        // Точка перелёта: угол и радиус вокруг центра кольца, высота — от валуна к валуну.
        Vector3 RelocatePoint(float e)
        {
            Vector3 c = RingCenter();
            float a = Mathf.Lerp(angleFrom, angleTo, e) * Mathf.Deg2Rad;
            float r = Mathf.Lerp(radiusFrom, radiusTo, e);
            return new Vector3(c.x + Mathf.Cos(a) * r, Mathf.Lerp(moveFrom.y, moveTo.y, e), c.z + Mathf.Sin(a) * r);
        }

        Vector3 RingCenter()
        {
            Vector3 sum = Vector3.zero;
            foreach (var p in flightRing) sum += p;
            return sum / flightRing.Length;
        }

        void TickRelocating(float dt)
        {
            timer += dt;
            float t = Mathf.Clamp01(timer / moveDuration);
            float e = Mathf.SmoothStep(0f, 1f, t);
            Vector3 position = RelocatePoint(e) + Vector3.up * (arcHeight * 4f * t * (1f - t));
            Vector3 facing = Turn(faceFrom, RelocateTravel(e), Mathf.Clamp01(t * 5f), ref departTurn);
            facing = Turn(facing, faceTo, Mathf.SmoothStep(0f, 1f, (t - 0.75f) / 0.25f), ref landTurn);
            // Наклон нарастает от нуля и к посадке уходит в ноль: без рывка корпуса (и хвоста) на взлёте и посадке.
            PlaceBird(position, facing, Mathf.Lerp(-12f, 10f, t) * Mathf.Clamp01(Mathf.Min(t, 1f - t) * 6f));
            // Клипы взлёта и посадки — целиком за свою часть перелёта.
            if (t < 0.3f) bird.Play(ZhuqueBird.TakeOff, -1f, moveDuration * 0.3f);
            else if (t < 0.75f) bird.Play(ZhuqueBird.Flight);
            else bird.Play(ZhuqueBird.Landing, -1f, moveDuration * 0.25f);
            // Хвост перестаёт свисать за первую четверть перелёта и снова свисает к посадке.
            bird.SetTailHang(Mathf.Max(1f - Mathf.SmoothStep(0f, 1f, t / 0.25f), Mathf.SmoothStep(0f, 1f, (t - 0.7f) / 0.3f)));
            if (t > 0.65f) DrapeTail(facing);   // к посадке хвост уже свисает под птицей
            bird.SetTailLength(Mathf.MoveTowards(bird.TailLength, 1f, dt));
            bird.SetGlow(0f);
            bird.LookForward(dt);
            if (t >= 1f) Sit(perch, perchSide);
        }

        void Sit(int index, int side)
        {
            perch = index;
            perchSide = side;
            RememberPerch(index);
            PoseAt(SeatOf(index, side), FacingOf(index, side));
            bird.Play(ZhuqueBird.Perch);
            Detection = 0f;
            Enter(ZhuqueState.Perched);
            Landed?.Invoke(perches[index].top);
        }

        // ---------------- Хвост по камню ----------------

        const int DrapeMax = 96;
        const float DrapeStep = 0.3f;        // шаг укладки по горизонтали, м
        const float DrapeClearance = 0.35f;  // перья (толщиной ~0.3 м) лежат на поверхности, а не в ней, м
        readonly Vector3[] drape = new Vector3[DrapeMax];

        const float TailStraight = 0.8f;     // сначала хвост чуть идёт назад, м
        const float TailHook = 2.6f;         // последние метры хвоста слегка отходят назад, м
        const float TailHookTurn = 0.6f;     // на сколько отходит конец от вертикали, рад (~35°), без крючка
        public const float TailAirGap = 1.4f; // кончик хвоста не ниже этого над землёй — тогда и весь хвост в воздухе, м

        // Свисающий хвост (как у сидящего феникса): чуть назад, отвесно вниз за краем камня, конец плавно
        // отходит назад — весь хвост в воздухе, до земли не достаёт (насесты достаточно высокие).
        // Если камень расширяется книзу, хвост отходит от него наружу, а не проходит сквозь.
        void DrapeTail(Vector3 facing)
        {
            Vector3 root = bird.TailRoot;
            Vector3 back = -Flat(facing).normalized;
            float length = bird.TailSpan;
            float hook = Mathf.Min(TailHook, length * 0.4f);
            float radius = hook / TailHookTurn;
            float drop = Mathf.Max(0f, length - TailStraight - hook);
            Vector3 dropAt = root + back * TailStraight;
            Vector3 hookStart = dropAt + Vector3.down * (0.05f + drop);

            int n = 0;
            for (float d = 0f; d <= length + 0.01f && n < DrapeMax; d += DrapeStep)
            {
                Vector3 p;
                if (d <= TailStraight) p = root + back * d + Vector3.down * (0.05f * d / TailStraight);
                else if (d <= TailStraight + drop) p = dropAt + Vector3.down * (0.05f + d - TailStraight);
                else
                {
                    float a = (d - TailStraight - drop) / radius;   // от вертикали плавно назад
                    p = hookStart + Vector3.down * (radius * Mathf.Sin(a)) + back * (radius * (1f - Mathf.Cos(a)));
                }
                drape[n++] = p;
            }

            // Хвост не должен быть в камнях. Проверяем с учётом ширины перьев: у корня приподнимаем,
            // ниже — отодвигаем назад, от камня; сдвиг копится, поэтому весь хвост ниже отходит целиком.
            Vector3 side = Vector3.Cross(Vector3.up, back);
            float shift = 0f;
            for (int i = 1; i < n; i++)
            {
                Vector3 p = drape[i] + back * shift;
                if (i * DrapeStep <= TailStraight)
                {
                    for (int k = 0; k < 20 && TailBlocked(p, side, back); k++) p += Vector3.up * 0.1f;
                }
                else
                {
                    for (int k = 0; k < 40 && TailBlocked(p, side, back); k++) { p += back * 0.25f; shift += 0.25f; }
                }
                drape[i] = p;
            }

            // Сглаживаем (концы на месте), чтобы стыки участков не читались, и ещё раз выталкиваем
            // точки, которые сглаживание вернуло в камень.
            for (int pass = 0; pass < 3; pass++)
            {
                Vector3 prev = drape[0];
                for (int i = 1; i < n - 1; i++)
                {
                    Vector3 cur = drape[i];
                    drape[i] = 0.25f * prev + 0.5f * cur + 0.25f * drape[i + 1];
                    prev = cur;
                }
            }
            for (int i = 1; i < n; i++)
            {
                for (int k = 0; k < 8 && TailBlocked(drape[i], side, back); k++) drape[i] += (i * DrapeStep <= TailStraight ? Vector3.up : back) * 0.2f;
                // Не ниже земли: на низком камне или когда птица лежит, хвост ложится на землю.
                drape[i].y = Mathf.Max(drape[i].y, TerrainHeight(drape[i]) + DrapeClearance);
            }
            bird.SetDrape(drape, n);
        }

        const float TailHalfWidth = 0.6f;    // перья хвоста в стороны от линии, м
        const float TailFront = 0.5f;        // перья выступают от линии к камню (вперёд), м
        const float TailThickness = 0.35f;   // толщина хвоста от линии, м
        readonly Collider[] tailHits = new Collider[16];

        // Задевает ли хвост в этой точке камень (террейн и героиня не считаются): точка, края перьев в стороны
        // и их передний край (к камню) не внутри камня и не ближе TailThickness к его поверхности.
        bool TailBlocked(Vector3 p, Vector3 side, Vector3 back)
        {
            for (int o = -1; o <= 2; o++)
            {
                Vector3 q = o == 2 ? p - back * TailFront : p + side * (TailHalfWidth * o);
                if (InsideRock(q)) return true;
                int count = Physics.OverlapSphereNonAlloc(q, TailThickness, tailHits, ~0, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < count; i++)
                {
                    var c = tailHits[i];
                    if (c is TerrainCollider || c.transform.IsChildOf(hero.Body)) continue;
                    return true;
                }
            }
            return false;
        }

        // Точка внутри камня: луч вверх первым упирается в его поверхность изнутри (с обратной стороны).
        static bool InsideRock(Vector3 q)
        {
            bool backfaces = Physics.queriesHitBackfaces;
            Physics.queriesHitBackfaces = true;
            bool inside = Physics.Raycast(q, Vector3.up, out var hit, 40f, ~0, QueryTriggerInteraction.Ignore) &&
                          !(hit.collider is TerrainCollider) && Vector3.Dot(hit.normal, Vector3.up) > 0f;
            Physics.queriesHitBackfaces = backfaces;
            return inside;
        }

        // Высота террейна под точкой (камни не считаются).
        float TerrainHeight(Vector3 point)
        {
            int count = Physics.RaycastNonAlloc(new Vector3(point.x, 300f, point.z), Vector3.down, groundHits, 600f, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
                if (groundHits[i].collider is TerrainCollider) return groundHits[i].point.y;
            return GroundHeight(point + Vector3.up * 50f, point.y);
        }

        // Из двух мест на валуне — то, где героиня у птицы за спиной (0 — facing/seat, 1 — facingAlt/seatAlt).
        int ChooseSide(int index, Vector3 heroPosition)
        {
            var p = perches[index];
            Vector3 toHero = Flat(heroPosition - p.top);
            return Vector3.Dot(p.facing, toHero) <= Vector3.Dot(p.facingAlt, toHero) ? 0 : 1;
        }

        Vector3 SeatOf(int index, int side) => side == 0 ? perches[index].seat : perches[index].seatAlt;
        Vector3 FacingOf(int index, int side) => side == 0 ? perches[index].facing : perches[index].facingAlt;

        void PoseAt(Vector3 top, Vector3 facing)
        {
            bool moved = (bird.transform.position - top).sqrMagnitude > 0.25f;
            PlaceBird(top, facing, 0f);
            bird.SetTailHang(1f);
            DrapeTail(facing);
            // Птицу переставили (а не долетела сама): хвост сразу в новой форме, а не тянется физикой с прежнего места.
            if (moved) bird.ResetTail();
        }

        // Кончик хвоста лежит на ровном дне, а не на соседнем камне и не в будущем русле лавы: героиня до него дойдёт.
        bool TailReachable(Vector3 top, Vector3 facing)
        {
            PoseAt(top, facing);
            Vector3 tip = bird.TailTip;
            // Луч сверху: если над кончиком камень, первым попадётся он.
            if (!Physics.Raycast(tip + Vector3.up * 6f, Vector3.down, out var hit, 6f + tuning.grabMaxHeight + 1f, ~0, QueryTriggerInteraction.Ignore))
                return false;
            // Под кончиком — земля на высоте ног героини, а не камень посередине.
            // Кончик в воздухе на высоте рук героини, а не над ямой и не в земле.
            float tipAbove = tip.y - hit.point.y;
            if (tipAbove > tuning.grabMaxHeight || tipAbove < TailAirGap) return false;
            if (hit.normal.y < 0.75f || hit.point.y - transform.position.y > 1.6f) return false;
            // Кончик — над открытой землёй, а не над валуном или укрытием: там героиня до него не дотянется.
            if (!(hit.collider is TerrainCollider)) return false;
            if (lava != null && lava.dryBeds != null && hit.transform.IsChildOf(lava.dryBeds.transform)) return false;
            return !RockBehindSeat(top, facing);
        }

        // Перья у корня хвоста свисают почти до лап: если сразу за птицей камень выше места, где она стоит,
        // они уходят в камень — на такой разворот птица не садится.
        bool RockBehindSeat(Vector3 seat, Vector3 facing)
        {
            Vector3 back = -Flat(facing).normalized;
            Vector3 side = Vector3.Cross(Vector3.up, back);
            for (float s = 0.2f; s <= 1.2f; s += 0.2f)
                for (int o = -1; o <= 1; o++)
                {
                    Vector3 q = seat + back * s + side * (0.4f * o) + Vector3.up * 3f;
                    int count = Physics.RaycastNonAlloc(q, Vector3.down, groundHits, 3f, ~0, QueryTriggerInteraction.Ignore);
                    for (int i = 0; i < count; i++)
                    {
                        var c = groundHits[i].collider;
                        if (c is TerrainCollider || c.transform.IsChildOf(bird.transform) || c.transform.IsChildOf(hero.Body)) continue;
                        if (groundHits[i].point.y > seat.y + 0.2f) return true;
                    }
                }
            return false;
        }

        // ---------------- Хвост и полёт ----------------

        void Grab()
        {
            bird.SetAlarm(0f);
            grabGround = hero.Body.position;
            carrying = true;
            bird.PinTip(true);
            hero.SetCarried(true);
            TailInReach = false;
            bird.SetGlow(0f);   // с героиней на хвосте птица не светится
            moveFrom = bird.transform.position;
            faceFrom = bird.transform.forward;
            ringPos = NearestOnRing(moveFrom);
            ringStart = ringPos;
            PlanTakeOff();
            timer = 0f;
            holdTime = 0f;
            Enter(ZhuqueState.TakeOff);
            Grabbed?.Invoke(grabGround);
            TookOff?.Invoke(moveFrom);
        }

        void TickTakeOff(float dt)
        {
            timer += dt;
            float t = Mathf.Clamp01(timer / takeOffDuration);
            TakeOffPose(t, ref takeOffTurn);
            if (t < 0.6f) bird.Play(ZhuqueBird.TakeOff, -1f, takeOffDuration * 0.6f);
            else bird.Play(ZhuqueBird.Flight);
            bird.LookForward(dt);
            CarryHero();
            if (t >= 1f)
            {
                timer = 0f;
                Enter(ZhuqueState.Flight);
            }
        }

        // Поза птицы на взлёте с героиней, t = 0…1. Точка на кольце уже бежит вперёд с нарастающей скоростью
        // (к концу — flightSpeed), поэтому переход в полёт без рывка. Если героиню протащило бы сквозь камень
        // (она висит за краем валуна, а кольцо — по другую сторону), птица сначала поднимается на takeOffLift
        // почти отвесно и только потом летит к кольцу.
        void TakeOffPose(float t, ref float turn)
        {
            float s = Mathf.SmoothStep(0f, 1f, t);
            ringPos = ringStart + tuning.flightDirection * tuning.flightSpeed * takeOffDuration * t * t * 0.5f;
            Vector3 target = RingPoint(ringPos);
            float across = takeOffLift > 0f ? Mathf.SmoothStep(0f, 1f, (t - TakeOffClimb) / (1f - TakeOffClimb)) : s;
            Vector3 position = Vector3.Lerp(moveFrom, target, across);
            float bump = Mathf.SmoothStep(0f, 1f, t / TakeOffClimb) * (1f - Mathf.SmoothStep(0f, 1f, (t - 0.55f) / 0.45f));
            position.y = Mathf.Lerp(moveFrom.y, target.y, s) + takeOffLift * bump;
            Vector3 facing = Turn(faceFrom, RingFacing(ringPos), s, ref turn);
            // Нос вверх плавно, от нуля: резкий наклон в первый же кадр бросал хвост с героиней вниз, под землю.
            PlaceBird(position, facing, -25f * Mathf.Sin(Mathf.PI * Mathf.Min(1f, t * 1.5f)) * (1f - t));
            bird.SetTailHang(1f - s);
        }

        const float TakeOffClimb = 0.35f;     // доля взлёта, за которую птица набирает запас высоты
        const float TakeOffLiftStep = 1.5f;   // шаг подбора запаса высоты, м
        const float TakeOffLiftMax = 18f;     // больше не поднимается, м

        // Подбираем запас высоты: проигрываем взлёт по точкам и смотрим, не проходит ли героиня сквозь камень
        // (середина её тела внутри камня).
        // Чем выше подъём, тем дольше взлёт (вертикальная скорость та же).
        void PlanTakeOff()
        {
            Vector3 position = bird.transform.position;
            Quaternion rotation = bird.transform.rotation;
            for (takeOffLift = 0f; takeOffLift <= TakeOffLiftMax; takeOffLift += TakeOffLiftStep)
            {
                takeOffDuration = tuning.takeOffTime * (1f + takeOffLift / 12f);
                if (!TakeOffHitsRock()) break;
            }
            takeOffLift = Mathf.Min(takeOffLift, TakeOffLiftMax);
            takeOffDuration = tuning.takeOffTime * (1f + takeOffLift / 12f);
            takeOffTurn = TurnAngle(faceFrom, RingFacing(ringStart));
            ringPos = ringStart;
            bird.transform.SetPositionAndRotation(position, rotation);
            bird.SetTailHang(1f);
        }

        bool TakeOffHitsRock()
        {
            float turn = TurnAngle(faceFrom, RingFacing(ringStart));
            for (float t = 0.1f; t <= 1.001f; t += 0.05f)
            {
                TakeOffPose(t, ref turn);
                Vector3 feet = bird.TailTip + Vector3.down * tuning.hangDrop;
                // Задеть край камня не страшно — выталкивает PushOutOfRocks; плохо, когда героиню тащит сквозь камень.
                if (InsideRock(feet + Vector3.up * 0.9f)) return true;
            }
            return false;
        }

        readonly Collider[] heroHits = new Collider[16];
        CapsuleCollider heroProbe;

        // Выталкиваем героиню (капсула её размера) из камней. Террейн, лава, засыпанные русла и сама птица не в счёт.
        Vector3 PushOutOfRocks(Vector3 feet)
        {
            if (heroProbe == null)
            {
                var go = new GameObject("Zhuque hero probe") { hideFlags = HideFlags.HideAndDontSave, layer = 2 };
                go.transform.position = new Vector3(0f, -10000f, 0f);
                heroProbe = go.AddComponent<CapsuleCollider>();
                heroProbe.isTrigger = true;
                heroProbe.radius = HeroRadius;
                heroProbe.height = HeroHeight;
                heroProbe.center = Vector3.up * (HeroHeight * 0.5f);
            }
            for (int pass = 0; pass < 3; pass++)
            {
                Vector3 push = Vector3.zero;
                int count = Physics.OverlapCapsuleNonAlloc(feet + Vector3.up * HeroRadius, feet + Vector3.up * (HeroHeight - HeroRadius), HeroRadius, heroHits, ~0, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < count; i++)
                {
                    var c = heroHits[i];
                    if (!IsRock(c)) continue;
                    if (Physics.ComputePenetration(heroProbe, feet, Quaternion.identity, c, c.transform.position, c.transform.rotation, out var dir, out float depth))
                        push += dir * depth;
                }
                if (push.sqrMagnitude < 1e-6f) break;
                feet += push;
            }
            return feet;
        }

        bool IsRock(Collider c)
        {
            if (c == null || c.isTrigger || c is TerrainCollider || c is CharacterController) return false;
            if (c.transform.IsChildOf(bird.transform) || c.transform.IsChildOf(hero.Body)) return false;
            if (lava != null && lava.dryBeds != null && c.transform.IsChildOf(lava.dryBeds.transform)) return false;
            if (lava != null && lava.rivers != null && System.Array.IndexOf(lava.rivers, c as MeshCollider) >= 0) return false;
            return true;
        }

        const float HeroRadius = 0.35f, HeroHeight = 1.7f;   // капсула героини для выталкивания из камней, м

        void TickFlight(float dt)
        {
            timer += dt;
            holdTime += dt;
            FlyRing(dt, 0f);
            UpdateShadow();
            CarryHero();

            if (hero.AttackPressed)
            {
                if (Cuts + 1 < tuning.requiredCuts) { CutFeathers(); return; }
                if (ShadowOverLava) { FinalCut(); return; }
                // Над сушей четвёртый удар не проходит: птица стряхивает героиню раньше, чем перья отсечены.
                CutRefused?.Invoke(hero.AimPoint);
                BeginShakeOff();
                return;
            }
            if (holdTime >= tuning.holdLimit) BeginShakeOff();
        }

        void BeginShakeOff()
        {
            timer = 0f;
            Enter(ZhuqueState.ShakeOff);
        }

        void TickShakeOff(float dt)
        {
            timer += dt;
            holdTime += dt;
            float wobble = Mathf.Sin(timer * 38f) * 22f * Mathf.Clamp01(1f - timer / Mathf.Max(0.01f, tuning.shakeOffTime * 1.5f));
            FlyRing(dt, wobble);
            UpdateShadow();
            CarryHero();
            // Сбрасывает только над сушей: смерти от стряхивания не бывает (GDD-SU §08).
            bool overLava = lava != null && lava.IsOverLava(hero.Body.position);
            if (timer >= tuning.shakeOffTime && !overLava)
            {
                Release();
                ShakenOff?.Invoke(hero.Body.position);
                FlyToPerch(ChoosePerch(hero.Body.position));
            }
        }

        void FlyRing(float dt, float roll)
        {
            ringPos += tuning.flightDirection * tuning.flightSpeed * dt;
            Vector3 position = RingPoint(ringPos) + Vector3.up * (Mathf.Sin(Time.time * 2.1f) * 0.35f);
            PlaceBird(position, RingFacing(ringPos), 0f, roll);
            bird.Play(ZhuqueBird.Flight);
            bird.SetTailHang(0f);
            bird.LookForward(dt);
        }

        void CarryHero()
        {
            bird.Apply();
            Vector3 facing = Flat(bird.transform.forward);
            Vector3 at = bird.TailEnd + Vector3.down * tuning.hangDrop;   // висит на видимом кончике
            // Ноги никогда не уходят под землю: у самой земли (взлёт с валуна) героиня стоит на ней.
            at.y = Mathf.Max(at.y, GroundUnderHero(at));
            // И не заходит в камни: кончик хвоста висит у самой стенки валуна, героиня шире него.
            at = PushOutOfRocks(at);
            hero.Body.SetPositionAndRotation(at, facing.sqrMagnitude > 0.001f ? Quaternion.LookRotation(facing) : hero.Body.rotation);
        }

        void Release()
        {
            throwing = false;
            if (!carrying) return;
            carrying = false;
            bird.PinTip(false);
            hero.SetCarried(false);
        }

        // Отсечённые перья отбрасывают героиню назад, против хода полёта, по дуге. Приземляется на ровное
        // дно — не в лаву, не в засыпанное русло и не на валун.
        void ThrowHero()
        {
            Vector3 back = -Flat(bird.transform.forward).normalized;
            throwFrom = hero.Body.position;
            throwTo = SafeLanding(throwFrom, back, tuning.cutThrowDistance);
            throwFacing = -back;
            throwTimer = 0f;
            throwing = true;
        }

        void TickThrow(float dt)
        {
            throwTimer += dt;
            float t = Mathf.Clamp01(throwTimer / tuning.cutThrowTime);
            Vector3 position = Vector3.Lerp(throwFrom, throwTo, t);
            // Взлёт вверх-назад, потом падение: высоту дуги отсчитываем от линии «откуда — куда».
            position.y += tuning.cutThrowHeight * 4f * t * (1f - t);
            hero.Body.SetPositionAndRotation(position, Quaternion.LookRotation(throwFacing));
            if (t >= 1f) Release();
        }

        Vector3 SafeLanding(Vector3 from, Vector3 back, float distance)
        {
            foreach (float k in new[] { 1f, 0.75f, 1.3f, 0.5f, 1.6f, 0.25f, 0f })
            {
                Vector3 p = from + back * (distance * k);
                if (!Physics.Raycast(new Vector3(p.x, from.y + 20f, p.z), Vector3.down, out var hit, 60f, ~0, QueryTriggerInteraction.Ignore))
                    continue;
                if (hit.normal.y < 0.75f || hit.transform.IsChildOf(perchRoot)) continue;
                if (lava != null && (lava.IsOverLava(hit.point, 1f) ||
                                     (lava.dryBeds != null && hit.transform.IsChildOf(lava.dryBeds.transform)))) continue;
                return hit.point + Vector3.up * 0.05f;
            }
            return grabGround;
        }

        // Тень падает по лучу солнца: игрок видит именно её, по ней и решается четвёртый удар.
        void UpdateShadow()
        {
            Vector3 dir = sun != null ? sun.transform.forward : Vector3.down;
            if (dir.y > -0.05f) dir = Vector3.down;
            Vector3 from = bird.BodyCenter;
            bool hitGround = Physics.Raycast(from, dir, out var hit, 300f, ~0, QueryTriggerInteraction.Ignore);
            ShadowPoint = hitGround ? hit.point : from + dir * ((from.y - transform.position.y) / -dir.y);
            ShadowNormal = hitGround ? hit.normal : Vector3.up;
            // Засчитываем и тень, и саму птицу над рекой: солнце низкое, тень ложится метров на 9 в сторону,
            // а игрок естественно бьёт, когда над лавой птица.
            ShadowOverLava = lava != null && (lava.IsOverLava(ShadowPoint, tuning.lavaShadowMargin) ||
                                              lava.IsOverLava(bird.BodyCenter, tuning.lavaShadowMargin));
        }

        // ---------------- Удары 1–3: падение и восстановление ----------------

        void CutFeathers()
        {
            Cuts++;
            bird.SetTailLength(tuning.cutStub);
            Vector3 at = hero.AimPoint;
            ThrowHero();
            Cut?.Invoke(Cuts, tuning.requiredCuts, at);

            moveFrom = bird.transform.position;
            Vector3 ahead = moveFrom + RingFacing(ringPos) * (tuning.flightSpeed * tuning.fallTime * 0.45f);
            moveTo = new Vector3(ahead.x, GroundHeight(ahead + Vector3.up * 30f, ahead.y - tuning.flightHeight), ahead.z);
            faceFrom = bird.transform.forward;
            // Заваливается на бок, но не переворачивается: крылья ложатся на землю (ZhuqueBird.SetLying).
            fallRoll = UnityEngine.Random.value < 0.5f ? -30f : 30f;
            timer = 0f;
            Enter(ZhuqueState.Falling);
        }

        void TickFalling(float dt)
        {
            timer += dt;
            float t = Mathf.Clamp01(timer / tuning.fallTime);
            // Теряет управление: летит вперёд по инерции и всё круче вниз.
            Vector3 position = Vector3.Lerp(moveFrom, moveTo, t);
            position.y = Mathf.Lerp(moveFrom.y, moveTo.y, t * t);
            PlaceBird(position, faceFrom, Mathf.Lerp(0f, 35f, t), fallRoll * Mathf.SmoothStep(0f, 1f, t));
            bird.Play(ZhuqueBird.Fall, -1f, tuning.fallTime);
            // Упала — лежит: дышит, пытается поднять голову (ZhuqueBird.SetLying).
            bird.SetLying(Mathf.SmoothStep(0f, 1f, (timer - tuning.fallTime) / 0.4f));
            bird.SetTailHang(t);
            DrapeTail(bird.transform.forward);
            if (timer >= tuning.fallTime && State == ZhuqueState.Falling && timer - dt < tuning.fallTime)
                BirdFell?.Invoke(moveTo);
            if (timer >= tuning.fallTime + tuning.lieTime)
            {
                timer = 0f;
                lavaRequested = false;
                Enter(ZhuqueState.Regenerate);
                Regenerating?.Invoke(moveTo, Cuts == tuning.lavaAfterCuts && lava != null && !lava.IsOpen);
            }
        }

        void TickRegenerate(float dt)
        {
            bool opensLava = Cuts == tuning.lavaAfterCuts && lava != null && !lava.IsOpen;
            // Лава не открывается под ногами: если героиня стоит в засыпанном русле, птица ждёт в полной вспышке.
            bool heroInChannel = opensLava && !lavaRequested && lava.IsOnDryChannel(hero.Body.position);
            if (!(heroInChannel && timer + dt >= tuning.regenerateTime * 0.5f)) timer += dt;
            float t = Mathf.Clamp01(timer / tuning.regenerateTime);
            // Встаёт (первая треть), расправляет крылья и вспыхивает, хвост отрастает из огня.
            float up = Mathf.SmoothStep(0f, 1f, t / 0.3f);
            PlaceBird(moveTo, faceFrom, Mathf.Lerp(35f, 0f, up), fallRoll * (1f - up));
            float open = Mathf.SmoothStep(0f, 1f, (t - 0.2f) / 0.25f) * (1f - Mathf.SmoothStep(0f, 1f, (t - 0.85f) / 0.15f));
            if (t < 0.3f) bird.Play(ZhuqueBird.Rise, -1f, tuning.regenerateTime * 0.3f);
            else bird.Play(ZhuqueBird.Recovery, -1f, tuning.regenerateTime * 0.7f);
            bird.SetLying(1f - Mathf.SmoothStep(0f, 1f, t / 0.15f));
            // Вспышка (VFX SU-05): резкий подъём, когда крылья расправляются, короткий пик и спад. На пике
            // начинает отрастать хвост — его появление прячет огненный выброс (SU-06, событие Ignited).
            float flare = Mathf.SmoothStep(0f, 1f, (t - IgniteAt + 0.12f) / 0.12f) * (1f - Mathf.SmoothStep(0f, 1f, (t - IgniteAt - 0.05f) / 0.55f));
            bird.SetGlow(flare * tuning.regenerateGlow);
            if (t >= IgniteAt && timer - dt < IgniteAt * tuning.regenerateTime) Ignited?.Invoke(bird.BodyCenter);
            bird.SetTailHang(1f);
            DrapeTail(bird.transform.forward);
            bird.SetTailLength(Mathf.Lerp(tuning.cutStub, 1f, Mathf.SmoothStep(0f, 1f, (t - IgniteAt) / 0.55f)));
            bird.LookForward(dt);

            // Третье восстановление открывает лавовые реки из центра долины.
            if (!lavaRequested && t >= 0.5f && opensLava && !heroInChannel)
            {
                lavaRequested = true;
                lava.Open(tuning.lavaOpenTime);
                lavaArmedAt = Time.time + tuning.lavaOpenTime;
                LavaOpened?.Invoke(moveTo);
            }
            if (t >= 1f)
            {
                bird.SetGlow(0f);
                FlyToPerch(ChoosePerch(hero.Body.position));
            }
        }

        public const float IgniteAt = 0.35f;   // доля восстановления: пик вспышки и начало роста хвоста

        // ---------------- Удар 4: над лавой ----------------

        void FinalCut()
        {
            Cuts++;
            bird.SetTailLength(tuning.cutStub);
            Vector3 at = hero.AimPoint;
            ThrowHero();
            Cut?.Invoke(Cuts, tuning.requiredCuts, at);
            moveFrom = bird.transform.position;
            // Падает туда, где под ней лава: сначала прямо вниз, иначе — на тень.
            Vector3 below = bird.BodyCenter;
            moveTo = lava != null && lava.IsOverLava(below, tuning.lavaShadowMargin)
                ? new Vector3(below.x, GroundHeight(below, ShadowPoint.y), below.z)
                : ShadowPoint;
            faceFrom = bird.transform.forward;
            fallRoll = UnityEngine.Random.value < 0.5f ? -110f : 110f;
            timer = 0f;
            Enter(ZhuqueState.FinalFall);
            FellIntoLava?.Invoke(moveTo);
        }

        void TickFinalFall(float dt)
        {
            timer += dt;
            float t = Mathf.Clamp01(timer / tuning.finalFallTime);
            Vector3 position = Vector3.Lerp(moveFrom, moveTo, t * t);
            PlaceBird(position, faceFrom, Mathf.Lerp(0f, 60f, t), fallRoll * t);
            bird.Play(ZhuqueBird.Fall, -1f, tuning.finalFallTime);
            bird.SetGlow(t * 0.8f);
            // Белая янь — за несколько кадров до того, как птица коснётся магмы.
            if (timer >= tuning.finalFallTime - tuning.cutLeadTime) Win();
        }

        void Win()
        {
            Enter(ZhuqueState.Defeated);
            hero.SetInputEnabled(false);
            if (cover != null) cover.Run(YinYangCover.Kind.Yang, VictoryUnderCover, () => hero.SetInputEnabled(true));
            else { ApplyVictory(); hero.SetInputEnabled(true); }
        }

        IEnumerator VictoryUnderCover()
        {
            ApplyVictory();
            yield return null;
        }

        void ApplyVictory()
        {
            // Перья отделились вместе с героиней; где именно она приземлится, GDD не решил (§12) — ставим на берег,
            // откуда она схватила хвост.
            Release();
            hero.Teleport(throwTo, hero.Body.rotation);
            bird.gameObject.SetActive(false);
            if (lava != null) lava.HazardArmed = false;
            if (world != null) world.ApplyDead();
            SeasonProgress.MarkDefeated(Season.Summer);
            Defeated?.Invoke();
        }

        // ---------------- Смерть и сброс ----------------

        void OnHeroBurned()
        {
            if (State == ZhuqueState.HeroDown || State == ZhuqueState.Defeated) return;
            Enter(ZhuqueState.HeroDown);
            HeroKilled?.Invoke();
            hero.SetInputEnabled(false);
            if (cover != null) cover.Run(YinYangCover.Kind.Yin, RestartUnderCover, () => hero.SetInputEnabled(true));
            else { ResetEncounter(); hero.Teleport(heroStartPosition, heroStartRotation); hero.SetInputEnabled(true); }
        }

        IEnumerator RestartUnderCover()
        {
            ResetEncounter();
            hero.Teleport(heroStartPosition, heroStartRotation);
            yield return null;
        }

        // Настоящая смерть обнуляет всё (GDD-SU §08): 0/4, лавы нет, птица на исходном валуне.
        public void ResetEncounter()
        {
            Release();
            bird.SetLying(0f);
            Cuts = 0;
            Detection = 0f;
            holdTime = 0f;
            lavaArmedAt = -1f;
            lavaRequested = false;
            bird.gameObject.SetActive(true);
            bird.SetTailLength(1f);
            bird.SetGlow(0f);
            bird.SetAlarm(0f);
            if (lava != null) lava.SetDry();
            recentPerches.Clear();
            ringDirection = 0;
            Sit(startPerch, ChooseSide(startPerch, heroStartPosition));
            EncounterReset?.Invoke();
        }

        // ---------------- Кольцо ----------------

        void BuildRing()
        {
            if (flightRing == null || flightRing.Length < 3)
            {
                // Страховка: окружность по середине дна, если сетап не записал точки.
                flightRing = new Vector3[72];
                for (int i = 0; i < flightRing.Length; i++)
                {
                    float a = i * Mathf.PI * 2f / flightRing.Length;
                    flightRing[i] = transform.position + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 95f;
                }
            }
            ringAt = new float[flightRing.Length + 1];
            for (int i = 0; i < flightRing.Length; i++)
                ringAt[i + 1] = ringAt[i] + Vector3.Distance(flightRing[i], flightRing[(i + 1) % flightRing.Length]);
            ringLength = ringAt[flightRing.Length];
            BuildRingLift();
        }

        float[] ringLift;   // насколько поднять кольцо над flightHeight, м — через каждый метр
        const float RingClearance = 1.5f;    // зазор над камнем под героиней и птицей, м
        const float RingProbeRadius = 1.8f;  // полуширина коридора полёта (героиня, хвост), м
        const float RingClimbWindow = 14f;   // за сколько метров до камня птица начинает набирать высоту, м

        // Кольцо идёт по дну на flightHeight, но под ним бывают укрытия и насесты выше героини (она висит на
        // несколько метров ниже птицы и на хвост позади неё) — над ними птица плавно набирает высоту.
        void BuildRingLift()
        {
            int n = Mathf.CeilToInt(ringLength) + 1;
            var need = new float[n];
            ringLift = new float[n];
            // Где висит героиня в полёте относительно птицы: ниже на depth, позади по кольцу на trail.
            float hang = bird.TailSpan;
            Vector3 tip = bird.TailRoot - bird.transform.position + bird.transform.TransformDirection(bird.flightTailDirection.normalized) * hang;
            float depth = -(tip.y - tuning.hangDrop);
            float trail = Flat(tip).magnitude;
            for (int i = 0; i < n; i++)
            {
                Vector3 p = RingPoint(i);
                float top = ObstacleTop(p, p.y - tuning.flightHeight);
                need[i] = Mathf.Max(0f, Mathf.Max(top + depth, top + 1.5f) + RingClearance - p.y);
            }
            // Птица высоко и там, где камень под ней, и там, где под камнем будет висящая позади героиня.
            int lag = Mathf.RoundToInt(trail);
            int window = Mathf.RoundToInt(RingClimbWindow);
            var raw = new float[n];
            for (int i = 0; i < n; i++)
            {
                float m = 0f;
                for (int k = -window; k <= window + lag; k++)
                {
                    int j = ((i - tuning.flightDirection * k) % n + n) % n;
                    // Внутри [0, lag] — полный подъём (героиня ещё над камнем), за краями — плавный спуск.
                    float edge = k < 0 ? -k : k > lag ? k - lag : 0;
                    m = Mathf.Max(m, need[j] * Mathf.SmoothStep(1f, 0f, edge / Mathf.Max(1, window)));
                }
                raw[i] = m;
            }
            // Сглаживаем ступеньки.
            for (int i = 0; i < n; i++)
            {
                float sum = 0f;
                for (int k = -4; k <= 4; k++) sum += raw[((i + k) % n + n) % n];
                ringLift[i] = Mathf.Max(raw[i] * 0.85f, sum / 9f);
            }
        }

        readonly RaycastHit[] ringHits = new RaycastHit[16];

        // Самая высокая точка камней в коридоре полёта над точкой (террейн, лава, птица, героиня не в счёт).
        float ObstacleTop(Vector3 at, float ground)
        {
            float top = ground;
            for (int k = 0; k <= 8; k++)
            {
                Vector3 o = k == 0 ? Vector3.zero : Quaternion.Euler(0f, k * 45f, 0f) * Vector3.forward * RingProbeRadius;
                int count = Physics.RaycastNonAlloc(new Vector3(at.x + o.x, ground + 60f, at.z + o.z), Vector3.down, ringHits, 70f, ~0, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < count; i++)
                {
                    var c = ringHits[i].collider;
                    if (!IsRock(c)) continue;
                    if (lava != null && lava.lavaRoot != null && c.transform.IsChildOf(lava.lavaRoot.transform)) continue;
                    top = Mathf.Max(top, ringHits[i].point.y);
                }
            }
            return top;
        }

        Vector3 RingPoint(float s)
        {
            int n = flightRing.Length;
            s = Mathf.Repeat(s, ringLength);
            int i = 0;
            while (i < n - 1 && ringAt[i + 1] < s) i++;
            float t = (s - ringAt[i]) / Mathf.Max(0.001f, ringAt[i + 1] - ringAt[i]);
            Vector3 p = CatmullRom(flightRing[(i - 1 + n) % n], flightRing[i], flightRing[(i + 1) % n], flightRing[(i + 2) % n], t);
            return p + Vector3.up * (tuning.flightHeight + RingLift(s));
        }

        float RingLift(float s)
        {
            if (ringLift == null || ringLift.Length < 2) return 0f;
            float x = Mathf.Repeat(s, ringLength);
            int i = Mathf.FloorToInt(x);
            return Mathf.Lerp(ringLift[i % ringLift.Length], ringLift[(i + 1) % ringLift.Length], x - i);
        }

        Vector3 RingFacing(float s)
        {
            Vector3 d = Flat(RingPoint(s + tuning.flightDirection * 1f) - RingPoint(s - tuning.flightDirection * 1f));
            return d.sqrMagnitude > 0.0001f ? d.normalized : Vector3.forward;
        }

        float NearestOnRing(Vector3 p)
        {
            float best = 0f, bestDistance = float.MaxValue;
            int n = flightRing.Length;
            for (int i = 0; i < n; i++)
            {
                Vector3 a = Flat(flightRing[i]), b = Flat(flightRing[(i + 1) % n]);
                Vector3 ab = b - a;
                float t = Mathf.Clamp01(Vector3.Dot(Flat(p) - a, ab) / Mathf.Max(0.0001f, ab.sqrMagnitude));
                float d = (a + ab * t - Flat(p)).sqrMagnitude;
                if (d < bestDistance) { bestDistance = d; best = ringAt[i] + t * (ringAt[i + 1] - ringAt[i]); }
            }
            return best;
        }

        static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * (2f * p1 + (p2 - p0) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (3f * p1 - p0 - 3f * p2 + p3) * t3);
        }

        // ---------------- Помощники ----------------

        void CollectPerches()
        {
            perches.Clear();
            startPerch = 0;
            foreach (Transform rock in perchRoot)
            {
                if (!rock.name.StartsWith("First perch") && !rock.name.StartsWith("Cover and perch")) continue;
                if (!FindSeat(rock, out Vector3 top)) continue;
                // Для каждого разворота птица садится у края камня спиной к краю — хвост свисает за край.
                Vector3 ring = RingFacing(NearestOnRing(top));
                var reachable = new List<Vector3>();
                var seats = new List<Vector3>();
                for (int turn = 0; turn < 360; turn += 15)
                {
                    Vector3 f = Quaternion.Euler(0f, turn, 0f) * ring;
                    Vector3 seat = EdgeSeat(rock, top, f);
                    if (TailReachable(seat, f)) { reachable.Add(f); seats.Add(seat); }
                }
                if (reachable.Count == 0)
                {
                    if (rock != firstPerch) continue;   // на такой валун птица не садится
                    reachable.Add(ring);
                    seats.Add(EdgeSeat(rock, top, ring));
                }
                // Из подходящих разворотов — два самых противоположных: так птица чаще сидит спиной к героине.
                int a = 0, b = 0;
                for (int i = 0; i < reachable.Count; i++)
                    for (int j = i + 1; j < reachable.Count; j++)
                        if (Vector3.Dot(reachable[i], reachable[j]) < Vector3.Dot(reachable[a], reachable[b])) { a = i; b = j; }
                if (rock == firstPerch) startPerch = perches.Count;
                perches.Add(new Perch { rock = rock, top = top, facing = reachable[a], facingAlt = reachable[b], seat = seats[a], seatAlt = seats[b] });
            }
            // Без явного стартового валуна — тот, что ближе всего к startPerchDistance от входа:
            // героиня успевает пробежать и увидеть птицу раньше, чем птица увидит её.
            if (firstPerch == null)
            {
                float best = float.MaxValue;
                for (int i = 0; i < perches.Count; i++)
                {
                    float d = Mathf.Abs(Flat(perches[i].top - heroStartPosition).magnitude - tuning.startPerchDistance);
                    if (d < best) { best = d; startPerch = i; }
                }
            }
            perch = startPerch;
        }

        // Место у края камня позади птицы: от лучшего места верхушки идём назад (против facing), пока под
        // лапами ровный камень почти той же высоты; встаём чуть не доходя до края — хвост свисает за край.
        static Vector3 EdgeSeat(Transform rock, Vector3 center, Vector3 facing)
        {
            var colliders = rock.GetComponentsInChildren<Collider>();
            Vector3 back = -Flat(facing).normalized;
            bool OnRock(Vector3 xz, out RaycastHit best)
            {
                best = default;
                float top = float.MinValue;
                var ray = new Ray(new Vector3(xz.x, center.y + 3f, xz.z), Vector3.down);
                foreach (var c in colliders)
                    if (c.Raycast(ray, out var h, 10f) && h.point.y > top) { top = h.point.y; best = h; }
                return top > float.MinValue;
            }
            float last = 0f;
            for (float s = 0.25f; s <= 12f; s += 0.25f)
            {
                if (!OnRock(center + back * s, out var hit) || hit.normal.y < 0.6f || hit.point.y < center.y - 0.6f) break;
                last = s;
            }
            float at = Mathf.Max(0f, last - 0.35f);
            return OnRock(center + back * at, out var seat) ? seat.point : center;
        }

        // Где на камне встать лапами: самое высокое ровное место верхушки, где под обеими лапами камень
        // (лучи сверху по сетке 0.5 м, только по коллайдерам этого камня). Птица стоит на камне, а не над ним.
        static bool FindSeat(Transform rock, out Vector3 seat)
        {
            seat = Vector3.zero;
            var colliders = rock.GetComponentsInChildren<Collider>();
            var renderers = rock.GetComponentsInChildren<Renderer>();
            if (colliders.Length == 0 || renderers.Length == 0) return false;
            var bounds = renderers[0].bounds;
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);

            bool OnRock(Vector3 xz, out RaycastHit best)
            {
                best = default;
                float top = float.MinValue;
                var ray = new Ray(new Vector3(xz.x, bounds.max.y + 1f, xz.z), Vector3.down);
                foreach (var c in colliders)
                    if (c.Raycast(ray, out var h, bounds.size.y + 2f) && h.point.y > top) { top = h.point.y; best = h; }
                return top > float.MinValue;
            }

            // Самая высокая точка камня: садимся только на верхушку (не ниже 1.2 м от неё),
            // а не на плоский выступ у подножия.
            float topY = float.MinValue;
            Vector3 peak = Vector3.zero;
            for (float x = bounds.min.x; x <= bounds.max.x; x += 0.5f)
            for (float z = bounds.min.z; z <= bounds.max.z; z += 0.5f)
                if (OnRock(new Vector3(x, 0f, z), out var hit) && hit.point.y > topY) { topY = hit.point.y; peak = hit.point; }
            if (topY == float.MinValue) return false;

            float bestScore = float.MinValue;
            var feet = new[] { new Vector3(0.5f, 0f, 0f), new Vector3(-0.5f, 0f, 0f), new Vector3(0f, 0f, 0.5f), new Vector3(0f, 0f, -0.5f) };
            for (float x = bounds.min.x; x <= bounds.max.x; x += 0.5f)
            for (float z = bounds.min.z; z <= bounds.max.z; z += 0.5f)
            {
                if (!OnRock(new Vector3(x, 0f, z), out var hit) || hit.point.y < topY - 1.2f || hit.normal.y < 0.6f) continue;
                // Под лапами (±0.5 м в стороны) — тоже камень и почти той же высоты.
                bool steady = true;
                foreach (var o in feet)
                    if (!OnRock(hit.point + o, out var h2) || Mathf.Abs(h2.point.y - hit.point.y) > 0.45f) { steady = false; break; }
                if (!steady) continue;
                // Ровнее и выше — лучше; при равном — ближе к середине камня.
                float score = hit.point.y + hit.normal.y - 0.05f * new Vector2(x - bounds.center.x, z - bounds.center.z).magnitude;
                if (score > bestScore) { bestScore = score; seat = hit.point; }
            }
            if (bestScore == float.MinValue) seat = peak;   // ровного места наверху нет — на самую вершину
            return true;
        }

        void PlaceBird(Vector3 position, Vector3 facing, float pitch, float roll = 0f)
        {
            facing = Flat(facing);
            var heading = facing.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(facing) : bird.transform.rotation;
            bird.transform.SetPositionAndRotation(position, heading * Quaternion.Euler(pitch, 0f, roll));
        }

        readonly RaycastHit[] groundHits = new RaycastHit[16];

        // Земля под точкой, не считая саму героиню. Луч — сверху, с высоты птицы: сама точка может быть
        // уже под землёй, и луч из неё землю не найдёт.
        float GroundUnderHero(Vector3 point)
        {
            // Луч — от уровня героини (сейчас или куда её несёт), а не сверху: иначе у стенки валуна «землёй»
            // оказывалась его верхушка, и героиню забрасывало на камень.
            var from = new Vector3(point.x, Mathf.Max(point.y, hero.Body.position.y) + 1.5f, point.z);
            int count = Physics.RaycastNonAlloc(from, Vector3.down, groundHits, 400f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MinValue;
            for (int i = 0; i < count; i++)
            {
                var h = groundHits[i];
                if (h.transform == hero.Body || h.transform.IsChildOf(hero.Body) || h.transform.IsChildOf(bird.transform)) continue;
                if (h.point.y > best) best = h.point.y;
            }
            return best;
        }

        static float GroundHeight(Vector3 from, float fallback)
        {
            return Physics.Raycast(from, Vector3.down, out var hit, 200f, ~0, QueryTriggerInteraction.Ignore) ? hit.point.y : fallback;
        }

        void Enter(ZhuqueState next)
        {
            State = next;
            StateChanged?.Invoke(next);
        }

        static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        void OnGUI()
        {
            if (!showDebug || tuning == null) return;
            GUI.depth = -10;
            var text = $"ЧЖУЦЮЭ (F3)\nСостояние: {State}\nОтсечено: {Cuts}/{tuning.requiredCuts}\n" +
                       $"Видит героиню: {(HeroInSight ? "да" : "нет")}  {Detection:0.00}\n" +
                       $"Хвост рядом: {(TailInReach ? "да (ЛКМ)" : "нет")}\n" +
                       $"Висит: {HoldTime:0.0}/{tuning.holdLimit:0} с\n" +
                       $"Лава: {(lava != null && lava.IsOpen ? "открыта" : "нет")}  Тень над лавой: {(ShadowOverLava ? "да" : "нет")}";
            GUI.Box(new Rect(12, 12, 260, 128), text);
        }

        void OnDrawGizmosSelected()
        {
            if (flightRing == null || flightRing.Length < 2) return;
            Gizmos.color = new Color(1f, 0.5f, 0.15f);
            float h = tuning != null ? tuning.flightHeight : 0f;
            for (int i = 0; i < flightRing.Length; i++)
                Gizmos.DrawLine(flightRing[i] + Vector3.up * h, flightRing[(i + 1) % flightRing.Length] + Vector3.up * h);
        }
#endif
    }
}
