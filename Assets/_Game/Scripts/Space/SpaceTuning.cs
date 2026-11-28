using UnityEngine;

namespace TanShadow.Cosmos
{
    // Все числа космических сцен (общие для всех четырёх). Всё — плейсхолдеры [Tbd], подбираются в игре.
    // Белая заливка-загрузка и её время — LoadingCoverConfig слайд-шоу (Data/Narrative/Slideshows).
    [CreateAssetMenu(menuName = "Tan Shadow/Cosmos/Space Tuning", fileName = "SpaceTuning")]
    public sealed class SpaceTuning : ScriptableObject
    {
        [Header("Пространство")]
        [Tooltip("[Tbd] Радиус невидимой сферы вокруг МКС, за неё не вылететь, м")]
        [Min(10)] public float boundaryRadius = 400f;
        [Tooltip("[Tbd] На каком расстоянии от МКС появляется игрок, м")]
        [Min(1)] public float spawnDistance = 250f;
        [Tooltip("[Tbd] Высота точки спавна над плоскостью станции, м (взгляд всё равно на МКС)")]
        public float spawnHeight = 30f;
        [Tooltip("[Tbd] С какого расстояния до корпуса станции засчитывается подлёт, м")]
        [Min(0.5f)] public float arriveDistance = 12f;

        [Header("Полёт (как камера сцены в Unity: WASD, Q — вниз, E — вверх, Shift — быстрее)")]
        [Tooltip("[Tbd] Скорость, м/с")]
        [Min(0.1f)] public float speed = 25f;
        [Tooltip("[Tbd] Во сколько раз быстрее с Shift")]
        [Min(1)] public float fastMultiplier = 4f;
        [Tooltip("[Tbd] Во сколько раз медленнее с Ctrl (точный подлёт)")]
        [Min(1)] public float slowMultiplier = 4f;
        [Tooltip("[Tbd] Колесо мыши меняет скорость в этих пределах (множитель)")]
        public Vector2 wheelRange = new Vector2(0.25f, 8f);
        [Tooltip("[Tbd] Разгон и торможение: за сколько секунд скорость доходит до нужной")]
        [Min(0.01f)] public float smoothing = 0.25f;
        [Tooltip("[Tbd] Чувствительность мыши, градусов на пиксель")]
        [Min(0.001f)] public float lookSensitivity = 0.12f;

        [Header("Тело героини (вид от первого лица)")]
        [Tooltip("[Tbd] Глаз впереди центра головы, м: камера не должна видеть плечи изнутри")]
        public float eyeForward = 0.12f;
        [Tooltip("[Tbd] Глаз выше центра головы, м")]
        public float eyeUp = 0.06f;

        [Header("Звёзды: четыре сектора по сторонам света, сектор убитого босса гаснет")]
        [Tooltip("[Tbd] Через сколько после того, как спадёт белая заливка, начинает гаснуть сектор, с")]
        [Min(0)] public float extinguishDelay = 1.5f;
        [Tooltip("[Tbd] За сколько секунд сектор гаснет полностью")]
        [Min(0.01f)] public float extinguishSeconds = 4f;
        [Tooltip("[Tbd] Звёзд в секторе (билдер)")]
        [Min(1)] public int starsPerSector = 1100;
        [Tooltip("[Tbd] Ширина сектора по горизонту, градусов (90 — без зазоров между секторами)")]
        [Range(10, 90)] public float sectorSpan = 80f;
        [Tooltip("[Tbd] Звёзды по высоте: от и до, градусов над горизонтом (билдер)")]
        public Vector2 starElevation = new Vector2(-70f, 70f);
        [Tooltip("[Tbd] Яркость самых ярких звёзд, нит (билдер)")]
        [Min(0)] public float starNits = 40000f;
        [Tooltip("[Tbd] Угловой размер звезды, градусов: от и до (билдер)")]
        public Vector2 starAngularSize = new Vector2(0.06f, 0.22f);
    }
}
