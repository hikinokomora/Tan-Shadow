using UnityEngine;
using UnityEngine.InputSystem;
using TanShadow.Player;

namespace TanShadow.Autumn
{
    [DefaultExecutionOrder(150)]
    public sealed class AutumnTrial : MonoBehaviour
    {
        public PlayerController hero;
        public Camera view;
        public Transform spawn, tiger;
        public Transform[] observationPoints;
        public Transform arenaPoint;
        CharacterController body;
        float previousZ, yaw, pitch = 13;
        bool showBlockHint;

        void Start()
        {
            body = hero.GetComponent<CharacterController>();
            previousZ = hero.transform.position.z;
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
            float z = hero.transform.position.z;
            Transform point = arenaPoint;
            foreach (Transform station in observationPoints)
            {
                if (z <= station.position.z + 32f)
                {
                    point = station;
                    break;
                }
            }
            // One physical scale proxy. It changes stations only between distant sight lines.
            tiger.SetPositionAndRotation(point.position, point.rotation);
            bool guarding = hero.State == PlayerState.Block;
            float entry = point.position.z - 5;
            float exit = point.position.z + 4;
            showBlockHint = point != arenaPoint && !guarding && z >= entry - 3 && z < exit;
            if (point != arenaPoint && !guarding && previousZ < exit && z > entry && z > previousZ)
            {
                body.Move(Vector3.back * (z - Mathf.Max(entry, previousZ)));
            }
            previousZ = hero.transform.position.z;
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

        void OnGUI()
        {
            if (showBlockHint)
                GUI.Box(new Rect((Screen.width - 320) * .5f, Screen.height - 84, 320, 32),
                    "Удерживайте ПКМ, чтобы пройти мимо");
        }
    }
}
