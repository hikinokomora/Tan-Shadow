using TanShadow.Combat;
using UnityEngine;

namespace TanShadow.Enemies
{
    // Переводит состояния GuardAI в Animator: передвижение, вздрагивание, слом ци, смерть.
    // Удары копьём клипов не используют: ими двигает SpearPose, руки держатся за древко через SpearGrip.
    [DefaultExecutionOrder(100)]
    [RequireComponent(typeof(GuardAI), typeof(Combatant))]
    public class GuardAnimator : MonoBehaviour
    {
        public Animator animator;
        [Tooltip("Сглаживание параметров передвижения, с")]
        [Min(0)] public float moveDamp = 0.12f;

        static readonly int MoveX = Animator.StringToHash("MoveX");
        static readonly int MoveZ = Animator.StringToHash("MoveZ");
        static readonly int Hit = Animator.StringToHash("Hit");
        static readonly int Broken = Animator.StringToHash("Broken");
        static readonly int Dead = Animator.StringToHash("Dead");

        GuardAI ai;
        Combatant combatant;
        GuardState lastState;
        Vector3 lastPosition;

        void Awake()
        {
            ai = GetComponent<GuardAI>();
            combatant = GetComponent<Combatant>();
            if (animator == null) animator = GetComponentInChildren<Animator>();
            lastPosition = transform.position;
        }

        void Update()
        {
            if (animator == null) return;
            // хитстоп — замирает аниматор участника, а не время
            animator.speed = combatant.InHitstop ? 0f : 1f;

            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            Vector3 velocity = (transform.position - lastPosition) / dt;
            lastPosition = transform.position;
            Vector3 local = transform.InverseTransformDirection(velocity);
            float norm = ai.moveSpeed > 0f ? ai.moveSpeed : 3f;
            // после воскрешения стражник телепортируется: не даём скачку скорости попасть в аниматор
            animator.SetFloat(MoveX, Mathf.Clamp(local.x / norm, -1.5f, 1.5f), moveDamp, dt);
            animator.SetFloat(MoveZ, Mathf.Clamp(local.z / norm, -1.5f, 1.5f), moveDamp, dt);

            if (ai.State == lastState) return;
            if (ai.State == GuardState.Recoil) animator.SetTrigger(Hit);
            animator.SetBool(Broken, ai.State == GuardState.Broken);
            animator.SetBool(Dead, ai.State == GuardState.Dead);
            lastState = ai.State;
        }
    }
}

