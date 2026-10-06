using System;
using UnityEngine;

namespace TanShadow.Combat
{
    // Ци врага — постура как в Sekiro: растёт от наших ударов и от дефлектов его атак.
    // Полная шкала или нулевое здоровье — враг сломлен и открыт для добивания.
    [RequireComponent(typeof(Combatant))]
    public class Posture : MonoBehaviour
    {
        [Min(1)] public float maxPosture = 100f;
        [Tooltip("До какой доли восстанавливается шкала, если врага не добили, 0–1")]
        [Range(0, 1)] public float valueAfterRecover = 0.5f;

        public float Value { get; private set; }
        public float Normalized => Value / maxPosture;
        public bool IsBroken { get; private set; }

        public event Action Broken;
        public event Action Recovered;

        Combatant combatant;
        float lastGainTime;
        float brokenLeft;

        CombatSettings Settings => combatant.settings;

        void Awake()
        {
            combatant = GetComponent<Combatant>();
            combatant.Hurt += OnHurt;
            combatant.AttackLanded += OnAttackLanded;
        }

        void OnHurt(HitInfo hit)
        {
            if (hit.result != HitResult.Hit) return;
            Add(hit.QiDamage);
            if (combatant.Health <= 0f) Break();
        }

        void OnAttackLanded(HitInfo hit)
        {
            if (hit.result == HitResult.Deflected) Add(hit.attack.qiDamage * Settings.postureDeflectFactor);
        }

        void Add(float amount)
        {
            if (IsBroken || combatant.IsDead) return;
            Value = Mathf.Min(maxPosture, Value + amount);
            lastGainTime = combatant.LocalTime;
            if (Value >= maxPosture) Break();
        }

        void Break()
        {
            if (IsBroken) return;
            Value = maxPosture;
            IsBroken = true;
            brokenLeft = Settings.brokenDuration;
            Broken?.Invoke();
        }

        public void ResetPosture()
        {
            Value = 0f;
            IsBroken = false;
        }

        void Update()
        {
            if (combatant.IsDead) return;
            float dt = combatant.DeltaTime;

            if (IsBroken)
            {
                brokenLeft -= dt;
                if (brokenLeft > 0f) return;
                IsBroken = false;
                Value = maxPosture * valueAfterRecover;
                if (combatant.Health <= 0f) combatant.ResetHealth();
                lastGainTime = combatant.LocalTime;
                Recovered?.Invoke();
                return;
            }

            if (combatant.LocalTime - lastGainTime > Settings.postureRecoverDelay)
                Value = Mathf.MoveTowards(Value, 0f, Settings.postureRecoverRate * dt);
        }
    }
}
