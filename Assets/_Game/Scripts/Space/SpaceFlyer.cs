using UnityEngine;
using UnityEngine.InputSystem;

namespace TanShadow.Cosmos
{
    // Свободный полёт от первого лица, как камера сцены в Unity: мышь — взгляд, WASD — вперёд/влево/назад/вправо
    // по взгляду, Q — вниз, E — вверх (по осям камеры), Shift — быстрее, Ctrl — медленнее, колесо — скорость.
    // Невидимая сфера: дальше boundaryRadius от центра не улететь, скорость наружу гасится.
    public sealed class SpaceFlyer : MonoBehaviour
    {
        public SpaceTuning tuning;
        [Tooltip("Центр сферы-границы (МКС)")]
        public Transform center;

        Vector3 velocity;
        float yaw, pitch;
        float wheel = 1f;

        void OnEnable()
        {
            SyncLook();
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        void OnDisable()
        {
            velocity = Vector3.zero;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        // Повернуть взгляд извне (спавн): дальше мышь крутит от этого положения.
        public void Place(Vector3 position, Quaternion rotation)
        {
            velocity = Vector3.zero;
            transform.SetPositionAndRotation(position, rotation);
            SyncLook();
        }

        void SyncLook()
        {
            var e = transform.rotation.eulerAngles;
            yaw = e.y;
            pitch = e.x > 180f ? e.x - 360f : e.x;
        }

        void Update()
        {
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            if (tuning == null || keyboard == null) return;
            float dt = Time.deltaTime;

            // Esc отпускает курсор, клик — снова захватывает.
            if (keyboard.escapeKey.wasPressedThisFrame) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
            if (mouse != null && mouse.leftButton.wasPressedThisFrame) { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }

            if (mouse != null && Cursor.lockState == CursorLockMode.Locked)
            {
                Vector2 d = mouse.delta.ReadValue() * tuning.lookSensitivity;
                yaw += d.x;
                pitch = Mathf.Clamp(pitch - d.y, -89f, 89f);
                float scroll = mouse.scroll.ReadValue().y;
                if (scroll != 0f) wheel = Mathf.Clamp(wheel * Mathf.Pow(1.15f, Mathf.Sign(scroll)), tuning.wheelRange.x, tuning.wheelRange.y);
            }
            transform.rotation = Quaternion.Euler(pitch, yaw, 0f);

            Vector3 input = Vector3.zero;
            if (keyboard.wKey.isPressed) input.z += 1f;
            if (keyboard.sKey.isPressed) input.z -= 1f;
            if (keyboard.dKey.isPressed) input.x += 1f;
            if (keyboard.aKey.isPressed) input.x -= 1f;
            if (keyboard.eKey.isPressed) input.y += 1f;
            if (keyboard.qKey.isPressed) input.y -= 1f;
            if (input.sqrMagnitude > 1f) input.Normalize();

            float speed = tuning.speed * wheel;
            if (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed) speed *= tuning.fastMultiplier;
            if (keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed) speed /= tuning.slowMultiplier;

            Vector3 target = transform.TransformDirection(input) * speed;
            velocity = Vector3.Lerp(velocity, target, 1f - Mathf.Exp(-dt / tuning.smoothing));
            Vector3 position = transform.position + velocity * dt;

            Vector3 c = center != null ? center.position : Vector3.zero;
            Vector3 offset = position - c;
            if (offset.magnitude > tuning.boundaryRadius)
            {
                Vector3 normal = offset.normalized;
                position = c + normal * tuning.boundaryRadius;
                float outward = Vector3.Dot(velocity, normal);
                if (outward > 0f) velocity -= normal * outward;   // скользим по сфере
            }
            transform.position = position;
        }
    }
}
