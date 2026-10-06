using System;
using UnityEngine;

namespace TanShadow.Combat
{
    public enum AttackType
    {
        Normal,
        Unblockable // только уворот, блок и дефлект не работают
    }

    [Flags]
    public enum AttackTelegraph
    {
        None = 0,
        BladeGlint = 1 << 0,
        DangerKanji = 1 << 1, // красный 危 над врагом
        Sound = 1 << 2
    }

    // Контракт атаки, общий для игрока и врагов. Числа тюнятся здесь, не в коде.
    [CreateAssetMenu(fileName = "Attack_", menuName = "Tan Shadow/Attack Data")]
    public class AttackData : ScriptableObject
    {
        [Header("Урон")]
        [Tooltip("Урон по здоровью")]
        [Min(0)] public float damage = 10f;
        [Tooltip("Урон по ци (постуре) цели")]
        [Min(0)] public float qiDamage = 20f;

        [Header("Фазы, с (синхронно с анимацией)")]
        [Tooltip("Замах: удар ещё не бьёт, атаку можно отменить в блок")]
        [Min(0)] public float windup = 0.4f;
        [Tooltip("Активная фаза: удар попадает и его можно отразить")]
        [Min(0)] public float active = 0.15f;
        [Tooltip("Восстановление: атакующий открыт")]
        [Min(0)] public float recovery = 0.4f;

        [Header("Свойства")]
        public AttackType type = AttackType.Normal;
        [Tooltip("Сила удара. После дефлекта усиливает ответ: × (1 + force × 0,5) по ци")]
        [Min(0)] public float force = 1f;
        [Tooltip("Замирание обоих участников на попадании, с")]
        [Min(0)] public float hitstop = 0.06f;
        [Tooltip("Чем атака предупреждает игрока")]
        public AttackTelegraph telegraph = AttackTelegraph.BladeGlint;
        [Tooltip("Дальность удара, м. 0 — по умолчанию оружия (AttackExecutor)")]
        [Min(0)] public float range = 0f;

        public float Duration => windup + active + recovery;
    }
}
