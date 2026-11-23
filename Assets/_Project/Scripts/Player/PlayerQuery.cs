using System.Collections.Generic;
using ShadowTan.Core.Contracts;
using ShadowTan.Core.Services;
using UnityEngine;

namespace ShadowTan.Player
{
    // Реализация IPlayerQuery: чужие модули спрашивают героиню через этот компонент (§9.2, §11.4).
    public class PlayerQuery : MonoBehaviour, IPlayerQuery
    {
        [SerializeField] PlayerController player;
        [SerializeField] PlayerMotor motor;
        [SerializeField] GrabController grab;

        [Header("Точки видимости")]
        [SerializeField] Transform head;
        [SerializeField] Transform chest;
        [SerializeField] Transform feet;

        [Header("Укрытие")]
        [SerializeField] LayerMask coverMask;
        [Tooltip("Внутри CoverVolume; выставляет сам объём укрытия")]
        [SerializeField] bool insideCoverVolume;

        readonly Vector3[] points = new Vector3[3];

        public Vector3 Position => transform.position;
        public Quaternion Rotation => transform.rotation;
        public Vector3 Velocity => motor != null ? motor.Velocity : Vector3.zero;
        public string StateName => player != null ? player.StateName : PlayerStateId.Disabled.ToString();
        public bool IsHanging => grab != null && grab.IsAttached;
        public GrabMode GrabMode
        {
            get
            {
                var target = grab != null ? grab.CurrentTarget : null;
                return target != null ? target.Mode : GrabMode.HoldInPlace;
            }
        }
        public string Zone { get; set; }

        public IReadOnlyList<Vector3> VisibilityPoints
        {
            get
            {
                points[0] = head != null ? head.position : transform.position + Vector3.up * 1.6f;
                points[1] = chest != null ? chest.position : transform.position + Vector3.up * 1.1f;
                points[2] = feet != null ? feet.position : transform.position + Vector3.up * 0.2f;
                return points;
            }
        }

        void Awake()
        {
            if (player == null) player = GetComponent<PlayerController>();
            if (motor == null) motor = GetComponent<PlayerMotor>();
            if (grab == null) grab = GetComponent<GrabController>();
        }

        void OnEnable() => Services.Register<IPlayerQuery>(this);
        void OnDisable() => Services.Unregister<IPlayerQuery>();

        public void SetInsideCoverVolume(bool value) => insideCoverVolume = value;

        // Укрытие весны: либо внутри объёма, либо взгляд источника перекрыт для всех точек видимости (§11.4).
        public bool IsInCover(IDetectionSource source)
        {
            if (insideCoverVolume) return true;
            if (source == null || !source.IsPresent || coverMask.value == 0) return false;

            var eye = source.EyePosition;
            foreach (var point in VisibilityPoints)
            {
                if (!Physics.Linecast(eye, point, coverMask, QueryTriggerInteraction.Ignore)) return false;
            }

            return true;
        }
    }
}
