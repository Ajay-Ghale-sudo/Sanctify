using UnityEngine;

namespace Sanctify.Characters.Player
{
    [CreateAssetMenu(fileName = "SO_PlayerMovement", menuName = "Sanctify/Player/Movement Settings")]
    public sealed class PlayerMovementSettings : ScriptableObject
    {
        [Header("Walk speeds (m/s)")]
        [Min(0f)] public float walkForward = 1.8f;
        [Min(0f)] public float walkBackward = 1.3f;
        [Min(0f)] public float walkStrafe = 1.4f;

        [Header("Run speeds (m/s)")]
        [Tooltip("Running boosts forward/back much more than strafing. Speeds combine on an ellipse, so strafing while running forward can never exceed the forward cap.")]
        [Min(0f)] public float runForward = 3.6f;
        [Min(0f)] public float runBackward = 2.2f;
        [Min(0f)] public float runStrafe = 2.0f;

        [Header("Ground response (m/s²)")]
        [Tooltip("How fast speed builds along the direction you're pushing.")]
        [Min(0f)] public float acceleration = 6f;
        [Tooltip("How fast any velocity you are NOT pushing along bleeds off. This is what makes direction changes gradual: the old direction decelerates while the new one accelerates.")]
        [Min(0f)] public float deceleration = 8f;
        [Tooltip("Deceleration multiplier when no direction is held, so letting go of the stick coasts a little longer than actively reversing.")]
        [Range(0f, 1f)] public float noInputDecelerationScale = 0.6f;
        [Tooltip("Above this fraction of top speed, acceleration is reduced so arrival at top speed is soft rather than abrupt.")]
        [Range(0f, 1f)] public float softApproachStart = 0.7f;
        [Range(0.05f, 1f)] public float softApproachScale = 0.3f;

        [Header("Air")]
        [Min(0f)] public float airAcceleration = 2f;
        [Tooltip("Wished speed is capped to this while airborne, limiting steering during a fall.")]
        [Min(0f)] public float airSpeedCap = 0.6f;

        [Header("Gravity")]
        [Min(0f)] public float gravity = 15f;
        [Min(0f)] public float maxFallSpeed = 30f;

        [Header("Stride (metres per full cycle = two steps)")]
        [Min(0.01f)] public float walkStrideLength = 1.4f;
        [Min(0.01f)] public float runStrideLength = 2.0f;

        [Header("Stopping")]
        [Tooltip("Horizontal speed under which the player counts as fully stopped, which resets the stride cycle.")]
        [Min(0f)] public float stoppedSpeedThreshold = 0.03f;
    }
}
