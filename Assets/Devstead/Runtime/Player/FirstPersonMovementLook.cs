using UnityEngine;

namespace Devstead.Player
{
    internal static class FirstPersonMovementLook
    {
        private const float GroundedStickVelocity = -2.0f;

        public static float NormalizePitch(float value)
        {
            return value > 180.0f ? value - 360.0f : value;
        }

        public static float CalculatePitch(float currentPitch, float lookY, float minPitch, float maxPitch)
        {
            return Mathf.Clamp(currentPitch - lookY, minPitch, maxPitch);
        }

        public static Quaternion CreateLookRotation(float pitch, float currentYaw, float lookX)
        {
            return Quaternion.Euler(pitch, currentYaw + lookX, 0.0f);
        }

        public static bool IsBelowMinimumEyeHeight(float currentY, float minimumEyeHeight)
        {
            return currentY < minimumEyeHeight;
        }

        public static Vector3 ClampToMinimumEyeHeight(Vector3 position, float minimumEyeHeight)
        {
            return new Vector3(position.x, minimumEyeHeight, position.z);
        }

        public static Vector2 ClampMoveInput(Vector2 input)
        {
            return Vector2.ClampMagnitude(input, 1.0f);
        }

        public static float CalculateSpeed(float moveSpeed, float sprintMultiplier, bool isSprinting)
        {
            return isSprinting ? moveSpeed * sprintMultiplier : moveSpeed;
        }

        public static Vector3 CalculatePlanarVelocity(Vector3 right, Vector3 forward, Vector2 input, float speed)
        {
            return (right * input.x + forward * input.y) * speed;
        }

        public static float ApplyGrounding(float verticalVelocity, bool isGrounded)
        {
            return isGrounded && verticalVelocity < 0.0f ? GroundedStickVelocity : verticalVelocity;
        }

        public static float CalculateJumpVelocity(float jumpHeight, float gravity)
        {
            return Mathf.Sqrt(jumpHeight * -2.0f * gravity);
        }

        public static float ApplyGravity(float verticalVelocity, float gravity, float deltaTime)
        {
            return verticalVelocity + gravity * deltaTime;
        }
    }
}
