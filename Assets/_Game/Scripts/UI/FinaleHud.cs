using TanShadow.Enemies;
using UnityEngine;
using UnityEngine.UI;

namespace TanShadow.UI
{
    // Надписи финала: 子时 перед полночью, 一搏不中 («один удар — и мимо»), когда удар отражён, 刘昌裔 死 при провале.
    // Собирается в коде из DangerMark этого холста, чтобы не трогать сцену.
    public class FinaleHud : MonoBehaviour
    {
        [Tooltip("За сколько секунд до полуночи проступает 子时")]
        [Min(0)] public float midnightWarning = 5f;
        [Min(0)] public float outcomeShowTime = 5f;
        public Color midnightColor = new Color(0.85f, 0.85f, 0.95f);
        public Color successColor = new Color(0.45f, 0.95f, 0.7f);
        public Color failColor = new Color(0.95f, 0.15f, 0.1f);

        Kongkonger master;
        Text midnight;
        Text outcome;
        float outcomeSince = float.NegativeInfinity;

        void Start()
        {
            master = FindAnyObjectByType<Kongkonger>();
            var danger = GetComponent<DangerTelegraph>();
            if (master == null || danger == null) return;
            midnight = MakeLabel(danger.mark, "FinaleMidnight", 64);
            outcome = MakeLabel(danger.mark, "FinaleOutcome", 120);
            master.Resolved += ok =>
            {
                outcomeSince = Time.time;
                outcome.text = ok ? "一搏不中" : "刘昌裔 死";
                outcome.color = ok ? successColor : failColor;
            };
        }

        static Text MakeLabel(Text template, string name, int size)
        {
            var label = Instantiate(template, template.transform.parent);
            label.name = name;
            label.font = CjkFont.Get();
            label.fontSize = size;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.rectTransform.localScale = Vector3.one;
            label.gameObject.SetActive(false);
            return label;
        }

        void LateUpdate()
        {
            if (master == null || midnight == null) return;

            bool warn = master.Phase == FinalePhase.Night && master.TimeToMidnight < midnightWarning;
            midnight.gameObject.SetActive(warn);
            if (warn)
            {
                midnight.text = "子时";
                midnight.rectTransform.position = new Vector3(Screen.width * 0.5f, Screen.height * 0.82f, 0f);
                var c = midnightColor;
                c.a = (1f - master.TimeToMidnight / midnightWarning) * (0.6f + 0.4f * Mathf.Sin(Time.time * 4f));
                midnight.color = c;
            }

            float t = Time.time - outcomeSince;
            bool show = t < outcomeShowTime;
            outcome.gameObject.SetActive(show);
            if (!show) return;
            outcome.rectTransform.position = new Vector3(Screen.width * 0.5f, Screen.height * 0.62f, 0f);
            var oc = outcome.color;
            oc.a = Mathf.Clamp01(t / 0.5f) * Mathf.Clamp01((outcomeShowTime - t) / 0.8f);
            outcome.color = oc;
        }
    }
}
