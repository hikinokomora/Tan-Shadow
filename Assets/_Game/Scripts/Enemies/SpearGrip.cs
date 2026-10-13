using UnityEngine;

namespace TanShadow.Enemies
{
    // Обе руки стражника тянутся к точкам хвата на древке (Humanoid IK), поэтому копьё, которым двигает SpearPose,
    // всегда в руках. На смерти копьё наоборот следует за правой рукой и падает вместе с телом.
    // Скрипт стоит на объекте с Animator; в слое контроллера должен быть включён IK Pass.
    [DefaultExecutionOrder(50)]
    public class SpearGrip : MonoBehaviour
    {
        public Animator animator;
        public GuardAI ai;
        public SpearPose pose;
        [Tooltip("Копьё: начало координат — хват правой руки, наконечник вдоль +Z")]
        public Transform spear;

        [Header("Хват")]
        [Tooltip("Хват правой (задней) руки в системе копья, м")]
        public Vector3 rightLocal = Vector3.zero;
        [Tooltip("Хват левой (передней) руки в системе копья, м")]
        public Vector3 leftLocal = new Vector3(0f, 0f, 0.55f);
        [Tooltip("Поворот кисти относительно копья, градусы")]
        public Vector3 rightHandEuler = new Vector3(0f, 90f, 90f);
        public Vector3 leftHandEuler = new Vector3(0f, -90f, -90f);
        [Range(0, 1)] public float ikWeight = 1f;
        [Min(0)] public float weightSpeed = 8f;

        float currentWeight;

        void Reset()
        {
            animator = GetComponent<Animator>();
            ai = GetComponentInParent<GuardAI>();
            pose = GetComponentInParent<SpearPose>();
        }

        bool IsDead => ai != null && ai.State == GuardState.Dead;

        void Update()
        {
            float target = IsDead ? 0f : ikWeight;
            currentWeight = Mathf.MoveTowards(currentWeight, target, weightSpeed * Time.deltaTime);
            if (pose != null) pose.enabled = !IsDead;
        }

        void OnAnimatorIK(int layerIndex)
        {
            if (spear == null || animator == null) return;
            SetGoal(AvatarIKGoal.RightHand, rightLocal, rightHandEuler);
            SetGoal(AvatarIKGoal.LeftHand, leftLocal, leftHandEuler);
        }

        void SetGoal(AvatarIKGoal goal, Vector3 local, Vector3 euler)
        {
            animator.SetIKPositionWeight(goal, currentWeight);
            animator.SetIKRotationWeight(goal, currentWeight);
            animator.SetIKPosition(goal, spear.TransformPoint(local));
            animator.SetIKRotation(goal, spear.rotation * Quaternion.Euler(euler));
        }

        void LateUpdate()
        {
            if (!IsDead || spear == null || animator == null) return;
            var hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            if (hand == null) return;
            Quaternion rotation = hand.rotation * Quaternion.Inverse(Quaternion.Euler(rightHandEuler));
            spear.SetPositionAndRotation(hand.position - rotation * rightLocal, rotation);
        }
    }
}

