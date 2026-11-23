using UnityEngine;

namespace ShadowTan.Player
{
    // Движение через CharacterController: скорость, ускорение, поворот, гравитация, земля,
    // сползание по крутым склонам и внешние силы. Не знает о вводе и состояниях (§9.2).
    [RequireComponent(typeof(CharacterController))]
    public class PlayerMotor : MonoBehaviour
    {
        [SerializeField] CharacterController controller;
        [SerializeField] LayerMask groundMask = ~0;
        [Tooltip("Точка проверки земли; пусто — центр капсулы")]
        [SerializeField] Transform groundProbe;

        PlayerConfig config;
        Vector3 planarVelocity;
        Vector3 externalVelocity;
        float verticalVelocity;
        float lastGroundedTime = -100f;
        float lastGroundedHeight;
        bool suspended;

        public Vector3 PlanarVelocity => planarVelocity;
        public Vector3 Velocity => planarVelocity + externalVelocity + Vector3.up * verticalVelocity;
        public bool IsGrounded { get; private set; }
        public bool IsSuspended => suspended;
        public float TimeSinceGrounded => Time.time - lastGroundedTime;
        public float LastGroundedHeight => lastGroundedHeight;

        void Awake()
        {
            if (controller == null) controller = GetComponent<CharacterController>();
        }

        public void Configure(PlayerConfig playerConfig)
        {
            config = playerConfig;
            if (controller == null) controller = GetComponent<CharacterController>();
            if (config == null || controller == null) return;

            controller.slopeLimit = config.maxWalkableSlope;
            controller.skinWidth = Mathf.Min(0.08f, config.stepOffset * 0.5f);
            // PhysX меряет подъём от опорной позиции капсулы с зазором skinWidth.
            const float stepTolerance = 0.0001f;
            controller.stepOffset = config.stepOffset > 0f
                ? Mathf.Max(0f, config.stepOffset - controller.skinWidth + stepTolerance)
                : 0f;
            controller.minMoveDistance = 0f;
            lastGroundedHeight = transform.position.y;
            SetGrounded(ProbeGround());
        }

        // Обычное движение: плавный разгон и торможение.
        public void MoveTowards(Vector3 desiredPlanar, float acceleration, float deceleration, float deltaTime)
        {
            var rate = desiredPlanar.sqrMagnitude > planarVelocity.sqrMagnitude ? acceleration : deceleration;
            planarVelocity = Vector3.MoveTowards(planarVelocity, desiredPlanar, rate * deltaTime);
            ApplyMotion(deltaTime);
        }

        // Прямая скорость: кувырок, падение.
        public void MoveAt(Vector3 velocity, float deltaTime)
        {
            planarVelocity = velocity;
            ApplyMotion(deltaTime);
        }

        // Гашение скорости за заданное время (attackStopTime, I-3).
        public void StopIn(float seconds, float deltaTime)
        {
            var rate = planarVelocity.magnitude / Mathf.Max(seconds, 0.0001f);
            MoveTowards(Vector3.zero, rate, rate, deltaTime);
        }

        public void StopImmediate()
        {
            planarVelocity = Vector3.zero;
            externalVelocity = Vector3.zero;
        }

        public void Face(Vector3 direction, float degreesPerSecond, float deltaTime)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f) return;

            var target = Quaternion.LookRotation(direction, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, target, degreesPerSecond * deltaTime);
        }

        public void SnapFace(Vector3 direction)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f) return;
            transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
        }

        public void AddExternalVelocity(Vector3 velocity)
        {
            externalVelocity = velocity;
        }

        public void Suspend(bool value)
        {
            suspended = value;
            if (controller != null) controller.enabled = !value;
            if (value)
            {
                planarVelocity = Vector3.zero;
                externalVelocity = Vector3.zero;
                verticalVelocity = 0f;
            }
        }

        public void Warp(Vector3 position)
        {
            var wasEnabled = controller.enabled;
            controller.enabled = false;
            transform.position = position;
            controller.enabled = wasEnabled;
            verticalVelocity = 0f;
            planarVelocity = Vector3.zero;
            externalVelocity = Vector3.zero;
            lastGroundedHeight = position.y;
            SetGrounded(wasEnabled && ProbeGround());
        }

        void ApplyMotion(float deltaTime)
        {
            if (suspended || config == null || deltaTime <= 0f) return;

            if (IsGrounded && verticalVelocity <= 0f)
            {
                verticalVelocity = -2f;
            }
            else
            {
                verticalVelocity -= config.gravity * deltaTime;
            }

            var motion = planarVelocity + externalVelocity;

            // Слишком крутой склон: героиня сползает вниз, подъём невозможен (I-1).
            if (IsGrounded && TryGetGround(out var ground))
            {
                var slopeAngle = Vector3.Angle(ground.normal, Vector3.up);
                if (slopeAngle > controller.slopeLimit + 0.001f)
                {
                    var downhill = Vector3.ProjectOnPlane(Vector3.down, ground.normal).normalized;
                    motion += downhill * config.steepSlideSpeed;
                }
            }

            motion.y += verticalVelocity;
            var contacts = controller.Move(motion * deltaTime);
            SetGrounded((contacts & CollisionFlags.Below) != 0);
            externalVelocity = Vector3.MoveTowards(externalVelocity, Vector3.zero, 10f * deltaTime);
        }

        bool ProbeGround()
        {
            var origin = transform.position + controller.center - Vector3.up * (controller.height * 0.5f);
            var lift = controller.radius;
            var distance = lift + controller.skinWidth + Physics.defaultContactOffset;
            return Physics.Raycast(origin + Vector3.up * lift, Vector3.down, distance,
                groundMask, QueryTriggerInteraction.Ignore);
        }

        void SetGrounded(bool value)
        {
            IsGrounded = value;
            if (!value) return;
            lastGroundedTime = Time.time;
            lastGroundedHeight = transform.position.y;
        }

        public bool TryGetGround(out RaycastHit hit)
        {
            var origin = groundProbe != null ? groundProbe.position : transform.position + Vector3.up * 0.2f;
            return Physics.Raycast(origin, Vector3.down, out hit, 1.2f, groundMask, QueryTriggerInteraction.Ignore);
        }

        public void SetGroundMask(LayerMask mask) => groundMask = mask;
    }
}
