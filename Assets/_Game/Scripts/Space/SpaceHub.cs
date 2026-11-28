using System.Collections;
using ShadowTan.Narrative;
using TanShadow.Seasons;
using UnityEngine;

namespace TanShadow.Cosmos
{
    // Космос с МКС в центре; четыре сцены, по одной после каждого босса (`defeated`).
    //   Спавн: белая заливка-загрузка, через миг — чёрная точка-пульс (LoadingCover слайд-шоу; если пришли из сезона,
    //   она уже висит). Игрок стоит на стороне света убитого босса (весна — восток, лето — юг, осень — запад,
    //   зима — север) и смотрит на МКС. Когда заливка спала — гаснет сектор звёзд этого босса.
    //   Подлёт к корпусу станции ближе arriveDistance: та же белая заливка, под ней грузится слайд-шоу `nextScene`.
    public sealed class SpaceHub : MonoBehaviour
    {
        public SpaceTuning tuning;
        public SpaceFlyer flyer;
        [Tooltip("Корень МКС: подлёт считается до ближайшего коллайдера внутри")]
        public Transform station;
        public StarSectors stars;
        [Tooltip("Какой босс только что убит: сторона спавна и гаснущий сектор звёзд")]
        public Season defeated;
        [Tooltip("Сцена слайд-шоу после подлёта к МКС (должна быть в Build Settings)")]
        public string nextScene;
        public LoadingCoverConfig loadingConfig;

        Collider[] hull;
        bool arrived;

        public float DistanceToStation { get; private set; } = float.MaxValue;
        Vector3 Center => station != null ? station.position : Vector3.zero;

        public Vector3 SpawnPoint =>
            Center + SeasonProgress.Direction(defeated) * tuning.spawnDistance + Vector3.up * tuning.spawnHeight;

        void Awake()
        {
            hull = station != null ? station.GetComponentsInChildren<Collider>() : new Collider[0];
            flyer.enabled = false;
            Vector3 p = SpawnPoint;
            flyer.Place(p, Quaternion.LookRotation(Center - p, Vector3.up));
            if (stars != null) stars.Prepare(defeated);
        }

        IEnumerator Start()
        {
            // Пришли из сезона — белая заливка уже висит. Сцену открыли напрямую — показать её сейчас.
            if (!LoadingCover.IsCovering) LoadingCover.CoverCurrentScene(LoadingCover.Kind.Yang, loadingConfig);
            while (LoadingCover.IsCovering) yield return null;
            flyer.enabled = true;
            if (stars != null) stars.Extinguish();
        }

        void Update()
        {
            DistanceToStation = Distance(flyer.transform.position);
            if (!arrived && flyer.enabled && DistanceToStation <= tuning.arriveDistance) Arrive();
        }

        float Distance(Vector3 p)
        {
            float best = float.MaxValue;
            foreach (var c in hull)
                if (c != null && c.enabled)
                    best = Mathf.Min(best, Vector3.Distance(p, c.ClosestPoint(p)));
            return best;
        }

        void Arrive()
        {
            arrived = true;
            flyer.enabled = false;
            if (!string.IsNullOrEmpty(nextScene) && Application.CanStreamedLevelBeLoaded(nextScene))
                LoadingCover.LoadScene(nextScene, LoadingCover.Kind.Yang, loadingConfig);
            else
                Debug.LogWarning("[Space] Сцена «" + nextScene + "» не задана или не в Build Settings", this);
        }
    }
}
