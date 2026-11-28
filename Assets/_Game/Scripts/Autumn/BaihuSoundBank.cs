using System;
using UnityEngine;

namespace TanShadow.Autumn
{
    // Все готовые звуки испытания Байху. Пустые слоты молчат.
    [CreateAssetMenu(menuName = "Tan Shadow/Autumn/Baihu Sound Bank", fileName = "BaihuSoundBank")]
    public sealed class BaihuSoundBank : ScriptableObject
    {
        [Serializable]
        public sealed class Slot
        {
            [Tooltip("Клипы; играется случайный. Пусто — тишина")]
            public AudioClip[] clips;
            [Range(0, 1)] public float volume = 1f;
            [Tooltip("Случайный разброс высоты тона, ±")]
            [Range(0, 0.5f)] public float pitchJitter = 0.05f;
            [Tooltip("0 — звук без положения (2D), 1 — в мире (3D)")]
            [Range(0, 1)] public float spatial = 1f;

            public AudioClip Pick()
            {
                if (clips == null || clips.Length == 0) return null;
                return clips[UnityEngine.Random.Range(0, clips.Length)];
            }
        }

        [Header("Окружение")]
        [Tooltip("Петля: ветер в бамбуке, шорох травы. Играет, пока лес живой")]
        public Slot forestAmbience = new Slot { volume = 0.35f, spatial = 0f };
        public Slot forestOneShot = new Slot { volume = .32f };

        [Header("Тигр в траве")]
        [Tooltip("Шевеление бамбука и травы перед появлением глаз (по нему читается направление)")]
        public Slot rustle = new Slot { volume = 0.8f };
        public Slot fleetingRustle = new Slot { volume = .48f };
        public Slot tigerRoar = new Slot { volume = .55f };
        [Tooltip("Появились глаза")]
        public Slot eyesAppear = new Slot { volume = 0.6f };
        [Tooltip("Петля нарастающего шума: громкость идёт за агрессией тигра")]
        public Slot aggroNoiseLoop = new Slot { volume = 1f, spatial = 0f, pitchJitter = 0f };
        [Tooltip("Отпугивающий взмах засчитан")]
        public Slot swingCounted = new Slot { volume = 0.5f, spatial = 0f };
        [Tooltip("Тигр отпугнут и уходит обратно в бамбук: удаляющийся шорох")]
        [UnityEngine.Serialization.FormerlySerializedAs("scaredRunAcross")]
        public Slot scaredWithdraw = new Slot { volume = 0.9f };

        [Header("Прыжок")]
        [Tooltip("Гонг перед прыжком")]
        public Slot gong = new Slot { volume = 1f, spatial = 0f, pitchJitter = 0f };
        [Tooltip("Прыжок тигра")]
        public Slot pounce = new Slot { volume = 1f };
        [Tooltip("Промах: тигр приземлился и уходит в лес")]
        public Slot pounceMiss = new Slot { volume = 0.7f };
        [Tooltip("Тигр отлетел от столба и рухнул на бок")]
        public Slot tigerFall = new Slot { volume = 0.9f };
        [Tooltip("Тигр поднимается, оглушённый (рык, тяжёлое дыхание)")]
        public Slot tigerGetUp = new Slot { volume = 0.8f };
        [Tooltip("Тигр прыжком уходит обратно в лес")]
        public Slot tigerLeapAway = new Slot { volume = 0.8f };

        [Header("Столб")]
        [Tooltip("Удар тигра о столб")]
        public Slot monumentImpact = new Slot { volume = 1f };
        [Tooltip("Треск основания после удара (громче с каждым ударом)")]
        public Slot monumentCrack = new Slot { volume = 0.8f };
        public Slot monumentDebris = new Slot { volume = .35f };
        [Tooltip("Столб начинает падать")]
        public Slot monumentFall = new Slot { volume = 1f };
        [Tooltip("Столб рухнул")]
        public Slot monumentLand = new Slot { volume = 1f };

        [Header("Переходы")]
        [Tooltip("Чёрная инь: обрыв кадра при смерти героини")]
        public Slot cutYin = new Slot { volume = 0.9f, spatial = 0f, pitchJitter = 0f };
        [Tooltip("Белая янь: смерть колосса")]
        public Slot cutYang = new Slot { volume = 0.9f, spatial = 0f, pitchJitter = 0f };
        [Tooltip("Петля мёртвого мира после победы (может быть пустой тишиной)")]
        public Slot deadWorldAmbience = new Slot { volume = 0.2f, spatial = 0f };
    }
}
