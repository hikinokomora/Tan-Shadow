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
        public bool ShadowOverLava { get; private set; }
        public float HoldTime => State == ZhuqueState.Flight || State == ZhuqueState.ShakeOff ? holdTime : 0f;

        struct Perch
        {
            public Transform rock;
            public Vector3 top;
            public Vector3 facing;
        }

        readonly List<Perch> perches = new List<Perch>();
        int perch, startPerch;
        float[] ringAt;               // накопленная длина кольца у каждой точки
        float ringLength, ringPos;    // ringPos — где птица на кольце, м
        Vector3 heroStartPosition;
        Quaternion heroStartRotation;

        float timer, holdTime, moveDuration;
        Vector3 moveFrom, moveTo, faceFrom, faceTo;
        float arcHeight, fallRoll;
        Vector3 grabGround;           // где стояла героиня, когда схватила хвост: безопасная земля
        bool carrying, lavaRequested, shakeAfterRefuse;
        float lavaArmedAt = -1f;
        float lavaGlow;
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
            bird.SetTailGlow(TailInReach ? 0.6f : 0f);
            if (TailInReach && hero.AttackPressed)
            {
                Grab();
                return;
            }
            if (Detection >= 1f)
            {
                timer = tuning.alertTime;
                Enter(ZhuqueState.Alert);
                Alerted?.Invoke(bird.EyePoint);
            }
        }

        void TickAlert(float dt)
        {
            bird.LookAt(hero.AimPoint, 150f, dt);
            bird.SetWings(0.45f, 0f);
            bird.SetGlow(0f);
            bird.SetAlarm(1f);
            bird.SetTailGlow(0f);
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
            Vector3 tip = bird.TailTip;
            Vector3 feet = hero.Body.position;
            float above = tip.y - feet.y;
            return Flat(tip - feet).magnitude <= tuning.grabRadius && above <= tuning.grabMaxHeight && above >= -1f;
        }

        // ---------------- Перелёт ----------------

        int ChoosePerch(Vector3 heroPosition)
        {
            int best = -1, farthest = -1;
            float bestScore = float.MaxValue, farDistance = -1f;
            for (int i = 0; i < perches.Count; i++)
            {
                if (i == perch) continue;
                float d = Flat(perches[i].top - heroPosition).magnitude;
                if (d > farDistance) { farDistance = d; farthest = i; }
                if (d < tuning.minRelocateDistance) continue;
                // Не на другой конец кольца, а на «некотором расстоянии» (GDD-SU): ближе всего к предпочтительному.
                float score = Mathf.Abs(d - tuning.preferredRelocateDistance);
                if (score < bestScore) { bestScore = score; best = i; }
            }
            return best >= 0 ? best : Mathf.Max(0, farthest);
        }

        void FlyToPerch(int index)
        {
            perch = index;
            moveFrom = bird.transform.position;
            moveTo = perches[index].top;
            faceFrom = bird.transform.forward;
            bird.SetAlarm(0f);
            faceTo = perches[index].facing;
            float distance = Vector3.Distance(moveFrom, moveTo);
            moveDuration = Mathf.Max(0.8f, distance / tuning.relocateSpeed);
            arcHeight = tuning.relocateArcHeight * Mathf.Clamp01(distance / 30f);
            timer = 0f;
            Detection = 0f;
            HeroInSight = false;
            TailInReach = false;
            Enter(ZhuqueState.Relocating);
            TookOff?.Invoke(moveFrom);
        }

        void TickRelocating(float dt)
        {
            timer += dt;
            float t = Mathf.Clamp01(timer / moveDuration);
            float e = Mathf.SmoothStep(0f, 1f, t);
            Vector3 position = Vector3.Lerp(moveFrom, moveTo, e) + Vector3.up * (arcHeight * 4f * t * (1f - t));
            Vector3 travel = Flat(moveTo - moveFrom);
            Vector3 facing = travel.sqrMagnitude > 0.01f ? travel.normalized : faceTo;
            facing = Vector3.Slerp(Vector3.Slerp(faceFrom, facing, Mathf.Clamp01(t * 5f)), faceTo, Mathf.SmoothStep(0f, 1f, (t - 0.75f) / 0.25f));
            PlaceBird(position, facing, Mathf.Lerp(-12f, 10f, t));
            bird.SetWings(Mathf.Lerp(1f, 0f, Mathf.SmoothStep(0f, 1f, (t - 0.8f) / 0.2f)), Flap(timer, 4f, 35f) * (1f - t * t));
            bird.SetTailHang(Mathf.SmoothStep(0f, 1f, (t - 0.7f) / 0.3f));
            bird.SetTailLength(Mathf.MoveTowards(bird.TailLength, 1f, dt));
            bird.SetGlow(0f);
            bird.SetTailGlow(0f);
            bird.LookForward(dt);
            if (t >= 1f) Sit(perch);
        }

        void Sit(int index)
        {
            perch = index;
            PoseOnPerch(index);
            Detection = 0f;
            Enter(ZhuqueState.Perched);
            Landed?.Invoke(perches[index].top);
        }

        void PoseOnPerch(int index) => PoseAt(perches[index].top, perches[index].facing);

        void PoseAt(Vector3 top, Vector3 facing)
        {
            PlaceBird(top, facing, 0f);
            bird.SetWings(0f, 0f);
            bird.SetTailHang(1f);
            bird.SetTailDrop(1f);
            bird.Apply();
            // Хвост свисает до земли с любого валуна: вытягиваем его под высоту этого камня.
            Vector3 tip = bird.TailTip;
            float ground = GroundHeight(new Vector3(tip.x, top.y + 0.5f, tip.z), top.y - 3.06f);
            float pivotY = top.y + 1.27f;
            bird.SetTailDrop(Mathf.Clamp((pivotY - ground - 0.15f) / 4.33f, 0.6f, 2.6f));
            bird.Apply();
        }

        // Кончик хвоста лежит на ровном дне, а не на соседнем камне и не в будущем русле лавы: героиня до него дойдёт.
        bool TailReachable(Vector3 top, Vector3 facing)
        {
            PoseAt(top, facing);
            Vector3 tip = bird.TailTip;
            if (!Physics.Raycast(tip + Vector3.up * 0.6f, Vector3.down, out var hit, 2.5f, ~0, QueryTriggerInteraction.Ignore))
                return false;
            if (hit.normal.y < 0.75f || hit.point.y - transform.position.y > 1.6f) return false;
            if (hit.transform.IsChildOf(perchRoot)) return false;
            if (lava != null && lava.dryBeds != null && hit.transform.IsChildOf(lava.dryBeds.transform)) return false;
            return true;
        }

        // ---------------- Хвост и полёт ----------------

        void Grab()
        {
            bird.SetAlarm(0f);
            grabGround = hero.Body.position;
            carrying = true;
            hero.SetCarried(true);
            TailInReach = false;
            bird.SetTailGlow(0f);
            moveFrom = bird.transform.position;
            faceFrom = bird.transform.forward;
            ringPos = NearestOnRing(moveFrom);
            timer = 0f;
            holdTime = 0f;
            Enter(ZhuqueState.TakeOff);
            Grabbed?.Invoke(grabGround);
            TookOff?.Invoke(moveFrom);
        }

        void TickTakeOff(float dt)
        {
            timer += dt;
            float t = Mathf.Clamp01(timer / tuning.takeOffTime);
            // Пока птица набирает высоту, точка на кольце уже бежит вперёд — переход в полёт без рывка.
            ringPos += tuning.flightDirection * tuning.flightSpeed * dt * t;
            Vector3 target = RingPoint(ringPos);
            Vector3 position = Vector3.Lerp(moveFrom, target, Mathf.SmoothStep(0f, 1f, t));
            Vector3 facing = Vector3.Slerp(faceFrom, RingFacing(ringPos), Mathf.SmoothStep(0f, 1f, t));
            PlaceBird(position, facing, Mathf.Lerp(-25f, 0f, t));
            bird.SetWings(1f, Flap(timer, 5f, 40f));
            bird.SetTailHang(1f - Mathf.SmoothStep(0f, 1f, t));
            bird.LookForward(dt);
            CarryHero();
            if (t >= 1f)
            {
                timer = 0f;
                Enter(ZhuqueState.Flight);
            }
        }

        void TickFlight(float dt)
        {
            timer += dt;
            holdTime += dt;
            FlyRing(dt, 0f);
            UpdateShadow();
            GlowOverLava(dt);
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
            bird.SetWings(1f, Flap(Time.time, 2.6f, 28f));
            bird.SetTailHang(0f);
            bird.LookForward(dt);
        }

        // Над рекой птицу снизу подсвечивает лава — видно, даже когда тень теряется.
        void GlowOverLava(float dt)
        {
            lavaGlow = Mathf.MoveTowards(lavaGlow, ShadowOverLava ? tuning.overLavaGlow : 0f, dt * 3f);
            bird.SetGlow(lavaGlow);
        }

        void CarryHero()
        {
            bird.Apply();
            Vector3 facing = Flat(bird.transform.forward);
            hero.Body.SetPositionAndRotation(bird.TailTip + Vector3.down * tuning.hangDrop,
                facing.sqrMagnitude > 0.001f ? Quaternion.LookRotation(facing) : hero.Body.rotation);
        }

        void Release()
        {
            throwing = false;
            if (!carrying) return;
            carrying = false;
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
            ShadowPoint = Physics.Raycast(from, dir, out var hit, 300f, ~0, QueryTriggerInteraction.Ignore)
                ? hit.point
                : from + dir * ((from.y - transform.position.y) / -dir.y);
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
            fallRoll = UnityEngine.Random.value < 0.5f ? -80f : 80f;
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
            bird.SetWings(Mathf.Lerp(1f, 0.4f, t), Mathf.Sin(timer * 30f) * 25f * (1f - t));
            bird.SetTailHang(t);
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
            bird.SetWings(open, Mathf.Sin(timer * 9f) * 8f * open);
            bird.SetGlow(Mathf.Sin(Mathf.PI * Mathf.Clamp01((t - 0.2f) / 0.75f)) * 1.6f);
            bird.SetTailHang(1f);
            bird.SetTailDrop(1f);
            bird.SetTailLength(Mathf.Lerp(tuning.cutStub, 1f, Mathf.SmoothStep(0f, 1f, (t - 0.35f) / 0.55f)));
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
            bird.SetWings(Mathf.Lerp(1f, 0.2f, t), Mathf.Sin(timer * 34f) * 30f * (1f - t));
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
            Cuts = 0;
            Detection = 0f;
            holdTime = 0f;
            lavaArmedAt = -1f;
            lavaRequested = false;
            bird.gameObject.SetActive(true);
            bird.SetTailLength(1f);
            bird.SetGlow(0f);
            bird.SetAlarm(0f);
            bird.SetTailGlow(0f);
            if (lava != null) lava.SetDry();
            Sit(startPerch);
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
        }

        Vector3 RingPoint(float s)
        {
            int n = flightRing.Length;
            s = Mathf.Repeat(s, ringLength);
            int i = 0;
            while (i < n - 1 && ringAt[i + 1] < s) i++;
            float t = (s - ringAt[i]) / Mathf.Max(0.001f, ringAt[i + 1] - ringAt[i]);
            Vector3 p = CatmullRom(flightRing[(i - 1 + n) % n], flightRing[i], flightRing[(i + 1) % n], flightRing[(i + 2) % n], t);
            return p + Vector3.up * tuning.flightHeight;
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
                var r = rock.GetComponent<Renderer>();
                if (r == null) continue;
                Vector3 top = new Vector3(rock.position.x, r.bounds.max.y - 0.15f, rock.position.z);
                // Сидит по ходу кольца; если так хвост падает на камень или в русло — поворачивается, пока не найдёт ровное дно.
                Vector3 ring = RingFacing(NearestOnRing(top));
                Vector3 facing = Vector3.zero;
                foreach (float turn in new[] { 0f, 180f, 45f, -45f, 135f, -135f, 90f, -90f })
                {
                    Vector3 f = Quaternion.Euler(0f, turn, 0f) * ring;
                    if (TailReachable(top, f)) { facing = f; break; }
                }
                if (facing == Vector3.zero)
                {
                    if (rock != firstPerch) continue;   // на такой валун птица не садится
                    facing = ring;
                }
                if (rock == firstPerch) startPerch = perches.Count;
                perches.Add(new Perch { rock = rock, top = top, facing = facing });
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

        void PlaceBird(Vector3 position, Vector3 facing, float pitch, float roll = 0f)
        {
            facing = Flat(facing);
            var heading = facing.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(facing) : bird.transform.rotation;
            bird.transform.SetPositionAndRotation(position, heading * Quaternion.Euler(pitch, 0f, roll));
        }

        static float Flap(float time, float rate, float degrees) => Mathf.Sin(time * rate * Mathf.PI * 2f) * degrees;

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
