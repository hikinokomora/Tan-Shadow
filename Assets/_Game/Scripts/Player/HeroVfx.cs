using System;
using TanShadow.Combat;
using UnityEngine;
using Random = UnityEngine.Random;

namespace TanShadow.Player
{
    // Общие эффекты героини (VFX-инструкция v3): след от меча (G-01), вспышка отскока (G-02), пыль при
    // кувырке (G-03 / SU-02) и вмятины от шагов в мягком грунте (SU-01). Мягкий грунт — террейн и то, что
    // лежит в softGround (засыпанные русла лета); на камнях эффектов нет.
    // Без ссылок на испытания: сезонные эффекты зовут Rebound сами.
    [DefaultExecutionOrder(600)]
    public sealed class HeroVfx : MonoBehaviour
    {
        public HeroVfxSettings settings;
        public PlayerController controller;
        public AttackExecutor attacks;
        [Tooltip("Шлейф на лезвии кинжала")]
        public TrailRenderer trail;
        [Tooltip("Вспышка отскока: одна частица из пула")]
        public ParticleSystem rebound;
        [Tooltip("Пыль кувырка (world space)")]
        public ParticleSystem rollDust;
        [Tooltip("Плоский квадрат 1×1 в плоскости XZ и материал затемнения (TanShadow/VfxUnlit, Multiply)")]
        public Mesh footprintMesh;
        public Material footprintMaterial;
        [Tooltip("Цвет пыли этой сцены")]
        public Color groundColor = new Color(0.36f, 0.33f, 0.31f);
        [Tooltip("Что ещё считается мягким грунтом, кроме террейна")]
        public Transform[] softGround;

        // Шаг и кувырок по мягкому грунту (точка контакта) — для звука.
        public event Action<Vector3> SoftStep;
        public event Action<Vector3> SoftRoll;

        Animator animator;
        Transform leftFoot, rightFoot;
        bool leftDown = true, rightDown = true;
        PlayerState lastState;
        Vector3 lastPosition;

        Transform[] prints;
        Renderer[] printRenderers;
        float[] printAge;
        int nextPrint;
        MaterialPropertyBlock block;
        static readonly int ColorId = Shader.PropertyToID("_Color");

        void Awake()
        {
            if (controller == null) controller = GetComponent<PlayerController>();
            if (attacks == null) attacks = GetComponent<AttackExecutor>();
            animator = GetComponentInChildren<Animator>(true);
            if (animator != null && animator.isHuman)
            {
                leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
                rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
            }
            block = new MaterialPropertyBlock();
            lastPosition = transform.position;
            if (trail != null) { trail.emitting = false; trail.Clear(); }
            BuildPrints();
        }

        void OnEnable() { if (attacks != null) attacks.PhaseChanged += OnPhase; }
        void OnDisable()
        {
            if (attacks != null) attacks.PhaseChanged -= OnPhase;
            if (trail != null) trail.emitting = false;
        }

        void OnDestroy()
        {
            if (prints == null) return;
            foreach (var p in prints) if (p != null) Destroy(p.gameObject);
        }

        // Шлейф только в активной фазе взмаха.
        void OnPhase(AttackPhase phase)
        {
            if (trail == null) return;
            // Шлейф копит точки и без emitting: замах не должен попасть в след удара.
            if (phase == AttackPhase.Active && !trail.emitting) trail.Clear();
            trail.emitting = phase == AttackPhase.Active;
        }

        // G-02: удар не прошёл (неподходящая поверхность, удар 4 над сушей) — короткая вспышка в точке контакта.
        public void Rebound(Vector3 point, Vector3 normal)
        {
            if (rebound == null || settings == null) return;
            var p = new ParticleSystem.EmitParams
            {
                position = point + normal * 0.05f,
                velocity = Vector3.zero,
                startLifetime = settings.reboundTime,
                startSize = settings.reboundSize,
                rotation = Random.Range(0f, 360f),
                applyShapeToPosition = false
            };
            rebound.Emit(p, 1);
        }

        // Мгновенно убрать все следы (смерть, перезапуск, телепорт).
        public void ClearAll()
        {
            if (trail != null) { trail.emitting = false; trail.Clear(); }
            if (rollDust != null) rollDust.Clear();
            if (rebound != null) rebound.Clear();
            if (prints == null) return;
            for (int i = 0; i < prints.Length; i++) { printAge[i] = -1f; printRenderers[i].enabled = false; }
        }

        void LateUpdate()
        {
            if (settings == null) return;
            // Телепорт (возрождение под заливкой): шлейф и следы не тянутся через сцену.
            if ((transform.position - lastPosition).sqrMagnitude > 9f) ClearAll();
            lastPosition = transform.position;

            if (controller != null)
            {
                var state = controller.State;
                if (state == PlayerState.Dash && lastState != PlayerState.Dash && controller.enabled) RollBurst(controller.DashDirection);
                lastState = state;
                bool walking = controller.enabled && state == PlayerState.Locomotion;
                Step(leftFoot, ref leftDown, walking);
                Step(rightFoot, ref rightDown, walking);
            }
            FadePrints(Time.deltaTime);
        }

        // ---------------- Кувырок ----------------

        void RollBurst(Vector3 direction)
        {
            if (rollDust == null || !SoftGround(transform.position + Vector3.up * 0.3f, settings.groundProbe + 0.3f, out var hit)) return;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.01f) direction = transform.forward;
            direction.Normalize();
            Vector3 side = Vector3.Cross(Vector3.up, direction);
            SoftRoll?.Invoke(hit.point);
            int count = Random.Range(settings.rollCount.x, settings.rollCount.y + 1);
            var p = new ParticleSystem.EmitParams { applyShapeToPosition = false, startColor = groundColor };
            for (int i = 0; i < count; i++)
            {
                p.position = hit.point + side * Random.Range(-0.3f, 0.3f) + Vector3.up * 0.05f;
                p.velocity = -direction * Random.Range(settings.rollSpeed.x, settings.rollSpeed.y)
                             + side * Random.Range(-1f, 1f) + Vector3.up * Random.Range(settings.rollLift.x, settings.rollLift.y);
                p.startLifetime = Random.Range(settings.rollLifetime.x, settings.rollLifetime.y);
                p.startSize = Random.Range(settings.rollSize.x, settings.rollSize.y);
                p.rotation = Random.Range(0f, 360f);
                rollDust.Emit(p, 1);
            }
        }

        // ---------------- Шаги ----------------

        // Касание стопы: стопа опустилась к корню героини. Луч — только в момент касания, не каждый кадр.
        void Step(Transform foot, ref bool down, bool walking)
        {
            if (foot == null) return;
            float h = foot.position.y - transform.position.y;
            if (down) { if (h > settings.footReleaseHeight) down = false; return; }
            if (h > settings.footContactHeight) return;
            down = true;
            if (!walking || !SoftGround(foot.position + Vector3.up * 0.3f, 0.6f, out var hit)) return;
            PlacePrint(hit.point, hit.normal, transform.forward);
            SoftStep?.Invoke(hit.point);
        }

        void BuildPrints()
        {
            if (settings == null || footprintMesh == null || footprintMaterial == null) return;
            int n = settings.footprintCount;
            prints = new Transform[n];
            printRenderers = new Renderer[n];
            printAge = new float[n];
            for (int i = 0; i < n; i++)
            {
                var go = new GameObject("Hero footprint " + i);
                go.AddComponent<MeshFilter>().sharedMesh = footprintMesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = footprintMaterial;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.enabled = false;
                prints[i] = go.transform;
                printRenderers[i] = r;
                printAge[i] = -1f;
            }
        }

        void PlacePrint(Vector3 point, Vector3 normal, Vector3 forward)
        {
            if (prints == null) return;
            int i = nextPrint;
            nextPrint = (nextPrint + 1) % prints.Length;
            forward = Vector3.ProjectOnPlane(forward, normal);
            if (forward.sqrMagnitude < 0.001f) forward = Vector3.ProjectOnPlane(Vector3.forward, normal);
            prints[i].SetPositionAndRotation(point + normal * 0.012f, Quaternion.LookRotation(forward, normal));
            prints[i].localScale = new Vector3(settings.footprintSize.x, 1f, settings.footprintSize.y);
            printAge[i] = 0f;
            printRenderers[i].enabled = true;
            SetPrintAlpha(i, 1f);
        }

        void FadePrints(float dt)
        {
            if (prints == null) return;
            for (int i = 0; i < prints.Length; i++)
            {
                if (printAge[i] < 0f) continue;
                printAge[i] += dt;
                float fade = 1f - Mathf.Clamp01((printAge[i] - settings.footprintHold) / Mathf.Max(0.01f, settings.footprintFade));
                if (fade <= 0f) { printAge[i] = -1f; printRenderers[i].enabled = false; continue; }
                if (printAge[i] > settings.footprintHold) SetPrintAlpha(i, fade);
            }
        }

        void SetPrintAlpha(int i, float fade)
        {
            block.SetColor(ColorId, new Color(1f - settings.footprintDarkness, 1f - settings.footprintDarkness, 1f - settings.footprintDarkness, fade));
            printRenderers[i].SetPropertyBlock(block);
        }

        // ---------------- Грунт ----------------

        readonly RaycastHit[] hits = new RaycastHit[8];

        bool SoftGround(Vector3 from, float distance, out RaycastHit ground)
        {
            ground = default;
            int count = Physics.RaycastNonAlloc(from, Vector3.down, hits, distance, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            bool found = false;
            for (int i = 0; i < count; i++)
            {
                var c = hits[i].collider;
                if (c is CharacterController || c.transform.IsChildOf(transform)) continue;
                if (hits[i].distance < best) { best = hits[i].distance; ground = hits[i]; found = true; }
            }
            return found && IsSoft(ground.collider);
        }

        bool IsSoft(Collider c)
        {
            if (c is TerrainCollider) return true;
            if (softGround != null)
                foreach (var root in softGround)
                    if (root != null && c.transform.IsChildOf(root)) return true;
            return false;
        }
    }
}
