using TanShadow.Combat;
using TanShadow.Player;
using UnityEngine;

namespace TanShadow.DebugTools
{
    // Минимальный оверлей для плейтеста: состояние игрока, фаза врага, последний результат удара.
    public class CombatDebugHud : MonoBehaviour
    {
        public PlayerController player;
        public AttackExecutor enemy;
        [Min(0)] public float resultShowTime = 0.6f;

        string lastResult = "";
        float resultLeft;
        int deflects, blocks, hits;
        GUIStyle big, small;

        void Start()
        {
            if (player == null) player = FindAnyObjectByType<PlayerController>();
            if (player != null) player.GetComponent<Combatant>().Hurt += OnPlayerHurt;
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

        void Update() => resultLeft -= Time.deltaTime;

        void OnGUI()
        {
            if (big == null)
            {
                big = new GUIStyle(GUI.skin.label) { fontSize = 40, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
                small = new GUIStyle(GUI.skin.label) { fontSize = 16 };
            }

            if (player != null)
            {
                var c = player.GetComponent<Combatant>();
                var lockOn = player.GetComponent<LockOnTargeting>();
                string text = $"Игрок: {player.State}   HP {c.Health:0}   окно дефлекта {c.CurrentDeflectWindow * 1000f:0} мс" +
                              (lockOn != null && lockOn.IsLocked ? $"   захват: {lockOn.Target.name}" : "") +
                              (enemy != null ? $"\nВраг: {enemy.Phase}" : "") +
                              $"\nДефлекты {deflects}   Блоки {blocks}   Пропущено {hits}";
                GUI.Label(new Rect(16, 16, 900, 80), text, small);
            }

            if (resultLeft > 0f)
                GUI.Label(new Rect(0, Screen.height * 0.25f, Screen.width, 60), lastResult, big);
        }
    }
}
