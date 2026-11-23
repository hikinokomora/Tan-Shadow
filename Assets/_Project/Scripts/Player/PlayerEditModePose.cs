using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace ShadowTan.Player
{
    // Animator не проигрывает клипы вне Play Mode, поэтому префаб показывался в T-позе.
    // Компонент только редакторский: ставит Idle-позу и гарантированно снимает AnimationMode
    // перед входом в Play Mode, чтобы не мешать рантайм-анимациям.
    [ExecuteAlways]
    public class PlayerEditModePose : MonoBehaviour
    {
        [SerializeField] Animator animator;
        [SerializeField] AnimationClip idleClip;
        [Range(0f, 1f)]
        [SerializeField] float normalizedTime = 0.15f;
        [SerializeField] bool previewInEditor = true;

#if UNITY_EDITOR
        bool ownsAnimationMode;

        void OnEnable()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            Sample();
        }

        void OnDisable()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            Stop();
        }

        void OnValidate() => Sample();

        void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode || state == PlayModeStateChange.EnteredPlayMode)
            {
                Stop();
            }
            else if (state == PlayModeStateChange.EnteredEditMode)
            {
                Sample();
            }
        }

        void Sample()
        {
            if (!previewInEditor || Application.isPlaying || idleClip == null) return;
            if (animator == null) animator = GetComponentInChildren<Animator>();
            if (animator == null) return;

            if (!AnimationMode.InAnimationMode())
            {
                AnimationMode.StartAnimationMode();
                ownsAnimationMode = true;
            }

            AnimationMode.BeginSampling();
            AnimationMode.SampleAnimationClip(animator.gameObject, idleClip,
                idleClip.length * Mathf.Clamp01(normalizedTime));
            AnimationMode.EndSampling();
        }

        void Stop()
        {
            if (!ownsAnimationMode) return;
            AnimationMode.StopAnimationMode();
            ownsAnimationMode = false;
        }

        public void SetIdleClip(AnimationClip clip)
        {
            idleClip = clip;
            Sample();
        }
#endif
    }
}
