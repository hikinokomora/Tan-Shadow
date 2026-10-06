using System;
using UnityEngine;

namespace TanShadow.Combat
{
    // Ци игрока — одна шкала от −100 (инь) до +100 (ян).
    // Атаки тянут к ян, блоки к инь, идеальный дефлект возвращает к центру. У края — оглушение.
    [RequireComponent(typeof(Combatant))]
    public class YinYangQi : MonoBehaviour
    {
        public const float Limit = 100f;

        public float Value { get; private set; }
        public float Normalized => Value / Limit;

        // Вышли за край: −1 — инь, +1 — ян.
        public event Action<int> Overflow;

        Combatant combatant;
        float lastChangeTime;

        CombatSettings Settings => combatant.settings;

        void Awake()
        {
            combatant = GetComponent<Combatant>();
            combatant.Hurt += OnHurt;
            var executor = GetComponent<AttackExecutor>();
            if (executor != null) executor.PhaseChanged += OnAttackPhase;
        }

        void OnAttackPhase(AttackPhase phase)
        {
            if (phase == AttackPhase.Windup) Shift(Settings.qiAttackShift);
        }

        void OnHurt(HitInfo hit)
        {
            switch (hit.result)
            {
                case HitResult.Blocked:
                    Shift(-hit.attack.qiDamage * Settings.qiBlockFactor);
                    break;
                case HitResult.Deflected:
                    Value = Mathf.MoveTowards(Value, 0f, Settings.qiDeflectRestore);
                    lastChangeTime = combatant.LocalTime;
                    break;
            }
        }

        void Shift(float amount)
        {
            Value += amount;
            lastChangeTime = combatant.LocalTime;
            if (Mathf.Abs(Value) < Limit) return;

            int side = Value > 0f ? 1 : -1;
            Value = 0f;
            Overflow?.Invoke(side);
        }

        void Update()
        {
            if (combatant.LocalTime - lastChangeTime < Settings.qiRecoverDelay) return;
            Value = Mathf.MoveTowards(Value, 0f, Settings.qiRecoverRate * combatant.DeltaTime);
        }
    }
}
