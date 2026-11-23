using UnityEngine;
using UnityEngine.InputSystem;

namespace TanShadow.Spring
{
    [RequireComponent(typeof(Camera))]
    public sealed class SpringFollowCamera : MonoBehaviour
    {
        public Transform player;
        float yaw, pitch = 12f;

        void Start()
        {
            yaw = player.eulerAngles.y;
            Cursor.lockState = CursorLockMode.Locked;
        }

        void LateUpdate()
        {
            var mouse = Mouse.current;
            if (mouse != null && Cursor.lockState == CursorLockMode.Locked)
            {
                var delta = mouse.delta.ReadValue();
                yaw += delta.x * .13f;
                pitch = Mathf.Clamp(pitch - delta.y * .13f, -24f, 64f);
            }

            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0);
            Vector3 pivot = player.position + Vector3.up * 1.55f;
            Vector3 backward = rotation * Vector3.back;
            float distance = 4.1f;
            if (Physics.SphereCast(pivot, .16f, backward, out var obstacle, distance, 1, QueryTriggerInteraction.Ignore))
                distance = Mathf.Max(.18f, obstacle.distance - .1f);
            transform.SetPositionAndRotation(pivot + backward * distance, rotation);
        }

        void OnDisable() => Cursor.lockState = CursorLockMode.None;
    }
}
