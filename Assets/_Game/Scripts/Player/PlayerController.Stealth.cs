using TanShadow.Combat;
using TanShadow.Enemies;
using UnityEngine;

namespace TanShadow.Player
{
    // Убийство из тени: сзади по земле или сверху в прыжке — по стражнику, который ещё не поднял тревогу.
    // Проводится тем же приёмом добивания, что и после слома ци. Числа — в CombatSettings («Стелс»).
    public partial class PlayerController
    {
        // Кого можно убить из тени прямо сейчас (для подсказки на экране); null — никого.
        public Combatant StealthKillTarget { get; private set; }

        void UpdateStealthKillTarget()
        {
            StealthKillTarget = null;
            bool onGround = State == PlayerState.Locomotion;
            bool fromAbove = State == PlayerState.Air;
            if (!onGround && !fromAbove) return;

            float best = float.MaxValue;
            foreach (var guard in GuardStealth.All)
            {
                if (guard.IsDead || guard.IsAlert) continue;
                Vector3 toMe = transform.position - guard.transform.position;
                float height = toMe.y;
                toMe.y = 0f;
                float distance = toMe.magnitude;

                bool ok = onGround
                    ? distance <= settings.stealthKillRange && Mathf.Abs(height) < 0.8f &&
                      Vector3.Angle(guard.transform.forward, toMe) >= settings.stealthKillBehindAngle
                    : height >= settings.stealthKillFromAboveHeight && distance <= settings.stealthKillFromAboveRange;
                if (!ok || distance >= best) continue;
                best = distance;
                StealthKillTarget = guard.GetComponent<Combatant>();
            }
        }

        // Удар в прыжке есть только один — сверху по ничего не подозревающему стражнику.
        bool TryStealthKillFromAir()
        {
            if (StealthKillTarget == null) return false;
            if (!buffer.Consume(BufferedAction.Attack, combatant.LocalTime, settings.inputBuffer)) return false;
            StartFinisher(StealthKillTarget);
            return true;
        }
    }
}
