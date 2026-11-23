using UnityEngine;
using UnityEngine.InputSystem;
using TanShadow.Player;

namespace TanShadow.Autumn
{
    // Камера от третьего лица для осенней локации.
    // Поведение тигра перенесено в BaihuEncounter. Поля tiger, observationPoints и arenaPoint
    // остались как разметка для AutumnBlockoutBuilder; во время игры их двигает уже не этот компонент.
    [DefaultExecutionOrder(150)]
    public sealed class AutumnTrial : MonoBehaviour
    {
        public PlayerController hero;
        public Camera view;
        public Transform spawn, tiger;
        public Transform[] observationPoints;
        public Transform arenaPoint;
        float yaw, pitch = 13;

        void Start()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        void OnDestroy()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        void LateUpdate()
        {
            var mouse = Mouse.current;
            if (mouse != null)
            {
                var delta = mouse.delta.ReadValue();
                yaw += delta.x * .13f;
                pitch = Mathf.Clamp(pitch - delta.y * .13f, -20, 65);
            }
            Vector3 focus = hero.transform.position + Vector3.up * 1.55f;
            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0);
            Vector3 back = rotation * Vector3.back;
            float distance = 4.1f;
            if (Physics.SphereCast(focus, .14f, back, out var hit, distance, 1, QueryTriggerInteraction.Ignore))
                distance = Mathf.Max(.2f, hit.distance - .12f);
            view.transform.SetPositionAndRotation(focus + back * distance, rotation);
        }
    }
}
