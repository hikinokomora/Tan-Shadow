using ShadowTan.Narrative;
using UnityEngine;

namespace TanShadow.Seasons
{
    // После победы над колоссом этой сцены — переход в космос (`spaceScene`) под белой заливкой-загрузкой.
    // Победа и так показывается белым экраном с чёрной точкой (YinYangCover), поэтому для игрока это одна белая
    // загрузка: смерть колосса → космос.
    public sealed class VictoryToSpace : MonoBehaviour
    {
        public Season season;
        [Tooltip("Космическая сцена после победы (должна быть в Build Settings)")]
        public string spaceScene;
        public LoadingCoverConfig loadingConfig;

        void OnEnable() => SeasonProgress.Defeated += OnDefeated;
        void OnDisable() => SeasonProgress.Defeated -= OnDefeated;

        void OnDefeated(Season s)
        {
            if (s != season) return;
            if (!string.IsNullOrEmpty(spaceScene) && Application.CanStreamedLevelBeLoaded(spaceScene))
                LoadingCover.LoadScene(spaceScene, LoadingCover.Kind.Yang, loadingConfig);
            else
                Debug.LogWarning("[Victory] Сцена «" + spaceScene + "» не задана или не в Build Settings", this);
        }
    }
}
