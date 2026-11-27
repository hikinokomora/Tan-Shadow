using UnityEngine;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace ShadowTan.Gameplay.Autumn
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class AutumnWindController : MonoBehaviour
    {
        [SerializeField] Vector2 direction = new(0.88f, 0.48f);
        [SerializeField, Range(0f, 1f)] float baseStrength = 0.51f;
        [SerializeField, Range(0f, 1f)] float gustStrength = 0.49f;
        [SerializeField, Min(0.01f)] float gustRate = 0.24f;
        [SerializeField, Min(0f)] float speed = 1.62f;
        [SerializeField, Min(0f)] float frequency = 0.44f;

        static readonly int DirectionId = Shader.PropertyToID("_AutumnWindDirection");
        static readonly int StrengthId = Shader.PropertyToID("_AutumnWindStrength");
        static readonly int SpeedId = Shader.PropertyToID("_AutumnWindSpeed");
        static readonly int FrequencyId = Shader.PropertyToID("_AutumnWindFrequency");
        static readonly int GustId = Shader.PropertyToID("_AutumnWindGust");
        static readonly int EditorClockId = Shader.PropertyToID("_AutumnWindEditorClock");
        static readonly int ImpulseCenterId = Shader.PropertyToID("_AutumnImpulseCenter");
        static readonly int ImpulseDirectionId = Shader.PropertyToID("_AutumnImpulseDirection");

        static AutumnWindController owner;

        public float CurrentStrength { get; private set; }
        public float Gust { get; private set; }
        Vector4 impulseCenter;
        Vector2 impulseHeading;
        float impulseStart, impulseDuration;

#if UNITY_EDITOR
        double nextPreview;

        void OnEnable()
        {
            EditorApplication.update += Preview;
            Preview();
        }

        void Preview()
        {
            if (Application.isPlaying || !isActiveAndEnabled || !IsActiveScene()) return;

            double now = EditorApplication.timeSinceStartup;
            if (now < nextPreview) return;
            nextPreview = now + 1.0 / 30.0;
            Publish((float)now, true);
            SceneView.RepaintAll();
        }
#endif

        void Update()
        {
            if (Application.IsPlaying(gameObject) && IsActiveScene())
                Publish(Time.timeSinceLevelLoad, false);
        }

        void OnDisable()
        {
#if UNITY_EDITOR
            EditorApplication.update -= Preview;
#endif
            CurrentStrength = 0f;
            Gust = 0f;
            if (owner != this) return;

            owner = null;
            Shader.SetGlobalFloat(StrengthId, 0f);
            Shader.SetGlobalFloat(GustId, 0f);
            Shader.SetGlobalVector(EditorClockId, Vector4.zero);
            Shader.SetGlobalVector(ImpulseCenterId, Vector4.zero);
        }

        bool IsActiveScene() => gameObject.scene == SceneManager.GetActiveScene();

        public void TriggerImpulse(Vector3 point, Vector3 heading, float radius, float duration, float strength)
        {
            impulseCenter = new Vector4(point.x, point.z, Mathf.Max(radius, 0.01f), strength);
            impulseHeading = new Vector2(heading.x, heading.z).normalized;
            impulseStart = Time.time;
            impulseDuration = Mathf.Max(duration, 0.01f);
        }

        public void ClearImpulse()
        {
            impulseCenter = Vector4.zero;
            if (owner == this) Shader.SetGlobalVector(ImpulseCenterId, Vector4.zero);
        }

        void Publish(float time, bool editMode)
        {
            owner = this;
            float t = time * gustRate;
            float noise = Mathf.PerlinNoise(t, 8.31f) * 0.8f
                        + Mathf.PerlinNoise(t * 2.1f, 19.73f) * 0.2f;
            Gust = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, 0.60f, noise));
            CurrentStrength = Mathf.Clamp01(baseStrength + Gust * gustStrength);
            Vector2 horizontal = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.right;

            Shader.SetGlobalVector(DirectionId, new Vector4(horizontal.x, 0f, horizontal.y, 0f));
            Shader.SetGlobalFloat(StrengthId, CurrentStrength);
            Shader.SetGlobalFloat(SpeedId, speed);
            Shader.SetGlobalFloat(FrequencyId, frequency);
            Shader.SetGlobalFloat(GustId, Gust);
            Shader.SetGlobalVector(EditorClockId, editMode ? new Vector4(time, 1f, 0f, 0f) : Vector4.zero);
            if (editMode || impulseCenter.w <= 0f) return;
            float progress = Mathf.Clamp01((Time.time - impulseStart) / impulseDuration);
            if (progress >= 1f)
            {
                ClearImpulse();
                return;
            }
            float envelope = Mathf.SmoothStep(0f, 1f, Mathf.Min(progress * 11f, (1f - progress) * 1.35f));
            Vector4 center = impulseCenter;
            center.w *= envelope;
            Shader.SetGlobalVector(ImpulseCenterId, center);
            Shader.SetGlobalVector(ImpulseDirectionId, new Vector4(impulseHeading.x, impulseHeading.y, progress, 0f));
        }
    }
}
