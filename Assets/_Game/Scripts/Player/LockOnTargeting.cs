using TanShadow.CameraRig;
using TanShadow.Combat;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TanShadow.Player
{
    // Захват цели: выбор ближайшего к центру экрана врага, переключение фликом камеры, сброс по дистанции.
    [RequireComponent(typeof(Combatant))]
    public class LockOnTargeting : MonoBehaviour
    {
        public InputActionAsset controls;
        [Tooltip("Максимальная дистанция захвата, м")]
        [Min(0)] public float acquireRange = 15f;
        [Tooltip("Захват сбрасывается, если враг дальше, м")]
        [Min(0)] public float breakRange = 20f;
        [Tooltip("Сколько пикселей мыши по горизонтали нужно для переключения цели")]
        [Min(0)] public float mouseSwitchThreshold = 60f;
        [Tooltip("Отклонение правого стика для переключения цели")]
        [Range(0, 1)] public float stickSwitchThreshold = 0.7f;
        [Min(0)] public float switchCooldown = 0.35f;

        public Combatant Target { get; private set; }
        public bool IsLocked => Target != null;

        const float AimHeight = 1.3f;

        Combatant self;
        LockOnCameraRig rig;
        Camera view;
        InputActionMap map;
        InputAction lockAction, look;
        float mouseAccumulated;
        bool stickArmed = true;
        float nextSwitchTime;

        void Awake()
        {
            self = GetComponent<Combatant>();
            map = controls.FindActionMap("Player", true);
            lockAction = map.FindAction("LockOn", true);
            look = map.FindAction("Look", true);
        }

        void Start()
        {
            rig = FindAnyObjectByType<LockOnCameraRig>();
            view = Camera.main;
        }

        void OnEnable() => map.Enable();
        void OnDisable() => SetTarget(null);

        void Update()
        {
            if (lockAction.WasPressedThisFrame())
                SetTarget(IsLocked ? null : FindBestTarget());

            if (!IsLocked) return;

            if (!Target.isActiveAndEnabled || FlatDistance(Target) > breakRange)
            {
                SetTarget(null);
                return;
            }

            float direction = ReadSwitchFlick();
            if (direction != 0f && Time.time >= nextSwitchTime)
            {
                var next = FindNeighbour(direction);
                if (next != null)
                {
                    SetTarget(next);
                    nextSwitchTime = Time.time + switchCooldown;
                }
            }
        }

        void SetTarget(Combatant target)
        {
            Target = target;
            mouseAccumulated = 0f;
            if (rig != null) rig.SetTarget(transform, target);
        }

        Combatant FindBestTarget()
        {
            Combatant best = null;
            float bestScore = float.MaxValue;
            foreach (var other in Combatant.All)
            {
                if (other == self) continue;
                float distance = FlatDistance(other);
                if (distance > acquireRange) continue;

                Vector3 vp = Viewport(other);
                bool onScreen = vp.z > 0f && vp.x > 0f && vp.x < 1f && vp.y > 0f && vp.y < 1f;
                float score = (onScreen ? 0f : 10f) + Vector2.Distance(vp, new Vector2(0.5f, 0.5f)) + distance * 0.02f;
                if (score < bestScore)
                {
                    best = other;
                    bestScore = score;
                }
            }
            return best;
        }

        // Ближайший по экрану враг в сторону флика.
        Combatant FindNeighbour(float direction)
        {
            float currentX = Viewport(Target).x;
            Combatant best = null;
            float bestGap = float.MaxValue;
            foreach (var other in Combatant.All)
            {
                if (other == self || other == Target || FlatDistance(other) > acquireRange) continue;
                Vector3 vp = Viewport(other);
                if (vp.z <= 0f) continue;
                float gap = (vp.x - currentX) * direction;
                if (gap > 0f && gap < bestGap)
                {
                    best = other;
                    bestGap = gap;
                }
            }
            return best;
        }

        float ReadSwitchFlick()
        {
            float x = look.ReadValue<Vector2>().x;
            if (look.activeControl != null && look.activeControl.device is Gamepad)
            {
                if (Mathf.Abs(x) < 0.3f) stickArmed = true;
                if (!stickArmed || Mathf.Abs(x) < stickSwitchThreshold) return 0f;
                stickArmed = false;
                return Mathf.Sign(x);
            }

            // Мышь: копим резкое движение, медленное — затухает.
            mouseAccumulated = mouseAccumulated * Mathf.Exp(-Time.unscaledDeltaTime * 10f) + x;
            if (Mathf.Abs(mouseAccumulated) < mouseSwitchThreshold) return 0f;
            float sign = Mathf.Sign(mouseAccumulated);
            mouseAccumulated = 0f;
            return sign;
        }

        Vector3 Viewport(Combatant c)
        {
            if (view == null) return new Vector3(0.5f, 0.5f, 1f);
            return view.WorldToViewportPoint(c.transform.position + Vector3.up * AimHeight);
        }

        float FlatDistance(Combatant c)
        {
            Vector3 d = c.transform.position - transform.position;
            d.y = 0f;
            return d.magnitude;
        }
    }
}
