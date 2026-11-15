using System;
using System.Collections.Generic;
using TanShadow.Characters;
using TanShadow.Combat;
using TanShadow.Environment;
using UnityEngine;

namespace TanShadow.Enemies
{
    public enum FinalePhase
    {
        Waiting, // героиня ещё не рядом с Лю
        Night,   // мастер невидимо кружит: дрожат свечи, на занавесах тень
        Strike,  // полночь: гаснет свеча с той стороны, откуда придёт удар
        Done,    // удар отражён, звенит нефрит — мастер уходит от стыда
        Failed   // удар дошёл до Лю — рестарт
    }

    // Кункунэр (空空儿) — невидимый мастер финала. Не боец в бою: его не видно, не захватить и не ударить.
    // Один удар в полночь — отразить его можно только идеальным дефлектом рядом с Лю.
    // Combatant на этом объекте выключен: он нужен только как «кто бьёт» для ReceiveHit.
    [RequireComponent(typeof(Combatant))]
    public class Kongkonger : MonoBehaviour
    {
        public Protectee protectee;
        [Tooltip("Полночный удар: замах — это время, пока гаснет свеча и звучит предупреждение")]
        public AttackData midnightStrike;
        [Tooltip("Полотна занавесов, лицевая сторона (+Z) смотрит внутрь комнаты: на них ложится тень")]
        public Transform[] curtains;
        [Tooltip("Тёмный силуэт, который показываем на занавесе")]
        public Transform silhouette;

        [Header("Ночь")]
        [Tooltip("Ночь начинается, когда героиня подходит к Лю ближе, м")]
        [Min(0)] public float startDistance = 6f;
        [Tooltip("Сколько длится ночь до полночного удара, с")]
        [Min(0)] public float midnightDelay = 25f;
        [Tooltip("Радиус, по которому мастер кружит вокруг Лю, м")]
        [Min(0)] public float circleRadius = 5f;
        [Tooltip("Скорость кружения, градусы в секунду")]
        [Min(0)] public float circleSpeed = 30f;
        [Tooltip("Свечи ближе этого к мастеру дрожат, м")]
        [Min(0)] public float disturbRadius = 1.8f;
        [Tooltip("Тень видна, когда мастер ближе к занавесу, м")]
        [Min(0)] public float shadowDistance = 1.3f;

        [Header("Удар")]
        [Tooltip("Удар перехватывает героиня, если стоит к Лю ближе, м")]
        [Min(0)] public float protectRadius = 2.2f;
        [Tooltip("С какого расстояния от Лю приходит удар, м")]
        [Min(0)] public float strikeDistance = 1.4f;
        [Tooltip("Через сколько секунд после провала ночь начинается заново")]
        [Min(0)] public float restartDelay = 3f;

        public FinalePhase Phase { get; private set; }
        public float TimeToMidnight => Mathf.Max(0f, midnightDelay - nightTime);
        // true — отразила, false — Лю погиб.
        public event Action<bool> Resolved;

        Combatant self;
        Combatant player;
        readonly List<CandleFlicker> candles = new List<CandleFlicker>();
        float nightTime;
        float angle;
        float strikeLeft;
        float phaseTime;
        Vector3 strikeFrom;

        void Awake()
        {
            self = GetComponent<Combatant>();
            self.enabled = false; // не попадает в Combatant.All: ни захвата, ни шкал врага
            if (silhouette != null) silhouette.gameObject.SetActive(false);
        }

        void Start()
        {
            var pc = FindAnyObjectByType<Player.PlayerController>();
            if (pc != null) player = pc.GetComponent<Combatant>();
            foreach (var candle in FindObjectsByType<CandleFlicker>(FindObjectsSortMode.None))
                if (Vector3.Distance(candle.transform.position, protectee.transform.position) < circleRadius + 3f) candles.Add(candle);
        }

        void SetPhase(FinalePhase next)
        {
            Phase = next;
            phaseTime = 0f;
        }

        void Update()
        {
            if (player == null || protectee == null) return;
            phaseTime += Time.deltaTime;
            Vector3 center = protectee.transform.position;

            switch (Phase)
            {
                case FinalePhase.Waiting:
                    if (!player.IsDead && Flat(player.transform.position - center).magnitude < startDistance)
                    {
                        nightTime = 0f;
                        SetPhase(FinalePhase.Night);
                    }
                    break;

                case FinalePhase.Night:
                    nightTime += Time.deltaTime;
                    // Кружит неровно: то быстрее, то медленнее, иногда разворачивается.
                    float drift = Mathf.PerlinNoise(nightTime * 0.3f, 7f) * 2f - 0.6f;
                    angle += circleSpeed * drift * Time.deltaTime;
                    float radius = circleRadius * (0.85f + 0.3f * Mathf.PerlinNoise(nightTime * 0.5f, 3f));
                    transform.position = center + Quaternion.Euler(0f, angle, 0f) * Vector3.forward * radius;
                    DisturbCandles();
                    UpdateSilhouette();
                    if (nightTime >= midnightDelay) BeginStrike(center);
                    break;

                case FinalePhase.Strike:
                    strikeLeft -= Time.deltaTime;
                    if (strikeLeft <= 0f) ResolveStrike(center);
                    break;

                case FinalePhase.Done:
                    // Уходит: тень мелькает на занавесе и пропадает.
                    if (silhouette != null && phaseTime > 0.6f) silhouette.gameObject.SetActive(false);
                    break;

                case FinalePhase.Failed:
                    if (phaseTime >= restartDelay)
                    {
                        protectee.Revive();
                        SetPhase(FinalePhase.Waiting);
                    }
                    break;
            }
        }

        // Полночь: удар придёт со стороны свечи, которая сейчас погаснет.
        void BeginStrike(Vector3 center)
        {
            CandleFlicker tell = null;
            if (candles.Count > 0) tell = candles[UnityEngine.Random.Range(0, candles.Count)];
            Vector3 dir = tell != null ? Flat(tell.transform.position - center) : Vector3.forward;
            dir = dir.sqrMagnitude > 0.001f ? dir.normalized : Vector3.forward;

            strikeFrom = center + dir * strikeDistance;
            transform.position = strikeFrom;
            transform.rotation = Quaternion.LookRotation(-dir);
            strikeLeft = midnightStrike.windup;
            if (tell != null) tell.Gutter(midnightStrike.windup + 0.8f);
            if (silhouette != null) silhouette.gameObject.SetActive(false);
            PlayDangerSound(tell != null ? tell.transform.position : strikeFrom);
            SetPhase(FinalePhase.Strike);
        }

        void ResolveStrike(Vector3 center)
        {
            bool deflected = false;
            // Героиня рядом с Лю — удар приходится на неё; спасёт только идеальный дефлект.
            if (!player.IsDead && Flat(player.transform.position - center).magnitude <= protectRadius)
                deflected = player.ReceiveHit(midnightStrike, self) == HitResult.Deflected;

            if (deflected)
            {
                // Отражённый клинок звенит о нефрит на шее Лю. Одного промаха мастеру достаточно: он уходит.
                var effects = FindAnyObjectByType<HitEffects>();
                Vector3 jade = protectee.jade != null ? protectee.jade.position : center + Vector3.up * 1.5f;
                if (effects != null) effects.JadeRing(jade, strikeFrom);
                ShowSilhouetteOnNearestCurtain();
                SetPhase(FinalePhase.Done);
            }
            else
            {
                protectee.Kill();
                SetPhase(FinalePhase.Failed);
            }
            Resolved?.Invoke(deflected);
        }

        void DisturbCandles()
        {
            foreach (var candle in candles)
            {
                float d = Vector3.Distance(Flat(candle.transform.position), Flat(transform.position));
                if (d < disturbRadius) candle.Disturb(Mathf.Lerp(0.9f, 0.4f, d / disturbRadius), 0.4f);
            }
        }

        // Тень мастера на ближайшем занавесе, если он прямо за ним.
        void UpdateSilhouette()
        {
            if (silhouette == null) return;
            Transform best = null;
            Vector3 onCurtain = Vector3.zero;
            float bestDistance = shadowDistance;
            foreach (var curtain in curtains)
            {
                if (curtain == null) continue;
                Vector3 local = curtain.InverseTransformPoint(transform.position);
                // За занавесом (сзади по -Z) и в пределах его ширины.
                float behind = -local.z * curtain.lossyScale.z;
                if (behind < 0f || behind > bestDistance || Mathf.Abs(local.x) > 0.5f) continue;
                best = curtain;
                bestDistance = behind;
                onCurtain = curtain.TransformPoint(new Vector3(local.x, 0f, 0.5f));
            }
            silhouette.gameObject.SetActive(best != null);
            if (best == null) return;
            silhouette.position = new Vector3(onCurtain.x, best.position.y - best.lossyScale.y * 0.5f + 0.95f, onCurtain.z) + best.forward * 0.03f;
            silhouette.rotation = best.rotation;
        }

        void ShowSilhouetteOnNearestCurtain()
        {
            if (silhouette == null) return;
            Transform nearest = null;
            float best = float.MaxValue;
            foreach (var curtain in curtains)
            {
                if (curtain == null) continue;
                float d = Vector3.Distance(curtain.position, strikeFrom);
                if (d < best) { best = d; nearest = curtain; }
            }
            if (nearest == null) return;
            silhouette.gameObject.SetActive(true);
            silhouette.SetPositionAndRotation(nearest.position + nearest.forward * 0.03f + Vector3.down * (nearest.lossyScale.y * 0.5f - 0.95f), nearest.rotation);
        }

        void PlayDangerSound(Vector3 at)
        {
            var audio = FindAnyObjectByType<Audio.CombatAudio>();
            if (audio == null || audio.danger == null || audio.danger.Length == 0) return;
            AudioSource.PlayClipAtPoint(audio.danger[0], at, audio.telegraphVolume);
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
    }
}
