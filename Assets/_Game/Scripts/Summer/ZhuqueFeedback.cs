using UnityEngine;

namespace TanShadow.Summer
{
    // Толчки камеры: отсечение перьев, падение птицы, открытие лавы. В полёте — лёгкая дрожь от ветра.
    // Обнаружение без шкалы: дрожь и сужение кадра растут со шкалой птицы (шум — в ZhuqueSounds).
    // Работает после камеры героини, поэтому накладывается поверх.
    [DefaultExecutionOrder(500)]
    public sealed class ZhuqueFeedback : MonoBehaviour
    {
        public ZhuqueEncounter encounter;
        public Camera view;
        [Tooltip("Множитель дрожи камеры (настройка доступности: 0 — без дрожи)")]
        [Range(0, 1)] public float cameraMotionMultiplier = 1f;
        [Tooltip("Дрожь в полёте на хвосте, °")]
        public float flightTremor = 0.25f;
        [Tooltip("Толчок при отсечении, падении птицы и открытии лавы, °")]
        public float cutKick = 1f, fallKick = 1.3f, lavaKick = 1.8f;

        [Header("Обнаружение (GDD-00 §05: без шкалы — камера и шум)")]
        [Tooltip("Дрожь камеры при полной шкале обнаружения, °. Растёт плавно, заметна уже с половины")]
        public float detectionTremor = 0.9f;
        [Tooltip("Насколько сужается кадр при полной шкале, ° поля зрения")]
        public float detectionFovSqueeze = 7f;

        float kick, rumble, baseFov = -1f, detectionShown;

        void OnEnable()
        {
            if (encounter == null) return;
            encounter.Cut += OnCut;
            encounter.BirdFell += OnFell;
            encounter.LavaOpened += OnLava;
        }

        void OnDisable()
        {
            if (encounter == null) return;
            encounter.Cut -= OnCut;
            encounter.BirdFell -= OnFell;
            encounter.LavaOpened -= OnLava;
        }

        void OnCut(int cuts, int required, Vector3 _) => kick = Mathf.Max(kick, cutKick);
        void OnFell(Vector3 _) => kick = Mathf.Max(kick, fallKick);
        void OnLava(Vector3 _)
        {
            kick = Mathf.Max(kick, lavaKick);
            rumble = 2.5f; // земля дрожит, пока заливаются русла
        }

        void LateUpdate()
        {
            if (view == null || encounter == null) return;
            float dt = Time.deltaTime;
            kick = Mathf.MoveTowards(kick, 0f, dt * 3f);
            rumble = Mathf.Max(0f, rumble - dt);
            var s = encounter.State;
            bool flying = s == ZhuqueState.TakeOff || s == ZhuqueState.Flight || s == ZhuqueState.ShakeOff;

            // Чем ближе птица к тому, чтобы заметить, тем сильнее дрожит и сужается кадр.
            float watched = s == ZhuqueState.Alert ? 1f : encounter.Detection;
            detectionShown = Mathf.MoveTowards(detectionShown, watched, dt * 4f);
            if (baseFov < 0f) baseFov = view.fieldOfView;
            view.fieldOfView = baseFov - detectionFovSqueeze * Mathf.SmoothStep(0f, 1f, detectionShown) * cameraMotionMultiplier;

            float tremor = (kick + (flying ? flightTremor : 0f) + Mathf.Min(rumble, 1f) * 0.35f
                            + detectionTremor * detectionShown * detectionShown) * cameraMotionMultiplier;
            if (tremor <= 0.0001f) return;
            float time = Time.time;
            float pitch = (Mathf.PerlinNoise(time * 17f, 0.3f) - 0.5f) * 2f * tremor;
            float yaw = (Mathf.PerlinNoise(4.1f, time * 17f) - 0.5f) * 2f * tremor;
            view.transform.rotation *= Quaternion.Euler(pitch, yaw, 0f);
        }
    }
}
