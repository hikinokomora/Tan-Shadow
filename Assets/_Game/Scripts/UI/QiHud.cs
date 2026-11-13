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

        [Header("Слом ци игрока (край шкалы)")]
        [Tooltip("Высота иероглифа 阴/阳 над ногами игрока, м")]
        public float breakMarkHeight = 2.3f;
        [Min(1)] public int breakMarkSize = 96;

        [Header("Здоровье и смерть игрока")]
        public Color playerHealthColor = new Color(0.75f, 0.12f, 0.1f);
        [Tooltip("Насколько полоса здоровья ниже шкалы ци, пикселей макета")]
        public float playerHealthOffset = 14f;
        [Min(1)] public int deathMarkSize = 200;

        RectTransform playerHealthFill;
        Text deathLabel;
        float deadSince = -1f;

        LockOnTargeting lockOn;
        Combatant player;
        PlayerController controller;
        Huajin huajin;
        Camera view;
        Text breakLabel;
        int breakSide;
        float breakDuration = 1f;
        float breakUntil = float.NegativeInfinity;

        void Start()
        {
            if (playerQi == null) playerQi = FindAnyObjectByType<YinYangQi>();
            if (playerQi != null)
            {
                player = playerQi.GetComponent<Combatant>();
                controller = playerQi.GetComponent<PlayerController>();
                lockOn = playerQi.GetComponent<LockOnTargeting>();
                huajin = playerQi.GetComponent<Huajin>();
                playerQi.Overflow += OnQiBreak;
            }
            view = Camera.main;
            if (huajinLabel != null)
            {
                huajinLabel.font = CjkFont.Get();
                huajinLabel.text = "化劲";
                huajinLabel.gameObject.SetActive(false);

                // Иероглиф слома делаем копией подписи хуацзиня, чтобы не трогать сцену.
                breakLabel = Instantiate(huajinLabel, huajinLabel.transform.parent);
                breakLabel.name = "QiBreakLabel";
                breakLabel.fontSize = breakMarkSize;
                // Тёмная обводка, чтобы иероглиф читался и на освещённых стенах, и в тени.
                if (breakLabel.GetComponent<Shadow>() == null)
                {
                    var outline = breakLabel.gameObject.AddComponent<Outline>();
                    outline.effectColor = new Color(0f, 0f, 0f, 0.7f);
                    outline.effectDistance = new Vector2(2f, -2f);
                }

                // 死 по центру экрана, пока героиня лежит до рестарта.
                deathLabel = Instantiate(breakLabel, huajinLabel.transform.parent);
                deathLabel.name = "DeathLabel";
                deathLabel.text = "死";
                deathLabel.fontSize = deathMarkSize;
            }
            BuildPlayerHealthBar();
        }

        // Полоса здоровья игрока — тонкая копия шкалы ци под ней (собираем в коде, чтобы не трогать сцену).
        void BuildPlayerHealthBar()
        {
            if (playerFill == null || player == null) return;
            var qiBar = (RectTransform)playerFill.parent;
            var bar = Instantiate(qiBar, qiBar.parent);
            bar.name = "PlayerHealth";
            bar.SetSiblingIndex(qiBar.GetSiblingIndex() + 1);
            bar.sizeDelta = new Vector2(qiBar.sizeDelta.x, qiBar.sizeDelta.y * 0.5f);
            bar.anchoredPosition = qiBar.anchoredPosition - new Vector2(0f, playerHealthOffset);

            playerHealthFill = (RectTransform)bar.Find(playerFill.name);
            for (int i = bar.childCount - 1; i >= 0; i--)
                if (bar.GetChild(i) != playerHealthFill) Destroy(bar.GetChild(i).gameObject);
            playerHealthFill.anchorMin = Vector2.zero;
            playerHealthFill.GetComponent<Image>().color = playerHealthColor;
        }

        void OnDestroy()
        {
            if (playerQi != null) playerQi.Overflow -= OnQiBreak;
        }

        void OnQiBreak(int side)
        {
            breakSide = side;
            breakDuration = Mathf.Max(0.1f, player.settings.qiBreakStun);
            breakUntil = Time.time + breakDuration;
            if (breakLabel != null) breakLabel.text = side > 0 ? "阳" : "阴";
        }

        void LateUpdate()
        {
            UpdatePlayer();
            UpdateHealthAndDeath();
            UpdateEnemy();
        }

        void UpdateHealthAndDeath()
        {
            if (player == null) return;
            if (playerHealthFill != null)
                playerHealthFill.anchorMax = new Vector2(Mathf.Clamp01(player.Health / player.maxHealth), 1f);

            if (deathLabel == null) return;
            if (!player.IsDead) deadSince = -1f;
            else if (deadSince < 0f) deadSince = Time.time;
            deathLabel.gameObject.SetActive(player.IsDead);
            if (!player.IsDead) return;

            float t = Time.time - deadSince;
            deathLabel.rectTransform.position = new Vector3(Screen.width * 0.5f, Screen.height * 0.58f, 0f);
            var c = edgeColor;
            c.a = Mathf.Clamp01(t / 0.6f); // проступает, а не вспыхивает
            deathLabel.color = c;
            deathLabel.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.25f, 1f, Mathf.Clamp01(t / 0.6f));
        }

        void UpdatePlayer()
        {
            if (playerQi == null) return;
            float v = playerQi.Normalized * 0.5f;
            Color color = playerQi.Value >= 0f ? yangColor : yinColor;
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 18f);

            // Слом: сама шкала уже сброшена в ноль, поэтому показываем, как лопнувшая сторона стекает к центру.
            // Если оглушение оборвалось раньше (игрока ударили и он очнулся), гасим сразу.
            float breakLeft = (breakUntil - Time.time) / breakDuration;
            bool broken = breakLeft > 0f && (controller == null || controller.State == PlayerState.Stagger);
            if (broken)
            {
                v = breakSide * 0.5f * breakLeft;
                color = Color.Lerp(breakSide > 0 ? yangColor : yinColor, edgeColor, pulse);
            }
            else
            {
                float edge = Mathf.Abs(playerQi.Normalized);
                if (edge > dangerFrom) color = Color.Lerp(color, edgeColor, pulse * Mathf.InverseLerp(dangerFrom, 1f, edge));
            }

            playerFill.anchorMin = new Vector2(Mathf.Min(0.5f, 0.5f + v), 0f);
            playerFill.anchorMax = new Vector2(Mathf.Max(0.5f, 0.5f + v), 1f);
            playerFillImage.color = color;
            UpdateBreakLabel(broken, breakLeft);

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

        // Крупный 阴/阳 над головой героини, пока она оглушена сломом ци.
        void UpdateBreakLabel(bool broken, float left)
        {
            if (breakLabel == null) return;
            breakLabel.gameObject.SetActive(broken && view != null);
            if (!broken || view == null) return;

            Vector3 screen = view.WorldToScreenPoint(playerQi.transform.position + Vector3.up * breakMarkHeight);
            breakLabel.enabled = screen.z > 0f;
            breakLabel.rectTransform.position = screen;

            // Ян уводим в красный, инь осветляем: синий на ночной сцене иначе теряется.
            Color c = breakSide > 0 ? Color.Lerp(yangColor, edgeColor, 0.35f) : Color.Lerp(yinColor, Color.white, 0.4f);
            c.a = Mathf.Clamp01(left / 0.3f); // гаснет к концу оглушения
            breakLabel.color = c;
            // Появляется крупнее и за первые мгновения сжимается до своего размера.
            breakLabel.rectTransform.localScale = Vector3.one * (1f + 0.7f * Mathf.Clamp01((left - 0.85f) / 0.15f));
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
