using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace TanShadow.MotionMatchingLab
{
    [RequireComponent(typeof(Camera))]
    public sealed class MotionLabView : MonoBehaviour
    {
        public MotionMatchingPlayer player;
        public Transform[] stations;
        public Transform[] signs;
        public string[] signNames;

        public bool InputEnabled { get; set; } = true;

        float yaw, pitch = 15f;
        Vector3 cameraVelocity;
        Camera view;
        GUIStyle heading, body, value, sign;
        static readonly Color Ink = new Color(.025f, .05f, .085f, .94f);
        static readonly Color Accent = new Color(.38f, .91f, .85f);
        static readonly Key[] StationKeys = { Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5, Key.Digit6 };

        void Awake()
        {
            view = GetComponent<Camera>();
            yaw = player ? player.transform.eulerAngles.y : 0f;
        }

        void Start() => UpdateCamera(true);

        void Update()
        {
            if (!InputEnabled || !player)
                return;

            Keyboard keyboard = Keyboard.current;
            Mouse mouse = Mouse.current;
            Gamepad pad = Gamepad.current;
            bool orbit = mouse != null && mouse.rightButton.isPressed;
            if (orbit)
            {
                Vector2 delta = mouse.delta.ReadValue();
                yaw += delta.x * .17f;
                pitch = Mathf.Clamp(pitch - delta.y * .15f, 4f, 52f);
            }
            if (pad != null)
            {
                Vector2 aim = pad.rightStick.ReadValue();
                yaw += aim.x * 135f * Time.deltaTime;
                pitch = Mathf.Clamp(pitch - aim.y * 92f * Time.deltaTime, 4f, 52f);
            }

            Vector2 axes = pad != null ? pad.leftStick.ReadValue() : Vector2.zero;
            if (keyboard != null)
            {
                var keys = new Vector2(
                    (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f),
                    (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f));
                if (keys != Vector2.zero)
                    axes = Vector2.ClampMagnitude(keys, 1f);
                if (keyboard.rKey.wasPressedThisFrame)
                {
                    player.ResetToStart();
                    yaw = player.transform.eulerAngles.y;
                    UpdateCamera(true);
                }
                for (int i = 0; stations != null && i < Mathf.Min(StationKeys.Length, stations.Length); i++)
                {
                    if (!keyboard[StationKeys[i]].wasPressedThisFrame)
                        continue;
                    Transform station = stations[i];
                    player.ResetTo(station.position, station.rotation);
                    yaw = station.eulerAngles.y;
                    UpdateCamera(true);
                }
            }

            Quaternion orbitYaw = Quaternion.Euler(0f, yaw, 0f);
            player.Simulate(new MotionInput
            {
                move = orbitYaw * new Vector3(axes.x, 0f, axes.y),
                facing = orbit || (pad != null && pad.leftTrigger.isPressed) ? orbitYaw * Vector3.forward : Vector3.zero,
                walk = keyboard != null && keyboard.leftAltKey.isPressed,
                sprint = (keyboard != null && keyboard.leftShiftKey.isPressed) || (pad != null && pad.leftStickButton.isPressed),
                crouch = (keyboard != null && (keyboard.leftCtrlKey.isPressed || keyboard.cKey.isPressed))
                          || (pad != null && pad.rightShoulder.isPressed),
                dodge = (keyboard != null && keyboard.spaceKey.wasPressedThisFrame) || (pad != null && pad.buttonSouth.wasPressedThisFrame),
                traverse = (keyboard != null && keyboard.eKey.wasPressedThisFrame) || (pad != null && pad.buttonWest.wasPressedThisFrame)
            }, Time.deltaTime);
        }

        void LateUpdate() => UpdateCamera(false);

        void UpdateCamera(bool immediate)
        {
            if (!player)
                return;
            Vector3 pivot = player.transform.position;
            pivot.y = player.CameraFollowY + 1.43f;
            Quaternion heading = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 offset = heading * new Vector3(.53f, .12f, -4.65f);
            if (Physics.SphereCast(pivot, .16f, offset.normalized, out RaycastHit hit, offset.magnitude,
                    ~0, QueryTriggerInteraction.Ignore) && hit.collider.gameObject != player.gameObject)
                offset = offset.normalized * Mathf.Max(.45f, hit.distance - .12f);
            Vector3 target = pivot + offset;
            transform.position = immediate ? target : Vector3.SmoothDamp(transform.position, target, ref cameraVelocity, .105f);
            transform.rotation = Quaternion.LookRotation(pivot + heading * Vector3.right * .33f - transform.position);
        }

        void OnGUI()
        {
            if (!player || !player.Database)
                return;
            BuildStyles();
            float scale = Mathf.Clamp(Screen.width / 1440f, .68f, 1.35f);
            Matrix4x4 previous = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            float width = Screen.width / scale;
            float height = Screen.height / scale;

            Panel(new Rect(20f, 20f, 350f, 131f));
            GUI.Label(new Rect(34f, 29f, 320f, 29f), "Motion Matching Lab", heading);
            GUI.Label(new Rect(34f, 64f, 320f, 23f), "WASD Move  /  RMB Orbit + strafe", body);
            GUI.Label(new Rect(34f, 87f, 320f, 23f), "Alt Walk  /  Shift Sprint  /  Ctrl Crouch", body);
            GUI.Label(new Rect(34f, 110f, 325f, 23f), "Space Dodge roll  /  E Parkour or ledge catch", body);

            const float statusWidth = 342f;
            float left = width - statusWidth - 20f;
            Panel(new Rect(left, height - 105f, statusWidth, 85f));
            GUI.Label(new Rect(left + 13f, height - 99f, statusWidth - 26f, 23f),
                player.CurrentState + "   /   " + player.PlanarSpeed.ToString("0.0") + " m/s", value);
            string current = player.CurrentClip;
            if (current.StartsWith("M_Neutral_"))
                current = current.Substring("M_Neutral_".Length);
            GUI.Label(new Rect(left + 13f, height - 73f, statusWidth - 26f, 45f), current, body);

            Panel(new Rect(20f, height - 52f, 378f, 32f));
            GUI.Label(new Rect(32f, height - 49f, 352f, 25f), "1–6 Test stations  /  R Reset to start", body);
            DrawSigns(scale);
            GUI.matrix = previous;
        }

        void DrawSigns(float scale)
        {
            if (signs == null || !view)
                return;
            for (int i = 0; i < signs.Length; i++)
            {
                Vector3 screen = view.WorldToScreenPoint(signs[i].position);
                if (screen.z < 1f || screen.z > 23f || screen.x < 50f || screen.x > Screen.width - 50f
                    || screen.y < 65f || screen.y > Screen.height - 97f)
                    continue;
                string label = signNames[i];
                float labelWidth = sign.CalcSize(new GUIContent(label)).x + 19f;
                var rect = new Rect(screen.x / scale - labelWidth * .5f, (Screen.height - screen.y) / scale - 12f, labelWidth, 24f);
                Panel(rect);
                GUI.Label(rect, label, sign);
            }
        }

        void BuildStyles()
        {
            if (heading != null)
                return;
            heading = new GUIStyle(GUI.skin.label) { fontSize = 23, fontStyle = FontStyle.Bold };
            heading.normal.textColor = Color.white;
            body = new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = true };
            body.normal.textColor = new Color(.82f, .88f, .95f);
            value = new GUIStyle(body) { fontStyle = FontStyle.Bold, fontSize = 16 };
            value.normal.textColor = Accent;
            sign = new GUIStyle(value) { fontSize = 13, alignment = TextAnchor.MiddleCenter };
        }

        static void Panel(Rect rect)
        {
            Color before = GUI.color;
            GUI.color = Ink;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = before;
        }
    }
}
