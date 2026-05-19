using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Devstead.Rendering
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController))]
    [AddComponentMenu("Devstead/Rendering/First Person Camera Controller")]
    public sealed class FirstPersonCameraController : MonoBehaviour
    {
        [Header("Movement")]
        [SerializeField] private float moveSpeed = 5.0f;
        [SerializeField] private float sprintMultiplier = 1.75f;
        [SerializeField] private float jumpHeight = 1.2f;
        [SerializeField] private float gravity = -20.0f;
        [SerializeField] private float minimumEyeHeight = 1.8f;

        [Header("Look")]
        [SerializeField] private float mouseSensitivity = 2.0f;
        [SerializeField] private float minPitch = -85.0f;
        [SerializeField] private float maxPitch = 85.0f;
        [SerializeField] private bool lockCursorOnPlay = true;

        private const float MouseDeltaScale = 0.05f;

        private CharacterController characterController;
        private float pitch;
        private float verticalVelocity;

        private void Awake()
        {
            characterController = GetComponent<CharacterController>();
            pitch = NormalizePitch(transform.localEulerAngles.x);
        }

        private void OnEnable()
        {
            if (Application.isPlaying && lockCursorOnPlay)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        private void OnDisable()
        {
            if (Application.isPlaying && Cursor.lockState == CursorLockMode.Locked)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        private void Update()
        {
            if (WasEscapePressedThisFrame())
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            else if (lockCursorOnPlay && WasPrimaryMousePressedThisFrame())
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }

            UpdateLook();
            UpdateMovement();
        }

        private void UpdateLook()
        {
            if (Cursor.lockState != CursorLockMode.Locked)
            {
                return;
            }

            var look = ReadLookInput() * mouseSensitivity;

            pitch = Mathf.Clamp(pitch - look.y, minPitch, maxPitch);
            transform.localRotation = Quaternion.Euler(pitch, transform.localEulerAngles.y + look.x, 0.0f);
        }

        private void UpdateMovement()
        {
            if (transform.position.y < minimumEyeHeight)
            {
                characterController.enabled = false;
                transform.position = new Vector3(transform.position.x, minimumEyeHeight, transform.position.z);
                characterController.enabled = true;
                verticalVelocity = 0.0f;
            }

            var input = ReadMoveInput();
            input = Vector2.ClampMagnitude(input, 1.0f);

            var speed = IsSprinting() ? moveSpeed * sprintMultiplier : moveSpeed;
            var planarVelocity = (transform.right * input.x + transform.forward * input.y) * speed;

            if (characterController.isGrounded && verticalVelocity < 0.0f)
            {
                verticalVelocity = -2.0f;
            }

            if (characterController.isGrounded && WasJumpPressedThisFrame())
            {
                verticalVelocity = Mathf.Sqrt(jumpHeight * -2.0f * gravity);
            }

            verticalVelocity += gravity * Time.deltaTime;

            var velocity = planarVelocity + Vector3.up * verticalVelocity;
            characterController.Move(velocity * Time.deltaTime);
        }

        private static float NormalizePitch(float value)
        {
            return value > 180.0f ? value - 360.0f : value;
        }

        private static Vector2 ReadMoveInput()
        {
#if ENABLE_INPUT_SYSTEM
            var keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return Vector2.zero;
            }

            var input = Vector2.zero;
            input.x += keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed ? 1.0f : 0.0f;
            input.x -= keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed ? 1.0f : 0.0f;
            input.y += keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed ? 1.0f : 0.0f;
            input.y -= keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed ? 1.0f : 0.0f;
            return input;
#else
            return new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
#endif
        }

        private static Vector2 ReadLookInput()
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current == null ? Vector2.zero : Mouse.current.delta.ReadValue() * MouseDeltaScale;
#else
            return new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"));
#endif
        }

        private static bool IsSprinting()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.leftShiftKey.isPressed;
#else
            return Input.GetKey(KeyCode.LeftShift);
#endif
        }

        private static bool WasJumpPressedThisFrame()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame;
#else
            return Input.GetButtonDown("Jump");
#endif
        }

        private static bool WasEscapePressedThisFrame()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.Escape);
#endif
        }

        private static bool WasPrimaryMousePressedThisFrame()
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
#else
            return Input.GetMouseButtonDown(0);
#endif
        }
    }
}
