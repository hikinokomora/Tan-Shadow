using System.Collections.Generic;
using UnityEngine;

namespace TanShadow.Characters
{
    // Вторичная анимация: цепочки костей (подол, шнуры, ножны) догоняют движение тела с инерцией и гравитацией.
    // Работает после Animator и PoseInertializer, поэтому подходит к любой гуманоидной анимации
    // и к переходам между ними: юбку не нужно анимировать вручную в каждом клипе.
    // Симуляция идёт фиксированными подшагами 60 Гц, так что поведение не зависит от FPS.
    [DefaultExecutionOrder(1000)]
    public class SpringBone : MonoBehaviour
    {
        [Tooltip("Первые кости цепочек; дальше цепочка идёт по первому ребёнку до конца")]
        public Transform[] roots;

        [Header("Характер движения")]
        [Tooltip("Насколько сильно кость возвращается в исходное положение")]
        [Min(0)] public float stiffness = 1f;
        [Tooltip("Жёсткость концов цепочки относительно корня: меньше 1 — края подола свободнее")]
        [Range(0, 1)] public float tipStiffness = 1f;
        [Tooltip("Гашение колебаний, 0 — качается долго, 1 — сразу замирает")]
        [Range(0, 1)] public float drag = 0.4f;
        [Min(0)] public float gravity = 0.1f;
        public Vector3 gravityDirection = Vector3.down;
        [Tooltip("Максимальное отклонение от исходного положения, градусы")]
        [Range(0, 180)] public float maxAngle = 60f;
        [Tooltip("Насколько перемещение персонажа по миру раскачивает кости: 1 — как настоящая ткань, 0 — только движения тела")]
        [Range(0, 1)] public float worldInfluence = 1f;
        [Tooltip("Аниматор персонажа: на его хитстопе вторичная анимация тоже замирает")]
        public Animator timeSource;

        [Header("Столкновения")]
        [Min(0)] public float jointRadius = 0.02f;
        public SpringCollider[] colliders;

        const float StepRate = 60f;

        class Joint
        {
            public Transform bone;
            public Quaternion restLocal;
            public Vector3 axis;      // направление на ребёнка в локальных координатах кости
            public float length;
            public float stiffness;   // с учётом спада к концу цепочки
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
                for (int i = 0; i < chain.Count; i++)
                    chain[i].stiffness = Mathf.Lerp(1f, tipStiffness, chain.Count > 1 ? i / (chain.Count - 1f) : 0f);
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
            lastRootPosition = transform.position;
        }

        void LateUpdate()
        {
            // телепорт (рестарт, возрождение) — не тянуть ткань через полкарты
            Vector3 move = transform.position - lastRootPosition;
            if (move.sqrMagnitude > 1f) ResetChains();
            else if (worldInfluence < 1f)
            {
                // часть перемещения по миру переносим вместе с персонажем, не раскачивая ткань
                Vector3 carry = move * (1f - worldInfluence);
                foreach (var chain in chains)
                    foreach (var j in chain) { j.tail += carry; j.prevTail += carry; }
            }
            lastRootPosition = transform.position;

            float dt = Time.deltaTime * (timeSource != null ? timeSource.speed : 1f);
            if (dt <= 0f) return;
            dt = Mathf.Min(dt, 1f / 15f);

            int steps = Mathf.Clamp(Mathf.CeilToInt(dt * StepRate - 0.01f), 1, 4);
            float h = dt / steps;
            float keep = Mathf.Pow(1f - drag, h * StepRate);
            Vector3 gravityStep = gravityDirection.normalized * (gravity * h);

            for (int s = 0; s < steps; s++)
                foreach (var chain in chains)
                    foreach (var j in chain)
                        Step(j, h, keep, gravityStep);
        }

        void Step(Joint j, float h, float keep, Vector3 gravityStep)
        {
            Quaternion restRotation = j.bone.parent.rotation * j.restLocal;
            Vector3 restDir = restRotation * j.axis;
            Vector3 head = j.bone.position;

            Vector3 next = j.tail
                           + (j.tail - j.prevTail) * keep
                           + restDir * (stiffness * j.stiffness * h)
                           + gravityStep;

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
