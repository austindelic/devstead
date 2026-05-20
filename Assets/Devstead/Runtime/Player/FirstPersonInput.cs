using UnityEngine;

namespace Devstead.Player
{
    internal static class FirstPersonInput
    {
        public static Vector2 ComposeMoveInput(bool right, bool left, bool forward, bool backward)
        {
            var input = Vector2.zero;
            input.x += right ? 1.0f : 0.0f;
            input.x -= left ? 1.0f : 0.0f;
            input.y += forward ? 1.0f : 0.0f;
            input.y -= backward ? 1.0f : 0.0f;
            return input;
        }
    }
}
