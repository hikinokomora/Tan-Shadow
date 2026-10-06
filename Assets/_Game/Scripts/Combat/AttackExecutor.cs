using System;
using System.Collections.Generic;
using UnityEngine;

namespace TanShadow.Combat
{
    public enum AttackPhase
    {
        None,
        Windup,
        Active,
        Recovery
    }

    // Проигрывает AttackData по фазам и в активной фазе ищет цели перед бойцом.
    [RequireComponent(typeof(Combatant))]
    public class AttackExecutor : MonoBehaviour
    {
        [Tooltip("Расстояние от бойца до центра зоны удара, м")]
        [Min(0)] public float reach = 1.1f;
        [Tooltip("Радиус зоны удара, м")]
        [Min(0)] public float radius = 0.8f;
        [Tooltip("Высота центра зоны удара, м")]
        public float height = 1f;

        public AttackData Current { get; private set; }
        public AttackPhase Phase { get; private set; }
        public float PhaseTime { get; private set; }
        public bool IsBusy => Phase != AttackPhase.None;

        public event Action<AttackPhase> PhaseChanged;
        // Смена фазы у любого бойца — для звука свиста и т. п.
        public static event Action<AttackExecutor, AttackPhase> AnyPhaseChanged;

        static readonly Collider[] overlap = new Collider[16];
        readonly List<Combatant> alreadyHit = new List<Combatant>();
        Combatant self;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => AnyPhaseChanged = null;

        void Awake() => self = GetComponent<Combatant>();

        // Множитель урона по ци для текущей атаки (хуацзинь).
        public float QiMultiplier { get; private set; } = 1f;

        public void Begin(AttackData attack, float qiMultiplier = 1f)
        {
            Current = attack;
            QiMultiplier = qiMultiplier;
            alreadyHit.Clear();
            SetPhase(AttackPhase.Windup, 0f);
        }

        public void Cancel()
        {
            if (!IsBusy) return;
            Current = null;
            SetPhase(AttackPhase.None, 0f);
        }

        void Update()
        {
            if (!IsBusy) return;
            PhaseTime += self.DeltaTime;

            switch (Phase)
            {
                case AttackPhase.Windup:
                    if (PhaseTime >= Current.windup)
                        SetPhase(AttackPhase.Active, PhaseTime - Current.windup);
                    break;
                case AttackPhase.Active:
                    SweepHits();
                    if (IsBusy && PhaseTime >= Current.active)
                        SetPhase(AttackPhase.Recovery, PhaseTime - Current.active);
                    break;
                case AttackPhase.Recovery:
                    if (PhaseTime >= Current.recovery)
                    {
                        Current = null;
                        SetPhase(AttackPhase.None, 0f);
                    }
                    break;
            }
        }

        void SetPhase(AttackPhase phase, float carry)
        {
            Phase = phase;
            PhaseTime = carry;
            PhaseChanged?.Invoke(phase);
            AnyPhaseChanged?.Invoke(this, phase);
        }

        // У атаки со своей дальностью дальний край зоны удара совпадает с range.
        float Reach => Current != null && Current.range > 0f ? Mathf.Max(0f, Current.range - radius) : reach;
        Vector3 HitCenter => transform.position + Vector3.up * height + transform.forward * Reach;

        void SweepHits()
        {
            int count = Physics.OverlapSphereNonAlloc(HitCenter, radius, overlap, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var target = overlap[i].GetComponentInParent<Combatant>();
                if (target == null || target == self || target.IsDead || alreadyHit.Contains(target)) continue;

                alreadyHit.Add(target);
                target.ReceiveHit(Current, self, QiMultiplier);
                // Реакция на дефлект могла отменить атаку.
                if (!IsBusy) return;
            }
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Phase == AttackPhase.Active ? Color.red : new Color(1f, 0.6f, 0f, 0.6f);
            Gizmos.DrawWireSphere(HitCenter, radius);
        }
    }
}
