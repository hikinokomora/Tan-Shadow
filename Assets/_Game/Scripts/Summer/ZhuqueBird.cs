using System.Collections.Generic;
using UnityEngine;

namespace TanShadow.Summer
{
    // Тело Чжуцюэ: модель феникса со скелетом и Animator (AC_Zhuque). Испытание двигает корень птицы,
    // выбирает клип и задаёт простые параметры; компонент сам ничего не решает.
    // Начало координат птицы — у лап, клюв смотрит по +Z.
    //
    // Хвост — игровой объект: за кончик хватается героиня, на нём она висит в полёте. Поэтому кончик
    // считается не по анимации, а по правилу (TailTip), а кости хвоста после Animator дотягиваются
    // до этой точки (CCD IK). На валуне хвост свисает до земли, в полёте струится назад и вниз.
    public sealed class ZhuqueBird : MonoBehaviour
    {
        // Состояния AC_Zhuque (по именам клипов из phoenix.zip).
        public const string Perch = "01_Sleepy_Perch";
        public const string Alert = "02_Alert";
        public const string TakeOff = "03_Takeoff";
        public const string Landing = "04_Landing";
        public const string Fall = "05_Fall";
        public const string Rise = "06_Rise";
        public const string Recovery = "07_Recovery";
        public const string Flight = "08_Flight";

        [Tooltip("Animator модели феникса; пусто — первый найденный в детях")]
        public Animator animator;
        [Tooltip("Время смешивания клипов, с")]
        public float crossFade = 0.25f;
        [Tooltip("Цвет огненного сияния (восстановление, лава под птицей)")]
        public Color glowColor = new Color(1f, 0.45f, 0.12f);
        [Tooltip("Яркость сияния при 1. Экспозиция дневная, поэтому число большое")]
        public float glowIntensity = 600f;
        [Tooltip("Необязательный точечный свет сияния")]
        public Light glowLight;
        public float glowLightIntensity = 20000f;
        [Tooltip("На валуне тревога выше этого — поза тревоги (клип Alert)")]
        [Range(0, 1)] public float alarmPose = 0.6f;
        [Tooltip("Насколько хвост вытянут к цели: 1 — в струну. Меньше — провисает дугой")]
        [Range(0.8f, 1f)] public float tailReach = 0.96f;
        [Tooltip("Направление хвоста в полёте (в координатах птицы, вниз и назад). Было у прокси: 2 м вниз на 6 м назад")]
        public Vector3 flightTailDirection = new Vector3(0f, -0.33f, -0.94f);
        [Tooltip("Итераций CCD IK хвоста за кадр")]
        [Range(1, 20)] public int tailIterations = 8;

        Transform head, body;
        readonly List<Transform> tailBones = new List<Transform>();
        Vector3 tailRootLocal;          // корень хвоста в координатах птицы
        float tailChain = 1f;           // длина цепочки хвоста в координатах птицы, м
        readonly List<Renderer> renderers = new List<Renderer>();
        MaterialPropertyBlock block;
        static readonly int EmissiveColor = Shader.PropertyToID("_EmissiveColor");

        float tailLength = 1f, tailHang = 1f, hangDown = -1f;
        float glow, tailGlow, alarm, headYaw;
        string clip, held;

        public float TailLength => tailLength;
        public Vector3 EyePoint => head != null ? head.position : transform.TransformPoint(0f, 2.4f, 0.4f);
        public Vector3 BodyCenter => body != null ? body.position : transform.TransformPoint(0f, 1.3f, 0f);
        // Куда дотягивается кончик хвоста. Считается сразу из позы корня — можно читать в том же кадре, что и PlaceBird.
        public Vector3 TailTip => transform.TransformPoint(TailTipLocal());

        void Awake()
        {
            block = new MaterialPropertyBlock();
            if (animator == null) animator = GetComponentInChildren<Animator>(true);
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "Head") head = t;
                else if (t.name == "Body") body = t;
            }
            // Цепочка хвоста: Tail, Tail.001 … до последней кости.
            var bone = FindChild(transform, "Tail");
            while (bone != null)
            {
                tailBones.Add(bone);
                bone = NextTailBone(bone);
            }
            if (tailBones.Count >= 2)
            {
                tailRootLocal = transform.InverseTransformPoint(tailBones[0].position);
                tailChain = 0f;
                for (int i = 1; i < tailBones.Count; i++)
                    tailChain += (transform.InverseTransformPoint(tailBones[i].position) - transform.InverseTransformPoint(tailBones[i - 1].position)).magnitude;
            }
            else Debug.LogError("ZhuqueBird: в модели нет костей хвоста Tail…", this);
            renderers.AddRange(GetComponentsInChildren<Renderer>(true));
            Play(Perch, 0f);
        }

        static readonly int BaseColorMap = Shader.PropertyToID("_BaseColorMap");

        // Материал хвостовых перьев — тот, у которого текстура хвоста (Phoenix_Tail_*).
        static bool IsTailMaterial(Material m)
        {
            if (m == null || !m.HasProperty(BaseColorMap)) return false;
            var tex = m.GetTexture(BaseColorMap);
            return tex != null && tex.name.Contains("Tail");
        }

        static Transform FindChild(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            return null;
        }

        static Transform NextTailBone(Transform bone)
        {
            foreach (Transform c in bone) if (c.name.StartsWith("Tail")) return c;
            return null;
        }

        // ---------------- Управление из испытания ----------------

        // Сменить клип (без перезапуска, если он уже играет).
        public void Play(string state, float fade = -1f)
        {
            held = state;
            Show(state, fade);
        }

        void Show(string state, float fade = -1f)
        {
            if (animator == null || state == clip) return;
            clip = state;
            animator.CrossFadeInFixedTime(state, fade < 0f ? crossFade : fade);
        }

        // 1 — хвост целый, cutStub — отсечён.
        public void SetTailLength(float length) { tailLength = length; }
        // 1 — свисает (сидит на валуне, лежит на земле), 0 — струится назад в полёте.
        public void SetTailHang(float hang) { tailHang = Mathf.Clamp01(hang); }
        // Свисающий хвост ложится кончиком на землю на этой высоте (мировая Y).
        public void HangTo(float groundY)
        {
            float rootY = transform.TransformPoint(tailRootLocal).y;
            hangDown = Mathf.Max(0.2f, rootY - groundY - 0.15f);
        }
        public void SetGlow(float value) { glow = value; }
        // 0 — спокойна, 1 — сейчас сорвётся. На валуне выше alarmPose — поза тревоги.
        public void SetAlarm(float value) { alarm = Mathf.Clamp01(value); }
        // Подсветка хвоста: героиня достаёт до кончика.
        public void SetTailGlow(float value) { tailGlow = value; }
        // Голова поворачивается к цели, не дальше ±limit градусов от клюва.
        public void LookAt(Vector3 target, float limit, float dt)
        {
            Vector3 local = transform.InverseTransformDirection(target - EyePoint);
            float want = local.sqrMagnitude > 0.01f ? Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg : 0f;
            headYaw = Mathf.MoveTowardsAngle(headYaw, Mathf.Clamp(want, -limit, limit), 240f * dt);
        }
        public void LookForward(float dt) => headYaw = Mathf.MoveTowardsAngle(headYaw, 0f, 120f * dt);

        // Для совместимости с вызовами испытания: кончик хвоста считается в TailTip, кости — в LateUpdate.
        public void Apply() { }

        // ---------------- Хвост ----------------

        Vector3 TailTipLocal()
        {
            float length = tailChain * tailLength * tailReach;
            // Свисает: кончик на земле позади птицы, хвост почти в струну.
            float down = Mathf.Min(hangDown >= 0f ? hangDown : tailRootLocal.y, length * 0.95f);
            Vector3 hang = new Vector3(0f, -down, -Mathf.Sqrt(length * length - down * down));
            Vector3 fly = flightTailDirection.normalized * length;
            return tailRootLocal + Vector3.Slerp(fly, hang, tailHang);
        }

        void SolveTail()
        {
            int n = tailBones.Count;
            if (n < 2) return;
            // Отсечение укорачивает хвост от корня (масштаб корневой кости), отрастание — вытягивает обратно.
            tailBones[0].localScale = Vector3.one * Mathf.Max(0.05f, tailLength);
            Vector3 goal = TailTip;
            Transform end = tailBones[n - 1];
            for (int it = 0; it < tailIterations; it++)
            {
                for (int i = n - 2; i >= 0; i--)
                {
                    Vector3 toEnd = end.position - tailBones[i].position;
                    Vector3 toGoal = goal - tailBones[i].position;
                    if (toEnd.sqrMagnitude < 1e-6f || toGoal.sqrMagnitude < 1e-6f) continue;
                    tailBones[i].rotation = Quaternion.FromToRotation(toEnd, toGoal) * tailBones[i].rotation;
                }
                if ((end.position - goal).sqrMagnitude < 0.0025f) break;
            }
        }

        // ---------------- Кадр ----------------

        void LateUpdate()
        {
            // На валуне поза тревоги растёт из шкалы обнаружения.
            if (held == Perch || held == Alert)
                Show(alarm >= alarmPose || held == Alert ? Alert : Perch);

            if (head != null && Mathf.Abs(headYaw) > 0.01f)
                head.rotation = Quaternion.AngleAxis(headYaw, transform.up) * head.rotation;
            SolveTail();

            // Тело светится вспышкой, хвостовые перья — ещё и когда героиня достаёт до кончика.
            foreach (var r in renderers)
            {
                if (r == null) continue;
                var mats = r.sharedMaterials;
                for (int m = 0; m < mats.Length; m++)
                {
                    float value = IsTailMaterial(mats[m]) ? Mathf.Max(glow, tailGlow) : glow;
                    block.SetColor(EmissiveColor, glowColor * (glowIntensity * value));
                    r.SetPropertyBlock(block, m);
                }
            }
            if (glowLight != null)
            {
                glowLight.enabled = glow > 0.01f;
                glowLight.intensity = glowLightIntensity * glow;
            }
        }
    }
}
