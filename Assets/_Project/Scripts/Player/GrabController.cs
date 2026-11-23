using ShadowTan.Core.Contracts;
using ShadowTan.Core.Events;
using ShadowTan.Core.Services;
using UnityEngine;

namespace ShadowTan.Player
{
    // Ищет ближайшую доступную цель зацепа, привязывает героиню к якорю и отвязывает (§9.6.4).
    // Героиня не становится дочерней объекту колосса: иначе её уничтожило бы вместе с телом.
    public class GrabController : MonoBehaviour
    {
        [SerializeField] PlayerController player;
        [SerializeField] PlayerMotor motor;
        [SerializeField] PlayerQuery query;
        [Tooltip("Точка, от которой ищется зацеп; пусто — грудь героини")]
        [SerializeField] Transform probe;
        [SerializeField] LayerMask grabMask = 1 << ShadowTan.Core.Layers.GrabPointIndex;

        [Header("Привязка")]
        [Tooltip("Смещение героини относительно якоря, в локальных осях якоря")]
        [SerializeField] Vector3 hangLocalOffset = new Vector3(0f, -1.6f, 0.3f);

        readonly Collider[] overlap = new Collider[16];
        float searchRadius = 1.5f;
        float snapTime = 0.15f;

        IGrabTarget candidate;
        IGrabTarget current;
        Transform anchor;
        Vector3 snapFrom;
        Quaternion anchorStartRotation;
        Quaternion playerStartRotation;
        float snapElapsed;

        public bool IsAttached => IsAlive(current) && anchor != null && current.Anchor != null;
        public IGrabTarget CurrentTarget => IsAttached ? current : null;
        public bool HasCandidate => candidate != null && IsAlive(candidate);

        void Awake()
        {
            if (player == null) player = GetComponent<PlayerController>();
            if (motor == null) motor = GetComponent<PlayerMotor>();
            if (query == null) query = GetComponent<PlayerQuery>();
            if (probe == null) probe = transform;
        }

        public void Configure(float radius, float attachSnapTime)
        {
            searchRadius = radius;
            snapTime = attachSnapTime;
        }

        public void Scan()
        {
            candidate = null;
            if (IsAttached) return;

            var center = probe.position;
            var count = Physics.OverlapSphereNonAlloc(center, searchRadius, overlap, grabMask,
                QueryTriggerInteraction.Collide);

            var bestDistance = float.MaxValue;
            for (var i = 0; i < count; i++)
            {
                var target = overlap[i].GetComponentInParent<IGrabTarget>();
                if (target == null || !IsAlive(target) || target.Anchor == null) continue;
                if (query != null && !target.CanGrab(query)) continue;

                var distance = (target.Anchor.position - center).sqrMagnitude;
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                candidate = target;
            }
        }

        public bool TryAttach()
        {
            if (IsAttached) return false;
            if (!HasCandidate) return false;

            current = candidate;
            candidate = null;
            anchor = current.Anchor;

            snapFrom = transform.position;
            anchorStartRotation = anchor.rotation;
            playerStartRotation = transform.rotation;
            snapElapsed = 0f;

            motor?.Suspend(true);
            current.Shaken += OnShaken;
            current.OnGrabbed();
            EventBus.Publish(new PlayerGrabbed(current, current.Mode));
            return true;
        }

        public void Follow(float deltaTime)
        {
            if (!IsAttached)
            {
                if (current != null) Detach(GrabReleaseReason.TargetUnavailable);
                return;
            }

            if (current.Mode != GrabMode.FlyWithTarget) return;

            snapElapsed += deltaTime;
            var t = snapTime <= 0f ? 1f : Mathf.Clamp01(snapElapsed / snapTime);
            var targetPosition = anchor.position + anchor.rotation * hangLocalOffset;

            transform.position = Vector3.Lerp(snapFrom, targetPosition, t);
            transform.rotation = anchor.rotation * (Quaternion.Inverse(anchorStartRotation) * playerStartRotation);
        }

        public void Detach(GrabReleaseReason reason)
        {
            var target = current;
            if (target == null) return;

            current = null;
            anchor = null;

            if (IsAlive(target))
            {
                target.Shaken -= OnShaken;
                target.OnReleased();
            }

            motor?.Suspend(false);
            motor?.Warp(transform.position);

            EventBus.Publish(new PlayerReleased(target, reason));
        }

        void OnShaken()
        {
            Detach(GrabReleaseReason.Shaken);
        }

        // Уничтоженный компонент нельзя держать как интерфейс: Unity-объект уже null, но интерфейс — нет.
        static bool IsAlive(IGrabTarget target)
        {
            return target != null && target is Object unityObject && unityObject != null;
        }
    }
}
