using TanShadow.Combat;
using UnityEngine;
using UnityEngine.UI;

namespace TanShadow.UI
{
    // Красный 危 над врагом, пока тот замахивается неблокируемым ударом.
    public class DangerTelegraph : MonoBehaviour
    {
        public Text mark;
        [Tooltip("Высота над ногами врага, м")]
        public float height = 2.4f;

        AttackExecutor current;
        Camera view;

        void Awake()
        {
            mark.font = CjkFont.Get();
            mark.text = "危";
            mark.gameObject.SetActive(false);
            view = Camera.main;
        }

        void OnEnable() => AttackExecutor.AnyPhaseChanged += OnPhase;
        void OnDisable() => AttackExecutor.AnyPhaseChanged -= OnPhase;

        static bool IsDanger(AttackData attack) =>
            attack != null && (attack.type == AttackType.Unblockable || (attack.telegraph & AttackTelegraph.DangerKanji) != 0);

        void OnPhase(AttackExecutor executor, AttackPhase phase)
        {
            if (phase == AttackPhase.Windup && IsDanger(executor.Current))
            {
                current = executor;
                mark.gameObject.SetActive(true);
            }
            else if (executor == current && phase != AttackPhase.Active)
            {
                current = null;
                mark.gameObject.SetActive(false);
            }
        }

        void LateUpdate()
        {
            if (current == null || view == null) return;
            Vector3 screen = view.WorldToScreenPoint(current.transform.position + Vector3.up * height);
            mark.enabled = screen.z > 0f;
            mark.rectTransform.position = screen;
            float pulse = 1f + 0.12f * Mathf.Sin(Time.time * 20f);
            mark.rectTransform.localScale = Vector3.one * pulse;
        }
    }
}
