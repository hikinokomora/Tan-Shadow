using TanShadow.Combat;
using UnityEngine;

namespace TanShadow.Enemies
{
    // Процедурная поза копья по фазам атаки — читаемый телеграф до появления анимаций.
    public class SpearPose : MonoBehaviour
    {
        [Tooltip("Точка хвата; копьё смотрит вдоль её +Z")]
        public Transform spear;
        public GuardAI ai;
        public AttackExecutor executor;

        public Vector3 restPosition = new Vector3(0.3f, 1.1f, 0.15f);
        public Vector3 restEuler = new Vector3(25f, 0f, 0f);
        [Min(0)] public float returnSpeed = 10f;

        struct Pose
        {
            public Vector3 position;
            public Quaternion rotation;

            public Pose(Vector3 p, Vector3 euler)
            {
                position = p;
                rotation = Quaternion.Euler(euler);
            }

            public static Pose Lerp(Pose a, Pose b, float t) => new Pose
            {
                position = Vector3.LerpUnclamped(a.position, b.position, t),
                rotation = Quaternion.SlerpUnclamped(a.rotation, b.rotation, t)
            };
        }

        void LateUpdate()
        {
            if (spear == null || executor == null) return;
            var rest = new Pose(restPosition, restEuler);

            if (!executor.IsBusy || executor.Current == null)
            {
                float k = 1f - Mathf.Exp(-returnSpeed * Time.deltaTime);
                spear.localPosition = Vector3.Lerp(spear.localPosition, rest.position, k);
                spear.localRotation = Quaternion.Slerp(spear.localRotation, rest.rotation, k);
                return;
            }

            GetKeyPoses(ai != null ? ai.CurrentMotion : SpearMotion.Overhead, rest, out var windup, out var strike);
            var attack = executor.Current;
            Pose pose;
            switch (executor.Phase)
            {
                case AttackPhase.Windup:
                    pose = Pose.Lerp(rest, windup, Ease(Progress(attack.windup)));
                    break;
                case AttackPhase.Active:
                    pose = Pose.Lerp(windup, strike, Ease(Progress(attack.active)));
                    break;
                default:
                    pose = Pose.Lerp(strike, rest, Ease(Progress(attack.recovery)));
                    break;
            }
            spear.localPosition = pose.position;
            spear.localRotation = pose.rotation;
        }

        void GetKeyPoses(SpearMotion motion, Pose rest, out Pose windup, out Pose strike)
        {
            Vector3 p = restPosition;
            switch (motion)
            {
                case SpearMotion.SweepRight:
                    windup = new Pose(p + new Vector3(0.1f, 0.1f, -0.1f), new Vector3(5f, 75f, 0f));
                    strike = new Pose(p + new Vector3(-0.25f, 0.1f, 0.15f), new Vector3(5f, -60f, 0f));
                    break;
                case SpearMotion.SweepLeft:
                    windup = new Pose(p + new Vector3(-0.35f, 0.1f, -0.1f), new Vector3(5f, -75f, 0f));
                    strike = new Pose(p + new Vector3(0.1f, 0.1f, 0.15f), new Vector3(5f, 60f, 0f));
                    break;
                case SpearMotion.Thrust:
                    windup = new Pose(p + new Vector3(-0.15f, 0.15f, -0.5f), new Vector3(5f, 0f, 0f));
                    strike = new Pose(p + new Vector3(-0.15f, 0.15f, 0.6f), new Vector3(5f, 0f, 0f));
                    break;
                default:
                    windup = new Pose(p + new Vector3(-0.1f, 0.5f, -0.15f), new Vector3(-75f, 0f, 0f));
                    strike = new Pose(p + new Vector3(-0.1f, 0.1f, 0.25f), new Vector3(40f, 0f, 0f));
                    break;
            }
        }

        float Progress(float duration) => duration > 0f ? Mathf.Clamp01(executor.PhaseTime / duration) : 1f;

        static float Ease(float t) => t * t * (3f - 2f * t);
    }
}
