using System.Collections.Generic;
using ShadowTan.Combat;
using ShadowTan.Core;
using ShadowTan.Core.Contracts;
using ShadowTan.Core.Events;
using ShadowTan.Core.Services;
using UnityEngine;

namespace ShadowTan.Player
{
    // Доставляет попадание меча и публикует события. Не решает, засчитан ли удар (ADR-006).
    public class SwordController : MonoBehaviour
    {
        [SerializeField] PlayerController player;
        [Tooltip("Точка, от которой считается дуга удара (грудь или кисть)")]
        [SerializeField] Transform origin;

        [Tooltip("Поверхность для коллайдеров без SurfaceTag")]
        [SerializeField] SurfaceDefinition defaultSurface;

        [Header("Маски слоёв")]
        [SerializeField] LayerMask swingMask = (1 << Layers.WeakPointIndex) | (1 << Layers.ColossusIndex);
        [SerializeField] LayerMask stabMask = (1 << Layers.WeakPointIndex) | (1 << Layers.ColossusIndex) |
                                                (1 << Layers.WallIndex) | (1 << Layers.GroundIndex) | (1 << Layers.CoverIndex);
        [SerializeField] LayerMask scareMask = 1 << Layers.ColossusIndex;

        readonly Collider[] overlap = new Collider[32];
        readonly List<Collider> candidates = new List<Collider>(32);
        readonly HashSet<ISwordTarget> hitTargets = new HashSet<ISwordTarget>();
        bool resolved;

        public Transform Origin => origin != null ? origin : transform;

        void Awake()
        {
            if (player == null) player = GetComponent<PlayerController>();
            if (origin == null) origin = transform;
        }

        public void BeginSwing()
        {
            hitTargets.Clear();
            resolved = false;
        }

        // Возвращает true, если взмах прерван отскоком.
        public bool CheckSwing(bool whileHanging)
        {
            if (resolved) return false;
            var config = player != null ? player.Config : null;
            if (config == null) return false;
            return Resolve(SwordActionType.Swing, whileHanging, config.attackRange, config.attackArc, swingMask);
        }

        public bool Stab(bool whileHanging)
        {
            if (resolved) return false;
            var config = player != null ? player.Config : null;
            if (config == null) return false;
            return Resolve(SwordActionType.Stab, whileHanging, config.attackRange, config.attackArc, stabMask);
        }

        // Направленный взмах пугает IScareable в конусе (§10.5).
        public void ScareSwing(Vector3 direction)
        {
            var config = player != null ? player.Config : null;
            if (config == null) return;

            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f) direction = transform.forward;
            direction.Normalize();

            var center = player.transform.position;
            var count = Physics.OverlapSphereNonAlloc(center, config.scareRange, overlap, scareMask,
                QueryTriggerInteraction.Collide);

            var scared = new HashSet<IScareable>();
            for (var i = 0; i < count; i++)
            {
                var scareable = overlap[i].GetComponentInParent<IScareable>();
                if (scareable == null || !scared.Add(scareable)) continue;

                var component = scareable as Component;
                if (component == null) continue;

                var toTarget = component.transform.position - center;
                toTarget.y = 0f;
                if (toTarget.sqrMagnitude > config.scareRange * config.scareRange) continue;
                if (Vector3.Angle(direction, toTarget) > config.scareConeAngle * 0.5f) continue;

                scareable.OnScareSwing(direction, center);
            }
        }

        bool Resolve(SwordActionType action, bool whileHanging, float range, float arc, LayerMask mask)
        {
            var swordOrigin = Origin;
            var forward = player != null ? player.transform.forward : transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) forward = transform.forward;

            var count = Physics.OverlapSphereNonAlloc(swordOrigin.position, range, overlap, mask,
                QueryTriggerInteraction.Collide);

            candidates.Clear();
            for (var i = 0; i < count; i++)
            {
                var collider = overlap[i];
                if (collider == null) continue;
                if (player != null && collider.transform.IsChildOf(player.transform)) continue;
                candidates.Add(collider);
            }

            candidates.Sort((a, b) =>
                (a.ClosestPoint(swordOrigin.position) - swordOrigin.position).sqrMagnitude
                    .CompareTo((b.ClosestPoint(swordOrigin.position) - swordOrigin.position).sqrMagnitude));

            foreach (var collider in candidates)
            {
                var point = collider.ClosestPoint(swordOrigin.position);
                var toPoint = point - swordOrigin.position;
                if (!SwordArc.Contains(swordOrigin.position, forward, point, range, arc)) continue;

                var target = collider.GetComponentInParent<ISwordTarget>();
                var surface = ResolveSurface(collider);

                if (action == SwordActionType.Stab && surface?.AcceptsStab != true)
                {
                    resolved = true;
                    EventBus.Publish(new SwordDeflected(surface, point));
                    return true;
                }

                if (target != null)
                {
                    if (action == SwordActionType.Swing && hitTargets.Contains(target)) continue;

                    var normal = toPoint.sqrMagnitude > 0.0001f ? -toPoint.normalized : -forward;
                    var hit = new HitInfo(action, point, normal, forward, whileHanging, surface);
                    var result = target.OnSwordHit(in hit);

                    if (action == SwordActionType.Swing) hitTargets.Add(target);
                    resolved = true;
                    EventBus.Publish(new SwordHit(target, result, surface, point));

                    if (result == HitResult.Deflected)
                    {
                        EventBus.Publish(new SwordDeflected(surface, point));
                        return true;
                    }

                    // По умолчанию одна дуга задевает только первую цель (TBD §10.3).
                    if (action == SwordActionType.Swing) return false;
                }

                if (action != SwordActionType.Stab) continue;
                resolved = true;
                EventBus.Publish(new SwordStuck(surface, point));
                return false;
            }

            return false;
        }

        ISurface ResolveSurface(Collider collider)
        {
            var tag = collider.GetComponentInParent<SurfaceTag>();
            if (tag != null && tag.Surface != null) return tag.Surface;
            return defaultSurface;
        }
    }
}
