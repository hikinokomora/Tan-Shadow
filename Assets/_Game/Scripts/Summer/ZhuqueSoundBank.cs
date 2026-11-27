using TanShadow.Seasons;
using UnityEngine;

namespace TanShadow.Summer
{
    // Звуки испытания Чжуцюэ. Звуковик кладёт клипы сюда; пустой слот звучит синтезированной заглушкой.
    // Порядок — по ходу боя (GDD-SU §03 «Предполагаемые звуковые акценты»).
    [CreateAssetMenu(menuName = "Tan Shadow/Summer/Zhuque Sound Bank", fileName = "ZhuqueSoundBank")]
    public sealed class ZhuqueSoundBank : ScriptableObject
    {
        [Header("Общая громкость [Tbd]")]
        [Tooltip("Множитель всех звуков испытания")]
        [Range(0, 1)] public float master = 0.8f;
        [Tooltip("Множитель синтезированных заглушек (пустых слотов): они резкие, громче настоящих клипов")]
        [Range(0, 1)] public float placeholders = 0.25f;

        [Header("Фон")]
        [Tooltip("Сухая кальдера: ветер, шорох пепла")]
        public SoundSlot ashAmbience = new SoundSlot(0.3f, 0f);
        [Tooltip("Рокот и потрескивание лавовых рек — только после третьего удара")]
        public SoundSlot lavaAmbience = new SoundSlot(0.5f, 0f);
        [Tooltip("Бульканье лавы: редкие звуки в мире, в реке рядом с героиней (пузыри SU-08)")]
        public SoundSlot lavaGurgle = new SoundSlot(0.8f, 1f);
        [Tooltip("[Tbd] Пауза между бульканьями, с (от и до)")]
        public Vector2 lavaGurgleInterval = new Vector2(2.5f, 6f);
        [Tooltip("[Tbd] Бульканье — в реке не дальше этого от героини, м")]
        [Min(1)] public float lavaGurgleRadius = 18f;
        [Tooltip("Петля «на виду»: громкость растёт со шкалой обнаружения")]
        public SoundSlot detectionLoop = new SoundSlot(0.6f, 0f);
        [Tooltip("Воздушный шум, пока героиня висит на хвосте")]
        public SoundSlot flightWind = new SoundSlot(0.6f, 0f);
        [Tooltip("Мёртвый серый мир после победы")]
        public SoundSlot deadWorldAmbience = new SoundSlot(0.3f, 0f);

        [Header("Птица")]
        [Tooltip("Короткий тревожный крик перед перелётом")]
        public SoundSlot alertCry = new SoundSlot();
        [Tooltip("Взмах крыльев: по анимации — на каждый взмах вниз, пока птица в воздухе")]
        public SoundSlot wingsTakeOff = new SoundSlot();
        [Tooltip("[Tbd] Взмах засчитывается, если кисть крыла поднялась над нижней точкой хотя бы на столько, м (планирование без взмахов — тишина)")]
        [Min(0.01f)] public float flapMinRise = 0.6f;
        [Tooltip("Села на валун: когти по камню, главный момент — в касание")]
        public SoundSlot perchLand = new SoundSlot(0.6f, 1f);
        [Tooltip("Перья отсечены (удары 1–3): теряет управление и падает. Звук летит вместе с птицей")]
        public SoundSlot loseControl = new SoundSlot();
        [Tooltip("Рухнула на землю после отсечения")]
        public SoundSlot birdFall = new SoundSlot();
        [Tooltip("Кувыркается по песку после удара о землю (вместе с birdFall)")]
        public SoundSlot birdTumble = new SoundSlot();
        [Tooltip("Огненная вспышка восстановления, хвост отрастает")]
        public SoundSlot regenerate = new SoundSlot();
        [Tooltip("Открываются лавовые реки (третье восстановление)")]
        public SoundSlot lavaOpen = new SoundSlot(1f, 0f);
        [Tooltip("Падение в магму (финал)")]
        public SoundSlot fallIntoLava = new SoundSlot();

        [Header("Героиня и хвост")]
        [Tooltip("Шаг по пеплу и песку (на камнях не звучит)")]
        public SoundSlot heroStep = new SoundSlot(1f, 0.3f);
        [Tooltip("Кувырок по пеплу и песку")]
        public SoundSlot heroRoll = new SoundSlot(1f, 0.3f);
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
