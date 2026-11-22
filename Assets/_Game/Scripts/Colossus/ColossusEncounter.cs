using TanShadow.Combat;
using TanShadow.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TanShadow.Colossus
{
    [DefaultExecutionOrder(200)]
    public sealed class ColossusEncounter : MonoBehaviour
    {
        enum BossPhase { Track, Windup, Strike, Exposed, Rise, Shake, Recover, Collapse, Won, Reset }
        enum Rider { Ground, Jump, Grip, Fall }

        public ColossusTuning tuning;
        public PlayerController player;
        public PlayerController playerTemplate;
        public Camera view;
        public Combatant boss;
        public Transform upperArm, forearm, hand, seal, grips;
        public Renderer sealSurface, fistSurface;
        public Transform aimBody;
        public LineRenderer ring;

        static readonly Vector3 Shoulder = new Vector3(1.18f, 5.05f, 0f);
        static readonly Vector3 Rest = new Vector3(1.58f, 2.25f, 0.42f);
        static readonly Vector3 Raised = new Vector3(1.1f, 7.45f, 1.85f);
        static readonly Vector3 Planted = new Vector3(1.65f, 0.43f, 3.1f);
        static readonly Color Jade = new Color(0.12f, 1f, 0.68f);
        static readonly Color Amber = new Color(1f, 0.24f, 0.08f);
        static readonly int Emissive = Shader.PropertyToID("_EmissiveColor");

        BossPhase phase;
        Rider rider;
        float elapsed, motion, climb, attachedFor, jumpBuffer, flash, shakeCamera;
        int wounds;
        Vector3 spawn, bossSpawn, leapStart, landing, handPoint, recoverFrom;
        Quaternion spawnRotation, bossRotation;
        CharacterController body;
        Combatant hero;
        AttackExecutor dagger;
        readonly RaycastHit[] cameraHits = new RaycastHit[16];
        MaterialPropertyBlock glow;
        AudioSource audioSource;
        AudioClip impact, rattle, fracture;

        void Awake()
        {
            spawn = player.transform.position;
            spawnRotation = player.transform.rotation;
            bossSpawn = boss.transform.position;
            bossRotation = boss.transform.rotation;
            glow = new MaterialPropertyBlock();
            audioSource = gameObject.AddComponent<AudioSource>();
            impact = MakeSound("Stone impact", 0.62f, 63f, 0.6f);
            rattle = MakeSound("Stone strain", 0.48f, 104f, 0.22f);
            fracture = MakeSound("Seal fracture", 0.5f, 238f, 0.35f);
            BindPlayer();
            PoseArm(Rest);
            ring.enabled = false;
        }

        void BindPlayer()
        {
            player.cameraTransform = view.transform;
            hero = player.GetComponent<Combatant>();
            body = player.GetComponent<CharacterController>();
            dagger = player.GetComponent<AttackExecutor>();
            dagger.PhaseChanged += OnDaggerPhase;
        }

        void OnDestroy()
        {
            if (dagger != null) dagger.PhaseChanged -= OnDaggerPhase;
        }

        void Update()
        {
            if (Keyboard.current?.rKey.wasPressedThisFrame == true ||
                Gamepad.current?.startButton.wasPressedThisFrame == true)
            {
                ResetFight();
                return;
            }

            float dt = Time.deltaTime;
            elapsed += dt;
            flash = Mathf.Max(0f, flash - dt);
            shakeCamera = Mathf.MoveTowards(shakeCamera, 0f, dt * 3f);
            TickBoss(dt);
            TickRider(dt);
            UpdateSignals();
        }

        void TickBoss(float dt)
        {
            switch (phase)
            {
                case BossPhase.Track:
                    Vector3 offset = player.transform.position - boss.transform.position;
                    offset.y = 0f;
                    if (offset.sqrMagnitude < 0.01f) offset = boss.transform.forward;
                    boss.transform.rotation = Quaternion.RotateTowards(boss.transform.rotation, Quaternion.LookRotation(offset), dt * 105f);
                    if (offset.magnitude > tuning.attackDistance)
                        boss.transform.position = InCourt(boss.transform.position + offset.normalized * tuning.pursuitSpeed * dt, 5.6f);
                    else if (elapsed > 0.55f && rider == Rider.Ground &&
                             Vector3.Angle(boss.transform.forward, offset) < 14f)
                        Enter(BossPhase.Windup);
                    break;
                case BossPhase.Windup:
                    PoseArm(Vector3.Lerp(Rest, Raised, Smooth(tuning.slam.windup)));
                    if (elapsed >= tuning.slam.windup) Enter(BossPhase.Strike);
                    break;
                case BossPhase.Strike:
                    PoseArm(Vector3.Lerp(Raised, Planted, Smooth(tuning.slam.active)));
                    if (elapsed >= tuning.slam.active) Slam();
                    break;
                case BossPhase.Exposed:
                    if (elapsed >= (wounds == 0 ? tuning.firstGripWindow : tuning.secondGripWindow))
                    {
                        Enter(BossPhase.Rise);
                        audioSource.PlayOneShot(rattle);
                    }
                    break;
                case BossPhase.Rise:
                    PoseArm(Vector3.Lerp(Planted, Rest, Smooth(tuning.riseDuration)));
                    if (elapsed >= tuning.riseDuration && rider == Rider.Ground) Enter(BossPhase.Recover);
                    break;
                case BossPhase.Shake:
                    PoseArm(Vector3.Lerp(handPoint, Rest, dt * 4f) + Vector3.right * Mathf.Sin(elapsed * 42f) * dt * 1.8f);
                    if (elapsed >= tuning.shakeWarning && !dagger.IsBusy) ThrowRider();
                    break;
                case BossPhase.Recover:
                    PoseArm(Vector3.Lerp(recoverFrom, Rest, Smooth(tuning.slam.recovery)));
                    if (elapsed >= tuning.slam.recovery && rider == Rider.Ground) Enter(BossPhase.Track);
                    break;
                case BossPhase.Collapse:
                    boss.transform.rotation = bossRotationAtFall * Quaternion.Euler(0f, 0f, -79f * Smooth(1.35f));
                    if (elapsed >= 1.35f)
                    {
                        audioSource.PlayOneShot(impact);
                        shakeCamera = 1f;
                        Enter(BossPhase.Won);
                    }
                    break;
                case BossPhase.Reset:
                    if (elapsed >= tuning.resetDelay) ResetFight();
                    break;
            }
        }

        Quaternion bossRotationAtFall;

        void Slam()
        {
            PoseArm(Planted);
            Enter(BossPhase.Exposed);
            audioSource.PlayOneShot(impact);
            shakeCamera = 0.75f;
            Vector3 offset = player.transform.position - boss.transform.TransformPoint(Planted);
            if (rider != Rider.Ground || player.transform.position.y > 0.9f ||
                new Vector2(offset.x, offset.z).magnitude > tuning.impactRadius) return;

            bool lethal = hero.Health <= tuning.slam.damage;
            if (hero.ReceiveHit(tuning.slam, boss) == HitResult.Hit && lethal)
            {
                player.enabled = false;
                Enter(BossPhase.Reset);
            }
        }

        void TickRider(float dt)
        {
            if (phase == BossPhase.Reset) return;
            bool jump = Keyboard.current?.eKey.wasPressedThisFrame == true ||
                        Gamepad.current?.buttonSouth.wasPressedThisFrame == true;
            jumpBuffer = jump ? 0.2f : Mathf.Max(0f, jumpBuffer - dt);

            switch (rider)
            {
                case Rider.Ground:
                    if (jumpBuffer > 0f && CanGrab()) StartJump();
                    break;
                case Rider.Jump:
                    motion += dt;
                    float progress = Mathf.Clamp01(motion / tuning.jumpDuration);
                    Place(Vector3.Lerp(leapStart, GripPosition(0f), progress) +
                          Vector3.up * (Mathf.Sin(progress * Mathf.PI) * 0.95f));
                    if (progress >= 1f)
                    {
                        rider = Rider.Grip;
                        attachedFor = 0f;
                        climb = 0f;
                    }
                    break;
                case Rider.Grip:
                    attachedFor += dt;
                    if (jump && !dagger.IsBusy)
                    {
                        ThrowRider();
                        break;
                    }
                    if (!dagger.IsBusy)
                    {
                        Vector2 stick = Gamepad.current?.leftStick.ReadValue() ?? Vector2.zero;
                        var keyboard = Keyboard.current;
                        float vertical = stick.y + (keyboard?.wKey.isPressed == true ? 1f : 0f) -
                            (keyboard?.sKey.isPressed == true ? 1f : 0f);
                        climb = Mathf.Clamp01(climb + Mathf.Clamp(vertical, -1f, 1f) * tuning.climbSpeed * dt);
                        bool strike = Mouse.current?.leftButton.wasPressedThisFrame == true ||
                                      Gamepad.current?.rightShoulder.wasPressedThisFrame == true;
                        if (climb >= 0.94f && strike &&
                            (phase != BossPhase.Shake || elapsed <= tuning.shakeWarning))
                            dagger.Begin(player.combo[0]);
                    }
                    Place(GripPosition(climb));
                    if (phase != BossPhase.Shake && attachedFor >= tuning.shakeDelay)
                    {
                        Enter(BossPhase.Shake);
                        audioSource.PlayOneShot(rattle);
                    }
                    break;
                case Rider.Fall:
                    motion += dt;
                    float t = Mathf.Clamp01(motion / 0.72f);
                    Place(Vector3.Lerp(leapStart, landing, t) + Vector3.up * (Mathf.Sin(t * Mathf.PI) * 0.7f));
                    if (t >= 1f)
                    {
                        dagger.Cancel();
                        player.GetComponent<PlayerAnimator>().animator.CrossFadeInFixedTime(PlayerAnimator.LocomotionState, 0.14f);
                        body.enabled = true;
                        player.enabled = true;
                        rider = Rider.Ground;
                        elapsed = phase == BossPhase.Recover ? 0f : elapsed;
                    }
                    break;
            }
        }

        bool CanGrab()
        {
            bool open = phase == BossPhase.Exposed || (phase == BossPhase.Rise && elapsed <= tuning.lateGripGrace);
            return open && player.State == PlayerState.Locomotion && body.isGrounded &&
                   Vector3.Distance(player.transform.position, GripPosition(0f)) <= tuning.jumpReach;
        }

        void StartJump()
        {
            jumpBuffer = 0f;
            leapStart = player.transform.position;
            motion = 0f;
            rider = Rider.Jump;
            player.enabled = false;
            body.enabled = false;
        }

        void OnDaggerPhase(AttackPhase attackPhase)
        {
            if (attackPhase != AttackPhase.Active || rider != Rider.Grip || climb < 0.94f) return;
            if (phase == BossPhase.Collapse || phase == BossPhase.Won || wounds >= 2) return;
            wounds++;
            flash = 0.55f;
            audioSource.PlayOneShot(fracture);
            hero.Freeze(0.08f);
            ThrowRider();
            if (wounds == 2)
            {
                bossRotationAtFall = boss.transform.rotation;
                Enter(BossPhase.Collapse);
            }
        }

        void ThrowRider()
        {
            leapStart = player.transform.position;
            landing = FindLanding();
            motion = 0f;
            jumpBuffer = 0f;
            rider = Rider.Fall;
            Enter(BossPhase.Recover);
        }

        Vector3 FindLanding()
        {
            for (int i = 0; i < 8; i++)
            {
                Vector3 outward = Quaternion.Euler(0f, i * 45f, 0f) * (boss.transform.forward + boss.transform.right).normalized;
                Vector3 point = InCourt(boss.transform.position + outward * 4.6f, 8.4f);
                point.y = 0.04f;
                if (Vector3.Distance(point, boss.transform.position) < 2.8f) continue;
                if (!ClearForPlayer(point)) continue;

                bool clear = true;
                for (int step = 1; step <= 5; step++)
                {
                    float t = step / 6f;
                    Vector3 sample = Vector3.Lerp(leapStart, point, t) + Vector3.up * (Mathf.Sin(t * Mathf.PI) * 0.7f);
                    if (!ClearForPlayer(sample))
                    {
                        clear = false;
                        break;
                    }
                }
                if (clear) return point;
            }
            return spawn;
        }

        static bool ClearForPlayer(Vector3 point)
        {
            return !Physics.CheckCapsule(point + Vector3.up * 0.48f, point + Vector3.up * 1.55f, 0.41f,
                                         ~0, QueryTriggerInteraction.Ignore);
        }

        void ResetFight()
        {
            dagger.PhaseChanged -= OnDaggerPhase;
            player.gameObject.SetActive(false);
            Destroy(player.gameObject);
            player = Instantiate(playerTemplate, spawn, spawnRotation);
            player.name = "Player";
            player.gameObject.SetActive(true);
            BindPlayer();
            boss.transform.SetPositionAndRotation(bossSpawn, bossRotation);
            rider = Rider.Ground;
            wounds = 0;
            motion = climb = attachedFor = jumpBuffer = flash = shakeCamera = 0f;
            PoseArm(Rest);
            Enter(BossPhase.Track);
        }

        void Place(Vector3 position)
        {
            Vector3 direction = boss.transform.position - position;
            direction.y = 0f;
            if (direction.sqrMagnitude > 0.001f)
                player.transform.rotation = Quaternion.LookRotation(direction);
            player.transform.position = position;
        }

        Vector3 GripPosition(float progress)
        {
            Vector3 from = handPoint + new Vector3(0.65f, 0.06f, 0.38f);
            Vector3 to = Shoulder + new Vector3(0.55f, -0.35f, 0.92f);
            Vector3 along = Vector3.Lerp(from, to, progress);
            along.x += Mathf.Sin(progress * Mathf.PI) * 0.48f;
            return boss.transform.TransformPoint(along);
        }

        void PoseArm(Vector3 target)
        {
            handPoint = target;
            Vector3 elbow = Vector3.Lerp(Shoulder, target, 0.5f) + new Vector3(0.35f, -0.28f, -0.18f);
            PoseSegment(upperArm, Shoulder, elbow, 0.92f);
            PoseSegment(forearm, elbow, target, 0.76f);
            hand.localPosition = target;
            for (int i = 0; i < grips.childCount; i++)
            {
                var hold = grips.GetChild(i);
                hold.position = GripPosition(i / (float)(grips.childCount - 1)) - boss.transform.right * 0.35f + Vector3.up * 0.75f;
                hold.rotation = boss.transform.rotation;
            }
        }

        static void PoseSegment(Transform segment, Vector3 start, Vector3 end, float width)
        {
            Vector3 direction = end - start;
            segment.localPosition = (start + end) * 0.5f;
            segment.localRotation = Quaternion.FromToRotation(Vector3.up, direction);
            segment.localScale = new Vector3(width, direction.magnitude + 0.35f, width);
        }

        void Enter(BossPhase next)
        {
            if (next == BossPhase.Recover) recoverFrom = handPoint;
            phase = next;
            elapsed = 0f;
        }

        float Smooth(float duration) => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / Mathf.Max(0.01f, duration)));

        static Vector3 InCourt(Vector3 point, float extent)
        {
            point.x = Mathf.Clamp(point.x, -extent, extent);
            point.z = Mathf.Clamp(point.z, -extent, extent);
            return point;
        }

        void UpdateSignals()
        {
            bool danger = phase == BossPhase.Windup || phase == BossPhase.Strike;
            bool open = phase == BossPhase.Exposed || (phase == BossPhase.Rise && elapsed <= tuning.lateGripGrace);
            ring.enabled = danger;
            if (danger)
            {
                Vector3 center = boss.transform.TransformPoint(Planted);
                center.y = 0.055f;
                for (int i = 0; i < ring.positionCount; i++)
                {
                    float a = i * Mathf.PI * 2f / ring.positionCount;
                    ring.SetPosition(i, center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * tuning.impactRadius);
                }
            }

            SetGlow(fistSurface, danger ? Amber * 3f : open ? Jade * 2.4f : Color.black);
            Color signal = flash > 0f ? Color.white * 8f : wounds == 2 ? Color.black :
                phase == BossPhase.Shake ? Amber * (2f + Mathf.Sin(elapsed * 30f)) : Jade * (wounds == 0 ? 2.6f : 1.5f);
            SetGlow(sealSurface, signal);
            seal.localScale = wounds == 1 ? new Vector3(0.62f, 0.67f, 0.16f) : new Vector3(0.82f, 0.84f, 0.2f);
        }

        void SetGlow(Renderer surface, Color color)
        {
            surface.GetPropertyBlock(glow);
            glow.SetColor(Emissive, color);
            surface.SetPropertyBlock(glow);
        }

        void LateUpdate()
        {
            Vector3 heroFocus = player.transform.position + Vector3.up * 1.4f;
            bool climbing = rider == Rider.Grip || rider == Rider.Jump;
            Vector3 away = Vector3.ProjectOnPlane(heroFocus - boss.transform.position, Vector3.up).normalized;
            if (away.sqrMagnitude < 0.1f) away = boss.transform.forward;
            Vector3 focus = Vector3.Lerp(heroFocus, climbing ? seal.position : aimBody.position, climbing ? 0.32f : 0.28f);
            Vector3 desired = climbing
                ? heroFocus + boss.transform.right * 3.5f + boss.transform.forward * 3.1f + Vector3.up * 1.45f
                : heroFocus + away * 6.2f + Vector3.Cross(Vector3.up, away) * 1.65f + Vector3.up * 2.1f;
            desired = ClearCameraPosition(focus, desired);
            float blend = 1f - Mathf.Exp(-Time.deltaTime * (climbing ? 11f : 8f));
            Vector3 next = ClearCameraPosition(focus, Vector3.Lerp(view.transform.position, desired, blend));
            view.transform.position = next;
            Vector3 tremor = Vector3.up * (Mathf.Sin(Time.time * 74f) * shakeCamera * 0.16f);
            view.transform.rotation = Quaternion.LookRotation(focus - next + tremor);
        }

        Vector3 ClearCameraPosition(Vector3 focus, Vector3 destination)
        {
            Vector3 offset = destination - focus;
            float distance = offset.magnitude;
            if (distance < 0.01f) return destination;

            Vector3 direction = offset / distance;
            int count = Physics.SphereCastNonAlloc(focus, 0.25f, direction, cameraHits, distance, ~0,
                                                  QueryTriggerInteraction.Ignore);
            float limit = distance;
            for (int i = 0; i < count; i++)
            {
                var hit = cameraHits[i];
                if (hit.collider.transform.IsChildOf(player.transform)) continue;
                limit = Mathf.Min(limit, Mathf.Max(0.15f, hit.distance - 0.16f));
            }
            return focus + direction * limit;
        }

        void OnGUI()
        {
            var style = new GUIStyle(GUI.skin.label) { fontSize = 19, alignment = TextAnchor.MiddleCenter, wordWrap = true };
            string tip = "Вымани удар каменного стража";
            if (phase == BossPhase.Windup || phase == BossPhase.Strike) tip = "Замах! Выйди из круга или уклонись";
            if (phase == BossPhase.Exposed) tip = CanGrab() ? "E / A — прыгнуть на кисть" : "Подбеги к светящейся кисти";
            if (phase == BossPhase.Rise) tip = "Рука поднимается. Дождись следующего удара";
            if (rider == Rider.Jump) tip = "Захват кисти";
            if (rider == Rider.Grip) tip = climb >= 0.94f ? "ЛКМ / RB — ударить в печать" : "W / стик вверх — карабкаться";
            if (phase == BossPhase.Shake) tip = climb >= 0.94f ? "Сейчас стряхнёт! ЛКМ / RB — удар" : "Сейчас стряхнёт! E / A — спрыгнуть";
            if (rider == Rider.Fall) tip = wounds > 0 && flash > 0f ? "Печать треснула" : "Приземление";
            if (phase == BossPhase.Collapse || phase == BossPhase.Won) tip = "Каменный страж повержен   ·   R / Start — заново";
            if (phase == BossPhase.Reset) tip = "Иньнян пала. Возврат к воротам…";

            float width = Mathf.Min(720f, Screen.width - 24f);
            float left = (Screen.width - width) * 0.5f;
            GUI.Box(new Rect(left, 18f, width, 76f), GUIContent.none);
            GUI.Label(new Rect(left, 22f, width, 32f),
                "КАМЕННЫЙ СТРАЖ     Печать " + wounds + "/2     Здоровье " +
                (phase == BossPhase.Reset ? 0 : Mathf.CeilToInt(hero.Health)), style);
            GUI.Label(new Rect(left, 53f, width, 35f), tip, style);
        }

        static AudioClip MakeSound(string label, float duration, float frequency, float volume)
        {
            const int rate = 22050;
            var samples = new float[Mathf.CeilToInt(rate * duration)];
            var noise = new System.Random(34);
            float filtered = 0f;
            for (int i = 0; i < samples.Length; i++)
            {
                float time = i / (float)rate;
                filtered = Mathf.Lerp(filtered, (float)noise.NextDouble() * 2f - 1f, 0.12f);
                float tone = Mathf.Sin(2f * Mathf.PI * frequency * time * (1f - time * 0.6f));
                samples[i] = (tone * 0.55f + filtered * 0.85f) * Mathf.Min(1f, time * 450f) *
                             Mathf.Exp(-time * 8f) * volume;
            }
            var clip = AudioClip.Create(label, samples.Length, 1, rate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
