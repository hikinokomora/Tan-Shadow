using UnityEngine;

namespace TanShadow.Cosmos
{
    // Тело героини при полёте от первого лица: камера — в её голове, при взгляде вниз видно тело.
    // Тело поворачивается за взглядом только по горизонтали (наклон головы его не наклоняет), голова скрыта —
    // иначе камера оказалась бы внутри лица. Всё в LateUpdate: после Animator и после полёта (SpaceFlyer).
    [DefaultExecutionOrder(100)]
    public sealed class SpaceBody : MonoBehaviour
    {
        public SpaceTuning tuning;
        [Tooltip("Камера игрока (SpaceFlyer)")]
        public Transform view;
        [Tooltip("Корень модели героини с гуманоидным Animator")]
        public Animator body;

        Transform head;

        void Awake()
        {
            if (body != null && body.isHuman) head = body.GetBoneTransform(HumanBodyBones.Head);
        }

        void LateUpdate()
        {
            if (view == null || body == null || head == null) return;
            var t = body.transform;
            float yaw = view.eulerAngles.y;
            t.rotation = Quaternion.Euler(0f, yaw, 0f);

            // Глаз — чуть впереди и выше центра головы. Ставим тело так, чтобы глаз совпал с камерой.
            head.localScale = Vector3.one;
            Vector3 eye = head.position + t.forward * tuning.eyeForward + t.up * tuning.eyeUp;
            t.position += view.position - eye;
            head.localScale = Vector3.one * 0.001f;
        }
    }
}
