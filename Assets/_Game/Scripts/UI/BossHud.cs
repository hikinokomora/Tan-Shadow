using TanShadow.Enemies;
using UnityEngine;
using UnityEngine.UI;

namespace TanShadow.UI
{
    // Имя босса над шкалами врага и знаки смерти (ромбы): сколько добиваний ещё нужно.
    // Собирается в коде из подписей этого холста, чтобы не трогать сцену. Работает рядом с QiHud.
    public class BossHud : MonoBehaviour
    {
        [Tooltip("Показывать босса, если он ближе, м")]
        [Min(0)] public float showRange = 20f;
        [Min(1)] public int nameSize = 36;
        public Color nameColor = new Color(0.92f, 0.88f, 0.8f);
        public Color pipColor = new Color(0.9f, 0.05f, 0.05f);
        public Color spentPipColor = new Color(0.25f, 0.25f, 0.25f, 0.8f);
        [Tooltip("Отступ имени над панелью врага, пиксели макета")]
        public float nameOffset = 26f;

        Text nameLabel;
        RectTransform[] pips;
        RectTransform enemyPanel;
        Transform player;
        BossPhases boss;

        void Start()
        {
            var qiHud = GetComponent<QiHud>();
            var danger = GetComponent<DangerTelegraph>();
            var playerController = FindAnyObjectByType<Player.PlayerController>();
            if (qiHud == null || danger == null || playerController == null) return;
            player = playerController.transform;
            enemyPanel = (RectTransform)qiHud.enemyPanel.transform;

            nameLabel = Instantiate(danger.mark, enemyPanel.parent);
            nameLabel.name = "BossName";
            nameLabel.font = CjkFont.Get();
            nameLabel.fontSize = nameSize;
            nameLabel.color = nameColor;
            nameLabel.rectTransform.localScale = Vector3.one;

            pips = new RectTransform[BossPhases.Deathblows];
            for (int i = 0; i < pips.Length; i++)
            {
                pips[i] = Instantiate(qiHud.finisherMark, enemyPanel.parent);
                pips[i].name = "BossDeathblow_" + (i + 1);
                pips[i].localScale = Vector3.one * 0.7f;
            }
            Show(false);
        }

        void LateUpdate()
        {
            if (nameLabel == null) return;
            boss = FindBoss();
            bool show = boss != null && (!boss.IsDead || boss.DeathblowsLeft > 0);
            Show(show);
            if (!show) return;

            nameLabel.text = boss.displayName;
            // Над панелью врага: имя по центру, ромбы слева от имени.
            Vector3 top = enemyPanel.position + Vector3.up * nameOffset * enemyPanel.lossyScale.y;
            nameLabel.rectTransform.position = top;
            float step = 26f * enemyPanel.lossyScale.x;
            float left = top.x - (nameLabel.preferredWidth * 0.5f + 20f) * enemyPanel.lossyScale.x;
            for (int i = 0; i < pips.Length; i++)
            {
                pips[i].position = new Vector3(left - i * step, top.y, 0f);
                var image = pips[i].GetComponent<Image>();
                if (image != null) image.color = i < boss.DeathblowsLeft ? pipColor : spentPipColor;
            }
        }

        BossPhases FindBoss()
        {
            if (player == null) return null;
            BossPhases best = null;
            float bestDistance = showRange;
            foreach (var candidate in FindObjectsByType<BossPhases>(FindObjectsSortMode.None))
            {
                float distance = Vector3.Distance(candidate.transform.position, player.position);
                if (distance > bestDistance) continue;
                best = candidate;
                bestDistance = distance;
            }
            return best;
        }

        void Show(bool on)
        {
            nameLabel.gameObject.SetActive(on);
            foreach (var pip in pips) pip.gameObject.SetActive(on);
        }
    }
}
