using UnityEngine;

namespace TanShadow.Autumn
{
    // Нарастающая угроза без шкалы на экране: дрожь и раскачивание камеры растут с агрессией тигра.
    // Гонг, удар о столб и падение столба — толчок камеры. Шум и гонг звучат из BaihuSounds.
    // Работает после камеры AutumnTrial, поэтому дрожь накладывается поверх.
    [DefaultExecutionOrder(500)]
    public sealed class BaihuFeedback : MonoBehaviour
    {
        public BaihuEncounter encounter;
        public Camera view;
        [Tooltip("Множитель дрожи камеры (настройка доступности: 0 — без дрожи). Шум не отключается")]
        [Range(0, 1)] public float cameraMotionMultiplier = 1f;
        [Tooltip("Насколько угроза ощущается, пока только шевелится трава (до глаз), 0..1")]
        [Range(0, 1)] public float rustleUnease = 0.1f;

        float smoothed, kick;

        BaihuTuning T => encounter != null ? encounter.tuning : null;

        void OnEnable()
        {
            if (encounter == null) return;
            encounter.GongStruck += OnGong;
            encounter.MonumentHit += OnMonumentHit;
            encounter.MonumentLanded += OnLanded;
        }

        void OnDisable()
        {
            if (encounter == null) return;
            encounter.GongStruck -= OnGong;
            encounter.MonumentHit -= OnMonumentHit;
            encounter.MonumentLanded -= OnLanded;
        }

        void OnGong(Vector3 _)
        {
            kick = Mathf.Max(kick, T != null ? T.gongKick : 0.8f);
        }

        void OnMonumentHit(int hits, int required, Vector3 _) => kick = Mathf.Max(kick, 1f);
        void OnLanded(Vector3 _) => kick = Mathf.Max(kick, 1.6f);

        float Target()
        {
            switch (encounter.State)
            {
                case BaihuState.Rustle:
                case BaihuState.Eyes: return Mathf.Max(encounter.Aggro, rustleUnease);
                case BaihuState.Gong:
                case BaihuState.Pounce: return 1f;
                case BaihuState.Defeated:
                case BaihuState.HeroDown: return 0f;
                default: return encounter.Aggro;
            }
        }

        void LateUpdate()
        {
            if (T == null) return;
            float dt = Time.deltaTime;
            float target = Target();
            smoothed = Mathf.MoveTowards(smoothed, target, dt * (target > smoothed ? 1.5f : 2.5f));
            kick = Mathf.MoveTowards(kick, 0f, dt * 4f);

            if (view == null) return;
            float tremor = (T.tremorByAggro.Evaluate(smoothed) + kick) * cameraMotionMultiplier;
            float sway = T.swayByAggro.Evaluate(smoothed) * cameraMotionMultiplier;
            if (tremor + sway <= 0.0001f) return;
            float time = Time.time;
            float pitch = Perlin(time * 16f, 0.1f) * tremor + Perlin(time * 0.4f, 3.3f) * sway;
            float yaw = Perlin(5.2f, time * 16f) * tremor + Perlin(7.7f, time * 0.35f) * sway;
            view.transform.rotation *= Quaternion.Euler(pitch, yaw, 0f);
        }

        static float Perlin(float x, float y) => (Mathf.PerlinNoise(x, y) - 0.5f) * 2f;
    }
}
