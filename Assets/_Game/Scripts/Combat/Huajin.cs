using System;
using UnityEngine;

namespace TanShadow.Combat
{
    // Хуацзинь (化劲) — «четыре ляна отводят тысячу цзиней».
    // Идеальный дефлект копит силу вражеского удара: первый удар, дошедший до врага в течение окна, бьёт по ци сильнее.
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

        void OnAttackLanded(HitInfo hit)
        {
            if (hit.qiMultiplier <= 1f || hit.result == HitResult.Evaded) return;
            // Заряд тратится, только когда удар дошёл до врага: после дефлекта стражника отбрасывает,
            // и укол мимо не должен сжигать хуацзинь.
            chargedUntil = float.NegativeInfinity;
            if (hit.result != HitResult.Hit) return;
            // Freeze берёт максимум, а не сумму: бонус прибавляем к хитстопу самого удара.
            float stop = hit.attack.hitstop + Settings.huajinHitstopBonus;
            hit.defender.Freeze(stop);
            combatant.Freeze(stop);
            Released?.Invoke(hit);
        }
    }
}
