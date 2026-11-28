using UnityEngine;

namespace ShadowTan.Narrative
{
    // Настройки заливки загрузки вокруг слайд-шоу. Вид и числа повторяют YinYangCover из сцен колоссов.
    [CreateAssetMenu(menuName = "ShadowTan/Narrative/Loading Cover Config", fileName = "LoadingCoverConfig")]
    public sealed class LoadingCoverConfig : ScriptableObject
    {
        [Tooltip("Заливка держится не меньше этого времени, даже если сцена загрузилась раньше, с")]
        [Min(0f)] public float minSeconds = 3f;
        [Tooltip("Исчезновение заливки, с")]
        [Min(0f)] public float fadeOutSeconds = 0.3f;

        [Header("Точка-сердцебиение в правом нижнем углу")]
        [Min(0f)] public float dotDelay = 0.45f;
        [Min(0f)] public float dotFadeIn = 0.25f;
        [Range(30, 180)] public float heartRate = 66f;
        [Range(0.1f, 0.6f)] public float secondBeatOffset = 0.3f;
        [Range(0, 1)] public float dotRestAlpha = 0.3f;
        [Range(0, 1)] public float dotBeatGrow = 0.35f;
        [Tooltip("Размер точки в долях высоты экрана")]
        public float dotSize = 0.032f;
        [Tooltip("Отступ точки от правого нижнего угла в долях высоты экрана")]
        public float dotMargin = 0.05f;
    }
}
