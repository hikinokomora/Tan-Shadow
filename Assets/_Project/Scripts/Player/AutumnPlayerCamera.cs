using UnityEngine;
using UnityEngine.InputSystem;

namespace ShadowTan.Player
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    [DefaultExecutionOrder(150)]
    public sealed class AutumnPlayerCamera : MonoBehaviour
    {
        [SerializeField] Transform target;
        [SerializeField] PlayerInputReader input;
        [SerializeField] Camera[] reviewCameras;
        [SerializeField] LayerMask obstacles;
        [SerializeField] float arm = 3.8f;
        [SerializeField] float height = 1.45f;
        [SerializeField] float sensitivity = .13f;

        float yaw, pitch = 12f;
        float distance;
        CursorLockMode previousLock;
        bool previousVisibility;
        bool cursorOwned;
        bool[] reviewEnabled;

        void OnEnable()
        {
            if (!Application.isPlaying) return;
            previousLock = Cursor.lockState;
            previousVisibility = Cursor.visible;
            cursorOwned = true;
            yaw = target ? target.eulerAngles.y : transform.eulerAngles.y;
            distance = arm;
            if (reviewCameras != null)
            {
                reviewEnabled = new bool[reviewCameras.Length];
                for (int i = 0; i < reviewCameras.Length; i++)
                {
                    if (!reviewCameras[i]) continue;
                    reviewEnabled[i] = reviewCameras[i].enabled;
                    reviewCameras[i].enabled = false;
                }
            }
            LockCursor();
        }

        void OnDisable()
        {
            if (!cursorOwned) return;
            cursorOwned = false;
            Cursor.lockState = previousLock;
            Cursor.visible = previousVisibility;
            if (reviewEnabled != null)
                for (int i = 0; i < reviewEnabled.Length; i++)
                    if (reviewCameras[i]) reviewCameras[i].enabled = reviewEnabled[i];
        }

        public void ResetView(Quaternion facing)
        {
            yaw = facing.eulerAngles.y;
            pitch = 12f;
            distance = arm;
        }

        void LateUpdate()
        {
            if (!target) return;
            var mouse = Mouse.current;
            bool canLook = !input || input.InputEnabled;
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            else if (canLook && mouse != null)
            {
                if (Cursor.lockState == CursorLockMode.Locked)
                {
                    Vector2 motion = mouse.delta.ReadValue();
                    yaw = Mathf.Repeat(yaw + motion.x * sensitivity, 360f);
                    pitch = Mathf.Clamp(pitch - motion.y * sensitivity, -20f, 65f);
                }
                else if (mouse.rightButton.wasPressedThisFrame)
                    LockCursor();
            }

            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0);
            Vector3 pivot = target.position + Vector3.up * height;
            Vector3 back = rotation * Vector3.back;
            float clear = arm;
            if (Physics.SphereCast(pivot, .16f, back, out var hit, arm, obstacles, QueryTriggerInteraction.Ignore))
                clear = Mathf.Max(.12f, hit.distance - .08f);
            distance = clear < distance ? clear : Mathf.MoveTowards(distance, clear, 12f * Time.unscaledDeltaTime);
            transform.SetPositionAndRotation(pivot + back * distance, rotation);
        }

        static void LockCursor()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }
}
