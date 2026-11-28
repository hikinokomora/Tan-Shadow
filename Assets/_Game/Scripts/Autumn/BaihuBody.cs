using UnityEngine;

namespace TanShadow.Autumn
{
    // Тело Байху: скелетная модель с Animator (AC_Baihu). Испытание двигает корень тигра и говорит, в какой
    // доле фазы он сейчас; компонент ставит кадр нужного клипа. Клипы проигрываются не по времени, а по ходу
    // фазы: прыжок любой длины, падение, подъём и прыжок в лес всегда показываются целиком.
    // Клипы сдвигают и разворачивают корневую кость Master (прыжок вперёд, разворот к лесу). Тигра двигает испытание,
    // поэтому после Animator смещение Master по земле и поворот вокруг вертикали гасятся; высота и наклоны остаются.
    // Ось «вверх» у Master — локальная Z (риг из Blender), поэтому root motion Unity тут не подходит.
    // Начало координат — у лап, морда смотрит по +Z.
    public sealed class BaihuBody : MonoBehaviour
    {
        // Состояния AC_Baihu (по клипам из Baihu.blend).
        public const string AttackJump = "AttackJump";
        public const string LandMiss = "LandMiss";
        public const string MonumentHit = "MonumentHit";
        public const string ReturnToBamboo = "ReturnToBamboo";

        [Tooltip("Animator модели; пусто — первый найденный в детях")]
        public Animator animator;

        [Header("Кадры клипов (30 fps, как в Baihu.blend)")]
        [Tooltip("AttackJump: тигр отрывается от земли. Присед до него проходит, пока видны только глаза")]
        public int takeoffFrame = 9;
        [Tooltip("LandMiss: лапы на земле после промаха")]
        public int landFrame = 16;
        [Tooltip("Доля полёта, на которой прыжок сменяется приземлением")]
        [Range(0.1f, 0.9f)] public float landingStart = 0.5f;
        [Tooltip("MonumentHit: тигр рухнул после удара о столб")]
        public int hitGroundFrame = 12;
        [Tooltip("MonumentHit: начинает подниматься")]
        public int getUpFrame = 28;
        [Tooltip("MonumentHit: стоит на лапах")]
        public int standFrame = 46;
        [Tooltip("ReturnToBamboo: конец разворота, начало прыжка в лес")]
        public int leapFrame = 35;

        public Vector3 HeadPoint => head != null ? head.position : transform.TransformPoint(0f, 3f, 4f);

        Transform head, master;
        Vector3 masterPosition;
        Quaternion masterRotation;

        void Awake()
        {
            if (animator == null) animator = GetComponentInChildren<Animator>(true);
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "Head") head = t;
                else if (t.name == "Master") master = t;
            }
            if (master != null)
            {
                masterPosition = master.localPosition;
                masterRotation = master.localRotation;
            }
        }

        void OnEnable()
        {
            if (animator != null) animator.speed = 0f;
        }

        void LateUpdate() => KeepInPlace();

        // Master остаётся над началом координат и смотрит вперёд; высота (локальная Z) и наклоны — из клипа.
        public void KeepInPlace()
        {
            if (master == null) return;
            Vector3 p = master.localPosition;
            master.localPosition = new Vector3(masterPosition.x, masterPosition.y, p.z);
            Quaternion delta = Quaternion.Inverse(masterRotation) * master.localRotation;
            var twist = new Quaternion(0f, 0f, delta.z, delta.w);
            float length = Mathf.Sqrt(twist.z * twist.z + twist.w * twist.w);
            if (length < 1e-5f) return;
            twist = new Quaternion(0f, 0f, twist.z / length, twist.w / length);
            master.localRotation = masterRotation * (delta * Quaternion.Inverse(twist));
        }

        // Прыжок на героиню. s — доля полёта 0..1: сначала толчок и полёт, к концу — приземление.
        public void Pounce(float s)
        {
            s = Mathf.Clamp01(s);
            if (s < landingStart) Pose(AttackJump, Mathf.Lerp(takeoffFrame, Frames(AttackJump), s / landingStart));
            else Pose(LandMiss, Mathf.Lerp(0f, landFrame, (s - landingStart) / (1f - landingStart)));
        }

        // Удар о столб: fall — падение, lie — лежит, getUp — поднимается (каждое 0..1).
        public void Knock(float fall, float lie, float getUp)
        {
            float frame = getUp > 0f ? Mathf.Lerp(getUpFrame, standFrame, Mathf.Clamp01(getUp))
                : lie > 0f ? Mathf.Lerp(hitGroundFrame, getUpFrame, Mathf.Clamp01(lie))
                : Mathf.Lerp(0f, hitGroundFrame, Mathf.Clamp01(fall));
            Pose(MonumentHit, frame);
        }

        // Оглушён и бродит: шаги разворота перед прыжком в лес (k 0..1).
        public void Wander(float k) => Pose(ReturnToBamboo, Mathf.Lerp(0f, leapFrame, Mathf.Clamp01(k)));

        // Прыжок обратно в лес (t 0..1).
        public void Leap(float t) => Pose(ReturnToBamboo, Mathf.Lerp(leapFrame, Frames(ReturnToBamboo), Mathf.Clamp01(t)));

        void Pose(string state, float frame)
        {
            if (animator == null || !animator.isActiveAndEnabled || animator.runtimeAnimatorController == null) return;
            animator.speed = 0f;
            animator.Play(state, 0, Mathf.Clamp01(frame / Mathf.Max(1f, Frames(state))));
        }

        float Frames(string state)
        {
            var controller = animator != null ? animator.runtimeAnimatorController : null;
            if (controller != null)
                foreach (var clip in controller.animationClips)
                    if (clip != null && clip.name.EndsWith(state)) return clip.length * clip.frameRate;
            return 1f;
        }
    }
}
