using System.Collections.Generic;
using UnityEngine;

namespace TanShadow.Characters
{
    // Вторичная анимация: цепочки костей (подол, шнуры, ножны) догоняют движение тела с инерцией и гравитацией.
    // Работает после Animator, поэтому совместима с любыми гуманоидными анимациями.
    [DefaultExecutionOrder(1000)]
    public class SpringBone : MonoBehaviour
    {
        [Tooltip("Первые кости цепочек; дальше цепочка идёт по первому ребёнку до конца")]
        public Transform[] roots;

        [Header("Характер движения")]
        [Tooltip("Насколько сильно кость возвращается в исходное положение")]
        [Min(0)] public float stiffness = 1f;
        [Tooltip("Гашение колебаний, 0 — качается долго, 1 — сразу замирает")]
        [Range(0, 1)] public float drag = 0.4f;
        [Min(0)] public float gravity = 0.1f;
        public Vector3 gravityDirection = Vector3.down;
        [Tooltip("Максимальное отклонение от исходного положения, градусы")]
        [Range(0, 180)] public float maxAngle = 60f;

        [Header("Столкновения")]
        [Min(0)] public float jointRadius = 0.02f;
        public SpringCollider[] colliders;

        class Joint
        {
            public Transform bone;
            public Quaternion restLocal;
            public Vector3 axis;      // направление на ребёнка в локальных координатах кости
            public float length;
            public Vector3 tail, prevTail;
        }

        readonly List<List<Joint>> chains = new List<List<Joint>>();
        Vector3 lastRootPosition;

        void Awake()
        {
            foreach (var root in roots)
            {
                if (root == null) continue;
                var chain = new List<Joint>();
                var bone = root;
                while (bone.childCount > 0)
                {
                    var child = bone.GetChild(0);
                    var j = new Joint
                    {
                        bone = bone,
                        restLocal = bone.localRotation,
                        axis = bone.InverseTransformPoint(child.position).normalized,
                        length = Vector3.Distance(bone.position, child.position)
                    };
                    j.tail = j.prevTail = child.position;
                    chain.Add(j);
                    bone = child;
                }
                chains.Add(chain);
            }
            lastRootPosition = transform.position;
        }

        void OnEnable() => ResetChains();

        public void ResetChains()
        {
            foreach (var chain in chains)
                foreach (var j in chain)
                {
                    j.bone.localRotation = j.restLocal;
                    j.tail = j.prevTail = j.bone.TransformPoint(j.axis * j.length / j.bone.lossyScale.x);
                }
        }

        void LateUpdate()
        {
            // телепорт (рестарт, возрождение) — не тянуть ткань через полкарты
            if ((transform.position - lastRootPosition).sqrMagnitude > 1f) ResetChains();
            lastRootPosition = transform.position;

            float dt = Mathf.Min(Time.deltaTime, 1f / 30f);
            if (dt <= 0f) return;

            foreach (var chain in chains)
            {
                foreach (var j in chain)
                {
                    Quaternion restRotation = j.bone.parent.rotation * j.restLocal;
                    Vector3 restDir = restRotation * j.axis;
                    Vector3 head = j.bone.position;

                    Vector3 next = j.tail
                                   + (j.tail - j.prevTail) * (1f - drag)
                                   + restDir * (stiffness * dt)
                                   + gravityDirection.normalized * (gravity * dt);

                    Vector3 dir = (next - head).normalized;
                    float angle = Vector3.Angle(restDir, dir);
                    if (angle > maxAngle)
                        dir = Vector3.Slerp(restDir, dir, maxAngle / angle);
                    next = head + dir * j.length;

                    if (colliders != null)
                        foreach (var c in colliders)
                            if (c != null) next = c.PushOut(next, jointRadius);
                    next = head + (next - head).normalized * j.length;

                    j.prevTail = j.tail;
                    j.tail = next;
                    j.bone.rotation = Quaternion.FromToRotation(restDir, next - head) * restRotation;
                }
            }
        }
    }
}
