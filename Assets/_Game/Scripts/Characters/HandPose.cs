using System.Collections.Generic;
using UnityEngine;

namespace TanShadow.Characters
{
    // Фиксированные позы кистей поверх анимации: правая сжата в кулак под кинжал, левая расслаблена.
    // Клипы Mixamo несут мусор в мышцах пальцев (разведение до ×10), поэтому пальцы из них не берём.
    [DefaultExecutionOrder(50)]
    [RequireComponent(typeof(Animator))]
    public class HandPose : MonoBehaviour
    {
        [Tooltip("Сгиб пальцев правой руки: −1 кулак, 1 прямые")]
        [Range(-1f, 1f)] public float rightCurl = -0.75f;
        [Range(-1f, 1f)] public float rightThumbCurl = -0.2f;
        [Range(-1f, 1f)] public float rightThumbSpread = -0.2f;
        [Tooltip("Сгиб пальцев левой руки")]
        [Range(-1f, 1f)] public float leftCurl = 0.25f;
        [Range(-1f, 1f)] public float leftThumbCurl = 0.3f;

        readonly List<Transform> bones = new List<Transform>();
        readonly List<Quaternion> rotations = new List<Quaternion>();

        void Awake() => Bake();

        // Позу считаем один раз через мышцы гуманоида, затем каждый кадр просто ставим локальные повороты костей пальцев.
        public void Bake()
        {
            var animator = GetComponent<Animator>();
            if (animator.avatar == null || !animator.avatar.isHuman) return;
            bones.Clear();
            rotations.Clear();

            var handler = new HumanPoseHandler(animator.avatar, animator.transform);
            var pose = new HumanPose();
            handler.GetHumanPose(ref pose);
            var saved = new List<(Transform, Vector3, Quaternion)>();
            foreach (var t in animator.GetComponentsInChildren<Transform>()) saved.Add((t, t.localPosition, t.localRotation));

            string[] names = HumanTrait.MuscleName;
            for (int i = 0; i < names.Length; i++)
            {
                string n = names[i];
                bool right = n.StartsWith("Right"), left = n.StartsWith("Left");
                if (!(right || left) || !IsFinger(n)) continue;
                bool thumb = n.Contains("Thumb");
                if (n.Contains("Spread")) pose.muscles[i] = thumb && right ? rightThumbSpread : 0f;
                else if (thumb) pose.muscles[i] = right ? rightThumbCurl : leftThumbCurl;
                else pose.muscles[i] = right ? rightCurl : leftCurl;
            }
            handler.SetHumanPose(ref pose);

            for (int b = (int)HumanBodyBones.LeftThumbProximal; b <= (int)HumanBodyBones.RightLittleDistal; b++)
            {
                var t = animator.GetBoneTransform((HumanBodyBones)b);
                if (t == null) continue;
                bones.Add(t);
                rotations.Add(t.localRotation);
            }
            foreach (var (t, p, r) in saved) { t.localPosition = p; t.localRotation = r; }
            handler.Dispose();
        }

        static bool IsFinger(string muscle) =>
            muscle.Contains("Thumb") || muscle.Contains("Index") || muscle.Contains("Middle") || muscle.Contains("Ring") || muscle.Contains("Little");

        void LateUpdate()
        {
            for (int i = 0; i < bones.Count; i++) bones[i].localRotation = rotations[i];
        }
    }
}
