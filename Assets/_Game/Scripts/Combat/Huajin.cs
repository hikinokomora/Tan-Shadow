using System;
using UnityEngine;

namespace TanShadow.Combat
{
    // Хуацзинь (化劲) — «четыре ляна отводят тысячу цзиней».
    // Идеальный дефлект копит силу вражеского удара: следующий удар в течение окна бьёт по ци сильнее.
    [RequireComponent(typeof(Combatant))]
    public class Huajin : MonoBehaviour
    {
        public float Charge { get; private set; }
        public bool IsCharged => combatant.LocalTime < chargedUntil;
        public float TimeLeft => Mathf.Max(0f, chargedUntil - combatant.LocalTime);
        public float Multiplier => IsCharged ? 1f + Charge * Settings.huajinForceFactor : 1f;

        public event Action Charged;
        // Усиленный удар дошёл до цели.
        public event Action<HitInfo> Released;

        Combatant combatant;
        float chargedUntil = float.NegativeInfinity;

        CombatSettings Settings => combatant.settings;

        void Awake()
        {
            combatant = GetComponent<Combatant>();
            combatant.Hurt += OnHurt;
            combatant.AttackLanded += OnAttackLanded;
        }

        void OnHurt(HitInfo hit)
        {
            if (hit.result != HitResult.Deflected) return;
            Charge = hit.attack.force;
            chargedUntil = combatant.LocalTime + Settings.huajinWindow;
            Charged?.Invoke();
        }

        // Забрать заряд в начале атаки.
        public float Consume()
        {
            if (!IsCharged) return 1f;
            float m = Multiplier;
            chargedUntil = float.NegativeInfinity;
            return m;
        }

        void OnAttackLanded(HitInfo hit)
        {
            if (hit.qiMultiplier <= 1f || hit.result != HitResult.Hit) return;
            hit.defender.Freeze(Settings.huajinHitstopBonus);
            combatant.Freeze(Settings.huajinHitstopBonus);
            Released?.Invoke(hit);
        }
    }
}
