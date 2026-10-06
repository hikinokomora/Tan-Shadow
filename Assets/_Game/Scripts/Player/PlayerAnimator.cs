using System.Collections.Generic;
using TanShadow.Combat;
using UnityEngine;

namespace TanShadow.Player
{
    // Переводит состояния PlayerController в Animator: передвижение, комбо, блок, дефлект, оглушение, рывок, добивание.
    // Длительность клипов подгоняется под тайминги из AttackData и CombatSettings через параметр ActionSpeed.
    [DefaultExecutionOrder(100)]
    [RequireComponent(typeof(PlayerController))]
    public class PlayerAnimator : MonoBehaviour
    {
        public Animator animator;
        [Tooltip("Плавность перехода между анимациями, с")]
        [Min(0)] public float crossFade = 0.08f;
        [Tooltip("Сглаживание параметров передвижения, с")]
        [Min(0)] public float moveDamp = 0.1f;
        [Tooltip("За сколько секунд слой блока поднимает руки")]
        [Min(0.01f)] public float blockBlend = 0.08f;
        [Tooltip("Сколько длится визуальный уворот, с (дольше самого рывка — чтобы не дёргалось)")]
        [Min(0.05f)] public float dodgeVisualDuration = 0.5f;
        [Tooltip("Сколько длится анимация дефлекта, с")]
        [Min(0.05f)] public float deflectVisualDuration = 0.45f;

        static readonly int MoveX = Animator.StringToHash("MoveX");
        static readonly int MoveZ = Animator.StringToHash("MoveZ");
        static readonly int DashX = Animator.StringToHash("DashX");
        static readonly int DashZ = Animator.StringToHash("DashZ");
        static readonly int ActionSpeed = Animator.StringToHash("ActionSpeed");

        static readonly string[] StabStates = { "Stab_1", "Stab_2", "Stab_3", "Stab_4_ThrustSlash" };

        PlayerController player;
        Combatant combatant;
        AttackExecutor executor;
        CombatSettings settings;
        readonly Dictionary<string, float> clipLength = new Dictionary<string, float>();
        PlayerState lastState;
        Vector3 lastPosition;
        float blockWeight;

        void Awake()
        {
            player = GetComponent<PlayerController>();
            combatant = GetComponent<Combatant>();
            executor = GetComponent<AttackExecutor>();
            settings = player.settings;
            if (animator == null) animator = GetComponentInChildren<Animator>();
            foreach (var clip in animator.runtimeAnimatorController.animationClips)
                clipLength[clip.name] = clip.length;

            executor.PhaseChanged += OnAttackPhase;
            lastPosition = transform.position;
        }

        void OnAttackPhase(AttackPhase phase)
        {
            // Замах приходит раньше, чем PlayerController переключит состояние на Attack, поэтому состояние не проверяем.
            if (phase != AttackPhase.Windup || executor.Current == null) return;
            int step = Mathf.Clamp(player.ComboStep, 0, StabStates.Length - 1);
            Play(StabStates[step], executor.Current.Duration);
        }

        void Update()
        {
            // хитстоп — замирают аниматоры участников, а не время
            animator.speed = combatant.InHitstop ? 0f : 1f;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            Vector3 velocity = (transform.position - lastPosition) / dt;
            lastPosition = transform.position;
            Vector3 local = transform.InverseTransformDirection(velocity);
            float norm = settings != null && settings.moveSpeed > 0f ? settings.moveSpeed : 4.5f;
            animator.SetFloat(MoveX, Mathf.Clamp(local.x / norm, -1f, 1f), moveDamp, dt);
            animator.SetFloat(MoveZ, Mathf.Clamp(local.z / norm, -1f, 1f), moveDamp, dt);

            bool blocking = combatant.IsBlocking && (player.State == PlayerState.Block || player.State == PlayerState.Locomotion);
            blockWeight = Mathf.MoveTowards(blockWeight, blocking ? 1f : 0f, dt / blockBlend);
            if (animator.layerCount > 1) animator.SetLayerWeight(1, blockWeight);

            if (player.State != lastState) OnStateChanged(player.State);
            lastState = player.State;
        }

        void OnStateChanged(PlayerState state)
        {
            switch (state)
            {
                case PlayerState.Locomotion:
                case PlayerState.Block:
                    animator.CrossFadeInFixedTime("Locomotion", crossFade * 2f, 0);
                    break;
                case PlayerState.Deflect:
                    Play("Deflect_BlockReact", deflectVisualDuration);
                    break;
                case PlayerState.Stagger:
                    bool heavy = player.StaggerDuration > 1f;
                    Play(heavy ? "Stagger_Impact" : "Hit_Reaction", player.StaggerDuration);
                    break;
                case PlayerState.Dash:
                    Vector3 d = transform.InverseTransformDirection(player.DashDirection);
                    animator.SetFloat(DashX, d.x);
                    animator.SetFloat(DashZ, d.z);
                    Play("Dodge", dodgeVisualDuration, "Dodge_Forward");
                    break;
                case PlayerState.Finisher:
                    Play("Finisher_DoubleStab", settings.finisherDuration);
                    break;
            }
        }

        // lengthKey — клип, по длине которого считаем скорость (для blend tree уворота)
        void Play(string state, float duration, string lengthKey = null)
        {
            float len = clipLength.TryGetValue(lengthKey ?? state, out var l) ? l : duration;
            animator.SetFloat(ActionSpeed, duration > 0f ? len / duration : 1f);
            animator.CrossFadeInFixedTime(state, crossFade, 0, 0f);
        }
    }
}
