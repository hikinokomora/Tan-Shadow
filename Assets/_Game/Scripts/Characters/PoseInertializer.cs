using UnityEngine;

namespace TanShadow.Characters
{
    // Плавные переходы без задержки реакции ("dead blending", как инерционный блендинг в Unreal).
    // Аниматор переключается на новое состояние мгновенно, а этот компонент доигрывает старую позу
    // по инерции её же движения и за blendTime плавно сводит её в новую. Так удар или уворот
    // начинается в тот же кадр, что и ввод, но без рывка в позе.
    [DefaultExecutionOrder(900)]
    public class PoseInertializer : MonoBehaviour
    {
        public Animator animator;
        [Tooltip("За сколько секунд гасится инерция старой позы. Меньше — старое движение быстрее останавливается")]
        [Min(0.01f)] public float velocityHalfLife = 0.08f;
        [Tooltip("Ограничение угловой скорости при экстраполяции, рад/с — защита от выбросов")]
        [Min(1f)] public float maxAngularSpeed = 20f;

        Transform[] bones;
        Quaternion[] lastOut, source;
        Vector3[] velocity, sourceVelocity;
        Transform hips;
        Vector3 hipsLast, hipsVelocity, hipsSource, hipsSourceVelocity;

        bool hasHistory, pending, blending;
        float pendingDuration, duration, elapsed;

        public bool IsBlending => blending || pending;

        void Awake()
        {
            if (animator == null) animator = GetComponentInChildren<Animator>();
            var list = new System.Collections.Generic.List<Transform>();
            for (var b = HumanBodyBones.Hips; b < HumanBodyBones.LastBone; b++)
            {
                var t = animator.GetBoneTransform(b);
                if (t != null) list.Add(t);
            }
            bones = list.ToArray();
            hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            lastOut = new Quaternion[bones.Length];
            source = new Quaternion[bones.Length];
            velocity = new Vector3[bones.Length];
            sourceVelocity = new Vector3[bones.Length];
        }

        void OnEnable() => hasHistory = false;

        // Начать переход. Вызывать в тот же кадр, что и переключение состояния аниматора.
        public void Begin(float blendTime)
        {
            if (!isActiveAndEnabled || blendTime <= 0f) return;
            pending = true;
            pendingDuration = blendTime;
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime * (animator != null ? animator.speed : 1f);

            if (!hasHistory)
            {
                Record(0f);
                hasHistory = true;
                pending = false;
                return;
            }

            if (pending)
            {
                // Источник — то, что было на экране в прошлом кадре, вместе с его скоростью.
                // Если переход прервал другой переход, это уже смешанная поза — так и надо.
                for (int i = 0; i < bones.Length; i++)
                {
                    source[i] = lastOut[i];
                    sourceVelocity[i] = velocity[i];
                }
                hipsSource = hipsLast;
                hipsSourceVelocity = hipsVelocity;
                duration = pendingDuration;
                elapsed = 0f;
                blending = true;
                pending = false;
            }

            if (blending && dt > 0f)
            {
                elapsed += dt;
                float decay = Mathf.Exp(-0.6931472f * dt / velocityHalfLife);
                float t = Mathf.Clamp01(elapsed / duration);
                float alpha = t * t * (3f - 2f * t);

                for (int i = 0; i < bones.Length; i++)
                {
                    sourceVelocity[i] *= decay;
                    source[i] = Integrate(source[i], sourceVelocity[i], dt);
                    bones[i].localRotation = Quaternion.Slerp(source[i], bones[i].localRotation, alpha);
                }
                hipsSourceVelocity *= decay;
                hipsSource += hipsSourceVelocity * dt;
                hips.localPosition = Vector3.Lerp(hipsSource, hips.localPosition, alpha);

                if (t >= 1f) blending = false;
            }
            else if (blending)
            {
                // хитстоп: держим ту же смешанную позу
                float t = Mathf.Clamp01(elapsed / duration);
                float alpha = t * t * (3f - 2f * t);
                for (int i = 0; i < bones.Length; i++)
                    bones[i].localRotation = Quaternion.Slerp(source[i], bones[i].localRotation, alpha);
                hips.localPosition = Vector3.Lerp(hipsSource, hips.localPosition, alpha);
            }

            Record(dt);
        }

        void Record(float dt)
        {
            // Скорости не считаем на паузе и после больших провалов кадра.
            bool measure = dt > 0f && dt < 0.1f;
            float maxHips = 10f / Mathf.Max(1e-4f, hips.parent != null ? hips.parent.lossyScale.x : 1f);
            for (int i = 0; i < bones.Length; i++)
            {
                var current = bones[i].localRotation;
                if (measure) velocity[i] = Vector3.ClampMagnitude(AngularVelocity(lastOut[i], current, dt), maxAngularSpeed);
                else if (dt > 0f) velocity[i] = Vector3.zero;
                lastOut[i] = current;
            }
            var hp = hips.localPosition;
            if (measure) hipsVelocity = Vector3.ClampMagnitude((hp - hipsLast) / dt, maxHips);
            else if (dt > 0f) hipsVelocity = Vector3.zero;
            hipsLast = hp;
        }

        static Vector3 AngularVelocity(Quaternion from, Quaternion to, float dt)
        {
            var delta = to * Quaternion.Inverse(from);
            if (delta.w < 0f) { delta.x = -delta.x; delta.y = -delta.y; delta.z = -delta.z; delta.w = -delta.w; }
            delta.ToAngleAxis(out float angle, out Vector3 axis);
            if (angle < 1e-3f || float.IsNaN(axis.x)) return Vector3.zero;
            return axis * (angle * Mathf.Deg2Rad / dt);
        }

        static Quaternion Integrate(Quaternion q, Vector3 angularVelocity, float dt)
        {
            float speed = angularVelocity.magnitude;
            if (speed < 1e-5f) return q;
            return Quaternion.AngleAxis(speed * dt * Mathf.Rad2Deg, angularVelocity / speed) * q;
        }
    }
}
