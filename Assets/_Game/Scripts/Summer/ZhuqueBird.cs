using System.Collections.Generic;
using UnityEngine;

namespace TanShadow.Summer
{
    // Оснастка грейбокс-птицы из SummerCalderaBuilder: хвост, крылья и голова получают шарниры,
    // а испытание управляет позой через простые параметры. Сам по себе компонент ничего не решает.
    // Начало координат птицы — у лап.
    public sealed class ZhuqueBird : MonoBehaviour
    {
        [Tooltip("Цвет огненного сияния (восстановление, тревога)")]
        public Color glowColor = new Color(1f, 0.45f, 0.12f);
        [Tooltip("Яркость сияния при 1. Экспозиция дневная, поэтому число большое")]
        public float glowIntensity = 600f;
        [Tooltip("Необязательный точечный свет сияния")]
        public Light glowLight;
        public float glowLightIntensity = 20000f;
        [Tooltip("На сколько градусов хвост поднимается в полёте (0 — висит как на валуне)")]
        [Range(0, 45)] public float flightTailLift = 25f;

        // Геометрия хвоста из билдера: корень и кончик в координатах птицы.
        static readonly Vector3 TailRoot = new Vector3(0f, 1.27f, -0.92f);
        static readonly Vector3 TailTipLocal = new Vector3(0f, -3.06f, -5.37f);
        static readonly Vector3 NeckBase = new Vector3(0f, 1.5f, 0.59f);
        static readonly Vector3 EyeLocal = new Vector3(0f, 2.72f, 1.0f);

        Transform tail, tip, head, eye;
        readonly Transform[] wings = new Transform[2];
        readonly List<Renderer> body = new List<Renderer>();
        readonly List<Renderer> tailRenderers = new List<Renderer>();
        MaterialPropertyBlock block;
        static readonly int EmissiveColor = Shader.PropertyToID("_EmissiveColor");

        float tailLength = 1f, tailDrop = 1f, tailHang = 1f;
        float wingSpread, flap, glow, tailGlow, alarm;
        float headYaw;

        public Vector3 TailTip => tip.position;
        public Vector3 EyePoint => eye.position;
        public Vector3 BodyCenter => transform.TransformPoint(new Vector3(0f, 1.3f, 0f));
        public float TailLength => tailLength;

        void Awake()
        {
            block = new MaterialPropertyBlock();
            tail = MakePivot("Tail pivot", TailRoot);
            head = MakePivot("Head pivot", NeckBase);
            for (int s = 0; s < 2; s++)
            {
                int side = s == 0 ? -1 : 1;
                wings[s] = MakePivot(side < 0 ? "Wing pivot L" : "Wing pivot R", new Vector3(side * 0.31f, 1.67f, 0.45f));
            }

            var children = new List<Transform>();
            foreach (Transform c in transform) children.Add(c);
            foreach (var c in children)
            {
                string n = c.name;
                if (n.StartsWith("Trailing tail plume")) c.SetParent(tail, true);
                else if (n.StartsWith("Folded primary -1")) c.SetParent(wings[0], true);
                else if (n.StartsWith("Folded primary 1")) c.SetParent(wings[1], true);
                else if (n.StartsWith("Raised neck") || n.StartsWith("Curved beak") || n.StartsWith("Swept crest") || n == "Eye")
                    c.SetParent(head, true);
            }

            tip = new GameObject("Tail tip").transform;
            tip.SetParent(tail, false);
            tip.localPosition = TailTipLocal - TailRoot;
            eye = new GameObject("Eye point").transform;
            eye.SetParent(head, false);
            eye.localPosition = EyeLocal - NeckBase;

            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                if (r.transform.IsChildOf(tail)) tailRenderers.Add(r);
                else body.Add(r);
            }
            Apply();
        }

        Transform MakePivot(string name, Vector3 local)
        {
            var t = new GameObject(name).transform;
            t.SetParent(transform, false);
            t.localPosition = local;
            return t;
        }

        // 1 — хвост целый, cutStub — отсечён.
        public void SetTailLength(float length) { tailLength = length; }
        // Насколько вытянуть хвост вниз, чтобы с этого валуна он доставал до земли (1 — как в модели).
        public void SetTailDrop(float drop) { tailDrop = drop; }
        // 1 — свисает (сидит на валуне), 0 — струится назад в полёте.
        public void SetTailHang(float hang) { tailHang = hang; }
        public void SetWings(float spread, float flapDegrees) { wingSpread = spread; flap = flapDegrees; }
        public void SetGlow(float value) { glow = value; }
        // 0 — спокойна, 1 — сейчас сорвётся (заглушка позы тревоги).
        public void SetAlarm(float value) { alarm = Mathf.Clamp01(value); }
        // Подсветка кончика хвоста: героиня достаёт до него.
        public void SetTailGlow(float value) { tailGlow = value; }
        // Голова поворачивается к цели, не дальше ±limit градусов от клюва.
        public void LookAt(Vector3 target, float limit, float dt)
        {
            Vector3 local = transform.InverseTransformDirection(target - head.position);
            float want = local.sqrMagnitude > 0.01f ? Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg : 0f;
            headYaw = Mathf.MoveTowardsAngle(headYaw, Mathf.Clamp(want, -limit, limit), 240f * dt);
        }
        public void LookForward(float dt) => headYaw = Mathf.MoveTowardsAngle(headYaw, 0f, 120f * dt);

        // Хвост, нарисованный за кадр до этого, должен совпадать с тем, где висит героиня: вызывать до чтения TailTip.
        public void Apply()
        {
            if (tail == null) return;
            // Отсечение укорачивает хвост от корня; восстановление вытягивает обратно.
            float down = Mathf.Lerp(1f, tailDrop, tailHang) * tailLength;
            tail.localScale = new Vector3(1f, down, tailLength);
            // В полёте хвост поднимается и струится назад (кончик ≈ 2 м ниже корня, 6 м позади).
            // Заглушка тревоги (до анимации роли D): шея вытянута, голова дёргается рывками по сторонам,
            // крылья приподняты и подрагивают, хвост нервно подёргивается.
            float time = Time.time;
            float jerk = (Mathf.PerlinNoise(Mathf.Floor(time * 4f) * 0.37f, 0.5f) - 0.5f) * 70f * alarm;
            float shiver = Mathf.Sin(time * 31f) * alarm;
            tail.localRotation = Quaternion.Euler(Mathf.Lerp(flightTailLift, 0f, tailHang) - 6f * alarm, 0f, Mathf.Sin(time * 9f) * 7f * alarm);
            head.localPosition = NeckBase + Vector3.up * (0.18f * alarm);
            head.localRotation = Quaternion.Euler(-18f * alarm, headYaw + jerk, 0f);
            for (int s = 0; s < 2; s++)
            {
                int side = s == 0 ? -1 : 1;
                float spread = Mathf.Max(wingSpread, 0.22f * alarm);
                wings[s].localRotation = Quaternion.Euler(0f, -side * 80f * spread, -side * (flap + 14f * alarm + 9f * shiver));
            }

            block.SetColor(EmissiveColor, glowColor * (glowIntensity * glow));
            foreach (var r in body) r.SetPropertyBlock(block);
            block.SetColor(EmissiveColor, glowColor * (glowIntensity * Mathf.Max(glow, tailGlow)));
            foreach (var r in tailRenderers) r.SetPropertyBlock(block);
            if (glowLight != null)
            {
                glowLight.enabled = glow > 0.01f;
                glowLight.intensity = glowLightIntensity * glow;
            }
        }

        void LateUpdate() => Apply();
    }
}
