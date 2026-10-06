using TanShadow.Combat;
using TanShadow.Player;
using UnityEngine;
using UnityEngine.UI;

namespace TanShadow.UI
{
    // Шкала инь/ян игрока внизу экрана, ци и здоровье врага сверху, ромб добивания над сломленным врагом.
    public class QiHud : MonoBehaviour
    {
        [Header("Игрок")]
        public YinYangQi playerQi;
        [Tooltip("Заливка шкалы игрока; растёт от центра влево (инь) или вправо (ян)")]
        public RectTransform playerFill;
        public Image playerFillImage;
        public Color yinColor = new Color(0.35f, 0.5f, 0.85f);
        public Color yangColor = new Color(1f, 0.82f, 0.5f);
        public Color edgeColor = new Color(0.95f, 0.2f, 0.15f);
        [Tooltip("С какой доли шкалы она начинает мигать, 0–1")]
        [Range(0, 1)] public float dangerFrom = 0.75f;

        [Header("Хуацзинь")]
        [Tooltip("Подпись 化劲 над шкалой, пока заряд активен")]
        public Text huajinLabel;
        public Color huajinColor = new Color(0.45f, 0.95f, 0.7f);

        [Header("Враг")]
        public GameObject enemyPanel;
        [Tooltip("Заливка ци врага; растёт от центра в обе стороны")]
        public RectTransform enemyPostureFill;
        public Image enemyPostureImage;
        public RectTransform enemyHealthFill;
        public Color postureColor = new Color(1f, 0.6f, 0.15f);
        public Color brokenColor = new Color(1f, 0.15f, 0.1f);
        [Tooltip("Показывать врага без захвата, если он ближе, м")]
        [Min(0)] public float showRange = 10f;

        [Header("Добивание")]
        public RectTransform finisherMark;
        public float finisherMarkHeight = 1.6f;

        LockOnTargeting lockOn;
        Combatant player;
        Huajin huajin;
        Camera view;

        void Start()
        {
            if (playerQi == null) playerQi = FindAnyObjectByType<YinYangQi>();
            if (playerQi != null)
            {
                player = playerQi.GetComponent<Combatant>();
                lockOn = playerQi.GetComponent<LockOnTargeting>();
                huajin = playerQi.GetComponent<Huajin>();
            }
            view = Camera.main;
            if (huajinLabel != null)
            {
                huajinLabel.font = CjkFont.Get();
                huajinLabel.text = "化劲";
                huajinLabel.gameObject.SetActive(false);
            }
        }

        void LateUpdate()
        {
            UpdatePlayer();
            UpdateEnemy();
        }

        void UpdatePlayer()
        {
            if (playerQi == null) return;
            float v = playerQi.Normalized * 0.5f;
            playerFill.anchorMin = new Vector2(Mathf.Min(0.5f, 0.5f + v), 0f);
            playerFill.anchorMax = new Vector2(Mathf.Max(0.5f, 0.5f + v), 1f);

            Color color = playerQi.Value >= 0f ? yangColor : yinColor;
            float edge = Mathf.Abs(playerQi.Normalized);
            if (edge > dangerFrom)
            {
                float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 18f);
                color = Color.Lerp(color, edgeColor, pulse * Mathf.InverseLerp(dangerFrom, 1f, edge));
            }
            playerFillImage.color = color;

            if (huajinLabel == null) return;
            bool charged = huajin != null && huajin.IsCharged;
            huajinLabel.gameObject.SetActive(charged);
            if (charged)
            {
                var c = huajinColor;
                c.a = Mathf.Clamp01(huajin.TimeLeft / 0.4f) * (0.75f + 0.25f * Mathf.Sin(Time.time * 14f));
                huajinLabel.color = c;
            }
        }

        void UpdateEnemy()
        {
            var enemy = PickEnemy();
            var posture = enemy != null ? enemy.GetComponent<Posture>() : null;
            bool show = enemy != null && posture != null && !enemy.IsDead;
            enemyPanel.SetActive(show);
            finisherMark.gameObject.SetActive(show && posture.IsBroken);
            if (!show) return;

            float p = posture.Normalized * 0.5f;
            enemyPostureFill.anchorMin = new Vector2(0.5f - p, 0f);
            enemyPostureFill.anchorMax = new Vector2(0.5f + p, 1f);
            enemyPostureImage.color = posture.IsBroken ? brokenColor : Color.Lerp(postureColor, brokenColor, posture.Normalized * posture.Normalized);
            enemyHealthFill.anchorMax = new Vector2(enemy.Health / enemy.maxHealth, 1f);

            if (posture.IsBroken && view != null)
            {
                Vector3 screen = view.WorldToScreenPoint(enemy.transform.position + Vector3.up * finisherMarkHeight);
                finisherMark.gameObject.SetActive(screen.z > 0f);
                finisherMark.position = screen;
            }
        }

        Combatant PickEnemy()
        {
            if (lockOn != null && lockOn.IsLocked) return lockOn.Target;
            if (player == null) return null;

            Combatant best = null;
            float bestDistance = showRange;
            foreach (var other in Combatant.All)
            {
                if (other == player || other.IsDead) continue;
                float distance = Vector3.Distance(other.transform.position, player.transform.position);
                if (distance < bestDistance)
                {
                    best = other;
                    bestDistance = distance;
                }
            }
            return best;
        }
    }
}
