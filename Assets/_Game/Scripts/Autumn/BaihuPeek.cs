using System.Collections.Generic;
using UnityEngine;

namespace TanShadow.Autumn
{
    // Часть морды тигра, выглядывающая из сухой травы: фарфоровая голова с тёмными полосами,
    // светящиеся красные глаза, нижняя часть морды скрыта стеблями, позади — тёмная масса зарослей.
    // Грейбокс из примитивов; финальную модель головы можно подставить в поле headOverride.
    // Голова сама «выплывает» из травы, а с ростом агрессии пригибается и подаётся вперёд (телеграф прыжка).
    public sealed class BaihuPeek : MonoBehaviour
    {
        [Header("Материалы")]
        public Material peltMaterial;    // фарфоровая шерсть
        public Material stripeMaterial;  // тёмные полосы, нос
        public Material eyesMaterial;    // красные глаза
        public Material grassMaterial;   // сухая трава перед мордой
        public Material shadowMaterial;  // тёмная масса зарослей позади

        [Header("Форма")]
        [Tooltip("Финальная модель головы (если есть). Пусто — собирается грейбокс из примитивов")]
        public GameObject headOverride;
        [Tooltip("Масштаб головы. Тигр крупнее обычного (GDD-AU)")]
        [Min(0.1f)] public float scale = 1.5f;
        [Tooltip("Насколько стебли закрывают морду снизу, м (от уровня глаз вниз)")]
        public float grassCoverTop = -0.06f;
        [Tooltip("Высота глаз над землёй, м (должна совпадать с BaihuTuning.eyesHeight)")]
        public float eyesHeightAboveGround = 1.3f;

        [Header("Движение")]
        [Tooltip("Насколько голова выдвигается из травы при появлении, м")]
        public float emergeDistance = 0.55f;
        [Tooltip("Время появления и исчезновения, с")]
        public float emergeTime = 0.45f;
        [Tooltip("При полной агрессии голова пригибается на столько, м")]
        public float crouchAtFullAggro = 0.14f;
        [Tooltip("При полной агрессии голова подаётся вперёд на столько, м")]
        public float pushAtFullAggro = 0.2f;
        [Tooltip("Яркость свечения глаз: обычная и при полной агрессии")]
        public Vector2 eyeGlow = new Vector2(3f, 14f);

        public bool IsVisible => gameObject.activeSelf && !hiding;

        Transform head, grass;
        readonly List<Renderer> eyeRenderers = new List<Renderer>();
        Renderer[] visualParts;
        bool[] visualDefaults;
        MaterialPropertyBlock block;
        float emerge, aggro, phase;
        float scareFadeUntil;
        bool hiding;
        float hideTime;                   // длительность текущего исчезновения
        float withdrawBack, withdrawSink; // отступление вглубь зарослей (отпугнули)
        static readonly int Emissive = Shader.PropertyToID("_EmissiveColor");
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly int ExposureWeight = Shader.PropertyToID("_EmissiveExposureWeight");

        void Awake()
        {
            block = new MaterialPropertyBlock();
            Build();
            visualParts = GetComponentsInChildren<Renderer>(true);
            visualDefaults = new bool[visualParts.Length];
            for (int i = 0; i < visualParts.Length; i++)
                visualDefaults[i] = visualParts[i].enabled;
            gameObject.SetActive(false);
        }

        public void Show(Vector3 eyesPosition, Vector3 lookAt)
        {
            SetPinned(false);
            transform.position = eyesPosition;
            Face(lookAt);
            hiding = false;
            withdrawBack = withdrawSink = 0f;
            emerge = 0f;
            aggro = 0f;
            scareFadeUntil = 0f;
            phase = Random.value * 10f;
            gameObject.SetActive(true);
            Pose();
        }

        public void ShowPinned(Vector3 eyesPosition, Vector3 lookAt)
        {
            transform.position = eyesPosition;
            Face(lookAt);
            hiding = false;
            withdrawBack = withdrawSink = 0f;
            emerge = 1f;
            aggro = .4f;
            SetPinned(true);
            gameObject.SetActive(true);
            Pose();
        }

        void SetPinned(bool value)
        {
            if (visualParts == null || eyeRenderers.Count == 0) return;
            for (int i = 0; i < visualParts.Length; i++)
            {
                var part = visualParts[i];
                bool foreground = !value && part.transform.IsChildOf(grass);
                part.enabled = visualDefaults[i] && (foreground || eyeRenderers.Contains(part));
            }
        }

        public void Hide(bool instant = false)
        {
            if (!gameObject.activeSelf) return;
            if (instant) { gameObject.SetActive(false); hiding = false; return; }
            hiding = true;
            hideTime = emergeTime;
            withdrawBack = withdrawSink = 0f;
        }

        // Отпугнули: морда пятится вглубь бамбука и опускается, стебли сильно колышутся.
        public void Withdraw(float back, float sink, float duration)
        {
            if (!gameObject.activeSelf) return;
            hiding = true;
            hideTime = duration;
            withdrawBack = back;
            withdrawSink = sink;
            scareFadeUntil = Time.time + 0.24f;
        }

        public void Face(Vector3 lookAt)
        {
            Vector3 d = lookAt - transform.position;
            d.y = 0f;
            if (d.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(d);
        }

        public void SetAggro(float value) => aggro = Mathf.Clamp01(value);

        void Update()
        {
            float speed = Time.deltaTime / Mathf.Max(0.01f, hiding ? hideTime : emergeTime);
            emerge = hiding ? emerge - speed : Mathf.Min(1f, emerge + speed);
            if (hiding && emerge <= 0f)
            {
                hiding = false;
                gameObject.SetActive(false);
                return;
            }
            Pose();
        }

        void Pose()
        {
            if (head == null) return;
            phase += Time.deltaTime;
            float e = Mathf.SmoothStep(0f, 1f, emerge);
            float breathe = Mathf.Sin(phase * 1.9f) * 0.012f;
            float tilt = Mathf.Sin(phase * 0.7f) * 4f * (1f - aggro);
            float gone = 1f - e;
            head.localPosition = new Vector3(0f,
                breathe - crouchAtFullAggro * aggro - withdrawSink * gone,
                -emergeDistance * gone + pushAtFullAggro * aggro - withdrawBack * gone);
            head.localRotation = Quaternion.Euler(6f * aggro, 0f, tilt);

            // Стебли перед мордой чуть колышутся.
            if (grass != null)
            {
                float stir = withdrawBack > 0f ? 1f + 4f * Mathf.Sin(Mathf.PI * gone) : 1f;
                grass.localRotation = Quaternion.Euler(Mathf.Sin(phase * 2.3f * stir) * 2f * stir, 0f, Mathf.Sin(phase * 1.7f * stir) * 3f * stir);
            }

            float eyeOpacity = hiding && withdrawBack > 0f
                ? Mathf.Clamp01((scareFadeUntil - Time.time) / 0.24f)
                : e;
            float emission = Mathf.Lerp(eyeGlow.x, eyeGlow.y, aggro) * .32f * eyeOpacity;
            Color glow = new Color(1f, 0.035f, 0.018f) * emission;
            foreach (var r in eyeRenderers)
            {
                r.GetPropertyBlock(block);
                block.SetColor(Emissive, glow);
                block.SetColor(BaseColor, new Color(.36f, .015f, .008f) * eyeOpacity);
                block.SetFloat(ExposureWeight, 0f);
                r.SetPropertyBlock(block);
            }
        }

        // ---------------- Сборка грейбокса ----------------

        void Build()
        {
            head = new GameObject("Head").transform;
            head.SetParent(transform, false);

            if (headOverride != null)
            {
                Instantiate(headOverride, head, false);
                foreach (var r in head.GetComponentsInChildren<Renderer>())
                    if (r.sharedMaterial == eyesMaterial) eyeRenderers.Add(r);
            }
            else
            {
                float s = scale;
                // Череп и лоб
                Part(head, PrimitiveType.Sphere, peltMaterial, new Vector3(0f, 0.02f, -0.16f) * s, new Vector3(0.7f, 0.56f, 0.66f) * s);
                Part(head, PrimitiveType.Cube, peltMaterial, new Vector3(0f, 0.12f, 0.1f) * s, new Vector3(0.6f, 0.16f, 0.24f) * s);
                // Морда и нос
                Part(head, PrimitiveType.Cube, peltMaterial, new Vector3(0f, -0.11f, 0.24f) * s, new Vector3(0.36f, 0.24f, 0.34f) * s);
                Part(head, PrimitiveType.Cube, stripeMaterial, new Vector3(0f, -0.03f, 0.42f) * s, new Vector3(0.13f, 0.08f, 0.05f) * s);
                // Скулы
                Part(head, PrimitiveType.Sphere, peltMaterial, new Vector3(-0.22f, -0.08f, 0.08f) * s, new Vector3(0.26f, 0.26f, 0.3f) * s);
                Part(head, PrimitiveType.Sphere, peltMaterial, new Vector3(0.22f, -0.08f, 0.08f) * s, new Vector3(0.26f, 0.26f, 0.3f) * s);
                // Уши
                Part(head, PrimitiveType.Cube, peltMaterial, new Vector3(-0.26f, 0.3f, -0.16f) * s, new Vector3(0.15f, 0.17f, 0.05f) * s, 18f);
                Part(head, PrimitiveType.Cube, peltMaterial, new Vector3(0.26f, 0.3f, -0.16f) * s, new Vector3(0.15f, 0.17f, 0.05f) * s, -18f);
                // Полосы на лбу и щеках
                for (int i = -1; i <= 1; i++)
                    Part(head, PrimitiveType.Cube, stripeMaterial, new Vector3(i * 0.09f, 0.2f, 0.215f) * s, new Vector3(0.035f, 0.13f, 0.02f) * s, i * 12f);
                for (int side = -1; side <= 1; side += 2)
                for (int i = 0; i < 2; i++)
                    Part(head, PrimitiveType.Cube, stripeMaterial,
                         new Vector3(side * 0.33f, -0.02f - i * 0.09f, 0.03f) * s, new Vector3(0.02f, 0.035f, 0.18f) * s, side * 8f);
                // Глаза
                for (int side = -1; side <= 1; side += 2)
                {
                    var eye = Part(head, PrimitiveType.Sphere, eyesMaterial,
                                   new Vector3(side * 0.13f, 0.05f, 0.24f) * s, new Vector3(0.1f, 0.065f, 0.05f) * s);
                    eyeRenderers.Add(eye.GetComponent<Renderer>());
                }
            }

            // Тёмная масса зарослей позади: голова не висит в воздухе, тело «уходит» в бамбук.
            Part(transform, PrimitiveType.Sphere, shadowMaterial != null ? shadowMaterial : stripeMaterial,
                 new Vector3(0f, -0.3f, -0.75f) * scale, new Vector3(0.8f, 1f, 1.1f) * scale);

            // Стебли сухой травы перед мордой: закрывают нижнюю часть, видна только верхняя часть морды и глаза.
            grass = new GameObject("Grass screen").transform;
            grass.SetParent(transform, false);
            grass.localPosition = new Vector3(0f, 0f, 0.62f * scale);
            var random = new System.Random(731);
            float R() => (float)random.NextDouble();
            float groundY = -eyesHeightAboveGround;
            for (int i = 0; i < 26; i++)
            {
                float x = (R() - 0.5f) * 1.25f * scale;
                float top = grassCoverTop + (R() - 0.35f) * 0.28f - Mathf.Abs(x) * 0.25f;
                float height = top - groundY;
                var blade = Part(grass, PrimitiveType.Cube, grassMaterial,
                                 new Vector3(x, groundY + height * 0.5f, (R() - 0.5f) * 0.25f),
                                 new Vector3(Mathf.Lerp(0.025f, 0.05f, R()), height, 0.012f), (R() - 0.5f) * 16f);
                blade.name = "Blade";
            }
        }

        static GameObject Part(Transform parent, PrimitiveType type, Material material, Vector3 position, Vector3 size, float roll = 0f)
        {
            var go = GameObject.CreatePrimitive(type);
            var collider = go.GetComponent<Collider>();
            if (collider != null) Object.Destroy(collider);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localRotation = Quaternion.Euler(0f, 0f, roll);
            go.transform.localScale = size;
            if (material != null) go.GetComponent<Renderer>().sharedMaterial = material;
            return go;
        }
    }
}
