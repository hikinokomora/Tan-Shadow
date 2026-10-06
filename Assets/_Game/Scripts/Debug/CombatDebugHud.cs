using TanShadow.Combat;
using TanShadow.Enemies;
using TanShadow.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TanShadow.DebugTools
{
    // Оверлей для плейтеста и тюнинга (F1 — скрыть/показать):
    // состояния, ци, результат удара и шкала текущей атаки врага с зоной дефлекта и отметками нажатий блока.
    public class CombatDebugHud : MonoBehaviour
    {
        public PlayerController player;
        public AttackExecutor enemy;
        [Min(0)] public float resultShowTime = 0.6f;
        public bool visible = true;

        string lastResult = "";
        float resultLeft;
        int deflects, blocks, hits;
        GUIStyle big, small;

        Combatant playerCombatant;
        GuardAI guard;
        AttackData shownAttack;
        float attackElapsed;
        readonly System.Collections.Generic.List<float> blockPresses = new System.Collections.Generic.List<float>();
        bool wasBlocking;

        void Start()
        {
            if (player == null) player = FindAnyObjectByType<PlayerController>();
            if (player != null)
            {
                playerCombatant = player.GetComponent<Combatant>();
                playerCombatant.Hurt += OnPlayerHurt;
            }
            if (enemy == null)
            {
                guard = FindAnyObjectByType<GuardAI>();
                if (guard != null) enemy = guard.GetComponent<AttackExecutor>();
            }
            else
            {
                guard = enemy.GetComponent<GuardAI>();
            }
            if (enemy != null) enemy.PhaseChanged += OnEnemyPhase;
        }

        void OnEnemyPhase(AttackPhase phase)
        {
            if (phase != AttackPhase.Windup) return;
            shownAttack = enemy.Current;
            attackElapsed = 0f;
            blockPresses.Clear();
        }

        void OnPlayerHurt(HitInfo hit)
        {
            switch (hit.result)
            {
                case HitResult.Deflected: lastResult = "ДЕФЛЕКТ"; deflects++; break;
                case HitResult.Blocked: lastResult = "блок"; blocks++; break;
                case HitResult.Hit: lastResult = "попадание"; hits++; break;
                case HitResult.Evaded: lastResult = "уворот"; break;
            }
            resultLeft = resultShowTime;
        }

        void Update()
        {
            resultLeft -= Time.deltaTime;
            if (Keyboard.current != null && Keyboard.current.f1Key.wasPressedThisFrame) visible = !visible;

            if (enemy != null && enemy.IsBusy && enemy.Current == shownAttack)
                attackElapsed = PhaseStart(enemy.Phase, shownAttack) + enemy.PhaseTime;

            if (playerCombatant != null)
            {
                if (playerCombatant.IsBlocking && !wasBlocking && shownAttack != null) blockPresses.Add(attackElapsed);
                wasBlocking = playerCombatant.IsBlocking;
            }
        }

        static float PhaseStart(AttackPhase phase, AttackData a)
        {
            switch (phase)
            {
                case AttackPhase.Active: return a.windup;
                case AttackPhase.Recovery: return a.windup + a.active;
                default: return 0f;
            }
        }

        void OnGUI()
        {
            if (big == null)
            {
                big = new GUIStyle(GUI.skin.label) { fontSize = 40, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
                small = new GUIStyle(GUI.skin.label) { fontSize = 16 };
            }

            if (resultLeft > 0f)
                GUI.Label(new Rect(0, Screen.height * 0.25f, Screen.width, 60), lastResult, big);
            if (!visible || player == null) return;

            var lockOn = player.GetComponent<LockOnTargeting>();
            var qi = player.GetComponent<YinYangQi>();
            var huajin = player.GetComponent<Huajin>();
            var posture = enemy != null ? enemy.GetComponent<Posture>() : null;
            string text = $"Игрок: {player.State}   HP {playerCombatant.Health:0}   ци {(qi != null ? qi.Value : 0f):+0;-0;0}   окно дефлекта {playerCombatant.CurrentDeflectWindow * 1000f:0} мс" +
                          (huajin != null && huajin.IsCharged ? $"   化劲 ×{huajin.Multiplier:0.00} ({huajin.TimeLeft:0.0} с)" : "") +
                          (lockOn != null && lockOn.IsLocked ? $"   захват: {lockOn.Target.name}" : "") +
                          (enemy != null ? $"\nВраг: {(guard != null ? guard.State + (guard.State == GuardState.Attack && guard.CurrentPattern != null ? " " + guard.CurrentPattern.name : "") + "  " : "")}{enemy.Phase}" +
                                           (posture != null ? $"   ци {posture.Value:0}/{posture.maxPosture:0}" + (posture.IsBroken ? "  СЛОМЛЕН" : "") : "") : "") +
                          $"\nДефлекты {deflects}   Блоки {blocks}   Пропущено {hits}   (F1 — скрыть)";
            GUI.Label(new Rect(16, 16, 1000, 80), text, small);

            if (shownAttack != null) DrawTimeline(new Rect(16, 92, 420, 14));
        }

        // Шкала атаки: замах (жёлтый), удар (красный), восстановление (серый),
        // зелёная зона — когда нажать блок для дефлекта, белая черта — сейчас, синие — ваши нажатия.
        void DrawTimeline(Rect r)
        {
            var a = shownAttack;
            float total = a.windup + a.active + a.recovery;
            if (total <= 0f) return;
            float px = r.width / total;

            Box(new Rect(r.x, r.y, a.windup * px, r.height), new Color(0.9f, 0.75f, 0.2f, 0.8f));
            Box(new Rect(r.x + a.windup * px, r.y, a.active * px, r.height), new Color(0.9f, 0.15f, 0.1f, 0.9f));
            Box(new Rect(r.x + (a.windup + a.active) * px, r.y, a.recovery * px, r.height), new Color(0.4f, 0.4f, 0.4f, 0.8f));

            if (a.type != AttackType.Unblockable && playerCombatant.settings != null)
            {
                float window = playerCombatant.settings.deflectWindow;
                float from = Mathf.Max(0f, a.windup - window);
                Box(new Rect(r.x + from * px, r.y - 5, (a.windup - from) * px, 4), new Color(0.3f, 1f, 0.4f, 0.95f));
            }

            foreach (var t in blockPresses)
                Box(new Rect(r.x + t * px - 1, r.y - 3, 3, r.height + 6), new Color(0.35f, 0.6f, 1f, 1f));

            Box(new Rect(r.x + Mathf.Min(attackElapsed, total) * px - 1, r.y - 4, 2, r.height + 8), Color.white);
            GUI.Label(new Rect(r.x + r.width + 8, r.y - 4, 300, 24),
                $"{a.name}  {a.windup * 1000:0}/{a.active * 1000:0}/{a.recovery * 1000:0} мс" + (a.type == AttackType.Unblockable ? "  危 — только рывок" : ""), small);
        }

        static void Box(Rect r, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }
    }
}
