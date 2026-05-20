using UnityEngine;
using Devstead.Player;

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

        private CharacterController characterController;
        private float pitch;
        private float verticalVelocity;

        private void Awake()
        {
            characterController = GetComponent<CharacterController>();
            pitch = FirstPersonMovementLook.NormalizePitch(transform.localEulerAngles.x);
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
            if (FirstPersonInputReader.WasEscapePressedThisFrame())
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            else if (lockCursorOnPlay && FirstPersonInputReader.WasPrimaryMousePressedThisFrame())
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

            var look = FirstPersonInputReader.ReadLookInput() * mouseSensitivity;

            pitch = FirstPersonMovementLook.CalculatePitch(pitch, look.y, minPitch, maxPitch);
            transform.localRotation = FirstPersonMovementLook.CreateLookRotation(pitch, transform.localEulerAngles.y, look.x);
        }

        private void UpdateMovement()
        {
            if (FirstPersonMovementLook.IsBelowMinimumEyeHeight(transform.position.y, minimumEyeHeight))
            {
                characterController.enabled = false;
                transform.position = FirstPersonMovementLook.ClampToMinimumEyeHeight(transform.position, minimumEyeHeight);
                characterController.enabled = true;
                verticalVelocity = 0.0f;
            }

            var input = FirstPersonMovementLook.ClampMoveInput(FirstPersonInputReader.ReadMoveInput());

            var speed = FirstPersonMovementLook.CalculateSpeed(moveSpeed, sprintMultiplier, FirstPersonInputReader.IsSprinting());
            var planarVelocity = FirstPersonMovementLook.CalculatePlanarVelocity(transform.right, transform.forward, input, speed);

            verticalVelocity = FirstPersonMovementLook.ApplyGrounding(verticalVelocity, characterController.isGrounded);

            if (characterController.isGrounded && FirstPersonInputReader.WasJumpPressedThisFrame())
            {
                verticalVelocity = FirstPersonMovementLook.CalculateJumpVelocity(jumpHeight, gravity);
            }

            verticalVelocity = FirstPersonMovementLook.ApplyGravity(verticalVelocity, gravity, Time.deltaTime);

            var velocity = planarVelocity + Vector3.up * verticalVelocity;
            characterController.Move(velocity * Time.deltaTime);
        }
    }
}
