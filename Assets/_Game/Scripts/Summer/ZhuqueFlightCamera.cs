using UnityEngine;

namespace TanShadow.Summer
{
    // Камера полёта на хвосте. Обычная камера смотрит на героиню вблизи, и тень птицы на дне долины
    // остаётся за кадром, а по ней решается четвёртый удар. Здесь камера отходит назад и вверх
    // и смотрит между героиней и тенью. Вне полёта ничего не делает — работает обычная камера.
    // Порядок: после камеры героини, до дрожи ZhuqueFeedback.
    [DefaultExecutionOrder(400)]
    public sealed class ZhuqueFlightCamera : MonoBehaviour
    {
        public ZhuqueEncounter encounter;
        public Camera view;
        [Tooltip("Насколько позади героини (против хода полёта), м")]
        public float back = 12f;
        [Tooltip("Насколько выше героини, м")]
        public float up = 3f;
        [Tooltip("Где между птицей (0) и её тенью (1) центр кадра: в кадре и птица с отсветом лавы, и тень")]
        [Range(0, 1)] public float lookAtShadow = 0.5f;
        [Tooltip("Время перехода в камеру полёта и обратно, с")]
        public float blendTime = 0.6f;

        float weight;
        Vector3 smoothedLook;

        void LateUpdate()
        {
            if (encounter == null || view == null) return;
            var s = encounter.State;
            bool flying = s == ZhuqueState.TakeOff || s == ZhuqueState.Flight || s == ZhuqueState.ShakeOff;
            weight = Mathf.MoveTowards(weight, flying ? 1f : 0f, Time.deltaTime / Mathf.Max(0.01f, blendTime));
            if (weight <= 0f) return;

            Transform hero = encounter.hero.Body;
            Vector3 forward = encounter.bird.transform.forward;
            forward.y = 0f;
            forward = forward.sqrMagnitude > 0.001f ? forward.normalized : hero.forward;

            Vector3 shadow = s == ZhuqueState.TakeOff ? hero.position + forward * 8f : encounter.ShadowPoint;
            Vector3 look = Vector3.Lerp(encounter.bird.BodyCenter, shadow, lookAtShadow);
            smoothedLook = weight < 0.05f ? look : Vector3.Lerp(smoothedLook, look, 1f - Mathf.Exp(-6f * Time.deltaTime));

            Vector3 position = hero.position - forward * back + Vector3.up * up;
            Quaternion rotation = Quaternion.LookRotation(smoothedLook - position);
            float k = Mathf.SmoothStep(0f, 1f, weight);
            view.transform.SetPositionAndRotation(
                Vector3.Lerp(view.transform.position, position, k),
                Quaternion.Slerp(view.transform.rotation, rotation, k));
        }
    }
}
