using System;
using System.Collections.Generic;
using UnityEngine;

namespace TanShadow.Combat
{
    public enum HitResult
    {
        Hit,
        Blocked,
        Deflected,
        Evaded
    }

    public struct HitInfo
    {
        public AttackData attack;
        public Combatant attacker;
        public Combatant defender;
        public HitResult result;
    }

    // Боец: здоровье, блок, дефлект и собственное время.
    // Хитстоп замораживает только участников удара, Time.timeScale не трогаем.
    [DefaultExecutionOrder(-50)]
    public class Combatant : MonoBehaviour
    {
        public static readonly List<Combatant> All = new List<Combatant>();

        public CombatSettings settings;
        [Min(1)] public float maxHealth = 100f;

        public float Health { get; private set; }
        public bool IsBlocking { get; private set; }
        public bool IsInvulnerable { get; set; }
        public bool InHitstop => hitstopLeft > 0f;

        // Время бойца без хитстопа. Окна и фазы считаем от него.
        public float LocalTime { get; private set; }
        public float DeltaTime => InHitstop ? 0f : Time.deltaTime;

        // Окно дефлекта для текущего нажатия блока, с учётом антиспама.
        public float CurrentDeflectWindow { get; private set; }

        public event Action<HitInfo> Hurt;
        public event Action<HitInfo> AttackLanded;
        // Любой удар в сцене — для звука, эффектов и отладки.
        public static event Action<HitInfo> AnyHit;

        float hitstopLeft;
        float blockStartTime;
        float lastBlockPressTime = float.NegativeInfinity;
        int spamPresses;

        // На случай Enter Play Mode без перезагрузки домена.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            All.Clear();
            AnyHit = null;
        }

        void Awake() => Health = maxHealth;
        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        void Update()
        {
            if (hitstopLeft > 0f)
                hitstopLeft -= Time.deltaTime;
            else
                LocalTime += Time.deltaTime;
        }

        public void Freeze(float seconds) => hitstopLeft = Mathf.Max(hitstopLeft, seconds);

        public void StartBlock()
        {
            if (IsBlocking) return;
            IsBlocking = true;
            blockStartTime = LocalTime;

            // Частые нажатия без удачного дефлекта сужают окно.
            spamPresses = LocalTime - lastBlockPressTime < settings.deflectSpamInterval ? spamPresses + 1 : 0;
            lastBlockPressTime = LocalTime;
            CurrentDeflectWindow = Mathf.Max(settings.minDeflectWindow,
                settings.deflectWindow * Mathf.Pow(settings.deflectSpamFalloff, spamPresses));
        }

        public void StopBlock() => IsBlocking = false;

        public void ResetHealth() => Health = maxHealth;

        public HitResult ReceiveHit(AttackData attack, Combatant attacker)
        {
            HitResult result;
            if (IsInvulnerable)
                result = HitResult.Evaded;
            else if (IsBlocking && attack.type != AttackType.Unblockable)
                result = LocalTime - blockStartTime <= CurrentDeflectWindow ? HitResult.Deflected : HitResult.Blocked;
            else
                result = HitResult.Hit;

            if (result == HitResult.Hit)
                Health = Mathf.Max(0f, Health - attack.damage);
            if (result == HitResult.Deflected)
                spamPresses = 0;

            if (result != HitResult.Evaded)
            {
                float stop = result == HitResult.Deflected ? settings.deflectHitstop : attack.hitstop;
                Freeze(stop);
                attacker.Freeze(stop);
            }

            var info = new HitInfo { attack = attack, attacker = attacker, defender = this, result = result };
            Hurt?.Invoke(info);
            attacker.AttackLanded?.Invoke(info);
            AnyHit?.Invoke(info);
            return result;
        }
    }
}
