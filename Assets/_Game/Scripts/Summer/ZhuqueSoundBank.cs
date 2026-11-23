using TanShadow.Seasons;
using UnityEngine;

namespace TanShadow.Summer
{
    // Звуки испытания Чжуцюэ. Звуковик кладёт клипы сюда; пустой слот звучит синтезированной заглушкой.
    // Порядок — по ходу боя (GDD-SU §03 «Предполагаемые звуковые акценты»).
    [CreateAssetMenu(menuName = "Tan Shadow/Summer/Zhuque Sound Bank", fileName = "ZhuqueSoundBank")]
    public sealed class ZhuqueSoundBank : ScriptableObject
    {
        [Header("Фон")]
        [Tooltip("Сухая кальдера: ветер, шорох пепла")]
        public SoundSlot ashAmbience = new SoundSlot(0.3f, 0f);
        [Tooltip("Рокот и потрескивание лавовых рек — только после третьего удара")]
        public SoundSlot lavaAmbience = new SoundSlot(0.5f, 0f);
        [Tooltip("Петля «на виду»: громкость растёт со шкалой обнаружения")]
        public SoundSlot detectionLoop = new SoundSlot(0.6f, 0f);
        [Tooltip("Воздушный шум, пока героиня висит на хвосте")]
        public SoundSlot flightWind = new SoundSlot(0.6f, 0f);
        [Tooltip("Мёртвый серый мир после победы")]
        public SoundSlot deadWorldAmbience = new SoundSlot(0.3f, 0f);

        [Header("Птица")]
        [Tooltip("Короткий тревожный крик перед перелётом")]
        public SoundSlot alertCry = new SoundSlot();
        [Tooltip("Взмахи крыльев на взлёте")]
        public SoundSlot wingsTakeOff = new SoundSlot();
        [Tooltip("Села на валун")]
        public SoundSlot perchLand = new SoundSlot(0.6f, 1f);
        [Tooltip("Рухнула на землю после отсечения")]
        public SoundSlot birdFall = new SoundSlot();
        [Tooltip("Огненная вспышка восстановления, хвост отрастает")]
        public SoundSlot regenerate = new SoundSlot();
        [Tooltip("Открываются лавовые реки (третье восстановление)")]
        public SoundSlot lavaOpen = new SoundSlot(1f, 0f);
        [Tooltip("Падение в магму (финал)")]
        public SoundSlot fallIntoLava = new SoundSlot();

        [Header("Героиня и хвост")]
        [Tooltip("Схватила хвост")]
        public SoundSlot grab = new SoundSlot(0.8f, 0f);
        [Tooltip("Отсекла перья — отдельный акцент удара по хвосту")]
        public SoundSlot featherCut = new SoundSlot(1f, 0f);
        [Tooltip("4-й удар над сушей не прошёл")]
        public SoundSlot cutRefused = new SoundSlot(0.8f, 0f);
        [Tooltip("Птица стряхнула героиню")]
        public SoundSlot shakeOff = new SoundSlot(0.8f, 0f);

        [Header("Переходы")]
        public SoundSlot cutYin = new SoundSlot(1f, 0f);
        public SoundSlot cutYang = new SoundSlot(1f, 0f);
    }
}
