using TanShadow.Combat;
using UnityEngine;

namespace TanShadow.DebugTools
{
    // Временная подсветка капсул вместо анимаций и VFX: фазы атаки, блок, вспышки попаданий.
    [RequireComponent(typeof(Combatant))]
    public class DebugTint : MonoBehaviour
    {
        public Renderer target;
        public Color baseColor = new Color(0.6f, 0.6f, 0.6f);
        public Color windupGlint = new Color(1f, 0.85f, 0.2f);
        public Color windupDanger = new Color(0.9f, 0.05f, 0.05f);
        public Color activeColor = Color.white;
        public Color blockColor = new Color(0.25f, 0.45f, 0.9f);
        public Color hitFlash = new Color(1f, 0.2f, 0.2f);
        public Color deflectFlash = new Color(0.6f, 1f, 1f);
        public Color brokenColor = new Color(0.55f, 0.05f, 0.35f);
        public Color deadColor = new Color(0.12f, 0.12f, 0.12f);
        [Min(0)] public float flashTime = 0.15f;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        Combatant combatant;
        AttackExecutor executor;
        Posture posture;
        MaterialPropertyBlock block;
        Color flashColor;
        float flashLeft;

        void Awake()
        {
            combatant = GetComponent<Combatant>();
            executor = GetComponent<AttackExecutor>();
            posture = GetComponent<Posture>();
            block = new MaterialPropertyBlock();
            combatant.Hurt += hit => Flash(hit.result == HitResult.Hit ? hitFlash : deflectFlash, hit.result);
            combatant.AttackLanded += hit => Flash(deflectFlash, hit.result);
        }

        void Flash(Color color, HitResult result)
        {
            if (result == HitResult.Hit || result == HitResult.Deflected)
            {
                flashColor = color;
                flashLeft = flashTime;
            }
        }

        void LateUpdate()
        {
            if (target == null) return;
            flashLeft -= Time.deltaTime;

            Color color = baseColor;
            if (combatant.IsDead) color = deadColor;
            else if (flashLeft > 0f) color = flashColor;
            else if (posture != null && posture.IsBroken) color = Color.Lerp(baseColor, brokenColor, 0.5f + 0.5f * Mathf.Sin(Time.time * 10f));
            else if (executor != null && executor.Phase == AttackPhase.Windup) color = WindupColor();
            else if (executor != null && executor.Phase == AttackPhase.Active) color = activeColor;
            else if (combatant.IsBlocking) color = blockColor;

            target.GetPropertyBlock(block);
            block.SetColor(BaseColorId, color);
            target.SetPropertyBlock(block);
        }

        Color WindupColor()
        {
            var attack = executor.Current;
            if (attack == null) return baseColor;
            if (attack.type == AttackType.Unblockable || (attack.telegraph & AttackTelegraph.DangerKanji) != 0) return windupDanger;
            if ((attack.telegraph & AttackTelegraph.BladeGlint) != 0) return windupGlint;
            return baseColor;
        }
    }
}
