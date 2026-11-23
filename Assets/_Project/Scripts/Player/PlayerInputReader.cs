using UnityEngine;
using UnityEngine.InputSystem;

namespace ShadowTan.Player
{
    // Превращает ввод в намерения. Не принимает решений и не знает о состояниях (§9.2).
    public class PlayerInputReader : MonoBehaviour
    {
        const string DefaultGameplayMap = "Gameplay";

        [SerializeField] InputActionAsset actions;
        [SerializeField] string gameplayMapName = DefaultGameplayMap;
        [SerializeField] string moveActionName = "Move";
        [SerializeField] string rollActionName = "Roll";
        [SerializeField] string attackActionName = "Attack";
        [SerializeField] string grabActionName = "Grab";
        [SerializeField] string stabActionName = "Stab";

        [Tooltip("Относительно этой камеры считается направление движения; пусто — Camera.main")]
        [SerializeField] Transform cameraTransform;

        InputActionMap gameplayMap;
        InputAction moveAction;
        InputAction rollAction;
        InputAction attackAction;
        InputAction grabAction;
        InputAction stabAction;

        readonly InputBuffer buffer = new InputBuffer();
        Vector2 moveInput;
        Transform cachedCamera;

        public float BufferWindow { get; set; } = 0.15f;
        public Vector2 MoveInput => moveInput;
        public Vector3 MoveWorldDirection { get; private set; }
        public bool GrabHeld => grabAction != null && grabAction.IsPressed();
        public bool StabPressed => stabAction != null && stabAction.WasPressedThisFrame();
        public bool InputEnabled { get; private set; }

        void Awake()
        {
            if (actions == null)
            {
                Debug.LogError("PlayerInputReader: не назначен InputActionAsset", this);
                enabled = false;
                return;
            }

            gameplayMap = actions.FindActionMap(gameplayMapName, false);
            if (gameplayMap == null)
            {
                Debug.LogError($"PlayerInputReader: нет карты {gameplayMapName}", this);
                enabled = false;
                return;
            }

            moveAction = gameplayMap.FindAction(moveActionName, false);
            rollAction = gameplayMap.FindAction(rollActionName, false);
            attackAction = gameplayMap.FindAction(attackActionName, false);
            grabAction = gameplayMap.FindAction(grabActionName, false);
            stabAction = gameplayMap.FindAction(stabActionName, false);

            if (moveAction == null)
                Debug.LogError($"PlayerInputReader: нет действия {moveActionName}", this);

            if (rollAction != null) rollAction.performed += OnRoll;
            if (attackAction != null) attackAction.performed += OnAttack;
            if (moveAction != null)
            {
                moveAction.performed += OnMove;
                moveAction.canceled += OnMove;
            }
        }

        void OnEnable() => SetInputEnabled(true);
        void OnDisable()
        {
            gameplayMap?.Disable();
            InputEnabled = false;
            moveInput = Vector2.zero;
            MoveWorldDirection = Vector3.zero;
        }

        void OnDestroy()
        {
            if (rollAction != null) rollAction.performed -= OnRoll;
            if (attackAction != null) attackAction.performed -= OnAttack;
            if (moveAction != null)
            {
                moveAction.performed -= OnMove;
                moveAction.canceled -= OnMove;
            }
        }

        void Update()
        {
            if (!InputEnabled)
            {
                MoveWorldDirection = Vector3.zero;
                return;
            }

            MoveWorldDirection = ToWorldDirection(moveInput);
        }

        public void SetInputEnabled(bool value)
        {
            if (gameplayMap == null) return;
            if (value)
            {
                if (!InputEnabled) gameplayMap.Enable();
            }
            else
            {
                gameplayMap.Disable();
                moveInput = Vector2.zero;
                MoveWorldDirection = Vector3.zero;
                buffer.Clear();
            }

            InputEnabled = value;
        }

        public bool HasBuffered(BufferedAction action)
        {
            return buffer.Has(action, Time.time, BufferWindow);
        }

        public bool ConsumeBuffered(BufferedAction action)
        {
            return buffer.Consume(action, Time.time, BufferWindow);
        }

        public void ClearBuffer() => buffer.Clear();

        void OnMove(InputAction.CallbackContext context)
        {
            moveInput = context.ReadValue<Vector2>();
        }

        void OnRoll(InputAction.CallbackContext context)
        {
            if (InputEnabled) buffer.Press(BufferedAction.Roll, Time.time);
        }

        void OnAttack(InputAction.CallbackContext context)
        {
            if (InputEnabled) buffer.Press(BufferedAction.Attack, Time.time);
        }

        Vector3 ToWorldDirection(Vector2 input)
        {
            if (input.sqrMagnitude < 0.0001f) return Vector3.zero;

            if (cachedCamera == null)
            {
                cachedCamera = cameraTransform != null ? cameraTransform : Camera.main != null ? Camera.main.transform : null;
            }

            if (cachedCamera == null)
            {
                return new Vector3(input.x, 0f, input.y);
            }

            var forward = Vector3.ProjectOnPlane(cachedCamera.forward, Vector3.up).normalized;
            var right = Vector3.ProjectOnPlane(cachedCamera.right, Vector3.up).normalized;
            var direction = forward * input.y + right * input.x;
            return Vector3.ClampMagnitude(direction, 1f);
        }
    }
}
