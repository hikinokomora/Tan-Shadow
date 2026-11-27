using System;
using System.Collections;
using TanShadow.Seasons;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TanShadow.Autumn
{
    public enum BaihuState
    {
        Dormant,      // начало тропы, нападений нет
        Hidden,       // тигр скрыт, ждёт следующего появления
        Rustle,       // шевелится трава и бамбук, тигра не видно
        Eyes,         // из травы смотрят глаза, растёт агрессия
        Gong,         // агрессия дошла до предела: гонг, через миг прыжок
        Pounce,       // прыжок: тело видно целиком
        Withdraw,     // отпугнут на тропе: морда уходит обратно в бамбук
        Knocked,      // ударился о столб: падает на бок, встаёт, бродит
        Retreat,      // прыжок обратно в лес после столба или промаха
        Stunned,      // четвёртый удар о столб: упал и лежит
        MonumentFall, // столб падает на тигра
        Defeated,     // мир серый, испытание пройдено
        HeroDown      // героиня погибла, идёт чёрная инь
    }

    public enum BaihuZone { Trail, Arena }

    // Испытание Байху. Отвечает только за тигра, столб и исход. Героиню видит через HeroBridge,
    // звук и обратную связь отдаёт событиями — их слушают BaihuSounds и BaihuFeedback.
    [DefaultExecutionOrder(160)]
    public sealed class BaihuEncounter : MonoBehaviour
    {
        [Header("Данные")]
        public BaihuTuning tuning;

        [Header("Героиня")]
        public HeroBridge hero;
        [Tooltip("Точка возрождения после чёрной инь (начало осени)")]
        public Transform heroSpawn;

        [Header("Геометрия уровня")]
        [Tooltip("Когда героиня проходит этот маркер по оси тропы (Z), начинается охота. Начало тропы — без нападений")]
        public Transform trailActivation;
        [Tooltip("Центр круглой арены")]
        public Transform arenaCenter;
        [Tooltip("Радиус песчаной арены до кромки травы, м")]
        public float arenaRadius = 36f;
        [Tooltip("Угол входа с тропы на арену (от центра, ° в плоскости XZ, 0 = +X, 90 = +Z). Здесь тигр не появляется")]
        public float arenaEntranceAngle = -90f;

        [Header("Объекты")]
        [Tooltip("Модель тигра. Видна только в прыжке, после удара о столб и в прыжке обратно в лес")]
        public Transform tigerBody;
        [Tooltip("Поправка поворота модели, если её «вперёд» не совпадает с +Z, °")]
        public float tigerBodyYawOffset;
        [Tooltip("Часть морды с глазами, выглядывающая из травы")]
        public BaihuPeek peek;
        public BaihuMonument monument;
        public YinYangCover cover;
        public DeadWorld world;
        [SerializeField] bool eyesOnlyInEyesPhase;

        [Header("Отладка")]
        [Tooltip("Показывать состояние тигра в углу экрана (F3 — переключить). Только редактор и dev-сборка")]
        public bool showDebug = true;

        // --- События для звука, VFX и обратной связи ---
        public event Action<BaihuState> StateChanged;
        public event Action<Vector3> Rustled;
        public event Action<Vector3> EyesShown;
        public event Action EyesHidden;
        public event Action<int, int> SwingCounted;          // засчитано, нужно
        public event Action<Vector3> Scared;
        public event Action<Vector3> GongStruck;
        public event Action<Vector3> Pounced;
        public event Action<int, int, Vector3> MonumentHit;   // ударов, нужно, точка
        public event Action<Vector3> PounceMissed;
        public event Action<Vector3> TigerFell;              // отлетел от столба и рухнул на бок
        public event Action<Vector3> TigerGotUp;
        public event Action<Vector3> TigerLeptAway;          // прыжок обратно в лес
        public event Action HeroKilled;
        public event Action<Vector3> MonumentFalling;
        public event Action<Vector3> MonumentLanded;
        public event Action Defeated;
        public event Action EncounterReset;

        public BaihuState State { get; private set; }
        public BaihuZone Zone { get; private set; }
        public float Aggro { get; private set; }           // 0..1: насколько тигр готов прыгнуть
        public int Swings { get; private set; }
        public int Hits => monument != null ? monument.Hits : 0;
        public Vector3 ThreatPoint { get; private set; }    // где сейчас глаза / тигр
        public Vector3 PresentationHeading => State == BaihuState.Pounce
            ? pounceDir
            : State == BaihuState.Retreat
                ? (moveTo - moveFrom).normalized
                : Vector3.forward;
        public bool RustleIsRelocation { get; private set; }
        public bool EyesOnlyInEyesPhase => eyesOnlyInEyesPhase;

        float timer;
        float flightTime;
        int side;                     // с какой стороны тропы тигр: -1 слева, +1 справа
        float eyesAngle;                  // арена: под каким углом от центра глаза
        Vector3 groundPoint;          // точка на земле под глазами
        Vector3 moveFrom, moveTo;     // прыжок обратно в лес
        float moveDuration;
        Vector3 pounceFrom, pounceTarget, pounceDir;   // прыжок на героиню: откуда, куда приземлится
        float pounceBaseY;
        Vector3 knockFrom, knockTo, wanderTo, knockFacing;
        float knockRoll;              // на какой бок падает: ±
        bool fellEventSent, gotUpEventSent;
        bool monumentLanded;
        bool relocating;              // тигр переползает за героиней: агрессия и взмахи не сбрасываются

        void Awake()
        {
            HideTiger();
        }

        void OnEnable()
        {
            if (hero != null) hero.SwingStarted += OnHeroSwing;
        }

        void OnDisable()
        {
            if (hero != null) hero.SwingStarted -= OnHeroSwing;
        }

        void Start() => Enter(BaihuState.Dormant);

        void Update()
        {
            if (Keyboard.current != null && Keyboard.current.f3Key.wasPressedThisFrame) showDebug = !showDebug;
            if (hero == null || tuning == null) return;
            if (State == BaihuState.Defeated || State == BaihuState.HeroDown) return;

            float dt = Time.deltaTime;
            var zone = IsInArena(hero.Body.position) ? BaihuZone.Arena : BaihuZone.Trail;
            if (zone != Zone)
            {
                Zone = zone;
                // Сменилась зона, пока тигр только готовился: он уходит и появится снова уже по правилам новой зоны.
                if (State == BaihuState.Rustle || State == BaihuState.Eyes) GoHidden(1f);
            }

            switch (State)
            {
                case BaihuState.Dormant:
                    if (trailActivation == null || hero.Body.position.z >= trailActivation.position.z || Zone == BaihuZone.Arena)
                        GoHidden(tuning.firstAppearDelay);
                    break;

                case BaihuState.Hidden:
                    DecayAggro(dt);
                    timer -= dt;
                    if (timer <= 0f) BeginRustle();
                    break;

                case BaihuState.Rustle:
                    // Переползая вслед за героиней, тигр продолжает «заряжаться»: бегство не откладывает прыжок.
                    if (relocating) Aggro = Mathf.Min(1f, Aggro + dt / tuning.aggroFillTime);
                    if (Zone == BaihuZone.Trail && HeroLeftBehind(groundPoint)) { RelocateAhead(); break; }
                    if (Zone == BaihuZone.Arena && HeroOnOtherSide()) { RelocateToHeroSide(); break; }
                    timer -= dt;
                    if (timer <= 0f) ShowEyes();
                    break;

                case BaihuState.Eyes:
                    Aggro = Mathf.Min(1f, Aggro + dt / tuning.aggroFillTime);
                    if (peek != null) { peek.Face(hero.Body.position); peek.SetAggro(Aggro); }
                    if (Zone == BaihuZone.Trail && Aggro < 1f && HeroLeftBehind(groundPoint)) { RelocateAhead(); break; }
                    if (Zone == BaihuZone.Arena && Aggro < 1f && HeroOnOtherSide()) { RelocateToHeroSide(); break; }
                    if (Aggro >= 1f)
                    {
                        Enter(BaihuState.Gong);
                        timer = tuning.gongToPounce;
                        GongStruck?.Invoke(ThreatPoint);
                    }
                    break;

                case BaihuState.Gong:
                    if (peek != null) { peek.Face(hero.Body.position); peek.SetAggro(1f); }
                    timer -= dt;
                    if (timer <= 0f) BeginPounce();
                    break;

                case BaihuState.Pounce:
                    TickPounce(dt);
                    break;

                case BaihuState.Withdraw:
                    DecayAggro(dt);
                    timer -= dt;
                    if (timer <= 0f) GoHidden(UnityEngine.Random.Range(tuning.reappearDelay.x, tuning.reappearDelay.y));
                    break;

                case BaihuState.Knocked:
                    DecayAggro(dt);
                    TickKnocked(dt);
                    break;

                case BaihuState.Retreat:
                    DecayAggro(dt);
                    timer += dt;
                    float t = Mathf.Clamp01(timer / moveDuration);
                    // Прыжок дугой: нос вверх на взлёте, вниз на приземлении.
                    Vector3 leap = Vector3.Lerp(moveFrom, moveTo, t) + Vector3.up * (tuning.retreatLeapHeight * 4f * t * (1f - t));
                    PlaceTiger(leap, moveTo - moveFrom, Mathf.Lerp(-20f, 20f, t));
                    if (t >= 1f)
                    {
                        HideTiger();
                        GoHidden(UnityEngine.Random.Range(tuning.reappearDelay.x, tuning.reappearDelay.y));
                    }
                    break;

                case BaihuState.Stunned:
                    timer += dt;
                    PoseKnockFall(timer);
                    if (timer >= tuning.knockFallTime + tuning.stunBeforeFall)
                    {
                        monument.BeginFall(knockTo);
                        Enter(BaihuState.MonumentFall);
                        timer = 0f;
                        monumentLanded = false;
                        MonumentFalling?.Invoke(monument.HitBounds.center);
                    }
                    break;

                case BaihuState.MonumentFall:
                    timer += dt;
                    float fall = Mathf.Clamp01(timer / tuning.monumentFallDuration);
                    monument.SetFall(fall);
                    // Белая янь — за несколько кадров до того, как столб накроет тигра.
                    if (timer >= tuning.monumentFallDuration - tuning.cutLeadTime) Win();
                    break;
            }
        }

        // ---------------- Появление ----------------

        void GoHidden(float delay)
        {
            HideEyes();
            Swings = 0;
            relocating = false;
            timer = delay;
            Enter(BaihuState.Hidden);
        }

        // Героиня убежала от глаз: вперёд по тропе дальше trailLeaveBehind или просто далеко.
        bool HeroLeftBehind(Vector3 point)
        {
            Vector3 h = hero.Body.position;
            return h.z - point.z > tuning.trailLeaveBehind || Flat(h - point).magnitude > tuning.trailKeepRange;
        }

        // Тигр переползает в траве вперёд, к героине. Агрессия и засчитанные взмахи сохраняются.
        void RelocateAhead()
        {
            HideEyes();
            relocating = true;
            groundPoint = PickTrailPoint();
            ThreatPoint = groundPoint;
            timer = tuning.trailRelocateTime;
            Enter(BaihuState.Rustle);
            RustleIsRelocation = true;
            Rustled?.Invoke(groundPoint);
        }

        void BeginRustle()
        {
            groundPoint = Zone == BaihuZone.Arena ? PickArenaPoint() : PickTrailPoint();
            ThreatPoint = groundPoint;
            timer = tuning.rustleDuration;
            Enter(BaihuState.Rustle);
            RustleIsRelocation = false;
            Rustled?.Invoke(groundPoint);
        }

        void ShowEyes()
        {
            Vector3 eyesPosition = groundPoint + Vector3.up * tuning.eyesHeight;
            if (peek != null) peek.Show(eyesPosition, hero.Body.position);
            ThreatPoint = eyesPosition;
            if (!relocating) Swings = 0;
            relocating = false;
            Enter(BaihuState.Eyes);
            EyesShown?.Invoke(eyesPosition);
        }

        Vector3 PickTrailPoint()
        {
            Vector3 h = hero.Body.position;
            side = UnityEngine.Random.value < 0.5f ? -1 : 1;
            float z = h.z + UnityEngine.Random.Range(tuning.trailAhead.x, tuning.trailAhead.y);
            if (arenaCenter != null)
                z = Mathf.Min(z, arenaCenter.position.z - arenaRadius - 2f);
            float axisX = arenaCenter != null ? arenaCenter.position.x : 0f;
            return new Vector3(axisX + side * tuning.trailEyesSideOffset, h.y, z);
        }

        // Арена: тигр выходит с той же стороны столба, что и героиня, — столб у неё за спиной.
        // Увернётся в последний миг — тигр пролетит мимо и врежется в столб.
        Vector3 PickArenaPoint()
        {
            Vector3 c = arenaCenter.position;
            float angle = HeroArenaAngle() + UnityEngine.Random.Range(-tuning.arenaSameSideSpread, tuning.arenaSameSideSpread);
            // Не из прохода на тропу: там нет травы.
            float fromEntrance = Mathf.DeltaAngle(arenaEntranceAngle, angle);
            if (Mathf.Abs(fromEntrance) < 18f) angle = arenaEntranceAngle + (fromEntrance >= 0f ? 18f : -18f);
            eyesAngle = angle;
            float r = arenaRadius + UnityEngine.Random.Range(tuning.arenaEyesDepth.x, tuning.arenaEyesDepth.y);
            float a = angle * Mathf.Deg2Rad;
            return new Vector3(c.x + Mathf.Cos(a) * r, c.y, c.z + Mathf.Sin(a) * r);
        }

        float HeroArenaAngle()
        {
            Vector3 d = Flat(hero.Body.position - arenaCenter.position);
            return d.sqrMagnitude > 0.01f ? Mathf.Atan2(d.z, d.x) * Mathf.Rad2Deg : UnityEngine.Random.Range(0f, 360f);
        }

        bool HeroOnOtherSide() =>
            Mathf.Abs(Mathf.DeltaAngle(eyesAngle, HeroArenaAngle())) > tuning.arenaSideTolerance;

        // Героиня обошла столб: тигр переползает в траве на её сторону. Агрессия сохраняется.
        void RelocateToHeroSide()
        {
            HideEyes();
            relocating = true;
            groundPoint = PickArenaPoint();
            ThreatPoint = groundPoint;
            timer = tuning.arenaRelocateTime;
            Enter(BaihuState.Rustle);
            RustleIsRelocation = true;
            Rustled?.Invoke(groundPoint);
        }

        // ---------------- Отпугивание ----------------

        void OnHeroSwing(Vector3 direction)
        {
            if (State != BaihuState.Eyes) return; // начатый прыжок мечом не остановить
            Vector3 to = ThreatPoint - hero.Body.position;
            to.y = 0f;
            if (to.magnitude > tuning.scareRange) return;
            if (Vector3.Angle(direction, to) > tuning.scareConeAngle * 0.5f) return;

            if (Zone == BaihuZone.Trail)
            {
                Swings++;
                SwingCounted?.Invoke(Swings, tuning.scareSwingsRequired);
                if (Swings >= tuning.scareSwingsRequired) BeginWithdraw();
            }
            else
            {
                // На арене меч только задерживает прыжок.
                Aggro = Mathf.Max(0f, Aggro - tuning.arenaSwingAggroDrop);
                SwingCounted?.Invoke(0, 0);
            }
        }

        // Отпугнут: тигр не показывается, морда пятится и скрывается в бамбуке.
        void BeginWithdraw()
        {
            if (peek != null && peek.IsVisible)
            {
                peek.Withdraw(tuning.withdrawBack, tuning.withdrawSink, tuning.withdrawDuration);
                EyesHidden?.Invoke();
            }
            timer = tuning.withdrawDuration;
            Enter(BaihuState.Withdraw);
            Scared?.Invoke(groundPoint);
        }

        // ---------------- Прыжок ----------------

        void BeginPounce()
        {
            HideEyes(true);
            ShowTiger();
            flightTime = 0f;
            // На тропе тигр не прыгает издалека: если героиня успела убежать, он уже в траве рядом с ней.
            if (Zone == BaihuZone.Trail && Flat(hero.Body.position - groundPoint).magnitude > tuning.pounceMaxDistance)
            {
                float axisX = arenaCenter != null ? arenaCenter.position.x : 0f;
                Vector3 h = hero.Body.position;
                groundPoint = new Vector3(axisX + side * tuning.trailEyesSideOffset, h.y, h.z + 1.5f);
            }
            pounceBaseY = groundPoint.y;
            pounceFrom = groundPoint;
            pounceTarget = PounceAim();
            pounceDir = Flat(pounceTarget - pounceFrom).normalized;
            PlaceTiger(groundPoint, pounceDir);
            Enter(BaihuState.Pounce);
            Pounced?.Invoke(groundPoint);
        }

        Vector3 PounceAim()
        {
            Vector3 aim = hero.Body.position;
            aim.y = pounceBaseY;
            return aim;
        }

        // Где тигр в плане через time секунд полёта. До героини — ровно pounceDuration с любого расстояния,
        // дальше летит с той же скоростью ещё pounceOvershoot метров.
        Vector3 PounceFlatAt(float time) =>
            pounceFrom + (pounceTarget - pounceFrom) * (time / tuning.pounceDuration);

        float PounceTotalTime()
        {
            float distance = Mathf.Max(0.5f, Flat(pounceTarget - pounceFrom).magnitude);
            return tuning.pounceDuration * (distance + tuning.pounceOvershoot) / distance;
        }

        void TickPounce(float dt)
        {
            flightTime += dt;
            Vector3 position = tigerBody.position;
            Vector3 heroGround = hero.Body.position;

            // Пока до героини лететь больше pounceCommitTime, тигр доворачивает на неё: ранний рывок не спасает.
            // На тропе — до самого конца: убежать нельзя, спасает только отпугивание.
            bool homing = (Zone == BaihuZone.Trail && tuning.trailPounceAlwaysHoming) ||
                          tuning.pounceDuration - flightTime > tuning.pounceCommitTime;
            if (homing) pounceTarget = Vector3.MoveTowards(pounceTarget, PounceAim(), tuning.pounceTrackSpeed * dt);
            pounceDir = Flat(pounceTarget - pounceFrom).normalized;

            // Дуга на весь полёт: над героиней тигр ещё в воздухе, приземляется за ней.
            float total = PounceTotalTime();
            float s = Mathf.Clamp01(flightTime / total);
            Vector3 next = PounceFlatAt(Mathf.Min(flightTime, total));
            next.y = pounceBaseY + tuning.pounceLeapHeight * 4f * s * (1f - s);

            // Столб на пути — тигр врезается в него. Так и побеждают: увернуться, когда за спиной столб.
            if (monument != null && !monument.HasFallen && SegmentHitsBounds(position, next, monument.HitBounds, tuning.monumentHitTolerance))
            {
                ImpactMonument(position);
                return;
            }

            PlaceTiger(next, pounceDir, Mathf.Lerp(-20f, 20f, s));
            ThreatPoint = next;

            // Чёрная инь — за cutLeadTime до того, как тигр накроет героиню (если между ними не столб).
            Vector3 soon = PounceFlatAt(Mathf.Min(flightTime + tuning.cutLeadTime, total));
            bool monumentBetween = monument != null && !monument.HasFallen &&
                                   SegmentHitsBounds(next, heroGround, monument.HitBounds, tuning.monumentHitTolerance);
            if (!monumentBetween && Flat(heroGround - soon).magnitude <= tuning.killCutDistance)
            {
                KillHero();
                return;
            }

            if (flightTime >= total)
            {
                PounceMissed?.Invoke(next);
                BeginRetreat(next);
            }
        }

        void ImpactMonument(Vector3 from)
        {
            Vector3 hitPoint = monument.BasePointToward(from);
            monument.ApplyHit(from);
            ThreatPoint = hitPoint;
            MonumentHit?.Invoke(monument.Hits, tuning.requiredHits, hitPoint);
            BeginKnocked(hitPoint, from, monument.Hits >= tuning.requiredHits);
        }

        // Тигра отбрасывает от столба, он падает на бок. Четвёртый удар — так и лежит, пока не рухнет столб.
        void BeginKnocked(Vector3 hitPoint, Vector3 from, bool final)
        {
            Vector3 away = Flat(from - hitPoint);
            if (away.sqrMagnitude < 0.0001f) away = Flat(hero.Body.position - hitPoint);
            away = away.sqrMagnitude > 0.0001f ? away.normalized : Vector3.back;
            knockFrom = hitPoint + away * 1.2f;
            knockFrom.y = pounceBaseY;
            knockTo = knockFrom + away * tuning.knockback;
            knockFacing = -away;
            knockRoll = UnityEngine.Random.value < 0.5f ? -85f : 85f;
            // Бредёт вбок вокруг столба, немного отходя от него.
            Vector3 sideways = Vector3.Cross(Vector3.up, away) * (UnityEngine.Random.value < 0.5f ? -1f : 1f);
            wanderTo = knockTo + (sideways * 0.85f + away * 0.5f).normalized * tuning.wanderDistance;
            fellEventSent = gotUpEventSent = false;
            timer = 0f;
            PoseKnockFall(0f);
            Enter(final ? BaihuState.Stunned : BaihuState.Knocked);
        }

        void PoseKnockFall(float time)
        {
            float k = Mathf.Clamp01(time / tuning.knockFallTime);
            float slide = 1f - (1f - k) * (1f - k);
            Vector3 position = Vector3.Lerp(knockFrom, knockTo, slide) + Vector3.up * (Mathf.Sin(Mathf.PI * k) * 0.8f);
            PlaceTiger(position, knockFacing, -10f * (1f - k), knockRoll * Mathf.SmoothStep(0f, 1f, k));
            if (k >= 1f && !fellEventSent)
            {
                fellEventSent = true;
                TigerFell?.Invoke(knockTo);
            }
        }

        void TickKnocked(float dt)
        {
            timer += dt;
            float lieEnd = tuning.knockFallTime + tuning.knockLieTime;
            float upEnd = lieEnd + tuning.knockGetUpTime;
            float wanderEnd = upEnd + tuning.wanderTime;

            if (timer < lieEnd)
            {
                PoseKnockFall(timer);
            }
            else if (timer < upEnd)
            {
                if (!gotUpEventSent) { gotUpEventSent = true; TigerGotUp?.Invoke(knockTo); }
                float k = (timer - lieEnd) / tuning.knockGetUpTime;
                PlaceTiger(knockTo, knockFacing, 0f, knockRoll * (1f - Mathf.SmoothStep(0f, 1f, k)));
            }
            else if (timer < wanderEnd)
            {
                // Оглушён: идёт медленно, покачиваясь и рыская головой.
                float k = (timer - upEnd) / Mathf.Max(0.01f, tuning.wanderTime);
                Vector3 position = Vector3.Lerp(knockTo, wanderTo, Mathf.SmoothStep(0f, 1f, k));
                // Плавно отворачивается от столба, а к концу — к лесу, куда прыгнет.
                Vector3 walk = Flat(wanderTo - knockTo).normalized;
                Vector3 leap = Flat(RetreatHome(wanderTo) - wanderTo).normalized;
                Vector3 heading = Vector3.Slerp(knockFacing.normalized, walk, Mathf.SmoothStep(0f, 1f, k / 0.35f));
                heading = Vector3.Slerp(heading, leap, Mathf.SmoothStep(0f, 1f, (k - 0.65f) / 0.35f));
                Vector3 facing = Quaternion.Euler(0f, Mathf.Sin(k * Mathf.PI * 3f) * 18f * (1f - k), 0f) * heading;
                PlaceTiger(position, facing, 0f, Mathf.Sin(k * Mathf.PI * 4f) * 6f);
                ThreatPoint = position;
            }
            else
            {
                BeginRetreat(wanderTo);
            }
        }

        void BeginRetreat(Vector3 from)
        {
            from.y = pounceBaseY;
            moveFrom = from;
            moveTo = RetreatHome(from);
            moveDuration = tuning.retreatDuration;
            timer = 0f;
            Enter(BaihuState.Retreat);
            TigerLeptAway?.Invoke(from);
        }

        // Куда тигр прыгает обратно: на арене — за кромку травы, на тропе — в заросли сбоку.
        Vector3 RetreatHome(Vector3 from)
        {
            if (Zone == BaihuZone.Arena && arenaCenter != null)
            {
                Vector3 outward = Flat(from - arenaCenter.position);
                if (outward.sqrMagnitude < 0.01f) outward = Flat(from - hero.Body.position);
                Vector3 home = arenaCenter.position + outward.normalized * (arenaRadius + 6f);
                home.y = from.y;
                return home;
            }
            float axisX = arenaCenter != null ? arenaCenter.position.x : 0f;
            int s = from.x >= axisX ? 1 : -1;
            return new Vector3(axisX + s * (tuning.trailEyesSideOffset + 5f), from.y, from.z + 3f);
        }

        // ---------------- Исходы ----------------

        void KillHero()
        {
            Enter(BaihuState.HeroDown);
            HeroKilled?.Invoke();
            hero.SetInputEnabled(false);
            if (cover != null) cover.Run(YinYangCover.Kind.Yin, RestartUnderCover, () => hero.SetInputEnabled(true));
            else { ResetEncounter(); RespawnHero(); hero.SetInputEnabled(true); }
        }

        IEnumerator RestartUnderCover()
        {
            ResetEncounter();
            RespawnHero();
            yield return null;
        }

        void RespawnHero()
        {
            if (heroSpawn != null) hero.Teleport(heroSpawn.position, heroSpawn.rotation);
        }

        public void ResetEncounter()
        {
            if (monument != null) monument.ResetMonument();
            Aggro = 0f;
            Swings = 0;
            HideEyes(true);
            HideTiger();
            Enter(BaihuState.Dormant);
            EncounterReset?.Invoke();
        }

        void Win()
        {
            Enter(BaihuState.Defeated);
            hero.SetInputEnabled(false);
            Aggro = 0f;
            if (cover != null) cover.Run(YinYangCover.Kind.Yang, VictoryUnderCover, () => hero.SetInputEnabled(true));
            else { ApplyVictory(); hero.SetInputEnabled(true); }
        }

        IEnumerator VictoryUnderCover()
        {
            // Столб долетает уже под белой янью.
            if (monument != null) monument.SetFall(1f);
            if (!monumentLanded)
            {
                monumentLanded = true;
                MonumentLanded?.Invoke(ThreatPoint);
            }
            ApplyVictory();
            yield return null;
        }

        void ApplyVictory()
        {
            HideEyes(true);
            if (world != null) world.ApplyDead();
            SeasonProgress.MarkDefeated(Season.Autumn);
            Defeated?.Invoke();
        }

        // ---------------- Помощники ----------------

        void Enter(BaihuState next)
        {
            if (eyesOnlyInEyesPhase && next == BaihuState.Gong) HideEyes(true);
            State = next;
            StateChanged?.Invoke(next);
        }

        void DecayAggro(float dt) => Aggro = Mathf.Max(0f, Aggro - tuning.aggroDecayPerSecond * dt);

        bool IsInArena(Vector3 p)
        {
            if (arenaCenter == null) return false;
            return Flat(p - arenaCenter.position).magnitude < arenaRadius - 1f;
        }

        // Морда прячется в траву. instant — сразу (прыжок: дальше видно тело тигра, а не морду).
        void HideEyes(bool instant = false)
        {
            if (peek != null && peek.IsVisible)
            {
                peek.Hide(instant);
                EyesHidden?.Invoke();
            }
        }

        void ShowTiger()
        {
            if (tigerBody != null) tigerBody.gameObject.SetActive(true);
        }

        void HideTiger()
        {
            if (tigerBody != null) tigerBody.gameObject.SetActive(false);
        }

        // pitch > 0 — нос вниз, roll — завалиться на бок.
        void PlaceTiger(Vector3 position, Vector3 facing, float pitch = 0f, float roll = 0f)
        {
            if (tigerBody == null) return;
            facing = Flat(facing);
            var heading = facing.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(facing)
                : tigerBody.rotation * Quaternion.Euler(0f, -tigerBodyYawOffset, 0f);
            tigerBody.SetPositionAndRotation(position,
                heading * Quaternion.Euler(pitch, 0f, roll) * Quaternion.Euler(0f, tigerBodyYawOffset, 0f));
        }

        static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v;
        }

        static Quaternion LookFlat(Vector3 from, Vector3 to)
        {
            Vector3 d = Flat(to - from);
            return d.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(d) : Quaternion.identity;
        }

        // Пересекает ли отрезок a→b прямоугольник столба в плане (высоту не учитываем: тигр летит низко).
        static bool SegmentHitsBounds(Vector3 a, Vector3 b, Bounds bounds, float tolerance)
        {
            var box = new Bounds(new Vector3(bounds.center.x, 0f, bounds.center.z),
                                 new Vector3(bounds.size.x + tolerance * 2f, 1f, bounds.size.z + tolerance * 2f));
            Vector3 a0 = new Vector3(a.x, 0f, a.z), b0 = new Vector3(b.x, 0f, b.z);
            if (box.Contains(a0) || box.Contains(b0)) return true;
            Vector3 d = b0 - a0;
            float length = d.magnitude;
            if (length < 0.0001f) return false;
            return box.IntersectRay(new Ray(a0, d / length), out float distance) && distance <= length;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        void OnGUI()
        {
            if (!showDebug || tuning == null) return;
            GUI.depth = -10;
            var text = $"БАЙХУ (F3)\nСостояние: {State}\nЗона: {Zone}\nАгрессия: {Aggro:0.00}\n" +
                       $"Взмахи: {Swings}/{tuning.scareSwingsRequired}\nУдары о столб: {Hits}/{tuning.requiredHits}\n" +
                       $"Рывок героини: {(hero != null && hero.IsDodging ? "да" : "нет")}";
            GUI.Box(new Rect(12, 12, 230, 128), text);
        }
#endif
    }
}
