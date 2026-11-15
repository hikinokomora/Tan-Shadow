using System;
using TanShadow.Combat;
using UnityEngine;

namespace TanShadow.Enemies
{
    // Босс на двух знаках смерти: после первого добивания встаёт и дерётся быстрее, с другими сериями.
    // Числа второй фазы — здесь, на префабе босса; атаки — в его AttackData.
    [RequireComponent(typeof(GuardAI))]
    public class BossPhases : MonoBehaviour
    {
        public string displayName = "精精儿";
        [Tooltip("Серии второй фазы; пусто — те же, что в первой")]
        public GuardPattern[] phase2Patterns;
        [Tooltip("Во второй фазе ходит и кружит быстрее во столько раз")]
        [Min(0.1f)] public float phase2SpeedScale = 1.25f;
        [Tooltip("Паузы между сериями во второй фазе — такая доля от первой")]
        [Min(0)] public float phase2CooldownScale = 0.6f;
        [Tooltip("Сколько лежит после первого добивания, прежде чем встать, с")]
        [Min(0)] public float riseDelay = 2f;

        public const int Deathblows = 2;
        public int Phase { get; private set; } // 0 — первая, 1 — вторая
        public bool IsDead => combatant.IsDead;
        // Сколько знаков смерти ещё не поставлено (для HUD).
        public int DeathblowsLeft => IsDead ? Deathblows - Phase - 1 : Deathblows - Phase;
        public event Action<int> PhaseStarted;

        GuardAI ai;
        Combatant combatant;
        GuardPattern[] phase1Patterns;
        float moveSpeed, circleSpeed, cooldownMin, cooldownMax;
        float riseAt = -1f;

        void Awake()
        {
            ai = GetComponent<GuardAI>();
            combatant = GetComponent<Combatant>();
            phase1Patterns = ai.patterns;
            moveSpeed = ai.moveSpeed;
            circleSpeed = ai.circleSpeed;
            cooldownMin = ai.cooldownMin;
            cooldownMax = ai.cooldownMax;
            combatant.Finished += _ => { if (Phase == 0) riseAt = Time.time + riseDelay; };
            ai.Revived += () => { riseAt = -1f; ApplyPhase(0); };
        }

        void Update()
        {
            if (riseAt < 0f || Time.time < riseAt) return;
            riseAt = -1f;
            ai.ReviveInPlace();
            ApplyPhase(1);
        }

        void ApplyPhase(int phase)
        {
            Phase = phase;
            bool second = phase > 0;
            ai.patterns = second && phase2Patterns != null && phase2Patterns.Length > 0 ? phase2Patterns : phase1Patterns;
            float speed = second ? phase2SpeedScale : 1f;
            float pause = second ? phase2CooldownScale : 1f;
            ai.moveSpeed = moveSpeed * speed;
            ai.circleSpeed = circleSpeed * speed;
            ai.cooldownMin = cooldownMin * pause;
            ai.cooldownMax = cooldownMax * pause;
            PhaseStarted?.Invoke(phase);
        }
    }
}
