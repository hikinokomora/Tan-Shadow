using TanShadow.Combat;
using Unity.Cinemachine;
using UnityEngine;

namespace TanShadow.CameraRig
{
    // Камера захвата: стоит за плечом игрока, развёрнута на врага, смотрит между ними.
    // Включается поверх свободной камеры, пока есть цель.
    [DefaultExecutionOrder(-100)]
    public class LockOnCameraRig : MonoBehaviour
    {
        public CinemachineCamera lockCamera;
        [Tooltip("Цель Follow камеры захвата: позиция игрока, поворот на врага")]
        public Transform pivot;
        [Tooltip("Цель LookAt камеры захвата")]
        public Transform lookAt;
        [Tooltip("Маркер над захваченным врагом")]
        public Transform marker;

        [Tooltip("Куда смотрит камера: 0 — на игрока, 1 — на врага")]
        [Range(0, 1)] public float lookBias = 0.55f;
        public float pivotHeight = 1.5f;
        public float aimHeight = 1.3f;
        public float markerHeight = 2.35f;

        Transform player;
        Combatant target;

        void Awake() => SetTarget(null, null);

        public void SetTarget(Transform playerTransform, Combatant newTarget)
        {
            player = playerTransform;
            target = newTarget;
            bool locked = target != null;
            if (lockCamera != null) lockCamera.gameObject.SetActive(locked);
            if (marker != null) marker.gameObject.SetActive(locked);
            if (locked) LateUpdate();
        }

        void LateUpdate()
        {
            if (player == null || target == null) return;

            Vector3 from = player.position;
            Vector3 to = target.transform.position;
            Vector3 direction = to - from;
            direction.y = 0f;

            pivot.position = from + Vector3.up * pivotHeight;
            if (direction.sqrMagnitude > 0.001f) pivot.rotation = Quaternion.LookRotation(direction);
            lookAt.position = Vector3.Lerp(from + Vector3.up * aimHeight, to + Vector3.up * aimHeight, lookBias);
            if (marker != null) marker.position = to + Vector3.up * markerHeight;
        }
    }
}
