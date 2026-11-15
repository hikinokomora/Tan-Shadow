using System.Collections.Generic;
using TanShadow.Enemies;
using TanShadow.Player;
using UnityEngine;
using UnityEngine.UI;

namespace TanShadow.UI
{
    // Над стражниками со стелсом: жёлтый ？ растёт с подозрением, красный ！ на тревоге.
    // Над тем, кого можно убить из тени, — красный ромб, как у добивания.
    // Знаки собираются в коде из DangerMark и FinisherMark этого же холста, чтобы не трогать сцену.
    public class StealthIndicators : MonoBehaviour
    {
        [Tooltip("Высота знака над ногами стражника, м")]
        public float height = 2.6f;
        [Min(1)] public int size = 72;
        public Color suspiciousColor = new Color(1f, 0.82f, 0.3f);
        public Color alertColor = new Color(0.95f, 0.15f, 0.1f);
        [Tooltip("Сколько секунд висит ！ после тревоги")]
        [Min(0)] public float alertShowTime = 1.5f;
        [Tooltip("Высота ромба убийства из тени над стражником, м")]
        public float killMarkHeight = 1.6f;

        class Marker
        {
            public GuardStealth guard;
            public Text label;
            public float alertSince = float.NegativeInfinity;
        }

        readonly List<Marker> markers = new List<Marker>();
        Text template;
        RectTransform killMark;
        PlayerController player;
        Camera view;

        void Start()
        {
            view = Camera.main;
            player = FindAnyObjectByType<PlayerController>();
            var danger = GetComponent<DangerTelegraph>();
            template = danger != null ? danger.mark : null;
            var hud = GetComponent<QiHud>();
            if (hud != null && hud.finisherMark != null)
            {
                killMark = Instantiate(hud.finisherMark, hud.finisherMark.parent);
                killMark.name = "StealthKillMark";
                killMark.gameObject.SetActive(false);
            }
        }

        Marker MarkerFor(GuardStealth guard)
        {
            foreach (var m in markers)
                if (m.guard == guard) return m;
            if (template == null) return null;

            var label = Instantiate(template, template.transform.parent);
            label.name = "Stealth_" + guard.name;
            label.font = CjkFont.Get();
            label.fontSize = size;
            label.gameObject.SetActive(false);
            var marker = new Marker { guard = guard, label = label };
            guard.AwarenessChanged += a => { if (a == Awareness.Alert) marker.alertSince = Time.time; };
            markers.Add(marker);
            return marker;
        }

        void LateUpdate()
        {
            if (view == null) return;
            foreach (var guard in GuardStealth.All) MarkerFor(guard);

            foreach (var m in markers)
            {
                if (m.guard == null || m.label == null) continue;
                bool alertShown = m.guard.IsAlert && Time.time - m.alertSince < alertShowTime;
                bool suspicious = !m.guard.IsAlert && m.guard.Suspicion > 0.01f;
                bool show = !m.guard.IsDead && m.guard.isActiveAndEnabled && (alertShown || suspicious);
                Vector3 screen = view.WorldToScreenPoint(m.guard.transform.position + Vector3.up * height);
                m.label.gameObject.SetActive(show && screen.z > 0f);
                if (!show) continue;

                m.label.rectTransform.position = screen;
                if (alertShown)
                {
                    m.label.text = "！";
                    m.label.color = alertColor;
                    m.label.rectTransform.localScale = Vector3.one * (1f + 0.4f * Mathf.Clamp01(1f - (Time.time - m.alertSince) / 0.2f));
                }
                else
                {
                    m.label.text = "？";
                    var c = suspiciousColor;
                    c.a = Mathf.Lerp(0.35f, 1f, m.guard.Suspicion);
                    m.label.color = c;
                    m.label.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.7f, 1.1f, m.guard.Suspicion);
                }
            }

            UpdateKillMark();
        }

        void UpdateKillMark()
        {
            if (killMark == null) return;
            var target = player != null ? player.StealthKillTarget : null;
            Vector3 screen = target != null ? view.WorldToScreenPoint(target.transform.position + Vector3.up * killMarkHeight) : Vector3.zero;
            killMark.gameObject.SetActive(target != null && screen.z > 0f);
            if (target != null) killMark.position = screen;
        }
    }
}
